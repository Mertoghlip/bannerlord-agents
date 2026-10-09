using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BannerlordAgents
{
    /// <summary>A realm's view of the world: what its ruler knows and sees. JSON in the logs, text for the agent.</summary>
    internal sealed class View
    {
        public string Faction, FactionName, Ruler, Date;
        public double Hours;
        public bool Night;
        public List<string> AtWar = new List<string>();
        public List<string> Allies = new List<string>();
        public List<ViewParty> Forces = new List<ViewParty>();
        public List<ViewHolding> Holdings = new List<ViewHolding>();
        public List<ViewParty> Seen = new List<ViewParty>();
        public int SeenNotListed;
        public List<ViewParty> LastSeen = new List<ViewParty>();
        public List<ViewHolding> Frontier = new List<ViewHolding>();
        public List<string> News = new List<string>();
        public int OthersInView;
    }

    internal sealed class ViewParty
    {
        public string Id, Lord, Name, Faction, FactionName, Relation, Kind, Doing, Near, SeenBy, NearestHolding;
        public int Men, ArmyMen, ArmyParties;
        public float Strength, X, Y, NearDistance, HoldingDistance;
        public double SeenHours;
    }

    internal sealed class ViewHolding
    {
        public string Id, Name, Kind, Faction, FactionName, Status;
        public int Garrison = -1;
        public int Militia;
        public float X, Y;
        public string Threat;
        public int ThreatMen;
        public float ThreatDistance;
    }

    /// <summary>Builds views and reports on the worker, from a snapshot. No game objects here.</summary>
    internal static class Views
    {
        public static View Build(Snapshot s, string fid, Dictionary<string, Sighting> sees, HashSet<string> hostile, Brain.Memory mem)
        {
            var v = new View { Faction = fid, FactionName = FactionName(s, fid), Hours = s.Hours, Date = s.Date, Night = s.Night };
            HashSet<string> allies = new HashSet<string>();
            if (s.Factions.TryGetValue(fid, out FactionRow fr))
            {
                v.Ruler = fr.Ruler;
                v.AtWar = fr.AtWar.Select(id => FactionName(s, id)).ToList();
                v.Allies = fr.Allies.Select(id => FactionName(s, id)).ToList();
                allies = new HashSet<string>(fr.Allies);
            }

            List<SettlementRow> own = s.Settlements.Where(h => h.Faction == fid).ToList();

            foreach (PartyRow r in s.Parties)
            {
                if (r.Faction != fid || r.Kind != "lord" || r.Attached) continue;
                v.Forces.Add(Party(s, r, null, "own", own));
            }
            v.Forces = v.Forces.OrderByDescending(x => x.ArmyMen > 0 ? x.ArmyMen : x.Men).ToList();

            foreach (KeyValuePair<string, Sighting> kv in sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r) || r.Attached) continue;
                bool enemy = hostile.Contains(r.Faction ?? "") || r.Kind == "bandit";
                if (!enemy && r.Kind != "lord") { v.OthersInView++; continue; }
                string relation = enemy ? "enemy" : allies.Contains(r.Faction ?? "") ? "ally" : "neutral";
                v.Seen.Add(Party(s, r, kv.Value, relation, own));
            }
            v.Seen = v.Seen.OrderBy(x => x.Relation == "enemy" ? 0 : 1).ThenBy(x => x.HoldingDistance).ToList();
            if (v.Seen.Count > 40) { v.SeenNotListed = v.Seen.Count - 40; v.Seen = v.Seen.Take(40).ToList(); }

            foreach (SettlementRow h in own)
            {
                ViewHolding vh = Holding(s, h, true);
                ViewParty threat = v.Seen.Where(x => x.Relation == "enemy" && x.Kind == "lord")
                                         .OrderBy(x => Dist(x.X, x.Y, h.X, h.Y)).FirstOrDefault();
                if (threat != null)
                {
                    vh.Threat = threat.Name;
                    vh.ThreatMen = threat.ArmyMen > 0 ? threat.ArmyMen : threat.Men;
                    vh.ThreatDistance = Text.R1(Dist(threat.X, threat.Y, h.X, h.Y));
                }
                v.Holdings.Add(vh);
            }
            v.Holdings = v.Holdings.OrderBy(x => x.Kind == "village" ? 1 : 0).ThenBy(x => x.Name).ToList();

            if (mem != null)
            {
                foreach (ViewParty last in mem.LastSeen.Values.OrderBy(x => s.Hours - x.SeenHours))
                {
                    if (sees.ContainsKey(last.Id) || s.Hours - last.SeenHours < 1) continue;
                    v.LastSeen.Add(last);
                    if (v.LastSeen.Count >= 25) break;
                }
                foreach (Notice n in mem.News.Where(n => s.Hours - n.Hours <= 48))
                    v.News.Add(Text.When(n.Hours) + ": " + n.Text);
                if (v.News.Count > 20) v.News = v.News.Skip(v.News.Count - 20).ToList();
            }

            if (own.Count > 0)
            {
                foreach (SettlementRow h in s.Settlements)
                {
                    if (h.Kind == "village" || h.Kind == "other" || h.Faction == null || !hostile.Contains(h.Faction)) continue;
                    float d = own.Min(o => Dist(o.X, o.Y, h.X, h.Y));
                    if (d > 60f) continue;
                    bool counted = s.InspectedByWatched != null && fid == s.Watched && s.InspectedByWatched.Contains(h.Id);
                    v.Frontier.Add(Holding(s, h, counted));
                }
                v.Frontier = v.Frontier.OrderBy(h => own.Min(o => Dist(o.X, o.Y, h.X, h.Y))).Take(20).ToList();
            }
            return v;
        }

        public static string Report(View v, List<Trigger> triggers)
        {
            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine("REPORT FOR " + v.FactionName.ToUpperInvariant());
            sb.AppendLine("Ruler: " + (v.Ruler ?? "unknown") + ". " + Text.When(v.Hours) + ", " + (v.Night ? "night." : "daytime."));
            sb.AppendLine("At war with: " + (v.AtWar.Count > 0 ? string.Join(", ", v.AtWar) : "no one") + ". Allies: " + (v.Allies.Count > 0 ? string.Join(", ", v.Allies) : "none") + ".");
            sb.AppendLine("Distances are in map units. A party is seen only within sight of your lords, castles and towns. Ids in [brackets] are what orders should name.");

            if (triggers != null && triggers.Count > 0)
            {
                sb.AppendLine().AppendLine("WHY YOU ARE ASKED NOW");
                foreach (Trigger t in triggers) sb.AppendLine("- " + t.Text);
            }

            sb.AppendLine().AppendLine("YOUR LORDS AND ARMIES (" + v.Forces.Count + ", " + Text.N(v.Forces.Sum(f => f.ArmyMen > 0 ? f.ArmyMen : f.Men)) + " men)");
            foreach (ViewParty f in v.Forces)
            {
                string size = f.ArmyMen > 0 ? "army of " + Text.N(f.ArmyMen) + " men in " + f.ArmyParties + " parties" : Text.N(f.Men) + " men";
                sb.AppendLine("- " + f.Name + Tag(f) + ": " + size + ", strength " + Text.N((int)f.Strength) + "; " + Where(f) + "; " + f.Doing + ".");
            }

            sb.AppendLine().AppendLine("YOUR HOLDINGS (" + v.Holdings.Count + ")");
            foreach (ViewHolding h in v.Holdings)
            {
                var line = new StringBuilder("- " + h.Name + " [" + h.Id + "] (" + h.Kind + ")");
                if (h.Kind != "village") line.Append(": garrison " + Text.N(Math.Max(0, h.Garrison)) + ", militia " + Text.N(h.Militia));
                if (!string.IsNullOrEmpty(h.Status)) line.Append("; " + h.Status);
                if (h.Threat != null) line.Append("; nearest enemy in view: " + h.Threat + ", " + Text.N(h.ThreatMen) + " men at " + Text.D(h.ThreatDistance));
                sb.AppendLine(line.Append('.').ToString());
            }

            sb.AppendLine().AppendLine("IN VIEW (" + v.Seen.Count + (v.SeenNotListed > 0 ? ", and " + v.SeenNotListed + " more further off" : "") + ")");
            if (v.Seen.Count == 0) sb.AppendLine("- No lords or enemies in view.");
            foreach (ViewParty p in v.Seen)
            {
                string size = p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) + " men" : Text.N(p.Men) + " men";
                string where = p.NearestHolding == null ? Where(p)
                             : Text.D(p.HoldingDistance) + " from your " + p.NearestHolding + (p.Near != p.NearestHolding ? " (" + Where(p) + ")" : "");
                sb.AppendLine("- " + p.Relation.ToUpperInvariant() + " " + p.Name + Tag(p) + " (" + p.FactionName + "): " + size + ", strength " + Text.N((int)p.Strength)
                    + "; " + where + "; " + p.Doing + "; seen by " + p.SeenBy + ".");
            }
            if (v.OthersInView > 0) sb.AppendLine("- Also in view: " + v.OthersInView + " caravans, villagers and other small parties.");

            if (v.LastSeen.Count > 0)
            {
                sb.AppendLine().AppendLine("SEEN EARLIER, NOW OUT OF SIGHT (" + v.LastSeen.Count + ")");
                foreach (ViewParty p in v.LastSeen)
                    sb.AppendLine("- " + p.Name + Tag(p) + " (" + p.FactionName + "): " + Text.N(p.ArmyMen > 0 ? p.ArmyMen : p.Men) + " men, last seen " + Where(p)
                        + ", " + Math.Round(v.Hours - p.SeenHours) + " hours ago, " + p.Doing + ".");
            }

            if (v.Frontier.Count > 0)
            {
                sb.AppendLine().AppendLine("ENEMY HOLDINGS NEAR YOUR LANDS (" + v.Frontier.Count + ")");
                foreach (ViewHolding h in v.Frontier)
                    sb.AppendLine("- " + h.Name + " [" + h.Id + "] (" + h.Kind + ", " + h.FactionName + "): " + (h.Garrison >= 0 ? "garrison " + Text.N(h.Garrison) : "garrison not in sight")
                        + (string.IsNullOrEmpty(h.Status) ? "" : "; " + h.Status) + ".");
            }

            if (v.News.Count > 0)
            {
                sb.AppendLine().AppendLine("NEWS, LAST TWO DAYS");
                foreach (string n in v.News) sb.AppendLine("- " + n);
            }
            return sb.ToString();
        }

        /// <summary>The player's realm, compactly, for each recorded decision.</summary>
        public static object Realm(Snapshot s, string fid)
        {
            var forces = s.Parties.Where(r => r.Faction == fid && r.Kind == "lord" && !r.Attached)
                .Select(r => new { r.Id, r.Name, r.Men, r.ArmyMen, r.X, r.Y, Doing = Doing(s, r) }).ToList();
            var holdings = s.Settlements.Where(h => h.Faction == fid && h.Kind != "village")
                .Select(h => new { h.Id, h.Name, h.Kind, h.Garrison, Status = Status(s, h) }).ToList();
            return new { Forces = forces, Holdings = holdings };
        }

        public static ViewParty Party(Snapshot s, PartyRow r, Sighting seen, string relation, List<SettlementRow> own)
        {
            var vp = new ViewParty
            {
                Id = r.Id, Lord = r.Leader, Name = r.Name ?? r.Id, Faction = r.Faction, FactionName = FactionName(s, r.Faction), Relation = relation,
                Kind = r.Kind, Men = r.Men, ArmyMen = r.ArmyMen, ArmyParties = r.ArmyParties, Strength = r.Strength,
                X = r.X, Y = r.Y, Doing = Doing(s, r), SeenBy = seen?.By, SeenHours = s.Hours
            };
            SettlementRow near = NearestSettlement(s, r.X, r.Y, out float nd);
            if (near != null) { vp.Near = near.Name; vp.NearDistance = Text.R1(nd); }
            if (own != null && own.Count > 0 && relation != "own")
            {
                SettlementRow h = own.OrderBy(o => Dist(o.X, o.Y, r.X, r.Y)).First();
                vp.NearestHolding = h.Name;
                vp.HoldingDistance = Text.R1(Dist(h.X, h.Y, r.X, r.Y));
            }
            return vp;
        }

        public static ViewHolding Holding(Snapshot s, SettlementRow h, bool counted)
        {
            return new ViewHolding
            {
                Id = h.Id, Name = h.Name, Kind = h.Kind, Faction = h.Faction, FactionName = FactionName(s, h.Faction),
                Garrison = counted ? h.Garrison : -1, Militia = h.Militia, X = h.X, Y = h.Y, Status = Status(s, h)
            };
        }

        public static string Status(Snapshot s, SettlementRow h)
        {
            if (h.BesiegedBy != null)
            {
                string who = h.Besieger != null && s.PartyById.TryGetValue(h.Besieger, out PartyRow b) ? b.Name ?? b.Id : "?";
                return "under siege by " + who + " (" + FactionName(s, h.BesiegedBy) + ")";
            }
            if (h.VillageState == "BeingRaided") return "being raided";
            if (h.VillageState == "Looted") return "looted";
            return null;
        }

        public static string Doing(Snapshot s, PartyRow r)
        {
            if (r.Raiding != null) return "raiding " + SName(s, r.Raiding);
            if (r.InBattle) return "fighting";
            if (r.Besieging != null) return "besieging " + SName(s, r.Besieging);
            if (r.InSettlement != null) return "inside " + SName(s, r.InSettlement);
            switch (r.Doing)
            {
                case "GoToSettlement": return "going to " + SName(s, r.TargetSettlement);
                case "BesiegeSettlement": return "marching to besiege " + SName(s, r.TargetSettlement);
                case "AssaultSettlement": return "assaulting " + SName(s, r.TargetSettlement);
                case "RaidSettlement": return "on the way to raid " + SName(s, r.TargetSettlement);
                case "DefendSettlement": return "defending " + SName(s, r.TargetSettlement);
                case "PatrolAroundPoint": return r.TargetSettlement != null ? "patrolling around " + SName(s, r.TargetSettlement) : "patrolling";
                case "EngageParty": return "chasing " + PName(s, r.TargetParty);
                case "EscortParty": return "following " + PName(s, r.TargetParty);
                case "JoinParty": return "joining " + PName(s, r.TargetParty);
                case "GoAroundParty":
                case "FleeToPoint":
                case "FleeToGate":
                case "FleeToParty": return "fleeing";
                case "GoToPoint": return "on the move";
                case "Hold": return "holding position";
                default: return "idle";
            }
        }

        private static string Tag(ViewParty p) => " [" + (p.Lord ?? p.Id) + "]";

        private static string Where(ViewParty p) => p.Near != null ? (p.NearDistance < 1f ? "at " + p.Near : Text.D(p.NearDistance) + " from " + p.Near) : "in the wilds";

        public static SettlementRow NearestSettlement(Snapshot s, float x, float y, out float distance)
        {
            SettlementRow best = null;
            distance = float.MaxValue;
            foreach (SettlementRow h in s.Settlements)
            {
                float d = Dist(h.X, h.Y, x, y);
                if (d < distance) { distance = d; best = h; }
            }
            return best;
        }

        public static string FactionName(Snapshot s, string id)
        {
            if (id == null) return "no realm";
            return s.FactionNames.TryGetValue(id, out string n) && !string.IsNullOrEmpty(n) ? n : id;
        }

        private static string SName(Snapshot s, string id) => id != null && s.SettlementById.TryGetValue(id, out SettlementRow h) ? h.Name : "somewhere";
        private static string PName(Snapshot s, string id) => id != null && s.PartyById.TryGetValue(id, out PartyRow p) ? p.Name ?? "a party" : "a party";
        public static float Dist(float x1, float y1, float x2, float y2) => (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    }
}
