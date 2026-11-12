using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// The continuous lieutenant (0.18.4), with no calls: its instructions with the one conversation and the handover laid over its own, the first message of a
        /// conversation (the handover of its day, the save's conversation, the whole report), a later message (only the new receipts and chronicle lines, the big lists
        /// in a line within their hours and whole after, a section as last shown in a line, a rule paragraph once), a reload at the same hour carried on and an earlier
        /// save begun again, and the handovers kept, chosen by their hour and set aside when an earlier save is loaded.
        /// </summary>
        private static void LieutenantContinuousTests(string root)
        {
            // the instructions: the lieutenant's own with the continuous sections laid over them
            string agentDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "agent");
            string sys = Path.Combine(agentDir, "lieutenant-system.template.md"), tpl = Path.Combine(agentDir, "lieutenant-continuous.template.md");
            if (File.Exists(sys) && File.Exists(tpl))
            {
                string full = Room.Overlay(File.ReadAllText(sys).Replace("\r", ""), File.ReadAllText(tpl));
                int At(string h) => full.IndexOf("\n" + h + "\n", StringComparison.Ordinal);
                Check(At("## How you are asked") > 0 && At("## Your memory") > At("## How you are asked") && At("## Your handover") > At("## Your memory") && At("## Look-ups") > At("## Your handover")
                      && full.Contains("report.txt") && full.Contains("one conversation") && !full.Contains("Each message has the conversation in this save so far")
                      && At("## Who you are") > 0 && At("## Answer format") > 0 && !full.Contains("This file is put over"),
                      "the continuous lieutenant's instructions: how it is asked replaced, its memory and handover added in place, the rest its own");
                string shape = full.Substring(At("## Your handover"));
                int next = shape.IndexOf("\n## ", 5, StringComparison.Ordinal);
                if (next > 0) shape = shape.Substring(0, next);
                string[] parts = { "1. HOW WE GOT HERE", "2. WHERE THE WAR STANDS", "3. WHAT MY RULER WANTS", "4. ORDERS IN FORCE", "5. WHAT IS WAITING", "6. LESSONS", "7. WHAT I WAS ABOUT TO DO" };
                Check(parts.All(shape.Contains) && parts.Select(x => shape.IndexOf(x, StringComparison.Ordinal)).SequenceEqual(parts.Select(x => shape.IndexOf(x, StringComparison.Ordinal)).OrderBy(i => i))
                      && shape.Contains("game's own list") && shape.Contains("1,000 and 3,000 words"), "the lieutenant's handover: its seven parts in order, the orders from the game's own list");
            }

            string dir = Path.Combine(root, "lieutenant-continuous");
            string channel = Path.Combine(dir, "warroom");
            Directory.CreateDirectory(channel);
            var c = new Config { AgentDir = dir, Channel = "warroom", MemoryMode = "continuous" };
            var room = new LieutenantRoom(c, Path.Combine(dir, "logs"));

            // the handovers: kept with their hour, the newest of the day taken, those after an earlier save's hour set aside
            string hdir = Path.Combine(dir, "memory", "handovers");
            Directory.CreateDirectory(hdir);
            LieutenantRoom.KeepHandover(hdir, "HANDOVER, Summer 21. The first.", 90, "Summer 21, 1087, 18:00");
            LieutenantRoom.KeepHandover(hdir, "HANDOVER, Summer 22. The second.", 115, "Summer 22, 1087, 19:00");
            Check(LieutenantRoom.NewestHandover(hdir, 120, out string d1)?.Contains("The second.") == true && d1 == "Summer 22, 1087, 19:00"
                  && LieutenantRoom.NewestHandover(hdir, 100, out _)?.Contains("The first.") == true && LieutenantRoom.NewestHandover(hdir, 50, out _) == null,
                  "the handover taken is the newest written at or before the hour");
            Check(LieutenantRoom.VoidHandoversAfter(hdir, 100) == 1 && LieutenantRoom.NewestHandover(hdir, 500, out _)?.Contains("The first.") == true && LieutenantRoom.VoidHandoversAfter(hdir, 100) == 0,
                  "loading a save from before a handover sets it aside, once");

            // a reload: the first load is only noted; one at or after the hour reached carries the conversation on; an earlier save begins a new one
            string mem = Path.Combine(dir, "memory", "camp");
            Directory.CreateDirectory(mem);
            Talk talk = Talk.Load(mem);
            room.UseForTest(talk, mem);
            JsonObject Chat(string load, double hours, double? loadedAt = null, string said = "How stands the north?") => (JsonObject)JsonNode.Parse(
                "{\"number\":5,\"said\":\"" + said + "\",\"date\":\"Summer 20, 1087, 22:00\",\"hours\":" + hours + ",\"campaign\":\"camp\",\"realm\":\"player\",\"load_id\":\"" + load + "\""
                + (loadedAt != null ? ",\"loaded_at\":" + loadedAt.Value : "") + ",\"history\":[\"Summer 20 · You: the old line\",\"Summer 20 · Lieutenant: an answer\"],\"history_total\":2,\"party_ai\":\"Holding: none.\"}");
            Check(room.EnsureTalk(mem, Chat("L1", 100), 100) == null && talk.LoadId == "L1", "the first load is only noted");
            talk.Started = true;
            talk.SessionId = "s1";
            talk.LastHours = 120;
            string again = room.EnsureTalk(mem, Chat("L2", 126, 125), 126);
            Check(again != null && again.StartsWith("THE GAME WAS LOADED AGAIN", StringComparison.Ordinal) && talk.Started && talk.SessionId == "s1" && talk.LoadId == "L2",
                  "a load at or after the hour the conversation reached carries it on, told");
            room.EnsureTalk(mem, Chat("L3", 111, 110), 111);
            Check(!room.TalkNow.Started && room.TalkNow.SessionId == null && (room.TalkNow.NextWhy ?? "").Contains("earlier than your last conversation reached") && room.TalkNow.LastHours == 110 && room.TalkNow.LoadId == "L3",
                  "an earlier save's load begins a new conversation, told why, from that save's hour");

            // the first message of a conversation: the handover of its day, the save's conversation, the whole report, and each section kept as shown
            LieutenantRoom.KeepHandover(mem, "HANDOVER, Summer 19. Hold the fords; the ruler wants proposals before any siege.", 105, "Summer 19, 1087, 09:00");
            string rule = "Each lord's line says where he is, what he does, his men and strength, and the hours to reach your ruler's party; a lord of the ruler's own clan is marked so, and so is a lord held by Party AI Controls.";
            string Report(string lords, string view, string history, string orders = "- Dain: defend Erebor.") =>
                "SITUATION AT Summer 20, 1087, 22:00 (refreshed every in-game hour)\n\nREPORT FOR KINGDOM OF ARNOR AND GONDOR\n\nYOUR ORDERS IN FORCE\n" + orders + "\n\n"
                + "YOUR LORDS AND ARMIES (2, 900 men)\n" + rule + "\n" + lords + "\n\nIN VIEW (1)\n" + view + "\n\nRECENT HISTORY, LAST TWO DAYS (from your chronicle)\n" + history + "\n";
            File.WriteAllText(Path.Combine(channel, "receipts.log"), "r1: Dain's defend applied\nr2: Thorin's recruit applied\n");
            string open = room.Opening(Chat("L3", 112), mem, Report("- Dain at Erebor, 400 men.\n- Thorin at Dale, 500 men.", "- Orc band, 60 men, 4 hours away.", "- Summer 20 · An orc band raided Dale."), 112);
            Check(open.StartsWith("A NEW CONVERSATION WITH YOUR RULER BEGINS: the game was loaded from a save", StringComparison.Ordinal) && open.Contains("YOUR HANDOVER (written by you, as of Summer 19, 1087, 09:00)")
                  && open.Contains("Hold the fords") && open.Contains("THE CONVERSATION IN THIS SAVE SO FAR") && open.Contains("the old line") && open.Contains("- Thorin at Dale, 500 men.") && open.Contains(rule)
                  && open.Contains("r2: Thorin's recruit applied") && open.Contains("PARTY AI CONTROLS NOW") && open.TrimEnd().EndsWith("\"How stands the north?\"", StringComparison.Ordinal),
                  "the first message: why, the handover of its day, the save's conversation, what became of the orders, the whole report and the words");
            Check(talk.SentAt.ContainsKey("YOUR LORDS AND ARMIES") && talk.Sent.ContainsKey("IN VIEW"), "and the conversation keeps each section of the report as shown");

            // a later message two hours on: only what is new
            talk.Started = true;
            File.WriteAllText(Path.Combine(channel, "receipts.log"), "r1: Dain's defend applied\nr2: Thorin's recruit applied\nr3: Thorin's recruit ended: no volunteers\nr4: Dain's defend: he struck the band\n");
            string later = room.Later(Chat("L3", 114, said: "And now?"), Report("- Dain at Erebor, 380 men.\n- Thorin at Dale, 500 men.", "- Orc band, 60 men, 4 hours away.",
                                                                                 "- Summer 20 · An orc band raided Dale.\n- Summer 20 · Dain struck the band."), null, 114);
            Check(later.Contains("r3: Thorin's recruit ended") && later.Contains("r4: Dain's defend") && !later.Contains("r2: Thorin's recruit applied") && later.Contains("WHAT BECAME OF ORDERS SINCE YOUR LAST ANSWER"),
                  "a later message: only the receipts since the last answer");
            Check(later.Contains("YOUR LORDS AND ARMIES (2, 900 men): changed since you saw it 2 hours ago; the whole of it as of this hour is in report.txt") && !later.Contains("380 men")
                  && later.Contains("IN VIEW (1): as in your last message that showed it.") && later.Contains("YOUR ORDERS IN FORCE: as in your last message that showed it."),
                  "a big list within its hours is a line (changed or not), a section as last shown is a line");
            Check(later.Contains("Dain struck the band.") && !later.Contains("An orc band raided Dale.") && !later.Contains("THE CONVERSATION IN THIS SAVE SO FAR") && later.Contains("PARTY AI CONTROLS NOW: as in your last message")
                  && later.TrimEnd().EndsWith("\"And now?\"", StringComparison.Ordinal), "only the chronicle's new lines, no conversation again, Party AI Controls in a line when unchanged, and the words");
            string after = room.Later(Chat("L3", 121, said: "Report."), Report("- Dain at Erebor, 350 men.\n- Thorin at Dale, 500 men.", "- Orc band, 60 men, 4 hours away.", "- Summer 20 · Dain struck the band."), null, 121);
            Check(after.Contains("- Dain at Erebor, 350 men.") && !after.Contains(rule) && after.Contains("RECENT HISTORY, LAST TWO DAYS (from your chronicle): nothing new since your last message.") && after.Contains("- Nothing new."),
                  "past its hours a big list is whole again, its rule paragraph not repeated; no new chronicle lines or receipts say so");
            Check(LieutenantRoom.ReportDate(Report("", "", "")) == "Summer 20, 1087, 22:00" && LieutenantRoom.ReportDate("no date") == null, "the handover's date is read from the report's first line");

            // 0.18.7: the campaign's one conversation taken back with an earlier save, by the save's history the message carries (save_history, module 0.18.5)
            string rmem = Path.Combine(dir, "memory", "rollback");
            Directory.CreateDirectory(rmem);
            var rroom = new LieutenantRoom(c, Path.Combine(dir, "logs-rollback"));
            Talk rt = Talk.Load(rmem);
            rroom.UseForTest(rt, rmem);
            rt.SessionId = "lt1"; rt.Started = true; rt.LoadId = "L1"; rt.Tokens = 90000;
            rt.Checkpoint("m150", 150, "Summer 2, 1087, 06:00", "L1");
            rt.Tokens = 120000;
            rt.Checkpoint("m190", 190, "Summer 3, 1087, 22:00", "L1");
            rt.LastHours = 200;
            rt.Save();
            JsonObject Loaded(string load, double hours, double loadedAt, string history) => (JsonObject)JsonNode.Parse(
                "{\"number\":9,\"said\":\"Report.\",\"date\":\"Summer 3, 1087, 10:00\",\"hours\":" + hours + ",\"campaign\":\"rollback\",\"realm\":\"player\",\"load_id\":\"" + load + "\",\"loaded_at\":" + loadedAt
                + (history != null ? ",\"save_history\":{\"before\":" + history + "}" : "") + "}");
            List<KeyValuePair<string, double>> parsed = LieutenantRoom.SaveHistory(Loaded("L2", 171, 170, "[{\"load\":\"L0\",\"until\":100},{\"load\":\"L1\",\"until\":170}]"));
            Check(parsed != null && parsed.Count == 2 && parsed[1].Key == "L1" && parsed[1].Value == 170 && LieutenantRoom.SaveHistory(Loaded("L2", 171, 170, null)) == null,
                  "the lieutenant reads the save's history from the message");
            string told = rroom.EnsureTalk(rmem, Loaded("L2", 171, 170, "[{\"load\":\"L0\",\"until\":100},{\"load\":\"L1\",\"until\":170}]"), 171);
            Check(told != null && told.StartsWith("THE GAME WAS LOADED FROM AN EARLIER SAVE", StringComparison.Ordinal) && told.Contains("Summer 2, 1087, 06:00")
                  && rroom.TalkNow.Started && rroom.TalkNow.ForkFrom == "lt1" && rroom.TalkNow.ForkAt == "m150" && rroom.TalkNow.SessionId != "lt1" && rroom.TalkNow.Tokens == 90000
                  && rroom.TalkNow.LastHours == 170 && rroom.TalkNow.LoadId == "L2",
                  "an earlier save's load takes the lieutenant's conversation back to its last answer within the save's history, and tells it so");
            Check(rroom.EnsureTalk(rmem, Loaded("L3", 260, 250, "[{\"load\":\"L2\",\"until\":250}]"), 260) != null && rroom.TalkNow.ForkAt == "m150",
                  "a later load where the conversation left off carries it on");
        }
    }
}
