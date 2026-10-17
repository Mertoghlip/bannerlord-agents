using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAgents.Tools
{
    /// <summary>
    /// The world as one realm knows it, for its tools: built from the hourly copy and the realm's own sight, never from
    /// the game itself, so a tool sees exactly what the agent could see (the fog rule). Enemies are hostile lords and
    /// armies in view; one seen earlier is known from where it was last seen. Game-free, worker thread.
    /// </summary>
    internal sealed class World
    {
        public readonly Snapshot S;
        public readonly string Realm;
        public readonly double Hours;
        private readonly Dictionary<string, Sighting> _sees;
        private readonly Dictionary<string, ViewParty> _lastSeen;
        private readonly HashSet<string> _hostile, _allies;
        private readonly List<PartyRow> _enemies, _bandits;

        /// <summary>An enemy army out of sight is still counted as a threat where it was last seen, for this many hours.</summary>
        public const double RecentHours = 6;

        /// <summary>Map units a party covers in an hour when its speed is unknown.</summary>
        public const float DefaultSpeed = 4f;

        public World(Snapshot s, string realm, Dictionary<string, Sighting> sees, Dictionary<string, ViewParty> lastSeen)
        {
            S = s;
            Realm = realm;
            Hours = s.Hours;
            _sees = sees ?? new Dictionary<string, Sighting>();
            _lastSeen = lastSeen ?? new Dictionary<string, ViewParty>();
            _hostile = realm == s.Watched ? s.HostileToWatched : realm == s.PlayerFaction ? s.HostileToPlayer : new HashSet<string>();
            _allies = s.Factions.TryGetValue(realm ?? "", out FactionRow fr) ? new HashSet<string>(fr.Allies) : new HashSet<string>();
            _enemies = new List<PartyRow>();
            _bandits = new List<PartyRow>();
            foreach (KeyValuePair<string, Sighting> kv in _sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r) || r.Attached) continue;
                if (r.Kind == "lord" && _hostile.Contains(r.Faction ?? "")) _enemies.Add(r);
                else if (r.Kind == "bandit") _bandits.Add(r);
            }
        }

        public PartyRow Party(string id) => id != null && S.PartyById.TryGetValue(id, out PartyRow r) ? r : null;
        public SettlementRow Holding(string id) => id != null && S.SettlementById.TryGetValue(id, out SettlementRow h) ? h : null;

        public bool Own(PartyRow r) => r != null && r.Faction == Realm;
        public bool Visible(PartyRow r) => r != null && (Own(r) || _sees.ContainsKey(r.Id));
        public bool HostileFaction(string f) => f != null && _hostile.Contains(f);
        public bool Ally(string f) => f != null && _allies.Contains(f);

        public static bool IsFort(SettlementRow h) => h != null && (h.Kind == "town" || h.Kind == "castle");

        /// <summary>A party's fighting strength: its whole army's when it leads one.</summary>
        public static double StrengthOf(PartyRow r) => r == null ? 0 : r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength;
        public static int MenOf(PartyRow r) => r == null ? 0 : r.ArmyMen > 0 ? r.ArmyMen : r.Men;

        /// <summary>Where something is: a holding, a party in view or of ours, or where an enemy was last seen.</summary>
        public bool Where(string id, out float x, out float y)
        {
            x = y = 0;
            if (id == null) return false;
            if (Spots.TryParse(id, out x, out y)) return true;
            SettlementRow h = Holding(id);
            if (h != null) { x = h.X; y = h.Y; return true; }
            PartyRow r = Party(id);
            if (r != null && Visible(r)) { x = r.X; y = r.Y; return true; }
            if (_lastSeen.TryGetValue(id, out ViewParty v)) { x = v.X; y = v.Y; return true; }
            return false;
        }

        public double Distance(string a, string b)
        {
            if (!Where(a, out float ax, out float ay) || !Where(b, out float bx, out float by)) return double.NaN;
            return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        }

        /// <summary>
        /// The distance by road between two things the realm knows of, as the game reckons a march (Roads): holdings by the
        /// game's own table, a party through the entrance of the ground it stands on (an enemy seen earlier, through where it
        /// was seen). NaN when the game's table lacks the way, or for a spot (no entrance known).
        /// </summary>
        public double RoadDistance(string a, string b)
        {
            if (!Locate(a, out float ax, out float ay, out string ea, out SettlementRow ha) || !Locate(b, out float bx, out float by, out string eb, out SettlementRow hb)) return double.NaN;
            if (ha != null && hb != null) return S.Roads != null && S.Roads.TryGet(ha.Id, hb.Id, out float d) ? d : double.NaN;
            if (hb != null) return Roads.To(S, ax, ay, ea, null, hb);
            if (ha != null) return Roads.To(S, bx, by, eb, null, ha);
            return Roads.Between(S, ax, ay, ea, bx, by, eb);
        }

        /// <summary>What a party is doing, as the realm knows it: in view (or its own) now, else as last seen; null if never seen.</summary>
        public string DoingOf(string id)
        {
            PartyRow r = Lead(Party(id));
            if (r != null && Visible(r)) return Views.Doing(S, r);
            return id != null && _lastSeen.TryGetValue(r?.Id ?? id, out ViewParty v) ? v.Doing : null;
        }

        /// <summary>The holding a party is heading for (going to, besieging, raiding...), as the realm knows it; null for none.</summary>
        public string TowardOf(string id)
        {
            PartyRow r = Lead(Party(id));
            if (r != null && Visible(r)) return Views.Toward(r);
            return id != null && _lastSeen.TryGetValue(r?.Id ?? id, out ViewParty v) ? v.Toward : null;
        }

        /// <summary>Where a party is, in words, with the realm's nearest holding: "12 (14 by road) from Caradhras; 63 (260 by road) from your Western Moria".</summary>
        public string WhereOf(string id)
        {
            PartyRow r = Lead(Party(id));
            ViewParty v = null;
            if (r != null && Visible(r)) v = Views.Party(S, r, null, "enemy", S.Settlements.Where(h => h.Faction == Realm).ToList());
            else if (id != null) _lastSeen.TryGetValue(r?.Id ?? id, out v);
            if (v == null) return "where not known";
            string near = v.Near == null ? "in the wilds" : v.NearDistance < 1f ? "at " + v.Near : Roads.Both(v.NearDistance, v.NearRoad) + " from " + v.Near;
            return near + (v.NearestHolding != null && v.NearestHolding != v.Near ? "; " + Roads.Both(v.HoldingDistance, v.HoldingRoad) + " from your " + v.NearestHolding : "");
        }

        /// <summary>Where something is and the entrance its road distances count from (a holding: itself, at its gate).</summary>
        private bool Locate(string id, out float x, out float y, out string entrance, out SettlementRow holding)
        {
            x = y = 0;
            entrance = null;
            holding = Holding(id);
            if (holding != null) { x = holding.Gx != 0 || holding.Gy != 0 ? holding.Gx : holding.X; y = holding.Gx != 0 || holding.Gy != 0 ? holding.Gy : holding.Y; entrance = holding.Id; return true; }
            if (id == null || Spots.TryParse(id, out x, out y)) return id != null;
            PartyRow r = Party(id);
            if (r != null && Visible(r)) { x = r.X; y = r.Y; entrance = r.InSettlement ?? r.Entrance; return true; }
            if (_lastSeen.TryGetValue(id, out ViewParty v)) { x = v.X; y = v.Y; entrance = v.Entrance; return true; }
            return false;
        }

        /// <summary>The game's speed number for a party (its base: open ground, by day), as its tooltip shows it there.</summary>
        public static float Speed(PartyRow r) => r != null && r.Speed > 0.2f ? r.Speed : DefaultSpeed;

        /// <summary>
        /// Map units the party covers in an hour over a whole day on open ground, night counted, by the game's own rules
        /// (GamePace: its speed sum, / 1.2): for marches known only by their length (0.7.3; before, distance over the speed
        /// number, some 20% too quick).
        /// </summary>
        public double PerHour(PartyRow r)
        {
            float u = Forecast.DayPerHour(S, r);
            return u > 0.05f ? u : Speed(r) * Common.GamePace.UnitsPerSpeedHour;
        }

        public bool InView(string id)
        {
            PartyRow r = Party(id);
            return r != null && Visible(r);
        }

        public double SeenAgo(string id)
        {
            if (InView(id)) return 0;
            return id != null && _lastSeen.TryGetValue(id, out ViewParty v) ? Hours - v.SeenHours : 9999;
        }

        /// <summary>A party no longer on the map: beaten, captured or broken up. A party merely out of sight is not gone.</summary>
        public bool Gone(string id)
        {
            if (id == null) return true;
            if (Holding(id) != null) return false;
            return Party(id) == null;
        }

        /// <summary>A party marching inside an army stands for its army: its leader's party.</summary>
        public PartyRow Lead(PartyRow r) => r != null && r.Attached && r.ArmyLeader != null && S.PartyById.TryGetValue(r.ArmyLeader, out PartyRow l) ? l : r;

        /// <summary>The strength the realm knows of (the whole army's, for a party in one): in view or ours now, else as last seen; none if never seen.</summary>
        public double? KnownStrength(string id)
        {
            PartyRow r = Lead(Party(id));
            if (r != null && Visible(r)) return StrengthOf(r);
            if (r != null && _lastSeen.TryGetValue(r.Id, out ViewParty lv)) return lv.Strength;
            if (id != null && _lastSeen.TryGetValue(id, out ViewParty v)) return v.Strength;
            return null;
        }

        public int? KnownMen(string id)
        {
            PartyRow r = Party(id);
            if (r != null && Visible(r)) return MenOf(r);
            if (id != null && _lastSeen.TryGetValue(id, out ViewParty v)) return v.ArmyMen > 0 ? v.ArmyMen : v.Men;
            return null;
        }

        /// <summary>Hostile lords and armies in view within r of a place, nearest first.</summary>
        public List<PartyRow> EnemiesNear(string place, double r, string except = null)
        {
            var list = new List<PartyRow>();
            if (!Where(place, out float px, out float py)) return list;
            foreach (PartyRow e in _enemies)
            {
                if (e.Id == except) continue;
                double d = Math.Sqrt((e.X - px) * (e.X - px) + (e.Y - py) * (e.Y - py));
                if (d <= r) list.Add(e);
            }
            return list.OrderBy(e => (e.X - px) * (e.X - px) + (e.Y - py) * (e.Y - py)).ToList();
        }

        /// <summary>Everything in view within r of a place that could attack a lord: hostile lords and armies, and bandits. Nearest first.</summary>
        public List<PartyRow> ThreatsInView(string place, double r)
        {
            List<PartyRow> list = EnemiesNear(place, r);
            if (!Where(place, out float px, out float py)) return list;
            foreach (PartyRow b in _bandits)
                if (Math.Sqrt((b.X - px) * (b.X - px) + (b.Y - py) * (b.Y - py)) <= r) list.Add(b);
            return list.OrderBy(e => (e.X - px) * (e.X - px) + (e.Y - py) * (e.Y - py)).ToList();
        }

        /// <summary>
        /// The strength that threatens a place, as the realm can judge it: hostile lords, armies and bandits in view within r,
        /// and hostile lords and armies seen there within the last RecentHours, at the place they were last seen (an army
        /// that walked out of sight an hour ago has not gone away). A party known gone is not counted.
        /// </summary>
        public double ThreatStrength(string place, double r, string except = null) => Threat(place, r, except, true);

        /// <summary>The same without bandits: hostile lords and armies only, the forces that make a lord needed where it is.</summary>
        public double EnemyThreat(string place, double r) => Threat(place, r, null, false);

        private double Threat(string place, double r, string except, bool bandits)
        {
            if (!Where(place, out float px, out float py)) return 0;
            double sum = (bandits ? ThreatsInView(place, r) : EnemiesNear(place, r)).Where(e => e.Id != except).Sum(StrengthOf);
            foreach (KeyValuePair<string, ViewParty> kv in _lastSeen)
            {
                ViewParty v = kv.Value;
                if (kv.Key == except || v.Relation != "enemy" || Hours - v.SeenHours > RecentHours || InView(kv.Key) || Gone(kv.Key)) continue;
                if (Math.Sqrt((v.X - px) * (v.X - px) + (v.Y - py) * (v.Y - py)) <= r) sum += v.Strength;
            }
            return sum;
        }

        /// <summary>One enemy lord or army counted as coming to a place's relief.</summary>
        internal sealed class Comer
        {
            public string Id, Name, Doing;
            public double Strength, Distance, Road = double.NaN, SeenAgo;
            public bool Heading;
        }

        /// <summary>
        /// The enemy coming to a place's relief, as the realm can judge it: hostile lords and armies in view within r of it,
        /// or heading for it from anywhere (going to it, defending it, besieging it), and those seen so within the last
        /// RecentHours, where they were last seen. Each counted once, an army as a whole; nearest first. Lords are counted
        /// one by one, not only armies: the game sends a besieged holding's realm's lords to it one at a time (Oct 16,
        /// Hollin Gate: about 50 of Arnor's lords turned toward it within six hours of the siege, none in an army, so a
        /// relief alarm that waited for an army never rang). A party known gone is not counted.
        /// </summary>
        public List<Comer> ReliefComing(string place, double r)
        {
            var list = new List<Comer>();
            if (!Where(place, out float px, out float py)) return list;
            double Dist(float x, float y) => Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
            foreach (PartyRow e in _enemies)
            {
                double d = Dist(e.X, e.Y);
                bool heading = Views.Toward(e) == place;
                if (d > r && !heading) continue;
                list.Add(new Comer { Id = e.Id, Name = e.Name ?? e.Id, Doing = Views.Doing(S, e), Strength = StrengthOf(e), Distance = d, Heading = heading });
            }
            foreach (KeyValuePair<string, ViewParty> kv in _lastSeen)
            {
                ViewParty v = kv.Value;
                if (v.Relation != "enemy" || v.Kind != "lord" || Hours - v.SeenHours > RecentHours || InView(kv.Key) || Gone(kv.Key)) continue;
                PartyRow now = Party(kv.Key);
                if (now != null && now.Attached) continue;
                double d = Dist(v.X, v.Y);
                bool heading = v.Toward == place;
                if (d > r && !heading) continue;
                list.Add(new Comer { Id = kv.Key, Name = v.Name ?? kv.Key, Doing = v.Doing, Strength = v.Strength, Distance = d, Heading = heading, SeenAgo = Hours - v.SeenHours });
            }
            foreach (Comer c in list) c.Road = RoadDistance(c.Id, place);
            return list.OrderBy(c => double.IsNaN(c.Road) ? c.Distance : c.Road).ToList();
        }

        /// <summary>The coming relief in words, nearest first: "Daeron, strength 855, 3.5 (4.3 by road) away, defending Hollin Gate; ... and 12 more (strength 4,100)".</summary>
        public string ReliefText(string place, double r, int most = 6)
        {
            List<Comer> list = ReliefComing(place, r);
            if (list.Count == 0) return "none in sight";
            IEnumerable<string> lines = list.Take(most).Select(c => c.Name + ", strength " + Text.N((int)Math.Round(c.Strength)) + ", "
                + Roads.Both((float)c.Distance, (float)c.Road) + " away, " + (c.Doing ?? "doing what is not known")
                + (c.SeenAgo > 0 ? " (seen " + Math.Round(c.SeenAgo) + " hours ago)" : ""));
            string text = string.Join("; ", lines);
            if (list.Count > most)
                text += "; and " + (list.Count - most) + " more (strength " + Text.N((int)Math.Round(list.Skip(most).Sum(c => c.Strength))) + ")";
            return text;
        }

        public bool Besieged(SettlementRow h) => h != null && h.BesiegedBy != null;

        // ---------- would a place hold? ----------

        /// <summary>How far around a place the forces threatening it are counted, in map units.</summary>
        public const double HoldRadius = 20;

        /// <summary>
        /// What a castle's or town's walls are worth to its defenders, by the game's own test for whether besiegers should
        /// assault (BesiegerCamp.StartingAssaultOnBesiegedSettlementIsLogical, 1.4.8): they assault only when stronger than
        /// the defenders times the settlement's advantage to the power 0.8, the advantage being (4 + wall level) over the
        /// worth of their engines. Taken here with a ram and two towers built, the usual siege: 2.4 for walls of level 1,
        /// 2.8 for level 2, 3.2 for level 3.
        /// </summary>
        public static double WallFactor(SettlementRow h) => Math.Pow((4.0 + Math.Max(1, h?.WallLevel ?? 1)) / 1.65, 0.8);

        /// <summary>
        /// The strength that would hold a castle or town: its garrison and militia (known for the realm's own places, and an
        /// ally's in sight), the realm's lords inside it (and allies' inside a place in sight), the realm's lords holding an
        /// order to defend or garrison it within HoldRadius of it (they shelter there too), and the given party.
        /// </summary>
        public double DefenceStrength(SettlementRow h, string withParty)
        {
            if (!IsFort(h)) return 0;
            bool known = h.Faction == Realm || (S.InspectedByWatched != null && Realm == S.Watched && S.InspectedByWatched.Contains(h.Id));
            double sum = known ? h.GarrisonStrength + h.MilitiaStrength : 0;
            var counted = new HashSet<string>();
            foreach (PartyRow r in S.Parties)
            {
                if (r.Kind != "lord" || r.Attached || r.InSettlement != h.Id) continue;
                if (r.Faction != Realm && !(known && Ally(r.Faction))) continue;
                sum += StrengthOf(r);
                counted.Add(r.Id);
            }
            foreach (ToolInstance t in Runner.All())
            {
                if (t.Order.At != h.Id || (t.Def.Name != "defend" && t.Def.Name != "garrison")) continue;
                PartyRow r = Party(t.Order.Party);
                if (r == null || r.Attached || counted.Contains(r.Id)) continue;
                double d = Distance(r.Id, h.Id);
                if (double.IsNaN(d) || d > HoldRadius) continue;
                sum += StrengthOf(r);
                counted.Add(r.Id);
            }
            PartyRow me = Party(withParty);
            if (me != null && !me.Attached && !counted.Contains(me.Id)) sum += StrengthOf(me);
            return sum;
        }

        /// <summary>Would the defenders, with this party among them, hold the place against this much strength? (The game's assault test.)</summary>
        public bool Holds(SettlementRow h, string withParty, double opposing) => IsFort(h) && DefenceStrength(h, withParty) * WallFactor(h) >= opposing;

        /// <summary>
        /// Where a lord goes when it must shelter:
        /// - first the preferred place (the holding an order guards), then the realm's and its allies' castles and towns not
        ///   under siege, nearest first, but only those the lord can reach before the danger (no nearer to it than to the
        ///   lord); the preferred one may be under siege when there is no danger party (defend reinforcing its own walls);
        /// - the first whose defenders, with this lord, would hold it against what threatens it (the danger, if there is
        ///   one, and every force near that place: ThreatStrength) is the shelter: the lord goes inside (holds = true);
        /// - if none would hold, the lord runs: with a danger party, toward the nearest of those places that takes it
        ///   farther from the danger, stopping outside (running = true); without one (an order sheltering from the forces
        ///   near its holding), into the nearest other castle or town not under siege.
        /// Null when there is nowhere to go.
        /// </summary>
        public SettlementRow ChooseRefuge(string me, string preferred, double radius, PartyRow danger, out bool holds, out bool running)
        {
            holds = running = false;
            if (!Where(me, out float mx, out float my)) return null;
            double dangerStrength = danger != null ? StrengthOf(danger) : 0;
            double DistTo(SettlementRow h, float x, float y) => Math.Sqrt((h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y));
            // reached before the danger: the lord's hours to it, at its own speed, no more than the danger's at its speed
            double mySpeed = Speed(Party(me)), itsSpeed = danger != null ? Speed(danger) : 1;
            bool Reachable(SettlementRow h) => danger == null || DistTo(h, mx, my) < 2 || DistTo(h, mx, my) / mySpeed <= DistTo(h, danger.X, danger.Y) / itsSpeed;
            List<SettlementRow> places = S.Settlements
                .Where(h => IsFort(h) && (h.Faction == Realm || Ally(h.Faction)) && (!Besieged(h) || (h.Id == preferred && danger == null)))
                .OrderBy(h => h.Id == preferred ? -1 : DistTo(h, mx, my)).ToList();
            foreach (SettlementRow h in places)
            {
                if (!Reachable(h)) continue;
                double opposing = dangerStrength + ThreatStrength(h.Id, radius, danger?.Id);
                if (Holds(h, me, opposing)) { holds = true; return h; }
            }
            if (danger != null)
            {
                // run: toward the nearest place it reaches first that takes it farther from the danger, but never the place
                // the danger is besieging or marching on
                string marchedOn = danger.Besieging ?? (danger.Doing == "BesiegeSettlement" || danger.Doing == "AssaultSettlement" ? danger.TargetSettlement : null);
                double fromDanger = Math.Sqrt((mx - danger.X) * (mx - danger.X) + (my - danger.Y) * (my - danger.Y));
                SettlementRow away = places.Where(h => !Besieged(h) && h.Id != marchedOn && Reachable(h) && DistTo(h, danger.X, danger.Y) > fromDanger + 1)
                                           .OrderBy(h => DistTo(h, mx, my)).FirstOrDefault();
                if (away != null) running = true;
                return away;
            }
            return places.Where(h => h.Id != preferred && !Besieged(h)).OrderBy(h => DistTo(h, mx, my)).FirstOrDefault();
        }

        /// <summary>
        /// The besiegers' strength, if the realm can know it: always for its own holdings (their lords are told), else
        /// only for besiegers in view. Every lord camped there counts.
        /// </summary>
        public double? SiegeStrength(SettlementRow h)
        {
            if (!Besieged(h)) return null;
            bool ours = h.Faction == Realm;
            double sum = 0;
            bool any = false;
            foreach (PartyRow r in S.Parties)
            {
                if (r.Besieging != h.Id || r.Attached || r.Kind != "lord") continue;
                if (!ours && !Visible(r)) continue;
                sum += StrengthOf(r);
                any = true;
            }
            return any ? sum : (double?)null;
        }

        /// <summary>
        /// The defenders inside one of the realm's own castles or towns who can sally out: the garrison and the lords of the
        /// realm and its allies sheltering there. Militia never sally (the game's rule). Null for anyone else's holding.
        /// </summary>
        public double? InsideStrength(SettlementRow h, string except = null)
        {
            if (h == null || h.Faction != Realm || !IsFort(h)) return null;
            double sum = h.GarrisonStrength;
            foreach (PartyRow r in S.Parties)
                if (r.InSettlement == h.Id && r.Kind == "lord" && !r.Attached && r.Id != except && (r.Faction == Realm || Ally(r.Faction))) sum += StrengthOf(r);
            return sum;
        }

        /// <summary>
        /// What a relief force of this strength would fight the besiegers of one of the realm's holdings with. The game's
        /// rule (SallyOutsCampaignBehavior, 1.4.8): once a relief force attacks the besiegers, the garrison and the lords
        /// inside join the battle when they and the friendly forces there are more than 1.5 times the enemy's strength
        /// there; it checks again every hour of the battle. So: the relief's strength plus theirs when that holds, else the
        /// relief's alone.
        /// </summary>
        public double ReliefStrength(SettlementRow h, double relief, string reliefParty = null)
        {
            double? inside = InsideStrength(h, reliefParty);
            double? siege = SiegeStrength(h);
            if (inside == null || siege == null || inside.Value <= 0) return relief;
            return relief + inside.Value > SallyRatio * siege.Value ? relief + inside.Value : relief;
        }

        /// <summary>The game's bar for the defenders to sally out beside a relief force (SallyOutPowerRatioForHelpingReliefForce).</summary>
        public const double SallyRatio = 1.5;

        /// <summary>Nearest castle or town of the realm or an ally, not under siege, to a place.</summary>
        public SettlementRow NearestFort(string from, Func<SettlementRow, bool> extra = null)
        {
            if (!Where(from, out float px, out float py)) return null;
            SettlementRow best = null;
            double bd = double.MaxValue;
            foreach (SettlementRow h in S.Settlements)
            {
                if (!IsFort(h) || Besieged(h) || (h.Faction != Realm && !Ally(h.Faction))) continue;
                if (extra != null && !extra(h)) continue;
                double d = (h.X - px) * (h.X - px) + (h.Y - py) * (h.Y - py);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        /// <summary>
        /// Nearest castle or town of the realm or an ally, not under siege, to a party, other than the given holding: a
        /// refuge outside the place the enemy is coming for, so a lord sheltering there cannot be trapped and taken with it.
        /// </summary>
        public SettlementRow OtherFort(string except, string from)
        {
            if (!Where(from, out float px, out float py)) return null;
            SettlementRow best = null;
            double bd = double.MaxValue;
            foreach (SettlementRow h in S.Settlements)
            {
                if (!IsFort(h) || Besieged(h) || h.Id == except || (h.Faction != Realm && !Ally(h.Faction))) continue;
                double d = (h.X - px) * (h.X - px) + (h.Y - py) * (h.Y - py);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        /// <summary>Nearest town or village of the realm or an ally where food can be had: not besieged, raided or looted.</summary>
        public SettlementRow NearestSupply(string from)
        {
            if (!Where(from, out float px, out float py)) return null;
            SettlementRow best = null;
            double bd = double.MaxValue;
            foreach (SettlementRow h in S.Settlements)
            {
                if ((h.Kind != "town" && h.Kind != "village") || Besieged(h) || (h.Faction != Realm && !Ally(h.Faction))) continue;
                if (h.Kind == "village" && h.VillageState != null && h.VillageState != "Normal") continue;
                double d = (h.X - px) * (h.X - px) + (h.Y - py) * (h.Y - py);
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }

        /// <summary>Nearest enemy village within r of a place that still has something to take.</summary>
        public SettlementRow NextVillage(string place, double r)
        {
            if (!Where(place, out float px, out float py)) return null;
            SettlementRow best = null;
            double bd = double.MaxValue;
            foreach (SettlementRow h in S.Settlements)
            {
                if (h.Kind != "village" || !HostileFaction(h.Faction)) continue;
                if (h.VillageState != null && h.VillageState != "Normal") continue;
                double d = Math.Sqrt((h.X - px) * (h.X - px) + (h.Y - py) * (h.Y - py));
                if (d <= r && d < bd) { bd = d; best = h; }
            }
            return best;
        }

        /// <summary>A name for an id, as the agent reads it.</summary>
        public string Name(string id)
        {
            if (id == null) return "none";
            if (Spots.TryParse(id, out float sx, out float sy)) return Spots.Describe(S, sx, sy);
            SettlementRow h = Holding(id);
            if (h != null) return h.Name ?? id;
            PartyRow r = Party(id);
            if (r != null && !string.IsNullOrEmpty(r.Name)) return r.Name;
            if (_lastSeen.TryGetValue(id, out ViewParty v) && !string.IsNullOrEmpty(v.Name)) return v.Name;
            return id;
        }

        /// <summary>The id the agent's orders use for a party: its lord's, when it has one.</summary>
        public string Tag(string id)
        {
            PartyRow r = Party(id);
            if (r?.Leader != null) return r.Leader;
            if (id != null && _lastSeen.TryGetValue(id, out ViewParty v) && v.Lord != null) return v.Lord;
            return id;
        }
    }
}
