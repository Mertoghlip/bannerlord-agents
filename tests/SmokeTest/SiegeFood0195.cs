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
        /// 0.19.5 (Nov 13: the food that parties have in their inventories should also count towards a besieged settlement's food): the draw's share-out (one
        /// item at a time from the party whose food lasts longest, never more than one has), the days a place lasts with its lords' food (its store first,
        /// then what the lords have left, shared), and the report, the siege clock, the hunger warning and the order language all counting it.
        /// </summary>
        private static void SiegeFoodTests()
        {
            // who gives what: the party with the most days of food gives first; a shortfall above all they carry takes all of it
            List<int> five = SiegeFoodRules.Plan(5, new List<float> { 10, 3 }, new List<float> { 5, 0.5f });
            List<int> all = SiegeFoodRules.Plan(20, new List<float> { 4, 3 }, new List<float> { 2, 2 });
            List<int> none = SiegeFoodRules.Plan(0, new List<float> { 4 }, new List<float> { 2 });
            bool plan = five.Sum() == 5 && five[0] <= 10 && five[1] <= 3 && five[1] >= 2 && all.SequenceEqual(new[] { 4, 3 }) && none.Sum() == 0 && SiegeFoodRules.Plan(3, new List<float>(), new List<float>()).Count == 0;
            // the days: a store of 64 eaten at 32 lasts 2 days; lords with 120 who eat 6 a day have 108 left then, shared at 38 a day: 2.84 more
            double pooled = SiegeFoodRules.Days(64, 32, 120, 6), eatenFirst = SiegeFoodRules.Days(64, 32, 10, 6), onlyLords = SiegeFoodRules.Days(0, 32, 64, 0);
            bool days = Math.Abs(pooled - (2 + 108.0 / 38)) < 1e-9 && Math.Abs(eatenFirst - 2) < 1e-9 && Math.Abs(onlyLords - 2) < 1e-9
                        && double.IsPositiveInfinity(SiegeFoodRules.Days(10, 0, 0, 0)) && Math.Abs(SiegeFoodRules.Days(64, 32, 0, 0) - 2) < 1e-9;
            Check(plan && days, "the siege food rule's share-out and days: the longest-fed lord gives first, never more than he has; the store lasts alone first, then the lords' leftover is shared, "
                                + "and lords who would eat all theirs first add nothing (" + plan + ", " + days + "; " + string.Join(",", five) + "; " + Math.Round(pooled, 3) + ")");

            // the report: an own castle's line adds the days with its own lords' food inside
            var fed = new PartyRow { Id = "p_fed", Leader = "lord_fed", Name = "Gorbag", Faction = "orcs", Kind = "lord", Men = 120, Strength = 200, Speed = 5, FoodDays = 20, FoodItems = 120, FoodEats = 6,
                                     X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            Snapshot s = ToolWorld(43900, fed);
            SettlementRow orc = s.SettlementById["castle_orc"];
            orc.Food = 64; orc.FoodCap = 450; orc.FoodPerDay = 32;
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, null);
            string report = Views.Report(v, null);
            bool line = report.Contains("Orc Castle [castle_orc] (castle): garrison 450 (strength 600), militia 120; food 64 of 450, about 2 days under siege (it eats 32 a day), about 4 with the 120 food your lords inside carry")
                        && report.Contains("takes what it lacks each day from the food its lords inside carry");
            Snapshot bare = ToolWorld(43900);
            SettlementRow orc2 = bare.SettlementById["castle_orc"];
            orc2.Food = 64; orc2.FoodCap = 450; orc2.FoodPerDay = 32;
            string plainReport = Views.Report(Views.Build(bare, "orcs", bare.WatchedSees, bare.HostileToWatched, null), null);
            bool plain = plainReport.Contains("food 64 of 450, about 2 days under siege (it eats 32 a day);") && !plainReport.Contains("food your lords inside carry");
            Check(line && plain, "the report counts an own castle's lords' food inside: about 4 days with the 120 they carry, against 2 on its store alone, and says nothing more with none inside (" + line + ", " + plain + ")");

            // the siege clock: the place's food runs out once its store and its lords' leftover are both eaten
            var g = new SiegeRow { Settlement = "castle_orc", Besieger = "p_x", BesiegerFaction = "player_k", StartHours = 43900, Prep = 0.2f, PrepRate = 0.02f, WallLevel = 1, Advantage = 2f, Defenders = 600, Attackers = 1000,
                                   Food = 64, FoodChange = -32, LordsFood = 120, LordsEat = 6 };
            Forecast.SiegeClock withLords = Forecast.Siege(g, 43900, null);
            g.LordsFood = 0; g.LordsEat = 0;
            Forecast.SiegeClock alone = Forecast.Siege(g, 43900, null);
            bool clock = Math.Abs(withLords.FoodOutAt - (43900 + 24 * (2 + 108.0 / 38))) < 1e-6 && Math.Abs(alone.FoodOutAt - (43900 + 48)) < 1e-6;
            Check(clock, "the siege clock counts the lords' food inside: the food runs out after about 4.8 days with it, 2 without (" + withLords.FoodOutAt + ", " + alone.FoodOutAt + ")");

            // the hunger warning: a castle whose store alone would last 1 day is not hungry while its lords inside carry enough
            var foe = new PartyRow { Id = "p_kralza", Leader = "lord_kralza", Name = "Kralza", Faction = "player_k", Kind = "lord", Men = 300, Strength = 500, Speed = 5, X = 120, Y = 100, Doing = "Hold", Spottable = true };
            var heavy = new PartyRow { Id = "p_heavy", Leader = "lord_heavy", Name = "Shagrat", Faction = "orcs", Kind = "lord", Men = 120, Strength = 200, Speed = 5, FoodDays = 20, FoodItems = 300, FoodEats = 6,
                                       X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            Snapshot hungry = ToolWorld(43950, foe, heavy);
            SettlementRow hc = hungry.SettlementById["castle_orc"];
            hc.Food = 50; hc.FoodCap = 450; hc.FoodPerDay = 30;
            bool notHungry = Brain.LowFood(hungry, new Brain.Memory(), hungry.WatchedSees, hungry.HostileToWatched, hungry.Settlements.Where(x => x.Faction == "orcs").ToList()).Count(t => t.Place == "castle_orc") == 0;
            heavy.FoodItems = 0;
            bool hungerBack = Brain.LowFood(hungry, new Brain.Memory(), hungry.WatchedSees, hungry.HostileToWatched, hungry.Settlements.Where(x => x.Faction == "orcs").ToList()).Count(t => t.Place == "castle_orc") == 1;
            Check(notHungry && hungerBack, "the hunger warning counts the lords' food inside: none while they carry enough, and it comes back once they carry none (" + notHungry + ", " + hungerBack + ")");

            // the order language: food_days_at counts it too
            ToolDef larder = ToolDef.FromJson(@"{ ""name"": ""larder2"", ""at"": ""own_fort"", ""start"": ""a"", ""states"": {
                ""a"": { ""do"": ""hold"", ""when"": [ { ""if"": ""food_days_at(at) < 3"", ""go"": ""hungry"" } ] },
                ""hungry"": { ""do"": ""hold"" } } }", "larder2", "orcs");
            string StateWith(int lordsFood, int id)
            {
                var keeper = new PartyRow { Id = "p_lar2", Leader = "lord_lar2", Name = "Lugdush", Faction = "orcs", Kind = "lord", Men = 100, Strength = 200, Speed = 5, FoodDays = 20, FoodItems = lordsFood, FoodEats = 6,
                                            X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
                double h0 = 44000 + id;
                Snapshot w1 = ToolWorld(h0, keeper);
                SettlementRow c1 = w1.SettlementById["castle_orc"];
                c1.Food = 50; c1.FoodCap = 450; c1.FoodPerDay = 30;
                Diag.Hours = h0;
                Brain.OnHour(w1);
                Runner.Start(id, new ToolOrder { Tool = "larder2", Def = larder, Lord = "lord_lar2", LordName = "Lugdush", Party = "p_lar2", At = "castle_orc", Realm = "orcs" });
                Snapshot w2 = ToolWorld(h0 + 1, keeper);
                SettlementRow c2 = w2.SettlementById["castle_orc"];
                c2.Food = 50; c2.FoodCap = 450; c2.FoodPerDay = 30;
                Brain.OnHour(w2);
                string st = Runner.All().FirstOrDefault(t => t.Id == id)?.State;
                Runner.Ended(id, "test over");
                return st;
            }
            string fedState = StateWith(120, 9881), unfedState = StateWith(0, 9882);
            Check(larder.Problems.Count == 0 && fedState == "a" && unfedState == "hungry",
                  "the order language's food_days_at counts the lords' food inside: 50 food at 30 a day with 120 inside lasts over 3 days, without it under 2 (" + fedState + ", " + unfedState + ")");
        }
    }
}
