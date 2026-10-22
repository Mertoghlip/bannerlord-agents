using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// The notices a ruler gets, for the watched realm and the player's realm only: wars and peace, alliances and
    /// calls to war, sieges and raids on their lands and sieges lifted, holdings changing hands, armies broken up, lords
    /// lost or killed, battles fought and the realm's votes. Each also goes into the realm's chronicle (Chronicle). A
    /// lord set free and an army of the realm's own formed go only into the chronicle (Notice.RecordOnly). Each other
    /// notice becomes a poke trigger at the next in-game hour, except the ones kept
    /// quiet (Notice.Quiet), which only go to the logs: fights with bandits, caravans or villagers away from any
    /// holding, a raid announced again, and a lost party whose lord's capture already says it (the first session's
    /// pokes, docs/reports/2026-10-08-pokes.md). Game's thread: the handlers only write a sentence and hand it to the
    /// worker.
    /// </summary>
    internal static class Notices
    {

        /// <summary>Who led each side when a battle began: by its end the loser may already be a prisoner, the party nameless.</summary>
        private sealed class Opened
        {
            public string Attacker, Defender;
            public bool Lords;
        }

        private static readonly Dictionary<MapEvent, Opened> Battles = new Dictionary<MapEvent, Opened>();
        /// <summary>Raids announced, by village and raider, with the in-game hour.</summary>
        private static readonly Dictionary<string, double> Raids = new Dictionary<string, double>();
        /// <summary>Who laid each siege, by the besieged holding: by the siege's end the besiegers may be gone.</summary>
        private static readonly Dictionary<string, KeyValuePair<string, IFaction>> Sieges = new Dictionary<string, KeyValuePair<string, IFaction>>();

        public static void Reset()
        {
            Battles.Clear();
            Raids.Clear();
            Sieges.Clear();
        }

        private static List<string> Involved(params IFaction[] factions)
        {
            var list = new List<string>(2);
            string player = Clan.PlayerClan?.MapFaction?.StringId;
            foreach (IFaction f in factions)
            {
                if (f == null) continue;
                // every watched realm (since 0.8 there may be more than one)
                if (Bridge.IsSeat(f.StringId) && !list.Contains(f.StringId)) list.Add(f.StringId);
                if (f.StringId == player && !list.Contains(player)) list.Add(player);
            }
            return list;
        }

        private static void Send(string kind, string text, List<string> factions, string party = null, string quiet = null, string place = null, bool recordOnly = false)
        {
            if (!Bridge.Running || (factions.Count == 0 && kind != "party_destroyed")) return;
            var n = new Notice { Id = NoticeIds.Next(), Hours = CampaignTime.Now.ToHours, Kind = kind, Text = text, Factions = factions, Party = party, Quiet = quiet, Place = place, RecordOnly = recordOnly };
            Background.Post(() => Brain.OnNotice(n));
        }

        private static string F(IFaction f) => Names.Faction(f);

        private static string P(PartyBase p)
        {
            if (p == null) return "unknown";
            if (p.MobileParty != null) return Names.Party(p.MobileParty);
            if (p.Settlement != null) return Names.Settlement(p.Settlement);
            return p.Name?.ToString() ?? "unknown";
        }

        private static int Men(MobileParty p)
        {
            if (p == null) return 0;
            return p.Army != null && p.Army.LeaderParty == p ? p.Army.TotalHealthyMembers : p.Party.NumberOfHealthyMembers;
        }

        public static void OnWarDeclared(IFaction a, IFaction b, DeclareWarAction.DeclareWarDetail detail)
            => Send("war", F(a) + " declared war on " + F(b) + ".", Involved(a, b));

        public static void OnPeace(IFaction a, IFaction b, MakePeaceAction.MakePeaceDetail detail)
            => Send("peace", F(a) + " and " + F(b) + " made peace.", Involved(a, b));

        public static void OnAllianceStarted(Kingdom a, Kingdom b)
            => Send("alliance", F(a) + " and " + F(b) + " formed an alliance.", Involved(a, b));

        public static void OnAllianceEnded(Kingdom a, Kingdom b)
            => Send("alliance_ended", "The alliance between " + F(a) + " and " + F(b) + " ended.", Involved(a, b));

        public static void OnCallToWarStarted(Kingdom calling, Kingdom called, Kingdom against)
            => Send("call_to_war", F(calling) + " called " + F(called) + " to war against " + F(against) + ".", Involved(calling, called, against));

        public static void OnCallToWarEnded(Kingdom calling, Kingdom called, Kingdom against)
            => Send("call_to_war_ended", F(called) + "'s war against " + F(against) + " at " + F(calling) + "'s call ended.", Involved(calling, called, against));

        public static void OnSiegeStarted(SiegeEvent se)
        {
            Settlement s = se?.BesiegedSettlement;
            if (s == null) return;
            MobileParty lead = se.BesiegerCamp?.LeaderParty;
            List<string> defenders = Involved(s.MapFaction);
            Send("besieged", Names.Settlement(s) + " is besieged by " + Names.Party(lead) + " (" + F(lead?.MapFaction) + "), " + Text.N(Men(lead)) + " men.",
                 defenders, lead?.StringId, place: s.StringId);
            List<string> attackers = Involved(lead?.MapFaction);
            attackers.RemoveAll(defenders.Contains);
            Send("siege_started", Names.Party(lead) + " laid siege to " + Names.Settlement(s) + " (" + F(s.MapFaction) + ").", attackers, lead?.StringId, place: s.StringId);
            Sieges[s.StringId] = new KeyValuePair<string, IFaction>(Names.Party(lead), lead?.MapFaction);
        }

        /// <summary>A siege over. Taken, the holding's change of hands says it; otherwise it was lifted, which both sides hear.</summary>
        public static void OnSiegeEnded(SiegeEvent se)
        {
            Settlement s = se?.BesiegedSettlement;
            if (s == null) return;
            Sieges.TryGetValue(s.StringId, out KeyValuePair<string, IFaction> laid);
            Sieges.Remove(s.StringId);
            IFaction besiegers = laid.Value ?? se.BesiegerCamp?.LeaderParty?.MapFaction;
            if (besiegers != null && s.MapFaction == besiegers) return;
            Send("siege_ended", "The siege of " + Names.Settlement(s) + " (" + F(s.MapFaction) + ") was lifted"
                 + (laid.Key != null ? "; " + laid.Key + (besiegers != null ? " (" + F(besiegers) + ")" : "") + " had laid it" : "") + ".",
                 Involved(s.MapFaction, besiegers), place: s.StringId);
        }

        /// <summary>A lord dead: in battle, executed, of age or otherwise.</summary>
        public static void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool notify)
        {
            if (victim == null || !victim.IsLord) return;
            string how;
            switch (detail)
            {
                case KillCharacterAction.KillCharacterActionDetail.DiedInBattle:
                case KillCharacterAction.KillCharacterActionDetail.WoundedInBattle: how = "died in battle"; break;
                case KillCharacterAction.KillCharacterActionDetail.Executed:
                case KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent: how = "was executed"; break;
                case KillCharacterAction.KillCharacterActionDetail.Murdered: how = "was murdered"; break;
                case KillCharacterAction.KillCharacterActionDetail.DiedOfOldAge: how = "died of old age"; break;
                case KillCharacterAction.KillCharacterActionDetail.Lost: how = "was lost"; break;
                default: how = "died"; break;
            }
            Send("lord_killed", Names.Hero(victim) + " (" + F(victim.MapFaction) + ") " + how + (killer != null && killer != victim ? ", by " + Names.Hero(killer) + " (" + F(killer.MapFaction) + ")" : "") + ".",
                 Involved(victim.MapFaction, killer?.MapFaction));
        }

        /// <summary>A lord set free: in the chronicle, without a poke (the lord's return shows in the next report).</summary>
        public static void OnPrisonerReleased(Hero h, PartyBase party, IFaction capturers, EndCaptivityDetail detail, bool notify)
        {
            if (h == null || !h.IsLord || detail == EndCaptivityDetail.Death) return;
            string how;
            switch (detail)
            {
                case EndCaptivityDetail.Ransom: how = "was ransomed"; break;
                case EndCaptivityDetail.ReleasedAfterPeace: how = "was released after the peace"; break;
                case EndCaptivityDetail.ReleasedAfterBattle: how = "was freed in a battle"; break;
                case EndCaptivityDetail.ReleasedAfterEscape: how = "escaped"; break;
                default: how = "was released"; break;
            }
            Send("released", Names.Hero(h) + " (" + F(h.MapFaction) + ") " + how + (capturers != null ? " from " + F(capturers) : "") + ".",
                 Involved(h.MapFaction, capturers), recordOnly: true);
        }

        /// <summary>An army formed by one of the watched realm's lords (the agent's or the game's own): in its chronicle, without a poke.</summary>
        public static void OnArmyCreated(Army army)
        {
            if (army?.LeaderParty == null || !Bridge.IsSeat(army.Kingdom)) return;
            // an army an order forms meets where the order says; the game fires this before that place is set
            Settlement at = Orders.FormingAt ?? army.AiBehaviorObject as Settlement;
            string where = at != null ? " at " + Names.Settlement(at) : "";
            Send("army_formed", Names.Party(army.LeaderParty) + " formed an army" + where + ".", Involved(army.Kingdom), army.LeaderParty.StringId, recordOnly: true);
        }

        /// <summary>The game says "being raided" when the raiders arrive and again once the village's defenders are beaten: only the first is news.</summary>
        public static void OnVillageRaided(Village v)
        {
            Settlement s = v?.Settlement;
            if (s == null) return;
            MobileParty raider = s.Party?.MapEvent?.AttackerSide?.LeaderParty?.MobileParty;
            double now = CampaignTime.Now.ToHours;
            string key = s.StringId + "|" + raider?.StringId;
            string quiet = Raids.TryGetValue(key, out double last) && now - last < 24 ? "raid already announced" : null;
            if (quiet == null)
            {
                Raids[key] = now;
                if (Raids.Count > 200)
                    foreach (string old in Raids.Where(x => now - x.Value >= 24).Select(x => x.Key).ToList()) Raids.Remove(old);
            }
            Send("raided", Names.Settlement(s) + " is being raided by " + Names.Party(raider) + " (" + F(raider?.MapFaction) + ").",
                 Involved(s.MapFaction), raider?.StringId, quiet, s.StringId);
        }

        public static void OnVillageLooted(Village v)
        {
            Settlement s = v?.Settlement;
            if (s == null) return;
            Send("looted", Names.Settlement(s) + " was looted.", Involved(s.MapFaction), place: s.StringId);
        }

        public static void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
                                          ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            IFaction now = newOwner?.MapFaction, before = oldOwner?.MapFaction;
            if (s == null || now == before) return;
            Send("holding_changed", Names.Settlement(s) + " passed from " + F(before) + " to " + F(now)
                 + (capturer != null ? ", taken by " + Names.Hero(capturer) : "") + ".", Involved(now, before), place: s.StringId);
        }

        public static void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            if (army == null) return;
            Send("army_dispersed", "The army of " + Names.Party(army.LeaderParty) + " broke up (" + reason + ").", Involved(army.Kingdom));
        }

        /// <summary>
        /// A lord's party wiped out. When the lord was taken prisoner, the party goes in the same moment and the capture
        /// already says it, so that one is kept quiet. Without its lord the party goes by its owner's name.
        /// </summary>
        public static void OnPartyDestroyed(MobileParty p, PartyBase destroyer)
        {
            if (p == null || !p.IsLordParty) return;
            Hero lord = p.LeaderHero ?? p.Owner;
            string who = lord != null ? Names.Hero(lord) + "'s party" : Names.Party(p);
            string quiet = p.LeaderHero == null && p.Owner != null && p.Owner.IsPrisoner ? "the capture says it" : null;
            Send("party_destroyed", who + " (" + F(p.MapFaction) + ") was destroyed" + (destroyer != null ? " by " + P(destroyer) : "") + ".",
                 Involved(p.MapFaction, destroyer?.MapFaction), p.StringId, quiet);
        }

        public static void OnBattleStarted(MapEvent me, PartyBase attacker, PartyBase defender)
        {
            if (me == null || Involved(me.AttackerSide?.MapFaction ?? attacker?.MapFaction, me.DefenderSide?.MapFaction ?? defender?.MapFaction).Count == 0) return;
            if (Battles.Count > 500)
            {
                Diag.Count("notice:battle starts forgotten", Battles.Count);
                Battles.Clear();
            }
            Battles[me] = new Opened { Attacker = P(me.AttackerSide?.LeaderParty ?? attacker), Defender = P(me.DefenderSide?.LeaderParty ?? defender), Lords = LordsOnBothSides(me) };
        }

        public static void OnBattleEnded(MapEvent me)
        {
            if (me == null) return;
            Battles.TryGetValue(me, out Opened start);
            Battles.Remove(me);
            if (!me.HasWinner) return;
            IFaction a = me.AttackerSide?.MapFaction, d = me.DefenderSide?.MapFaction;
            List<string> involved = Involved(a, d);
            if (involved.Count == 0) return;
            string attacker = Named(me.AttackerSide?.LeaderParty, start?.Attacker), defender = Named(me.DefenderSide?.LeaderParty, start?.Defender);
            string winner = me.WinningSide == BattleSideEnum.Attacker ? attacker : defender;
            string what = me.IsSiegeAssault ? "Assault" : me.IsRaid ? "Fight over a raid" : "Battle";
            string place = me.MapEventSettlement != null ? " at " + Names.Settlement(me.MapEventSettlement) : "";
            Send("battle", what + place + ": " + attacker + " (" + F(a) + ") against " + defender + " (" + F(d) + "); " + winner + " won.", involved, null, Small(me, start),
                 me.MapEventSettlement?.StringId);
        }

        /// <summary>A side's leader by name; for a lord taken in the battle, the name from when it began.</summary>
        private static string Named(PartyBase p, string atStart)
        {
            MobileParty m = p?.MobileParty;
            if (m == null || m.LeaderHero != null) return P(p);
            if (!string.IsNullOrEmpty(atStart)) return atStart;
            return m.IsLordParty && m.Owner != null ? Names.Hero(m.Owner) : P(p);
        }

        /// <summary>
        /// Why a battle is too small to poke anyone (it still goes to the logs), or null when it counts. A battle counts
        /// when it is fought at a town, castle or village (an assault, a sally, a raid) or has lords on both sides. The
        /// rest are bandits chased and caravans or villagers robbed: 100 of the 122 battles in the first session.
        /// </summary>
        private static string Small(MapEvent me, Opened start)
        {
            Settlement at = me.MapEventSettlement;
            if (me.IsSiegeAssault || me.IsSallyOut || me.IsSiegeOutside || me.IsRaid || (at != null && !at.IsHideout)) return null;
            if ((start != null && start.Lords) || LordsOnBothSides(me)) return null;
            if (AnyParty(me, p => p.IsCaravan)) return "a caravan";
            if (AnyParty(me, p => p.IsVillager)) return "villagers";
            if (AnyParty(me, p => p.IsBandit)) return "bandits";
            return "no lord on one side";
        }

        private static bool LordsOnBothSides(MapEvent me) => HasLord(me.AttackerSide) && HasLord(me.DefenderSide);

        private static bool HasLord(MapEventSide side)
        {
            if (side?.Parties == null) return false;
            foreach (MapEventParty mp in side.Parties)
                if (mp?.Party?.MobileParty?.IsLordParty == true) return true;
            return false;
        }

        private static bool AnyParty(MapEvent me, Func<MobileParty, bool> test)
        {
            foreach (MapEventSide side in new[] { me.AttackerSide, me.DefenderSide })
            {
                if (side?.Parties == null) continue;
                foreach (MapEventParty mp in side.Parties)
                {
                    MobileParty p = mp?.Party?.MobileParty;
                    if (p != null && test(p)) return true;
                }
            }
            return false;
        }

        public static void OnPrisonerTaken(PartyBase capturer, Hero h)
        {
            if (h == null || !h.IsLord) return;
            Send("captured", Names.Hero(h) + " (" + F(h.MapFaction) + ") was taken prisoner by " + P(capturer) + ".", Involved(h.MapFaction, capturer?.MapFaction));
        }

        public static void OnDecisionAdded(KingdomDecision d, bool isPlayerInvolved)
        {
            if (d == null) return;
            string title = d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
            Send("proposal", F(d.ProposerClan) + " proposed: " + title + ".", Involved(d.Kingdom));
        }

        public static void OnDecisionConcluded(KingdomDecision d, DecisionOutcome outcome, bool isPlayerInvolved)
        {
            if (d == null) return;
            string title = outcome?.GetDecisionTitle()?.ToString() ?? d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
            Send("decided", F(d.Kingdom) + " decided: " + title + ".", Involved(d.Kingdom));
        }

        /// <summary>One of the watched realm's orders finished or failed: a poke trigger, as the plan has it.</summary>
        public static void Order(IFaction realm, string kind, string text, string party, bool recordOnly = false)
            => Send(kind, text, Involved(realm ?? Bridge.Watched), party, recordOnly: recordOnly);

        public static void OnPeaceOfferedToPlayer(IFaction from, int amount, int days)
        {
            List<string> player = Involved(Clan.PlayerClan?.MapFaction);
            Send("peace_offer", F(from) + " offered peace" + (amount != 0 ? " (" + Text.N(amount) + " denars)" : "") + ".", player);
        }
    }
}
