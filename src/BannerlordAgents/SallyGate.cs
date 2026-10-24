using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// The sally rule (0.11.4; run C2, Oct 23: Aragorn's and Imák's armies, sheltering in Imnagath on the agent's orders, were sent
    /// out by the game against Lorgath's 22.6k the hour his siege began, and all 20 lords were taken). The game's own test
    /// (SallyOutsCampaignBehavior.CheckSallyOut, 1.4.8), every 4th hour for each besieged castle or town: everyone inside (the
    /// garrison and the lords there) and friendly parties at the besiegers' camp go out when more than 2 times the enemy parties
    /// within 3 of the camp's leader; beside a relief's battle at the camp, 1.5 times, and they join it. The game skips the test
    /// for a holding whose defence the player leads from inside, who decides at the menu instead.
    /// Our orders put lords there (shelter, garrison, defend), so ours decide too: while any lord one of our orders holds is
    /// inside, the defenders never go out on their own. They still join a relief's battle by the game's own bar, which the
    /// relieve order counts on (World.ReliefStrength). Every other holding keeps the game's test untouched.
    /// For each holding of a realm with an agent, every test is recorded with the game's own numbers (the "sally" events in the
    /// trace): inside, friends and enemies as it counts them, and the besiegers it did not count, with how far they stood and
    /// how aggressive they were. C2's count is not understood yet (by our numbers it needed under 8k of Lorgath's 22.6k).
    /// </summary>
    internal static class SallyGate
    {
        /// <summary>The last decision logged for each besieged holding, so the log names a change once.</summary>
        private static readonly Dictionary<string, string> Last = new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Reset() => Last.Clear();

        /// <summary>Game's thread, in front of the game's test: false keeps the defenders inside.</summary>
        public static bool Allow(Settlement s, bool naval)
        {
            MobileParty leader = s?.SiegeEvent?.BesiegerCamp?.LeaderParty;
            if (leader == null || s.Town == null) return true;
            MapEvent atCamp = leader.MapEvent;
            bool relief = atCamp != null && (atCamp.IsSiegeOutside || atCamp.IsBlockade);
            List<string> ours = s.Parties.Where(p => p != null && p.IsLordParty && Orders.Holds(p)).Select(Names.Party).ToList();
            if (ours.Count == 0 && !Bridge.IsSeat(s.MapFaction)) return true;

            // the game's own numbers, counted as its test counts them
            MapEvent.PowerCalculationContext ctx = naval ? MapEvent.PowerCalculationContext.SeaBattle : MapEvent.PowerCalculationContext.PlainBattle;
            float inside = 0, friends = 0, enemies = 0;
            foreach (PartyBase x in s.GetInvolvedPartiesForEventType(naval ? MapEvent.BattleTypes.BlockadeSallyOutBattle : MapEvent.BattleTypes.SallyOut))
                inside += x.GetCustomStrength(BattleSideEnum.Attacker, ctx);
            var counted = new HashSet<MobileParty>();
            LocatableSearchData<MobileParty> data = MobileParty.StartFindingLocatablesAroundPosition(leader.Position.ToVec2(), Campaign.Current.Models.EncounterModel.GetEncounterJoiningRadius);
            for (MobileParty m = MobileParty.FindNextLocatable(ref data); m != null; m = MobileParty.FindNextLocatable(ref data))
            {
                if (m.CurrentSettlement != null || m.Aggressiveness <= 0f) continue;
                float w = m.Aggressiveness > 0.5f ? 1f : m.Aggressiveness * 2f;
                if (m.MapFaction.IsAtWarWith(s.Party.MapFaction)) { enemies += w * m.Party.GetCustomStrength(BattleSideEnum.Defender, ctx); counted.Add(m); }
                else if (m.MapFaction == s.MapFaction && naval == m.IsCurrentlyAtSea) friends += w * m.Party.GetCustomStrength(BattleSideEnum.Attacker, ctx);
            }
            // the whole camp, and those of it the test passed over
            float camp = 0;
            int campParties = 0;
            var missed = new List<object>();
            foreach (PartyBase x in s.SiegeEvent.BesiegerCamp.GetInvolvedPartiesForEventType())
            {
                float str = x.GetCustomStrength(BattleSideEnum.Defender, ctx);
                camp += str;
                campParties++;
                MobileParty m = x.MobileParty;
                if (m == null || counted.Contains(m)) continue;
                missed.Add(new
                {
                    party = m.StringId, strength = Math.Round(str), distance = Math.Round((m.Position.ToVec2() - leader.Position.ToVec2()).Length, 2),
                    aggressiveness = Math.Round(m.Aggressiveness, 2), attached = m.AttachedTo != null, inside = m.CurrentSettlement?.StringId,
                    at_war = m.MapFaction != null && m.MapFaction.IsAtWarWith(s.Party.MapFaction)
                });
            }
            float bar = relief ? 1.5f : 2f;
            bool goes = inside + friends > enemies * bar;
            bool keep = ours.Count > 0 && !relief;
            string decision = keep ? (goes ? "kept inside (your lords hold orders there)" : "stays inside") : goes ? (relief ? "join the relief" : "sally out") : "stay inside";
            Diag.Event("sally", "check", new
            {
                place = s.StringId, realm = s.MapFaction?.StringId, besieger = leader.StringId, relief, naval, inside = Math.Round(inside), friends = Math.Round(friends),
                enemies = Math.Round(enemies), bar, camp = Math.Round(camp), camp_parties = campParties,
                counted = counted.Count, missed, ours, decision
            });
            if (!Last.TryGetValue(s.StringId, out string was) || was != decision)
            {
                Last[s.StringId] = decision;
                if (goes || keep)
                    Log.Line("sally test at " + Names.Settlement(s) + ": the defenders " + decision + "; inside " + Text.N((int)inside) + " and friends " + Text.N((int)friends)
                             + " against " + Text.N((int)enemies) + " of the besiegers counted (the camp " + Text.N((int)camp) + ", " + missed.Count + " parties of it not counted), the game's bar " + bar + " times");
            }
            return !keep;
        }
    }
}
