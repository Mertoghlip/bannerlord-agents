using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using BannerlordAgents;

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

            Brain.Reset(campaign);
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
