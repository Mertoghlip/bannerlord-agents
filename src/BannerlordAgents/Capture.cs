using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
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

        public static Snapshot Take(Kingdom watched, bool withVision)
        {
            long t0 = Perf.Now();
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
                MapCrc = MapExport.Crc
            };
            var factions = new Dictionary<string, IFaction>();

            foreach (Kingdom k in Kingdom.All)
            {
                if (k == null || k.IsEliminated) continue;
                var row = new FactionRow { Id = k.StringId, Name = Names.Faction(k), Ruler = Names.Hero(k.Leader), Color = k.Color, Color2 = k.Color2 };
                foreach (Kingdom o in Kingdom.All)
                    if (o != null && o != k && !o.IsEliminated && k.IsAtWarWith(o)) row.AtWar.Add(o.StringId);
                foreach (Kingdom a in k.AlliedKingdoms)
                    if (a != null) row.Allies.Add(a.StringId);
                snap.Factions[k.StringId] = row;
                factions[k.StringId] = k;
            }
            long t1 = Perf.Now();
            var allies = new HashSet<Kingdom>();
            if (watched != null) foreach (Kingdom a in watched.AlliedKingdoms) if (a != null) allies.Add(a);

            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s.IsHideout) continue;
                IFaction f = s.MapFaction;
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
                    if (watched != null && (f == watched || (f is Kingdom fk && allies.Contains(fk))))
                    {
                        if (g != null) r.GarrisonStrength = (float)Math.Round(g.EstimatedStrength);
                        MobileParty militia = s.MilitiaPartyComponent?.MobileParty;
                        if (militia != null && militia.IsActive) r.MilitiaStrength = (float)Math.Round(militia.Party.EstimatedStrength);
                        r.WallLevel = s.Town.GetWallLevel();
                    }
                }
                if (s.Village != null)
                {
                    r.VillageState = s.Village.VillageState.ToString();
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
                PartyRow r = Row(p, watched != null && p.MapFaction == watched);
                live.Add(p);
                snap.Parties.Add(r);
                snap.PartyById[r.Id] = r;
                IFaction f = p.MapFaction;
                if (f != null && !factions.ContainsKey(f.StringId)) factions[f.StringId] = f;
            }
            long t3 = Perf.Now();

            foreach (KeyValuePair<string, IFaction> kv in factions)
            {
                snap.FactionNames[kv.Key] = Names.Faction(kv.Value);
                snap.FactionColors[kv.Key] = kv.Value.Color;
                if (watched != null && kv.Value != watched && FactionManager.IsAtWarAgainstFaction(kv.Value, watched)) snap.HostileToWatched.Add(kv.Key);
                if (player != null && kv.Value != player && FactionManager.IsAtWarAgainstFaction(kv.Value, player)) snap.HostileToPlayer.Add(kv.Key);
            }

            if (watched != null) Realm(watched, snap);
            snap.Roads = RoadsFor(watched, player);
            long t4 = Perf.Now();
            if (withVision && watched != null) Vision.FactionSees(watched, live, snap);
            long t5 = Perf.Now();

            // Names only where someone may read them: lords, the two realms' own parties, and anything seen.
            for (int i = 0; i < live.Count; i++)
            {
                PartyRow r = snap.Parties[i];
                if (r.Kind == "lord" || r.SeenByPlayer || r.Faction == snap.Watched || r.Faction == snap.PlayerFaction
                    || (snap.WatchedSees != null && snap.WatchedSees.ContainsKey(r.Id)))
                    r.Name = Names.Party(live[i]);
            }
            long t6 = Perf.Now();
            snap.PartsMs = new[] { Ms(t0, t1), Ms(t1, t2), Ms(t2, t3), Ms(t3, t4), Ms(t4, t5), Ms(t5, t6) };
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
        public static RoadBook RoadsFor(IFaction watched, IFaction player)
        {
            try
            {
                MapDistanceModel model = Campaign.Current.Models.MapDistanceModel;
                List<Settlement> all = Settlement.All.Where(x => x != null && !x.IsHideout).ToList();
                var need = new List<Settlement>();
                foreach (Settlement s in all)
                    if ((watched != null && s.MapFaction == watched) || (player != null && s.MapFaction == player))
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
        private static void Realm(Kingdom watched, Snapshot snap)
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
                snap.Captives.Add(c);
            }
            foreach (Clan cl in watched.Clans)
            {
                if (cl == null || cl.IsEliminated) continue;
                bool ruler = cl == watched.RulingClan;
                snap.Clans.Add(new ClanRow
                {
                    Id = cl.StringId, Name = Names.Faction(cl), Leader = cl.Leader?.StringId, Influence = (float)Math.Round(cl.Influence),
                    Gold = ruler ? cl.Gold : -1, Ruler = ruler
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
            if (Settings.Dashboard) Way(p, r, inside == null && me == null && !r.Attached);
            return r;
        }

        // the game keeps whether a party is walking its planned way, and its speed with the ground counted, in fields of its own
        private static readonly AccessTools.FieldRef<MobileParty, bool> PathModeField = Field<bool>("_pathMode");
        private static readonly AccessTools.FieldRef<MobileParty, float> FinalSpeedField = Field<float>("_lastCalculatedSpeed");
        public const int MostRoutePoints = 48;

        private static AccessTools.FieldRef<MobileParty, T> Field<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<MobileParty, T>(name); }
            catch (Exception e) { Diag.Swallowed("finding MobileParty." + name, e); return null; }
        }

        /// <summary>Both of the game's fields the dashboard reads were found (the self-check).</summary>
        public static string WayFieldsProblem => PathModeField == null || FinalSpeedField == null
            ? "MobileParty." + (PathModeField == null ? "_pathMode" : "_lastCalculatedSpeed") + " was not found, so the dashboard shows no planned ways" : null;

        /// <summary>
        /// Where a party walks to, and, for a lord or caravan on the march, the corners still ahead of the way the game planned
        /// for it (MobileParty.Path from PathBegin, which it walks corner by corner while _pathMode is on). Plain copies only.
        /// </summary>
        private static void Way(MobileParty p, PartyRow r, bool marching)
        {
            CampaignVec2 to = p.MoveTargetPoint;
            r.Tx = Text.R1(to.X);
            r.Ty = Text.R1(to.Y);
            if (p.IsLordParty && FinalSpeedField != null) r.FinalSpeed = (float)Math.Round(FinalSpeedField(p), 2);
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
