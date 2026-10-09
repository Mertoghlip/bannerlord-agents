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
        private readonly List<PartyRow> _enemies;

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
            foreach (KeyValuePair<string, Sighting> kv in _sees)
                if (s.PartyById.TryGetValue(kv.Key, out PartyRow r) && r.Kind == "lord" && !r.Attached && _hostile.Contains(r.Faction ?? ""))
                    _enemies.Add(r);
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

        public static float Speed(PartyRow r) => r != null && r.Speed > 0.2f ? r.Speed : DefaultSpeed;

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

        public bool Besieged(SettlementRow h) => h != null && h.BesiegedBy != null;

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
