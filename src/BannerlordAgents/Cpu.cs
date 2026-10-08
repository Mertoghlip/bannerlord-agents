using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace BannerlordAgents
{
    /// <summary>
    /// The processor's layout and how busy each core is.
    ///
    /// The laptop's Core Ultra 9 275HX has 8 performance and 16 efficiency cores, and Bannerlord's speed depends on
    /// one busy thread. So the bridge's background work asks Windows for three things: low priority, Windows'
    /// efficiency mode, and the efficiency cores only. Which cores are which comes from Windows' own CPU-set table
    /// (a higher efficiency class is a faster core), so nothing here is specific to one processor.
    /// </summary>
    internal static class Cpu
    {
        internal sealed class Core
        {
            public uint Id;
            public int Logical;
            public int Efficiency;
        }

        public static readonly List<Core> Cores = new List<Core>();
        public static bool Hybrid { get; private set; }
        public static int TopClass { get; private set; }
        public static string Description = "not detected";
        private static readonly bool[] Efficient = new bool[256];

        public static bool IsEfficient(int logical) => logical >= 0 && logical < Efficient.Length && Efficient[logical];

        public static void Detect()
        {
            try
            {
                Cores.Clear();
                GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint len, IntPtr.Zero, 0);
                if (len == 0) { Description = "Windows gave no CPU-set table"; return; }
                IntPtr buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) { Description = "CPU-set query failed"; return; }
                    int off = 0;
                    while (off + 8 <= len)
                    {
                        int size = Marshal.ReadInt32(buf, off);
                        int type = Marshal.ReadInt32(buf, off + 4);
                        if (size <= 0) break;
                        if (type == 0 && size >= 20)
                        {
                            Cores.Add(new Core
                            {
                                Id = (uint)Marshal.ReadInt32(buf, off + 8),
                                Logical = Marshal.ReadByte(buf, off + 14),
                                Efficiency = Marshal.ReadByte(buf, off + 18)
                            });
                        }
                        off += size;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }

                int min = int.MaxValue, max = int.MinValue;
                foreach (Core c in Cores) { min = Math.Min(min, c.Efficiency); max = Math.Max(max, c.Efficiency); }
                TopClass = max;
                Hybrid = Cores.Count > 0 && max > min;
                int eff = 0;
                if (Hybrid)
                {
                    foreach (Core c in Cores)
                    {
                        if (c.Efficiency >= max) continue;
                        if (c.Logical < Efficient.Length) Efficient[c.Logical] = true;
                        eff++;
                    }
                }
                Description = Hybrid
                    ? Cores.Count + " cores: " + (Cores.Count - eff) + " performance, " + eff + " efficiency"
                    : Cores.Count + " cores, all alike";
            }
            catch (Exception e) { Description = "processor layout could not be read: " + e.Message; }
        }

        /// <summary>
        /// Marks the calling thread as background work: below-normal priority, Windows' efficiency mode, and on a
        /// hybrid processor the efficiency cores only. Returns what Windows accepted, for the log.
        /// </summary>
        public static string MakeCurrentThreadBackground()
        {
            var notes = new List<string>();
            try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; notes.Add("low priority"); }
            catch (Exception e) { notes.Add("low priority refused: " + e.Message); }
            try
            {
                var state = new ThreadPowerThrottlingState { Version = 1, ControlMask = 1, StateMask = 1 };
                bool ok = SetThreadInformation(GetCurrentThread(), 3, ref state, (uint)Marshal.SizeOf(typeof(ThreadPowerThrottlingState)));
                notes.Add(ok ? "efficiency mode" : "efficiency mode refused (" + Marshal.GetLastWin32Error() + ")");
            }
            catch (Exception e) { notes.Add("efficiency mode failed: " + e.Message); }
            if (Hybrid)
            {
                try
                {
                    var ids = new List<uint>();
                    foreach (Core c in Cores) if (c.Efficiency < TopClass) ids.Add(c.Id);
                    bool ok = SetThreadSelectedCpuSets(GetCurrentThread(), ids.ToArray(), (uint)ids.Count);
                    notes.Add(ok ? "kept to the " + ids.Count + " efficiency cores" : "core choice refused (" + Marshal.GetLastWin32Error() + ")");
                }
                catch (Exception e) { notes.Add("core choice failed: " + e.Message); }
            }
            return string.Join(", ", notes);
        }

        private static bool _noProcessorNumber;

        public static int CurrentProcessor()
        {
            if (_noProcessorNumber) return -1;
            try { return (int)GetCurrentProcessorNumber(); }
            catch (Exception e) { _noProcessorNumber = true; Diag.Swallowed("which core a thread runs on", e); return -1; }
        }

        /// <summary>Processor time used by the calling thread so far, in ms (-1 if Windows does not say).</summary>
        public static long ThreadCpuMs()
        {
            try { if (GetThreadTimes(GetCurrentThread(), out long _, out long _, out long kernel, out long user)) return (kernel + user) / 10000; }
            catch (Exception e) { Diag.Swallowed("thread processor time", e); }
            return -1;
        }

        private static long[] _idle, _total;

        /// <summary>How busy each core was (0 to 1) since the last call; null on the first call.</summary>
        public static float[] SampleLoads()
        {
            try
            {
                int size = 48 * Math.Max(1, Environment.ProcessorCount);
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (NtQuerySystemInformation(8, buf, size, out int ret) != 0) return null;
                    int count = ret / 48;
                    var idle = new long[count];
                    var total = new long[count];
                    for (int i = 0; i < count; i++)
                    {
                        idle[i] = Marshal.ReadInt64(buf, i * 48);
                        total[i] = Marshal.ReadInt64(buf, i * 48 + 8) + Marshal.ReadInt64(buf, i * 48 + 16);
                    }
                    float[] loads = null;
                    if (_idle != null && _idle.Length == count)
                    {
                        loads = new float[count];
                        for (int i = 0; i < count; i++)
                        {
                            long dt = total[i] - _total[i];
                            long di = idle[i] - _idle[i];
                            loads[i] = dt > 0 ? Math.Max(0f, Math.Min(1f, 1f - (float)di / dt)) : 0f;
                        }
                    }
                    _idle = idle;
                    _total = total;
                    return loads;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception e) { Diag.Swallowed("core loads", e); return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ThreadPowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemCpuSetInformation(IntPtr information, uint bufferLength, out uint returnedLength, IntPtr process, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadSelectedCpuSets(IntPtr thread, uint[] cpuSetIds, uint cpuSetIdCount);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadInformation(IntPtr thread, int informationClass, ref ThreadPowerThrottlingState information, uint size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessorNumber();

        [DllImport("kernel32.dll")]
        private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int informationClass, IntPtr information, int length, out int returnLength);
    }
}
