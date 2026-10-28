using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BannerlordAgents;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13 step 5, the allied channel (docs/PLAN-0.13.md B5): a letter and a joint plan from one agent realm to its ally, delivered in the
        /// ally's next poke, answered and agreed, shown in both reports and recorded; letters to a realm not allied, or to one no agent reads,
        /// refused; a letter given in an answer receipted; and the letters and plans read back at a new load.
        /// </summary>
        private static void AlliesTests(string root, string agentDir)
        {
            string orcDir = Path.Combine(root, "allies_orcs"), dgDir = Path.Combine(root, "allies_dg");
            Settings.AgentDir = orcDir;
            Settings.AgentDirs["dg"] = dgDir;
            Settings.Agent = "live";
            Brain.Reset("smoketest-allies", double.NaN, new List<string> { "orcs", "dg" }, null);
            Snapshot World(double h)
            {
                var bolg = new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, Strength = 400, Speed = 5, FoodDays = 10, X = 104, Y = 100, Doing = "Hold", Spottable = true };
                Snapshot s = ToolWorld(h, bolg);
                s.Factions["dg"] = new FactionRow { Id = "dg", Name = "Dol Guldur", Ruler = "Khamul", AtWar = { "player_k" }, Allies = { "orcs" } };
                s.Factions["rohan"] = new FactionRow { Id = "rohan", Name = "Rohan", Ruler = "Theoden", Allies = { "orcs" } };
                s.Factions["orcs"].Allies.AddRange(new[] { "dg", "rohan" });
                s.FactionNames["dg"] = "Dol Guldur";
                s.FactionNames["rohan"] = "Rohan";
                Add(s, new SettlementRow { Id = "town_dg", Name = "Dol Guldur", Kind = "town", Faction = "dg", X = 400, Y = 400, Garrison = 500, Militia = 100 });
                Add(s, new PartyRow { Id = "p_khamul", Leader = "lord_khamul", Name = "Khamul", Faction = "dg", Kind = "lord", Men = 400, Strength = 800, X = 402, Y = 400, Doing = "Hold", InSettlement = "town_dg" });
                s.Seats = new List<SeatSight>
                {
                    new SeatSight { Realm = "orcs", Sees = s.WatchedSees, Inspected = s.InspectedByWatched, Hostile = s.HostileToWatched },
                    new SeatSight { Realm = "dg", Sees = new Dictionary<string, Sighting>(), Inspected = new HashSet<string> { "town_dg" }, Hostile = new HashSet<string> { "player_k" } }
                };
                return s;
            }
            Snapshot s0 = World(70000);
            Brain.OnHour(s0);
            Files.FlushAll();
            string Send(string from, string json, out string status) => Allies.Send(Agents.Of(from).Latest, from, JObject.Parse(json), "agent", 1, out status);
            string why = Send("orcs", "{\"to\": \"player_k\", \"text\": \"Surrender.\"}", out string st1);
            string why2 = Send("orcs", "{\"to\": \"Rohan\", \"text\": \"Ride with us.\"}", out string st2);
            Check(st1 == "refused" && why.Contains("not your ally: letters go to allies only") && st2 == "refused" && why2.Contains("no one reads letters for Rohan"),
                  "a letter goes only to an ally whose ruler reads letters: " + why + " | " + why2);
            string sent = Send("orcs", "{\"to\": \"dg\", \"text\": \"Strike Lasgalor with me on the 12th.\", \"plan\": {\"what\": \"siege\", \"where\": \"Lasgalor\", \"when\": \"Summer 12\", \"parts\": {\"orcs\": \"3,000 men from the north\", \"Dol Guldur\": \"the southern gate\"}}}", out string st3);
            Allies.Plan plan = Allies.AllPlans().FirstOrDefault();
            Check(st3 == "sent" && sent.Contains("your letter l1 goes to Dol Guldur's ruler, with a joint plan, jp1, proposed") && plan != null && plan.State == "proposed" && plan.Parts.ContainsKey("dg") && plan.Parts["orcs"] == "3,000 men from the north",
                  "a letter to an ally carries a joint plan in its fixed form, each side's part by realm: " + sent);
            Brain.OnHour(World(70001));
            Files.FlushAll();
            Check(ReadIf(Path.Combine(dgDir, "poke.txt")).Contains("A letter from the ruler of Misty Mountain Orcs [orcs] (l1): \"Strike Lasgalor with me on the 12th.\" [Joint plan jp1: siege at Lasgalor, Summer 12")
                  && !ReadIf(Path.Combine(orcDir, "poke.txt")).Contains("A letter from the ruler"),
                  "the letter reaches the ally's agent in its next poke, saying who wrote it; the writer is not poked with it");
            why = Send("orcs", "{\"to\": \"dg\", \"text\": \"I agree with myself.\", \"plan_id\": \"jp1\", \"state\": \"agreed\"}", out string st4);
            Check(st4 == "refused" && why.Contains("only the realm a plan was proposed to agrees"), "only the realm a plan was proposed to may agree to it");
            sent = Send("dg", "{\"to\": \"orcs\", \"text\": \"Agreed: my riders take the southern gate at dawn.\", \"plan_id\": \"jp1\", \"state\": \"agreed\"}", out string st5);
            Check(st5 == "sent" && plan.State == "agreed" && sent.Contains("plan jp1 is now agreed"), "the ally answers and agrees to the plan");
            Snapshot s2 = World(70002);
            List<string> orcSide = Allies.Section(s2, "orcs"), dgSide = Allies.Section(s2, "dg");
            Check(orcSide.Any(l => l.StartsWith("Dol Guldur [dg]: your ally; its ruler reads letters") && l.Contains("its letter l2") && l.Contains("my riders take the southern gate"))
                  && orcSide.Any(l => l.StartsWith("Rohan [rohan]: your ally; its ruler is the game's own and reads no letters"))
                  && orcSide.Any(l => l.StartsWith("Joint plan jp1: siege at Lasgalor, Summer 12; parts: Misty Mountain Orcs: 3,000 men from the north; Dol Guldur: the southern gate; agreed"))
                  && dgSide.Any(l => l.StartsWith("Joint plan jp1")),
                  "ALLIES AND JOINT PLANS: each ally, who reads letters, its last letters, and the plan agreed, in both realms' reports:\n  " + string.Join("\n  ", orcSide));
            View v = Views.Build(s2, "orcs", s2.WatchedSees, s2.HostileToWatched, new Brain.Memory());
            Check(Views.Report(v, new List<Trigger>()).Contains("ALLIES AND JOINT PLANS (an ally knows only what it is told"), "the section is in the realm's report");
            Files.FlushAll();
            List<JObject> kept = Lines(ReadIf(Path.Combine(Brain.Dir, "allies.jsonl")));
            List<JObject> book = Lines(ReadIf(Path.Combine(Brain.Dir, "diplomacy.jsonl")));
            Check(kept.Count(l => (string)l["k"] == "letter") == 2 && kept.Count(l => (string)l["k"] == "plan") == 2 && book.Count(l => (string)l["type"] == "letter") == 2 && book.Count(l => (string)l["type"] == "plan") == 2,
                  "every letter and every change of a plan is kept (allies.jsonl) and in the diplomacy book");

            // an answer's letters, receipted; a new load reads the letters and plans back
            Files.FlushAll();
            int poke = (int?)JObject.Parse(ReadIf(Path.Combine(orcDir, "poke.json")))["number"] ?? -1;
            File.WriteAllText(Path.Combine(orcDir, "orders", "poke-" + poke + ".json"), "{\"poke\": " + poke + ", \"letters\": [{\"to\": \"dg\", \"text\": \"We march now.\", \"plan_id\": \"jp1\", \"state\": \"under way\"}, {\"to\": \"rohan\", \"text\": \"Hello?\"}]}");
            Thread.Sleep(400);
            string receipts = Path.Combine(orcDir, "receipts.log");
            int before = ReadIf(receipts).Length;
            AgentChannel.Scan();
            Files.FlushAll();
            string said = ReadIf(receipts).Substring(before);
            Check(said.Contains("letter 1 (to dg): SENT. your letter l3 goes to Dol Guldur's ruler, with plan jp1 is now under way") && said.Contains("letter 2 (to rohan): REFUSED. no one reads letters for Rohan"),
                  "an answer's letters are receipted one by one: " + said);
            Brain.Reset("smoketest-allies", double.NaN, new List<string> { "orcs", "dg" }, null);
            Check(Allies.AllPlans().Count == 1 && Allies.AllPlans()[0].State == "under way" && Allies.AllLetters().Count == 3,
                  "a new load of the same game reads its letters and plans back, as they stood");

            Settings.AgentDirs.Remove("dg");
            Settings.Agent = "shadow";
            Settings.AgentDir = agentDir;
        }
    }
}
