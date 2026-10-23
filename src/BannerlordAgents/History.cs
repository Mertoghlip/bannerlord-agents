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
                    var steps = new List<HistoryStep>();
                    if (o["before"] is JArray before)
                        foreach (JToken t in before)
                            steps.Add(new HistoryStep { Load = (string)t["load"], Until = (double?)t["until"] ?? double.NaN });
                    steps.Add(new HistoryStep { Load = (string)o["load"], Until = h });
                    return new History { Load = load ?? "", Before = steps, Save = save, Found = "marked" };
                }
            History u = Unmarked(load, loadedAt);
            u.Save = save;
            return u;
        }

        /// <summary>The note to write when this session saves: the save, its hour, this session, and this session's history.</summary>
        public object NoteFor(string save, double hours) => new
        {
            Type = "save", Save = save, Load = Label, Hours = hours, Date = Text.When(hours),
            Before = Before.Select(s => new { s.Load, Until = double.IsNaN(s.Until) ? (double?)null : s.Until }).ToList(),
            Wall = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Text.Inv)
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
                        try { if (JToken.Parse(line) is JObject o) list.Add(o); }
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
