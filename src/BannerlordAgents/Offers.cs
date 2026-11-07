using System.Collections.Generic;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// What a recruiting stop has for a lord, and whether he can pay (0.16). Nov 7, test A1: Celebrían rode a recruiting loop into Mount
    /// Gundabad and took no one, because Elrohir, on the same loop 14 hours ahead of her, had taken every volunteer her standing reached;
    /// the loop could not tell, so it stopped there blind. And Dol Guldur's agent gave 64 recruit orders of which 47 ended at once or soon at
    /// the wage cap or the gold floor, 29 lords again and again. So:
    /// - each hour, for every own lord whose tool order names holdings (its at and places, Orders.HandOrder.Stops), the volunteers waiting
    ///   at each that he could take now: at each notable, those within the reach the game gives him (VolunteerModel.MaximumIndexHeroCanRecruitFromHero:
    ///   his standing with the notable, his realm, his skills), none where he may not recruit (an enemy's, besieged, raided); the language's offers;
    /// - what keeps him from paying for any recruit (Bar): his wage cap (the game's own budget, PaymentLimit less his wages less 5), his gold
    ///   under the floor the game keeps an AI lord above (StartRecruitingMoneyLimit), his clan's purse under its floor for a lord who does not
    ///   lead it; the player's clan has no gold floors, as the game's own recruiting has none for it. The language's can_recruit, and the
    ///   reason a recruiting order is refused up front (Orders.RecruitRefusal).
    /// The reach is the game's own sum for that lord and notable, kept for a day (it moves only with standing, realm and skills), so the hour's
    /// count is a look along each notable's six slots. Game's thread only.
    /// </summary>
    internal static class Offers
    {
        private struct Reach
        {
            public int Slots;
            public double At;
        }

        private static readonly Dictionary<Hero, Dictionary<Hero, Reach>> Reaches = new Dictionary<Hero, Dictionary<Hero, Reach>>();
        private const double ReachHours = 24;

        public static void Reset() => Reaches.Clear();

        /// <summary>The hourly copy of an own lord (Purse.Fill): his bar, and the volunteers at his order's holdings.</summary>
        public static void Fill(PartyRow r, MobileParty p)
        {
            r.RecruitBar = Bar(p);
            Orders.HandOrder o = Orders.HeldBy(p);
            Settlement[] stops = o?.Stops;
            Hero leader = p.LeaderHero;
            if (stops == null || leader == null) return;
            double now = CampaignTime.Now.ToHours;
            r.Offers = new Dictionary<string, int>(stops.Length);
            foreach (Settlement s in stops)
                if (s != null) r.Offers[s.StringId] = Count(p, leader, s, now);
        }

        /// <summary>What keeps him from paying for any recruit now: null when nothing, else "wage cap", "no gold" or "clan purse" (the game's own gates).</summary>
        public static string Bar(MobileParty p)
        {
            Hero leader = p?.LeaderHero;
            if (leader == null) return null;
            if (p.HasLimitedWage() && p.GetAvailableWageBudget() < 2) return "wage cap";
            if (leader.Clan == Clan.PlayerClan) return null;
            if (leader.Gold <= HeroHelper.StartRecruitingMoneyLimit(leader)) return "no gold";
            if (leader.Clan != null && leader != leader.Clan.Leader && leader.Clan.Gold < HeroHelper.StartRecruitingMoneyLimitForClanLeader(leader)) return "clan purse";
            return null;
        }

        /// <summary>The bar in words, for a refusal: the numbers, and how to lift it.</summary>
        public static string BarWords(MobileParty p, string bar)
        {
            Hero leader = p.LeaderHero;
            switch (bar)
            {
                case "wage cap":
                    return "his wages are at their cap: " + Text.N(p.TotalWage) + " a day of the " + Text.N(p.PaymentLimit) + " he may pay"
                           + (Wages.Has(p) ? " (the cap you set)" : " (the game sets that cap each day from his clan leader's gold)")
                           + ", so he cannot pay a single recruit's wage; raise his cap with a wages order first (it may come earlier in the same answer)";
                case "no gold":
                    return "his gold, " + Text.N(leader.Gold) + ", is at or below the " + Text.N((int)HeroHelper.StartRecruitingMoneyLimit(leader))
                           + " the game keeps a lord from recruiting under, so he cannot buy a single recruit";
                case "clan purse":
                    return "his clan's purse, " + Text.N(leader.Clan.Gold) + ", is below the " + Text.N((int)HeroHelper.StartRecruitingMoneyLimitForClanLeader(leader))
                           + " the game keeps its lords from recruiting under, so he cannot buy a single recruit";
                default: return bar;
            }
        }

        /// <summary>The volunteers waiting at a holding that this lord could take now (his reach at each notable), 0 where he may not recruit.</summary>
        private static int Count(MobileParty p, Hero leader, Settlement s, double now)
        {
            if (s.Notables == null || s.Notables.Count == 0) return 0;
            if (s.MapFaction != null && FactionManager.IsAtWarAgainstFaction(p.MapFaction, s.MapFaction)) return 0;
            if (s.IsVillage ? (s.IsRaided || s.IsUnderRaid) : (!(s.IsTown || s.IsCastle) || s.IsUnderSiege)) return 0;
            int n = 0;
            foreach (Hero notable in s.Notables)
            {
                if (notable == null || !notable.IsAlive || !notable.CanHaveRecruits) continue;
                CharacterObject[] slots = notable.VolunteerTypes;
                if (slots == null) continue;
                int reach = ReachOf(leader, notable, now);
                if (reach > slots.Length) reach = slots.Length;
                for (int i = 0; i < reach; i++) if (slots[i] != null) n++;
            }
            return n;
        }

        private static int ReachOf(Hero leader, Hero notable, double now)
        {
            if (!Reaches.TryGetValue(leader, out Dictionary<Hero, Reach> row)) Reaches[leader] = row = new Dictionary<Hero, Reach>();
            if (row.TryGetValue(notable, out Reach r) && now - r.At < ReachHours) return r.Slots;
            int slots;
            try { slots = Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(leader, notable); }
            catch (System.Exception e) { Diag.Swallowed("the game's recruiting reach for a lord", e); slots = 0; }
            row[notable] = new Reach { Slots = slots, At = now };
            return slots;
        }
    }
}
