using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// The strategists' flight recorder (docs/LOGGING.md). Any thread may report to it; only the background helper
    /// writes files, about once a second. Each game launch gets its own folder, logs\sessions\(date-time), holding:
    /// - story.log: the readable story (the same lines go to logs\bridge.log, which runs across launches);
    /// - trace.jsonl: everything, one JSON object per line, for searching;
    /// - health.csv: one row a minute;
    /// - errors.log: the full trace of the first three errors of each kind;
    /// - lords.log: what the game's AI does with the watched realm's lords, and why;
    /// - summary.txt: the session's totals, rewritten every 10 minutes and when the game closes;
    /// - blackbox-*.txt, freeze-*.txt, crash-*.txt, watchdog.log: written only when something goes wrong.
    /// Nothing handed to it may be a game object: only text and numbers, so the helper can write them safely.
    /// </summary>
    internal static class Diag
    {
        internal sealed class Ev
        {
            public long Wall;      // UTC ticks
            public double Hours;   // in-game hours, or -1
            public string Kind;
            public string What;
            public object Data;
            public string RunFile; // the test run's events.jsonl when this kind is the run's (Run.EventsFile at the time), else null
        }

        /// <summary>
        /// The kinds of the detailed log a test run keeps a copy of in its own events.jsonl (0.11, docs/RUNS.md): its own events,
        /// the orders, tools, pokes and answers, the lieutenant, your decisions, armies and notices, and what spoils or slows a
        /// run (alarms, errors, freezes, the fog and sight checks, speed, focus, saves and loads). Not the AI's reasons, the
        /// lords' plan changes or the hourly sight lines, which are many and stay in the launch's trace.
        /// </summary>
        private static readonly HashSet<string> RunKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "run", "order", "tool", "poke", "answer", "agent", "lieutenant", "decision", "recorder", "army", "notice", "takings", "wages", "purse", "storm",
            "alarm", "error", "freeze", "fog", "sight_check", "speed", "focus", "save", "load", "session", "history", "self_check", "pac", "command", "day"
        };

        /// <summary>Time spent on the game's thread by one of our hooks. Written by the game's thread only.</summary>
        internal sealed class HookStat
        {
            public string Name;
            public long Calls, Ticks, MaxTicks;
            /// <summary>A single call over this raises slow_call (0.11.3: 5 ms, but two frames, 33 ms, for the once-an-hour copy, which took 10 to 18 ms in every hour of run C1 and so alarmed every hour).</summary>
            public long LimitTicks = Stopwatch.Frequency / 200;
            /// <summary>The slowest call since the last health row; the helper reads and resets it.</summary>
            public long WindowMax;
            public long LastCalls, LastTicks;   // helper only: the totals at the previous health row
        }

        internal sealed class AlarmState
        {
            public string Code, LastText;
            public int Count;
            public bool Serious;
            public DateTime First, Last, LastScreen;
        }

        internal sealed class ErrorState
        {
            public string Where, Type, Message;
            public int Count;
            public DateTime First, Last;
        }

        internal sealed class SampleStat
        {
            public long Count;
            public double Sum, Max = double.MinValue, Min = double.MaxValue;
            public readonly List<double> Values = new List<double>();
        }

        private struct LineItem
        {
            public long Wall;
            public string Text;
        }

        public static string SessionDir { get; private set; }
        public static readonly DateTime LaunchStarted = DateTime.Now;
        /// <summary>Set at start-up when the previous launch did not close cleanly.</summary>
        public static string PreviousLaunchNote;
        /// <summary>In-game hours as last seen by the campaign tick, or -1. Any thread may read it.</summary>
        public static double Hours = -1;
        /// <summary>Managed id of the game's thread, set on its first frame.</summary>
        public static int MainThread;
        public static string Version => typeof(Diag).Assembly.GetName().Version.ToString(3);

        private static readonly ConcurrentQueue<LineItem> Lines = new ConcurrentQueue<LineItem>();
        private static readonly ConcurrentQueue<Ev> Trace = new ConcurrentQueue<Ev>();
        private static readonly ConcurrentQueue<string> ErrorTexts = new ConcurrentQueue<string>();
        private static readonly ConcurrentQueue<string> LordLines = new ConcurrentQueue<string>();
        private const int RingSize = 1000;
        private static readonly Ev[] Ring = new Ev[RingSize];
        private static long _ringNext;
        private static readonly ConcurrentDictionary<string, AlarmState> Alarms = new ConcurrentDictionary<string, AlarmState>();
        private static readonly ConcurrentDictionary<string, ErrorState> Errors = new ConcurrentDictionary<string, ErrorState>();
        private static readonly ConcurrentDictionary<string, long[]> Counters = new ConcurrentDictionary<string, long[]>();
        private static readonly ConcurrentDictionary<string, SampleStat> Samples = new ConcurrentDictionary<string, SampleStat>();
        private static readonly Dictionary<string, HookStat> HookByName = new Dictionary<string, HookStat>();
        private static HookStat[] _hooks = new HookStat[0];
        private static readonly HookStat Logging = Hook("the logging itself");
        private static readonly object WriteGate = new object();
        private static StreamWriter _trace, _errors, _lords, _health;
        private static long _traceBytes;
        private static bool _traceCapped;
        private const long TraceCap = 200L * 1024 * 1024;
        private static long _lastScreen;
        private static int _ended, _drainQueued;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        // ---------- the launch ----------

        /// <summary>Makes this launch's folder, notes whether the previous launch closed cleanly, and keeps the newest 30.</summary>
        public static string StartLaunch()
        {
            try
            {
                string root = Paths.Sessions;
                string name = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dir = Path.Combine(root, name);
                for (int i = 2; Directory.Exists(dir); i++) dir = Path.Combine(root, name + "-" + i);
                Directory.CreateDirectory(dir);
                string previous = Directory.GetDirectories(root).Where(d => d != dir).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
                if (previous != null)
                {
                    string marker = Path.Combine(previous, "running.marker");
                    if (File.Exists(marker))
                    {
                        DateTime last = Directory.GetFiles(previous).Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max();
                        string left = string.Join(", ", Directory.GetFiles(previous, "blackbox-*.txt").Concat(Directory.GetFiles(previous, "freeze-*.txt"))
                                                                 .Concat(Directory.GetFiles(previous, "crash-*.txt")).Select(Path.GetFileName));
                        PreviousLaunchNote = "The previous launch (" + Path.GetFileName(previous) + ") did not close cleanly: the game crashed or was stopped. "
                                             + "Its last file was written at " + last.ToString("yyyy-MM-dd HH:mm:ss") + "."
                                             + (left.Length > 0 ? " It left: " + left + "." : "");
                        try { File.Delete(marker); } catch (Exception e) { Swallowed("previous launch marker", e); }
                    }
                }
                File.WriteAllText(Path.Combine(dir, "running.marker"),
                    "Bannerlord Agents started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ". This file is removed when the game closes normally.");
                SessionDir = dir;
                // keep the newest 30 launches, and no more than 2 GB of them in all
                long total = 0;
                int kept = 0;
                foreach (string old in Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal))
                {
                    long size = 0;
                    try { size = new DirectoryInfo(old).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
                    catch (Exception e) { Swallowed("measuring old session folders", e); }
                    if (old == dir || (kept < 30 && total + size <= 2L * 1024 * 1024 * 1024))
                    {
                        kept++;
                        total += size;
                        continue;
                    }
                    try { Directory.Delete(old, true); } catch (Exception e) { Swallowed("old session folders", e); }
                }
            }
            catch (Exception e)
            {
                SessionDir = null;
                Log.Line("The session folder could not be made, so the detailed logs are off this launch: " + e.Message);
            }
            return PreviousLaunchNote;
        }

        /// <summary>Once, when the game closes normally: the final summary, every queued line, and the marker removed.</summary>
        public static void EndLaunch(string how)
        {
            if (Interlocked.Exchange(ref _ended, 1) == 1) return;
            try
            {
                Event("session", "end", new { how });
                Summary(true);
                Drain();
                lock (WriteGate)
                {
                    foreach (StreamWriter w in new[] { _trace, _errors, _lords, _health, _runWriter })
                    {
                        try { w?.Dispose(); } catch { /* closing anyway */ }
                    }
                    _trace = _errors = _lords = _health = _runWriter = null;
                }
                if (SessionDir != null) File.Delete(Path.Combine(SessionDir, "running.marker"));
                Log.Close();
            }
            catch { /* the game is closing; nothing more can be reported */ }
        }

        /// <summary>An exception nobody caught. Written at once, without the helper, because the process may be ending.</summary>
        public static void OnUnhandled(Exception e, bool terminating)
        {
            try
            {
                if (SessionDir == null) return;
                string path = Path.Combine(SessionDir, "crash-" + DateTime.Now.ToString("HHmmss") + ".txt");
                File.WriteAllText(path, "An exception nobody caught" + (terminating ? ", and the game is ending because of it" : "") + ", at "
                                        + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ":\n\n" + e + "\n");
                Emergency("!! An exception nobody caught: " + e?.GetType().Name + ": " + e?.Message + " (" + Path.GetFileName(path) + ")");
                DumpBlackBox("an exception nobody caught: " + e?.GetType().Name);
                Drain();
            }
            catch { /* nothing left to report with */ }
        }

        // ---------- reporting (any thread) ----------

        /// <summary>One event for trace.jsonl and the black box. Data must hold only text and numbers.</summary>
        public static void Event(string kind, string what, object data = null)
        {
            bool main = MainThread != 0 && Thread.CurrentThread.ManagedThreadId == MainThread;
            long t = main ? Stopwatch.GetTimestamp() : 0;
            string runFile = Run.EventsFile;
            // the run's copy leaves out the many small lines: an order's every hour, a lord's look-up, a tool's every step
            bool forRun = runFile != null && RunKinds.Contains(kind) && !(kind == "order" && (what == "hour" || what == "lord lookup")) && !(kind == "tool" && what == "step");
            var e = new Ev { Wall = DateTime.UtcNow.Ticks, Hours = Hours, Kind = kind, What = what, Data = data, RunFile = forRun ? runFile : null };
            Keep(e);
            if (!_traceCapped || kind == "alarm" || kind == "error" || e.RunFile != null) Trace.Enqueue(e);
            if (main) Took(Logging, t, false);
        }

        /// <summary>A line of the readable story, kept for the black box.</summary>
        public static void Remember(string line) => Keep(new Ev { Wall = DateTime.UtcNow.Ticks, Hours = Hours, Kind = "log", What = line });

        internal static void QueueLine(long wall, string text) => Lines.Enqueue(new LineItem { Wall = wall, Text = text });

        /// <summary>A line for lords.log: what the game's AI does with the watched realm's lords, and why.</summary>
        public static void Lord(string line)
        {
            Keep(new Ev { Wall = DateTime.UtcNow.Ticks, Hours = Hours, Kind = "lord", What = line });
            LordLines.Enqueue(line);
        }

        private static void Keep(Ev e)
        {
            long i = Interlocked.Increment(ref _ringNext) - 1;
            Ring[(int)(i % RingSize)] = e;
        }

        /// <summary>
        /// Something is wrong. The first three of each kind are written in full, then every tenth; all are counted.
        /// A serious alarm also shows on screen (at most one a minute) and writes the black box the first time.
        /// "direct" writes without the helper, for when the helper itself is stuck.
        /// </summary>
        public static void Alarm(string code, string text, bool serious = false, object data = null, bool direct = false)
        {
            AlarmState st = Alarms.GetOrAdd(code, c => new AlarmState { Code = c, First = DateTime.Now });
            int n = Interlocked.Increment(ref st.Count);
            st.Last = DateTime.Now;
            st.LastText = text;
            if (serious) st.Serious = true;
            Event("alarm", code, new { text, n, serious, data });
            if (n <= 3 || n % 10 == 0)
            {
                string line = "!! " + text + (n > 3 ? " (" + n + " times so far)" : n == 3 ? " (from now on this kind is counted, and written every tenth time)" : "");
                if (direct) Emergency(line);
                else Log.Line(line);
            }
            if (!serious) return;
            if (n == 1 || (DateTime.Now - st.LastScreen).TotalMinutes >= 10)
            {
                st.LastScreen = DateTime.Now;
                Screen(text);
            }
            if (n == 1)
            {
                if (direct || !Background.Running || Background.IsWorker) DumpBlackBox("alarm: " + code);
                else Background.Post("black box", () => DumpBlackBox("alarm: " + code));
            }
        }

        /// <summary>A red line on screen, at most one a minute; the rest are counted.</summary>
        public static void Screen(string text)
        {
            if (!Settings.ScreenAlarms) return;
            long now = Stopwatch.GetTimestamp();
            long last = Interlocked.Read(ref _lastScreen);
            if (last != 0 && now - last < Stopwatch.Frequency * 60) { Count("screen alarms held back"); return; }
            Interlocked.Exchange(ref _lastScreen, now);
            Background.ToUi.Enqueue(() => Ui.Say("Problem: " + text + " (agents.health for more)", Colors.Red));
        }

        /// <summary>An error our code met. The first three of each kind go to errors.log with the full trace.</summary>
        public static void Error(string where, Exception e, bool handled = false)
        {
            if (e == null) return;
            string key = where + "|" + e.GetType().FullName + "|" + (e.TargetSite?.Name ?? "");
            ErrorState st = Errors.GetOrAdd(key, k => new ErrorState { Where = where, Type = e.GetType().Name, Message = e.Message, First = DateTime.Now });
            int n = Interlocked.Increment(ref st.Count);
            st.Last = DateTime.Now;
            Event("error", where, new { type = e.GetType().Name, message = e.Message, n, handled });
            if (n > 3) return;
            ErrorTexts.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + where + (handled ? " (handled)" : "") + ", #" + n + Environment.NewLine + e + Environment.NewLine);
            Log.Line((handled ? "error (handled) in " : "ERROR in ") + where + ": " + e.GetType().Name + ": " + e.Message
                     + (n == 3 ? " (further ones of this kind are only counted)" : ""));
        }

        /// <summary>An error that has a safe fallback. It used to vanish without a word; now it is counted and the first few written.</summary>
        public static void Swallowed(string where, Exception e) => Error(where, e, true);

        public static void Count(string name, long by = 1)
        {
            long[] c = Counters.GetOrAdd(name, _ => new long[1]);
            Interlocked.Add(ref c[0], by);
        }

        public static long CountOf(string name) => Counters.TryGetValue(name, out long[] c) ? Interlocked.Read(ref c[0]) : 0;

        public static void Sample(string name, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return;
            SampleStat s = Samples.GetOrAdd(name, _ => new SampleStat());
            lock (s)
            {
                s.Count++;
                s.Sum += v;
                if (v > s.Max) s.Max = v;
                if (v < s.Min) s.Min = v;
                if (s.Values.Count < 5000) s.Values.Add(v);
            }
        }

        // ---------- time on the game's thread ----------

        /// <summary>The timer for one of our hooks. Game's thread only.</summary>
        public static HookStat Hook(string name, double limitMs = 5)
        {
            if (HookByName.TryGetValue(name, out HookStat h)) return h;
            h = new HookStat { Name = name, LimitTicks = (long)(Stopwatch.Frequency * limitMs / 1000.0) };
            HookByName[name] = h;
            var arr = new HookStat[_hooks.Length + 1];
            Array.Copy(_hooks, arr, _hooks.Length);
            arr[_hooks.Length] = h;
            _hooks = arr;
            return h;
        }

        /// <summary>Adds one call's time; a single call over its limit (5 ms for most) is a pause a player can feel, so it raises an alarm.</summary>
        public static void Took(HookStat h, long start, bool alarm = true)
        {
            long d = Stopwatch.GetTimestamp() - start;
            h.Calls++;
            h.Ticks += d;
            if (d > h.MaxTicks) h.MaxTicks = d;
            if (d > Interlocked.Read(ref h.WindowMax)) Interlocked.Exchange(ref h.WindowMax, d);
            if (alarm && d > h.LimitTicks)
                Alarm("slow_call", "One call of ours on the game's thread took " + Ms(d).ToString("0.0", CultureInfo.InvariantCulture) + " ms (" + h.Name
                                   + "). A pause that long can be felt.", false, new { hook = h.Name, ms = Math.Round(Ms(d), 2) });
        }

        public static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        // ---------- writing (the helper) ----------

        /// <summary>Asks the helper to write what is queued; called once a second by the watchdog.</summary>
        public static void RequestDrain()
        {
            if (Interlocked.Exchange(ref _drainQueued, 1) == 1) return;
            Background.Post("log writing", () =>
            {
                try { Drain(); }
                finally { Interlocked.Exchange(ref _drainQueued, 0); }
            });
        }

        public static void Drain()
        {
            while (Lines.TryDequeue(out LineItem li)) Log.Write(li.Wall, li.Text);
            Log.Flush();
            lock (WriteGate)
            {
                if (SessionDir == null)
                {
                    while (Trace.TryDequeue(out _)) { }
                    while (ErrorTexts.TryDequeue(out _)) { }
                    while (LordLines.TryDequeue(out _)) { }
                    return;
                }
                try
                {
                    if (!Trace.IsEmpty)
                    {
                        StreamWriter w = Writer(ref _trace, "trace.jsonl");
                        while (Trace.TryDequeue(out Ev e))
                        {
                            string line = TraceLine(e);
                            // past the cap, only alarms and errors go on in the trace; a run's copy is kept whole
                            if (!_traceCapped || e.Kind == "alarm" || e.Kind == "error")
                            {
                                w.WriteLine(line);
                                _traceBytes += line.Length + 2;
                            }
                            if (e.RunFile != null) RunLine(e.RunFile, line);
                        }
                        w.Flush();
                        _runWriter?.Flush();
                        if (!_traceCapped && _traceBytes > TraceCap)
                        {
                            _traceCapped = true;
                            Log.Line("!! The detailed record (trace.jsonl) passed 200 MB this launch; from now on it keeps only alarms and errors.");
                        }
                    }
                    if (!ErrorTexts.IsEmpty)
                    {
                        StreamWriter w = Writer(ref _errors, "errors.log");
                        while (ErrorTexts.TryDequeue(out string t)) w.WriteLine(t);
                        w.Flush();
                    }
                    if (!LordLines.IsEmpty)
                    {
                        StreamWriter w = Writer(ref _lords, "lords.log");
                        while (LordLines.TryDequeue(out string t)) w.WriteLine(t);
                        w.Flush();
                    }
                }
                catch (Exception e) { Emergency("the detailed logs could not be written: " + e.Message); }
            }
        }

        private static StreamWriter _runWriter;
        private static string _runWriterPath;

        /// <summary>A line for the test run's events.jsonl (under WriteGate), its file opened anew when the run changes.</summary>
        private static void RunLine(string path, string line)
        {
            try
            {
                if (_runWriter == null || _runWriterPath != path)
                {
                    try { _runWriter?.Dispose(); } catch { /* the old run's file is closed anyway */ }
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    _runWriter = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8);
                    _runWriterPath = path;
                }
                _runWriter.WriteLine(line);
            }
            catch (Exception e) { Emergency("the test run's events could not be written: " + e.Message); }
        }

        private static StreamWriter Writer(ref StreamWriter w, string file)
        {
            if (w == null) w = new StreamWriter(new FileStream(Path.Combine(SessionDir, file), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8);
            return w;
        }

        private static string TraceLine(Ev e)
        {
            string t = Local(e.Wall, "yyyy-MM-ddTHH:mm:ss.fffzzz");
            double? h = e.Hours >= 0 ? Math.Round(e.Hours, 3) : (double?)null;
            try { return Json.Line(new { T = t, H = h, K = e.Kind, W = e.What, D = e.Data }); }
            catch (Exception x) { return Json.Line(new { T = t, H = h, K = e.Kind, W = e.What, D = "(could not be written: " + x.Message + ")" }); }
        }

        private static string Local(long utcTicks, string format) => new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Straight to watchdog.log, without the helper: for when the helper itself is stuck.</summary>
        public static void Emergency(string line)
        {
            try
            {
                if (SessionDir == null) return;
                lock (Ring) File.AppendAllText(Path.Combine(SessionDir, "watchdog.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + line + Environment.NewLine);
            }
            catch { /* nowhere else to write */ }
        }

        // ---------- the black box ----------

        /// <summary>
        /// The last 1,000 things the mod saw and did, with the state right now, in a file. Safe from any thread except
        /// the game's (which should post it to the helper), because it reads only the mod's own records.
        /// </summary>
        public static string DumpBlackBox(string reason)
        {
            if (SessionDir == null) return null;
            try
            {
                string path = Path.Combine(SessionDir, "blackbox-" + DateTime.Now.ToString("HHmmss-fff") + ".txt");
                var sb = new StringBuilder(128 * 1024);
                sb.AppendLine("BLACK BOX, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + ". Why: " + reason);
                sb.AppendLine(StateLines());
                sb.AppendLine("LAST THINGS THE MOD SAW AND DID (oldest first)");
                long next = Interlocked.Read(ref _ringNext);
                for (long i = Math.Max(0, next - RingSize); i < next; i++)
                {
                    Ev e = Ring[(int)(i % RingSize)];
                    if (e == null) continue;
                    sb.Append(Local(e.Wall, "HH:mm:ss.fff")).Append("  ");
                    if (e.Hours >= 0) sb.Append("h").Append(e.Hours.ToString("0.00", CultureInfo.InvariantCulture)).Append("  ");
                    if (e.Kind == "log" || e.Kind == "lord") sb.AppendLine(e.What);
                    else sb.AppendLine(e.Kind + " " + e.What + (e.Data != null ? " " + SafeJson(e.Data) : ""));
                }
                File.WriteAllText(path, sb.ToString(), Utf8);
                Count("black boxes written");
                Event("blackbox", Path.GetFileName(path), new { reason });
                return path;
            }
            catch (Exception e)
            {
                Emergency("the black box could not be written: " + e.Message);
                return null;
            }
        }

        private static string SafeJson(object o)
        {
            try { return Json.Line(o); } catch { return "(unreadable)"; }
        }

        /// <summary>The state right now, from the mod's own records only (no game objects).</summary>
        public static string StateLines()
        {
            var sb = new StringBuilder();
            double frameAgo = Watchdog.LastFrame == 0 ? -1 : Watchdog.Seconds(Watchdog.LastFrame);
            double tickAgo = Watchdog.LastCampaignTick == 0 ? -1 : Watchdog.Seconds(Watchdog.LastCampaignTick);
            sb.AppendLine("Game: " + Watchdog.State + "; speed " + (Watchdog.Speed.Length > 0 ? Watchdog.Speed : "n/a") + "; the clock is "
                          + (Watchdog.ClockRunning ? "running" : "stopped") + "; the window is " + (Watchdog.InFront ? "in front" : "in the background")
                          + (Hours >= 0 ? "; in-game " + Text.When(Hours) : "") + ".");
            sb.AppendLine("The game's thread last moved " + Secs(frameAgo) + " ago; the campaign last ticked " + Secs(tickAgo) + " ago. Our code on it right now: "
                          + (Watchdog.Doing ?? "none") + ".");
            string job = Background.CurrentJob;
            long started = Background.JobStarted;
            sb.AppendLine("Background helper: " + (job != null && started != 0 ? "working on '" + job + "' for " + Secs(Watchdog.Seconds(started)) : "idle")
                          + "; queued " + Background.Queued + ", done " + Background.Ran + ", dropped " + Background.Dropped + ", failed " + Background.Failed + ".");
            sb.AppendLine("Orders held: " + Live.OrdersText);
            sb.AppendLine("Agent channel: " + AgentChannel.StatusLine());
            List<AlarmState> alarms = Alarms.Values.OrderByDescending(a => a.Last).ToList();
            sb.AppendLine("Alarms this launch: " + (alarms.Count == 0 ? "none" : string.Join("; ", alarms.Select(a => a.Code + " x" + a.Count))));
            return sb.ToString();
        }

        private static string Secs(double s) => s < 0 ? "never" : s.ToString(s < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";

        // ---------- health, once a minute (the helper) ----------

        private static long _gc0, _gc1, _gc2, _frames, _ticks, _helperCpuMs = -1, _lastAnswers, _lastPokes;

        public static void Health()
        {
            if (SessionDir == null) return;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                HookStat[] hooks = _hooks;
                var minute = new List<KeyValuePair<string, double>>();
                double ourMs = 0, worst = 0;
                string worstHook = "";
                foreach (HookStat h in hooks)
                {
                    long calls = h.Calls, ticks = h.Ticks;
                    double ms = Ms(ticks - h.LastTicks);
                    h.LastCalls = calls;
                    h.LastTicks = ticks;
                    long wmax = Interlocked.Exchange(ref h.WindowMax, 0);
                    if (h != Logging) ourMs += ms;
                    if (ms > 0) minute.Add(new KeyValuePair<string, double>(h.Name, ms));
                    if (Ms(wmax) > worst) { worst = Ms(wmax); worstHook = h.Name; }
                }
                string top = string.Join(";", minute.OrderByDescending(x => x.Value).Take(4).Select(x => x.Key.Replace(',', ' ') + ":" + x.Value.ToString("0.00", inv)));
                long gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                long frames = Watchdog.Frames, ticks2 = Watchdog.CampaignTicks;
                long cpu = Cpu.ThreadCpuMs();
                long helperMs = _helperCpuMs < 0 || cpu < 0 ? 0 : cpu - _helperCpuMs;
                _helperCpuMs = cpu;
                long privateMb = 0, threads = 0, handles = 0;
                using (Process p = Process.GetCurrentProcess())
                {
                    privateMb = p.PrivateMemorySize64 / (1024 * 1024);
                    threads = p.Threads.Count;
                    handles = p.HandleCount;
                }
                double eff = Background.EfficientShare;
                long answers = AgentChannel.AnswerFiles, pokes = Brain.PokesWatched;
                string row = string.Join(",", new[]
                {
                    DateTime.Now.ToString("HH:mm:ss", inv),
                    Hours >= 0 ? Hours.ToString("0.00", inv) : "",
                    Csv(Watchdog.State), Csv(Watchdog.Speed), Watchdog.ClockRunning ? "yes" : "no", Watchdog.InFront ? "front" : "back",
                    (frames - _frames).ToString(inv), (ticks2 - _ticks).ToString(inv),
                    ourMs.ToString("0.00", inv), Csv(top), worst.ToString("0.00", inv), Csv(worstHook),
                    Background.Ran.ToString(inv), Background.Queued.ToString(inv), Ms(Interlocked.Exchange(ref Background.MaxJobTicks, 0)).ToString("0.0", inv),
                    helperMs.ToString(inv), double.IsNaN(eff) ? "" : eff.ToString("0.00", inv),
                    Live.OrdersHeld.ToString(inv), (pokes - _lastPokes).ToString(inv), (answers - _lastAnswers).ToString(inv),
                    AgentChannel.WaitingSeconds.ToString("0", inv),
                    Alarms.Values.Sum(a => a.Count).ToString(inv), Errors.Values.Sum(x => x.Count).ToString(inv),
                    (gc0 - _gc0).ToString(inv), (gc1 - _gc1).ToString(inv), (gc2 - _gc2).ToString(inv),
                    privateMb.ToString(inv), threads.ToString(inv), handles.ToString(inv), Files.OpenCount.ToString(inv), Names.Count.ToString(inv),
                    (_traceBytes / (1024.0 * 1024.0)).ToString("0.0", inv)
                });
                _gc0 = gc0; _gc1 = gc1; _gc2 = gc2; _frames = frames; _ticks = ticks2; _lastAnswers = answers; _lastPokes = pokes;
                lock (WriteGate)
                {
                    bool fresh = _health == null && !File.Exists(Path.Combine(SessionDir, "health.csv"));
                    StreamWriter w = Writer(ref _health, "health.csv");
                    if (fresh)
                        w.WriteLine("time,game_hours,state,speed,clock_running,window,frames,campaign_ticks,our_ms_on_game_thread,top_hooks_ms,slowest_call_ms,slowest_call,"
                                    + "helper_jobs_total,helper_queued,helper_slowest_job_ms,helper_cpu_ms,helper_on_efficiency_cores,orders_held,pokes,answers,"
                                    + "waiting_for_answer_s,alarms_total,errors_total,gc0,gc1,gc2,private_mb,threads,handles,open_files,names_cached,trace_mb");
                    w.WriteLine(row);
                    w.Flush();
                }
            }
            catch (Exception e) { Swallowed("health row", e); }
        }

        private static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';').Replace('\n', ' ');

        // ---------- the summary ----------

        /// <summary>The session's totals in plain words: summary.txt every 10 minutes, and at the end into the story too.</summary>
        public static string Summary(bool final)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            TimeSpan up = DateTime.Now - LaunchStarted;
            sb.AppendLine("SESSION SUMMARY: Bannerlord Agents " + Version + ", launch of " + LaunchStarted.ToString("yyyy-MM-dd HH:mm") + ", "
                          + (int)up.TotalMinutes + " min " + (final ? "(final, the game closed)" : "so far") + ".");
            sb.AppendLine("Time: " + Minutes("time:clock running") + " with the clock running, " + Minutes("time:paused on the map") + " paused on the map, "
                          + Minutes("time:in a menu") + " in menus, " + Minutes("time:battle or scene") + " in battles or scenes, " + Minutes("time:other screens")
                          + " on other screens, " + Minutes("time:no campaign") + " outside a campaign; the game window was in the background for " + Minutes("time:window in the background") + ".");
            sb.AppendLine("Campaign loads: " + CountOf("campaign loads") + ". In-game hours covered: " + CountOf("hours") + ".");
            sb.AppendLine("Pokes for the watched realm: " + CountOf("pokes") + Breakdown("poke:") + ". Reports: " + SampleText("report:tokens", "tokens", "0")
                          + "; the 40-party limit hid parties " + CountOf("report:cap hit") + " times.");
            sb.AppendLine("Not poked: " + CountOf("quiet") + " notices kept to the logs" + Breakdown("quiet:") + "; enemy lords newly seen far off "
                          + CountOf("sighting:far off") + ".");
            sb.AppendLine("Fog check: " + CountOf("fog:reports") + " reports checked, " + CountOf("fog:leaks") + " leaks. Sight check: " + CountOf("sight:runs")
                          + " runs, our rule agreed with the game on " + CountOf("sight:agree") + " of " + CountOf("sight:parties") + " parties.");
            sb.AppendLine("Agents (" + string.Join(", ", Agents.All.Select(a => (a.Realm ?? "?") + " " + a.Config.Mode)) + "): " + CountOf("agent:files") + " answer files, " + CountOf("agent:orders") + " orders (" + CountOf("agent:applied")
                          + " applied, " + CountOf("agent:checked") + " checked only, " + CountOf("agent:refused") + " refused); answer time " + SampleText("agent:answer seconds", "s", "0")
                          + "; answers to a poke already replaced " + CountOf("agent:outdated") + "; second answers to one poke " + CountOf("agent:duplicate")
                          + "; files set aside " + CountOf("agent:set aside") + "; answers from before a load " + CountOf("agent:before load")
                          + "; ids it was never shown " + CountOf("agent:unknown id") + "; silences " + CountOf("agent:silences")
                          + "; orders that waited for the clock " + CountOf("agent:waited for clock") + ".");
            sb.AppendLine("Orders: given " + CountOf("order:issued") + Breakdown("order source:") + "; arrived " + CountOf("order:arrived") + "; ended " + CountOf("order:ended")
                          + Breakdown("order end:") + "; given again " + CountOf("order:reissued") + " times; not taken " + CountOf("order:not taken")
                          + "; stuck " + CountOf("order:stuck") + "; fought over " + CountOf("order:fought") + "; still held after release " + CountOf("order:held after release") + ".");
            sb.AppendLine("Saves: " + CountOf("save:count") + ", lords handed back " + CountOf("save:handed back") + ", hand-back failures " + CountOf("save:failures")
                          + ". Load checks: " + CountOf("load:checks") + ", lords found held at load " + CountOf("load:held lords") + ".");
            sb.AppendLine("The game's AI (watched realm): weighed its options " + CountOf("ai:thoughts") + " times and changed plan " + CountOf("ai:changes")
                          + " times; lord plan changes seen " + CountOf("lord:changes") + "; movement-watcher records " + CountOf("witness:records") + ".");
            sb.AppendLine("Your play: decisions recorded " + CountOf("record:decisions") + ", events " + CountOf("record:events") + ".");
            sb.AppendLine("Freezes of the game's thread (20 s or more): " + CountOf("freeze:count") + (CountOf("freeze:count") > 0 ? ", longest " + SampleMax("freeze:seconds") + " s" : "")
                          + "; probes run " + CountOf("probe:runs") + ".");
            HookStat[] hooks = _hooks;
            double total = hooks.Where(h => h != Logging).Sum(h => Ms(h.Ticks));
            long hours = Math.Max(1, CountOf("hours"));
            HookStat slow = hooks.OrderByDescending(h => h.MaxTicks).FirstOrDefault();
            sb.AppendLine("Cost on the game's thread: " + total.ToString("0.0", inv) + " ms in all, " + (total / hours).ToString("0.000", inv) + " ms per in-game hour"
                          + (slow != null ? "; slowest single call " + Ms(slow.MaxTicks).ToString("0.0", inv) + " ms (" + slow.Name + ")" : "")
                          + "; the logging itself " + Ms(Logging.Ticks).ToString("0.0", inv) + " ms.");
            sb.AppendLine("  by hook: " + string.Join("; ", hooks.Where(h => h.Calls > 0).OrderByDescending(h => h.Ticks)
                                                            .Select(h => h.Name + " " + Ms(h.Ticks).ToString("0.0", inv) + " ms over " + h.Calls + " calls")));
            double eff = Background.EfficientShare;
            sb.AppendLine("Background helper: " + Background.Ran + " jobs, " + Background.Dropped + " dropped, " + Background.Failed + " failed; "
                          + (double.IsNaN(eff) ? "" : (eff * 100).ToString("0", inv) + "% of its work on the efficiency cores; ") + Background.Placement + "; urgent work " + Cpu.UrgentPlacement + ".");
            // its hour since 0.12.0: the urgent part (the order rules and the look) and the reports of the newest hour, apart
            sb.AppendLine("  each hour's orders and look: " + SampleText("helper:orders ms", "ms", "0.0") + "; report rounds: " + SampleText("helper:reports ms", "ms", "0.0")
                          + "; hours folded into a later report " + CountOf("reports:hours folded") + "; council briefings: " + SampleText("helper:briefing ms", "ms", "0.0") + ".");
            sb.AppendLine("  look-up files written " + CountOf("files:look-ups written") + ", left alone unchanged " + CountOf("files:look-ups unchanged")
                          + "; swaps tried again " + CountOf("files:swap tried again") + ", files written in place " + CountOf("files:rewritten in place")
                          + "; same order kept as it was " + CountOf("order:kept as it was") + ".");
            List<AlarmState> alarms = Alarms.Values.OrderByDescending(a => a.Serious).ThenByDescending(a => a.Count).ToList();
            sb.AppendLine("Alarms: " + (alarms.Count == 0 ? "none." : ""));
            foreach (AlarmState a in alarms) sb.AppendLine("  " + (a.Serious ? "SERIOUS " : "") + a.Code + " x" + a.Count + ", last at " + a.Last.ToString("HH:mm:ss") + ": " + a.LastText);
            List<ErrorState> errors = Errors.Values.OrderByDescending(x => x.Count).ToList();
            sb.AppendLine("Errors: " + (errors.Count == 0 ? "none." : ""));
            foreach (ErrorState x in errors) sb.AppendLine("  " + x.Where + ": " + x.Type + " x" + x.Count + " (" + x.Message + ")");
            sb.AppendLine("Files: " + (SessionDir ?? "(no session folder)"));
            string text = sb.ToString();
            try
            {
                if (SessionDir != null) File.WriteAllText(Path.Combine(SessionDir, "summary.txt"), text, Utf8);
                if (final) foreach (string line in text.Replace("\r", "").Split('\n')) if (line.Length > 0) Log.Line("summary: " + line);
            }
            catch (Exception e) { Swallowed("summary", e); }
            return text;
        }

        private static string Minutes(string counter) => (CountOf(counter) / 60.0).ToString("0", CultureInfo.InvariantCulture) + " min";

        private static string Breakdown(string prefix)
        {
            List<string> parts = Counters.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Value[0] > 0)
                                         .OrderByDescending(kv => kv.Value[0]).Select(kv => kv.Key.Substring(prefix.Length) + " " + kv.Value[0]).ToList();
            return parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
        }

        private static string SampleText(string name, string unit, string format)
        {
            if (!Samples.TryGetValue(name, out SampleStat s) || s.Count == 0) return "none yet";
            lock (s)
            {
                List<double> sorted = s.Values.OrderBy(x => x).ToList();
                double median = sorted[sorted.Count / 2];
                var inv = CultureInfo.InvariantCulture;
                return "median " + median.ToString(format, inv) + " " + unit + ", average " + (s.Sum / s.Count).ToString(format, inv) + ", largest " + s.Max.ToString(format, inv) + " (" + s.Count + ")";
            }
        }

        private static string SampleMax(string name)
        {
            if (!Samples.TryGetValue(name, out SampleStat s) || s.Count == 0) return "0";
            lock (s) return s.Max.ToString("0", CultureInfo.InvariantCulture);
        }

        // ---------- for the console ----------

        public static string HealthText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Logs: " + Settings.Detail + " detail; this launch's folder: " + (SessionDir ?? "(none)"));
            sb.Append(StateLines());
            List<AlarmState> alarms = Alarms.Values.OrderByDescending(a => a.Last).ToList();
            if (alarms.Count == 0) sb.AppendLine("No alarms this launch.");
            foreach (AlarmState a in alarms.Take(12))
                sb.AppendLine("  " + (a.Serious ? "SERIOUS " : "") + a.Code + " x" + a.Count + ", last at " + a.Last.ToString("HH:mm:ss") + ": " + a.LastText);
            List<ErrorState> errors = Errors.Values.OrderByDescending(x => x.Last).ToList();
            if (errors.Count > 0) sb.AppendLine("Errors: " + string.Join("; ", errors.Take(8).Select(x => x.Where + " " + x.Type + " x" + x.Count)));
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Plain copies of game-side state that the logs read from other threads (the black box, the health row). Kept
    /// apart from the classes that hold game objects, so reading them never touches the game.
    /// </summary>
    internal static class Live
    {
        /// <summary>A campaign session is running (Bridge.Start to Bridge.Stop).</summary>
        public static volatile bool CampaignRunning;
        public static volatile int OrdersHeld;
        public static volatile string OrdersText = "none";
    }
}
