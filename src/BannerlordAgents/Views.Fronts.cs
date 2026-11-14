using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents.Common;

namespace BannerlordAgents
{
    /// <summary>
    /// YOUR FRONTS (0.19.3, Nov 13). Dol Guldur kept a quarter of its field strength against me and the rest on guard loops at castles where it saw no
    /// enemy army at all (Lórien 9,300 against the Naith's single lords, its north 8,800 against bandits, its home lands 7,100), and weighed only its western
    /// lords against my army, so every comparison looked hopeless and it only waited. Its report listed lords and holdings one by one and never set the
    /// realm's strength in each region beside what could reach that region. This section does, from what the realm knows alone: its own castles and towns
    /// grouped into fronts, its lords counted at the front nearest them, the enemy lords and armies it sees or saw lately near each, and the realms bordering
    /// each with their whole strength as their encyclopedia pages show it and whether they are enemies or allies. Facts only: what to do with them is the
    /// agent's (principle 30). Worker, from the snapshot and the view; no ids of holdings the realm does not own (the fog check).
    /// </summary>
    internal static partial class Views
    {
        /// <summary>Castles and towns within this many days' march of every other in their group are one front; enemies and bordering realms count within these.</summary>
        public const float FrontDays = 2f, NearDays = 3f;

        internal const string FrontsNote = "Your castles and towns, grouped so that each lies within two days' march of all the others in its group, at the pace of a middling lord party "
                                           + "(speed 2.8); each of your lords is counted at the front of the castle or town nearest him. Near each front: the enemy lords and armies within "
                                           + "three days' march of it, those in view now and those seen in the last three days and out of sight now (each counted once, at the front it is "
                                           + "nearest; bandits not counted); and the realms with castles or towns within two days' march of it, each with its whole strength as its "
                                           + "encyclopedia page shows it.";

        private sealed class Front
        {
            public readonly List<SettlementRow> Forts = new List<SettlementRow>();
            public string Name;
            public int Lords, InView, Seen;
            public float Strength, Defenders, InViewStrength, SeenStrength;
            public double SeenNewest = double.NaN, SeenOldest = double.NaN;
            public readonly Dictionary<string, float> ByRealm = new Dictionary<string, float>(StringComparer.Ordinal);
            public float Enemy => InViewStrength + SeenStrength;
        }

