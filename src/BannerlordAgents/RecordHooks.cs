using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace BannerlordAgents
{
    /// <summary>
    /// The party screen's Done (0.13, the record, GameHooks.InstallRecord): with a garrison or a clan party on its left, the troops and
    /// prisoners moved to or from it, counted from the screen's own rosters before (its initial copy) and after. Read only.
    /// </summary>
    internal static class PartyScreenPatch
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<PartyScreenLogic, PartyScreenData> Initial = HarmonyLib.AccessTools.FieldRefAccess<PartyScreenLogic, PartyScreenData>("_initialData");

        private static void Prefix(PartyScreenLogic __instance, out List<Recorder.Moved> __state)
        {
            __state = null;
            try
            {
                if (!Bridge.Running || __instance?.LeftOwnerParty == null || __instance.LeftOwnerParty == PartyBase.MainParty) return;
                PartyScreenData before = Initial(__instance);
                if (before == null || __instance.MemberRosters == null || __instance.MemberRosters.Length < 1) return;
                var moved = new List<Recorder.Moved>();
                Diff(before.LeftMemberRoster, __instance.MemberRosters[0], false, moved);
                if (__instance.PrisonerRosters != null && __instance.PrisonerRosters.Length > 0) Diff(before.LeftPrisonerRoster, __instance.PrisonerRosters[0], true, moved);
                if (moved.Count > 0) __state = moved;
            }
            catch (Exception e) { Diag.Swallowed("the party screen for the record", e); }
        }

        private static void Postfix(PartyScreenLogic __instance, bool __result, List<Recorder.Moved> __state)
        {
            if (!__result || __state == null) return;
            try { Recorder.OnPartyScreen(__instance.LeftOwnerParty, __state); }
            catch (Exception e) { Diag.Swallowed("the party screen for the record", e); }
        }

        /// <summary>Each troop's count on the left side after, less before.</summary>
        private static void Diff(TroopRoster before, TroopRoster after, bool prisoners, List<Recorder.Moved> into)
        {
            var counts = new Dictionary<CharacterObject, int>();
            if (before != null) for (int i = 0; i < before.Count; i++) { CharacterObject c = before.GetCharacterAtIndex(i); counts.TryGetValue(c, out int n); counts[c] = n - before.GetElementNumber(i); }
            if (after != null) for (int i = 0; i < after.Count; i++) { CharacterObject c = after.GetCharacterAtIndex(i); counts.TryGetValue(c, out int n); counts[c] = n + after.GetElementNumber(i); }
            foreach (KeyValuePair<CharacterObject, int> kv in counts)
                if (kv.Value != 0 && kv.Key != null) into.Add(new Recorder.Moved { Troop = kv.Key, Delta = kv.Value, Prisoner = prisoners });
        }
    }

    /// <summary>The player's vote in the realm's council, or choice as ruler (KingdomElection.OnPlayerSupport, OnPlayerAbstainedAsRuler): recorded (0.13).</summary>
    internal static class VotePatch
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<KingdomElection, KingdomDecision> Decision = HarmonyLib.AccessTools.FieldRefAccess<KingdomElection, KingdomDecision>("_decision");

        private static void Support(KingdomElection __instance, DecisionOutcome decisionOutcome, Supporter.SupportWeights supportWeight)
        {
            try { Recorder.OnPlayerVote(Decision(__instance), decisionOutcome, supportWeight, __instance.IsPlayerChooser); }
            catch (Exception e) { Diag.Swallowed("your vote for the record", e); }
        }

        private static void Abstain(KingdomElection __instance)
        {
            try { Recorder.OnPlayerAbstained(Decision(__instance)); }
            catch (Exception e) { Diag.Swallowed("your vote for the record", e); }
        }
    }

    /// <summary>The alliance and call-to-war windows shown to the player, and a call turned down (AllianceCampaignBehavior): recorded (0.13).</summary>
    internal static class OfferPatch
    {
        private static void Alliance(Kingdom offeringKingdom)
        {
            try { Recorder.OnAllianceOffered(offeringKingdom); }
            catch (Exception e) { Diag.Swallowed("an alliance offer for the record", e); }
        }

        private static void Call(Kingdom proposerKingdom, Kingdom kingdomToCallToWarAgainst)
        {
            try { Recorder.OnCallOffered(proposerKingdom, kingdomToCallToWarAgainst); }
            catch (Exception e) { Diag.Swallowed("a call to war for the record", e); }
        }

        private static void Deny(Kingdom callingKingdom, Kingdom calledKingdom)
        {
            try { Recorder.OnCallDenied(callingKingdom, calledKingdom); }
            catch (Exception e) { Diag.Swallowed("a call to war turned down, for the record", e); }
        }
    }
}
