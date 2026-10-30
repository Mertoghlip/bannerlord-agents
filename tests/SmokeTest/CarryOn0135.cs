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
        }
    }
}
