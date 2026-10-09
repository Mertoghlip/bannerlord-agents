using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using BannerlordAgents;
using BannerlordAgents.Tools;
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
    internal static class Program
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--spin") return Spin();
            if (args.Length > 1 && args[0] == "--library") return ExportLibrary(args[1]);
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
            Check(File.Exists(Path.Combine(agentDir, "report.txt")), "report.txt is refreshed every hour");
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
            Check(!Read(Path.Combine(dir, "chronicle_orcs.jsonl")).Contains("Theoden"), "in the campaign's own copy too");

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
            Check(Library.All.Count == 16, "the library holds 16 orders (7 moves, 7 tools, 2 for armies), got " + Library.All.Count);
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
            var host = new PartyRow { Id = "p_host", Leader = "lord_host", Name = "Theoden", Faction = "player_k", Kind = "lord", Men = 1500, ArmyMen = 2000, ArmyParties = 4, ArmyStrength = 2600, Strength = 900, Speed = 4, X = 112, Y = 102, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(3001, bolg, host));
            Check(moves.Any(m => m.Key == 901 && m.Value.Verb == "enter" && m.Value.Place == "castle_orc"), "defend shelters inside from an army too strong for it");
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
            var hunter = new PartyRow { Id = "p_hunter", Leader = "lord_hunter", Name = "Elrohir", Faction = "player_k", Kind = "lord", Men = 500, Strength = 700, Speed = 6, X = 133, Y = 101, Doing = "Hold", Spottable = true };
            moves.Clear();
            Brain.OnHour(ToolWorld(3008, bolg, azog, hunter));
            Check(moves.Any(m => m.Key == 902 && m.Value.Verb == "enter" && m.Value.Place == "castle_two"), "a lord on an order shelters in the nearest friendly castle from an enemy far stronger");
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
            Check(notes.Contains("Enemies too strong for Bolg") && notes.Contains("is sheltering in Orc Keep from Elrohir") && notes.Contains("army has formed at Orc Keep"),
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

            // poke numbers: a campaign's poke file holds a whole report per line, longer than any tail; the highest number
            // is read from the whole file once, then kept beside it
            string numbered = Path.Combine(dir, "pokes_numbers_test.jsonl");
            File.WriteAllText(numbered, "{\"type\":\"poke\",\"number\":125,\"report\":\"" + new string('x', 200000) + "\"}\n{\"type\":\"poke\",\"number\":9,\"report\":\"short\"}\n");
            Check(AgentChannel.HighestIn(numbered) == 125, "the highest poke number is found in a poke file of long lines (" + AgentChannel.HighestIn(numbered) + ")");

            Runner.Reset();
        }

        private sealed class TestScope : IScope
        {
            public object Get(string name) => name == "x" ? (object)2.0 : null;
            public object Call(string fn, object[] a) => fn == "twice" ? (object)(Expr.Number(a[0]) * 2) : null;
        }

        /// <summary>A small orc realm for the tools: two castles and a village of theirs, a castle and a village of the player's.</summary>
        private static Snapshot ToolWorld(double hours, params PartyRow[] parties)
        {
            var s = new Snapshot { Campaign = "smoketest", Hours = hours, Date = "Summer 9, 1085", PlayerFaction = "player_k", PlayerParty = "main_party", Watched = "orcs" };
            s.Factions["orcs"] = new FactionRow { Id = "orcs", Name = "Misty Mountain Orcs", Ruler = "Azog", AtWar = { "player_k" } };
            s.Factions["player_k"] = new FactionRow { Id = "player_k", Name = "Kingdom of Arnor and Gondor", Ruler = "Aragorn", AtWar = { "orcs" } };
            s.FactionNames["orcs"] = "Misty Mountain Orcs";
            s.FactionNames["player_k"] = "Kingdom of Arnor and Gondor";
            s.HostileToWatched.Add("player_k");
            s.HostileToPlayer.Add("orcs");
            Add(s, new SettlementRow { Id = "castle_orc", Name = "Orc Castle", Kind = "castle", Faction = "orcs", X = 100, Y = 100, Garrison = 450, Militia = 120 });
            Add(s, new SettlementRow { Id = "castle_two", Name = "Orc Keep", Kind = "castle", Faction = "orcs", X = 140, Y = 100, Garrison = 200, Militia = 80 });
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
