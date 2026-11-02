using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Common;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13.16 (Mert, Nov 1). The report's recent history keeps the news when it is cut: the lieutenant could not say who broke Dáin's
        /// siege of Nanduhirion, because a day of order lines had pushed the battle and the twelve captures out of the last 25 lines.
        /// </summary>
        private static void RecentHistoryTests()
        {
            var hours = new List<double>();
            var kinds = new List<string>();
            void Add(double h, string kind, int n) { for (int i = 0; i < n; i++) { hours.Add(h + i * 0.001); kinds.Add(kind); } }
            Add(100.3, "battle", 1);
            Add(100.3, "captured", 12);
            Add(100.3, "siege_ended", 1);
            Add(100.3, "army_dispersed", 1);
            for (int h = 101; h <= 114; h++) Add(h + 0.1, "tool", 2);   // 28 lines of orders sheltering and finding few recruits
            Add(114.5, "raided", 1);
            List<int> shown = RecentHistory.Pick(hours, kinds, 115);
            bool newsKept = Enumerable.Range(0, 15).All(shown.Contains) && shown.Contains(hours.Count - 1);
            int routine = shown.Count(i => RecentHistory.Routine(kinds[i]));
            // the orders' lines fill what is left of 25 (25 - 16 = 9), by whole hours (2 lines each): 4 hours, the newest
            bool filled = routine == 8 && shown.Where(i => RecentHistory.Routine(kinds[i])).All(i => hours[i] >= 111);
            bool ordered = shown.SequenceEqual(shown.OrderBy(i => i));
            // the old cut, the newest 25 lines, would have shown none of the battle's 15
            bool oldCutMissed = Enumerable.Range(hours.Count - 25, 25).All(i => i >= 15);
            // an hour of news larger than the limit is still shown whole; a second such hour is not split
            var h2 = new List<double>(); var k2 = new List<string>();
            for (int i = 0; i < 45; i++) { h2.Add(200.2); k2.Add("captured"); }
            for (int i = 0; i < 5; i++) { h2.Add(199.2); k2.Add("battle"); }
            List<int> big = RecentHistory.Pick(h2, k2, 201);
            Check(newsKept && filled && ordered && oldCutMissed && big.Count == 45,
                  "the report's recent history keeps the last two days' news (a battle with its 12 captures, a siege lifted, an army broken up) and fills what is left of 25 lines with the orders' own, by whole hours; an hour of news is never split ("
                  + newsKept + ", " + routine + " order lines, " + filled + ", " + ordered + ", " + oldCutMissed + ", " + big.Count + ")");
        }

        /// <summary>
        /// 0.13.16 (Nov 1, Winter 2): sixteen lords rode toward Osven's fight with a band of Mirkwood Stalkers (139 men against 118, Osven
        /// winning), on a clock of 24 hours or more; five more bands came in, 577 men by noon, and took Osven in 13 hours. Bands of the enemy's
        /// own kind who could join now count against the relief, the clock runs with them coming in, and a rider must get there with time to
        /// spare. And a relief's place reads "2 north-west of X", not "the spot 2 north-west of X" after "fighting Y".
        /// </summary>
        private static void BandReliefTests(List<KeyValuePair<int, Move>> moves)
        {
            var friend = new PartyRow { Id = "p_os", Leader = "lord_os", Name = "Osven", Faction = "orcs", Kind = "lord", Men = 139, Strength = 300, Speed = 5, FoodDays = 10, X = 150, Y = 100, InBattle = true, Doing = "Hold", Spottable = true };
            var band = new PartyRow { Id = "p_b0", Name = "Mirkwood Stalkers", Faction = "stalkers", Kind = "bandit", Men = 118, Strength = 236, Speed = 5, X = 150, Y = 100, InBattle = true, Doing = "EngageParty", TargetParty = "p_os", Spottable = true };
            var bands = Enumerable.Range(1, 4).Select(i => new PartyRow { Id = "p_b" + i, Name = "Mirkwood Stalkers", Faction = "stalkers", Kind = "bandit", Men = 110, Strength = 220, Speed = 5, X = 150 + 8, Y = 100 + i, Doing = "Hold", Spottable = true }).ToList();
            var rider = new PartyRow { Id = "p_rd", Leader = "lord_rd", Name = "Ufthak", Faction = "orcs", Kind = "lord", Men = 200, Strength = 400, Speed = 5, FoodDays = 10, X = 160, Y = 104, Doing = "Hold", Spottable = true };
            BattleRow Fight(double h) => new BattleRow
            {
                Key = "p_b0@" + (h - 1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "field", X = 150, Y = 100, StartHours = h - 1,
                Attackers = new BattleSideRow { Leader = "p_b0", Faction = "stalkers", Healthy = 118, StartHealthy = 118, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_b0" } },
                Defenders = new BattleSideRow { Leader = "p_os", Faction = "orcs", Healthy = 139, StartHealthy = 139, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_os" } }
            };
            Snapshot World(double h, bool bandsNear)
            {
                foreach (PartyRow b in bands) b.X = bandsNear ? 158 : 400;
                Snapshot s = ToolWorld(h, new[] { friend, band, rider }.Concat(bands).ToArray());
                s.HostileToWatched.Add("stalkers");
                s.FactionNames["stalkers"] = "Mirkwood Stalkers";
                s.Battles.Add(Fight(h));
                return s;
            }
            Move? Last(int id) => moves.Where(m => m.Key == id).Select(m => (Move?)m.Value).LastOrDefault();
            double h0 = 43400;
            Diag.Hours = h0;
            Brain.OnHour(World(h0, true));
            Runner.Start(9840, new ToolOrder { Tool = "hold", Lord = "lord_rd", LordName = "Ufthak", Party = "p_rd", Realm = "orcs" });
            moves.Clear();
            // 300 + 400 against 236 in it and four bands of 220 who could join: nobody rides
            Brain.OnHour(World(h0 + 1, true));
            bool stays = !moves.Any(m => m.Key == 9840 && m.Value.Verb == "engage");
            // the bands far off: 700 against 236, Ufthak rides
            Brain.OnHour(World(h0 + 2, false));
            bool rides = Last(9840)?.Verb == "engage" && Last(9840)?.Party == "p_b0";
            Runner.Ended(9840, "test over");
            Check(stays && rides && Math.Abs(Library.ReliefSpare(24) - 4.8) < 1e-9 && Library.ReliefSpare(3) == 1,
                  "a relief counts the bands of the enemy's own kind who could join a fight with bandits (Osven's Mirkwood Stalkers), and a rider must get there with a fifth of the fight's hours to spare, at least an hour ("
                  + stays + ", " + rides + ", " + Library.ReliefSpare(24) + ", " + Library.ReliefSpare(3) + ")");
            Check(Runner.FightPlace("the spot 2 north-west of Düglar-mauz") == "2 north-west of Düglar-mauz" && Runner.FightPlace("the spot at Brak-gash") == "at Brak-gash"
                  && Runner.FightPlace("the spot p673_994") == "at the spot p673_994",
                  "a relief's place reads \"fighting X 2 north-west of Düglar-mauz\" / \"at Brak-gash\" / \"at the spot p673_994\": " + Runner.FightPlace("the spot 2 north-west of Düglar-mauz"));
        }

        /// <summary>
        /// 0.13.16 (Nov 1, 16:32): the fog check flagged Dol Guldur's every report for the id of Sirannon [castle_MM7] in its own order in force,
        /// "Dramthak gather at Caradhras, then besiege [castle_MM7]", the id from its ally's joint plan. The ids an order's next orders name count
        /// as known, like its own.
        /// </summary>
        private static void ThenIdsTests()
        {
            var lord = new PartyRow { Id = "p_dr", Leader = "lord_dr", Name = "Dramthak", Faction = "orcs", Kind = "lord", Men = 300, Strength = 600, Speed = 5, FoodDays = 10, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            Snapshot s = ToolWorld(43500, lord);
            Diag.Hours = 43500;
            Brain.OnHour(s);
            var then = new JObject { ["do"] = "besiege", ["at"] = "castle_far", ["then"] = new JObject { ["do"] = "follow", ["target"] = "lord_far2" } };
            Runner.Start(9850, new ToolOrder { Tool = "hold", Lord = "lord_dr", LordName = "Dramthak", Party = "p_dr", Realm = "orcs", Then = then });
            var w = new World(s, "orcs", s.WatchedSees, new Dictionary<string, ViewParty>());
            List<string> ids = Runner.Ids(w).ToList();
            Runner.Ended(9850, "test over");
            Check(ids.Contains("castle_far") && ids.Contains("lord_far2"), "the fog check knows the ids an order's next orders name (then besiege [castle_far], then follow [lord_far2]): " + string.Join(", ", ids));
        }
    }
}
