using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents.Common;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// The copy the bridge takes on the game's own thread, once per in-game hour. Only plain numbers and names
    /// leave this class; everything heavier happens on the worker (Brain).
    /// </summary>
    internal static class Capture
    {
        private static readonly string[] Behaviours = Enum.GetNames(typeof(AiBehavior));

        public static Snapshot Take(Kingdom watched, bool withVision) => Take(watched != null ? new List<Kingdom> { watched } : new List<Kingdom>(), withVision);

        /// <summary>The hour for every watched realm (since 0.8): one copy of the world, each realm's own part in Snapshot.Seats, the first's also in the fields.</summary>
        public static Snapshot Take(List<Kingdom> seats, bool withVision)
        {
            long t0 = Perf.Now();
            Kingdom watched = seats.Count > 0 ? seats[0] : null;
            var seatIds = new HashSet<IFaction>(seats);
            Campaign c = Campaign.Current;
            IFaction player = Clan.PlayerClan?.MapFaction;
            var snap = new Snapshot
            {
                Campaign = c.UniqueGameId ?? "unknown",
                Hours = CampaignTime.Now.ToHours,
                Night = c.IsNight,
                Date = DateText(),
                PlayerFaction = player?.StringId,
                PlayerParty = MobileParty.MainParty?.StringId,
                Watched = watched?.StringId,
                Wall = DateTime.UtcNow.Ticks,
                LoadId = Bridge.LoadId,
                LoadedAt = Bridge.LoadedAt,
                MapCrc = MapExport.Crc,
                // the run and whether you rule your realm yourself (0.13: the record's tags, and your realm's hourly report in runs of yours)
                Run = RunTags.Id,
                Arm = RunTags.Arm,
                YouRule = Recorder.YouRuleNow
            };
            snap.RecordHourly = snap.YouRule && snap.Run != null && Settings.Record;
            var factions = new Dictionary<string, IFaction>();

            foreach (Kingdom k in Kingdom.All)
            {
                if (k == null || k.IsEliminated) continue;
                var row = new FactionRow { Id = k.StringId, Name = Names.Faction(k), Ruler = Names.Hero(k.Leader), Color = k.Color, Color2 = k.Color2, Lords = k.AliveLords.Count };
                foreach (Kingdom o in Kingdom.All)
                    if (o != null && o != k && !o.IsEliminated && k.IsAtWarWith(o)) row.AtWar.Add(o.StringId);
                foreach (Kingdom a in k.AlliedKingdoms)
                    if (a != null) row.Allies.Add(a.StringId);
                snap.Factions[k.StringId] = row;
                factions[k.StringId] = k;
            }
            // each realm an agent serves: its diplomacy as its ruler's screens show it (0.13, Diplomacy)
            foreach (Kingdom k in seats)
            {
                if (k == null || k.IsEliminated) continue;
                try { snap.Diplomacy[k.StringId] = Diplomacy.Capture(k); }
                catch (Exception e) { Diag.Swallowed("copying a realm's diplomacy", e); }
            }
            long t1 = Perf.Now();
            var allies = new HashSet<Kingdom>();
            if (watched != null) foreach (Kingdom a in watched.AlliedKingdoms) if (a != null) allies.Add(a);

            var besieged = new List<Settlement>();
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s.IsHideout) continue;
                IFaction f = s.MapFaction;
                if (s.SiegeEvent != null) besieged.Add(s);
                var r = new SettlementRow
                {
                    Id = s.StringId,
                    Name = Names.Settlement(s),
                    Kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : "other",
                    Faction = f?.StringId,
                    X = Text.R1(s.Position.X),
                    Y = Text.R1(s.Position.Y),
                    Gx = Text.R1(s.GatePosition.X),
                    Gy = Text.R1(s.GatePosition.Y),
                    Militia = (int)s.Militia
                };
                if (s.SiegeEvent != null)
                {
                    var camp = s.SiegeEvent.BesiegerCamp;
                    r.Besieger = camp?.LeaderParty?.StringId;
                    r.BesiegedBy = camp?.MapFaction?.StringId;
                }
                if (s.Town != null)
                {
                    PartyBase g = s.Town.GarrisonParty?.Party;
                    r.Garrison = g?.NumberOfHealthyMembers ?? 0;
                    // the game keeps these estimates until the party changes; only the watched realm's tools weigh them, for its
                    // own and its allies' castles and towns (whether a place would hold an assault)
                    // since 0.7.4 for every castle and town (the game keeps these estimates; the view shows an enemy's only where the realm's eyes reach),
                    // for storms run ahead by the "battle odds" question
                    if (g != null) r.GarrisonStrength = (float)Math.Round(g.EstimatedStrength);
                    MobileParty militia = s.MilitiaPartyComponent?.MobileParty;
                    if (militia != null && militia.IsActive) r.MilitiaStrength = (float)Math.Round(militia.Party.EstimatedStrength);
                    r.WallLevel = s.Town.GetWallLevel();
                    // its food (0.13.19): the store, its most, and what it eats a day under siege (the food model's two eaters)
                    SettlementFoodModel food = Campaign.Current.Models.SettlementFoodModel;
                    r.Food = (float)Math.Round(s.Town.FoodStocks, 1);
                    r.FoodCap = s.Town.FoodStocksUpperLimit();
                    r.FoodPerDay = (float)Math.Round(s.Town.Prosperity / Math.Max(1, food.NumberOfProsperityToEatOneFood)
                                                     + (g?.NumberOfAllMembers ?? 0) / (float)Math.Max(1, food.NumberOfMenOnGarrisonToEatOneFood), 1);
                }
                if (s.Village != null)
                {
                    r.VillageState = s.Village.VillageState.ToString();
                    r.HitPoints = s.SettlementHitPoints;   // a raid loots the village when this runs out (RaidEventComponent)
                    r.Bound = s.Village.Bound?.StringId;
                }
                snap.Settlements.Add(r);
                snap.SettlementById[r.Id] = r;
                if (f != null && !factions.ContainsKey(f.StringId)) factions[f.StringId] = f;
            }
            long t2 = Perf.Now();

            var live = new List<MobileParty>(4096);
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == null || !p.IsActive) continue;
                PartyRow r = Row(p, p.MapFaction != null && seatIds.Contains(p.MapFaction));
                live.Add(p);
                snap.Parties.Add(r);
                snap.PartyById[r.Id] = r;
                IFaction f = p.MapFaction;
                if (f != null && !factions.ContainsKey(f.StringId)) factions[f.StringId] = f;
            }
            long t2a = Perf.Now();
            MarchRules(snap);
            Sieges(snap, besieged);
            long t2b = Perf.Now();
            Battles(snap);
            long t3 = Perf.Now();

            foreach (Kingdom k in seats) snap.Seats.Add(new SeatSight { Realm = k.StringId });
            foreach (KeyValuePair<string, IFaction> kv in factions)
            {
                snap.FactionNames[kv.Key] = Names.Faction(kv.Value);
                snap.FactionColors[kv.Key] = kv.Value.Color;
                for (int i = 0; i < seats.Count; i++)
                    if (kv.Value != seats[i] && FactionManager.IsAtWarAgainstFaction(kv.Value, seats[i])) snap.Seats[i].Hostile.Add(kv.Key);
                if (player != null && kv.Value != player && FactionManager.IsAtWarAgainstFaction(kv.Value, player)) snap.HostileToPlayer.Add(kv.Key);
            }

            for (int i = 0; i < seats.Count; i++) Realm(seats[i], snap.Seats[i], snap);
            snap.Roads = RoadsFor(seats.Cast<IFaction>().ToList(), player);
            long t4 = Perf.Now();
            if (withVision)
                for (int i = 0; i < seats.Count; i++)
                {
                    // the sight rule writes into a copy's fields; each realm's goes into its own part
                    Snapshot into = snap.For(snap.Seats[i]);
                    Vision.FactionSees(seats[i], live, into);
                    snap.Seats[i].Sees = into.WatchedSees;
                    snap.Seats[i].Inspected = into.InspectedByWatched;
                    snap.Seats[i].Vision = into.Vision;
                }
            if (snap.Seats.Count > 0)
            {
                SeatSight first = snap.Seats[0];
                snap.WatchedSees = first.Sees;
                snap.InspectedByWatched = first.Inspected;
                snap.HostileToWatched = first.Hostile;
                snap.Captives = first.Captives;
                snap.Clans = first.Clans;
                snap.Vision = first.Vision;
            }
            long t5 = Perf.Now();

            // Names only where someone may read them: lords, the watched realms' and the player's own parties, and anything seen.
            for (int i = 0; i < live.Count; i++)
            {
                PartyRow r = snap.Parties[i];
                if (r.Kind == "lord" || r.SeenByPlayer || r.Faction == snap.PlayerFaction || snap.Seats.Any(x => x.Realm == r.Faction || (x.Sees != null && x.Sees.ContainsKey(r.Id))))
                    r.Name = Names.Party(live[i]);
            }
            long t6 = Perf.Now();
            // kingdoms, settlements, parties (the rows, then the march rules and sieges, then the battles: split in 0.7.4), the realm, sight, names
            snap.PartsMs = new[] { Ms(t0, t1), Ms(t1, t2), Ms(t2, t3), Ms(t3, t4), Ms(t4, t5), Ms(t5, t6), Ms(t2, t2a), Ms(t2a, t2b), Ms(t2b, t3) };
            return snap;
        }

        private static double Ms(long a, long b) => (b - a) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        // ---------- the game's road distances (0.7.1) ----------

        private static RoadBook _roads;
        private const int Neighbours = 12;

        /// <summary>A new campaign: the road table is copied again at its first hour.</summary>
        public static void ResetRoads() => _roads = null;

        /// <summary>
        /// The game's own road distances (its navigation cache, filled for every pair of holdings when the campaign starts or
        /// read from the map pack's file, so these are lookups, not searches): each holding of the watched realm and of the
        /// player's to every holding, and each holding to its 12 nearest. The first copy is made at load; a holding a realm
        /// gains later gets its row in the next hour's copy. A new book replaces the old, so the worker's copy never changes.
        /// </summary>
        public static RoadBook RoadsFor(IFaction watched, IFaction player) => RoadsFor(watched != null ? new List<IFaction> { watched } : new List<IFaction>(), player);

        /// <summary>The same, for every watched realm (since 0.8).</summary>
        public static RoadBook RoadsFor(List<IFaction> watched, IFaction player)
        {
            try
            {
                MapDistanceModel model = Campaign.Current.Models.MapDistanceModel;
                List<Settlement> all = Settlement.All.Where(x => x != null && !x.IsHideout).ToList();
                var need = new List<Settlement>();
                foreach (Settlement s in all)
                    if ((s.MapFaction != null && watched.Contains(s.MapFaction)) || (player != null && s.MapFaction == player))
                        if (_roads == null || !_roads.HasRow(s.StringId)) need.Add(s);
                if (_roads != null && need.Count == 0) return _roads;
                var book = new RoadBook();
                if (_roads != null)
                    foreach (KeyValuePair<string, Dictionary<string, float>> row in _roads.Rows)
                        foreach (KeyValuePair<string, float> kv in row.Value) book.Set(row.Key, kv.Key, kv.Value);
                else
                    foreach (Settlement a in all)
                        foreach (Settlement b in all.Where(x => x != a).OrderBy(x => x.Position.DistanceSquared(a.Position)).Take(Neighbours))
                            book.Set(a.StringId, b.StringId, Road(model, a, b));
                foreach (Settlement a in need)
                    foreach (Settlement b in all)
                        if (b != a) book.Set(a.StringId, b.StringId, Road(model, a, b));
                Diag.Event("roads", "copied", new { rows = book.Rows.Count, pairs = book.Pairs, new_rows = need.Count });
                return _roads = book;
            }
            catch (Exception e)
            {
                Diag.Swallowed("copying the game's road distances", e);
                return _roads;
            }
        }

        private static float Road(MapDistanceModel model, Settlement a, Settlement b)
        {
            float d = model.GetDistance(a, b, false, false, MobileParty.NavigationType.Default);
            return d >= 0 && d < 1e7f ? Text.R1(d) : float.NaN;
        }

        /// <summary>The holding whose gate the game counts a party's road distances from (the ground it stands on decides).</summary>
        private static string Entrance(MobileParty p)
        {
            try
            {
                PathFaceRecord face = p.CurrentNavigationFace;
                if (!face.IsValid()) return null;
                return Campaign.Current.Models.MapDistanceModel.GetClosestEntranceToFace(face, MobileParty.NavigationType.Default).Item1?.StringId;
            }
            catch (Exception e) { Diag.Swallowed("the game's closest entrance for a party", e); return null; }
        }

        /// <summary>
        /// The watched realm's own records: its lords held prisoner (where, by whom, since when), its clans' influence and
        /// the ruler's clan's gold, and the game's range for calling lords into an army.
        /// </summary>
        private static void Realm(Kingdom watched, SeatSight seat, Snapshot snap)
        {
            snap.CallRange = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy;
            foreach (Hero h in watched.AliveLords)
            {
                if (h == null || !h.IsPrisoner) continue;
                var c = new CaptiveRow { Hero = h.StringId, Name = Names.Hero(h) };
                PartyBase at = h.PartyBelongedToAsPrisoner;
                if (at?.Settlement != null)
                {
                    c.Holding = at.Settlement.StringId;
                    c.Faction = at.Settlement.MapFaction?.StringId;
                    c.X = Text.R1(at.Settlement.Position.X);
                    c.Y = Text.R1(at.Settlement.Position.Y);
                }
                else if (at?.MobileParty != null)
                {
                    MobileParty p = at.MobileParty;
                    // a prisoner inside a holding with the party holding them is in that holding
                    c.Holding = p.CurrentSettlement?.StringId;
                    c.Party = p.StringId;
                    c.PartyLord = p.LeaderHero?.StringId;
                    c.Faction = p.MapFaction?.StringId;
                    c.X = Text.R1(p.Position.X);
                    c.Y = Text.R1(p.Position.Y);
                }
                double since = h.CaptivityStartTime.ToHours;
                if (since > 0 && since <= snap.Hours + 1) c.Since = since;
                seat.Captives.Add(c);
            }
            foreach (Clan cl in watched.Clans)
            {
                if (cl == null || cl.IsEliminated) continue;
                bool ruler = cl == watched.RulingClan;
                // every clan's gold (its leader's) and its day's change by the game's reckoning: the realm's own purses (0.9.4)
                seat.Clans.Add(new ClanRow
                {
                    Id = cl.StringId, Name = Names.Faction(cl), Leader = cl.Leader?.StringId, Influence = (float)Math.Round(cl.Influence),
                    Gold = cl.Leader != null ? cl.Gold : -1, Daily = Purse.DailyOf(cl), Ruler = ruler
                });
            }
        }

        /// <summary>One party as plain numbers. For the watched realm's own lords (own) a few more, which its tools need.</summary>
        public static PartyRow Row(MobileParty p, bool own)
        {
            var r = new PartyRow { Id = p.StringId, X = Text.R1(p.Position.X), Y = Text.R1(p.Position.Y) };
            r.Faction = p.MapFaction?.StringId;
            PartyBase party = p.Party;
            r.Men = party.NumberOfHealthyMembers;
            r.Wounded = party.NumberOfWoundedTotalMembers;
            r.Kind = p.IsLordParty ? "lord" : p.IsCaravan ? "caravan" : p.IsVillager ? "villager" : p.IsBandit ? "bandit"
                   : p.IsGarrison ? "garrison" : p.IsMilitia ? "militia" : "other";
            // the game recomputes a party's strength whenever its roster changes, so only ask where a report needs it
            if (p.IsLordParty || p.IsBandit) r.Strength = (float)Math.Round(party.EstimatedStrength);
            if (p.IsLordParty) r.Speed = Text.R1(p.LastCalculatedBaseSpeed);   // the speed the game last worked out: a field, not a new sum
            Hero leader = p.LeaderHero;
            if (own && p.IsLordParty)
            {
                r.SizeLimit = party.PartySizeLimit;
                r.FoodDays = p.GetNumDaysForFoodToLast();
                r.Clan = leader?.Clan?.StringId;
                if (leader != null) Purse.Fill(r, p);
            }
            r.Leader = leader?.StringId;
            Settlement inside = p.CurrentSettlement;
            r.InSettlement = inside?.StringId;
            r.Entrance = inside != null ? inside.StringId : Entrance(p);
            Army army = p.Army;
            if (army != null)
            {
                r.ArmyLeader = army.LeaderParty?.StringId;
                if (army.LeaderParty == p)
                {
                    r.ArmyMen = army.TotalHealthyMembers;
                    r.ArmyParties = army.Parties.Count;
                    r.ArmyStrength = (float)Math.Round(army.EstimatedStrength);
                }
                else r.Attached = p.AttachedTo != null;
            }
            int b = (int)p.DefaultBehavior;
            r.Doing = b >= 0 && b < Behaviours.Length ? Behaviours[b] : "?";
            r.TargetSettlement = p.TargetSettlement?.StringId;
            r.TargetParty = p.TargetParty?.StringId;
            r.Besieging = p.BesiegedSettlement?.StringId;
            MapEvent me = p.MapEvent;
            r.InBattle = me != null;
            if (me != null && me.IsRaid) r.Raiding = me.MapEventSettlement?.StringId;
            r.SeenByPlayer = p.IsVisible && !p.IsMainParty;
            r.Spottable = inside == null || leader?.ClanBanner != null
                          || (me != null && me.IsSiegeAssault && party.Side == BattleSideEnum.Attacker);
            // the way and the speed's parts: lords always (their arrival times, 0.7.3), caravans for the dashboard
            if (p.IsLordParty || Settings.Dashboard) Way(p, r, inside == null && me == null && !r.Attached);
            if (p.IsLordParty && !r.Attached) Pace(p, r, leader);
            if (p.IsLordParty)
            {
                r.Morale = (float)Math.Round(p.Morale, 1);
                if (leader != null) r.Tactics = leader.GetSkillValue(DefaultSkills.Tactics);
            }
            return r;
        }

        // ---------- the march's parts (0.7.3, GamePace) ----------

        // the game's own cache of a party's base speed: the very sum it moves the party with (MobileParty.CalculateSpeedForPartyUnified)
        private static readonly AccessTools.FieldRef<MobileParty, ExplainedNumber> BaseSpeedField = Field<ExplainedNumber>("_lastCalculatedBaseSpeedExplained");

        /// <summary>
        /// A lord's march, as the parts of the game's speed sum (GamePace): the base number and the base factors from the game's
        /// cache (no new sum: an army's leader's cache pools its parties), the ground under it, its people as TAOM finds it (the
        /// lord's culture, else its realm's, else its owner's: CultureFeatAdapter.ResolvePartyCulture), its scout's ground and
        /// hour perks, the share on foot (Forest Kin needs three quarters), and snowfall where it stands.
        /// </summary>
        private static void Pace(MobileParty p, PartyRow r, Hero leader)
        {
            try
            {
                if (BaseSpeedField != null)
                {
                    ExplainedNumber e = BaseSpeedField(p);
                    r.SpeedBase = (float)Math.Round(e.BaseNumber, 4);
                    r.SpeedFactors = (float)Math.Round(e.SumOfFactors, 4);
                }
                if (FinalSpeedField != null) r.FinalSpeed = (float)Math.Round(FinalSpeedField(p), 4);
                r.Culture = (leader?.Culture ?? p.MapFaction?.Culture ?? p.Owner?.Culture)?.StringId;
                // the ground, the scout's perks and the weather matter only on the march (0.7.4: a cost trim; the weather call carries
                // TAOM's own prefix)
                if (r.InSettlement != null) return;
                PathFaceRecord face = p.CurrentNavigationFace;
                if (face.IsValid()) r.Terrain = (int)Campaign.Current.MapSceneWrapper.GetFaceTerrainType(face);
                Hero scout = p.EffectiveScout;
                if (scout != null)
                {
                    int perks = 0;
                    if (scout.GetPerkValue(DefaultPerks.Scouting.ForestKin)) perks |= GamePace.PerkForestKin;
                    if (scout.GetPerkValue(DefaultPerks.Scouting.Pathfinder)) perks |= GamePace.PerkPathfinder;
                    if (scout.GetPerkValue(DefaultPerks.Scouting.DesertBorn)) perks |= GamePace.PerkDesertBorn;
                    if (scout.GetPerkValue(DefaultPerks.Scouting.NightRunner)) perks |= GamePace.PerkNightRunner;
                    if (scout.GetPerkValue(DefaultPerks.Scouting.DayTraveler)) perks |= GamePace.PerkDayTraveler;
                    r.ScoutPerks = perks;
                    if ((perks & GamePace.PerkForestKin) != 0)
                    {
                        int all = p.MemberRoster.TotalManCount;
                        r.FootShare = all > 0 ? (float)Math.Round((float)p.Party.NumberOfMenWithoutHorse / all, 3) : 1f;
                    }
                }
                MapWeatherModel.WeatherEvent w = Campaign.Current.Models.MapWeatherModel.GetWeatherEventInPosition(p.Position.ToVec2());
                r.Snowing = w == MapWeatherModel.WeatherEvent.Snowy || w == MapWeatherModel.WeatherEvent.Blizzard;
            }
            catch (Exception e) { Diag.Swallowed("a lord's speed parts", e); }
        }

        private static string _rulesFor;
        private static Dictionary<string, float[]> _cultureSpeeds;
        private static Dictionary<string, string> _cultureNames;
        private static string _speedModel;
        private static PerkValues _perkValues;

        /// <summary>A new campaign: the marching rules are read again.</summary>
        public static void ResetRules() => _rulesFor = null;

        /// <summary>
        /// The marching rules in force, read once a load: the speed model's name; each people's march bonuses (TAOM's feats as the
        /// game holds them: a feat whose id ends in _forest_speed, _snow_speed, _desert_speed, _plain_speed, _swamp_speed,
        /// _steppe_speed or _night_speed, with its own EffectBonus), only when the model in force is TAOM's, which is what adds
        /// them; and the scout perks' values.
        /// </summary>
        private static void MarchRules(Snapshot snap)
        {
            string key = snap.Campaign + "|" + Bridge.LoadId;
            if (_rulesFor != key)
            {
                _rulesFor = key;
                _speedModel = Campaign.Current.Models.PartySpeedCalculatingModel?.GetType().FullName;
                _cultureSpeeds = null;
                try
                {
                    if (_speedModel != null && _speedModel.IndexOf("Taom", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var table = new Dictionary<string, float[]>();
                        var names = new Dictionary<string, string>();
                        foreach (CultureObject c in TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObjectTypeList<CultureObject>())
                        {
                            if (c == null) continue;
                            float[] f = null;
                            foreach (FeatObject feat in c.GetCulturalFeats())
                            {
                                int kind = FeatKind(feat?.StringId);
                                if (kind <= 0) continue;
                                if (f == null) f = new float[GamePace.Kinds];
                                f[kind] += feat.EffectBonus;
                            }
                            if (f != null) { table[c.StringId] = f; names[c.StringId] = c.Name?.ToString() ?? c.StringId; }
                        }
                        _cultureSpeeds = table;
                        _cultureNames = names;
                    }
                    _perkValues = new PerkValues
                    {
                        ForestKin = DefaultPerks.Scouting.ForestKin.PrimaryBonus, Pathfinder = DefaultPerks.Scouting.Pathfinder.PrimaryBonus,
                        DesertBorn = DefaultPerks.Scouting.DesertBorn.PrimaryBonus, NightRunner = DefaultPerks.Scouting.NightRunner.PrimaryBonus,
                        DayTraveler = DefaultPerks.Scouting.DayTraveler.PrimaryBonus
                    };
                    Diag.Event("march", "rules", new { model = _speedModel, peoples = _cultureSpeeds?.Count ?? 0, bonuses = _cultureSpeeds?.ToDictionary(kv => kv.Key, kv => string.Join(" ", kv.Value.Select(v => v.ToString("0.##")))) });
                }
                catch (Exception e) { Diag.Swallowed("reading the marching rules in force", e); }
            }
            snap.SpeedModel = _speedModel;
            snap.CultureSpeeds = _cultureSpeeds;
            snap.CultureNames = _cultureNames;
            snap.PerkValues = _perkValues;
        }

        private static int FeatKind(string id)
        {
            if (id == null) return 0;
            if (id.EndsWith("_forest_speed", StringComparison.Ordinal)) return GamePace.KindForest;
            if (id.EndsWith("_snow_speed", StringComparison.Ordinal)) return GamePace.KindSnow;
            if (id.EndsWith("_desert_speed", StringComparison.Ordinal)) return GamePace.KindDesert;
            if (id.EndsWith("_plain_speed", StringComparison.Ordinal)) return GamePace.KindPlain;
            if (id.EndsWith("_swamp_speed", StringComparison.Ordinal)) return GamePace.KindSwamp;
            if (id.EndsWith("_steppe_speed", StringComparison.Ordinal)) return GamePace.KindSteppe;
            if (id.EndsWith("_night_speed", StringComparison.Ordinal)) return GamePace.KindNight;
            return 0;
        }

        // ---------- sieges and battles (0.7.3, Forecast) ----------

        private static readonly SiegeEngineType[] Buildable = { DefaultSiegeEngineTypes.Ram, DefaultSiegeEngineTypes.SiegeTower, DefaultSiegeEngineTypes.Ballista, DefaultSiegeEngineTypes.Onager, DefaultSiegeEngineTypes.Trebuchet };

        /// <summary>
        /// Every siege on the map as the game runs it: the camp's preparations and engines, each engine type's building rate by
        /// the model in force (GetConstructionProgressPerHour: the camp's men, its engineer, the pack's mods), the numbers of
        /// the game's storm test (StartingAssaultOnBesiegedSettlementIsLogical: the defenders inside it counts, the besiegers,
        /// the walls' worth now, the engines built, the most advanced unfinished one), and the place's food.
        /// </summary>
        private static void Sieges(Snapshot snap, List<Settlement> besieged)
        {
            if (besieged.Count == 0) return;
            SiegeEventModel sm = Campaign.Current.Models.SiegeEventModel;
            CombatSimulationModel cm = Campaign.Current.Models.CombatSimulationModel;
            foreach (Settlement s in besieged)
            {
                try
                {
                    SiegeEvent ev = s.SiegeEvent;
                    BesiegerCamp camp = ev?.BesiegerCamp;
                    if (camp == null || s.Town == null) continue;
                    var g = new SiegeRow
                    {
                        Settlement = s.StringId, Besieger = camp.LeaderParty?.StringId, BesiegerFaction = camp.MapFaction?.StringId,
                        Strategy = camp.SiegeStrategy?.StringId, StartHours = Math.Round(ev.SiegeStartTime.ToHours, 2), WallLevel = s.Town.GetWallLevel()
                    };
                    SiegeEvent.SiegeEnginesContainer engines = camp.SiegeEngines;
                    if (engines != null)
                    {
                        g.Prep = (float)Math.Round(engines.SiegePreparations?.Progress ?? 0f, 4);
                        g.PrepRate = sm.GetConstructionProgressPerHour(DefaultSiegeEngineTypes.Preparations, ev, camp);
                        foreach (SiegeEvent.SiegeEngineConstructionProgress e in engines.DeployedMeleeSiegeEngines.Concat(engines.DeployedRangedSiegeEngines))
                            if (e?.SiegeEngine != null) g.Engines.Add(new EngineRow { Type = e.SiegeEngine.StringId, Progress = (float)Math.Round(e.Progress, 4), Hitpoints = (float)Math.Round(e.Hitpoints) });
                        foreach (SiegeEvent.SiegeEngineConstructionProgress e in engines.ReservedSiegeEngines)
                            if (e?.SiegeEngine != null) g.Engines.Add(new EngineRow { Type = e.SiegeEngine.StringId, Progress = (float)Math.Round(e.Progress, 4), Hitpoints = (float)Math.Round(e.Hitpoints), Reserve = true });
                    }
                    foreach (SiegeEngineType t in Buildable) g.Rates[t.StringId] = (float)Math.Round(sm.GetConstructionProgressPerHour(t, ev, camp), 5);
                    MBReadOnlyList<float> walls = s.SettlementWallSectionHitPointsRatioList;
                    if (walls != null && walls.Count > 0) { g.WallShare = (float)Math.Round(walls.Average(), 3); g.WallsDown = s.SettlementTotalWallHitPoints < 1e-5f; }
                    g.Advantage = (float)Math.Round(cm.GetSettlementAdvantage(s), 4);
                    g.Built = cm.GetNumberOfEquipmentsBuilt(s);
                    g.MaxProgress = (float)Math.Round(cm.GetMaximumSiegeEquipmentProgress(s), 4);
                    // the storm test's defenders: the parties inside it counts (lords not shy of a fight, the militia, the garrison)
                    bool playerInside = MobileParty.MainParty?.CurrentSettlement == s;
                    float d = 0, a = 0;
                    int dMen = 0, aMen = 0;
                    double aMorale = 0;
                    foreach (PartyBase x in s.GetInvolvedPartiesForEventType())
                        if (x.IsMobile && x.MobileParty.CurrentSettlement == s && (x.MobileParty.Aggressiveness > 0.01f || x.MobileParty.IsMilitia || x.MobileParty.IsGarrison))
                        {
                            d += (playerInside ? 0.5f : 1f) * x.CalculateCurrentStrength();
                            dMen += x.NumberOfHealthyMembers;
                        }
                    foreach (PartyBase x in camp.GetInvolvedPartiesForEventType())
                    {
                        a += x.CalculateCurrentStrength();
                        aMen += x.NumberOfHealthyMembers;
                        if (x.IsMobile) aMorale += x.MobileParty.Morale * x.NumberOfHealthyMembers;
                    }
                    g.AttackerMen = aMen;
                    g.DefenderMen = dMen;
                    g.AttackerMorale = aMen > 0 ? (float)Math.Round(aMorale / aMen, 1) : 50f;
                    g.Defenders = (float)Math.Round(d, 1);
                    g.Attackers = (float)Math.Round(a, 1);
                    g.PlayerInside = playerInside;
                    PartyBase garrison = s.Town.GarrisonParty?.Party;
                    if (garrison != null)
                    {
                        g.GarrisonPower = (float)Math.Round((playerInside ? 0.5f : 1f) * garrison.CalculateCurrentStrength(), 1);
                        g.GarrisonRegulars = garrison.MemberRoster.TotalRegulars;
                        g.GarrisonWounded = garrison.MemberRoster.TotalWoundedRegulars;
                    }
                    g.Food = (float)Math.Round(s.Town.FoodStocks, 1);
                    g.FoodChange = (float)Math.Round(s.Town.FoodChange, 2);
                    g.FoodCap = s.Town.FoodStocksUpperLimit();
                    g.Starving = s.Town.FoodStocks <= 0;
                    Army army = camp.LeaderParty?.Army;
                    if (army != null) { g.Cohesion = (float)Math.Round(army.Cohesion, 2); g.CohesionPerDay = (float)Math.Round(army.DailyCohesionChange, 2); }
                    g.Storming = s.Party?.MapEvent != null && s.Party.MapEvent.IsSiegeAssault;
                    snap.Sieges.Add(g);
                }
                catch (Exception e) { Diag.Swallowed("copying a siege's clock", e); }
            }
        }

        /// <summary>
        /// Every battle with a lord or a holding in it: its kind, place and start, and each side's healthy men, losses, mean troop
        /// power by the model in force (MilitaryPowerModel.GetTroopPower, with the side's leader's modifier and the battle's
        /// context), battle advantage (GetBattleAdvantage) and morale weighted by men.
        /// </summary>
        private static void Battles(Snapshot snap)
        {
            MBReadOnlyList<MapEvent> all = Campaign.Current.MapEventManager?.MapEvents;
            if (all == null) return;
            CombatSimulationModel cm = Campaign.Current.Models.CombatSimulationModel;
            // a troop's power for a side and a battle's context, asked of the model once an hour (0.7.4: a cost trim; parties share troop types)
            var power = new Dictionary<(CharacterObject, BattleSideEnum, int, float), float>();
            foreach (MapEvent me in all)
            {
                try
                {
                    if (me == null || me.IsFinalized) continue;
                    bool lords = me.InvolvedParties.Any(x => x.IsMobile && x.MobileParty.IsLordParty);
                    Settlement at = me.MapEventSettlement;
                    if (!lords && (at == null || !at.IsFortification)) continue;
                    var b = new BattleRow
                    {
                        Kind = me.IsSiegeAssault ? "siege" : me.IsSallyOut ? "sally" : me.IsSiegeOutside ? "siege_outside" : me.IsRaid ? "raid" : me.IsFieldBattle ? "field" : me.EventType.ToString().ToLowerInvariant(),
                        Settlement = at?.StringId, Town = at != null && at.IsTown, X = Text.R1(me.Position.X), Y = Text.R1(me.Position.Y),
                        StartHours = Math.Round(me.BattleStartTime.ToHours, 2), Retreating = me.RetreatingSide == BattleSideEnum.None ? null : me.RetreatingSide.ToString().ToLowerInvariant()
                    };
                    b.Key = (me.AttackerSide.LeaderParty?.Id ?? "?") + "@" + b.StartHours.ToString("0.00", Text.Inv);
                    if (at != null && at.IsFortification) b.SettlementAdvantage = (float)Math.Round(cm.GetSettlementAdvantage(at), 4);
                    cm.GetBattleAdvantage(me, out ExplainedNumber defAdv, out ExplainedNumber attAdv);
                    Side(me, me.AttackerSide, BattleSideEnum.Attacker, b.Attackers, attAdv.ResultNumber, power);
                    Side(me, me.DefenderSide, BattleSideEnum.Defender, b.Defenders, defAdv.ResultNumber, power);
                    snap.Battles.Add(b);
                }
                catch (Exception e) { Diag.Swallowed("copying a battle's clock", e); }
            }
        }

        private static void Side(MapEvent me, MapEventSide side, BattleSideEnum which, BattleSideRow row, float advantage, Dictionary<(CharacterObject, BattleSideEnum, int, float), float> cache)
        {
            MilitaryPowerModel pm = Campaign.Current.Models.MilitaryPowerModel;
            PartyBase leader = side.LeaderParty;
            row.Leader = leader?.MobileParty?.StringId ?? leader?.Settlement?.StringId;
            row.Faction = side.MapFaction?.StringId;
            row.SettlementLed = leader != null && !leader.IsMobile;
            row.Advantage = (float)Math.Round(advantage, 4);
            row.Casualties = side.TroopCasualties;
            row.StartHealthy = side.HealthyTroopCountAtMapEventStart;
            float modifier = leader?.LeaderHero != null ? pm.GetPowerModifierOfHero(leader.LeaderHero) : 0f;
            double power = 0, moraleSum = 0;
            int healthy = 0, wounded = 0, moraleMen = 0;
            foreach (MapEventParty mp in side.Parties)
            {
                PartyBase pb = mp.Party;
                if (pb == null) continue;
                if (pb.IsMobile) row.Parties.Add(pb.MobileParty.StringId);
                int partyHealthy = 0;
                foreach (TroopRosterElement el in pb.MemberRoster.GetTroopRoster())
                {
                    int n = el.Number - el.WoundedNumber;
                    if (n <= 0 || el.Character == null) continue;
                    var key = (el.Character, which, (int)me.SimulationContext, modifier);
                    if (!cache.TryGetValue(key, out float tp)) cache[key] = tp = pm.GetTroopPower(el.Character, which, me.SimulationContext, modifier);
                    power += n * tp;
                    partyHealthy += n;
                    wounded += el.WoundedNumber;
                }
                healthy += partyHealthy;
                if (pb.IsMobile && partyHealthy > 0) { moraleSum += pb.MobileParty.Morale * partyHealthy; moraleMen += partyHealthy; }
            }
            row.Healthy = healthy;
            row.Wounded = wounded;
            row.Power = healthy > 0 ? (float)Math.Round(power / healthy, 4) : 1f;
            row.Morale = moraleMen > 0 ? (float)Math.Round(moraleSum / moraleMen, 1) : 50f;
        }

        // the game keeps whether a party is walking its planned way, and its speed with the ground counted, in fields of its own
        private static readonly AccessTools.FieldRef<MobileParty, bool> PathModeField = Field<bool>("_pathMode");
        private static readonly AccessTools.FieldRef<MobileParty, float> FinalSpeedField = Field<float>("_lastCalculatedSpeed");
        /// <summary>The whole of the game's planned way (a NavigationPath holds 128 corners), so a march's arrival is reckoned to its end.</summary>
        public const int MostRoutePoints = 128;

        private static AccessTools.FieldRef<MobileParty, T> Field<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<MobileParty, T>(name); }
            catch (Exception e) { Diag.Swallowed("finding MobileParty." + name, e); return null; }
        }

        /// <summary>Both of the game's fields the dashboard reads were found (the self-check).</summary>
        public static string WayFieldsProblem => PathModeField == null || FinalSpeedField == null
            ? "MobileParty." + (PathModeField == null ? "_pathMode" : "_lastCalculatedSpeed") + " was not found, so the dashboard shows no planned ways"
            : BaseSpeedField == null ? "MobileParty._lastCalculatedBaseSpeedExplained was not found, so march times use the speed number alone (0.7.3)" : null;

        /// <summary>
        /// Where a party walks to, and, for a lord or caravan on the march, the corners still ahead of the way the game planned
        /// for it (MobileParty.Path from PathBegin, which it walks corner by corner while _pathMode is on). Plain copies only.
        /// </summary>
        private static void Way(MobileParty p, PartyRow r, bool marching)
        {
            CampaignVec2 to = p.MoveTargetPoint;
            r.Tx = Text.R1(to.X);
            r.Ty = Text.R1(to.Y);
            if (p.IsLordParty && FinalSpeedField != null) r.FinalSpeed = (float)Math.Round(FinalSpeedField(p), 4);
            if (!marching || !(p.IsLordParty || p.IsCaravan)) return;
            float dx = to.X - p.Position.X, dy = to.Y - p.Position.Y;
            if (dx * dx + dy * dy < 0.25f) return;
            NavigationPath path = p.Path;
            int begin = p.PathBegin;
            bool walking = PathModeField != null ? PathModeField(p) : path != null && path.Size > begin;
            if (!walking || path == null || path.Size <= begin || path.PathPoints == null) { r.Straight = true; return; }
            int n = Math.Min(path.Size - begin, MostRoutePoints);
            var pts = new float[n * 2];
            for (int k = 0; k < n; k++)
            {
                Vec2 v = path.PathPoints[begin + k];
                pts[2 * k] = Text.R1(v.x);
                pts[2 * k + 1] = Text.R1(v.y);
            }
            r.Route = pts;
        }

        public static string DateText()
        {
            try { return CampaignTime.Now.ToString(); }
            catch (Exception e) { Diag.Swallowed("the game's date text", e); return ""; }
        }
    }
}
