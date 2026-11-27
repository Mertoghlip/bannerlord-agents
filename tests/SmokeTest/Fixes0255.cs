using System;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// Module 0.25.5 (Nov 27, run 20261127-0430-C1): guards sat inside castles and towns for weeks (125 stays of 5 days or more, the longest 50), since lords
        /// inside a holding counted for nobody: a guard group that went in when the threat passed 1.2 times the group could come out only once it fell below 72% of
        /// one of them. A lord holding a guard or harass order inside a holding now counts the lords inside the same holding who hold a guard or harass order; and
        /// harass's withdraw check, weighed while sheltering, counts the group inside (it counted neither the mates nor the lord himself).
        /// </summary>
        private static void ShelterMates0255Tests()
        {
            Runner.Reset();
            PartyRow Lord(string id, string name, float strength, float x, string inside) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), Strength = strength, Speed = 4, FoodDays = 10,
                X = x, Y = 100, Doing = "Hold", InSettlement = inside, Spottable = true
            };
            var g1 = Lord("sg1", "Uglag", 500, 100, "castle_orc");
            var g2 = Lord("sg2", "Muzga", 500, 100, "castle_orc");
            var g3 = Lord("sg3", "Lugdush", 500, 100, "castle_orc");
            var idle = Lord("sg4", "Snaga", 700, 100, "castle_orc");     // inside, no order of ours: not counted
            var h1 = Lord("sh1", "Grishnakh", 400, 140, "castle_two");
            var h2 = Lord("sh2", "Shagrat", 300, 140, "castle_two");
            Snapshot World(double h) => ToolWorld(h, g1, g2, g3, idle, h1, h2);
            double h0 = 91000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            ToolOrder Defend(string id, string name) => new ToolOrder { Tool = "defend", Lord = "lord_" + id, LordName = name, Party = "p_" + id, At = "castle_orc", Realm = "orcs" };
            ToolOrder Harass(string id, string name) => new ToolOrder { Tool = "harass", Lord = "lord_" + id, LordName = name, Party = "p_" + id, At = "castle_men", Realm = "orcs" };
            Runner.Start(9257, Defend("sg1", "Uglag"));
            Runner.Start(9258, Defend("sg2", "Muzga"));
            Runner.Start(9259, Defend("sg3", "Lugdush"));
            Runner.Start(9260, Harass("sh1", "Grishnakh"));
            Runner.Start(9261, Harass("sh2", "Shagrat"));
            Diag.Hours = h0 + 1;
            World w = Runner.WorldFor(World(h0 + 1));
            double side = Expr.Number(ScopeFor(9257, w)?.Call("side_strength", new object[] { "p_sg1" }));
            Check(side > 1450 && side < 1550,
                  "a guard inside a castle counts the guards inside with him, not a lord there with no order of ours (side " + ToolDef.Num(side) + ": his 500 and two guards' 500 each)");
            double group = Expr.Number(ScopeFor(9260, w)?.Call("mates_strength", new object[] { "castle_men", 40.0 }));
            double sideH = Expr.Number(ScopeFor(9260, w)?.Call("side_strength", new object[] { "p_sh1" }));
            Check(group > 650 && group < 750 && sideH > 650 && sideH < 750,
                  "a harasser sheltering inside a castle counts himself and his mate inside for his group (" + ToolDef.Num(group) + ") and his side (" + ToolDef.Num(sideH) + "), 400 and 300");
            foreach (int id in new[] { 9257, 9258, 9259, 9260, 9261 }) Runner.Ended(id, "test over");
            Runner.Reset();
            ThreatFights0255Tests();
        }

        /// <summary>
        /// Module 0.25.5 (Nov 27): the enemy lords' fight and the bandits' fight never join (bandits are at war with every realm and with none of their own kind,
        /// and a party joins a side only when none there is at war with it): the threat a lord weighs is the worse of the two, never their sum; the bandits' fight
        /// is the strongest band near him with the bands that could join it within two hours; and a fight weighed against a band counts the bands that reach it
        /// while it lasts, never enemy lords. Guards sat inside for weeks at Caras Laerolin, about 49 bands and no enemy lord within 50.
        /// </summary>
        private static void ThreatFights0255Tests()
        {
            Runner.Reset();
            var guard = new PartyRow { Id = "p_tg", Leader = "lord_tg", Name = "Ufthak", Faction = "orcs", Kind = "lord", Men = 250, Strength = 500, Speed = 4, FoodDays = 10, X = 100, Y = 100, Doing = "Hold", Spottable = true };
            PartyRow Band(string id, float x, float y) => new PartyRow { Id = "p_" + id, Name = "Looters", Faction = "looters", Kind = "bandit", Men = 150, Strength = 200, Speed = 4, X = x, Y = y, Doing = "PatrolAroundPoint", Spottable = true };
            PartyRow[] far = { Band("bf1", 120, 100), Band("bf2", 100, 122), Band("bf3", 80, 100) };   // about 20 off, none within two hours of him or of each other
            var pair = new[] { Band("bc1", 103, 100), Band("bc2", 103, 101) };                           // two bands side by side, close to him
            var enemy = new PartyRow { Id = "p_te", Leader = "lord_te", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 450, Strength = 900, Speed = 4, X = 115, Y = 100, Doing = "Hold", Spottable = true };
            double h0 = 92000;
            Snapshot Snap(double h, params PartyRow[] more) => ToolWorld(h, new[] { guard }.Concat(more).ToArray());
            Diag.Hours = h0;
            Brain.OnHour(Snap(h0, far));
            Runner.Start(9262, new ToolOrder { Tool = "defend", Lord = "lord_tg", LordName = "Ufthak", Party = "p_tg", At = "castle_orc", Realm = "orcs" });
            Diag.Hours = h0 + 1;
            double Threat(params PartyRow[] more) => Expr.Number(ScopeFor(9262, Runner.WorldFor(Snap(h0 + 1, more)))?.Call("threat_near", new object[] { "p_tg", 30.0 }));
            double bandsOnly = Threat(far), withLord = Threat(far.Append(enemy).ToArray()), withPair = Threat(far.Concat(pair).ToArray());
            Check(Math.Abs(bandsOnly - 200) < 1 && Math.Abs(withLord - 900) < 1 && Math.Abs(withPair - 400) < 1,
                  "the threat near a lord is the worse of the enemy lords' fight and the bandits' (the strongest band and those that could join it within two hours), never their sum: three bands apart "
                  + ToolDef.Num(bandsOnly) + " (was 600), with an enemy lord of 900 " + ToolDef.Num(withLord) + " (was 1,500), with two bands side by side " + ToolDef.Num(withPair));
            BannerlordAgents.Tools.World wd = Runner.WorldFor(Snap(h0 + 1, far.Append(enemy).ToArray()));
            double danger = wd.DangerAround("p_tg", 30, 8, e => false).Sum(BannerlordAgents.Tools.World.StrengthOf);
            double dangerBands = Runner.WorldFor(Snap(h0 + 1, far)).DangerAround("p_tg", 30, 8, e => false).Sum(BannerlordAgents.Tools.World.StrengthOf);
            Check(Math.Abs(danger - 900) < 1 && Math.Abs(dangerBands - 200) < 1,
                  "the safety rule's danger is the worse fight too: the enemy lord (" + ToolDef.Num(danger) + "), or with bands only the strongest band (" + ToolDef.Num(dangerBands) + ")");
            double odds = Expr.Number(ScopeFor(9262, Runner.WorldFor(Snap(h0 + 1, pair.Append(enemy).ToArray())))?.Call("fight_odds", new object[] { "p_bc1" }));
            Check(odds > 0.7 && odds < 0.9,
                  "a fight weighed against a band counts the band beside it that reaches the fight while it lasts, not the enemy lord near him (odds " + ToolDef.Num(Math.Round(odds, 2)) + ": 400 against his 500; was 0.4)");
            Runner.Ended(9262, "test over");
            Runner.Reset();
        }
    }
}
