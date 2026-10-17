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
                case "route":
                {
                    string dir = Routes.WalkMap.FindDir(root);
                    if (dir == null || args.Length < 3) { Console.WriteLine("Dashboard route x1,y1 x2,y2 (needs a walking map)"); return 1; }
                    Routes.WalkMap m = Routes.WalkMap.Load(dir);
                    float[] a = args[1].Split(',').Select(float.Parse).ToArray(), b = args[2].Split(',').Select(float.Parse).ToArray();
                    Routes.RouteResult r = new Routes.Finder(m).Find(a[0], a[1], b[0], b[1]);
                    foreach (string line in Routes.Explain.Way(m, Routes.Atlas.Load(dir), r, 3.4f, "a typical pace", 0, Routes.SpeedFactors.Load(dir), h => Routes.Words.N(h) + " h", out _, out _))
                        Console.WriteLine("- " + line);
                    Console.WriteLine("(" + r.Expanded + " cells looked at, " + Math.Round(r.Ms) + " ms)");
                    return 0;
                }
                default:
                    Console.WriteLine("Dashboard.exe [run] [--port 7329] [--open] | atlas | route x1,y1 x2,y2 | selftest");
                    return 1;
            }
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
