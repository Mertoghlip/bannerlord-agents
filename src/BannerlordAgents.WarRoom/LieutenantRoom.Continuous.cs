using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The continuous lieutenant (0.18.4, war room; docs/LIEUTENANT.md#one-conversation-since-0184): one conversation with the ruler instead of a fresh call per message,
    /// kept as the realm agents keep theirs (Talk, conversation.json in the campaign's memory folder). Its first message carries its handover (if it wrote one), the save's
    /// conversation so far, what became of its orders, the waiting proposal and the whole report of the hour; each later message only what is new: the ruler's words, what
    /// became of its orders since its last answer, what the ruler did by the buttons, the letters, the waiting proposal, and the report with each section exactly as it last
    /// saw it in a line, the big lists whole only every few hours (the whole report of the hour is report.txt, which it may read), and only the chronicle's new lines.
    /// The conversation ends only with its handover: at the ruler's battle (spare.txt, the clock stands still) once it holds compact_at_tokens, at the game's close once it
    /// holds compact_at_close_tokens, or before a message once it holds compact_hard_tokens. Loading the game where the conversation left off carries it on; loading an
    /// earlier save begins a new one, from the last handover written before that save's day. Every call of a conversation has the same instructions, tools, model,
    /// effort and thinking cap (continuous_*), so each reads the conversation from the one-hour cache.
    /// </summary>
    internal sealed partial class LieutenantRoom
    {
        private Talk _talk;
        private string _talkDir, _campaign, _realm;
        private DateTime _lastCallEnded = DateTime.MinValue, _gameLooked = DateTime.MinValue, _spareWord = DateTime.MinValue;
        private bool _gameSeen;
        /// <summary>The last message id a call of this message reached (0.18.7), checkpointed once it is answered.</summary>
        private string _lastUuid;

        /// <summary>The handovers written in a campaign, one line each (hours, date, file, void), so a reload of an earlier save takes the one of its day.</summary>
        internal const string HandoverList = "lieutenant-handovers.jsonl";

        /// <summary>
        /// The report's big lists, shown whole again only this many in-game hours after this conversation last saw them; in between, a line says whether they changed
        /// (and that report.txt holds them as of the hour). Everything else is shown whole, or in a line when exactly as last shown.
        /// </summary>
        internal static readonly Dictionary<string, double> BigLists = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["YOUR LORDS AND ARMIES"] = 6, ["IN VIEW"] = 6, ["YOUR REALM IN SUM"] = 12, ["SEEN EARLIER"] = 12, ["ENEMY ARMIES' TRAILS"] = 12,
            ["YOUR HOLDINGS"] = 24, ["ENEMY HOLDINGS NEAR"] = 24, ["ENEMY REALMS"] = 24, ["DIPLOMACY"] = 24, ["YOUR PURSE"] = 24,
            ["YOUR OWN BATTLES"] = 24, ["ALLIES AND JOINT PLANS"] = 24, ["YOUR CAPTURED LORDS"] = 24
        };

        private void SayContinuous()
        {
            Say("One conversation (continuous): model " + _c.ContinuousModel + ", effort " + _c.ContinuousEffort + ", "
                + (_c.ContinuousThinkingCap > 0 ? "thinking up to " + _c.ContinuousThinkingCap + " tokens" : "thinking as long as it needs") + ", "
                + (_c.ContinuousTimeoutSeconds > 0 ? "at most " + _c.ContinuousTimeoutSeconds + " s a call" : "no time limit") + ", the same for every call of a conversation; Claude Code at " + _claude);
            Say("Handed over at the ruler's battle past " + _c.CompactAtTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens, at the game's close past "
                + _c.CompactAtCloseTokens.ToString("N0", CultureInfo.InvariantCulture) + ", before any message past " + _c.CompactHardTokens.ToString("N0", CultureInfo.InvariantCulture) + ".");
        }

        // ---------- a message ----------

        /// <summary>One message of the ruler's, answered in the conversation (begun with the opening when there is none yet, or it was handed over).</summary>
        public void AnswerContinuous(JsonObject chat)
        {
            DateTime began = DateTime.Now;
            _lastUuid = null;
            int n = (int)Num(chat["number"]);
            string campaign = Str(chat["campaign"]), realm = Str(chat["realm"]), said = Str(chat["said"]) ?? "";
            int pending = chat["pending"] is JsonObject p ? (int)Num(p["chat"]) : -1;
            double hours = Hours(chat["hours"]);
            string memDir = Path.Combine(_c.MemoryDir, Safe(campaign ?? "unknown"));
            Directory.CreateDirectory(memDir);
            _campaign = campaign;
            _realm = realm;
            string carried = EnsureTalk(memDir, chat, hours);
            if (_talk.Started && _talk.Tokens >= _c.CompactHardTokens) Handover("this conversation is near the end of what one conversation can hold", n);
            bool opening = !_talk.Started;
            if (opening) { _talk.Sent.Clear(); _talk.SentAt.Clear(); _talk.Static.Clear(); }
            object[] mark = _talk.Mark();
            string report = Room.ReadShared(Path.Combine(_c.ChannelDir, "report.txt")) ?? "(The realm's report is not written yet: the game writes it every in-game hour.)";
            string prompt = (opening ? Opening(chat, memDir, report, hours) : Later(chat, report, carried, hours)).Replace("\r\n", "\n");
            Say("Message #" + n + ": \"" + Short(said, 80) + "\": " + (opening ? "a new conversation" : "in the conversation (" + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens)") + "...");
            CallResult r = Converse(prompt, opening ? "opening" : "chat", n, hours, out string folder);
            if (!Reached(r)) _talk.Restore(mark);
            JsonObject answer = null;
            string problem = r.Ok ? ExtractAnswer(r.Text, out answer) : r.Error;
            bool retried = false;
            if (answer == null && r.Ok)
            {
                retried = true;
                CallResult r2 = Converse("Your answer could not be read: " + problem + ". Reply again with ONLY the JSON object, nothing before or after it.", "chat-retry", n, hours, out _);
                problem = r2.Ok ? ExtractAnswer(r2.Text, out answer) : r2.Error;
                r.Seconds += r2.Seconds;
                r.OutTokens += r2.OutTokens;
                r.Cost += r2.Cost;
            }
            answer ??= new JsonObject { ["say"] = "(I could not answer that: " + (problem ?? "no answer") + ")" };
            WriteAnswer(_c.ChannelDir, n, answer, pending);
            File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            Remember(Path.Combine(memDir, "chat.jsonl"), n, chat, answer);
            if (!double.IsNaN(hours)) _talk.LastHours = double.IsNaN(_talk.LastHours) ? hours : Math.Max(_talk.LastHours, hours);
            _talk.Number = n;
            _talk.Save();
            _talk.Checkpoint(_lastUuid, hours, Str(chat["date"]), Str(chat["load_id"]));
            double seconds = (DateTime.Now - began).TotalSeconds;
            int proposed = (answer["propose"] as JsonArray)?.Count ?? 0, given = answer["carry_out"] is JsonValue cv && cv.TryGetValue(out bool cb) && cb ? (answer["orders"] as JsonArray)?.Count ?? 0 : 0;
            Log(new
            {
                kind = "chat", call = opening ? "opening" : "chat", continuous = true, at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), chat = n, campaign, realm, said,
                model = _c.ContinuousModel, effort = _c.ContinuousEffort, thinking_cap = _c.ContinuousThinkingCap, ok = problem == null, problem, retried, seconds = Math.Round(seconds, 2),
                call_seconds = Math.Round(r.Seconds, 2), tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite,
                cache_write_1h = r.CacheWrite1h, cost_usd_list = r.Cost, turns = r.Turns, proposed, carried = given, pending, memory = "continuous", context_tokens = r.LastContext,
                conversation_tokens = _talk.Tokens, conversation_messages = _talk.Messages, new_conversation = opening, session = _talk.SessionId, message_chars = prompt.Length,
                folder, run = Str(chat["run"]), arm = Str(chat["arm"]), system_hash = _talk.SystemHash, thinking_chars = r.ThinkingText.Length
            });
            Say("Message #" + n + (opening ? " (a new conversation)" : "") + ": answered in " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" + (proposed > 0 ? ", proposing " + proposed + " orders" : "")
                + (given > 0 ? ", carrying out " + given + " orders" : "") + "; the conversation holds " + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens" + (problem != null ? " (" + problem + ")" : "") + ".");
        }

        // ---------- the messages ----------

        /// <summary>
        /// The first message of a conversation: why it begins, the handover of its day (or none yet), then the whole message a fresh call would have had: the save's
        /// conversation so far, what became of its orders lately, the proposal waiting, the whole report of the hour, the chronicle's index, Party AI Controls, the words.
        /// </summary>
        internal string Opening(JsonObject chat, string memDir, string report, double hours)
        {
            var sb = new StringBuilder();
            string why = string.IsNullOrWhiteSpace(_talk.NextWhy) ? "your first conversation with your ruler in this campaign" : _talk.NextWhy.Trim().TrimEnd('.');
            sb.AppendLine("A NEW CONVERSATION WITH YOUR RULER BEGINS: " + why + ".");
            sb.AppendLine("From here on this conversation is your memory: your ruler's every message, your every answer and what you looked up stay in it until your next handover. "
                          + "Each later message brings only what is new (see \"How you are asked\" in your instructions).");
            sb.AppendLine();
            string handover = NewestHandover(memDir, hours, out string date);
            if (handover != null)
            {
                sb.AppendLine("YOUR HANDOVER (written by you, as of " + date + ")");
                sb.AppendLine(handover.Trim());
            }
            else sb.AppendLine("(No handover yet: this is your first conversation in this campaign. The conversation in this save so far, your chronicle and the report below are your memory.)");
            sb.AppendLine();
            string past = PastTalk.OpeningLine(memDir, _talk.Past);
            if (past != null) sb.AppendLine(past).AppendLine();
            List<string> receipts = Tail(Path.Combine(_c.ChannelDir, "receipts.log"), 12);
            if (receipts.Count > 0) _talk.Sent[ReceiptKey] = receipts[receipts.Count - 1];
            string chronicle = string.Join("\n\n", new[] { ChronicleIndex(_c.ChannelDir), OtherTalksNote(_c.ChannelDir) }.Where(x => x != null));
            string pac = Str(chat["party_ai"]);
            if (pac != null && pac.Trim().Length > 0) Room.ShownIn(_talk, "PARTY AI CONTROLS NOW", pac.Trim(), hours, false);
            sb.Append(Message(chat, ReadHistory(Path.Combine(memDir, "chat.jsonl"), HistoryLines), OpeningReport(_talk, report, hours), receipts, chronicle));
            return sb.ToString();
        }

        /// <summary>A later message of the conversation: only what is new since the last answer, and the report less what the conversation already holds.</summary>
        internal string Later(JsonObject chat, string report, string carried, double hours)
        {
            var sb = new StringBuilder();
            if (carried != null) sb.AppendLine(carried).AppendLine();
            string library = Room.ReadShared(Path.Combine(_c.ChannelDir, "tools.txt"));
            string own = library != null ? Room.OwnToolsChange(_talk, library) : null;
            if (own != null) sb.AppendLine(own).AppendLine();
            Middle(sb, chat, NewReceipts(Path.Combine(_c.ChannelDir, "receipts.log")), "WHAT BECAME OF ORDERS SINCE YOUR LAST ANSWER (receipts.log, newest last)", "- Nothing new.");
            sb.AppendLine("THE REALM NOW (the report of the hour, less what this conversation holds: a section exactly as you last saw it is one line, and the big lists come whole every few hours; "
                          + "the whole report of this hour is report.txt)");
            sb.Append(SlimReport(_talk, report, hours)).AppendLine();
            string index = ChronicleIndex(_c.ChannelDir);
            if (index != null) sb.AppendLine(index).AppendLine();
            string pac = Str(chat["party_ai"]);
            if (pac != null && pac.Trim().Length > 0) sb.AppendLine(Room.ShownIn(_talk, "PARTY AI CONTROLS NOW", pac.Trim(), hours, true)).AppendLine();
            Words(sb, chat);
            return sb.ToString();
        }

        /// <summary>The key under which the conversation keeps the last line of receipts.log it was shown.</summary>
        private const string ReceiptKey = "#receipts";

        /// <summary>The lines of receipts.log after the last one this conversation was shown (its last 12 when that line is gone), and that line kept.</summary>
        private List<string> NewReceipts(string file)
        {
            List<string> all = Tail(file, 60);
            int from = 0;
            if (_talk.Sent.TryGetValue(ReceiptKey, out string last) && last != null)
            {
                int at = all.LastIndexOf(last);
                from = at >= 0 ? at + 1 : Math.Max(0, all.Count - 12);
            }
            else from = Math.Max(0, all.Count - 12);
            if (all.Count > 0) _talk.Sent[ReceiptKey] = all[all.Count - 1];
            return all.Skip(from).ToList();
        }

        /// <summary>The whole report for an opening, each section kept as shown (so later messages can shorten it) and the chronicle's lines marked as seen.</summary>
        internal static string OpeningReport(Talk talk, string report, double hours)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> b in Room.Blocks(report))
            {
                if (b.Key == null) { sb.AppendLine(b.Value.TrimEnd()).AppendLine(); continue; }
                if (Room.Key(b.Key).StartsWith("RECENT HISTORY", StringComparison.Ordinal))
                    foreach (string l in b.Value.Replace("\r", "").Split('\n')) if (l.Trim().Length > 0) talk.Static.Add("h:" + Room.Hash(l.Trim()));
                sb.AppendLine(Room.ShownIn(talk, b.Key, b.Value, hours, false)).AppendLine();
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        /// <summary>
        /// The report for a later message: a big list shown within its hours becomes a line (changed or not); the chronicle's recent history only its lines not yet shown;
        /// any other section whole, or in a line when exactly as last shown, its rule paragraphs once a conversation.
        /// </summary>
        internal static string SlimReport(Talk talk, string report, double hours)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> b in Room.Blocks(report))
            {
                string head = b.Key, body = b.Value;
                if (head == null) { sb.AppendLine(body.TrimEnd()).AppendLine(); continue; }
                string key = Room.Key(head);
                if (key.StartsWith("RECENT HISTORY", StringComparison.Ordinal))
                {
                    List<string> fresh = body.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Trim().Length > 0 && talk.Static.Add("h:" + Room.Hash(l.Trim()))).ToList();
                    sb.AppendLine(fresh.Count == 0 ? head + ": nothing new since your last message." : head + " (only its lines new since your last message)\n" + string.Join("\n", fresh)).AppendLine();
                    continue;
                }
                KeyValuePair<string, double> every = BigLists.FirstOrDefault(kv => key.StartsWith(kv.Key, StringComparison.Ordinal));
                if (every.Key != null && talk.SentAt.TryGetValue(key, out double at) && !double.IsNaN(hours) && !double.IsNaN(at) && hours - at < every.Value)
                {
                    bool same = talk.Sent.TryGetValue(key, out string was) && was == Room.Hash(body.Trim());
                    string ago = Math.Max(0, Math.Round(hours - at)).ToString("0", CultureInfo.InvariantCulture);
                    sb.AppendLine(same ? head + ": as in your last message that showed it."
                                       : head + ": changed since you saw it " + ago + " hours ago; the whole of it as of this hour is in report.txt (read the part you need).").AppendLine();
                    continue;
                }
                sb.AppendLine(Room.ShownIn(talk, head, body, hours, true)).AppendLine();
            }
            return sb.ToString().TrimEnd() + "\n";
        }

        // ---------- the conversation's calls ----------

        /// <summary>
        /// One call in the conversation: begun under a new session (the first) or resumed, with the instructions and tools it began with, its one effort and thinking cap,
        /// the one-hour cache and Claude Code's compaction off. A call that stalls or runs away is asked once more. The conversation's size is kept after each.
        /// </summary>
        private CallResult Converse(string prompt, string kind, int n, double hours, out string folder)
        {
            bool fresh = !_talk.Started;
            RouteTool routes = Routes(_campaign, _realm, n);
            if (fresh)
            {
                _talk.SessionId = Guid.NewGuid().ToString();
                _talk.Routes = routes != null;
                _talk.PastTool = _talk.Past.Count > 0;
                _talk.SystemFile = SystemFile(_talk.Routes);
                _talk.SystemHash = _systemHash;
                _talk.StartedHours = hours;
                _talk.StartedWall = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _talk.StartedWhy = _talk.NextWhy ?? "";
                _talk.OwnTools = Room.OwnToolNames(Room.ReadShared(Path.Combine(_c.ChannelDir, "tools.txt")) ?? "");
                _talk.Save();
            }
            else if (_talk.SystemFile == null || !File.Exists(_talk.SystemFile))
            {
                _talk.SystemFile = SystemFile(_talk.Routes);
                if (_systemHash != _talk.SystemHash) Say("Continuous: the instructions this conversation began with are gone and their text has changed since; the next call writes the conversation into the cache again.");
                _talk.SystemHash = _systemHash;
            }
            // the same tools in every call: none when the conversation began without them
            if (!_talk.Routes) routes = null;
            else if (routes == null) Say("Continuous: the questions are not ready now; this call goes without them, so the conversation is written into the cache again.");
            // 0.18.9: its earlier conversations word for word, when it began with some (on their own when the way-finding questions are not offered)
            if (_talk.PastTool && _talkDir != null)
            {
                routes ??= new RouteTool(RouteTool.DefaultRoot(), _logDir, _campaign, _realm, n, "chat") { ChannelDir = _c.ChannelDir, BookOnly = true };
                routes.PastMem = _talkDir;
                routes.PastIds = _talk.Past;
            }
            folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-" + kind + "-" + n);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), prompt);
            // a conversation taken back to an earlier save (0.18.7) resumes the old session just after its checkpoint, as the new session
            bool fork = !fresh && _talk.ForkFrom != null && _talk.ForkAt != null;
            if (!fresh && !fork) { _talk.SinceCheckpoint = true; _talk.Save(); }   // 0.18.8: whatever this call leaves in the conversation is past the last checkpoint
            CallResult r = Claude.Run(_claude, _talk.SystemFile, prompt, _c.ContinuousModel, _c.ContinuousEffort, _c.ChannelDir, _c.Lookups, _c.ContinuousTimeoutSeconds, _c.ContinuousThinkingCap,
                                      fork ? _talk.ForkFrom : _talk.SessionId, !fresh, tools: routes, env: Room.ContinuousEnv, stallSeconds: kind.StartsWith("handover", StringComparison.Ordinal) ? Claude.HandoverStallSeconds : 0,
                                      forkAt: fork ? _talk.ForkAt : null, forkTo: fork ? _talk.SessionId : null);
            if (!r.Ok && (r.Stalled || r.Runaway))
            {
                File.WriteAllText(Path.Combine(folder, "raw-stalled.jsonl"), r.Raw ?? "");
                Say(kind + " #" + n + ": the call " + (r.Runaway ? "ran away" : "stalled") + " after " + r.Seconds.ToString("0", CultureInfo.InvariantCulture) + " s; asking again.");
                double lost = r.Seconds;
                if (fresh || fork) _talk.SessionId = Guid.NewGuid().ToString();   // a conversation's first call (or a taken-back one's) is begun again under a new id
                r = Claude.Run(_claude, _talk.SystemFile, prompt, _c.ContinuousModel, _c.ContinuousEffort, _c.ChannelDir, _c.Lookups, _c.ContinuousTimeoutSeconds, _c.ContinuousThinkingCap,
                               fork ? _talk.ForkFrom : _talk.SessionId, !fresh, tools: routes, env: Room.ContinuousEnv, stallSeconds: kind.StartsWith("handover", StringComparison.Ordinal) ? Claude.HandoverStallSeconds : 0,
                               forkAt: fork ? _talk.ForkAt : null, forkTo: fork ? _talk.SessionId : null);
                r.Seconds += lost;
            }
            File.WriteAllText(Path.Combine(folder, kind.EndsWith("retry", StringComparison.Ordinal) ? "raw-retry.jsonl" : "raw.jsonl"), r.Raw ?? "");
            Room.KeepThinking(folder, r);
            // a resumed session's cost as Claude Code reports it is the session's running total: this call's own is what it added
            if (r.Cost > 0)
            {
                double total = r.Cost;
                r.Cost = total >= _talk.CostSoFar - 1e-9 ? total - _talk.CostSoFar : total;
                _talk.CostSoFar = total;
            }
            if (Reached(r))
            {
                _talk.Started = true;
                _talk.Messages++;
                if (fork) _talk.ForkFrom = _talk.ForkAt = null;
                if (r.LastUuid != null && !kind.StartsWith("handover", StringComparison.Ordinal)) _lastUuid = r.LastUuid;
                long size = r.LastContext > 0 ? r.LastContext : r.InTokens + r.CacheRead + r.CacheWrite + r.OutTokens;
                if (size > 0) _talk.Tokens = size;
            }
            _talk.Save();
            _lastCallEnded = DateTime.Now;
            return r;
        }

        private static bool Reached(CallResult r) => r.Ok || (r.Raw != null && r.Raw.Contains("\"type\":\"assistant\""));

        // ---------- the campaign's conversation, and a reload ----------

        /// <summary>
        /// The conversation of the message's campaign. A new load of the game at or after the hour the conversation reached carries it on (told, when it had begun); a
        /// load of an earlier save sets aside the handovers written after that save's hour and begins a new conversation, told why.
        /// </summary>
        internal string EnsureTalk(string memDir, JsonObject chat, double hours)
        {
            if (_talk == null || !string.Equals(_talkDir, memDir, StringComparison.OrdinalIgnoreCase))
            {
                _talk = Talk.Load(memDir);
                _talkDir = memDir;
                Say("Memory of campaign " + Path.GetFileName(memDir) + ": " + (_talk.Started ? "its conversation holds " + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens" : "no conversation yet") + ".");
            }
            string loadId = Str(chat["load_id"]);
            if (string.IsNullOrEmpty(loadId) || loadId == _talk.LoadId) return null;
            _talk.AfterHandover = false;
            double loadedAt = Hours(chat["loaded_at"]), point = !double.IsNaN(loadedAt) ? loadedAt : hours;
            // 0.18.9: the earlier conversations this save's history holds, kept word for word (a handover's copy not made yet is made now)
            PastTalk.Mend(memDir, Say);
            _talk.Past = PastTalk.Visible(memDir, SaveHistory(chat), point);
            _talk.Save();
            bool known = !double.IsNaN(_talk.LastHours);
            // 0.18.8: something was sent after the last checkpoint (a message unanswered, a handover that never came): that is past the save too, so back to the checkpoint
            if (known && !double.IsNaN(point) && point >= _talk.LastHours - 0.5 && _talk.SinceCheckpoint && _talk.RollBack(SaveHistory(chat), point, out string kept))
            {
                _talk.LoadId = loadId;
                _talk.LastHours = point;
                _talk.Save();
                Say("Memory: the game was loaded again at " + Program.When(point) + "; what the conversation was sent after its answer of " + kept + " is taken out, and it carries on from that answer.");
                return "THE GAME WAS LOADED AGAIN at " + Program.When(point) + ". This conversation has been taken back to just after your answer of " + kept + ": anything sent after it did not happen in this game.";
            }
            if (_talk.AfterHandover)
            {
                // the last mark was a handover: the next message begins the conversation that follows it
                _talk.LoadId = loadId;
                _talk.LastHours = point;
                _talk.Save();
                return null;
            }
            if (!known || double.IsNaN(point) || point >= _talk.LastHours - 0.5)
            {
                _talk.LoadId = loadId;
                _talk.Save();
                if (!_talk.Started || !known) return null;
                string at = double.IsNaN(point) ? "the hour this conversation reached" : Program.When(point);
                Say("Memory: the game was loaded again at " + at + ", where the conversation left off; it carries on.");
                return "THE GAME WAS LOADED AGAIN at " + at + ", where this conversation left off. Your orders in force carry on as the report says.";
            }
            int set = VoidHandoversAfter(memDir, point);
            // the campaign's one conversation, taken back to just where it stood at the save (0.18.7); a new one only when the save is older than it
            if (_talk.RollBack(SaveHistory(chat), point, out string back))
            {
                _talk.LoadId = loadId;
                _talk.LastHours = point;
                _talk.Save();
                Say("Memory: an earlier save was loaded (" + Program.When(point) + "): the conversation goes back to its answer of " + back + " (" + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens) and carries on from there.");
                return "THE GAME WAS LOADED FROM AN EARLIER SAVE, of " + Program.When(point) + ". This conversation has been taken back to where it stood then, just after your answer of " + back
                       + ": what was said, ordered and learned after that did not happen. Your orders in force are as they stood at that save.";
            }
            // the save's history last saw a handover (0.18.8): the next message begins the conversation that follows it, from it
            if (_talk.AfterHandover)
            {
                _talk.LoadId = loadId;
                _talk.LastHours = point;
                _talk.Save();
                Say("Memory: a save from after the handover of " + back + " was loaded: the next message begins a new conversation from it.");
                return null;
            }
            _talk.Reset("the game was loaded from a save of " + Program.When(point) + ", earlier than your last conversation reached, so what it held after that day did not happen; you begin from what you knew then");
            _talk.LoadId = loadId;
            _talk.LastHours = point;
            _talk.Save();
            Say("Memory: an earlier save was loaded (" + Program.When(point) + "): the next message begins a new conversation" + (set > 0 ? "; " + set + " handover" + (set == 1 ? "" : "s") + " written after that day set aside" : "") + ".");
            return null;
        }

        /// <summary>The newest handover of the campaign written at or before this hour and not set aside, or null.</summary>
        internal static string NewestHandover(string memDir, double hours, out string date)
        {
            date = "";
            foreach (JsonObject h in ReadHandovers(memDir).AsEnumerable().Reverse())
            {
                if (h["void"] is JsonValue v && v.TryGetValue(out bool off) && off) continue;
                double at = Hours(h["hours"]);
                if (!double.IsNaN(hours) && !double.IsNaN(at) && at > hours + 0.5) continue;
                string file = Path.Combine(memDir, Str(h["file"]) ?? "");
                string text = Room.ReadShared(file);
                if (string.IsNullOrWhiteSpace(text)) continue;
                date = Str(h["date"]) ?? "";
                return text;
            }
            return null;
        }

        /// <summary>Sets aside the handovers written after this hour (a save from before them was loaded); how many.</summary>
        internal static int VoidHandoversAfter(string memDir, double hours)
        {
            List<JsonObject> all = ReadHandovers(memDir);
            int set = 0;
            foreach (JsonObject h in all)
            {
                double at = Hours(h["hours"]);
                bool off = h["void"] is JsonValue v && v.TryGetValue(out bool b) && b;
                if (!off && !double.IsNaN(at) && at > hours + 0.5) { h["void"] = true; set++; }
            }
            if (set > 0) Memory.Write(Path.Combine(memDir, HandoverList), string.Join("\n", all.Select(h => h.ToJsonString())) + "\n");
            return set;
        }

        /// <summary>A handover kept: its text as handover-(time).md in the campaign's memory folder, and its line in the list.</summary>
        internal static string KeepHandover(string memDir, string text, double hours, string date)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), name = "handover-" + stamp + ".md";
            for (int k = 2; File.Exists(Path.Combine(memDir, name)); k++) name = "handover-" + stamp + "-" + k + ".md";
            File.WriteAllText(Path.Combine(memDir, name), text + "\n", new UTF8Encoding(false));
            var line = new JsonObject { ["hours"] = double.IsNaN(hours) ? null : hours, ["date"] = date, ["file"] = name, ["written"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
            File.AppendAllText(Path.Combine(memDir, HandoverList), line.ToJsonString() + "\n", new UTF8Encoding(false));
            return name;
        }

        private static List<JsonObject> ReadHandovers(string memDir)
        {
            var list = new List<JsonObject>();
            string text = Room.ReadShared(Path.Combine(memDir, HandoverList));
            if (text == null) return list;
            foreach (string l in text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(l)) continue;
                try { if (JsonNode.Parse(l) is JsonObject o) list.Add(o); }
                catch (JsonException) { }
            }
            return list;
        }

        // ---------- the handover ----------

        /// <summary>
        /// The moments of a handover: the ruler's own battle beginning (spare.txt fresh; the clock stands still), once the conversation holds compact_at_tokens; the game
        /// closing (its program gone, after it was seen running), once it holds compact_at_close_tokens and the conversation's cache is still warm.
        /// </summary>
        private void HandoverIfDue()
        {
            bool battle = SpareWord();
            bool closed = GameClosed();
            if (_talk == null || !_talk.Started) return;
            if (battle && _talk.Tokens >= _c.CompactAtTokens) Handover("your ruler's battle has begun, and the clock stands still while it lasts", _talk.Number);
            else if (closed && _talk.Tokens >= _c.CompactAtCloseTokens && (DateTime.Now - _lastCallEnded).TotalMinutes < 55) Handover("your ruler has closed the game", _talk.Number);
        }

        /// <summary>The game has just said the ruler's own battle began (spare.txt), once each time; a word older than two minutes is let go.</summary>
        private bool SpareWord()
        {
            string file = Path.Combine(_c.ChannelDir, "spare.txt");
            try
            {
                if (!File.Exists(file)) return false;
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written == _spareWord) return false;
                _spareWord = written;
                return (DateTime.UtcNow - written).TotalSeconds <= Room.SpareFreshSeconds;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>The game's program has gone, having been seen running by this war room: true once. Looked at every five seconds.</summary>
        private bool GameClosed()
        {
            if ((DateTime.Now - _gameLooked).TotalSeconds < 5) return false;
            _gameLooked = DateTime.Now;
            if (Room.GameRunning()) { _gameSeen = true; return false; }
            if (!_gameSeen) return false;
            _gameSeen = false;
            Say("Continuous: the game has been closed.");
            return true;
        }

        /// <summary>The report's sections the handover's part on orders takes: the orders in force and every lord's doing now.</summary>
        private static readonly string[] FactSections = { "YOUR ORDERS IN FORCE", "YOUR LORDS AND ARMIES" };

        /// <summary>
        /// The lieutenant writes its handover in the conversation, in the shape its instructions give; it is kept in the campaign's memory folder with its hour, and the
        /// conversation ends: the ruler's next message begins a new one from it. True when it was written.
        /// </summary>
        internal bool Handover(string why, int n)
        {
            if (_talk == null || !_talk.Started || _talkDir == null) return false;
            DateTime began = DateTime.Now;
            string report = Room.ReadShared(Path.Combine(_c.ChannelDir, "report.txt")) ?? "";
            string date = ReportDate(report) ?? Str(ReadChat(_c.ChannelDir)?["date"]) ?? "";
            double hours = _talk.LastHours;
            long before = _talk.Tokens;
            var facts = new List<string>();
            foreach (KeyValuePair<string, string> b in Room.Blocks(report))
                if (b.Key != null && FactSections.Any(f => Room.Key(b.Key).StartsWith(f, StringComparison.Ordinal))) facts.Add((b.Key + "\n" + b.Value.TrimEnd()).Trim());
            string pac = Str(ReadChat(_c.ChannelDir)?["party_ai"]);
            if (pac != null && pac.Trim().Length > 0) facts.Add("PARTY AI CONTROLS NOW\n" + pac.Trim());
            string prompt = "WRITE YOUR HANDOVER NOW: " + why.TrimEnd('.') + ". This conversation ends with it. Your ruler's next message begins a new conversation that holds only your instructions, "
                            + "this handover, the latest lines of your conversation with him in this save (the whole of it is conversation.txt), what became of your orders lately and the realm's report "
                            + "of the hour, so put in it everything you will need that those do not hold (this conversation stays readable word for word through the past question, but keep at hand "
                            + "what you will need). Write it as \"Your handover\" in your instructions says, in its parts and in your own voice, "
                            + "as plain text beginning with the word HANDOVER and the date (" + date + "). No JSON and no orders: your orders in force carry on as they are."
                            + (facts.Count > 0 ? " Write the part on orders in force from the game's own list below (as of " + (date.Length > 0 ? date : "its last report") + "), adding what only you know: why each lord has his order and what would end it.\n\n" + string.Join("\n\n", facts) + "\n" : "");
            Say("Handover (" + why + "): the conversation holds " + before.ToString("N0", CultureInfo.InvariantCulture) + " tokens...");
            CallResult r = Converse(prompt, "handover", n, hours, out string folder);
            string text = r.Ok ? Room.HandoverText(r.Text) : null;
            bool retried = false;
            if (text == null && r.Ok)
            {
                retried = true;
                CallResult r2 = Converse("That is not a handover in the shape your instructions give. Write it again: plain text beginning with HANDOVER, every part in it.", "handover-retry", n, hours, out _);
                text = r2.Ok ? Room.HandoverText(r2.Text) : null;
                r.Seconds += r2.Seconds;
                r.OutTokens += r2.OutTokens;
                r.Cost += r2.Cost;
            }
            bool kept = text != null;
            if (kept)
            {
                KeepHandover(_talkDir, text, hours, date);
                File.WriteAllText(Path.Combine(folder, "handover.md"), text + "\n", new UTF8Encoding(false));
                string ended = _talk.SessionId;
                _talk.MarkHandover(hours, date, _talk.LoadId, why.TrimEnd('.'), ended);
                PastTalk.KeepEnded(_talk, _talkDir, ended, hours, date, _talk.LoadId, why.TrimEnd('.'), Say);
                _talk.Reset("your last conversation ended with the handover below (" + why.TrimEnd('.') + ", " + date + ")");
            }
            double seconds = (DateTime.Now - began).TotalSeconds;
            Log(new
            {
                kind = "handover", continuous = true, at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), chat = n, when = date, why, model = _c.ContinuousModel,
                effort = _c.ContinuousEffort, thinking_cap = _c.ContinuousThinkingCap, ok = kept, retried, seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2),
                tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cache_write_1h = r.CacheWrite1h, cost_usd_list = r.Cost,
                conversation_tokens = before, handover_words = Room.Words(text), campaign = _campaign, folder, problem = kept ? null : r.Error ?? "no handover in its answer"
            });
            Say(kept ? "Handover written (" + Room.Words(text) + " words, " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s); the ruler's next message begins a new conversation from it."
                     : "Handover: NOT WRITTEN (" + (r.Error ?? "no handover in its answer") + "); the conversation carries on.");
            return kept;
        }

        /// <summary>The date on the report's first line ("SITUATION AT Summer 20, 1087, 22:00 (refreshed ...)"), or null.</summary>
        internal static string ReportDate(string report)
        {
            string first = (report ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.StartsWith("SITUATION AT ", StringComparison.Ordinal));
            if (first == null) return null;
            string d = first.Substring("SITUATION AT ".Length);
            int b = d.IndexOf(" (", StringComparison.Ordinal);
            return (b > 0 ? d.Substring(0, b) : d).Trim();
        }

        /// <summary>The save's history as the message carries it (save_history.before, module 0.18.5): the game's sessions its world lived through, each to its hour; null without one.</summary>
        internal static List<KeyValuePair<string, double>> SaveHistory(JsonObject chat)
        {
            if (chat?["save_history"] is not JsonObject h || h["before"] is not JsonArray steps) return null;
            var list = new List<KeyValuePair<string, double>>();
            foreach (JsonNode st in steps) list.Add(new KeyValuePair<string, double>(st?["load"] is JsonValue lv && lv.TryGetValue(out string l) ? l : null, Hours(st?["until"])));
            return list;
        }

        /// <summary>An in-game hour from a message's field: NaN when it is missing or not a number.</summary>
        private static double Hours(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return d;
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out int i)) return i;
            }
            return double.NaN;
        }

        // ---------- for the self-test ----------

        internal Talk TalkNow => _talk;
        internal void UseForTest(Talk t, string dir) { _talk = t; _talkDir = dir; }
    }
}
