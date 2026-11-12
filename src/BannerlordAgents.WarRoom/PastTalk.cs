using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// A continuous agent's earlier conversations, word for word (0.18.9). Nov 11: after a handover the agent kept its handover, notes and chronicle, but no longer what it
    /// was told and what it answered; it should keep access to that, as Claude Code keeps the transcript after it compacts. At each handover the conversation's Claude Code
    /// transcript is turned into its exchanges (what was sent, the thinking as summarized, the questions asked with their answers, the answer) and kept in the campaign's
    /// memory folder, past\(id)\: about.json, exchanges.jsonl, and conversation.txt for reading by hand. The "past" question reads them, only those whose handover is in the
    /// loaded save's history (Talk.Past), so a conversation that did not happen in this game is never shown.
    /// </summary>
    internal static class PastTalk
    {
        public const string FolderName = "past";

        /// <summary>Where Claude Code keeps its transcripts (the self-test points it at a scratch folder); null: its own (CLAUDE_CONFIG_DIR or the user's .claude), projects\.</summary>
        public static string ProjectsRoot;

        public sealed class Exchange
        {
            public int N;
            public string Date = "", Kind = "", Text = "";
            public double Hours = double.NaN;
        }

        public sealed class About
        {
            public string Id = "", Session = "", Date = "", Why = "", From = "", To = "", Written = "";
            public string Load;
            public double Hours = double.NaN;
            public int Exchanges;
            public long Chars;
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        // ---------- the transcript ----------

        /// <summary>Claude Code's transcript of a session, (projects)\(folder named after the call's working folder)\(session).jsonl; the newest when there are more; null when none.</summary>
        public static string SessionFile(string session)
        {
            if (string.IsNullOrEmpty(session)) return null;
            string root = ProjectsRoot;
            if (root == null)
            {
                string config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                root = Path.Combine(!string.IsNullOrWhiteSpace(config) ? config.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "projects");
            }
            if (!Directory.Exists(root)) return null;
            string best = null;
            DateTime bestAt = DateTime.MinValue;
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string f = Path.Combine(dir, session + ".jsonl");
                if (!File.Exists(f)) continue;
                DateTime at = File.GetLastWriteTimeUtc(f);
                if (best == null || at > bestAt) { best = f; bestAt = at; }
            }
            return best;
        }

        private static readonly Regex DateRx = new Regex(@"(Spring|Summer|Autumn|Winter) (\d+), (\d+), (\d\d):(\d\d)", RegexOptions.Compiled);
        private static readonly Regex PokeRx = new Regex(@"POKE #(\d+), ((Spring|Summer|Autumn|Winter) \d+, \d+, \d\d:\d\d)", RegexOptions.Compiled);
        private static readonly Regex ReviewRx = new Regex(@"STRATEGY REVIEW, ((Spring|Summer|Autumn|Winter) \d+, \d+, \d\d:\d\d)", RegexOptions.Compiled);
        private static readonly Regex RulerRx = new Regex(@"YOUR RULER SAYS \(message #(\d+), ((Spring|Summer|Autumn|Winter) \d+, \d+, \d\d:\d\d)\)", RegexOptions.Compiled);

        /// <summary>An in-game date's hour count, as the game keeps it (84 days a year, 21 a season).</summary>
        public static double HoursOf(string date)
        {
            Match m = DateRx.Match(date ?? "");
            if (!m.Success) return double.NaN;
            int season = Array.IndexOf(new[] { "Spring", "Summer", "Autumn", "Winter" }, m.Groups[1].Value);
            int day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            return (year * 84.0 + season * 21 + day - 1) * 24 + hour + minute / 60.0;
        }

        /// <summary>What a message sent to the conversation was, and its in-game date (empty when it says none).</summary>
        internal static string KindOf(string text, out string date)
        {
            date = "";
            string t = text ?? "";
            if (t.StartsWith("WRITE YOUR HANDOVER", StringComparison.Ordinal)) return "the request for your handover";
            if (t.StartsWith("That is not a handover", StringComparison.Ordinal)) return "the request for your handover, again";
            if (t.StartsWith("Your answer could not be read", StringComparison.Ordinal) || t.StartsWith("Your review's answer could not be read", StringComparison.Ordinal)) return "asked to answer again";
            string kind = null;
            Match p = PokeRx.Match(t), rv = ReviewRx.Match(t), ru = RulerRx.Match(t);
            Match first = new[] { p, rv, ru }.Where(m => m.Success).OrderBy(m => m.Index).FirstOrDefault();
            if (first == p && p.Success) { kind = "wake (poke #" + p.Groups[1].Value + ")"; date = p.Groups[2].Value; }
            else if (first == rv && rv.Success) { kind = "strategy review"; date = rv.Groups[1].Value; }
            else if (first == ru && ru.Success) { kind = "your ruler's message #" + ru.Groups[1].Value; date = ru.Groups[2].Value; }
            if (t.StartsWith("A NEW CONVERSATION", StringComparison.Ordinal)) kind = "the opening of the conversation" + (kind != null ? ", then " + kind : "");
            else if (t.StartsWith("THE GAME WAS LOADED", StringComparison.Ordinal)) kind = "the game loaded again" + (kind != null ? ", then " + kind : "");
            if (date.Length == 0)
            {
                Match d = DateRx.Match(t.Length > 400 ? t.Substring(0, 400) : t);
                if (d.Success) date = d.Value;
            }
            return kind ?? "a message";
        }

        /// <summary>The exchanges of a transcript, oldest first: each message sent to the conversation with all that followed it up to the next.</summary>
        public static List<Exchange> Read(string jsonl)
        {
            var list = new List<Exchange>();
            StringBuilder sb = null;
            Exchange cur = null;
            string lastDate = "";
            double lastHours = double.NaN;
            void Close()
            {
                if (cur == null) return;
                cur.Text = sb.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
                list.Add(cur);
            }
            foreach (string raw in File.ReadLines(jsonl))
            {
                if (raw.Length == 0 || raw[0] != '{') continue;
                JsonObject d;
                try { d = JsonNode.Parse(raw) as JsonObject; }
                catch (JsonException) { continue; }
                if (d == null) continue;
                string type = Str(d["type"]);
                if (type != "user" && type != "assistant") continue;
                if (Bool(d["isMeta"]) || Bool(d["isSidechain"]) || Bool(d["isCompactSummary"])) continue;
                JsonNode content = d["message"]?["content"];
                if (type == "user")
                {
                    string said = TextOf(content);
                    if (said.Length > 0)
                    {
                        Close();
                        string kind = KindOf(said, out string date);
                        if (date.Length > 0) { lastDate = date; lastHours = HoursOf(date); }
                        cur = new Exchange { N = list.Count + 1, Date = lastDate, Hours = lastHours, Kind = kind };
                        sb = new StringBuilder();
                        sb.Append("=== EXCHANGE ").Append(cur.N).Append(" · ").Append(cur.Date.Length > 0 ? cur.Date : "no date").Append(" · ").Append(kind).Append(" ===\n\n");
                        sb.Append("--- YOU WERE SENT ---\n").Append(said.TrimEnd()).Append("\n\n");
                        continue;
                    }
                    if (cur == null || content is not JsonArray results) continue;
                    foreach (JsonNode b in results)
                    {
                        if (b is not JsonObject r || Str(r["type"]) != "tool_result") continue;
                        sb.Append(Bool(r["is_error"]) ? "--- ITS ANSWER (an error) ---\n" : "--- ITS ANSWER ---\n").Append(TextOf(r["content"]).TrimEnd()).Append("\n\n");
                    }
                    continue;
                }
                if (cur == null || content is not JsonArray blocks) continue;
                foreach (JsonNode b in blocks)
                {
                    if (b is not JsonObject o) continue;
                    switch (Str(o["type"]))
                    {
                        case "thinking":
                            string th = Str(o["thinking"]) ?? "";
                            if (th.Trim().Length > 0) sb.Append("--- YOUR THINKING (as summarized) ---\n").Append(th.TrimEnd()).Append("\n\n");
                            break;
                        case "text":
                            string tx = Str(o["text"]) ?? "";
                            if (tx.Trim().Length > 0) sb.Append("--- YOUR ANSWER ---\n").Append(tx.TrimEnd()).Append("\n\n");
                            break;
                        case "tool_use":
                            string name = Str(o["name"]) ?? "?";
                            int cut = name.LastIndexOf("__", StringComparison.Ordinal);
                            if (name.StartsWith("mcp__", StringComparison.Ordinal) && cut > 0) name = name.Substring(cut + 2);
                            sb.Append("--- YOU ASKED ").Append(name).Append(": ").Append(o["input"]?.ToJsonString() ?? "{}").Append(" ---\n\n");
                            break;
                    }
                }
            }
            Close();
            return list;
        }

        /// <summary>A message's words: a plain string, or its text blocks (a tool's answer, a list of text blocks) joined.</summary>
        private static string TextOf(JsonNode content)
        {
            if (content is JsonValue v && v.TryGetValue(out string s)) return s;
            if (content is not JsonArray a) return "";
            var parts = new List<string>();
            foreach (JsonNode b in a)
                if (b is JsonObject o && Str(o["type"]) == "text" && Str(o["text"]) is string t && !t.StartsWith("<system-reminder>", StringComparison.Ordinal)) parts.Add(t);
            return string.Join("\n", parts);
        }

        // ---------- the copies ----------

        /// <summary>
        /// The conversation that just ended (or one found later from its handover's mark, Mend): its transcript turned into exchanges and kept in past\(id)\, with the hour,
        /// date and game session of its handover; null (and why) when its transcript is not found.
        /// </summary>
        public static About Keep(string memDir, string session, double hours, string date, string load, string why, out string problem)
        {
            problem = null;
            string file = SessionFile(session);
            if (file == null) { problem = "the transcript of conversation " + session + " was not found"; return null; }
            List<Exchange> all;
            try { all = Read(file); }
            catch (IOException e) { problem = "its transcript could not be read: " + e.Message; return null; }
            if (all.Count == 0) { problem = "its transcript holds no messages"; return null; }
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), id = stamp;
            string root = Path.Combine(memDir, FolderName);
            for (int k = 2; Directory.Exists(Path.Combine(root, id)); k++) id = stamp + "-" + k;
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            var a = new About
            {
                Id = id, Session = session, Hours = hours, Date = date ?? "", Load = string.IsNullOrEmpty(load) ? null : load, Why = why ?? "", Exchanges = all.Count,
                From = all.FirstOrDefault(e => e.Date.Length > 0)?.Date ?? "", To = all.LastOrDefault(e => e.Date.Length > 0)?.Date ?? "",
                Chars = all.Sum(e => (long)e.Text.Length), Written = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(dir, "exchanges.jsonl"), string.Join("\n", all.Select(e => JsonSerializer.Serialize(e, Options))) + "\n", utf8);
            File.WriteAllText(Path.Combine(dir, "conversation.txt"), "A conversation of " + all.Count + " exchanges, " + a.From + " to " + a.To + ", ended by the handover of " + a.Date + " (" + a.Why + ").\n"
                                                                     + "Claude Code's transcript: " + file + "\n\n" + string.Join("\n", all.Select(e => e.Text)), utf8);
            File.WriteAllText(Path.Combine(dir, "about.json"), JsonSerializer.Serialize(a, new JsonSerializerOptions(Options) { WriteIndented = true }), utf8);
            return a;
        }

        /// <summary>Every kept conversation of the campaign, oldest handover first.</summary>
        public static List<About> All(string memDir)
        {
            var list = new List<About>();
            string root = Path.Combine(memDir ?? "", FolderName);
            if (!Directory.Exists(root)) return list;
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string text = Room.ReadShared(Path.Combine(dir, "about.json"));
                if (text == null) continue;
                try { if (JsonSerializer.Deserialize<About>(text, Options) is About a) { a.Id = Path.GetFileName(dir); list.Add(a); } }
                catch (JsonException) { }
            }
            return list.OrderBy(a => double.IsNaN(a.Hours) ? double.MaxValue : a.Hours).ThenBy(a => a.Written, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The kept conversations whose handover is in the loaded save's history (the game sessions its world lived through, each to the hour it left it, as the handover's
        /// mark is chosen), or with no history, written at or before the save's hour; by id, oldest first.
        /// </summary>
        public static List<string> Visible(string memDir, List<KeyValuePair<string, double>> before, double loadedAt)
        {
            bool history = before != null && before.Count > 0;
            return All(memDir).Where(a => history ? Memory.InHistory(before, a.Load, a.Hours) : double.IsNaN(loadedAt) || double.IsNaN(a.Hours) || a.Hours <= loadedAt + 0.5).Select(a => a.Id).ToList();
        }

        /// <summary>
        /// A copy for every handover mark in the checkpoints that has none (one written before 0.18.9, or a copy that failed at its handover): its conversation is the one
        /// named on the mark, or on the checkpoint just before it. How many were made; each one's trouble told.
        /// </summary>
        public static int Mend(string memDir, Action<string> say)
        {
            string file = Path.Combine(memDir ?? "", Talk.CheckpointFile);
            string text = Room.ReadShared(file);
            if (text == null) return 0;
            List<About> kept = All(memDir);
            string lastSession = null;
            int made = 0;
            foreach (string l in text.Split('\n'))
            {
                if (l.Trim().Length == 0) continue;
                JsonObject o;
                try { o = JsonNode.Parse(l) as JsonObject; }
                catch (JsonException) { continue; }
                if (o == null) continue;
                if (!Bool(o["cp_handover"])) { lastSession = Str(o["cp_session"]) ?? lastSession; continue; }
                string session = Str(o["cp_session"]) ?? lastSession;
                if (session == null || kept.Any(a => a.Session == session)) continue;
                double hours = o["cp_hours"] is JsonValue hv && hv.TryGetValue(out double h) ? h : double.NaN;
                About a = Keep(memDir, session, hours, Str(o["cp_date"]) ?? "", Str(o["cp_load"]), Str(o["cp_why"]) ?? "", out string problem);
                if (a != null) { kept.Add(a); made++; say?.Invoke("Earlier conversation kept word for word: " + a.Exchanges + " exchanges, " + a.From + " to " + a.To + " (past\\" + a.Id + ")."); }
                else say?.Invoke("Earlier conversation of the handover of " + Str(o["cp_date"]) + " NOT kept: " + problem + ".");
            }
            return made;
        }

        /// <summary>
        /// The conversation that just ended, kept at its handover: the copy's id added to the conversations of this game's history (the next conversation then offers the
        /// past question); its trouble told when the transcript is not found.
        /// </summary>
        public static void KeepEnded(Talk talk, string memDir, string session, double hours, string date, string load, string why, Action<string> say)
        {
            About a = Keep(memDir, session, hours, date, load, why, out string problem);
            if (a != null)
            {
                talk.Past.Add(a.Id);
                say?.Invoke("The conversation is kept word for word: " + a.Exchanges + " exchanges, " + a.From + " to " + a.To + " (past\\" + a.Id + ").");
            }
            else say?.Invoke("The conversation could NOT be kept word for word: " + problem + ".");
        }

        /// <summary>The opening's line on the earlier conversations of this game's history, or null when there are none.</summary>
        public static string OpeningLine(string memDir, IList<string> ids)
        {
            if (ids == null || ids.Count == 0) return null;
            About last = All(memDir).LastOrDefault(a => ids.Contains(a.Id));
            return "YOUR EARLIER CONVERSATIONS, WORD FOR WORD: " + (ids.Count == 1 ? "the one this handover ended" : ids.Count + " of them, the latest the one this handover ended")
                   + (last != null ? " (" + last.Exchanges + " exchanges, " + last.From + " to " + last.To + ")" : "")
                   + ". Your handover is your summary of them, and they stay readable whole: ask past, which lists the exchanges by in-game date, reads one whole by its number, "
                   + "lists a day's, or finds the lines that hold some words. Read them when you need exactly what you were told, what you answered, or why you decided something.";
        }

        public static List<Exchange> Exchanges(string memDir, string id)
        {
            var list = new List<Exchange>();
            string text = Room.ReadShared(Path.Combine(memDir, FolderName, id, "exchanges.jsonl"));
            if (text == null) return list;
            foreach (string l in text.Split('\n'))
            {
                if (l.Trim().Length == 0) continue;
                try { if (JsonSerializer.Deserialize<Exchange>(l, Options) is Exchange e) list.Add(e); }
                catch (JsonException) { }
            }
            return list;
        }

        // ---------- the question ----------

        public const string ToolName = "past";
        /// <summary>The most of an exchange one answer gives; the rest comes in parts.</summary>
        public const int PartChars = 30000;

        public static JsonObject Definition() => new JsonObject
        {
            ["name"] = ToolName,
            ["description"] = "Your earlier conversations in this campaign, word for word, each ended by your handover: every message you were sent, your thinking as summarized, "
                              + "the questions you asked with their answers, and your answers. With nothing given: the list of your earlier conversations and the exchanges of the latest, "
                              + "by in-game date. Give an exchange's number to read it whole (a long one comes in parts), a day to list that day's exchanges, or words to find the exchanges that hold them.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["conversation"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: which earlier conversation, by its number in the list (1 is the oldest); the latest when left out." },
                    ["exchange"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: the exchange to read whole, by its number." },
                    ["part"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: the part of a long exchange (1 is the first)." },
                    ["day"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: an in-game day, such as \"Autumn 3, 1087\", to list its exchanges." },
                    ["with"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: words or an id to find; each line that holds them, with its exchange." }
                }
            }
        };

        /// <summary>The answer to the past question, from the conversations of this save's history (ids, oldest first).</summary>
        public static string Answer(string memDir, IList<string> ids, JsonObject args)
        {
            Dictionary<string, About> about = All(memDir).ToDictionary(a => a.Id, StringComparer.Ordinal);
            List<About> shown = (ids ?? new List<string>()).Where(about.ContainsKey).Select(i => about[i]).ToList();
            if (shown.Count == 0) return "You have no earlier conversation in this game yet: this is your first since the campaign began (or since the save you are playing from).";
            int which = args["conversation"] is JsonValue cv && cv.TryGetValue(out double c) ? (int)c : shown.Count;
            if (which < 1 || which > shown.Count) return "There is no earlier conversation " + which + ": you have " + shown.Count + " (1 is the oldest).";
            About a = shown[which - 1];
            List<Exchange> all = Exchanges(memDir, a.Id);
            string title = "Earlier conversation " + which + " of " + shown.Count + ": " + a.From + " to " + a.To + ", " + all.Count + " exchanges, ended by your handover of " + a.Date + (a.Why.Length > 0 ? " (" + a.Why + ")" : "");
            if (args["exchange"] is JsonValue ev && ev.TryGetValue(out double en))
            {
                Exchange e = all.FirstOrDefault(x => x.N == (int)en);
                if (e == null) return title + ".\nThere is no exchange " + (int)en + " in it: they run from 1 to " + all.Count + ".";
                int parts = Math.Max(1, (e.Text.Length + PartChars - 1) / PartChars);
                int part = args["part"] is JsonValue pv && pv.TryGetValue(out double pn) ? (int)pn : 1;
                if (part < 1 || part > parts) return title + ".\nExchange " + e.N + " has " + parts + " part" + (parts == 1 ? "" : "s") + ".";
                int from = Cut(e.Text, (part - 1) * PartChars), to = part == parts ? e.Text.Length : Cut(e.Text, part * PartChars);
                return title + ".\n" + (parts > 1 ? "Exchange " + e.N + ", part " + part + " of " + parts + (part < parts ? " (ask for part " + (part + 1) + " to read on)" : "") + ":\n\n" : "\n")
                       + e.Text.Substring(from, to - from);
            }
            if (args["with"] is JsonValue wv && wv.TryGetValue(out string with) && with.Trim().Length > 0)
            {
                var hits = new List<string>();
                int found = 0;
                foreach (Exchange e in all)
                    foreach (string line in e.Text.Split('\n'))
                    {
                        if (line.IndexOf(with.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                        found++;
                        if (hits.Count < 40) hits.Add("#" + e.N + " · " + e.Date + " · " + (line.Length > 300 ? line.Substring(0, 300) + "..." : line.Trim()));
                    }
                return title + ".\n" + (found == 0 ? "No line holds \"" + with.Trim() + "\"." : found + " line" + (found == 1 ? "" : "s") + " hold \"" + with.Trim() + "\"" + (found > hits.Count ? " (the first " + hits.Count + " below)" : "") + "; read an exchange whole by its number:\n" + string.Join("\n", hits));
            }
            IEnumerable<Exchange> pick = all;
            string head = "Its exchanges (read one whole by its number):";
            if (args["day"] is JsonValue dv && dv.TryGetValue(out string day) && day.Trim().Length > 0)
            {
                string d = day.Trim().TrimEnd(',');
                pick = all.Where(e => e.Date.StartsWith(d + ",", StringComparison.OrdinalIgnoreCase) || e.Date.Equals(d, StringComparison.OrdinalIgnoreCase)).ToList();
                head = pick.Any() ? "Its exchanges on " + d + ":" : "No exchange of it is dated " + d + ".";
            }
            var sb = new StringBuilder();
            if (args["day"] == null && shown.Count > 1)
            {
                sb.AppendLine("Your earlier conversations (1 is the oldest):");
                for (int i = 0; i < shown.Count; i++) sb.AppendLine((i + 1) + ". " + shown[i].From + " to " + shown[i].To + ", " + shown[i].Exchanges + " exchanges, ended by your handover of " + shown[i].Date);
                sb.AppendLine();
            }
            sb.AppendLine(title + ".");
            sb.AppendLine(head);
            foreach (Exchange e in pick.Take(400)) sb.AppendLine("#" + e.N + " · " + (e.Date.Length > 0 ? e.Date : "no date") + " · " + e.Kind + " · " + e.Text.Length.ToString("N0", CultureInfo.InvariantCulture) + " characters");
            return sb.ToString().TrimEnd();
        }

        /// <summary>A place to cut a long text near this many characters: the line's end before it, else the place itself.</summary>
        private static int Cut(string text, int at)
        {
            if (at >= text.Length) return text.Length;
            int nl = text.LastIndexOf('\n', at);
            return nl > at - 4000 && nl > 0 ? nl + 1 : at;
        }

        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
        private static bool Bool(JsonNode n) => n is JsonValue v && v.TryGetValue(out bool b) && b;
    }
}
