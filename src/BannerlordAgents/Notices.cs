using System.Collections.Generic;
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
    /// calls to war, sieges and raids on their lands, holdings changing hands, armies broken up, lords lost, battles
    /// fought and the realm's votes. Each becomes a poke trigger at the next in-game hour. Game's thread: the
    /// handlers only write a sentence and hand it to the worker.
    /// </summary>
    internal static class Notices
    {
        private static int _nextId;

        private static List<string> Involved(params IFaction[] factions)
        {
            var list = new List<string>(2);
            string watched = Bridge.Watched?.StringId;
            string player = Clan.PlayerClan?.MapFaction?.StringId;
            foreach (IFaction f in factions)
            {
                if (f == null) continue;
                if (f.StringId == watched && !list.Contains(watched)) list.Add(watched);
                if (f.StringId == player && !list.Contains(player)) list.Add(player);
            }
            return list;
        }

        private static void Send(string kind, string text, List<string> factions, string party = null)
        {
            if (!Bridge.Running || (factions.Count == 0 && kind != "party_destroyed")) return;
            var n = new Notice { Id = Interlocked.Increment(ref _nextId), Hours = CampaignTime.Now.ToHours, Kind = kind, Text = text, Factions = factions, Party = party };
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
                 defenders, lead?.StringId);
            List<string> attackers = Involved(lead?.MapFaction);
            attackers.RemoveAll(defenders.Contains);
            Send("siege_started", Names.Party(lead) + " laid siege to " + Names.Settlement(s) + " (" + F(s.MapFaction) + ").", attackers, lead?.StringId);
        }

        public static void OnVillageRaided(Village v)
        {
            Settlement s = v?.Settlement;
            if (s == null) return;
            MobileParty raider = s.Party?.MapEvent?.AttackerSide?.LeaderParty?.MobileParty;
            Send("raided", Names.Settlement(s) + " is being raided by " + Names.Party(raider) + " (" + F(raider?.MapFaction) + ").",
                 Involved(s.MapFaction), raider?.StringId);
        }

        public static void OnVillageLooted(Village v)
        {
            Settlement s = v?.Settlement;
            if (s == null) return;
            Send("looted", Names.Settlement(s) + " was looted.", Involved(s.MapFaction));
        }

        public static void OnOwnerChanged(Settlement s, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer,
                                          ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            IFaction now = newOwner?.MapFaction, before = oldOwner?.MapFaction;
            if (s == null || now == before) return;
            Send("holding_changed", Names.Settlement(s) + " passed from " + F(before) + " to " + F(now)
                 + (capturer != null ? ", taken by " + Names.Hero(capturer) : "") + ".", Involved(now, before));
        }

        public static void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy)
        {
            if (army == null) return;
            Send("army_dispersed", "The army of " + Names.Party(army.LeaderParty) + " broke up (" + reason + ").", Involved(army.Kingdom));
        }

        public static void OnPartyDestroyed(MobileParty p, PartyBase destroyer)
        {
            if (p == null || !p.IsLordParty) return;
            Send("party_destroyed", Names.Party(p) + "'s party (" + F(p.MapFaction) + ") was destroyed" + (destroyer != null ? " by " + P(destroyer) : "") + ".",
                 Involved(p.MapFaction, destroyer?.MapFaction), p.StringId);
        }

        public static void OnBattleEnded(MapEvent me)
        {
            if (me == null || !me.HasWinner) return;
            IFaction a = me.AttackerSide?.MapFaction, d = me.DefenderSide?.MapFaction;
            List<string> involved = Involved(a, d);
            if (involved.Count == 0) return;
            string attacker = P(me.AttackerSide?.LeaderParty), defender = P(me.DefenderSide?.LeaderParty);
            string winner = me.WinningSide == BattleSideEnum.Attacker ? attacker : defender;
            string what = me.IsSiegeAssault ? "Assault" : me.IsRaid ? "Fight over a raid" : "Battle";
            string place = me.MapEventSettlement != null ? " at " + Names.Settlement(me.MapEventSettlement) : "";
            Send("battle", what + place + ": " + attacker + " (" + F(a) + ") against " + defender + " (" + F(d) + "); " + winner + " won.", involved);
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
        public static void Order(string kind, string text, string party)
            => Send(kind, text, Involved(Bridge.Watched), party);

        public static void OnPeaceOfferedToPlayer(IFaction from, int amount, int days)
        {
            List<string> player = Involved(Clan.PlayerClan?.MapFaction);
            Send("peace_offer", F(from) + " offered peace" + (amount != 0 ? " (" + Text.N(amount) + " denars)" : "") + ".", player);
        }
    }
}
