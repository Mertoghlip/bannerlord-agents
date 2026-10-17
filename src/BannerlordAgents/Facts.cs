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
    /// Travel hours: the distance by road (Roads; else as the crow flies) over the lord's pace on open ground. Worker thread.
    /// </summary>
    internal static class Facts
    {
        private sealed class Lord
        {
            public PartyRow Row;
            public string Name, Tag, Order;
            public double Strength;
            /// <summary>Its men, with its whole army's when it leads one.</summary>
            public int Men;
            /// <summary>Cannot be called into another lord's army: it rules the realm, or it is already in an army.</summary>
            public bool Uncallable;
        }

        private static List<Lord> OwnLords(World w)
        {
            var list = new List<Lord>();
            // the ruler cannot be called into another lord's army (the ruling clan's leader rules the realm)
            string rulerClan = w.Realm == w.S.Watched ? w.S.Clans.FirstOrDefault(c => c.Ruler)?.Leader : null;
            foreach (PartyRow r in w.S.Parties)
            {
                if (r.Faction != w.Realm || r.Kind != "lord" || r.Attached) continue;
                ToolInstance t = Runner.ForParty(r.Id);
                list.Add(new Lord
                {
                    Row = r, Name = r.Name ?? r.Id, Tag = r.Leader ?? r.Id, Strength = World.StrengthOf(r), Men = World.MenOf(r),
                    Uncallable = r.ArmyLeader != null || (rulerClan != null && r.Leader == rulerClan),
                    Order = t != null ? "your order: " + t.Def.Name + (t.Order.At != null ? " " + w.Name(t.Order.At) : t.Order.Target != null ? " " + w.Name(t.Order.Target) : "") : "free"
                });
            }
            return list;
        }

        // ---------- the muster ----------

        private static readonly double[] Bands = { 24, 48, 72 };
        private static readonly string[] BandNames = { "within a day", "within 2 days", "within 3 days" };

        /// <summary>Within the game's range for calling a lord into an army led from this place (straight line, so a little generous).</summary>
        private static bool AtOnce(World w, Lord l, string place)
        {
            if (l.Uncallable || w.S.CallRange <= 0) return false;
            double d = w.Distance(l.Row.Id, place);
            return !double.IsNaN(d) && d <= w.S.CallRange;
        }

        /// <summary>
        /// The muster at a place in one line: the realm's lords who could be there within one, two and three days, as running
        /// totals (lords, men, strength), with how many of the first day's a leader there could call into an army at once.
        /// </summary>
        public static string MusterLine(World w, string place)
        {
            List<Lord> lords = OwnLords(w).Where(l => !double.IsNaN(Hours(w, l.Row, place))).ToList();
            var parts = new List<string>();
            for (int b = 0; b < Bands.Length; b++)
            {
                List<Lord> within = lords.Where(l => Hours(w, l.Row, place) <= Bands[b]).ToList();
                string atOnce = b == 0 ? " (" + within.Count(l => AtOnce(w, l, place)) + " can be called at once)" : "";
                parts.Add(BandNames[b] + " " + within.Count + " lords, " + Text.N(within.Sum(l => l.Men)) + " men, strength " + Text.N((int)within.Sum(l => l.Strength)) + atOnce);
            }
            return string.Join("; ", parts) + "; all " + lords.Count + ": " + Text.N(lords.Sum(l => l.Men)) + " men, strength " + Text.N((int)lords.Sum(l => l.Strength));
        }

        /// <summary>
        /// The muster at a place in full: every lord of the realm leading a party, by how soon they could be there, with the
        /// running totals at one, two and three days. * marks a lord a leader there could call into an army at once; the
        /// others must march first (a join order does both). An army counts with its leader, with all its men.
        /// </summary>
        public static List<string> Muster(World w, string place)
        {
            var lines = new List<string>();
            List<Lord> lords = OwnLords(w).Where(l => !double.IsNaN(Hours(w, l.Row, place))).OrderBy(l => Hours(w, l.Row, place)).ToList();
            int b = 0, count = 0, men = 0;
            double strength = 0;
            var band = new List<string>();
            void Close(string name)
            {
                lines.Add(name + ": " + count + " lords, " + Text.N(men) + " men, strength " + Text.N((int)strength) + " in all"
                          + (band.Count > 0 ? "; adds " + string.Join("; ", band) : "; adds no one") + ".");
                band.Clear();
            }
            foreach (Lord l in lords)
            {
                double h = Hours(w, l.Row, place);
                while (b < Bands.Length && h > Bands[b]) Close(BandNames[b++]);
                count++;
                men += l.Men;
                strength += l.Strength;
                band.Add(l.Name + (AtOnce(w, l, place) ? "*" : "") + " [" + l.Tag + "] " + Text.N(l.Men) + " men, strength " + Text.N((int)l.Strength) + ", " + H(h)
                         + (l.Row.ArmyLeader == l.Row.Id ? ", leads an army" : l.Row.ArmyLeader != null ? ", in an army" : "") + ", " + l.Order);
            }
            while (b < Bands.Length) Close(BandNames[b++]);
            if (band.Count > 0) Close("later");
            return lines;
        }

        /// <summary>Hours to march there at the lord's pace on open ground: by road where the game's table knows the way (0.7.1), else as the crow flies.</summary>
        private static double Hours(World w, PartyRow lord, string place)
        {
            double d = w.RoadDistance(lord.Id, place);
            if (double.IsNaN(d)) d = w.Distance(lord.Id, place);
            return double.IsNaN(d) ? double.NaN : d / World.Speed(lord);
        }

        /// <summary>"12 (14 by road)" between two things the realm knows of.</summary>
        private static string Far(World w, string a, string b)
        {
            double d = w.Distance(a, b);
            return double.IsNaN(d) ? "?" : Roads.Both((float)d, (float)w.RoadDistance(a, b));
        }

        /// <summary>For ordering: by road where known, else as the crow flies.</summary>
        private static double Reach(World w, string a, string b)
        {
            double r = w.RoadDistance(a, b);
            return double.IsNaN(r) ? w.Distance(a, b) : r;
        }

        private static string H(double hours) => double.IsNaN(hours) ? "?" : hours < 1 ? "under an hour" : "about " + Math.Round(hours) + " h";

        private static string LordLine(World w, Lord l, string place)
            => l.Name + " [" + l.Tag + "] (strength " + Text.N((int)l.Strength) + ", " + H(Hours(w, l.Row, place)) + ", " + l.Order + ")";

        private static string EnemyLine(World w, PartyRow e, string place)
        {
            return w.Name(e.Id) + " [" + w.Tag(e.Id) + "] (strength " + Text.N((int)World.StrengthOf(e)) + ", " + Far(w, e.Id, place) + " away, "
                   + Views.Doing(w.S, e) + ")";
        }

        /// <summary>The READY-MADE FACTS section: up to four subjects of this poke, then the realm's lords in one line.</summary>
        public static string ForPoke(World w, List<Trigger> triggers)
        {
            List<Lord> lords = OwnLords(w);
            var sb = new StringBuilder();
            var done = new HashSet<string>();
            int subjects = 0;
            bool mustered = false;
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
                    if (h.Faction == w.Realm && w.Besieged(h))
                    {
                        double? inside = w.InsideStrength(h);
                        if (inside.HasValue)
                            sb.AppendLine("- Inside " + h.Name + ": the garrison and your lords there, strength " + Text.N((int)inside.Value)
                                          + ". They sally out to join a relief only when they and the relief together are more than 1.5 times the besiegers' strength ("
                                          + Text.N((int)(w.SiegeStrength(h) ?? 0)) + "); militia never sally.");
                    }
                    if (!mustered)
                    {
                        // the muster for the first place this poke is about: how much of the realm could be there, and how soon
                        mustered = true;
                        sb.AppendLine("- Muster at " + h.Name + " (running totals, rough): " + MusterLine(w, place) + ". The full list: lookups/" + h.Id + ".txt.");
                    }
                }
                PartyRow p = w.Party(t.Party);
                if (p != null && p.Faction != w.Realm && w.HostileFaction(p.Faction) && done.Add(p.Id))
                {
                    p = w.Lead(p);
                    subjects++;
                    string where = w.InView(p.Id) ? "in view" : "last seen " + Math.Round(w.SeenAgo(p.Id)) + " hours ago";
                    SettlementRow home = w.S.Settlements.Where(x => x.Faction == w.Realm).OrderBy(x => Reach(w, p.Id, x.Id)).FirstOrDefault();
                    List<string> near = lords.Where(l => !double.IsNaN(Hours(w, l.Row, p.Id))).OrderBy(l => Hours(w, l.Row, p.Id)).Take(5).Select(l => LordLine(w, l, p.Id)).ToList();
                    double? str = w.KnownStrength(p.Id);
                    sb.AppendLine("- " + w.Name(p.Id) + " [" + w.Tag(p.Id) + "] (" + where + ", strength " + (str.HasValue ? Text.N((int)str.Value) : "?")
                                  + (home != null ? ", " + Far(w, p.Id, home.Id) + " from your " + home.Name : "") + "): your lords who can reach it soonest "
                                  + (near.Count > 0 ? string.Join("; ", near) : "none") + ".");
                }
            }
            List<Lord> free = lords.Where(l => l.Order == "free").ToList();
            sb.AppendLine("- Your lords leading parties: " + lords.Count + "; free: " + free.Count + " (strength " + Text.N((int)free.Sum(l => l.Strength))
                          + "); holding your orders: " + (lords.Count - free.Count) + ". Travel times are rough: distance over the lord's speed.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The look-up files: one per holding of the realm, lord of the realm, and enemy it knows of.</summary>
        public static Dictionary<string, string> Lookups(World w, Dictionary<string, ViewParty> lastSeen, Dictionary<string, ViewTrail> trails = null)
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
                if (hostile.Count > 0) sb.AppendLine("Enemy castles and towns within 60: " + string.Join("; ", hostile.Select(x => x.Name + " [" + x.Id + "] " + Far(w, h.Id, x.Id) + " away")) + ".");
                if (World.IsFort(h))
                {
                    sb.AppendLine("Muster here: every lord of yours by how soon they could be here, with running totals. * = a leader here could call them into an army at once; the others march first (gather and add send them to join by themselves):");
                    foreach (string line in Muster(w, h.Id)) sb.AppendLine("- " + line);
                }
                files[h.Id] = sb.ToString();
            }

            // the realm's lords held prisoner: one file each, and all of them by where they are held
            List<ViewCaptive> captives = w.S.Captives.Select(c => Views.Captive(w.S, c)).ToList();
            for (int i = 0; i < captives.Count; i++)
            {
                ViewCaptive c = captives[i];
                CaptiveRow row = w.S.Captives[i];
                var sb = new StringBuilder();
                string where = c.Holding != null ? "held in " + (c.HoldingName ?? c.Holding) + " [" + c.Holding + "], " + (c.HoldingKind ?? "holding") + " of " + (c.HoldingFaction ?? "?")
                             : "held by " + (c.PartyName ?? "a party") + (c.PartyLord != null ? " [" + c.PartyLord + "]" : "") + " (" + (c.PartyFaction ?? "?") + ")";
                sb.AppendLine(c.Name + " [" + c.Hero + "], your lord, is a prisoner: " + where + (double.IsNaN(c.Since) ? "" : ", since " + Text.When(c.Since)) + ". As of " + Text.When(w.Hours) + ".");
                // where to go for them: the holding, or the party holding them if it is in sight
                string at = row.Holding ?? (row.Party != null && w.InView(row.Party) ? row.Party : null);
                if (at != null)
                {
                    sb.AppendLine("Your lords who can reach " + (c.Holding != null ? "that holding" : "that party") + " soonest:");
                    foreach (Lord l in lords.Where(l => !double.IsNaN(Hours(w, l.Row, at))).OrderBy(l => Hours(w, l.Row, at)).Take(8)) sb.AppendLine("- " + LordLine(w, l, at));
                }
                else sb.AppendLine("Where that party is now is not in your sight.");
                files[c.Hero] = sb.ToString();
            }
            files["captured"] = captives.Count == 0 ? "None of your lords is a prisoner. As of " + Text.When(w.Hours) + ".\n"
                : "Your lords held prisoner, by where they are held, as of " + Text.When(w.Hours) + ":\n" + string.Concat(Views.CaptiveLines(captives).Select(l => "- " + l + "\n"));

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
                if (trails != null && trails.TryGetValue(id, out ViewTrail trail) && trail.Points.Count >= 2)
                {
                    sb.AppendLine("Its trail as your realm saw it, in the last two days, oldest first:");
                    foreach (string step in Views.TrailSteps(trail)) sb.AppendLine("- " + step);
                }
                sb.AppendLine("Your holdings nearest to it:");
                foreach (SettlementRow h in own.OrderBy(h => Reach(w, id, h.Id)).Take(5)) sb.AppendLine("- " + h.Name + " [" + h.Id + "]: " + Far(w, id, h.Id) + " away");
                sb.AppendLine("Your lords who can reach it soonest:");
                foreach (Lord l in lords.OrderBy(l => Hours(w, l.Row, id)).Take(8)) sb.AppendLine("- " + LordLine(w, l, id));
                files[tag] = sb.ToString();
            }

            files["README"] = "Look-ups for " + Views.FactionName(w.S, w.Realm) + ", as of " + Text.When(w.Hours) + ", rewritten with each poke.\n"
                              + "One file per id you see in [brackets]: lookups/<id>.txt.\n"
                              + "- a holding of yours: your lords soonest to it, enemies near it, enemy castles and towns near it, and for a castle or town the muster there (every lord by how soon they could come, with running totals);\n"
                              + "- a lord of yours: men, strength, ranks, food, hours to each of your holdings, enemies near the lord;\n"
                              + "- a lord of yours held prisoner: where and since when, and your lords who can reach that place soonest;\n"
                              + "- an enemy: where it is or was last seen, its strength, for an army its trail over the last two days, your holdings near it, your lords who can reach it soonest.\n"
                              + "lookups/captured.txt lists all your lords held prisoner, by where they are held.\n"
                              + "Travel hours are by road where the game knows the way (else as the crow flies), over the lord's pace on open ground; forest, fords and night slow a march further.\n";
            return files;
        }

        // ---------- the council of war ----------

        /// <summary>
        /// The briefing for a council of war (council.txt in the channel, rewritten every in-game hour): the whole realm at
        /// once, which a poke's report gives only in part. The purse (the ruler's clan's gold, every clan's influence), every
        /// lord with ranks, food and whose judgment it follows, the muster at each castle and town, and the captured lords.
        /// The war room puts the hourly report after it. Same fog as the report: only the realm's own affairs.
        /// </summary>
        public static string Council(World w)
        {
            Snapshot s = w.S;
            List<Lord> lords = OwnLords(w);
            var sb = new StringBuilder();
            sb.AppendLine("COUNCIL BRIEFING FOR " + Views.FactionName(s, w.Realm).ToUpperInvariant() + ", " + Text.When(w.Hours));
            sb.AppendLine("The whole realm at once: your purse, every lord, the muster at each of your castles and towns, and your captured lords. The situation report follows it.");

            sb.AppendLine().AppendLine("YOUR PURSE");
            ClanRow mine = s.Clans.FirstOrDefault(c => c.Ruler);
            if (mine != null) sb.AppendLine("- Your own clan (" + mine.Name + "): gold " + Text.N(mine.Gold) + ", influence " + Text.N((int)mine.Influence) + ".");
            else sb.AppendLine("- Not known this hour.");
            List<ClanRow> others = s.Clans.Where(c => !c.Ruler).OrderByDescending(c => c.Influence).ToList();
            if (others.Count > 0)
                sb.AppendLine("- Influence of your other clans (an army's leader's clan pays influence for every lord called into it): "
                              + string.Join("; ", others.Select(c => c.Name + " " + Text.N((int)c.Influence))) + ".");

            sb.AppendLine().AppendLine("EVERY LORD (" + lords.Count + "; an army counts with its leader; \"free\" lords are led by the game's own AI, not by you)");
            var clanNames = s.Clans.ToDictionary(c => c.Id, c => c);
            foreach (Lord l in lords.OrderByDescending(l => l.Men))
            {
                PartyRow r = l.Row;
                string ranks = r.SizeLimit > 0 ? " of " + Text.N(r.SizeLimit) + " (" + Math.Round(100.0 * r.Men / r.SizeLimit) + "%)" : "";
                string army = r.ArmyLeader == r.Id ? ", leads an army of " + Text.N(r.ArmyMen) + " men in " + r.ArmyParties + " parties" : r.ArmyLeader != null ? ", in " + w.Name(r.ArmyLeader) + "'s army" : "";
                string clan = r.Clan != null && clanNames.TryGetValue(r.Clan, out ClanRow cr) ? "; clan " + cr.Name + (cr.Ruler ? " (yours)" : "") + ", influence " + Text.N((int)cr.Influence) : "";
                sb.AppendLine("- " + l.Name + " [" + l.Tag + "]: " + Text.N(r.Men) + ranks + " men" + army + ", strength " + Text.N((int)l.Strength)
                              + (r.FoodDays >= 0 ? ", food " + r.FoodDays + " days" : "") + ", speed " + Text.D(World.Speed(r)) + "; " + Views.Doing(s, r) + "; " + l.Order + clan + ".");
            }

            List<SettlementRow> forts = s.Settlements.Where(h => h.Faction == w.Realm && World.IsFort(h)).OrderBy(h => h.Name).ToList();
            if (forts.Count > 0)
            {
                sb.AppendLine().AppendLine("MUSTER AT EACH OF YOUR CASTLES AND TOWNS (running totals, rough; the full list per place: lookups/<id>.txt)");
                foreach (SettlementRow h in forts)
                    sb.AppendLine("- " + h.Name + " [" + h.Id + "]" + (w.Besieged(h) ? " (UNDER SIEGE)" : "") + ": " + MusterLine(w, h.Id) + ".");
            }

            List<ViewCaptive> captives = s.Captives.Select(c => Views.Captive(s, c)).ToList();
            sb.AppendLine().AppendLine("YOUR CAPTURED LORDS (" + captives.Count + (captives.Count > 0 ? ", by where they are held)" : ")"));
            if (captives.Count == 0) sb.AppendLine("- None.");
            foreach (string line in Views.CaptiveLines(captives)) sb.AppendLine("- " + line);
            return sb.ToString().TrimEnd();
        }
    }
}
