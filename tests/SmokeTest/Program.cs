using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents;

namespace SmokeTest
{
    /// <summary>
    /// A small made-up world: an orc realm with a castle, a village and two lords, the player's realm with a castle,
    /// and the player's army closing in. Three hours are run through the worker exactly as the game would hand them
    /// over, then the report, the pokes and a recorded decision are printed and the files checked.
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            const string campaign = "smoketest";
            string dir = Paths.Data(campaign);
            try { Directory.Delete(dir, true); } catch { }
            Settings.Notify = false;
            string agentDir = Path.Combine(dir, "agent");
            Settings.Agent = "shadow";
            Settings.AgentDir = agentDir;

            Brain.Reset(campaign);
            Brain.OnHour(World(1000, playerSeen: false));                    // first look
            Brain.OnHour(World(1001, playerSeen: true));                     // the player's army comes into view
            Brain.OnNotice(new Notice { Hours = 1001.5, Kind = "raided", Text = "Orc Village is being raided by Aragorn (Arnor and Gondor).", Factions = { "orcs" }, Party = "main_party" });
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
                SeenByYou = new List<SeenParty> { new SeenParty { Id = "lord_bolg", Name = "Bolg", Men = 300, Hostile = true, Distance = 25 } }
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
            string decisions = Read(Path.Combine(dir, "record", "decisions.jsonl"));
            Check(decisions.Contains("\"realm\"") && decisions.Contains("\"recent_pokes\""), "the decision record carries the realm and the recent pokes");
            Check(File.Exists(Path.Combine(dir, "reports", "orcs_latest.txt")), "the latest report file exists");
            Check(now.Contains("Bolg [lord_bolg]") && now.Contains("Orc Castle [castle_orc]"), "the report shows ids in brackets for orders");

            // the agent channel: what the agent reads, and one answer file read back and receipted
            string pokesLog = Read(Path.Combine(agentDir, "pokes.log"));
            Check(pokesLog.Split('\n').Count(l => l.StartsWith("#")) == 3, "pokes.log has one line per poke");
            string pokeTxt = Read(Path.Combine(agentDir, "poke.txt"));
            Check(pokeTxt.StartsWith("POKE #3") && pokeTxt.Contains("SHADOW") && pokeTxt.Contains("WHY YOU ARE ASKED NOW"), "poke.txt carries the number, the mode and the reasons");
            Check(File.Exists(Path.Combine(agentDir, "report.txt")), "report.txt is refreshed every hour");
            string answer = Path.Combine(agentDir, "orders", "answer-3.json");
            Directory.CreateDirectory(Path.GetDirectoryName(answer));
            File.WriteAllText(answer, "{\"poke\": 3, \"orders\": [{\"lord\": \"lord_bolg\", \"do\": \"garrison\", \"target\": \"castle_orc\", \"why\": \"hold\"},"
                                      + " {\"lord\": \"Azog\", \"do\": \"engage\", \"target\": \"main_party\"}], \"note\": \"test\"}");
            bool parsed = AgentChannel.Parse(answer, out int poke, out List<AgentChannel.AgentOrder> orders, out string error);
            Check(parsed && poke == 3 && orders.Count == 2 && orders[1].Do == "engage" && orders[0].Poke == 3, "an answer file is read: " + (error ?? orders.Count + " orders for poke " + poke));
            AgentChannel.Receipt(orders[0], "checked", "valid: Bolg garrisons Orc Castle (shadow mode: not carried out)", 1002.5);
            Files.FlushAll();
            string receipts = Read(Path.Combine(agentDir, "receipts.log"));
            Check(receipts.Contains("CHECKED") && receipts.Contains("Answered") && receipts.Contains("after poke #3"), "the receipt says what happened and how long the answer took");
            Check(!AgentChannel.Parse(answer + ".missing", out _, out _, out _), "a missing or broken file is reported, not guessed at");

            Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : "\n" + _failures + " CHECK(S) FAILED");
            Console.WriteLine("files: " + dir);
            return _failures == 0 ? 0 : 1;
        }

        /// <summary>The module keeps its files open for writing (shared), so read them the same way.</summary>
        private static string Read(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs)) return sr.ReadToEnd();
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  pass: " : "  FAIL: ") + what);
            if (!ok) _failures++;
        }

        private static Snapshot World(double hours, bool playerSeen)
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
            s.InspectedByWatched = new HashSet<string> { "castle_orc", "village_orc" };
            return s;
        }

        private static void Add(Snapshot s, SettlementRow r) { s.Settlements.Add(r); s.SettlementById[r.Id] = r; }
        private static void Add(Snapshot s, PartyRow r) { s.Parties.Add(r); s.PartyById[r.Id] = r; }
    }
}