        /// <summary>The section's lines (empty when the realm holds no castle or town).</summary>
        public static List<string> Fronts(Snapshot s, string fid, View v, HashSet<string> hostile)
        {
            var lines = new List<string>();
            List<SettlementRow> forts = s.Settlements.Where(h => h.Faction == fid && Tools.World.IsFort(h)).OrderBy(h => h.Id, StringComparer.Ordinal).ToList();
            if (forts.Count == 0) return lines;
            float perHour = GamePace.DayAverageOfSpeed(Tools.World.ReliefFloorSpeed), perDay = perHour * 24f;
            if (perDay <= 0) return lines;

            // between two holdings: by road where the game's table knows the way (either way round), never less than the straight line
            float Gap(SettlementRow a, SettlementRow b)
            {
                float crow = Dist(a.X, a.Y, b.X, b.Y);
                float road = Roads.To(s, a.X, a.Y, null, a.Id, b);
                if (float.IsNaN(road)) road = Roads.To(s, b.X, b.Y, null, b.Id, a);
                return float.IsNaN(road) ? crow : Math.Max(crow, road);
            }
            int n = forts.Count;
            var d = new float[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++) d[i, j] = d[j, i] = Gap(forts[i], forts[j]);

            // complete linkage: two groups join only while every castle or town of the one is within FrontDays of every one of the other
            var groups = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList();
            float limit = FrontDays * perDay;
            while (true)
            {
                int bi = -1, bj = -1;
                float best = float.MaxValue;
                for (int gi = 0; gi < groups.Count; gi++)
                    for (int gj = gi + 1; gj < groups.Count; gj++)
                    {
                        float far = 0;
                        foreach (int a in groups[gi]) foreach (int b in groups[gj]) far = Math.Max(far, d[a, b]);
                        if (far <= limit && far < best) { best = far; bi = gi; bj = gj; }
                    }
                if (bi < 0) break;
                groups[bi].AddRange(groups[bj]);
                groups.RemoveAt(bj);
            }
            var fronts = new List<Front>();
            var frontOf = new Dictionary<string, Front>(StringComparer.Ordinal);
            foreach (List<int> g in groups)
            {
                var f = new Front();
                foreach (int i in g) { f.Forts.Add(forts[i]); frontOf[forts[i].Id] = f; }
                // named after its town (the one with the strongest garrison when it has several), else its strongest castle
                SettlementRow head = f.Forts.OrderBy(h => h.Kind == "town" ? 0 : 1).ThenByDescending(h => h.GarrisonStrength).ThenBy(h => h.Name, StringComparer.Ordinal).First();
                f.Name = f.Forts.Count == 1 ? head.Name : "the front of " + head.Name;
                f.Defenders = f.Forts.Sum(h => Math.Max(0f, h.GarrisonStrength) + Math.Max(0f, h.MilitiaStrength));
                f.Forts.Sort((a, b) => a == head ? -1 : b == head ? 1 : string.CompareOrdinal(a.Name, b.Name));
                fronts.Add(f);
            }

            // the castle or town nearest a point (by road from a party where the table knows it), and how far
            SettlementRow Nearest(PartyRow r, float x, float y, out float far)
            {
                SettlementRow best = null;
                far = float.MaxValue;
                foreach (SettlementRow h in forts)
                {
                    float crow = Dist(x, y, h.X, h.Y);
                    float road = r != null ? Roads.To(s, r, h) : float.NaN;
                    float dd = float.IsNaN(road) ? crow : Math.Max(crow, road);
                    if (dd < far) { far = dd; best = h; }
                }
                return best;
            }

            // its own lords, where they are now
            foreach (ViewParty p in v.Forces)
            {
                s.PartyById.TryGetValue(p.Id, out PartyRow r);
                SettlementRow h = Nearest(r, p.X, p.Y, out _);
                if (h == null) continue;
                Front f = frontOf[h.Id];
                f.Lords += 1 + (p.Members?.Count ?? 0);
                f.Strength += p.Strength;
            }

            // the enemy's lords and armies near each: in view, then seen lately (each once, at the front it is nearest, within NearDays)
            float near = NearDays * perDay;
            bool Enemy(ViewParty p) => p != null && (p.Kind == "lord" || p.InPerson) && p.Faction != null && hostile != null && hostile.Contains(p.Faction);
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (ViewParty p in v.Seen.Where(Enemy))
            {
                s.PartyById.TryGetValue(p.Id, out PartyRow r);
                SettlementRow h = Nearest(r, p.X, p.Y, out float far);
                if (h == null || far > near || !counted.Add(p.Id)) continue;
                Front f = frontOf[h.Id];
                f.InView++;
                f.InViewStrength += p.Strength;
                string realm = p.FactionName ?? FactionName(s, p.Faction);
                f.ByRealm.TryGetValue(realm, out float had);
                f.ByRealm[realm] = had + p.Strength;
            }
            foreach (ViewParty p in v.LastSeen.Where(Enemy))
            {
                double ago = v.Hours - p.SeenHours;
                if (ago > NearDays * 24 || ago < 0) continue;
                SettlementRow h = Nearest(null, p.X, p.Y, out float far);
                if (h == null || far > near || !counted.Add(p.Id)) continue;
                Front f = frontOf[h.Id];
                f.Seen++;
                f.SeenStrength += p.Strength;
                f.SeenNewest = double.IsNaN(f.SeenNewest) ? ago : Math.Min(f.SeenNewest, ago);
                f.SeenOldest = double.IsNaN(f.SeenOldest) ? ago : Math.Max(f.SeenOldest, ago);
                string realm = p.FactionName ?? FactionName(s, p.Faction);
                f.ByRealm.TryGetValue(realm, out float had);
                f.ByRealm[realm] = had + p.Strength;
            }

            // each realm's whole strength, as its encyclopedia page shows any player (its lord parties' strength), and its living lords
            s.Factions.TryGetValue(fid, out FactionRow me);
            var whole = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (PartyRow r in s.Parties)
                if (r.Kind == "lord" && r.Faction != null) { whole.TryGetValue(r.Faction, out float w); whole[r.Faction] = w + r.Strength; }
            string Relation(string f) => me != null && me.AtWar.Contains(f) ? "enemy" : me != null && me.Allies.Contains(f) ? "ally" : "at peace";
            string RealmText(string f)
            {
                whole.TryGetValue(f, out float w);
                int lords = s.Factions.TryGetValue(f, out FactionRow fr) ? fr.Lords : 0;
                return FactionName(s, f) + " (" + Relation(f) + (lords > 0 ? "; " + lords + " lords" : "") + "; strength " + Text.N((int)Math.Round(w)) + ")";
            }
            List<SettlementRow> foreign = s.Settlements.Where(h => Tools.World.IsFort(h) && h.Faction != null && h.Faction != fid).ToList();

            fronts = fronts.OrderByDescending(f => f.Enemy).ThenByDescending(f => f.Strength).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
            float total = fronts.Sum(f => f.Strength);
            lines.Add("Your lords in the field by front: " + string.Join("; ", fronts.Select(f => f.Name + " " + Text.N((int)Math.Round(f.Strength))
                      + (total > 0 ? " (" + Math.Round(100 * f.Strength / total) + "%)" : ""))) + ".");
            foreach (Front f in fronts)
            {
                string places = string.Join(", ", f.Forts.Select(h => h.Name + " [" + h.Id + "]"));
                string mine = f.Lords == 0 ? "no lord of yours near it" : f.Lords + (f.Lords == 1 ? " lord, strength " : " lords, strength ") + Text.N((int)Math.Round(f.Strength));
                string enemy;
                if (f.InView + f.Seen == 0) enemy = "none in view or seen in the last three days";
                else
                {
                    var parts = new List<string>();
                    if (f.InView > 0) parts.Add("in view " + f.InView + ", strength " + Text.N((int)Math.Round(f.InViewStrength)));
                    if (f.Seen > 0) parts.Add("seen " + Math.Round(f.SeenNewest) + (Math.Round(f.SeenOldest) > Math.Round(f.SeenNewest) ? " to " + Math.Round(f.SeenOldest) : "") + " hours ago and out of sight now "
                                              + f.Seen + ", strength " + Text.N((int)Math.Round(f.SeenStrength)));
                    enemy = string.Join("; ", parts) + " (" + string.Join(", ", f.ByRealm.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + Text.N((int)Math.Round(kv.Value)))) + ")";
                }
                // the realms bordering it: their castles and towns within FrontDays of any of its own, the nearest named
                var border = new List<string>();
                foreach (IGrouping<string, SettlementRow> g in foreign.GroupBy(h => h.Faction))
                {
                    var near2 = new List<KeyValuePair<SettlementRow, float>>();
                    foreach (SettlementRow h in g)
                    {
                        float dd = f.Forts.Min(o => Gap(o, h));
                        if (dd <= limit) near2.Add(new KeyValuePair<SettlementRow, float>(h, dd));
                    }
                    if (near2.Count == 0) continue;
                    KeyValuePair<SettlementRow, float> first = near2.OrderBy(kv => kv.Value).First();
                    border.Add(RealmText(g.Key) + ": " + near2.Count + (near2.Count == 1 ? " castle or town" : " castles or towns") + " within two days' march, the nearest "
                               + first.Key.Name + ", about " + Text.D(first.Value / perDay) + " days");
                }
                lines.Add(f.Name.Substring(0, 1).ToUpperInvariant() + f.Name.Substring(1) + ": " + places + ". Your lords: " + mine
                          + "; garrisons and militia strength " + Text.N((int)Math.Round(f.Defenders)) + ". Enemy lords and armies near it: " + enemy + ". "
                          + (border.Count == 0 ? "No other realm's castle or town within two days' march." : "Bordering realms: " + string.Join("; ", border) + "."));
            }
            // where each enemy realm's strength has been seen, front by front: where it is gathering
            if (me != null)
                foreach (string e in me.AtWar)
                {
                    string name = FactionName(s, e);
                    whole.TryGetValue(e, out float w);
                    if (w <= 0) continue;
                    var at = fronts.Where(f => f.ByRealm.ContainsKey(name)).Select(f => new KeyValuePair<string, float>(f.Name, f.ByRealm[name])).OrderByDescending(kv => kv.Value).ToList();
                    float seen = at.Sum(kv => kv.Value);
                    lines.Add(name + " (strength " + Text.N((int)Math.Round(w)) + " in all): "
                              + (at.Count == 0 ? "none of it seen near your fronts in the last three days"
                                               : string.Join(", ", at.Select(kv => Text.N((int)Math.Round(kv.Value)) + " near " + kv.Key)) + "; "
                                                 + Text.N((int)Math.Round(Math.Max(0, w - seen))) + " (" + Math.Round(100 * Math.Max(0, w - seen) / w) + "%) not seen near any of your fronts in the last three days")
                              + ".");
                }
            return lines;
        }
    }
}
