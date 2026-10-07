using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// What the bridge costs, measured rather than assumed. On the game's thread: the time our own code takes, the
    /// frame count, the engine's frame-rate counters, and which kind of core the game's thread ran on. On the
    /// worker: how busy every core was. One row per second goes to perf\perf_(date).csv.
    /// </summary>
    internal static class Perf
    {
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
        private static long _secondStart = Stopwatch.GetTimestamp();
        private static long _ourTicks;
        private static int _frames, _mainOnPerformance, _mainSamples;

        public static double CaptureLastMs, CaptureMaxMs, CaptureSumMs;
        public static int Captures;

        public static long Now() => Stopwatch.GetTimestamp();
        public static double Ms(long start) => (Stopwatch.GetTimestamp() - start) * TicksToMs;

        /// <summary>Counts time our code spent on the game's thread.</summary>
        public static void Add(long start) => _ourTicks += Stopwatch.GetTimestamp() - start;

        /// <summary>Counts the hourly copy separately as well, so its cost per in-game hour can be reported.</summary>
        public static void Capture(long start)
        {
            long d = Stopwatch.GetTimestamp() - start;
            _ourTicks += d;
            double ms = d * TicksToMs;
            CaptureLastMs = ms;
            CaptureSumMs += ms;
            Captures++;
            if (ms > CaptureMaxMs) CaptureMaxMs = ms;
        }

        public static void ResetCapture()
        {
            CaptureLastMs = CaptureMaxMs = CaptureSumMs = 0;
            Captures = 0;
        }

        /// <summary>Called once per campaign tick on the game's thread.</summary>
        public static void OnTick()
        {
            _frames++;
            int cpu = Cpu.CurrentProcessor();
            if (cpu >= 0)
            {
                _mainSamples++;
                if (!Cpu.IsEfficient(cpu)) _mainOnPerformance++;
            }
            long now = Stopwatch.GetTimestamp();
            double elapsed = (now - _secondStart) * TicksToMs;
            if (elapsed < 1000) return;
            if (Settings.Perf)
            {
                var row = new PerfRow
                {
                    Wall = DateTime.Now,
                    Seconds = elapsed / 1000.0,
                    Ticks = _frames,
                    EngineFps = Fps(0),
                    MainFps = Fps(1),
                    RenderFps = Fps(2),
                    OurMs = _ourTicks * TicksToMs,
                    Bridge = Settings.Bridge,
                    Speed = SafeSpeed(),
                    GameHours = CampaignTime.Now.ToHours,
                    MainOnPerformance = _mainSamples == 0 ? -1f : (float)_mainOnPerformance / _mainSamples
                };
                Background.Post(() => PerfLog.Write(row));
            }
            _secondStart = now;
            _frames = 0;
            _ourTicks = 0;
            _mainOnPerformance = 0;
            _mainSamples = 0;
        }

        private static float Fps(int which)
        {
            try
            {
                switch (which)
                {
                    case 0: return TaleWorlds.Engine.Utilities.GetFps();
                    case 1: return TaleWorlds.Engine.Utilities.GetMainFps();
                    default: return TaleWorlds.Engine.Utilities.GetRendererFps();
                }
            }
            catch { return -1f; }
        }

        private static string SafeSpeed()
        {
            try { return Campaign.Current?.TimeControlMode.ToString(); } catch { return "?"; }
        }
    }

    internal sealed class PerfRow
    {
        public DateTime Wall;
        public double Seconds, GameHours, OurMs;
        public int Ticks;
        public float EngineFps, MainFps, RenderFps, MainOnPerformance;
        public bool Bridge;
        public string Speed;
        public float PerformanceAvg = -1, PerformanceMax = -1, EfficiencyAvg = -1, EfficiencyMax = -1;
        public float[] Loads;
    }

    /// <summary>The worker's half of the performance log.</summary>
    internal static class PerfLog
    {
        private static readonly Queue<PerfRow> Recent = new Queue<PerfRow>();

        public static void Write(PerfRow r)
        {
            float[] loads = Cpu.SampleLoads();
            if (loads != null)
            {
                r.Loads = loads;
                float pSum = 0, eSum = 0, pMax = 0, eMax = 0;
                int pN = 0, eN = 0;
                for (int i = 0; i < loads.Length; i++)
                {
                    if (Cpu.IsEfficient(i)) { eSum += loads[i]; eN++; eMax = Math.Max(eMax, loads[i]); }
                    else { pSum += loads[i]; pN++; pMax = Math.Max(pMax, loads[i]); }
                }
                if (pN > 0) { r.PerformanceAvg = pSum / pN; r.PerformanceMax = pMax; }
                if (eN > 0) { r.EfficiencyAvg = eSum / eN; r.EfficiencyMax = eMax; }
            }

            string path = Path.Combine(Paths.Ensure(Path.Combine(Paths.Root, "perf")), "perf_" + r.Wall.ToString("yyyyMMdd") + ".csv");
            if (Files.IsNew(path))
                Files.Append(path, "time,seconds,ticks,fps_engine,fps_main,fps_render,our_ms,bridge,speed,game_hours,main_thread_on_performance_cores,performance_cores_avg,performance_cores_max,efficiency_cores_avg,efficiency_cores_max,core_loads");
            var inv = Text.Inv;
            var sb = new StringBuilder(256);
            sb.Append(r.Wall.ToString("HH:mm:ss")).Append(',')
              .Append(r.Seconds.ToString("0.000", inv)).Append(',')
              .Append(r.Ticks).Append(',')
              .Append(r.EngineFps.ToString("0.0", inv)).Append(',')
              .Append(r.MainFps.ToString("0.0", inv)).Append(',')
              .Append(r.RenderFps.ToString("0.0", inv)).Append(',')
              .Append(r.OurMs.ToString("0.000", inv)).Append(',')
              .Append(r.Bridge ? "on" : "off").Append(',')
              .Append(r.Speed).Append(',')
              .Append(r.GameHours.ToString("0.00", inv)).Append(',')
              .Append(r.MainOnPerformance.ToString("0.00", inv)).Append(',')
              .Append(r.PerformanceAvg.ToString("0.00", inv)).Append(',')
              .Append(r.PerformanceMax.ToString("0.00", inv)).Append(',')
              .Append(r.EfficiencyAvg.ToString("0.00", inv)).Append(',')
              .Append(r.EfficiencyMax.ToString("0.00", inv)).Append(',');
            if (r.Loads != null) sb.Append(string.Join(";", r.Loads.Select(x => x.ToString("0.00", inv))));
            Files.Append(path, sb.ToString());

            Recent.Enqueue(r);
            while (Recent.Count > 900) Recent.Dequeue();
        }

        /// <summary>The last 15 minutes, split by whether the bridge was on.</summary>
        public static string Summary()
        {
            if (Recent.Count == 0) return "  no performance rows yet (they start a second after the campaign map is up)";
            var sb = new StringBuilder();
            foreach (bool on in new[] { true, false })
            {
                var rows = Recent.Where(x => x.Bridge == on).ToList();
                if (rows.Count == 0) continue;
                double fps = rows.Average(x => x.EngineFps);
                double ticks = rows.Average(x => x.Ticks / Math.Max(0.001, x.Seconds));
                double ours = rows.Average(x => x.OurMs / Math.Max(0.001, x.Seconds));
                double mainP = rows.Where(x => x.MainOnPerformance >= 0).Select(x => (double)x.MainOnPerformance).DefaultIfEmpty(-1).Average();
                double pAvg = rows.Where(x => x.PerformanceAvg >= 0).Select(x => (double)x.PerformanceAvg).DefaultIfEmpty(-1).Average();
                double eAvg = rows.Where(x => x.EfficiencyAvg >= 0).Select(x => (double)x.EfficiencyAvg).DefaultIfEmpty(-1).Average();
                sb.AppendLine("  bridge " + (on ? "ON " : "OFF") + ": " + rows.Count + " s sampled, frame rate " + fps.ToString("0.0", Text.Inv)
                    + " (campaign ticks " + ticks.ToString("0.0", Text.Inv) + "/s), our time on the game's thread "
                    + ours.ToString("0.000", Text.Inv) + " ms per second");
                sb.AppendLine("      game's thread on performance cores " + Pct(mainP) + "; cores busy: performance " + Pct(pAvg)
                    + ", efficiency " + Pct(eAvg));
            }
            return sb.ToString().TrimEnd();
        }

        private static string Pct(double v) => v < 0 ? "n/a" : (v * 100).ToString("0", Text.Inv) + "%";
    }
}
