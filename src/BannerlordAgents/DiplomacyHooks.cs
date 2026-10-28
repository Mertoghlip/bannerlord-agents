using System;
using TaleWorlds.CampaignSystem.Election;

namespace BannerlordAgents
{
    /// <summary>
    /// The ruler's choice in a vote (KingdomElection.GetAiChoice, 0.13, Diplomacy): in a realm an agent rules, the agent's vote counted as its
    /// clan's support and its decision taken, else the outcome its clans support most (never the game's own ruler's whim); elsewhere the
    /// game's own choice. Every choice is kept for the announcement that follows it (ShowPatch).
    /// </summary>
    internal static class ChoicePatch
    {
        private static bool Prefix(KingdomElection __instance, TaleWorlds.Library.MBReadOnlyList<DecisionOutcome> possibleOutcomes, ref DecisionOutcome __result)
        {
            try
            {
                DecisionOutcome ruler = Diplomacy.RulerChoice(__instance, possibleOutcomes);
                if (ruler == null) return true;
                __result = ruler;
                return false;
            }
            catch (Exception e)
            {
                Diag.Swallowed("the ruler's choice in an agent's realm", e);
                return true;
            }
        }

        private static void Postfix(KingdomElection __instance, DecisionOutcome __result)
        {
            Diplomacy.LastElection = __instance;
            Diplomacy.LastChoice = __result;
        }
    }

    /// <summary>
    /// A vote's result about to be announced and applied (KingdomDecision.OnShowDecision and the four that override it, 0.13): held for the
    /// agent of the realm voting, or as an offer to the realm an agent rules (Diplomacy.Hold); a "no" to your kingdom while an agent rules it
    /// is applied without the player's window.
    /// </summary>
    internal static class ShowPatch
    {
        private static bool Prefix(KingdomDecision __instance, ref bool __result)
        {
            try
            {
                if (Diplomacy.Hold(__instance))
                {
                    __result = false;
                    return false;
                }
                if (Diplomacy.QuietNo(__instance))
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception e) { Diag.Swallowed("holding a vote for an agent", e); }
            return true;
        }
    }
}
