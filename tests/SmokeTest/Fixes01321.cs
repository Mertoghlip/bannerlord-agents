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
        /// 0.13.21 (Nov 1: Dol Guldur's agent left Gwígar for "no food" while its own supply was seven hours from handing over 219):
        /// YOUR HOLDINGS names each own holding's clan, the food coming with a supply order (how much, when), and the rule that lords inside
        /// eat their own food; no hunger warning for stores too full to take a day more.
        /// </summary>
        private static void FoodComingTests()
        {
            var lord = new PartyRow { Id = "p_dur", Leader = "lord_dur", Name = "Durnia", Faction = "orcs", Kind = "lord", Men = 74, Strength = 173, Speed = 5, FoodDays = 60,
                                      FoodItems = 225, FoodEats = 2, X = 120, Y = 100, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Snapshot s = ToolWorld(43900, lord);
            SettlementRow c = s.SettlementById["castle_orc"];
            c.Food = 0; c.FoodCap = 450; c.FoodPerDay = 24; c.OwnerClanName = "House of Durbag";
            Diag.Hours = 43900;
            Brain.OnHour(s);
            Runner.Start(9880, new ToolOrder { Tool = "supply", Lord = "lord_dur", LordName = "Durnia", Party = "p_dur", At = "castle_orc", Realm = "orcs" });
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, null);
            string report = Views.Report(v, null);
            Runner.Ended(9880, "test over");
            // 225 carried, 3 days of 2 a day kept: 219 to spare; it needs 10 days of 24 = 240
            bool coming = report.Contains("Orc Castle [castle_orc] (castle of House of Durbag)") && report.Contains("food coming: Durnia with about 219 (about 9 days more)");
            bool rule = report.Contains("Lords sheltering inside never eat a holding's food");

            var foe = new PartyRow { Id = "p_cel", Leader = "lord_cel", Name = "Baranthir", Faction = "player_k", Kind = "lord", Men = 200, Strength = 436, Speed = 4, FoodDays = 10, X = 110, Y = 100, Doing = "Hold", Spottable = true };
            Snapshot full = ToolWorld(43901, foe);
            SettlementRow fc = full.SettlementById["castle_orc"];
            fc.Food = 800; fc.FoodCap = 800; fc.FoodPerDay = 284;
            bool quiet = Brain.LowFood(full, new Brain.Memory(), full.WatchedSees, full.HostileToWatched, full.Settlements.Where(x => x.Faction == "orcs").ToList()).Count == 0;
            Check(coming && rule && quiet,
                  "YOUR HOLDINGS names each own holding's clan and the food a supply order brings it (how much), says lords inside eat their own food, and no hunger warning comes for full stores ("
                  + coming + ", " + rule + ", " + quiet + ")");
        }
    }
}
