using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// The recruit order as a tour (since 0.9.2). Mert, Oct 22: a lord with a recruit order from an agent "also follows the
    /// logic as my own parties". The same rules as the player's own parties on a recruit order and as the computer's lords
    /// (both in TFAOMFixes, his own mod; this is the agents' copy, kept in this module):
    /// - from the order's place, then the nearest town, castle or village of the realm, an ally or a realm at peace with it,
    ///   within reach (four times the usual gap between neighbouring towns), not swept by this order in the last day; one
    ///   after another;
    /// - in each, every volunteer the lord's standing with the notable reaches and he can pay for (the game's own gold and
    ///   wage rules), with no dice, until his ranks are at 80%; above that only men of his clan's troop lines (the troops of
    ///   the clan's party recipe, what grows into them and what they grow into), and once he is full one man from outside
    ///   them goes for each one taken (lowest tier, wounded first);
    /// - the order ends when his ranks are full and all of his clan's lines, or when nothing within reach is left to recruit,
    ///   and the agent hears why. A new order replaces it, as any order.
    /// The single-stop "recruit at" move that defend and patrol use to refill stays one stop, but buys the same way there.
    /// Purchases go through the game's own (RecruitmentCampaignBehavior.GetRecruitVolunteerFromIndividual), so other mods see
    /// them as usual; when TFAOMFixes is loaded, what the lord is shown and gets is his own people's troop (its Offers), and
    /// this asks it by name what that will be. Game's thread only.
    /// </summary>
    internal static class RecruitTour
    {
        public const float FillTo = 0.8f;
        private const double SweepMemoryHours = 24, ResweepHours = 0.5;
        private const float ReachInTownGaps = 4f;

        // per order: the places swept, and when
        private static readonly Dictionary<int, Dictionary<Settlement, double>> Swept = new Dictionary<int, Dictionary<Settlement, double>>();
        // the clans' troop lines, per recipe; troop objects of this session only
        private static readonly Dictionary<PartyTemplateObject, HashSet<CharacterObject>> Lines = new Dictionary<PartyTemplateObject, HashSet<CharacterObject>>();
        private static Dictionary<CharacterObject, List<CharacterObject>> _parents;

        private static MethodInfo _buy, _offersFor;
        private static bool _looked;

        public static void Reset() { Swept.Clear(); Lines.Clear(); _parents = null; }
        public static void Forget(int orderId) => Swept.Remove(orderId);

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            _buy = AccessTools.Method(typeof(RecruitmentCampaignBehavior), "GetRecruitVolunteerFromIndividual", new[] { typeof(MobileParty), typeof(CharacterObject), typeof(Hero), typeof(int) });
            Type offers = AccessTools.TypeByName("TFAOMFixes.Offers");
            _offersFor = offers != null ? AccessTools.Method(offers, "For", new[] { typeof(Clan), typeof(CultureObject), typeof(Hero), typeof(int), typeof(CharacterObject) }) : null;
            if (_buy == null) Diag.Alarm("recruit_buy_missing", "The game's volunteer purchase (GetRecruitVolunteerFromIndividual) was not found: recruit orders cannot buy.", true, null);
        }

        /// <summary>
        /// One hour of a touring lord: what he may take where he stands, then whether the order is done, else the next stop
        /// (moved: o.Place changed, the caller makes the move). Returns the end reason, or null to carry on.
        /// </summary>
        public static string Step(Orders.HandOrder o, double now, out string key, out bool moved)
        {
            key = null;
            moved = false;
            MobileParty p = o.Party;
            int limit = p.Party.PartySizeLimit;
            if (limit <= 0) return null;
            Func<CharacterObject, bool> lines = LinesOf(p);
            Settlement here = p.CurrentSettlement;
            if (here != null && MayRecruitHere(p, here) && !SweptWithin(o.Id, here, ResweepHours, now))
            {
                Sweep(p, here, limit, lines);
                Mark(o.Id, here, now);
            }
            int men = p.Party.NumberOfAllMembers;
            if (men >= limit && !HasOffLines(p, lines))
            {
                key = "ranks full";
                return "the ranks are full (" + men + " men), all of the clan's own troop lines";
            }
            Settlement target = o.Place;
            if (target == null || target == here || !MayRecruitHere(p, target) || SweptWithin(o.Id, target, SweepMemoryHours, now) || !AnyOffer(p, target, limit, lines))
                target = FindNext(o, p, now, limit, lines);
            if (target == null)
            {
                key = "nothing to recruit";
                return "nothing left to recruit within reach, ranks at " + (100 * men / limit) + "%";
            }
            if (target != o.Place) { o.Place = target; moved = true; }
            return null;
        }

        /// <summary>The single-stop move (defend's and patrol's refill): buys the same way, at most every half hour.</summary>
        public static void SweepAt(Orders.HandOrder o, double now)
        {
            MobileParty p = o.Party;
            Settlement here = p.CurrentSettlement;
            if (here == null || p.Party.PartySizeLimit <= 0 || !MayRecruitHere(p, here) || SweptWithin(o.Id, here, ResweepHours, now)) return;
            Sweep(p, here, p.Party.PartySizeLimit, LinesOf(p));
            Mark(o.Id, here, now);
        }

        private static bool SweptWithin(int id, Settlement s, double hours, double now)
            => Swept.TryGetValue(id, out Dictionary<Settlement, double> m) && m.TryGetValue(s, out double last) && now - last < hours;

        private static void Mark(int id, Settlement s, double now)
        {
            if (!Swept.TryGetValue(id, out Dictionary<Settlement, double> m)) { m = new Dictionary<Settlement, double>(); Swept[id] = m; }
            m[s] = now;
        }

        /// <summary>A place the game lets the lord recruit in now: no siege, no raid, not an enemy's.</summary>
        private static bool MayRecruitHere(MobileParty p, Settlement s)
        {
            if (s == null || s.Notables == null || s.Notables.Count == 0) return false;
            if (s.MapFaction != null && FactionManager.IsAtWarAgainstFaction(p.MapFaction, s.MapFaction)) return false;
            if (s.IsVillage) return !s.IsRaided && !s.IsUnderRaid;
            return (s.IsTown || s.IsCastle) && !s.IsUnderSiege;
        }

        private static Settlement FindNext(Orders.HandOrder o, MobileParty p, double now, int limit, Func<CharacterObject, bool> lines)
        {
            Vec2 at = p.GetPosition2D;
            float reach = Campaign.Current.GetAverageDistanceBetweenClosestTwoTownsWithNavigationType(MobileParty.NavigationType.Default) * ReachInTownGaps;
            float reach2 = reach > 0 ? reach * reach : float.MaxValue;
            var places = new List<KeyValuePair<float, Settlement>>();
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s == p.CurrentSettlement || (!s.IsTown && !s.IsCastle && !s.IsVillage)) continue;
                float d2 = at.DistanceSquared(s.GetPosition2D);
                if (d2 > reach2 || SweptWithin(o.Id, s, SweepMemoryHours, now)) continue;
                places.Add(new KeyValuePair<float, Settlement>(d2, s));
            }
            places.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var kv in places)
                if (MayRecruitHere(p, kv.Value) && AnyOffer(p, kv.Value, limit, lines)) return kv.Value;
            return null;
        }

        /// <summary>Is there a volunteer here he would take now, and could pay for?</summary>
        private static bool AnyOffer(MobileParty p, Settlement s, int limit, Func<CharacterObject, bool> lines)
        {
            Hero leader = p.LeaderHero;
            int men = p.Party.NumberOfAllMembers;
            bool fill = men < FillTo * limit;
            if (men >= limit && !HasOffLines(p, lines)) return false;
            foreach (Hero n in s.Notables)
            {
                if (n == null || !n.IsAlive || !n.CanHaveRecruits || n.VolunteerTypes == null) continue;
                int max = Math.Min(n.VolunteerTypes.Length, Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(leader, n));
                for (int i = 0; i < max; i++)
                {
                    CharacterObject raw = n.VolunteerTypes[i];
                    if (raw == null) continue;
                    CharacterObject t = Offered(p, n, i, raw);
                    if (!fill && !lines(t)) continue;
                    if (CanPay(p, leader, t)) return true;
                }
            }
            return false;
        }

        /// <summary>Everything until 80%, then his clan's lines only, one out for one in once he is full.</summary>
        private static void Sweep(MobileParty p, Settlement s, int limit, Func<CharacterObject, bool> lines)
        {
            Look();
            if (_buy == null) return;
            RecruitmentCampaignBehavior game = Campaign.Current.GetCampaignBehavior<RecruitmentCampaignBehavior>();
            if (game == null) return;
            Hero leader = p.LeaderHero;
            int got = 0, let = 0;
            foreach (Hero n in s.Notables.ToList())
            {
                if (n == null || !n.IsAlive || !n.CanHaveRecruits || n.VolunteerTypes == null) continue;
                int max = Math.Min(n.VolunteerTypes.Length, Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(leader, n));
                for (int i = 0; i < max; i++)
                {
                    CharacterObject raw = n.VolunteerTypes[i];
                    if (raw == null) continue;
                    CharacterObject t = Offered(p, n, i, raw);
                    int men = p.Party.NumberOfAllMembers;
                    if (men >= FillTo * limit && !lines(t)) continue;
                    if (!CanPay(p, leader, t)) continue;
                    if (men >= limit)
                    {
                        if (!LetOneGo(p, lines)) { Report(p, s, got, let); return; }
                        let++;
                    }
                    int before = p.MemberRoster.TotalManCount;
                    _buy.Invoke(game, new object[] { p, raw, n, i });
                    if (p.MemberRoster.TotalManCount > before) got++;
                }
            }
            Report(p, s, got, let);
        }

        private static void Report(MobileParty p, Settlement s, int got, int let)
        {
            if (got == 0) return;
            Diag.Count("recruit order:men bought", got);
            if (let > 0) Diag.Count("recruit order:off-line men let go", let);
            Diag.Event("order", "swept", new { lord = p.LeaderHero?.StringId, place = s.StringId, got, let_go = let, men = p.Party.NumberOfAllMembers, limit = p.Party.PartySizeLimit });
        }

        /// <summary>The troop the lord will get from this slot: TFAOMFixes' offer when it is loaded, else the volunteer himself.</summary>
        private static CharacterObject Offered(MobileParty p, Hero notable, int slot, CharacterObject raw)
        {
            Look();
            if (_offersFor == null) return raw;
            try
            {
                Clan clan = p.LeaderHero?.Clan ?? p.ActualClan;
                CultureObject want = p.LeaderHero?.Culture ?? clan?.Leader?.Culture ?? clan?.Culture;
                return _offersFor.Invoke(null, new object[] { clan, want, notable, slot, raw }) as CharacterObject ?? raw;
            }
            catch { return raw; }
        }

        /// <summary>The game's own gates: gold above what an AI lord keeps in hand (and his clan's, for a lord who does not lead it), room in his wages.</summary>
        private static bool CanPay(MobileParty p, Hero leader, CharacterObject t)
        {
            int cost = Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(t, leader).RoundedResultNumber;
            if (leader.Gold <= cost) return false;
            if (leader.Clan != Clan.PlayerClan)
            {
                if (leader.Gold - cost < HeroHelper.StartRecruitingMoneyLimit(leader)) return false;
                if (leader.Clan != null && leader != leader.Clan.Leader && leader.Clan.Gold < HeroHelper.StartRecruitingMoneyLimitForClanLeader(leader)) return false;
            }
            if (p.HasLimitedWage() && p.GetAvailableWageBudget() < Campaign.Current.Models.PartyWageModel.GetCharacterWage(t)) return false;
            return true;
        }

        private static bool HasOffLines(MobileParty p, Func<CharacterObject, bool> lines)
        {
            foreach (TroopRosterElement e in p.MemberRoster.GetTroopRoster())
                if (e.Character != null && !e.Character.IsHero && e.Number > 0 && !lines(e.Character)) return true;
            return false;
        }

        /// <summary>Lets one man from outside the clan's lines go: the lowest tier, a wounded one first.</summary>
        private static bool LetOneGo(MobileParty p, Func<CharacterObject, bool> lines)
        {
            TroopRoster r = p.MemberRoster;
            int pick = -1, pickTier = int.MaxValue;
            bool pickHurt = false;
            for (int i = 0; i < r.Count; i++)
            {
                TroopRosterElement e = r.GetElementCopyAtIndex(i);
                if (e.Character == null || e.Character.IsHero || e.Number <= 0 || lines(e.Character)) continue;
                bool hurt = e.WoundedNumber > 0;
                int tier = e.Character.Tier;
                if (pick < 0 || (hurt && !pickHurt) || (hurt == pickHurt && tier < pickTier)) { pick = i; pickTier = tier; pickHurt = hurt; }
            }
            if (pick < 0) return false;
            r.AddToCountsAtIndex(pick, -1, pickHurt ? -1 : 0);
            r.RemoveZeroCounts();
            return true;
        }

        /// <summary>The lord's clan's troop lines: its party recipe's troops, what grows into them and what they grow into;
        /// without a recipe, his own people's troops.</summary>
        private static Func<CharacterObject, bool> LinesOf(MobileParty p)
        {
            Clan clan = p.LeaderHero?.Clan ?? p.ActualClan;
            CultureObject culture = p.LeaderHero?.Culture ?? clan?.Culture;
            HashSet<CharacterObject> set = null;
            PartyTemplateObject recipe = clan?.DefaultPartyTemplate;
            if (recipe != null && !Lines.TryGetValue(recipe, out set)) { set = Build(recipe); Lines[recipe] = set; }
            if (set != null && set.Count > 0) return c => c == null || c.IsHero || set.Contains(c);
            return c => c == null || c.IsHero || (culture != null && c.Culture == culture);
        }

        private static HashSet<CharacterObject> Build(PartyTemplateObject recipe)
        {
            var set = new HashSet<CharacterObject>();
            if (recipe.Stacks == null) return set;
            if (_parents == null)
            {
                _parents = new Dictionary<CharacterObject, List<CharacterObject>>();
                foreach (CharacterObject c in MBObjectManager.Instance.GetObjectTypeList<CharacterObject>())
                {
                    if (c == null || c.IsHero || c.UpgradeTargets == null) continue;
                    foreach (CharacterObject u in c.UpgradeTargets)
                    {
                        if (u == null) continue;
                        if (!_parents.TryGetValue(u, out List<CharacterObject> l)) { l = new List<CharacterObject>(); _parents[u] = l; }
                        l.Add(c);
                    }
                }
            }
            var down = new Stack<CharacterObject>();
            var up = new Stack<CharacterObject>();
            foreach (PartyTemplateStack st in recipe.Stacks)
                if (st.Character != null && !st.Character.IsHero) { down.Push(st.Character); up.Push(st.Character); }
            var seen = new HashSet<CharacterObject>();
            while (down.Count > 0)
            {
                CharacterObject c = down.Pop();
                if (!seen.Add(c)) continue;
                set.Add(c);
                if (c.UpgradeTargets != null) foreach (CharacterObject u in c.UpgradeTargets) if (u != null) down.Push(u);
            }
            seen.Clear();
            while (up.Count > 0)
            {
                CharacterObject c = up.Pop();
                if (!seen.Add(c)) continue;
                set.Add(c);
                if (_parents.TryGetValue(c, out List<CharacterObject> l)) foreach (CharacterObject parent in l) up.Push(parent);
            }
            return set;
        }
    }
}
