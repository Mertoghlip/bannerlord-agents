using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BannerlordAgents;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.21.5 (Nov 20: after reloading TEST START DGG MERGE my lieutenant's window showed none of our earlier talk). The laptop's time zone
        /// moved from Pacific to Turkey between Nov 17 and Nov 19. The conversation's lines keep the offset they were written with ("...-08:00"),
        /// the saves' notes wrote the clock alone, and a load turned each line's time into the new zone, 11 hours later, so every line fell after
        /// its save and none came back (the talks of the other saves lost theirs too). These checks write the records in a zone 11 or 13 hours
        /// from this computer's, whatever it is, and load them here, so they fail with the rule of 0.11.1 to 0.21.4 in any time zone.
        /// </summary>
        private static void ZoneTests(string root)
        {
            TimeSpan here = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            double th = here.TotalHours + 11;
            if (th > 14) th -= 24;
            TimeSpan there = TimeSpan.FromHours(th);
            string Line(DateTime clock, TimeSpan offset) => new DateTimeOffset(clock, offset).ToString("o", Text.Inv);   // a line's wall, as Lieutenant.Keep writes it
            string OldNote(DateTime clock) => clock.ToString("yyyy-MM-dd HH:mm:ss.fff", Text.Inv);                         // a note's wall before 0.21.5: the clock alone
            string NewNote(DateTime clock, TimeSpan offset) => new DateTimeOffset(clock, offset).ToString("yyyy-MM-dd HH:mm:ss.fffzzz", Text.Inv);

            string data = Path.Combine(root, "zone-data"), dir = Path.Combine(data, "camp"), chat = Path.Combine(dir, "lieutenant", "chat.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(chat));
            DateTime made = new DateTime(2026, 11, 16, 18, 43, 47, 836), saved = new DateTime(2026, 11, 17, 21, 51, 14, 169), later = new DateTime(2026, 11, 19, 9, 0, 0);
            // the real records' shape, all written in the old zone: session M made MERGE at hour 100; session R loaded it and saved it again at 110.
            // Session Q made LATER at hour 200 with a note of 0.21.5 (its offset said), and the zone moved while it ran.
            File.WriteAllText(History.NotesFile(dir),
                "{\"type\":\"save\",\"save\":\"MERGE\",\"load\":\"M\",\"hours\":100,\"date\":\"Summer 21, 1087, 06:00\",\"before\":[],\"wall\":\"" + OldNote(made) + "\"}\n"
                + "{\"type\":\"save\",\"save\":\"MERGE\",\"load\":\"R\",\"hours\":110,\"date\":\"Summer 21, 1087, 16:00\",\"before\":[{\"load\":\"M\",\"until\":100,\"wall\":\"" + OldNote(made) + "\"}],\"wall\":\"" + OldNote(saved) + "\"}\n"
                + "{\"type\":\"save\",\"save\":\"LATER\",\"load\":\"Q\",\"hours\":200,\"date\":\"Summer 25, 1087, 08:00\",\"before\":[],\"wall\":\"" + NewNote(later, there) + "\"}\n");
            string L(string load, double hours, string wall, string k, string t) => "{\"load\":\"" + load + "\",\"h\":" + hours.ToString(Text.Inv) + ",\"w\":\"" + wall + "\",\"k\":\"" + k + "\",\"t\":\"" + t + "\"}\n";
            File.WriteAllText(chat,
                L("M", 99, Line(made.AddMinutes(-20), there), "you", "M: hold the fords.")
                + L("M", 100, Line(made.AddSeconds(-30), there), "lt", "M: done, before its save, paused.")
                + L("M", 100, Line(made.AddMinutes(1), there), "you", "M: after its save, the same paused hour.")
                + L("R", 104, Line(saved.AddMinutes(-6), there), "you", "R: gather at Baracharn.")
                + L("R", 110, Line(saved.AddSeconds(-15), there), "lt", "R: done, before the save, paused.")
                + L("R", 110, Line(saved.AddSeconds(30), there), "you", "R: after the save, the same paused hour.")
                + L("R", 112, Line(saved.AddMinutes(5), there), "heard", "R: after the save, the clock running.")
                + L("Q", 199, Line(later.AddMinutes(-10), there), "you", "Q: guard Imnagath.")
                // the zone moved during Q's session: written here, an instant before its save, in the same paused hour
                + L("Q", 200, new DateTimeOffset(later.AddSeconds(-5) - there + here, here).ToString("o", Text.Inv), "lt", "Q: done, written in the new zone before the save.")
                + L("Q", 200, Line(later.AddSeconds(20), there), "you", "Q: after the save."));

            // the conversation of a save loaded in another time zone than it was played in
            History loaded = History.Of("NOW", 110, "MERGE", dir, false);
            List<string> got = LabelledLines.Read(chat, loaded, out int ofSessions, out int off).Select(o => (string)o["t"]).ToList();
            Check(Math.Abs((there - here).TotalHours) >= 11 && loaded.Found == "marked" && ofSessions == 7 && off == 0
                  && got.SequenceEqual(new[] { "M: hold the fords.", "M: done, before its save, paused.", "R: gather at Baracharn.", "R: done, before the save, paused." }),
                  "a save's conversation comes back whole when the computer's time zone has moved since it was played (records at " + there + ", loaded at " + here
                  + "), and nothing said after its save, the same paused hour included: " + string.Join(" | ", got));
            History q = History.Of("NOW2", 200, "LATER", dir, false);
            List<string> gotQ = LabelledLines.Read(chat, q).Select(o => (string)o["t"]).ToList();
            Check(gotQ.SequenceEqual(new[] { "Q: guard Imnagath.", "Q: done, written in the new zone before the save." }),
                  "a note that says its offset (0.21.5) compares as an instant, so a line written after the zone moved still falls on the right side of its save: " + string.Join(" | ", gotQ));
            Check(loaded.HoldsLine("R", 104, Line(saved.AddHours(13), there), out bool clockOff) && clockOff && !loaded.HoldsLine("R", 116, Line(saved.AddHours(-13), there), out bool clockOff2) && clockOff2,
                  "a line of an earlier in-game hour than its save is the save's, of a later one is not, whatever the computer's clock said (the disagreement is counted for the log)");

            // the notes written from now on say their offset, and pass the steps before on as they were written
            JObject note = Json.Raw(Json.Line(loaded.NoteFor("MERGE 2", 115)));
            WallTime? nw = WallTime.Parse((string)note["wall"]);
            Check(nw?.Offset == here && ((string)note["wall"]).Substring(10, 1) == " " && (string)note["before"]?[0]?["wall"] == OldNote(made) && (string)note["before"]?[1]?["wall"] == OldNote(saved),
                  "a save's note now writes its wall clock with its offset (" + note["wall"] + "), and keeps the walls of the steps before it exactly as their notes wrote them");

            // the talks of the other saves: the same records, from another save
            string channel = Path.Combine(root, "zone-channel");
            List<LieutenantChats.Talk> talks = LieutenantChats.Write(channel, data, "camp", "ANOTHER");
            string index = File.Exists(Path.Combine(channel, LieutenantChats.Folder, "index.txt")) ? File.ReadAllText(Path.Combine(channel, LieutenantChats.Folder, "index.txt")) : "";
            Check(talks.Count == 2 && talks[0].Save == "LATER" && talks[1].Save == "MERGE" && talks[1].Words == 2 && talks[1].Text.Contains("R: done, before the save, paused.") && !talks[1].Text.Contains("after the save")
                  && index.Contains("(Nov 17, 2026 21:51 by the clock)"),
                  "the lieutenant's talks in its other saves come back whole too after the zone moved, newest first, each save's clock as its note wrote it: " + index.Replace("\n", " | "));
        }

        /// <summary>
        /// "SmokeTest --chat (campaign data folder) (save)" (0.21.5): what a load of that save brings back into the lieutenant's window, from the real
        /// records, without the game. It only reads them; its own log folder is a scratch one.
        /// </summary>
        private static int ChatAtLoad(string[] args)
        {
            Environment.SetEnvironmentVariable("BANNERLORD_AGENTS_ROOT", Path.Combine(Path.GetTempPath(), "BannerlordAgents-chatcheck"));
            return ChatAtLoadRun(args[1], args[2]);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ChatAtLoadRun(string dir, string save)
        {
            if (!History.LatestNotes(dir).TryGetValue(save, out JObject note)) { Console.WriteLine("No note of the save '" + save + "' in " + dir); return 1; }
            History h = History.Of("chat-check", (double?)note["hours"] ?? double.NaN, save, dir, false);
            List<JObject> lines = LabelledLines.Read(Path.Combine(dir, "lieutenant", "chat.jsonl"), h, out int ofSessions, out int off);
            List<string> shown = lines.Select(Lieutenant.Shown).Where(x => x != null).Select(x => x.Value.Key).ToList();
            Console.WriteLine(h.ToString());
            Console.WriteLine("Lines brought back: " + lines.Count + " of the " + ofSessions + " its sessions wrote; shown in the window: " + shown.Count + "; wall clocks disagreeing with the hour: " + off);
            foreach (string s in shown.Skip(Math.Max(0, shown.Count - 6)))
                Console.WriteLine("  " + (s.Length > 160 ? s.Substring(0, 160).Replace("\n", " ") + "..." : s.Replace("\n", " ")));
            // the talks of the other saves, as that load would write them, into the scratch folder (never the lieutenant's)
            List<LieutenantChats.Talk> talks = LieutenantChats.Write(Path.Combine(Paths.Root, "chat-check-channel"), Path.GetDirectoryName(Path.GetFullPath(dir).TrimEnd('\\', '/')), Path.GetFileName(Path.GetFullPath(dir).TrimEnd('\\', '/')), save);
            Console.WriteLine("Its talks in other saves: " + talks.Count + " saves, " + talks.Sum(t => t.Lines) + " lines (" + string.Join(", ", talks.Select(t => t.Save + " " + t.Words)) + ")");
            return shown.Count > 0 ? 0 : 2;
        }
    }
}
