using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAgents.Common
{
    /// <summary>One side of a battle run ahead: its healthy men, mean troop power, battle advantage and morale.</summary>
    public sealed class BattleSide
    {
        public double Men;
        public float Power = 1f, Advantage = 1f, Morale = 50f;
        /// <summary>Behind walls (a storm's defenders): while the battle is a storm their morale counts at least 30 (MapEventSide). Also led by the holding.</summary>
        public bool Walls;
        /// <summary>
        /// Led by a holding, walls or not (module 0.24.7 / war room 0.20.10): its men never rout or break (MapEvent: a side whose leader party is a settlement).
        /// A storm a relief joined stays led by the holding once it is a battle outside the walls (MapEvent.AddInvolvedParty does not change the side's leader),
        /// so the lords who came to relieve it fight under the holding: no leader's Tactics edge (Advantage 1), no captain's bonus to their strength.
        /// </summary>
        public bool LedByPlace;
        /// <summary>How hard its blows land against the rules' expectation, measured in a battle under way (1: as the rules expect; Forecast.Measured).</summary>
        public double Hit = 1;
    }

    /// <summary>Men who reach a battle under way and join a side, at an hour counted from the battle's start.</summary>
    public sealed class BattleJoiner
    {
        public bool Attackers;
        public double Men, At;
        public float Power;
        /// <summary>Their own morale, blended into their side's by men as they join (negative: their side's, module 0.24.7).</summary>
        public float Morale = -1f;
    }

    public sealed class BattleOutcome
    {
        /// <summary>Hours from the start to the end (NaN: not within the horizon).</summary>
        public double Hours = double.NaN;
        /// <summary>"attackers" or "defenders", and how: "all down", "rout" or "retreat".</summary>
        public string Winner, How;
        public double AttackerLossPerHour, DefenderLossPerHour;
        public int AttackersLeft, DefendersLeft;
        /// <summary>A storm a relief joined: the hour from the start at which it became a battle outside the walls (NaN: it never did).</summary>
        public double OutsideAt = double.NaN;
    }

    /// <summary>
    /// A campaign-map battle as the game simulates it (read in the 1.4.8 code with TAOM's combat and power models for 0.7.3,
    /// docs/TOOLS.md "The game's clocks"), run ahead with the expected numbers. One file, built into the game module and the
    /// war room, so the report's clocks and the agent's "battle odds" question reckon a battle the same way.
    /// - Rounds: one an hour in a storm of the walls, two in any other battle (DefaultCombatSimulationModel).
    /// - Strikes a round (GetSimulationTicksForBattleRound): in a storm while the defenders are above 30 at a castle or 100 at
    ///   a town, the attackers 2 x round(1.5 + D^0.3), growing only with the defenders' number, the defenders 2 x round(0.5 +
    ///   max(1 + 0.7 x walls x D^0.3, (D+1)/(A+1))); with 10 defenders or fewer, min(3 x the other's men, 0.3 x its own), at
    ///   least 1; otherwise each side min(2 x the other's men, its own men^0.6).
    /// - Each strike knocks a man down with chance about 0.295 x (power ratio)^0.7 x advantage x (1 + 0.005 x (min(own morale
    ///   - 50, 0) - max(their morale - 50, 0))).
    /// - A side that loses a round (losses above 1.3 times the other's, above the other's plus one, and above 2% of its own
    ///   men) loses 3 morale; a side led by a party routs at 0; in a field battle, after 8 rounds a side at morale 20 or less
    ///   that lost the last 4 may retreat (chance 0.2 a round at middling valour), then is pursued 4 rounds without striking
    ///   back, and nothing is captured.
    /// - A storm that a party from outside joins on the defenders' side (a relief) becomes a battle outside the walls (MapEvent.AddInvolvedParty:
    ///   a siege assault joined by a defender not inside the settlement turns SiegeOutside; the combat model weighs the walls only for Siege):
    ///   from that hour two rounds an hour, each side's strikes by the field rule, the walls no longer counting for anyone, the garrison included;
    ///   the besiegers' storm penalty (-0.1 to their advantage, DefaultCombatSimulationModel.GetBattleAdvantage for Siege only) is gone and the
    ///   defenders' morale floor of 30 with it (MapEventSide, storms only); the side stays led by the holding, so it never breaks (module 0.24.7).
    /// - Only a field battle has retreats; a battle outside the walls has none (DefaultEncounterModel), and a side led by a holding never breaks.
    /// - Unseen morale (module 0.24.7, Nov 25): runs at 100, 85 and 70, 100 the headline. Run 20261125-1457-C2's two lost battles: Dol Guldur's
    ///   army sat at 100 all through both, and the old headline of 50 made every one of its blows land up to 25% harder in our reckoning than in the game.
    /// </summary>
    public static class GameBattle
    {
        public static void StrikesPerRound(bool storm, bool town, float walls, double attackers, double defenders, out double byAttackers, out double byDefenders)
        {
            double A = Math.Max(0, attackers), D = Math.Max(0, defenders);
            if (storm && D > (town ? 100 : 30))
            {
                byAttackers = 2 * Math.Round(1.5 + Math.Pow(D, 0.3), MidpointRounding.ToEven);
                byDefenders = 2 * Math.Round(0.5 + Math.Max(1 + 0.7 * walls * Math.Pow(D, 0.3), Math.Floor((D + 1) / (A + 1))), MidpointRounding.ToEven);
            }
            else if (D <= 10)
            {
                byDefenders = Math.Max(1, Math.Round(Math.Min(3 * A, 0.3 * D), MidpointRounding.ToEven));
                byAttackers = Math.Max(1, Math.Round(Math.Min(3 * D, 0.3 * A), MidpointRounding.ToEven));
            }
            else
            {
                byDefenders = Math.Round(Math.Min(2 * A, Math.Pow(D, 0.6)), MidpointRounding.ToEven);
                byAttackers = Math.Round(Math.Min(2 * D, Math.Pow(A, 0.6)), MidpointRounding.ToEven);
            }
        }

        public static double HitChance(float strikerPower, float targetPower, float advantage, double strikerMorale, double targetMorale)
        {
            double ratio = targetPower > 0 ? strikerPower / targetPower : 1;
            double morale = 1 + 0.005 * (Math.Min(strikerMorale - 50, 0) - Math.Max(targetMorale - 50, 0));
            return Math.Max(0, Math.Min(1, 0.295 * Math.Pow(ratio, 0.7) * Math.Max(0.1, advantage) * morale));
        }

        /// <summary>Runs a battle ahead round by round. kind: "siege" (a storm), "field", "sally", "siege_outside" or "raid".</summary>
        public static BattleOutcome Run(string kind, bool town, float walls, BattleSide attackers, BattleSide defenders, IEnumerable<BattleJoiner> joiners = null, double horizon = 24 * 5)
        {
            var o = new BattleOutcome();
            bool storm = kind == "siege", field = kind == "field";
            double A = attackers.Men, D = defenders.Men, mA = attackers.Morale, mD = defenders.Morale;
            double pA = attackers.Power, pD = defenders.Power;
            float advA = attackers.Advantage;
            bool held = defenders.Walls || defenders.LedByPlace;
            var pending = (joiners ?? Enumerable.Empty<BattleJoiner>()).OrderBy(j => j.At).ToList();
            int perHour = storm ? 1 : 2;
            int rounds = 0, lostA = 0, lostD = 0, retreat = 0, pursuit = 0;
            string retreating = null;
            double t = 0;
            bool first = true;
            while (t < horizon)
            {
                t += 1.0 / perHour;
                rounds++;
                while (pending.Count > 0 && pending[0].At <= t)
                {
                    BattleJoiner j = pending[0];
                    pending.RemoveAt(0);
                    // a relief joining a storm's defenders from outside turns it into a battle outside the walls (war room 0.20.4 / module 0.24.3);
                    // the storm's -0.1 to the besiegers' advantage goes with it (module 0.24.7: the game weighs it for Siege only)
                    if (storm && !j.Attackers && j.Men > 0) { storm = false; perHour = 2; o.OutsideAt = t; advA += 0.1f; }
                    float jp = j.Power > 0 ? j.Power : j.Attackers ? (float)pA : (float)pD;
                    if (j.Attackers)
                    {
                        if (j.Morale >= 0 && A + j.Men > 0) mA = (mA * A + j.Morale * j.Men) / (A + j.Men);
                        pA = A + j.Men > 0 ? (pA * A + jp * j.Men) / (A + j.Men) : pA; A += j.Men;
                    }
                    else
                    {
                        if (j.Morale >= 0 && D + j.Men > 0) mD = (mD * D + j.Morale * j.Men) / (D + j.Men);
                        pD = D + j.Men > 0 ? (pD * D + jp * j.Men) / (D + j.Men) : pD; D += j.Men;
                    }
                }
                StrikesPerRound(storm, town, walls, A, D, out double sA, out double sD);
                if (retreating == "attackers") sA = 0;
                if (retreating == "defenders") sD = 0;
                // the defenders' morale floor of 30 holds only behind the walls, while the battle is a storm (module 0.24.7)
                double mDeff = defenders.Walls && storm ? Math.Max(30, mD) : mD;
                double lossD = Math.Min(D, sA * Math.Min(1, HitChance((float)pA, (float)pD, advA, mA, mDeff) * attackers.Hit));
                double lossA = Math.Min(A, sD * Math.Min(1, HitChance((float)pD, (float)pA, defenders.Advantage, mDeff, mA) * defenders.Hit));
                if (first) { o.AttackerLossPerHour = lossA * perHour; o.DefenderLossPerHour = lossD * perHour; first = false; }
                bool aLost = lossA > 1.3 * lossD && lossA > lossD + 1 && lossA > 0.02 * A;
                bool dLost = lossD > 1.3 * lossA && lossD > lossA + 1 && lossD > 0.02 * D;
                // the game counts each side's lost rounds in a row (a round only one side can lose)
                if (aLost) { mA -= retreating == "attackers" ? 1 : 3; lostA++; } else lostA = 0;
                if (dLost) { mD -= retreating == "defenders" ? 1 : 3; lostD++; } else lostD = 0;
                A -= lossA;
                D -= lossD;
                if (A < 0.5) { End(o, t, "defenders", "all down"); break; }
                if (D < 0.5) { End(o, t, "attackers", "all down"); break; }
                if (mA <= 0) { End(o, t, "defenders", "rout"); break; }
                if (mD <= 0 && !held) { End(o, t, "attackers", "rout"); break; }
                if (field)
                {
                    if (retreating != null)
                    {
                        if (++pursuit >= 4) { End(o, t, retreating == "attackers" ? "defenders" : "attackers", "retreat"); break; }
                    }
                    else if (rounds > 8)
                    {
                        // the game's chance is 0.2 - 0.05 x valour a round; at middling valour a retreat comes about the fifth round it is open
                        if (mA <= 20 && lostA >= 4 && ++retreat >= 5) retreating = "attackers";
                        else if (!held && mD <= 20 && lostD >= 4 && ++retreat >= 5) retreating = "defenders";
                    }
                }
            }
            o.AttackersLeft = (int)Math.Round(Math.Max(0, A));
            o.DefendersLeft = (int)Math.Round(Math.Max(0, D));
            return o;
        }

        private static void End(BattleOutcome o, double t, string winner, string how) { o.Hours = t; o.Winner = winner; o.How = how; }

        /// <summary>The other side's unseen morale in the three runs (module 0.24.7): the headline (index 1) at full, then the hopeful cases.</summary>
        public static readonly float[] UnseenMorale = { 70f, 100f, 85f };

        /// <summary>
        /// The same battle at the other side's unseen morale (UnseenMorale: 100 the headline, run 1; 85 and 70 the hopeful cases, since 0.24.7;
        /// was 30, 50 and 70 with 50 the headline), for a side whose morale the realm cannot see: the headline run, and the hours of the three.
        /// </summary>
        public static BattleOutcome[] ByUnseenMorale(string kind, bool town, float walls, BattleSide attackers, BattleSide defenders, bool attackersUnseen, IEnumerable<BattleJoiner> joiners = null, double horizon = 24 * 5)
        {
            var list = joiners?.ToList();
            var runs = new BattleOutcome[3];
            float[] m = UnseenMorale;
            for (int i = 0; i < 3; i++)
            {
                BattleSide a = Copy(attackers), d = Copy(defenders);
                if (attackersUnseen) a.Morale = m[i]; else d.Morale = m[i];
                runs[i] = Run(kind, town, walls, a, d, list, horizon);
            }
            return runs;
        }

        private static BattleSide Copy(BattleSide s) => new BattleSide { Men = s.Men, Power = s.Power, Advantage = s.Advantage, Morale = s.Morale, Walls = s.Walls, LedByPlace = s.LedByPlace, Hit = s.Hit };

        /// <summary>"about 45 hours (32 to 60 as their unseen morale is lower or full)"; "more than 5 days" when it does not end. The headline is their morale at full (0.24.7).</summary>
        public static string HoursRange(BattleOutcome[] runs)
        {
            double mid = runs[1].Hours;
            var ends = runs.Where(r => !double.IsNaN(r.Hours)).Select(r => r.Hours).ToList();
            string Fmt(double h) => h < 1.5 ? "an hour" : h < 72 ? Math.Round(h) + " hours" : Math.Round(h / 24, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " days";
            if (double.IsNaN(mid)) return ends.Count == 0 ? "not within 5 days" : "not within 5 days with their morale at full (" + Fmt(ends.Min()) + " if their morale is lower)";
            if (ends.Count < 3) return "about " + Fmt(mid) + " with their morale at full (longer than 5 days in some cases)";
            double lo = ends.Min(), hi = ends.Max();
            return Math.Abs(hi - lo) < 1.5 ? "about " + Fmt(mid) : "about " + Fmt(mid) + " (" + Fmt(lo) + " to " + Fmt(hi) + " as their unseen morale is lower or full)";
        }
    }
}
