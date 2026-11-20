using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>One step of a save's history: what one game session wrote (its load id; null for what was written before
    /// records were labelled, 0.9.1), up to an in-game hour.</summary>
    internal sealed class HistoryStep
    {
        public string Load;
        public double Until = double.NaN;
        /// <summary>The wall clock of the save that ended this step (0.11.1; null in notes written before), for lines written while paused, as the note wrote it.</summary>
        public WallTime? Wall;
    }

    /// <summary>
    /// A wall clock as a record wrote it (0.21.5): the clock time it showed and, when it says one, its offset from UTC. Saves' notes wrote the
    /// clock alone until 0.21.5 ("2026-11-17 21:51:14.169"), the lieutenant's lines write it with the offset ("2026-11-17T21:49:12.07-08:00").
    /// Until 0.21.5 a line's time was turned into this computer's time zone and compared with the note's clock as written, so once the laptop's
    /// time zone changed (Pacific to Turkey, between Nov 17 and Nov 19) every line of every save fell 11 hours after its save and none came back.
    /// Two walls compare as instants when both say their offset, and otherwise by the clocks as written: a line is only ever compared with the
    /// note of the save its own session made, so both were written by one session on one computer, in one time zone.
    /// </summary>
    internal struct WallTime
    {
        /// <summary>The clock time as written, never turned into another zone.</summary>
        public DateTime Clock;
        /// <summary>Its offset from UTC, when the record says one.</summary>
        public TimeSpan? Offset;
        /// <summary>The text as the record wrote it (a note's history passes it on unchanged).</summary>
        public string Written;

        public static WallTime? Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            if (!DateTime.TryParse(s, Text.Inv, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt)) return null;
            if (dt.Kind == DateTimeKind.Unspecified) return new WallTime { Clock = dt, Written = s };
            if (DateTimeOffset.TryParse(s, Text.Inv, System.Globalization.DateTimeStyles.None, out DateTimeOffset o)) return new WallTime { Clock = o.DateTime, Offset = o.Offset, Written = s };
            return new WallTime { Clock = dt, Written = s };
        }

        /// <summary>At or before another: as instants when both say their offset, else by the clocks as written.</summary>
        public bool AtOrBefore(WallTime other)
            => Offset.HasValue && other.Offset.HasValue ? Clock - Offset.Value <= other.Clock - other.Offset.Value : Clock <= other.Clock;

        /// <summary>For ordering saves: the instant when the offset is known, else the clock as written.</summary>
        public DateTime SortKey => Offset.HasValue ? Clock - Offset.Value : Clock;

        /// <summary>Now, as a note writes it since 0.21.5: the clock with its offset ("2026-11-20 10:06:55.123+03:00"; a space, not a T, as before).</summary>
        public static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fffzzz", Text.Inv);
    }

    /// <summary>
    /// Which records make up the history of the game now running (since 0.9.1). Every chronicle line, and every copy of an
    /// agent's notes and story in its war room, is labelled with the game session (load id) that wrote it, and nothing is
    /// deleted. At every save the bridge notes in the campaign's own saves.jsonl - never in the save itself (nothing of ours
    /// goes into a save) - the save's name, its in-game hour, the session that made it and that session's own history. A
    /// load finds its save there by name and hour, so its history is exactly what that save's world lived through, whether
    /// the save is earlier, later or on another branch of play than the last one loaded. A save with no such note (made before
    /// 0.9.1, renamed, or made without this mod) has the records written before labels began, up to its date: the history as
    /// it stood then, which the old rule kept to one line of play.
    /// </summary>
    internal sealed class History
    {
        /// <summary>This session's id, which labels everything written now (empty in the smoke test: unlabelled).</summary>
        public string Load = "";
        /// <summary>The sessions this save's history came from, oldest first, each up to the hour it was saved at.</summary>
        public List<HistoryStep> Before = new List<HistoryStep>();
        /// <summary>The save loaded (null: a new game, or not known) and how its history was found: marked, unmarked, new.</summary>
        public string Save, Found = "unmarked";

        /// <summary>Did this record, written by session load at in-game hour hours, happen in this game's history?</summary>
        public bool Holds(string load, double hours)
        {
            if (!string.IsNullOrEmpty(load) && load == Load) return true;
            foreach (HistoryStep s in Before)
                if (Same(s.Load, load) && (double.IsNaN(s.Until) || hours <= s.Until + 0.01)) return true;
            return false;
        }

        /// <summary>
        /// The same for a line of the lieutenant's conversation (0.11.1): many are written with the clock stopped, so a line said in its save's
        /// own in-game hour is told by the wall clock when the note has one (a line said after a save, in the same paused hour, is not that
        /// save's). Since 0.21.5 the in-game hour decides every other line, since a session's clock only runs forward: a line of an earlier
        /// hour than its save is the save's, of a later hour is not, whatever the computer's clock said (on Nov 19 its time zone moved 11
        /// hours and every line fell after its save); and the walls compare as WallTime does. wall is the line's time as written.
        /// </summary>
        public bool HoldsLine(string load, double hours, string wall) => HoldsLine(load, hours, wall, out _);

        /// <summary>The same, and whether the line's wall clock disagreed with its in-game hour about its save (a computer clock moved; the hour was taken).</summary>
        public bool HoldsLine(string load, double hours, string wall, out bool clockOff)
        {
            clockOff = false;
            if (!string.IsNullOrEmpty(load) && load == Load) return true;
            WallTime? w = null;
            bool parsed = false;
            foreach (HistoryStep s in Before)
            {
                if (!Same(s.Load, load)) continue;
                // by the in-game hour: before the save's hour, after it, or (null) its very hour, or not known
                bool? byHour = double.IsNaN(s.Until) || double.IsNaN(hours) ? (bool?)null : hours < s.Until - SameHour ? true : hours > s.Until + SameHour ? false : (bool?)null;
                bool? byWall = null;
                if (s.Wall.HasValue)
                {
                    if (!parsed) { w = WallTime.Parse(wall); parsed = true; }
                    if (w.HasValue) byWall = w.Value.AtOrBefore(s.Wall.Value);
                }
                if (byHour.HasValue && byWall.HasValue && byHour.Value != byWall.Value) clockOff = true;
                if (byHour ?? byWall ?? (double.IsNaN(s.Until) || hours <= s.Until + SameHour)) return true;
            }
            return false;
        }

        /// <summary>How near its save's hour a line counts as said in that very hour (in-game hours, as before 0.21.5).</summary>
        private const double SameHour = 0.01;

        private static WallTime? Wall(JToken t) => t == null || t.Type == JTokenType.Null ? (WallTime?)null : WallTime.Parse((string)t);

        private static bool Same(string a, string b) => string.IsNullOrEmpty(a) ? string.IsNullOrEmpty(b) : a == b;

        /// <summary>The label for a record written now (null when this session has none, as in the smoke test).</summary>
        public string Label => string.IsNullOrEmpty(Load) ? null : Load;

        /// <summary>A history with no note: the unlabelled records up to that hour (NaN: all of them).</summary>
        public static History Unmarked(string load, double until)
            => new History { Load = load ?? "", Before = new List<HistoryStep> { new HistoryStep { Load = null, Until = until } } };

        /// <summary>
        /// The history of a session loaded from a save: the note of the latest save of that name at that hour (a save of the same
        /// name is overwritten, so the latest note is the file's), else unmarked up to the save's hour. A new game has none.
        /// </summary>
        public static History Of(string load, double loadedAt, string save, string campaignDir, bool newGame)
        {
            if (newGame) return new History { Load = load ?? "", Save = null, Found = "new" };
            if (!string.IsNullOrEmpty(save))
                foreach (JObject o in Notes(campaignDir).AsEnumerable().Reverse())
                {
                    if ((string)o["save"] != save) continue;
                    double h = (double?)o["hours"] ?? double.NaN;
                    if (double.IsNaN(h) || double.IsNaN(loadedAt) || Math.Abs(h - loadedAt) > 0.05) continue;
                    History marked = FromNote(o);
                    marked.Load = load ?? "";
                    return marked;
                }
            History u = Unmarked(load, loadedAt);
            u.Save = save;
            return u;
        }

        /// <summary>The history a save's note holds (0.16, also for the lieutenant's talks in other saves): its sessions, each up to its hour; no session of its own.</summary>
        public static History FromNote(JObject o)
        {
            double h = (double?)o["hours"] ?? double.NaN;
            var steps = new List<HistoryStep>();
            if (o["before"] is JArray before)
                foreach (JToken t in before)
                    steps.Add(new HistoryStep { Load = (string)t["load"], Until = (double?)t["until"] ?? double.NaN, Wall = Wall(t["wall"]) });
            steps.Add(new HistoryStep { Load = (string)o["load"], Until = h, Wall = Wall(o["wall"]) });
            return new History { Load = "", Before = steps, Save = (string)o["save"], Found = "marked" };
        }

        /// <summary>Every save's latest note in a campaign's folder, by save name (a save of the same name is overwritten, so the latest is the save as it is now).</summary>
        public static Dictionary<string, JObject> LatestNotes(string campaignDir)
        {
            var latest = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (JObject o in Notes(campaignDir))
                if ((string)o["type"] == "save" && !string.IsNullOrEmpty((string)o["save"])) latest[(string)o["save"]] = o;
            return latest;
        }

        /// <summary>The note to write when this session saves: the save, its hour, this session, and this session's history.</summary>
        public object NoteFor(string save, double hours) => new
        {
            Type = "save", Save = save, Load = Label, Hours = hours, Date = Text.When(hours),
            // each step's wall as its note wrote it; this save's with its offset from UTC (0.21.5)
            Before = Before.Select(s => new { s.Load, Until = double.IsNaN(s.Until) ? (double?)null : s.Until, Wall = s.Wall?.Written }).ToList(),
            Wall = WallTime.Now()
        };

        /// <summary>The history for a war room (poke.json): this session and the steps before it.</summary>
        public object ForAgent() => new { Load = Label, Save, Found, Before = Before.Select(s => new { s.Load, Until = double.IsNaN(s.Until) ? (double?)null : s.Until }).ToList() };

        public static string NotesFile(string campaignDir) => Path.Combine(campaignDir, "saves.jsonl");

        private static List<JObject> Notes(string campaignDir)
        {
            var list = new List<JObject>();
            try
            {
                string f = NotesFile(campaignDir);
                if (!File.Exists(f)) return list;
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0) continue;
                        try { list.Add(Json.Raw(line)); }   // walls as written (0.21.5)
                        catch (Newtonsoft.Json.JsonException) { }   // a line half-written when the game closed
                    }
                }
            }
            catch (Exception e) { Diag.Swallowed("reading the saves' history notes", e); }
            return list;
        }

        /// <summary>In words, for the log.</summary>
        public override string ToString()
        {
            if (Found == "new") return "a new game: its history starts now";
            string steps = string.Join(", then ", Before.Select(s => (string.IsNullOrEmpty(s.Load) ? "the records from before labels began" : "session " + s.Load)
                                                                  + (double.IsNaN(s.Until) ? "" : " up to " + Text.When(s.Until))));
            return (Found == "marked" ? "save '" + Save + "', from its note: " : "save '" + (Save ?? "?") + "', with no note (made before 0.9.1, renamed, or without this mod): ") + steps;
        }
    }
}
