using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents.Common;

namespace BannerlordAgents
{
    /// <summary>
    /// YOUR BORDERS (0.19.4, Nov 13; it replaces 0.19.3's YOUR FRONTS). Dol Guldur kept a quarter of its field strength against me and the rest on guard
    /// loops in regions where it saw no enemy army. 0.19.3 grouped its castles and towns into fronts by distance, which split it into ten small fronts and
    /// framed the question by geography; I want the agents to think the other way round: the enemy realms that border them, how strong each is, and their
    /// own strength shared among those enemies by it, with the heartland and the borders with allies left unguarded unless threatened and used to rebuild
    /// weak lords. So this section names, strongest first, each enemy realm: its whole strength as its encyclopedia page shows it, whether it borders the
    /// realm and which of the realm's castles and towns face it, and how much of it the realm has seen near its lands now and lately; then the allies and
    /// realms at peace that border it, and the realm's castles and towns that face no enemy. Facts only: principle 30 says what to make of them. Worker, from
    /// the snapshot and the view; no ids of holdings the realm does not own (the fog check).
    /// </summary>
    internal static partial class Views
    {
        /// <summary>A realm borders yours when a castle or town of its lies within BorderDays' march of one of yours; its lords seen within NearDays of yours, in the last NearDays, are near your lands.</summary>
        public const float BorderDays = 2f, NearDays = 3f;

        internal const string BordersNote = "Each enemy realm, strongest first: its whole strength as its encyclopedia page shows it; whether it borders you (a castle or town of its within two "
                                            + "days' march of one of yours, at the pace of a middling lord party, speed 2.8, by road where known), and your castles and towns that face it; "
                                            + "and how much of its strength your realm has seen near your lands (its lords and armies within three days' march of your castles and towns), "
                                            + "in view now and in the last three days. Then your allies and the realms at peace that border you, and your castles and towns that face no enemy.";

