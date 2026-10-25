using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace BannerlordAgents
{
    /// <summary>
    /// The bridge's one background thread. The game's thread only copies numbers and hands them over here; this
    /// thread works out views and pokes, builds reports and writes every file, the logs included. It runs on the
    /// efficiency cores (see Cpu), and it never touches a game object. It starts when the module loads, so even
    /// start-up lines are written off the game's thread.
    ///
    /// Three lanes since 0.12.0 (run 20261025-1417-C2: the helper fell up to 142 s behind the game, so every order, every
    /// shelter and every poke landed a game day late). One thread still does it all, so nothing it keeps is shared:
    /// - urgent (PostFirst): what moves a lord - the agents' answers, an order's start, change and end, and each hour's
    ///   order rules (Brain.Decide). Always first, at normal priority and out of Windows' efficiency mode;
    /// - regular (Post): logs, records and the rest, first come first served, low priority in efficiency mode;
    /// - reporting (Publisher): the views, pokes, look-ups and dashboard of the newest hour only, one small step at a
    ///   time when nothing else waits, so an urgent job waits at most one step and old hours never pile up.
    /// </summary>
    internal static class Background
    {
        private struct Job
        {
            public string Name;
            public Action Run;
        }

        private static readonly BlockingCollection<Job> Jobs = new BlockingCollection<Job>(new ConcurrentQueue<Job>(), 4096);
        // jobs that go before the queue (0.11.3: the look at the agents' answer folders, which waited behind up to 621 jobs at run C1's start, so answers waited up to 30 s; 0.12.0: every order and each hour's order rules)
        private static readonly ConcurrentQueue<Job> First = new ConcurrentQueue<Job>();

        /// <summary>The reporting lane's next step, or null when there is none (Brain.NextStep; set by the bridge, never by the smoke test).</summary>
        public static Func<Action> Publisher;

        // the hours, for how far behind the game the helper is (game's thread writes the first, the helper the others)
        private static long _postedHours = BitConverter.DoubleToInt64Bits(double.NaN), _decidedHours = BitConverter.DoubleToInt64Bits(double.NaN), _publishedHours = BitConverter.DoubleToInt64Bits(double.NaN);
        /// <summary>The newest in-game hour the game's thread handed over.</summary>
        public static double PostedHours => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _postedHours));
        /// <summary>The newest hour whose order rules have run.</summary>
        public static double DecidedHours
        {
            get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _decidedHours));
            set => Interlocked.Exchange(ref _decidedHours, BitConverter.DoubleToInt64Bits(value));
        }
        /// <summary>The newest hour reported to the agents and the dashboard.</summary>
        public static double PublishedHours
        {
            get => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _publishedHours));
            set => Interlocked.Exchange(ref _publishedHours, BitConverter.DoubleToInt64Bits(value));
        }
        /// <summary>In-game hours the order rules (orders) and the reports (reports) are behind the game; 0 when not known.</summary>
        public static double OrdersBehind => Behind(DecidedHours);
        public static double ReportsBehind => Behind(PublishedHours);
        private static double Behind(double h) { double p = PostedHours; return double.IsNaN(p) || double.IsNaN(h) ? 0 : Math.Max(0, p - h); }

        /// <summary>The helper's day for the run's day line (DayStats): the most it was behind, and hours folded into a later report.</summary>
        private static readonly object StatsGate = new object();
        private static double _dayOrdersBehind, _dayReportsBehind;
        private static int _dayMerged, _dayRounds;

        /// <summary>A new hour from the game's thread: its order rules go first; its reports follow when the helper has room.</summary>
        public static void PostHour(double hours, Action decide)
        {
            Interlocked.Exchange(ref _postedHours, BitConverter.DoubleToInt64Bits(hours));
            PostFirst("hour: orders and look", decide);
        }

        /// <summary>The helper is behind (watchdog, once a second): kept for the day line.</summary>
        public static void NoteBehind()
        {
            double o = OrdersBehind, r = ReportsBehind;
            lock (StatsGate)
            {
                if (o > _dayOrdersBehind) _dayOrdersBehind = o;
                if (r > _dayReportsBehind) _dayReportsBehind = r;
            }
        }

        /// <summary>A report round ended (Brain): how many hours went into one report.</summary>
        public static void NoteRound(int merged)
        {
            lock (StatsGate) { _dayRounds++; _dayMerged += Math.Max(0, merged); }
        }

        /// <summary>The day so far, and a fresh one (game's thread, at the run's day line).</summary>
        public static HelperDay TakeDay()
        {
            lock (StatsGate)
            {
                var d = new HelperDay { OrdersBehindMost = Math.Round(_dayOrdersBehind, 1), ReportsBehindMost = Math.Round(_dayReportsBehind, 1), Rounds = _dayRounds, HoursFolded = _dayMerged };
                _dayOrdersBehind = _dayReportsBehind = 0;
                _dayMerged = _dayRounds = 0;
                return d;
            }
        }
        private static readonly long DrainBudget = Stopwatch.Frequency / 500;   // 2 ms
        private static Thread _thread;
        private static long _onEfficient, _sampled;
        [ThreadStatic] private static bool _isWorker;

        public static string Placement = "not started";
        public static long Ran, Dropped, Failed;
        /// <summary>The job running now and when it started (Stopwatch ticks, 0 when idle), for the watchdog.</summary>
        public static volatile string CurrentJob;
        public static long JobStarted;
        /// <summary>The slowest job since the health row last read it.</summary>
        public static long MaxJobTicks;

        /// <summary>Work sent back to the game's thread, such as agent orders; drained on each campaign tick (only while the clock runs).</summary>
        public static readonly ConcurrentQueue<Action> ToMain = new ConcurrentQueue<Action>();
        /// <summary>On-screen messages; drained every frame in any state, so alarms show even while paused or in a menu.</summary>
        public static readonly ConcurrentQueue<Action> ToUi = new ConcurrentQueue<Action>();

        public static bool Running => _thread != null;
        public static bool IsWorker => _isWorker;
        public static int Queued => Jobs.Count;
        public static double EfficientShare => _sampled == 0 ? double.NaN : (double)_onEfficient / _sampled;

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Bannerlord Agents worker", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        /// <summary>Queues a job. Never blocks the game: if the queue is full, the job is dropped and counted.</summary>
        public static void Post(Action job) => Post("work", job);

        public static void Post(string name, Action job)
        {
            if (_thread == null) Start();
            if (!Jobs.TryAdd(new Job { Name = name, Run = job })) Interlocked.Increment(ref Dropped);
        }

        /// <summary>Queues a short job to run before everything already queued (it still waits for the job running now).</summary>
        public static void PostFirst(string name, Action job)
        {
            if (_thread == null) Start();
            First.Enqueue(new Job { Name = name, Run = job });
            if (!Jobs.TryAdd(new Job { Name = "wake", Run = Nothing })) { }   // wakes the worker if it is idle; a full queue is being worked anyway
        }

        private static void Nothing() { }

        /// <summary>Runs a job on the worker and waits for its answer. For console commands only.</summary>
        public static bool Ask<T>(Func<T> job, int timeoutMs, out T result)
        {
            T value = default(T);
            var done = new ManualResetEventSlim(false);
            Post("console", () =>
            {
                try { value = job(); }
                finally { done.Set(); }
            });
            bool ok = done.Wait(timeoutMs);
            if (!ok) Diag.Alarm("helper_slow_answer", "The background helper did not answer a console command within " + timeoutMs / 1000 + " s (busy with: " + (CurrentJob ?? "nothing") + ").");
            result = value;
            return ok;
        }

        /// <summary>
        /// Called by the game's thread on each campaign tick: carries out what the worker sent back, at most max and, since 0.11.3,
        /// within about 2 ms (an answer's 8 orders at once made pauses of 5 to 34 ms in run C1); the rest go next tick.
        /// </summary>
        public static void DrainToMain(int max = 8)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < max && ToMain.TryDequeue(out Action a); i++)
            {
                try { a(); } catch (Exception e) { Diag.Error("work handed back to the game's thread", e); }
                if (Stopwatch.GetTimestamp() - start > DrainBudget) break;
            }
        }

        /// <summary>Called by the game's thread every frame: on-screen messages.</summary>
        public static void DrainToUi(int max = 4)
        {
            for (int i = 0; i < max && ToUi.TryDequeue(out Action a); i++)
            {
                try { a(); } catch (Exception e) { Diag.Swallowed("on-screen message", e); }
            }
        }

        /// <summary>
        /// The lanes in order of rank: every urgent job first; then one regular job; then, with nothing else waiting, one
        /// reporting step (0.12.0). Idle, it writes out what is buffered and sleeps until woken.
        /// </summary>
        private static void Loop()
        {
            _isWorker = true;
            Placement = Cpu.MakeCurrentThreadBackground();
            Log.Line("worker thread started: " + Placement + "; urgent work: " + Cpu.UrgentPlacement);
            while (true)
            {
                if (RunUrgent()) continue;
                if (Jobs.TryTake(out Job queued))
                {
                    if (queued.Run != Nothing) RunJob(queued);
                    continue;
                }
                Action step = null;
                try { step = Publisher?.Invoke(); }
                catch (Exception e) { Failed++; Diag.Error("choosing the next report step", e); }
                if (step != null)
                {
                    RunJob(new Job { Name = "reports", Run = step });
                    continue;
                }
                Files.FlushAll();
                if (Jobs.TryTake(out queued, 250))
                {
                    RunUrgent();
                    if (queued.Run != Nothing) RunJob(queued);
                }
            }
        }

        /// <summary>Runs every urgent job waiting, out of efficiency mode while it does; true when there was one.</summary>
        private static bool RunUrgent()
        {
            if (First.IsEmpty) return false;
            Cpu.SetCurrentThreadUrgent(true);
            try { while (First.TryDequeue(out Job first)) RunJob(first); }
            finally { Cpu.SetCurrentThreadUrgent(false); }
            return true;
        }

        private static void RunJob(Job job)
        {
            int cpu = Cpu.CurrentProcessor();
            if (cpu >= 0)
            {
                _sampled++;
                if (Cpu.IsEfficient(cpu)) _onEfficient++;
            }
            long t = Stopwatch.GetTimestamp();
            CurrentJob = job.Name;
            Interlocked.Exchange(ref JobStarted, t);
            try { job.Run(); Ran++; }
            catch (Exception e) { Failed++; Diag.Error("background job '" + job.Name + "'", e); }
            long d = Stopwatch.GetTimestamp() - t;
            Interlocked.Exchange(ref JobStarted, 0);
            CurrentJob = null;
            if (d > Interlocked.Read(ref MaxJobTicks)) Interlocked.Exchange(ref MaxJobTicks, d);
        }
    }

    /// <summary>The helper's day for the run's day line (0.12.0): the most the orders and the reports were behind, in in-game hours.</summary>
    internal sealed class HelperDay
    {
        public double OrdersBehindMost, ReportsBehindMost;
        public int Rounds, HoursFolded;
    }
}
