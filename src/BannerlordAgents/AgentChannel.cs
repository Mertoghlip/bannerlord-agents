using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// - poke.txt: the latest poke, written before its line appears in pokes.log: which number to answer, the mode,
    ///   and the full report with the reasons for the poke.
    /// - report.txt: the situation now, refreshed every in-game hour.
    /// - receipts.log: one line per order the agent gave (applied, checked in shadow mode, or refused and why), how long
    ///   the answer took after its poke, and later what became of each order.
    /// The agent writes each answer as a JSON file into orders\. Each file is read once, answered in receipts.log, and
    /// moved to orders\done\. One file per answer means nothing is ever overwritten or lost.
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
            public string Lord, Do, Target, Why;
            public DateTime Received;
        }

        private static Timer _timer;
        private static int _scanQueued;
        private static readonly Dictionary<int, KeyValuePair<DateTime, double>> PokeTimes = new Dictionary<int, KeyValuePair<DateTime, double>>();
        private static readonly Dictionary<string, int> Unreadable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static int _lastPoke = -1;

        public static int AnswerFiles, OrdersReceived, Applied, Checked, Refused;

        public static bool On => Settings.Agent == "shadow" || Settings.Agent == "live";
        public static string Dir => Settings.AgentFolder;
        private static string OrdersDir => Path.Combine(Dir, "orders");

        /// <summary>Game's thread, at session start: a once-a-second tick that hands the folder scan to the worker.</summary>
        public static void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(_ =>
            {
                if (!On || !Bridge.Running || Interlocked.Exchange(ref _scanQueued, 1) == 1) return;
                Background.Post(() =>
                {
                    try { Scan(); }
                    finally { Interlocked.Exchange(ref _scanQueued, 0); }
                });
            }, null, 1000, 1000);
        }

        public static void Reset()
        {
            PokeTimes.Clear();
            Unreadable.Clear();
            _lastPoke = -1;
            AnswerFiles = OrdersReceived = Applied = Checked = Refused = 0;
        }

        public static string ModeText()
        {
            switch (Settings.Agent)
            {
                case "live":
                    return "LIVE. " + (Text.Plain(Settings.AgentLords) == "all" ? "Every lord of yours obeys your orders."
                           : "Only these lords obey: " + Settings.AgentLords + ". The others follow their own judgment, and orders for them are refused.");
                case "shadow":
                    return "SHADOW. Every order is checked against the game and answered in receipts.log, but none is carried out yet.";
                default:
                    return "OFF.";
            }
        }

        // ---------- what the agent reads ----------

        public static void OnPoke(int n, double hours, List<Trigger> triggers, string report)
        {
            _lastPoke = n;
            PokeTimes[n] = new KeyValuePair<DateTime, double>(DateTime.Now, hours);
            Paths.Ensure(OrdersDir);
            string head = "POKE #" + n + ", " + Text.When(hours) + "\n"
                          + "Answer with one orders file in orders\\ that says \"poke\": " + n + ".\n"
                          + "Your orders right now: " + ModeText() + "\n\n";
            Files.Overwrite(Path.Combine(Dir, "poke.txt"), head + report);
            string first = triggers.Count > 0 ? triggers[0].Text : "";
            Files.Append(Path.Combine(Dir, "pokes.log"), "#" + n + " | " + Text.When(hours) + " | " + first + (triggers.Count > 1 ? " (and " + (triggers.Count - 1) + " more)" : ""));
            Files.FlushAll();
        }

        public static void OnHour(double hours, string report)
        {
            string head = "SITUATION AT " + Text.When(hours) + " (refreshed every in-game hour"
                          + (_lastPoke >= 0 ? "; the latest poke is #" + _lastPoke : "") + ")\n"
                          + "Your orders right now: " + ModeText() + "\n\n";
            Files.Overwrite(Path.Combine(Dir, "report.txt"), head + report);
        }

        // ---------- what the agent writes ----------

        public static void Scan()
        {
            if (!Directory.Exists(OrdersDir)) { Paths.Ensure(OrdersDir); return; }
            foreach (string f in Directory.GetFiles(OrdersDir, "*.json").OrderBy(System.IO.File.GetLastWriteTimeUtc))
            {
                if ((DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(f)).TotalMilliseconds < 300) continue;   // still being written
                if (!Parse(f, out int poke, out List<AgentOrder> orders, out string error))
                {
                    Unreadable.TryGetValue(f, out int tries);
                    if (++tries < 5) { Unreadable[f] = tries; continue; }
                    Unreadable.Remove(f);
                    MoveDone(f);
                    Line("file " + Path.GetFileName(f) + ": UNREADABLE, nothing in it was carried out (" + error + ")");
                    continue;
                }
                Unreadable.Remove(f);
                MoveDone(f);
                AnswerFiles++;
                if (orders.Count == 0) { Line("file " + Path.GetFileName(f) + " (poke #" + poke + "): no orders in it; nothing changes"); continue; }
                foreach (AgentOrder o in orders)
                {
                    OrdersReceived++;
                    AgentOrder order = o;
                    Background.ToMain.Enqueue(() => Orders.FromAgent(order));
                }
            }
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

            DateTime now = DateTime.Now;
            int i = 0;
            foreach (JToken t in list)
            {
                i++;
                if (!(t is JObject o)) continue;
                orders.Add(new AgentOrder
                {
                    File = Path.GetFileName(path), Index = i, Poke = (int?)o["poke"] ?? poke, Received = now,
                    Lord = (string)o["lord"], Do = (string)(o["do"] ?? o["order"] ?? o["verb"]), Target = (string)o["target"], Why = (string)o["why"]
                });
            }
            return true;
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
            catch (Exception e) { Log.Line("agent channel: could not move " + f + " to done: " + e.Message); }
        }

        /// <summary>The answer to one order, with how long it took after its poke.</summary>
        public static void Receipt(AgentOrder a, string status, string reason, double hours)
        {
            if (status == "applied") Applied++;
            else if (status == "checked") Checked++;
            else Refused++;
            string delay = "";
            double seconds = -1, gameHours = -1;
            if (a.Poke >= 0 && PokeTimes.TryGetValue(a.Poke, out KeyValuePair<DateTime, double> t))
            {
                seconds = (a.Received - t.Key).TotalSeconds;
                gameHours = hours - t.Value;
                delay = " Answered " + seconds.ToString("0", Text.Inv) + " s after poke #" + a.Poke + " (" + gameHours.ToString("0.0", Text.Inv) + " in-game hours).";
            }
            else if (a.Poke < 0) delay = " (The file gave no poke number, so the answer time is unknown.)";
            string what = (a.Lord ?? "?") + " " + (a.Do ?? "?") + (string.IsNullOrEmpty(a.Target) ? "" : " " + a.Target);
            Line("poke #" + a.Poke + ", " + a.File + " order " + a.Index + " (" + what + "): " + status.ToUpperInvariant() + ". " + reason + "." + delay);
            Brain.WriteOrder(new
            {
                Type = "agent_receipt", a.Poke, a.File, a.Index, a.Lord, a.Do, a.Target, a.Why, Status = status, Reason = reason,
                Hours = hours, AnswerSeconds = seconds >= 0 ? seconds : (double?)null, AnswerGameHours = gameHours >= 0 ? gameHours : (double?)null,
                Mode = Settings.Agent
            });
        }

        /// <summary>What became of an agent's order later (arrived, given again, ended).</summary>
        public static void Event(string text) => Line(text);

        private static void Line(string text)
        {
            Files.Append(Path.Combine(Dir, "receipts.log"), DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            Files.FlushAll();
        }
    }
}
