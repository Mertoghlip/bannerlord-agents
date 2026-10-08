using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace FreezeProbe;

/// <summary>
/// Says where a game's main thread is, from outside the game: in plain words first, then the raw addresses.
/// 1. Ten samples of the main thread over one second (each pauses it for microseconds): busy in one small loop,
///    busy moving around, or waiting.
/// 2. How busy every thread of the game was over that second, and where each of the busiest ones started.
/// 3. The main thread's call trail: return addresses read off its stack, kept only where the instruction before
///    them is a call, named by module (and by function for Windows' own modules), and by method for C# code.
/// 4. The C# stacks of the main thread and of the other threads running C#, read from a snapshot of the process
///    (Windows clones it, so the game is not held while they are read).
/// Usage: FreezeProbe --pid N [--thread OSID] [--out file] [--why text] [--state text] [--doing text]
/// </summary>
internal static class Program
{
    private static int Main(string[] argv)
    {
        Dictionary<string, string> a = ParseArgs(argv);
        if (!a.TryGetValue("pid", out string pidText) || !int.TryParse(pidText, out int pid))
        {
            Console.WriteLine("usage: FreezeProbe --pid N [--thread OSID] [--out file] [--why text] [--state text] [--doing text]");
            return 2;
        }
        a.TryGetValue("thread", out string tidText);
        int.TryParse(tidText, out int tid);
        string output = a.TryGetValue("out", out string o) && o.Length > 0 ? o : Path.Combine(Path.GetTempPath(), "freeze-" + DateTime.Now.ToString("HHmmss") + ".txt");
        var sb = new StringBuilder();
        var clock = Stopwatch.StartNew();
        try { Run(pid, tid, a, sb); }
        catch (Exception e) { sb.AppendLine().AppendLine("The probe failed: " + e); }
        sb.AppendLine().AppendLine("(the probe took " + clock.ElapsedMilliseconds + " ms)");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
            File.WriteAllText(output, sb.ToString());
        }
        catch (Exception e) { Console.WriteLine("could not write " + output + ": " + e.Message); }
        Console.Write(sb.ToString());
        return 0;
    }

    private static void Run(int pid, int tid, Dictionary<string, string> a, StringBuilder sb)
    {
        using Process p = Process.GetProcessById(pid);
        sb.AppendLine("FREEZE PROBE, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (a.TryGetValue("why", out string why) && why.Length > 0) sb.AppendLine("Why: " + why + ".");
        if (a.TryGetValue("state", out string state) && state.Length > 0) sb.AppendLine("The game's state when it stopped: " + state + ".");
        if (a.TryGetValue("doing", out string doing)) sb.AppendLine("Bannerlord Agents code running on the game's thread then: " + (doing.Length > 0 ? doing : "none") + ".");
        sb.AppendLine("Process: " + p.ProcessName + " (pid " + pid + "), up " + Up(p) + ", private memory " + (p.PrivateMemorySize64 >> 20).ToString("N0")
                      + " MB, " + p.Threads.Count + " threads.");
        if (tid == 0)
        {
            tid = MainThreadGuess(p);
            sb.AppendLine("No thread was named, so the oldest thread (" + tid + ") is taken as the game's main thread.");
        }

        IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (hProc == IntPtr.Zero) throw new Exception("the process could not be opened (error " + Marshal.GetLastWin32Error() + ")");
        try
        {
            List<Module> modules = Modules(hProc);
            Dictionary<int, TimeSpan> before = ThreadTimes(p);
            var samples = new List<ulong>();
            byte[] stack = null;
            ulong stackAt = 0;
            var window = Stopwatch.StartNew();
            IntPtr hThread = OpenThread(THREAD_SUSPEND_RESUME | THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, false, (uint)tid);
            if (hThread == IntPtr.Zero) sb.AppendLine("The main thread (" + tid + ") could not be opened (error " + Marshal.GetLastWin32Error() + ").");
            else
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        if (i > 0) Thread.Sleep(100);
                        if (SuspendThread(hThread) == uint.MaxValue) continue;
                        try
                        {
                            if (!GetContext(hThread, out ulong rip, out ulong rsp)) continue;
                            samples.Add(rip);
                            stackAt = rsp;
                            stack = ReadPages(hProc, rsp, 64 * 1024);
                        }
                        finally { ResumeThread(hThread); }
                    }
                }
                finally { CloseHandle(hThread); }
            }
            window.Stop();
            Dictionary<int, TimeSpan> after = ThreadTimes(p);
            double windowMs = Math.Max(1, window.Elapsed.TotalMilliseconds);
            var cpu = new Dictionary<int, double>();
            foreach (KeyValuePair<int, TimeSpan> kv in after)
                if (before.TryGetValue(kv.Key, out TimeSpan b)) cpu[kv.Key] = (kv.Value - b).TotalMilliseconds / windowMs * 100;
            cpu.TryGetValue(tid, out double mainCpu);

            // C# names for code that is not in any module (the JIT's code), and the C# stacks
            ClrRuntime rt = null;
            DataTarget dt = null;
            string clrNote;
            try
            {
                try { dt = DataTarget.CreateSnapshotAndAttach(pid); clrNote = "read from a snapshot of the process"; }
                catch (Exception e1)
                {
                    dt = DataTarget.AttachToProcess(pid, suspend: true);
                    clrNote = "read with the process paused for a moment (no snapshot: " + e1.Message + ")";
                }
                ClrInfo clr = dt.ClrVersions.FirstOrDefault();
                rt = clr?.CreateRuntime();
                if (rt == null) clrNote = "no .NET runtime was found in the process";
            }
            catch (Exception e) { clrNote = "the C# stacks could not be read: " + e.Message; }

            try
            {
                // ---- the verdict ----
                var named = samples.Select(ip => Name(modules, hProc, rt, ip)).ToList();
                int waits = named.Count(IsWait);
                sb.AppendLine();
                sb.AppendLine("WHAT THE GAME'S MAIN THREAD (" + tid + ") IS DOING");
                if (samples.Count == 0) sb.AppendLine("No sample could be taken.");
                else if (waits * 2 >= samples.Count && mainCpu < 25)
                    sb.AppendLine("WAITING: it used " + mainCpu.ToString("0") + "% of a core over one second and sat in a wait (" + named.First(IsWait)
                                  + "). It is waiting for something that has not come: another thread, a lock, the disk or the graphics card. "
                                  + "The busiest other threads below may be what it waits for.");
                else
                {
                    ulong lo = samples.Min(), hi = samples.Max();
                    bool tight = hi - lo < 0x400;
                    sb.AppendLine((mainCpu >= 50 ? "BUSY" : "PARTLY BUSY") + ": it used " + mainCpu.ToString("0") + "% of a core over one second and "
                                  + (tight ? "stayed in one small stretch of code, which looks like a loop: " + named[named.Count - 1] + "."
                                           : "moved between several places (see the samples)."));
                }
                sb.AppendLine("Samples, 100 ms apart:");
                foreach (var g in named.GroupBy(x => x).OrderByDescending(g => g.Count())) sb.AppendLine("  " + g.Count() + " x " + g.Key);

                // ---- the call trail ----
                sb.AppendLine();
                sb.AppendLine("CALL TRAIL OF THE MAIN THREAD, innermost first (return addresses on its stack at the last sample, stack at 0x" + stackAt.ToString("x") + ")");
                List<string> trail = Trail(modules, hProc, rt, stack);
                if (trail.Count == 0) sb.AppendLine("  (none found)");
                foreach (string t in trail) sb.AppendLine("  " + t);

                // ---- the busiest threads ----
                sb.AppendLine();
                sb.AppendLine("BUSIEST THREADS OVER THE SAME SECOND");
                Dictionary<uint, ClrThread> managed = rt?.Threads.Where(t => t.IsAlive).GroupBy(t => t.OSThreadId).ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<uint, ClrThread>();
                foreach (KeyValuePair<int, double> kv in cpu.OrderByDescending(x => x.Value).Take(10))
                {
                    string start = StartOf(modules, hProc, kv.Key);
                    string top = "";
                    if (managed.TryGetValue((uint)kv.Key, out ClrThread ct))
                    {
                        ClrStackFrame f = ct.EnumerateStackTrace().FirstOrDefault(x => x.Method != null);
                        top = "; C# thread #" + ct.ManagedThreadId + (f != null ? ", in " + MethodName(f.Method) : "");
                    }
                    sb.AppendLine("  " + kv.Key + (kv.Key == tid ? " (main)" : "") + ": " + kv.Value.ToString("0") + "% of a core; started at " + start + top);
                }

                // ---- the C# stacks ----
                sb.AppendLine();
                sb.AppendLine("C# STACKS (" + clrNote + ")");
                if (rt != null)
                {
                    if (managed.TryGetValue((uint)tid, out ClrThread main))
                    {
                        sb.AppendLine("Main thread " + tid + ", innermost first:");
                        foreach (string f in Frames(main, 80)) sb.AppendLine("    " + f);
                    }
                    else sb.AppendLine("The main thread is not running C# right now (it is in the engine or in Windows).");
                    int shown = 0;
                    foreach (ClrThread t in managed.Values.OrderByDescending(t => cpu.TryGetValue((int)t.OSThreadId, out double c) ? c : 0))
                    {
                        if (t.OSThreadId == (uint)tid) continue;
                        List<string> frames = Frames(t, 12);
                        if (frames.Count == 0) continue;
                        if (++shown > 25) break;
                        cpu.TryGetValue((int)t.OSThreadId, out double c2);
                        sb.AppendLine("Thread " + t.OSThreadId + " (C# #" + t.ManagedThreadId + (t.IsFinalizer ? ", finalizer" : "") + (t.IsGc ? ", garbage collector" : "")
                                      + ", " + c2.ToString("0") + "% of a core):");
                        foreach (string f in frames) sb.AppendLine("    " + f);
                    }
                }
            }
            finally
            {
                rt?.Dispose();
                dt?.Dispose();
            }
        }
        finally { CloseHandle(hProc); }
    }

    // ---------- naming addresses ----------

    private static readonly string[] WindowsModules =
        { "ntdll.dll", "kernelbase.dll", "kernel32.dll", "win32u.dll", "user32.dll", "gdi32.dll", "d3d11.dll", "dxgi.dll", "clr.dll", "ws2_32.dll", "combase.dll" };

    private static string Name(List<Module> modules, IntPtr hProc, ClrRuntime rt, ulong ip)
    {
        Module m = modules.FirstOrDefault(x => ip >= x.Base && ip < x.Base + x.Size);
        if (m != null)
        {
            string s = m.Name + "+0x" + (ip - m.Base).ToString("x");
            string exp = WindowsModules.Contains(m.Name.ToLowerInvariant()) ? m.Nearest(hProc, ip) : null;
            return exp != null ? s + " (" + exp + ")" : s;
        }
        ClrMethod cm = rt?.GetMethodByInstructionPointer(ip);
        return cm != null ? "C# " + MethodName(cm) : "0x" + ip.ToString("x") + " (code outside any module)";
    }

    private static bool IsWait(string name)
    {
        string[] waits = { "NtWait", "ZwWait", "NtDelayExecution", "ZwDelayExecution", "NtRemoveIoCompletion", "NtSignalAndWait", "NtWaitForAlertByThreadId",
                           "NtUserMsgWaitForMultipleObjectsEx", "NtUserGetMessage", "NtUserWaitMessage", "NtWaitForWorkViaWorkerFactory" };
        return waits.Any(w => name.Contains("(" + w));
    }

    private static string MethodName(ClrMethod m) => (m.Type?.Name ?? "?") + "." + m.Name;

    private static List<string> Frames(ClrThread t, int max)
    {
        var list = new List<string>();
        try
        {
            foreach (ClrStackFrame f in t.EnumerateStackTrace().Take(max))
            {
                if (f.Method != null) list.Add(MethodName(f.Method));
                else if (f.Kind == ClrStackFrameKind.Runtime) list.Add("[" + f.FrameName + "]");
            }
        }
        catch (Exception e) { list.Add("(the stack could not be read: " + e.Message + ")"); }
        return list;
    }

    /// <summary>Return addresses on the stack whose preceding bytes are a call instruction, innermost first.</summary>
    private static List<string> Trail(List<Module> modules, IntPtr hProc, ClrRuntime rt, byte[] stack)
    {
        var trail = new List<string>();
        if (stack == null) return trail;
        string last = null;
        for (int off = 0; off + 8 <= stack.Length && trail.Count < 40; off += 8)
        {
            ulong v = BitConverter.ToUInt64(stack, off);
            if (v < 0x10000 || v > 0x7FFFFFFFFFFF) continue;
            Module m = modules.FirstOrDefault(x => v >= x.Base && v < x.Base + x.Size);
            string name = null;
            if (m != null)
            {
                if (!m.IsCode(hProc, v) || !AfterCall(hProc, v)) continue;
                name = Name(modules, hProc, rt, v);
            }
            else if (rt != null)
            {
                ClrMethod cm = rt.GetMethodByInstructionPointer(v);
                if (cm == null) continue;
                name = "C# " + MethodName(cm);
            }
            if (name == null || name == last) continue;
            trail.Add(name);
            last = name;
        }
        return trail;
    }

    /// <summary>Is the instruction just before this address a call? (E8 rel32; FF /2 with the usual address forms.)</summary>
    private static bool AfterCall(IntPtr hProc, ulong ret)
    {
        byte[] b = ReadExact(hProc, ret - 7, 7);
        if (b == null) return false;
        // b[0..6] are the 7 bytes before the return address; a call ends exactly at it
        if (b[2] == 0xE8) return true;                                                      // call rel32
        if (b[1] == 0xFF && b[2] == 0x15) return true;                                      // call [rip+disp32]
        if (b[1] == 0xFF && (b[2] & 0xF8) == 0x90 && b[2] != 0x94) return true;             // call [reg+disp32]
        if (b[0] == 0xFF && b[1] == 0x94) return true;                                      // call [reg+index+disp32]
        if (b[4] == 0xFF && (b[5] & 0xF8) == 0x50 && b[5] != 0x54) return true;             // call [reg+disp8]
        if (b[3] == 0xFF && b[4] == 0x54) return true;                                      // call [reg+index+disp8]
        if (b[5] == 0xFF && ((b[6] & 0xF8) == 0xD0 || ((b[6] & 0xF8) == 0x10 && b[6] != 0x14 && b[6] != 0x15))) return true;   // call reg, call [reg]
        return false;
    }

    private static string StartOf(List<Module> modules, IntPtr hProc, int tid)
    {
        IntPtr h = OpenThread(THREAD_QUERY_INFORMATION, false, (uint)tid);
        if (h == IntPtr.Zero) return "?";
        try
        {
            if (NtQueryInformationThread(h, 9, out IntPtr start, IntPtr.Size, out _) != 0) return "?";
            return Name(modules, hProc, null, (ulong)start.ToInt64());
        }
        finally { CloseHandle(h); }
    }

    // ---------- the process ----------

    private static int MainThreadGuess(Process p)
    {
        ProcessThread best = null;
        foreach (ProcessThread t in p.Threads)
        {
            try { if (best == null || t.StartTime < best.StartTime) best = t; }
            catch { /* a thread we may not query */ }
        }
        return best?.Id ?? 0;
    }

    private static Dictionary<int, TimeSpan> ThreadTimes(Process p)
    {
        var d = new Dictionary<int, TimeSpan>();
        p.Refresh();
        foreach (ProcessThread t in p.Threads)
        {
            try { d[t.Id] = t.TotalProcessorTime; }
            catch { /* the thread ended, or may not be queried */ }
        }
        return d;
    }

    private static string Up(Process p)
    {
        try { return (DateTime.Now - p.StartTime).ToString(@"hh\:mm\:ss"); }
        catch { return "?"; }
    }

    private static bool GetContext(IntPtr hThread, out ulong rip, out ulong rsp)
    {
        rip = rsp = 0;
        IntPtr raw = Marshal.AllocHGlobal(0x4D0 + 16);
        try
        {
            IntPtr ctx = new IntPtr((raw.ToInt64() + 15) & ~15L);
            for (int i = 0; i < 0x4D0; i += 8) Marshal.WriteInt64(ctx, i, 0);
            Marshal.WriteInt32(ctx, 0x30, 0x00100003);   // CONTEXT_AMD64 | CONTROL | INTEGER
            if (!GetThreadContext(hThread, ctx)) return false;
            rsp = (ulong)Marshal.ReadInt64(ctx, 0x98);
            rip = (ulong)Marshal.ReadInt64(ctx, 0xF8);
            return true;
        }
        finally { Marshal.FreeHGlobal(raw); }
    }

    /// <summary>Reads page by page, stopping at the first page that cannot be read (the top of the stack).</summary>
    private static byte[] ReadPages(IntPtr hProc, ulong at, int max)
    {
        var ms = new MemoryStream();
        ulong pos = at;
        while (ms.Length < max)
        {
            int len = (int)Math.Min(4096 - (long)(pos % 4096), max - ms.Length);
            byte[] chunk = ReadExact(hProc, pos, len);
            if (chunk == null) break;
            ms.Write(chunk, 0, chunk.Length);
            pos += (ulong)len;
        }
        return ms.ToArray();
    }

    private static byte[] ReadExact(IntPtr hProc, ulong at, int len)
    {
        var buf = new byte[len];
        return ReadProcessMemory(hProc, (IntPtr)(long)at, buf, (IntPtr)len, out IntPtr read) && read.ToInt64() == len ? buf : null;
    }

    private static List<Module> Modules(IntPtr hProc)
    {
        var list = new List<Module>();
        var handles = new IntPtr[2048];
        if (!K32EnumProcessModulesEx(hProc, handles, (uint)(handles.Length * IntPtr.Size), out uint needed, 3)) return list;
        int n = (int)Math.Min(handles.Length, needed / IntPtr.Size);
        for (int i = 0; i < n; i++)
        {
            if (!K32GetModuleInformation(hProc, handles[i], out MODULEINFO info, (uint)Marshal.SizeOf<MODULEINFO>())) continue;
            var name = new StringBuilder(260);
            K32GetModuleBaseNameW(hProc, handles[i], name, (uint)name.Capacity);
            list.Add(new Module { Name = name.ToString(), Base = (ulong)info.lpBaseOfDll.ToInt64(), Size = info.SizeOfImage });
        }
        return list;
    }

    private sealed class Module
    {
        public string Name;
        public ulong Base;
        public uint Size;
        private List<KeyValuePair<uint, uint>> _code;
        private List<KeyValuePair<uint, string>> _exports;

        /// <summary>Inside one of the module's executable sections?</summary>
        public bool IsCode(IntPtr hProc, ulong addr)
        {
            if (_code == null)
            {
                _code = new List<KeyValuePair<uint, uint>>();
                byte[] h = ReadExact(hProc, Base, 0x1000);
                if (h != null)
                {
                    int nt = BitConverter.ToInt32(h, 0x3C);
                    if (nt > 0 && nt < 0xF00)
                    {
                        int sections = BitConverter.ToUInt16(h, nt + 6);
                        int optSize = BitConverter.ToUInt16(h, nt + 20);
                        int first = nt + 24 + optSize;
                        for (int i = 0; i < sections && first + i * 40 + 40 <= h.Length; i++)
                        {
                            int s = first + i * 40;
                            uint vsize = BitConverter.ToUInt32(h, s + 8), rva = BitConverter.ToUInt32(h, s + 12), flags = BitConverter.ToUInt32(h, s + 36);
                            if ((flags & 0x20000000) != 0) _code.Add(new KeyValuePair<uint, uint>(rva, rva + vsize));
                        }
                    }
                }
            }
            uint off = (uint)(addr - Base);
            return _code.Count == 0 || _code.Any(c => off >= c.Key && off < c.Value);
        }

        /// <summary>The nearest exported function at or before the address, for Windows' own modules.</summary>
        public string Nearest(IntPtr hProc, ulong addr)
        {
            if (_exports == null)
            {
                _exports = new List<KeyValuePair<uint, string>>();
                try
                {
                    byte[] h = ReadExact(hProc, Base, 0x1000);
                    int nt = h != null ? BitConverter.ToInt32(h, 0x3C) : 0;
                    if (nt > 0 && nt < 0xF00)
                    {
                        uint dirRva = BitConverter.ToUInt32(h, nt + 0x88);
                        byte[] dir = dirRva != 0 ? ReadExact(hProc, Base + dirRva, 40) : null;
                        if (dir != null)
                        {
                            int names = BitConverter.ToInt32(dir, 0x18);
                            uint funcs = BitConverter.ToUInt32(dir, 0x1C), nameRvas = BitConverter.ToUInt32(dir, 0x20), ords = BitConverter.ToUInt32(dir, 0x24);
                            int count = BitConverter.ToInt32(dir, 0x14);
                            byte[] f = ReadExact(hProc, Base + funcs, count * 4);
                            byte[] nr = ReadExact(hProc, Base + nameRvas, names * 4);
                            byte[] od = ReadExact(hProc, Base + ords, names * 2);
                            if (f != null && nr != null && od != null)
                                for (int i = 0; i < names; i++)
                                {
                                    int ord = BitConverter.ToUInt16(od, i * 2);
                                    if (ord >= count) continue;
                                    uint rva = BitConverter.ToUInt32(f, ord * 4);
                                    byte[] s = ReadExact(hProc, Base + BitConverter.ToUInt32(nr, i * 4), 64);
                                    if (s == null) continue;
                                    int z = Array.IndexOf(s, (byte)0);
                                    _exports.Add(new KeyValuePair<uint, string>(rva, Encoding.ASCII.GetString(s, 0, z < 0 ? s.Length : z)));
                                }
                            _exports.Sort((x, y) => x.Key.CompareTo(y.Key));
                        }
                    }
                }
                catch { /* names are a bonus; the module and offset are always given */ }
            }
            uint off = (uint)(addr - Base);
            KeyValuePair<uint, string> best = default;
            foreach (KeyValuePair<uint, string> e in _exports)
            {
                if (e.Key > off) break;
                best = e;
            }
            return best.Value != null && off - best.Key < 0x4000 ? best.Value + "+0x" + (off - best.Key).ToString("x") : null;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] argv)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) continue;
            string key = argv[i].Substring(2);
            d[key] = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : "";
        }
        return d;
    }

    // ---------- Windows ----------

    private const uint PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_VM_READ = 0x0010;
    private const uint THREAD_SUSPEND_RESUME = 0x0002, THREAD_GET_CONTEXT = 0x0008, THREAD_QUERY_INFORMATION = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct MODULEINFO
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadContext(IntPtr thread, IntPtr context);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool K32EnumProcessModulesEx(IntPtr process, IntPtr[] modules, uint cb, out uint needed, uint filter);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool K32GetModuleInformation(IntPtr process, IntPtr module, out MODULEINFO info, uint cb);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint K32GetModuleBaseNameW(IntPtr process, IntPtr module, StringBuilder name, uint size);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationThread(IntPtr thread, int infoClass, out IntPtr info, int length, out int returned);
}