        /// <summary>The section's lines (empty when the realm holds no castle or town).</summary>
        public static List<string> Borders(Snapshot s, string fid, View v, HashSet<string> hostile)
        {
            var lines = new List<string>();
            List<SettlementRow> forts = s.Settlements.Where(h => h.Faction == fid && Tools.World.IsFort(h)).OrderBy(h => h.Name, StringComparer.Ordinal).ToList();
            if (forts.Count == 0 || !s.Factions.TryGetValue(fid, out FactionRow me)) return lines;
            float perDay = GamePace.DayAverageOfSpeed(Tools.World.ReliefFloorSpeed) * 24f;
            if (perDay <= 0) return lines;
            float border = BorderDays * perDay, near = NearDays * perDay;

            // between two holdings: by road where the game's table knows the way (either way round), never less than the straight line
            float Gap(SettlementRow a, SettlementRow b)
            {
                float crow = Dist(a.X, a.Y, b.X, b.Y);
                float road = Roads.To(s, a.X, a.Y, null, a.Id, b);
                if (float.IsNaN(road)) road = Roads.To(s, b.X, b.Y, null, b.Id, a);
                return float.IsNaN(road) ? crow : Math.Max(crow, road);
            }
            // how far a party (by road from it where known) or a point is from the nearest of the realm's castles and towns
            float FromLands(PartyRow r, float x, float y)
            {
                float best = float.MaxValue;
                foreach (SettlementRow h in forts)
                {
                    float crow = Dist(x, y, h.X, h.Y);
                    float road = r != null ? Roads.To(s, r, h) : float.NaN;
                    best = Math.Min(best, float.IsNaN(road) ? crow : Math.Max(crow, road));
                }
                return best;
            }

            // each realm's whole strength, as its encyclopedia page shows any player (its lord parties' strength), and its living lords
            var whole = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (PartyRow r in s.Parties)
                if (r.Kind == "lord" && r.Faction != null) { whole.TryGetValue(r.Faction, out float w); whole[r.Faction] = w + r.Strength; }
            float Whole(string f) => whole.TryGetValue(f, out float w) ? w : 0f;
            int Lords(string f) => s.Factions.TryGetValue(f, out FactionRow fr) ? fr.Lords : 0;

            // the enemy's lords and armies seen near the realm's lands: in view now, and seen in the last NearDays and out of sight now
            var now = new Dictionary<string, float>(StringComparer.Ordinal);
            var lately = new Dictionary<string, float>(StringComparer.Ordinal);
            bool Enemy(ViewParty p) => p != null && (p.Kind == "lord" || p.InPerson) && p.Faction != null && hostile != null && hostile.Contains(p.Faction);
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (ViewParty p in v.Seen.Where(Enemy))
            {
                s.PartyById.TryGetValue(p.Id, out PartyRow r);
                if (FromLands(r, p.X, p.Y) > near || !counted.Add(p.Id)) continue;
                now.TryGetValue(p.Faction, out float had);
                now[p.Faction] = had + p.Strength;
            }
            foreach (ViewParty p in v.LastSeen.Where(Enemy))
            {
                double ago = v.Hours - p.SeenHours;
                if (ago < 0 || ago > NearDays * 24 || FromLands(null, p.X, p.Y) > near || !counted.Add(p.Id)) continue;
                lately.TryGetValue(p.Faction, out float had);
                lately[p.Faction] = had + p.Strength;
            }

            // the castles and towns of each other realm, and which of the realm's own face it
            var foreign = s.Settlements.Where(h => Tools.World.IsFort(h) && h.Faction != null && h.Faction != fid).GroupBy(h => h.Faction).ToDictionary(g => g.Key, g => g.ToList());
            var facingEnemy = new HashSet<string>(StringComparer.Ordinal);
            string N(float x) => Text.N((int)Math.Round(x));

            float mine = v.Forces.Sum(p => p.Strength);
            List<string> enemies = me.AtWar.Where(e => s.Factions.ContainsKey(e)).OrderByDescending(Whole).ThenBy(e => FactionName(s, e), StringComparer.Ordinal).ToList();
            lines.Add("Your lords in the field: strength " + N(mine) + ". Your enemies' in all: "
                      + (enemies.Count == 0 ? "none (you are at war with no one)" : string.Join(", ", enemies.Select(e => FactionName(s, e) + " " + N(Whole(e))))) + ".");
            foreach (string e in enemies)
            {
                var line = new System.Text.StringBuilder(FactionName(s, e) + " (enemy; " + (Lords(e) > 0 ? Lords(e) + " lords, " : "") + "strength " + N(Whole(e)) + "): ");
                List<SettlementRow> theirs = foreign.TryGetValue(e, out List<SettlementRow> t) ? t : new List<SettlementRow>();
                var facing = new List<SettlementRow>();
                int theirsNear = 0;
                SettlementRow nearestTheirs = null, nearestMine = null;
                float nearest = float.MaxValue;
                foreach (SettlementRow o in theirs)
                {
                    bool close = false;
                    foreach (SettlementRow h in forts)
                    {
                        float g = Gap(h, o);
                        if (g < nearest) { nearest = g; nearestTheirs = o; nearestMine = h; }
                        if (g <= border) { close = true; if (!facing.Contains(h)) facing.Add(h); }
                    }
                    if (close) theirsNear++;
                }
                if (facing.Count > 0)
                {
                    foreach (SettlementRow h in facing) facingEnemy.Add(h.Id);
                    line.Append("it borders you: " + theirsNear + (theirsNear == 1 ? " castle or town of its lies" : " castles and towns of its lie") + " within two days' march of yours, the nearest "
                                + nearestTheirs.Name + ", about " + Text.D(nearest / perDay) + " days from your " + nearestMine.Name + ". Your castles and towns facing it: "
                                + string.Join(", ", facing.OrderBy(h => h.Name, StringComparer.Ordinal).Select(h => h.Name + " [" + h.Id + "]")) + ".");
                }
                else if (nearestTheirs != null)
                    line.Append("it does not border you: its nearest castle or town, " + nearestTheirs.Name + ", is about " + Text.D(nearest / perDay) + " days from your " + nearestMine.Name + ".");
                else line.Append("it holds no castle or town.");
                now.TryGetValue(e, out float n0);
                lately.TryGetValue(e, out float n1);
                line.Append(n0 + n1 <= 0 ? " None of its strength seen near your lands in the last three days."
                                         : " Of its strength, seen near your lands: " + (n0 > 0 ? N(n0) + " in view now" : "none in view now") + (n1 > 0 ? ", " + N(n1) + " more seen in the last three days" : "") + ".");
                lines.Add(line.ToString());
            }

            // the friendly and the neutral that border it
            var friends = new List<string>();
            var neutral = new List<string>();
            foreach (KeyValuePair<string, List<SettlementRow>> kv in foreign.OrderByDescending(kv => Whole(kv.Key)).ThenBy(kv => FactionName(s, kv.Key), StringComparer.Ordinal))
            {
                if (me.AtWar.Contains(kv.Key)) continue;
                if (!kv.Value.Any(o => forts.Any(h => Gap(h, o) <= border))) continue;
                string text = FactionName(s, kv.Key) + " (strength " + N(Whole(kv.Key)) + ")";
                if (me.Allies.Contains(kv.Key)) friends.Add(text); else neutral.Add(text);
            }
            if (friends.Count > 0) lines.Add("Your allies that border you: " + string.Join(", ", friends) + ".");
            if (neutral.Count > 0) lines.Add("Realms at peace with you that border you: " + string.Join(", ", neutral) + ".");
            List<SettlementRow> safe = forts.Where(h => !facingEnemy.Contains(h.Id)).ToList();
            lines.Add(safe.Count == 0 ? "Every one of your castles and towns has an enemy castle or town within two days' march."
                                      : "Your castles and towns with no enemy castle or town within two days' march (your heartland, and your borders with allies and realms at peace): "
                                        + string.Join(", ", safe.Select(h => h.Name + " [" + h.Id + "]")) + ".");
            return lines;
        }
    }
}
