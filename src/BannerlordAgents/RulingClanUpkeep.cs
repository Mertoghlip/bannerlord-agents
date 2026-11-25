using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BannerlordAgents
{
    /// <summary>
    /// Module 0.24.6 (Nov 25, after run 20261125-0020-C2): in a playerless run the kingdom's ruling clan is the game's player clan (the clan made for the player at the
    /// campaign's start, Clan.PlayerClan, whose leader is the ruler while the player's stand-in is one of its companions). The game's daily clan step
    /// (ClanVariablesCampaignBehavior.DailyTickClan) skips the player clan for four things it does for every other clan: the financial evaluation that sets each of its
    /// parties' wage limit from the clan's gold (MakeClanFinancialEvaluation), its governors, its garrisons' wage limits and its garrisons' recruiting. So House of
    /// Telcontar's six parties went on paying full wages until its gold was gone on day 27, and stayed broke. Here the same four steps are run for it each day (by
    /// name, the game's own private methods), only while the player's hero is not the clan's leader (a playerless run; when I play the ruler, the game's own rules
    /// for the player stand). Right after, the agent's own wage caps are put back as for every clan (Wages.OnDailyClan). Game thread, once a day.
    /// </summary>
    internal static class RulingClanUpkeep
    {
        private static object _behavior;
        private static MethodInfo[] _steps;
        private static bool _resolved, _told;

        internal static void Daily(Clan clan)
        {
            if (!Settings.RulingClanUpkeep || clan == null || clan != Clan.PlayerClan || clan.IsEliminated || clan.Kingdom == null || clan.Leader == null || clan.Leader == Hero.MainHero) return;
            try
            {
                Resolve();
                if (_behavior == null || _steps == null) return;
                int done = 0;
                foreach (MethodInfo m in _steps)
                {
                    if (m == null) continue;
                    m.Invoke(_behavior, new object[] { clan });
                    done++;
                }
                Diag.Event("upkeep", "ruling clan", new { clan = clan.StringId, name = clan.Name?.ToString(), gold = clan.Leader.Gold, steps = done });
                if (!_told)
                {
                    _told = true;
                    Log.Line("ruling clan upkeep: " + clan.Name + " is the player clan but the player does not lead it, so the game's daily clan upkeep (wage limits by gold, governors, garrison wages and recruiting) runs for it too (" + done + " of 4 steps found).");
                }
            }
            catch (Exception e) { Diag.Swallowed("the ruling clan's upkeep", e); }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _behavior = Campaign.Current?.GetCampaignBehavior<ClanVariablesCampaignBehavior>();
            Type t = typeof(ClanVariablesCampaignBehavior);
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo Step(string name) => t.GetMethod(name, any, null, new[] { typeof(Clan) }, null);
            _steps = new[] { Step("MakeClanFinancialEvaluation"), Step("UpdateGovernorsOfClan"), Step("UpdateClanSettlementsPaymentLimit"), Step("UpdateClanSettlementAutoRecruitment") };
            int found = 0;
            foreach (MethodInfo m in _steps) if (m != null) found++;
            if (found < 4) Diag.Alarm("upkeep_missing", "The ruling clan's daily upkeep found " + found + " of the game's 4 clan steps; the missing ones are not run for it.", false);
        }
    }
}
