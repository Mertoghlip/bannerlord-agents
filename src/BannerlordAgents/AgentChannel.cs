using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The agent channel: plain files through which an agent outside the game (for the first tests, a separate Claude
    /// Code chat; later the war room) plays the watched realm. It sees only what the realm sees, and acts only through
    /// orders that the game checks.
    ///
    /// The bridge writes, in the realm's channel folder (Settings.AgentFolderFor):
    /// - pokes.log: one line per poke ("#12 | Summer 3, 1085, 14:00 | Enemy army in view: ..."). The agent watches it.
    ///   Poke numbers carry on across loads, so a number never means two different pokes.
    /// - poke.txt: the latest poke, written (swapped in whole) before its line appears in pokes.log: which number to
    ///   answer, the mode, and the full report with the reasons for the poke.
    /// - report.txt and council.txt: the situation now and the council's briefing, written when the war room puts a
    ///   council-request*.json in orders\ as a council sits (0.12.0); the lieutenant's report.txt every in-game hour.
    /// - receipts.log: one line per order the agent gave (applied, checked in shadow mode, or refused and why), how long
    ///   the answer took after its poke, and later what became of each order. When the game's clock is stopped, a file
    ///   is acknowledged at once as RECEIVED and its orders answered when the clock runs again.
    /// The agent writes each answer as a JSON file into orders\. The folder is watched (Windows tells us when a file
    /// appears; a slow check every 10 s is the safety net). Each file is read once, answered in receipts.log, and moved
    /// to orders\done\. Files written before this game session began are set aside unread in orders\stale\, and an
    /// answer to a poke from before the last load is refused: the situation it answered no longer exists.
    ///
    /// Since 0.8 every watched realm has a channel of its own (Channel), in its own folder, numbering its own pokes; an
    /// answer in a realm's folder can order only that realm's lords. The first realm's channel is agent_dir.
    ///
    /// Modes (Settings.Agent): off; shadow (every order is checked against the game and receipted, none carried out);
    /// live (orders are carried out, but only by the lords in Settings.AgentLords). Worker thread, except Start; the
    /// orders themselves are carried out on the game's thread (Orders.FromAgent).
    /// </summary>
    internal static class AgentChannel
    {
        internal sealed class AgentOrder
        {
            public string File;
            public int Index;
            public int Poke = -1;
            public string Lord, Do, Target, At, Why;
            /// <summary>The realm whose channel the order came in by (null: the first realm's, or the console).</summary>
            public string Realm;
            public readonly List<string> Lords = new List<string>();
            /// <summary>The holdings a tool that takes "places" works through (0.13): ids or names, in the order given.</summary>
            public readonly List<string> Places = new List<string>();
            public readonly Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The order to start when this one finishes (same lord).</summary>
            public JObject Then;
            /// <summary>The order as written (the lieutenant's party_ai reads its settings from it, 0.11.1).</summary>
            public JObject Raw;
            public DateTime Received;
            /// <summary>The tool run the order started (0.13, the episode index), once the game's thread started it; 0 for none.</summary>
            public int ToolRun;
            /// <summary>The agent's own tool the order names, from its answer or its realm's library (0.13, OwnTools); null for ours.</summary>
            public Tools.ToolDef Tool;
        }

        /// <summary>
        /// What one answer file holds besides its orders (0.13): what kind of call wrote it (an answer or a council), the war room's
        /// folder for that call, and its tools, diplomatic acts and letters (each list as written; null when absent).
        /// </summary>
        internal sealed class Answer
        {
            public string Kind = "answer", Call, File;
            public int Poke = -1, Orders;
            public JArray ToolList, DiplomacyList, LetterList;
            public int Tools => ToolList?.Count ?? 0;
            public int Diplomacy => DiplomacyList?.Count ?? 0;
            public int Letters => LetterList?.Count ?? 0;
        }

        /// <summary>The keys of an order that are not settings; any other number in an order is taken as a setting.</summary>
        private static readonly HashSet<string> OrderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lord", "do", "order", "verb", "target", "at", "place", "why", "lords", "settings", "then", "poke", "places"
        };

        private static Timer _fallback;

        /// <summary>Is any agent's channel on (shadow or live)? Each agent has its own mode (Agents, since 0.9).</summary>
        public static bool On => Agents.AnyOn;

        /// <summary>Every agent's channel, in the order the settings list the agents.</summary>
        public static List<Channel> All => Agents.All.Select(a => a.Channel).ToList();

        /// <summary>The channel of the agent listed first (the console's and the smoke test's default).</summary>
        public static Channel Primary => Agents.For(null).Channel;

        /// <summary>A realm's channel (the first agent's for null or a realm without an agent).</summary>
        public static Channel For(string realm) => Agents.For(realm).Channel;

        /// <summary>The first agent's channel folder.</summary>
        public static string Dir => Primary.Dir;

        /// <summary>At session start: the slow safety-net check every 10 s, which also keeps each folder watch in place.</summary>
        public static void Start()
        {
            if (_fallback != null) return;
            _fallback = new Timer(_ =>
            {
                if (!On || !Live.CampaignRunning) return;
                foreach (Channel c in All)
                {
                    c.EnsureWatcher();
                    c.RequestScan();
                }
            }, null, 1000, 10000);
        }

        /// <summary>The first agent's mode, in words (the console).</summary>
        public static string ModeText() => Primary.ModeText();

        /// <summary>A mode in words, as the agent reads it.</summary>
        public static string ModeText(string mode, string lords)
        {
            switch (mode)
            {
                case "live":
                    return "LIVE. " + (Text.Plain(lords ?? "all") == "all" ? "Every lord of yours obeys your orders."
                           : "Only these lords obey: " + lords + ". The others are led by the game's own AI, and orders for them are refused.");
                case "shadow":
                    return "SHADOW. Every order is checked against the game and answered in receipts.log, but none is carried out yet.";
                default:
                    return "OFF.";
            }
        }

        /// <summary>One line for the black box and agents.health (each realm's channel, when there is more than one).</summary>
        public static string StatusLine()
        {
            List<Channel> all = All;
            return all.Count == 1 ? all[0].StatusLine() : string.Join(" | ", all.Select(c => c.Realm + ": " + c.StatusLine()));
        }

        // the session's sums, for the summary and the console
        public static int AnswerFiles => All.Sum(c => c.AnswerFiles);
        public static int OrdersReceived => All.Sum(c => c.OrdersReceived);
        public static int Applied => All.Sum(c => c.Applied);
        public static int Checked => All.Sum(c => c.Checked);
        public static int Refused => All.Sum(c => c.Refused);
        /// <summary>Seconds since the oldest poke still waiting for any answer, in any channel, for the health row.</summary>
        public static double WaitingSeconds => All.Max(c => c.WaitingSeconds);

        /// <summary>Any thread: asks the worker to look at every orders folder.</summary>
        public static void RequestScan() { foreach (Channel c in All) c.RequestScan(); }

        /// <summary>Worker: looks at every orders folder now.</summary>
        public static void Scan() { foreach (Channel c in All) c.Scan(); }

        /// <summary>The answer to one order, in the channel it came by.</summary>
        public static void Receipt(AgentOrder a, string status, string reason, double hours) => For(a?.Realm).Receipt(a, status, reason, hours);

        /// <summary>What became of a diplomatic act (0.13), in the channel it came by.</summary>
        public static void DiploReceipt(DiploAct a, string status, string reason, double hours) => For(a?.Realm).DiploReceipt(a, status, reason, hours);

        /// <summary>What became of an agent's order later (arrived, given again, ended), in the first realm's channel.</summary>
        public static void Event(string text) => Primary.Event(text);

        /// <summary>What became of an agent's order later, in that realm's channel.</summary>
        public static void Event(string realm, string text) => For(realm).Event(text);

        /// <summary>
        /// The library of orders each agent can give, written into its channel as tools.txt once a session (the war room's system prompt
        /// reads it): ours, Party AI Controls' for the lieutenant (0.11.1), and since 0.13 the realm's own kept tools with their records;
        /// and each tool's whole definition as tools\(name).json, ours and the realm's own (docs/PLAN-0.13.md A1). With a realm: only the
        /// channels serving it, when its own library changed.
        /// </summary>
        public static void WriteLibrary(string realm = null)
        {
            if (!On) return;
            List<string> problems = Tools.Library.Problems();
            if (problems.Count > 0 && realm == null)
                Diag.Alarm("tool_library", "The tool library has " + problems.Count + " problems, so some orders will not work: " + string.Join("; ", problems.Take(3)) + ".", true);
            string text = Tools.Library.Text();
            int written = 0;
            foreach (Channel c in All)
            {
                if (realm != null && c.Realm != realm) continue;
                string own = c.Realm != null ? Tools.OwnLibrary.TextFor(c.Realm) : "";
                Files.Overwrite(Path.Combine(c.Dir, "tools.txt"), (c.IsLieutenant ? text + PacRules.LibraryText() : text) + (own.Length > 0 ? "\n" + own : ""));
                c.WriteDefinitions();
                written++;
            }
            Diag.Event("tool", "library written", new { tools = Tools.Library.All.Count, problems, channels = written, realm });
        }

        /// <summary>The watchdog's checks (its own thread; reads plain fields only).</summary>
        public static void CheckAlive() { foreach (Channel c in All) c.CheckAlive(); }

        // ---------- numbering (the campaign's poke file) ----------

        internal static string NumberFile(string campaignPokesFile)
            => Path.Combine(Path.GetDirectoryName(campaignPokesFile), Path.GetFileNameWithoutExtension(campaignPokesFile) + ".number");

        /// <summary>
        /// The highest poke number a campaign has used, kept in pokes_&lt;realm&gt;.number beside its poke file. The first
        /// time, it is read from the whole poke file: each of its lines holds a whole report, longer than any tail worth
        /// reading, so a look at the file's end found no number and the numbers began again at #1 (first live test, Oct 9).
        /// </summary>
        internal static int HighestIn(string campaignPokesFile)
        {
            if (campaignPokesFile == null) return 0;
            try
            {
                string kept = NumberFile(campaignPokesFile);
                if (System.IO.File.Exists(kept) && int.TryParse(System.IO.File.ReadAllText(kept).Trim(), NumberStyles.Integer, Text.Inv, out int n)) return n;
                if (!System.IO.File.Exists(campaignPokesFile)) return 0;
                const string marker = "{\"type\":\"poke\",\"number\":";
                int best = 0;
                using (var fs = new FileStream(campaignPokesFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!line.StartsWith(marker, StringComparison.Ordinal)) continue;
                        int j = marker.Length;
                        while (j < line.Length && char.IsDigit(line[j])) j++;
                        if (int.TryParse(line.Substring(marker.Length, j - marker.Length), NumberStyles.Integer, Text.Inv, out int v) && v > best) best = v;
                    }
                }
                return best;
            }
            catch (Exception e)
            {
                Diag.Swallowed("reading the campaign's highest poke number", e);
                return 0;
            }
        }

        /// <summary>The last number after the marker in the end of a file (lines start with it when atLineStart).</summary>
        internal static int LastNumberIn(string path, string marker, bool atLineStart)
        {
            try
            {
                if (path == null || !System.IO.File.Exists(path)) return 0;
                string tail;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - 64 * 1024);
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8)) tail = sr.ReadToEnd();
                }
                int best = 0, at = -1;
                if (atLineStart)
                {
                    int i = tail.LastIndexOf("\n" + marker, StringComparison.Ordinal);
                    if (i >= 0) at = i + 1 + marker.Length;
                    else if (tail.StartsWith(marker, StringComparison.Ordinal)) at = marker.Length;
                }
                else
                {
                    int i = tail.LastIndexOf(marker, StringComparison.Ordinal);
                    if (i >= 0) at = i + marker.Length;
                }
                if (at < 0) return 0;
                int j = at;
                while (j < tail.Length && char.IsDigit(tail[j])) j++;
                if (j > at) int.TryParse(tail.Substring(at, j - at), out best);
                return best;
            }
            catch (Exception e)
            {
                Diag.Swallowed("reading the last poke number", e);
                return 0;
            }
        }

        // ---------- reading an answer ----------

        /// <summary>
        /// Reads one answer file: {"poke": 12, "orders": [{"lord": ..., "do": ..., "target": ..., "why": ...}], "note": ...}.
        /// A bare list of orders, or a single order, is accepted too.
        /// </summary>
        public static bool Parse(string path, out int poke, out List<AgentOrder> orders, out string error) => Parse(path, out poke, out orders, out _, out error);

        /// <summary>The same, with what else the answer holds (0.13): its kind, its call's folder, its tools, diplomatic acts and letters.</summary>
        public static bool Parse(string path, out int poke, out List<AgentOrder> orders, out Answer answer, out string error)
        {
            poke = -1;
            orders = new List<AgentOrder>();
            answer = new Answer { File = Path.GetFileName(path) };
            error = null;
            string text;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
            }
            catch (Exception e) { error = e.Message; return false; }
            JToken root;
            try { root = JToken.Parse(text); }
            catch (Exception e) { error = "not valid JSON: " + e.Message; return false; }

            JArray list;
            if (root is JObject obj)
            {
                poke = (int?)obj["poke"] ?? -1;
                list = obj["orders"] as JArray;
                if (list == null && obj["lord"] != null) list = new JArray(obj);
                if (list == null) list = new JArray();
                answer.Kind = ((string)obj["kind"] ?? "answer").Trim().ToLowerInvariant();
                answer.Call = (string)obj["call"];
                answer.ToolList = obj["tools"] as JArray;
                answer.DiplomacyList = obj["diplomacy"] as JArray;
                answer.LetterList = obj["letters"] as JArray;
            }
            else if (root is JArray arr) list = arr;
            else { error = "expected an object with \"orders\""; return false; }

            int i = 0;
            foreach (JToken t in list)
            {
                i++;
                if (!(t is JObject o)) continue;
                AgentOrder a = OrderFrom(o, null, poke, Path.GetFileName(path));
                a.Index = i;
                orders.Add(a);
            }
            answer.Poke = poke;
            answer.Orders = orders.Count;
            return true;
        }

        /// <summary>
        /// One order from its JSON: lord, do, at (a holding) or target (a lord), lords (a list), settings (numbers, also
        /// accepted at the order's top level), why, and then (the next order, for the same lord). Also used for "then".
        /// </summary>
        public static AgentOrder OrderFrom(JObject o, string lord, int poke, string file)
        {
            var a = new AgentOrder
            {
                File = file, Index = 1, Poke = (int?)o["poke"] ?? poke, Received = DateTime.Now,
                Lord = (string)o["lord"] ?? lord, Do = (string)(o["do"] ?? o["order"] ?? o["verb"]),
                Target = Str(o["target"]), At = Str(o["at"] ?? o["place"]), Why = (string)o["why"], Then = o["then"] as JObject, Raw = o
            };
            JToken lords = o["lords"];
            if (lords is JArray arr) a.Lords.AddRange(arr.Select(Str).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            else if (lords != null && lords.Type == JTokenType.String) a.Lords.AddRange(((string)lords).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            // a tool's list of holdings (0.13): ids or names, in order
            JToken places = o["places"];
            if (places is JArray pa) a.Places.AddRange(pa.Select(Str).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            else if (places != null && places.Type == JTokenType.String) a.Places.AddRange(((string)places).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            if (o["settings"] is JObject set)
                foreach (JProperty p in set.Properties())
                    if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float) a.Settings[p.Name] = (double)p.Value;
            foreach (JProperty p in o.Properties())
                if (!OrderKeys.Contains(p.Name) && (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)) a.Settings[p.Name] = (double)p.Value;
            // a next order naming a tool its answer wrote carries that tool's fingerprint (Channel.Attach): it starts that very tool
            if (o["tool_hash"] is JValue th && th.Type == JTokenType.String) a.Tool = Tools.OwnLibrary.ByHash((string)th);
            return a;
        }

        /// <summary>An id written as a string, a number or ["id"].</summary>
        private static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t is JArray a) return a.Count > 0 ? Str(a[0]) : null;
            return ((string)t)?.Trim().Trim('[', ']');
        }
    }

    /// <summary>One watched realm's agent channel (AgentChannel): its folder, its pokes, the answers in it and their receipts.</summary>
    internal sealed class Channel
    {
        private sealed class PokeInfo
        {
            public DateTime Wall;
            public double Hours;
            public int Answers;
        }

        /// <summary>The agent whose channel this is (Agents).</summary>
        private readonly RealmAgent _agent;

        private Timer _recheck;
        private FileSystemWatcher _watcher;
        private string _watchedDir;
        private int _scanQueued;
        private readonly Dictionary<int, PokeInfo> Pokes = new Dictionary<int, PokeInfo>();
        private readonly Dictionary<string, int> Unreadable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> Shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Brackets = new Regex(@"\[([^\]\s]+)\]", RegexOptions.Compiled);
        private string _shownFile;
        private int _lastPoke = -1, _before = -1, _next;
        private DateTime _sessionStart = DateTime.Now;
        private bool _leftoversChecked;
        private DateTime _waitingSince = DateTime.MinValue;
        private int _waitingFor = -1;
        private bool _silenceAlarmed, _scanAlarmed;
        /// <summary>The newest poke the agent has answered this session, and what happened since, for the next poke.</summary>
        private int _lastAnswered = -1;
        private readonly List<KeyValuePair<int, List<string>>> PokeReasons = new List<KeyValuePair<int, List<string>>>();
        private readonly List<KeyValuePair<DateTime, string>> Lines = new List<KeyValuePair<DateTime, string>>();

        /// <summary>When the folder was last checked (Stopwatch ticks); the watchdog alarms if this stops.</summary>
        public long LastScan;
        public int AnswerFiles, OrdersReceived, Applied, Checked, Refused;

        public Channel(RealmAgent agent) { _agent = agent; }

        /// <summary>The war room's request for its council's briefing, in the orders folder (0.12.0): council-request*.json.</summary>
        public const string BriefingRequest = "council-request";

        /// <summary>The realm (null: the stand-in agent before a campaign, or in the smoke test).</summary>
        public string Realm => _agent.Realm;
        public string Dir => _agent.Config.ChannelDir;
        public bool On => _agent.On;
        public string Mode => _agent.Config.Mode;
        public string ModeText() => AgentChannel.ModeText(_agent.Config.Mode, _agent.Config.Lords);
        /// <summary>The player's lieutenant's channel (since 0.10): it gets the player's words (chat.json), never pokes.</summary>
        public bool IsLieutenant => _agent.Config.IsLieutenant;

        // ---------- the lieutenant's chat (0.10) ----------

        /// <summary>The highest chat number in the channel's chats.log, so numbers carry on across loads (-1: none yet).</summary>
        public int LastChatNumber() => AgentChannel.LastNumberIn(Path.Combine(Dir, "chats.log"), "#", true);

        /// <summary>
        /// The player's words for the lieutenant: the save's whole conversation so far (conversation.txt, which the lieutenant may
        /// read, 0.11.1), chat.json (swapped in whole), then a line in chats.log. Worker.
        /// </summary>
        public void WriteChat(int n, object chat, string said, string conversation = null)
        {
            Paths.Ensure(OrdersDir);
            if (conversation != null)
                Files.Overwrite(Path.Combine(Dir, "conversation.txt"), "THE WHOLE CONVERSATION WITH YOUR RULER IN THIS SAVE (oldest first)\n" + (conversation.Length > 0 ? conversation : "(nothing yet)") + "\n");
            Files.Overwrite(Path.Combine(Dir, "chat.json"), Json.Line(chat));
            Files.Append(Path.Combine(Dir, "chats.log"), "#" + n + " | " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " | you: " + said);
            Files.FlushAll();
        }
        private string OrdersDir => Path.Combine(Dir, "orders");
        /// <summary>In an alarm, which realm's channel (only when there is more than one).</summary>
        private string Whose => Realm != null && Agents.All.Count > 1 ? "(" + Realm + ") " : "";

        /// <summary>Seconds since the oldest poke still waiting for any answer, for the health row.</summary>
        public double WaitingSeconds => _waitingSince == DateTime.MinValue ? 0 : (DateTime.Now - _waitingSince).TotalSeconds;

        /// <summary>The channel is replaced (a new campaign): its folder is no longer watched.</summary>
        public void Close()
        {
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
            try { _recheck?.Dispose(); } catch { }
            _recheck = null;
        }

        /// <summary>A new campaign session (worker): forget the last one's pokes; ids it was shown are kept per campaign and realm.</summary>
        public void Reset(string dataDir)
        {
            Pokes.Clear();
            Unreadable.Clear();
            Shown.Clear();
            _lastPoke = -1;
            _before = -1;
            _next = 0;
            _sessionStart = DateTime.Now;
            _leftoversChecked = false;
            _waitingSince = DateTime.MinValue;
            _waitingFor = -1;
            _silenceAlarmed = false;
            _lastAnswered = -1;
            PokeReasons.Clear();
            Lines.Clear();
            AnswerFiles = OrdersReceived = Applied = Checked = Refused = 0;
            _shownFile = dataDir != null ? Path.Combine(dataDir, Realm == null ? "ids_shown.txt" : "ids_shown_" + Realm + ".txt") : null;
            try
            {
                if (_shownFile != null && System.IO.File.Exists(_shownFile))
                    foreach (string id in System.IO.File.ReadAllLines(_shownFile)) if (id.Trim().Length > 0) Shown.Add(id.Trim());
            }
            catch (Exception e) { Diag.Swallowed("reading the ids shown to the agent", e); }
        }

        /// <summary>One line for the black box and agents.health.</summary>
        public string StatusLine()
        {
            string wait = _waitingSince == DateTime.MinValue ? "no poke waiting" : "poke #" + _waitingFor + " unanswered for " + (int)WaitingSeconds + " s";
            return Mode + "; last poke #" + _lastPoke + "; " + wait + "; answer files " + AnswerFiles + ", orders " + OrdersReceived
                   + " (" + Applied + " applied, " + Checked + " checked, " + Refused + " refused)";
        }

        // ---------- numbering ----------

        /// <summary>
        /// The next poke number. Numbers carry on from the channel's pokes.log and this campaign's own poke file, so a
        /// number never comes back after a load. Worker thread.
        /// </summary>
        public int NextNumber(string campaignPokesFile)
        {
            EnsureBefore(campaignPokesFile);
            if (_next <= _before) _next = _before + 1;
            int n = _next++;
            if (campaignPokesFile != null) Files.Overwrite(AgentChannel.NumberFile(campaignPokesFile), n.ToString(Text.Inv));
            return n;
        }

        private void EnsureBefore(string campaignPokesFile)
        {
            if (_before >= 0) return;
            string realm = Realm ?? Brain.Latest?.Watched;
            if (campaignPokesFile == null && realm != null) campaignPokesFile = Path.Combine(Brain.Dir, "pokes_" + realm + ".jsonl");
            _before = Math.Max(AgentChannel.LastNumberIn(Path.Combine(Dir, "pokes.log"), "#", true), AgentChannel.HighestIn(campaignPokesFile));
            _next = _before + 1;
            if (_before > 0) Diag.Event("agent", "poke numbers carry on", new { realm = Realm, from = _before + 1 });
        }

        // ---------- what the agent reads ----------

        /// <summary>The newest poke the agent answered this session (-1 before its first answer).</summary>
        public int LastAnswered => _lastAnswered;

        /// <summary>
        /// What the agent has not heard since its last answer: the reasons of the pokes that came after the one it
        /// answered (the current poke's own reasons are in WHY YOU ARE ASKED NOW), and what became of its orders.
        /// </summary>
        public string SinceLastAnswer(int current)
        {
            if (_lastAnswered < 0 || !Pokes.TryGetValue(_lastAnswered, out PokeInfo last))
                return "- This is the first poke you answer in this game session; the report below has everything.";
            var sb = new StringBuilder();
            var seen = new HashSet<string>();
            int shown = 0, more = 0;
            foreach (KeyValuePair<int, List<string>> kv in PokeReasons)
            {
                if (kv.Key <= _lastAnswered || kv.Key >= current) continue;
                foreach (string r in kv.Value)
                {
                    if (!seen.Add(r)) continue;
                    if (shown >= 40) { more++; continue; }
                    sb.AppendLine("- (#" + kv.Key + ") " + r);
                    shown++;
                }
            }
            if (more > 0) sb.AppendLine("- ... and " + more + " more.");
            if (shown == 0) sb.AppendLine("- No other pokes came since your last answer (to #" + _lastAnswered + ").");
            // the tools' call-backs are already among the reasons above
            List<string> lines = Lines.Where(l => l.Key >= last.Wall && !l.Value.StartsWith("tool: ", StringComparison.Ordinal)).Select(l => l.Value).ToList();
            if (lines.Count > 0)
            {
                sb.AppendLine("What became of your orders since then:");
                foreach (string l in lines.Skip(Math.Max(0, lines.Count - 30))) sb.AppendLine("- " + l);
                if (lines.Count > 30) sb.AppendLine("- (" + (lines.Count - 30) + " earlier lines are in receipts.log)");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// A poke for the agent: poke.json (for the war room: the number, the in-game hour, the campaign, the realm and
        /// this load, by which it tells a reloaded save), then poke.txt, then its line in pokes.log, each swapped in whole.
        /// </summary>
        public void OnPoke(int n, double hours, List<Trigger> triggers, string report, object meta = null, string stale = null)
        {
            _lastPoke = n;
            Pokes[n] = new PokeInfo { Wall = DateTime.Now, Hours = hours };
            PokeReasons.Add(new KeyValuePair<int, List<string>>(n, triggers.Select(t => t.Text).ToList()));
            if (PokeReasons.Count > 300) PokeReasons.RemoveRange(0, PokeReasons.Count - 300);
            if (_waitingSince == DateTime.MinValue) { _waitingSince = DateTime.Now; _waitingFor = n; }
            Paths.Ensure(OrdersDir);
            Files.Overwrite(Path.Combine(Dir, "poke.json"), Json.Line(meta ?? new { Number = n, Hours = hours, Date = Text.When(hours) }));
            string head = "POKE #" + n + ", " + Text.When(hours) + "\n"
                          + "Answer with one orders file in orders\\ that says \"poke\": " + n + ".\n"
                          + "Your orders right now: " + ModeText() + "\n" + (stale ?? "") + "\n";
            Files.Overwrite(Path.Combine(Dir, "poke.txt"), head + report);
            string first = triggers.Count > 0 ? triggers[0].Text : "";
            Files.Append(Path.Combine(Dir, "pokes.log"), "#" + n + " | " + Text.When(hours) + " | " + first + (triggers.Count > 1 ? " (and " + (triggers.Count - 1) + " more)" : ""));
            Files.FlushAll();
            RememberIds(report);
        }

        /// <summary>The situation report (report.txt): the lieutenant's every in-game hour, a realm agent's when its war room asks for its council (0.12.0).</summary>
        public void OnHour(double hours, string report)
        {
            string head = "SITUATION AT " + Text.When(hours) + " (" + (IsLieutenant ? "refreshed every in-game hour" : "written for your council of war")
                          + (_lastPoke >= 0 ? "; the latest poke is #" + _lastPoke : "") + ")\n"
                          + "Your orders right now: " + ModeText() + "\n\n";
            Files.Overwrite(Path.Combine(Dir, "report.txt"), head + report);
            RememberIds(report);
        }

        /// <summary>The briefing for a council of war (Facts.Council), written when the war room asks as a council sits (0.12.0; every in-game hour before).</summary>
        public void WriteCouncil(string text)
        {
            Files.Overwrite(Path.Combine(Dir, "council.txt"), text);
            RememberIds(text);
        }

        /// <summary>Every id the agent is shown, so an order naming any other id can be flagged.</summary>
        private void RememberIds(string report)
        {
            List<string> fresh = null;
            foreach (Match m in Brackets.Matches(report ?? ""))
            {
                string id = m.Groups[1].Value;
                if (id == "brackets") continue;   // the report's own "Ids in [brackets]" line
                if (Shown.Add(id)) (fresh ?? (fresh = new List<string>())).Add(id);
            }
            if (fresh == null || _shownFile == null) return;
            try { System.IO.File.AppendAllLines(_shownFile, fresh); }
            catch (Exception e) { Diag.Swallowed("keeping the ids shown to the agent", e); }
        }

        /// <summary>Look-up files written, and left alone because nothing in them changed.</summary>
        internal sealed class LookupCount
        {
            public int Written, Unchanged;
        }

        /// <summary>
        /// The look-up files, one per id, for details on request; files no longer written are removed. Since 0.12.0 only the files
        /// whose text changed are written, and in place: written beside the old one and swapped in, 216 of them took 1.2 s with the
        /// virus scanner holding each new file (run 20261025-1417-C2), and a look-up is one small file the agent reads whole.
        /// </summary>
        public LookupCount WriteLookups(Dictionary<string, string> files)
        {
            if (!On || files == null) return null;
            string dir = Paths.Ensure(Path.Combine(Dir, "lookups"));
            var count = new LookupCount();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in files)
            {
                string name = Safe(kv.Key) + ".txt";
                keep.Add(name);
                if (Files.WriteIfChanged(Path.Combine(dir, name), kv.Value)) count.Written++;
                else count.Unchanged++;
            }
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.txt"))
                    if (!keep.Contains(Path.GetFileName(f)))
                    {
                        System.IO.File.Delete(f);
                        Files.Forget(f);
                    }
            }
            catch (Exception e) { Diag.Swallowed("tidying the look-up files", e); }
            Diag.Count("files:look-ups written", count.Written);
            Diag.Count("files:look-ups unchanged", count.Unchanged);
            return count;
        }

        /// <summary>Each tool's whole definition as tools\(name).json (0.13, A1): ours and the realm's own kept tools; files no longer written are removed.</summary>
        public void WriteDefinitions()
        {
            string dir = Paths.Ensure(Path.Combine(Dir, "tools"));
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Put(string name, string json)
            {
                if (json == null) return;
                string file = Safe(name) + ".json";
                keep.Add(file);
                Files.WriteIfChanged(Path.Combine(dir, file), json);
            }
            foreach (Tools.ToolDef d in Tools.Library.All.Values) Put(d.Name, d.Json);
            if (Realm != null)
                foreach (KeyValuePair<string, string> kv in Tools.OwnLibrary.Definitions(Realm)) Put(kv.Key, kv.Value);
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.json"))
                    if (!keep.Contains(Path.GetFileName(f)))
                    {
                        System.IO.File.Delete(f);
                        Files.Forget(f);
                    }
            }
            catch (Exception e) { Diag.Swallowed("tidying the tools' definitions", e); }
        }

        private static string Safe(string id)
        {
            var sb = new StringBuilder(id.Length);
            foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        // ---------- what the agent writes ----------

        public void EnsureWatcher()
        {
            string dir = OrdersDir;
            if (_watcher != null && string.Equals(_watchedDir, dir, StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                _watcher?.Dispose();
                Paths.Ensure(dir);
                var w = new FileSystemWatcher(dir, "*.json")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    InternalBufferSize = 64 * 1024
                };
                w.Created += (s, e) => RequestScan();
                w.Changed += (s, e) => RequestScan();
                w.Renamed += (s, e) => RequestScan();
                w.Error += (s, e) =>
                {
                    Diag.Swallowed("watching the agent's orders folder", e.GetException());
                    _watcher = null;
                    RequestScan();
                };
                w.EnableRaisingEvents = true;
                _watcher = w;
                _watchedDir = dir;
                Diag.Event("agent", "watching the orders folder", new { realm = Realm, dir });
            }
            catch (Exception e)
            {
                _watcher = null;
                Diag.Swallowed("watching the agent's orders folder", e);
            }
        }

        /// <summary>Any thread: asks the worker to look at the orders folder (once, however many times it is asked meanwhile).</summary>
        public void RequestScan()
        {
            if (!On || !Live.CampaignRunning) return;
            if (Interlocked.Exchange(ref _scanQueued, 1) == 1) return;
            Background.PostFirst("agent orders folder", () =>
            {
                try { Scan(); }
                finally { Interlocked.Exchange(ref _scanQueued, 0); }
            });
        }

        public void Scan()
        {
            LastScan = System.Diagnostics.Stopwatch.GetTimestamp();
            _scanAlarmed = false;
            if (!Directory.Exists(OrdersDir)) { Paths.Ensure(OrdersDir); return; }
            if (!_leftoversChecked) SetAsideLeftovers();
            bool fresh = false;
            foreach (string f in Directory.GetFiles(OrdersDir, "*.json").OrderBy(System.IO.File.GetLastWriteTimeUtc))
            {
                // the war room asks for its council's briefing (0.12.0): written now from the newest hour (it moves the request in whole)
                if (Path.GetFileName(f).StartsWith(BriefingRequest, StringComparison.OrdinalIgnoreCase))
                {
                    try { System.IO.File.Delete(f); }
                    catch (Exception e) { Diag.Swallowed("removing the war room's request for a briefing", e); }
                    try { Brain.Briefing(_agent); }
                    catch (Exception e) { Diag.Error("writing the council's briefing", e); }
                    continue;
                }
                // the war room's "check a tool" asks for a try on the newest hour (0.13): answered at once (it moves the request in whole)
                if (Path.GetFileName(f).StartsWith(ToolCheckRequest, StringComparison.OrdinalIgnoreCase))
                {
                    AnswerToolCheck(f);
                    continue;
                }
                if ((DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(f)).TotalMilliseconds < 300) { fresh = true; continue; }   // still being written
                // the lieutenant's answer to the player's words (0.10): its own path, through the game's thread
                if (Path.GetFileName(f).StartsWith("chat-", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Lieutenant.ReadAnswer(f, Realm, out string chatError))
                    {
                        Unreadable.TryGetValue(f, out int tries);
                        if (++tries < 5) { Unreadable[f] = tries; fresh = true; continue; }
                        Diag.Alarm("lieutenant_unreadable", "The lieutenant's answer " + Path.GetFileName(f) + " could not be read (" + chatError + ").");
                    }
                    Unreadable.Remove(f);
                    MoveDone(f);
                    continue;
                }
                if (!AgentChannel.Parse(f, out int poke, out List<AgentChannel.AgentOrder> orders, out AgentChannel.Answer ans, out string error))
                {
                    Unreadable.TryGetValue(f, out int tries);
                    if (++tries < 5) { Unreadable[f] = tries; fresh = true; continue; }
                    Unreadable.Remove(f);
                    MoveDone(f);
                    Diag.Count("agent:unreadable");
                    Diag.Event("answer", "unreadable", new { realm = Realm, file = Path.GetFileName(f), error });
                    Diag.Alarm("agent_unreadable", Whose + "An answer file from the agent could not be read: " + Path.GetFileName(f) + " (" + error + ").");
                    Line("file " + Path.GetFileName(f) + ": UNREADABLE, nothing in it was carried out (" + error + ")");
                    continue;
                }
                Unreadable.Remove(f);
                foreach (AgentChannel.AgentOrder o in orders) o.Realm = Realm;
                Episodes.Answer(Realm, poke, Path.GetFileName(f), ans, Diag.Hours);
                Answered(f, poke, orders, ans);
                MoveDone(f);
            }
            if (fresh)
            {
                if (_recheck == null) _recheck = new Timer(_ => RequestScan(), null, Timeout.Infinite, Timeout.Infinite);
                _recheck.Change(400, Timeout.Infinite);
            }
        }

        /// <summary>One answer file: timed against its poke, checked, and handed to the game's thread (or refused here).</summary>
        private void Answered(string f, int poke, List<AgentChannel.AgentOrder> orders, AgentChannel.Answer ans = null)
        {
            string name = Path.GetFileName(f);
            AnswerFiles++;
            Diag.Count("agent:files");
            DateTime written = SafeCreation(f);
            DateTime read = DateTime.Now;
            Pokes.TryGetValue(poke, out PokeInfo pi);
            double afterPoke = pi != null ? (written - pi.Wall).TotalSeconds : -1;
            int newer = poke >= 0 && _lastPoke > poke ? _lastPoke - poke : 0;
            bool duplicate = pi != null && pi.Answers > 0;
            if (pi != null) pi.Answers++;
            bool earlier = poke >= 0 && !Pokes.ContainsKey(poke);
            if (afterPoke >= 0) Diag.Sample("agent:answer seconds", afterPoke);
            if (newer > 0 && !earlier) Diag.Count("agent:outdated");
            if (duplicate) Diag.Count("agent:duplicate");
            if (poke < 0) Diag.Count("agent:no poke number");
            Diag.Event("answer", "file", new
            {
                realm = Realm, file = name, poke, orders = orders.Count, written = written.ToString("HH:mm:ss.fff"), read_delay_s = Math.Round((read - written).TotalSeconds, 2),
                after_poke_s = afterPoke >= 0 ? Math.Round(afterPoke, 1) : (double?)null, newer_pokes_out = newer, duplicate, clock_running = Watchdog.ClockRunning
            });
            if (_silenceAlarmed) Log.Line(Whose + "The agent answered again, " + (int)((DateTime.Now - _waitingSince).TotalMinutes) + " minutes after poke #" + _waitingFor + ".");
            _silenceAlarmed = false;
            _waitingSince = DateTime.MinValue;
            _waitingFor = -1;
            if (poke >= 0 && pi != null && poke > _lastAnswered) _lastAnswered = poke;

            EnsureBefore(null);
            if (poke >= 0 && poke <= _before && !Pokes.ContainsKey(poke))
            {
                Diag.Count("agent:before load", orders.Count);
                foreach (AgentChannel.AgentOrder o in orders)
                    Receipt(o, "refused", "it answers poke #" + poke + ", from before the game was loaded again; the situation has changed, so nothing in it was carried out", Diag.Hours);
                if (orders.Count == 0 || ans?.Tools > 0) Line("file " + name + " (poke #" + poke + "): answers a poke from before the game was loaded again; nothing changes" + (ans?.Tools > 0 ? ", its tools included" : ""));
                return;
            }
            // the answer's own tools first (0.13, docs/PLAN-0.13.md A8): checked, then kept, changed, retired or used once, so its orders can name them
            var refusedTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Tools.ToolDef> theirs = ans?.Tools > 0 ? TakeTools(name, poke, ans, refusedTools) : null;
            // its diplomatic acts (0.13, B2): checked here against the realm's screens as last copied, carried out on the game's thread
            if (ans?.Diplomacy > 0) TakeDiplomacy(name, poke, ans);
            // its letters to allies (0.13, B5): checked and delivered here
            if (ans?.Letters > 0) TakeLetters(name, poke, ans);
            if (orders.Count == 0) { if (theirs == null && !(ans?.Diplomacy > 0) && !(ans?.Letters > 0)) Line("file " + name + " (poke #" + poke + "): no orders in it; nothing changes"); return; }
            foreach (AgentChannel.AgentOrder o in orders) CheckIds(o);
            foreach (AgentChannel.AgentOrder o in orders) Attach(o, theirs);
            List<AgentChannel.AgentOrder> refused = orders.Where(o => refusedTools.ContainsKey((o.Do ?? "").Trim())).ToList();
            foreach (AgentChannel.AgentOrder o in refused)
                Receipt(o, "refused", "its tool " + o.Do.Trim().ToLowerInvariant() + " was refused (" + refusedTools[o.Do.Trim()] + ")", Diag.Hours);
            orders = orders.Except(refused).ToList();
            if (orders.Count == 0) return;
            if (!Watchdog.ClockRunning)
            {
                Diag.Count("agent:waited for clock", orders.Count);
                Line("poke #" + poke + ", " + name + ": RECEIVED " + orders.Count + " order" + (orders.Count == 1 ? "" : "s") + ". The game's clock is stopped right now "
                     + "(paused, in a menu or in a battle), so they will be checked and carried out when it runs again, and answered here then.");
            }
            foreach (AgentChannel.AgentOrder o in orders)
            {
                OrdersReceived++;
                Diag.Count("agent:orders");
                AgentChannel.AgentOrder order = o;
                Background.ToMain.Enqueue(() => Orders.FromAgent(order));
            }
        }

        /// <summary>
        /// An answer's tools (0.13, A8), each checked and then kept, changed, retired or used once in the realm's library (in shadow mode
        /// checked only); each answered in receipts.log. Returns the usable ones by name for this answer's orders; refused gets the others.
        /// </summary>
        private Dictionary<string, Tools.ToolDef> TakeTools(string file, int poke, AgentChannel.Answer ans, Dictionary<string, string> refused)
        {
            var written = new Dictionary<string, Tools.ToolDef>(StringComparer.OrdinalIgnoreCase);
            Tools.OwnLibrary lib = Tools.OwnLibrary.For(Realm);
            double hours = Diag.Hours;
            bool changed = false;
            int i = 0;
            foreach (JToken t in ans.ToolList)
            {
                i++;
                Tools.OwnLibrary.Taken r;
                try
                {
                    r = lib != null ? lib.Take(t as JObject, poke, ans.Kind, file, hours, Mode == "shadow")
                                    : new Tools.OwnLibrary.Taken { Name = (string)(t as JObject)?["name"], Status = "refused", Reason = "your realm has no library: no agent folder serves it" };
                }
                catch (Exception e)
                {
                    Diag.Error("taking an agent's tool", e);
                    r = new Tools.OwnLibrary.Taken { Name = (string)(t as JObject)?["name"], Status = "refused", Reason = "it could not be read (" + e.Message + ")" };
                }
                ToolReceipt(poke, file, i, r, hours);
                if (r.Name == null) continue;
                if (r.Usable) written[r.Name] = r.Def;
                else if (r.Status == "refused") refused[r.Name] = r.Reason;
                if (r.Status == "kept" || r.Status == "changed" || r.Status == "retired") changed = true;
            }
            if (changed) AgentChannel.WriteLibrary(Realm);
            return written;
        }

        /// <summary>
        /// An answer's diplomatic acts (0.13, B2): each checked against the realm's screens as last copied (refused here with its reason, or
        /// checked only in shadow mode), else handed to the game's thread, which checks the game's own conditions again and carries it out.
        /// </summary>
        private void TakeDiplomacy(string file, int poke, AgentChannel.Answer ans)
        {
            DiploView v = Realm != null && _agent.Latest?.Diplomacy != null && _agent.Latest.Diplomacy.TryGetValue(Realm, out DiploView dv) ? dv : null;
            int i = 0;
            foreach (JToken t in ans.DiplomacyList)
            {
                i++;
                DiploAct a = t is JObject o ? DiploAct.From(o, poke, file, i) : new DiploAct { File = file, Index = i, Poke = poke, Act = "?" };
                a.Realm = Realm;
                string why = t is JObject ? DiploRules.Check(a, v, out _) : "write each act as an object, for example {\"act\": \"propose_peace\", \"with\": \"...\"}";
                if (why != null) { DiploReceipt(a, "refused", why, Diag.Hours); continue; }
                if (Mode == "shadow") { DiploReceipt(a, "checked", "valid: " + a + " (shadow mode: not carried out)", Diag.Hours); continue; }
                DiploAct act = a;
                Background.ToMain.Enqueue(() => Diplomacy.FromAgent(act));
            }
        }

        /// <summary>An answer's letters (0.13, B5): each to an ally whose ruler reads letters, delivered with its joint plan if it carries one (in shadow mode checked only).</summary>
        private void TakeLetters(string file, int poke, AgentChannel.Answer ans)
        {
            int i = 0;
            foreach (JToken t in ans.LetterList)
            {
                i++;
                var l = t as JObject;
                string to = (string)l?["to"], status, reason;
                if (Mode == "shadow")
                {
                    reason = l == null ? "write each letter as an object" : Allies.CanWrite(_agent.Latest, Realm, to);
                    status = reason == null ? "checked" : "refused";
                    if (reason == null) reason = "valid (shadow mode: not sent)";
                }
                else
                {
                    try { reason = Allies.Send(_agent.Latest, Realm, l, "agent", poke, out status); }
                    catch (Exception e) { status = "refused"; reason = "it could not be sent (" + e.Message + ")"; Diag.Error("sending an agent's letter", e); }
                }
                LetterReceipt(poke, file, i, to, status, reason, Diag.Hours);
            }
        }

        /// <summary>What became of one letter, in receipts.log, the orders' record and the episode index.</summary>
        public void LetterReceipt(int poke, string file, int index, string to, string status, string reason, double hours)
        {
            Diag.Count("agent:letter " + status);
            Line("poke #" + poke + ", " + file + " letter " + index + " (to " + (to ?? "?") + "): " + status.ToUpperInvariant() + ". " + reason.TrimEnd('.') + ".");
            Brain.WriteOrder(new { Type = "agent_letter", Realm, Poke = poke, File = file, Index = index, To = to, Status = status, Reason = reason, Hours = hours, Mode, Run = RunTags.Id, Arm = RunTags.Arm });
            Episodes.Letter(Realm, poke, file, index, to, status, reason, hours);
            if (status == "sent") _agent.Chronicle.Line(hours, "letter", "You wrote to " + to + ": " + reason.TrimEnd('.') + ".");
        }

        /// <summary>What became of one diplomatic act, in receipts.log, the orders' record, the episode index, the diplomacy book and the chronicle.</summary>
        public void DiploReceipt(DiploAct a, string status, string reason, double hours)
        {
            Diag.Count("agent:diplomacy " + status);
            Line("poke #" + a.Poke + ", " + a.File + " diplomacy " + a.Index + " (" + a + "): " + status.ToUpperInvariant() + ". " + reason.TrimEnd('.') + ".");
            Diag.Event("answer", "diplomacy", new { realm = Realm, poke = a.Poke, file = a.File, index = a.Index, act = a.Act, with = a.With, status, reason });
            Brain.WriteOrder(new
            {
                Type = "agent_diplomacy", Realm, a.Poke, a.File, a.Index, a.Act, a.With, a.Against, a.Clan, a.Policy, a.Vote, a.Option, a.Offer, a.Tribute, a.Days, a.Why,
                Status = status, Reason = reason, Hours = hours, Mode, Run = RunTags.Id, Arm = RunTags.Arm
            });
            Episodes.Diplomacy(Realm, a, status, reason, hours);
            DiploBook.Write(new { Type = "act", Realm, Run = RunTags.Id, Arm = RunTags.Arm, Hours = hours, a.Poke, a.File, a.Index, a.Act, a.With, a.Against, a.Clan, a.Policy, a.Vote, a.Option, a.Offer, a.Tribute, a.Days, a.Overrule, a.Why, Status = status, Reason = reason });
            string why = string.IsNullOrWhiteSpace(a.Why) ? "" : " Why: " + a.Why.Trim().TrimEnd('.') + ".";
            if (status == "applied") _agent.Chronicle.Line(hours, "diplomacy", "Your diplomacy: " + reason.TrimEnd('.') + "." + why);
            else if (status == "refused") _agent.Chronicle.Line(hours, "refused", "Your diplomatic act (" + a + ") was refused: " + reason.TrimEnd('.') + "." + why);
        }

        /// <summary>An order naming a tool of its answer runs that very tool, else the realm's kept tool of that name; a next order (then) naming one carries its fingerprint.</summary>
        internal void Attach(AgentChannel.AgentOrder o, Dictionary<string, Tools.ToolDef> written)
        {
            string verb = (o.Do ?? "").Trim().ToLowerInvariant();
            if (written != null && written.TryGetValue(verb, out Tools.ToolDef d)) o.Tool = d;
            else if (Tools.Library.Get(verb) == null) o.Tool = Tools.OwnLibrary.Find(Realm, verb);
            for (JObject then = o.Then; then != null; then = then["then"] as JObject)
            {
                string next = ((string)then["do"] ?? "").Trim().ToLowerInvariant();
                Tools.ToolDef td = written != null && written.TryGetValue(next, out Tools.ToolDef w) ? w : Tools.Library.Get(next) == null ? Tools.OwnLibrary.Find(Realm, next) : null;
                if (td?.Hash != null) then["tool_hash"] = td.Hash;
            }
        }

        /// <summary>What became of one tool of an answer, in receipts.log, the orders' record, the episode index and the chronicle.</summary>
        public void ToolReceipt(int poke, string file, int index, Tools.OwnLibrary.Taken r, double hours)
        {
            Diag.Count("agent:tool " + r.Status);
            Line("poke #" + poke + ", " + file + " tool " + index + " (" + (r.Name ?? "?") + "): " + r.Status.Replace('_', '-').ToUpperInvariant() + ". " + r.Reason + ".");
            Diag.Event("answer", "tool", new { realm = Realm, poke, file, index, name = r.Name, version = r.Version, status = r.Status, reason = r.Reason, hash = r.Hash });
            Brain.WriteOrder(new
            {
                Type = "agent_tool", Realm, Poke = poke, File = file, Index = index, r.Name, Version = r.Version > 0 ? r.Version : (int?)null, r.Status, r.Reason, r.Hash,
                Hours = hours, Mode, Run = RunTags.Id, Arm = RunTags.Arm
            });
            Episodes.Tool(Realm, poke, file, index, r.Name, r.Version, r.Status, r.Reason, r.Hash, hours);
            string about = r.Def?.About != null ? " (" + r.Def.About.Trim().TrimEnd('.') + ")" : "";
            if (r.Status == "kept" || r.Status == "changed") _agent.Chronicle.Line(hours, "tool", "You kept a tool of your own: " + r.Name + " v" + r.Version + about + ".");
            else if (r.Status == "retired") _agent.Chronicle.Line(hours, "tool", "You retired your tool " + r.Name + " (v" + r.Version + ").");
        }

        /// <summary>The war room's "check a tool" asks for a try on the realm's newest hour (0.13, A4): orders\toolcheck-request-(id).json.</summary>
        public const string ToolCheckRequest = "toolcheck-request";

        /// <summary>A try asked for: answered from the realm's newest hour into toolcheck-(id).txt beside the channel's other files (the war room waits for it).</summary>
        private void AnswerToolCheck(string f)
        {
            string text = null;
            try
            {
                text = System.IO.File.ReadAllText(f);
                System.IO.File.Delete(f);
            }
            catch (Exception e) { Diag.Swallowed("reading the war room's request to try a tool", e); }
            string id = Path.GetFileNameWithoutExtension(f).Substring(ToolCheckRequest.Length).Trim('-');
            string answer;
            try { answer = text == null ? "The request could not be read." : Tools.ToolTry.Answer(_agent, text); }
            catch (Exception e)
            {
                Diag.Error("trying a tool for the war room", e);
                answer = "The try could not be made (" + e.Message + ").";
            }
            Files.Overwrite(Path.Combine(Dir, "toolcheck-" + Safe(id) + ".txt"), answer);
            Diag.Count("agent:tool tries");
        }

        /// <summary>An id the agent was never shown: made up, or learned outside its folder (the independence check).</summary>
        private void CheckIds(AgentChannel.AgentOrder o)
        {
            foreach (string v in new[] { o.Lord, o.Target, o.At }.Concat(o.Lords).Concat(o.Places))
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                string s = v.Trim().Trim('[', ']');
                if (s.IndexOf(' ') >= 0 || s.IndexOf('_') < 0 || Tools.Spots.Is(s)) continue;   // a name, not an id; or a spot on the map (geography, never hidden)
                if (Shown.Contains(s)) continue;
                Diag.Count("agent:unknown id");
                Diag.Event("answer", "unknown id", new { realm = Realm, id = s, file = o.File, index = o.Index });
                Diag.Alarm("agent_unknown_id", Whose + "The agent named '" + s + "', which never appeared in a report it was given. It either made the id up or learned it outside its folder.",
                           false, new { id = s });
            }
        }

        private void SetAsideLeftovers()
        {
            _leftoversChecked = true;
            try
            {
                foreach (string f in Directory.GetFiles(OrdersDir, "*.json"))
                {
                    DateTime written = System.IO.File.GetLastWriteTime(f);
                    if (written >= _sessionStart) continue;
                    string stale = Paths.Ensure(Path.Combine(OrdersDir, "stale"));
                    string to = Path.Combine(stale, Path.GetFileName(f));
                    if (System.IO.File.Exists(to)) to = Path.Combine(stale, Path.GetFileNameWithoutExtension(f) + "-" + DateTime.Now.ToString("HHmmssfff") + ".json");
                    System.IO.File.Move(f, to);
                    Diag.Count("agent:set aside");
                    Diag.Event("answer", "set aside", new { realm = Realm, file = Path.GetFileName(f), written = written.ToString("yyyy-MM-dd HH:mm:ss") });
                    Line("file " + Path.GetFileName(f) + ": SET ASIDE unread (in orders\\stale). It was written at " + written.ToString("HH:mm:ss")
                         + ", before this game session began, so it answers a situation that no longer exists.");
                }
            }
            catch (Exception e) { Diag.Swallowed("setting aside old answer files", e); }
        }

        private static DateTime SafeCreation(string f)
        {
            try { return System.IO.File.GetCreationTime(f); }
            catch (Exception e) { Diag.Swallowed("reading a file's time", e); return DateTime.Now; }
        }

        private void MoveDone(string f)
        {
            try
            {
                string done = Paths.Ensure(Path.Combine(OrdersDir, "done"));
                string to = Path.Combine(done, Path.GetFileName(f));
                if (System.IO.File.Exists(to)) to = Path.Combine(done, Path.GetFileNameWithoutExtension(f) + "-" + DateTime.Now.ToString("HHmmssfff") + ".json");
                System.IO.File.Move(f, to);
            }
            catch (Exception e)
            {
                Diag.Error("moving an answer file to done", e);
                Diag.Alarm("answer_not_moved", Whose + "An answer file could not be moved to done and may be read again: " + Path.GetFileName(f) + " (" + e.Message + ").");
            }
        }

        /// <summary>The answer to one order, with how long it took after its poke and whether it waited for the clock.</summary>
        public void Receipt(AgentChannel.AgentOrder a, string status, string reason, double hours)
        {
            if (status == "applied") { Applied++; Diag.Count("agent:applied"); }
            else if (status == "checked") { Checked++; Diag.Count("agent:checked"); }
            else { Refused++; Diag.Count("agent:refused"); }
            string delay = "";
            double seconds = -1, gameHours = -1;
            if (a.Poke >= 0 && Pokes.TryGetValue(a.Poke, out PokeInfo t))
            {
                seconds = (a.Received - t.Wall).TotalSeconds;
                gameHours = hours - t.Hours;
                delay = " Answered " + seconds.ToString("0", Text.Inv) + " s after poke #" + a.Poke + " (" + gameHours.ToString("0.0", Text.Inv) + " in-game hours).";
            }
            else if (a.Poke < 0) delay = " (The file gave no poke number, so the answer time is unknown.)";
            double waited = (DateTime.Now - a.Received).TotalSeconds;
            if (waited >= 2)
            {
                Diag.Sample("agent:wait seconds", waited);
                delay += " Carried out " + waited.ToString("0", Text.Inv) + " s after it was read, when the game's clock ran again.";
            }
            string what = (a.Lord ?? "?") + " " + (a.Do ?? "?") + (string.IsNullOrEmpty(a.Target) ? "" : " " + a.Target);
            Line("poke #" + a.Poke + ", " + a.File + " order " + a.Index + " (" + what + "): " + status.ToUpperInvariant() + ". " + reason + "." + delay);
            // the chronicle keeps what was ordered, and what was refused and why (shadow mode's checks are not history)
            string why = string.IsNullOrWhiteSpace(a.Why) ? "" : " Why: " + a.Why.Trim().TrimEnd('.') + ".";
            ChronicleBook book = _agent.Chronicle;
            if (status == "applied") book.Line(hours, "order", "You ordered: " + reason.TrimEnd('.') + "." + why, a.At, a.Lord);
            else if (status == "refused" && !reason.StartsWith("it answers poke #", StringComparison.Ordinal))
                book.Line(hours, "refused", "Your order (" + (a.Lord ?? "?") + " " + (a.Do ?? "?") + (string.IsNullOrEmpty(a.At ?? a.Target) ? "" : " " + (a.At ?? a.Target))
                                            + ") was refused: " + reason.TrimEnd('.') + "." + why, a.At, a.Lord);
            Diag.Event("answer", "receipt", new { realm = Realm, poke = a.Poke, file = a.File, index = a.Index, lord = a.Lord, @do = a.Do, target = a.Target, status, reason,
                                                  answer_s = seconds >= 0 ? Math.Round(seconds, 1) : (double?)null, waited_s = Math.Round(waited, 1) });
            Brain.WriteOrder(new
            {
                Type = "agent_receipt", Realm, a.Poke, a.File, a.Index, a.Lord, a.Do, a.Target, a.Why, Status = status, Reason = reason,
                Hours = hours, AnswerSeconds = seconds >= 0 ? seconds : (double?)null, AnswerGameHours = gameHours >= 0 ? gameHours : (double?)null,
                WaitedSeconds = waited >= 2 ? waited : (double?)null, Mode, Run = RunTags.Id, Arm = RunTags.Arm, ToolRun = a.ToolRun > 0 ? a.ToolRun : (int?)null
            });
            Episodes.Receipt(Realm, a, status, reason, hours);
        }

        /// <summary>What became of an agent's order later (arrived, given again, ended).</summary>
        public void Event(string text) => Line(text);

        private void Line(string text)
        {
            Lines.Add(new KeyValuePair<DateTime, string>(DateTime.Now, text));
            if (Lines.Count > 400) Lines.RemoveRange(0, Lines.Count - 400);
            Files.Append(Path.Combine(Dir, "receipts.log"), DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            Files.FlushAll();
        }

        // ---------- the watchdog's checks (its own thread; reads plain fields only) ----------

        public void CheckAlive()
        {
            if (!On || !Live.CampaignRunning) return;
            if (LastScan != 0 && Watchdog.Seconds(LastScan) > 30 && !_scanAlarmed)
            {
                _scanAlarmed = true;
                Diag.Alarm("scan_stopped", Whose + "The agent's orders folder has not been checked for " + (int)Watchdog.Seconds(LastScan) + " s; its answers would wait.");
            }
            DateTime since = _waitingSince;
            if (since == DateTime.MinValue || _silenceAlarmed) return;
            double minutes = (DateTime.Now - since).TotalMinutes;
            if (minutes < Settings.AnswerAlarmMinutes) return;
            _silenceAlarmed = true;
            Diag.Count("agent:silences");
            Diag.Alarm("agent_silent", Whose + "No answer from the agent for " + (int)minutes + " minutes, since poke #" + _waitingFor + ". Is the agent chat still watching for pokes?", true);
        }
    }
}
