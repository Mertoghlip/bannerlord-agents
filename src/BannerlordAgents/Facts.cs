using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BannerlordAgents.Tools;

namespace BannerlordAgents
{
    /// <summary>
    /// The arithmetic done for the agent, from the realm's own view (the same fog as the report):
    /// - READY-MADE FACTS, in each poke: for what the poke is about (a holding, an enemy), the realm's lords who can get
    ///   there soonest, with their strength and whether they are free, and the enemies around it;
    /// - the look-up files (channel\lookups\&lt;id&gt;.txt), one per holding, lord and enemy the realm knows, for details on
    ///   request: travel hours to every holding, who is near, where an enemy was last seen.
    /// Travel hours are rough: straight-line distance over the lord's speed. Worker thread.
    /// </summary>
    internal static class Facts
    {
        private sealed class Lord
        {
            public PartyRow Row;
            public string Name, Tag, Order;
            public double Strength;
        }

        private static List<Lord> OwnLords(World w)
        {
            var list = new List<Lord>();
            foreach (PartyRow r in w.S.Parties)
            {
                if (r.Faction != w.Realm || r.Kind != "lord" || r.Attached) continue;
                ToolInstance t = Runner.ForParty(r.Id);
                list.Add(new Lord
                {
                    Row = r, Name = r.Name ?? r.Id, Tag = r.Leader ?? r.Id, Strength = World.StrengthOf(r),
                    Order = t != null ? "your order: " + t.Def.Name + (t.Order.At != null ? " " + w.Name(t.Order.At) : t.Order.Target != null ? " " + w.Name(t.Order.Target) : "") : "free"
                });
            }
            return list;
        }

        private static double Hours(World w, PartyRow lord, string place)
        {
            double d = w.Distance(lord.Id, place);
            return double.IsNaN(d) ? double.NaN : d / World.Speed(lord);
        }

        private static string H(double hours) => double.IsNaN(hours) ? "?" : hours < 1 ? "under an hour" : "about " + Math.Round(hours) + " h";

        private static string LordLine(World w, Lord l, string place)
            => l.Name + " [" + l.Tag + "] (strength " + Text.N((int)l.Strength) + ", " + H(Hours(w, l.Row, place)) + ", " + l.Order + ")";

        private static string EnemyLine(World w, PartyRow e, string place)
        {
            double d = w.Distance(e.Id, place);
            return w.Name(e.Id) + " [" + w.Tag(e.Id) + "] (strength " + Text.N((int)World.StrengthOf(e)) + ", " + (double.IsNaN(d) ? "?" : Text.D((float)d)) + " away, "
                   + Views.Doing(w.S, e) + ")";
        }

