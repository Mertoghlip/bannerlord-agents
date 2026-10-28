using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Localization;

namespace BannerlordAgents
{
    /// <summary>
    /// The agents' diplomacy (0.13, docs/PLAN-0.13.md Part B), on the game's thread. Everything goes through the game's own votes, costs and
    /// rules, and the pack's (the War of the Ring):
    /// - acts: a proposal becomes the game's own vote in the realm (Kingdom.AddDecision, the ruler's clan the proposer, the game's influence
    ///   price), settled at once with the ruler's clan supporting it fully; mercenary clans are hired and let go on the game's own terms;
    /// - votes: a computer-run realm settles its votes the moment they are proposed, so in a realm an agent rules each vote proposed by its
    ///   other clans is held where the game is about to announce it (DiplomacyHooks), with the clans' support already counted, for up to
    ///   VoteHours: the agent votes as its clan (the game's price for its weight) and, as ruler, may decide against the clans (the game's
    ///   price for overruling); then it is settled as the game settles it. With no decision the clans' choice stands;
    /// - offers: peace, an alliance, a call to war or a trade agreement a realm chose to offer one an agent rules is held as the game holds
    ///   such an offer for the player, for up to OfferHours, until its agent accepts or declines; with no answer it is accepted, as the game
    ///   does between the realms it runs.
    /// Held votes and offers live here only, never in the save. Game's thread, except the worker's copies of the views (Capture).
    /// </summary>
    internal static class Diplomacy
    {
        public const double VoteHours = 12, OfferHours = 12;

        internal sealed class Held
        {
            public string Id, Kind;
            public bool Offer;
            public KingdomElection Election;
            public KingdomDecision Decision;
            /// <summary>A vote: the realm voting; an offer: the realm it is made to.</summary>
            public Kingdom Realm;
            public double Opened, Closes;
            public bool ClosingTold;
        }

        /// <summary>The agent's vote as its realm's ruling clan, by the option's words (a vote the game holds is settled on new outcomes).</summary>
        private sealed class RulerVote
        {
            public string Option;
            public Supporter.SupportWeights Weight;
        }

        private static readonly List<Held> Holds = new List<Held>();
        private static readonly Dictionary<KingdomDecision, RulerVote> Votes = new Dictionary<KingdomDecision, RulerVote>();
        private static readonly Dictionary<KingdomDecision, string> Decisions = new Dictionary<KingdomDecision, string>();
        /// <summary>The agents' own proposals: settled at once, never held for their own vote.</summary>
        private static readonly HashSet<KingdomDecision> Proposed = new HashSet<KingdomDecision>();
        /// <summary>The votes of your realm the game holds itself, while an agent rules it, by id.</summary>
        private static readonly Dictionary<KingdomDecision, Held> GameHeld = new Dictionary<KingdomDecision, Held>();
        private static readonly HashSet<KingdomElection> Resuming = new HashSet<KingdomElection>();
        private static int _votes, _offers;

        /// <summary>The election the game last chose an outcome for, and that outcome (DiplomacyHooks: the announcement that follows reads them).</summary>
        internal static KingdomElection LastElection;
        internal static DecisionOutcome LastChoice;

        public static void Reset()
        {
            Holds.Clear();
            Votes.Clear();
            Decisions.Clear();
            Proposed.Clear();
            GameHeld.Clear();
            Resuming.Clear();
            LastElection = null;
            LastChoice = null;
        }

        // ---------- the game's private parts this needs ----------

        internal static readonly HarmonyLib.AccessTools.FieldRef<KingdomElection, KingdomDecision> DecisionOf = Ref<KingdomDecision>("_decision");
        internal static readonly HarmonyLib.AccessTools.FieldRef<KingdomElection, TaleWorlds.Library.MBList<DecisionOutcome>> Outcomes = Ref<TaleWorlds.Library.MBList<DecisionOutcome>>("_possibleOutcomes");
        internal static readonly HarmonyLib.AccessTools.FieldRef<KingdomElection, List<Supporter>> Supporters = Ref<List<Supporter>>("_supporters");
        internal static readonly HarmonyLib.AccessTools.FieldRef<KingdomElection, DecisionOutcome> Chosen = Ref<DecisionOutcome>("_chosenOutcome");

