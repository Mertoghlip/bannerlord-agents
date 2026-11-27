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
            EngageGroup0252Tests();
        }

        /// <summary>
        /// Module 0.25.2 (Nov 27, run 20261126-1838-C2, day 106: lords sent at Lorgath's army in one answer each broke off within two hours, at 415-746% of
        /// himself): a lord weighing a fight counts the realm's lords holding an order against the same foe who can be at him by the time he is, and the join
        /// hours on top; a lord farther off than that does not count.
        /// </summary>
        private static void EngageGroup0252Tests()
        {
            Runner.Reset();
            PartyRow Lord(string id, string name, float x) => new PartyRow { Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = 200,
                                                                             Strength = 400, Speed = 5, FoodDays = 10, X = x, Y = 100, Doing = "Hold", Spottable = true };
            var a = Lord("ga", "Feren", 100);      // 60 from the foe
            var b = Lord("gb", "Náth", 99);        // 61: there about when Feren is
            var c = Lord("gc", "Dáin", 10);        // 150: far beyond Feren's march and the join hours
            var scout = Lord("gs", "Gaín", 157);   // no order: keeps the foe in view
            var foe = new PartyRow { Id = "p_fx", Leader = "lord_fx", Name = "Lorgath", Faction = "player_k", Kind = "lord", Men = 500, Strength = 1000, Speed = 3,
                                     X = 160, Y = 100, Doing = "Hold", Spottable = true };
            Snapshot World(double h) => ToolWorld(h, a, b, c, scout, foe);
            double h0 = 89000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            ToolOrder Engage(string id, string name) => new ToolOrder { Tool = "engage", Lord = "lord_" + id, LordName = name, Party = "p_" + id, Target = "p_fx", Realm = "orcs" };
            Runner.Start(9252, Engage("ga", "Feren"));
            World w0 = Runner.WorldFor(World(h0));
            double alone = Expr.Number(ScopeFor(9252, w0)?.Call("fight_odds", new object[] { "p_fx" }));
            Runner.Start(9253, Engage("gb", "Náth"));
            Runner.Start(9254, Engage("gc", "Dáin"));
            Diag.Hours = h0 + 1;
            World w1 = Runner.WorldFor(World(h0 + 1));
            double group = Expr.Number(ScopeFor(9252, w1)?.Call("fight_odds", new object[] { "p_fx" }));
            Check(alone > 2.3 && group > 1.1 && group < 1.4,
                  "a lord sent at a foe counts the lords sent at him who can be there by the time he is, and not one far beyond (odds " + ToolDef.Num(Math.Round(group, 2))
                  + " with Náth, against " + ToolDef.Num(Math.Round(alone, 2)) + " alone)");
            foreach (int id in new[] { 9252, 9253, 9254 }) Runner.Ended(id, "test over");
            Runner.Reset();
            StrikeMates0253Tests();
        }

        /// <summary>
        /// Module 0.25.3 (Nov 27, run 20261126-1838-C2, day 125 at Lasgalor): the agent's camp strike weighs mates_strength(me, group_r), and a late lord counted the
        /// mates already fighting at the camp as nothing ("fighting elsewhere" than at himself), so the late lords called the strike off while their mates fought on.
        /// A mate fighting at the order's own place counts.
        /// </summary>
        private static void StrikeMates0253Tests()
        {
            Runner.Reset();
            var fighting = new PartyRow { Id = "p_mf", Leader = "lord_mf", Name = "Forim", Faction = "orcs", Kind = "lord", Men = 1500, Strength = 3000, Speed = 5, FoodDays = 10,
                                          X = 102, Y = 100, Doing = "Hold", InBattle = true, Spottable = true };
            var late = new PartyRow { Id = "p_ml", Leader = "lord_ml", Name = "Nár", Faction = "orcs", Kind = "lord", Men = 200, Strength = 400, Speed = 5, FoodDays = 10,
                                      X = 130, Y = 100, Doing = "Hold", Spottable = true };
            var camp = new PartyRow { Id = "p_mc", Leader = "lord_mc", Name = "Shelka", Faction = "player_k", Kind = "lord", Men = 1800, Strength = 3600, Speed = 4,
                                      X = 102, Y = 100, Doing = "BesiegeSettlement", Besieging = "castle_orc", TargetSettlement = "castle_orc", InBattle = true, Spottable = true };
            Snapshot World(double h)
            {
                Snapshot s = ToolWorld(h, fighting, late, camp);
                s.Battles.Add(new BattleRow
                {
                    Key = "p_mc@" + (h - 3).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "sally", Settlement = "castle_orc", X = 102, Y = 100, StartHours = h - 3,
                    Attackers = new BattleSideRow { Leader = "p_mf", Faction = "orcs", Healthy = 1500, StartHealthy = 1600, Power = 2f, Advantage = 1f, Morale = 80f, Parties = { "p_mf" } },
                    Defenders = new BattleSideRow { Leader = "p_mc", Faction = "player_k", Healthy = 1800, StartHealthy = 1900, Power = 2f, Advantage = 1f, Morale = 70f, Parties = { "p_mc" } }
                });
                return s;
            }
            double h0 = 90000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            Runner.Start(9255, new ToolOrder { Tool = "defend", Lord = "lord_mf", LordName = "Forim", Party = "p_mf", At = "castle_orc", Realm = "orcs" });
            Runner.Start(9256, new ToolOrder { Tool = "defend", Lord = "lord_ml", LordName = "Nár", Party = "p_ml", At = "castle_orc", Realm = "orcs" });
            Diag.Hours = h0 + 1;
            World w = Runner.WorldFor(World(h0 + 1));
            double mates = Expr.Number(ScopeFor(9256, w)?.Call("mates_strength", new object[] { "p_ml", 45.0 }));
            Check(mates > 3300 && mates < 3500,
                  "a late lord's group count (mates_strength around himself) counts the mate already fighting at the order's own place (" + ToolDef.Num(mates) + ", his 400 and the 3,000 fighting)");
            foreach (int id in new[] { 9255, 9256 }) Runner.Ended(id, "test over");
            Runner.Reset();
            ShelterMates0255Tests();
        }
    }
}
