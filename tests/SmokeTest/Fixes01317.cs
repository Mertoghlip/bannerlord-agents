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
        /// 0.13.17 (Nov 1: the proposed fix, and the logic must assign enough lords to tip the balance for
        /// relief, and lords that have a defend/patrol/recruit order (so lords that are basically not doing anything important) still go
        /// to relieve another lord regardless of how the battle is going, since a crushing victory reduces losses compared to a close
        /// victory): a lord free to ride (defend here) goes to every fight his side can win; lords on other orders (goto here) go only while
        /// the lords in it would not win alone, nearest first, until our side is 1.5 times the enemy; the rest keep to their orders.
        /// </summary>
        private static void TipTheBalanceTests(List<KeyValuePair<int, Move>> moves)
        {
            var friend = new PartyRow { Id = "p_vu", Leader = "lord_vu", Name = "Vugra", Faction = "orcs", Kind = "lord", Men = 150, Strength = 300, Speed = 5, FoodDays = 10, X = 150, Y = 100, InBattle = true, Doing = "Hold", Spottable = true };
            var foe = new PartyRow { Id = "p_ki", Leader = "lord_ki", Name = "Kisha", Faction = "player_k", Kind = "lord", Men = 450, Strength = 900, Speed = 5, FoodDays = 10, X = 150, Y = 100, InBattle = true, Doing = "EngageParty", TargetParty = "p_vu", Spottable = true };
            var guard = new PartyRow { Id = "p_g1", Leader = "lord_g1", Name = "Uglúk", Faction = "orcs", Kind = "lord", Men = 250, Strength = 500, Speed = 5, FoodDays = 10, X = 140, Y = 100, InSettlement = "castle_two", Doing = "Hold", Spottable = true };
            PartyRow Busy(string id, string name, float x) => new PartyRow { Id = id, Leader = "lord" + id.Substring(1), Name = name, Faction = "orcs", Kind = "lord", Men = 300, Strength = 600, Speed = 5, FoodDays = 10, X = x, Y = 100, Doing = "GoToSettlement", Spottable = true };
            PartyRow b1 = Busy("p_b1", "Dramthak", 158), b2 = Busy("p_b2", "Nûrghak", 162), b3 = Busy("p_b3", "Rûkmash", 166);
            Snapshot World(double h, bool fighting)
            {
                Snapshot s = ToolWorld(h, friend, foe, guard, b1, b2, b3);
                if (fighting)
                    s.Battles.Add(new BattleRow
                    {
                        Key = "p_ki@" + (h - 1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "field", X = 150, Y = 100, StartHours = h - 1,
                        Attackers = new BattleSideRow { Leader = "p_ki", Faction = "player_k", Healthy = 2000, StartHealthy = 2000, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_ki" } },
                        Defenders = new BattleSideRow { Leader = "p_vu", Faction = "orcs", Healthy = 1500, StartHealthy = 1500, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_vu" } }
                    });
                return s;
            }
            bool Engages(int id) => moves.Any(m => m.Key == id && m.Value.Verb == "engage" && m.Value.Party == "p_ki");
            double h0 = 43600;
            Diag.Hours = h0;
            Brain.OnHour(World(h0, false));
            Runner.Start(9860, new ToolOrder { Tool = "defend", Lord = "lord_g1", LordName = "Uglúk", Party = "p_g1", At = "castle_two", Realm = "orcs" });
            Runner.Start(9861, new ToolOrder { Tool = "goto", Lord = "lord_b1", LordName = "Dramthak", Party = "p_b1", At = "castle_orc", Realm = "orcs" });
            Runner.Start(9862, new ToolOrder { Tool = "goto", Lord = "lord_b2", LordName = "Nûrghak", Party = "p_b2", At = "castle_orc", Realm = "orcs" });
            Runner.Start(9863, new ToolOrder { Tool = "goto", Lord = "lord_b3", LordName = "Rûkmash", Party = "p_b3", At = "castle_orc", Realm = "orcs" });
            bool free = Runner.FreeToRide(Runner.All().First(t => t.Id == 9860)) && !Runner.FreeToRide(Runner.All().First(t => t.Id == 9861))
                        && Runner.FreeToRide(new ToolInstance { Order = new ToolOrder { Tool = "recruit_round" } }) && !Runner.FreeToRide(new ToolInstance { Order = new ToolOrder { Tool = "gather" } });
            moves.Clear();
            // Vugra (300) against Kisha (900): not won alone. Uglúk (500) goes, free to ride; 800 needs the nearest goto lord (600) to pass 1,350
            Brain.OnHour(World(h0 + 1, true));
            bool needed = Engages(9860) && Engages(9861) && !Engages(9862) && !Engages(9863);
            Brain.OnHour(World(h0 + 2, false));
            moves.Clear();
            // Kisha down to 150: Vugra wins alone (300 against 150). Uglúk still goes, to win it bigger; no lord leaves a goto for it
            foe.Strength = 150;
            Brain.OnHour(World(h0 + 3, true));
            bool notNeeded = Engages(9860) && !Engages(9861) && !Engages(9862) && !Engages(9863);
            foreach (int id in new[] { 9860, 9861, 9862, 9863 }) Runner.Ended(id, "test over");
            Check(free && needed && notNeeded,
                  "a lord on defend, patrol, hold, recruit or rest rides to every fight his side can win, however it goes; lords on other orders ride only while the lords there would not win alone, nearest first, until our side is 1.5 times the enemy ("
                  + free + ", " + needed + ", " + notNeeded + ")");
        }
    }
}
