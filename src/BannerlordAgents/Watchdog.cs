using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// Heartbeats, like the battle watcher's beats. The game's thread stamps the time on every frame and every campaign
    /// tick; a small thread of our own (low priority, efficiency cores) looks once a second and raises an alarm when
    /// something stops: the game's thread, our background helper, the hourly look at the map, or the check of the
    /// agent's folder. It knows when the clock stops for a good reason (paused, a menu, a battle, loading, the window in
    /// the background), so it does not cry wolf. When the game's thread stops for 20 s it runs the freeze probe, an
    /// outside program that says where inside the game the thread is stuck.
    ///
    /// The fields here are plain numbers and text, so any thread may read them.
    /// </summary>
    internal static class Watchdog
    {
        private static readonly double Freq = Stopwatch.Frequency;

        public static long LastFrame, LastCampaignTick, Frames, CampaignTicks;
        /// <summary>Our code running on the game's thread right now, or null (set by BridgeBehavior's Guard).</summary>
        public static volatile string Doing;
        public static volatile string State = "starting up";
        public static volatile string Speed = "";
        public static volatile bool InFront = true;
        /// <summary>The game's thread, as Windows numbers it, for the freeze probe.</summary>
        public static volatile int MainThreadNative;
        public static double LastHourlyHours = -1;

        private static Thread _thread;
        private static Type _stateType;
        private static bool _atMenu, _stateKnown;
        private static int _mode = int.MinValue;
        private static IntPtr _window;
        private static uint _pid;
        private static bool _inStall, _stallAlarmed, _hourlyAlarmed;
        private static DateTime _stallStart;
        private static string _stallDoing, _stallState;
        private static int _probes, _stallProbes;
        private static long _stuckJob, _lastDropped;

        public static double Seconds(long since) => (Stopwatch.GetTimestamp() - since) / Freq;
        public static bool ClockRunning => LastCampaignTick != 0 && Seconds(LastCampaignTick) < 1.5;

        // ---------- the game's thread ----------

        /// <summary>Every frame, in every state (SubModule.OnApplicationTick). Must stay tiny.</summary>
        public static void Frame()
        {
            LastFrame = Stopwatch.GetTimestamp();
            Frames++;
            if (MainThreadNative == 0)
            {
                MainThreadNative = (int)GetCurrentThreadId();
                Diag.MainThread = Thread.CurrentThread.ManagedThreadId;
            }
            try
            {
                TaleWorlds.Core.GameState st = TaleWorlds.Core.GameStateManager.Current?.ActiveState;
                Type t = st?.GetType();
                bool atMenu = st is TaleWorlds.CampaignSystem.GameState.MapState ms && ms.AtMenu;
                if (!_stateKnown || t != _stateType || atMenu != _atMenu)
                {
                    _stateKnown = true;
                    _stateType = t;
                    _atMenu = atMenu;
                    string s = Describe(st, t, atMenu);
                    if (s != State)
                    {
                        Diag.Event("state", s, new { from = State });
                        State = s;
                        if (st is MissionState) SceneBegan();
                    }
                }
                Campaign c = Campaign.Current;
                int mode = c != null ? (int)c.TimeControlMode : -1;
                if (mode != _mode)
                {
                    _mode = mode;
                    string sp = c == null ? "" : SpeedText(c.TimeControlMode);
                    if (sp != Speed)
                    {
                        Diag.Event("speed", sp.Length > 0 ? sp : "no campaign", new { from = Speed });
                        Speed = sp;
                    }
                }
            }
            catch (Exception e) { Diag.Swallowed("watchdog: reading the game's state", e); }
        }

        /// <summary>Each campaign tick, which the game only runs while its clock moves.</summary>
        public static void CampaignBeat(double hours)
        {
            LastCampaignTick = Stopwatch.GetTimestamp();
            CampaignTicks++;
            double before = Diag.Hours;
            Diag.Hours = hours;
            if (before >= 0 && hours > before)
            {
                long crossed = (long)Math.Floor(hours) - (long)Math.Floor(before);
                if (crossed > 0 && crossed < 48) Diag.Count("hours", crossed);
            }
        }

        public static void HourlyBeat(double hours)
        {
            LastHourlyHours = hours;
            _hourlyAlarmed = false;
        }

        /// <summary>A new campaign session: forget the last one's clock.</summary>
        public static void NewCampaign()
        {
            LastHourlyHours = -1;
            Diag.Hours = -1;
            _hourlyAlarmed = false;
        }

        /// <summary>
        /// A scene has begun. When it is the player's own battle (not a walk through a town, and not an auto-resolve, which opens no scene) the game's clock stands
        /// still for minutes (run A: 4.9 minutes at the shortest, against councils of 72 s), so the realm agents' war rooms are told (0.15.1).
        /// </summary>
        private static void SceneBegan()
        {
            if (!Bridge.Running || !Settings.Bridge || TaleWorlds.CampaignSystem.MapEvents.MapEvent.PlayerMapEvent == null) return;
            // the sides of the human player's battle as he fights it, the ring's joiners in (0.17.3, PlayerBattles)
            try { PlayerBattles.AsFought(); }
            catch (Exception e) { Diag.Swallowed("the sides of the player's battle as it is fought", e); }
            double hours = Diag.Hours;
            Background.PostFirst("the player's battle began", () => AgentChannel.PlayerBattle(hours));
        }

        private static string Describe(TaleWorlds.Core.GameState st, Type t, bool atMenu)
        {
            if (st == null) return "loading";
            if (st is TaleWorlds.CampaignSystem.GameState.MapState) return atMenu ? "campaign map, in a menu" : "campaign map";
            if (st is MissionState) return "battle or scene";
            string n = t.Name;
            if (n.EndsWith("State", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 5);
            return n == "Initial" ? "main menu" : "screen: " + n;
        }

        private static string SpeedText(CampaignTimeControlMode m)
        {
            switch (m)
            {
                case CampaignTimeControlMode.Stop:
                case CampaignTimeControlMode.FastForwardStop:
                    return "stopped";
                case CampaignTimeControlMode.StoppablePlay:
                case CampaignTimeControlMode.UnstoppablePlay:
                    return "play";
                default:
                    return "fast";
            }
        }

        // ---------- the watchdog's own thread ----------

        public static void Start()
        {
            if (_thread != null) return;
            using (Process p = Process.GetCurrentProcess()) _pid = (uint)p.Id;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Bannerlord Agents watchdog", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private static void Loop()
        {
            string placement = Cpu.MakeCurrentThreadBackground();
            Diag.Event("session", "watchdog started", new { placement });
            int n = 0;
            while (true)
            {
                Thread.Sleep(1000);
                n++;
                try { Check(n); }
                catch (Exception e) { Diag.Swallowed("watchdog", e); }
            }
        }

        private static void Check(int n)
        {
            string bucket = !Live.CampaignRunning ? "time:no campaign"
                          : ClockRunning ? "time:clock running"
                          : State == "campaign map" ? "time:paused on the map"
                          : State == "campaign map, in a menu" ? "time:in a menu"
                          : State == "battle or scene" ? "time:battle or scene"
                          : "time:other screens";
            Diag.Count(bucket);

            if (_window == IntPtr.Zero && n % 10 == 1) FindWindow();
            bool front = IsFront();
            if (front != InFront)
            {
                InFront = front;
                Diag.Event("focus", front ? "game window in front" : "game window in the background", new { state = State, clock_running = ClockRunning });
            }
            if (!front) Diag.Count("time:window in the background");

            CheckFrames();
            CheckHelper();

            if (Live.CampaignRunning && Settings.Bridge && LastHourlyHours >= 0 && Diag.Hours - LastHourlyHours > 3 && !_hourlyAlarmed)
            {
                _hourlyAlarmed = true;
                Diag.Alarm("hourly_stopped", "The hourly look at the map has not run for " + (int)(Diag.Hours - LastHourlyHours)
                                             + " in-game hours although the clock ran. Pokes and reports have stopped.", true);
            }
            AgentChannel.CheckAlive();

            Diag.RequestDrain();
            if (n % 60 == 0) Background.Post("health row", Diag.Health);
            if (n % 600 == 0) Background.Post("summary", () => Diag.Summary(false));
        }

        private static void CheckFrames()
        {
            if (LastFrame == 0) return;
            double idle = Seconds(LastFrame);
            if (!_inStall)
            {
                if (idle < 20) return;
                _inStall = true;
                _stallAlarmed = false;
                _stallProbes = 0;
                _stallStart = DateTime.Now.AddSeconds(-idle);
                _stallDoing = Doing;
                _stallState = State;
                bool hung = IsHung();
                Diag.Count("freeze:count");
                Diag.Event("freeze", "began", new { seconds = Math.Round(idle), state = _stallState, doing = _stallDoing, window_front = InFront, not_responding = hung });
                Log.Line("The game's thread has not moved for " + (int)idle + " s (" + Where() + (hung ? "; Windows marks the game as not responding" : "")
                         + "). A long load can do this too; the probe will say where it is.");
                if (Settings.FreezeProbe && (InFront || hung)) RunProbe("the game's thread had not moved for " + (int)idle + " s");
                return;
            }
            if (idle < 2)
            {
                double lasted = (DateTime.Now - _stallStart).TotalSeconds;
                _inStall = false;
                Diag.Sample("freeze:seconds", lasted);
                Diag.Event("freeze", "ended", new { seconds = Math.Round(lasted) });
                Log.Line("The game's thread moved again after " + (int)lasted + " s.");
                return;
            }
            if (idle >= 120 && !_stallAlarmed)
            {
                _stallAlarmed = true;
                bool helperStuck = Background.JobStarted != 0 && Seconds(Background.JobStarted) > 10;
                bool playing = _stallState != null && (_stallState.StartsWith("campaign map", StringComparison.Ordinal) || _stallState == "battle or scene");
                if (playing)
                    Diag.Alarm("game_frozen", "The game has not moved for 2 minutes (" + Where() + "). The freeze probe and the black box in this launch's log folder say where it is stuck.",
                               true, null, helperStuck);
                else
                    Diag.Alarm("long_load", "The game's thread has been busy for 2 minutes outside play (" + Where() + "), most likely a long load. The freeze probe says where it was.",
                               false, null, helperStuck);
                if (Settings.FreezeProbe && _stallProbes < 2) RunProbe("the game's thread had not moved for " + (int)idle + " s");
            }
        }

        private static string Where() => _stallState + (_stallDoing != null ? "; our code was running on it: " + _stallDoing : "; none of our code was running on it");

        private static void CheckHelper()
        {
            long js = Interlocked.Read(ref Background.JobStarted);
            if (js != 0 && Seconds(js) > 10 && _stuckJob != js)
            {
                _stuckJob = js;
                Diag.Alarm("helper_stuck", "Our background helper has been on one piece of work for " + (int)Seconds(js) + " s (" + (Background.CurrentJob ?? "?")
                                           + "). Logs, pokes and reports wait until it finishes.", true, null, true);
            }
            // since 0.12.0 the helper must be on time: an order's rules no later than the next in-game hour, the reports within a few
            // (run 20261025-1417-C2: 142 s behind the game, every order and shelter a game day late, and these were only counted)
            Background.NoteBehind();
            double orders = Background.OrdersBehind, reports = Background.ReportsBehind;
            if (ClockRunning && orders >= OrdersLateHours)
                Diag.Alarm("helper_late", "Our background helper is " + Math.Round(orders) + " in-game hours behind the game with the lords' orders (busy with: "
                                          + (Background.CurrentJob ?? "nothing") + "). Orders and shelters land late.", true, new { orders_behind = Math.Round(orders, 1), reports_behind = Math.Round(reports, 1) });
            else if (ClockRunning && reports >= ReportsLateHours)
                Diag.Alarm("helper_reports_late", "Our background helper is " + Math.Round(reports) + " in-game hours behind the game with the agents' reports (busy with: "
                                                  + (Background.CurrentJob ?? "nothing") + "). The agents see an old picture.", true, new { orders_behind = Math.Round(orders, 1), reports_behind = Math.Round(reports, 1) });
            if (Background.Queued > 1000) Diag.Alarm("helper_backlog", "Our background helper has " + Background.Queued + " pieces of work waiting.", true);
            long dropped = Interlocked.Read(ref Background.Dropped);
            if (dropped > _lastDropped)
            {
                Diag.Alarm("helper_dropped", (dropped - _lastDropped) + " pieces of background work were dropped because the queue was full.", true);
                _lastDropped = dropped;
            }
        }

        /// <summary>How far behind the game the helper may fall, in in-game hours, before it is a serious alarm (0.12.0).</summary>
        private const double OrdersLateHours = 2, ReportsLateHours = 6;

        /// <summary>Runs the freeze probe (at most five times a launch). Also used by agents.probe.</summary>
        public static string RunProbe(string why)
        {
            if (_probes >= 5) return "The probe already ran 5 times this launch; restart the game to run it again.";
            _probes++;
            _stallProbes++;
            string result = Probe.Run(why);
            Log.Line("Freeze probe: " + result);
            return result;
        }

        private static bool IsFront()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return InFront;
                GetWindowThreadProcessId(fg, out uint pid);
                return pid == _pid;
            }
            catch { return InFront; }
        }

        private static bool IsHung()
        {
            try { return _window != IntPtr.Zero && IsHungAppWindow(_window); }
            catch { return false; }
        }

        private static void FindWindow()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess()) _window = p.MainWindowHandle;
            }
            catch (Exception e) { Diag.Swallowed("watchdog: finding the game window", e); }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }

    /// <summary>
    /// Starts the freeze probe: a separate program (tools/FreezeProbe, installed in the module's probe folder) that
    /// samples the game's thread from outside, names where it is in the engine and in the game's own code, and writes
    /// freeze-(time).txt into this launch's log folder. From outside, it cannot deadlock the game it looks at.
    /// </summary>
    internal static class Probe
    {
        public static string Folder
        {
            get
            {
                string dll = typeof(Probe).Assembly.Location;   // ...\Modules\BannerlordAgents\bin\Win64_Shipping_Client\BannerlordAgents.dll
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dll) ?? ".", "..", "..", "probe"));
            }
        }

        public static string Exe => Path.Combine(Folder, "FreezeProbe.exe");

        public static string Run(string why)
        {
            try
            {
                if (Diag.SessionDir == null) return "there is no log folder to write into this launch";
                if (!File.Exists(Exe)) return "it is not installed (" + Exe + ")";
                string output = Path.Combine(Diag.SessionDir, "freeze-" + DateTime.Now.ToString("HHmmss") + ".txt");
                int pid;
                using (Process p = Process.GetCurrentProcess()) pid = p.Id;
                string args = "--pid " + pid + " --thread " + Watchdog.MainThreadNative + " --out " + Quote(output) + " --why " + Quote(why)
                              + " --state " + Quote(Watchdog.State) + " --doing " + Quote(Watchdog.Doing ?? "");
                var psi = new ProcessStartInfo(Exe, args) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Folder };
                using (Process.Start(psi)) { }
                Diag.Count("probe:runs");
                Diag.Event("probe", "started", new { why, output });
                return "running; it writes " + output;
            }
            catch (Exception e)
            {
                Diag.Swallowed("freeze probe", e);
                return "it could not start: " + e.Message;
            }
        }

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "'") + "\"";
    }
}
