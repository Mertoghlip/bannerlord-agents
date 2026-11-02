using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// Version 0.13.5 (Mert, Oct 29: "make sure that the test logs and the agents' records survive the game being closed and reloaded
        /// into the same save, so the test continues"): a tool run written down as it stands, cleared as a load clears it, and taken up
        /// again from what was written goes on exactly as it would have: its state and the hour it entered it, its remembered values (a
        /// table of the circuit's visits, a guard's refill check), its settings and the move in force, given again.
        /// </summary>
        private static void CarryOnTests(List<KeyValuePair<int, Move>> moves, List<KeyValuePair<int, string>> released)
        {
            // remembered values of every kind go out and come back as they were
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["n"] = 3.5, ["yes"] = true, ["words"] = "castle_two", ["nothing"] = null, ["list"] = new List<object> { "a", 2.0, null },
                ["table"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["town_orc"] = 40001.0, ["inner"] = new List<object> { true } }
            };
            var back = Runner.FromJson(JToken.Parse(Runner.ToJson(values).ToString())) as Dictionary<string, object>;
            Check(back != null && Expr.Number(back["n"]) == 3.5 && (bool)back["yes"] && (string)back["words"] == "castle_two" && back["nothing"] == null
                  && back["list"] is List<object> l && l.Count == 3 && (string)l[0] == "a" && Expr.Number(l[1]) == 2 && l[2] == null
                  && back["table"] is Dictionary<string, object> t && Expr.Number(t["TOWN_ORC"]) == 40001 && t["inner"] is List<object> il && (bool)il[0],
                  "remembered values (numbers, yes/no, words, none, lists, tables, a table's keys in any case) are written out and read back as they were");

            // the circuit, an agent's own tool, three stops into its round: written down at a "save", cleared, taken up again
            ToolDef circuit = ToolDef.FromJson(CircuitJson, "circuit", "orcs");
            var lord = new PartyRow { Id = "p_keep", Leader = "lord_keep", Name = "Shagrat", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 300, Strength = 400, Speed = 5, FoodDays = 20, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true, Loot = 100, Gold = 2000 };
            Snapshot World(double h)
            {
                Snapshot s = ToolWorld(h, lord);
                Add(s, new SettlementRow { Id = "town_orc", Name = "Goblin Town", Kind = "town", Faction = "orcs", X = 120, Y = 130, Garrison = 300, GarrisonStrength = 400, WallLevel = 2 });
                return s;
            }
            Move? Last(int id) => moves.Where(m => m.Key == id).Select(m => (Move?)m.Value).LastOrDefault();
            var order = new ToolOrder { Tool = "circuit", Def = circuit, Lord = "lord_keep", LordName = "Shagrat", Party = "p_keep", At = "castle_orc", Realm = "orcs", Poke = 77 };
            order.Places.AddRange(new[] { "village_orc", "castle_two" });
            order.Settings["radius"] = 12;
            double h0 = 41000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            Runner.Start(9800, order);
            lord.InSettlement = "town_orc"; lord.X = 120; lord.Y = 130;
            Brain.OnHour(World(h0 + 1));
            lord.InSettlement = null; lord.X = 110; lord.Y = 104;
            Brain.OnHour(World(h0 + 2));
            lord.InSettlement = "castle_two"; lord.X = 140; lord.Y = 100;
            Brain.OnHour(World(h0 + 3));
            bool home = Last(9800)?.Verb == "recruit" && Last(9800)?.Place == "castle_orc";
            JObject saved = Runner.Export(9800);
            JObject record = Outcomes.Export(9800);
            string text = new JObject { ["tool"] = saved, ["record"] = record }.ToString();
            Check(home && saved != null && (string)saved["state"] == "home" && saved["memory"]?["visited"] is JObject v && v.Count == 3 && (double?)saved["settings"]?["radius"] == 12,
                  "a tool run is written down as it stands: its state, its table of visits, its settings (" + (string)saved?["state"] + ")");
            // the load clears every order; the save's file brings this one back
            Runner.Ended(9800, "the game was loaded");
            moves.Clear();
            JObject file = JObject.Parse(text);
            string why = Runner.Resume((JObject)file["tool"], file["record"] as JObject);
            Check(why == null && Last(9800)?.Verb == "recruit" && Last(9800)?.Place == "castle_orc" && Runner.InForce(Runner.WorldFor(World(h0 + 3))).Contains("recruiting at home in Orc Castle"),
                  "taken up again, it is in the same state, its move in force given again (" + (why ?? Last(9800)?.ToString()) + ")");
            Check(Runner.NewId() > 9800, "new tool runs are numbered past the one carried on");
            lord.InSettlement = "castle_orc"; lord.X = 100; lord.Y = 100;
            Brain.OnHour(World(h0 + 4));
            Check(Last(9800)?.Verb == "patrol" && Math.Abs(Last(9800).Value.R - 12) < 0.01, "and goes on as before: home done, it patrols within its own radius setting (" + Last(9800) + ")");
            lord.InSettlement = null; lord.X = 104; lord.Y = 100;
            int before = moves.Count;
            for (int h = 5; h <= 20; h++) Brain.OnHour(World(h0 + h));
            Check(!moves.Skip(before).Any(m => m.Key == 9800), "its visits came back with it: within the day it goes back to none of the places it saw before the save");
            Files.FlushAll();
            Check(Lines(ReadIf(Path.Combine(Brain.Dir, "toolbook.jsonl"))).Any(x => (string)x["type"] == "run_resumed" && (int?)x["tool_run"] == 9800 && (bool?)x["record_kept"] == true),
                  "the toolbook says the run was carried on, its record so far kept");
            Runner.Ended(9800, "test over");

            // ours: a guard two hours into its castle's own refill, carried on: the ride to the village comes at the same hour it would have
            var thin = new PartyRow { Id = "p_thin2", Leader = "lord_thin2", Name = "Gorbag", Faction = "orcs", Kind = "lord", Men = 50, SizeLimit = 300, Strength = 70, Speed = 4, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            Diag.Hours = h0 + 100;
            Brain.OnHour(ToolWorld(h0 + 100, thin));
            moves.Clear();
            Runner.Start(9801, new ToolOrder { Tool = "defend", Lord = "lord_thin2", LordName = "Gorbag", Party = "p_thin2", At = "castle_two", Realm = "orcs" });
            Brain.OnHour(ToolWorld(h0 + 101, thin));
            JObject guard = Runner.Export(9801);
            Runner.Ended(9801, "the game was loaded");
            string gwhy = Runner.Resume(JObject.Parse(guard.ToString()), null);
            // arrived in its first hour (h0 + 100), so without the reload it would ride on at h0 + 102
            bool same = gwhy == null && Last(9801)?.Verb == "recruit" && Last(9801)?.Place == "castle_two" && moves.Count(m => m.Key == 9801) == 2;
            Brain.OnHour(ToolWorld(h0 + 102, thin));
            Check((string)guard["state"] == "restock" && same && Last(9801)?.Verb == "recruit" && Last(9801)?.Place == "village_orc",
                  "a guard carried on an hour into its castle's refill rides on to the village at the hour it would have (" + (string)guard["state"] + ", " + same + ", " + Last(9801) + ")");
            Runner.Ended(9801, "test over");

            // what cannot be carried on is said, not guessed at
            JObject broken = JObject.Parse(guard.ToString());
            broken["state"] = "no_such_state";
            Check(Runner.Resume(broken, null)?.Contains("not one of its tool's") == true, "a run whose state its tool no longer has is not carried on, and says why");

            // 0.13.7 (Mert, Oct 29: on screen only his lieutenant's and the game's own): an order's line goes to the game's message log
            // only for the lieutenant's realm or his own orders (the console's, his vassal orders), never another agent's
            Check(Orders.Shown("agent", true) && Orders.Shown("hand", false) && Orders.Shown("you", false) && !Orders.Shown("agent", false) && !Orders.Shown("then", false),
                  "an order's line goes on screen for the lieutenant's orders and your own, never for another agent's");
            RulerChaseTests(moves);
            // 0.13.12: at a load, before the helper's reset fills its agents, the lieutenant's realm is known from the load's own settings, so its
            // carried-on orders for lords on a Party AI Controls order are its own (Oct 29, 21:12: two were refused)
            List<AgentConfig> kept = Bridge.Configs;
            Bridge.Configs = new List<AgentConfig> { new AgentConfig { Realm = "realm_lt", Kind = "lieutenant", Mode = "live" } };
            bool known = Lieutenant.IsRealm("realm_lt") && !Lieutenant.IsRealm("realm_other");
            Bridge.Configs = kept;
            Check(known, "the lieutenant's realm is known from the load's own settings, before the helper's agents are filled");
        }

        /// <summary>
        /// 0.13.11 (Mert, Oct 29, chasing Mauhur with 3,865 men: "allied lords around me are still not engaging him ... They should be engaging him
        /// normally since they know I am around"): a guard joins its realm ruler's own attack on an enemy near it, weighing the ruler's force on its
        /// side, follows it past its own area (up to three times its radius from the holding) while the ruler chases, and goes back once he stops.
        /// </summary>
        private static void RulerChaseTests(List<KeyValuePair<int, Move>> moves)
        {
            var guard = new PartyRow { Id = "p_grd", Leader = "lord_grd", Name = "Amarie", Faction = "orcs", Kind = "lord", Men = 160, SizeLimit = 200, Strength = 300, Speed = 5, FoodDays = 10, X = 160, Y = 100, Doing = "Hold", Spottable = true };
            var ruler = new PartyRow { Id = "p_rul", Leader = "lord_rul", Name = "Azog", Faction = "orcs", Kind = "lord", Men = 900, ArmyLeader = "p_rul", ArmyMen = 3865, ArmyParties = 6, ArmyStrength = 10800, Strength = 2000, Speed = 5, FoodDays = 10, X = 166, Y = 100, Doing = "EngageParty", TargetParty = "p_foe", Spottable = true };
            var foe = new PartyRow { Id = "p_foe", Leader = "lord_foe", Name = "Mauhur", Faction = "player_k", Kind = "lord", Men = 686, Strength = 1600, Speed = 5, FoodDays = 10, X = 170, Y = 100, Doing = "GoToPoint", Spottable = true };
            Snapshot World(double h)
            {
                Snapshot s = ToolWorld(h, guard, ruler, foe);
                s.Clans.Add(new ClanRow { Id = "clan_rul", Name = "Azog's clan", Leader = "lord_rul", Ruler = true });
                return s;
            }
            Move? Last(int id) => moves.Where(m => m.Key == id).Select(m => (Move?)m.Value).LastOrDefault();
            double h0 = 43000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            moves.Clear();
            Runner.Start(9810, new ToolOrder { Tool = "defend", Lord = "lord_grd", LordName = "Amarie", Party = "p_grd", At = "castle_two", Realm = "orcs" });
            bool joins = Last(9810)?.Verb == "engage" && Last(9810)?.Party == "p_foe";
            // the enemy runs on past the guard's area (45 from the holding, radius 20): the guard keeps up the chase while the ruler does
            foe.X = 185; ruler.X = 180; guard.X = 176;
            Brain.OnHour(World(h0 + 1));
            bool keeps = Last(9810)?.Verb == "engage";
            // the ruler stops: the guard goes back to its area
            ruler.Doing = "Hold"; ruler.TargetParty = null;
            Brain.OnHour(World(h0 + 2));
            Check(joins && keeps && Last(9810)?.Verb == "patrol" && Last(9810)?.Place == "castle_two",
                  "a guard joins its ruler's attack on an enemy just outside its area, keeps up the chase past its area while the ruler does, and goes back once he stops ("
                  + joins + ", " + keeps + ", " + Last(9810) + ")");
            Runner.Ended(9810, "test over");
            FollowerTests();
            ReliefTests(moves);
            BandReliefTests(moves);
            TipTheBalanceTests(moves);
            ThenIdsTests();
            RecentHistoryTests();
            SupplyAndGarrisonTests();
            HungerTests();
        }

        /// <summary>
        /// 0.13.15 (Mert, Oct 30: "lords under any order and not in an army should always ride with all haste to relieve lords in a fight if
        /// their combined strength beats the enemies' in the area's combined strength for the expected duration of the fight ... Armies should
        /// still try to relieve lords ... but should never break up because of this"): held lords out of an army, and an army's leader with his
        /// army, ride to a lord of ours fighting in the field when they can get there before it ends and our side with them outweighs the
        /// enemies in it and those who could join it; a lord in another's army, one camped in a siege and one too far do not; none rides when
        /// the enemy outweighs; each goes back to his order once the fight is over.
        /// </summary>
        private static void ReliefTests(List<KeyValuePair<int, Move>> moves)
        {
            var friend = new PartyRow { Id = "p_fr", Leader = "lord_fr", Name = "Grishnakh", Faction = "orcs", Kind = "lord", Men = 150, Strength = 300, Speed = 5, FoodDays = 10, X = 150, Y = 100, InBattle = true, Doing = "Hold", Spottable = true };
            var foe = new PartyRow { Id = "p_en", Leader = "lord_en", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 450, Strength = 900, Speed = 5, FoodDays = 10, X = 150, Y = 100, InBattle = true, Doing = "EngageParty", TargetParty = "p_fr", Spottable = true };
            var coming = new PartyRow { Id = "p_cm", Leader = "lord_cm", Name = "Beregond", Faction = "player_k", Kind = "lord", Men = 150, Strength = 300, Speed = 5, FoodDays = 10, X = 170, Y = 100, Doing = "Hold", Spottable = true };
            var guard = new PartyRow { Id = "p_ga", Leader = "lord_ga", Name = "Lugdush", Faction = "orcs", Kind = "lord", Men = 250, Strength = 500, Speed = 5, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            var waiter = new PartyRow { Id = "p_wa", Leader = "lord_wa", Name = "Mauhur", Faction = "orcs", Kind = "lord", Men = 200, Strength = 400, Speed = 5, FoodDays = 10, X = 160, Y = 104, Doing = "Hold", Spottable = true };
            var head = new PartyRow { Id = "p_hd", Leader = "lord_hd", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, Strength = 450, ArmyLeader = "p_hd", ArmyMen = 500, ArmyParties = 2, ArmyStrength = 800, Speed = 4, FoodDays = 10, X = 135, Y = 96, Doing = "Hold", Spottable = true };
            var member = new PartyRow { Id = "p_mb", Leader = "lord_mb", Name = "Snaga", Faction = "orcs", Kind = "lord", Men = 200, Strength = 350, ArmyLeader = "p_hd", Attached = true, Speed = 4, FoodDays = 10, X = 135, Y = 96, Doing = "EscortParty", Spottable = true };
            var camped = new PartyRow { Id = "p_sg", Leader = "lord_sg", Name = "Gorbag", Faction = "orcs", Kind = "lord", Men = 300, Strength = 600, Speed = 5, FoodDays = 10, X = 168, Y = 108, Besieging = "castle_men", Doing = "BesiegeSettlement", Spottable = true };
            var far = new PartyRow { Id = "p_fa", Leader = "lord_fa", Name = "Shagrat", Faction = "orcs", Kind = "lord", Men = 300, Strength = 600, Speed = 5, FoodDays = 10, X = 10, Y = 100, Doing = "Hold", Spottable = true };
            BattleRow Fight(double h, int foeMen) => new BattleRow
            {
                Key = "p_en@" + (h - 1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "field", X = 150, Y = 100, StartHours = h - 1,
                Attackers = new BattleSideRow { Leader = "p_en", Faction = "player_k", Healthy = foeMen, StartHealthy = foeMen, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_en" } },
                Defenders = new BattleSideRow { Leader = "p_fr", Faction = "orcs", Healthy = 1500, StartHealthy = 1500, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_fr" } }
            };
            Snapshot World(double h, bool fighting, int foeMen = 2000)
            {
                Snapshot s = ToolWorld(h, friend, foe, coming, guard, waiter, head, member, camped, far);
                if (fighting) s.Battles.Add(Fight(h, foeMen));
                return s;
            }
            Move? Last(int id) => moves.Where(m => m.Key == id).Select(m => (Move?)m.Value).LastOrDefault();
            double h0 = 43200;
            Diag.Hours = h0;
            Brain.OnHour(World(h0, false));
            Runner.Start(9830, new ToolOrder { Tool = "defend", Lord = "lord_ga", LordName = "Lugdush", Party = "p_ga", At = "castle_two", Realm = "orcs" });
            Runner.Start(9831, new ToolOrder { Tool = "hold", Lord = "lord_wa", LordName = "Mauhur", Party = "p_wa", Realm = "orcs" });
            Runner.Start(9832, new ToolOrder { Tool = "hold", Lord = "lord_hd", LordName = "Bolg", Party = "p_hd", Realm = "orcs" });
            Runner.Start(9833, new ToolOrder { Tool = "hold", Lord = "lord_mb", LordName = "Snaga", Party = "p_mb", Realm = "orcs" });
            Runner.Start(9834, new ToolOrder { Tool = "besiege", Lord = "lord_sg", LordName = "Gorbag", Party = "p_sg", At = "castle_men", Realm = "orcs" });
            Runner.Start(9835, new ToolOrder { Tool = "hold", Lord = "lord_fa", LordName = "Shagrat", Party = "p_fa", Realm = "orcs" });
            moves.Clear();
            // the enemy outweighs (900 in the fight and 300 coming, against 300 with every lord who can get there): nobody rides
            foe.Strength = 5000;
            Brain.OnHour(World(h0 + 1, true));
            bool noneWhenOutweighed = !moves.Any(m => m.Value.Verb == "engage" && m.Value.Party == "p_en");
            // ours outweighs: 300 in it, 500 + 400 + 800 riding, against 900 and Beregond's 300 who could join
            foe.Strength = 900;
            Brain.OnHour(World(h0 + 2, true));
            bool rides = Last(9830)?.Verb == "engage" && Last(9830)?.Party == "p_en" && Last(9831)?.Verb == "engage" && Last(9832)?.Verb == "engage" && Last(9832)?.Party == "p_en";
            bool staysOut = !moves.Any(m => (m.Key == 9833 || m.Key == 9834 || m.Key == 9835) && m.Value.Verb == "engage");
            bool marked = Runner.All().Any(t => t.Id == 9830 && t.ReliefOf != null) && Runner.All().Any(t => t.Id == 9832 && t.ReliefOf != null);
            // the fight is over: each goes back to his order (the lord holding, to holding)
            Brain.OnHour(World(h0 + 3, false));
            bool back = Last(9831)?.Verb == "hold" && Last(9832)?.Verb == "hold" && Runner.All().Where(t => t.Id >= 9830 && t.Id <= 9835).All(t => t.ReliefOf == null);
            Check(noneWhenOutweighed && rides && staysOut && marked && back,
                  "held lords ride to relieve a lord of ours in a fight when they outweigh the enemy with those who could join it (a guard from inside its castle, a lord holding, an army whole); a lord in another's army, one camped in a siege and one too far do not; none rides when the enemy outweighs; each goes back to his order once it is over ("
                  + noneWhenOutweighed + ", " + rides + ", " + staysOut + ", " + marked + ", " + back + ")");
            foreach (int id in new[] { 9830, 9831, 9832, 9833, 9834, 9835 }) Runner.Ended(id, "test over");
        }

        /// <summary>
        /// 0.13.14 (Mert, Oct 30: "The logic for our orders might still be discounting the strength of nearby lords/lords following other
        /// lords"): Alenon (1,137), to guard Tinnulas, weighed 2,087 near it against himself alone and sheltered in Gwígar, though Oropherion
        /// (961) and Arien (860), ordered to follow him, were inside Gwígar with him. A lord following another counts on his side, in the field
        /// within the hours given and inside the very holding he is in; another lord inside that holding, on no order with him, does not.
        /// </summary>
        private static void FollowerTests()
        {
            var leader = new PartyRow { Id = "p_ale", Leader = "lord_ale", Name = "Alenon", Faction = "orcs", Kind = "lord", Men = 377, Strength = 1137, Speed = 5, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            var oro = new PartyRow { Id = "p_oro", Leader = "lord_oro", Name = "Oropherion", Faction = "orcs", Kind = "lord", Men = 374, Strength = 961, Speed = 5, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            var ari = new PartyRow { Id = "p_ari", Leader = "lord_ari", Name = "Arien", Faction = "orcs", Kind = "lord", Men = 386, Strength = 860, Speed = 5, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            var idle = new PartyRow { Id = "p_idl", Leader = "lord_idl", Name = "Haldir", Faction = "orcs", Kind = "lord", Men = 300, Strength = 700, Speed = 5, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            var far = new PartyRow { Id = "p_far", Leader = "lord_far", Name = "Galdor", Faction = "orcs", Kind = "lord", Men = 300, Strength = 650, Speed = 5, FoodDays = 10, X = 40, Y = 100, Doing = "EscortParty", TargetParty = "p_ale", Spottable = true };
            Snapshot s = ToolWorld(43100, leader, oro, ari, idle, far);
            Diag.Hours = 43100;
            Brain.OnHour(s);
            Runner.Start(9820, new ToolOrder { Tool = "follow", Lord = "lord_oro", LordName = "Oropherion", Party = "p_oro", Target = "p_ale", Realm = "orcs" });
            Runner.Start(9821, new ToolOrder { Tool = "follow", Lord = "lord_ari", LordName = "Arien", Party = "p_ari", Target = "p_ale", Realm = "orcs" });
            var w = new World(s, "orcs", s.WatchedSees, new Dictionary<string, ViewParty>());
            var with = new List<PartyRow>();
            double side = w.SideStrength(leader, 140, 100, 12, null, new HashSet<string> { "castle_two" }, with);
            Check(Math.Abs(side - (1137 + 961 + 860)) < 0.5 && with.Count == 2 && with.Any(p => p.Id == "p_oro") && with.Any(p => p.Id == "p_ari"),
                  "a lord's side counts the lords following him inside the very holding he is in (2,958, as Alenon with Oropherion and Arien), not another lord there on no order with him, nor a follower beyond the hours given: "
                  + side + " with " + string.Join(", ", with.Select(p => p.Name)));
            Runner.Ended(9820, "test over");
            Runner.Ended(9821, "test over");
        }
    }
}
