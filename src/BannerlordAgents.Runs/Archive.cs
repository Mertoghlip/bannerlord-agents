using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BannerlordAgents.Common;
using static BannerlordAgents.Runs.RunData;

namespace BannerlordAgents.Runs
{
    /// <summary>
    /// The archive (Nov 11: everything about each test and each of its sessions kept in one folder, for the reports): archive\(run) (test)\ under the records folder,
    /// laid out by the game's sessions (each load of the game in the run) and by agent:
    /// - run\: the run's own record (run.json, setup.json, days.jsonl, events.jsonl, its report);
    /// - sessions\(n) - (load)\(agent)\: that session's calls (calls.jsonl, and each call's folder: prompt, Claude Code's event stream, thinking, answer, handover),
    ///   the questions asked of its tools with their answers (questions.jsonl), and the war room's log;
    /// - agents\(agent)\: its conversations (Claude Code's own transcript, which Claude Code deletes after 30 days, and a readable copy), its memory (notes, story,
    ///   snapshots, the conversation's state and checkpoints, handovers, the earlier conversations kept word for word) and its instructions (the system prompts its calls
    ///   used, its folder's settings and order library);
    /// - campaign\ and you\: the campaign's records in the run (the bundle's choice of lines), the lieutenant's chat, the orders carried at each of the run's saves;
    /// - game-logs\(launch)\: the game's own logs of each launch in the run (the game keeps only its newest 30);
    /// - README.md: what is where, each session and each agent's calls and cost.
    /// Copies only what changed; never deletes; a file that changed other than by growing keeps its old copy under earlier\(time)\. Runs.exe archive [run|all] makes
    /// it by hand; Runs.exe archive --follow (started with the war rooms by scripts\agents.ps1) keeps the open run's archive up to date every 10 minutes while the game
    /// runs and as it closes, and makes one for every run that has none.
    /// </summary>
    internal static class Archive
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string Dir => Path.Combine(Root, "archive");
        public const int EveryMinutes = 10;

        private sealed class Pass
        {
            public string Root, Stamp;
            public int Copied, Kept;
            public long Bytes;
            public List<string> Missing = new List<string>();
            /// <summary>How far each campaign file has been read for this run (archive path: source, bytes read), kept in .cursors.json beside the archive.</summary>
            public Dictionary<string, (string Src, long Pos)> Cursors = new Dictionary<string, (string, long)>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Seg
        {
            public int N;
            public string Load, How, Folder;
            public DateTime From, To;
            public double H0, H1;
        }

        private sealed class Agent
        {
            public string Name, Realm, Kind, Mode, LogDir, Workspace;
        }

        // ---------- the commands ----------

        /// <summary>Runs.exe archive [run|all]: one run's archive brought up to date (the open or newest when none is named), or every run's.</summary>
        public static int Run(string id)
        {
            Native.Self();
            string open = OpenRun();
            List<string> ids = id == "all" ? All() : new List<string> { Pick(id) }.Where(x => x != null).ToList();
            if (ids.Count == 0) { Console.WriteLine("No test run yet (" + RunsDir + ")."); return 2; }
            int missing = 0;
            foreach (string r in ids)
            {
                Pass p = Locked(() => One(r, r == open));
                if (p == null) continue;
                Console.WriteLine(Say(r, p));
                foreach (string m in p.Missing.Distinct().Take(10)) Console.WriteLine("  ! missing: " + m);
                missing += p.Missing.Count;
            }
            return missing == 0 ? 0 : 1;
        }

        /// <summary>Runs.exe archive --follow: the open run's archive kept up to date while the game runs, as it closes and once more five minutes later; every run with no archive made at the start.</summary>
        public static int Follow()
        {
            Native.Self();
            using var only = new Mutex(false, @"Local\BannerlordAgents.ArchiveFollower");
            if (!only.WaitOne(0)) { Console.WriteLine("The archive is already being kept (another Runs.exe archive --follow)."); return 1; }
            Log("Keeping the archive in " + Dir + ": every " + EveryMinutes + " minutes while the game runs, as it closes and five minutes after.");
            foreach (string id in All().Where(x => !HasArchive(x)))
                Try(id, id == OpenRun(), "the first archive of an earlier run");
            string last = OpenRun();
            if (last != null) Try(last, true, "the open run, as the archive keeper starts");
            DateTime lastPass = DateTime.Now, closedAt = DateTime.MinValue;
            bool gameWas = GameRunning();
            while (true)
            {
                Thread.Sleep(15000);
                string open = OpenRun();
                bool game = GameRunning();
                if (gameWas && !game) closedAt = DateTime.Now;
                gameWas = game;
                if (last != null && open != last) Try(last, false, "its last pass: it is no longer the open run");
                string why = open == null ? null
                    : open != last ? "a new open run"
                    : closedAt != DateTime.MinValue && (DateTime.Now - closedAt).TotalSeconds < 20 ? "the game has closed"
                    : closedAt != DateTime.MinValue && (DateTime.Now - closedAt).TotalMinutes >= 5 && lastPass < closedAt.AddMinutes(5) ? "five minutes after the game closed (handovers written at its close)"
                    : game && (DateTime.Now - lastPass).TotalMinutes >= EveryMinutes ? "every " + EveryMinutes + " minutes while the game runs"
                    : null;
                last = open;
                if (why == null) continue;
                Try(open, true, why);
                lastPass = DateTime.Now;
            }
        }

        private static void Try(string id, bool open, string why)
        {
            try
            {
                Pass p = Locked(() => One(id, open));
                if (p != null) Log(Say(id, p) + " (" + why + ")" + (p.Missing.Count > 0 ? "; missing: " + string.Join("; ", p.Missing.Distinct().Take(5)) : ""));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is FormatException)
            {
                Log("Run " + id + ": the archive pass FAILED (" + e.GetType().Name + ": " + e.Message + "); the next pass tries again.");
            }
        }

