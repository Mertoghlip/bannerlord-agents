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
        /// <summary>With the war room's own tools: Claude Code did not answer the handshake within 30 s (the message went anyway).</summary>
        public bool HandshakeLate;
        public string Text, Raw, Error;
        /// <summary>The model's thinking as the API summarized it (0.13, Claude Code's --thinking-display summarized); empty when not asked for.</summary>
        public string ThinkingText = "";
        public double Seconds, ApiMs, TtftMs, Cost, FirstTextMs = -1;
        public int Turns;
        public long InTokens, OutTokens, CacheRead, CacheWrite, Thinking;
        public List<string> Denied = new List<string>();
    }

    /// <summary>
    /// Tools the war room itself serves the agent during a call (0.7: "ask the way"), as an in-process ("sdk") MCP server that
    /// Claude Code reaches over the call's own stream. A real tool, so safe mode stays on: Claude Code's safe mode switches
    /// off every MCP server it would start itself, but not one its host serves (tried Oct 16).
    /// </summary>
    internal interface IToolHost
    {
        /// <summary>The server's name: its tools are mcp__(name)__(tool).</summary>
        string Server { get; }
        IEnumerable<string> Tools { get; }
        /// <summary>One JSON-RPC message from Claude Code; the answer, or null for a notification.</summary>
        System.Text.Json.Nodes.JsonNode Handle(System.Text.Json.Nodes.JsonObject message);
    }

    /// <summary>
    /// One headless Claude Code call on the subscription: the system prompt from a file, the message on standard input,
    /// the answer and its usage as JSON. No session is kept (except the one long conversation of the conversation
    /// memory), no project instructions are loaded (safe mode), and the only tool is Read, inside the channel folder
    /// (anything outside is refused without asking).
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

        /// <summary>The same for an answer that writes tools of its own (0.13, docs/PLAN-0.13.md A8): each tool's definition is long, so it may run longer.</summary>
        public const int RunawayCharsWithTools = 48000;

        /// <summary>The runaway limit for what a call has written so far: raised once it has begun an answer's "tools".</summary>
        internal static int RunawayLimit(string written) => written.Contains("\"tools\"") ? RunawayCharsWithTools : RunawayChars;

        /// <summary>
        /// One call. With a session id, the conversation is kept: started under that id (resume false) or carried on
        /// (resume true), for the one-long-conversation memory; without one, nothing is kept. A cancelled call is stopped.
        /// </summary>
        /// <summary>
        /// How Claude Code asks for the model's thinking (0.13, docs/PLAN-0.13.md C3.8): "summarized" brings the API's summary of it
        /// into the stream (kept in raw.jsonl and thinking.txt), "omitted" leaves it out; empty passes nothing. Set from warroom.txt.
        /// </summary>
        public static string ThinkingDisplay = "summarized";

        public static CallResult Run(string exe, string systemFile, string prompt, string model, string effort, string cwd, bool read, int timeoutSeconds, int thinkingCap = 0,
                                     string sessionId = null, bool resume = false, CancellationToken cancel = default, IToolHost tools = null)
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
                "--tools", read ? "Read" : "", "--safe-mode", "--permission-prompts", "none"
            }) psi.ArgumentList.Add(a);
            if (!string.IsNullOrWhiteSpace(ThinkingDisplay)) { psi.ArgumentList.Add("--thinking-display"); psi.ArgumentList.Add(ThinkingDisplay.Trim()); }
            if (sessionId == null) psi.ArgumentList.Add("--no-session-persistence");
            else
            {
                psi.ArgumentList.Add(resume ? "--resume" : "--session-id");
                psi.ArgumentList.Add(sessionId);
            }
            if (tools != null)
            {
                // the message and the tool's answers go as JSON lines on standard input; each variadic flag is followed by another flag
                foreach (string a in new[]
                {
                    "--input-format", "stream-json",
                    "--mcp-config", "{\"mcpServers\":{\"" + tools.Server + "\":{\"type\":\"sdk\",\"name\":\"" + tools.Server + "\"}}}", "--strict-mcp-config",
                    "--allowedTools", string.Join(",", tools.Tools.Select(t => "mcp__" + tools.Server + "__" + t))
                }) psi.ArgumentList.Add(a);
            }
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
                var thoughts = new StringBuilder();
                string resultLine = null;
                double firstText = -1;
                long lastEvent = sw.ElapsedMilliseconds;
                var got = new ManualResetEventSlim(false);
                var handshake = new ManualResetEventSlim(false);
                StreamWriter stdin = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                // Standard input is written by one thread of its own, from a queue, so reading Claude Code's output never waits on
                // a write. (Oct 16: with the message written from the caller while Claude Code was busy with the tools' handshake,
                // a message of a real poke's size filled the pipe, the tools' answer queued behind it, and both sides waited for good.)
                var outbox = new System.Collections.Concurrent.BlockingCollection<string>();
                var writer = new Thread(() =>
                {
                    try
                    {
                        foreach (string line in outbox.GetConsumingEnumerable())
                        {
                            stdin.Write(line);
                            if (tools != null) stdin.Write("\n");
                            stdin.Flush();
                        }
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                    finally { try { stdin.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { } }
                }) { IsBackground = true, Name = "claude stdin" };
                writer.Start();
                void Send(string line)
                {
                    try { outbox.Add(line); }
                    catch (InvalidOperationException) { /* closed: the call is over */ }
                }
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) { got.Set(); return; }
                    if (e.Data.Length == 0) return;
                    Interlocked.Exchange(ref lastEvent, sw.ElapsedMilliseconds);
                    if (tools != null && e.Data.Contains("\"control_request\"")) Control(e.Data, tools, Send);
                    if (tools != null && e.Data.Contains("\"control_response\"") && e.Data.Contains("warroom-init")) handshake.Set();
                    lock (events)
                    {
                        // keys come in any order, so look for the type, then make sure
                        if (e.Data.Contains("\"type\":\"result\"") && IsType(e.Data, "result")) { resultLine = e.Data; got.Set(); }
                        else if (e.Data.Contains("\"text_delta\""))
                        {
                            if (firstText < 0) firstText = sw.Elapsed.TotalMilliseconds;
                            if (written.Length < 200_000) written.Append(Delta(e.Data));
                        }
                        // the thinking's summary, in each whole assistant message (0.13)
                        else if (e.Data.Contains("\"type\":\"assistant\"") && e.Data.Contains("\"thinking\"") && thoughts.Length < 400_000) Thoughts(e.Data, thoughts);
                        if (events.Length < 2_000_000 && !e.Data.Contains("\"stream_event\"")) events.AppendLine(e.Data);
                    }
                };
                var err = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) if (err.Length < 20_000) err.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (tools == null)
                {
                    Send(prompt);
                    outbox.CompleteAdding();
                }
                else
                {
                    // the host's handshake names the tools it serves; the message goes only once Claude Code has answered it, so the
                    // tools' own handshake is never stuck behind it. Standard input stays open for the tools' answers.
                    Send(JsonSerializer.Serialize(new { type = "control_request", request_id = "warroom-init", request = new { subtype = "initialize", sdkMcpServers = new[] { tools.Server } } }));
                    if (!handshake.Wait(TimeSpan.FromSeconds(30), cancel) && !got.IsSet) r.HandshakeLate = true;
                    Send(JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = prompt } }));
                }
                // the answer ends the call, not the program's exit: Claude Code sometimes lingers after answering
                string stopped = null;
                while (!got.Wait(500))
                {
                    if (cancel.IsCancellationRequested) { stopped = "cancelled"; break; }
                    if (sw.Elapsed.TotalSeconds > timeoutSeconds) { stopped = "the call took over " + timeoutSeconds + " s and was stopped"; break; }
                    if (sw.ElapsedMilliseconds - Interlocked.Read(ref lastEvent) > StallSeconds * 1000L) { stopped = "stalled: nothing came for " + StallSeconds + " s"; break; }
                    lock (events)
                    {
                        // an answer that types tool calls as text, or runs far past any answer's length, will not end well
                        string so = written.Length > 0 ? written.ToString() : "";
                        int limit = RunawayLimit(so);
                        if (so.Contains("<invoke ") || so.Contains("<function_calls>") || so.Length > limit)
                        {
                            stopped = so.Length > limit ? "runaway: over " + limit + " characters written" : "runaway: it typed tool calls as text";
                            r.Runaway = true;
                            break;
                        }
                    }
                }
                outbox.CompleteAdding();
                if (!p.WaitForExit(2000)) { try { p.Kill(true); } catch { } }
                writer.Join(2000);
                r.Seconds = sw.Elapsed.TotalSeconds;
                lock (events)
                {
                    r.Raw = events.ToString() + (resultLine == null && written.Length > 0 ? "\n--- the text written before it was stopped ---\n" + written : "");
                    r.ThinkingText = thoughts.ToString();
                }
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

        /// <summary>
        /// A control request from Claude Code: a message for the tools the war room serves is answered by them; anything else
        /// (the reply to our own handshake comes as a control_response, not here) is answered as not supported.
        /// </summary>
        private static void Control(string line, IToolHost tools, Action<string> send)
        {
            System.Text.Json.Nodes.JsonObject o;
            try { o = System.Text.Json.Nodes.JsonNode.Parse(line) as System.Text.Json.Nodes.JsonObject; }
            catch (JsonException) { return; }
            if (o == null || (string)o["type"] != "control_request") return;
            string id = (string)o["request_id"];
            var req = o["request"] as System.Text.Json.Nodes.JsonObject;
            string subtype = (string)req?["subtype"];
            if (subtype == "mcp_message" && req["message"] is System.Text.Json.Nodes.JsonObject msg)
            {
                System.Text.Json.Nodes.JsonNode answer;
                try { answer = tools.Handle(msg); }
                catch (Exception e)
                {
                    answer = new System.Text.Json.Nodes.JsonObject
                    {
                        ["jsonrpc"] = "2.0", ["id"] = msg["id"]?.DeepClone(),
                        ["error"] = new System.Text.Json.Nodes.JsonObject { ["code"] = -32603, ["message"] = "the tool failed: " + e.Message }
                    };
                }
                answer ??= new System.Text.Json.Nodes.JsonObject { ["jsonrpc"] = "2.0", ["result"] = new System.Text.Json.Nodes.JsonObject(), ["id"] = 0 };
                var reply = new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "control_response",
                    ["response"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["subtype"] = "success", ["request_id"] = id,
                        ["response"] = new System.Text.Json.Nodes.JsonObject { ["mcp_response"] = answer }
                    }
                };
                send(reply.ToJsonString());
                return;
            }
            send(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "control_response",
                ["response"] = new System.Text.Json.Nodes.JsonObject { ["subtype"] = "error", ["request_id"] = id, ["error"] = "the war room does not handle " + (subtype ?? "this request") }
            }.ToJsonString());
        }

        /// <summary>The thinking blocks of one whole assistant message, appended (empty ones, when the API leaves the text out, add nothing).</summary>
        internal static void Thoughts(string line, StringBuilder into)
        {
            try
            {
                using JsonDocument d = JsonDocument.Parse(line);
                if (!d.RootElement.TryGetProperty("message", out JsonElement m) || !m.TryGetProperty("content", out JsonElement c) || c.ValueKind != JsonValueKind.Array) return;
                foreach (JsonElement b in c.EnumerateArray())
                    if (b.TryGetProperty("type", out JsonElement t) && t.GetString() == "thinking" && b.TryGetProperty("thinking", out JsonElement th))
                    {
                        string s = th.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) into.Append(s.Trim()).Append("\n\n");
                    }
            }
            catch (JsonException) { }
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
