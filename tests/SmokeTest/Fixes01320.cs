using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13.20 (Nov 1, with all the proposed optional extras): the hunger warning (an own castle or town not besieged
        /// with food for under 3 days of siege and an enemy lord or army in view within 40), told once a day or sooner as it falls a day
        /// more; and food_at / food_days_at in the order language, for the realm's own castles and towns.
        /// </summary>
        private static void HungerTests()
        {
            var foe = new PartyRow { Id = "p_hun", Leader = "lord_hun", Name = "Kralza", Faction = "player_k", Kind = "lord", Men = 780, Strength = 1666, Speed = 4, FoodDays = 10, X = 120, Y = 100, Doing = "BesiegeSettlement", Spottable = true };
            Snapshot World(double h, float foeX, float food)
            {
                foe.X = foeX;
                Snapshot w = ToolWorld(h, foe);
                SettlementRow c = w.SettlementById["castle_orc"];
                c.Food = food; c.FoodCap = 450; c.FoodPerDay = 30;
                SettlementRow keep = w.SettlementById["castle_two"];
                keep.Food = 400; keep.FoodCap = 450; keep.FoodPerDay = 30;
                return w;
            }
            var m = new Brain.Memory();
            List<SettlementRow> Own(Snapshot w) => w.Settlements.Where(x => x.Faction == "orcs").ToList();
            Snapshot s1 = World(43800, 120, 50);
            List<Trigger> first = Brain.LowFood(s1, m, s1.WatchedSees, s1.HostileToWatched, Own(s1));
            bool warns = first.Count == 1 && first[0].Kind == "low_food" && first[0].Place == "castle_orc"
                         && first[0].Text.StartsWith("Hunger at Orc Castle: its food would last only 1 day of siege (50 of 450, it eats 30 a day), and enemies are near: Kralza, 20.0 away");
            Snapshot s2 = World(43801, 120, 49);
            bool once = Brain.LowFood(s2, m, s2.WatchedSees, s2.HostileToWatched, Own(s2)).Count == 0;
            Snapshot s3 = World(43802, 120, 15);
            bool sooner = Brain.LowFood(s3, m, s3.WatchedSees, s3.HostileToWatched, Own(s3)).Count == 1;
            var m2 = new Brain.Memory();
            Snapshot s4 = World(43803, 300, 50);
            bool farOff = Brain.LowFood(s4, m2, s4.WatchedSees, s4.HostileToWatched, Own(s4)).Count == 0;
            Check(warns && once && sooner && farOff,
                  "the hunger warning: an own castle short of food for a siege with an enemy in view near it is told once (again only a day later or a day of food lower), and not with the enemy far off ("
                  + warns + ", " + once + ", " + sooner + ", " + farOff + "): " + (first.Count > 0 ? first[0].Text : "none"));

            // the language reads an own castle's days of food
            ToolDef watcher = ToolDef.FromJson(@"{ ""name"": ""larder"", ""at"": ""own_fort"", ""start"": ""a"", ""states"": {
                ""a"": { ""do"": ""hold"", ""when"": [ { ""if"": ""food_days_at(at) < 3 and food_at(at) > 0"", ""go"": ""hungry"" } ] },
                ""hungry"": { ""do"": ""hold"" } } }", "larder", "orcs");
            var lord = new PartyRow { Id = "p_lar", Leader = "lord_lar", Name = "Grishnakh", Faction = "orcs", Kind = "lord", Men = 100, Strength = 200, Speed = 5, FoodDays = 10, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            Snapshot s5 = ToolWorld(43810, lord);
            SettlementRow orc = s5.SettlementById["castle_orc"];
            orc.Food = 50; orc.FoodCap = 450; orc.FoodPerDay = 30;
            Diag.Hours = 43810;
            Brain.OnHour(s5);
            Runner.Start(9870, new ToolOrder { Tool = "larder", Def = watcher, Lord = "lord_lar", LordName = "Grishnakh", Party = "p_lar", At = "castle_orc", Realm = "orcs" });
            Snapshot s6 = ToolWorld(43811, lord);
            SettlementRow orc6 = s6.SettlementById["castle_orc"];
            orc6.Food = 50; orc6.FoodCap = 450; orc6.FoodPerDay = 30;
            Brain.OnHour(s6);
            string state = Runner.All().FirstOrDefault(t => t.Id == 9870)?.State;
            Runner.Ended(9870, "test over");
            Check(watcher.Problems.Count == 0 && state == "hungry",
                  "the order language reads an own castle's food and its days of siege (food_at, food_days_at): " + (watcher.Problems.Count > 0 ? string.Join("; ", watcher.Problems) : "state " + state));
        }
    }
}