        /// <summary>A private field of the game's vote, by name; null when this game has no such field (then the agents' say in votes is off, and the self-check says why).</summary>
        private static HarmonyLib.AccessTools.FieldRef<KingdomElection, T> Ref<T>(string name)
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<KingdomElection, T>(name); }
            catch (Exception e) { Diag.Swallowed("the game's vote field " + name, e); return null; }
        }

        /// <summary>Everything the held votes and offers need is in this game.</summary>
        private static bool Ready => DecisionOf != null && Outcomes != null && Supporters != null && Chosen != null && GetAiChoice != null && ApplyChosen != null;
        private static readonly MethodInfo GetAiChoice = HarmonyLib.AccessTools.Method(typeof(KingdomElection), "GetAiChoice");
        private static readonly MethodInfo ApplyChosen = HarmonyLib.AccessTools.Method(typeof(KingdomElection), "ApplyChosenOutcome");

        /// <summary>The vote's private parts this reads, by name (the self-check): null when all are there.</summary>
        public static string PartsProblem
        {
            get
            {
                var missing = new List<string>();
                foreach (string f in new[] { "_decision", "_possibleOutcomes", "_supporters", "_chosenOutcome" })
                    if (HarmonyLib.AccessTools.Field(typeof(KingdomElection), f) == null) missing.Add(f);
                if (GetAiChoice == null) missing.Add("GetAiChoice");
                if (ApplyChosen == null) missing.Add("ApplyChosenOutcome");
                return missing.Count == 0 ? null : "the game's vote has no " + string.Join(", ", missing) + ", so held votes and offers cannot be settled";
            }
        }

        // ---------- who rules ----------

        /// <summary>Does a live agent of its own rule this realm (not a lieutenant, which serves the player)?</summary>
        public static bool Rules(Kingdom k)
        {
            if (k == null || !Bridge.Running) return false;
            RealmAgent a = Agents.Of(k.StringId);
            return a != null && !a.Config.IsLieutenant && a.Config.Mode == "live";
        }

        /// <summary>The realm an offer is made to, for the votes that end in one (peace, an alliance, a call to war, a trade agreement); null for the rest.</summary>
        public static Kingdom OfferTarget(KingdomDecision d)
        {
            switch (d)
            {
                case MakePeaceKingdomDecision p: return p.FactionToMakePeaceWith as Kingdom;
                case StartAllianceDecision a: return a.KingdomToStartAllianceWith;
                case ProposeCallToWarAgreementDecision c: return c.CalledKingdom;
                case TradeAgreementDecision t: return t.TargetKingdom;
                default: return null;
            }
        }

        private static string OfferKind(KingdomDecision d) => d is MakePeaceKingdomDecision ? "peace" : d is StartAllianceDecision ? "alliance" : d is ProposeCallToWarAgreementDecision ? "call" : d is TradeAgreementDecision ? "trade" : null;

        private static string VoteKind(KingdomDecision d) =>
            d is DeclareWarDecision ? "war" : d is MakePeaceKingdomDecision ? "peace" : d is StartAllianceDecision ? "alliance" : d is ProposeCallToWarAgreementDecision ? "call to war"
            : d is TradeAgreementDecision ? "trade" : d is KingdomPolicyDecision ? "policy" : d is SettlementClaimantDecision ? "fief" : d is SettlementClaimantPreliminaryDecision ? "annexation"
            : d is ExpelClanFromKingdomDecision ? "expulsion" : "other";

        /// <summary>The outcome that says yes to a vote's question (the queried one), for its own proposer and for an offer.</summary>
        private static DecisionOutcome Yes(KingdomDecision d, TaleWorlds.Library.MBReadOnlyList<DecisionOutcome> outcomes) => d.GetQueriedDecisionOutcome(outcomes);

        private static string Title(DecisionOutcome o) => o?.GetDecisionTitle()?.ToString() ?? "?";

        // ---------- the hooks' questions (DiplomacyHooks) ----------

        /// <summary>
        /// The ruler's choice in a realm an agent rules (the game's GetAiChoice): its vote counted as its clan's support, then its decision if
        /// it gave one, else the outcome the clans support most. Null: not an agent's realm (the game chooses).
        /// </summary>
        internal static DecisionOutcome RulerChoice(KingdomElection e, TaleWorlds.Library.MBReadOnlyList<DecisionOutcome> outcomes)
        {
            if (!Ready) return null;
            KingdomDecision d = DecisionOf(e);
            if (d == null || !Rules(d.Kingdom) || outcomes == null || outcomes.Count == 0) return null;
            Clan ruler = d.Kingdom.RulingClan;
            Supporter s = Supporters(e)?.FirstOrDefault(x => x.Clan == ruler);
            if (s != null)
            {
                foreach (DecisionOutcome o in outcomes) o.ResetSupport(s);
                // the agent's own proposal: its clan supports it fully; else its vote, if it gave one (no vote: its clan abstains)
                RulerVote v = Votes.TryGetValue(d, out RulerVote rv) ? rv : Proposed.Contains(d) ? new RulerVote { Option = Title(Yes(d, outcomes)), Weight = Supporter.SupportWeights.FullyPush } : null;
                DecisionOutcome voted = v != null ? outcomes.FirstOrDefault(o => Title(o) == v.Option) : null;
                if (voted != null)
                {
                    Supporter.SupportWeights w = v.Weight;
                    while (w >= Supporter.SupportWeights.SlightlyFavor && ruler.Influence < d.GetInfluenceCostOfSupport(ruler, w)) w--;
                    if (w >= Supporter.SupportWeights.SlightlyFavor)
                    {
                        s.SupportWeight = w;
                        voted.AddSupport(s);
                    }
                }
            }
            e.DetermineOfficialSupport();
            DecisionOutcome popular = outcomes.OrderByDescending(o => o.TotalSupportPoints).First();
            DecisionOutcome decided = Decisions.TryGetValue(d, out string title) ? outcomes.FirstOrDefault(o => Title(o) == title) : null;
            return decided ?? popular;
        }

        /// <summary>
        /// Before the game announces a vote's result (OnShowDecision): hold it when it is a vote of an agent's realm its agent has not decided
        /// (not its own proposal, not one the game holds itself), or an offer the proposer chose to make to a realm an agent rules. True: held,
        /// so the game does not apply it now.
        /// </summary>
        internal static bool Hold(KingdomDecision d)
        {
            KingdomElection e = LastElection;
            if (!Ready) return false;
            if (d == null || e == null || DecisionOf(e) != d) return false;
            Kingdom k = d.Kingdom;
            double now = CampaignTime.Now.ToHours;
            // a vote being settled now (Resuming) is not held again; it may still end in an offer to an agent's realm, held below
            if (!Resuming.Contains(e) && Rules(k) && k != Clan.PlayerClan?.Kingdom && !Proposed.Contains(d) && !Decisions.ContainsKey(d) && !(d is KingSelectionKingdomDecision))
            {
                var h = new Held { Id = "v" + (++_votes), Kind = VoteKind(d), Election = e, Decision = d, Realm = k, Opened = now, Closes = now + VoteHours };
                Holds.Add(h);
                Diag.Count("diplomacy:votes held");
                Diag.Event("diplomacy", "vote held", new { id = h.Id, realm = k.StringId, vote = d.GetGeneralTitle()?.ToString(), proposer = d.ProposerClan?.StringId, closes = h.Closes });
                Notices.Diplomacy(k, "vote", "A vote is open in your realm: " + d.GetGeneralTitle() + ", proposed by " + Names.Faction(d.ProposerClan) + " [" + h.Id + "]. It closes "
                                              + Text.When(h.Closes) + "; you vote and, as ruler, decide (OPEN VOTES).");
                Book("vote_held", h, null);
                return true;
            }
            Kingdom target = OfferTarget(d);
            DecisionOutcome chosen = LastChoice;
            if (target != null && target != k && Rules(target) && chosen != null && chosen == Yes(d, Outcomes(e)) && !RingForbids(d))
            {
                var h = new Held { Id = "o" + (++_offers), Kind = OfferKind(d), Offer = true, Election = e, Decision = d, Realm = target, Opened = now, Closes = now + OfferHours };
                Holds.Add(h);
                Diag.Count("diplomacy:offers held");
                Diag.Event("diplomacy", "offer held", new { id = h.Id, kind = h.Kind, from = k.StringId, to = target.StringId, closes = h.Closes });
                Notices.Diplomacy(target, "offer", Names.Faction(k) + " offers " + (h.Kind == "peace" ? "peace" : DiploRules.KindWords(h.Kind)) + " [" + h.Id + "]" + Terms(d, target)
                                                   + ". Answer by " + Text.When(h.Closes) + " (accept_" + h.Kind + " or decline_" + h.Kind + "); with no answer it is accepted.");
                Book("offer_held", h, null);
                return true;
            }
            return false;
        }

        /// <summary>A "no" to a realm an agent rules (your kingdom in a C run): never the player's window for it.</summary>
        internal static bool QuietNo(KingdomDecision d)
        {
            Kingdom target = OfferTarget(d);
            return target != null && target == Clan.PlayerClan?.Kingdom && Rules(target);
        }

        /// <summary>Peace the War of the Ring would block anyway: not offered to the agent, the game's own way follows (and the pack stops it).</summary>
        private static bool RingForbids(KingdomDecision d) => d is MakePeaceKingdomDecision p && p.FactionToMakePeaceWith is Kingdom other && RingRules.NoPeace(d.Kingdom, other);

        private static string Terms(KingdomDecision d, Kingdom to)
        {
            switch (d)
            {
                case MakePeaceKingdomDecision p when p.DailyTributeToBePaid > 0: return ": they pay you " + Text.N(p.DailyTributeToBePaid) + " a day for " + p.DailyTributeDurationInDays + " days";
                case MakePeaceKingdomDecision p when p.DailyTributeToBePaid < 0: return ": you pay them " + Text.N(-p.DailyTributeToBePaid) + " a day for " + p.DailyTributeDurationInDays + " days";
                case ProposeCallToWarAgreementDecision c: return ", to join their war on " + Names.Faction(c.KingdomToCallToWarAgainst) + " [" + c.KingdomToCallToWarAgainst.StringId + "]";
                default: return "";
            }
        }

        // ---------- every in-game hour (game's thread) ----------

        /// <summary>Votes and offers whose time is up settled as the game would; a vote closing within 3 hours that the agent has not voted on, told once.</summary>
        public static void Hourly()
        {
            if (!Bridge.Running) return;
            double now = CampaignTime.Now.ToHours;
            // your realm's own votes while an agent rules it: the game holds them; they are listed, and settled at the same time as the others
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (mine != null && Rules(mine))
            {
                foreach (KingdomDecision d in mine.UnresolvedDecisions.ToList())
                {
                    if (GameHeld.ContainsKey(d) || d is KingSelectionKingdomDecision) continue;
                    var h = new Held { Id = "v" + (++_votes), Kind = VoteKind(d), Decision = d, Realm = mine, Opened = now, Closes = now + VoteHours };
                    GameHeld[d] = h;
                    Holds.Add(h);
                    if (!Proposed.Contains(d))
                        Notices.Diplomacy(mine, "vote", "A vote is open in your realm: " + d.GetGeneralTitle() + ", proposed by " + Names.Faction(d.ProposerClan) + " [" + h.Id + "]. It closes " + Text.When(h.Closes) + ".");
                }
                foreach (Held h in Holds.Where(x => x.Election == null && !mine.UnresolvedDecisions.Contains(x.Decision)).ToList()) Forget(h);
            }
            foreach (Held h in Holds.ToList())
            {
                try
                {
                    if (now >= h.Closes - 1e-6)
                    {
                        string said = h.Offer ? Answer(h, true, "no answer came in time") : Settle(h);
                        Notices.Diplomacy(h.Realm, h.Offer ? "offer_settled" : "vote_settled", (h.Offer ? Names.Faction(h.Decision.Kingdom) + "'s offer [" + h.Id + "]: " : "The vote [" + h.Id + "], " + h.Decision.GetGeneralTitle() + ": ") + said);
                    }
                    else if (!h.Offer && !h.ClosingTold && h.Closes - now <= 3 && !Votes.ContainsKey(h.Decision) && !Decisions.ContainsKey(h.Decision))
                    {
                        h.ClosingTold = true;
                        Notices.Diplomacy(h.Realm, "vote_closing", "The vote [" + h.Id + "] (" + h.Decision.GetGeneralTitle() + ") closes " + Text.When(h.Closes) + " and you have not voted.");
                    }
                }
                catch (Exception e)
                {
                    Diag.Error("settling a held vote or offer", e);
                    Forget(h);
                }
            }
        }

        private static void Forget(Held h)
        {
            Holds.Remove(h);
            GameHeld.Remove(h.Decision);
            Votes.Remove(h.Decision);
            Decisions.Remove(h.Decision);
            Proposed.Remove(h.Decision);
        }

        /// <summary>A held vote settled now: the agent's vote and decision (if any) applied, as the game settles a vote. Returns what came of it.</summary>
        private static string Settle(Held h)
        {
            KingdomDecision d = h.Decision;
            Holds.Remove(h);
            try
            {
                if (h.Election == null)
                {
                    // one of your realm's votes the game holds: settled as the game settles a computer-run realm's (our hooks apply the agent's say)
                    new KingdomElection(d).StartElectionWithoutPlayer();
                    return Concluded(d);
                }
                if (d.Kingdom.IsEliminated || !d.IsAllowed())
                {
                    CampaignEventDispatcher.Instance?.OnKingdomDecisionCancelled(d, false);
                    Book("vote_cancelled", h, null);
                    return "it no longer stands (the realms' stances changed meanwhile), so it was dropped";
                }
                Resuming.Add(h.Election);
                try
                {
                    var chosen = (DecisionOutcome)GetAiChoice.Invoke(h.Election, new object[] { Outcomes(h.Election) });
                    Chosen(h.Election) = chosen;
                    if (d.OnShowDecision()) ApplyChosen.Invoke(h.Election, null);
                }
                finally { Resuming.Remove(h.Election); }
                return Concluded(d);
            }
            finally
            {
                GameHeld.Remove(d);
                Votes.Remove(d);
                Decisions.Remove(d);
                Proposed.Remove(d);
            }
        }

        /// <summary>What came of a vote just settled (the hooks keep the choice), in words; an offer held from it says so.</summary>
        private static string Concluded(KingdomDecision d)
        {
            Held offer = Holds.FirstOrDefault(x => x.Offer && x.Decision == d);
            if (offer != null) return "the clans and the ruler chose to offer it; " + Names.Faction(offer.Realm) + "'s ruler answers by " + Text.When(offer.Closes);
            string chose = LastElection != null && DecisionOf(LastElection) == d ? Title(LastChoice) : null;
            return chose != null ? "decided: " + chose + "." : "settled.";
        }

        /// <summary>An offer answered (or its time up): accepted, the game applies it as it would have; declined, it is dropped.</summary>
        private static string Answer(Held h, bool accept, string why)
        {
            Holds.Remove(h);
            KingdomDecision d = h.Decision;
            if (!accept)
            {
                Book("offer_declined", h, why);
                return "declined";
            }
            if (d.Kingdom.IsEliminated || h.Realm.IsEliminated || !d.IsAllowed())
            {
                Book("offer_lapsed", h, why);
                return "it no longer stands (the realms' stances changed), so nothing came of it";
            }
            Resuming.Add(h.Election);
            try { ApplyChosen.Invoke(h.Election, null); }
            finally { Resuming.Remove(h.Election); }
            Book("offer_accepted", h, why);
            return "accepted" + (why != null ? " (" + why + ")" : "") + ": " + Accepted(d);
        }

        private static string Accepted(KingdomDecision d)
        {
            switch (d)
            {
                case MakePeaceKingdomDecision p: return "peace is made between " + Names.Faction(d.Kingdom) + " and " + Names.Faction(p.FactionToMakePeaceWith) + (d.Kingdom.IsAtWarWith(p.FactionToMakePeaceWith) ? " (but the war goes on: the pack's rules kept it)" : "");
                case StartAllianceDecision a: return Names.Faction(d.Kingdom) + " and " + Names.Faction(a.KingdomToStartAllianceWith) + " are allies" + (d.Kingdom.IsAllyWith(a.KingdomToStartAllianceWith) ? "" : " (the game did not make it)");
                case ProposeCallToWarAgreementDecision c: return Names.Faction(c.CalledKingdom) + " joins " + Names.Faction(d.Kingdom) + "'s war on " + Names.Faction(c.KingdomToCallToWarAgainst);
                case TradeAgreementDecision t: return "a trade agreement between " + Names.Faction(d.Kingdom) + " and " + Names.Faction(t.TargetKingdom);
                default: return "done";
            }
        }

        // ---------- the agent's acts (game's thread) ----------

        /// <summary>One act of an agent's answer: checked against the realm's screens and the game's own conditions, then carried out (or checked only in shadow mode).</summary>
        public static void FromAgent(DiploAct a, Action<string, string> told = null)
        {
            string status = "refused", reason;
            double hours = CampaignTime.Now.ToHours;
            try
            {
                Kingdom k = a.Realm != null ? Bridge.SeatOf(a.Realm) : null;
                RealmAgent agent = k != null ? Agents.Of(k.StringId) : null;
                if (k == null) reason = (a.Realm ?? "the realm") + " is not under watch";
                else if (agent == null || agent.Config.IsLieutenant && Clan.PlayerClan?.Kingdom != k) reason = "no agent rules " + Names.Faction(k);
                else
                {
                    DiploView v = Capture(k);
                    reason = DiploRules.Check(a, v, out string target);
                    if (reason == null)
                    {
                        if (agent.Config.Mode == "shadow") { status = "checked"; reason = "valid: " + a + " (shadow mode: not carried out)"; }
                        else if (agent.Config.Mode != "live") reason = "the agent is off";
                        else reason = Carry(a, k, target, ref status);
                    }
                }
            }
            catch (Exception e)
            {
                reason = "it could not be carried out (" + e.Message + ")";
                Diag.Error("carrying out an agent's diplomatic act", e);
            }
            try { told?.Invoke(status, reason); }
            catch (Exception e) { Diag.Error("telling the lieutenant what became of an act", e); }
            string s = status, r = reason;
            Background.Post("diplomacy receipt", () => AgentChannel.DiploReceipt(a, s, r, hours));
        }

        private static string Carry(DiploAct a, Kingdom k, string target, ref string status)
        {
            Clan ruler = k.RulingClan;
            Kingdom other = target != null ? Kingdom.All.FirstOrDefault(x => x.StringId == target) : null;
            switch (a.Act)
            {
                case "propose_war": return Propose(a, k, new DeclareWarDecision(ruler, other), ref status);
                case "propose_peace":
                {
                    int tribute = a.Tribute ?? 0;
                    int days = a.Days ?? (tribute != 0 ? 100 : 0);
                    return Propose(a, k, new MakePeaceKingdomDecision(ruler, other, tribute, days), ref status);
                }
                case "propose_alliance": return Propose(a, k, new StartAllianceDecision(ruler, other), ref status);
                case "propose_trade": return Propose(a, k, new TradeAgreementDecision(ruler, other), ref status);
                case "call_to_war":
                {
                    Kingdom enemy = Kingdom.All.FirstOrDefault(x => x.StringId == DiploRules.Realm(Capture(k), a.Against)?.Id);
                    return Propose(a, k, new ProposeCallToWarAgreementDecision(ruler, other, enemy), ref status);
                }
                case "propose_policy":
                {
                    PolicyObject p = PolicyObject.All.FirstOrDefault(x => x.StringId == target);
                    if (p == null) return "no such policy";
                    if (!Campaign.Current.Models.KingdomDecisionPermissionModel.IsPolicyDecisionAllowed(p)) return "the game does not allow a vote on " + p.Name + " now";
                    return Propose(a, k, new KingdomPolicyDecision(ruler, p, a.Abolish), ref status);
                }
                case "propose_expel":
                {
                    Clan c = Clan.All.FirstOrDefault(x => x.StringId == target);
                    if (c == null || c.Kingdom != k) return "that clan is not in your realm";
                    return Propose(a, k, new ExpelClanFromKingdomDecision(ruler, c), ref status);
                }
                case "hire_mercenary":
                {
                    Clan c = Clan.All.FirstOrDefault(x => x.StringId == target);
                    if (c == null || c.IsEliminated || c.Kingdom != null) return "that clan is not for hire";
                    if (c.IsAtWarWith(k)) return Names.Faction(c) + " is at war with you";
                    int value = new MercenaryJoinKingdomBarterable(c.Leader, null, k).GetValueForFaction(c);
                    if (value <= 0) return Names.Faction(c) + " will not serve you on the game's terms now (it values the offer at " + value + ")";
                    int award = Campaign.Current.Models.MinorFactionsModel.GetMercenaryAwardFactorToJoinKingdom(c, k);
                    ChangeKingdomAction.ApplyByJoinFactionAsMercenary(c, k, default(CampaignTime), award);
                    status = "applied";
                    Book("mercenary_hired", null, null, k, new { clan = c.StringId, award });
                    return Names.Faction(c) + " serves " + Names.Faction(k) + " for pay from now on (" + award + " a day for each point of influence it wins)";
                }
                case "release_mercenary":
                {
                    Clan c = Clan.All.FirstOrDefault(x => x.StringId == target);
                    if (c == null || c.Kingdom != k || !c.IsUnderMercenaryService) return "that clan does not serve you for pay";
                    if (c.ShouldStayInKingdomUntil.IsFuture) return Names.Faction(c) + " is bound to you until " + Text.When(c.ShouldStayInKingdomUntil.ToHours);
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(c);
                    status = "applied";
                    Book("mercenary_released", null, null, k, new { clan = c.StringId });
                    return Names.Faction(c) + " has left your service";
                }
                case "vote":
                case "decide":
                {
                    Held h = Holds.FirstOrDefault(x => !x.Offer && x.Id == target && x.Realm == k);
                    if (h == null) return "that vote is no longer open";
                    DiploVote dv = Capture(k).Votes.FirstOrDefault(x => x.Id == h.Id);
                    int i = DiploRules.OptionIndex(dv, a.Option);
                    string option = i >= 0 ? dv.Options[i] : null;
                    if (option == null) return "that option is not in the vote";
                    if (a.Act == "vote")
                    {
                        Votes[h.Decision] = new RulerVote { Option = option, Weight = a.Weight == "slight" || a.Weight == "slightly" ? Supporter.SupportWeights.SlightlyFavor : a.Weight == "strong" || a.Weight == "strongly" ? Supporter.SupportWeights.StronglyFavor : Supporter.SupportWeights.FullyPush };
                        status = "applied";
                        Book("voted", h, null, k, new { option, weight = Votes[h.Decision].Weight.ToString() });
                        return "your clan's vote on [" + h.Id + "] is " + option + " (" + (a.Weight ?? "full") + "); the vote closes " + Text.When(h.Closes) + ", or when you decide it";
                    }
                    Decisions[h.Decision] = option;
                    Book("decided", h, null, k, new { option });
                    status = "applied";
                    return "you decided [" + h.Id + "]: " + Settle(h);
                }
                default:
                {
                    Held h = Holds.FirstOrDefault(x => x.Offer && x.Id == target && x.Realm == k);
                    if (h == null) return "that offer is no longer open";
                    bool accept = a.Act.StartsWith("accept_", StringComparison.Ordinal);
                    string said = Answer(h, accept, null);
                    status = "applied";
                    return "you " + (accept ? "accepted" : "declined") + " " + Names.Faction(h.Decision.Kingdom) + "'s offer [" + h.Id + "]: " + said;
                }
            }
        }

        /// <summary>A proposal: the game's own vote in the realm, at the game's influence price, settled at once with the ruler's clan supporting it fully (overrule: decided yes even against the clans, at the game's price).</summary>
        private static string Propose(DiploAct a, Kingdom k, KingdomDecision d, ref string status)
        {
            if (!d.IsAllowed()) return "the game does not allow it now (" + Plain(d) + ")";
            if (!d.CanMakeDecision(out TextObject why, true)) return "the game does not allow it now: " + (why?.ToString() ?? "no reason given");
            int cost = d.GetInfluenceCost(k.RulingClan);
            if (k.RulingClan.Influence < cost) return "it costs " + cost + " influence and your clan has " + (int)k.RulingClan.Influence;
            Proposed.Add(d);
            if (a.Overrule)
            {
                var probe = new KingdomElection(d);
                Decisions[d] = Title(Yes(d, probe.PossibleOutcomes));
            }
            Book("proposed", null, null, k, new { act = a.Act, decision = d.GetType().Name, title = d.GetGeneralTitle()?.ToString(), cost, overrule = a.Overrule, a.Why });
            k.AddDecision(d);
            status = "applied";
            // your realm's own vote waits for the game's resolution: an agent ruling it settles its own proposal at once, as for the others;
            // proposed for you (the lieutenant, on your word), it waits for your decision in the kingdom's screen
            if (k == Clan.PlayerClan?.Kingdom && k.UnresolvedDecisions.Contains(d))
            {
                if (!Rules(k))
                {
                    Proposed.Remove(d);
                    Decisions.Remove(d);
                    return "proposed (" + cost + " influence): " + d.GetGeneralTitle() + "; the vote waits for your ruler's decision in the kingdom's screen";
                }
                new KingdomElection(d).StartElectionWithoutPlayer();
            }
            string came = Concluded(d);
            Proposed.Remove(d);
            Decisions.Remove(d);
            return "proposed (" + cost + " influence): " + d.GetGeneralTitle() + "; the clans voted at once and " + came;
        }

        private static string Plain(KingdomDecision d) => d is MakePeaceKingdomDecision ? "peace between these realms is not allowed by the game's rules now"
                                                          : d is DeclareWarDecision ? "war between these realms is not allowed by the game's rules now" : "its conditions are not met";

        // ---------- the realm's screens, copied (game's thread) ----------

        /// <summary>A realm's diplomacy as its ruler's screens show it: stances and their tallies, costs, clans, mercenaries, policies, its open votes and the offers made to it.</summary>
        public static DiploView Capture(Kingdom k)
        {
            var v = new DiploView { Realm = k.StringId, RealmName = Names.Faction(k), RulerClan = k.RulingClan?.StringId, RulerClanName = Names.Faction(k.RulingClan), Influence = k.RulingClan?.Influence ?? 0 };
            Clan ruler = k.RulingClan;
            if (ruler == null) return v;
            var m = Campaign.Current.Models;
            v.Costs["propose_war"] = m.DiplomacyModel.GetInfluenceCostOfProposingWar(ruler);
            v.Costs["propose_peace"] = m.DiplomacyModel.GetInfluenceCostOfProposingPeace(ruler);
            v.Costs["propose_alliance"] = m.AllianceModel.GetInfluenceCostOfProposingStartingAlliance(ruler);
            v.Costs["call_to_war"] = m.AllianceModel.GetInfluenceCostOfCallingToWar(ruler);
            v.Costs["propose_trade"] = m.TradeAgreementModel.GetInfluenceCostOfProposingTradeAgreement(ruler);
            v.Costs["propose_policy"] = m.DiplomacyModel.GetInfluenceCostOfPolicyProposalAndDisavowal(ruler);
            v.Costs["propose_expel"] = m.DiplomacyModel.GetInfluenceCostOfExpellingClan(ruler);
            float perk = ruler.Leader != null && ruler.Leader.GetPerkValue(DefaultPerks.Charm.FlexibleEthics) ? 1f + DefaultPerks.Charm.FlexibleEthics.PrimaryBonus : 1f;
            v.Slight = (int)(20 * perk);
            v.Strong = (int)(60 * perk);
            v.Full = (int)(150 * perk);
            v.RingWar = RingRules.Active;
            var alliances = Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            var trade = Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            foreach (Kingdom o in Kingdom.All)
            {
                if (o == null || o == k || o.IsEliminated) continue;
                StanceLink link = k.GetStanceWith(o);
                var s = new DiploStance { Id = o.StringId, Name = Names.Faction(o), Lords = o.AliveLords.Count, Strength = o.CurrentTotalStrength };
                if (k.IsAtWarWith(o))
                {
                    s.Stance = "war";
                    if (link != null && link.WarStartDate.ToHours > 0) s.Since = link.WarStartDate.ToHours;
                    if (alliances != null && alliances.IsAtWarByCallToWarAgreement(k, o, out Kingdom caller) && caller != null) s.CalledBy = Names.Faction(caller);
                    foreach (Kingdom ally in k.AlliedKingdoms)
                        if (alliances != null && alliances.IsAtWarByCallToWarAgreement(ally, o, out Kingdom c2) && c2 == k) s.Called = Names.Faction(ally);
                    s.NoPeace = RingRules.NoPeace(k, o);
                }
                else
                {
                    if (k.IsAllyWith(o)) { s.Stance = "alliance"; s.Permanent = RingRules.Permanent(k, o); }
                    if (link != null && link.PeaceDeclarationDate.ToHours > 0) s.Since = link.PeaceDeclarationDate.ToHours;
                    s.NoWar = RingRules.NoWar(k, o);
                }
                if (link != null)
                {
                    s.OurLosses = link.GetCasualties(k);
                    s.TheirLosses = link.GetCasualties(o);
                    s.OurSieges = link.GetSuccessfulSieges(k);
                    s.TheirSieges = link.GetSuccessfulSieges(o);
                    s.OurRaids = link.GetSuccessfulRaids(k);
                    s.TheirRaids = link.GetSuccessfulRaids(o);
                    s.Tribute = link.GetDailyTributeToPay(k);
                    s.TributeDays = link.GetRemainingTributePaymentCount();
                }
                if (trade != null && trade.HasTradeAgreement(k, o, out _))
                {
                    s.Trade = true;
                    s.TradeUntil = trade.GetTradeAgreementEndDate(k, o).ToHours;
                }
                v.Stances.Add(s);
            }
            foreach (Clan c in k.Clans)
            {
                if (c == null || c.IsEliminated) continue;
                v.Clans.Add(new DiploClan
                {
                    Id = c.StringId, Name = Names.Faction(c), Mercenary = c.IsUnderMercenaryService, Ruling = c == ruler, Fiefs = c.Fiefs.Count, Lords = c.Heroes.Count(h => h.IsLord && h.IsAlive),
                    Strength = c.CurrentTotalStrength, ContractUntil = c.IsUnderMercenaryService ? c.ShouldStayInKingdomUntil.ToHours : double.NaN, Award = c.IsUnderMercenaryService ? c.MercenaryAwardMultiplier : 0
                });
            }
            foreach (Clan c in Clan.All)
            {
                if (c == null || c.IsEliminated || c.Kingdom != null || !c.IsMinorFaction || c.IsBanditFaction || c == Clan.PlayerClan || c.Leader == null || c.WarPartyComponents.Count == 0 || c.IsAtWarWith(k)) continue;
                v.ForHire.Add(new DiploClan { Id = c.StringId, Name = Names.Faction(c), Strength = c.CurrentTotalStrength, Lords = c.Heroes.Count(h => h.IsAlive) });
            }
            foreach (PolicyObject p in PolicyObject.All)
                if (p != null && m.KingdomDecisionPermissionModel.IsPolicyDecisionAllowed(p)) v.Policies.Add(new DiploPolicy { Id = p.StringId, Name = p.Name?.ToString() ?? p.StringId, Active = k.ActivePolicies.Contains(p) });
            foreach (Held h in Holds.Where(x => x.Realm == k))
            {
                try
                {
                    if (h.Offer) v.Offers.Add(OfferRow(h));
                    else v.Votes.Add(VoteRow(h));
                }
                catch (Exception e) { Diag.Swallowed("a held vote's row", e); }
            }
            return v;
        }

        private static DiploOffer OfferRow(Held h)
        {
            KingdomDecision d = h.Decision;
            var o = new DiploOffer { Id = h.Id, Kind = h.Kind, From = d.Kingdom.StringId, FromName = Names.Faction(d.Kingdom), Closes = h.Closes };
            if (d is MakePeaceKingdomDecision p) { o.Tribute = p.DailyTributeToBePaid; o.Days = p.DailyTributeDurationInDays; }
            if (d is ProposeCallToWarAgreementDecision c) { o.Against = c.KingdomToCallToWarAgainst.StringId; o.AgainstName = Names.Faction(c.KingdomToCallToWarAgainst); }
            return o;
        }

        private static DiploVote VoteRow(Held h)
        {
            KingdomDecision d = h.Decision;
            KingdomElection e = h.Election;
            if (e == null)
            {
                // a vote the game holds: its support so far, as the game would count it without the player
                e = new KingdomElection(d);
                e.SetupResultWithoutPlayerSupport();
            }
            else e.DetermineOfficialSupport();
            var outcomes = e.PossibleOutcomes;
            var v = new DiploVote { Id = h.Id, Title = d.GetGeneralTitle()?.ToString() ?? d.GetType().Name, Kind = h.Kind, Proposer = Names.Faction(d.ProposerClan), Opened = h.Opened, Closes = h.Closes, ByGame = h.Election == null };
            DecisionOutcome popular = outcomes.OrderByDescending(o => o.TotalSupportPoints).FirstOrDefault();
            foreach (DecisionOutcome o in outcomes)
            {
                v.Options.Add(Title(o));
                v.Support.Add(o.WinChance);
                v.Override.Add(o == popular || popular == null ? 0 : Campaign.Current.Models.ClanPoliticsModel.GetInfluenceRequiredToOverrideKingdomDecision(popular, o, d));
            }
            if (Votes.TryGetValue(d, out RulerVote rv)) v.YourVote = rv.Option + " (" + rv.Weight + ")";
            if (Decisions.TryGetValue(d, out string dec)) v.YourDecision = dec;
            return v;
        }

        // ---------- the diplomacy book (C3.5) ----------

        private static void Book(string type, Held h, string why, Kingdom realm = null, object detail = null)
        {
            KingdomDecision d = h?.Decision;
            var line = new
            {
                Type = type, Realm = (realm ?? h?.Realm)?.StringId, Run = RunTags.Id, Arm = RunTags.Arm, Hours = CampaignTime.Now.ToHours, Id = h?.Id, Kind = h?.Kind,
                Decision = d?.GetType().Name, Title = d?.GetGeneralTitle()?.ToString(), From = d?.Kingdom?.StringId, Proposer = d?.ProposerClan?.StringId, Closes = h != null ? h.Closes : (double?)null,
                Why = why, Detail = detail
            };
            Background.Post("diplomacy book", () => DiploBook.Write(line));
        }

        /// <summary>A vote concluded in a realm an agent rules, or in the player's (the game's event): its outcome into the book.</summary>
        public static void OnConcluded(KingdomDecision d, DecisionOutcome chosen)
        {
            if (d?.Kingdom == null || !(Rules(d.Kingdom) || Bridge.IsSeat(d.Kingdom))) return;
            Book("concluded", null, null, d.Kingdom, new { decision = d.GetType().Name, title = d.GetGeneralTitle()?.ToString(), chosen = Title(chosen), proposer = d.ProposerClan?.StringId, ours = Proposed.Contains(d) });
        }
    }

    /// <summary>
    /// The War of the Ring's rules, as the pack itself applies them (TAOM 2.0.26: a peace between realms of hostile tiers blocked while the war
    /// rages, wars it does not allow, alliances bound for good), asked of the pack's own hooks by name, so the agents are told what it forbids
    /// before they spend influence on it. Without the pack (or another version of it), nothing is forbidden here. Game's thread.
    /// </summary>
    internal static class RingRules
    {
        private static bool _looked;
        private static object _peace, _alliance;
        private static MethodInfo _noPeace, _noWar, _keep;
        private static PropertyInfo _active;
        private static object _service;

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                Type peace = HarmonyLib.AccessTools.TypeByName("TAOM.Features.Diplomacy.Hooks.MakePeaceAction_ApplyInternal_Patch");
                Type war = HarmonyLib.AccessTools.TypeByName("TAOM.Features.Diplomacy.Hooks.DeclareWarAction_ApplyInternal_Patch");
                _peace = peace != null ? HarmonyLib.AccessTools.Field(peace, "_hook")?.GetValue(null) : null;
                _alliance = war != null ? HarmonyLib.AccessTools.Field(war, "_hook")?.GetValue(null) : null;
                _noPeace = _peace != null ? HarmonyLib.AccessTools.Method(_peace.GetType(), "ShouldPreventPeace") : null;
                _noWar = _alliance != null ? HarmonyLib.AccessTools.Method(_alliance.GetType(), "ShouldPreventWarDeclaration") : null;
                _keep = _alliance != null ? HarmonyLib.AccessTools.Method(_alliance.GetType(), "ShouldPreventAllianceEnd") : null;
                _service = _peace != null ? HarmonyLib.AccessTools.Field(_peace.GetType(), "_wotrService")?.GetValue(_peace) : null;
                _active = _service != null ? HarmonyLib.AccessTools.Property(_service.GetType(), "IsWarOfTheRingActive") : null;
                Diag.Event("diplomacy", "war of the ring rules", new { peace = _noPeace != null, war = _noWar != null, alliances = _keep != null, phase = _active != null });
            }
            catch (Exception e) { Diag.Swallowed("finding the War of the Ring's rules", e); }
        }

        private static bool Ask(MethodInfo m, object hook, IFaction a, IFaction b)
        {
            Look();
            if (m == null || hook == null || a == null || b == null) return false;
            try { return (bool)m.Invoke(hook, new object[] { a.StringId, b.StringId }); }
            catch (Exception e) { Diag.Swallowed("asking the War of the Ring's rules", e); return false; }
        }

        public static bool NoPeace(IFaction a, IFaction b) => Ask(_noPeace, _peace, a, b);
        public static bool NoWar(IFaction a, IFaction b) => Ask(_noWar, _alliance, a, b);
        public static bool Permanent(IFaction a, IFaction b) => Ask(_keep, _alliance, a, b);

        public static bool Active
        {
            get
            {
                Look();
                try { return _active != null && (bool)_active.GetValue(_service); }
                catch (Exception e) { Diag.Swallowed("the War of the Ring's phase", e); return false; }
            }
        }
    }
}
