using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BannerlordAgents.Runs.RunData;

namespace BannerlordAgents.Runs
{
    /// <summary>
    /// Runs.exe (docs/RUNS.md):
    ///   status [run]    where the open (or named) run stands
    ///   watch [run]     what is new since the last look, and what needs a look (exit code 1 then)
    ///   report [run]    the run's numbers in full: report.md and report.json in its folder
    ///   compare         every run side by side: compare.md in the runs folder
    /// A run is its folder's name (or "latest"); none given: the open one, else the newest.
    /// </summary>
    internal static class Program
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            string id = args.Length > 1 ? args[1] : null;
            try
            {
                switch (cmd)
                {
                    case "status": return Status(id);
                    case "watch": return Watch.Run(id);
                    case "report": return Report.Run(id);
                    case "compare": return Report.Compare();
                    default:
                        Console.WriteLine("Runs.exe status|watch|report [run] | compare");
                        return 2;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Runs.exe failed: " + e.GetType().Name + ": " + e.Message);
                return 2;
            }
        }

        private static int Status(string id)
        {
            string pick = Pick(id);
            if (pick == null) { Console.WriteLine("No test run yet (" + RunsDir + ")."); return 0; }
            RunData r = Load(pick);
            Console.WriteLine(Text.Head(r));
            foreach (string line in Text.Standing(r, r.FirstDay, r.LastDay)) Console.WriteLine("  " + line);
            Console.WriteLine("  " + Text.AgentsLine(r.Calls));
            foreach (JsonNode f in r.State?["flags"] as JsonArray ?? new JsonArray()) Console.WriteLine("  ! day " + Str(f, "day") + ", " + Str(f, "kind") + ": " + Str(f, "text"));
            return 0;
        }
    }

    /// <summary>The words every command shares.</summary>
    internal static class Text
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string N(long n) => n.ToString("#,0", Inv);
        public static string N(double n) => double.IsNaN(n) ? "-" : n.ToString("#,0", Inv);
        public static string S(double seconds) => double.IsNaN(seconds) ? "-" : seconds.ToString("0.0", Inv) + " s";
        public static string Signed(long n) => n > 0 ? "+" + N(n) : n < 0 ? "-" + N(-n) : "±0";
        public static string K(long tokens) => tokens >= 1_000_000 ? (tokens / 1e6).ToString("0.00", Inv) + "M" : tokens >= 1000 ? (tokens / 1e3).ToString("0.0", Inv) + "k" : tokens.ToString(Inv);

        public static string Head(RunData r)
        {
            JsonObject last = r.LastDay;
            string day = last != null ? "day " + Str(last, "day") + " of " + r.DaysPlanned + " (" + Str(last, "date") + ")" : "no day line yet";
            string arm = r.Arm + (ArmWords(r.Arm) != null ? ", " + ArmWords(r.Arm) : "");
            return "Run " + r.Id + " (" + arm + "): " + r.Status + ", " + day + "; started " + (Str(r.State, "started") ?? "-") + (Str(r.State, "finished") != null ? ", ended " + Str(r.State, "finished") : "")
                   + "; " + (r.State?["segments"] as JsonArray)?.Count + " load(s), " + (r.State?["flags"] as JsonArray)?.Count + " mark(s) against it.";
        }

        public static string ArmWords(string arm) => arm switch
        {
            "A" => "you against the agents", "B" => "you against the game's AI", "C1" => "an agent ruling your kingdom against the agents",
            "C2" => "an agent ruling your kingdom against the game's AI", _ => null
        };

        public static string WhoWords(string who) => who switch
        {
            "you" => "you", "agent" => "its agent", "lieutenant" => "your lieutenant (your yes)", "watching" => "the game's AI (agent watching)", "game" => "the game's AI", _ => who ?? "?"
        };

        public static JsonObject Realm(JsonObject day, string id) => (day?["realms"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => Str(x, "realm") == id);

        /// <summary>Each realm now, and its change since the earlier day line.</summary>
        public static List<string> Standing(RunData r, JsonObject before, JsonObject now)
        {
            var lines = new List<string>();
            if (now == null) return lines;
            foreach (string id in r.Realms)
            {
                JsonObject a = Realm(before, id), b = Realm(now, id);
                if (b == null) continue;
                string D(string key) => a == null || ReferenceEquals(a, b) ? "" : " (" + Signed(Lng(b, key) - Lng(a, key)) + ")";
                lines.Add(Str(b, "name") + " [" + WhoWords(Str(b, "who")) + "]: " + Str(b, "towns") + " towns" + D("towns") + ", " + Str(b, "castles") + " castles" + D("castles") + ", "
                          + Str(b, "villages") + " villages" + D("villages") + "; " + Str(b, "lords") + " lords in the field" + D("lords") + ", " + N(Lng(b, "men")) + " men" + D("men")
                          + ", strength " + N(Lng(b, "strength")) + D("strength") + "; " + Str(b, "lords_captive") + " of its lords held" + D("lords_captive") + ", "
                          + Str(b, "enemy_lords_held") + " enemy lords held" + D("enemy_lords_held") + "; clans' gold " + N(Lng(b, "clans_gold")) + D("clans_gold"));
            }
            return lines;
        }

        public static string AgentsLine(List<Call> calls)
        {
            if (calls.Count == 0) return "agents: no calls in this run yet";
            return "agents: " + string.Join("; ", calls.GroupBy(c => c.Agent.Name).Select(g =>
            {
                Usage u = Usage.Of(g);
                Usage ans = Usage.Of(g.Where(c => c.Kind == "answer" || c.Kind == "chat"));
                return g.Key + " " + u.Calls + " calls (" + (u.Failed > 0 ? u.Failed + " failed, " : "") + "answers median " + S(ans.Median) + "), tokens read " + K(u.Read) + " (" + K(u.CacheRead) + " from cache), written "
                       + K(u.Out) + ", list price $" + u.Cost.ToString("0.00", Inv);
            }));
        }

        public static string Battle(JsonNode d)
        {
            JsonNode a = d["attacker"], b = d["defender"];
            string side(JsonNode s) => Str(s, "leader") + " (" + Str(s, "realm") + ", " + WhoWords(Str(s, "by")) + ", " + N(Lng(s, "men")) + " men, lost " + N(Lng(s, "lost")) + ")";
            return Str(d, "kind") + (Str(d, "place") != null ? " at " + Str(d, "place") : "") + ": " + side(a) + " against " + side(b) + "; "
                   + (Str(d, "winner") == "attacker" ? Str(a, "leader") : Str(d, "winner") == "defender" ? Str(b, "leader") : "nobody") + " won"
                   + (Str(d, "you_fought") == "true" ? "; you fought in it" : "");
        }

        public static bool LordBattle(JsonNode d) => Lng(d["attacker"], "lords") > 0 && Lng(d["defender"], "lords") > 0;
    }

    /// <summary>What is new since the last look (the coding session runs this every few minutes during a test).</summary>
    internal static class Watch
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(string id)
        {
            string pick = Pick(id);
            if (pick == null) { Console.WriteLine("No test run yet."); return 0; }
            RunData r = Load(pick, calls: false);
            string cursorPath = Path.Combine(r.Dir, "watch.json");
            JsonObject cur = Obj(cursorPath) ?? new JsonObject();
            bool first = cur.Count == 0;
            var attention = new List<string>();
            var said = new List<string>();

            // the run's own events since the last look
            List<JsonObject> evs = LinesFrom(Path.Combine(r.Dir, "events.jsonl"), Lng(cur, "events"), out long evNext);
            int daysSeen = Int(cur, "days", 0), flagsSeen = Int(cur, "flags", 0);
            JsonObject before = daysSeen > 0 && daysSeen <= r.Days.Count ? r.Days[daysSeen - 1] : r.FirstDay;

            said.Add(Text.Head(r));
            if (r.Days.Count > daysSeen || first)
                foreach (string line in Text.Standing(r, before, r.LastDay)) said.Add("  " + line);

            var flags = (r.State?["flags"] as JsonArray ?? new JsonArray()).Skip(flagsSeen).ToList();
            foreach (JsonNode f in flags) attention.Add("mark against the run, day " + Str(f, "day") + ", " + Str(f, "kind") + ": " + Str(f, "text"));

            var run = evs.Where(e => Str(e, "k") == "run").ToList();
            foreach (JsonObject e in run)
            {
                string w = Str(e, "w");
                JsonNode d = e["d"];
                switch (w)
                {
                    case "holding": said.Add("  day " + Str(d, "day") + ": " + Str(d, "place") + " (" + Str(d, "kind") + ") passed from " + Str(d, "from") + " to " + Str(d, "to") + " (" + Str(d, "how") + ")"); break;
                    case "battle": if (Text.LordBattle(d)) said.Add("  day " + Str(d, "day") + ": " + Text.Battle(d)); break;
                    case "siege began": case "siege ended": said.Add("  day " + Str(d, "day") + ": " + w + " at " + Str(d, "place") + " (" + Str(d, "by") + " against " + Str(d, "of") + ")"); break;
                    case "lord killed": said.Add("  day " + Str(d, "day") + ": " + Str(d, "lord") + " (" + Str(d, "realm") + ") was killed" + (Str(d, "killer") != null ? " by " + Str(d, "killer") : "")); break;
                    case "lord taken": said.Add("  day " + Str(d, "day") + ": " + Str(d, "lord") + " (" + Str(d, "realm") + ") was taken by " + Str(d, "by")); break;
                    case "note": said.Add("  your note, day " + Str(d, "day") + ": " + Str(d, "text")); break;
                    case "began": case "finished": case "stopped": case "carried on": said.Add("  the run " + w + "."); break;
                    case "war": case "peace": said.Add("  day " + Str(d, "day") + ": " + w + " between " + Str(d, "a") + " and " + Str(d, "b")); break;
                }
            }
            int small = run.Count(e => Str(e, "w") == "battle" && !Text.LordBattle(e["d"])), armies = run.Count(e => Str(e, "w") == "army formed");
            int freed = run.Count(e => Str(e, "w") == "lord freed"), looted = run.Count(e => Str(e, "w") == "village looted");
            if (small + armies + freed + looted > 0) said.Add("  also: " + small + " smaller battles (bandits, villagers, caravans), " + armies + " armies formed, " + freed + " lords freed, " + looted + " villages looted");

            // the orders' receipts, the pokes, your decisions, the lieutenant
            var receipts = evs.Where(e => Str(e, "k") == "answer" && Str(e, "w") == "receipt").ToList();
            if (receipts.Count > 0)
                said.Add("  agents' orders: " + string.Join("; ", receipts.GroupBy(e => Str(e["d"], "realm")).Select(g => g.Key + " " + string.Join(", ", g.GroupBy(e => Str(e["d"], "status")).Select(s => s.Count() + " " + s.Key)))));
            var refused = receipts.Where(e => Str(e["d"], "status") == "refused").ToList();
            if (refused.Count > 0) said.Add("  refused, e.g.: " + string.Join(" | ", refused.Take(3).Select(e => Str(e["d"], "reason"))));
            var pokes = evs.Where(e => Str(e, "k") == "poke").ToList();
            if (pokes.Count > 0) said.Add("  pokes: " + string.Join("; ", pokes.GroupBy(e => Str(e["d"], "faction")).Select(g => g.Key + " " + g.Count())));
            var mine = evs.Where(e => Str(e, "k") == "decision").ToList();
            var lt = evs.Where(e => Str(e, "k") == "lieutenant").ToList();
            if (mine.Count + lt.Count > 0) said.Add("  yours: " + mine.Count + " decisions recorded" + (lt.Count > 0 ? "; lieutenant: " + string.Join(", ", lt.GroupBy(e => Str(e, "w")).Select(g => g.Count() + " " + g.Key)) : ""));

            // what needs a look: serious alarms, errors, freezes outside loading, fog leaks, a low sight check
            foreach (JsonObject e in evs.Where(e => Str(e, "k") == "alarm" && Str(e["d"], "serious") == "true").Take(5)) attention.Add("alarm " + Str(e, "w") + ": " + Str(e["d"], "text"));
            foreach (JsonObject e in evs.Where(e => Str(e, "k") == "error").Take(3)) attention.Add("error in " + Str(e, "w") + ": " + (Str(e["d"], "message") ?? Str(e["d"], "text") ?? ""));
            foreach (JsonObject e in evs.Where(e => Str(e, "k") == "freeze" && Str(e, "w") == "began" && !(Str(e["d"], "state") ?? "").Contains("Loading")).Take(3))
                attention.Add("the game froze " + Str(e["d"], "seconds") + " s (" + Str(e["d"], "state") + ")");
            int leaks = evs.Count(e => Str(e, "k") == "fog" && Str(e, "w") == "leak");
            if (leaks > 0) attention.Add("the fog check flagged " + leaks + " report(s)");
            foreach (JsonObject e in evs.Where(e => Str(e, "k") == "sight_check"))
            {
                string[] p = (Str(e, "w") ?? "").Split(" of ");
                if (p.Length == 2 && int.TryParse(p[0], out int agree) && int.TryParse(p[1], out int total) && total >= 10 && agree < total * 0.97)
                {
                    attention.Add("the sight check agreed on only " + agree + " of " + total);
                    break;
                }
            }

            // the war rooms' calls since the last look
            JsonObject callsCur = cur["calls"] as JsonObject ?? new JsonObject();
            var nextCalls = new JsonObject();
            var fresh = new List<Call>();
            foreach (AgentInfo a in r.Agents)
            {
                long from = Lng(callsCur, a.LogDir);
                List<Call> got = ReadCalls(a, from, out long next);
                nextCalls[a.LogDir] = next;
                fresh.AddRange(got.Where(c => r.InRun(c.At)));
            }
            if (fresh.Count > 0) said.Add("  since the last look, " + Text.AgentsLine(fresh));
            foreach (Call c in fresh.Where(c => !c.Ok).Take(3)) attention.Add(c.Agent.Name + "'s " + c.Kind + " failed at " + c.At.ToString("HH:mm:ss", Inv) + ": " + (c.Problem ?? "no reason given"));
            if (fresh.Any(c => c.LimitHit)) attention.Add("a usage limit was hit: " + fresh.First(c => c.LimitHit).Problem);

            // is everything alive?
            if (r.Status == "running")
            {
                bool game = Process.GetProcesses().Any(p => Safe(() => p.ProcessName).Contains("Bannerlord", StringComparison.OrdinalIgnoreCase));
                int warRooms = Process.GetProcesses().Count(p => Safe(() => p.ProcessName).Equals("WarRoom", StringComparison.OrdinalIgnoreCase));
                int wanted = r.Agents.Count;
                if (!game) attention.Add("the game is not running (the run is open; load " + ((r.State?["saves"] as JsonArray)?.LastOrDefault()?.ToString() ?? "its last save") + " to carry it on)");
                if (warRooms < wanted) attention.Add("only " + warRooms + " of " + wanted + " war rooms are running (scripts\\agents.ps1 start)");
                string bridgeLog = Path.Combine(Root, "logs", "bridge.log");
                if (game && File.Exists(bridgeLog) && DateTime.Now - File.GetLastWriteTime(bridgeLog) > TimeSpan.FromMinutes(10))
                    attention.Add("the bridge has written nothing for " + (int)(DateTime.Now - File.GetLastWriteTime(bridgeLog)).TotalMinutes + " minutes (paused, or stuck in a menu?)");
            }

            if (attention.Count > 0)
            {
                Console.WriteLine("NEEDS A LOOK:");
                foreach (string a in attention) Console.WriteLine("  ! " + a);
            }
            foreach (string s in said) Console.WriteLine(s);
            if (said.Count <= 1 && attention.Count == 0) Console.WriteLine("  nothing new since the last look.");

            var next2 = new JsonObject { ["events"] = evNext, ["days"] = r.Days.Count, ["flags"] = (r.State?["flags"] as JsonArray)?.Count ?? 0, ["calls"] = nextCalls, ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) };
            File.WriteAllText(cursorPath, next2.ToJsonString());
            return attention.Count > 0 ? 1 : 0;
        }

        private static string Safe(Func<string> f)
        {
            try { return f() ?? ""; }
            catch (Exception) { return ""; }
        }
    }
}
