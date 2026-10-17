using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BannerlordAgents.Dashboard
{
    /// <summary>
    /// Dashboard.exe [run] [--port 7329] [--open] [--root folder] [--web folder]
    ///     serves the dashboard on http://localhost:port/ (this laptop only) until closed; --open opens it in the browser;
    ///     --root reads another data folder than %LOCALAPPDATA%\BannerlordAgents; --web serves the page from a folder (for work on it)
    /// Dashboard.exe atlas [--root folder]    works out the crossing atlas for the newest walking map now, and prints it
    /// Dashboard.exe route x1,y1 x2,y2 [--root folder]   the way between two points on the newest walking map, in words
    /// Dashboard.exe selftest                  checks the way-finder, the atlas, the picture and the log on made-up data
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Native.Self();
            string mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "run";
            if (mode == "selftest") return SelfTest.Run();
            string root = Arg(args, "--root") ?? DefaultRoot();
            var store = new Store(root);
            switch (mode)
            {
                case "run":
                {
                    int port = int.TryParse(Arg(args, "--port"), out int p) && p > 0 && p < 65536 ? p : 7329;
                    using var one = new Mutex(false, @"Local\BannerlordAgents.Dashboard." + port);
                    bool mine;
                    try { mine = one.WaitOne(0); } catch (AbandonedMutexException) { mine = true; }
                    if (!mine)
                    {
                        Console.WriteLine("A dashboard is already running on port " + port + ": open http://localhost:" + port + "/");
                        if (args.Contains("--open")) Open("http://localhost:" + port + "/");
                        return 0;
                    }
                    var checks = new Checks(store);
                    var server = new Server(store, checks, port, Arg(args, "--web"));
                    using var stop = new CancellationTokenSource();
                    Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Cancel(); };
                    checks.Start(stop.Token);
                    if (!server.Start(out string why)) { Console.WriteLine(why); return 1; }
                    string url = "http://localhost:" + port + "/";
                    Log.Say("Dashboard " + typeof(Program).Assembly.GetName().Version.ToString(3) + " on " + url + " (reading " + root + "). Ctrl+C stops it.");
                    if (args.Contains("--open")) Open(url);
                    server.Serve(stop.Token);
                    return 0;
                }
                case "atlas":
                {
                    string dir = Routes.WalkMap.FindDir(root);
                    if (dir == null) { Console.WriteLine("No walking map yet under " + Path.Combine(root, "map") + ": load a campaign with the dashboard on (agents.map)."); return 1; }
                    Routes.WalkMap m = Routes.WalkMap.Load(dir);
                    Routes.Atlas a = Routes.Atlas.Build(m, s => Console.WriteLine("  " + s));
                    Routes.JsonText.Write(Routes.Atlas.PathIn(dir), a);
                    Console.WriteLine(a.Crossings.Count + " crossings that matter (" + a.Crossings.Count(c => c.Critical) + " critical), from " + a.WaysFound + " ways between " + a.Places + " holdings, in " + a.Seconds + " s:");
                    foreach (Routes.AtlasCrossing c in a.Crossings.Take(40))
                        Console.WriteLine("  " + c.Id + " " + c.Name + (c.Critical ? " [critical]" : "") + ": " + (c.CutsOff ? "no other way" : "+" + Routes.Words.N(c.Detour)) + "; " + Routes.Explain.Closing(c));
                    return 0;
                }
                case "learncheck": return LearnCheck(store);
                case "etacheck": return EtaCheck(store, args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null);
                case "route":
                {
                    string dir = Routes.WalkMap.FindDir(root);
                    if (dir == null || args.Length < 3) { Console.WriteLine("Dashboard route x1,y1 x2,y2 (needs a walking map)"); return 1; }
                    Routes.WalkMap m = Routes.WalkMap.Load(dir);
                    float[] a = args[1].Split(',').Select(float.Parse).ToArray(), b = args[2].Split(',').Select(float.Parse).ToArray();
                    Routes.RouteResult r = new Routes.Finder(m).Find(a[0], a[1], b[0], b[1]);
                    foreach (string line in Routes.Explain.Way(m, Routes.Atlas.Load(dir), r, BannerlordAgents.Common.Pace.FromSpeed(3.4f), "a typical pace", 0, h => Routes.Words.N(h) + " h", out _, out _))
                        Console.WriteLine("- " + line);
                    Console.WriteLine("(" + r.Expanded + " cells looked at, " + Math.Round(r.Ms) + " ms)");
                    return 0;
                }
                default:
                    Console.WriteLine("Dashboard.exe [run] [--port 7329] [--open] | atlas | route x1,y1 x2,y2 | selftest");
                    return 1;
            }
        }

        /// <summary>
        /// "Dashboard learncheck": how well our ways match the game's own planned ways in the newest load's frames, held out: the
        /// old map, the map with its bridges and fords rebuilt, and that map after learning from the even hours' ways, each
        /// measured on the odd hours' ways only.
        /// </summary>
        private static int LearnCheck(Store store)
        {
            System.Text.Json.Nodes.JsonObject cur = store.Current();
            string campaign = (string)cur?["campaign"], load = (string)cur?["load_id"];
            string dir = Routes.WalkMap.FindDir(store.Root, (string)cur?["map"]);
            if (campaign == null || dir == null) { Console.WriteLine("No frames or no walking map yet."); return 1; }
            var train = new List<List<float[]>>();
            var test = new List<(float x, float y, float[] end, float game)>();
            foreach (long h in store.FrameHours(campaign, load))
            {
                System.Text.Json.Nodes.JsonObject f = store.ReadFrame(store.FramePath(campaign, load, h));
                if (f?["parties"] is not System.Text.Json.Nodes.JsonArray parties) continue;
                foreach (System.Text.Json.Nodes.JsonNode n in parties)
                {
                    if (n is not System.Text.Json.Nodes.JsonObject p || p["route"] is not System.Text.Json.Nodes.JsonArray r || r.Count < 2) continue;
                    var pts = new List<float[]> { new[] { (float)(double)p["x"], (float)(double)p["y"] } };
                    for (int i = 0; i + 1 < r.Count; i += 2) pts.Add(new[] { (float)(double)r[i], (float)(double)r[i + 1] });
                    if (h % 2 == 0) { train.Add(pts); continue; }
                    float len = 0;
                    for (int i = 1; i < pts.Count; i++) len += Routes.WalkMap.Dist(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1]);
                    if (len >= 15 && (string)p["kind"] == "lord") test.Add((pts[0][0], pts[0][1], pts[pts.Count - 1], len));
                }
            }
            var sample = test.Where((_, i) => i % Math.Max(1, test.Count / 300) == 0).ToList();
            Console.WriteLine(train.Count + " ways to learn from (even hours), " + sample.Count + " lords' ways to test on (odd hours).");
            void Measure(string name, Routes.WalkMap m)
            {
                var f = new Routes.Finder(m);
                var gaps = new List<float>();
                int none = 0;
                foreach ((float x, float y, float[] end, float game) in sample)
                {
                    float ours = f.Distance(x, y, end[0], end[1]);
                    if (float.IsNaN(ours)) { none++; continue; }
                    gaps.Add((ours - game) / game * 100f);
                }
                gaps.Sort();
                float med = gaps.Count == 0 ? 0 : gaps.Select(Math.Abs).OrderBy(v => v).ElementAt(gaps.Count / 2);
                Console.WriteLine("  " + name + ": median gap " + med.ToString("0.0") + "%, over 5% longer: " + gaps.Count(g => g > 5) + ", over 20% longer: " + gaps.Count(g => g > 20)
                                  + ", no way at all: " + none + " (of " + sample.Count + "); rebuilt " + m.Rebuilt + " cells, learned " + m.Learned);
            }
            Routes.WalkMap.RebuildCrossings = false;
            Measure("the map as copied", Routes.WalkMap.Load(dir, false));
            Routes.WalkMap.RebuildCrossings = true;
            Routes.WalkMap rebuilt = Routes.WalkMap.Load(dir, false);
            Measure("bridges and fords rebuilt", rebuilt);
            foreach (List<float[]> way in train) rebuilt.Learn(way);
            Measure("rebuilt, and learned from the other hours", rebuilt);
            return 0;
        }

        /// <summary>
        /// "Dashboard etacheck [load]": how well the game's marching rule (GamePace, 0.7.3) foretells when a lord gets where it was
        /// going, against what the frames show it did. For every lord's march to a holding (from the first hour of the trip, the
        /// way the game planned, until the party is within 1.5 of where it walked to or inside the holding, its target unchanged
        /// and no battle on the way), the hours foretold by: the game's rule along the planned way with the ground and the
        /// night; the old reckoning, the way's length over the speed number; and the old way-finder, the speed number as map
        /// units an hour with the ground and night multiplied in. Frames before 0.7.3 lack the speed sum's parts: there the base
        /// is worked out from the men (4 x (200 / (200 + men))^0.4) and the factors from the speed number.
        /// </summary>
        private static int EtaCheck(Store store, string loadArg)
        {
            System.Text.Json.Nodes.JsonObject cur = store.Current();
            string campaign = (string)cur?["campaign"], load = loadArg ?? (string)cur?["load_id"];
            string dir = Routes.WalkMap.FindDir(store.Root, (string)cur?["map"]);
            if (campaign == null || dir == null) { Console.WriteLine("No frames or no walking map yet."); return 1; }
            Routes.WalkMap m = Routes.WalkMap.Load(dir);
            float Fl(System.Text.Json.Nodes.JsonNode n) => n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out double d) ? (float)d : 0f;
            string St(System.Text.Json.Nodes.JsonNode n) => n is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string s) ? s : null;
            var frames = new SortedDictionary<long, Dictionary<string, System.Text.Json.Nodes.JsonObject>>();
            var cultures = new Dictionary<string, float[]>();
            foreach (long h in store.FrameHours(campaign, load))
            {
                System.Text.Json.Nodes.JsonObject f = store.ReadFrame(store.FramePath(campaign, load, h));
                if (f?["parties"] is not System.Text.Json.Nodes.JsonArray parties) continue;
                if (f["culture_speeds"] is System.Text.Json.Nodes.JsonObject cs)
                    foreach (var kv in cs) if (kv.Value is System.Text.Json.Nodes.JsonArray a) cultures[kv.Key] = a.Select(Fl).ToArray();
                var byId = new Dictionary<string, System.Text.Json.Nodes.JsonObject>();
                foreach (System.Text.Json.Nodes.JsonNode n in parties)
                    if (n is System.Text.Json.Nodes.JsonObject p && St(p["kind"]) == "lord") byId[St(p["id"])] = p;
                frames[h] = byId;
            }
            var hours = frames.Keys.ToList();
            var errNew = new List<double>(); var errOld = new List<double>(); var errWay = new List<double>();
            var others = new List<float>();
            var longNew = new List<double>(); var longOld = new List<double>(); var longWay = new List<double>();
            var started = new HashSet<string>();
            var lines = new List<string>();
            for (int i = 0; i < hours.Count; i++)
            {
                foreach ((string id, System.Text.Json.Nodes.JsonObject p) in frames[hours[i]])
                {
                    string doing = St(p["doing"]), target = St(p["target_settlement"]);
                    bool marching = p["in_settlement"] == null && p["in_battle"] == null && p["besieging"] == null && p["attached"] == null && p["route"] is System.Text.Json.Nodes.JsonArray;
                    if (!marching || target == null || (doing != "GoToSettlement" && doing != "DefendSettlement" && doing != "BesiegeSettlement" && doing != "RaidSettlement")) continue;
                    string trip = id + ">" + target;
                    if (!started.Add(trip)) continue;
                    float tx = Fl(p["tx"]), ty = Fl(p["ty"]);
                    var way = new List<float[]> { new[] { Fl(p["x"]), Fl(p["y"]) } };
                    var r = (System.Text.Json.Nodes.JsonArray)p["route"];
                    for (int k = 0; k + 1 < r.Count; k += 2) way.Add(new[] { Fl(r[k]), Fl(r[k + 1]) });
                    if (Routes.WalkMap.Dist(way[^1][0], way[^1][1], tx, ty) > 0.3f) way.Add(new[] { tx, ty });
                    double length = BannerlordAgents.Common.GamePace.Length(way);
                    if (length < 8) continue;
                    // when it got there, its course unchanged and no battle on the way
                    double actual = double.NaN;
                    for (int j = i + 1; j < hours.Count && hours[j] - hours[i] <= 24 * 20; j++)
                    {
                        if (!frames[hours[j]].TryGetValue(id, out System.Text.Json.Nodes.JsonObject q)) break;
                        if (q["in_battle"] != null || (St(q["target_settlement"]) != target && St(q["in_settlement"]) != target && St(q["besieging"]) != target)) break;
                        bool there = St(q["in_settlement"]) == target || St(q["besieging"]) == target || Routes.WalkMap.Dist(Fl(q["x"]), Fl(q["y"]), tx, ty) <= 1.5f;
                        if (there) { actual = hours[j] - hours[i]; break; }
                    }
                    if (double.IsNaN(actual) || actual < 4) continue;
                    float speed = Fl(p["speed"]);
                    if (speed <= 0.2f) continue;
                    // the game's rule: the speed sum's parts from the frame, else worked out from the men and the speed number
                    var pace = new BannerlordAgents.Common.Pace();
                    if (Fl(p["speed_base"]) > 0) { pace.Base = Fl(p["speed_base"]); pace.Factors = Fl(p["speed_factors"]); pace.Perks = (int)Fl(p["scout_perks"]); if (Fl(p["foot_share"]) > 0) pace.FootShare = Fl(p["foot_share"]); }
                    else
                    {
                        float men = (Fl(p["army_men"]) > 0 ? Fl(p["army_men"]) : Fl(p["men"])) + Fl(p["wounded"]);
                        pace.Base = (float)(4 * Math.Pow(200.0 / (200.0 + men), 0.4));
                        pace.Factors = speed / pace.Base - 1;
                    }
                    string culture = St(p["culture"]);
                    if (culture != null && cultures.TryGetValue(culture, out float[] feats)) pace.Feats = feats;
                    // what the named rules leave unexplained in its speed now (as the bridge measures it each hour: Forecast.PaceOf)
                    float rest = BannerlordAgents.Common.GamePace.Residual(pace, Fl(p["final_speed"]), m.GroundAt(Fl(p["x"]), Fl(p["y"])), BannerlordAgents.Common.GamePace.IsNight(hours[i]), p["snowing"] != null);
                    if (!float.IsNaN(rest) && Math.Abs(rest) <= 0.6f) pace.Other = rest;
                    others.Add(pace.Other);
                    double hNew = BannerlordAgents.Common.GamePace.March(way, (x, y) => m.GroundAt(x, y), pace, hours[i], out _, out bool ok);
                    if (!ok) continue;
                    double hOld = length / speed;
                    // the old way-finder: the speed number as map units an hour, forest, fords and bridges x 0.7, desert x 0.9, night x 0.75
                    double t = hours[i];
                    for (int k = 1; k < way.Count; k++)
                    {
                        float dx = way[k][0] - way[k - 1][0], dy = way[k][1] - way[k - 1][1], len = MathF.Sqrt(dx * dx + dy * dy);
                        for (float d = 0; d < len; d += 0.5f)
                        {
                            byte g = m.GroundAt(way[k - 1][0] + dx / len * d, way[k - 1][1] + dy / len * d);
                            double f = g == Routes.Ground.Forest || g == Routes.Ground.Fording || g == Routes.Ground.Bridge ? 0.7 : g == Routes.Ground.Desert || g == Routes.Ground.Dune ? 0.9 : 1.0;
                            t += Math.Min(0.5f, len - d) / (speed * f * (BannerlordAgents.Common.GamePace.IsNight(t) ? 0.75 : 1.0));
                        }
                    }
                    double hWay = t - hours[i];
                    errNew.Add((hNew - actual) / actual * 100);
                    errOld.Add((hOld - actual) / actual * 100);
                    errWay.Add((hWay - actual) / actual * 100);
                    if (actual >= 12) { longNew.Add((hNew - actual) / actual * 100); longOld.Add((hOld - actual) / actual * 100); longWay.Add((hWay - actual) / actual * 100); }
                    if (lines.Count < 12) lines.Add("  " + (St(p["name"]) ?? id) + " to " + target + ": " + length.ToString("0") + " units, took " + actual.ToString("0") + " h; the game's rule " + hNew.ToString("0.0") + ", old sum " + hOld.ToString("0.0") + ", old way-finder " + hWay.ToString("0.0"));
                }
            }
            string Sum(string name, List<double> e)
            {
                if (e.Count == 0) return "  " + name + ": no marches";
                double med = e.Select(Math.Abs).OrderBy(v => v).ElementAt(e.Count / 2), bias = e.OrderBy(v => v).ElementAt(e.Count / 2);
                return "  " + name + ": median error " + med.ToString("0.0") + "% (median signed " + bias.ToString("+0.0;-0.0") + "%), within 10%: " + e.Count(v => Math.Abs(v) <= 10) + " of " + e.Count;
            }
            Console.WriteLine(errNew.Count + " marches to a holding with their arrival seen, load " + load + " (hours from the frames, to the hour).");
            foreach (string l in lines) Console.WriteLine(l);
            if (others.Count > 0)
                Console.WriteLine("  left unexplained by the named rules, measured from each lord's speed then: median " + others.OrderBy(v => v).ElementAt(others.Count / 2).ToString("+0.000;-0.000")
                                  + ", a quarter below " + others.OrderBy(v => v).ElementAt(others.Count / 4).ToString("+0.000;-0.000") + ", a quarter above " + others.OrderBy(v => v).ElementAt(others.Count * 3 / 4).ToString("+0.000;-0.000"));
            Console.WriteLine(Sum("the game's rule (0.7.3)", errNew));
            Console.WriteLine(Sum("the old sum, length over the speed number", errOld));
            Console.WriteLine(Sum("the old way-finder", errWay));
            Console.WriteLine("Marches of 12 hours or more (" + longNew.Count + "), where the frames' hour-long steps blur less:");
            Console.WriteLine(Sum("the game's rule (0.7.3)", longNew));
            Console.WriteLine(Sum("the old sum", longOld));
            Console.WriteLine(Sum("the old way-finder", longWay));
            return 0;
        }

        public static string DefaultRoot()
        {
            string over = Environment.GetEnvironmentVariable("BANNERLORD_AGENTS_ROOT");
            if (!string.IsNullOrWhiteSpace(over)) return over.Trim();
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents");
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Console.WriteLine("Could not open the browser (" + e.Message + "): open " + url + " yourself."); }
        }
    }

    /// <summary>The dashboard's own log: the console, and logs\dashboard\dashboard-(date).log.</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        public static string Dir;

        public static void Say(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            lock (Gate)
            {
                Console.WriteLine(line);
                if (Dir == null) return;
                try { File.AppendAllText(Path.Combine(Dir, "dashboard-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + Environment.NewLine); }
                catch (IOException) { }
            }
        }

        public static void Line(string file, string json)
        {
            if (Dir == null) return;
            lock (Gate)
            {
                try { File.AppendAllText(Path.Combine(Dir, file), json + "\n"); }
                catch (IOException) { }
            }
        }
    }

    /// <summary>Below-normal priority and efficiency mode (EcoQoS), so Windows keeps the dashboard off the game's busy core.</summary>
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottling
        {
            public uint Version, ControlMask, StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int size);

        public static void Self()
        {
            try
            {
                Process me = Process.GetCurrentProcess();
                me.PriorityClass = ProcessPriorityClass.BelowNormal;
                var s = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 1 };
                SetProcessInformation(me.Handle, 4, ref s, Marshal.SizeOf<PowerThrottling>());
            }
            catch { /* runs at normal priority then */ }
        }
    }
}
