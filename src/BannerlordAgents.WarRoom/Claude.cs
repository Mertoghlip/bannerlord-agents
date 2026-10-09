using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace BannerlordAgents.WarRoom
{
    /// <summary>What one Claude Code call took and gave back.</summary>
    internal sealed class CallResult
    {
        public bool Ok, Stalled, Runaway;
        public string Text, Raw, Error;
        public double Seconds, ApiMs, TtftMs, Cost, FirstTextMs = -1;
        public int Turns;
        public long InTokens, OutTokens, CacheRead, CacheWrite, Thinking;
        public List<string> Denied = new List<string>();
    }

    /// <summary>
    /// One fresh, headless Claude Code call on the subscription: the system prompt from a file, the poke on standard
    /// input, the answer and its usage as JSON. No session is kept, no project instructions are loaded (safe mode), and
    /// the only tool is Read, inside the channel folder (anything outside is refused without asking).
    /// </summary>
    internal static class Claude
    {
        public static string Find(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
            string ext = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
            if (Directory.Exists(ext))
            {
                string best = Directory.GetDirectories(ext, "anthropic.claude-code-*")
                    .Select(d => Path.Combine(d, "resources", "native-binary", "claude.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(p => Version(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(p))))))
                    .FirstOrDefault();
                if (best != null) return best;
            }
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
            if (File.Exists(local)) return local;
            return "claude";
        }

        private static Version Version(string folder)
        {
            // anthropic.claude-code-2.1.282-win32-x64
            string[] parts = folder.Split('-');
            foreach (string p in parts)
                if (System.Version.TryParse(p, out Version v)) return v;
            return new Version(0, 0);
        }

        /// <summary>Seconds without a single event from Claude Code before a call counts as stalled and is stopped.</summary>
        public const int StallSeconds = 45;

        /// <summary>No answer is this long (the longest so far was under 6,000 characters); past it, the call is stopped.</summary>
        public const int RunawayChars = 16000;

        public static CallResult Run(string exe, string systemFile, string prompt, string model, string effort, string cwd, bool read, int timeoutSeconds, int thinkingCap = 0)
        {
            var r = new CallResult();
            var psi = new ProcessStartInfo(exe)
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            // streamed to the token, one event per line: read as it comes, so a stall is seen within seconds and the moment
            // thinking ends and writing begins is known; no prompt suggestion afterwards (it only delays the exit)
            foreach (string a in new[]
            {
                "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--prompt-suggestions", "false",
                "--model", model, "--effort", effort, "--system-prompt-file", systemFile,
                "--tools", read ? "Read" : "", "--safe-mode", "--permission-prompts", "none", "--no-session-persistence"
            }) psi.ArgumentList.Add(a);
            if (thinkingCap > 0) psi.Environment["MAX_THINKING_TOKENS"] = thinkingCap.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sw = Stopwatch.StartNew();
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception e) { r.Error = "Claude Code could not be started (" + exe + "): " + e.Message; return r; }
            try
            {
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; Native.Efficiency(p.Handle); }
                catch { /* the call still runs, just not throttled */ }
                var events = new StringBuilder();
                var written = new StringBuilder();
                string resultLine = null;
                double firstText = -1;
                long lastEvent = sw.ElapsedMilliseconds;
                var got = new ManualResetEventSlim(false);
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) { got.Set(); return; }
                    if (e.Data.Length == 0) return;
                    Interlocked.Exchange(ref lastEvent, sw.ElapsedMilliseconds);
                    lock (events)
                    {
                        // keys come in any order, so look for the type, then make sure
                        if (e.Data.Contains("\"type\":\"result\"") && IsType(e.Data, "result")) { resultLine = e.Data; got.Set(); }
                        else if (e.Data.Contains("\"text_delta\""))
                        {
                            if (firstText < 0) firstText = sw.Elapsed.TotalMilliseconds;
                            if (written.Length < 200_000) written.Append(Delta(e.Data));
                        }
                        if (events.Length < 2_000_000 && !e.Data.Contains("\"stream_event\"")) events.AppendLine(e.Data);
                    }
                };
                var err = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) if (err.Length < 20_000) err.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                using (var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) w.Write(prompt);
                // the answer ends the call, not the program's exit: Claude Code sometimes lingers after answering
                string stopped = null;
                while (!got.Wait(500))
                {
                    if (sw.Elapsed.TotalSeconds > timeoutSeconds) { stopped = "the call took over " + timeoutSeconds + " s and was stopped"; break; }
                    if (sw.ElapsedMilliseconds - Interlocked.Read(ref lastEvent) > StallSeconds * 1000L) { stopped = "stalled: nothing came for " + StallSeconds + " s"; break; }
                    lock (events)
                    {
                        // an answer that types tool calls as text, or runs far past any answer's length, will not end well
                        string so = written.Length > 0 ? written.ToString() : "";
                        if (so.Contains("<invoke ") || so.Contains("<function_calls>") || so.Length > RunawayChars)
                        {
                            stopped = so.Length > RunawayChars ? "runaway: over " + RunawayChars + " characters written" : "runaway: it typed tool calls as text";
                            r.Runaway = true;
                            break;
                        }
                    }
                }
                if (!p.WaitForExit(2000)) { try { p.Kill(true); } catch { } }
                r.Seconds = sw.Elapsed.TotalSeconds;
                lock (events) r.Raw = events.ToString() + (resultLine == null && written.Length > 0 ? "\n--- the text written before it was stopped ---\n" + written : "");
                if (resultLine == null)
                {
                    r.Error = stopped ?? ("no answer" + (err.Length > 0 ? ": " + First(err.ToString()) : ""));
                    r.Stalled = stopped != null && stopped.StartsWith("stalled", StringComparison.Ordinal);
                    return r;
                }
                using JsonDocument doc = JsonDocument.Parse(resultLine);
                JsonElement root = doc.RootElement;
                if (firstText >= 0) r.FirstTextMs = firstText;
                if (root.TryGetProperty("is_error", out JsonElement isErr) && isErr.ValueKind == JsonValueKind.True)
                    r.Error = "Claude Code reported an error: " + (root.TryGetProperty("result", out JsonElement er) ? First(er.ToString()) : "?");
                r.Text = root.TryGetProperty("result", out JsonElement res) ? res.GetString() : null;
                r.ApiMs = Num(root, "duration_api_ms");
                r.TtftMs = Num(root, "ttft_ms");
                r.Turns = (int)Num(root, "num_turns");
                r.Cost = Num(root, "total_cost_usd");
                if (root.TryGetProperty("usage", out JsonElement u))
                {
                    r.InTokens = (long)Num(u, "input_tokens");
                    r.OutTokens = (long)Num(u, "output_tokens");
                    r.CacheRead = (long)Num(u, "cache_read_input_tokens");
                    r.CacheWrite = (long)Num(u, "cache_creation_input_tokens");
                    if (u.TryGetProperty("output_tokens_details", out JsonElement od)) r.Thinking = (long)Num(od, "thinking_tokens");
                }
                if (root.TryGetProperty("permission_denials", out JsonElement den) && den.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement d in den.EnumerateArray())
                        r.Denied.Add(d.TryGetProperty("tool_input", out JsonElement ti) ? ti.ToString() : d.ToString());
                r.Ok = r.Error == null && !string.IsNullOrWhiteSpace(r.Text);
                if (!r.Ok && r.Error == null) r.Error = "an empty answer";
            }
            catch (Exception e) { r.Error = "the answer could not be read: " + e.Message; }
            finally { p.Dispose(); }
            return r;
        }

        /// <summary>The text of one streamed text_delta event.</summary>
        private static string Delta(string line)
        {
            try
            {
                using JsonDocument d = JsonDocument.Parse(line);
                return d.RootElement.TryGetProperty("event", out JsonElement ev) && ev.TryGetProperty("delta", out JsonElement de) && de.TryGetProperty("text", out JsonElement t)
                    ? t.GetString() : "";
            }
            catch (JsonException) { return ""; }
        }

        private static bool IsType(string line, string type)
        {
            try
            {
                using JsonDocument d = JsonDocument.Parse(line);
                return d.RootElement.TryGetProperty("type", out JsonElement t) && t.GetString() == type;
            }
            catch (JsonException) { return false; }
        }

        private static double Num(JsonElement e, string name)
            => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

        private static string First(string s)
        {
            s = (s ?? "").Trim();
            int nl = s.IndexOf('\n');
            if (nl > 0) s = s.Substring(0, nl);
            return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
        }
    }

    /// <summary>Below-normal priority and efficiency mode (EcoQoS), so Windows keeps the war room off the game's busy core.</summary>
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottling
        {
            public uint Version, ControlMask, StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int size);

        public static void Efficiency(IntPtr process)
        {
            var s = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 1 };   // execution speed: throttled
            SetProcessInformation(process, 4, ref s, Marshal.SizeOf<PowerThrottling>());
        }

        public static void Self()
        {
            try
            {
                Process me = Process.GetCurrentProcess();
                me.PriorityClass = ProcessPriorityClass.BelowNormal;
                Efficiency(me.Handle);
            }
            catch { /* runs at normal priority then */ }
        }
    }
}
