using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace BannerlordAgents
{
    /// <summary>
    /// The bridge's one background thread. The game's thread only copies numbers and hands them over here; this
    /// thread works out views and pokes, builds reports and writes every file, the logs included. It runs at low
    /// priority on the efficiency cores (see Cpu), and it never touches a game object. It starts when the module
    /// loads, so even start-up lines are written off the game's thread.
    /// </summary>
    internal static class Background
    {
        private struct Job
        {
            public string Name;
            public Action Run;
        }

        private static readonly BlockingCollection<Job> Jobs = new BlockingCollection<Job>(new ConcurrentQueue<Job>(), 4096);
        // jobs that go before the queue (0.11.3: the look at the agents' answer folders, which waited behind up to 621 jobs at run C1's start, so answers waited up to 30 s)
        private static readonly ConcurrentQueue<Job> First = new ConcurrentQueue<Job>();
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

        private static void Loop()
        {
            _isWorker = true;
            Placement = Cpu.MakeCurrentThreadBackground();
            Log.Line("worker thread started: " + Placement);
            foreach (Job queued in Jobs.GetConsumingEnumerable())
            {
                while (First.TryDequeue(out Job first)) RunJob(first);
                if (queued.Run != Nothing) RunJob(queued);
                if (Jobs.Count == 0) Files.FlushAll();
            }
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
}
