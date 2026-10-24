using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace BannerlordAgents
{
    /// <summary>
    /// The realms' votes (0.11.3, after run C1). The game's player clan is fixed (the clan "player_faction"), so when the player
    /// switches to another hero of it for a run an agent plays (agents.run playerless), the kingdom is still the player's to
    /// the game: a vote whose time is up waits for the player's own resolution (KingdomDecision.NeedsPlayerResolution, true
    /// while the ruling clan is the player's), and the map opens the kingdom screen for it only when no menu is up, so it
    /// waited all of run C1: 2 votes proposed, 0 settled in 60 days, and no conquered holding was ever put to a vote (the game
    /// opens one holding's vote at a time). While the player's clan rules and the player is not its ruler, a vote that is due
    /// is settled as the game settles a computer-run realm's (KingdomElection.StartElectionWithoutPlayer, as
    /// KingdomDecisionProposalBehavior.UpdateKingdomDecisions does). And a vote of any of our realms still open a day after its
    /// time raises an alarm. Game's thread, every in-game hour.
    /// </summary>
    internal static class Votes
    {
        private static readonly HashSet<KingdomDecision> Alarmed = new HashSet<KingdomDecision>();

        public static void Reset() => Alarmed.Clear();

        public static void Hourly()
        {
            Clan pc = Clan.PlayerClan;
            Kingdom mine = pc?.Kingdom;
            if (mine != null && mine.RulingClan == pc && mine.Leader != Hero.MainHero) SettleForTheAbsentRuler(mine);
            var realms = new HashSet<Kingdom>(Bridge.Seats);
            if (mine != null) realms.Add(mine);
            foreach (Kingdom k in realms) CheckWaiting(k);
        }

        private static void SettleForTheAbsentRuler(Kingdom k)
        {
            foreach (KingdomDecision d in k.UnresolvedDecisions.ToList())
            {
                try
                {
                    if (d.ShouldBeCancelled() || !(d.IsEnforced || d.TriggerTime.IsPast)) continue;
                    string title = d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
                    new KingdomElection(d).StartElectionWithoutPlayer();
                    Diag.Count("vote:settled for the absent ruler");
                    Diag.Event("vote", "settled for the absent ruler", new { kingdom = k.StringId, vote = title, proposer = d.ProposerClan?.StringId });
                    Log.Line("vote: " + Names.Faction(k) + "'s vote \"" + title + "\" settled as a computer-run realm's would be: its ruler, " + Names.Hero(k.Leader) + ", is not you now.");
                }
                catch (Exception e) { Diag.Error("settling a vote for the absent ruler", e); }
            }
        }

        private static void CheckWaiting(Kingdom k)
        {
            foreach (KingdomDecision d in k.UnresolvedDecisions)
            {
                if (Alarmed.Contains(d) || d.TriggerTime.ElapsedHoursUntilNow < 24) continue;
                Alarmed.Add(d);
                string title = d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
                Diag.Alarm("vote_waiting", Names.Faction(k) + "'s vote \"" + title + "\" is still open a day after its time, so the realm's votes (holdings to give out, policies, war and peace) wait behind it.",
                           true, new { kingdom = k.StringId, vote = title });
            }
        }
    }
}
