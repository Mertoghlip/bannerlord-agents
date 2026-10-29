using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13, the training record (docs/PLAN-0.13.md Part C): the run's tags on pokes, answers, receipts and tool runs; the episode
        /// index; each order's outcome in the toolbook; the realm's day lines; your moves with your realm's report kept beside them and
        /// the hourly copies of your runs; the recorder's two rules; and the run's bundle with its manifest and its check.
        /// </summary>
        private static void RecordTests(string root, string agentDir)
        {
            string dir = Path.Combine(root, "record-agent");
            Settings.AgentDir = dir;
            Brain.Reset("smoketest-record", double.NaN, "orcs", null);
            string data = Brain.Dir;
            RunTags.Set("smoke-run-C1", "C1");

            // a poke of the run: tagged in the pokes file, in poke.json and in the episode index
            var bolg = new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, SizeLimit = 320, Strength = 400, Speed = 5, FoodDays = 10, X = 104, Y = 100, Doing = "Hold", Spottable = true, Gold = 5000, Loot = 1200 };
            Snapshot s = ToolWorld(30000, bolg);
            s.Run = RunTags.Id;
            s.Arm = RunTags.Arm;
            Brain.OnHour(s);
            Files.FlushAll();
            string pokes = ReadIf(Path.Combine(data, "pokes_orcs.jsonl"));
            string episodes = ReadIf(Path.Combine(data, "episodes.jsonl"));
            Check(pokes.Contains("\"run\":\"smoke-run-C1\",\"arm\":\"C1\"") && ReadIf(Path.Combine(dir, "poke.json")).Contains("\"run\":\"smoke-run-C1\""),
                  "a poke carries its run and arm, in the pokes file and in poke.json (the war room logs them with its call)");
            JObject pokeLine = Lines(episodes).FirstOrDefault(o => (string)o["type"] == "poke");
            Check(pokeLine != null && (string)pokeLine["run"] == "smoke-run-C1" && ((string)pokeLine["report_hash"])?.Length == 12 && (string)pokeLine["pokes"] == "pokes_orcs.jsonl",
                  "the episode index has the poke: its number, reasons, run, and its report's fingerprint and file");

            // an answer with its call's folder and its tools, diplomatic acts and letters
            int number = (int)pokeLine["poke"];
            string answer = Path.Combine(dir, "orders", "poke-" + number + ".json");
            File.WriteAllText(answer, "{\"poke\": " + number + ", \"kind\": \"council\", \"call\": \"C:\\\\calls\\\\100000-council-" + number + "\", \"orders\": [{\"lord\": \"lord_bolg\", \"do\": \"circuit\", \"places\": [\"village_orc\", \"castle_two\"], \"at\": \"castle_orc\"}],"
                                 + " \"tools\": [{\"name\": \"circuit\", \"keep\": true}], \"diplomacy\": [{\"act\": \"vote\"}], \"letters\": [{\"to\": \"dg\", \"text\": \"hold the pass\"}]}");
            System.Threading.Thread.Sleep(400);
            AgentChannel.Scan();
            Files.FlushAll();
            while (Background.ToMain.TryDequeue(out _)) { }
            JObject answerLine = Lines(ReadIf(Path.Combine(data, "episodes.jsonl"))).FirstOrDefault(o => (string)o["type"] == "answer");
            Check(answerLine != null && (string)answerLine["kind"] == "council" && ((string)answerLine["call"]).EndsWith("council-" + number) && (int)answerLine["orders"] == 1
                  && (int)answerLine["tools"] == 1 && (int)answerLine["diplomacy"] == 1 && (int)answerLine["letters"] == 1,
                  "the episode index has the answer: the kind of call, the call's folder, its orders, tools, diplomatic acts and letters");
            AgentChannel.Parse(answer.Replace("poke-" + number, "done\\poke-" + number), out _, out List<AgentChannel.AgentOrder> orders, out AgentChannel.Answer parsed, out _);
            Check(orders.Count == 1 && orders[0].Places.SequenceEqual(new[] { "village_orc", "castle_two" }) && parsed.ToolList?.Count == 1 && parsed.LetterList?.Count == 1,
                  "an order's places are read in their order, and the answer's tools and letters are kept for the bridge");
            orders[0].Realm = "orcs";
            orders[0].ToolRun = 9100;
            AgentChannel.Receipt(orders[0], "applied", "Bolg goes round the circuit", 30000.5);
            Files.FlushAll();
            JObject receipt = Lines(ReadIf(Path.Combine(data, "episodes.jsonl"))).LastOrDefault(o => (string)o["type"] == "receipt");
            Check(receipt != null && (int)receipt["tool_run"] == 9100 && (string)receipt["status"] == "applied" && (string)receipt["run"] == "smoke-run-C1"
                  && ReadIf(Path.Combine(data, "orders.jsonl")).Contains("\"run\":\"smoke-run-C1\",\"arm\":\"C1\",\"tool_run\":9100"),
                  "a receipt names the tool run its order started, in the episode index and the orders' record, with the run");

            // a tool run's outcome: its battles, the lords it took, the holdings at its places, and how it ended
            Diag.Hours = 30001;
            Brain.OnHour(Tag(ToolWorld(30001, bolg)));
            Runner.Start(9100, new ToolOrder { Tool = "goto", Lord = "lord_bolg", LordName = "Bolg", Party = "p_bolg", At = "castle_two", Realm = "orcs", Poke = number });
            var won = new BattleEnd { Hours = 30001.5, Kind = "field", Winner = "attacker" };
            won.Parties.Add(new BattleParty { Party = "p_bolg", Faction = "orcs", Side = "attacker", Men = 300, Lost = 12 });
            won.Parties.Add(new BattleParty { Party = "p_foe", Faction = "player_k", Side = "defender", Men = 80, Lost = 70 });
            Outcomes.OnBattle(won);
            Outcomes.OnCapture("lord_foe", "player_k", "p_bolg");
            Outcomes.OnHolding("castle_two", "player_k", "orcs");
            bolg.Men = 290;
            Brain.OnHour(Tag(ToolWorld(30002, bolg)));
            Diag.Hours = 30003;
            Runner.Ended(9100, "the lord was taken prisoner");
            Files.FlushAll();
            List<JObject> book = Lines(ReadIf(Path.Combine(data, "toolbook.jsonl")));
            JObject outcome = Lines(ReadIf(Path.Combine(data, "episodes.jsonl"))).LastOrDefault(o => (string)o["type"] == "outcome");
            Check(book.Any(o => (string)o["type"] == "run_start" && (int)o["tool_run"] == 9100 && (string)o["version"] == "ours" && (string)o["author"] == "ours")
                  && book.Any(o => (string)o["type"] == "run_end" && (int)o["tool_run"] == 9100),
                  "the toolbook has the tool run's start (who wrote the tool, its version, the lord, the settings) and its end");
            Check(outcome != null && (string)outcome["how"] == "captured" && (int)outcome["battles"]["fought"] == 1 && (int)outcome["battles"]["won"] == 1 && (int)outcome["battles"]["men_lost"] == 12
                  && (int)outcome["battles"]["enemy_fallen"] == 70 && (int)outcome["lords_taken"] == 1 && (int)outcome["holdings_gained"] == 1 && (int)outcome["start"]["men"] == 300
                  && (int)outcome["poke"] == number && (string)outcome["run"] == "smoke-run-C1",
                  "the order's outcome joins its poke: how it ended, its battles fought and won, men lost on each side, lords taken, holdings gained, the lord's numbers at its start");

            // the realm's day line: once at each day's start
            int days = Lines(ReadIf(Path.Combine(data, "episodes.jsonl"))).Count(o => (string)o["type"] == "day");
            Episodes.MaybeDay(Tag(ToolWorld(30024, bolg)));
            Episodes.MaybeDay(Tag(ToolWorld(30025, bolg)));
            Files.FlushAll();
            List<JObject> dayLines = Lines(ReadIf(Path.Combine(data, "episodes.jsonl"))).Where(o => (string)o["type"] == "day").ToList();
            Check(days == 1 && dayLines.Count == 2 && (int)dayLines[1]["day"] == 1251 && (int)dayLines[1]["lords"] == 1 && (int)dayLines[1]["castles"] == 2,
                  "the realm's numbers go into the index once at each in-game day's start (" + dayLines.Count + " day lines)");

            // your own moves: your realm's report of that hour kept beside each, packed; and every hour of a run of yours
            Snapshot mine = Tag(ToolWorld(30030, bolg));
            mine.Parties.First(p => p.Id == "p_bolg").SeenByPlayer = true;
            Brain.OnHour(mine);
            Brain.WriteDecision(new DecisionRecord { Kind = "recruit", Text = "You recruited 12 men at Fornost.", Hours = 30030.2, Faction = "player_k", Run = RunTags.Id, Arm = RunTags.Arm, Detail = new { men = 12 } });
            Files.FlushAll();
            JObject move = Lines(ReadIf(Path.Combine(data, "record", "decisions.jsonl"))).LastOrDefault();
            string reportFile = move != null && move["report"] != null ? Path.Combine(data, "record", ((string)move["report"]).Replace('/', '\\')) : null;
            Check(move != null && (string)move["report"] == "reports/h30030.txt.gz" && reportFile != null && File.Exists(reportFile) && (string)move["run"] == "smoke-run-C1" && (int)move["detail"]["men"] == 12,
                  "a move of yours names your realm's report of that hour (record\\reports\\h30030.txt.gz), its run, and what it was in detail");
            string kept = reportFile != null && File.Exists(reportFile) ? RecordFiles.ReadGz(reportFile) : "";
            Check(kept.StartsWith("REPORT FOR KINGDOM OF ARNOR AND GONDOR") && kept.Contains("Bolg") && RecordFiles.Hash(kept) == (string)move["report_hash"],
                  "the report kept is the one an agent of your realm would read, with what your own party saw, and its fingerprint matches");
            Snapshot hour = Tag(ToolWorld(30031, bolg));
            hour.RecordHourly = true;
            Brain.OnHour(hour);
            Files.FlushAll();
            JObject hourLine = Lines(ReadIf(Path.Combine(data, "record", "hours.jsonl"))).LastOrDefault();
            Check(hourLine != null && (double)hourLine["hours"] == 30031 && (string)hourLine["report"] == "reports/h30031.txt.gz" && File.Exists(Path.Combine(data, "record", "reports", "h30031.txt.gz")),
                  "in a run of yours (A, B) your realm's report is kept every hour too, for the hours you let things run");

            // the recorder's rules: yours only while you rule your realm yourself; Party AI Controls' own refills are not yours
            Check(RecordRules.Yours(true, true, true, true, true, true) && RecordRules.Yours(true, true, true, false, true, false) && !RecordRules.Yours(true, true, true, false, true, true)
                  && !RecordRules.Yours(true, true, true, false, false, false) && RecordRules.Yours(true, true, false, false, true, false) && !RecordRules.Yours(false, true, true, true, true, false),
                  "the record is yours while you rule your realm, or are its lord with no live agent commanding it; not a wanderer standing in for you");
            Check(RecordRules.PacRefill("RecruitFromTemplate|", false, false) && !RecordRules.PacRefill("RecruitFromTemplate|", false, true) && !RecordRules.PacRefill("DefendSettlement|town_x", true, false),
                  "a recruit order Party AI Controls set by itself, with no word from you, is its own refill, not your order");
            RunTags.Set(null, null);

            BundleTests(root, data);
            BackupTests(root);
        }

        /// <summary>
        /// Runs.exe backup (0.13.1) on a made-up records folder and agent folder: the first copy whole, nothing written when nothing
        /// changed, a grown file's new bytes added, a shrunk one copied whole with its old copy kept, nothing deleted, a file held shut
        /// left for the next backup, a place inside what it copies refused (through a link too, 0.13.2), the backup after a run's
        /// bundle, and the bridge's setting.
        /// </summary>
        private static void BackupTests(string root)
        {
            string exe = FindRuns();
            if (exe == null) return;   // the bundle's check has said so
            string records = Path.Combine(root, "backup-test", "records"), agent = Path.Combine(root, "backup-test", "orc-council"), copy = Path.Combine(root, "backup-test-copy");
            Directory.CreateDirectory(Path.Combine(records, "data", "c1"));
            Directory.CreateDirectory(Path.Combine(records, "live", "frames"));
            Directory.CreateDirectory(Path.Combine(agent, "warroom", "library"));
            File.WriteAllText(Path.Combine(records, "settings.txt"), "# made up\nagents = " + agent + "\nbackup = " + copy + "\n");
            string pokes = Path.Combine(records, "data", "c1", "pokes_orcs.jsonl"), notes = Path.Combine(agent, "notes.md"), frame = Path.Combine(records, "live", "frames", "h1.json.gz");
            File.WriteAllText(pokes, string.Concat(Enumerable.Range(1, 3000).Select(i => "{\"number\":" + i + ",\"text\":\"a poke line long enough to fill some bytes\"}\n")));
            File.WriteAllText(notes, "The plan: hold the Redhorn Gate, then strike at Lorien when Dol Guldur moves.\n");
            File.WriteAllBytes(frame, Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray());
            File.WriteAllText(Path.Combine(agent, "warroom", "library", "index.json"), "{\"tools\":[]}");
            string copyPokes = Path.Combine(copy, "records", "data", "c1", "pokes_orcs.jsonl"), copyNotes = Path.Combine(copy, "agents", "orc-council", "notes.md");

            (int Code, string Out) Runs(string args, string at = null)
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                psi.EnvironmentVariables["BANNERLORD_AGENTS_ROOT"] = at ?? records;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    return (p.ExitCode, o);
                }
            }
            bool SameFile(string a, string b) => File.Exists(a) && File.Exists(b) && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));

            var first = Runs("backup");
            Check(first.Code == 0 && SameFile(pokes, copyPokes) && SameFile(frame, Path.Combine(copy, "records", "live", "frames", "h1.json.gz")) && SameFile(notes, copyNotes)
                  && File.Exists(Path.Combine(copy, "agents", "orc-council", "warroom", "library", "index.json")) && File.Exists(Path.Combine(copy, "README.txt"))
                  && ReadIf(Path.Combine(copy, "backup.log")).Contains("5 new"),
                  "the first backup copies the records folder and each agent's folder whole, into the place settings.txt names (" + first.Out.Split('\n')[0].Trim() + ")");
            var second = Runs("backup");
            Check(second.Code == 0 && second.Out.Contains("0 new, 0 grown, 0 rewritten") && second.Out.Contains("5 unchanged"), "a backup with nothing changed writes nothing");

            // a file grows, one is wiped by mistake, one is deleted, and a new one is held shut by another program
            DateTime born = File.GetCreationTimeUtc(copyPokes);
            File.AppendAllText(pokes, "{\"number\":3001,\"text\":\"one more\"}\n");
            File.WriteAllText(notes, "Wiped by mistake.\n");
            File.Delete(frame);
            string held = Path.Combine(records, "data", "c1", "held.jsonl"), copyHeld = Path.Combine(copy, "records", "data", "c1", "held.jsonl");
            File.WriteAllText(held, "{\"a\":1}\n");
            (int Code, string Out) third;
            using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) third = Runs("backup");
            string[] kept = Directory.Exists(Path.Combine(copy, "earlier")) ? Directory.GetFiles(Path.Combine(copy, "earlier"), "notes.md", SearchOption.AllDirectories) : new string[0];
            Check(SameFile(pokes, copyPokes) && File.GetCreationTimeUtc(copyPokes) == born && third.Out.Contains("1 grown"),
                  "a file that grew gets only its new bytes added: the copy is the same file, whole again");
            Check(SameFile(notes, copyNotes) && kept.Length == 1 && File.ReadAllText(kept[0]).StartsWith("The plan:") && third.Out.Contains("1 rewritten"),
                  "a file that shrank is copied whole, and the copy it replaced is kept under earlier\\");
            Check(File.Exists(Path.Combine(copy, "records", "live", "frames", "h1.json.gz")), "a file gone from its place stays in the copy: a backup never deletes");
            Check(third.Code == 1 && !File.Exists(copyHeld) && third.Out.Contains("held.jsonl"), "a file another program holds shut is named and left for the next backup");
            var fourth = Runs("backup");
            Check(fourth.Code == 0 && SameFile(held, copyHeld), "and the next backup copies it");

            string inside = Path.Combine(records, "copy-here");
            var refused = Runs("backup --to \"" + inside + "\"");
            Check(refused.Code == 2 && !Directory.Exists(inside), "a backup place inside what it copies is refused");

            // the records folder can be a link to where it really is (Mert's has been one to D: since Oct 28): a place in there is inside it too
            string link = Path.Combine(root, "backup-test", "records-link"), deep = Path.Combine(records, "copy-here-too");
            var mklink = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + records + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (Process p = Process.Start(mklink)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(10000); }
            bool linked = Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
            try
            {
                var through = linked ? Runs("backup --to \"" + deep + "\"", link) : (Code: -1, Out: "");
                Check(linked && through.Code == 2 && !Directory.Exists(deep), "and so is a place inside where a linked records folder really is");
            }
            finally { if (linked) try { Directory.Delete(link); } catch (Exception) { /* the next run starts by wiping its folder */ } }

            // a run's end: the bundle is packed, then backed up with everything else
            string run = Path.Combine(records, "runs", "r1");
            Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, "run.json"), "{\"id\":\"r1\",\"arm\":\"C1\",\"status\":\"finished\",\"campaign\":\"c1\"}");
            var chained = Runs("bundle r1 --backup");
            Check(File.Exists(Path.Combine(copy, "records", "runs", "r1", "bundle.zip")) && chained.Out.Contains("Backup into"),
                  "bundle --backup packs the run, then backs it up with everything else");

            // the bridge keeps the place in settings.txt (it writes the file back with only the keys it knows), and asks for the backup
            string file = Path.Combine(Paths.Root, "settings.txt");
            string before = File.Exists(file) ? File.ReadAllText(file) : null;
            Settings.Backup = @"D:\BannerlordAgents-backup";
            Settings.Save();
            Settings.Backup = "";
            Settings.Load();
            bool keptPlace = ReadIf(file).Contains(@"backup = D:\BannerlordAgents-backup") && Settings.Backup == @"D:\BannerlordAgents-backup";
            string withPlace = RunFiles.BundleArguments("r1");
            Settings.Backup = "";
            Check(keptPlace && withPlace == "bundle r1 --backup" && RunFiles.BundleArguments("r1") == "bundle r1",
                  "the bridge reads and keeps the backup's place in settings.txt, and at a run's end asks for the backup after the bundle (only when a place is set)");
            if (before != null) File.WriteAllText(file, before); else File.Delete(file);
        }

        /// <summary>Runs.exe bundle on a made-up run: the files by the run's tag, the call folders, the manifest, and the check's findings.</summary>
        private static void BundleTests(string root, string data)
        {
            string exe = FindRuns();
            if (exe == null) { Check(false, "Runs.exe is built beside the smoke test (build the whole solution first)"); return; }
            string runDir = Path.Combine(Paths.Runs, "smoke-run-C1");
            Directory.CreateDirectory(runDir);
            string logs = Path.Combine(root, "record-warroom-logs");
            string call = Path.Combine(logs, "calls", "20261028", "100000-poke-1");
            Directory.CreateDirectory(call);
            File.WriteAllText(Path.Combine(call, "prompt.txt"), "the prompt");
            File.WriteAllText(Path.Combine(call, "thinking.txt"), "the thinking");
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.WriteAllText(Path.Combine(logs, "calls.jsonl"),
                "{\"kind\":\"answer\",\"at\":\"" + now + "\",\"poke\":1,\"ok\":true,\"written\":true,\"folder\":\"" + call.Replace("\\", "\\\\") + "\",\"run\":\"smoke-run-C1\",\"realm\":\"orcs\",\"system_hash\":\"abc\"}\n"
                + "{\"kind\":\"answer\",\"at\":\"" + now + "\",\"poke\":2,\"ok\":true,\"written\":true,\"folder\":\"" + Path.Combine(logs, "calls", "gone").Replace("\\", "\\\\") + "\",\"run\":\"smoke-run-C1\",\"realm\":\"orcs\"}\n"
                + "{\"kind\":\"answer\",\"at\":\"" + now + "\",\"poke\":3,\"ok\":true,\"folder\":\"x\",\"run\":\"another-run\"}\n");
            File.WriteAllText(Path.Combine(logs, "system-abc.md"), "the instructions");
            string from = DateTime.Now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss"), to = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss");
            File.WriteAllText(Path.Combine(runDir, "run.json"), "{\"id\":\"smoke-run-C1\",\"arm\":\"C1\",\"status\":\"finished\",\"campaign\":\"smoketest-record\",\"kingdom\":\"player_k\",\"rivals\":[\"orcs\"],"
                + "\"segments\":[{\"load\":\"" + Bridge.LoadId + "\",\"from\":29990,\"to\":30100,\"wall_from\":\"" + from + "\",\"wall_to\":\"" + to + "\"}]}");
            File.WriteAllText(Path.Combine(runDir, "setup.json"), "{\"agents\":[{\"realm\":\"orcs\",\"kind\":\"realm\",\"mode\":\"live\",\"log_dir\":\"" + logs.Replace("\\", "\\\\") + "\"}]}");
            File.WriteAllText(Path.Combine(runDir, "days.jsonl"), "{\"type\":\"day\",\"day\":0}\n");
            var psi = new ProcessStartInfo(exe, "bundle smoke-run-C1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            psi.EnvironmentVariables["BANNERLORD_AGENTS_ROOT"] = Paths.Root;
            string output;
            int code;
            using (Process p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(60000);
                code = p.ExitCode;
            }
            string zip = Path.Combine(runDir, "bundle.zip");
            Check(File.Exists(zip), "Runs.exe bundle packs the run into bundle.zip in its folder (" + output.Split('\n').FirstOrDefault()?.Trim() + ")");
            if (!File.Exists(zip)) return;
            using (ZipArchive z = ZipFile.OpenRead(zip))
            {
                List<string> names = z.Entries.Select(e => e.FullName).ToList();
                string Entry(string n) { ZipArchiveEntry e = z.GetEntry(n); if (e == null) return ""; using (var r = new StreamReader(e.Open())) return r.ReadToEnd(); }
                Check(names.Contains("run/run.json") && names.Contains("campaign/pokes_orcs.jsonl") && names.Contains("campaign/episodes.jsonl") && names.Contains("campaign/toolbook.jsonl")
                      && names.Contains("you/decisions.jsonl") && names.Contains("you/reports/h30030.txt.gz") && names.Contains("you/hours.jsonl"),
                      "the bundle holds the run's folder, the campaign's records of the run (pokes, the episode index, the toolbook) and your moves with their reports");
                Check(names.Contains("agents/orcs/calls.jsonl") && names.Any(n => n.EndsWith("100000-poke-1/thinking.txt")) && names.Contains("agents/orcs/prompts/system-abc.md")
                      && Entry("agents/orcs/calls.jsonl").Split('\n').Count(l => l.Trim().Length > 0) == 2,
                      "and each agent's calls of the run (not another run's), each call's whole folder with its thinking, and the instructions they used");
                string pokesInZip = Entry("campaign/pokes_orcs.jsonl");
                Check(pokesInZip.Length > 0 && pokesInZip.Split('\n').Where(l => l.Trim().Length > 0).All(l => l.Contains("\"run\":\"smoke-run-C1\"")),
                      "lines are taken by the run's tag");
                JObject manifest = JObject.Parse(Entry("manifest.json"));
                var missing = (manifest["missing"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
                Check((manifest["files"] as JArray).Count >= 10 && missing.Any(m => m.Contains("call folder gone")) && (int)manifest["counts"]["calls"] == 2 && code == 1,
                      "the manifest lists every file, and the check names what is missing (a call's folder that is gone), so the bundle says it is not whole");
            }
        }

        private static Snapshot Tag(Snapshot s)
        {
            s.Run = RunTags.Id;
            s.Arm = RunTags.Arm;
            return s;
        }

        private static List<JObject> Lines(string text) =>
            text.Split('\n').Where(l => l.Trim().Length > 0).Select(l => { try { return JObject.Parse(l); } catch { return null; } }).Where(o => o != null).ToList();

        /// <summary>Runs.exe as the solution builds it, found from the smoke test's own folder.</summary>
        private static string FindRuns()
        {
            string d = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && d != null; i++, d = Path.GetDirectoryName(d.TrimEnd('\\')))
            {
                string exe = Path.Combine(d, "src", "BannerlordAgents.Runs", "bin", "Release", "net8.0", "Runs.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }
    }
}
