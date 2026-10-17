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
    /// The bridge writes, in Settings.AgentFolder:
    /// - pokes.log: one line per poke ("#12 | Summer 3, 1085, 14:00 | Enemy army in view: ..."). The agent watches it.
    ///   Poke numbers carry on across loads, so a number never means two different pokes.
    /// - poke.txt: the latest poke, written (swapped in whole) before its line appears in pokes.log: which number to
    ///   answer, the mode, and the full report with the reasons for the poke.
    /// - report.txt: the situation now, refreshed every in-game hour.
    /// - receipts.log: one line per order the agent gave (applied, checked in shadow mode, or refused and why), how long
    ///   the answer took after its poke, and later what became of each order. When the game's clock is stopped, a file
    ///   is acknowledged at once as RECEIVED and its orders answered when the clock runs again.
    /// The agent writes each answer as a JSON file into orders\. The folder is watched (Windows tells us when a file
    /// appears; a slow check every 10 s is the safety net). Each file is read once, answered in receipts.log, and moved
    /// to orders\done\. Files written before this game session began are set aside unread in orders\stale\, and an
    /// answer to a poke from before the last load is refused: the situation it answered no longer exists.
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
            public readonly List<string> Lords = new List<string>();
            public readonly Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The order to start when this one finishes (same lord).</summary>
            public JObject Then;
            public DateTime Received;
        }

        /// <summary>The keys of an order that are not settings; any other number in an order is taken as a setting.</summary>
        private static readonly HashSet<string> OrderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lord", "do", "order", "verb", "target", "at", "place", "why", "lords", "settings", "then", "poke"
        };

        private sealed class PokeInfo
        {
            public DateTime Wall;
            public double Hours;
            public int Answers;
        }

        private static Timer _fallback, _recheck;
        private static FileSystemWatcher _watcher;
        private static string _watchedDir;
        private static int _scanQueued;
        private static readonly Dictionary<int, PokeInfo> Pokes = new Dictionary<int, PokeInfo>();
        private static readonly Dictionary<string, int> Unreadable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Brackets = new Regex(@"\[([^\]\s]+)\]", RegexOptions.Compiled);
        private static string _shownFile;
        private static int _lastPoke = -1, _before = -1, _next;
        private static DateTime _sessionStart = DateTime.Now;
        private static bool _leftoversChecked;
        private static DateTime _waitingSince = DateTime.MinValue;
        private static int _waitingFor = -1;
        private static bool _silenceAlarmed, _scanAlarmed;
        /// <summary>The newest poke the agent has answered this session, and what happened since, for the next poke.</summary>
        private static int _lastAnswered = -1;
        private static readonly List<KeyValuePair<int, List<string>>> PokeReasons = new List<KeyValuePair<int, List<string>>>();
        private static readonly List<KeyValuePair<DateTime, string>> Lines = new List<KeyValuePair<DateTime, string>>();

        /// <summary>When the folder was last checked (Stopwatch ticks); the watchdog alarms if this stops.</summary>
        public static long LastScan;
        public static int AnswerFiles, OrdersReceived, Applied, Checked, Refused;

        public static bool On => Settings.Agent == "shadow" || Settings.Agent == "live";
        public static string Dir => Settings.AgentFolder;
        private static string OrdersDir => Path.Combine(Dir, "orders");

        /// <summary>Seconds since the oldest poke still waiting for any answer, for the health row.</summary>
        public static double WaitingSeconds => _waitingSince == DateTime.MinValue ? 0 : (DateTime.Now - _waitingSince).TotalSeconds;

        /// <summary>At session start: the slow safety-net check every 10 s, which also keeps the folder watch in place.</summary>
        public static void Start()
        {
            if (_fallback != null) return;
            _fallback = new Timer(_ =>
            {
                if (!On || !Live.CampaignRunning) return;
                EnsureWatcher();
                RequestScan();
            }, null, 1000, 10000);
            _recheck = new Timer(_ => RequestScan(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>A new campaign session (worker): forget the last one's pokes; ids it was shown are kept per campaign.</summary>
        public static void Reset(string dataDir)
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
            _shownFile = dataDir != null ? Path.Combine(dataDir, "ids_shown.txt") : null;
            try
            {
                if (_shownFile != null && System.IO.File.Exists(_shownFile))
                    foreach (string id in System.IO.File.ReadAllLines(_shownFile)) if (id.Trim().Length > 0) Shown.Add(id.Trim());
            }
            catch (Exception e) { Diag.Swallowed("reading the ids shown to the agent", e); }
        }

        public static string ModeText()
        {
            switch (Settings.Agent)
            {
                case "live":
                    return "LIVE. " + (Text.Plain(Settings.AgentLords) == "all" ? "Every lord of yours obeys your orders."
                           : "Only these lords obey: " + Settings.AgentLords + ". The others are led by the game's own AI, and orders for them are refused.");
                case "shadow":
                    return "SHADOW. Every order is checked against the game and answered in receipts.log, but none is carried out yet.";
                default:
                    return "OFF.";
            }
        }

        /// <summary>One line for the black box and agents.health.</summary>
        public static string StatusLine()
        {
            string wait = _waitingSince == DateTime.MinValue ? "no poke waiting" : "poke #" + _waitingFor + " unanswered for " + (int)WaitingSeconds + " s";
            return Settings.Agent + "; last poke #" + _lastPoke + "; " + wait + "; answer files " + AnswerFiles + ", orders " + OrdersReceived
                   + " (" + Applied + " applied, " + Checked + " checked, " + Refused + " refused)";
        }

        // ---------- numbering ----------

        /// <summary>
        /// The next poke number. Numbers carry on from the channel's pokes.log and this campaign's own poke file, so a
        /// number never comes back after a load. Worker thread.
        /// </summary>
        public static int NextNumber(string campaignPokesFile)
        {
            EnsureBefore(campaignPokesFile);
            if (_next <= _before) _next = _before + 1;
            int n = _next++;
            if (campaignPokesFile != null) Files.Overwrite(NumberFile(campaignPokesFile), n.ToString(Text.Inv));
            return n;
        }

        private static void EnsureBefore(string campaignPokesFile)
        {
            if (_before >= 0) return;
            if (campaignPokesFile == null && Brain.Latest?.Watched != null) campaignPokesFile = Path.Combine(Brain.Dir, "pokes_" + Brain.Latest.Watched + ".jsonl");
            _before = Math.Max(LastNumberIn(Path.Combine(Dir, "pokes.log"), "#", true), HighestIn(campaignPokesFile));
            _next = _before + 1;
            if (_before > 0) Diag.Event("agent", "poke numbers carry on", new { from = _before + 1 });
        }

        private static string NumberFile(string campaignPokesFile)
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
        private static int LastNumberIn(string path, string marker, bool atLineStart)
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

        // ---------- what the agent reads ----------

        /// <summary>The newest poke the agent answered this session (-1 before its first answer).</summary>
        public static int LastAnswered => _lastAnswered;

        /// <summary>
        /// What the agent has not heard since its last answer: the reasons of the pokes that came after the one it
        /// answered (the current poke's own reasons are in WHY YOU ARE ASKED NOW), and what became of its orders.
        /// </summary>
        public static string SinceLastAnswer(int current)
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
        public static void OnPoke(int n, double hours, List<Trigger> triggers, string report, object meta = null)
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
                          + "Your orders right now: " + ModeText() + "\n\n";
            Files.Overwrite(Path.Combine(Dir, "poke.txt"), head + report);
            string first = triggers.Count > 0 ? triggers[0].Text : "";
            Files.Append(Path.Combine(Dir, "pokes.log"), "#" + n + " | " + Text.When(hours) + " | " + first + (triggers.Count > 1 ? " (and " + (triggers.Count - 1) + " more)" : ""));
            Files.FlushAll();
            RememberIds(report);
        }

        public static void OnHour(double hours, string report)
        {
            string head = "SITUATION AT " + Text.When(hours) + " (refreshed every in-game hour"
                          + (_lastPoke >= 0 ? "; the latest poke is #" + _lastPoke : "") + ")\n"
                          + "Your orders right now: " + ModeText() + "\n\n";
            Files.Overwrite(Path.Combine(Dir, "report.txt"), head + report);
            RememberIds(report);
        }

        /// <summary>The briefing for a council of war (Facts.Council), rewritten every in-game hour: the war room reads it for a review.</summary>
        public static void WriteCouncil(string text)
        {
            Files.Overwrite(Path.Combine(Dir, "council.txt"), text);
            RememberIds(text);
        }

        /// <summary>Every id the agent is shown, so an order naming any other id can be flagged.</summary>
        private static void RememberIds(string report)
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

        // ---------- what the agent writes ----------

        private static void EnsureWatcher()
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
                Diag.Event("agent", "watching the orders folder", new { dir });
            }
            catch (Exception e)
            {
                _watcher = null;
                Diag.Swallowed("watching the agent's orders folder", e);
            }
        }

        /// <summary>Any thread: asks the worker to look at the orders folder (once, however many times it is asked meanwhile).</summary>
        public static void RequestScan()
        {
            if (!On || !Live.CampaignRunning) return;
            if (Interlocked.Exchange(ref _scanQueued, 1) == 1) return;
            Background.Post("agent orders folder", () =>
            {
                try { Scan(); }
                finally { Interlocked.Exchange(ref _scanQueued, 0); }
            });
        }

        public static void Scan()
        {
            LastScan = System.Diagnostics.Stopwatch.GetTimestamp();
            _scanAlarmed = false;
            if (!Directory.Exists(OrdersDir)) { Paths.Ensure(OrdersDir); return; }
            if (!_leftoversChecked) SetAsideLeftovers();
            bool fresh = false;
            foreach (string f in Directory.GetFiles(OrdersDir, "*.json").OrderBy(System.IO.File.GetLastWriteTimeUtc))
            {
                if ((DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(f)).TotalMilliseconds < 300) { fresh = true; continue; }   // still being written
                if (!Parse(f, out int poke, out List<AgentOrder> orders, out string error))
                {
                    Unreadable.TryGetValue(f, out int tries);
                    if (++tries < 5) { Unreadable[f] = tries; fresh = true; continue; }
                    Unreadable.Remove(f);
                    MoveDone(f);
                    Diag.Count("agent:unreadable");
                    Diag.Event("answer", "unreadable", new { file = Path.GetFileName(f), error });
                    Diag.Alarm("agent_unreadable", "An answer file from the agent could not be read: " + Path.GetFileName(f) + " (" + error + ").");
                    Line("file " + Path.GetFileName(f) + ": UNREADABLE, nothing in it was carried out (" + error + ")");
                    continue;
                }
                Unreadable.Remove(f);
                Answered(f, poke, orders);
                MoveDone(f);
            }
            if (fresh) _recheck?.Change(400, Timeout.Infinite);
        }

        /// <summary>One answer file: timed against its poke, checked, and handed to the game's thread (or refused here).</summary>
        private static void Answered(string f, int poke, List<AgentOrder> orders)
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
                file = name, poke, orders = orders.Count, written = written.ToString("HH:mm:ss.fff"), read_delay_s = Math.Round((read - written).TotalSeconds, 2),
                after_poke_s = afterPoke >= 0 ? Math.Round(afterPoke, 1) : (double?)null, newer_pokes_out = newer, duplicate, clock_running = Watchdog.ClockRunning
            });
            if (_silenceAlarmed) Log.Line("The agent answered again, " + (int)((DateTime.Now - _waitingSince).TotalMinutes) + " minutes after poke #" + _waitingFor + ".");
            _silenceAlarmed = false;
            _waitingSince = DateTime.MinValue;
            _waitingFor = -1;
            if (poke >= 0 && pi != null && poke > _lastAnswered) _lastAnswered = poke;

            EnsureBefore(null);
            if (poke >= 0 && poke <= _before && !Pokes.ContainsKey(poke))
            {
                Diag.Count("agent:before load", orders.Count);
                foreach (AgentOrder o in orders)
                    Receipt(o, "refused", "it answers poke #" + poke + ", from before the game was loaded again; the situation has changed, so nothing in it was carried out", Diag.Hours);
                if (orders.Count == 0) Line("file " + name + " (poke #" + poke + "): answers a poke from before the game was loaded again; nothing changes");
                return;
            }
            if (orders.Count == 0) { Line("file " + name + " (poke #" + poke + "): no orders in it; nothing changes"); return; }
            foreach (AgentOrder o in orders) CheckIds(o);
            if (!Watchdog.ClockRunning)
            {
                Diag.Count("agent:waited for clock", orders.Count);
                Line("poke #" + poke + ", " + name + ": RECEIVED " + orders.Count + " order" + (orders.Count == 1 ? "" : "s") + ". The game's clock is stopped right now "
                     + "(paused, in a menu or in a battle), so they will be checked and carried out when it runs again, and answered here then.");
            }
            foreach (AgentOrder o in orders)
            {
                OrdersReceived++;
                Diag.Count("agent:orders");
                AgentOrder order = o;
                Background.ToMain.Enqueue(() => Orders.FromAgent(order));
            }
        }

        /// <summary>An id the agent was never shown: made up, or learned outside its folder (the independence check).</summary>
        private static void CheckIds(AgentOrder o)
        {
            foreach (string v in new[] { o.Lord, o.Target, o.At }.Concat(o.Lords))
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                string s = v.Trim().Trim('[', ']');
                if (s.IndexOf(' ') >= 0 || s.IndexOf('_') < 0 || Tools.Spots.Is(s)) continue;   // a name, not an id; or a spot on the map (geography, never hidden)
                if (Shown.Contains(s)) continue;
                Diag.Count("agent:unknown id");
                Diag.Event("answer", "unknown id", new { id = s, file = o.File, index = o.Index });
                Diag.Alarm("agent_unknown_id", "The agent named '" + s + "', which never appeared in a report it was given. It either made the id up or learned it outside its folder.",
                           false, new { id = s });
            }
        }

        private static void SetAsideLeftovers()
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
                    Diag.Event("answer", "set aside", new { file = Path.GetFileName(f), written = written.ToString("yyyy-MM-dd HH:mm:ss") });
                    Line("file " + Path.GetFileName(f) + ": SET ASIDE unread (in orders\\stale). It was written at " + written.ToString("HH:mm:ss")
                         + ", before this game session began, so it answers a situation that no longer exists.");
                }
            }
            catch (Exception e) { Diag.Swallowed("setting aside old answer files", e); }
        }

        /// <summary>
        /// Reads one answer file: {"poke": 12, "orders": [{"lord": ..., "do": ..., "target": ..., "why": ...}], "note": ...}.
        /// A bare list of orders, or a single order, is accepted too.
        /// </summary>
        public static bool Parse(string path, out int poke, out List<AgentOrder> orders, out string error)
        {
            poke = -1;
            orders = new List<AgentOrder>();
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
                Target = Str(o["target"]), At = Str(o["at"] ?? o["place"]), Why = (string)o["why"], Then = o["then"] as JObject
            };
            JToken lords = o["lords"];
            if (lords is JArray arr) a.Lords.AddRange(arr.Select(Str).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            else if (lords != null && lords.Type == JTokenType.String) a.Lords.AddRange(((string)lords).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
            if (o["settings"] is JObject set)
                foreach (JProperty p in set.Properties())
                    if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float) a.Settings[p.Name] = (double)p.Value;
            foreach (JProperty p in o.Properties())
                if (!OrderKeys.Contains(p.Name) && (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)) a.Settings[p.Name] = (double)p.Value;
            return a;
        }

        /// <summary>An id written as a string, a number or ["id"].</summary>
        private static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t is JArray a) return a.Count > 0 ? Str(a[0]) : null;
            return ((string)t)?.Trim().Trim('[', ']');
        }

        private static DateTime SafeCreation(string f)
        {
            try { return System.IO.File.GetCreationTime(f); }
            catch (Exception e) { Diag.Swallowed("reading a file's time", e); return DateTime.Now; }
        }

        private static void MoveDone(string f)
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
                Diag.Alarm("answer_not_moved", "An answer file could not be moved to done and may be read again: " + Path.GetFileName(f) + " (" + e.Message + ").");
            }
        }

        /// <summary>The answer to one order, with how long it took after its poke and whether it waited for the clock.</summary>
        public static void Receipt(AgentOrder a, string status, string reason, double hours)
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
            if (status == "applied") Chronicle.Line(hours, "order", "You ordered: " + reason.TrimEnd('.') + "." + why, a.At, a.Lord);
            else if (status == "refused" && !reason.StartsWith("it answers poke #", StringComparison.Ordinal))
                Chronicle.Line(hours, "refused", "Your order (" + (a.Lord ?? "?") + " " + (a.Do ?? "?") + (string.IsNullOrEmpty(a.At ?? a.Target) ? "" : " " + (a.At ?? a.Target))
                                                 + ") was refused: " + reason.TrimEnd('.') + "." + why, a.At, a.Lord);
            Diag.Event("answer", "receipt", new { poke = a.Poke, file = a.File, index = a.Index, lord = a.Lord, @do = a.Do, target = a.Target, status, reason,
                                                  answer_s = seconds >= 0 ? Math.Round(seconds, 1) : (double?)null, waited_s = Math.Round(waited, 1) });
            Brain.WriteOrder(new
            {
                Type = "agent_receipt", a.Poke, a.File, a.Index, a.Lord, a.Do, a.Target, a.Why, Status = status, Reason = reason,
                Hours = hours, AnswerSeconds = seconds >= 0 ? seconds : (double?)null, AnswerGameHours = gameHours >= 0 ? gameHours : (double?)null,
                WaitedSeconds = waited >= 2 ? waited : (double?)null, Mode = Settings.Agent
            });
        }

        /// <summary>What became of an agent's order later (arrived, given again, ended).</summary>
        public static void Event(string text) => Line(text);

        private static void Line(string text)
        {
            Lines.Add(new KeyValuePair<DateTime, string>(DateTime.Now, text));
            if (Lines.Count > 400) Lines.RemoveRange(0, Lines.Count - 400);
            Files.Append(Path.Combine(Dir, "receipts.log"), DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            Files.FlushAll();
        }

        /// <summary>The library of orders the agent can give, written into the channel once a session (the war room reads it too).</summary>
        public static void WriteLibrary()
        {
            if (!On) return;
            List<string> problems = Tools.Library.Problems();
            if (problems.Count > 0)
                Diag.Alarm("tool_library", "The tool library has " + problems.Count + " problems, so some orders will not work: " + string.Join("; ", problems.Take(3)) + ".", true);
            Files.Overwrite(Path.Combine(Dir, "tools.txt"), Tools.Library.Text());
            Diag.Event("tool", "library written", new { tools = Tools.Library.All.Count, problems });
        }

        /// <summary>The look-up files, one per id, for details on request; files no longer written are removed.</summary>
        public static void WriteLookups(Dictionary<string, string> files)
        {
            if (!On || files == null) return;
            string dir = Paths.Ensure(Path.Combine(Dir, "lookups"));
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> kv in files)
            {
                string name = Safe(kv.Key) + ".txt";
                keep.Add(name);
                Files.Overwrite(Path.Combine(dir, name), kv.Value);
            }
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.txt"))
                    if (!keep.Contains(Path.GetFileName(f))) System.IO.File.Delete(f);
            }
            catch (Exception e) { Diag.Swallowed("tidying the look-up files", e); }
        }

        private static string Safe(string id)
        {
            var sb = new StringBuilder(id.Length);
            foreach (char c in id) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        // ---------- the watchdog's checks (its own thread; reads plain fields only) ----------

        public static void CheckAlive()
        {
            if (!On || !Live.CampaignRunning) return;
            if (LastScan != 0 && Watchdog.Seconds(LastScan) > 30 && !_scanAlarmed)
            {
                _scanAlarmed = true;
                Diag.Alarm("scan_stopped", "The agent's orders folder has not been checked for " + (int)Watchdog.Seconds(LastScan) + " s; its answers would wait.");
            }
            DateTime since = _waitingSince;
            if (since == DateTime.MinValue || _silenceAlarmed) return;
            double minutes = (DateTime.Now - since).TotalMinutes;
            if (minutes < Settings.AnswerAlarmMinutes) return;
            _silenceAlarmed = true;
            Diag.Count("agent:silences");
            Diag.Alarm("agent_silent", "No answer from the agent for " + (int)minutes + " minutes, since poke #" + _waitingFor + ". Is the agent chat still watching for pokes?", true);
        }
    }
}
