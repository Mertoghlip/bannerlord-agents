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
        /// How near something is to a place (0.12.1): to a holding, the nearer of its centre and its gate, since a lord sent
        /// close to it stands by its gate. On a mountain castle the gate is over 3 from the centre (Caradhras 3.3, Sirannon 3.4,
        /// Nanduhirion 3.5): in run 20261025-1524-C2 three sieges stood 4.3 to 4.7 from Caradhras' gate, 6.8 and 7.8 from its
        /// centre, for up to 15 days, waiting for "within 6" of the centre.
        /// </summary>
        public double Near(string a, string place)
        {
            double d = Distance(a, place);
            SettlementRow h = Holding(place);
            if (h == null || (h.Gx == 0 && h.Gy == 0) || !Where(a, out float ax, out float ay)) return d;
            double g = Math.Sqrt((ax - h.Gx) * (ax - h.Gx) + (ay - h.Gy) * (ay - h.Gy));
            return double.IsNaN(d) ? g : Math.Min(d, g);
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
        public double ThreatStrength(string place, double r, string except = null) => Threat(place, r, except != null ? new HashSet<string> { except } : null, true, 0);

        /// <summary>The same, leaving out a whole group already counted (a danger counted as a group).</summary>
        public double ThreatStrength(string place, double r, ICollection<string> except) => Threat(place, r, except, true, 0);

        /// <summary>The same, and every enemy lord and army in view that could reach the place within joinHours (a tool's join_hours, 0.12.3).</summary>
        public double ThreatStrength(string place, double r, double joinHours) => Threat(place, r, null, true, joinHours);

        /// <summary>The same without bandits: hostile lords and armies only, the forces that make a lord needed where it is.</summary>
        public double EnemyThreat(string place, double r) => Threat(place, r, null, false, 0);

        /// <summary>
        /// Since 0.12.3 the threat a tool's rules weigh counts, beside the parties within r, every enemy lord and army in view that
        /// could reach the place within the tool's join_hours (Joiners), wherever it stands: in run 20261025-1609-C2 eleven lords
        /// marching to join Thrurg stood 27 to 29 from Torech Emel, outside Alenon's area, and counted for nothing.
        /// </summary>
        private double Threat(string place, double r, ICollection<string> except, bool bandits, double joinHours)
        {
            if (!Where(place, out float px, out float py)) return 0;
            List<PartyRow> near = bandits ? ThreatsInView(place, r) : EnemiesNear(place, r);
            var counted = new HashSet<string>(near.Select(e => e.Id));
            // an enemy the realm's ruler is attacking in person with the stronger force is his fight, not a threat (0.13.11)
            HashSet<string> hunted = RulerHunts();
            if (hunted.Count > 0) except = except == null ? hunted : new HashSet<string>(except.Concat(hunted));
            double sum = near.Where(e => except == null || !except.Contains(e.Id)).Sum(StrengthOf);
            if (joinHours > 0)
                foreach (PartyRow m in Joiners(px, py, joinHours))
                    if (counted.Add(m.Id) && (except == null || !except.Contains(m.Id))) sum += StrengthOf(m);
            foreach (KeyValuePair<string, ViewParty> kv in _lastSeen)
            {
                ViewParty v = kv.Value;
                if ((except != null && except.Contains(kv.Key)) || counted.Contains(kv.Key) || v.Relation != "enemy" || Hours - v.SeenHours > RecentHours || InView(kv.Key) || Gone(kv.Key)) continue;
                if (Math.Sqrt((v.X - px) * (v.X - px) + (v.Y - py) * (v.Y - py)) <= r) sum += v.Strength;
            }
            return sum;
        }

        /// <summary>
        /// The army a party marches with (0.12.2): the army it is in, or the one it is marching to join (Views.JoiningHead), else
        /// itself. A shadow spares the army it shadows and the lords marching to join it.
        /// </summary>
        public PartyRow HeadOf(PartyRow e) => e == null ? null : Views.JoiningHead(S, e) is PartyRow head ? Lead(head) : Lead(e);

        /// <summary>Hours a party needs to reach a point at its own pace, as the crow flies (a fight's place is no holding, so there is no road table to it).</summary>
        public double HoursTo(PartyRow e, float x, float y) => Math.Sqrt((e.X - x) * (e.X - x) + (e.Y - y) * (e.Y - y)) / Math.Max(0.3, PerHour(e));

        /// <summary>
        /// Who could join a fight at a point (0.12.3: the agent decides which lords count, by an hours window, so a fight
        /// of 20 hours counts those who could get there within them): every hostile lord and army in view that could reach the
        /// point within so many hours at its own pace, nearest in hours first. Bandits are not counted: they do not join lords'
        /// battles.
        /// </summary>
        public List<PartyRow> Joiners(float x, float y, double hours)
        {
            if (hours <= 0) return new List<PartyRow>();
            return _enemies.Where(e => HoursTo(e, x, y) <= hours).OrderBy(e => HoursTo(e, x, y)).ToList();
        }

        /// <summary>
        /// Bands of one bandit kind in view that could reach a point within so many hours, nearest first (0.13.16). Bandits join no lord's
        /// fight, but they do join one their own kind is fighting: Osven (139 men) was beating a band of Mirkwood Stalkers (118) on Winter 2
        /// when five more bands came in, 577 men by noon, and took him.
        /// </summary>
        public List<PartyRow> BandsOf(string faction, float x, float y, double hours)
        {
            if (hours <= 0 || faction == null) return new List<PartyRow>();
            return _bandits.Where(b => b.Faction == faction && HoursTo(b, x, y) <= hours).OrderBy(b => HoursTo(b, x, y)).ToList();
        }

        /// <summary>
        /// The force an enemy party would be fought with (0.12.3): itself and every other hostile lord and army in view that could
        /// reach where it stands within joinHours. Alenon struck Narsha (strength 1,751 against his 8,505) because she was the
        /// weakest lord near Torech Emel; a dozen lords marching to join Thrurg, about 17,000 together, were a few hours from her.
        /// </summary>
        public List<PartyRow> Company(string id, double joinHours)
        {
            var list = new List<PartyRow>();
            PartyRow e = Lead(Party(id));
            if (e == null || !Visible(e)) return list;
            list.Add(e);
            foreach (PartyRow m in Joiners(e.X, e.Y, joinHours)) if (m.Id != e.Id) list.Add(m);
            return list;
        }

        public double CompanyStrength(string id, double joinHours) => Company(id, joinHours).Sum(StrengthOf);

        /// <summary>
        /// Our side of a fight (0.12.4, run 20261025-1831-C2: Imák was going back and forth because he was scared of Khamûl's
        /// force, even though our lords' combined force was enough to take him; the strength calculator was not taking into
        /// account the strength of the other lords around him): five lords besieged Torech Emel, each under his own order, and
        /// each weighed Khamûl's relief (4,155) against himself alone (559 to 1,199). Counted with a lord, of the realm's lords and
        /// its allies' in view, out in the field: every one camped in the siege he is in or making for (besieging), wherever; and,
        /// within joinHours of the fight at their own pace, those marching to join him or his army, and those bound for the same
        /// place or target (objectives): besieging it, defending it, or holding one of the agent's orders aimed at it. Not every lord
        /// near him: a lord holding one of the agent's orders has the game's own choices switched off, so he joins no fight by himself.
        /// </summary>
        public List<PartyRow> Friends(PartyRow me, float x, float y, double joinHours, string besieging, ICollection<string> objectives)
        {
            var list = new List<PartyRow>();
            foreach (PartyRow r in S.Parties)
            {
                if (r.Kind != "lord" || r.Attached || (me != null && r.Id == me.Id) || !(Own(r) || (Ally(r.Faction) && Visible(r)))) continue;
                if (besieging != null && r.Besieging == besieging) { list.Add(r); continue; }
                // a lord marching with him (following him, by our order or the game's escort, or joining his army) counts in the field within
                // joinHours, and inside the very holding he is in, which he leaves with him (0.13.14, Oct 30: the logic for our orders
                // might still be discounting the strength of nearby lords/lords following other lords). Winter 1, 12:00: Alenon (1,137),
                // to guard Tinnulas, weighed 2,087 near it against himself alone (1,135) and sheltered in Gwígar; Oropherion (961) and Arien
                // (860), ordered to follow him, were inside Gwígar with him and not counted, 2,958 together. Tinnulas was looted by 163 men.
                if (me != null && MarchesWith(r, me))
                {
                    bool beside = r.InSettlement != null && r.InSettlement == me.InSettlement;
                    if (beside || (r.InSettlement == null && !(r.InBattle && HoursTo(r, x, y) > 1) && joinHours > 0 && HoursTo(r, x, y) <= joinHours)) list.Add(r);
                    continue;
                }
                if (r.InSettlement != null || (r.InBattle && HoursTo(r, x, y) > 1) || joinHours <= 0 || HoursTo(r, x, y) > joinHours) continue;
                if (objectives == null || objectives.Count == 0) continue;
                // since 0.13.11 also a lord attacking the same target (Oct 29, when I was chasing Mauhur with 3,865 men: my realm's guards struck
                // beside me weighing only themselves)
                bool bound = (r.Besieging != null && objectives.Contains(r.Besieging))
                             || (r.Doing == "EngageParty" && r.TargetParty != null && objectives.Contains(r.TargetParty))
                             || ((r.Doing == "BesiegeSettlement" || r.Doing == "DefendSettlement") && r.TargetSettlement != null && objectives.Contains(r.TargetSettlement))
                             || (Runner.ForParty(r.Id) is ToolInstance ti && ((ti.Order.At != null && objectives.Contains(ti.Order.At)) || (ti.Order.Target != null && objectives.Contains(ti.Order.Target))));
                if (bound) list.Add(r);
            }
            return list;
        }

        /// <summary>Does r march with me (0.13.14): joining my army, escorting me in the game, or holding our order to follow me?</summary>
        private bool MarchesWith(PartyRow r, PartyRow me)
            => Views.JoiningHead(S, r)?.Id == me.Id
               || (Runner.ForParty(r.Id) is ToolInstance ti && string.Equals(ti.Order.Tool, "follow", StringComparison.OrdinalIgnoreCase) && ti.Order.Target == me.Id);

        /// <summary>
        /// The party the realm's ruler is attacking in person now, or null (0.13.11, Oct 29: I was chasing Mauhur, who had 700
        /// men, with my army of more than 4000, but allied lords around me were still not engaging him; they should be engaging him normally
        /// since they know I am around): the ruling clan's leader's own party, of this realm, set on attacking a party (EngageParty).
        /// </summary>
        public string RulerTarget()
        {
            string ruler = S.Clans?.FirstOrDefault(c => c.Ruler)?.Leader;
            if (ruler == null) return null;
            PartyRow r = S.Parties.FirstOrDefault(p => p.Kind == "lord" && p.Leader == ruler && Own(p) && !p.Attached);
            if (r == null || r.Doing != "EngageParty" || r.TargetParty == null) return null;
            PartyRow t = Party(r.TargetParty);
            return t != null && HostileFaction(t.Faction) ? t.Id : null;   // an enemy only
        }

        /// <summary>
        /// The ring of a battle the human player fights (0.17.3, Nov 8: battle times work differently when the player is fighting the battle; read in
        /// 1.4.8's DefaultEncounterModel.FindNonAttachedNpcPartiesWhoWillJoinPlayerEncounter): when one begins, the lords, patrols and bands within 3 of
        /// it join a side, those in armies come with their leader, and the clock stands still until it ends (3.75 while he is in a siege; a storm he
        /// leads or defends takes only the siege camp and those inside).
        /// </summary>
        public const double Ring = 3;

        /// <summary>Is this the human player's own party, or an army he leads, or a lord marching in it (0.17.3)?</summary>
        public bool InPerson(string id)
        {
            string pp = S.PlayerParty;
            if (id == null || pp == null) return false;
            if (id == pp) return true;
            PartyRow r = Party(id);
            return r != null && (r.ArmyLeader == pp || Lead(r)?.Id == pp);
        }

        /// <summary>Our side of a battle with the human player begun where this lord stands (0.17.3): he and the lords of ours and our allies' in view within the ring of him.</summary>
        public double RingSide(PartyRow me)
        {
            if (me == null) return 0;
            double sum = StrengthOf(me);
            foreach (PartyRow r in S.Parties)
            {
                if (r.Kind != "lord" || r.Attached || r.Id == me.Id || r.InSettlement != null || r.InBattle || !(Own(r) || (Ally(r.Faction) && Visible(r)))) continue;
                double d = Distance(me.Id, r.Id);
                if (!double.IsNaN(d) && d <= Ring) sum += StrengthOf(r);
            }
            return sum;
        }

        /// <summary>The human player's side of a battle with him begun now (0.17.3): his party (his army with it) and the enemy lords and bands in view within the ring of him.</summary>
        public double RingEnemy(string id)
        {
            PartyRow lead = Lead(Party(id));
            if (lead == null) return 0;
            double sum = StrengthOf(lead);
            foreach (PartyRow e in EnemiesNear(lead.Id, Ring))
                if (e.Id != lead.Id && !e.Attached) sum += StrengthOf(e);
            return sum;
        }

        /// <summary>A lord with our side of a fight: his own strength and every friend's (Friends) together.</summary>
        public double SideStrength(PartyRow me, float x, float y, double joinHours, string besieging, ICollection<string> objectives, List<PartyRow> with = null)
        {
            double sum = StrengthOf(me);
            foreach (PartyRow f in Friends(me, x, y, joinHours, besieging, objectives))
            {
                sum += StrengthOf(f);
                with?.Add(f);
            }
            return sum;
        }

        /// <summary>
        /// What threatens a lord where he stands (the safety rule): once anything hostile is within r of him, a fight may start
        /// where he is, so every hostile lord, army and band of bandits in view within r, and every hostile lord and army in view
        /// that could reach him within joinHours (Joiners), count together; spared parties (his own target) left out, though
        /// those who could join a fight with it count (0.12.2 counted a lord's host within 20; 0.12.3 counts anyone by the hours
        /// the order is given). Before 0.12.2 each was weighed alone, so a host split into a dozen parties was never a danger
        /// (Thalion and Girda in run 20261025-1524-C2, Alenon's army in 20261025-1609-C2).
        /// </summary>
        public List<PartyRow> DangerAround(string me, double r, double joinHours, Func<PartyRow, bool> spared)
        {
            var list = new List<PartyRow>();
            PartyRow lord = Party(me);
            List<PartyRow> near = ThreatsInView(me, r);
            if (lord == null || near.Count == 0) return list;
            var seen = new HashSet<string>();
            // an enemy the realm's ruler is attacking in person with the stronger force is no danger to flee (0.13.11)
            HashSet<string> hunted = RulerHunts();
            foreach (PartyRow e in near) if (!spared(e) && !hunted.Contains(e.Id) && seen.Add(e.Id)) list.Add(e);
            foreach (PartyRow m in Joiners(lord.X, lord.Y, joinHours))
                if (m.Id != me && !spared(m) && !hunted.Contains(m.Id) && seen.Add(m.Id)) list.Add(m);
            return list;
        }

        /// <summary>
        /// The enemy the realm's ruler is attacking in person (RulerTarget) and every hostile lord and army that could join that fight within
        /// RulerHuntHours, when the ruler's force (his army, if he leads one) is the stronger; else none (0.13.11). Their threat is the ruler's fight:
        /// a guard nearby joins it rather than shelters or flees from it (Oct 29, when I was chasing Mauhur: my realm's guards turned back).
        /// </summary>
        public HashSet<string> RulerHunts() => _hunts ?? (_hunts = FindRulerHunts());

        // once per hour's world: the threat and danger counts ask for it many times an hour
        private HashSet<string> _hunts;

        private HashSet<string> FindRulerHunts()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            string target = RulerTarget();
            if (target == null) return set;
            string ruler = S.Clans?.FirstOrDefault(c => c.Ruler)?.Leader;
            PartyRow rp = ruler == null ? null : S.Parties.FirstOrDefault(p => p.Kind == "lord" && p.Leader == ruler && Own(p) && !p.Attached);
            List<PartyRow> company = Company(target, RulerHuntHours);
            if (rp == null || company.Count == 0 || StrengthOf(rp) <= company.Sum(StrengthOf)) return set;
            foreach (PartyRow e in company) set.Add(e.Id);
            set.Add(target);
            return set;
        }

        /// <summary>The hours within which an enemy is counted as joining the ruler's fight (the default join_hours).</summary>
        public const double RulerHuntHours = 8;

        /// <summary>One enemy lord or army counted as coming to a place's relief.</summary>
        internal sealed class Comer
        {
            public string Id, Name, Doing;
            public double Strength, Distance, Road = double.NaN, SeenAgo;
            public bool Heading;
            /// <summary>Hours until it gets there by the game's marching rules (0.7.4): along its planned way when heading for it, else its distance at its pace over a day.</summary>
            public double Eta = double.NaN;
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
                if (e.InSettlement == place) continue;   // inside the place itself: part of its defence, which the storm test counts already
                double d = Dist(e.X, e.Y);
                bool heading = Views.Toward(e) == place;
                if (d > r && !heading) continue;
                var c = new Comer { Id = e.Id, Name = e.Name ?? e.Id, Doing = Views.Doing(S, e), Strength = StrengthOf(e), Distance = d, Heading = heading };
                // when it gets there (0.7.4): along its way only when that way ends at the place, else by road at its pace (two lords
                // walking to a waypoint 8.6 ahead were counted "due in about 3 hours" at Hollin Gate, 96.5 away by road, and set
                // off the withdrawal bar)
                SettlementRow at = Holding(place);
                Forecast.Arrival arr = at != null ? Forecast.ToHolding(S, e, at, 24 * 10) : null;
                c.Eta = arr != null ? (arr.Reached ? arr.Hours : double.PositiveInfinity) : d / Math.Max(0.3, PerHour(e));
                list.Add(c);
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
                // seen earlier: from where it was then (by road where the game's table knows the way), at its pace then, less the hours since
                double perHour = v.March != null ? Common.GamePace.DayAverage(v.March) : DefaultSpeed * Common.GamePace.UnitsPerSpeedHour;
                double road = RoadDistance(kv.Key, place);
                list.Add(new Comer { Id = kv.Key, Name = v.Name ?? kv.Key, Doing = v.Doing, Strength = v.Strength, Distance = d, Heading = heading, SeenAgo = Hours - v.SeenHours,
                                     Eta = Math.Max(0, (double.IsNaN(road) ? d : road) / Math.Max(0.3, perHour) - (Hours - v.SeenHours)) });
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
                + Roads.Both((float)c.Distance, (float)c.Road) + " away" + (double.IsNaN(c.Eta) ? "" : double.IsInfinity(c.Eta) ? " (more than 10 days off at its pace)" : " (due in about " + Forecast.HoursText(c.Eta) + ")") + ", " + (c.Doing ?? "doing what is not known")
                + (c.SeenAgo > 0 ? " (seen " + Math.Round(c.SeenAgo) + " hours ago)" : ""));
            string text = string.Join("; ", lines);
            if (list.Count > most)
                text += "; and " + (list.Count - most) + " more (strength " + Text.N((int)Math.Round(list.Skip(most).Sum(c => c.Strength))) + ")";
            return text;
        }

        /// <summary>The strength of the relief (ReliefComing) due within so many hours.</summary>
        public double ReliefWithin(string place, double hours, double r) => ReliefComing(place, r).Where(c => !double.IsNaN(c.Eta) && c.Eta <= hours).Sum(c => c.Strength);

        /// <summary>The siege of a place as the hourly copy has it (a siege the realm can know of: its own holding, or by its own or an ally's lords, or the camp in sight), or null.</summary>
        public SiegeRow Siege(string place)
        {
            SiegeRow g = S.Sieges?.FirstOrDefault(x => x.Settlement == place);
            if (g == null) return null;
            SettlementRow h = Holding(place);
            return h?.Faction == Realm || g.BesiegerFaction == Realm || Ally(g.BesiegerFaction) || Ally(h?.Faction) || InView(g.Besieger) ? g : null;
        }

        /// <summary>Hours until the camp besieging a place has its preparations done (0: done), by the rate the model in force builds them; NaN if no siege known.</summary>
        public double PrepHours(string place)
        {
            SiegeRow g = Siege(place);
            if (g == null) return double.NaN;
            return g.Prep >= 1 ? 0 : g.PrepRate > 0 ? (1 - g.Prep) / g.PrepRate : double.NaN;
        }

        /// <summary>"The camp may storm now." or "The camp's preparations are done in about 12 hours (Summer 19, 16:00); it cannot storm before."</summary>
        public string StormText(string place)
        {
            double h = PrepHours(place);
            if (double.IsNaN(h)) return "No siege of it is known.";
            if (Siege(place)?.Storming == true) return "A storm of it is being fought now: nobody leaves it until it ends.";
            return h <= 0.05 ? "The camp may storm now." : "The camp's preparations are done in about " + Forecast.HoursText(h) + " (" + Text.When(Hours + h) + "); it cannot storm before.";
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
        public SettlementRow ChooseRefuge(string me, string preferred, double radius, PartyRow danger, out bool holds, out bool running, double groupStrength = -1, ICollection<string> group = null)
        {
            holds = running = false;
            if (!Where(me, out float mx, out float my)) return null;
            // a danger counted as a group (0.12.2): its whole strength, and none of its parties counted again near the refuge
            double dangerStrength = groupStrength >= 0 ? groupStrength : danger != null ? StrengthOf(danger) : 0;
            ICollection<string> counted = group ?? (danger != null ? new HashSet<string> { danger.Id } : null);
            double DistTo(SettlementRow h, float x, float y) => Math.Sqrt((h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y));
            // reached before the danger: the lord's hours to it, at its own speed, no more than any of the danger's parties' at
            // theirs (0.12.4: only the strongest was weighed, and Zlatka, Alvar and Haldir, sheltering from a group, were caught on
            // the way by another part of it)
            double mySpeed = Speed(Party(me));
            List<PartyRow> members = (group ?? new List<string>()).Select(Party).Where(p => p != null).ToList();
            if (danger != null && !members.Contains(danger)) members.Add(danger);
            bool Reachable(SettlementRow h) => members.Count == 0 || DistTo(h, mx, my) < 2
                                               || members.All(d => DistTo(h, mx, my) / mySpeed <= DistTo(h, d.X, d.Y) / Speed(d));
            List<SettlementRow> places = S.Settlements
                .Where(h => IsFort(h) && (h.Faction == Realm || Ally(h.Faction)) && (!Besieged(h) || (h.Id == preferred && danger == null)))
                .OrderBy(h => h.Id == preferred ? -1 : DistTo(h, mx, my)).ToList();
            foreach (SettlementRow h in places)
            {
                if (!Reachable(h)) continue;
                double opposing = dangerStrength + ThreatStrength(h.Id, radius, counted);
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

        /// <summary>Bands of bandits in view within r of a place, nearest first (0.13: a tool can strike small bands and leave armies alone).</summary>
        public List<PartyRow> BanditsNear(string place, double r)
        {
            var list = new List<PartyRow>();
            if (!Where(place, out float px, out float py)) return list;
            foreach (PartyRow b in _bandits)
                if (Math.Sqrt((b.X - px) * (b.X - px) + (b.Y - py) * (b.Y - py)) <= r) list.Add(b);
            return list.OrderBy(b => (b.X - px) * (b.X - px) + (b.Y - py) * (b.Y - py)).ToList();
        }

        /// <summary>The nearest town of the realm (and of an ally's, when allies) not under siege, to a party or place (0.13: where loot sells).</summary>
        public SettlementRow NearestTown(string from, bool allies)
        {
            if (!Where(from, out float px, out float py)) return null;
            SettlementRow best = null;
            double bd = double.MaxValue;
            foreach (SettlementRow h in S.Settlements)
            {
                if (h.Kind != "town" || Besieged(h) || (h.Faction != Realm && !(allies && Ally(h.Faction)))) continue;
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
