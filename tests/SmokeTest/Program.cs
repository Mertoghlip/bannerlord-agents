using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using BannerlordAgents;
using BannerlordAgents.Tools;
using Common = BannerlordAgents.Common;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    /// <summary>
    /// A small made-up world: an orc realm with a castle, a village and two lords, the player's realm with a castle,
    /// and the player's army closing in. Three hours are run through the worker exactly as the game would hand them
    /// over, then the report, the pokes and a recorded decision are printed and the files checked, along with the
    /// logs (docs/LOGGING.md): poke numbers carrying on, old answer files set aside, the fog check, the black box.
    /// Everything goes to its own folder (BANNERLORD_AGENTS_ROOT), never the game's.
    ///
    /// "SmokeTest --spin" instead starts a thread that spins for ever while the main thread waits: a stand-in for a
    /// frozen game, to try the freeze probe on (scripts\probe.ps1 -TestSpin).
    /// </summary>
    internal static partial class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--spin") return Spin();
            if (args.Length > 1 && args[0] == "--library") return ExportLibrary(args[1]);
            if (args.Length > 0 && args[0] == "--perf") return Perf(args);
            if (args.Length > 0 && args[0] == "--coverage") return Coverage(args);
            if (args.Length > 0 && args[0] == "--newspokes") return NewsPokes(args);
            string root = Path.Combine(Path.GetTempPath(), "BannerlordAgents-smoketest");
            try { Directory.Delete(root, true); } catch { /* first run */ }
            Environment.SetEnvironmentVariable("BANNERLORD_AGENTS_ROOT", root);
            return Run(root);   // a separate method, so nothing of the module is touched before the folder is set
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string root)
        {
            const string campaign = "smoketest";
            Check(Paths.Root == root, "the test writes to its own folder, never the game's (" + Paths.Root + ")");
            Diag.StartLaunch();
            Check(Diag.SessionDir != null && Diag.SessionDir.StartsWith(root), "a log folder for this launch: " + Diag.SessionDir);
            string dir = Paths.Data(campaign);
            Settings.Notify = false;
            string agentDir = Path.Combine(root, "agent");
            Settings.Agent = "shadow";
            Settings.AgentDir = agentDir;
            Live.CampaignRunning = true;

            // the channel already holds pokes up to #57 from an earlier session, and an answer file written back then
            Directory.CreateDirectory(Path.Combine(agentDir, "orders"));
            File.WriteAllText(Path.Combine(agentDir, "pokes.log"), "#56 | Summer 1, 1085, 10:00 | earlier\r\n#57 | Summer 1, 1085, 11:00 | earlier\r\n");
            string old = Path.Combine(agentDir, "orders", "poke-57.json");
            File.WriteAllText(old, "{\"poke\": 57, \"orders\": [{\"lord\": \"lord_bolg\", \"do\": \"hold\"}]}");
            File.SetLastWriteTime(old, DateTime.Now.AddHours(-1));

            // the game's own history of the orcs (GameHistory) starts their chronicle: two lines from before the session
            var history = new List<ChronicleEntry>
            {
                new ChronicleEntry { Hours = 900, Kind = "game:DeclareWar", Text = "Misty Mountain Orcs has declared war on Kingdom of Arnor and Gondor.", From = "game" },
                new ChronicleEntry { Hours = 950, Kind = "game:TakePrisoner", Text = "Grakmaw has been taken prisoner by Galenor.", From = "game" }
            };
            Brain.Reset(campaign, double.NaN, "orcs", history);
            Brain.OnHour(World(1000, playerSeen: false));                    // first look
            Brain.OnHour(World(1001, playerSeen: true));                     // the player's army comes into view
            Brain.OnNotice(new Notice { Id = 1, Hours = 1001.5, Kind = "raided", Text = "Orc Village is being raided by Aragorn (Arnor and Gondor).", Factions = { "orcs" }, Party = "main_party" });
            Snapshot last = World(1002, playerSeen: true);
            Brain.OnHour(last);                                              // the raid notice
            Brain.OnHour(World(1003, playerSeen: true));                     // nothing new: no poke

            string now = Brain.ReportNow(last);
            Console.WriteLine("---- agents.view ----");
            Console.WriteLine(now);
            Console.WriteLine("---- agents.pokes ----");
            Console.WriteLine(Brain.ShowPokes(10));

            Brain.WriteDecision(new DecisionRecord
            {
                Kind = "go_to", Text = "You set out for Orc Castle (castle, Misty Mountain Orcs).", Hours = 1002.25, Date = "Summer 3",
                Faction = "player_k", You = new SeenParty { Id = "main_party", Name = "Aragorn", Men = 500 },
                Target = new TargetInfo { Kind = "settlement", Id = "castle_orc", Name = "Orc Castle", Faction = "orcs" },
                SeenByYou = new List<SeenParty> { new SeenParty { Id = "lord_bolg", Name = "Bolg", Men = 300, Hostile = true, Distance = 25 } },
                SettleHours = 0.25, Corrections = 1
            });
            Console.WriteLine("---- agents.record show ----");
            Console.WriteLine(Brain.ShowDecisions(10));
            Files.FlushAll();
            // the checks read the whole report, not the console's first lines (since 0.9.4 YOUR PURSE comes before the lords)
            now = File.ReadAllText(Path.Combine(dir, "reports", "orcs_now.txt"));

            Check(Brain.PokesWatched == 3, "three pokes for the orcs (first look, the player's army, the raid), got " + Brain.PokesWatched);
            Check(now.Contains("YOUR LORDS AND ARMIES") && now.Contains("Bolg"), "the report lists the orc lords");
            Check(now.Contains("ENEMY Aragorn"), "the report lists the player's army as an enemy in view");
            string pokes = Read(Path.Combine(dir, "pokes_orcs.jsonl"));
            Check(pokes.Split('\n').Count(l => l.Trim().Length > 0) == 3, "three poke records written");
            Check(pokes.Contains("\"enemy_in_view\"") && pokes.Contains("\"raided\""), "the poke records carry their triggers");
            Check(pokes.Contains("\"notice\":1"), "the raid's trigger names the notice it came from");
            string decisions = Read(Path.Combine(dir, "record", "decisions.jsonl"));
            Check(decisions.Contains("\"realm\"") && decisions.Contains("\"recent_pokes\"") && decisions.Contains("\"settle_hours\":0.25"),
                  "the decision record carries the realm, the recent pokes and how long it took to settle");
            Check(File.Exists(Path.Combine(dir, "reports", "orcs_latest.txt")), "the latest report file exists");
            Check(now.Contains("Bolg [lord_bolg]") && now.Contains("Orc Castle [castle_orc]"), "the report shows ids in brackets for orders");

            // the agent channel: numbers carry on from the earlier session (#57), so these pokes are #58 to #60
            string pokesLog = Read(Path.Combine(agentDir, "pokes.log"));
            Check(pokesLog.Contains("#58 |") && pokesLog.Contains("#60 |") && !pokesLog.Contains("#1 |"), "poke numbers carry on after a load (#58 to #60, not #1 again)");
            string pokeTxt = Read(Path.Combine(agentDir, "poke.txt"));
            Check(pokeTxt.StartsWith("POKE #60") && pokeTxt.Contains("SHADOW") && pokeTxt.Contains("WHY YOU ARE ASKED NOW"), "poke.txt carries the number, the mode and the reasons");
            // 0.12.0: a realm agent's situation report and council briefing are written when its war room asks, as a council sits
            Check(!File.Exists(Path.Combine(agentDir, "report.txt")) && !File.Exists(Path.Combine(agentDir, "council.txt")), "a realm agent's report.txt and council.txt are not rewritten every hour any more");
            AskBriefing(AgentChannel.For("orcs"), agentDir);
            Check(ReadIf(Path.Combine(agentDir, "report.txt")).Contains("(written for your council of war") && ReadIf(Path.Combine(agentDir, "council.txt")).StartsWith("COUNCIL BRIEFING")
                  && !Directory.GetFiles(Path.Combine(agentDir, "orders"), Channel.BriefingRequest + "*").Any(),
                  "the war room's request brings the situation report and the briefing at once, and the request is taken away");
            Check(!Directory.GetFiles(agentDir, "*.new").Any(), "whole files are swapped in, with nothing half-written left behind");

            // answers: an old file is set aside; an answer to a poke from before the load is refused; a fresh one is
            // read and, with the clock stopped, acknowledged at once
            string answer = Path.Combine(agentDir, "orders", "answer-60.json");
            File.WriteAllText(answer, "{\"poke\": 60, \"orders\": [{\"lord\": \"lord_bolg\", \"do\": \"garrison\", \"target\": \"castle_orc\", \"why\": \"hold\"},"
                                      + " {\"lord\": \"Azog\", \"do\": \"engage\", \"target\": \"lord_ghost_9\"}], \"note\": \"test\"}");
            string stale = Path.Combine(agentDir, "orders", "answer-57.json");
            File.WriteAllText(stale, "{\"poke\": 57, \"orders\": [{\"lord\": \"lord_bolg\", \"do\": \"hold\"}]}");
            Thread.Sleep(400);   // a file younger than 300 ms counts as still being written
            AgentChannel.Scan();
            Files.FlushAll();
            string receipts = Read(Path.Combine(agentDir, "receipts.log"));
            Check(receipts.Contains("poke-57.json: SET ASIDE") && File.Exists(Path.Combine(agentDir, "orders", "stale", "poke-57.json")),
                  "an answer file left from before the session is set aside unread");
            Check(receipts.Contains("from before the game was loaded again"), "an answer to a poke from before the load is refused");
            Check(receipts.Contains("RECEIVED 2 orders") && Background.ToMain.Count == 2, "with the game's clock stopped, the answer is acknowledged at once and its orders wait for the clock");
            Check(Diag.CountOf("agent:unknown id") == 1, "an id the agent was never shown (lord_ghost_9) is flagged");

            bool parsed = AgentChannel.Parse(answer.Replace("answer-60", "done\\answer-60"), out int poke, out List<AgentChannel.AgentOrder> orders, out string error);
            Check(parsed && poke == 60 && orders.Count == 2 && orders[1].Do == "engage" && orders[0].Poke == 60, "an answer file is read: " + (error ?? orders.Count + " orders for poke " + poke));
            AgentChannel.Receipt(orders[0], "checked", "valid: Bolg garrisons Orc Castle (shadow mode: not carried out)", 1002.5);
            Files.FlushAll();
            receipts = Read(Path.Combine(agentDir, "receipts.log"));
            Check(receipts.Contains("CHECKED") && receipts.Contains("Answered") && receipts.Contains("after poke #60"), "the receipt says what happened and how long the answer took");
            Check(!AgentChannel.Parse(answer + ".missing", out _, out _, out _), "a missing or broken file is reported, not guessed at");

            // the chronicle (docs/MEMORY.md): the game's own history first, then every notice that poked, and the orders
            AgentChannel.Receipt(orders[0], "applied", "Bolg garrison Orc Castle", 1002.6);
            AgentChannel.Receipt(orders[1], "refused", "Azog cannot attack lord_ghost_9: no such lord", 1002.7);
            Files.FlushAll();
            string chronicleData = Read(Path.Combine(dir, "chronicle_orcs.jsonl"));
            string chronicleText = Read(Path.Combine(agentDir, "chronicle.txt"));
            Check(chronicleText.StartsWith("THE CHRONICLE OF MISTY MOUNTAIN ORCS") && chronicleText.Contains("declared war on Kingdom of Arnor and Gondor. (from the game's records)")
                  && chronicleText.IndexOf("taken prisoner by Galenor", StringComparison.Ordinal) < chronicleText.IndexOf("Orc Village is being raided", StringComparison.Ordinal),
                  "the chronicle starts with the game's own history, older lines first, marked as such");
            Check(chronicleData.Contains("\"kind\":\"raided\"") && chronicleText.Contains("You ordered: Bolg garrison Orc Castle. Why: hold.")
                  && chronicleText.Contains("was refused: Azog cannot attack lord_ghost_9") && !chronicleText.Contains("CHECKED"),
                  "the chronicle keeps every notice that poked, and the orders carried out or refused (not shadow mode's checks)");
            Check(File.Exists(Path.Combine(agentDir, "chronicle.jsonl")) && Read(Path.Combine(agentDir, "chronicle.jsonl")).Contains("\"hours\":950"),
                  "the war room gets the chronicle with its hours (chronicle.jsonl)");
            JObject pokeMeta = JObject.Parse(Read(Path.Combine(agentDir, "poke.json")));
            Check((int)pokeMeta["number"] == 60 && (string)pokeMeta["campaign"] == "smoketest" && (string)pokeMeta["realm"] == "orcs" && (double)pokeMeta["hours"] == 1002,
                  "poke.json tells the war room the poke's number, hour, campaign and realm");
            Check(pokeTxt.Contains("RECENT HISTORY, LAST TWO DAYS (from your chronicle)") && pokeTxt.Contains("Orc Village is being raided"),
                  "the report's recent history comes from the chronicle");

            // the fog check: the real reports are clean; a report with an id the realm cannot know is caught
            Check(Diag.CountOf("fog:reports") >= 4 && Diag.CountOf("fog:leaks") == 0, "every report was fog-checked, and none leaked (" + Diag.CountOf("fog:reports") + " checked)");
            var v = new View { Faction = "orcs", FactionName = "Misty Mountain Orcs", Hours = 1002 };
            v.Forces.Add(new ViewParty { Id = "lord_bolg", Lord = "lord_bolg", Name = "Bolg", Faction = "orcs" });
            List<string> leaks = Audit.Fog(v, "- Bolg [lord_bolg]\n- ENEMY Someone [lord_hidden_1] far away", new Dictionary<string, Sighting>(), new HashSet<string>());
            Check(leaks.Count == 1 && leaks[0].Contains("lord_hidden_1"), "the fog check catches a planted leak: " + string.Join("; ", leaks));
            // 0.11.3: a spot on the map is plain geography (its own watch posts in past orders were flagged 61 times in run C1)
            List<string> spots = Audit.Fog(v, "- Autumn 3 Â· You ordered: Bolg [lord_bolg] keeps watch 23 south-east of Rad-nakh [p784_1118]", new Dictionary<string, Sighting>(), new HashSet<string>());
            Check(spots.Count == 0, "a map spot in the realm's own past order is no leak: " + string.Join("; ", spots));
            Check(now.Contains("on the way to raid Orc Village"), "a lord marching to raid is not yet said to be raiding");

            // fewer pokes (docs/reports/2026-10-08-pokes.md): a small fight goes to the logs only; an enemy lord far
            // from the realm's holdings pokes once heading for one, or once it comes near while still in view
            int pokesBefore = Brain.PokesWatched;
            Brain.OnNotice(new Notice { Id = 2, Hours = 1009.5, Kind = "battle", Text = "Battle: Bolg (Misty Mountain Orcs) against Looters (Looters); Bolg won.", Factions = { "orcs" }, Quiet = "bandits" });
            Snapshot afterFight = World(1010, playerSeen: true);
            Brain.OnHour(afterFight);
            Check(Brain.PokesWatched == pokesBefore, "a fight with bandits pokes no one");
            Check(!Brain.ReportNow(afterFight).Contains("against Looters"), "and stays out of the report's news");
            Files.FlushAll();
            Check(!Read(Path.Combine(agentDir, "chronicle.txt")).Contains("against Looters"), "and out of the chronicle");
            // a lord set free goes into the chronicle without a poke
            Brain.OnNotice(new Notice { Id = 3, Hours = 1010.2, Kind = "released", Text = "Grakmaw (Misty Mountain Orcs) was ransomed from Kingdom of Arnor and Gondor.", Factions = { "orcs" }, RecordOnly = true });
            Brain.OnHour(World(1010.5, playerSeen: true));
            Files.FlushAll();
            Check(Brain.PokesWatched == pokesBefore && Read(Path.Combine(agentDir, "chronicle.txt")).Contains("Grakmaw (Misty Mountain Orcs) was ransomed"),
                  "a lord set free goes into the chronicle, without a poke");
            Files.FlushAll();
            Check(Read(Path.Combine(dir, "notices.jsonl")).Contains("\"quiet\":\"bandits\"") && Diag.CountOf("quiet:bandits") == 1, "but it is in the logs, marked as not poked");
            var far = new PartyRow { Id = "lord_far", Name = "Elendil", Faction = "player_k", Kind = "lord", Men = 200, Strength = 300, X = 160, Y = 140, Doing = "GoToSettlement", TargetSettlement = "castle_men", Spottable = true };
            Brain.OnHour(World(1011, true, far));
            Check(Brain.PokesWatched == pokesBefore && Diag.CountOf("sighting:far off") == 1, "an enemy lord 60 away, going elsewhere, pokes no one");
            far.Doing = "BesiegeSettlement";
            far.TargetSettlement = "castle_orc";
            Brain.OnHour(World(1012, true, far));
            Check(Brain.PokesWatched == pokesBefore + 1, "the same lord pokes once marching on an orc castle");
            var drifter = new PartyRow { Id = "lord_drifter", Name = "Isildur", Faction = "player_k", Kind = "lord", Men = 150, Strength = 200, X = 160, Y = 60, Doing = "Hold", Spottable = true };
            Brain.OnHour(World(1013, true, far, drifter));
            Check(Brain.PokesWatched == pokesBefore + 1, "another far-off lord pokes no one");
            drifter.X = 125;
            drifter.Y = 104;
            Brain.OnHour(World(1014, true, far, drifter));
            Check(Brain.PokesWatched == pokesBefore + 2, "until coming within reach of an orc holding, though never out of view");
            string cutPokes = Read(Path.Combine(dir, "pokes_orcs.jsonl"));
            Check(cutPokes.Contains("\"party\":\"lord_far\"") && cutPokes.Contains("\"party\":\"lord_drifter\""), "each of those pokes names its lord");

            ToolTests(dir, agentDir);
            DashboardTests(root, dir);

            // an enemy army in view is history; then an earlier save is loaded, and the chronicle forgets what came after it
            Files.FlushAll();
            Check(Read(Path.Combine(agentDir, "chronicle.txt")).Contains("Enemy army in view: Theoden"), "an enemy army coming into view goes into the chronicle");
            int linesBefore = Chronicle.Count;
            Brain.Reset(campaign, 1005, "orcs", null);
            Brain.OnHour(World(1005, playerSeen: false));
            Files.FlushAll();
            string cut = Read(Path.Combine(agentDir, "chronicle.txt"));
            Check(Chronicle.Count < linesBefore && !cut.Contains("Theoden") && !cut.Contains("ransomed") && cut.Contains("Orc Village is being raided") && cut.Contains("taken prisoner by Galenor"),
                  "loading an earlier save cuts the chronicle back to its date, and keeps what came before (" + linesBefore + " lines, then " + Chronicle.Count + ")");
            Check(Read(Path.Combine(dir, "chronicle_orcs.jsonl")).Contains("Theoden"), "but the campaign's own file keeps every line, for the saves whose history it is (0.9.1)");

            TwoRealmsTests(root, agentDir);
            HelperTests(root, agentDir);
            RecordTests(root, agentDir);
            LanguageTests(root, agentDir);
            OwnToolTests(root, agentDir);
            DiplomacyTests(root, agentDir);
            AlliesTests(root, agentDir);
            NewsTests(root);

            // whole-file writes are swapped in
            string swap = Path.Combine(root, "swap.txt");
            Files.Overwrite(swap, "first");
            Files.Overwrite(swap, "second");
            Check(File.ReadAllText(swap) == "second" && !File.Exists(swap + ".new"), "rewriting a whole file swaps the new one in");

            // the flight recorder's own files
            Diag.Alarm("smoke_alarm", "a test alarm");
            try { throw new InvalidOperationException("a test error"); }
            catch (Exception e) { Diag.Error("smoke test", e); }
            Diag.Health();
            Diag.Summary(false);
            string box = Diag.DumpBlackBox("the smoke test");
            Diag.Drain();
            string trace = Read(Path.Combine(Diag.SessionDir, "trace.jsonl"));
            Check(trace.Contains("\"k\":\"poke\"") && trace.Contains("\"k\":\"alarm\"") && trace.Contains("\"k\":\"answer\""), "the detailed record holds pokes, answers and alarms");
            Check(Read(Path.Combine(Diag.SessionDir, "errors.log")).Contains("a test error"), "errors.log keeps the full trace of an error");
            Check(box != null && Read(box).Contains("LAST THINGS THE MOD SAW AND DID"), "the black box is written");
            Check(File.Exists(Path.Combine(Diag.SessionDir, "health.csv")) && Read(Path.Combine(Diag.SessionDir, "summary.txt")).Contains("SESSION SUMMARY"), "the health row and the summary are written");
            Check(Read(Path.Combine(Diag.SessionDir, "summary.txt")).Contains("Not poked: 1 notices kept to the logs"), "the summary counts what was kept from the agent");
            Check(Read(Path.Combine(Diag.SessionDir, "story.log")).Contains("poke:"), "the readable story is written into this launch's folder too");
            Diag.EndLaunch("the smoke test ended");
            Check(!File.Exists(Path.Combine(Diag.SessionDir, "running.marker")), "a clean end removes the running marker");

            Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : "\n" + _failures + " CHECK(S) FAILED");
            Console.WriteLine("files: " + root);
            return _failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// The tools (docs/TOOLS.md) on a made-up world, with the game's thread replaced by catchers: the library's own
        /// checks, the language, defend sheltering and striking, the safety rule, an army gathering and its next order,
        /// adjust, the orders in force, the ready-made facts, the look-ups, and the agent's new order format.
        /// </summary>
        private static void ToolTests(string dir, string agentDir)
        {
            var moves = new List<KeyValuePair<int, Move>>();
            var released = new List<int>();
            var thens = new List<KeyValuePair<int, JObject>>();
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => released.Add(id);
            Runner.ThenSink = (id, then) => thens.Add(new KeyValuePair<int, JObject>(id, then));

            List<string> problems = Library.Problems();
            Check(problems.Count == 0, "the tool library passes its own checks" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""));
            Check(Library.All.Count == 21, "the library holds 21 orders (8 moves, 10 tools, 3 for armies), got " + Library.All.Count);
            Check(Library.Text().Contains("**join** (target: one of your lords)") && Library.Text().Contains("more than 1.5 times the besiegers' strength"),
                  "the library has the join order, and relieve says when the defenders inside sally out");
            var scope = new TestScope();
            Check(Expr.Truthy(Expr.Parse("2 + 3 * 4 == 14 and not false and twice(x) >= 4").Eval(scope)), "the language: sums, comparisons, and/not, names and functions");
            Check(!Expr.Truthy(Expr.Parse("none < 3 or missing > 0").Eval(scope)), "a comparison with something unknown is false, so no rule fires on a missing fact");
            ToolDef bad = ToolDef.FromJson("{\"tool\":\"bad\",\"start\":\"a\",\"states\":{\"a\":{\"do\":\"fly at\",\"when\":[{\"if\":\"teleport(x)\",\"go\":\"b\"}]}}}", "bad");
            Check(bad.Problems.Count >= 3, "the checker catches a made-up move, a made-up function and a missing state (" + bad.Problems.Count + " problems)");
            Check(Library.Text().Contains("**gather**") && Library.Text().Contains("caution"), "the agent's library lists every order with its settings");

            // defend: patrols, shelters from an army too strong for it, comes out once it has gone, strikes a weak lord
            var bolg = new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, SizeLimit = 320, Strength = 400, Speed = 5, FoodDays = 10, X = 104, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(3000, bolg));
            var d = new ToolOrder { Tool = "defend", Lord = "lord_bolg", LordName = "Bolg", Party = "p_bolg", At = "castle_orc" };
            Runner.Start(901, d);
            Check(moves.Any(m => m.Key == 901 && m.Value.Verb == "patrol" && m.Value.Place == "castle_orc"), "defend starts by patrolling around its holding");
            Check(moves.Any(m => m.Key == 901 && m.Value.Verb == "patrol" && m.Value.R == 20), "and its patrol keeps within the radius it watches (20), not the game's half a day's march");
            var host = new PartyRow { Id = "p_host", Leader = "lord_host", Name = "Theoden", Faction = "player_k", Kind = "lord", Men = 1500, ArmyMen = 2000, ArmyParties = 4, ArmyStrength = 2600, Strength = 900, Speed = 4, X = 112, Y = 102, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(3001, bolg, host));
            Check(moves.Any(m => m.Key == 901 && m.Value.Verb == "enter" && m.Value.Place == "castle_two"), "defend shelters from an army too strong for it in the nearest other castle, not the one it guards");
            moves.Clear();
            bolg.InSettlement = "castle_orc";
            for (int h = 3002; h <= 3005; h++) Brain.OnHour(ToolWorld(h, bolg));
            Check(moves.Any(m => m.Key == 901 && m.Value.Verb == "patrol"), "and patrols again some hours after the army has gone");
            bolg.InSettlement = null;
            var scout = new PartyRow { Id = "p_scout", Leader = "lord_scout", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 60, Strength = 80, Speed = 6, X = 108, Y = 96, Doing = "Hold", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(3006, bolg, scout));
            Check(moves.Any(m => m.Key == 901 && m.Value.Verb == "engage" && m.Value.Party == "p_scout"), "defend strikes a weak enemy lord in its area");

            // the safety rule: a lord on a plain move shelters from an enemy far stronger, in the nearest friendly castle
            var azog = new PartyRow { Id = "p_azog", Leader = "lord_azog", Name = "Azog", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 250, Strength = 260, Speed = 5, FoodDays = 10, X = 130, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(3007, bolg, azog));
            moves.Clear();
            Runner.Start(902, new ToolOrder { Tool = "goto", Lord = "lord_azog", LordName = "Azog", Party = "p_azog", At = "castle_men" });
            Check(moves.Any(m => m.Key == 902 && m.Value.Verb == "near" && m.Value.Place == "castle_men"), "goto to an enemy holding stops close to it, not inside");
            var hunter = new PartyRow { Id = "p_hunter", Leader = "lord_hunter", Name = "Elrohir", Faction = "player_k", Kind = "lord", Men = 500, Strength = 700, Speed = 6, X = 124, Y = 101, Doing = "Hold", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(3008, bolg, azog, hunter));
            Check(moves.Any(m => m.Key == 902 && m.Value.Verb == "enter" && m.Value.Place == "castle_two"),
                  "a lord on an order shelters from an enemy far stronger in the nearest castle it reaches first whose defenders, with it, would hold");
            Check(Runner.InForce(Runner.WorldFor(ToolWorld(3008, bolg, azog, hunter))).Contains("sheltering in Orc Keep from Elrohir"), "the orders in force say who is sheltering, and from whom");

            // gather, then relieve: the army forms once its lord has come, and the next order starts
            var ugluk = new PartyRow { Id = "p_ug", Leader = "lord_ug", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 200, Strength = 250, ArmyLeader = "p_ug", ArmyMen = 350, ArmyParties = 2, ArmyStrength = 450, Speed = 4, FoodDays = 10, X = 139, Y = 101, Doing = "Hold", Spottable = true };
            var lugdush = new PartyRow { Id = "p_lug", Leader = "lord_lug", Name = "Lugdush", Faction = "orcs", Kind = "lord", Men = 150, Strength = 200, ArmyLeader = "p_ug", Speed = 4, FoodDays = 10, X = 120, Y = 100, Doing = "EscortParty", Spottable = true };
            Brain.OnHour(ToolWorld(3009, ugluk, lugdush));
            var g = new ToolOrder { Tool = "gather", Lord = "lord_ug", LordName = "Ugluk", Party = "p_ug", At = "castle_two", Then = JObject.Parse("{\"do\":\"relieve\",\"at\":\"castle_orc\"}") };
            g.Lords.Add("p_lug");
            moves.Clear();
            Runner.Start(903, g);
            Check(moves.Any(m => m.Key == 903 && m.Value.Verb == "near" && m.Value.Place == "castle_two"), "gather waits close to the meeting place");
            Brain.OnHour(ToolWorld(3010, ugluk, lugdush));
            Check(thens.Count == 0, "and keeps waiting while a called lord is still on the way");
            lugdush.Attached = true;
            lugdush.X = 139;
            Brain.OnHour(ToolWorld(3011, ugluk, lugdush));
            Check(thens.Any(t => t.Key == 903 && (string)t.Value["do"] == "relieve"), "once the army has formed, its next order (then) starts");
            Files.FlushAll();
            string notes = Read(Path.Combine(dir, "notices.jsonl"));
            Check(notes.Contains("Forces too strong for Bolg") && notes.Contains("is sheltering in Orc Keep from Elrohir") && notes.Contains("army has formed at Orc Keep"),
                  "every call-back goes to the agent as a notice");

            // adjust: new numbers for a running tool, kept to their range
            Runner.Adjust(901, new Dictionary<string, double> { ["radius"] = 500 }, new AgentChannel.AgentOrder { File = "test.json", Index = 1, Lord = "lord_bolg", Do = "adjust", Received = DateTime.Now });
            Files.FlushAll();
            Check(Read(Path.Combine(agentDir, "receipts.log")).Contains("radius 80 (radius kept to 80"), "adjust changes a tool's numbers, kept to their range");

            // the poke: what changed since the last answer, the orders in force, the ready-made facts; and the look-ups
            string poke = Read(Path.Combine(agentDir, "poke.txt"));
            Check(poke.Contains("WHAT CHANGED SINCE YOUR LAST ANSWER (to poke #60)"), "the poke says what changed since the agent's last answer");
            Check(poke.Contains("YOUR ORDERS IN FORCE") && poke.Contains("Bolg [lord_bolg]: defend Orc Castle [castle_orc]"), "the poke lists the orders in force, with ids");
            Check(poke.Contains("READY-MADE FACTS") && poke.Contains("Your lords leading parties:"), "the poke has the ready-made facts");
            Check(poke.Contains("Ugluk [lord_ug]: army of 350 men in 2 parties") && poke.Contains("(your order: gather)"), "the lords' list marks who holds an order");
            string look = Path.Combine(agentDir, "lookups");
            Check(File.Exists(Path.Combine(look, "castle_orc.txt")) && File.Exists(Path.Combine(look, "lord_ug.txt")) && File.Exists(Path.Combine(look, "README.txt")),
                  "a look-up file for each holding and lord");
            Check(Read(Path.Combine(look, "castle_orc.txt")).Contains("Ugluk [lord_ug]") && Read(Path.Combine(look, "lord_ug.txt")).Contains("Hours to your holdings"),
                  "a holding's look-up lists the lords who can get there, and a lord's the hours to each holding");
            Check(Diag.CountOf("fog:leaks") == 0, "the new sections pass the fog check (" + Diag.CountOf("fog:reports") + " reports checked)");

            // the agent's new order format: places, lords, settings (also at the top level), and a next order
            string file = Path.Combine(agentDir, "orders", "v2-test.json");
            File.WriteAllText(file, "{\"poke\": 70, \"orders\": [{\"lord\": \"lord_bolg\", \"do\": \"defend\", \"at\": \"castle_orc\", \"radius\": 25},"
                                    + " {\"lord\": \"lord_ug\", \"do\": \"gather\", \"at\": \"castle_two\", \"lords\": [\"lord_lug\"], \"settings\": {\"wait_hours\": 24},"
                                    + " \"then\": {\"do\": \"relieve\", \"at\": \"castle_orc\"}}, {\"lord\": \"lord_azog\", \"do\": \"adjust\", \"caution\": 0}]}");
            bool ok = AgentChannel.Parse(file, out int pn, out List<AgentChannel.AgentOrder> orders, out string error);
            File.Delete(file);
            Check(ok && pn == 70 && orders.Count == 3 && orders[0].At == "castle_orc" && orders[0].Settings["radius"] == 25 && orders[1].Lords.Count == 1
                  && orders[1].Settings["wait_hours"] == 24 && (string)orders[1].Then["do"] == "relieve" && orders[2].Settings["caution"] == 0,
                  "the agent's new orders are read: places, lords, settings and a next order" + (error != null ? " (" + error + ")" : ""));

            // as in the game: the order forms the army after the hour's copy, so the tool's first look sees no army yet
            // (first live test, Oct 9: "broke up before it formed" at once, and the next order started early)
            var gorbag = new PartyRow { Id = "p_gor", Leader = "lord_gor", Name = "Gorbag", Faction = "orcs", Kind = "lord", Men = 200, Strength = 260, Speed = 4, FoodDays = 10, X = 138, Y = 100, Doing = "Hold", Spottable = true };
            var shagrat = new PartyRow { Id = "p_sha", Leader = "lord_sha", Name = "Shagrat", Faction = "orcs", Kind = "lord", Men = 180, Strength = 240, Speed = 4, FoodDays = 10, X = 125, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(3012, gorbag, shagrat));
            var g2 = new ToolOrder { Tool = "gather", Lord = "lord_gor", LordName = "Gorbag", Party = "p_gor", At = "castle_two", Then = JObject.Parse("{\"do\":\"defend\",\"at\":\"castle_two\"}") };
            g2.Lords.Add("p_sha");
            released.Clear();
            thens.Clear();
            Runner.Start(904, g2);
            Check(!released.Contains(904) && thens.Count == 0, "a new army is not taken for broken at the tool's first look, made before it formed");
            gorbag.ArmyLeader = "p_gor";
            gorbag.ArmyMen = 380;
            gorbag.ArmyParties = 2;
            shagrat.ArmyLeader = "p_gor";
            shagrat.Doing = "EscortParty";
            Brain.OnHour(ToolWorld(3013, gorbag, shagrat));
            Check(!released.Contains(904) && thens.Count == 0, "the hour after, it waits for the called lord");
            // an army that truly breaks up: its next order does not start, and the lord is set free
            gorbag.ArmyLeader = null;
            shagrat.ArmyLeader = null;
            Brain.OnHour(ToolWorld(3014, gorbag, shagrat));
            Check(released.Contains(904) && thens.Count == 0, "an army that broke up sets its lord free, and its next order does not start");

            FactsTests(dir, agentDir, moves, released, thens);
            DefendTests(moves, released);
            RefillAndReportTests(moves);
            TrailTests();
            WatchShadowTests(dir, moves, released);
            SiegeReliefTests(dir);
            SiegeControlTests(dir, moves, released, thens);
            ArrivalTests(dir, moves);
            GroupDangerTests(dir, moves, released);
            MoneyTests(dir, moves, released);
            FieldLessonsTests(dir, moves);
            GameClockTests(dir);
            RoadTests();
            RunTests();
            LieutenantPacTests();

            // poke numbers: a campaign's poke file holds a whole report per line, longer than any tail; the highest number
            // is read from the whole file once, then kept beside it
            string numbered = Path.Combine(dir, "pokes_numbers_test.jsonl");
            File.WriteAllText(numbered, "{\"type\":\"poke\",\"number\":125,\"report\":\"" + new string('x', 200000) + "\"}\n{\"type\":\"poke\",\"number\":9,\"report\":\"short\"}\n");
            Check(AgentChannel.HighestIn(numbered) == 125, "the highest poke number is found in a poke file of long lines (" + AgentChannel.HighestIn(numbered) + ")");

            Runner.Reset();
        }

        /// <summary>
        /// Version 0.6 on the made-up world: relieve counting the defenders inside by the game's sally rule, the join order
        /// and gather waiting for the lords still marching to join, the captured lords, the realm in sum, the muster, the
        /// council's briefing, and the fog check over all of it.
        /// </summary>
        private static void FactsTests(string dir, string agentDir, List<KeyValuePair<int, Move>> moves, List<int> released, List<KeyValuePair<int, JObject>> thens)
        {
            // relieve: the garrison and the lords inside count only when the game would send them out (relief + inside > 1.5 x besiegers)
            var sieger = new PartyRow { Id = "p_sieger", Leader = "lord_sieger", Name = "Elladan", Faction = "player_k", Kind = "lord", Men = 700, Strength = 1000, Speed = 4, X = 101, Y = 102, Besieging = "castle_orc", Doing = "BesiegeSettlement", Spottable = true };
            var inside = new PartyRow { Id = "p_in", Leader = "lord_in", Name = "Muzgash", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 300, Strength = 300, Speed = 4, FoodDays = 5, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            var relief = new PartyRow { Id = "p_rel", Leader = "lord_rel", Name = "Grishnakh", Faction = "orcs", Kind = "lord", Men = 500, SizeLimit = 600, Strength = 700, Speed = 4, FoodDays = 9, X = 125, Y = 100, Doing = "Hold", Spottable = true };
            Snapshot Siege(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                SettlementRow c = s.SettlementById["castle_orc"];
                c.BesiegedBy = "player_k";
                c.Besieger = "p_sieger";
                c.GarrisonStrength = 600;
                return s;
            }
            Snapshot w1 = Siege(4000, sieger, inside, relief);
            World world = Runner.WorldFor(w1);
            Check(world.InsideStrength(w1.SettlementById["castle_orc"]) == 900 && world.ReliefStrength(w1.SettlementById["castle_orc"], 700, "p_rel") == 1600
                  && world.ReliefStrength(w1.SettlementById["castle_orc"], 400, "p_rel") == 400,
                  "the defenders inside (garrison 600, Muzgash 300) join a relief of 700 against 1,000 (1,600 > 1,500), not one of 400 (1,300)");
            Brain.OnHour(w1);
            moves.Clear();
            Runner.Start(905, new ToolOrder { Tool = "relieve", Lord = "lord_rel", LordName = "Grishnakh", Party = "p_rel", At = "castle_orc" });
            Check(moves.Any(m => m.Key == 905 && m.Value.Verb == "engage" && m.Value.Party == "p_sieger"), "relieve strikes at once when the defenders inside would sally out beside it");
            relief.Strength = 400;
            Brain.OnHour(Siege(4001, sieger, inside, relief));
            moves.Clear();
            Runner.Start(906, new ToolOrder { Tool = "relieve", Lord = "lord_rel", LordName = "Grishnakh", Party = "p_rel", At = "castle_orc" });
            Files.FlushAll();
            Check(!moves.Any(m => m.Key == 906 && m.Value.Verb == "engage") && Read(Path.Combine(dir, "notices.jsonl")).Contains("too weak to strike the besiegers of Orc Castle yet: strength 400"),
                  "a weaker relief waits, and says it is too weak, counting itself alone");
            Runner.Ended(906, "test over");

            // join: the far lord marches to the army's leader; gather waits for it even once the army is otherwise complete
            var leader = new PartyRow { Id = "p_ldr", Leader = "lord_ldr", Name = "Mauhur", Faction = "orcs", Kind = "lord", Men = 300, Strength = 400, ArmyLeader = "p_ldr", ArmyMen = 450, ArmyParties = 2, ArmyStrength = 600, Speed = 4, FoodDays = 9, X = 139, Y = 100, Doing = "GoToPoint", Spottable = true };
            var member = new PartyRow { Id = "p_mem", Leader = "lord_mem", Name = "Snaga", Faction = "orcs", Kind = "lord", Men = 150, Strength = 200, ArmyLeader = "p_ldr", Attached = true, Speed = 4, FoodDays = 9, X = 139, Y = 100, Doing = "EscortParty", Spottable = true };
            var far = new PartyRow { Id = "p_far", Leader = "lord_far2", Name = "Radbug", Faction = "orcs", Kind = "lord", Men = 250, SizeLimit = 300, Strength = 320, Speed = 4, FoodDays = 9, X = 350, Y = 100, Doing = "Hold", Spottable = true };   // 210 to Orc Keep: about 66 hours at speed 4 by the game's rules (3.2 map units an hour over a day)
            Brain.OnHour(ToolWorld(4002, leader, member, far));
            var ga = new ToolOrder { Tool = "gather", Lord = "lord_ldr", LordName = "Mauhur", Party = "p_ldr", At = "castle_two", Then = JObject.Parse("{\"do\":\"defend\",\"at\":\"castle_two\"}") };
            ga.Lords.Add("p_mem");
            ga.Lords.Add("p_far");
            moves.Clear();
            thens.Clear();
            released.Clear();
            // as gather sends them: the far lord's join order goes out with the army's own
            Runner.Start(908, new ToolOrder { Tool = "join", Lord = "lord_far2", LordName = "Radbug", Party = "p_far", Target = "p_ldr" });
            Runner.Start(907, ga);
            Check(moves.Any(m => m.Key == 908 && m.Value.Verb == "join" && m.Value.Party == "p_ldr"), "join marches the far lord to the army's leader, to join once close enough");
            Brain.OnHour(ToolWorld(4003, leader, member, far));
            Check(thens.Count == 0 && Runner.InForce(Runner.WorldFor(ToolWorld(4003, leader, member, far))).Contains("1 marching to join"),
                  "gather waits for the lord still marching to join, though the rest of its army has come");
            Runner.Ended(908, "joined Mauhur's army");   // the game's thread joins it and ends the order
            Brain.OnHour(ToolWorld(4004, leader, member, far));
            Check(thens.Any(t => t.Key == 907), "once no lord is marching to join, the army is formed and its next order starts");
            Runner.Start(909, new ToolOrder { Tool = "join", Lord = "lord_far2", LordName = "Radbug", Party = "p_far", Target = "p_vanished" });
            Check(released.Contains(909), "a join order whose army's leader is gone fails, and frees the lord");
            // 0.13.8 (Mert, Oct 29: "my liutenant is not able to see folks in armies"): each army's lords are named under it in the report
            Snapshot armyWorld = ToolWorld(4005, leader, member, far);
            string armyReport = Views.Report(Views.Build(armyWorld, "orcs", armyWorld.WatchedSees, armyWorld.HostileToWatched, new Brain.Memory()), new List<Trigger>());
            int armyLine = armyReport.IndexOf("- Mauhur [lord_ldr]: army of 450 men in 2 parties", StringComparison.Ordinal);
            Check(armyLine >= 0 && armyReport.IndexOf("  In the army (1 lord beside its leader, marching with it):", armyLine, StringComparison.Ordinal) > armyLine
                  && armyReport.Contains("  - Snaga [lord_mem]: 150 men, strength 200."),
                  "an army's lords are named under it in the report, so they can be ordered by name");

            // the realm in sum, the captured lords (where, by whom, since when) and the muster, in the report and the look-ups
            Snapshot s2 = ToolWorld(4010, leader, member, far, inside);
            s2.CallRange = 60;
            s2.Clans.Add(new ClanRow { Id = "clan_azog", Name = "Azog's clan", Leader = "lord_azog_h", Influence = 900, Gold = 15000, Ruler = true });
            s2.Clans.Add(new ClanRow { Id = "clan_mau", Name = "Mauhur's clan", Leader = "lord_ldr", Influence = 120 });
            leader.Clan = "clan_mau";
            s2.Captives.Add(new CaptiveRow { Hero = "lord_cap1", Name = "Lugdush", Holding = "castle_men", Faction = "player_k", X = 170, Y = 100, Since = 3990 });
            s2.Captives.Add(new CaptiveRow { Hero = "lord_cap2", Name = "Ufthak", Holding = "castle_men", Faction = "player_k", X = 170, Y = 100, Since = 4001 });
            s2.Captives.Add(new CaptiveRow { Hero = "lord_cap3", Name = "Gorbag", Party = "p_hunter", PartyLord = "lord_hunter", Faction = "player_k", X = 200, Y = 150, Since = 4005 });
            View v = Views.Build(s2, "orcs", s2.WatchedSees, s2.HostileToWatched, Brain.Get("orcs"));
            string report = Views.Report(v, null);
            Check(report.Contains("YOUR REALM IN SUM") && report.Contains("In the field: 4 lords leading parties, 900 men") && report.Contains("Prisoners of the enemy: 3 of your lords"),
                  "the report sums up the realm: lords, men, castles, villages, prisoners");
            Check(report.Contains("YOUR CAPTURED LORDS (3, by where they are held)") && report.Contains("In Fornost [castle_men] (castle of Kingdom of Arnor and Gondor): 2 lords: Lugdush [lord_cap1] since")
                  && report.Contains("Held by a party [lord_hunter] (Kingdom of Arnor and Gondor), on the march: 1 lord: Gorbag [lord_cap3]"),
                  "the captured lords, grouped by where they are held, with since when");
            List<string> leaks = Audit.Fog(v, report, s2.WatchedSees, s2.InspectedByWatched);
            Check(leaks.Count == 0, "the captured lords pass the fog check (the game's public records)" + (leaks.Count > 0 ? ": " + string.Join("; ", leaks) : ""));
            World w2 = Runner.WorldFor(s2);
            // three lords lead parties (Snaga marches in Mauhur's army): Mauhur's army and Muzgash within a day, Radbug in under three
            string muster = Facts.MusterLine(w2, "castle_two");
            Check(muster.StartsWith("within a day 2 lords, 650 men, strength 900 (1 can be called at once)") && muster.Contains("within 3 days 3 lords") && muster.EndsWith("all 3: 900 men, strength 1,220"),
                  "the muster at a holding: running totals by day, and who can be called at once (" + muster + ")");
            List<string> full = Facts.Muster(w2, "castle_two");
            Check(full.Count == 3 && full[0].Contains("Mauhur [lord_ldr]") && full[0].Contains("leads an army") && full[0].Contains("Muzgash*") && full[1].Contains("adds no one")
                  && full[2].StartsWith("within 3 days: 3 lords") && full[2].Contains("Radbug [lord_far2]"),
                  "the full muster lists every lord by band, marking who can be called at once (*)");
            Dictionary<string, string> look = Facts.Lookups(w2, Brain.Get("orcs").LastSeen);
            Check(look.ContainsKey("captured") && look["captured"].Contains("Lugdush [lord_cap1]") && look.ContainsKey("lord_cap1") && look["lord_cap1"].Contains("is a prisoner: held in Fornost [castle_men]")
                  && look["castle_two"].Contains("Muster here"), "the look-ups: each captured lord, all of them, and the muster in each castle's file");
            string council = Facts.Council(w2);
            Check(council.StartsWith("COUNCIL BRIEFING FOR MISTY MOUNTAIN ORCS") && council.Contains("Your own clan (Azog's clan): gold 15,000, influence 900")
                  && council.Contains("Influence of your other clans") && council.Contains("Mauhur [lord_ldr]: 300") && council.Contains("clan Mauhur's clan, influence 120")
                  && council.Contains("MUSTER AT EACH OF YOUR CASTLES AND TOWNS") && council.Contains("YOUR CAPTURED LORDS (3, by where they are held)"),
                  "the council's briefing: the purse, every lord, the muster at each castle and town, the captured lords");
            File.Delete(Path.Combine(agentDir, "council.txt"));
            AskBriefing(AgentChannel.For("orcs"), agentDir);
            Check(File.Exists(Path.Combine(agentDir, "council.txt")) && Read(Path.Combine(agentDir, "council.txt")).StartsWith("COUNCIL BRIEFING"), "the briefing is written into the channel when the war room asks");
            Runner.Ended(905, "test over");
            Runner.Ended(907, "test over");
        }

        /// <summary>
        /// Version 0.6.1, the weak spots of defend and patrol: a patrol kept within its radius; shelter outside a castle
        /// under a strong siege (inside only when told); refilling at the castle it guards, then in a town or village; a chase broken off when a strong
        /// force comes; the danger rule counting bandits, sparing a siege camp being struck, and never sheltering in the
        /// place the danger marches on; and an army out of sight counted where it was last seen, for six hours.
        /// </summary>
        private static void DefendTests(List<KeyValuePair<int, Move>> moves, List<int> released)
        {
            PartyRow Lord(string id, float x, int men, int limit, float strength) => new PartyRow
            {
                Id = id, Leader = "lord_" + id, Name = id, Faction = "orcs", Kind = "lord", Men = men, SizeLimit = limit, Strength = strength, Speed = 4, FoodDays = 10, X = x, Y = 100, Doing = "Hold", Spottable = true
            };
            Snapshot Besieged(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                SettlementRow c = s.SettlementById["castle_orc"];
                c.BesiegedBy = "player_k";
                c.Besieger = ps.First(p => p.Besieging == "castle_orc").Id;
                c.GarrisonStrength = 600;
                return s;
            }

            // the plain patrol: the game leads it unless a radius is given
            var walker = Lord("p_walk", 130, 200, 250, 260);
            Brain.OnHour(ToolWorld(5000, walker));
            moves.Clear();
            Runner.Start(910, new ToolOrder { Tool = "patrol", Lord = "lord_p_walk", LordName = "Walker", Party = "p_walk", At = "castle_two" });
            bool gameLed = moves.Any(m => m.Key == 910 && m.Value.Verb == "patrol" && m.Value.R == 0);
            var bounded = new ToolOrder { Tool = "patrol", Lord = "lord_p_walk", LordName = "Walker", Party = "p_walk", At = "castle_two" };
            bounded.Settings["radius"] = 15;
            Runner.Start(911, bounded);
            Check(gameLed && moves.Any(m => m.Key == 911 && m.Value.Verb == "patrol" && m.Value.R == 15), "a plain patrol is the game's unless given a radius, then it keeps within it");
            Runner.Ended(911, "test over");

            // a strong siege: shelter in the nearest other castle by default; inside the holding only when told to stay inside
            var sieger = new PartyRow { Id = "p_sg", Leader = "lord_sg", Name = "Elladan", Faction = "player_k", Kind = "lord", Men = 2000, Strength = 3000, Speed = 4, X = 101, Y = 102, Besieging = "castle_orc", Doing = "BesiegeSettlement", Spottable = true };
            var d1 = Lord("p_d1", 115, 300, 320, 400);
            var d2 = Lord("p_d2", 115, 300, 320, 400);
            Brain.OnHour(Besieged(5001, sieger, d1, d2));
            moves.Clear();
            Runner.Start(912, new ToolOrder { Tool = "defend", Lord = "lord_p_d1", LordName = "D1", Party = "p_d1", At = "castle_orc" });
            var inside = new ToolOrder { Tool = "defend", Lord = "lord_p_d2", LordName = "D2", Party = "p_d2", At = "castle_orc" };
            inside.Settings["stay_inside"] = 1;
            Runner.Start(913, inside);
            Check(moves.Any(m => m.Key == 912 && m.Value.Verb == "enter" && m.Value.Place == "castle_two") && moves.Any(m => m.Key == 913 && m.Value.Verb == "enter" && m.Value.Place == "castle_orc"),
                  "under a siege too strong to break, defend shelters in the nearest other castle, and inside the besieged one only with stay_inside 1");
            Runner.Ended(912, "test over");
            Runner.Ended(913, "test over");

            // ranks below the floor (0.13.4, Mert: castles recruit in this pack): first the volunteers of the castle it guards,
            // without calling back; still short two hours after arriving, it rides on to the nearest town or village
            var weak = Lord("p_d3", 120, 50, 300, 70);
            Brain.OnHour(ToolWorld(5002, weak));
            moves.Clear();
            double callBacks = Diag.CountOf("tool:call-backs");
            Runner.Start(914, new ToolOrder { Tool = "defend", Lord = "lord_p_d3", LordName = "D3", Party = "p_d3", At = "castle_two" });
            bool home = moves.Any(m => m.Key == 914 && m.Value.Verb == "recruit" && m.Value.Place == "castle_two");
            bool quiet = Diag.CountOf("tool:call-backs") == callBacks;
            var weakIn = Lord("p_d3", 140, 50, 300, 70);
            weakIn.InSettlement = "castle_two";
            for (int h = 5003; h <= 5004; h++) Brain.OnHour(ToolWorld(h, weakIn));
            bool stillHome = !moves.Any(m => m.Key == 914 && m.Value.Place == "village_orc");
            Brain.OnHour(ToolWorld(5005, weakIn));
            bool onward = moves.Any(m => m.Key == 914 && m.Value.Verb == "recruit" && m.Value.Place == "village_orc") && Diag.CountOf("tool:call-backs") == callBacks + 1;
            Check(home && quiet && stillHome && onward, "a lord whose ranks fall below the floor first takes the volunteers of the castle it guards, without a call-back, and rides on to the nearest town or village only when still short two hours after arriving ("
                  + home + ", " + quiet + ", " + stillHome + ", " + onward + ")");
            Runner.Ended(914, "test over");
            // ... and one that the castle's volunteers bring up to the floor goes back to its patrol, never leaving its area
            var filled = Lord("p_d5", 140, 100, 300, 140);
            filled.InSettlement = "castle_two";
            Brain.OnHour(ToolWorld(5006, filled));
            moves.Clear();
            Runner.Start(944, new ToolOrder { Tool = "defend", Lord = "lord_p_d5", LordName = "D5", Party = "p_d5", At = "castle_two" });
            bool filling = moves.Any(m => m.Key == 944 && m.Value.Verb == "recruit" && m.Value.Place == "castle_two");
            filled = Lord("p_d5", 140, 210, 300, 290);
            filled.InSettlement = "castle_two";
            for (int h = 5007; h <= 5009; h++) Brain.OnHour(ToolWorld(h, filled));
            Check(filling && moves.Any(m => m.Key == 944 && m.Value.Verb == "patrol") && !moves.Any(m => m.Key == 944 && m.Value.Place == "village_orc")
                  && Diag.CountOf("tool:call-backs") == callBacks + 1, "a lord the castle's own volunteers bring up to the floor goes back to its patrol without leaving its area or calling back");
            Runner.Ended(944, "test over");

            // the recruit order tours (0.9.2, Mert: "follows the logic as my own parties"): its move is a tour from its place,
            // a castle may start it, and it no longer stops itself after three days there (the game's side ends it, and says why)
            var tourer = Lord("p_r1", 112, 60, 300, 80);
            Brain.OnHour(ToolWorld(5003, tourer));
            moves.Clear();
            released.Clear();
            Runner.Start(915, new ToolOrder { Tool = "recruit", Lord = "lord_p_r1", LordName = "R1", Party = "p_r1", At = "castle_two" });
            bool toured = moves.Any(m => m.Key == 915 && m.Value.Verb == "tour" && m.Value.Place == "castle_two");
            var stayed4days = Lord("p_r1", 140, 70, 300, 90);
            stayed4days.InSettlement = "castle_two";
            Brain.OnHour(ToolWorld(5003 + 100, stayed4days));
            Check(toured && !released.Contains(915) && Library.Text().Contains("**recruit** (at: your or an ally's holding)") && Library.Text().Contains("until his ranks are at 80%"),
                  "the recruit order tours from its place (a castle too), does not stop itself after three days, and the library says how it works");
            Runner.Ended(915, "test over");

            // a chase broken off when a strong force comes into the area
            var d4 = Lord("p_d4", 104, 300, 320, 400);
            var raider = new PartyRow { Id = "p_rd", Leader = "lord_rd", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 70, Strength = 100, Speed = 5, X = 108, Y = 96, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(5003, d4, raider));
            moves.Clear();
            Runner.Start(915, new ToolOrder { Tool = "defend", Lord = "lord_p_d4", LordName = "D4", Party = "p_d4", At = "castle_orc" });
            bool chased = moves.Any(m => m.Key == 915 && m.Value.Verb == "engage" && m.Value.Party == "p_rd");
            var host = new PartyRow { Id = "p_host2", Leader = "lord_host2", Name = "Imrahil", Faction = "player_k", Kind = "lord", Men = 2000, ArmyMen = 2400, ArmyParties = 3, ArmyStrength = 3000, Strength = 1200, Speed = 4, X = 90, Y = 100, Doing = "GoToPoint", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(5004, d4, raider, host));
            // since 0.12.3 the safety rule sees Imrahil's army too (it could reach D4 within join_hours), and Orc Keep would not hold
            // against it, so D4 falls back toward it rather than going in
            Check(chased && moves.Any(m => m.Key == 915 && (m.Value.Verb == "enter" || m.Value.Verb == "near") && m.Value.Place == "castle_two"),
                  "a chase is broken off, toward the nearest other castle, when a strong force comes into the area");
            Runner.Ended(915, "test over");

            // the danger rule counts bandits: a weak lord shelters in a castle that would hold against them...
            var lone = Lord("p_lone", 92, 40, 200, 60);
            var band = new PartyRow { Id = "p_band", Name = "Sea Raiders", Faction = "looters", Kind = "bandit", Men = 80, Strength = 200, Speed = 5, X = 86, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(5005, lone, band));
            moves.Clear();
            Runner.Start(916, new ToolOrder { Tool = "goto", Lord = "lord_p_lone", LordName = "Lone", Party = "p_lone", At = "castle_two" });
            Check(moves.Any(m => m.Key == 916 && m.Value.Verb == "enter" && m.Value.Place == "castle_orc"), "the danger rule shelters a weak lord from a strong band of bandits, in a castle whose defenders would hold");
            Runner.Ended(916, "test over");
            // ... and runs straight away from them when they stand nearer than it to every castle
            var cut = Lord("p_cut", 60, 40, 200, 60);
            var band2 = new PartyRow { Id = "p_band2", Name = "Sea Raiders", Faction = "looters", Kind = "bandit", Men = 80, Strength = 200, Speed = 5, X = 64, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(5005.5, cut, band2));
            moves.Clear();
            Runner.Start(919, new ToolOrder { Tool = "goto", Lord = "lord_p_cut", LordName = "Cut", Party = "p_cut", At = "castle_two" });
            Check(moves.Any(m => m.Key == 919 && m.Value.Verb == "toward" && m.Value.X < 60), "with no castle it can reach before the enemy, a lord falls back straight away from it");
            Runner.Ended(919, "test over");

            // ... but not from the other lords of a siege camp the order is striking
            var s1 = new PartyRow { Id = "p_s1", Leader = "lord_s1", Name = "Elrohir", Faction = "player_k", Kind = "lord", Men = 400, Strength = 500, Speed = 4, X = 101, Y = 102, Besieging = "castle_orc", Doing = "BesiegeSettlement", Spottable = true };
            var s2 = new PartyRow { Id = "p_s2", Leader = "lord_s2", Name = "Beregond", Faction = "player_k", Kind = "lord", Men = 1100, Strength = 1500, Speed = 4, X = 99, Y = 103, Besieging = "castle_orc", Doing = "BesiegeSettlement", Spottable = true };
            var rel = Lord("p_rel2", 115, 700, 800, 900);
            Brain.OnHour(Besieged(5006, s1, s2, rel));
            moves.Clear();
            var ro = new ToolOrder { Tool = "relieve", Lord = "lord_p_rel2", LordName = "Rel", Party = "p_rel2", At = "castle_orc" };
            ro.Settings["strike_at"] = 0.4;
            Runner.Start(917, ro);
            bool struck = moves.Any(m => m.Key == 917 && m.Value.Verb == "engage" && m.Value.Party == "p_s1");
            rel.X = 106;   // closing on the camp, now within reach of the stronger besieger too
            moves.Clear();
            Brain.OnHour(Besieged(5007, s1, s2, rel));
            Check(struck && !moves.Any(m => m.Key == 917 && m.Value.Verb == "enter"), "the danger rule does not turn a relief back from the siege camp it is striking");
            Runner.Ended(917, "test over");

            // the castle the danger marches to besiege: inside it if its garrison and the lord, behind its walls, would hold it
            // (600 + 300, times 2.4 for level-1 walls, against 2,000)...
            var near = Lord("p_near", 104, 200, 250, 300);
            var marcher = new PartyRow { Id = "p_march", Leader = "lord_march", Name = "Faramir", Faction = "player_k", Kind = "lord", Men = 1500, Strength = 2000, Speed = 4, X = 110, Y = 100, Doing = "BesiegeSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Brain.OnHour(ToolWorld(5008, near, marcher));
            moves.Clear();
            Runner.Start(918, new ToolOrder { Tool = "goto", Lord = "lord_p_near", LordName = "Near", Party = "p_near", At = "village_orc" });
            Check(moves.Any(m => m.Key == 918 && m.Value.Verb == "enter" && m.Value.Place == "castle_orc"), "a lord shelters in the castle the danger marches on when its defenders, with the lord, would hold it");
            Runner.Ended(918, "test over");
            // ... and runs, never toward that castle, when they would not (against 4,000)
            var near2 = Lord("p_near2", 104, 200, 250, 300);
            var marcher2 = new PartyRow { Id = "p_march2", Leader = "lord_march2", Name = "Faramir", Faction = "player_k", Kind = "lord", Men = 3000, Strength = 4000, Speed = 4, X = 110, Y = 100, Doing = "BesiegeSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Brain.OnHour(ToolWorld(5008.5, near2, marcher2));
            moves.Clear();
            Runner.Start(920, new ToolOrder { Tool = "goto", Lord = "lord_p_near2", LordName = "Near2", Party = "p_near2", At = "village_orc" });
            Check(moves.Any(m => m.Key == 920 && m.Value.Verb == "toward" && m.Value.X < 104) && !moves.Any(m => m.Key == 920 && m.Value.Place == "castle_orc"),
                  "and runs, not into that castle, when they would not hold it");
            Runner.Ended(920, "test over");

            // inside a castle that would not hold: a garrison stays (its order), a resting lord leaves for a place out of reach
            var keep = Lord("p_keep", 140, 250, 300, 300);
            keep.InSettlement = "castle_two";
            var rester = Lord("p_rest", 140, 150, 300, 200);
            rester.InSettlement = "castle_two";
            var horde = new PartyRow { Id = "p_horde", Leader = "lord_horde", Name = "Eomer", Faction = "player_k", Kind = "lord", Men = 3000, ArmyMen = 3000, ArmyParties = 4, ArmyStrength = 4000, Strength = 1500, Speed = 4, X = 146, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(5009, keep, rester, horde));
            moves.Clear();
            Runner.Start(921, new ToolOrder { Tool = "garrison", Lord = "lord_p_keep", LordName = "Keep", Party = "p_keep", At = "castle_two" });
            Runner.Start(922, new ToolOrder { Tool = "rest", Lord = "lord_p_rest", LordName = "Rest", Party = "p_rest", At = "castle_two" });
            Check(!moves.Any(m => m.Key == 921 && m.Value.Verb != "enter") && moves.Any(m => m.Key == 922 && m.Value.Verb == "near" && m.Value.Place == "castle_orc"),
                  "inside a castle that would not hold: a garrison stays, a resting lord falls back toward a place out of the enemy's reach");
            Runner.Ended(921, "test over");
            Runner.Ended(922, "test over");

            // an army that walked out of sight is still a threat where it was last seen, for six hours
            var shadow = new PartyRow { Id = "p_shadow", Leader = "lord_shadow", Name = "Eomer", Faction = "player_k", Kind = "lord", Men = 1600, ArmyMen = 1600, ArmyParties = 2, ArmyStrength = 2000, Strength = 1100, Speed = 4, X = 105, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(5010, near, shadow));
            Snapshot later = ToolWorld(5012, near, shadow);
            later.WatchedSees.Remove("p_shadow");
            Snapshot much = ToolWorld(5018, near, shadow);
            much.WatchedSees.Remove("p_shadow");
            double recent = Runner.WorldFor(later).ThreatStrength("castle_orc", 20), stale = Runner.WorldFor(much).ThreatStrength("castle_orc", 20);
            Check(recent == 2000 && stale == 0, "an army out of sight counts where it was last seen for six hours (" + recent + " two hours on, " + stale + " eight hours on)");

            // 0.11.6, Bagbûrz (run 20261024-2110-C2): a slow army told to defend from inside (stay_inside 1, caution 0, radius 8)
            // patrolled outside the walls and went in only once the stronger host was within 8, and was caught 5 from the gate.
            // Now it goes in as soon as a force too strong for it is due before it could ride back from its patrol's edge...
            PartyRow Slow(string id) { PartyRow r = Lord(id, 107, 1000, 1100, 1200); r.Speed = 1.2f; return r; }
            ToolOrder Hold(int id, string party)
            {
                var o = new ToolOrder { Tool = "defend", Lord = "lord_" + party, LordName = party, Party = party, At = "castle_orc" };
                o.Settings["radius"] = 8; o.Settings["shelter_at"] = 1; o.Settings["strike_below"] = 0.7; o.Settings["floor"] = 0; o.Settings["stay_inside"] = 1; o.Settings["caution"] = 0;
                return o;
            }
            var anvil = Slow("p_anvil");
            var hostNear = new PartyRow { Id = "p_hn", Leader = "lord_hn", Name = "Lorgath", Faction = "player_k", Kind = "lord", Men = 1300, Strength = 1700, Speed = 1.2f, X = 91, Y = 100, Doing = "BesiegeSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Brain.OnHour(ToolWorld(5060, anvil, hostNear));
            moves.Clear();
            Runner.Start(942, Hold(942, "p_anvil"));
            bool wentIn = moves.Any(m => m.Key == 942 && m.Value.Verb == "enter" && m.Value.Place == "castle_orc");
            Runner.Ended(942, "test over");
            // ... and not when the host is still far off (due long after it could get back), so it keeps patrolling
            var anvil2 = Slow("p_anvil2");
            var hostFar = new PartyRow { Id = "p_hf", Leader = "lord_hf", Name = "Lorgath", Faction = "player_k", Kind = "lord", Men = 1300, Strength = 1700, Speed = 1.2f, X = 60, Y = 100, Doing = "BesiegeSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Brain.OnHour(ToolWorld(5061, anvil2, hostFar));
            moves.Clear();
            Runner.Start(943, Hold(943, "p_anvil2"));
            bool stayedOut = !moves.Any(m => m.Key == 943 && m.Value.Verb == "enter") && moves.Any(m => m.Key == 943 && m.Value.Verb == "patrol");
            Runner.Ended(943, "test over");
            Check(wentIn && stayedOut, "defend with stay_inside 1 goes inside ahead of a stronger force due before it could ride back from its patrol, even with caution 0, and not while that force is far off");
        }

        /// <summary>
        /// Version 0.6.3: defend refills only when the lord is not needed where it is (never an army's leader, never with an
        /// enemy lord within twice its radius of the holding), breaks off when enemies come, and leaves a place that yields
        /// few recruits in 12 hours, trying again two days later; the report lists every enemy army in view however many
        /// bandits are near, and says what each castle and town would hold against.
        /// </summary>
        private static void RefillAndReportTests(List<KeyValuePair<int, Move>> moves)
        {
            PartyRow Lord(string id, float x, int men, int limit, float strength) => new PartyRow
            {
                Id = id, Leader = "lord_" + id, Name = id, Faction = "orcs", Kind = "lord", Men = men, SizeLimit = limit, Strength = strength, Speed = 4, FoodDays = 10, X = x, Y = 100, Doing = "Hold", Spottable = true
            };
            ToolOrder Defend(int id, string party) => new ToolOrder { Tool = "defend", Lord = "lord_" + party, LordName = party, Party = party, At = "castle_two" };
            string InForce(Snapshot s) => Runner.InForce(Runner.WorldFor(s));

            // an army's leader never leaves to refill, however thin its own ranks
            var lead = Lord("p_lead", 125, 60, 300, 120);
            lead.ArmyLeader = "p_lead";
            lead.ArmyMen = 700;
            lead.ArmyParties = 4;
            lead.ArmyStrength = 900;
            Brain.OnHour(ToolWorld(5020, lead));
            moves.Clear();
            Runner.Start(923, Defend(923, "p_lead"));
            Check(!moves.Any(m => m.Key == 923 && m.Value.Verb == "recruit") && InForce(ToolWorld(5020, lead)).Contains("patrolling"), "an army's leader does not leave to refill, however thin its own ranks");
            Runner.Ended(923, "test over");

            // a lone thin lord stays while an enemy lord is within twice its radius of the holding (32 away, radius 20)...
            var thin = Lord("p_thin", 130, 50, 300, 70);
            var watcher = new PartyRow { Id = "p_wt", Leader = "lord_wt", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 40, Strength = 60, Speed = 5, X = 172, Y = 100, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(5021, thin, watcher));
            moves.Clear();
            Runner.Start(924, Defend(924, "p_thin"));
            bool stayed = !moves.Any(m => m.Key == 924 && m.Value.Verb == "recruit");
            // ... refills once it has gone...
            moves.Clear();
            Brain.OnHour(ToolWorld(5022, thin));
            bool went = moves.Any(m => m.Key == 924 && m.Value.Verb == "recruit" && m.Value.Place == "castle_two");   // the castle it guards first (0.13.4)
            Check(stayed && went, "a lone thin lord does not leave to refill while an enemy lord is near the holding, and goes once it has gone");
            // ... and breaks off when an enemy lord comes near the holding
            var comer = new PartyRow { Id = "p_cm", Leader = "lord_cm", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 40, Strength = 60, Speed = 5, X = 150, Y = 100, Doing = "Hold", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(5023, thin, comer));
            Check(moves.Any(m => m.Key == 924 && m.Value.Verb == "patrol") && !InForce(ToolWorld(5023, thin, comer)).Contains("refilling"), "a lord refilling breaks off when an enemy lord comes near the holding");
            Runner.Ended(924, "test over");

            // a place that yields few recruits in 12 hours: back to defending, and no new try for two days (since 0.13.4 after
            // the castle it guards gave too few: two hours there, then the village)
            var dryHome = Lord("p_dry", 140, 50, 300, 70);
            dryHome.InSettlement = "castle_two";
            Brain.OnHour(ToolWorld(5030, dryHome));
            Runner.Start(925, Defend(925, "p_dry"));
            bool atHome = InForce(ToolWorld(5030, dryHome)).Contains("refilling its ranks at Orc Keep itself");
            for (int h = 5031; h <= 5032; h++) Brain.OnHour(ToolWorld(h, dryHome));
            var dry = Lord("p_dry", 110, 50, 300, 70);
            dry.Y = 104;
            bool refilling = atHome && InForce(ToolWorld(5032, dry)).Contains("refilling its ranks at Orc Village");
            for (int h = 5033; h <= 5044; h++) Brain.OnHour(ToolWorld(h, dry));
            bool stillAt11 = InForce(ToolWorld(5044, dry)).Contains("refilling");
            moves.Clear();
            Brain.OnHour(ToolWorld(5045, dry));
            bool back = moves.Any(m => m.Key == 925 && m.Value.Verb == "patrol");
            for (int h = 5046; h <= 5053; h++) Brain.OnHour(ToolWorld(h, dry));
            bool waits = InForce(ToolWorld(5053, dry)).Contains("patrolling");
            Check(refilling && stillAt11 && back && waits, "a lord leaves a place that yields few recruits after 12 hours there, and does not try again at once ("
                  + refilling + ", " + stillAt11 + ", " + back + ", " + waits + ")");
            Runner.Ended(925, "test over");

            // the report: an enemy army far off is listed though 45 bands of bandits near the holdings would fill the list
            var crowd = new List<PartyRow>();
            for (int i = 0; i < 45; i++)
                crowd.Add(new PartyRow { Id = "p_bd" + i, Name = "Looters", Faction = "looters", Kind = "bandit", Men = 10, Strength = 12, Speed = 4, X = 100 + i * 0.1f, Y = 101, Doing = "Hold", Spottable = true });
            crowd.Add(new PartyRow
            {
                Id = "p_aragorn", Leader = "lord_aragorn", Name = "Aragorn", Faction = "player_k", Kind = "lord", Men = 900, ArmyLeader = "p_aragorn", ArmyMen = 5532, ArmyParties = 15,
                ArmyStrength = 14695, Strength = 2000, Speed = 3, X = 190, Y = 140, Doing = "GoToPoint", Spottable = true
            });
            Snapshot cs = ToolWorld(5060, crowd.ToArray());
            View cv = Views.Build(cs, "orcs", cs.WatchedSees, cs.HostileToWatched, null);
            string report = Views.Report(cv, null);
            Check(cv.Seen.Count == 40 && cv.SeenNotListed == 6 && cv.Seen[0].Id == "p_aragorn" && report.Contains("army of 5,532 men, strength 14,695"),
                  "the report lists an enemy army in view first, whatever the bandits near the holdings (" + cv.Seen.Count + " listed, " + cv.SeenNotListed + " not)");
            // and what each castle would hold against: garrison 600 behind level-1 walls (worth 2.43) holds against up to 1,457
            Check(report.Contains("Orc Castle [castle_orc] (castle): garrison 450 (strength 600), militia 120; walls level 1; defenders' strength 600, so it holds against besiegers up to strength 1,457"),
                  "the report says what each castle or town would hold against");
        }

        /// <summary>
        /// Version 0.6.4: an enemy army's trail. It marches on a castle, turns aside into one of its own villages, comes back
        /// to its road, goes out of sight and comes back: a point for each change of course and for the return, none for a
        /// few units more of the same march; two days later the old points are gone, and a trail ends with its army.
        /// </summary>
        private static void TrailTests()
        {
            PartyRow Army(float x, float y, string target, string inside = null) => new PartyRow
            {
                Id = "p_arw", Leader = "lord_arw", Name = "Aragorn", Faction = "player_k", Kind = "lord", Men = 900, ArmyLeader = "p_arw", ArmyMen = 5532, ArmyParties = 15,
                ArmyStrength = 14695, Strength = 2000, Speed = 3, X = x, Y = y, Doing = "GoToSettlement", TargetSettlement = target, InSettlement = inside, Spottable = true
            };
            Snapshot Unseen(double h, PartyRow a)
            {
                Snapshot s = ToolWorld(h, a);
                s.WatchedSees.Remove(a.Id);
                return s;
            }
            Brain.OnHour(ToolWorld(6000, Army(170, 120, "castle_orc")));
            Brain.OnHour(ToolWorld(6001, Army(166, 118, "castle_orc")));
            Brain.OnHour(ToolWorld(6002, Army(162, 116, "village_men")));
            Brain.OnHour(ToolWorld(6003, Army(160, 110, "village_men", "village_men")));
            Brain.OnHour(ToolWorld(6004, Army(158, 108, "castle_orc")));
            for (int h = 6005; h <= 6007; h++) Brain.OnHour(Unseen(h, Army(150, 106, "castle_orc")));
            Snapshot back = ToolWorld(6008, Army(140, 104, "castle_orc"));
            Brain.OnHour(back);
            Brain.Memory m = Brain.Get("orcs");
            View v = Views.Build(back, "orcs", back.WatchedSees, back.HostileToWatched, m);
            string report = Views.Report(v, null);
            int points = m.Trails.TryGetValue("p_arw", out ViewTrail t) ? t.Points.Count : 0;
            Check(points == 5 && v.Trails.Count == 1 && report.Contains("ENEMY ARMIES' TRAILS (1)") && report.Contains("Aragorn [lord_arw] (Kingdom of Arnor and Gondor), army of 5,532 men, strength 14,695, in view now")
                  && report.Contains("going to Bree -> ") && report.Contains("inside Bree -> ") && report.Contains("(out of sight 4 h) "),
                  "an enemy army's trail: a point for each change of course and for its return into sight, none for a few units more of the same march (" + points + " points)");
            Dictionary<string, string> look = Facts.Lookups(Runner.WorldFor(back), m.LastSeen, m.Trails);
            Check(look.TryGetValue("lord_arw", out string page) && page.Contains("Its trail as your realm saw it") && page.Contains("- (out of sight 4 h) "), "the enemy's look-up carries its trail");
            // two days on, the old points are gone (one new point is no trail to show), and a trail ends with its army
            Snapshot later = ToolWorld(6060, Army(139, 104, "castle_orc"));
            Brain.OnHour(later);
            View lv = Views.Build(later, "orcs", later.WatchedSees, later.HostileToWatched, m);
            bool aged = m.Trails.TryGetValue("p_arw", out ViewTrail t2) && t2.Points.Count == 1 && lv.Trails.Count == 0;
            PartyRow broken = Army(139, 104, "castle_orc");
            broken.ArmyMen = 0;
            broken.ArmyParties = 0;
            broken.ArmyLeader = null;
            Brain.OnHour(ToolWorld(6061, broken));
            Check(aged && !m.Trails.ContainsKey("p_arw"), "a trail keeps two days, and ends when its army is seen broken up");
        }

        /// <summary>
        /// Version 0.7.1, watch and shadow (Mert, Oct 16: a weaker lord sent to keep tabs on the road from Hollin Gate, rather than
        /// screening round Western Moria). Watch: to its post (a spot), takes up the watch, reports an enemy army within its
        /// radius and again when it is out of sight. Shadow: keeps its distance from its target (closing in, stepping back),
        /// reports at once and when the target heads for a new place, goes to where it was last seen when out of sight, and gives up.
        /// </summary>
        private static void WatchShadowTests(string dir, List<KeyValuePair<int, Move>> moves, List<int> released)
        {
            float sx, sy;
            Check(Spots.TryParse("[p125_112]", out sx, out sy) && sx == 125 && sy == 112 && !Spots.Is("lord_MM1_1") && Spots.Id(124.6f, 111.7f) == "p125_112", "a spot is named by where it is: p125_112");
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            var grish = new PartyRow { Id = "p_gri", Leader = "lord_gri", Name = "Grishnakh", Faction = "orcs", Kind = "lord", Men = 80, SizeLimit = 100, Strength = 90, Speed = 6, FoodDays = 10, X = 115, Y = 110, Doing = "Hold", Spottable = true };
            Brain.OnHour(ToolWorld(7000, grish));
            Check(Runner.WorldFor(ToolWorld(7000, grish)).Name("p125_112").StartsWith("the spot ", StringComparison.Ordinal), "a spot reads as \"the spot ... of\" its nearest holding: " + Runner.WorldFor(ToolWorld(7000, grish)).Name("p125_112"));
            moves.Clear();
            Runner.Start(950, new ToolOrder { Tool = "watch", Lord = "lord_gri", LordName = "Grishnakh", Party = "p_gri", At = "p125_112" });
            Check(moves.Any(m => m.Key == 950 && m.Value.Verb == "post" && m.Value.X == 125 && m.Value.Y == 112), "watch sends the lord to its post, the spot");
            grish.X = 125; grish.Y = 112;
            Brain.OnHour(ToolWorld(7001, grish));
            Check(Notices().Contains("Grishnakh has taken up the watch at the spot"), "it says when it has taken up the watch");
            var rider = new PartyRow { Id = "p_rider", Leader = "lord_rider", Name = "Eomer", Faction = "player_k", Kind = "lord", Men = 900, ArmyLeader = "p_rider", ArmyMen = 2400, ArmyParties = 6, ArmyStrength = 3000, Strength = 1100, Speed = 5,
                                       X = 135, Y = 112, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Brain.OnHour(ToolWorld(7002, grish, rider));
            Check(Notices().Contains("Grishnakh, keeping watch at the spot") && Notices().Contains("sees an enemy army: Eomer, 2,400 men, strength 3,000") && Notices().Contains("going to Orc Castle"),
                  "an enemy army within its radius is reported with its strength and what it is doing");
            Brain.OnHour(ToolWorld(7003, grish));
            Check(Notices().Contains("Eomer has gone out of sight; when last seen it was going to Orc Castle"), "and again when it has gone out of sight");
            Check(Runner.InForce(Runner.WorldFor(ToolWorld(7003, grish))).Contains("watch the spot") && Runner.InForce(Runner.WorldFor(ToolWorld(7003, grish))).Contains("[p125_112]"),
                  "the orders in force name the post by the spot and its id");

            // shadow: closes to keep (8) from its target, steps back when it comes nearer, reports, follows its course, searches, gives up
            var lurtz = new PartyRow { Id = "p_lur", Leader = "lord_lur", Name = "Lurtz", Faction = "orcs", Kind = "lord", Men = 90, SizeLimit = 100, Strength = 100, Speed = 6, FoodDays = 10, X = 150, Y = 130, Doing = "Hold", Spottable = true };
            PartyRow Host(float x, string target) => new PartyRow
            {
                Id = "p_hostx", Leader = "lord_hostx", Name = "Imrahil", Faction = "player_k", Kind = "lord", Men = 1000, ArmyLeader = "p_hostx", ArmyMen = 3000, ArmyParties = 8, ArmyStrength = 4000, Strength = 1200, Speed = 4,
                X = x, Y = 130, Doing = "GoToSettlement", TargetSettlement = target, Spottable = true
            };
            Brain.OnHour(ToolWorld(7010, lurtz, Host(170, "castle_orc")));
            moves.Clear();
            Runner.Start(951, new ToolOrder { Tool = "shadow", Lord = "lord_lur", LordName = "Lurtz", Party = "p_lur", Target = "p_hostx" });
            Move first = moves.LastOrDefault(m => m.Key == 951).Value;
            Check(first.Verb == "shadow" && Math.Abs(first.X - 162) < 0.6f && Math.Abs(first.Y - 130) < 0.6f, "shadow closes to 8 from its target, on its own side (" + first + ")");
            Check(Notices().Contains("Lurtz is shadowing Imrahil (3,000 men, strength 4,000): going to Orc Castle"), "and says so at once, with what the target is doing");
            lurtz.X = 156;
            moves.Clear();
            Brain.OnHour(ToolWorld(7011, lurtz, Host(160, "castle_orc")));
            Move back = moves.LastOrDefault(m => m.Key == 951).Value;
            Check(back.Verb == "shadow" && back.X < 150 && back.X > 147, "the target coming nearer, it steps back to 11 (" + back + ")");
            Brain.OnHour(ToolWorld(7012, lurtz, Host(158, "castle_two")));
            Check(Notices().Contains("Lurtz, shadowing Imrahil: it has changed course and is now going to Orc Keep"), "a target heading for a new place is reported");
            moves.Clear();
            Snapshot gone = ToolWorld(7013, lurtz, Host(150, "castle_two"));
            gone.WatchedSees.Remove("p_hostx");
            Brain.OnHour(gone);
            Snapshot gone2 = ToolWorld(7014, lurtz, Host(145, "castle_two"));
            gone2.WatchedSees.Remove("p_hostx");
            Brain.OnHour(gone2);
            Check(Notices().Contains("Lurtz lost sight of Imrahil") && moves.Any(m => m.Key == 951 && m.Value.Verb == "toward"), "out of sight, it goes to where the target was last seen");
            released.Clear();
            for (int h = 7015; h <= 7024; h++)
            {
                Snapshot unseen = ToolWorld(h, lurtz, Host(140, "castle_two"));
                unseen.WatchedSees.Remove("p_hostx");
                Brain.OnHour(unseen);
            }
            Check(released.Contains(951) && Notices().Contains("Lurtz lost track of Imrahil and stopped shadowing it"), "and gives up after lose_hours");
            Runner.Reset();
        }

        /// <summary>
        /// Version 0.7.2 (Mert, Oct 17, after the siege of Hollin Gate, where about 50 of Arnor's lords came to its relief one
        /// by one and the alarm, waiting for an army, never rang): besiege's relief alarm counts every enemy lord and army near
        /// the holding or heading for it, together, and calls again each time they grow by half the besieger's strength. And
        /// the report gives an enemy castle's militia beside its garrison, only where the realm's eyes reach.
        /// </summary>
        private static void SiegeReliefTests(string dir)
        {
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            int Calls() => System.Text.RegularExpressions.Regex.Matches(Notices(), "A relief is coming to Fornost").Count;
            var nag = new PartyRow { Id = "p_nag", Leader = "lord_nag", Name = "Naglur", Faction = "orcs", Kind = "lord", Men = 600, SizeLimit = 700, Strength = 900,
                                     ArmyLeader = "p_nag", ArmyMen = 2000, ArmyParties = 10, ArmyStrength = 3400, Speed = 1.6f, FoodDays = 40, X = 166, Y = 101, Doing = "Hold", Spottable = true };
            PartyRow Lord(string id, string name, float x, float y, float strength, string doing) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = (int)(strength * 0.6f), Strength = strength, Speed = 3.5f,
                X = x, Y = y, Doing = doing, TargetSettlement = doing == "Hold" ? null : "castle_men", Spottable = true
            };
            Snapshot Sieged(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                s.SettlementById["castle_men"].BesiegedBy = "orcs";
                s.SettlementById["castle_men"].Besieger = "p_nag";
                return s;
            }
            Brain.OnHour(ToolWorld(7200, nag));
            var siege = new ToolOrder { Tool = "besiege", Lord = "lord_nag", LordName = "Naglur", Party = "p_nag", At = "castle_men" };
            siege.Settings["relief_ratio"] = 0.8;   // as the agent gave it at Hollin Gate
            Runner.Start(960, siege);
            nag.Besieging = "castle_men";
            nag.Doing = "BesiegeSettlement";
            for (int h = 7201; h <= 7203; h++) Brain.OnHour(Sieged(h, nag));
            var near = Lord("imr", "Imrahil", 174, 100, 900, "Hold");
            var faramir = Lord("far3", "Faramir", 230, 100, 900, "DefendSettlement");
            var idle = Lord("eom2", "Eomer", 250, 160, 900, "Hold");
            Brain.OnHour(Sieged(7204, nag, near, faramir, idle));
            Check(Calls() == 0, "two lords coming to the relief, strength 1,800, under 0.8 of the besieger's 3,400: no call yet (a lord far off and not heading there is not counted)");
            var beregond = Lord("ber2", "Beregond", 200, 100, 1000, "GoToSettlement");
            Brain.OnHour(Sieged(7205, nag, near, faramir, idle, beregond));
            Check(Calls() == 1 && Notices().Contains("A relief is coming to Fornost: 3 enemy lords and armies within 15 of it or heading for it, strength 2,800 together, against your side's 3,400 (Naglur's 3,400"),
                  "a third lord makes 2,800, above 0.8 of its strength: besiege calls back, counting lords, not only armies");
            Check(Notices().Contains("Nearest first: Imrahil, strength 900, 4.0 away") && System.Text.RegularExpressions.Regex.IsMatch(Notices(), @"Faramir, strength 900, 60\.0 away \(due in about \d+ hours\), defending Fornost"),
                  "and names them nearest first, with what each is doing (those heading for it count from any distance)");
            Brain.OnHour(Sieged(7206, nag, near, faramir, idle, beregond));
            Check(Calls() == 1, "the same relief is not called again");
            var elrohir = Lord("elr2", "Elrohir", 210, 104, 1800, "DefendSettlement");
            Brain.OnHour(Sieged(7207, nag, near, faramir, idle, beregond, elrohir));
            Check(Calls() == 2 && Notices().Contains("strength 4,600 together"), "it calls again once the relief has grown by half the besieger's strength (4,600, above 2,800 + 1,700)");
            Runner.Reset();

            // militia beside the garrison, only where the realm's eyes reach
            Snapshot seen = ToolWorld(7210);
            seen.InspectedByWatched.Add("castle_men");
            View vs = Views.Build(seen, "orcs", seen.WatchedSees, seen.HostileToWatched, Brain.Get("orcs"));
            string rs = Views.Report(vs, null);
            Check(rs.Contains("Fornost [castle_men] (castle, Kingdom of Arnor and Gondor): garrison 600, militia 200") && Audit.Fog(vs, rs, seen.WatchedSees, seen.InspectedByWatched).Count == 0,
                  "an enemy castle in sight shows its militia beside its garrison");
            Snapshot unseen = ToolWorld(7211);
            View vu = Views.Build(unseen, "orcs", unseen.WatchedSees, unseen.HostileToWatched, Brain.Get("orcs"));
            string ru = Views.Report(vu, null);
            ViewHolding fu = vu.Frontier.FirstOrDefault(h => h.Id == "castle_men");
            Check(ru.Contains("Fornost [castle_men] (castle, Kingdom of Arnor and Gondor): garrison not in sight") && fu != null && fu.Militia == -1 && Audit.Fog(vu, ru, unseen.WatchedSees, unseen.InspectedByWatched).Count == 0,
                  "out of sight, neither its garrison nor its militia is shown, nor kept in the view");
        }

        /// <summary>
        /// Version 0.7.4, the agent's siege control (Mert, Oct 20: "a tool to dictate siege decisions, starting with the storm time",
        /// and "the agent should set the bar here - what relief force strength level forces a withdrawal, what relief force level
        /// forces an immediate storm ... as it begins the siege ... the logic should run as part of its siege tool"): the storm rule
        /// sent while besieging, the relief bars acted on the hour they are met (storm as soon as the camp may; withdraw into the
        /// nearest castle, stopping without the next order), the siege clock by the rule, a storm begun now, the enemy realms as
        /// their encyclopedia pages show them, and a field battle's odds in the facts.
        /// </summary>
        /// <summary>
        /// 0.9.4, money (Mert, Oct 23: the caps "should be the agent's call", and a battle's loot and ransom weighed): the report's
        /// purse, the battles' takings reckoned and read back at a load, the sell order and its call-back, and the library's text.
        /// </summary>
        private static void MoneyTests(string dir, List<KeyValuePair<int, Move>> moves, List<int> released)
        {
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            PartyRow Lord(string id, float x, int men, int limit) => new PartyRow
            {
                Id = id, Leader = "lord_" + id, Name = id, Faction = "orcs", Kind = "lord", Men = men, SizeLimit = limit, Strength = men * 1.5f, Speed = 4, FoodDays = 10, X = x, Y = 100, Doing = "Hold", Spottable = true
            };
            var capped = Lord("p_m1", 102, 80, 700);
            capped.Gold = 10555; capped.Wages = 354; capped.WageCap = 359;
            var rich = Lord("p_m2", 104, 300, 700);
            rich.Gold = 8000; rich.Wages = 900; rich.WageCap = 2000; rich.CapOurs = true; rich.Loot = 3400; rich.LootPieces = 41; rich.Prisoners = 23; rich.PrisonerLords = 1; rich.Ransom = 1150;
            Snapshot s = ToolWorld(6000, capped, rich);
            s.Clans.Add(new ClanRow { Id = "clan_a", Name = "Clan A", Leader = "lord_p_m1", Gold = 10555, Daily = -1240, Ruler = true });
            string report = Views.Report(Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, null), null);
            Check(report.Contains("YOUR PURSE") && report.Contains("Clan A (yours) 10,555 (-1,240)") && report.Contains("At their wage cap, so they cannot recruit (1): p_m1 [lord_p_m1] wages 354 of 359")
                  && report.Contains("Caps you set: p_m2 [lord_p_m2] 2,000 (wages 900)") && report.Contains("Carrying loot (1 lords, about 3,400 in all)") && report.Contains("about 1,150 in ransom"),
                  "the report's purse: the clans' gold and its day, who is at his wage cap, the caps set, the loot and prisoners carried");
            Check(Takings.Text("orcs").Contains("too few to reckon"), "with no won battle on record, what a battle brings in is not reckoned: " + Takings.Text("orcs"));
            for (int i = 0; i < 3; i++) Takings.Add(new TakingsRow { Hours = 6000 + i, Realm = "orcs", Kind = "field", EnemyFallen = 100, Loot = 2000, Pieces = 30, Prisoners = 40, Ransom = 800, Men = 300 });
            Check(Takings.Text("orcs").Contains("each 100 enemy men fallen left about 2,000 in loot and 40 prisoners, a prisoner worth about 20 in ransom"),
                  "three won battles reckon what each 100 fallen leave: " + Takings.Text("orcs"));
            Files.FlushAll();
            Takings.Reset(Brain.Dir);
            Check(Takings.For("orcs").Battles == 3 && Takings.For("dg").Battles == 0, "the takings are kept in the campaign's folder and read back at a load, each realm its own");

            // the fog check's two false alarms (0.10.4): a lord of the realm riding in its army with loot is named in YOUR PURSE
            // (Oct 23), and a raider of its village that the news named is gone from its sight before the hour's look (Oct 22)
            var host = Lord("p_mh", 150, 300, 700);
            host.ArmyLeader = "p_mh"; host.ArmyMen = 500; host.ArmyParties = 2; host.ArmyStrength = 750;
            var rider = Lord("p_mr", 150, 200, 700);
            rider.ArmyLeader = "p_mh"; rider.Attached = true; rider.Loot = 2500; rider.LootPieces = 20; rider.Gold = 500; host.Gold = 900;
            var raider = new PartyRow { Id = "p_cadda", Leader = "lord_cadda", Name = "Cadda", Faction = "player_k", Kind = "lord", Men = 60, Strength = 90, Speed = 5, FoodDays = 10, X = 400, Y = 400, Doing = "GoToPoint", Spottable = true };
            Snapshot raid = ToolWorld(6100, host, rider, raider);
            raid.WatchedSees.Remove("p_cadda");
            long leaksBefore = Diag.CountOf("fog:leaks"), checkedBefore = Diag.CountOf("fog:reports"), pokedBefore = Brain.PokesWatched;
            Brain.OnNotice(new Notice { Id = 9101, Hours = 6099.8, Kind = "raided", Text = "Orc Village is being raided by Cadda (Kingdom of Arnor and Gondor).", Factions = { "orcs" }, Party = "p_cadda" });
            Brain.OnHour(raid);
            Files.FlushAll();
            string raidPoke = Read(Path.Combine(dir, "pokes_orcs.jsonl")).Split('\n').Where(l => l.Trim().Length > 0).Last();
            Check(Brain.PokesWatched > pokedBefore && raidPoke.Contains("only named in the news") && raidPoke.Contains("lord_cadda") && !raidPoke.Contains("9999 hours"),
                  "a raider the news named but the realm never saw is said to be only named in the news, not seen 9999 hours ago");
            Check(raidPoke.Contains("p_mr [lord_p_mr] about 2,500") && Diag.CountOf("fog:reports") > checkedBefore && Diag.CountOf("fog:leaks") == leaksBefore,
                  "the fog check takes the realm's own lords in its armies and the parties the news named (" + (Diag.CountOf("fog:leaks") - leaksBefore) + " leaks)");

            // the sell order rides into a friendly castle or town, then calls back with what the sale brought, and ends
            var seller = Lord("p_s1", 130, 200, 400);
            Brain.OnHour(ToolWorld(6010, seller));
            moves.Clear();
            released.Clear();
            Runner.Start(991, new ToolOrder { Tool = "sell", Lord = "lord_p_s1", LordName = "S1", Party = "p_s1", At = "castle_two" });
            bool rode = moves.Any(m => m.Key == 991 && m.Value.Verb == "enter" && m.Value.Place == "castle_two");
            var inside = Lord("p_s1", 140, 200, 400);
            inside.InSettlement = "castle_two";
            inside.SaleHours = 6010.5;
            inside.SaleText = "handed over 23 prisoners for 1,150 in ransom at Orc Keep";
            Brain.OnHour(ToolWorld(6011, inside));
            Brain.OnHour(ToolWorld(6012, inside));
            string sold = Notices();
            Check(rode && released.Contains(991) && sold.Contains("p_s1 cashed in: handed over 23 prisoners for 1,150 in ransom at Orc Keep"),
                  "the sell order rides into the castle, calls back with what the sale brought, and lets the lord go (rode " + rode + ", let go " + released.Contains(991)
                  + ", last notice: " + sold.Substring(Math.Max(0, sold.Length - 300)).Replace("\n", " | ") + ")");
            Runner.Ended(991, "test over");
            moves.Clear();
            released.Clear();
            string lib = Library.Text();
            Check(Library.Get("sell")?.At == "friendly_fort" && lib.Contains("never food, horses, pack animals, herds, books or banners") && lib.Contains("**wages** (cap, or target \"none\" or \"game\"; lords)"),
                  "the library has the sell order (loot only, never food or horses) and the wages order");
        }

        /// <summary>
        /// Version 0.12.1 (run 20261025-1524-C2, Mert: "Why are oropherion and gorli simply outside caradhras but not besieging
        /// it?"): a lord sent close to a holding stands by its gate, and the gate of a castle or town is about 3 from its centre
        /// (Caradhras 3.3). The tools measured "within" from the centre, so Górli (4.3 from the gate, 6.8 from the centre) and
        /// Oropherion (4.7 and 7.8) waited 15 days for "within 6" and never besieged, and a watch at a gate over 3 from the centre
        /// never began. Now "within" a holding is the nearer of its centre and gate, and a lord standing still on his way, still
        /// far from his place, is called back once.
        /// </summary>
        private static void ArrivalTests(string dir, List<KeyValuePair<int, Move>> moves)
        {
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            // Fornost laid out as Caradhras: the gate 1.9 west and 2.7 north of the centre
            Snapshot Gated(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                s.SettlementById["castle_men"].Gx = 168.1f;
                s.SettlementById["castle_men"].Gy = 102.7f;
                return s;
            }
            PartyRow Orc(string id, string name, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = 500, SizeLimit = 600, Strength = 800, Speed = 3,
                FoodDays = 40, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            var gorli = Orc("gorl", "Gorli", 150, 106.8f);
            Brain.OnHour(Gated(7400, gorli));
            moves.Clear();
            Runner.Start(1201, new ToolOrder { Tool = "besiege", Lord = "lord_gorl", LordName = "Gorli", Party = "p_gorl", At = "castle_men" });
            Check(moves.Any(m => m.Key == 1201 && m.Value.Verb == "near" && m.Value.Place == "castle_men"), "besiege marches close to Fornost first");
            gorli.X = 169.3f;   // where Gorli stood at Caradhras: 4.3 from the gate, 6.8 from the centre
            Brain.OnHour(Gated(7401, gorli));
            Check(moves.Any(m => m.Key == 1201 && m.Value.Verb == "besiege" && m.Value.Place == "castle_men"),
                  "standing 4.3 from the gate (6.8 from the centre), it has arrived and besieges (it waited 15 days at Caradhras)");
            Runner.Ended(1201, "test over");

            var orop = Orc("orop", "Oropherion", 150, 107.4f);
            Brain.OnHour(Gated(7402, orop));
            Runner.Start(1202, new ToolOrder { Tool = "goto", Lord = "lord_orop", LordName = "Oropherion", Party = "p_orop", At = "castle_men" });
            orop.X = 167.5f;   // 4.7 from the gate, 7.8 from the centre
            Brain.OnHour(Gated(7403, orop));
            Check(Notices().Contains("Oropherion reached Fornost."), "goto close to an enemy castle arrives 4.7 from its gate (7.8 from its centre)");
            Runner.Ended(1202, "test over");

            var scout = Orc("sct", "Ugluk", 150, 102.7f);
            Brain.OnHour(Gated(7404, scout));
            moves.Clear();
            Runner.Start(1203, new ToolOrder { Tool = "watch", Lord = "lord_sct", LordName = "Ugluk", Party = "p_sct", At = "castle_men" });
            Check(moves.Any(m => m.Key == 1203 && m.Value.Verb == "post" && Math.Abs(m.Value.X - 168.1f) < 0.01f), "a watch at a castle posts at its gate");
            scout.X = 168.1f;
            Brain.OnHour(Gated(7405, scout));
            Check(Notices().Contains("Ugluk has taken up the watch at Fornost"), "standing at a gate 3.3 from the centre, the watch begins");
            Runner.Ended(1203, "test over");

            // a march that stops short: told once after 6 hours standing still, far from the place; the order is kept
            string Stuck() => System.Text.RegularExpressions.Regex.Matches(Notices(), "Lugdush has not moved for").Count.ToString();
            var lug = Orc("lug", "Lugdush", 120, 100);
            Brain.OnHour(Gated(7410, lug));
            Runner.Start(1204, new ToolOrder { Tool = "goto", Lord = "lord_lug", LordName = "Lugdush", Party = "p_lug", At = "castle_men" });
            for (int h = 7411; h <= 7416; h++) Brain.OnHour(Gated(h, lug));
            Check(Stuck() == "0", "five hours standing still is not yet a stall");
            Brain.OnHour(Gated(7417, lug));
            Check(Stuck() == "1" && Notices().Contains("Lugdush has not moved for 6 hours while going close to Fornost, still 48.2 from Fornost: he may be stuck on the map"),
                  "a lord standing still for 6 hours, 48 from where he goes, is called back (" + Stuck() + ")");
            for (int h = 7418; h <= 7421; h++) Brain.OnHour(Gated(h, lug));
            Check(Stuck() == "1" && Runner.ForParty("p_lug") != null, "once: the same stand is not told again, and the order is kept");
            lug.X = 130;
            for (int h = 7422; h <= 7424; h++) Brain.OnHour(Gated(h, lug));
            Check(Stuck() == "1", "moving on clears it (3 hours still since: not yet)");
            Runner.Ended(1204, "test over");

            // a lord held up by a battle on his way is not stuck
            var fighter = Orc("ftr", "Mauhur", 120, 100);
            fighter.InBattle = true;
            Brain.OnHour(Gated(7430, fighter));
            Runner.Start(1205, new ToolOrder { Tool = "goto", Lord = "lord_ftr", LordName = "Mauhur", Party = "p_ftr", At = "castle_men" });
            for (int h = 7431; h <= 7440; h++) Brain.OnHour(Gated(h, fighter));
            Check(!Notices().Contains("Mauhur has not moved for"), "ten hours in a battle on the way is not a stall");
            Runner.Ended(1205, "test over");
            moves.Clear();
        }

        /// <summary>
        /// Version 0.12.2 (run 20261025-1609-C2, Mert: "I think its something about our defend order's calculation of an enemy
        /// host's strength - the enemy was dispersed in a couple of different parties"): Alenon (strength 8,505) guarding Torech
        /// Emel struck Narsha (1,751), one of a dozen lords marching to join Thrurg, chased her 33 hours out of his area into
        /// them, and lost his army of 13 lords. Thalion and Girda (run 20261025-1524-C2) were caught by lords that came
        /// together, each too weak alone to make them shelter. Now: danger is counted as a group with the hosts lords march
        /// with; a strike weighs its target's company; a strike keeps to its area and gives up a chase not closing; engage
        /// gives up a chase not closing in 8 hours; a lord leaves shelter only once the area and half as far beyond are clear.
        /// </summary>
        private static void GroupDangerTests(string dir, List<KeyValuePair<int, Move>> moves, List<int> released)
        {
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            PartyRow Orc(string id, string name, float x, float y, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength * 0.6f), SizeLimit = (int)strength, Strength = strength, Speed = 4,
                FoodDays = 40, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            PartyRow Foe(string id, string name, float x, float y, float strength, string follows = null) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = (int)(strength * 0.5f), Strength = strength, Speed = 4,
                X = x, Y = y, Doing = follows != null ? "EscortParty" : "Hold", TargetParty = follows, Spottable = true
            };
            ToolOrder Guard(string id, string name, double radius)
            {
                var o = new ToolOrder { Tool = "defend", Lord = "lord_" + id, LordName = name, Party = "p_" + id, At = "castle_orc" };
                o.Settings["radius"] = radius; o.Settings["strike_below"] = 1; o.Settings["shelter_at"] = 1.5; o.Settings["floor"] = 0;
                return o;
            }

            // 1. a strike picks the enemy whose company is weakest: never one lord out of a gathering host
            var alenon = Orc("alen", "Alenon", 102, 100, 8505);
            var thrurg = Foe("thr", "Thrurg", 135, 100, 900);
            thrurg.ArmyLeader = "p_thr"; thrurg.ArmyMen = 2800; thrurg.ArmyParties = 6; thrurg.ArmyStrength = 5600;
            var narsha = Foe("nar", "Narsha", 124, 100, 1751, "p_thr");
            var rukmash = Foe("ruk", "Rukmash", 127, 104, 1477, "p_thr");
            var gralzuk = Foe("gra", "Gralzuk", 128, 97, 906, "p_thr");
            Brain.OnHour(ToolWorld(7600, alenon, thrurg, narsha, rukmash, gralzuk));
            moves.Clear();
            Runner.Start(1301, Guard("alen", "Alenon", 30));
            Brain.OnHour(ToolWorld(7601, alenon, thrurg, narsha, rukmash, gralzuk));
            World w1 = Runner.WorldFor(ToolWorld(7601, alenon, thrurg, narsha, rukmash, gralzuk));
            Check(Math.Abs(w1.CompanyStrength("p_gra", 8) - 9734) < 1 && !moves.Any(m => m.Key == 1301 && m.Value.Verb == "engage"),
                  "Gralzuk (906) alone is weak, but with the host she marches to join (Thrurg's army and two more lords) she is 9,734 against Alenon's 8,505: no strike (" + w1.CompanyStrength("p_gra", 8) + ")");
            var drub = Foe("drb", "Drubnash", 80, 115, 651);
            drub.Doing = "RaidSettlement";
            Brain.OnHour(ToolWorld(7602, alenon, thrurg, narsha, rukmash, gralzuk, drub));
            Check(moves.Any(m => m.Key == 1301 && m.Value.Verb == "engage" && m.Value.Party == "p_drb"), "a raider on the far side, more than 8 hours from the host (651), is struck instead");
            Runner.Ended(1301, "test over");

            // 0.12.3: the agent's hours window decides who counts: with join_hours 0 each lord stands alone, so the weakest is struck
            Check(Math.Abs(w1.CompanyStrength("p_gra", 0) - 906) < 1, "with a window of 0 hours Gralzuk stands alone (906)");
            var alenon2 = Orc("aln2", "Alenon II", 102, 100, 8505);
            Brain.OnHour(ToolWorld(7603, alenon2, thrurg, narsha, rukmash, gralzuk));
            moves.Clear();
            ToolOrder narrow = Guard("aln2", "Alenon II", 30);
            narrow.Settings["join_hours"] = 0;
            Runner.Start(1308, narrow);
            Check(moves.Any(m => m.Key == 1308 && m.Value.Verb == "engage" && m.Value.Party == "p_gra"), "the same guard given join_hours 0 strikes Gralzuk, the weakest alone: the window is the agent's to set");
            Runner.Ended(1308, "test over");

            // 0.12.3: the report lists the lords marching to join an army under it, with how far each is and the hours to it
            Snapshot rs = ToolWorld(7604, alenon, thrurg, narsha, rukmash, gralzuk);
            string report = Views.Report(Views.Build(rs, "orcs", rs.WatchedSees, rs.HostileToWatched, Brain.Get("orcs")), null);
            Check(report.Contains("Marching to join it (3 lords, strength 4,134 together), soonest first:") && System.Text.RegularExpressions.Regex.IsMatch(report, @"- Narsha \[lord_nar\]: 875 men, strength 1,751, 11\.0 from it, about \d+ hours? at its pace")
                  && System.Text.RegularExpressions.Regex.IsMatch(report, @"following Thrurg, 11\.0 from it \(about \d+ hours?\)"),
                  "the report lists under Thrurg's army the three lords marching to join it, each with its distance and hours: " + report.Substring(Math.Max(0, report.IndexOf("Thrurg", StringComparison.Ordinal)), 300).Replace("\n", " | "));
            Snapshot hidden = ToolWorld(7605, alenon, thrurg, narsha, rukmash, gralzuk);
            hidden.WatchedSees.Remove("p_thr");
            string report2 = Views.Report(Views.Build(hidden, "orcs", hidden.WatchedSees, hidden.HostileToWatched, Brain.Get("orcs")), null);
            Check(!report2.Contains("Marching to join it") && !report2.Contains("from it (about"), "toward an army the realm cannot see, no distance is given (the fog rule)");

            // 2. the safety rule counts the enemies near the lord together: three lords, none strong enough alone
            var thalion = Orc("thal", "Thalion", 150, 130, 600);
            var hrak = Foe("hrk", "Hrakgar", 151, 130, 300);
            var grisha = Foe("gsh", "Grisha", 155, 133, 500);
            var muzgur = Foe("mzg", "Muzgur", 153, 126, 480);
            Brain.OnHour(ToolWorld(7610, thalion, hrak));
            Runner.Start(1302, new ToolOrder { Tool = "engage", Lord = "lord_thal", LordName = "Thalion", Party = "p_thal", Target = "p_hrk" });
            Brain.OnHour(ToolWorld(7611, thalion, hrak, grisha, muzgur));
            Check(System.Text.RegularExpressions.Regex.IsMatch(Notices(), @"Thalion is (sheltering in|falling back)[^""]*Grisha and 1 more with it \(strength 980 together against 600\)"),
                  "Grisha (500) and Muzgur (480) near Thalion (600), each under 1.5 times him, are 980 together: he shelters or falls back from both");
            Runner.Ended(1302, "test over");

            // 3. ... and the host of the lord he is told to attack: Hrakgar marching to join an army 15 away
            var thal2 = Orc("thl2", "Thalion II", 150, 160, 600);
            var hrak2 = Foe("hk2", "Hrakgar II", 152, 160, 300, "p_ar2");
            var host2 = Foe("ar2", "Mauhur", 166, 165, 400);
            host2.ArmyLeader = "p_ar2"; host2.ArmyMen = 700; host2.ArmyParties = 3; host2.ArmyStrength = 1200;
            Brain.OnHour(ToolWorld(7620, thal2, hrak2, host2));
            Runner.Start(1303, new ToolOrder { Tool = "engage", Lord = "lord_thl2", LordName = "Thalion II", Party = "p_thl2", Target = "p_hk2" });
            Brain.OnHour(ToolWorld(7621, thal2, hrak2, host2));
            Check(System.Text.RegularExpressions.Regex.IsMatch(Notices(), @"Thalion II is (sheltering in|falling back)[^""]*Mauhur \(strength 1,200 against 600\)"),
                  "his target is spared, but the army it marches to join (1,200, 15 away) is a danger: he does not ride into it");
            Runner.Ended(1303, "test over");

            // 4. a guard's strike keeps to its area: the chase stops at its edge
            var g2 = Orc("gd2", "Faldira", 100, 100, 1000);
            var lone = Foe("lon", "Shagrat", 115, 100, 300);
            Brain.OnHour(ToolWorld(7630, g2, lone));
            moves.Clear();
            Runner.Start(1304, Guard("gd2", "Faldira", 20));
            Check(moves.Any(m => m.Key == 1304 && m.Value.Verb == "engage" && m.Value.Party == "p_lon"), "a guard strikes a lone weak lord in its area");
            g2.X = 121; lone.X = 118;
            Brain.OnHour(ToolWorld(7631, g2, lone));
            Check(Notices().Contains("Faldira broke off the chase of Shagrat: it would have led Faldira out of the area it guards (21 from Orc Castle)") && moves.Last(m => m.Key == 1304).Value.Verb == "patrol",
                  "21 from its castle with an area of 20, it breaks off and patrols again");
            g2.X = 112;
            for (int h = 7632; h <= 7636; h++) Brain.OnHour(ToolWorld(h, g2, lone));
            Check(moves.Last(m => m.Key == 1304).Value.Verb == "patrol", "back in its area, it leaves Shagrat be for 6 hours (before 0.12.2 a broken-off strike began again the same hour)");
            Brain.OnHour(ToolWorld(7637, g2, lone));
            Check(moves.Last(m => m.Key == 1304).Value.Verb == "engage", "and strikes him again after them");
            Runner.Ended(1304, "test over");

            // 5. ... and gives up a chase that has not closed in for 6 hours
            var g3 = Orc("gd3", "Lek", 100, 120, 1000);
            var runner = Foe("rnr", "Gorbag", 110, 120, 300);
            Brain.OnHour(ToolWorld(7640, g3, runner));
            moves.Clear();
            Runner.Start(1305, Guard("gd3", "Lek", 30));
            for (int i = 1; i <= 5; i++) { g3.X = 100 + i; runner.X = 110 + i; Brain.OnHour(ToolWorld(7640 + i, g3, runner)); }
            Check(!Notices().Contains("Lek gave up the chase"), "five hours at the same gap: still chasing");
            g3.X = 106; runner.X = 116;
            Brain.OnHour(ToolWorld(7646, g3, runner));
            Check(Notices().Contains("Lek gave up the chase of Gorbag: no closer in 6 hours (10 away). Back to patrolling around Orc Castle; it leaves Gorbag be for 6 hours."), "six hours no closer: it gives up and patrols again");
            Runner.Ended(1305, "test over");

            // 6. engage gives up a chase that has not closed in for 8 hours (Thalion trailed Hrakgar a mile behind for 40), then its next order starts
            var chaser = Orc("chs", "Nili", 50, 150, 1000);
            var quarry = Foe("qry", "Ufthak", 51.2f, 150, 300);
            released.Clear();
            Brain.OnHour(ToolWorld(7700, chaser, quarry));
            Runner.Start(1306, new ToolOrder { Tool = "engage", Lord = "lord_chs", LordName = "Nili", Party = "p_chs", Target = "p_qry" });
            for (int i = 1; i <= 7; i++) { chaser.X = 50 + i; quarry.X = 51.2f + i; Brain.OnHour(ToolWorld(7700 + i, chaser, quarry)); }
            Check(!released.Contains(1306), "seven hours a mile behind: still chasing");
            chaser.X = 58; quarry.X = 59.2f;
            Brain.OnHour(ToolWorld(7708, chaser, quarry));
            Check(released.Contains(1306) && Notices().Contains("Nili gave up the chase of Ufthak: no closer in 8 hours (1.2 away)"), "eight hours no closer: engage gives up and the lord is let go");

            // 7. a guard comes out of shelter only once its area and half as far beyond are clear
            var g4 = Orc("gd4", "Malthorn", 100, 100, 1000);
            var big = Foe("big", "Lugbur", 112, 100, 2000);
            Brain.OnHour(ToolWorld(7720, g4, big));
            moves.Clear();
            Runner.Start(1307, Guard("gd4", "Malthorn", 20));
            Check(moves.Any(m => m.Key == 1307 && m.Value.Verb == "enter"), "a force twice its strength in its area: it shelters");
            string refuge = moves.Last(m => m.Key == 1307 && m.Value.Verb == "enter").Value.Place;
            g4.InSettlement = refuge;
            big.X = 127;   // 27 off: outside its area of 20, inside 30
            for (int h = 7721; h <= 7725; h++) Brain.OnHour(ToolWorld(h, g4, big));
            Check(!moves.Any(m => m.Key == 1307 && m.Value.Verb == "patrol"), "the force parked 27 off, just outside its area: it stays inside");
            big.X = 140;
            Brain.OnHour(ToolWorld(7726, g4, big));
            Check(moves.Any(m => m.Key == 1307 && m.Value.Verb == "patrol"), "40 off: it comes out and patrols again");
            Runner.Ended(1307, "test over");

            // 0.12.4 (Mert: "Why is the strength calculator not taking into account the strength of the other lords around him?"):
            // four lords camped in one siege, each under his own order, weigh a relief against all of them, not each alone
            Snapshot Camp(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                s.SettlementById["castle_men"].BesiegedBy = "orcs";
                s.SettlementById["castle_men"].Besieger = "p_cmp1";
                s.Sieges.Add(new SiegeRow { Settlement = "castle_men", Besieger = "p_cmp1", BesiegerFaction = "orcs", Strategy = "siege_strategy_prepare_assault", StartHours = 7800, WallLevel = 1,
                                            Prep = 0.2f, PrepRate = 0.04f, Advantage = 5f, Defenders = 600, Attackers = 6000, AttackerMen = 4000, DefenderMen = 450, AttackerMorale = 70, GarrisonPower = 400 });
                return s;
            }
            PartyRow Camped(string id, string name, float y)
            {
                PartyRow r = Orc(id, name, 166, y, 1500);
                r.Besieging = "castle_men";
                r.Doing = "BesiegeSettlement";
                return r;
            }
            PartyRow Coming(string id, string name, float x, float strength)
            {
                PartyRow r = Foe(id, name, x, 100, strength);
                r.Doing = "DefendSettlement";
                r.TargetSettlement = "castle_men";
                return r;
            }
            var camp = new[] { Camped("cmp1", "Imak", 101), Camped("cmp2", "Tali", 102), Camped("cmp3", "Thalion", 99), Camped("cmp4", "Gorli", 98) };
            Brain.OnHour(Camp(7800, camp));
            for (int i = 0; i < camp.Length; i++)
            {
                var o = new ToolOrder { Tool = "besiege", Lord = "lord_" + camp[i].Id.Substring(2), LordName = camp[i].Name, Party = camp[i].Id, At = "castle_men" };
                o.Settings["withdraw_on_relief"] = 1.2; o.Settings["relief_hours"] = 24; o.Settings["relief_ratio"] = 0.5;
                Runner.Start(1401 + i, o);
            }
            var relief = new[] { Coming("rl1", "Khamul", 195, 1500), Coming("rl2", "Lurgha", 200, 1300), Coming("rl3", "Hlakum", 205, 1355) };
            Brain.OnHour(Camp(7801, camp.Concat(relief).ToArray()));
            Brain.OnHour(Camp(7802, camp.Concat(relief).ToArray()));
            Check(!new[] { "Imak", "Tali", "Thalion", "Gorli" }.Any(n => Notices().Contains(n + " lifts the siege")) && Notices().Contains("against your side's 6,000 (Imak's 1,500 with your lords camped there"),
                  "a relief of 4,155 against four lords camped in the siege (6,000 together, each 1,500): nobody lifts the siege");
            for (int i = 0; i < camp.Length; i++) Runner.Ended(1401 + i, "test over");
            var single = Camped("cmp5", "Valkava", 101);
            Brain.OnHour(Camp(7803, single));
            var lo = new ToolOrder { Tool = "besiege", Lord = "lord_cmp5", LordName = "Valkava", Party = "p_cmp5", At = "castle_men" };
            lo.Settings["withdraw_on_relief"] = 1.2; lo.Settings["relief_hours"] = 24;
            Runner.Start(1405, lo);
            Brain.OnHour(Camp(7804, new[] { single }.Concat(relief).ToArray()));
            Check(Notices().Contains("Valkava lifts the siege of Fornost by your bar"), "the same relief against one lord alone (1,500): he lifts the siege");
            Runner.Ended(1405, "test over");

            // two lords ordered at the same target weigh a danger together; a third nearby on his own errand does not count
            var e1 = Orc("st1", "Galyk", 100, 200, 600);
            var e2 = Orc("st2", "Forim", 104, 200, 600);
            var e3 = Orc("st3", "Tyaska", 98, 203, 900);
            var quarry2 = Foe("qr2", "Drubog", 103, 204, 200);
            var band1 = Foe("bd1", "Ushka", 106, 205, 700);
            var band2 = Foe("bd2", "Kargoth", 101, 196, 500);
            Brain.OnHour(ToolWorld(7810, e1, e2, e3, quarry2));
            Runner.Start(1406, new ToolOrder { Tool = "engage", Lord = "lord_st1", LordName = "Galyk", Party = "p_st1", Target = "p_qr2" });
            Runner.Start(1407, new ToolOrder { Tool = "engage", Lord = "lord_st2", LordName = "Forim", Party = "p_st2", Target = "p_qr2" });
            Runner.Start(1408, new ToolOrder { Tool = "goto", Lord = "lord_st3", LordName = "Tyaska", Party = "p_st3", At = "castle_two" });
            Brain.OnHour(ToolWorld(7811, e1, e2, e3, quarry2, band1, band2));
            Check(!System.Text.RegularExpressions.Regex.IsMatch(Notices(), @"Galyk is (sheltering|falling back)") && !System.Text.RegularExpressions.Regex.IsMatch(Notices(), @"Forim is (sheltering|falling back)"),
                  "Ushka and Kargoth (1,200) against Galyk and Forim, both ordered at Drubog (1,200 together): neither shelters, though each alone (600) would");
            Runner.Ended(1406, "test over"); Runner.Ended(1407, "test over"); Runner.Ended(1408, "test over");
            moves.Clear();
            released.Clear();
        }

        private static void SiegeControlTests(string dir, List<KeyValuePair<int, Move>> moves, List<int> released, List<KeyValuePair<int, JObject>> thens)
        {
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            var rules = new Dictionary<string, StormRule>();
            Action<string, StormRule> realSink = Runner.StormSink;
            Runner.StormSink = (party, r) => { if (party == null) rules.Clear(); else if (r == null) rules.Remove(party); else rules[party] = r; };
            var nag = new PartyRow { Id = "p_nag2", Leader = "lord_nag2", Name = "Grishnakh", Faction = "orcs", Kind = "lord", Men = 600, SizeLimit = 700, Strength = 900, Morale = 70, Tactics = 80,
                                     ArmyLeader = "p_nag2", ArmyMen = 2000, ArmyParties = 10, ArmyStrength = 3400, Speed = 1.6f, FoodDays = 40, X = 166, Y = 101, Doing = "Hold", Spottable = true };
            SiegeRow Camp() => new SiegeRow { Settlement = "castle_men", Besieger = "p_nag2", BesiegerFaction = "orcs", Strategy = "siege_strategy_prepare_assault", StartHours = 7300, WallLevel = 1,
                                               Prep = 0.5f, PrepRate = 0.04f, Advantage = 5f, Defenders = 600, Attackers = 3400, AttackerMen = 2000, DefenderMen = 450, AttackerMorale = 70, GarrisonPower = 400 };
            PartyRow Relief(string id, string name, float x, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = (int)(strength * 0.6f), Strength = strength, Speed = 3.5f,
                X = x, Y = 100, Doing = "DefendSettlement", TargetSettlement = "castle_men", Spottable = true
            };
            Snapshot Sieged(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                s.SettlementById["castle_men"].BesiegedBy = "orcs";
                s.SettlementById["castle_men"].Besieger = "p_nag2";
                s.Sieges.Add(Camp());
                return s;
            }
            Brain.OnHour(ToolWorld(7300, nag));
            var order = new ToolOrder { Tool = "besiege", Lord = "lord_nag2", LordName = "Grishnakh", Party = "p_nag2", At = "castle_men", Then = JObject.Parse("{\"do\":\"besiege\",\"at\":\"castle_two\"}") };
            order.Settings["storm"] = 1; order.Settings["storm_engines"] = 2; order.Settings["storm_on_relief"] = 0.5; order.Settings["withdraw_on_relief"] = 1.2; order.Settings["relief_hours"] = 24;
            Runner.Start(970, order);
            nag.Besieging = "castle_men";
            nag.Doing = "BesiegeSettlement";
            for (int h = 7301; h <= 7303; h++) Brain.OnHour(Sieged(h, nag));
            Check(rules.TryGetValue("p_nag2", out StormRule r0) && r0.Mode == 1 && r0.Engines == 2 && !r0.Now, "while it besieges, the order's storm rule goes to the game's storm hook: storm once ready with 2 engines built");
            // a relief of 2,000 due within the day: half its strength, the storm bar: storm as soon as the camp may
            Brain.OnHour(Sieged(7304, nag, Relief("rel1", "Hirgon", 200, 2000)));
            Check(rules.TryGetValue("p_nag2", out StormRule r1) && r1.Now && Notices().Contains("Grishnakh storms Fornost as soon as the camp may, by your bar")
                  && Notices().Contains("The camp's preparations are done in about 12 hours"),
                  "a relief due within the day at half its strength meets the storm bar: it storms as soon as the camp may, and says when that is");
            // a relief of 5,000: over 1.2 times its strength, the withdrawal bar: it lifts the siege and marches into the nearest castle of its own
            Brain.OnHour(Sieged(7305, nag, Relief("rel1", "Hirgon", 195, 2000), Relief("rel2", "Beregond", 205, 3000)));
            Check(!rules.ContainsKey("p_nag2") && moves.Any(m => m.Key == 970 && m.Value.Verb == "enter" && m.Value.Place == "castle_two") && Notices().Contains("Grishnakh lifts the siege of Fornost by your bar"),
                  "a relief over 1.2 times its strength meets the withdrawal bar: the storm rule is cleared and it marches into Orc Keep");
            nag.Besieging = null; nag.Doing = "GoToSettlement"; nag.InSettlement = "castle_two"; nag.X = 140; nag.Y = 100;
            Brain.OnHour(ToolWorld(7306, nag));
            Check(Notices().Contains("Grishnakh is inside Orc Keep, out of the relief's way. Its next order does not start") && !released.Contains(970) && !thens.Any(t => t.Key == 970),
                  "inside, the order stops: the lord is held there, and its next order (another siege) does not start");
            Runner.Reset();
            Runner.StormSink = realSink;

            // the siege clock by the agent's own rule: storm once the first engine is built, or never
            double k = Math.Sqrt(2000) / 24.0;
            var g = new SiegeRow { Settlement = "castle_orc", Strategy = "siege_strategy_prepare_assault", StartHours = 24 * 400, WallLevel = 1, Prep = 0, PrepRate = (float)(k / 48), Advantage = 5f, Defenders = 450, Attackers = 2600 };
            g.Rates["ram"] = (float)(k / 12); g.Rates["siege_tower_level1"] = (float)(k / 16); g.Rates["ballista"] = (float)(k / 8); g.Rates["onager"] = (float)(k / 20);
            Forecast.SiegeClock c1 = Forecast.Siege(g, g.StartHours, new StormRule { Mode = 1, Engines = 1 });
            Forecast.SiegeClock c2 = Forecast.Siege(g, g.StartHours, new StormRule { Mode = 2 });
            Check(Math.Abs(c1.RuleStormAt - g.StartHours - 32.2) < 0.5 && double.IsNaN(c2.RuleStormAt) && c2.Within240 == 0,
                  "by the rule \"storm once one engine is built\" the camp storms when its ram is done (" + (c1.RuleStormAt - g.StartHours).ToString("0.0") + " hours); by \"do not storm\", never");
            Snapshot s3 = ToolWorld(7310);
            string t2 = Forecast.SiegeText(s3, g, c2, false);
            Check(t2.Contains("Your storm rule: do not storm; by it the camp storms not within 10 days"), "the siege line says the rule and what it means: " + t2);
            // 0.11.3: a siege seen only by its camp tells nothing of what is inside the walls (run C1: a caravan's sight showed Khand's defenders)
            string t3 = Forecast.SiegeText(s3, g, Forecast.Siege(g, g.StartHours, null), false, null, known: false);
            Check(t3.Contains("cannot be seen from here") && !t3.Contains("against defenders") && !t3.Contains("Food") && !t3.Contains("A storm could come"),
                  "a siege seen only by its camp shows the besiegers, not the defenders, the food or when a storm comes: " + t3);

            // a storm begun now: 2,000 besiegers against 194 defenders, as at Hollin Gate, takes days by the game's rules
            var hg = new SiegeRow { Settlement = "castle_orc", StartHours = 7300, WallLevel = 1, Prep = 1, Advantage = 3.55f, Defenders = 310, Attackers = 3456, AttackerMen = 2086, DefenderMen = 194, AttackerMorale = 60 };
            string now = Forecast.StormNow(s3, hg, true, new List<Tuple<bool, int, double>>());
            string relieved = Forecast.StormNow(s3, hg, true, new List<Tuple<bool, int, double>> { Tuple.Create(false, 2000, 7310.0 + 6), Tuple.Create(false, 2000, 7310.0 + 12) });
            Check(now != null && now.StartsWith("A storm begun now would last about") && now.Contains("the besiegers would take it") && relieved != null && relieved.Contains("the defenders would hold") && relieved.Contains("counting the 2 lords seen marching to it"),
                  "a storm begun now: 2,086 against 194 takes days, and with relief marching in the defenders hold: " + now + " | " + relieved);

            // the enemy realms, as their encyclopedia pages show them, beside what is in sight
            var foe = new PartyRow { Id = "p_foe", Leader = "lord_foe", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 300, Strength = 450, X = 150, Y = 100, Doing = "Hold", Spottable = true };
            var hidden = new PartyRow { Id = "p_far9", Leader = "lord_far9", Name = "Imrahil", Faction = "player_k", Kind = "lord", Men = 900, Strength = 1500, X = 380, Y = 300, Doing = "Hold", Spottable = true };
            var mine2 = new PartyRow { Id = "p_ug2", Leader = "lord_ug2", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 500, Strength = 650, Morale = 60, Speed = 3.5f, X = 140, Y = 102, Doing = "Hold", Spottable = true };
            Snapshot s4 = ToolWorld(7320, foe, hidden, mine2);
            s4.WatchedSees.Remove("p_far9");
            s4.Factions["player_k"].Lords = 2;
            View v4 = Views.Build(s4, "orcs", s4.WatchedSees, s4.HostileToWatched, Brain.Get("orcs"));
            string r4 = Views.Report(v4, null);
            Check(r4.Contains("ENEMY REALMS, AS THEIR ENCYCLOPEDIA PAGES SHOW ANY RULER") && r4.Contains("Kingdom of Arnor and Gondor: 2 lords; their parties' strength 1,950 in all; in your sight now 1 party, strength 450"),
                  "the enemy realm's strength as its encyclopedia page shows it, beside the part of it in sight");
            Check(Audit.Fog(v4, r4, s4.WatchedSees, s4.InspectedByWatched).Count == 0, "and the report passes the fog check");
            // a field battle's odds in the facts, for an enemy the poke is about
            string facts = Facts.ForPoke(Runner.WorldFor(s4), new List<Trigger> { new Trigger { Kind = "enemy_in_view", Party = "p_foe", Text = "Halbarad in view" } });
            Check(facts.Contains("If they fought in the field, by the game's battle rules") && facts.Contains("Ugluk alone (500 men) would beat it"),
                  "the facts give a fight with the enemy by the game's battle rules: " + facts.Replace("\n", " | "));
            // 0.11.6: and what the loser keeps of what it brought
            Check(System.Text.RegularExpressions.Regex.IsMatch(facts, @"still standing and (none|[\d,]+) of its [\d,]+ left"), "the fight in the facts says what the beaten enemy has left of its men");
        }

        /// <summary>
        /// Version 0.7.5, what the first evening on 0.7.4 taught (Oct 20, the save besieging Western Moria): a relief lord walking
        /// to a waypoint is not "due in 3 hours" at the castle; the withdrawal bar waits while the lord is in a storm (Naglur was
        /// told the siege was lifted six hours into one); a lord standing off is not a joiner; a storm under way is run ahead at
        /// the rates measured in it (Hollin Gate: the rules alone had the orcs breaking, they were winning); a raid with nobody
        /// left to defend runs down the village's health; an ally's siege far off is not in the realm's sight.
        /// </summary>
        private static void FieldLessonsTests(string dir, List<KeyValuePair<int, Move>> moves)
        {
            string Notices() { Files.FlushAll(); return Read(Path.Combine(dir, "notices.jsonl")); }
            // a relief lord walking to a waypoint 8 ahead, 60 from the castle
            var waypoint = new PartyRow { Id = "p_sved", Leader = "lord_sved", Name = "Svedorn", Faction = "player_k", Kind = "lord", Men = 300, Strength = 484, Speed = 3.2f,
                                          X = 170, Y = 160, Tx = 170, Ty = 152, Doing = "DefendSettlement", TargetSettlement = "castle_men", Spottable = true };
            var walking = new PartyRow { Id = "p_nath", Leader = "lord_nath", Name = "Nath", Faction = "player_k", Kind = "lord", Men = 500, Strength = 774, Speed = 3.2f,
                                         X = 170, Y = 130, Tx = 170, Ty = 100, Doing = "DefendSettlement", TargetSettlement = "castle_men", Spottable = true };
            Snapshot sw = ToolWorld(8000, waypoint, walking);
            List<World.Comer> comers = Runner.WorldFor(sw).ReliefComing("castle_men", 15);
            World.Comer cw = comers.FirstOrDefault(c => c.Id == "p_sved"), cn = comers.FirstOrDefault(c => c.Id == "p_nath");
            Check(cw != null && cw.Eta > 15 && cn != null && cn.Eta > 8 && cn.Eta < 16,
                  "a relief lord walking to a waypoint 8 ahead is due at the castle by road at its pace (" + (cw == null ? "none" : cw.Eta.ToString("0.0")) + " hours for 60), one walking there along its way by its way ("
                  + (cn == null ? "none" : cn.Eta.ToString("0.0")) + " for 30)");
            Forecast.Arrival ta = Forecast.ToHolding(sw, walking, sw.SettlementById["castle_men"]);
            Forecast.Arrival tw = Forecast.ToHolding(sw, waypoint, sw.SettlementById["castle_men"]);
            Check(ta != null && ta.Way && Forecast.ArrivalText(ta).Contains("to go on its way") && tw != null && !tw.Way && Forecast.ArrivalText(tw).Contains("by road at its pace"),
                  "the arrival says how it is reckoned and what is left to go: " + Forecast.ArrivalText(ta) + " | " + Forecast.ArrivalText(tw));

            // the withdrawal bar waits while the lord is in a storm, and acts once it is over
            var nag = new PartyRow { Id = "p_nag3", Leader = "lord_nag3", Name = "Naglur", Faction = "orcs", Kind = "lord", Men = 600, SizeLimit = 700, Strength = 900, Morale = 100,
                                     ArmyLeader = "p_nag3", ArmyMen = 2000, ArmyParties = 10, ArmyStrength = 3400, Speed = 1.6f, FoodDays = 40, X = 166, Y = 101, Doing = "Hold", Spottable = true };
            PartyRow Coming(string id, float x, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = id, Faction = "player_k", Kind = "lord", Men = (int)(strength * 0.6f), Strength = strength, Speed = 3.5f,
                X = x, Y = 100, Tx = 170, Ty = 100, Doing = "DefendSettlement", TargetSettlement = "castle_men", Spottable = true
            };
            Snapshot Sieged(double h, bool storming, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                s.SettlementById["castle_men"].BesiegedBy = "orcs";
                s.SettlementById["castle_men"].Besieger = "p_nag3";
                s.Sieges.Add(new SiegeRow { Settlement = "castle_men", Besieger = "p_nag3", BesiegerFaction = "orcs", Strategy = "siege_strategy_prepare_assault", StartHours = 8000, WallLevel = 1,
                                            Prep = 1, PrepRate = 0.04f, Advantage = 5f, Defenders = 300, Attackers = 3400, AttackerMen = 2000, DefenderMen = 190, Storming = storming });
                return s;
            }
            Brain.OnHour(ToolWorld(8000, nag));
            var order = new ToolOrder { Tool = "besiege", Lord = "lord_nag3", LordName = "Naglur", Party = "p_nag3", At = "castle_men", Then = JObject.Parse("{\"do\":\"besiege\",\"at\":\"castle_two\"}") };
            order.Settings["storm"] = 2; order.Settings["withdraw_on_relief"] = 1.2; order.Settings["relief_hours"] = 24;
            Runner.Start(980, order);
            nag.Besieging = "castle_men"; nag.Doing = "BesiegeSettlement"; nag.InBattle = true;
            for (int h = 8001; h <= 8003; h++) Brain.OnHour(Sieged(h, true, nag, Coming("hirgon", 185, 2500), Coming("beregond", 190, 2500)));
            Check(!moves.Any(m => m.Key == 980 && m.Value.Verb == "enter") && !Notices().Contains("Naglur lifts the siege of Fornost") && Notices().Contains("A storm of it is being fought now: nobody leaves it until it ends"),
                  "in a storm, the withdrawal bar waits (a relief of 5,000 against 3,400 does not lift the siege), and the alarm says the storm is being fought");
            nag.InBattle = false;
            Brain.OnHour(Sieged(8004, false, nag, Coming("hirgon", 184, 2500), Coming("beregond", 189, 2500)));
            Check(moves.Any(m => m.Key == 980 && m.Value.Verb == "enter" && m.Value.Place == "castle_two") && Notices().Contains("Naglur lifts the siege of Fornost by your bar"),
                  "once the storm is over, the bar acts: it lifts the siege and marches into Orc Keep");
            Runner.Reset();

            // a lord standing off is not a joiner of the storm; one walking in is
            var asta = new PartyRow { Id = "p_asta", Leader = "lord_asta", Name = "Asta", Faction = "player_k", Kind = "lord", Men = 300, Strength = 495, Speed = 3.2f,
                                      X = 164, Y = 100, Tx = 164, Ty = 100, Doing = "DefendSettlement", TargetSettlement = "castle_men", Spottable = true };
            Snapshot sj = ToolWorld(8100, asta, walking);
            List<Tuple<bool, int, double>> js = Views.Joiners(sj, "orcs", sj.WatchedSees, "castle_men", "orcs", "player_k");
            Check(js.Count == 1 && js[0].Item2 == 500 && !js[0].Item1, "a lord standing 6 from the walls is not counted as joining the storm; the one walking in is (" + js.Count + ")");

            // a storm under way run ahead at its own measured rates: Hollin Gate 8 hours in, 2,080 against 197 at the start
            var hg = new BattleRow { Kind = "siege", Settlement = "castle_men", StartHours = 9000, SettlementAdvantage = 6.48f };
            hg.Attackers = new BattleSideRow { Faction = "orcs", Healthy = 2019, Casualties = 61, StartHealthy = 2080, Power = 1.5744f, Advantage = 1.23f, Morale = 100 };
            hg.Defenders = new BattleSideRow { Faction = "player_k", Healthy = 149, Casualties = 49, StartHealthy = 197, Power = 1.9294f, Advantage = 1f, Morale = 20, SettlementLed = true };
            Forecast.BattleClock byRules = Forecast.BattleByMorale(hg, 9008.2, false, null, out string ruleHours);
            Forecast.BattleClock measured = Forecast.Measured(hg, 9008.2, true);
            Check(measured != null && measured.Measured && measured.Winner == "attackers" && measured.AttackerLossPerHour < 11 && measured.DefenderLossPerHour > 4
                  && !double.IsNaN(measured.EndsAt) && measured.EndsAt - 9008.2 < 40,
                  "a storm 8 hours in, run ahead at its measured rates: the orcs take it in about " + (measured == null || double.IsNaN(measured.EndsAt) ? "?" : Math.Round(measured.EndsAt - 9008.2).ToString())
                  + " hours, losing " + (measured == null ? "?" : Math.Round(measured.AttackerLossPerHour).ToString()) + " an hour (the rules alone: the " + byRules.Winner + " win, " + ruleHours + "; in the game, 65 defenders were left 22 hours in, the orcs losing about 7 an hour)");

            // a raid with nobody left to defend: the village's health runs down
            var raid = new BattleRow { Kind = "raid", Settlement = "village_men", StartHours = 9100 };
            raid.Attackers = new BattleSideRow { Leader = "p_rd", Faction = "orcs", Healthy = 166, StartHealthy = 166 };
            raid.Attackers.Parties.Add("p_rd");
            raid.Defenders = new BattleSideRow { Leader = "village_men", Faction = "player_k", Healthy = 0, StartHealthy = 20, Casualties = 20, SettlementLed = true };
            var raider = new PartyRow { Id = "p_rd", Leader = "lord_rd", Name = "Galuvir", Faction = "orcs", Kind = "lord", Men = 166, Strength = 200, X = 160, Y = 110, InBattle = true, Spottable = true };
            Snapshot r1 = ToolWorld(9110, raider); r1.SettlementById["village_men"].HitPoints = 0.40f; r1.Battles.Add(raid);
            Views.Clocks(r1, "orcs", r1.WatchedSees, new HashSet<string>());
            Snapshot r2 = ToolWorld(9111, raider); r2.SettlementById["village_men"].HitPoints = 0.38f; r2.Battles.Add(raid);
            string rl = Views.Clocks(r2, "orcs", r2.WatchedSees, new HashSet<string>()).FirstOrDefault(l => l.StartsWith("At Bree", StringComparison.Ordinal)) ?? "";
            Check(rl.Contains("It has 38% left, falling about 2% an hour (measured over the last hour), so it is looted in about 19 hours") && !rl.Contains("Run ahead"),
                  "a raid with its defenders down: the looting, by the village's health: " + rl);

            // an ally's siege far off is not in the realm's sight; its own is
            Snapshot sa = ToolWorld(9200);
            sa.Factions["orcs"].Allies.Add("isengard");
            sa.Factions["isengard"] = new FactionRow { Id = "isengard", Name = "Isengard", Ruler = "Saruman", AtWar = { "player_k" }, Allies = { "orcs" } };
            sa.FactionNames["isengard"] = "Isengard";
            sa.SettlementById["castle_men"].BesiegedBy = "isengard";
            sa.Sieges.Add(new SiegeRow { Settlement = "castle_men", Besieger = "p_far_ally", BesiegerFaction = "isengard", Strategy = "siege_strategy_prepare_assault", StartHours = 9190, WallLevel = 1, Prep = 0.2f, PrepRate = 0.04f, Advantage = 5f, Defenders = 300, Attackers = 3000 });
            List<string> cl = Views.Clocks(sa, "orcs", sa.WatchedSees, new HashSet<string> { "isengard" });
            Check(!cl.Any(l => l.Contains("castle_men")), "an ally's siege no lord of the realm can see is not in its report (" + cl.Count + " lines)");
        }

        /// <summary>
        /// Version 0.7.3, the game's clocks (Mert, Oct 17: march speed "based on the game's exact equations", and "the same about
        /// siege and battle timers so it can plan ahead"): the speed sum with the ground and night added in, a party covering its
        /// speed / 1.2 an hour; an arrival along the planned way across a night; the siege's preparations, engines and storm chance
        /// against the worked example of the 1.4.8 code (2,000 men at a level-1 castle: preparations 25.8 hours, a ram at 32.2, a
        /// storm more likely than not by about 50 hours); a storm's strikes per round by the game's formula; and the report.
        /// </summary>
        private static void GameClockTests(string dir)
        {
            // the speed sum: base x (1 + factors + ground + night), at least 1; 1.2 speed to the map unit an hour
            float b = (float)(4 * Math.Pow(200.0 / 2130, 0.4));
            var army = new Common.Pace { Base = b, Factors = 1.7f / b - 1 };
            Check(Math.Abs(Common.GamePace.Speed(army, Common.GamePace.Plain, false) - 1.7f) < 1e-3 && Math.Abs(Common.GamePace.PerHour(1.7f) - 1.4167f) < 1e-3,
                  "an army of 2,130 men at speed 1.7 on open ground by day covers 1.42 map units an hour");
            Check(Math.Abs(Common.GamePace.Speed(army, Common.GamePace.Forest, false) - b * (1.7f / b - 0.3f)) < 1e-3 && Common.GamePace.Speed(army, Common.GamePace.Forest, true) == 1f,
                  "forest adds -0.3 to the one sum (not x 0.7), and with night's -0.25 it falls to the game's floor of 1");
            var orcs = new Common.Pace { Base = 2f, Factors = 0f, Feats = new float[Common.GamePace.Kinds] };
            orcs.Feats[Common.GamePace.KindSnow] = 0.1f;
            Check(Math.Abs(Common.GamePace.Speed(orcs, Common.GamePace.Snow, false) - 2.2f) < 1e-4 && Math.Abs(Common.GamePace.Speed(orcs, Common.GamePace.Plain, true) - 1.5f) < 1e-4,
                  "a people's bonus for its ground adds in too (Misty Mountain Orcs +10% on snow)");
            Check(Math.Abs(Common.GamePace.Residual(army, b * (1.7f / b - 0.25f), Common.GamePace.Plain, true, false)) < 1e-4,
                  "what the rules leave unexplained in a speed now is measured from it: nothing, for a plain night march");

            // an arrival along the way the game planned: 100 east over open ground at speed 2.4 (2 an hour by day, 1.5 by night)
            MapGround.Override = new MapGround("test", Enumerable.Repeat((byte)Common.GamePace.Plain, 400 * 400).ToArray(), 0, 0, 1, 400, 400);
            var marcher = new PartyRow { Id = "p_mar", Leader = "lord_mar", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 120, Strength = 150, Speed = 2.4f, SpeedBase = 2.4f, FinalSpeed = 2.4f,
                                         Terrain = Common.GamePace.Plain, X = 100, Y = 50, Tx = 200, Ty = 50, Route = new[] { 150f, 50f, 200f, 50f }, Doing = "GoToSettlement", TargetSettlement = "castle_far", Spottable = true };
            Snapshot sw = ToolWorld(24 * 300 + 6, marcher);
            Add(sw, new SettlementRow { Id = "castle_far", Name = "Far Keep", Kind = "castle", Faction = "orcs", X = 200, Y = 50, Garrison = 100 });
            Forecast.Arrival a = Forecast.ArrivalOf(sw, marcher);
            Check(a != null && a.Reached && Math.Abs(a.Hours - 52) < 0.6, "100 units from 06:00 at speed 2.4: 32 that day, 6 in the night, 40 the next day, 6 the next night, 16 more: about 52 hours (" + (a == null ? "none" : a.Hours.ToString("0.0")) + ")");
            View vw = Views.Build(sw, "orcs", sw.WatchedSees, sw.HostileToWatched, Brain.Get("orcs"));
            string rw = Views.Report(vw, null);
            Check(rw.Contains("going to Far Keep, there in about 52 hours (") && rw.Contains("is the game's own reckoning of the march"),
                  "the report gives the arrival with its hour, and says how it is reckoned: " + string.Join(" | ", rw.Split('\n').Where(l => l.Contains("Ugluk") || l.Contains("reckoning"))));
            MapGround.Override = null;

            // the siege's clock: 2,000 healthy besiegers, a level-1 castle, defenders 450, besiegers 2,600, from hour 0 of the siege
            double k = Math.Sqrt(2000) / 24.0;
            var g = new SiegeRow
            {
                Settlement = "castle_orc", Besieger = "p_sieger2", BesiegerFaction = "player_k", Strategy = "siege_strategy_prepare_assault", StartHours = 24 * 400, WallLevel = 1,
                Prep = 0, PrepRate = (float)(k / 48), Advantage = 5f, Defenders = 450, Attackers = 2600, GarrisonPower = 300, Food = 400, FoodChange = -40, GarrisonRegulars = 300
            };
            g.Rates["ram"] = (float)(k / 12); g.Rates["siege_tower_level1"] = (float)(k / 16); g.Rates["ballista"] = (float)(k / 8); g.Rates["onager"] = (float)(k / 20); g.Rates["trebuchet"] = (float)(k / 24);
            Forecast.SiegeClock c = Forecast.Siege(g, g.StartHours);
            double since(double t) => t - g.StartHours;
            Check(Math.Abs(since(c.PrepDoneAt) - 25.8) < 0.3 && c.Engines.Count >= 3 && c.Engines[0].Key == "ram" && Math.Abs(since(c.Engines[0].Value) - 32.2) < 0.4 && Math.Abs(since(c.Engines[2].Value) - 49.4) < 0.5,
                  "preparations about 25.8 hours (48 man-days at the square root of 2,000 a day), then a ram at 32.2, two towers by 49.4 (" + since(c.PrepDoneAt).ToString("0.0") + ", "
                  + string.Join(", ", c.Engines.Take(3).Select(e => e.Key + " " + since(e.Value).ToString("0.0"))) + ")");
            Check(!double.IsNaN(c.MedianAt) && since(c.MedianAt) > 44 && since(c.MedianAt) < 56 && c.Within24 < 0.01,
                  "no storm the first day; more likely than not by about 50 hours, as the code's worked example gives (" + (double.IsNaN(c.MedianAt) ? "never" : since(c.MedianAt).ToString("0.0")) + ")");
            Check(Math.Abs(since(c.FoodOutAt) - 240) < 0.5, "400 food falling 40 a day lasts 10 days");
            var weak = new SiegeRow { Settlement = g.Settlement, Strategy = g.Strategy, StartHours = g.StartHours, WallLevel = 1, Prep = 1, PrepRate = g.PrepRate, Advantage = 5f, Defenders = 1000, Attackers = 1500 };
            foreach (KeyValuePair<string, float> kv in g.Rates) weak.Rates[kv.Key] = kv.Value;
            Forecast.SiegeClock cw = Forecast.Siege(weak, weak.StartHours);
            Check(double.IsNaN(cw.FirstChanceAt) || since(cw.FirstChanceAt) > 96, "besiegers only 1.5 times the defenders wait for the bar to fall after the fourth day (" + (double.IsNaN(cw.FirstChanceAt) ? "never" : since(cw.FirstChanceAt).ToString("0")) + ")");

            // a storm's strikes per round (DefaultCombatSimulationModel): the attackers' depend on the defenders' number only
            Forecast.StrikesPerRound(true, false, 3.18f, 960, 4500, out double byA, out double byD);
            Forecast.StrikesPerRound(true, false, 3.18f, 2050, 4500, out double byA2, out _);
            Check(byA == 28 && byD == 58 && byA2 == 28, "a storm: 960 attackers against 4,500 strike 28 times a round, the defenders 58, and 2,050 attackers no more (" + byA + ", " + byD + ", " + byA2 + ")");
            Forecast.StrikesPerRound(false, false, 1f, 2050, 4500, out double fA, out double fD);
            Check(fA == Math.Round(Math.Pow(2050, 0.6), MidpointRounding.ToEven) && fD == Math.Round(Math.Pow(4500, 0.6), MidpointRounding.ToEven), "in the field each side's strikes grow with its own men (" + fA + ", " + fD + ")");
            var storm = new BattleRow { Kind = "siege", Settlement = "castle_men", StartHours = 24 * 400, SettlementAdvantage = 3.18f };
            storm.Attackers = new BattleSideRow { Faction = "orcs", Healthy = 960, Power = 1.3f, Advantage = 0.9f, Morale = 40 };
            storm.Defenders = new BattleSideRow { Faction = "player_k", Healthy = 4500, Power = 1.6f, Advantage = 1f, Morale = 50, SettlementLed = true };
            Forecast.BattleClock bc = Forecast.Battle(storm, storm.StartHours);
            Check(bc.Winner == "defenders" && bc.AttackerLossPerHour > 10 && bc.AttackerLossPerHour < 30 && bc.DefenderLossPerHour > 3 && bc.DefenderLossPerHour < 12,
                  "a storm of 960 against 4,500 behind walls: the attackers lose about " + Math.Round(bc.AttackerLossPerHour) + " an hour, the defenders " + Math.Round(bc.DefenderLossPerHour) + ", and the defenders win ("
                  + bc.How + ", in " + (double.IsNaN(bc.EndsAt) ? "more than 5 days" : Math.Round(bc.EndsAt - storm.StartHours) + " hours") + ")");

            // the report: the siege of the realm's own castle and the storm, with their clocks
            var sieger = new PartyRow { Id = "p_sieger2", Leader = "lord_sieger2", Name = "Elessar", Faction = "player_k", Kind = "lord", Men = 1500, Strength = 2600, Speed = 3, X = 103, Y = 100, Besieging = "castle_orc", Doing = "BesiegeSettlement", Spottable = true };
            Snapshot ss = ToolWorld(g.StartHours + 10, sieger);
            ss.SettlementById["castle_orc"].BesiegedBy = "player_k";
            ss.SettlementById["castle_orc"].Besieger = "p_sieger2";
            g.Prep = 0.39f;
            g.AttackerMen = 1500; g.DefenderMen = 450; g.AttackerMorale = 60;
            ss.Sieges.Add(g);
            View vs = Views.Build(ss, "orcs", ss.WatchedSees, ss.HostileToWatched, Brain.Get("orcs"));
            string rs = Views.Report(vs, null);
            Check(rs.Contains("SIEGES AND BATTLES (1)") && rs.Contains("Orc Castle [castle_orc] (castle, Misty Mountain Orcs), besieged by Elessar (Kingdom of Arnor and Gondor)")
                  && rs.Contains("preparations done in about 16 hours") && rs.Contains("A storm could come from") && rs.Contains("A storm begun now would last") && rs.Contains("Food lasts about"),
                  "the report runs the siege of the realm's own castle ahead: preparations, engines, the storm's chance, a storm begun now, the food: "
                  + string.Join(" | ", rs.Split('\n').Where(l => l.StartsWith("- Orc Castle [castle_orc] (castle, Misty", StringComparison.Ordinal))));
            Check(Audit.Fog(vs, rs, ss.WatchedSees, ss.InspectedByWatched).Count == 0, "and passes the fog check");
            string viewFile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(dir)) ?? dir, "live", "smoketest", "view_orcs.json");
            Brain.OnHour(sw);
            string vf = File.Exists(viewFile) ? Read(viewFile) : "";
            Check(vf.Contains("\"march\":{\"base\":2.4"), "the agent's view file carries each lord's march for the \"ask the way\" tool");
        }

        /// <summary>
        /// Version 0.7.1, road distances (Mert, Oct 16: "distances should be given both as the crow flies and the actual travel
        /// distance"): through the party's entrance and the game's table between holdings, the nearest holding chosen by road,
        /// both in the report, the poke rule by road, and times in the facts by road.
        /// </summary>
        /// <summary>
        /// Test runs (0.11, docs/RUNS.md): the arm from who commands each side, the run's days, the day line from an hour's copy,
        /// the run's state read back as it was written, and the run's copy of the detailed log (its kinds only, while it runs).
        /// </summary>
        private static void RunTests()
        {
            Check(RunRules.Arm("you", new[] { "agent", "agent" }) == "A" && RunRules.Arm("you", new[] { "watching", "game" }) == "B"
                  && RunRules.Arm("agent", new[] { "agent", "agent" }) == "C1" && RunRules.Arm("agent", new[] { "game", "watching" }) == "C2"
                  && RunRules.Arm("you", new[] { "agent", "game" }) == "mixed" && RunRules.Arm("game", new[] { "agent" }) == "mixed"
                  && RunRules.Arm("game", new[] { "watching", "game" }) == "C0" && RunRules.Arm("watching", new[] { "watching", "watching" }) == "C0" && RunRules.Arm("none", new[] { "game" }) == "other",
                  "a run's arm follows who commands each side: A, B, C1, C2, and C0 the control, every realm by the game's AI (mixed when the rivals differ)");
            double h0 = 1000.3;
            Check(RunRules.Day(h0, h0) == 1 && RunRules.Day(h0 + 23.9, h0) == 1 && RunRules.Day(h0 + 24, h0) == 2 && !RunRules.Over(h0 + 1439, h0, 60) && RunRules.Over(h0 + 1440, h0, 60),
                  "day 1 is the first 24 in-game hours from the save's time, and a 60-day run is over at 1,440 hours");

            var host = new PartyRow { Id = "p_rh", Leader = "lord_rh", Name = "Gothmog", Faction = "orcs", Kind = "lord", Men = 400, Wounded = 20, Strength = 600, ArmyLeader = "p_rh", ArmyMen = 700, ArmyParties = 2, X = 100, Y = 100, Spottable = true, Loot = 900, Prisoners = 12, Ransom = 240 };
            var rider = new PartyRow { Id = "p_rr", Leader = "lord_rr", Name = "Shagrat", Faction = "orcs", Kind = "lord", Men = 300, Strength = 400, ArmyLeader = "p_rh", Attached = true, X = 100, Y = 100, Spottable = true };
            var foe = new PartyRow { Id = "p_rf", Leader = "lord_rf", Name = "Faramir", Faction = "player_k", Kind = "lord", Men = 250, Strength = 500, X = 160, Y = 100, Spottable = true };
            Snapshot s = ToolWorld(1100, host, rider, foe);
            var head = new RunDayHeader { Run = "r1", Arm = "A", Day = 3, Hours = 1100, Date = "Summer 12", Wall = "2026-10-24 10:00:00", Who = { ["player_k"] = "you", ["orcs"] = "agent" } };
            var tally = new Dictionary<string, RunTally> { ["orcs"] = new RunTally { BattlesWon = 2, CastlesLost = 1 } };
            string line = Json.Line(RunDay.Build(head, new List<string> { "player_k", "orcs" }, "player_k", s,
                new List<RealmExtras> { new RealmExtras { Realm = "orcs", LordsAlive = 30, LordsCaptive = 4, ClansGold = 50000, ClansDaily = -800 } },
                new List<WarRow> { new WarRow { A = "player_k", B = "orcs", AtWar = true, CasualtiesA = 100, CasualtiesB = 300 } }, tally, new Dictionary<string, int> { ["orcs"] = 55 }));
            Check(line.Contains("\"type\":\"day\"") && line.Contains("\"day\":3") && line.Contains("\"realm\":\"orcs\",\"name\":\"Misty Mountain Orcs\",\"who\":\"agent\"")
                  && line.Contains("\"castles\":2") && line.Contains("\"villages\":1") && line.Contains("\"lords\":2,\"men\":700,\"wounded\":20,\"strength\":1000")
                  && line.Contains("\"armies\":1,\"army_men\":700") && line.Contains("\"lords_captive\":4") && line.Contains("\"recruited_today\":55") && line.Contains("\"battles_won\":2")
                  && line.Contains("\"casualties_b\":300"),
                  "the day line counts each realm's holdings, lords, men, strength and armies from the hour's copy, with the game's numbers and the run's tally");

            var st = new RunState { Id = "r1-A", Arm = "A", Status = "running", Save = "TEST START", H0 = 1000.3, Kingdom = "player_k", Rivals = { "orcs" }, Who = { ["player_k"] = "you" } };
            st.Saves.Add("autosave");
            st.Tally["orcs"] = new RunTally { LordsTaken = 3 };
            st.Flag("2026-10-24 10:00:00", 1030, "true sight", "a test mark");
            RunState back = Json.Read<RunState>(Json.Line(st));
            Check(back.Id == "r1-A" && back.H0 == 1000.3 && back.Rivals.SequenceEqual(new[] { "orcs" }) && back.Who["player_k"] == "you" && back.Saves[0] == "autosave"
                  && back.Tally["orcs"].LordsTaken == 3 && back.Flags[0].Day == 2 && back.AllRealms().SequenceEqual(new[] { "player_k", "orcs" }),
                  "a run's state reads back as it was written, so a run carried on after a crash keeps its counts and its saves");

            string events = Path.Combine(Paths.Runs, "smoke-run", "events.jsonl");
            BannerlordAgents.Run.EventsFile = events;
            Diag.Event("order", "issued for the run", new { lord = "lord_rh" });
            Diag.Event("ai", "changed plan for the run", new { lord = "lord_rh" });
            BannerlordAgents.Run.EventsFile = null;
            Diag.Event("order", "issued after the run", new { lord = "lord_rr" });
            Diag.Drain();
            string copied = File.Exists(events) ? Read(events) : "";
            Check(copied.Contains("issued for the run") && !copied.Contains("changed plan") && !copied.Contains("after the run"),
                  "a run keeps its own copy of the detailed log's run kinds (orders, not the AI's reasons), only while it runs");
        }

        private static void RoadTests()
        {
            var far = new PartyRow { Id = "p_far", Leader = "lord_far2", Name = "Beregond", Faction = "player_k", Kind = "lord", Men = 400, Strength = 500, Speed = 4, X = 150, Y = 120, Doing = "Hold", Spottable = true, Entrance = "castle_men" };
            var ug = new PartyRow { Id = "p_ugr", Leader = "lord_ugr", Name = "Shagrat", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 250, Strength = 250, Speed = 5, FoodDays = 10, X = 100, Y = 100, Doing = "Hold", InSettlement = "castle_orc", Entrance = "castle_orc", Spottable = true };
            Snapshot s = ToolWorld(7100, far, ug);
            s.Roads = new RoadBook();
            s.Roads.Set("castle_men", "castle_orc", 200);
            s.Roads.Set("castle_men", "castle_two", 35);
            s.Roads.Set("castle_men", "village_orc", 190);
            s.Roads.Set("castle_orc", "castle_two", 45);
            s.Roads.Set("castle_men", "village_men", 15);
            Brain.OnHour(s);
            float toOrc = Roads.To(s, far, s.SettlementById["castle_orc"]), toTwo = Roads.To(s, far, s.SettlementById["castle_two"]);
            Check(Math.Abs(toOrc - 183.9f) < 0.2f && Math.Abs(toTwo - 27.4f) < 0.2f, "by road through its entrance: to Orc Castle " + Text.D(toOrc) + " (53.9 as the crow flies), to Orc Keep " + Text.D(toTwo));
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, Brain.Get("orcs"));
            string report = Views.Report(v, null);
            ViewParty seen = v.Seen.FirstOrDefault(x => x.Id == "p_far");
            Check(seen != null && seen.NearestHolding == "Orc Keep" && report.Contains("22.4 (27.4 by road) from your Orc Keep"), "the report gives the nearest holding by road, both as the crow flies and by road");
            Check(report.Contains("as the crow flies, then in brackets by road"), "and says what the two distances are");
            Check(Math.Abs(Runner.WorldFor(s).RoadDistance("p_ugr", "p_far") - 183.9) < 0.3, "between two parties, each through its own entrance (" + Text.D((float)Runner.WorldFor(s).RoadDistance("p_ugr", "p_far")) + ")");
            Check(Facts.Lookups(Runner.WorldFor(s), Brain.Get("orcs").LastSeen, Brain.Get("orcs").Trails).TryGetValue("lord_ugr", out string page) && page.Contains("Orc Keep [castle_two]: about 11 h"),
                  "a lord's look-up gives hours by road at its pace by the game's rules (45 by road at speed 5: 4.0 map units an hour over a day, night counted)");
            Brain.OnHour(ToolWorld(7101, far, ug));
            Check(!Roads.Known, "without the game's table (a made-up world) distances go back to the crow's alone");
        }

        /// <summary>
        /// Version 0.7, the dashboard: the hourly frame (every party with its planned way, and the realm's view with what its
        /// report left out), the agent's own view file for the "ask the way" tool (nothing the realm did not see), where each
        /// record file stood when the load began, the holding a seen party heads for, and SEEN EARLIER never cutting an army.
        /// </summary>
        private static void DashboardTests(string root, string dir)
        {
            PartyRow Band(int i, float x) => new PartyRow { Id = "band_" + i, Name = "Looters", Faction = "player_k", Kind = "bandit", Men = 10, Strength = 6, X = x, Y = 130, Doing = "PatrolAroundPoint", Spottable = true };
            var army = new PartyRow
            {
                Id = "p_host", Leader = "lord_host", Name = "Elrohir", Faction = "player_k", Kind = "lord", Men = 600, ArmyLeader = "p_host", ArmyMen = 3000, ArmyParties = 8, ArmyStrength = 7000,
                Strength = 900, Speed = 3.2f, FinalSpeed = 2.4f, X = 150, Y = 120, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true,
                Tx = 100, Ty = 99, Route = new[] { 130f, 112f, 101f, 100f }
            };
            var hidden = new PartyRow { Id = "p_hidden", Leader = "lord_hidden", Name = "Glorfindel", Faction = "player_k", Kind = "lord", Men = 300, Strength = 500, X = 300, Y = 300, Doing = "Hold", Spottable = true };
            var own = new PartyRow { Id = "p_ugluk", Leader = "lord_ugluk", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 250, Strength = 300, Speed = 3.6f, FinalSpeed = 3.6f, X = 105, Y = 101, Doing = "GoToPoint", Spottable = true,
                                 Tx = 120, Ty = 102, Route = new[] { 120f, 102f } };
            var first = new List<PartyRow> { army, hidden, own };
            for (int i = 0; i < 45; i++) first.Add(Band(i, 110 + i));
            Snapshot s1 = ToolWorld(6100, first.ToArray());
            s1.WatchedSees.Remove("p_hidden");
            Brain.OnHour(s1);
            string current = Path.Combine(root, "live", "current.json");
            Check(File.Exists(current), "the dashboard's pointer to the newest hour is written (live\\current.json)");
            JObject cur = File.Exists(current) ? JObject.Parse(Read(current)) : new JObject();
            string framePath = Path.Combine((string)cur["dir"] ?? "", "frames", (string)cur["frame"] ?? "");
            Check(File.Exists(framePath) && ((string)cur["frame"]) == "h6100.json.gz" && cur["calendar"]?["days_in_season"] != null, "the hour's frame is written, named by its hour, with the calendar beside it (" + cur["frame"] + ")");
            JObject frame = null;
            if (File.Exists(framePath))
                using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(framePath), System.IO.Compression.CompressionMode.Decompress))
                using (var sr = new StreamReader(gz)) frame = JObject.Parse(sr.ReadToEnd());
            JObject hostRow = frame?["parties"]?.OfType<JObject>().FirstOrDefault(p => (string)p["id"] == "p_host");
            Check(hostRow != null && hostRow["route"] is JArray rr && rr.Count == 4 && (float)hostRow["final_speed"] == 2.4f && (float)hostRow["tx"] == 100,
                  "the frame carries each party with its planned way, its speed now and where it walks to");
            JObject realm = frame?["realms"]?["orcs"] as JObject;
            Check(realm != null && realm["view"]?["seen"] is JArray seen && seen.Count == 40 && realm["cut"] is JArray cutIds && cutIds.Count == 6 && realm["in_sight"] is JArray sight && sight.Count == 46,
                  "the realm's part holds the view its report was written from, and the 6 bands the report's list of 40 left out");
            JObject hostSeen = (realm?["view"]?["seen"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["id"] == "p_host");
            Check(hostSeen != null && (string)hostSeen["toward"] == "castle_orc", "a seen party carries the holding it heads for (" + hostSeen?["toward"] + ")");
            string viewFile = Path.Combine(root, "live", "smoketest", "view_orcs.json");
            string viewText = File.Exists(viewFile) ? Read(viewFile) : "";
            Check(viewText.Contains("\"p_ugluk\"") && viewText.Contains("\"pace\":3.6") && viewText.Contains("\"p_host\"") && !viewText.Contains("p_hidden") && frame != null && frame.ToString().Contains("p_hidden"),
                  "the agent's view file for the way-finding tool holds its own lords with their pace and what it saw, never the party it did not see");
            // the army out of sight, then 45 more bands seen later: SEEN EARLIER keeps the army though bands were seen since
            var second = new List<PartyRow> { army };
            for (int i = 45; i < 90; i++) second.Add(Band(i, 110 + i - 45));
            Snapshot s2 = ToolWorld(6102, second.ToArray());
            s2.WatchedSees.Remove("p_host");
            Brain.OnHour(s2);
            Snapshot s3 = ToolWorld(6104, army);
            s3.WatchedSees.Clear();
            Brain.OnHour(s3);
            View v3 = Views.Build(s3, "orcs", s3.WatchedSees, s3.HostileToWatched, Brain.Get("orcs"));
            Check(v3.LastSeen.Count >= 25 && v3.LastSeen[0].Id == "p_host", "SEEN EARLIER never cuts an enemy army, though 90 bands were seen since (" + v3.LastSeen.Count + " listed, first " + (v3.LastSeen.FirstOrDefault()?.Id ?? "none") + ")");
            string loads = Path.Combine(dir, "loads.jsonl");
            Check(File.Exists(loads) && Read(loads).Contains("\"offsets\"") && Read(loads).Contains("\"pokes_orcs.jsonl\""), "each load notes where each record file stood when it began (loads.jsonl)");
        }

        private sealed class TestScope : IScope
        {
            public object Get(string name) => name == "x" ? (object)2.0 : null;
            public object Call(string fn, object[] a) => fn == "twice" ? (object)(Expr.Number(a[0]) * 2) : null;
        }

        /// <summary>A small orc realm for the tools: two castles and a village of theirs, a castle and a village of the player's.</summary>
        /// <summary>
        /// 0.8: two watched realms, each with its own agent: each is poked in its own folder with a report of what its own eyes
        /// saw, keeps its own poke file, an answer in a realm's folder is that realm's, and each realm's orders in force are its own.
        /// </summary>
        private static void TwoRealmsTests(string root, string agentDir)
        {
            string orcDir = Path.Combine(root, "agent2_orcs"), dgDir = Path.Combine(root, "agent2_dg");
            Settings.AgentDir = orcDir;
            Settings.AgentDirs["dg"] = dgDir;
            Brain.Reset("smoketest2", double.NaN, new List<string> { "orcs", "dg" }, null);
            var host = new PartyRow { Id = "p_ar", Leader = "lord_ar", Name = "Elessar", Faction = "player_k", Kind = "lord", Men = 500, Strength = 900, X = 106, Y = 104, Doing = "Hold", Spottable = true };
            Snapshot s = ToolWorld(7000, host);
            s.Factions["dg"] = new FactionRow { Id = "dg", Name = "Dol Guldur", Ruler = "Khamul", AtWar = { "player_k" } };
            s.FactionNames["dg"] = "Dol Guldur";
            Add(s, new SettlementRow { Id = "town_dg", Name = "Dol Guldur", Kind = "town", Faction = "dg", X = 400, Y = 400, Garrison = 500, Militia = 100 });
            Add(s, new PartyRow { Id = "p_khamul", Leader = "lord_khamul", Name = "Khamul", Faction = "dg", Kind = "lord", Men = 400, Strength = 800, X = 402, Y = 400, Doing = "Hold", InSettlement = "town_dg" });
            Add(s, new PartyRow { Id = "p_elf", Leader = "lord_elf", Name = "Haldir", Faction = "player_k", Kind = "lord", Men = 300, Strength = 600, X = 410, Y = 405, Doing = "GoToSettlement", TargetSettlement = "town_dg", Spottable = true });
            s.Seats = new List<SeatSight>
            {
                new SeatSight { Realm = "orcs", Sees = s.WatchedSees, Inspected = s.InspectedByWatched, Hostile = s.HostileToWatched },
                new SeatSight { Realm = "dg", Sees = new Dictionary<string, Sighting> { ["p_elf"] = new Sighting { Party = "p_elf", By = "Dol Guldur", Distance = 9f } },
                                Inspected = new HashSet<string> { "town_dg" }, Hostile = new HashSet<string> { "player_k" } }
            };
            Brain.OnHour(s);
            Files.FlushAll();
            string orcPoke = Read(Path.Combine(orcDir, "poke.txt")), dgPoke = Read(Path.Combine(dgDir, "poke.txt"));
            Check(orcPoke.Contains("MISTY MOUNTAIN ORCS") && dgPoke.Contains("DOL GULDUR"), "two watched realms: each is poked in its own channel folder, with its own report");
            Check(dgPoke.Contains("Haldir") && !orcPoke.Contains("Haldir") && orcPoke.Contains("Elessar") && !dgPoke.Contains("Elessar"),
                  "each realm's report holds only what its own eyes saw");
            Check(File.Exists(Path.Combine(Brain.Dir, "pokes_orcs.jsonl")) && File.Exists(Path.Combine(Brain.Dir, "pokes_dg.jsonl")), "each realm's pokes are kept in its own file");
            AskBriefing(AgentChannel.For("orcs"), orcDir);
            AskBriefing(AgentChannel.For("dg"), dgDir);
            Check(ReadIf(Path.Combine(orcDir, "report.txt")).Contains("MISTY MOUNTAIN ORCS") && ReadIf(Path.Combine(dgDir, "report.txt")).Contains("DOL GULDUR") && File.Exists(Path.Combine(dgDir, "council.txt")),
                  "each realm's war room gets its own situation report and council briefing when it asks");

            // an answer in Dol Guldur's folder is Dol Guldur's: its receipt is written there, not in the orcs' folder
            AgentChannel.Receipt(new AgentChannel.AgentOrder { File = "a.json", Index = 1, Poke = 1, Lord = "lord_khamul", Do = "hold", Realm = "dg", Received = DateTime.Now },
                                 "checked", "valid: Khamul holds (shadow mode: not carried out)", 7000.5);
            Files.FlushAll();
            Check(Read(Path.Combine(dgDir, "receipts.log")).Contains("lord_khamul hold") && !ReadIf(Path.Combine(orcDir, "receipts.log")).Contains("lord_khamul"),
                  "a receipt goes to the channel the order came by");
            Check(!ReadIf(Path.Combine(orcDir, "chronicle.txt")).Contains("Khamul"), "and nothing of it into the other realm's chronicle");

            // the tools run per realm: Dol Guldur's order is in its own orders in force, not the orcs'
            Runner.Start(951, new ToolOrder { Tool = "goto", Lord = "lord_khamul", LordName = "Khamul", Party = "p_khamul", At = "town_dg", Realm = "dg" });
            List<Snapshot> seats = s.SeatViews();
            string dgInForce = Runner.InForce(Runner.WorldFor(seats[1])), orcInForce = Runner.InForce(Runner.WorldFor(seats[0]));
            Check(seats.Count == 2 && seats[1].Watched == "dg" && dgInForce.Contains("Khamul") && !orcInForce.Contains("Khamul"), "a realm's orders in force list only its own lords' orders");
            Runner.Ended(951, "test over");

            Settings.AgentDirs.Remove("dg");
            Settings.AgentDir = agentDir;
            FolderAgentsTests(root, agentDir);
            LieutenantTests(root, agentDir);
            SaveHistoryTests(agentDir);
        }

        /// <summary>
        /// 0.10, the player's lieutenant: a folder of kind lieutenant for the player's realm; its hour (a report saying where the
        /// player's party is, fresh look-ups, no pokes in its channel, the player's own record kept); the player's words written
        /// for it; and the rule that it never acts unasked.
        /// </summary>
        private static void LieutenantTests(string root, string agentDir)
        {
            string orcs = Path.Combine(root, "agents", "orcs"), lt = Path.Combine(root, "agents", "lieutenant");
            Directory.CreateDirectory(lt);
            File.WriteAllText(Path.Combine(lt, "agent.txt"), "realm = player_k\nmode = live\nlords = all\nchannel = warroom\nkind = lieutenant\n");
            AgentConfig lc = AgentConfig.Read(lt);
            Check(lc.IsLieutenant && lc.Realm == "player_k" && lc.Problem == null && !AgentConfig.Read(orcs).IsLieutenant, "a folder's agent.txt says kind = lieutenant; every other agent is a realm's");
            Settings.AgentFolders.Clear();
            Settings.AgentFolders.AddRange(new[] { orcs, lt });
            List<AgentConfig> configs = Agents.Configured();
            Brain.Reset("smoketest6", double.NaN, configs, null);
            var me = new PartyRow { Id = "main_party", Leader = "main_hero", Name = "Aragorn", Faction = "player_k", Kind = "lord", Men = 500, Strength = 900, X = 165, Y = 105, Doing = "Hold", Spottable = true };
            var vassal = new PartyRow { Id = "p_vas", Leader = "lord_vas", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 300, Strength = 450, X = 168, Y = 100, Doing = "Hold", Spottable = true, InSettlement = "castle_men" };
            var orc = new PartyRow { Id = "p_ug", Leader = "lord_ug", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 400, Strength = 500, X = 150, Y = 104, Doing = "Hold", Spottable = true };
            Snapshot Seated(double h)
            {
                Snapshot s = ToolWorld(h, me, vassal, orc);
                s.Seats = new List<SeatSight>
                {
                    new SeatSight { Realm = "orcs", Sees = s.WatchedSees, Inspected = s.InspectedByWatched, Hostile = s.HostileToWatched },
                    new SeatSight { Realm = "player_k", Sees = new Dictionary<string, Sighting> { ["p_ug"] = new Sighting { Party = "p_ug", By = "Halbarad", Distance = 18f } },
                                    Inspected = new HashSet<string> { "castle_men", "village_men" }, Hostile = new HashSet<string> { "orcs" } }
                };
                return s;
            }
            // 0.13.9: before any hour since the load, the ruler's words bring a report written on the spot (Oct 29: the lieutenant had read the
            // report the session before wrote); nothing else of an hour happens with it
            int pokedBefore6 = Brain.PokesWatched;
            Brain.LieutenantReportNow(Seated(8999), "player_k");
            Files.FlushAll();
            string spot = ReadIf(Path.Combine(lt, "warroom", "report.txt"));
            Check(spot.StartsWith("SITUATION AT " + Text.When(8999)) && spot.Contains("Aragorn [main_party]") && spot.Contains("Halbarad") && Brain.PokesWatched == pokedBefore6,
                  "before any hour since the load, the lieutenant's report is written on the spot from a copy of now, and nobody is poked");
            for (int h = 9000; h <= 9001; h++)
            {
                Snapshot s = ToolWorld(h, me, vassal, orc);
                s.Seats = new List<SeatSight>
                {
                    new SeatSight { Realm = "orcs", Sees = s.WatchedSees, Inspected = s.InspectedByWatched, Hostile = s.HostileToWatched },
                    new SeatSight { Realm = "player_k", Sees = new Dictionary<string, Sighting> { ["p_ug"] = new Sighting { Party = "p_ug", By = "Halbarad", Distance = 18f } },
                                    Inspected = new HashSet<string> { "castle_men", "village_men" }, Hostile = new HashSet<string> { "orcs" } }
                };
                Brain.OnHour(s);
            }
            Files.FlushAll();
            string channel = Path.Combine(lt, "warroom");
            string report = ReadIf(Path.Combine(channel, "report.txt"));
            Check(report.Contains("YOUR RULER (the player, whom you serve)") && report.Contains("Aragorn [main_party]: 500 men") && report.Contains("never give it orders")
                  && report.Contains("Halbarad") && !report.Contains("- Aragorn [main_hero]"),
                  "the lieutenant's hourly report says where the player's own party is, and lists the realm's lords without it");
            Check(!File.Exists(Path.Combine(channel, "poke.txt")) && File.Exists(Path.Combine(Brain.Dir, "pokes_player_k.jsonl")) && File.Exists(Path.Combine(channel, "lookups", "lord_vas.txt")),
                  "the lieutenant is never poked, though its realm's pokes are kept and its look-ups written every hour");
            Check(ReadIf(Path.Combine(Brain.Dir, "record", "pokes_you.jsonl")).Contains("\"seat\":true"), "the record of the player's own play keeps the realm's pokes while it has a lieutenant");
            Channel ch = Agents.Of("player_k").Channel;
            ch.WriteChat(5, new { Number = 5, Said = "hold Goblin Town with dwarves", Pending = (object)null }, "hold Goblin Town with dwarves");
            Check(ReadIf(Path.Combine(channel, "chat.json")).Contains("\"said\":\"hold Goblin Town with dwarves\"") && ch.LastChatNumber() == 5,
                  "the player's words go into the lieutenant's channel (chat.json, chats.log), numbered on from the last");
            // 0.13.8 (Mert, Oct 29: approve in the chat only, some orders yes and others with comments): the orders the words asked for or
            // agreed to are given, and a changed proposal can wait beside them in the same answer; no buttons
            Check(LieutenantRules.Gate(true, 2, 0, false) == "carry" && LieutenantRules.Gate(true, 2, 3, false) == "carry+propose" && LieutenantRules.Gate(false, 0, 3, false) == "propose"
                  && LieutenantRules.Gate(true, 0, 3, false) == "propose" && LieutenantRules.Gate(false, 0, 0, true) == "drop" && LieutenantRules.Gate(false, 0, 0, false) == "none",
                  "the lieutenant gives the orders the player's words agreed to, and what he wanted changed waits beside them as the new proposal");
            Settings.AgentFolders.Clear();
            Settings.AgentDir = agentDir;
        }

        /// <summary>
        /// 0.11.1: the lieutenant and Party AI Controls (what its panel shows for each of our orders, the options and troop mix in
        /// its terms), and the conversation of a save (a line said after a save, in the same paused hour, is not that save's).
        /// </summary>
        private static void LieutenantPacTests()
        {
            Check(PacRules.MirrorOf("defend", true) == "DefendSettlement" && PacRules.MirrorOf("relieve", true) == "DefendSettlement" && PacRules.MirrorOf("besiege", true) == "BesiegeSettlement"
                  && PacRules.MirrorOf("follow", false) == "EscortParty" && PacRules.MirrorOf("hunt", false) == "AttackParty" && PacRules.MirrorOf("recruit", true) == "RecruitFromTemplate"
                  && PacRules.MirrorOf("watch", true) == "PatrolAroundPoint" && PacRules.MirrorOf("watch", false) == null && PacRules.MirrorOf("raid", true) == null
                  && PacRules.MirrorOf("shadow", false) == null && PacRules.MirrorOf("gather", true) == null && PacRules.MirrorOf("join", false) == null && PacRules.MirrorOf("hold", false) == null,
                  "each of the lieutenant's orders shows in Party AI Controls as the nearest of its own kinds, and raids, shadows, watch posts and armies as none");
            Check(PacRules.UsesParty("EscortParty") && PacRules.UsesParty("AttackParty") && !PacRules.UsesParty("DefendSettlement"), "follow and attack aim at a party, the rest at a holding");
            bool a = PacRules.Option("raid_villages", new JValue(false), out string p1, out object v1, out string w1, out _);
            bool b = PacRules.Option("auto_recruit_below", new JValue(40), out string p2, out object v2, out _, out _);
            bool c = PacRules.Option("max_troop_tier", new JValue(12), out _, out _, out _, out string e3);
            bool d = PacRules.Option("fly", new JValue(true), out _, out _, out _, out string e4);
            bool e = PacRules.Option("patrol_radius", new JValue(5), out _, out _, out _, out _);
            bool f = PacRules.Option("join_armies", new JValue(1), out _, out _, out _, out string e6);
            Check(a && p1 == "AllowRaidVillages" && (bool)v1 == false && w1 == "raid_villages no" && b && p2 == "AutoRecruitmentPercentage" && Math.Abs((float)v2 - 0.4f) < 1e-6
                  && !c && e3.Contains("0 to 10") && !d && e4.Contains("not an option") && !e && !f && e6.Contains("true or false"),
                  "the panel's options in Party AI Controls' own terms: percentages as fractions, numbers kept to their ranges, unknown names and wrong kinds refused");
            Check(PacRules.Composition(JObject.Parse("{\"infantry\":50,\"ranged\":30,\"cavalry\":20}"), out float[] mix, out _) && Math.Abs(mix[0] - 0.5f) < 1e-6 && Math.Abs(mix[3]) < 1e-6
                  && PacRules.Composition(JObject.Parse("{\"infantry\":2,\"horse_archers\":2}"), out float[] mix2, out _) && Math.Abs(mix2[3] - 0.5f) < 1e-6
                  && !PacRules.Composition(JObject.Parse("{\"infantry\":-5,\"ranged\":10}"), out _, out _) && !PacRules.Composition(JObject.Parse("{\"cavalry\":0}"), out _, out _)
                  && !PacRules.Composition(JObject.Parse("{\"pikes\":50}"), out _, out _),
                  "a troop mix in percentages becomes Party AI Controls' fractions summing to 1; below 0, nothing, or an unknown part is refused");
            string lib = PacRules.LibraryText();
            Check(lib.Contains("**party_ai**") && lib.Contains("fallback") && lib.Contains("new_template") && lib.Contains("composition") && PacRules.Options.All(o => lib.Contains(o.Word))
                  && PacRules.Kinds.All(k => lib.Contains(k.Word + " (")) && lib.Contains("the ruler's wins"),
                  "the lieutenant's library tells every Party AI Controls order and option, and that the ruler's own order there wins");

            // the conversation of a save: session S1 saves "saveX" at 120; a line it said after the save, the clock stopped, is not saveX's
            const string campaign = "smoketest-lt";
            string dir = Paths.Data(campaign);
            History s1 = History.Of("S1", 100, null, dir, true);
            File.AppendAllText(History.NotesFile(dir), Json.Line(s1.NoteFor("saveX", 120)) + "\n");
            DateTime saved = DateTime.Now;
            string file = Path.Combine(dir, "lieutenant", "chat.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string L(string load, double h, DateTime w, string t) => "{\"load\":\"" + load + "\",\"h\":" + h.ToString(Text.Inv) + ",\"w\":\"" + w.ToString("o", Text.Inv) + "\",\"k\":\"you\",\"t\":\"" + t + "\"}\n";
            File.WriteAllText(file, L("S1", 110, saved.AddMinutes(-5), "before the save") + L("S1", 120, saved.AddMinutes(-1), "just before the save, paused")
                                    + L("S1", 120, saved.AddMinutes(2), "after the save, same hour") + L("S0", 105, saved.AddMinutes(-9), "another branch") + "{half a line");
            History s2 = History.Of("S2", 120, "saveX", dir, false);
            List<string> got = LabelledLines.Read(file, s2).Select(o => (string)o["t"]).ToList();
            Check(s2.Found == "marked" && got.SequenceEqual(new[] { "before the save", "just before the save, paused" }),
                  "loading a save brings back exactly the conversation it lived through: not what was said after it while paused, nor another branch's (" + string.Join(" | ", got) + ")");
            Check(s2.HoldsLine("S2", 500, DateTime.Now.AddHours(1)) && !s2.HoldsLine(null, 50, null), "this session's own lines are always its; unlabelled ones are not a marked save's");
            var old = new History { Load = "S3", Before = new List<HistoryStep> { new HistoryStep { Load = "S1", Until = 120 } } };
            Check(old.HoldsLine("S1", 120, saved.AddMinutes(30)) && !old.HoldsLine("S1", 121, null), "a note from before 0.11.1 (no wall clock) still goes by the in-game hour");
        }

        /// <summary>
        /// 0.9.1: a campaign played on two branches from one save, then a save of the first branch loaded again. Each line is
        /// labelled with its session and none is cut; each save's note says its history, so every load gets exactly its own.
        /// </summary>
        private static void SaveHistoryTests(string agentDir)
        {
            const string campaign = "smoketest5";
            string dir = Paths.Data(campaign);
            Settings.AgentFolders.Clear();
            Settings.AgentDir = agentDir;
            var configs = new List<AgentConfig> { AgentConfig.FromSettings("orcs", agentDir, "shadow", "all") };
            // the chronicle as 0.9.0 left it: unlabelled lines, one from before the save at 100 and one from a later session
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "chronicle_orcs.jsonl"),
                "{\"hours\":90,\"kind\":\"order\",\"text\":\"Old line at 90.\"}\n{\"hours\":150,\"kind\":\"order\",\"text\":\"Old line at 150.\"}\n");
            string Shown() { Files.FlushAll(); return Chronicle.For("orcs").Last(100); }
            void Load(string load, double at, string save)
            {
                History h = History.Of(load, at, save, dir, false);
                Brain.Reset(campaign, at, configs, null, h);
                Brain.OnHour(World(at, playerSeen: false));
            }

            // session A, from the old save "start" (made at 100, before notes): the old lines up to 100
            Load("A", 100, "start");
            Check(Brain.History.Found == "unmarked" && Shown().Contains("Old line at 90.") && !Shown().Contains("Old line at 150."),
                  "a save with no history note gets the unlabelled lines up to its date (" + Brain.History + ")");
            Chronicle.For("orcs").Line(110, "order", "A at 110.");
            Files.Append(History.NotesFile(dir), Json.Line(Brain.History.NoteFor("saveA", 120)));
            Chronicle.For("orcs").Line(130, "order", "A at 130, after its save.");

            // session B, from "start" again: another branch
            Load("B", 100, "start");
            Chronicle.For("orcs").Line(105, "order", "B at 105.");
            string b = Shown();
            Check(b.Contains("Old line at 90.") && b.Contains("B at 105.") && !b.Contains("A at 110."), "a second branch from the same save does not see the first branch's lines");

            // session C, from A's save at 120: A's history, not B's, though B came later
            Load("C", 120, "saveA");
            string c = Shown();
            Check(Brain.History.Found == "marked" && c.Contains("Old line at 90.") && c.Contains("A at 110.") && !c.Contains("A at 130") && !c.Contains("B at 105.") && !c.Contains("Old line at 150."),
                  "a save of the first branch, loaded after the second, gets exactly its own history (" + Brain.History + ")");
            string file = Read(Path.Combine(dir, "chronicle_orcs.jsonl"));
            Check(file.Contains("Old line at 150.") && file.Contains("A at 130") && file.Contains("B at 105.") && file.Contains("\"load\":\"C\"") == false, "and the file keeps every line of every branch");
            Check(History.Of("D", 121, "saveA", dir, false).Found == "unmarked", "a save whose hour does not match its note (overwritten without this mod) is treated as having none");
            Check(History.Of("E", 0, null, dir, true).Found == "new" && History.Of("E", 0, null, dir, true).Before.Count == 0, "a new game's history starts empty");
            Check(Read(Path.Combine(dir, "loads.jsonl")).Contains("\"found\":\"marked\""), "each load records the history it found");
        }

        /// <summary>
        /// 0.9: an agent is a folder. Its agent.txt says the realm, the mode, who obeys and the channel; its warroom.txt where its
        /// war room logs. The settings list the folders, every agent is made the same way, and each agent's mode is its own.
        /// </summary>
        private static void FolderAgentsTests(string root, string agentDir)
        {
            string orcs = Path.Combine(root, "agents", "orcs"), dg = Path.Combine(root, "agents", "dg"), empty = Path.Combine(root, "agents", "empty");
            foreach (string d in new[] { orcs, dg, empty }) Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(orcs, "agent.txt"), "# the orcs\nrealm = orcs\nmode = live\nlords = all\n");
            File.WriteAllText(Path.Combine(dg, "agent.txt"), "realm = dg\nmode = off\nlords = lord_khamul\nchannel = channel\n");
            string dgLogs = Path.Combine(root, "logs", "warroom-dg");
            File.WriteAllText(Path.Combine(dg, "warroom.txt"), "channel = channel\nlog_dir = " + dgLogs + "\n");

            AgentConfig c = AgentConfig.Read(dg);
            Check(c.Problem == null && c.Realm == "dg" && c.Mode == "off" && c.Lords == "lord_khamul" && c.ChannelDir == Path.Combine(dg, "channel") && c.LogDir == dgLogs,
                  "an agent's folder says its realm, mode, lords and channel (agent.txt) and where its war room logs (warroom.txt)");
            AgentConfig o = AgentConfig.Read(orcs);
            Check(o.Mode == "live" && o.ChannelDir == Path.Combine(orcs, "warroom") && o.LogDir == AgentConfig.DefaultLogDir, "the channel is warroom and the logs the shared folder unless the folder says otherwise");
            Check(AgentConfig.Read(empty).Problem != null, "a folder without agent.txt is reported, not guessed at");

            Settings.AgentFolders.Clear();
            Settings.AgentFolders.AddRange(new[] { orcs, dg });
            List<AgentConfig> configs = Agents.Configured();
            Check(configs.Count == 2 && configs[0].Realm == "orcs" && configs[1].Realm == "dg", "the settings' list of folders makes the agents, in its order");

            Brain.Reset("smoketest3", double.NaN, configs, null);
            Check(Agents.All.Count == 2 && Agents.Of("dg").Config.Mode == "off" && Agents.Of("orcs").On, "each agent keeps its own mode");
            var host = new PartyRow { Id = "p_ar", Leader = "lord_ar", Name = "Elessar", Faction = "player_k", Kind = "lord", Men = 500, Strength = 900, X = 106, Y = 104, Doing = "Hold", Spottable = true };
            Snapshot s = ToolWorld(8000, host);
            s.Factions["dg"] = new FactionRow { Id = "dg", Name = "Dol Guldur", Ruler = "Khamul", AtWar = { "player_k" } };
            s.FactionNames["dg"] = "Dol Guldur";
            Add(s, new SettlementRow { Id = "town_dg", Name = "Dol Guldur", Kind = "town", Faction = "dg", X = 400, Y = 400, Garrison = 500 });
            Add(s, new PartyRow { Id = "p_khamul", Leader = "lord_khamul", Name = "Khamul", Faction = "dg", Kind = "lord", Men = 400, Strength = 800, X = 402, Y = 400, Doing = "Hold", InSettlement = "town_dg" });
            s.Seats = new List<SeatSight>
            {
                new SeatSight { Realm = "orcs", Sees = s.WatchedSees, Inspected = s.InspectedByWatched, Hostile = s.HostileToWatched },
                new SeatSight { Realm = "dg", Sees = new Dictionary<string, Sighting>(), Inspected = new HashSet<string> { "town_dg" }, Hostile = new HashSet<string> { "player_k" } }
            };
            Brain.OnHour(s);
            Files.FlushAll();
            Check(File.Exists(Path.Combine(orcs, "warroom", "poke.txt")) && !File.Exists(Path.Combine(dg, "channel", "poke.txt")) && File.Exists(Path.Combine(Brain.Dir, "pokes_dg.jsonl")),
                  "an agent that is off gets nothing in its channel, though its realm's pokes are still kept; one that is live gets its poke");

            Runner.Start(961, new ToolOrder { Tool = "goto", Lord = "lord_khamul", LordName = "Khamul", Party = "p_khamul", At = "town_dg", Realm = "dg" });
            Check(Agents.Of("dg").Tools.ContainsKey(961) && !Agents.Of("orcs").Tools.ContainsKey(961), "a tool lives in its own agent's orders in force");
            Runner.Ended(961, "test over");
            Check(!Agents.Of("dg").Tools.ContainsKey(961), "and leaves them when it ends");

            // 0.9.3: a tool for a realm no agent serves no longer runs on the first agent listed; the lord is let go
            Action<int, string> keptSink = Runner.ReleaseSink;
            var letGo = new List<int>();
            Runner.ReleaseSink = (id, why) => letGo.Add(id);
            Runner.Start(962, new ToolOrder { Tool = "goto", Lord = "lord_ar", LordName = "Elessar", Party = "p_ar", At = "town_dg", Realm = "player_k" });
            Runner.ReleaseSink = keptSink;
            Check(letGo.Contains(962) && !Agents.Of("dg").Tools.ContainsKey(962) && !Agents.Of("orcs").Tools.ContainsKey(962) && Runner.ForParty("p_ar") == null,
                  "a tool for a realm no agent serves is not run on another realm's agent: the lord is let go");
            // and the player's own party is never among a realm's lords to order (a realm of the player's with an agent)
            Snapshot sp = ToolWorld(8001, host, new PartyRow { Id = "main_party", Leader = "main_hero", Name = "You", Faction = "player_k", Kind = "lord", Men = 100, Strength = 200, X = 108, Y = 104, Doing = "Hold" });
            View pv = Views.Build(sp, "player_k", new Dictionary<string, Sighting>(), new HashSet<string>(), null);
            Check(pv.Forces.Any(f => f.Id == "p_ar") && !pv.Forces.Any(f => f.Id == "main_party"), "the player's own party is not one of the realm's lords in its report");
            Check(Library.Text().Contains("Weigh the two paces first") && Library.Text().Contains("nothing better to do"), "the hunt order's guide says to weigh the paces, and when a faster target is worth it");

            string loads = ReadIf(Path.Combine(Brain.Dir, "loads.jsonl"));
            Check(loads.Contains("\"calls.jsonl@dg\"") && loads.Contains("\"calls.jsonl@orcs\"") && loads.Contains("warroom-dg"), "the load names each agent and marks its own war room's logs");

            // 0.11.4: each agent's poke and report name its own mode (run C2, Oct 23: the kingdom's agent, live, read "SHADOW ... none
            // is carried out" in every poke, the mode of the orcs, listed first)
            File.WriteAllText(Path.Combine(orcs, "agent.txt"), "# the orcs\nrealm = orcs\nmode = shadow\nlords = all\n");
            File.WriteAllText(Path.Combine(dg, "agent.txt"), "realm = dg\nmode = live\nlords = lord_khamul\nchannel = channel\n");
            Brain.Reset("smoketest3b", double.NaN, Agents.Configured(), null);
            Brain.OnHour(s);
            Files.FlushAll();
            string dgPoke = ReadIf(Path.Combine(dg, "channel", "poke.txt")), orcPoke = ReadIf(Path.Combine(orcs, "warroom", "poke.txt"));
            Check(dgPoke.Contains("Your orders right now: LIVE") && !dgPoke.Contains("SHADOW") && orcPoke.Contains("Your orders right now: SHADOW"),
                  "each agent's poke names its own mode, not the first agent's");
            AskBriefing(Agents.Of("dg").Channel, Path.Combine(dg, "channel"));
            Check(ReadIf(Path.Combine(dg, "channel", "report.txt")).Contains("Your orders right now: LIVE"), "and so does its situation report");

            Settings.AgentFolders.Clear();
            Settings.AgentDir = agentDir;
        }

        /// <summary>The war room's request for a council's briefing (0.12.0), served at once as the bridge's helper would.</summary>
        private static void AskBriefing(Channel ch, string channelDir)
        {
            string orders = Path.Combine(channelDir, "orders");
            Directory.CreateDirectory(orders);
            File.WriteAllText(Path.Combine(orders, Channel.BriefingRequest + "-test.json"), "{\"asked\":\"now\"}");
            ch.Scan();
            Files.FlushAll();
        }

        /// <summary>
        /// Version 0.12.0, the helper on time (run 20261025-1417-C2: up to 142 s behind the game, every order a game day late): the
        /// urgent lane runs before regular work; hours the reports fell behind on are folded into one poke with all they saw; an
        /// old picture says so; the same order given again is told apart from a changed one; look-ups are written only when changed;
        /// an hour saved for the speed test reads back whole; and the speed test runs on saved hours.
        /// </summary>
        private static void HelperTests(string root, string agentDir)
        {
            // the urgent lane: an order's job runs before regular work queued ahead of it
            var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var gate = new ManualResetEventSlim(false);
            Background.Post("test: slow", () => { gate.Wait(2000); order.Enqueue("slow"); });
            Background.Post("test: regular", () => order.Enqueue("regular"));
            Background.PostFirst("test: urgent", () => order.Enqueue("urgent"));
            gate.Set();
            for (int i = 0; i < 150 && order.Count < 3; i++) Thread.Sleep(20);
            List<string> ran = order.ToList();
            Check(ran.Count == 3 && ran.IndexOf("urgent") < ran.IndexOf("regular"), "the helper's urgent lane runs before regular work queued ahead of it (" + string.Join(", ", ran) + ")");

            // three hours whose reports fell behind: one poke, with the lord seen in the first hour only and the raid of the second
            string hDir = Path.Combine(root, "helper-agent");
            Settings.AgentDir = hDir;
            Brain.Reset("smoketest-helper", double.NaN, "orcs", null);
            string pokes = Path.Combine(Brain.Dir, "pokes_orcs.jsonl");
            Brain.OnHour(ToolWorld(20000));
            Files.FlushAll();
            int before = ReadIf(pokes).Split('\n').Count(l => l.Trim().Length > 0);
            var comer = new PartyRow { Id = "p_comer", Leader = "lord_comer", Name = "Elladan", Faction = "player_k", Kind = "lord", Men = 600, Strength = 900, Speed = 4, X = 108, Y = 100, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Brain.Decide(ToolWorld(20001, comer));
            Brain.OnNotice(new Notice { Id = 9001, Hours = 20001.5, Kind = "raided", Text = "Orc Village is being raided by Elladan (Kingdom of Arnor and Gondor).", Factions = { "orcs" }, Party = "p_comer" });
            Brain.Decide(ToolWorld(20002));
            Brain.Decide(ToolWorld(20003));
            for (Action a = Brain.NextStep(); a != null; a = Brain.NextStep()) a();
            Files.FlushAll();
            List<string> fresh = ReadIf(pokes).Split('\n').Where(l => l.Trim().Length > 0).Skip(before).ToList();
            Check(fresh.Count == 1 && fresh[0].Contains("\"enemy_in_view\"") && fresh[0].Contains("\"raided\"") && fresh[0].Contains("\"hours\":20003"),
                  "hours folded into one report: one poke, for the newest hour, with what each hour saw and the ruler's notice (" + fresh.Count + " pokes)");
            Check(Brain.NextStep() == null, "and nothing is left to report once the newest hour is");

            // a picture the game has moved on from says how old it is
            Background.PostHour(20010, () => { });
            Brain.OnNotice(new Notice { Id = 9002, Hours = 20004, Kind = "raided", Text = "Orc Village is being raided again.", Factions = { "orcs" }, Party = "p_comer" });
            Brain.OnHour(ToolWorld(20004));
            Files.FlushAll();
            Check(ReadIf(Path.Combine(hDir, "poke.txt")).Contains("This picture is 6 in-game hours old"), "a poke from an hour the game has moved on from says how old its picture is");
            Background.PostHour(double.NaN, () => { });

            // the same order given again, told apart from a changed one
            ToolOrder Engage() { var o = new ToolOrder { Tool = "engage", Lord = "lord_b", Party = "p_b", Target = "p_n" }; o.Settings["caution"] = 0; return o; }
            ToolOrder warier = Engage();
            warier.Settings["caution"] = 1.3;
            ToolOrder chained = Engage();
            chained.Then = JObject.Parse("{\"do\":\"garrison\",\"at\":\"castle_M3\"}");
            Check(Engage().Signature() == Engage().Signature() && Engage().Signature() != warier.Signature() && Engage().Signature() != chained.Signature(),
                  "the very same order is told apart from one with a changed setting or next order, so the same one given again keeps the running one");

            // look-ups written only when changed
            string lf = Path.Combine(root, "lookup-test.txt");
            bool w1 = Files.WriteIfChanged(lf, "a"), w2 = Files.WriteIfChanged(lf, "a"), w3 = Files.WriteIfChanged(lf, "b");
            File.Delete(lf);
            bool w4 = Files.WriteIfChanged(lf, "b");
            Check(w1 && !w2 && w3 && w4 && File.ReadAllText(lf) == "b", "a look-up file is written only when its text changed, and again when it went missing");

            // the open files with two threads at once (0.13.2: the worker's flush met a file this test had just opened, and the run ended)
            string many = Path.Combine(root, "files-race");
            Exception clash = null;
            var opener = new Thread(() => { for (int i = 0; i < 400; i++) Files.Append(Path.Combine(many, "f" + i + ".txt"), "a line"); });
            opener.Start();
            try { while (opener.IsAlive) Files.FlushAll(); } catch (Exception e) { clash = e; }
            opener.Join();
            for (int i = 0; i < 400; i++) Files.Close(Path.Combine(many, "f" + i + ".txt"));
            Check(clash == null && Directory.GetFiles(many).Length == 400 && File.ReadAllText(Path.Combine(many, "f399.txt")).StartsWith("a line"),
                  "the open files take lines on one thread while another flushes them all" + (clash != null ? " (" + clash.GetType().Name + ")" : ""));

            // an hour saved for the speed test reads back whole
            string pf = Path.Combine(root, "perf-test");
            Snapshot Seated(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                s.LoadId = "test";
                s.Seats = new List<SeatSight> { new SeatSight { Realm = "orcs", Sees = s.WatchedSees, Inspected = s.InspectedByWatched, Hostile = s.HostileToWatched } };
                s.Roads = new RoadBook();
                s.Roads.Set("castle_orc", "castle_two", 41.5f);
                return s;
            }
            PerfRecord.Save(Seated(20021, comer), pf);
            PerfRecord.Save(Seated(20020, comer), pf);
            List<Snapshot> back = PerfRecord.Load(pf);
            Check(back.Count == 2 && back[0].Hours == 20020 && back[0].PartyById.ContainsKey("p_comer") && back[0].PartyById["p_comer"].Men == 600 && back[0].SettlementById.ContainsKey("castle_orc")
                  && back[0].Roads != null && back[0].Roads.TryGet("castle_two", "castle_orc", out float rd) && rd == 41.5f && back[0].Seats.Count == 1 && back[0].Seats[0].Sees.ContainsKey("p_comer"),
                  "an hour saved for the speed test reads back whole: its parties, holdings, road table and each realm's sight, oldest first");

            // the speed test on those hours: each hour's orders and look, and its report round, timed by part
            PerfResult pr = PerfReplay(back, Path.Combine(root, "perf-run"), placed: false, quiet: true);
            Check(pr.Hours == 2 && pr.Orders.Count == 2 && pr.Reports.Count == 2 && pr.Parts.ContainsKey("view") && pr.Parts.ContainsKey("look"),
                  "the speed test runs the helper's hour on saved hours and times each part (" + string.Join(", ", pr.Parts.Keys) + ")");

            Settings.AgentFolders.Clear();
            Settings.AgentDir = agentDir;
        }

        internal sealed class PerfResult
        {
            public int Hours;
            public readonly List<double> Orders = new List<double>(), Reports = new List<double>();
            public readonly Dictionary<string, double> Parts = new Dictionary<string, double>();
            public long LookupsWritten, LookupsUnchanged;
        }

        /// <summary>
        /// "SmokeTest --perf [folder] [--budget ms]" (0.12.0): the helper's whole hour, its orders and look then its report round,
        /// on hours the game saved (PerfRecord, perf\snapshots\...) or, with no folder, on a big made-up world, with one watching
        /// agent per watched realm as in a run, on the helper's own cores and priority. Fails when the slowest hours (the 95th in
        /// a hundred) take more than the budget (1,000 ms: a third of a game hour at the runs' speed).
        /// </summary>
        private static int Perf(string[] args)
        {
            string folder = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;
            double budget = 1000;
            int bi = Array.IndexOf(args, "--budget");
            if (bi > 0 && bi + 1 < args.Length) double.TryParse(args[bi + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out budget);
            // 0.14: the agents' news mode for the test (full, shadow or theatre): a news report and, in theatre mode, a question book with each poke
            int ni = Array.IndexOf(args, "--news");
            if (ni > 0 && ni + 1 < args.Length) _perfNews = args[ni + 1];
            string root = Path.Combine(Path.GetTempPath(), "BannerlordAgents-perf");
            try { Directory.Delete(root, true); } catch { /* first run */ }
            Environment.SetEnvironmentVariable("BANNERLORD_AGENTS_ROOT", root);
            return PerfMain(root, folder, budget);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int PerfMain(string root, string folder, double budget)
        {
            Diag.StartLaunch();
            Cpu.Detect();
            List<Snapshot> hours = folder != null ? PerfRecord.Load(folder) : BigWorld(24);
            if (hours.Count == 0) { Console.WriteLine("No saved hours in " + folder); return 1; }
            Console.WriteLine("SPEED TEST of the helper's hour: " + hours.Count + " hours " + (folder != null ? "saved in " + folder : "of a big made-up world")
                              + ", " + hours[0].Parties.Count + " parties, " + hours[0].Settlements.Count + " holdings, " + Math.Max(1, hours[0].Seats.Count) + " watched realms; on " + Cpu.Description);
            // 0.13 (Part E): 30 of the agents' own tools running beside ours, as a realm keeping its whole library busy would
            PerfResult r = PerfReplay(hours, Path.Combine(root, "agents"), placed: true, quiet: false, ownTools: OwnToolsInPerf);
            List<double> total = r.Orders.Zip(r.Reports, (a, b) => a + b).OrderBy(x => x).ToList();
            double p95 = total[(int)Math.Min(total.Count - 1, Math.Ceiling(total.Count * 0.95) - 1)];
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Console.WriteLine("each hour's orders and look: median " + Median(r.Orders).ToString("0.0", inv) + " ms, most " + r.Orders.Max().ToString("0.0", inv)
                              + " ms; report round: median " + Median(r.Reports).ToString("0.0", inv) + " ms, most " + r.Reports.Max().ToString("0.0", inv) + " ms");
            Console.WriteLine("the whole hour: median " + Median(total).ToString("0.0", inv) + " ms, 95th of a hundred " + p95.ToString("0.0", inv) + " ms, most " + total.Max().ToString("0.0", inv) + " ms; budget " + budget.ToString("0", inv) + " ms");
            Console.WriteLine("by part, over all hours: " + string.Join("; ", r.Parts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value.ToString("0", inv) + " ms")));
            Console.WriteLine("look-up files written " + r.LookupsWritten + ", left alone unchanged " + r.LookupsUnchanged);
            bool ok = p95 <= budget;
            Console.WriteLine(ok ? "WITHIN BUDGET" : "OVER BUDGET");
            Diag.EndLaunch("the speed test ended");
            return ok ? 0 : 1;
        }

        private static double Median(List<double> xs) { List<double> s = xs.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }

        /// <summary>Runs the hours through the helper's two parts with a watching agent per watched realm (placed: on the helper's cores and priority).</summary>
        /// <summary>How many of the agents' own tools (the circuit) the speed test runs (0.13: 30, a realm's whole library).</summary>
        private const int OwnToolsInPerf = 30;
        private static string _perfNews = "full";

        private static PerfResult PerfReplay(List<Snapshot> hours, string agentsRoot, bool placed, bool quiet, int ownTools = 0)
        {
            Settings.Notify = false;
            Settings.Dashboard = true;
            Settings.PerfRecordHours = 0;
            Live.CampaignRunning = true;
            List<string> realms = hours[0].Seats.Count > 0 ? hours[0].Seats.Select(x => x.Realm).ToList() : new List<string> { hours[0].Watched };
            Settings.AgentFolders.Clear();
            foreach (string realm in realms)
            {
                string f = Path.Combine(agentsRoot, realm);
                Directory.CreateDirectory(f);
                File.WriteAllText(Path.Combine(f, "agent.txt"), "realm = " + realm + "\nmode = shadow\nlords = all\nchannel = warroom\nnews = " + _perfNews + "\n");
                Settings.AgentFolders.Add(f);
            }
            Brain.Reset(hours[0].Campaign ?? "perf", double.NaN, Agents.Configured(), null);
            var r = new PerfResult();
            long written0 = Diag.CountOf("files:look-ups written"), unchanged0 = Diag.CountOf("files:look-ups unchanged");
            if (ownTools > 0) StartOwnTools(hours[0], ownTools);
            void Work()
            {
                if (placed) Cpu.MakeCurrentThreadBackground();
                foreach (Snapshot s in hours)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (placed) Cpu.SetCurrentThreadUrgent(true);
                    Brain.Decide(s);
                    if (placed) Cpu.SetCurrentThreadUrgent(false);
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (Action a = Brain.NextStep(); a != null; a = Brain.NextStep()) a();
                    Files.FlushAll();
                    long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                    double orders = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, reports = (t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    r.Hours++;
                    r.Orders.Add(orders);
                    r.Reports.Add(reports);
                    foreach (KeyValuePair<string, double> kv in Brain.LastOrdersMs.Concat(Brain.LastRoundMs))
                        r.Parts[kv.Key] = (r.Parts.TryGetValue(kv.Key, out double p) ? p : 0) + kv.Value;
                    if (!quiet) Console.WriteLine("  " + Text.When(s.Hours) + ": orders and look " + orders.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                                                  + " ms, report round " + reports.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
                }
            }
            if (placed)
            {
                var th = new Thread(Work) { IsBackground = true, Name = "speed test (as the helper)" };
                th.Start();
                th.Join();
            }
            else Work();
            r.LookupsWritten = Diag.CountOf("files:look-ups written") - written0;
            r.LookupsUnchanged = Diag.CountOf("files:look-ups unchanged") - unchanged0;
            return r;
        }

        /// <summary>
        /// The agents' own tools for the speed test (0.13): the circuit, kept by the first watched realm, on its first lords, each with three of
        /// its holdings nearest him to go round and its nearest castle or town as home; their hours are timed with the rest, and a tool the time
        /// guard stops is said.
        /// </summary>
        private static void StartOwnTools(Snapshot first, int count)
        {
            string realm = first.Seats.Count > 0 ? first.Seats[0].Realm : first.Watched;
            ToolDef circuit = ToolDef.FromJson(CircuitJson, "circuit", realm);
            List<PartyRow> lords = first.Parties.Where(p => p.Faction == realm && p.Kind == "lord").Take(count).ToList();
            List<SettlementRow> own = first.Settlements.Where(h => h.Faction == realm).ToList();
            double D(PartyRow p, SettlementRow h) => (p.X - h.X) * (p.X - h.X) + (p.Y - h.Y) * (p.Y - h.Y);
            int id = 99000, started = 0;
            var stopped = new List<int>();
            Runner.MoveSink = (i, m) => { };
            Runner.ThenSink = (i, t) => { };
            Runner.ReleaseSink = (i, why) => { if (why.Contains("took too long")) stopped.Add(i); };
            foreach (PartyRow p in lords)
            {
                SettlementRow home = own.Where(h => h.Kind == "town" || h.Kind == "castle").OrderBy(h => D(p, h)).FirstOrDefault();
                List<SettlementRow> round = own.OrderBy(h => D(p, h)).Take(3).ToList();
                if (home == null || round.Count == 0) continue;
                var o = new ToolOrder { Tool = "circuit", Def = circuit, Lord = p.Leader, LordName = p.Name, Party = p.Id, At = home.Id, Realm = realm };
                o.Places.AddRange(round.Select(h => h.Id));
                Runner.Start(++id, o);
                started++;
            }
            Console.WriteLine("  " + started + " of the agents' own tools (the circuit) running for " + realm + "; the time guard stops one over " + Runner.SlowMs + " ms an hour for " + Runner.SlowHoursToStop + " hours running");
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { if (stopped.Count > 0) Console.WriteLine("  the time guard stopped " + stopped.Count + " of them"); };
        }

        /// <summary>A big made-up world for the speed test with no saved hours: nine realms, three of them watched, 828 holdings, about 3,400 parties moving a little each hour.</summary>
        private static List<Snapshot> BigWorld(int count)
        {
            var rnd = new Random(42);
            string[] realms = { "orcs", "dg", "kingdom", "rohan", "dunland", "rhun", "harad", "gundabad", "elves" };
            string[] watched = { "orcs", "dg", "kingdom" };
            var war = new Dictionary<string, string[]>
            {
                ["orcs"] = new[] { "kingdom", "rohan", "elves" }, ["dg"] = new[] { "kingdom", "elves", "rohan" }, ["kingdom"] = new[] { "orcs", "dg", "dunland", "gundabad", "rhun" },
                ["rohan"] = new[] { "orcs", "dg", "dunland" }, ["dunland"] = new[] { "kingdom", "rohan" }, ["rhun"] = new[] { "kingdom" }, ["harad"] = new string[0],
                ["gundabad"] = new[] { "kingdom", "elves" }, ["elves"] = new[] { "orcs", "dg", "gundabad" }
            };
            var centre = new Dictionary<string, float[]>();
            for (int i = 0; i < realms.Length; i++) centre[realms[i]] = new[] { 170f + 330 * (i % 3), 170f + 330 * (i / 3) };
            float Near(float c) => c + (float)(rnd.NextDouble() * 300 - 150);
            var holdings = new List<SettlementRow>();
            foreach (string realm in realms)
                for (int k = 0; k < 92; k++)
                {
                    string kind = k < 6 ? "town" : k < 26 ? "castle" : "village";
                    float x = Near(centre[realm][0]), y = Near(centre[realm][1]);
                    holdings.Add(new SettlementRow
                    {
                        Id = kind + "_" + realm + "_" + k, Name = realm + " " + kind + " " + k, Kind = kind, Faction = realm, X = x, Y = y, Gx = x, Gy = y + 1,
                        Garrison = kind == "village" ? 0 : 300, GarrisonStrength = kind == "village" ? 0 : 450, Militia = 100, WallLevel = kind == "village" ? 0 : 1 + k % 3,
                        VillageState = kind == "village" ? "Normal" : null, Bound = kind == "village" ? "castle_" + realm + "_" + (6 + k % 20) : null
                    });
                }
            var start = new List<PartyRow>();
            foreach (string realm in realms)
            {
                int lords = realm == "kingdom" ? 76 : realm == "orcs" ? 58 : realm == "dg" ? 48 : 110;
                for (int k = 0; k < lords; k++)
                    start.Add(new PartyRow { Id = "p_" + realm + "_" + k, Leader = "lord_" + realm + "_" + k, Name = realm + " lord " + k, Faction = realm, Kind = "lord", Men = 100 + rnd.Next(600), SizeLimit = 800,
                                             Strength = 200 + rnd.Next(1200), Speed = 3 + (float)rnd.NextDouble() * 2, FoodDays = 10, X = Near(centre[realm][0]), Y = Near(centre[realm][1]), Doing = k % 3 == 0 ? "PatrolAroundPoint" : "Hold", Spottable = true });
                for (int k = 0; k < 66; k++)
                    start.Add(new PartyRow { Id = "car_" + realm + "_" + k, Name = "Caravan " + k, Faction = realm, Kind = "caravan", Men = 30, Strength = 40, Speed = 4, X = Near(centre[realm][0]), Y = Near(centre[realm][1]), Doing = "GoToSettlement", Spottable = true });
                for (int k = 0; k < 90; k++)
                    start.Add(new PartyRow { Id = "vil_" + realm + "_" + k, Name = "Villagers " + k, Faction = realm, Kind = "villager", Men = 20, Strength = 15, Speed = 3, X = Near(centre[realm][0]), Y = Near(centre[realm][1]), Doing = "GoToSettlement", Spottable = true });
            }
            for (int k = 0; k < 700; k++)
                start.Add(new PartyRow { Id = "band_" + k, Name = "Looters " + k, Faction = "looters", Kind = "bandit", Men = 15 + rnd.Next(60), Strength = 20 + rnd.Next(80), Speed = 4, X = (float)rnd.NextDouble() * 1000, Y = (float)rnd.NextDouble() * 1000, Doing = "Hold", Spottable = true });
            var list = new List<Snapshot>();
            for (int h = 0; h < count; h++)
            {
                var s = new Snapshot { Campaign = "perf", LoadId = "big", Hours = 30000 + h, Date = "Autumn 1, 1087", PlayerFaction = "kingdom", Watched = "orcs" };
                foreach (string realm in realms)
                {
                    s.Factions[realm] = new FactionRow { Id = realm, Name = realm, Ruler = "lord_" + realm + "_0", Lords = 110 };
                    s.Factions[realm].AtWar.AddRange(war[realm]);
                    s.FactionNames[realm] = realm;
                }
                s.FactionNames["looters"] = "Looters";
                foreach (SettlementRow x in holdings) Add(s, x);
                foreach (PartyRow p in start)
                {
                    float dx = (float)(rnd.NextDouble() * 4 - 2), dy = (float)(rnd.NextDouble() * 4 - 2);
                    Add(s, new PartyRow { Id = p.Id, Leader = p.Leader, Name = p.Name, Faction = p.Faction, Kind = p.Kind, Men = p.Men, SizeLimit = p.SizeLimit, Strength = p.Strength, Speed = p.Speed,
                                          FoodDays = p.FoodDays, X = p.X + dx * h * 0.25f, Y = p.Y + dy * h * 0.25f, Doing = p.Doing, Spottable = true });
                }
                foreach (string realm in watched)
                {
                    var hostile = new HashSet<string>(war[realm]);
                    var eyes = s.Parties.Where(p => p.Faction == realm && p.Kind == "lord").Select(p => new[] { p.X, p.Y })
                                        .Concat(s.Settlements.Where(x => x.Faction == realm && x.Kind != "village").Select(x => new[] { x.X, x.Y })).ToList();
                    var sees = new Dictionary<string, Sighting>();
                    var inspected = new HashSet<string>();
                    foreach (PartyRow p in s.Parties)
                    {
                        if (p.Faction == realm) continue;
                        foreach (float[] e in eyes)
                        {
                            float d = (float)Math.Sqrt((p.X - e[0]) * (p.X - e[0]) + (p.Y - e[1]) * (p.Y - e[1]));
                            if (d <= 40) { sees[p.Id] = new Sighting { Party = p.Id, By = realm, Distance = d }; break; }
                        }
                    }
                    foreach (SettlementRow x in s.Settlements)
                        if (x.Faction == realm || eyes.Any(e => Math.Abs(x.X - e[0]) < 40 && Math.Abs(x.Y - e[1]) < 40)) inspected.Add(x.Id);
                    s.Seats.Add(new SeatSight { Realm = realm, Sees = sees, Inspected = inspected, Hostile = hostile });
                }
                s.WatchedSees = s.Seats[0].Sees;
                s.InspectedByWatched = s.Seats[0].Inspected;
                s.HostileToWatched = s.Seats[0].Hostile;
                list.Add(s);
            }
            return list;
        }

        private static Snapshot ToolWorld(double hours, params PartyRow[] parties)
        {
            var s = new Snapshot { Campaign = "smoketest", Hours = hours, Date = "Summer 9, 1085", PlayerFaction = "player_k", PlayerParty = "main_party", Watched = "orcs" };
            s.Factions["orcs"] = new FactionRow { Id = "orcs", Name = "Misty Mountain Orcs", Ruler = "Azog", AtWar = { "player_k" } };
            s.Factions["player_k"] = new FactionRow { Id = "player_k", Name = "Kingdom of Arnor and Gondor", Ruler = "Aragorn", AtWar = { "orcs" } };
            s.FactionNames["orcs"] = "Misty Mountain Orcs";
            s.FactionNames["player_k"] = "Kingdom of Arnor and Gondor";
            s.HostileToWatched.Add("player_k");
            s.HostileToPlayer.Add("orcs");
            Add(s, new SettlementRow { Id = "castle_orc", Name = "Orc Castle", Kind = "castle", Faction = "orcs", X = 100, Y = 100, Garrison = 450, GarrisonStrength = 600, Militia = 120, WallLevel = 1 });
            Add(s, new SettlementRow { Id = "castle_two", Name = "Orc Keep", Kind = "castle", Faction = "orcs", X = 140, Y = 100, Garrison = 200, GarrisonStrength = 250, Militia = 80, WallLevel = 1 });
            Add(s, new SettlementRow { Id = "village_orc", Name = "Orc Village", Kind = "village", Faction = "orcs", X = 110, Y = 104, Militia = 40, Bound = "castle_orc", VillageState = "Normal" });
            Add(s, new SettlementRow { Id = "castle_men", Name = "Fornost", Kind = "castle", Faction = "player_k", X = 170, Y = 100, Garrison = 600, Militia = 200 });
            Add(s, new SettlementRow { Id = "village_men", Name = "Bree", Kind = "village", Faction = "player_k", X = 160, Y = 110, Militia = 30, Bound = "castle_men", VillageState = "Normal" });
            s.WatchedSees = new Dictionary<string, Sighting>();
            foreach (PartyRow r in parties)
            {
                Add(s, r);
                if (r.Faction != "orcs") s.WatchedSees[r.Id] = new Sighting { Party = r.Id, By = "Bolg", Distance = 10f };
            }
            s.InspectedByWatched = new HashSet<string> { "castle_orc", "castle_two", "village_orc" };
            return s;
        }

        /// <summary>
        /// "SmokeTest --library file": writes the agent's order library (the same text the bridge writes into the channel
        /// as tools.txt) without the game, so the war room has it before the first game session.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int ExportLibrary(string path)
        {
            List<string> problems = Library.Problems();
            if (problems.Count > 0) { Console.WriteLine("The tool library has problems: " + string.Join("; ", problems)); return 1; }
            File.WriteAllText(path, Library.Text(), new System.Text.UTF8Encoding(false));
            Console.WriteLine("Order library written: " + path + " (" + Library.All.Count + " orders)");
            return 0;
        }

        /// <summary>A stand-in for a frozen game: one thread spins in a named method, the main thread waits.</summary>
        private static int Spin()
        {
            var spinner = new Thread(SpinForever) { IsBackground = true, Name = "spinner" };
            spinner.Start();
            Console.WriteLine("pid " + System.Diagnostics.Process.GetCurrentProcess().Id + " main " + GetCurrentThreadId());
            Console.Out.Flush();
            WaitHere();
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void WaitHere() => new ManualResetEvent(false).WaitOne();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SpinForever()
        {
            long n = 0;
            while (true) n = n * 31 + 7;
        }

        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        /// <summary>The module keeps its files open for writing (shared), so read them the same way.</summary>
        private static string ReadIf(string path) => File.Exists(path) ? Read(path) : "";

        private static string Read(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  pass: " : "  FAIL: ") + what);
            if (!ok) _failures++;
        }

        private static Snapshot World(double hours, bool playerSeen, params PartyRow[] extra)
        {
            var s = new Snapshot { Campaign = "smoketest", Hours = hours, Date = "Summer 3, 1085", PlayerFaction = "player_k", PlayerParty = "main_party", Watched = "orcs" };
            s.Factions["orcs"] = new FactionRow { Id = "orcs", Name = "Misty Mountain Orcs", Ruler = "Azog", AtWar = { "player_k" } };
            s.Factions["player_k"] = new FactionRow { Id = "player_k", Name = "Kingdom of Arnor and Gondor", Ruler = "Aragorn", AtWar = { "orcs" } };
            s.FactionNames["orcs"] = "Misty Mountain Orcs";
            s.FactionNames["player_k"] = "Kingdom of Arnor and Gondor";
            s.FactionNames["looters"] = "Looters";
            s.HostileToWatched.Add("player_k");
            s.HostileToWatched.Add("looters");
            s.HostileToPlayer.Add("orcs");
            s.HostileToPlayer.Add("looters");

            Add(s, new SettlementRow { Id = "castle_orc", Name = "Orc Castle", Kind = "castle", Faction = "orcs", X = 100, Y = 100, Garrison = 450, Militia = 120 });
            Add(s, new SettlementRow { Id = "village_orc", Name = "Orc Village", Kind = "village", Faction = "orcs", X = 110, Y = 104, Militia = 40, Bound = "castle_orc",
                                       VillageState = hours >= 1002 ? "BeingRaided" : "Normal" });
            Add(s, new SettlementRow { Id = "castle_men", Name = "Fornost", Kind = "castle", Faction = "player_k", X = 170, Y = 100, Garrison = 600, Militia = 200 });

            Add(s, new PartyRow { Id = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, Strength = 410, X = 104, Y = 100, Doing = "Hold", InSettlement = "castle_orc", SeenByPlayer = playerSeen, Spottable = true });
            Add(s, new PartyRow { Id = "lord_azog", Name = "Azog", Faction = "orcs", Kind = "lord", Men = 400, ArmyMen = 900, ArmyParties = 3, ArmyLeader = "lord_azog", Strength = 1300, X = 90, Y = 120, Doing = "PatrolAroundPoint", TargetSettlement = "castle_orc", Spottable = true });
            float px = playerSeen ? 112 : 150;
            Add(s, new PartyRow { Id = "main_party", Name = "Aragorn", Faction = "player_k", Kind = "lord", Men = 500, Strength = 900, X = px, Y = 104, Doing = hours >= 1002 ? "RaidSettlement" : "GoToSettlement", TargetSettlement = hours >= 1002 ? "village_orc" : "castle_orc", Spottable = true });
            Add(s, new PartyRow { Id = "looters_1", Name = "Looters", Faction = "looters", Kind = "bandit", Men = 12, Strength = 8, X = 98, Y = 95, Doing = "PatrolAroundPoint", Spottable = true });

            s.WatchedSees = new Dictionary<string, Sighting> { ["looters_1"] = new Sighting { Party = "looters_1", By = "Orc Castle", Distance = 5.4f } };
            if (playerSeen) s.WatchedSees["main_party"] = new Sighting { Party = "main_party", By = "Bolg", Distance = 8.2f };
            foreach (PartyRow r in extra)
            {
                Add(s, r);
                s.WatchedSees[r.Id] = new Sighting { Party = r.Id, By = "Azog", Distance = 12f };
            }
            s.InspectedByWatched = new HashSet<string> { "castle_orc", "village_orc" };
            return s;
        }

        private static void Add(Snapshot s, SettlementRow r) { s.Settlements.Add(r); s.SettlementById[r.Id] = r; }
        private static void Add(Snapshot s, PartyRow r) { s.Parties.Add(r); s.PartyById[r.Id] = r; }
    }
}
