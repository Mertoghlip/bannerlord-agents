using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.ScreenSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// The memory tidy (module 0.22.1, Nov 21; docs/ENGINE-NOTES.md "Memory on the campaign map"). The game lets go of the engine's resources no longer in use
    /// (Utilities.ClearOldResourcesAndObjects) and of its unused portrait and banner pictures (ThumbnailCacheManager.ClearUnusedCache) only when a game state other
    /// than a menu is pushed or popped (GameStateScreenManager.OnPushState/OnPopState): entering or leaving a battle or scene, the inventory, the party or clan
    /// screen, the banner screen. On the campaign map nothing calls them, so the game's reserved memory climbs for as long as one stays on the map: Nov 20, 14 to
    /// 24 GB in an hour of a test run, while the memory it touched stayed at 7 to 9 GB and its scripting memory under 1.3 GB; the game's ordinary full clean-ups
    /// (about one a minute) freed nothing of it, and leaving the banner screen freed 5 GB at once. Twice that night the whole laptop ran out of room and the game
    /// crashed making a texture.
    ///
    /// So on the campaign map, with no battle, scene or other screen up, once the game's reserved memory has grown <see cref="Settings.MemoryTidyMb"/> since its
    /// lowest point (while the clock stands; <see cref="Settings.MemoryTidyRunningMb"/> while it runs), and no sooner than three real minutes after the last, this
    /// makes the same two calls the game makes at every screen change, on the game's thread. Each is logged (memory/tidy: before, right after, five seconds later,
    /// the milliseconds it took, the in-game hours since the last) and counted in health.csv.
    /// </summary>
    internal static class MemoryTidy
    {
        private static readonly double Freq = Stopwatch.Frequency;
        private const double LookSeconds = 2, LaterSeconds = 5, MinMinutes = 3;

        private static long _nextLook, _lastTidy, _laterAt;
        private static long _lowMb = -1, _beforeMb, _afterMb, _beforeWsMb;
        private static double _tookMs, _lastHours = double.NaN, _hoursSince;
        private static bool _running, _broken;


        /// <summary>Every frame (SubModule.OnApplicationTick): a look every two seconds; allocation-free until a tidy is due.</summary>
        public static void Frame()
        {
            if (_broken || !Settings.MemoryTidy) return;
            long now = Stopwatch.GetTimestamp();
            if (_laterAt != 0 && now >= _laterAt) Later();
            if (now < _nextLook) return;
            _nextLook = now + (long)(LookSeconds * Freq);
            try
            {
                if (!OnTheMap() || !Memory(out long priv, out _)) { return; }
                if (_lowMb < 0 || priv < _lowMb) _lowMb = priv;
                if (_laterAt != 0 || _lastTidy != 0 && (now - _lastTidy) / Freq < MinMinutes * 60) return;
                bool running = Campaign.Current.TimeControlMode != CampaignTimeControlMode.Stop && Watchdog.ClockRunning;
                long grown = priv - _lowMb;
                if (grown < (running ? Settings.MemoryTidyRunningMb : Settings.MemoryTidyMb)) return;
                Tidy(priv, running);
            }
            catch (Exception e)
            {
                _broken = true;
                Diag.Error("memory tidy (switched off for this launch)", e);
            }
        }

        private static bool OnTheMap()
        {
            if (!Bridge.Running || Campaign.Current == null || Mission.Current != null) return false;
            if (!(GameStateManager.Current?.ActiveState is MapState)) return false;
            ScreenBase top = ScreenManager.TopScreen;
            return top != null && top.GetType().Name == "MapScreen";
        }

        private static void Tidy(long priv, bool running)
        {
            Memory(out _, out _beforeWsMb);
            _beforeMb = priv;
            _running = running;
            double hours = CampaignTime.Now.ToHours;
            _hoursSince = double.IsNaN(_lastHours) ? double.NaN : hours - _lastHours;
            _lastHours = hours;
            long t0 = Stopwatch.GetTimestamp();
            Utilities.ClearOldResourcesAndObjects();
            ThumbnailCacheManager.Current?.ClearUnusedCache();
            long t1 = Stopwatch.GetTimestamp();
            _tookMs = (t1 - t0) * 1000.0 / Freq;
            Memory(out _afterMb, out _);
            _lastTidy = t1;
            _laterAt = t1 + (long)(LaterSeconds * Freq);
        }

        /// <summary>Five seconds on: what the tidy gave back once the engine had let go, logged; the low point starts again from here.</summary>
        private static void Later()
        {
            _laterAt = 0;
            if (!Memory(out long later, out long ws)) return;
            long freed = _beforeMb - later;
            int n = Interlocked.Increment(ref Diag.TidyCount);
            if (freed > 0) Interlocked.Add(ref Diag.TidyFreedMb, freed);
            _lowMb = later;
            Diag.Event("memory", "tidy", new
            {
                before_mb = _beforeMb, right_after_mb = _afterMb, five_seconds_later_mb = later, freed_mb = freed, working_set_before_mb = _beforeWsMb, working_set_after_mb = ws,
                ms = Math.Round(_tookMs, 1), clock = _running ? "running" : "stopped", in_game_hours_since_last = double.IsNaN(_hoursSince) ? (double?)null : Math.Round(_hoursSince, 1), tidy = n
            });
            Log.Line("memory tidy #" + n + ": the game's reserved memory " + _beforeMb + " MB, " + later + " MB five seconds later (" + (freed >= 0 ? "freed " + freed : "grew " + (-freed)) + " MB), in "
                     + _tookMs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ms with the clock " + (_running ? "running" : "stopped"));
        }

        // ---------- the process's memory, allocation-free ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }

        private static readonly uint CountersSize = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters));

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool K32GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCounters counters, uint size);

        /// <summary>The game's reserved (private, committed) memory and its working set, in MB.</summary>
        internal static bool Memory(out long privateMb, out long workingSetMb)
        {
            privateMb = workingSetMb = 0;
            var c = new ProcessMemoryCounters { cb = CountersSize };
            if (!K32GetProcessMemoryInfo(GetCurrentProcess(), out c, c.cb)) return false;
            privateMb = (long)(c.PrivateUsage.ToUInt64() / (1024 * 1024));
            workingSetMb = (long)(c.WorkingSetSize.ToUInt64() / (1024 * 1024));
            return true;
        }
    }
}
