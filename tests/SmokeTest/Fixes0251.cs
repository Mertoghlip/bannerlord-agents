using System;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// Module 0.25.1 (Nov 26, run 20261126-1838-C2, day 32): three small lords sent at Lorgath while 53 of ours fought his siege camp before Dûglarshun broke
        /// off at "1,164% of your side's 1,596": the strike lords' orders named the castle, not Lorgath, so the friends' count left them out. An order weighing a
        /// fight with a foe who is already fighting our side now counts the side fighting him with the lord, and the foe's side as it fights now.
        /// </summary>
        private static void EngageJoin0251Tests()
        {
            Runner.Reset();
            var big = new PartyRow { Id = "p_bg", Leader = "lord_bg", Name = "Zlatka", Faction = "orcs", Kind = "lord", Men = 3000, Strength = 6000, Speed = 5, FoodDays = 10,
                                     X = 150, Y = 100, Doing = "Hold", InBattle = true, Spottable = true };
            var small = new PartyRow { Id = "p_sm", Leader = "lord_sm", Name = "Nár", Faction = "orcs", Kind = "lord", Men = 200, Strength = 400, Speed = 5, FoodDays = 10,
                                       X = 153, Y = 100, Doing = "Hold", Spottable = true };
            var foe = new PartyRow { Id = "p_lg", Leader = "lord_lg", Name = "Lorgath", Faction = "player_k", Kind = "lord", Men = 1500, Strength = 3000, Speed = 4,
                                     X = 150.5f, Y = 100, Doing = "EngageParty", TargetParty = "p_bg", InBattle = true, Spottable = true };
            Snapshot World(double h, bool fighting)
            {
                Snapshot s = ToolWorld(h, big, small, foe);
                big.InBattle = fighting;
                foe.InBattle = fighting;
                if (fighting)
                    s.Battles.Add(new BattleRow
                    {
                        Key = "p_lg@" + (h - 2).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "field", X = 150, Y = 100, StartHours = h - 2,
                        Attackers = new BattleSideRow { Leader = "p_lg", Faction = "player_k", Healthy = 1500, StartHealthy = 1600, Power = 2f, Advantage = 1f, Morale = 70f, Parties = { "p_lg" } },
                        Defenders = new BattleSideRow { Leader = "p_bg", Faction = "orcs", Healthy = 3000, StartHealthy = 3100, Power = 2f, Advantage = 1f, Morale = 80f, Parties = { "p_bg" } }
                    });
                return s;
            }
            double h0 = 88000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0, true));
            var order = new ToolOrder { Tool = "engage", Lord = "lord_sm", LordName = "Nár", Party = "p_sm", Target = "p_lg", Realm = "orcs" };
            Runner.Start(9251, order);
            Diag.Hours = h0 + 1;
            World w = Runner.WorldFor(World(h0 + 1, true));
            double withBattle = Expr.Number(ScopeFor(9251, w)?.Call("fight_odds", new object[] { "p_lg" }));
            World w2 = Runner.WorldFor(World(h0 + 1, false));
            double alone = Expr.Number(ScopeFor(9251, w2)?.Call("fight_odds", new object[] { "p_lg" }));
            Check(withBattle > 0 && withBattle < 1 && alone > 5,
                  "a lord sent at a foe already fighting our side weighs the fight with the side fighting it (odds " + ToolDef.Num(Math.Round(withBattle, 2))
                  + "), not himself alone as when no battle is under way (" + ToolDef.Num(Math.Round(alone, 2)) + ")");
            Runner.Ended(9251, "test over");
            Runner.Reset();
        }
    }
}