        private static string Say(string id, Pass p) =>
            "Run " + id + ": " + p.Copied + " file" + (p.Copied == 1 ? "" : "s") + " copied or written (" + (p.Bytes / 1048576.0).ToString("0.0", Inv) + " MB)"
            + (p.Kept > 0 ? ", " + p.Kept + " earlier version" + (p.Kept == 1 ? "" : "s") + " kept" : "") + " in " + p.Root;

        /// <summary>One pass at a time, whoever asks (the keeper or a pass by hand).</summary>
        private static Pass Locked(Func<Pass> pass)
        {
            using var m = new Mutex(false, @"Local\BannerlordAgents.ArchivePass");
            bool got = false;
            try { got = m.WaitOne(TimeSpan.FromMinutes(15)); }
            catch (AbandonedMutexException) { got = true; }
            try { return pass(); }
            finally { if (got) m.ReleaseMutex(); }
        }

        private static void Log(string line)
        {
            string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + "  " + line;
            Console.WriteLine(text);
            try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "archive.log"), text + "\n", new UTF8Encoding(false)); }
            catch (IOException) { }
        }

        /// <summary>The open run: the one current.txt names, while it is armed or running.</summary>
        public static string OpenRun()
        {
            string cur = Path.Combine(RunsDir, "current.txt");
            if (!File.Exists(cur)) return null;
            string id = ReadShared(cur).Trim();
            if (id.Length == 0 || !File.Exists(Path.Combine(RunsDir, id, "run.json"))) return null;
            string status = Str(Obj(Path.Combine(RunsDir, id, "run.json")), "status");
            return status == "running" || status == "armed" ? id : null;
        }

        private static readonly Regex GameProcess = new Regex(@"^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+)$", RegexOptions.Compiled);

        private static bool GameRunning()
        {
            bool found = false;
            foreach (Process p in Process.GetProcesses())
            {
                try { if (!found && GameProcess.IsMatch(p.ProcessName)) found = true; }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
            return found;
        }

        // ---------- one run ----------

        /// <summary>The test's name: its saves' (the first "TEST ..." save, less " START"), else its arm.</summary>
        public static string Label(RunData r)
        {
            List<string> saves = (r.State?["saves"] as JsonArray)?.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
            string test = saves.FirstOrDefault(x => x.StartsWith("TEST ", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("TEST END", StringComparison.OrdinalIgnoreCase));
            if (test != null && test.EndsWith(" START", StringComparison.OrdinalIgnoreCase)) test = test.Substring(0, test.Length - 6);
            return test ?? "arm " + r.Arm;
        }

        private static string Existing(string id) =>
            !Directory.Exists(Dir) ? null : Directory.GetDirectories(Dir).FirstOrDefault(d => Path.GetFileName(d) == id || Path.GetFileName(d).StartsWith(id + " ", StringComparison.Ordinal));

        private static bool HasArchive(string id) => Existing(id) is string d && File.Exists(Path.Combine(d, "README.md"));

        /// <summary>The run's archive folder, "(run) (test)"; renamed when the test's name is known later.</summary>
        private static string FolderFor(RunData r)
        {
            Directory.CreateDirectory(Dir);
            string want = Path.Combine(Dir, Bundle.Safe(r.Id + " " + Label(r)));
            string have = Existing(r.Id);
            if (have == null) return want;
            if (!string.Equals(have, want, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(want))
            {
                try { Directory.Move(have, want); return want; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return have;
        }

        private static Pass One(string id, bool open)
        {
            RunData r = Load(id, calls: false);
            if (r.State == null) return null;
            if (!open) Close(r);
            var p = new Pass { Root = FolderFor(r), Stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", Inv) };
            Directory.CreateDirectory(p.Root);
            string campaign = Str(r.State, "campaign") ?? "unknown";
            string data = Path.Combine(Root, "data", Bundle.Safe(campaign));
            foreach (string f in new[] { "run.json", "setup.json", "days.jsonl", "events.jsonl", "watch.json", "report.md", "report.json", "bundle-manifest.json" })
                Copy(p, Path.Combine(r.Dir, f), Path.Combine(p.Root, "run", f));
            List<Seg> segs = Segments(r);
            List<Agent> agents = Agents(r);
            var tallies = new Dictionary<string, Dictionary<int, Tally>>();
            foreach (Agent a in agents)
            {
                tallies[a.Name] = Sessions(p, r, a, segs);
                Conversations(p, r, a);
                Instructions(p, r, a, open);
                Memory(p, r, a, campaign, open);
            }
            List<(string Name, int Session)> launches = GameLogs(p, r, segs);
            Campaign(p, r, data, segs);
            Readme(p, r, segs, agents, tallies, launches, campaign, open);
            return p;
        }

        /// <summary>
        /// A run no longer open whose last session has no end (the game closed or crashed, then another run began): it ended when its record was last written, not now,
        /// so lines that carry no run's name are not taken from the runs after it.
        /// </summary>
        private static void Close(RunData r)
        {
            DateTime end = File.GetLastWriteTime(Path.Combine(r.Dir, "run.json")).AddMinutes(1);
            for (int i = 0; i < r.Windows.Count; i++)
                if (r.Windows[i].To > end) r.Windows[i] = (r.Windows[i].From, end > r.Windows[i].From ? end : r.Windows[i].From);
            if (r.State?["segments"] is JsonArray segs && segs.Count > 0 && segs[segs.Count - 1] is JsonObject last && string.IsNullOrEmpty(Str(last, "wall_to")))
                last["wall_to"] = end.ToString("yyyy-MM-dd HH:mm:ss", Inv);
        }

        private static List<Seg> Segments(RunData r)
        {
            var list = new List<Seg>();
            double last = Dbl(r.State, "last_hours");
            foreach (JsonNode s in r.State?["segments"] as JsonArray ?? new JsonArray())
            {
                DateTime from = Wall(Str(s, "wall_from")), to = Wall(Str(s, "wall_to"));
                if (from == DateTime.MinValue) continue;
                double h1 = Dbl(s, "to");
                var g = new Seg
                {
                    N = list.Count + 1, Load = Str(s, "load") ?? "?", How = Str(s, "how") ?? "", From = from, To = to == DateTime.MinValue ? DateTime.Now.AddMinutes(1) : to,
                    H0 = Dbl(s, "from"), H1 = h1 > 0 ? h1 : last
                };
                g.Folder = g.N + " - " + g.Load;
                list.Add(g);
            }
            return list;
        }

        private static List<Agent> Agents(RunData r)
        {
            var list = new List<Agent>();
            foreach (JsonNode a in r.Setup?["agents"] as JsonArray ?? new JsonArray())
            {
                string kind = Str(a, "kind") ?? "realm", realm = Str(a, "realm"), logDir = Str(a, "log_dir");
                if (string.IsNullOrEmpty(logDir)) logDir = Path.Combine(Root, "logs", "warroom");
                list.Add(new Agent { Name = kind == "lieutenant" ? "lieutenant" : realm ?? "agent", Realm = realm, Kind = kind, Mode = Str(a, "mode"), LogDir = logDir, Workspace = Str(a, "workspace") });
            }
            return list;
        }

        /// <summary>The session a moment belongs to: the last that began at or before it (the first for anything earlier); -1 outside the run when strict.</summary>
        private static int SessionOf(List<Seg> segs, DateTime t, bool strict)
        {
            if (segs.Count == 0) return -1;
            if (strict && (t < segs[0].From.AddMinutes(-2) || t > segs[segs.Count - 1].To)) return -1;
            int at = 0;
            for (int i = 0; i < segs.Count; i++) if (segs[i].From <= t) at = i;
            return at;
        }

        // ---------- each session's calls, questions and log ----------

        private sealed class Tally
        {
            public int Calls, Wakes, Reviews, Handovers, Chats, Failed, Questions;
            public double Cost;
        }

        private static Dictionary<int, Tally> Sessions(Pass p, RunData r, Agent a, List<Seg> segs)
        {
            var tally = new Dictionary<int, Tally>();
            if (segs.Count == 0) return tally;
            string callsFile = Path.Combine(a.LogDir, "calls.jsonl");
            List<string> calls = Bundle.SelectWall(callsFile, r.Windows, "at", r.Id);
            bool tagged = calls.Any(l => l.Contains("\"run\":\"" + r.Id + "\""));
            var byCalls = new Dictionary<int, List<string>>();
            foreach (string l in calls)
            {
                JsonObject o = Bundle.Parse(l);
                if (o == null) continue;
                int s = SessionOf(segs, Wall(Str(o, "at")), !tagged);
                if (s < 0) continue;
                (byCalls.TryGetValue(s, out List<string> list) ? list : byCalls[s] = new List<string>()).Add(l);
                Tally t = tally.TryGetValue(s, out Tally x) ? x : tally[s] = new Tally();
                string kind = Str(o, "kind") ?? "";
                t.Calls++;
                t.Cost += Dbl(o, "cost_usd_list");
                if (Str(o, "ok") == "false") t.Failed++;
                if (kind == "answer") t.Wakes++;
                else if (kind == "council") t.Reviews++;
                else if (kind == "handover") t.Handovers++;
                else if (kind == "chat") t.Chats++;
                string folder = Str(o, "folder");
                if (folder == null) continue;
                if (!Directory.Exists(folder)) { p.Missing.Add(a.Name + "'s call folder " + Path.GetFileName(folder) + " (" + kind + ", " + Str(o, "at") + ")"); continue; }
                CopyDir(p, folder, Path.Combine(p.Root, "sessions", segs[s].Folder, a.Name, "calls", Path.GetFileName(Path.GetDirectoryName(folder)) + "-" + Path.GetFileName(folder)));
            }
            foreach (KeyValuePair<int, List<string>> kv in byCalls) Write(p, Path.Combine(p.Root, "sessions", segs[kv.Key].Folder, a.Name, "calls.jsonl"), kv.Value);
            var byQuestion = new Dictionary<int, List<string>>();
            foreach (string l in Bundle.SelectWall(Path.Combine(a.LogDir, "routes.jsonl"), r.Windows, "at"))
            {
                JsonObject o = Bundle.Parse(l);
                int s = o == null ? -1 : SessionOf(segs, Wall(Str(o, "at")), true);
                if (s < 0) continue;
                (byQuestion.TryGetValue(s, out List<string> list) ? list : byQuestion[s] = new List<string>()).Add(l);
                (tally.TryGetValue(s, out Tally t) ? t : tally[s] = new Tally()).Questions++;
            }
            foreach (KeyValuePair<int, List<string>> kv in byQuestion) Write(p, Path.Combine(p.Root, "sessions", segs[kv.Key].Folder, a.Name, "questions.jsonl"), kv.Value);
            var byLog = new Dictionary<int, List<string>>();
            foreach ((DateTime t, string line) in LogLines(a.LogDir, segs[0].From.AddMinutes(-2), segs[segs.Count - 1].To))
            {
                int s = SessionOf(segs, t, true);
                if (s >= 0) (byLog.TryGetValue(s, out List<string> list) ? list : byLog[s] = new List<string>()).Add(line);
            }
            foreach (KeyValuePair<int, List<string>> kv in byLog) Write(p, Path.Combine(p.Root, "sessions", segs[kv.Key].Folder, a.Name, "warroom.log"), kv.Value);
            return tally;
        }

        private static readonly Regex LogTime = new Regex(@"^(\d\d):(\d\d):(\d\d)\s", RegexOptions.Compiled);

        /// <summary>The war room's log lines between two moments (warroom-(day).log, each line's time; a line without one goes with the line before it), dated.</summary>
        private static List<(DateTime, string)> LogLines(string logDir, DateTime from, DateTime to)
        {
            var list = new List<(DateTime, string)>();
            for (DateTime day = from.Date; day <= to.Date; day = day.AddDays(1))
            {
                string f = Path.Combine(logDir, "warroom-" + day.ToString("yyyyMMdd", Inv) + ".log");
                if (!File.Exists(f)) continue;
                DateTime at = day;
                foreach (string line in ReadShared(f).Replace("\r", "").Split('\n'))
                {
                    if (line.Length == 0) continue;
                    Match m = LogTime.Match(line);
                    if (m.Success) at = day.AddHours(int.Parse(m.Groups[1].Value, Inv)).AddMinutes(int.Parse(m.Groups[2].Value, Inv)).AddSeconds(int.Parse(m.Groups[3].Value, Inv));
                    if (at >= from && at <= to) list.Add((at, line));
                }
            }
            return list;
        }

        // ---------- each agent's conversations, instructions and memory ----------

        /// <summary>Claude Code's folders for a working folder: (its projects)\(the folder's path, every other character than a letter or digit made "-").</summary>
        private static List<string> ProjectFolders(string cwd)
        {
            string config = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            string projects = Path.Combine(!string.IsNullOrWhiteSpace(config) ? config.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "projects");
            if (!Directory.Exists(projects)) return new List<string>();
            string name = new string(cwd.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
            return Directory.GetDirectories(projects).Where(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void Conversations(Pass p, RunData r, Agent a)
        {
            if (a.Workspace == null || r.Windows.Count == 0) return;
            DateTime start = r.Windows.Min(w => w.From), end = r.Windows.Max(w => w.To);
            foreach (string proj in ProjectFolders(Path.Combine(a.Workspace, "warroom")))
                foreach (string f in Directory.GetFiles(proj, "*.jsonl"))
                {
                    if (File.GetLastWriteTime(f) < start) continue;
                    if (!Transcript.Span(f, out DateTime first, out DateTime last) || !r.Windows.Any(w => last >= w.From && first <= w.To) || first > end) continue;
                    string session = Path.GetFileNameWithoutExtension(f);
                    string dir = Path.Combine(p.Root, "agents", a.Name, "conversations", first.ToString("yyyyMMdd-HHmm", Inv) + " " + session);
                    bool changed = Copy(p, f, Path.Combine(dir, "transcript.jsonl"));
                    if (!changed && File.Exists(Path.Combine(dir, "conversation.txt"))) continue;
                    List<Transcript.Exchange> ex = Transcript.Read(f);
                    string head = a.Name + "'s conversation " + session + ", " + ex.Count + " exchanges, " + (ex.FirstOrDefault(e => e.Date.Length > 0)?.Date ?? "?") + " to "
                                  + (ex.LastOrDefault(e => e.Date.Length > 0)?.Date ?? "?") + " in the game (" + first.ToString("MMM d HH:mm", Inv) + " to " + last.ToString("MMM d HH:mm", Inv) + " real time).\n"
                                  + "Claude Code's own record of it is transcript.jsonl beside this file.\n\n";
                    Put(p, Path.Combine(dir, "conversation.txt"), Encoding.UTF8.GetBytes(head + string.Join("\n", ex.Select(e => e.Text))), null, keep: false);
                    Put(p, Path.Combine(dir, "exchanges.txt"), Encoding.UTF8.GetBytes(head + string.Join("\n", ex.Select(e => "#" + e.N + " · " + (e.Date.Length > 0 ? e.Date : "no date") + " · " + e.Kind + " · "
                                                                                                                           + e.Text.Length.ToString("N0", Inv) + " characters")) + "\n"), null, keep: false);
                }
        }

        private static void Instructions(Pass p, RunData r, Agent a, bool open)
        {
            string dst = Path.Combine(p.Root, "agents", a.Name, "instructions");
            // the system prompts the run's calls used: by the fingerprints the calls and checkpoints name, else those written during the run
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (string l in Bundle.SelectWall(Path.Combine(a.LogDir, "calls.jsonl"), r.Windows, "at", r.Id))
                if (Bundle.Parse(l) is JsonObject o && Str(o, "system_hash") is string h) hashes.Add(h);
            if (a.Workspace != null)
                foreach (string mem in Directory.Exists(Path.Combine(a.Workspace, "memory")) ? Directory.GetDirectories(Path.Combine(a.Workspace, "memory")) : Array.Empty<string>())
                    foreach (string l in Bundle.SelectWall(Path.Combine(mem, "checkpoints.jsonl"), r.Windows, "cp_written"))
                        if (Bundle.Parse(l) is JsonObject o && Str(o, "SystemHash") is string h) hashes.Add(h);
            if (Directory.Exists(a.LogDir))
                foreach (string f in Directory.GetFiles(a.LogDir, "system-*.md").Concat(Directory.GetFiles(a.LogDir, "chronicler-*.md")))
                {
                    string name = Path.GetFileNameWithoutExtension(f), h = name.Substring(name.IndexOf('-') + 1);
                    DateTime w = File.GetLastWriteTime(f);
                    if (hashes.Contains(h) || r.InRun(w)) Copy(p, f, Path.Combine(dst, "prompts", Path.GetFileName(f)));
                }
            if (!open || a.Workspace == null) return;
            // the folder's settings and its order library, as they stand while the run is open
            foreach (string f in new[] { "agent.txt", "warroom.txt", "warroom-system.md", "warroom-continuous.md", "chronicler-system.md" })
                Copy(p, Path.Combine(a.Workspace, f), Path.Combine(dst, f));
            string channel = Path.Combine(a.Workspace, "warroom");
            Copy(p, Path.Combine(channel, "tools.txt"), Path.Combine(dst, "tools.txt"));
            if (Directory.Exists(Path.Combine(channel, "library"))) CopyDir(p, Path.Combine(channel, "library"), Path.Combine(dst, "library"));
        }

        private static void Memory(Pass p, RunData r, Agent a, string campaign, bool open)
        {
            if (a.Workspace == null) return;
            string src = Path.Combine(a.Workspace, "memory", Bundle.Safe(campaign)), dst = Path.Combine(p.Root, "agents", a.Name, "memory");
            if (!Directory.Exists(src)) return;
            if (open)
            {
                // the whole of it while the run is open: notes, story, snapshots, the conversation's state, checkpoints, handovers, earlier conversations
                CopyDir(p, src, dst, f => !Path.GetFileName(f).Contains(".bak-", StringComparison.Ordinal));
                return;
            }
            if (Directory.Exists(dst)) return;   // kept while it was open: later passes leave it as it stood
            // an earlier run archived only now: what was written in its time (the campaign's memory has moved on since)
            Write(p, Path.Combine(dst, "snapshots.jsonl"), Bundle.SelectWall(Path.Combine(src, "snapshots.jsonl"), r.Windows, "at", r.Id));
            Write(p, Path.Combine(dst, "checkpoints.jsonl"), Bundle.SelectWall(Path.Combine(src, "checkpoints.jsonl"), r.Windows, "cp_written"));
            foreach (string f in Directory.GetFiles(src, "handover-*.md"))
                if (r.InRun(File.GetLastWriteTime(f))) Copy(p, f, Path.Combine(dst, Path.GetFileName(f)));
            if (Directory.Exists(Path.Combine(src, "past")))
                foreach (string d in Directory.GetDirectories(Path.Combine(src, "past")))
                    if (r.InRun(Directory.GetLastWriteTime(d))) CopyDir(p, d, Path.Combine(dst, "past", Path.GetFileName(d)));
            Put(p, Path.Combine(dst, "ABOUT.txt"), Encoding.UTF8.GetBytes("This run was archived after it closed, when its campaign's memory had moved on: only what was written during the run is here.\n"), null, keep: false);
        }

        // ---------- the game's logs and the campaign's records ----------

        /// <summary>The game's own log folders (logs\sessions\(launch)) of the launches in the run, with the session each launch began in (or -1).</summary>
        private static List<(string Name, int Session)> GameLogs(Pass p, RunData r, List<Seg> segs)
        {
            var list = new List<(string, int)>();
            string root = Path.Combine(Root, "logs", "sessions");
            if (!Directory.Exists(root) || segs.Count == 0) return list;
            DateTime start = segs[0].From, end = segs[segs.Count - 1].To;
            List<(string Dir, DateTime At)> launches = Directory.GetDirectories(root)
                .Select(d => (d, DateTime.TryParseExact(Path.GetFileName(d), "yyyyMMdd-HHmmss", Inv, DateTimeStyles.AssumeLocal, out DateTime t) ? t : DateTime.MinValue))
                .Where(x => x.Item2 != DateTime.MinValue).OrderBy(x => x.Item2).ToList();
            for (int i = 0; i < launches.Count; i++)
            {
                DateTime at = launches[i].At, until = i + 1 < launches.Count ? launches[i + 1].At : DateTime.MaxValue;
                if (at > end || until < start) continue;
                if (!segs.Any(s => s.From >= at && s.From < until)) continue;   // no load of the run in this launch
                CopyDir(p, launches[i].Dir, Path.Combine(p.Root, "game-logs", Path.GetFileName(launches[i].Dir)));
                list.Add((Path.GetFileName(launches[i].Dir), segs.FindIndex(s => s.From >= at && s.From < until)));
            }
            return list;
        }

        /// <summary>
        /// The campaign's records in the run. Its files hold every run of the campaign (a realm's pokes over a gigabyte), so each is read in pieces, from where the run's
        /// first load began writing it (loads.jsonl's offsets) to where the first load after the run began (a closed run), and on later passes only from where the
        /// last pass stopped. A line naming a run is kept when it names this one; one naming none, when its hour or time falls in the run.
        /// </summary>
        private static void Campaign(Pass p, RunData r, string data, List<Seg> segs)
        {
            ReadCursors(p);
            List<JsonObject> loads = Lines(Path.Combine(data, "loads.jsonl"));
            int first = segs.Count > 0 ? loads.FindIndex(x => Str(x, "load_id") == segs[0].Load) : -1;
            int lastAt = segs.Count > 0 ? loads.FindLastIndex(x => Str(x, "load_id") == segs[segs.Count - 1].Load) : -1;
            bool open = r.Status == "running" || r.Status == "armed";
            JsonObject after = !open && lastAt >= 0 && lastAt + 1 < loads.Count ? loads[lastAt + 1] : null;
            double h0 = segs.Count > 0 ? segs.Min(x => x.H0) : double.MinValue, h1 = segs.Count > 0 ? segs.Max(x => x.H1) + 1 : double.MaxValue;
            string tag = "\"run\":\"" + r.Id + "\"";
            bool Keep(string line)
            {
                if (line.Contains(tag, StringComparison.Ordinal)) return true;
                if (line.Contains("\"run\":\"", StringComparison.Ordinal)) return false;   // another run's
                double h = Field(line, HoursRx);
                if (!double.IsNaN(h)) return h >= h0 - 1e-6 && h <= h1;
                DateTime t = Wall(FieldText(line, WallRx));
                return t == DateTime.MinValue || r.InRun(t);
            }
            void Take(string src, string dst, string offsetKey)
            {
                long start = first >= 0 && offsetKey != null && loads[first]["offsets"]?[offsetKey] != null ? Lng(loads[first]["offsets"], offsetKey) : 0;
                long end = after != null && offsetKey != null && after["offsets"]?[offsetKey] != null ? Lng(after["offsets"], offsetKey) : long.MaxValue;
                Stream(p, src, dst, start, end, Keep);
            }
            List<string> realms = r.Realms.Concat(Agents(r).Select(a => a.Realm)).Where(x => x != null).Distinct().ToList();
            string dir = Path.Combine(p.Root, "campaign");
            foreach (string realm in realms)
            {
                Take(Path.Combine(data, "pokes_" + realm + ".jsonl"), Path.Combine(dir, "pokes_" + realm + ".jsonl"), "pokes_" + realm + ".jsonl");
                foreach (string f in new[] { "chronicle_" + realm + ".jsonl", "wake_" + realm + ".jsonl" }) Take(Path.Combine(data, f), Path.Combine(dir, f), null);
            }
            foreach (string f in new[] { "orders.jsonl", "notices.jsonl" }) Take(Path.Combine(data, f), Path.Combine(dir, f), f);
            foreach (string f in new[] { "episodes.jsonl", "toolbook.jsonl", "diplomacy.jsonl", "allies.jsonl", "in_person.jsonl", "takings.jsonl", "recruitment.jsonl", "handbacks.jsonl" })
                Take(Path.Combine(data, f), Path.Combine(dir, f), null);
            foreach (string f in new[] { "loads.jsonl", "saves.jsonl" }) Copy(p, Path.Combine(data, f), Path.Combine(dir, f));
            // the orders in force carried at each of the run's saves (CarryOn)
            foreach (string save in (r.State?["saves"] as JsonArray ?? new JsonArray()).Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x)))
                Copy(p, Path.Combine(data, "orders", Bundle.Safe(save) + ".json"), Path.Combine(dir, "orders-at-saves", Bundle.Safe(save) + ".json"));
            string you = Path.Combine(p.Root, "you");
            foreach (string f in new[] { "decisions.jsonl", "clicks.jsonl", "events.jsonl", "hours.jsonl" }) Take(Path.Combine(data, "record", f), Path.Combine(you, f), null);
            Take(Path.Combine(data, "lieutenant", "chat.jsonl"), Path.Combine(you, "lieutenant-chat.jsonl"), null);
            Take(Path.Combine(data, "lieutenant", "party_ai.jsonl"), Path.Combine(you, "party-ai.jsonl"), null);
            WriteCursors(p);
        }

        private static readonly Regex HoursRx = new Regex("\"hours\":(-?[0-9.]+)", RegexOptions.Compiled);
        private static readonly Regex WallRx = new Regex("\"(?:wall|at|w)\":\"([^\"]{10,30})\"", RegexOptions.Compiled);

        private static double Field(string line, Regex rx)
        {
            Match m = rx.Match(line.Length > 4000 ? line.Substring(0, 4000) : line);
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, Inv, out double d) ? d : double.NaN;
        }

        private static string FieldText(string line, Regex rx)
        {
            Match m = rx.Match(line.Length > 4000 ? line.Substring(0, 4000) : line);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// The lines of a big file between two byte offsets that pass the test, appended to the archive's copy, from where the last pass stopped; a partial last line waits
        /// for the next pass. A source that shrank (cut back at a load) is read again from the start, its old copy kept under earlier\.
        /// </summary>
        private static void Stream(Pass p, string src, string dst, long start, long end, Func<string, bool> keep)
        {
            if (!File.Exists(src)) return;
            string key = Path.GetRelativePath(p.Root, dst);
            long len = new FileInfo(src).Length;
            bool known = p.Cursors.TryGetValue(key, out (string Src, long Pos) c) && string.Equals(c.Src, src, StringComparison.OrdinalIgnoreCase);
            bool again = !known || c.Pos > len || !File.Exists(dst) || (c.Pos > 0 && !LineEndAt(src, c.Pos - 1));
            if (again && File.Exists(dst)) Aside(p, dst);
            long from = again ? Math.Min(start, len) : Math.Max(c.Pos, start), to = Math.Min(end, len);
            if (from >= to) { p.Cursors[key] = (src, Math.Max(from, again ? from : c.Pos)); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            long consumed = from, written = 0;
            using (var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
            using (var output = new FileStream(dst, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                fs.Seek(from, SeekOrigin.Begin);
                var line = new MemoryStream();
                byte[] buf = new byte[1 << 20];
                long pos = from;
                while (pos < to)
                {
                    int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, to - pos));
                    if (n <= 0) break;
                    int lineFrom = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] != (byte)'\n') continue;
                        line.Write(buf, lineFrom, i - lineFrom);
                        lineFrom = i + 1;
                        consumed = pos + i + 1;
                        if (line.Length > 0)
                        {
                            byte[] bytes = line.ToArray();
                            if (keep(Encoding.UTF8.GetString(bytes).TrimEnd('\r'))) { output.Write(bytes, 0, bytes.Length); output.WriteByte((byte)'\n'); written += bytes.Length + 1; }
                        }
                        line.SetLength(0);
                    }
                    line.Write(buf, lineFrom, n - lineFrom);
                    pos += n;
                }
            }
            p.Cursors[key] = (src, consumed);
            if (written > 0) { p.Copied++; p.Bytes += written; }
        }

        /// <summary>Is the byte there a line's end (a file rewritten since the last pass would not have one where it stopped)?</summary>
        private static bool LineEndAt(string path, long at)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                fs.Seek(at, SeekOrigin.Begin);
                return fs.ReadByte() == (byte)'\n';
            }
            catch (IOException) { return false; }
        }

        private static void ReadCursors(Pass p)
        {
            JsonObject o = Obj(Path.Combine(p.Root, ".cursors.json"));
            if (o == null) return;
            foreach (KeyValuePair<string, JsonNode> kv in o)
                if (kv.Value is JsonObject c && Str(c, "src") is string src) p.Cursors[kv.Key] = (src, Lng(c, "pos"));
        }

        private static void WriteCursors(Pass p)
        {
            var o = new JsonObject();
            foreach (KeyValuePair<string, (string Src, long Pos)> kv in p.Cursors) o[kv.Key] = new JsonObject { ["src"] = kv.Value.Src, ["pos"] = kv.Value.Pos };
            string path = Path.Combine(p.Root, ".cursors.json");
            File.WriteAllText(path + ".part", o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(path + ".part", path, true);
        }

        // ---------- the index ----------

        private static void Readme(Pass p, RunData r, List<Seg> segs, List<Agent> agents, Dictionary<string, Dictionary<int, Tally>> tallies, List<(string Name, int Session)> launches, string campaign, bool open)
        {
            var sb = new StringBuilder();
            string status = r.Status ?? "?";
            sb.AppendLine("# " + r.Id + " · " + Label(r));
            sb.AppendLine();
            sb.AppendLine("Arm " + r.Arm + " (" + Text.ArmWords(r.Arm) + "), campaign " + campaign + ", begun from the save \"" + (Str(r.State, "save") ?? "?") + "\" on " + (Str(r.State, "started") ?? Str(r.State, "armed") ?? "?")
                          + ". Status: " + status + ", day " + Int(r.State, "last_day") + " of " + r.DaysPlanned + ". Saves made in it: " + string.Join(", ", (r.State?["saves"] as JsonArray ?? new JsonArray()).Select(x => "\"" + x + "\"")) + ".");
            sb.AppendLine();
            sb.AppendLine("Brought up to date " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv) + (open ? " (the open run: kept up to date every " + EveryMinutes + " minutes while the game runs, and as it closes)." : " (the run is closed)."));
            sb.AppendLine();
            sb.AppendLine("## Sessions (each load of the game in the run)");
            sb.AppendLine();
            foreach (Seg s in segs)
            {
                sb.AppendLine("### " + s.N + ". " + s.From.ToString("MMM d HH:mm", Inv) + " to " + (s.To > DateTime.Now ? "now" : s.To.ToString("MMM d HH:mm", Inv)) + " · `sessions\\" + s.Folder + "`");
                sb.AppendLine("- In the game: " + Transcript.DateOf(s.H0) + " to " + Transcript.DateOf(s.H1) + "; " + s.How + " (game session " + s.Load + ").");
                foreach (Agent a in agents)
                {
                    if (!tallies.TryGetValue(a.Name, out Dictionary<int, Tally> byS) || !byS.TryGetValue(s.N - 1, out Tally t)) { sb.AppendLine("- " + a.Name + ": no calls."); continue; }
                    var parts = new List<string>();
                    if (t.Wakes > 0) parts.Add(Pl(t.Wakes, "wake", "wakes"));
                    if (t.Reviews > 0) parts.Add(Pl(t.Reviews, "strategy review or council", "strategy reviews or councils"));
                    if (t.Chats > 0) parts.Add(Pl(t.Chats, "message answered", "messages answered"));
                    if (t.Handovers > 0) parts.Add(Pl(t.Handovers, "handover", "handovers"));
                    if (t.Questions > 0) parts.Add(Pl(t.Questions, "question to its tools", "questions to its tools"));
                    if (t.Failed > 0) parts.Add(Pl(t.Failed, "call failed", "calls failed"));
                    sb.AppendLine("- " + a.Name + ": " + Pl(t.Calls, "call", "calls") + " (" + string.Join(", ", parts) + "), $" + t.Cost.ToString("0.00", Inv) + " at list prices.");
                }
                foreach ((string name, int session) in launches.Where(l => l.Session == s.N - 1)) sb.AppendLine("- The game's own logs of this launch: `game-logs\\" + name + "`.");
                sb.AppendLine();
            }
            sb.AppendLine("Each session's folder holds, for each agent: `calls.jsonl` (every call: what it was, tokens, cost, time), `calls\\` (each call's prompt, Claude Code's event stream, thinking and answer), "
                          + "`questions.jsonl` (every question to its tools, with the answer) and `warroom.log` (its war room's log).");
            sb.AppendLine();
            sb.AppendLine("## Agents");
            sb.AppendLine();
            foreach (Agent a in agents)
            {
                string ad = Path.Combine(p.Root, "agents", a.Name);
                sb.AppendLine("### " + a.Name + " (" + (a.Kind == "lieutenant" ? "your lieutenant" : "realm agent") + ", " + (a.Mode ?? "?") + ")");
                double cost = tallies.TryGetValue(a.Name, out Dictionary<int, Tally> byS) ? byS.Values.Sum(t => t.Cost) : 0;
                int calls = byS?.Values.Sum(t => t.Calls) ?? 0;
                sb.AppendLine("- " + calls + " calls in the run, $" + cost.ToString("0.00", Inv) + " at list prices.");
                string convs = Path.Combine(ad, "conversations");
                if (Directory.Exists(convs))
                    foreach (string d in Directory.GetDirectories(convs).OrderBy(x => x, StringComparer.Ordinal))
                    {
                        string first = File.Exists(Path.Combine(d, "exchanges.txt")) ? File.ReadLines(Path.Combine(d, "exchanges.txt")).FirstOrDefault() ?? "" : "";
                        sb.AppendLine("- Conversation `agents\\" + a.Name + "\\conversations\\" + Path.GetFileName(d) + "`: " + (first.IndexOf(", ", StringComparison.Ordinal) is int cut && cut > 0 ? first.Substring(cut + 2) : first));
                    }
                else sb.AppendLine("- No conversation transcripts (fresh calls keep none; each call's folder holds its whole exchange).");
                if (Directory.Exists(Path.Combine(ad, "memory"))) sb.AppendLine("- Memory: `agents\\" + a.Name + "\\memory` (notes, story, snapshots, the conversation's state and checkpoints, handovers, earlier conversations kept word for word).");
                if (Directory.Exists(Path.Combine(ad, "instructions"))) sb.AppendLine("- Instructions: `agents\\" + a.Name + "\\instructions` (the system prompts its calls used, its folder's settings, its order library).");
                sb.AppendLine();
            }
            sb.AppendLine("## The game's records");
            sb.AppendLine();
            sb.AppendLine("- `run\\`: the run's own record (run.json, setup.json, days.jsonl, events.jsonl, its report).");
            sb.AppendLine("- `campaign\\`: the campaign's records in the run: the pokes with their views and reports, the chronicles, wakes, episodes, toolbook, diplomacy, orders, notices, your battles in person, takings, recruitment, the loads and saves, and the orders carried at each of the run's saves.");
            sb.AppendLine("- `you\\`: your own record in the run (decisions, clicks, events, hours), your talk with the lieutenant, Party AI Controls.");
            sb.AppendLine("- `game-logs\\`: the game's own logs of each launch in the run (trace, health, lords, story, summary).");
            sb.AppendLine("- `earlier\\`: older copies of files that changed other than by growing (a conversation taken back, a rewritten state).");
            List<string> missing = p.Missing.Distinct().ToList();
            if (missing.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Missing");
                sb.AppendLine();
                foreach (string m in missing.Take(50)) sb.AppendLine("- " + m);
                if (missing.Count > 50) sb.AppendLine("- ... and " + (missing.Count - 50) + " more.");
            }
            Put(p, Path.Combine(p.Root, "README.md"), Encoding.UTF8.GetBytes(sb.ToString()), null, keep: false);
        }

        private static string Pl(int n, string one, string many) => n + " " + (n == 1 ? one : many);

        // ---------- copying ----------

        /// <summary>
        /// A file copied when it differs from its copy (size or time), in pieces: a copy the source only grew past gets the new bytes added; any other change keeps the old
        /// copy under earlier\ and copies it whole. True when copied. A file another program holds shut is left for the next pass.
        /// </summary>
        private static bool Copy(Pass p, string src, string dst)
        {
            if (!File.Exists(src)) return false;
            var si = new FileInfo(src);
            var di = new FileInfo(dst);
            if (di.Exists && di.Length == si.Length && di.LastWriteTimeUtc == si.LastWriteTimeUtc) return false;
            try
            {
                using var fs = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
                long add = 0;
                if (di.Exists && fs.Length >= di.Length && SameStart(fs, dst, di.Length))
                {
                    fs.Seek(di.Length, SeekOrigin.Begin);
                    using var output = new FileStream(dst, FileMode.Append, FileAccess.Write, FileShare.Read);
                    fs.CopyTo(output, 1 << 20);
                    add = output.Length - di.Length;
                }
                else
                {
                    if (di.Exists) Aside(p, dst);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    fs.Seek(0, SeekOrigin.Begin);
                    using (var output = new FileStream(dst + ".part", FileMode.Create, FileAccess.Write)) { fs.CopyTo(output, 1 << 20); add = output.Length; }
                    File.Move(dst + ".part", dst, true);
                }
                File.SetLastWriteTimeUtc(dst, si.LastWriteTimeUtc);
                p.Copied++;
                p.Bytes += add;
                return true;
            }
            catch (IOException) { return false; }
        }

        /// <summary>Do the source's first bytes match the copy's (all of it)?</summary>
        private static bool SameStart(FileStream src, string dst, long length)
        {
            using var d = new FileStream(dst, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            src.Seek(0, SeekOrigin.Begin);
            byte[] a = new byte[1 << 20], b = new byte[1 << 20];
            long left = length;
            while (left > 0)
            {
                int want = (int)Math.Min(a.Length, left);
                int na = ReadFull(src, a, want), nb = ReadFull(d, b, want);
                if (na != want || nb != want || !a.AsSpan(0, want).SequenceEqual(b.AsSpan(0, want))) return false;
                left -= want;
            }
            return true;
        }

        private static int ReadFull(Stream s, byte[] buf, int want)
        {
            int got = 0;
            while (got < want) { int n = s.Read(buf, got, want - got); if (n <= 0) break; got += n; }
            return got;
        }

        /// <summary>An archived copy about to be replaced, kept under earlier\(this pass)\.</summary>
        private static void Aside(Pass p, string dst)
        {
            string back = Path.Combine(p.Root, "earlier", p.Stamp, Path.GetRelativePath(p.Root, dst));
            Directory.CreateDirectory(Path.GetDirectoryName(back));
            File.Move(dst, back, true);
            p.Kept++;
        }

        private static void CopyDir(Pass p, string src, string dst, Func<string, bool> take = null)
        {
            if (!Directory.Exists(src)) return;
            foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                if (!f.EndsWith(".part", StringComparison.Ordinal) && (take == null || take(f))) Copy(p, f, Path.Combine(dst, Path.GetRelativePath(src, f)));
        }

        /// <summary>Lines written as a file (none: nothing written).</summary>
        private static void Write(Pass p, string dst, List<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            Put(p, dst, Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"), null);
        }

        /// <summary>
        /// Bytes put in place when they differ from what is there; the copy they replace is kept under earlier\(time)\ unless they only add to it (or keep is false: the
        /// index and the readable copies, made again each time). True when written.
        /// </summary>
        private static bool Put(Pass p, string dst, byte[] bytes, DateTime? stamp, bool keep = true)
        {
            if (File.Exists(dst))
            {
                byte[] old = File.ReadAllBytes(dst);
                if (old.AsSpan().SequenceEqual(bytes)) { if (stamp != null) File.SetLastWriteTimeUtc(dst, stamp.Value); return false; }
                if (keep && !(bytes.Length >= old.Length && bytes.AsSpan(0, old.Length).SequenceEqual(old))) Aside(p, dst);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            string tmp = dst + ".part";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, dst, true);
            if (stamp != null) File.SetLastWriteTimeUtc(dst, stamp.Value);
            p.Copied++;
            p.Bytes += bytes.Length;
            return true;
        }
    }

    /// <summary>Below-normal priority and efficiency mode, so Windows keeps the archive's copying on the efficiency cores and the game keeps the disk.</summary>
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerThrottling
        {
            public uint Version, ControlMask, StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int size);

        public static void Self()
        {
            try
            {
                Process me = Process.GetCurrentProcess();
                me.PriorityClass = ProcessPriorityClass.BelowNormal;
                var s = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 1 };
                SetProcessInformation(me.Handle, 4, ref s, Marshal.SizeOf<PowerThrottling>());
            }
            catch (Exception) { /* runs at normal priority then */ }
        }
    }
}
