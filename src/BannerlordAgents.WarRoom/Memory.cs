using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>One line of the realm's chronicle, as the bridge writes it into the channel (chronicle.jsonl).</summary>
    internal sealed class ChronicleLine
    {
        public double Hours;
        public string Date = "", Kind = "", Text = "";
    }

    /// <summary>
    /// A realm's memory between the agent's answers (docs/MEMORY.md), one folder per campaign in the agent's folder
    /// (memory\&lt;campaign&gt;\):
    /// - notes.md, the ruler's notes, which only the agent changes, through its answers: its plan (replaced), what it
    ///   knows about each enemy (one line per name, replaced by name) and its lessons (added; the chronicler merges them
    ///   when there are too many);
    /// - story.md, the story so far, which the chronicler rewrites from the bridge's chronicle in quiet moments;
    /// - state.json: how far the story reaches, the last poke answered and its in-game hour, this load of the game, and
    ///   the conversation (in the one-long-conversation mode);
    /// - snapshots.jsonl: the notes and the story after every change, with the in-game hour, so a reload of an earlier
    ///   save cuts the memory back to that date.
    /// Each file is written beside the old one and swapped in.
    /// </summary>
    internal sealed class Memory
    {
        public readonly string Campaign, Dir;
        public string Notes = "", Story = "";
        public double StoryHours = double.NaN, LastHours = double.NaN;
        public string StoryDate = "", LoadId = "", SessionId;
        /// <summary>The test run and arm of the latest poke (0.13): each snapshot of the notes and story carries them, for the run's bundle.</summary>
        public string Run, Arm;
        public int LastPoke = -1;
        public long SessionTokens;
        public bool SessionStarted;
        /// <summary>Goes up at every cut-back, so a story written meanwhile, from lines now cut, is thrown away.</summary>
        public int Generation;

        private static readonly object Gate = new object();
        public const string Title = "# Notes of the war council";

        private Memory(string campaign, string dir)
        {
            Campaign = campaign;
            Dir = dir;
        }

        private string NotesFile => Path.Combine(Dir, "notes.md");
        private string StoryFile => Path.Combine(Dir, "story.md");
        private string StateFile => Path.Combine(Dir, "state.json");
        private string SnapshotFile => Path.Combine(Dir, "snapshots.jsonl");

        /// <summary>
        /// The memory of one campaign. The first time, the old single journal (journal.md, from before memory was kept per
        /// campaign) becomes its notes: the plan and the lessons carry over, the list of lords' tasks does not (the orders in
        /// force say it every poke). The journal is then renamed, so it is never read twice.
        /// </summary>
        /// <param name="nowHours">The in-game hour of the poke that opens it. Notes taken over from the old journal are
        /// kept as of that hour, so loading a save from before it starts the realm afresh: the journal belonged to another
        /// run of the game (first live test, Oct 9: a Summer 9 save got notes written at Summer 17).</param>
        public static Memory Open(Config c, string campaign, Action<string> say, double nowHours = double.NaN)
        {
            string safe = Safe(string.IsNullOrWhiteSpace(campaign) ? "unknown" : campaign);
            var m = new Memory(campaign ?? "unknown", Path.Combine(c.MemoryDir, safe));
            bool fresh = !Directory.Exists(m.Dir) || !File.Exists(m.NotesFile);
            Directory.CreateDirectory(m.Dir);
            if (fresh)
            {
                string plan = "(not set yet)", lessons = "";
                bool migrated = false;
                if (File.Exists(c.Journal) && m.Campaign != "unknown")
                {
                    Dictionary<string, string> old = Sections(File.ReadAllText(c.Journal));
                    plan = Get(old, "Standing plan") ?? Get(old, "Plan") ?? plan;
                    lessons = Get(old, "Lessons") ?? "";
                    string stamp = "journal-before-memory-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
                    string moved = Path.Combine(c.AgentDir, stamp + ".md");
                    for (int i = 2; File.Exists(moved); i++) moved = Path.Combine(c.AgentDir, stamp + "-" + i + ".md");
                    File.Move(c.Journal, moved);
                    migrated = true;
                    say("Memory: the old journal became the notes of campaign " + m.Campaign + " (its plan and lessons); it is kept as " + Path.GetFileName(moved) + ".");
                }
                m.Notes = Compose(plan, new List<KeyValuePair<string, string>>(), Bullets(lessons));
                m.Save();
                // empty notes hold at any date; the old journal's only from now on (a save from before starts afresh)
                m.Snapshot(migrated && !double.IsNaN(nowHours) ? nowHours : -1, "", -1, migrated ? "seed from the old journal" : "seed");
                return m;
            }
            m.Load();
            return m;
        }

        private void Load()
        {
            Notes = Read(NotesFile) ?? "";
            Story = Read(StoryFile) ?? "";
            try
            {
                if (File.Exists(StateFile) && JsonNode.Parse(File.ReadAllText(StateFile)) is JsonObject s)
                {
                    StoryHours = Num(s["story_hours"]);
                    StoryDate = (string)s["story_date"] ?? "";
                    LastHours = Num(s["last_hours"]);
                    LastPoke = (int?)s["last_poke"] ?? -1;
                    LoadId = (string)s["load_id"] ?? "";
                    SessionId = (string)s["session_id"];
                    SessionTokens = (long?)s["session_tokens"] ?? 0;
                    SessionStarted = (bool?)s["session_started"] ?? false;
                }
            }
            catch (JsonException) { /* a state file cut short: the notes and the story are what matter */ }
        }

        public void Save()
        {
            lock (Gate)
            {
                Write(NotesFile, Notes);
                Write(StoryFile, Story);
                var s = new JsonObject
                {
                    ["campaign"] = Campaign, ["story_hours"] = double.IsNaN(StoryHours) ? null : StoryHours, ["story_date"] = StoryDate,
                    ["last_hours"] = double.IsNaN(LastHours) ? null : LastHours, ["last_poke"] = LastPoke, ["load_id"] = LoadId,
                    ["session_id"] = SessionId, ["session_tokens"] = SessionTokens, ["session_started"] = SessionStarted
                };
                Write(StateFile, s.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        /// <summary>The notes and the story as they stand, kept with the in-game hour they are good for.</summary>
        public void Snapshot(double hours, string date, int poke, string why)
        {
            lock (Gate)
            {
                var o = new JsonObject
                {
                    ["hours"] = double.IsNaN(hours) ? null : hours, ["date"] = date, ["poke"] = poke, ["why"] = why, ["load"] = string.IsNullOrEmpty(LoadId) ? null : LoadId, ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["run"] = Run, ["arm"] = Arm,
                    ["notes"] = Notes, ["story"] = Story, ["story_hours"] = double.IsNaN(StoryHours) ? null : StoryHours, ["story_date"] = StoryDate
                };
                File.AppendAllText(SnapshotFile, o.ToJsonString() + "\n", new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// A save was loaded, with the history the bridge found for it (since 0.9.1: which game sessions its world lived
        /// through, each up to the hour it was saved at). The notes and the story go back to the last copy written within that
        /// history, and no copy is dropped, so a later load of another save, earlier, later or on another branch of play, finds
        /// its own. Returns the date gone back to.
        /// </summary>
        public string Restore(List<KeyValuePair<string, double>> before, double loadedAt)
        {
            lock (Gate)
            {
                JsonObject last = null;
                if (File.Exists(SnapshotFile))
                    foreach (string line in File.ReadAllLines(SnapshotFile))
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            if (JsonNode.Parse(line) is not JsonObject o) continue;
                            if (InHistory(before, (string)o["load"], Num(o["hours"]))) last = o;
                        }
                        catch (JsonException) { }
                    }
                Notes = (string)last?["notes"] ?? Compose("(not set yet)", new List<KeyValuePair<string, string>>(), new List<string>());
                Story = (string)last?["story"] ?? "";
                StoryHours = Num(last?["story_hours"]);
                StoryDate = (string)last?["story_date"] ?? "";
                LastHours = loadedAt;
                Generation++;
                SessionId = null;   // a conversation held another history; it starts afresh
                SessionTokens = 0;
                SessionStarted = false;
                Save();
                return (string)last?["date"] ?? "";
            }
        }

        /// <summary>Was a copy written by that session (null: before copies were labelled) at that hour within this history?</summary>
        internal static bool InHistory(List<KeyValuePair<string, double>> before, string load, double hours)
        {
            foreach (KeyValuePair<string, double> s in before)
                if ((string.IsNullOrEmpty(s.Key) ? string.IsNullOrEmpty(load) : s.Key == load) && (double.IsNaN(s.Value) || double.IsNaN(hours) || hours <= s.Value + 0.01))
                    return true;
            return false;
        }

        /// <summary>
        /// An earlier save was loaded: the notes and the story go back to the last snapshot from no later than its date,
        /// and the snapshots after it are dropped. Returns the date gone back to.
        /// </summary>
        public string CutBack(double toHours)
        {
            lock (Gate)
            {
                var kept = new List<string>();
                JsonObject last = null;
                if (File.Exists(SnapshotFile))
                    foreach (string line in File.ReadAllLines(SnapshotFile))
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            if (JsonNode.Parse(line) is not JsonObject o) continue;
                            if (Num(o["hours"]) > toHours + 0.01) continue;
                            kept.Add(line);
                            last = o;
                        }
                        catch (JsonException) { }
                    }
                Notes = (string)last?["notes"] ?? Compose("(not set yet)", new List<KeyValuePair<string, string>>(), new List<string>());
                Story = (string)last?["story"] ?? "";
                StoryHours = Num(last?["story_hours"]);
                StoryDate = (string)last?["story_date"] ?? "";
                LastHours = toHours;
                Generation++;
                SessionId = null;   // a conversation held what came after; it starts afresh
                SessionTokens = 0;
                SessionStarted = false;
                Write(SnapshotFile, string.Concat(kept.Select(l => l + "\n")));
                Save();
                return (string)last?["date"] ?? "";
            }
        }

        // ---------- the notes ----------

        /// <summary>The agent's answer changes its notes: "plan" replaces the plan, "enemies" replaces each named note ("" removes it), "lessons" are added.</summary>
        public string Apply(JsonObject answer)
        {
            lock (Gate)
            {
                Dictionary<string, string> s = Sections(Notes);
                string plan = Get(s, "Plan") ?? "(not set yet)";
                List<KeyValuePair<string, string>> enemies = Enemies(Get(s, "Enemies") ?? "");
                List<string> lessons = Bullets(Get(s, "Lessons") ?? "");
                var changed = new List<string>();
                if (answer["plan"] is JsonValue pv && pv.TryGetValue(out string p) && !string.IsNullOrWhiteSpace(p)) { plan = p.Trim(); changed.Add("plan"); }
                if (answer["enemies"] is JsonObject eo)
                {
                    int n = 0;
                    foreach (KeyValuePair<string, JsonNode> kv in eo)
                    {
                        string name = kv.Key.Trim(), note = kv.Value?.ToString().Trim() ?? "";
                        if (name.Length == 0) continue;
                        int i = enemies.FindIndex(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase));
                        if (note.Length == 0) { if (i >= 0) enemies.RemoveAt(i); }
                        else if (i >= 0) enemies[i] = new KeyValuePair<string, string>(enemies[i].Key, note);
                        else enemies.Add(new KeyValuePair<string, string>(name, note));
                        n++;
                    }
                    if (n > 0) changed.Add(n + (n == 1 ? " enemy" : " enemies"));
                }
                if (answer["lessons"] is JsonArray la)
                {
                    int n = 0;
                    foreach (JsonNode l in la)
                    {
                        string t = l?.ToString().Trim() ?? "";
                        if (t.Length > 0 && !lessons.Contains(t)) { lessons.Add(t); n++; }
                    }
                    if (n > 0) changed.Add(n + (n == 1 ? " lesson" : " lessons"));
                }
                if (changed.Count == 0) return "";
                Notes = Compose(plan, enemies, lessons);
                return string.Join(", ", changed);
            }
        }

        public List<string> Lessons() { lock (Gate) return Bullets(Get(Sections(Notes), "Lessons") ?? ""); }

        /// <summary>The story and the notes as they stand, read together (the chronicler may be writing the story meanwhile).</summary>
        public void View(out string story, out double storyHours, out string storyDate, out string notes, out int generation)
        {
            lock (Gate)
            {
                story = Story;
                storyHours = StoryHours;
                storyDate = StoryDate;
                notes = NotesBody();
                generation = Generation;
            }
        }

        /// <summary>A new story so far from the chronicler, unless the memory was cut back since it began.</summary>
        public bool SetStory(string story, double hours, string date, int generation)
        {
            lock (Gate)
            {
                if (generation != Generation) return false;
                Story = story.Trim();
                StoryHours = hours;
                StoryDate = date;
                return true;
            }
        }

        /// <summary>The chronicler's merged lessons replace the ones it was given; any the agent added meanwhile are kept after them.</summary>
        public void ReplaceLessons(List<string> given, List<string> merged)
        {
            lock (Gate)
            {
                Dictionary<string, string> s = Sections(Notes);
                List<string> now = Bullets(Get(s, "Lessons") ?? "");
                var final = merged.Where(x => x.Trim().Length > 0).Select(x => x.Trim()).ToList();
                foreach (string l in now) if (!given.Contains(l) && !final.Contains(l)) final.Add(l);
                Notes = Compose(Get(s, "Plan") ?? "(not set yet)", Enemies(Get(s, "Enemies") ?? ""), final);
            }
        }

        /// <summary>The notes as the agent reads them: without their title.</summary>
        public string NotesBody()
        {
            string t = Notes.Replace("\r", "");
            if (t.StartsWith("# ", StringComparison.Ordinal)) { int nl = t.IndexOf('\n'); t = nl < 0 ? "" : t.Substring(nl + 1); }
            return t.Trim();
        }

        public static string Compose(string plan, List<KeyValuePair<string, string>> enemies, List<string> lessons)
        {
            var sb = new StringBuilder();
            sb.Append(Title).Append("\n\n## Plan\n").Append(plan.Trim()).Append("\n\n## Enemies\n");
            if (enemies.Count == 0) sb.Append("(none noted yet)\n");
            foreach (KeyValuePair<string, string> e in enemies) sb.Append("- ").Append(e.Key).Append(": ").Append(e.Value).Append('\n');
            sb.Append("\n## Lessons\n");
            if (lessons.Count == 0) sb.Append("(none yet)\n");
            foreach (string l in lessons) sb.Append("- ").Append(l).Append('\n');
            return sb.ToString();
        }

        /// <summary>"## heading" sections of a markdown file, by heading.</summary>
        public static Dictionary<string, string> Sections(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string head = null;
            var body = new StringBuilder();
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (head != null) d[head] = body.ToString().Trim();
                    head = raw.Substring(3).Trim();
                    body.Clear();
                    continue;
                }
                if (raw.StartsWith("# ", StringComparison.Ordinal)) continue;
                if (head != null) body.Append(raw).Append('\n');
            }
            if (head != null) d[head] = body.ToString().Trim();
            return d;
        }

        private static string Get(Dictionary<string, string> s, string head)
            => s.TryGetValue(head, out string v) && v.Length > 0 && !v.StartsWith("(not set", StringComparison.Ordinal) && !v.StartsWith("(none", StringComparison.Ordinal) ? v : null;

        private static List<string> Bullets(string text)
            => text.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.StartsWith("- ", StringComparison.Ordinal)).Select(x => x.Substring(2).Trim()).Where(x => x.Length > 0).ToList();

        private static List<KeyValuePair<string, string>> Enemies(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (string b in Bullets(text))
            {
                int c = b.IndexOf(": ", StringComparison.Ordinal);
                list.Add(c > 0 ? new KeyValuePair<string, string>(b.Substring(0, c).Trim(), b.Substring(c + 2).Trim()) : new KeyValuePair<string, string>(b, ""));
            }
            return list;
        }

        // ---------- the chronicle ----------

        /// <summary>The bridge's chronicle in the channel (chronicle.jsonl), oldest first; empty when there is none yet.</summary>
        public static List<ChronicleLine> ReadChronicle(string channelDir)
        {
            var list = new List<ChronicleLine>();
            string all = Room.ReadShared(Path.Combine(channelDir, "chronicle.jsonl"));
            if (string.IsNullOrEmpty(all)) return list;
            foreach (string line in all.Split('\n'))
            {
                if (line.Trim().Length < 2) continue;
                try
                {
                    if (JsonNode.Parse(line) is not JsonObject o) continue;
                    list.Add(new ChronicleLine { Hours = Num(o["hours"]), Date = (string)o["date"] ?? "", Kind = (string)o["kind"] ?? "", Text = (string)o["text"] ?? "" });
                }
                catch (JsonException) { /* the last line, still being written */ }
            }
            return list.Where(l => !double.IsNaN(l.Hours)).OrderBy(l => l.Hours).ToList();
        }

        // ---------- files ----------

        private static double Num(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return d;
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p)) return p;
            }
            return double.NaN;
        }

        private static string Read(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
        }

        public static void Write(string path, string text)
        {
            string tmp = path + ".part";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        private static string Safe(string id)
        {
            var sb = new StringBuilder(id.Length);
            foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }
    }
}
