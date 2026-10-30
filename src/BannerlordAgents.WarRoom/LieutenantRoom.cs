using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The player's lieutenant's war room (0.10): instead of pokes it answers the player, who types to it in the campaign map's
    /// chat box (the bridge's Lieutenant writes each message as chat.json in the channel). Each answer is one fresh call: the
    /// lieutenant's instructions and the order library as the system prompt; as the message the conversation so far (its
    /// memory, memory\(campaign)\chat.jsonl), what became of its orders lately (receipts.log), the proposal waiting for the
    /// player's answer if any, the realm's report of the hour (report.txt, which says where the player's own party is), and
    /// the player's words. It can read its look-ups and ask the same questions as an agent (the way, battle odds, can catch,
    /// roads in). The answer goes to orders\chat-N.json: "say" (a few words for the chat box), "propose" (orders it would give),
    /// "carry_out" with "orders" (what the player has just agreed to), "drop" (the proposal is off), "detail" (a longer text,
    /// shown in a window that pauses the game). The war room adds "pending_chat", the waiting proposal's number from chat.json,
    /// so the bridge carries out only orders that answer a proposal (the player's yes), whatever the answer says.
    /// Since 0.11.1 the conversation is the save's, kept by the bridge (chat.json's "history", the whole in conversation.txt), not
    /// this room's memory, which is still written as a log; and each message has PARTY AI CONTROLS NOW (chat.json's "party_ai").
    /// </summary>
    internal sealed class LieutenantRoom
    {
        private readonly Config _c;
        private readonly string _claude, _logDir;
        private int _lastAnswered = -1;
        private string _systemFile, _systemHash, _routesWhy;
        private static readonly object LogGate = new object();

        /// <summary>The exchanges of the conversation put in each message.</summary>
        public const int HistoryLines = 14;

        public LieutenantRoom(Config c, string logDir = null)
        {
            _c = c;
            _claude = Claude.Find(c.ClaudePath);
            _logDir = logDir ?? (!string.IsNullOrWhiteSpace(c.LogDir) ? c.LogDir.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents", "logs", "warroom-lieutenant"));
            Directory.CreateDirectory(_logDir);
        }

        public int Run(CancellationToken stop)
        {
            Say("Lieutenant " + typeof(LieutenantRoom).Assembly.GetName().Version + ": answering the player's words in " + _c.ChannelDir);
            Say("Model " + _c.Model + ", effort " + _c.Effort + ", " + (_c.ThinkingCap > 0 ? "thinking up to " + _c.ThinkingCap + " tokens" : "thinking as long as it needs") + ", "
                + (_c.TimeoutSeconds > 0 ? "at most " + _c.TimeoutSeconds + " s a call" : "no time limit") + ", Claude Code at " + _claude);
            _lastAnswered = LastAnsweredOnDisk(_c.ChannelDir);
            int said = -2;
            while (!stop.IsCancellationRequested)
            {
                JsonObject chat = ReadChat(_c.ChannelDir);
                int n = chat != null ? (int)Num(chat["number"]) : -1;
                if (chat != null && n > _lastAnswered)
                {
                    try { Answer(chat); }
                    catch (Exception e) when (e is IOException || e is JsonException || e is InvalidOperationException) { Say("Message #" + n + ": could not be answered (" + e.Message + ")."); WriteAnswer(_c.ChannelDir, n, new JsonObject { ["say"] = "(I could not answer that: " + e.Message + ")" }, -1); }
                    _lastAnswered = n;
                    continue;
                }
                if (said != _lastAnswered) { Say("Waiting for the player's next words (last answered #" + _lastAnswered + ")."); said = _lastAnswered; }
                stop.WaitHandle.WaitOne(200);
            }
            Say("Stopped.");
            return 0;
        }

        /// <summary>One message of the player's, answered with one call.</summary>
        public void Answer(JsonObject chat)
        {
            DateTime began = DateTime.Now;
            int n = (int)Num(chat["number"]);
            string campaign = Str(chat["campaign"]), realm = Str(chat["realm"]), said = Str(chat["said"]) ?? "";
            int pending = chat["pending"] is JsonObject p ? (int)Num(p["chat"]) : -1;
            string memDir = Path.Combine(_c.MemoryDir, Safe(campaign ?? "unknown"));
            Directory.CreateDirectory(memDir);
            string historyFile = Path.Combine(memDir, "chat.jsonl");
            List<JsonObject> history = ReadHistory(historyFile, HistoryLines);
            string report = Room.ReadShared(Path.Combine(_c.ChannelDir, "report.txt")) ?? "(The realm's report is not written yet: the game writes it every in-game hour.)";
            List<string> receipts = Tail(Path.Combine(_c.ChannelDir, "receipts.log"), 12);
            RouteTool routes = Routes(campaign, realm, n);
            string system = SystemFile(routes != null);
            string prompt = Message(chat, history, report, receipts);
            string folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-chat-" + n);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), prompt);
            Say("Message #" + n + ": \"" + Short(said, 80) + "\": asking " + _c.Model + "...");
            CallResult r = Claude.Run(_claude, system, prompt, _c.Model, _c.Effort, _c.ChannelDir, _c.Lookups, _c.TimeoutSeconds, _c.ThinkingCap, tools: routes);
            if (!r.Ok && (r.Stalled || r.Runaway))
            {
                double lost = r.Seconds;
                r = Claude.Run(_claude, system, prompt, _c.Model, _c.Effort, _c.ChannelDir, _c.Lookups, _c.TimeoutSeconds, _c.ThinkingCap, tools: routes);
                r.Seconds += lost;
            }
            File.WriteAllText(Path.Combine(folder, "raw.jsonl"), r.Raw ?? "");
            JsonObject answer = null;
            string problem = r.Ok ? ExtractAnswer(r.Text, out answer) : r.Error;
            if (answer == null && r.Ok)
            {
                CallResult r2 = Claude.Run(_claude, system, "Your answer could not be read: " + problem + ".\nHere it is:\n" + r.Text + "\n\nReply again with ONLY the JSON object, nothing before or after it.",
                                           _c.Model, "low", _c.ChannelDir, false, _c.TimeoutSeconds, 1000, tools: routes);
                problem = r2.Ok ? ExtractAnswer(r2.Text, out answer) : r2.Error;
                r.Seconds += r2.Seconds;
            }
            answer ??= new JsonObject { ["say"] = "(I could not answer that: " + (problem ?? "no answer") + ")" };
            WriteAnswer(_c.ChannelDir, n, answer, pending);
            File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Room.KeepThinking(folder, r);
            Remember(historyFile, n, chat, answer);
            double seconds = (DateTime.Now - began).TotalSeconds;
            int proposed = (answer["propose"] as JsonArray)?.Count ?? 0, carried = answer["carry_out"] is JsonValue cv && cv.TryGetValue(out bool cb) && cb ? (answer["orders"] as JsonArray)?.Count ?? 0 : 0;
            Log(new
            {
                kind = "chat", at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), chat = n, campaign, realm, said, model = _c.Model, effort = _c.Effort,
                thinking_cap = _c.ThinkingCap, ok = problem == null, problem, seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2), tokens_in = r.InTokens,
                tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cost_usd_list = r.Cost, turns = r.Turns, proposed, carried, pending,
                ways_asked = routes?.Asked ?? 0, folder, run = Str(chat["run"]), arm = Str(chat["arm"]), system_hash = _systemHash, thinking_chars = r.ThinkingText.Length
            });
            Say("Message #" + n + ": answered in " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" + (proposed > 0 ? ", proposing " + proposed + " orders" : "")
                + (carried > 0 ? ", carrying out " + carried + " orders" : "") + (problem != null ? " (" + problem + ")" : "") + ".");
        }

        /// <summary>The message: the conversation so far, what became of its orders, the waiting proposal, the realm now, the player's words.</summary>
        internal static string Message(JsonObject chat, List<JsonObject> history, string report, List<string> receipts)
        {
            var sb = new StringBuilder();
            if (chat["history"] is JsonArray lines)
            {
                // since 0.11.1 the bridge keeps the conversation of the save being played and sends its latest lines; the whole of it is conversation.txt
                int total = (int)Num(chat["history_total"]);
                bool cut = total > lines.Count;
                sb.AppendLine("THE CONVERSATION IN THIS SAVE SO FAR (oldest first" + (cut ? "; its last " + lines.Count + " of " + total + " lines: the whole of it is in conversation.txt" : "") + ")");
                if (lines.Count == 0) sb.AppendLine("- (This is the first thing your ruler says to you in this save.)");
                foreach (JsonNode l in lines) sb.AppendLine(Str(l));
            }
            else
            {
                sb.AppendLine("THE CONVERSATION SO FAR (oldest first" + (history.Count >= HistoryLines ? "; the last " + HistoryLines + " exchanges" : "") + ")");
                if (history.Count == 0) sb.AppendLine("- (This is the first thing your ruler says to you in this campaign.)");
                foreach (JsonObject h in history)
                {
                    sb.AppendLine("- " + Str(h["date"]) + " · Ruler: " + Str(h["said"]));
                    string orders = Str(h["carried"]) != null ? " [you gave: " + Str(h["carried"]) + "]" : Str(h["proposed"]) != null ? " [you proposed: " + Str(h["proposed"]) + "]" : "";
                    sb.AppendLine("  You: " + Str(h["say"]) + orders);
                }
            }
            sb.AppendLine();
            sb.AppendLine("WHAT BECAME OF ORDERS LATELY (receipts.log, newest last)");
            if (receipts.Count == 0) sb.AppendLine("- Nothing yet.");
            foreach (string l in receipts) sb.AppendLine("- " + l);
            sb.AppendLine();
            // what the ruler did with a proposal by the window's buttons (0.10.1), since your last answer
            if (chat["notes"] is JsonArray notes && notes.Count > 0)
            {
                sb.AppendLine("WHAT YOUR RULER DID SINCE YOUR LAST ANSWER");
                foreach (JsonNode n in notes) sb.AppendLine("- " + Str(n));
                sb.AppendLine();
            }
            // letters from your ruler's allies since your last answer (0.13): tell your ruler what they say and ask
            if (chat["letters"] is JsonArray mail && mail.Count > 0)
            {
                sb.AppendLine("LETTERS FROM YOUR RULER'S ALLIES SINCE YOUR LAST ANSWER (your ruler saw them in your window)");
                foreach (JsonNode m in mail) sb.AppendLine("- " + Str(m));
                sb.AppendLine();
            }
            if (chat["pending"] is JsonObject p && p["orders"] is JsonArray po)
            {
                sb.AppendLine("YOUR PROPOSAL WAITING FOR YOUR RULER'S ANSWER (from message #" + (int)Num(p["chat"]) + "): " + Str(p["say"]));
                sb.AppendLine(po.ToJsonString());
                // its tools, diplomatic acts and letters wait for the same yes (0.13)
                foreach (string part in new[] { "tools", "diplomacy", "letters" })
                    if (p[part] is JsonArray pa && pa.Count > 0) sb.AppendLine(part + ": " + pa.ToJsonString());
                // 0.13.8 (Mert, Oct 29): answered in words only, all of it, some of it, or some with changes
                sb.AppendLine("If your ruler's words below agree to all or some of it, give what they agree to with \"carry_out\": true in \"orders\" (changed as they say),"
                              + " with its tools, diplomacy and letters in \"tools\", \"diplomacy\" and \"letters\" if they agree to those; what they want changed goes, changed, in \"propose\""
                              + " in the same answer, for their next words; what they refuse is left out.");
            }
            else sb.AppendLine("No proposal of yours is waiting. Orders your ruler's words below tell you to give go in \"orders\" with \"carry_out\": true; anything of your own idea goes in \"propose\" for their yes.");
            sb.AppendLine();
            sb.AppendLine("THE REALM NOW");
            sb.AppendLine(report.Trim());
            sb.AppendLine();
            if (Str(chat["party_ai"]) is string pac && pac.Trim().Length > 0)
            {
                sb.AppendLine("PARTY AI CONTROLS NOW");
                sb.AppendLine(pac.Trim());
                sb.AppendLine();
            }
            sb.AppendLine("YOUR RULER SAYS (message #" + (int)Num(chat["number"]) + ", " + Str(chat["date"]) + ")");
            if (chat["earlier"] is JsonArray earlier && earlier.Count > 0)
                foreach (JsonNode e in earlier) sb.AppendLine("(said just before) \"" + Str(e) + "\"");
            sb.AppendLine("\"" + Str(chat["said"]) + "\"");
            return sb.ToString();
        }

        /// <summary>The answer object: the last JSON object in the text with "say"; its order lists checked (each order names a lord and an order).</summary>
        internal static string ExtractAnswer(string text, out JsonObject answer)
        {
            answer = null;
            if (string.IsNullOrWhiteSpace(text)) return "the answer was empty";
            string lastError = "there is no JSON object with \"say\" in it";
            foreach (string candidate in Objects(text).Reverse())
            {
                try
                {
                    if (JsonNode.Parse(candidate) is JsonObject o && o["say"] != null) { answer = o; break; }
                }
                catch (JsonException e) { lastError = "it is not valid JSON (" + e.Message + ")"; }
            }
            if (answer == null) return lastError;
            foreach (string list in new[] { "propose", "orders" })
            {
                if (answer[list] == null) continue;
                if (answer[list] is not JsonArray a) { answer = null; return "\"" + list + "\" is not a list"; }
                int i = 0;
                foreach (JsonNode n in a)
                {
                    i++;
                    if (n is not JsonObject ord || ord["lord"] == null || ord["do"] == null) { answer = null; return "order " + i + " of \"" + list + "\" needs \"lord\" and \"do\""; }
                }
            }
            return null;
        }

        /// <summary>The answer for the bridge, orders\chat-N.json, swapped in whole; pending_chat is the war room's, never the model's.</summary>
        internal static void WriteAnswer(string channelDir, int n, JsonObject answer, int pending)
        {
            string dir = Path.Combine(channelDir, "orders");
            Directory.CreateDirectory(dir);
            var file = new JsonObject { ["chat"] = n, ["pending_chat"] = pending };
            foreach (string k in new[] { "say", "propose", "carry_out", "orders", "drop", "detail", "tools", "diplomacy", "letters" })
                if (answer[k] != null) file[k] = answer[k].DeepClone();
            string final = Path.Combine(dir, "chat-" + n + ".json"), temp = final + ".part";
            File.WriteAllText(temp, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, final, true);
        }

        /// <summary>The player's newest words (chat.json), or null.</summary>
        internal static JsonObject ReadChat(string channelDir)
        {
            try { return Room.ReadShared(Path.Combine(channelDir, "chat.json")) is string t ? JsonNode.Parse(t) as JsonObject : null; }
            catch (JsonException) { return null; }
        }

        /// <summary>The newest message already answered: the highest N among the answer files, in orders\ and orders\done\.</summary>
        internal static int LastAnsweredOnDisk(string channelDir)
        {
            int best = -1;
            foreach (string d in new[] { Path.Combine(channelDir, "orders"), Path.Combine(channelDir, "orders", "done") })
            {
                if (!Directory.Exists(d)) continue;
                foreach (string f in Directory.GetFiles(d, "chat-*.json"))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(f).Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) && k > best) best = k;
            }
            return best;
        }

        private static void Remember(string file, int n, JsonObject chat, JsonObject answer)
        {
            string Orders(JsonNode list) => list is JsonArray a && a.Count > 0 ? string.Join("; ", a.OfType<JsonObject>().Select(o => Str(o["lord"]) + " " + Str(o["do"]) + " " + (Str(o["at"]) ?? Str(o["target"]) ?? "").Trim())) : null;
            bool carried = answer["carry_out"] is JsonValue v && v.TryGetValue(out bool b) && b;
            var line = new JsonObject
            {
                ["chat"] = n, ["date"] = Str(chat["date"]), ["said"] = Str(chat["said"]), ["say"] = Str(answer["say"]),
                ["proposed"] = Orders(answer["propose"]), ["carried"] = carried ? Orders(answer["orders"]) : null
            };
            File.AppendAllText(file, line.ToJsonString() + "\n", new UTF8Encoding(false));
        }

        private static List<JsonObject> ReadHistory(string file, int last)
        {
            var list = new List<JsonObject>();
            string text = Room.ReadShared(file);
            if (text == null) return list;
            foreach (string l in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                try { if (JsonNode.Parse(l) is JsonObject o) list.Add(o); }
                catch (JsonException) { }
            }
            return list.Skip(Math.Max(0, list.Count - last)).ToList();
        }

        private static List<string> Tail(string file, int last)
        {
            string text = Room.ReadShared(file);
            if (text == null) return new List<string>();
            List<string> lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
            return lines.Skip(Math.Max(0, lines.Count - last)).ToList();
        }

        private RouteTool Routes(string campaign, string realm, int n)
        {
            if (!_c.RouteTool || campaign == null || realm == null) return null;
            var tool = new RouteTool(RouteTool.DefaultRoot(), _logDir, campaign, realm, n, "chat") { ChannelDir = _c.ChannelDir };
            if (!tool.Ready(out string why))
            {
                if (_routesWhy != why) { Say("The questions (the way, battle odds, can catch, roads in) are not offered: " + why + "."); _routesWhy = why; }
                return null;
            }
            _routesWhy = "ready";
            return tool;
        }

        /// <summary>The system prompt: the lieutenant's instructions and the order library; a section is left out when the call lacks its tool.</summary>
        private string SystemFile(bool routes)
        {
            string template = File.Exists(_c.SystemTemplate) ? File.ReadAllText(_c.SystemTemplate)
                : "You are the lieutenant of a ruler in Mount & Blade II: Bannerlord. Answer with one JSON object: {\"say\": \"...\"}.";
            if (!routes) template = Cut(Cut(template, "## Asking the way"), "## Checking a tool");
            if (!_c.Lookups) template = Cut(template, "## Look-ups").TrimEnd() + "\n\nYou have no tools in this call: answer from the message alone.\n";
            string tools = Room.ReadShared(Path.Combine(_c.ChannelDir, "tools.txt")) ?? "(the order library appears once the game has loaded a campaign)";
            string text = template.TrimEnd() + "\n\n" + tools.Trim() + "\n";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 12);
            if (hash == _systemHash && _systemFile != null && File.Exists(_systemFile)) return _systemFile;
            _systemHash = hash;
            _systemFile = Path.Combine(_logDir, "system-" + hash + ".md");
            File.WriteAllText(_systemFile, text);
            return _systemFile;
        }

        private static string Cut(string text, string heading)
        {
            int a = text.IndexOf(heading, StringComparison.Ordinal);
            if (a < 0) return text;
            int b = text.IndexOf("\n## ", a + 3, StringComparison.Ordinal);
            return text.Substring(0, a) + (b >= 0 ? text.Substring(b + 1) : "");
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
                    if (depth == 0 && start >= 0) { yield return Room.EscapeBreaks(text.Substring(start, i - start + 1)); start = -1; }
                }
            }
        }

        private static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
        private static string Short(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";
        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private static double Num(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return d;
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out int i)) return i;   // a value made from an int converts only to an int
            }
            return -1;
        }

        private void Log(object record)
        {
            string line = JsonSerializer.Serialize(record);
            lock (LogGate) File.AppendAllText(Path.Combine(_logDir, "calls.jsonl"), line + "\n");
        }

        public void Say(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            lock (LogGate)
            {
                Console.WriteLine(line);
                try { File.AppendAllText(Path.Combine(_logDir, "warroom-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + Environment.NewLine); }
                catch (IOException) { }
            }
        }
    }
}
