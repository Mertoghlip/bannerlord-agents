using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace BannerlordAgents
{
    /// <summary>
    /// The helper's big writes in small steps (0.25.0, item 12; run 20261125-2341-C2: 765 fresh snapshots, a median 4 seconds and up to 13.4 each, and 257 briefings, up to
    /// 11.5, were written whole in the urgent lane that starts, changes and ends orders and runs each hour's decisions, so on 74 in-game hours the hourly order step ran more
    /// than 5 seconds late, up to 10.7, and 217 of the war room's 253 failed waits came within 30 seconds after a briefing). The helper still has one thread, so a write in
    /// progress would still hold an order up: each write is cut into steps (an iterator yields between them), and the helper takes one step at a time between its other jobs
    /// (Background), so an urgent job waits for at most one step. The jobs run in the order they were asked for. Worker only.
    /// </summary>
    internal static class Writes
    {
        private sealed class Job
        {
            public string Name;
            public IEnumerator Steps;
            public long Began;
            public int Taken;
            public double LongestMs;
        }

        private static readonly Queue<Job> Waiting = new Queue<Job>();
        private static Job _current;

        /// <summary>The longest step of the last job finished, and of all since the launch, in ms (the speed test and the logs read them).</summary>
        public static double LastLongestMs, LongestMs;
        public static int Finished;

        /// <summary>Jobs waiting or running (the speed test drains them).</summary>
        public static int Pending => Waiting.Count + (_current != null ? 1 : 0);

        /// <summary>A write cut into steps (each yield ends one): taken after every job asked for before it.</summary>
        public static void Add(string name, IEnumerable steps)
        {
            if (steps == null) return;
            Waiting.Enqueue(new Job { Name = name, Steps = steps.GetEnumerator() });
            Diag.Count("writes:asked");
        }

        /// <summary>The next step to take, or null when none waits (Background's writes lane).</summary>
        public static Action Next()
        {
            if (_current == null)
            {
                if (Waiting.Count == 0) return null;
                _current = Waiting.Dequeue();
                _current.Began = Stopwatch.GetTimestamp();
            }
            return Step;
        }

        private static void Step()
        {
            Job j = _current;
            if (j == null) return;
            long t = Stopwatch.GetTimestamp();
            bool more;
            try { more = j.Steps.MoveNext(); }
            catch (Exception e)
            {
                more = false;
                Diag.Error("the write '" + j.Name + "', step " + (j.Taken + 1), e);
            }
            double ms = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
            j.Taken++;
            if (ms > j.LongestMs) j.LongestMs = ms;
            if (more) return;
            _current = null;
            Finished++;
            LastLongestMs = j.LongestMs;
            if (j.LongestMs > LongestMs) LongestMs = j.LongestMs;
            Diag.Sample("helper:write step ms", j.LongestMs);
            Diag.Event("writes", "done", new { name = j.Name, steps = j.Taken, longest_step_ms = Math.Round(j.LongestMs, 1), wall_ms = Math.Round((Stopwatch.GetTimestamp() - j.Began) * 1000.0 / Stopwatch.Frequency, 1) });
        }

        /// <summary>Every job waiting is taken now, step by step (the smoke test, and anything outside the helper).</summary>
        public static void Drain()
        {
            for (Action a = Next(); a != null; a = Next()) a();
        }

        /// <summary>A new campaign session: the jobs of the last one are dropped.</summary>
        public static void Reset()
        {
            Waiting.Clear();
            _current = null;
        }
    }
}
