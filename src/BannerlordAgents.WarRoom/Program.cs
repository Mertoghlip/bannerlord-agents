using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// WarRoom.exe [run] [--dir agent-folder]        answers every poke as it comes (the live loop)
    /// WarRoom.exe once [--dir agent-folder]          answers the newest poke now, and stops
    /// WarRoom.exe story [--dir agent-folder]         has the chronicler rewrite the story so far now, from the channel's chronicle
    /// WarRoom.exe council [--dir agent-folder]       a council of war sits now (the game must be running), and stops
    /// WarRoom.exe selftest                           checks the realm's memory in a scratch folder, without calling Claude
    /// WarRoom.exe replay --pokes file.jsonl --numbers 6,10,54 [--effort e] [--cap tokens] [--model m] [--tag t]
    ///                                                 answers recorded pokes again, each on its own, without memory, to time them
    /// WarRoom.exe replay --pokes file.jsonl --memory written|conversation [--numbers ...|--from a --to b] [--model m] [--tag t]
    ///                                                 answers recorded pokes in order with the realm's memory carried along,
    ///                                                 to compare the two memories (docs/MEMORY.md)
    /// </summary>
    /// <summary>
    /// One war room answers an agent folder's pokes. "run" holds this lock (a named mutex, per folder) while it runs, and
    /// "once" and "council" refuse to answer beside it: on Oct 16 a "once" beside a running war room answered the first
    /// poke too, and the game carried out both answers' orders.
    /// </summary>
    internal static class LiveLock
    {
        private static string Name(Config c)
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(c.AgentDir).TrimEnd('\\', '/').ToLowerInvariant()));
            return @"Local\BannerlordAgents.WarRoom." + Convert.ToHexString(hash, 0, 8);
        }

        /// <summary>The lock for "run", or null when another war room holds it.</summary>
        public static Mutex Take(Config c)
        {
            var m = new Mutex(false, Name(c));
            try
            {
                if (m.WaitOne(0)) return m;
            }
            catch (AbandonedMutexException) { return m; }   // a war room that was killed: the lock is ours now
            m.Dispose();
            return null;
        }

        /// <summary>Is a war room running for this folder?</summary>
        public static bool Running(Config c)
        {
            if (!Mutex.TryOpenExisting(Name(c), out Mutex m)) return false;
            using (m)
            {
                try
                {
                    if (!m.WaitOne(0)) return true;
                    m.ReleaseMutex();
                    return false;
                }
                catch (AbandonedMutexException) { m.ReleaseMutex(); return false; }
            }
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Native.Self();
            string mode = args.Length > 0 && !args[0].StartsWith("--") ? args[0].ToLowerInvariant() : "run";
            if (mode == "selftest") return SelfTest.Run();   // its own scratch folder: the live agent folder is never read or written
            if (mode == "toolcheck") return args.Contains("--write") ? ToolWrite(Arg(args, "--model") ?? "claude-haiku-5-5") : ToolCheck(Arg(args, "--model") ?? "claude-haiku-5-5", args.Contains("--big"));
            string dir = Arg(args, "--dir");
            Config c = Config.Load(dir);
            if ((mode == "once" || mode == "council") && LiveLock.Running(c))
            {
                Console.WriteLine("A war room is already answering the pokes of " + c.AgentDir + " (WarRoom run), so " + mode + " would answer beside it and the game "
                                  + "would carry out both sets of orders. Leave it to the running war room, or stop that one first.");
                return 1;
            }
            switch (mode)
            {
                case "run":
                    {
                        using Mutex live = LiveLock.Take(c);
                        if (live == null)
                        {
                            Console.WriteLine("A war room is already answering the pokes of " + c.AgentDir + "; this one stops, so no poke is answered twice.");
                            return 1;
                        }
                        using var cts = new CancellationTokenSource();
                        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
                        // the player's lieutenant (0.10) answers the player's chat, not pokes
                        if (c.IsLieutenant) return new LieutenantRoom(c).Run(cts.Token);
                        var room = new Room(c);
                        // a continuous agent (0.18): one conversation, its own wakes, reviews and handovers in it
                        return c.Continuous ? room.RunContinuous(cts.Token) : room.Run(cts.Token);
                    }
                case "once":
                    {
                        if (c.IsLieutenant)
                        {
                            // the lieutenant: the player's newest words in chat.json, answered now
                            System.Text.Json.Nodes.JsonObject chat = LieutenantRoom.ReadChat(c.ChannelDir);
                            if (chat == null) { Console.WriteLine("No chat.json in " + c.ChannelDir); return 1; }
                            new LieutenantRoom(c).Answer(chat);
                            return 0;
                        }
                        var room = new Room(c);
                        string poke = Path.Combine(c.ChannelDir, "poke.txt");
                        if (!File.Exists(poke)) { Console.WriteLine("No poke in " + c.ChannelDir); return 1; }
                        string text = File.ReadAllText(poke);
                        var m = Regex.Match(text, @"^POKE #(\d+), (.*)$", RegexOptions.Multiline);
                        if (!m.Success) { Console.WriteLine("poke.txt has no number"); return 1; }
                        return room.Answer(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), text).Ok ? 0 : 1;
                    }
                case "story": return Story(c);
                case "past": return Past(c, args);
                case "council": return new Room(c).CouncilNow("asked for by hand");
                case "replay": return Arg(args, "--memory") != null ? ReplayInOrder(c, args) : Replay(c, args);
                default:
                    Console.WriteLine("Usage: WarRoom [run|once|story|council|past|replay] [--dir agent-folder]; replay --pokes file.jsonl --numbers 6,10 [--memory written|conversation] [--effort e] [--model m] [--tag t]"
                                      + " [--news --calls <the war room's calls folder> --news-template agent/warroom-news.template.md]");
                    return 1;
            }
        }

        /// <summary>
        /// "WarRoom past [--dir agent-folder] [--campaign id] [--ask json]" (0.18.9): makes the word-for-word copy of every handover's conversation not kept yet (as the next
        /// load would), lists the campaign's kept conversations, and with --ask prints what the past question would answer with all of them in this game's history.
        /// </summary>
        private static int Past(Config c, string[] args)
        {
            string campaign = Arg(args, "--campaign");
            string mem = campaign != null ? Path.Combine(c.MemoryDir, campaign)
                : Directory.Exists(c.MemoryDir) ? Directory.EnumerateDirectories(c.MemoryDir).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (mem == null || !Directory.Exists(mem)) { Console.WriteLine("No campaign memory in " + c.MemoryDir); return 1; }
            Console.WriteLine("Campaign memory: " + mem);
            int made = PastTalk.Mend(mem, Console.WriteLine);
            List<PastTalk.About> all = PastTalk.All(mem);
            Console.WriteLine(made + " made now; " + all.Count + " kept:");
            foreach (PastTalk.About a in all)
                Console.WriteLine("  past\\" + a.Id + ": " + a.Exchanges + " exchanges, " + a.From + " to " + a.To + ", " + a.Chars.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
                                  + " characters; handover of " + a.Date + " (" + a.Why + "), game session " + (a.Load ?? "?") + ", conversation " + a.Session);
            string ask = Arg(args, "--ask");
            if (ask != null)
            {
                var q = System.Text.Json.Nodes.JsonNode.Parse(ask) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
                Console.WriteLine();
                Console.WriteLine(PastTalk.Answer(mem, all.Select(a => a.Id).ToList(), q));
            }
            return 0;
        }

        /// <summary>
        /// "WarRoom toolcheck [--model m]": one real, short Claude Code call (a small model by default) on the self-test's made-up map,
        /// asked to use the "ask the way" tool once: the whole path the agent's tool takes, in safe mode, outside the live folder.
        /// </summary>
        private static int ToolCheck(string model, bool big)
        {
            string root = Path.Combine(Path.GetTempPath(), "warroom-selftest");
            string data = Path.Combine(root, "agents-data");
            if (!File.Exists(Path.Combine(data, "live", "camp5", "view_orcs.json")) && SelfTest.Run() != 0) return 1;
            string channel = Path.Combine(root, "warroom"), logs = Path.Combine(root, "logs");
            string system = Path.Combine(root, "toolcheck-system.md");
            File.WriteAllText(system, "You are checking a tool. Use the ask_the_way tool exactly once, as asked, then answer in one short line.\n");
            var tool = new RouteTool(data, logs, "camp5", "orcs", 0, "check");
            // --big: a message as long as a real poke (about 50 KB), as the agent's calls are
            string ask = "Ask the way from lord_bolg to town_b with the ask_the_way tool, then say how many map units the shortest way is.";
            if (big) ask = "BACKGROUND (ignore it)\n" + string.Concat(Enumerable.Repeat("The orcs keep their watch on the mountain roads while the men of the west march on. ", 600)) + "\n\n" + ask;
            CallResult r = Claude.Run(Claude.Find(""), system, ask,
                                      model, "low", channel, true, 120, 1000, tools: tool);
            // and a call without the tool (the chronicler's kind), through the same writer of standard input
            string plainSystem = Path.Combine(root, "toolcheck-plain-system.md");
            File.WriteAllText(plainSystem, "You are checking a call. Reply in one word.\n");
            CallResult plain = Claude.Run(Claude.Find(""), plainSystem, "Reply with the single word ready.", model, "low", channel, false, 120, 1000);
            Console.WriteLine("Plain call: " + (plain.Ok ? "answered \"" + plain.Text?.Trim() + "\" in " + Math.Round(plain.Seconds, 1) + " s" : "FAILED: " + plain.Error));
            bool ok = r.Ok && tool.Asked == 1 && (r.Text ?? "").Contains("60") && plain.Ok;
            Console.WriteLine((ok ? "TOOL CHECK PASSED" : "TOOL CHECK FAILED") + ": asked " + tool.Asked + " time(s), " + r.Turns + " steps, " + Math.Round(r.Seconds, 1) + " s; answer: " + (r.Text ?? r.Error));
            // 0.17.7: which cache the writes went to, as Claude Code reported them (cache_ttl asks for one)
            foreach (var (name, c) in new[] { ("tool call", r), ("plain call", plain) })
                Console.WriteLine("Cache, " + name + ": " + (c.CacheWrite - c.CacheWrite1h) + " tokens written for five minutes, " + c.CacheWrite1h + " for an hour, "
                                  + c.CacheRead + " read (cache_ttl " + (Claude.CacheTtl.Length > 0 ? Claude.CacheTtl : "Claude Code's default") + ")");
            return ok ? 0 : 1;
        }

        /// <summary>
        /// "WarRoom toolcheck --write [--model m]" (0.13, docs/PLAN-0.13.md Part E): one real, short call (a small model by default) given the
        /// instructions' "Writing your own tools" and "Checking a tool" sections, asked to write a two-state tool with one mistake in its first
        /// draft, check it with check_tool, fix it and answer with the tool: it passes when it checked at least twice, the first refused and
        /// the last passing, and the tool it answers with passes the game's checker. Outside the live folder; no game needed.
        /// </summary>
        private static int ToolWrite(string model)
        {
            string root = Path.Combine(Path.GetTempPath(), "warroom-toolwrite");
            string channel = Path.Combine(root, "warroom"), logs = Path.Combine(root, "logs");
            try { Directory.Delete(root, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(channel);
            string template = null;
            for (DirectoryInfo up = new DirectoryInfo(AppContext.BaseDirectory); up != null && template == null; up = up.Parent)
                if (File.Exists(Path.Combine(up.FullName, "agent", "warroom-system.template.md"))) template = File.ReadAllText(Path.Combine(up.FullName, "agent", "warroom-system.template.md"));
            if (template == null) { Console.WriteLine("TOOL WRITE FAILED: the instructions' template (agent\\warroom-system.template.md) was not found above " + AppContext.BaseDirectory); return 1; }
            string Section(string head)
            {
                int a = template.IndexOf(head, StringComparison.Ordinal), b = a < 0 ? -1 : template.IndexOf("\n## ", a + 3, StringComparison.Ordinal);
                return a < 0 ? "" : b < 0 ? template.Substring(a) : template.Substring(a, b - a);
            }
            string system = Path.Combine(root, "toolwrite-system.md");
            File.WriteAllText(system, "You write tools for a realm's lords in a game, in the language below. Answer with ONLY the final tool's JSON object.\n\n" + Section("## Writing your own tools") + "\n" + Section("## Checking a tool"));
            var tool = new RouteTool(root, logs, "camp-write", "orcs", 0, "toolwrite");
            string ask = "Write a tool named two_step that takes at (own_fort: where the lord goes). State go: the lord enters at; once he has arrived, go to state rest. "
                         + "State rest: he waits 6 hours (wait 6); once that has arrived, the tool ends (free). In your FIRST draft, on purpose, put one mistake in a rule: "
                         + "write a condition that calls next_due with only two values. Check every draft with the check_tool tool, fix what it says, and check again until it passes. "
                         + "Then answer with ONLY the final tool's JSON object.";
            CallResult r = Claude.Run(Claude.Find(""), system, ask, model, "low", channel, false, 240, 4000, tools: tool);
            List<string> checks = File.Exists(Path.Combine(logs, "routes.jsonl")) ? File.ReadAllLines(Path.Combine(logs, "routes.jsonl")).Where(l => l.Contains("\"tool\":\"check_tool\"")).ToList() : new List<string>();
            bool firstRefused = checks.Count > 0 && checks[0].Contains("\"valid\":false"), lastPassed = checks.Count > 0 && checks[checks.Count - 1].Contains("\"valid\":true");
            string json = r.Ok ? RouteTool.ToolJson(LastObject(r.Text)) : null;
            BannerlordAgents.Tools.ToolDef d = json != null ? BannerlordAgents.Tools.ToolDef.FromJson(json, "two_step", "orcs") : null;
            // the loop that matters: it checked its tool, and what it answers with passes; a first draft refused and fixed is told, as the small model may skip the mistake asked for
            bool ok = r.Ok && checks.Count >= 1 && lastPassed && d != null && d.Valid && d.Name == "two_step" && d.States.ContainsKey("go") && d.States.ContainsKey("rest");
            Console.WriteLine((ok ? "TOOL WRITE PASSED" : "TOOL WRITE FAILED") + ": " + checks.Count + " checks (" + (firstRefused && lastPassed ? "a draft refused, then fixed" : "first " + (firstRefused ? "refused" : "passed")) + ", last " + (lastPassed ? "passed" : "not passed")
                              + "), " + r.Turns + " steps, " + Math.Round(r.Seconds, 1) + " s; the tool answered " + (d == null ? "could not be read" : d.Valid ? "passes the checker (" + d.States.Count + " states)" : "fails: " + string.Join("; ", d.Problems))
                              + (r.Ok ? "" : "; call: " + r.Error));
            if (!ok && r.Text != null) Console.WriteLine("Its answer:\n" + r.Text);
            return ok ? 0 : 1;
        }

        /// <summary>The last whole { ... } object in a text, as a node (null: none).</summary>
        private static JsonNode LastObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            for (int end = text.LastIndexOf('}'); end > 0; end = text.LastIndexOf('}', end - 1))
                for (int start = text.LastIndexOf('{', end); start >= 0; start = start > 0 ? text.LastIndexOf('{', start - 1) : -1)
                {
                    try { if (JsonNode.Parse(text.Substring(start, end - start + 1)) is JsonObject o && o["states"] != null) return o; }
                    catch (JsonException) { }
                    if (start == 0) break;
                }
            return null;
        }

        /// <summary>The story so far, rewritten now from every chronicle line it does not cover yet (for a first story, or a test).</summary>
        private static int Story(Config c)
        {
            var room = new Room(c);
            string meta = Room.ReadShared(Path.Combine(c.ChannelDir, "poke.json"));
            string campaign = null;
            try { if (meta != null && JsonNode.Parse(meta) is JsonObject o) campaign = (string)o["campaign"]; }
            catch (JsonException) { }
            if (campaign == null) { Console.WriteLine("No poke.json in " + c.ChannelDir + " yet: load the campaign first."); return 1; }
            Memory m = Memory.Open(c, campaign, room.Say);
            m.View(out _, out double storyHours, out _, out _, out _);
            List<ChronicleLine> fresh = Memory.ReadChronicle(c.ChannelDir).Where(l => double.IsNaN(storyHours) || l.Hours > storyHours + 1e-6).ToList();
            if (fresh.Count == 0) { Console.WriteLine("The story so far already covers the whole chronicle."); return 0; }
            return room.WriteStory(m, fresh.Take(400).ToList()) ? 0 : 1;
        }

        /// <summary>
        /// Recorded pokes, answered again in a scratch folder (no journal, no orders written), to measure answer time and
        /// tokens at a given model and effort. The answers are kept in the war room's call logs for reading side by side.
        /// </summary>
        private static int Replay(Config live, string[] args)
        {
            string file = Arg(args, "--pokes");
            string numbers = Arg(args, "--numbers");
            if (file == null || numbers == null || !File.Exists(file)) { Console.WriteLine("replay needs --pokes <file.jsonl> and --numbers 6,10,54"); return 1; }
            var want = new HashSet<int>(numbers.Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)));
            string scratch = Path.Combine(Path.GetTempPath(), "warroom-replay");
            Config c = Scratch(live, scratch, args, "written");
            // a news replay (0.14, docs/PLAN-news.md): its own logs, so the live calls.jsonl keeps only live calls
            bool news = args.Contains("--news");
            var room = news ? new Room(c, Path.Combine(scratch, "logs")) : new Room(c);
            int? cap = Arg(args, "--cap") != null ? int.Parse(Arg(args, "--cap"), CultureInfo.InvariantCulture) : (int?)null;
            string tag = Arg(args, "--tag") ?? ((news ? "news-" : "replay-") + c.Model + "-" + c.Effort + (cap.HasValue ? "-cap" + cap : ""));
            string calls = Arg(args, "--calls");
            var rows = new List<string>();
            foreach (Recorded p in ReadPokes(file).Where(p => want.Contains(p.Number)))
            {
                if (news && p.News != null)
                {
                    // the poke as the bridge would have sent it in theatre mode: its poke.json with its news, its question book in the channel
                    File.WriteAllText(Path.Combine(c.ChannelDir, "poke.json"), new JsonObject
                    {
                        ["number"] = p.Number, ["hours"] = p.Hours, ["date"] = When(p.Hours),
                        // --campaign: a campaign with a view file and a map, so the way-finding questions and their instructions are there, as in a live call
                        // (they answer from that file's newest hour, so use it to measure a call's size, not to judge its answers)
                        ["campaign"] = Arg(args, "--campaign") ?? "replay", ["realm"] = p.Realm, ["realm_name"] = p.RealmName, ["load_id"] = "replay",
                        ["news"] = JsonNode.Parse(p.News)
                    }.ToJsonString(), new UTF8Encoding(false));
                    string book = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file)), "book", "poke-" + p.Number + ".json");
                    Directory.CreateDirectory(Path.Combine(c.ChannelDir, "book"));
                    if (File.Exists(book)) File.Copy(book, Path.Combine(c.ChannelDir, "book", "poke-" + p.Number + ".json"), true);
                }
                // the memory the original call read (its prompt up to the poke), when its calls folder is given; cut to the news in a news replay
                room.ReplayMemory = calls != null ? OriginalMemory(calls, p.Number) : null;
                CallResult res = room.Answer(p.Number, p.Date, PokeText(p), false, null, tag, cap);
                string was = calls != null ? Original(calls, p.Number) : "";
                rows.Add("#" + p.Number + "\t" + c.Model + "\t" + c.Effort + "\t" + res.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s\t" + (res.InTokens + res.CacheRead + res.CacheWrite) + " read\t"
                         + res.OutTokens + " out\t" + res.Thinking + " thinking\t" + res.Turns + " steps\t" + (res.Ok ? "ok" : "failed: " + res.Error) + (was.Length > 0 ? "\n\twas: " + was : ""));
            }
            Console.WriteLine();
            Console.WriteLine("poke\tmodel\teffort\ttime\tread\twritten\tthinking\tsteps\tresult");
            foreach (string row in rows) Console.WriteLine(row);
            if (news) Console.WriteLine("The calls (prompt, answer, thinking and the questions asked): " + Path.Combine(scratch, "logs"));
            return 0;
        }

        /// <summary>The call folder of a poke's own fast answer, in a war room's calls folder (its date folders), or null.</summary>
        private static string CallFolder(string calls, int number)
        {
            if (!Directory.Exists(calls)) return null;
            return Directory.GetDirectories(calls, "*-poke-" + number, SearchOption.AllDirectories).OrderBy(d => d, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>A poke's original call's memory: its prompt up to "Your last answer was ..." (the story, the notes, the chronicle lines), or null.</summary>
        private static string OriginalMemory(string calls, int number)
        {
            string folder = CallFolder(calls, number);
            string prompt = folder != null && File.Exists(Path.Combine(folder, "prompt.txt")) ? File.ReadAllText(Path.Combine(folder, "prompt.txt")) : null;
            if (prompt == null) return null;
            int at = prompt.IndexOf("Your last answer was to poke #", StringComparison.Ordinal);
            if (at < 0) at = prompt.IndexOf("You have not answered any poke yet", StringComparison.Ordinal);
            return at > 0 ? prompt.Substring(0, at) : null;
        }

        /// <summary>A poke's original fast answer in a few words, from its call folder: its orders, and its time, steps, tokens and questions, from the call's stream.</summary>
        private static string Original(string calls, int number)
        {
            string folder = CallFolder(calls, number);
            if (folder == null) return "(no original call found)";
            var parts = new List<string>();
            string answer = Path.Combine(folder, "answer.json");
            if (File.Exists(answer) && JsonNode.Parse(File.ReadAllText(answer)) is JsonObject a)
                parts.Add((a["orders"] as JsonArray)?.Count + " orders: " + string.Join("; ", (a["orders"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                    .Select(o => (string)o["lord"] + " " + (string)o["do"] + " " + ((string)o["at"] ?? (string)o["target"] ?? ""))));
            string raw = Path.Combine(folder, "raw.jsonl");
            if (File.Exists(raw))
            {
                int asks = 0;
                foreach (string line in File.ReadLines(raw))
                {
                    if (line.Contains("\"type\":\"tool_use\"")) asks += Regex.Matches(line, "\"type\":\"tool_use\"").Count;
                    if (!line.StartsWith("{\"type\":\"result\"", StringComparison.Ordinal)) continue;
                    try
                    {
                        JsonObject r = JsonNode.Parse(line) as JsonObject;
                        parts.Add(((double?)r?["duration_ms"] / 1000.0)?.ToString("0.0", CultureInfo.InvariantCulture) + " s, " + (int?)r?["num_turns"] + " steps, "
                                  + ((long?)r?["usage"]?["input_tokens"] + (long?)r?["usage"]?["cache_read_input_tokens"] + (long?)r?["usage"]?["cache_creation_input_tokens"]) + " read in all");
                    }
                    catch (JsonException) { }
                }
                parts.Add(asks + " questions");
            }
            return string.Join("; ", parts);
        }

        /// <summary>
        /// Recorded pokes answered in order, the realm's memory carried from each answer to the next, in a scratch folder of
        /// its own (nothing reaches the game). The chronicle grows from the recorded pokes' reasons, the fights the bridge now
        /// keeps quiet left out, and from the replay's own orders (nothing is carried out, so they have no outcomes). With the
        /// written memory, the chronicler rewrites the story whenever it is due, between pokes; with the conversation, every
        /// poke goes into one conversation. Prints each call's time and size, and where the answers are, to compare.
        /// </summary>
        private static int ReplayInOrder(Config live, string[] args)
        {
            string file = Arg(args, "--pokes");
            string memory = Arg(args, "--memory");
            if (file == null || !File.Exists(file) || (memory != "written" && memory != "conversation"))
            {
                Console.WriteLine("replay in order needs --pokes <file.jsonl> and --memory written|conversation, with --numbers 1,6,10 or --from 1 --to 40");
                return 1;
            }
            List<Recorded> all = ReadPokes(file);
            HashSet<int> want;
            if (Arg(args, "--numbers") != null) want = new HashSet<int>(Arg(args, "--numbers").Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)));
            else
            {
                int from = int.Parse(Arg(args, "--from") ?? "1", CultureInfo.InvariantCulture), to = int.Parse(Arg(args, "--to") ?? "100000", CultureInfo.InvariantCulture);
                // the pokes the bridge would still send: not the ones made only of small fights (version 0.3.1)
                want = new HashSet<int>(all.Where(p => p.Number >= from && p.Number <= to && p.Triggers.Any(t => !Quiet(t))).Select(p => p.Number));
            }
            string tag = Arg(args, "--tag") ?? ("order-" + memory + "-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture));
            string scratch = Path.Combine(Path.GetTempPath(), "warroom-replay-" + tag);
            Config c = Scratch(live, scratch, args, memory);
            var room = new Room(c);
            string channel = c.ChannelDir;
            var chronicle = new List<string>();
            var rows = new List<string>();
            var times = new List<double>();
            long tokensIn = 0, tokensOut = 0;
            int stories = 0;
            Console.WriteLine("Replaying " + want.Count + " pokes in order, memory " + memory + ", in " + scratch);
            foreach (Recorded p in all)
            {
                foreach (Trigger t in p.Triggers.Where(t => !Quiet(t)))
                    chronicle.Add(Line(t.Hours, t.Kind, t.Text));
                if (!want.Contains(p.Number)) continue;
                File.WriteAllText(Path.Combine(channel, "chronicle.jsonl"), string.Concat(chronicle.Select(l => l + "\n")), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(channel, "poke.json"), new JsonObject
                {
                    ["number"] = p.Number, ["hours"] = p.Hours, ["date"] = When(p.Hours), ["campaign"] = "replay", ["realm"] = p.Realm, ["realm_name"] = p.RealmName, ["load_id"] = "replay"
                }.ToJsonString(), new UTF8Encoding(false));
                CallResult r = room.Answer(p.Number, When(p.Hours), PokeText(p), true, null, tag);
                times.Add(r.Seconds);
                tokensIn += r.InTokens + r.CacheRead + r.CacheWrite;
                tokensOut += r.OutTokens;
                // its orders become chronicle lines, at the poke's hour
                foreach (string o in Orders(Path.Combine(channel, "orders", "poke-" + p.Number + ".json"))) chronicle.Add(Line(p.Hours, "order", o));
                File.WriteAllText(Path.Combine(channel, "chronicle.jsonl"), string.Concat(chronicle.Select(l => l + "\n")), new UTF8Encoding(false));
                if (memory == "written")
                {
                    List<ChronicleLine> due = room.DueStory(p.Hours);
                    if (due != null && room.WriteStory(room.Mem, due, default, tag)) stories++;
                }
                rows.Add("#" + p.Number + "\t" + r.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s\t" + (r.InTokens + r.CacheRead + r.CacheWrite).ToString("N0", CultureInfo.InvariantCulture)
                         + " read\t" + r.OutTokens.ToString("N0", CultureInfo.InvariantCulture) + " written\t" + r.Thinking.ToString("N0", CultureInfo.InvariantCulture) + " thinking\t" + (r.Ok ? "ok" : "failed: " + r.Error));
            }
            Console.WriteLine();
            Console.WriteLine("poke\ttime\tread\twritten\tthinking\tresult");
            foreach (string row in rows) Console.WriteLine(row);
            times.Sort();
            Console.WriteLine();
            Console.WriteLine("memory " + memory + ": " + times.Count + " answers, median " + (times.Count > 0 ? times[times.Count / 2].ToString("0.0", CultureInfo.InvariantCulture) : "-") + " s; "
                              + tokensIn.ToString("N0", CultureInfo.InvariantCulture) + " tokens read, " + tokensOut.ToString("N0", CultureInfo.InvariantCulture) + " written"
                              + (memory == "written" ? "; the story rewritten " + stories + " times" : "") + ".");
            Console.WriteLine("The notes, the story and the answers: " + scratch + " and the war room's call logs (tag " + tag + ").");
            return 0;
        }

        /// <summary>A scratch agent folder: the live instructions, chronicler and order library, no journal, no look-ups.</summary>
        private static Config Scratch(Config live, string scratch, string[] args, string memory)
        {
            try { Directory.Delete(scratch, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(Path.Combine(scratch, live.Channel, "orders"));
            if (File.Exists(live.SystemTemplate)) File.Copy(live.SystemTemplate, Path.Combine(scratch, "warroom-system.md"), true);
            if (File.Exists(live.ChroniclerTemplate)) File.Copy(live.ChroniclerTemplate, Path.Combine(scratch, "chronicler-system.md"), true);
            // a news wake's sections (0.14): the agent's own, else the repository's template beside the war room
            string newsFrom = File.Exists(live.NewsTemplate) ? live.NewsTemplate : Arg(args, "--news-template");
            if (newsFrom != null && File.Exists(newsFrom)) File.Copy(newsFrom, Path.Combine(scratch, "warroom-news.md"), true);
            string tools = Path.Combine(live.ChannelDir, "tools.txt");
            if (File.Exists(tools)) File.Copy(tools, Path.Combine(scratch, live.Channel, "tools.txt"), true);
            var c = new Config
            {
                AgentDir = scratch, Channel = live.Channel, Model = Arg(args, "--model") ?? live.Model, Effort = Arg(args, "--effort") ?? live.Effort,
                UrgentEffort = Arg(args, "--effort") ?? live.UrgentEffort, Urgent = live.Urgent, TimeoutSeconds = live.TimeoutSeconds, ClaudePath = live.ClaudePath, Lookups = args.Contains("--lookups"),
                ThinkingCap = live.ThinkingCap, UrgentThinkingCap = live.UrgentThinkingCap, MaxLessons = live.MaxLessons, MemoryMode = memory,
                StoryEvery = live.StoryEvery, StoryWords = live.StoryWords, StoryModel = Arg(args, "--model") ?? live.StoryModel, StoryEffort = live.StoryEffort,
                StoryThinkingCap = live.StoryThinkingCap, ConversationLimit = live.ConversationLimit
            };
            c.Save();
            return c;
        }

        internal sealed class Trigger
        {
            public double Hours;
            public string Kind = "", Text = "";
        }

        private sealed class Recorded
        {
            public int Number;
            public double Hours;
            public string Date = "", Realm = "", RealmName = "", Report = "";
            /// <summary>A news replay's poke (SmokeTest --newspokes): its theatre's news for poke.json, as JSON; null otherwise.</summary>
            public string News;
            public List<Trigger> Triggers = new List<Trigger>();
        }

        private static List<Recorded> ReadPokes(string file)
        {
            var list = new List<Recorded>();
            foreach (string line in File.ReadLines(file))
            {
                if (line.Length < 20) continue;
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement r = doc.RootElement;
                if (!r.TryGetProperty("number", out JsonElement num) || !r.TryGetProperty("report", out JsonElement rep)) continue;
                var p = new Recorded
                {
                    Number = num.GetInt32(), Hours = r.TryGetProperty("hours", out JsonElement h) ? h.GetDouble() : 0, Date = r.TryGetProperty("date", out JsonElement d) ? d.GetString() : "",
                    Realm = r.TryGetProperty("faction", out JsonElement f) ? f.GetString() : "", RealmName = r.TryGetProperty("faction_name", out JsonElement fn) ? fn.GetString() : "", Report = rep.GetString()
                };
                if (r.TryGetProperty("news", out JsonElement nw) && nw.ValueKind == JsonValueKind.Object) p.News = nw.GetRawText();
                if (r.TryGetProperty("triggers", out JsonElement ts) && ts.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement t in ts.EnumerateArray())
                        p.Triggers.Add(new Trigger
                        {
                            Hours = t.TryGetProperty("hours", out JsonElement th) ? th.GetDouble() : p.Hours, Kind = t.TryGetProperty("kind", out JsonElement k) ? k.GetString() : "",
                            Text = t.TryGetProperty("text", out JsonElement tx) ? tx.GetString() : ""
                        });
                list.Add(p);
            }
            return list.OrderBy(p => p.Number).ToList();
        }

        private static string PokeText(Recorded p)
            => "POKE #" + p.Number + ", " + When(p.Hours) + "\nAnswer with one JSON object that says \"poke\": " + p.Number + ".\nYour orders right now: REPLAY. Nothing is carried out.\n\n" + p.Report;

        private static readonly Regex BattleSides = new Regex(@"^Battle(?: at [^:]+)?: (.+?) \((.+?)\) against (.+?) \((.+?)\);", RegexOptions.Compiled);

        /// <summary>
        /// What the bridge now keeps out of pokes and the chronicle, judged from a recorded reason's words: a quiet day, the
        /// first look, a lord sighted (only armies are history), and a fight without lords on both sides away from any
        /// holding: caravans, villagers, bandits (version 0.3.1).
        /// </summary>
        internal static bool Quiet(Trigger t)
        {
            if (t.Kind == "quiet_day" || t.Kind == "first_look") return true;
            if (t.Kind == "enemy_in_view") return !t.Text.StartsWith("Enemy army", StringComparison.Ordinal);
            if (t.Kind != "battle" || t.Text.StartsWith("Assault", StringComparison.Ordinal) || t.Text.StartsWith("Fight over a raid", StringComparison.Ordinal)
                || t.Text.StartsWith("Battle at ", StringComparison.Ordinal)) return false;
            Match m = BattleSides.Match(t.Text);
            if (!m.Success) return false;
            return !Lordly(m.Groups[1].Value, m.Groups[2].Value) || !Lordly(m.Groups[3].Value, m.Groups[4].Value);
        }

        private static bool Lordly(string name, string realm)
            => name != realm && !name.StartsWith("Caravan of ", StringComparison.Ordinal) && !name.StartsWith("Villagers of ", StringComparison.Ordinal)
               && name.IndexOf("Patrol", StringComparison.Ordinal) < 0 && name.IndexOf("Soldiers", StringComparison.Ordinal) < 0;

        private static string Line(double hours, string kind, string text)
            => new JsonObject { ["hours"] = hours, ["date"] = When(hours), ["kind"] = kind, ["text"] = text }.ToJsonString();

        /// <summary>A replay's own orders, as the chronicle would keep them.</summary>
        private static IEnumerable<string> Orders(string path)
        {
            if (!File.Exists(path)) yield break;
            JsonObject o;
            try { o = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
            catch (JsonException) { yield break; }
            if (o?["orders"] is not JsonArray list) yield break;
            foreach (JsonNode n in list)
            {
                if (n is not JsonObject a) continue;
                string where = (string)(a["at"] as JsonValue) ?? (string)(a["target"] as JsonValue) ?? "";
                string why = (string)(a["why"] as JsonValue);
                yield return "You ordered: " + (string)a["lord"] + " " + (string)a["do"] + (where.Length > 0 ? " " + where : "") + "." + (string.IsNullOrWhiteSpace(why) ? "" : " Why: " + why.Trim().TrimEnd('.') + ".");
            }
        }

        /// <summary>The game's date from in-game hours: 24 hours a day, 21 days a season, 4 seasons (TAOM keeps the game's calendar).</summary>
        public static string When(double hours)
        {
            long h = (long)Math.Floor(hours), days = h / 24, year = days / 84;
            int dayOfYear = (int)(days % 84), hour = (int)(h % 24);
            string[] seasons = { "Spring", "Summer", "Autumn", "Winter" };
            return seasons[dayOfYear / 21] + " " + (dayOfYear % 21 + 1) + ", " + year + ", " + hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