        /// <summary>The READY-MADE FACTS section: up to four subjects of this poke, then the realm's lords in one line.</summary>
        public static string ForPoke(World w, List<Trigger> triggers)
        {
            List<Lord> lords = OwnLords(w);
            var sb = new StringBuilder();
            var done = new HashSet<string>();
            int subjects = 0;
            foreach (Trigger t in triggers ?? new List<Trigger>())
            {
                if (subjects >= 4) break;
                string place = t.Place;
                SettlementRow h = w.Holding(place);
                if (h != null && done.Add(place))
                {
                    subjects++;
                    List<string> near = lords.Where(l => !double.IsNaN(Hours(w, l.Row, place))).OrderBy(l => Hours(w, l.Row, place)).Take(5).Select(l => LordLine(w, l, place)).ToList();
                    List<string> foes = w.EnemiesNear(place, 40).Take(3).Select(e => EnemyLine(w, e, place)).ToList();
                    sb.AppendLine("- Near " + h.Name + " [" + h.Id + "]: your nearest lords " + (near.Count > 0 ? string.Join("; ", near) : "none") + "."
                                  + (foes.Count > 0 ? " Enemies in view within 40: " + string.Join("; ", foes) + "." : " No enemy in view within 40."));
                }
                PartyRow p = w.Party(t.Party);
                if (p != null && p.Faction != w.Realm && w.HostileFaction(p.Faction) && done.Add(p.Id))
                {
                    p = w.Lead(p);
                    subjects++;
                    string where = w.InView(p.Id) ? "in view" : "last seen " + Math.Round(w.SeenAgo(p.Id)) + " hours ago";
                    SettlementRow home = w.S.Settlements.Where(x => x.Faction == w.Realm).OrderBy(x => w.Distance(p.Id, x.Id)).FirstOrDefault();
                    List<string> near = lords.Where(l => !double.IsNaN(Hours(w, l.Row, p.Id))).OrderBy(l => Hours(w, l.Row, p.Id)).Take(5).Select(l => LordLine(w, l, p.Id)).ToList();
                    double? str = w.KnownStrength(p.Id);
                    sb.AppendLine("- " + w.Name(p.Id) + " [" + w.Tag(p.Id) + "] (" + where + ", strength " + (str.HasValue ? Text.N((int)str.Value) : "?")
                                  + (home != null ? ", " + Text.D((float)w.Distance(p.Id, home.Id)) + " from your " + home.Name : "") + "): your lords who can reach it soonest "
                                  + (near.Count > 0 ? string.Join("; ", near) : "none") + ".");
                }
            }
            List<Lord> free = lords.Where(l => l.Order == "free").ToList();
            sb.AppendLine("- Your lords leading parties: " + lords.Count + "; free: " + free.Count + " (strength " + Text.N((int)free.Sum(l => l.Strength))
                          + "); holding your orders: " + (lords.Count - free.Count) + ". Travel times are rough: distance over the lord's speed.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The look-up files: one per holding of the realm, lord of the realm, and enemy it knows of.</summary>
        public static Dictionary<string, string> Lookups(World w, Dictionary<string, ViewParty> lastSeen)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<Lord> lords = OwnLords(w);
            List<SettlementRow> own = w.S.Settlements.Where(x => x.Faction == w.Realm).ToList();

            foreach (SettlementRow h in own)
            {
                var sb = new StringBuilder();
                sb.AppendLine(h.Name + " [" + h.Id + "], your " + h.Kind + (w.Besieged(h) ? ", UNDER SIEGE" : "") + (h.VillageState == "BeingRaided" ? ", being raided" : h.VillageState == "Looted" ? ", looted" : "")
                              + (World.IsFort(h) ? "; garrison " + Text.N(h.Garrison) + ", militia " + Text.N(h.Militia) : "") + ". As of " + Text.When(w.Hours) + ".");
                sb.AppendLine("Your lords, soonest first:");
                foreach (Lord l in lords.OrderBy(l => Hours(w, l.Row, h.Id)).Take(15)) sb.AppendLine("- " + LordLine(w, l, h.Id));
                List<PartyRow> foes = w.EnemiesNear(h.Id, 60);
                sb.AppendLine(foes.Count > 0 ? "Enemies in view within 60:" : "No enemy in view within 60.");
                foreach (PartyRow e in foes.Take(12)) sb.AppendLine("- " + EnemyLine(w, e, h.Id));
                List<SettlementRow> hostile = w.S.Settlements.Where(x => World.IsFort(x) && w.HostileFaction(x.Faction)).OrderBy(x => w.Distance(h.Id, x.Id))
                                               .Where(x => w.Distance(h.Id, x.Id) <= 60).Take(5).ToList();
                if (hostile.Count > 0) sb.AppendLine("Enemy castles and towns within 60: " + string.Join("; ", hostile.Select(x => x.Name + " [" + x.Id + "] " + Text.D((float)w.Distance(h.Id, x.Id)) + " away")) + ".");
                files[h.Id] = sb.ToString();
            }

            foreach (Lord l in lords)
            {
                PartyRow r = l.Row;
                var sb = new StringBuilder();
                string ranks = r.SizeLimit > 0 ? ", ranks " + Math.Round(100.0 * r.Men / r.SizeLimit) + "%" : "";
                string wounded = r.Men + r.Wounded > 0 ? ", wounded " + Math.Round(100.0 * r.Wounded / (r.Men + r.Wounded)) + "%" : "";
                sb.AppendLine(l.Name + " [" + l.Tag + "]: " + Text.N(World.MenOf(r)) + " men" + (r.ArmyMen > 0 ? " in an army of " + r.ArmyParties + " parties" : "") + ", strength " + Text.N((int)l.Strength)
                              + ranks + wounded + (r.FoodDays >= 0 ? ", food for " + r.FoodDays + " days" : "") + ", speed " + Text.D(World.Speed(r)) + "; " + Views.Doing(w.S, r) + "; " + l.Order
                              + ". As of " + Text.When(w.Hours) + ".");
                sb.AppendLine("Hours to your holdings, nearest first:");
                foreach (SettlementRow h in own.OrderBy(h => Hours(w, r, h.Id)).Take(15)) sb.AppendLine("- " + h.Name + " [" + h.Id + "]: " + H(Hours(w, r, h.Id)));
                List<PartyRow> foes = w.EnemiesNear(r.Id, 60);
                sb.AppendLine(foes.Count > 0 ? "Enemies in view within 60 of this lord:" : "No enemy in view within 60 of this lord.");
                foreach (PartyRow e in foes.Take(10)) sb.AppendLine("- " + EnemyLine(w, e, r.Id) + ", " + H(Hours(w, r, e.Id)) + " to reach");
                files[l.Tag] = sb.ToString();
            }

            var known = new List<KeyValuePair<string, ViewParty>>();
            foreach (PartyRow r in w.S.Parties)
                if (r.Kind == "lord" && !r.Attached && w.HostileFaction(r.Faction) && w.InView(r.Id)) known.Add(new KeyValuePair<string, ViewParty>(r.Id, null));
            if (lastSeen != null)
                foreach (KeyValuePair<string, ViewParty> kv in lastSeen)
                    if (kv.Value.Relation == "enemy" && !w.InView(kv.Key)) known.Add(kv);
            foreach (KeyValuePair<string, ViewParty> kv in known)
            {
                string id = kv.Key;
                string tag = w.Tag(id);
                if (files.ContainsKey(tag)) continue;
                var sb = new StringBuilder();
                PartyRow r = w.Party(id);
                double? str = w.KnownStrength(id);
                int? men = w.KnownMen(id);
                string state = kv.Value == null ? "in view now, " + Views.Doing(w.S, r) : "last seen " + Math.Round(w.Hours - kv.Value.SeenHours) + " hours ago, " + kv.Value.Doing;
                sb.AppendLine(w.Name(id) + " [" + tag + "] (" + (kv.Value?.FactionName ?? Views.FactionName(w.S, r?.Faction)) + "): " + (men.HasValue ? Text.N(men.Value) + " men, " : "")
                              + "strength " + (str.HasValue ? Text.N((int)str.Value) : "?") + "; " + state + ". As of " + Text.When(w.Hours) + ".");
                sb.AppendLine("Your holdings nearest to it:");
                foreach (SettlementRow h in own.OrderBy(h => w.Distance(id, h.Id)).Take(5)) sb.AppendLine("- " + h.Name + " [" + h.Id + "]: " + Text.D((float)w.Distance(id, h.Id)) + " away");
                sb.AppendLine("Your lords who can reach it soonest:");
                foreach (Lord l in lords.OrderBy(l => Hours(w, l.Row, id)).Take(8)) sb.AppendLine("- " + LordLine(w, l, id));
                files[tag] = sb.ToString();
            }

            files["README"] = "Look-ups for " + Views.FactionName(w.S, w.Realm) + ", as of " + Text.When(w.Hours) + ", rewritten with each poke.\n"
                              + "One file per id you see in [brackets]: lookups/<id>.txt.\n"
                              + "- a holding of yours: your lords soonest to it, enemies near it, enemy castles and towns near it;\n"
                              + "- a lord of yours: men, strength, ranks, food, hours to each of your holdings, enemies near the lord;\n"
                              + "- an enemy: where it is or was last seen, its strength, your holdings near it, your lords who can reach it soonest.\n"
                              + "Travel hours are rough: straight-line distance over the lord's speed.\n";
            return files;
        }
    }
}
