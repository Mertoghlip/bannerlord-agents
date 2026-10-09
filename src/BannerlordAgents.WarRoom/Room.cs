using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The live loop. The bridge writes each poke into the channel (poke.txt, and a line in pokes.log). When a poke is
    /// newer than the last one answered and no call is running, the war room answers the newest poke with one fresh
    /// call: the agent's instructions and the order library as the system prompt (the same every time, so it stays
    /// cached), the journal and the poke as the message. Pokes that come while a call runs are not lost: the next poke
    /// says what changed since the last answer. The answer's orders go into orders\poke-N.json, its plan and lessons
    /// into the journal, and every call is logged with its time and tokens. With no new pokes it does nothing at all.
    /// </summary>
    internal sealed class Room
    {
        private readonly Config _c;
        private readonly string _claude;
        private readonly string _logDir;
        private int _lastAnswered = -1;
        private string _systemFile;
        private string _systemHash;

        private static readonly Regex PokeHead = new Regex(@"^POKE #(\d+), (.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

        public Room(Config c)
        {
            _c = c;
            _claude = Claude.Find(c.ClaudePath);
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents", "logs", "warroom");
            Directory.CreateDirectory(_logDir);
        }

        public string ClaudePath => _claude;

        // ---------- the loop ----------

        public int Run(CancellationToken stop)
        {
            Say("War room " + typeof(Room).Assembly.GetName().Version + ": answering pokes in " + _c.ChannelDir);
            Say("Model " + _c.Model + ", effort " + _c.Effort + " (urgent " + _c.UrgentEffort + "), Claude Code at " + _claude);
            Say("It waits for the game; with no pokes it does nothing. Ctrl+C stops it.");
            _lastAnswered = LastAnsweredOnDisk();
            int waitingSaid = -2;
            while (!stop.IsCancellationRequested)
            {
                int newest = NewestPoke(out string text, out string when);
                if (newest > _lastAnswered && text != null)
                {
                    Answer(newest, when, text);
                    _lastAnswered = newest;
                    continue;
                }
                if (waitingSaid != _lastAnswered) { Say("Waiting for the next poke (last answered #" + _lastAnswered + ")."); waitingSaid = _lastAnswered; }
                stop.WaitHandle.WaitOne(250);
            }
            Say("Stopped.");
            return 0;
        }

        /// <summary>Answers one poke now (for "once" and the replay).</summary>
        public CallResult Answer(int number, string when, string pokeText, bool write = true, string effort = null, string tag = null, int? thinkingCap = null)
        {
            DateTime noticed = DateTime.Now;
            string why = Section(pokeText, "WHY YOU ARE ASKED NOW");
            bool urgent = _c.Urgent.Any(u => why.IndexOf(u, StringComparison.OrdinalIgnoreCase) >= 0);
            effort ??= urgent ? _c.UrgentEffort : _c.Effort;
            string system = SystemFile();
            string journal = File.Exists(_c.Journal) ? File.ReadAllText(_c.Journal) : "(empty: this is your first answer)";
            var prompt = new StringBuilder();
            prompt.AppendLine("YOUR JOURNAL (your standing plan and lessons, kept between pokes)");
            prompt.AppendLine(journal.Trim());
            prompt.AppendLine();
            prompt.AppendLine(_lastAnswered >= 0 ? "Your last answer was to poke #" + _lastAnswered + "." : "You have not answered any poke yet in this run.");
            prompt.AppendLine();
            prompt.Append(pokeText);
            string folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-poke-" + number + (tag != null ? "-" + tag : ""));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), prompt.ToString());

            Say("Poke #" + number + " (" + when + ")" + (urgent ? ", urgent" : "") + ": asking " + _c.Model + " at effort " + effort + ", thinking up to " + (thinkingCap ?? (urgent ? _c.UrgentThinkingCap : _c.ThinkingCap)) + " tokens...");
            int cap = thinkingCap ?? (urgent ? _c.UrgentThinkingCap : _c.ThinkingCap);
            CallResult r = Claude.Run(_claude, system, prompt.ToString(), _c.Model, effort, _c.ChannelDir, _c.Lookups, _c.TimeoutSeconds, cap);
            bool stallRetry = false;
            if (!r.Ok && (r.Stalled || r.Runaway))
            {
                // a call that goes quiet is asked once more, fresh; the time lost is counted
                File.WriteAllText(Path.Combine(folder, "raw-stalled.jsonl"), r.Raw ?? "");
                Say("Poke #" + number + ": the call stalled after " + r.Seconds.ToString("0", CultureInfo.InvariantCulture) + " s; asking again.");
                double lost = r.Seconds;
                stallRetry = true;
                r = Claude.Run(_claude, system, prompt.ToString(), _c.Model, effort, _c.ChannelDir, _c.Lookups, _c.TimeoutSeconds, cap);
                r.Seconds += lost;
            }
            File.WriteAllText(Path.Combine(folder, "raw.jsonl"), r.Raw ?? "");
            JsonObject answer = null;
            string problem = r.Ok ? Extract(r.Text, number, out answer) : r.Error;
            bool retried = false;
            if (answer == null && r.Ok)
            {
                // one retry: show it its own answer and the problem, at the lowest effort
                retried = true;
                string fix = "Your answer to poke #" + number + " could not be read: " + problem + ".\nHere it is:\n" + r.Text
                             + "\n\nReply again with ONLY the JSON object, nothing before or after it.";
                CallResult r2 = Claude.Run(_claude, system, fix, _c.Model, "low", _c.ChannelDir, false, _c.TimeoutSeconds, 1000);
                File.WriteAllText(Path.Combine(folder, "raw-retry.jsonl"), r2.Raw ?? "");
                problem = r2.Ok ? Extract(r2.Text, number, out answer) : r2.Error;
                r.Seconds += r2.Seconds;
                r.OutTokens += r2.OutTokens;
                r.Thinking += r2.Thinking;
            }
            int orders = answer?["orders"] is JsonArray arr ? arr.Count : 0;
            if (answer != null && write)
            {
                WriteOrders(number, answer);
                UpdateJournal(answer);
            }
            if (answer != null) File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            double seconds = (DateTime.Now - noticed).TotalSeconds;
            Log(new
            {
                at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), poke = number, when, urgent, model = _c.Model, effort, thinking_cap = cap, ok = answer != null, problem, retried, stall_retry = stallRetry,
                seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2), api_ms = r.ApiMs, ttft_ms = r.TtftMs, turns = r.Turns,
                tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cost_usd_list = r.Cost,
                answer_began_ms = Math.Round(r.FirstTextMs),
                orders, denied = r.Denied, folder, written = write && answer != null
            });
            if (answer != null)
                Say("Poke #" + number + ": " + orders + " order" + (orders == 1 ? "" : "s") + " in " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s ("
                    + r.OutTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens written, " + r.Thinking.ToString("N0", CultureInfo.InvariantCulture) + " of them thinking; "
                    + r.Turns + " step" + (r.Turns == 1 ? "" : "s") + ")" + (retried ? " after one retry" : "") + ".");
            else Say("Poke #" + number + ": NO ANSWER (" + problem + ") after " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");
            return r;
        }

        // ---------- reading the channel ----------

        /// <summary>The newest poke: its number from pokes.log, its text from poke.txt (when poke.txt already holds it).</summary>
        private int NewestPoke(out string text, out string when)
        {
            text = when = null;
            int fromLog = LastLogNumber(Path.Combine(_c.ChannelDir, "pokes.log"));
            if (fromLog <= _lastAnswered) return fromLog;
            string poke = ReadShared(Path.Combine(_c.ChannelDir, "poke.txt"));
            if (poke == null) return -1;
            Match m = PokeHead.Match(poke);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int n) || n < fromLog) return -1;   // poke.txt is written before its line; wait for it
            text = poke;
            when = m.Groups[2].Value.Trim();
            return n;
        }

        private static int LastLogNumber(string path)
        {
            string all = ReadShared(path);
            if (string.IsNullOrEmpty(all)) return -1;
            int best = -1;
            foreach (string line in all.Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith("#")) continue;
                int sp = l.IndexOf(' ');
                if (sp > 1 && int.TryParse(l.Substring(1, sp - 1), out int n) && n > best) best = n;
            }
            return best;
        }

        /// <summary>The newest poke already answered in the channel (orders\ and orders\done\), so a restart does not answer it twice.</summary>
        private int LastAnsweredOnDisk()
        {
            int best = -1;
            foreach (string dir in new[] { Path.Combine(_c.ChannelDir, "orders"), Path.Combine(_c.ChannelDir, "orders", "done") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "poke-*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(f).Substring(5);
                    int dash = n.IndexOf('-');
                    if (dash > 0) n = n.Substring(0, dash);
                    if (int.TryParse(n, out int v) && v > best) best = v;
                }
            }
            // a poke left from before this run is answered only if it is fresh (the game is running and waiting); an old one
            // answers a game that has moved on, so the war room starts with the next one
            int logged = LastLogNumber(Path.Combine(_c.ChannelDir, "pokes.log"));
            string poke = Path.Combine(_c.ChannelDir, "poke.txt");
            bool fresh = File.Exists(poke) && (DateTime.Now - File.GetLastWriteTime(poke)).TotalSeconds < 120;
            return fresh ? best : Math.Max(best, logged);
        }

        private static string ReadShared(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            catch (IOException) { return null; }
        }

        private static string Section(string text, string head)
        {
            int i = text.IndexOf(head, StringComparison.Ordinal);
            if (i < 0) return "";
            int end = text.IndexOf("\n\n", i, StringComparison.Ordinal);
            return end < 0 ? text.Substring(i) : text.Substring(i, end - i);
        }

        // ---------- the prompt ----------

        /// <summary>The system prompt: the agent's instructions and the order library, rewritten only when either changes (so it stays cached).</summary>
        private string SystemFile()
        {
            string template = File.Exists(_c.SystemTemplate) ? File.ReadAllText(_c.SystemTemplate) : "You command a realm in Mount & Blade II: Bannerlord. Answer each poke with one JSON object of orders.";
            if (!_c.Lookups)
            {
                // never promise a tool the call does not have: the model would type tool calls as text instead
                int a = template.IndexOf("## Look-ups", StringComparison.Ordinal);
                if (a >= 0)
                {
                    int b = template.IndexOf("\n## ", a + 3, StringComparison.Ordinal);
                    template = template.Substring(0, a) + (b >= 0 ? template.Substring(b + 1) : "");
                }
                template = template.TrimEnd() + "\n\nYou have no tools in this call: answer from the poke alone.\n";
            }
            string tools = ReadShared(Path.Combine(_c.ChannelDir, "tools.txt")) ?? "(the order library appears once the game has loaded a campaign)";
            string text = template.TrimEnd() + "\n\n" + tools.Trim() + "\n";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 12);
            if (hash == _systemHash && _systemFile != null && File.Exists(_systemFile)) return _systemFile;
            _systemHash = hash;
            _systemFile = Path.Combine(_logDir, "system-" + hash + ".md");
            File.WriteAllText(_systemFile, text);
            Say("System prompt " + hash + ": " + text.Length.ToString("N0", CultureInfo.InvariantCulture) + " characters.");
            return _systemFile;
        }

        // ---------- the answer ----------

        /// <summary>The JSON object in the answer, checked: an object with an orders list whose orders name a lord and an order.</summary>
        private static string Extract(string text, int number, out JsonObject answer)
        {
            answer = null;
            if (string.IsNullOrWhiteSpace(text)) return "the answer was empty";
            // every complete top-level object in the text; the last one that reads (an answer may correct itself) wins
            JsonObject o = null;
            string lastError = "there is no JSON object in it";
            foreach (string candidate in Objects(text).Reverse())
            {
                try
                {
                    if (JsonNode.Parse(candidate) is JsonObject found && (found["orders"] != null || found["poke"] != null)) { o = found; break; }
                }
                catch (JsonException e) { lastError = "it is not valid JSON (" + e.Message + ")"; }
            }
            if (o == null) return lastError;
            if (o["orders"] == null) o["orders"] = new JsonArray();
            if (o["orders"] is not JsonArray list) return "\"orders\" is not a list";
            int i = 0;
            foreach (JsonNode n in list)
            {
                i++;
                if (n is not JsonObject ord) return "order " + i + " is not an object";
                if (ord["lord"] == null || ord["do"] == null) return "order " + i + " needs \"lord\" and \"do\"";
            }
            o["poke"] = number;
            answer = o;
            return null;
        }

        /// <summary>The balanced { ... } spans at the top level of a text, quotes and escapes respected.</summary>
        private static IEnumerable<string> Objects(string text)
        {
            int depth = 0, start = -1;
            bool inString = false, escape = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"' && depth > 0) { inString = true; continue; }
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}' && depth > 0)
                {
                    depth--;
                    if (depth == 0 && start >= 0) { yield return text.Substring(start, i - start + 1); start = -1; }
                }
            }
        }

        private void WriteOrders(int number, JsonObject answer)
        {
            string dir = Path.Combine(_c.ChannelDir, "orders");
            Directory.CreateDirectory(dir);
            var file = new JsonObject { ["poke"] = number, ["orders"] = answer["orders"]?.DeepClone() ?? new JsonArray() };
            if (answer["note"] != null) file["note"] = answer["note"].DeepClone();
            else if (answer["plan"] != null) file["note"] = answer["plan"].DeepClone();
            string final = Path.Combine(dir, "poke-" + number + ".json");
            string temp = Path.Combine(dir, "poke-" + number + ".json.part");
            File.WriteAllText(temp, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, final, true);
        }

        /// <summary>
        /// The journal, section by section ("## heading"): the standing plan is replaced when an answer brings one, new
        /// lessons are added (the newest max_lessons kept), and every other section is kept as it is.
        /// </summary>
        private void UpdateJournal(JsonObject answer)
        {
            string plan = answer["plan"] is JsonValue pv && pv.TryGetValue(out string ps) ? ps : null;
            var lessons = new List<string>();
            if (answer["lessons"] is JsonArray la) foreach (JsonNode n in la) { string s = n?.ToString(); if (!string.IsNullOrWhiteSpace(s)) lessons.Add(s.Trim()); }
            if (plan == null && lessons.Count == 0) return;
            var sections = new List<KeyValuePair<string, string>>();
            string title = "# Journal";
            if (File.Exists(_c.Journal))
            {
                string head = null;
                var body = new StringBuilder();
                foreach (string raw in File.ReadAllText(_c.Journal).Replace("\r", "").Split('\n'))
                {
                    if (raw.StartsWith("# ")) { title = raw.TrimEnd(); continue; }
                    if (raw.StartsWith("## "))
                    {
                        if (head != null) sections.Add(new KeyValuePair<string, string>(head, body.ToString().Trim()));
                        head = raw.Substring(3).Trim();
                        body.Clear();
                        continue;
                    }
                    body.AppendLine(raw);
                }
                if (head != null) sections.Add(new KeyValuePair<string, string>(head, body.ToString().Trim()));
            }
            int pi = sections.FindIndex(s => s.Key.StartsWith("Standing plan", StringComparison.OrdinalIgnoreCase));
            if (plan != null)
            {
                var kv = new KeyValuePair<string, string>("Standing plan", plan.Trim());
                if (pi >= 0) sections[pi] = kv; else sections.Insert(0, kv);
            }
            int li = sections.FindIndex(s => s.Key.StartsWith("Lessons", StringComparison.OrdinalIgnoreCase));
            var all = li >= 0 ? sections[li].Value.Split('\n').Select(x => x.Trim()).Where(x => x.StartsWith("- ")).Select(x => x.Substring(2)).ToList() : new List<string>();
            foreach (string l in lessons) if (!all.Contains(l)) all.Add(l);
            if (all.Count > _c.MaxLessons) all.RemoveRange(0, all.Count - _c.MaxLessons);
            var lk = new KeyValuePair<string, string>("Lessons", string.Join("\n", all.Select(l => "- " + l)));
            if (li >= 0) sections[li] = lk; else sections.Add(lk);
            var sb = new StringBuilder();
            sb.AppendLine(title);
            foreach (KeyValuePair<string, string> s in sections)
            {
                sb.AppendLine();
                sb.AppendLine("## " + s.Key);
                sb.AppendLine(s.Value);
            }
            File.WriteAllText(_c.Journal, sb.ToString());
        }

        // ---------- the logs ----------

        private void Log(object record)
        {
            string line = JsonSerializer.Serialize(record);
            File.AppendAllText(Path.Combine(_logDir, "calls.jsonl"), line + "\n");
        }

        public void Say(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            Console.WriteLine(line);
            try { File.AppendAllText(Path.Combine(_logDir, "warroom-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + Environment.NewLine); }
            catch (IOException) { }
        }
    }
}
