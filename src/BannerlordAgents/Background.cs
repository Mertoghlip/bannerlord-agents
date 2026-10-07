using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BannerlordAgents
{
    /// <summary>
    /// The bridge's one background thread. The game's thread only copies numbers and hands them over here; this
    /// thread works out views and pokes, builds reports and writes every file. It runs at low priority on the
    /// efficiency cores (see Cpu), and it never touches a game object.
    /// </summary>
    internal static class Background
    {
        private static readonly BlockingCollection<Action> Jobs = new BlockingCollection<Action>(new ConcurrentQueue<Action>(), 4096);
        private static Thread _thread;
        private static long _onEfficient, _sampled;

        public static string Placement = "not started";
        public static long Ran, Dropped, Failed;

        /// <summary>Work sent back to the game's thread, such as on-screen messages; drained every campaign tick.</summary>
        public static readonly ConcurrentQueue<Action> ToMain = new ConcurrentQueue<Action>();

        public static int Queued => Jobs.Count;
        public static double EfficientShare => _sampled == 0 ? double.NaN : (double)_onEfficient / _sampled;

        public static void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Bannerlord Agents worker", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        /// <summary>Queues a job. Never blocks the game: if the queue is full, the job is dropped and counted.</summary>
        public static void Post(Action job)
        {
            if (_thread == null) Start();
            if (!Jobs.TryAdd(job)) Interlocked.Increment(ref Dropped);
        }

        /// <summary>Runs a job on the worker and waits for its answer. For console commands only.</summary>
        public static bool Ask<T>(Func<T> job, int timeoutMs, out T result)
        {
            T value = default(T);
            var done = new ManualResetEventSlim(false);
            Post(() =>
            {
                try { value = job(); }
                finally { done.Set(); }
            });
            bool ok = done.Wait(timeoutMs);
            result = value;
            return ok;
        }

        /// <summary>Called by the game's thread every tick: shows what the worker sent back.</summary>
        public static void DrainToMain(int max = 8)
        {
            for (int i = 0; i < max && ToMain.TryDequeue(out Action a); i++)
            {
                try { a(); } catch (Exception e) { Log.Line("main-thread hand-back failed: " + e.Message); }
            }
        }

        private static void Loop()
        {
            Placement = Cpu.MakeCurrentThreadBackground();
            Log.Line("worker thread started: " + Placement);
            foreach (Action job in Jobs.GetConsumingEnumerable())
            {
                int cpu = Cpu.CurrentProcessor();
                if (cpu >= 0)
                {
                    _sampled++;
                    if (Cpu.IsEfficient(cpu)) _onEfficient++;
                }
                try { job(); Ran++; }
                catch (Exception e) { Failed++; Log.Line("worker job failed: " + e); }
                if (Jobs.Count == 0) Files.FlushAll();
            }
        }
    }
}
