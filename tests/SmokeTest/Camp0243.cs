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
        /// Module 0.24.3 (Nov 24, run 20261124-1400-C2: Girda's camp at Lasgalor, strength about 1,600, was weighed by its siege order as 5,900 with the lords due
        /// within 8 hours, so its withdraw bar of 1.0 never fired while the relief grew past the camp; the garrison then came out with Dol Guldur's lords and took
        /// the camp): the besiege order weighs a relief against the lords camped in the siege now (camp_strength), and the fight at the camp counts the garrison
        /// that comes out to join a relief once they and it are more than 1.5 times the camp.
        /// </summary>
        private static void CampStrengthTests(List<KeyValuePair<int, Move>> moves)
        {
            Runner.Reset();
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;
            PartyRow Orc(string id, string name, float strength, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), SizeLimit = 1000, Strength = strength, Speed = 5, FoodDays = 10,
                X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 5000
            };
            var camp = Orc("cg1", "Girda", 1600, 170, 103);
            camp.Besieging = "castle_men";
            camp.Doing = "BesiegeSettlement";
            camp.TargetSettlement = "castle_men";
            var due = Orc("cg2", "Thalion", 4000, 178, 106);      // a lord of ours a couple of hours off, marching to join the siege, not in the camp yet
            due.Doing = "BesiegeSettlement";
            due.TargetSettlement = "castle_men";
            var relief = new PartyRow { Id = "p_cr1", Leader = "lord_cr1", Name = "Grishûl", Faction = "player_k", Kind = "lord", Men = 1250, Strength = 2500, Speed = 4, X = 182, Y = 100,
                                        Doing = "DefendSettlement", TargetSettlement = "castle_men", Spottable = true };
            Snapshot World(double h, params PartyRow[] more)
            {
                Snapshot s = ToolWorld(h, new[] { camp, due }.Concat(more).ToArray());
                SettlementRow fornost = s.SettlementById["castle_men"];
                fornost.BesiegedBy = "orcs";
                fornost.Besieger = "p_cg1";
                fornost.GarrisonStrength = 600;
                s.Sieges.Add(new SiegeRow { Settlement = "castle_men", Besieger = "p_cg1", BesiegerFaction = "orcs", StartHours = h - 10, Prep = 0.5f, PrepRate = 0.02f, WallLevel = 1, Advantage = 2f, Defenders = 600, Attackers = 1600 });
                return s;
            }
            double h0 = 71000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            var order = new ToolOrder { Tool = "besiege", Lord = "lord_cg1", LordName = "Girda", Party = "p_cg1", At = "castle_men", Realm = "orcs" };
            order.Settings["withdraw_on_relief"] = 1.0;
            order.Settings["relief_radius"] = 30;
            order.Settings["storm"] = 2;
            Runner.Start(9243, order);
            Diag.Hours = h0 + 1;
            Brain.OnHour(World(h0 + 1));
            World w = Runner.WorldFor(World(h0 + 1));
            IScope sc = ScopeFor(9243, w);
            double campStr = Expr.Number(sc?.Call("camp_strength", new object[] { "castle_men" })), side = Expr.Number(sc?.Call("side_strength", new object[] { "castle_men" }));
            bool inSiege = StateOf(9243) == "siege";
            // the relief comes: 2,500 against the camp's 1,600, with the garrison's 600 coming out beside it (3,100 is over 1.5 times 1,600)
            Diag.Hours = h0 + 2;
            Brain.OnHour(World(h0 + 2, relief));
            World w2 = Runner.WorldFor(World(h0 + 2, relief));
            double odds = Expr.Number(ScopeFor(9243, w2)?.Call("fight_odds", new object[] { "castle_men" }));
            Diag.Hours = h0 + 3;
            Brain.OnHour(World(h0 + 3, relief));
            bool withdraws = StateOf(9243) == "withdraw" || StateOf(9243) == "out" || moves.Any(m => m.Key == 9243 && m.Value.Verb == "enter");
            Check(inSiege && Math.Abs(campStr - 1600) < 1 && side >= 5600 - 1 && odds > 1.8 && odds < 2.1 && withdraws,
                  "the besiege order weighs a relief against the lords camped in the siege now, not those on their way (camp " + ToolDef.Num(campStr) + " against the side's " + ToolDef.Num(side)
                  + "), the fight at the camp counts the garrison that comes out beside a relief (odds " + ToolDef.Num(Math.Round(odds, 2)) + "), and the camp withdraws by its bar of 1 ("
                  + inSiege + ", " + withdraws + ", " + StateOf(9243) + ")");
            Check(ToolDef.Functions.ContainsKey("camp_strength") && Library.Get("besiege") != null, "camp_strength is a function of the order language");
            Runner.Ended(9243, "test over");
            Runner.Reset();
        }
    }
}
