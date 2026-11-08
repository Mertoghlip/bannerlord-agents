using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Helpers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// The recruit order as a tour (since 0.9.2). Oct 22: a lord with a recruit order from an agent also follows the
    /// logic of my own parties. The same rules as the player's own parties on a recruit order and as the computer's lords
    /// (both in TFAOMFixes, my own mod; this is the agents' copy, kept in this module):
    /// - from the order's place, then the nearest town, castle or village of the realm, an ally or a realm at peace with it,
    ///   within reach (four times the usual gap between neighbouring towns), not swept by this order in the last day; one
    ///   after another;
    /// - in each, every volunteer the lord's standing with the notable reaches and he can pay for (the game's own gold and
    ///   wage rules), with no dice, until his ranks are at 80%; above that only men of his clan's troop lines (the troops of
    ///   the clan's party recipe, what grows into them and what they grow into), and once he is full one man from outside
    ///   them goes for each one taken (lowest tier, wounded first);
    /// - the order ends when his ranks are full and all of his clan's lines, or when nothing within reach is left to recruit,
    ///   and the agent hears why: no volunteers, or volunteers he cannot pay for (his gold, his clan's, his wage cap; since
    ///   0.9.3). A new order replaces it, as any order.
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

        public static void Reset() { Swept.Clear(); Lines.Clear(); _parents = null; LastStop.Clear(); }
        public static void Forget(int orderId) => Swept.Remove(orderId);

        /// <summary>Each lord's last recruiting stop by our orders (0.13, the language's recruited_last): the men taken, when, where.</summary>
        private sealed class Stop
        {
            public int Men;
            public double Hours;
            public string Place;
        }

        private static readonly Dictionary<MobileParty, Stop> LastStop = new Dictionary<MobileParty, Stop>();

        /// <summary>The hourly copy of a held lord (Purse.Fill): his last stop's recruits.</summary>
        public static void Fill(MobileParty p, PartyRow r)
        {
            if (!LastStop.TryGetValue(p, out Stop s)) return;
            r.RecruitedLast = s.Men;
            r.RecruitedLastHours = s.Hours;
            r.RecruitedLastAt = s.Place;
        }

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
            double floor = o.Setting("garrison_floor", 0);
            // a village is entered and left at once: where he stands is the game's own reckoning (0.11.3, Orders.Where)
            Settlement here = Orders.Where(p);
            if (here != null && !SweptWithin(o.Id, here, ResweepHours, now))
            {
                bool swept = false;
                if (MayRecruitHere(p, here)) { Sweep(p, here, limit, lines); swept = true; }
                // his clan's own garrison, down to the order's floor (0.13.19)
                if (GarrisonGives(p, here, floor, lines)) { FromGarrison(p, here, floor, lines); swept = true; }
                if (swept) Mark(o.Id, here, now);
            }
            int men = p.Party.NumberOfAllMembers;
            if (men >= limit && !HasOffLines(p, lines))
            {
                key = "ranks full";
                return "the ranks are full (" + men + " men), all of the clan's own troop lines";
            }
            Settlement target = o.Place;
            Array.Clear(Unpaid, 0, Unpaid.Length);
            if (target == null || target == here || SweptWithin(o.Id, target, SweepMemoryHours, now) || Threatened(o, p, target)
                || !((MayRecruitHere(p, target) && AnyOffer(p, target, limit, lines)) || GarrisonGives(p, target, floor, lines)))
                target = FindNext(o, p, now, limit, lines);
            if (target == null) return Stopped(p, men, limit, out key);
            if (target != o.Place) { o.Place = target; moved = true; }
            return null;
        }

        /// <summary>The single-stop move (defend's and patrol's refill): buys the same way, at most every half hour.</summary>
        public static void SweepAt(Orders.HandOrder o, double now)
        {
            MobileParty p = o.Party;
            Settlement here = Orders.Where(p);
            if (here == null || p.Party.PartySizeLimit <= 0 || SweptWithin(o.Id, here, ResweepHours, now)) return;
            Func<CharacterObject, bool> lines = LinesOf(p);
            double floor = o.Setting("garrison_floor", 0);
            bool swept = false;
            if (MayRecruitHere(p, here)) { Sweep(p, here, p.Party.PartySizeLimit, lines); swept = true; }
            if (GarrisonGives(p, here, floor, lines)) { FromGarrison(p, here, floor, lines); swept = true; }
            if (swept) Mark(o.Id, here, now);
        }

        /// <summary>
        /// Can the lord take men from the garrison of this place (0.13.19, garrison_floor)? A castle or town of his own clan, not besieged,
        /// whose garrison's strength (the game's estimate) is above the order's floor, with a healthy man he may take by the recruit order's
        /// rules (any kind under 80% of his ranks, his clan's lines above it, his wage cap, never past his ranks' limit). Floor 0: never.
        /// </summary>
        private static bool GarrisonGives(MobileParty p, Settlement s, double floor, Func<CharacterObject, bool> lines)
        {
            if (floor <= 0 || s == null || !s.IsFortification || s.IsUnderSiege || s.Town == null) return false;
            Clan clan = p.LeaderHero?.Clan;
            if (clan == null || s.OwnerClan != clan) return false;
            PartyBase g = s.Town.GarrisonParty?.Party;
            if (g == null || g.EstimatedStrength <= floor) return false;
            int limit = p.Party.PartySizeLimit, men = p.Party.NumberOfAllMembers;
            if (limit <= 0 || men >= limit) return false;
            foreach (TroopRosterElement e in g.MemberRoster.GetTroopRoster())
                if (Takes(p, e, men, limit, lines)) return true;
            return false;
        }

        private static bool Takes(MobileParty p, TroopRosterElement e, int men, int limit, Func<CharacterObject, bool> lines)
        {
            CharacterObject c = e.Character;
            if (c == null || c.IsHero || e.Number - e.WoundedNumber <= 0) return false;
            if (men >= FillTo * limit && !lines(c)) return false;
            return !(p.HasLimitedWage() && p.GetAvailableWageBudget() < Campaign.Current.Models.PartyWageModel.GetCharacterWage(c));
        }

        /// <summary>
        /// Takes healthy men from his clan's garrison where he stands, the highest tier first, one at a time, as long as the garrison's strength
        /// (the game's estimate, worked out again after each) stays at or above the floor (Nov 1: take troops from those garrisons as they
        /// recruit up until the strength of the garrison falls to a floor set by the agent). Returns how many.
        /// </summary>
        private static int FromGarrison(MobileParty p, Settlement s, double floor, Func<CharacterObject, bool> lines)
        {
            PartyBase g = s.Town.GarrisonParty.Party;
            int limit = p.Party.PartySizeLimit, got = 0;
            float before = g.EstimatedStrength;
            while (p.Party.NumberOfAllMembers < limit)
            {
                int men = p.Party.NumberOfAllMembers;
                CharacterObject pick = null;
                int tier = -1;
                foreach (TroopRosterElement e in g.MemberRoster.GetTroopRoster())
                    if (Takes(p, e, men, limit, lines) && e.Character.Tier > tier) { pick = e.Character; tier = e.Character.Tier; }
                if (pick == null) break;
                g.MemberRoster.AddToCounts(pick, -1);
                if (g.EstimatedStrength < floor) { g.MemberRoster.AddToCounts(pick, 1); break; }
                p.MemberRoster.AddToCounts(pick, 1);
                got++;
            }
            if (got == 0) return 0;
            // his last stop's recruits count them (recruited_last)
            double now = CampaignTime.Now.ToHours;
            if (!LastStop.TryGetValue(p, out Stop last) || last.Place != s.StringId || now - last.Hours > 1) LastStop[p] = last = new Stop { Place = s.StringId, Hours = now };
            last.Men += got;
            last.Hours = now;
            Diag.Count("recruit order:garrison men taken", got);
            Diag.Event("order", "from garrison", new { lord = p.LeaderHero?.StringId, place = s.StringId, got, floor, strength_before = Math.Round(before), strength_after = Math.Round(g.EstimatedStrength), men = p.Party.NumberOfAllMembers, limit });
            return got;
        }

        /// <summary>
        /// Why the tour stops with no place left to go (Oct 22: 13 of Dol Guldur's 14 tours ended in their first hour with
        /// "nothing left to recruit" while volunteers waited; their wages were at the cap the game sets each day from the clan
        /// leader's gold, and the agent, not told so, sent them again). When the volunteers within reach were there but none
        /// could be paid for, the commonest bar is named.
        /// </summary>
        private static string Stopped(MobileParty p, int men, int limit, out string key)
        {
            string ranks = ", ranks at " + (100 * men / limit) + "%";
            Hero leader = p.LeaderHero;
            Bar main = Bar.None;
            for (int b = 1; b < Unpaid.Length; b++) if (Unpaid[b] > 0 && (main == Bar.None || Unpaid[b] > Unpaid[(int)main])) main = (Bar)b;
            switch (main)
            {
                case Bar.Wage:
                    key = "wage cap";
                    return "volunteers are within reach, but his wages are at their cap: " + p.TotalWage + " a day of the " + p.PaymentLimit
                           + " his clan lets him pay (the game sets that cap each day from the gold of his clan's leader)" + ranks;
                case Bar.Gold:
                    key = "no gold";
                    return "volunteers are within reach, but he has too little gold: " + (leader?.Gold ?? 0) + ", and the game keeps a lord from recruiting below "
                           + (leader != null ? (int)HeroHelper.StartRecruitingMoneyLimit(leader) : 0) + ranks;
                case Bar.ClanGold:
                    key = "no gold";
                    return "volunteers are within reach, but his clan's purse is too low: " + (leader?.Clan?.Gold ?? 0) + ", and the game keeps its lords from recruiting below "
                           + (leader != null ? (int)HeroHelper.StartRecruitingMoneyLimitForClanLeader(leader) : 0) + ranks;
                default:
                    key = "nothing to recruit";
                    return "nothing left to recruit within reach" + ranks;
            }
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
            Settlement here = Orders.Where(p), entrance = EntranceOf(p);
            MapDistanceModel model = Campaign.Current.Models.MapDistanceModel;
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s == here || (!s.IsTown && !s.IsCastle && !s.IsVillage)) continue;
                float d2 = at.DistanceSquared(s.GetPosition2D);
                if (d2 > reach2 || SweptWithin(o.Id, s, SweepMemoryHours, now)) continue;
                // nearest by road, as the game reckons it, not as the crow flies (0.11.3: a tenth of run C1's legs ran 2.6 times the straight line)
                places.Add(new KeyValuePair<float, Settlement>(RoadTo(p, at, s, entrance, model), s));
            }
            places.Sort((a, b) => a.Key.CompareTo(b.Key));
            // 0.16 (idea D): a stop under threat is passed by, and one another lord of the realm on this order is already bound for is taken only when
            // nothing else is left (the safe recruiting loop's and the guard loop's ways: no riding into danger, no emptying each other's stops)
            Settlement claimed = null;
            foreach (var kv in places)
            {
                Settlement s = kv.Value;
                if (Threatened(o, p, s)) continue;
                if (!((MayRecruitHere(p, s) && AnyOffer(p, s, limit, lines)) || GarrisonGives(p, s, o.Setting("garrison_floor", 0), lines))) continue;
                if (!Claimed(o, s)) return s;
                if (claimed == null) claimed = s;
            }
            if (claimed != null) Diag.Count("recruit order:stop shared with another lord");
            return claimed;
        }

        /// <summary>
        /// A stop under threat (0.16): the enemies the realm saw this hour within 15 of its gate (Tools.Threats, from the realm's own view) are caution times
        /// his strength or more; caution 0 never passes one by.
        /// </summary>
        private static bool Threatened(Orders.HandOrder o, MobileParty p, Settlement s)
        {
            double caution = o.Setting("caution", Tools.Library.Caution.Default);
            if (caution <= 0 || s == null || o.Realm == null) return false;
            Vec2 g = s.GatePosition.ToVec2();
            double near = Tools.Threats.Around(o.Realm.StringId, g.X, g.Y);
            if (near <= 0 || near < caution * Math.Max(1f, p.Party.EstimatedStrength)) return false;
            Diag.Count("recruit order:stop passed by, under threat");
            return true;
        }

        /// <summary>Another lord of the realm on this order is bound for this stop now (0.16).</summary>
        private static bool Claimed(Orders.HandOrder o, Settlement s)
        {
            foreach (Orders.HandOrder x in Orders.ActiveOrders)
                if (x != o && x.Realm == o.Realm && x.Tool == "recruit" && x.Place == s) return true;
            return false;
        }

        /// <summary>The holding whose gate the game counts this party's road distances from, or null.</summary>
        private static Settlement EntranceOf(MobileParty p)
        {
            try
            {
                PathFaceRecord face = p.CurrentNavigationFace;
                return face.IsValid() ? Campaign.Current.Models.MapDistanceModel.GetClosestEntranceToFace(face, MobileParty.NavigationType.Default).Item1 : null;
            }
            catch (Exception e) { Diag.Swallowed("the game's closest entrance for a touring lord", e); return null; }
        }

        /// <summary>A party's way to a holding by road (Roads.To's rule, on the game's thread): through its entrance; the straight line when the game's table lacks it.</summary>
        private static float RoadTo(MobileParty p, Vec2 at, Settlement s, Settlement entrance, MapDistanceModel model)
        {
            Vec2 gate = s.GatePosition.ToVec2();
            float crow = at.Distance(gate);
            if (entrance == null || entrance == s) return crow;
            float road;
            try { road = model.GetDistance(entrance, s, false, false, MobileParty.NavigationType.Default); }
            catch (Exception e) { Diag.Swallowed("the game's road distance for a touring lord", e); return crow; }
            if (road < 0 || road > 1e7f) return crow;
            return Math.Max(crow, crow - entrance.GatePosition.ToVec2().Distance(gate) + road);
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
                    Bar bar = CanPay(p, leader, t);
                    if (bar == Bar.None) return true;
                    Unpaid[(int)bar]++;
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
                    if (CanPay(p, leader, t) != Bar.None) continue;
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
            // the stop is the lord's last, men or none (recruited_last: 0 when a stop gave nothing)
            double now = CampaignTime.Now.ToHours;
            if (!LastStop.TryGetValue(p, out Stop last) || last.Place != s.StringId || now - last.Hours > 1) LastStop[p] = last = new Stop { Place = s.StringId, Hours = now };
            last.Men += got;
            last.Hours = now;
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

        /// <summary>What keeps a lord from paying for a volunteer: none, his gold, his clan's purse, his wage cap.</summary>
        private enum Bar { None, Gold, ClanGold, Wage }

        // the bars met by volunteers seen but not payable, for the decision being made now
        private static readonly int[] Unpaid = new int[4];

        /// <summary>The game's own gates: gold above what an AI lord keeps in hand (and his clan's, for a lord who does not lead it), room in his wages.</summary>
        private static Bar CanPay(MobileParty p, Hero leader, CharacterObject t)
        {
            int cost = Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(t, leader).RoundedResultNumber;
            if (leader.Gold <= cost) return Bar.Gold;
            if (leader.Clan != Clan.PlayerClan)
            {
                if (leader.Gold - cost < HeroHelper.StartRecruitingMoneyLimit(leader)) return Bar.Gold;
                if (leader.Clan != null && leader != leader.Clan.Leader && leader.Clan.Gold < HeroHelper.StartRecruitingMoneyLimitForClanLeader(leader)) return Bar.ClanGold;
            }
            if (p.HasLimitedWage() && p.GetAvailableWageBudget() < Campaign.Current.Models.PartyWageModel.GetCharacterWage(t)) return Bar.Wage;
            return Bar.None;
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
