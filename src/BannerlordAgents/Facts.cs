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
                if (r.Faction != w.Realm || r.Kind != "lord" || r.Attached || r.Id == w.S.PlayerParty) continue;   // never the player's own party
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
                         + (l.Row.ArmyLeader == l.Row.Id ? ", leads an army" : l.Row.ArmyLeader != null ? (l.Row.Attached ? ", in an army" : ", called to an army") : "") + ", " + l.Order);
            }
            while (b < Bands.Length) Close(BandNames[b++]);
            if (band.Count > 0) Close("later");
            return lines;
        }

        /// <summary>Hours to march there by the march clock (0.21.0: the lord's exact pace, the ground and the hour stretch by stretch, stretched to the game's road length where its table knows the way; it was the road at his pace on open ground).</summary>
        private static double Hours(World w, PartyRow lord, string place) => w.HoursFor(lord, place);

        /// <summary>
        /// The k lords who could reach a place soonest, soonest first, leaving out those who cannot (0.21.4): the same lords as ordering them all by Hours, but the
        /// march clock runs only for those whose fastest pace on the straight line could still bring them among the k (the game's way is never shorter than the line;
        /// the bound World.CanReachWithin uses). With the game's own ways (0.21.2) ordering all of them asked a realm of 80 lords for 80 ways a page, thousands an
        /// hour. Ties keep the lords' own order, as OrderBy does.
        /// </summary>
        private static List<Lord> Soonest(World w, List<Lord> lords, string place, int k)
        {
            List<Lord> found = SoonestPruned(w, lords, place, k);
            if (CheckSoonest)
            {
                List<Lord> all = lords.Where(l => !double.IsNaN(Hours(w, l.Row, place))).OrderBy(l => Hours(w, l.Row, place)).Take(k).ToList();
                SoonestChecked++;
                if (!all.SequenceEqual(found)) SoonestDiffer++;
            }
            return found;
        }

        /// <summary>The speed test's check that Soonest picks the very lords ordering them all would (and how often it did not).</summary>
        internal static bool CheckSoonest;
        internal static long SoonestChecked, SoonestDiffer;

        private static List<Lord> SoonestPruned(World w, List<Lord> lords, string place, int k)
        {
            var result = new List<Lord>();
            if (k <= 0 || lords.Count == 0 || !PointOf(w, place, out float x, out float y)) return result;
            Common.ClockParty[] sp = lords.Select(l => w.SeenOf(l.Row)).ToArray();
            foreach (int i in TopK(lords.Count, i => Bound(w, sp[i], x, y), i => Hours(w, lords[i].Row, place), k)) result.Add(lords[i]);
            return result;
        }

        /// <summary>The k holdings a lord could reach soonest, soonest first, leaving out those he cannot reach (0.21.4: pruned as Soonest is).</summary>
        private static List<SettlementRow> NearestHoldings(World w, PartyRow r, List<SettlementRow> holdings, int k)
        {
            Common.ClockParty sp = w.SeenOf(r);
            List<SettlementRow> found = TopK(holdings.Count, i => PointOf(w, holdings[i].Id, out float x, out float y) ? Bound(w, sp, x, y) : double.PositiveInfinity,
                                             i => Hours(w, r, holdings[i].Id), k).Select(i => holdings[i]).ToList();
            if (CheckSoonest)
            {
                List<SettlementRow> all = holdings.Where(h => !double.IsNaN(Hours(w, r, h.Id))).OrderBy(h => Hours(w, r, h.Id)).Take(k).ToList();
                SoonestChecked++;
                if (!all.SequenceEqual(found)) SoonestDiffer++;
            }
            return found;
        }

        /// <summary>Where a march to a place ends, as World.HoursFor takes it: a holding's gate (else its middle), a party or spot where the realm knows it.</summary>
        private static bool PointOf(World w, string place, out float x, out float y)
        {
            x = y = 0;
            if (place == null) return false;
            SettlementRow h = w.Holding(place);
            if (h == null) return w.Locate(place, out x, out y, out _, out _);
            bool gate = h.Gx != 0 || h.Gy != 0;
            x = gate ? h.Gx : h.X;
            y = gate ? h.Gy : h.Y;
            return true;
        }

        /// <summary>No march there can take fewer hours: the straight line at the party's fastest pace on any ground, by day or night (World.CanReachWithin's bound).</summary>
        private static double Bound(World w, Common.ClockParty sp, float x, float y)
        {
            double d = Math.Sqrt((sp.X - x) * (sp.X - x) + (sp.Y - y) * (sp.Y - y));
            return d / Math.Max(0.05, Common.MarchClock.FastestUnits(sp, Common.MarchClock.BasisAt(sp, w.Hours)));
        }

        /// <summary>
        /// The k of n with the fewest hours, fewest first (ties in their own order, as OrderBy keeps them; NaN left out), working out the hours only while an
        /// item's bound could still bring it among the k.
        /// </summary>
        private static List<int> TopK(int n, Func<int, double> bound, Func<int, double> hours, int k)
        {
            var found = new List<KeyValuePair<int, double>>();
            if (k <= 0 || n == 0) return new List<int>();
            double kth = double.PositiveInfinity;
            foreach (KeyValuePair<int, double> b in Enumerable.Range(0, n).Select(i => new KeyValuePair<int, double>(i, bound(i))).OrderBy(b => b.Value))
            {
                if (found.Count >= k && b.Value > kth + 0.01) break;
                double t = hours(b.Key);
                if (double.IsNaN(t)) continue;
                found.Add(new KeyValuePair<int, double>(b.Key, t));
                if (found.Count >= k) kth = found.Select(e => e.Value).OrderBy(v => v).ElementAt(k - 1);
            }
            return found.OrderBy(e => e.Value).ThenBy(e => e.Key).Take(k).Select(e => e.Key).ToList();
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
                    List<string> near = Soonest(w, lords, place, 5).Select(l => LordLine(w, l, place)).ToList();
                    List<string> foes = w.EnemiesNear(place, 40).Take(3).Select(e => EnemyLine(w, e, place)).ToList();
                    sb.AppendLine("- Near " + h.Name + " [" + h.Id + "]: your nearest lords " + (near.Count > 0 ? string.Join("; ", near) : "none") + "."
                                  + (foes.Count > 0 ? " Enemies in view within 40: " + string.Join("; ", foes) + "." : " No enemy in view within 40."));
                    if (h.Faction == w.Realm && w.Besieged(h))
                    {
                        double? inside = w.InsideStrength(h);
                        if (inside.HasValue)
                            sb.AppendLine("- Inside " + h.Name + ": the garrison and your lords there, strength " + Text.N((int)inside.Value)
                                          + ". They sally out to join a relief only when they and the relief together are more than 1.5 times the besiegers' strength ("
                                          + Text.N((int)(w.SiegeStrength(h) ?? 0)) + "); militia never sally. While a lord holding your order is inside, they never go out"
                                          + " on their own; with none inside, the game sends the garrison and the lords there out by itself when it counts them more than"
                                          + " twice the besiegers at their camp (it looks every 4 hours), to fight in the open.");
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
                    // a party the news named that the realm's eyes never saw (a raider gone before the hour's look) has no "last seen"
                    double ago = w.SeenAgo(p.Id);
                    string where = w.InView(p.Id) ? "in view" : ago < 9999 ? "last seen " + Math.Round(ago) + " hours ago" : "not seen by your eyes, only named in the news";
                    SettlementRow home = w.S.Settlements.Where(x => x.Faction == w.Realm).OrderBy(x => Reach(w, p.Id, x.Id)).FirstOrDefault();
                    List<string> near = Soonest(w, lords, p.Id, 5).Select(l => LordLine(w, l, p.Id)).ToList();
                    double? str = w.KnownStrength(p.Id);
                    sb.AppendLine("- " + w.Name(p.Id) + " [" + w.Tag(p.Id) + "] (" + where + ", strength " + (str.HasValue ? Text.N((int)str.Value) : "?")
                                  + (home != null ? ", " + Far(w, p.Id, home.Id) + " from your " + home.Name : "") + "): your lords who can reach it soonest "
                                  + (near.Count > 0 ? string.Join("; ", near) : "none") + ".");
                    // a fight with it, by the game's battle rules (0.7.4): the nearest lord alone, and the nearest three together
                    List<PartyRow> soonest = Soonest(w, lords, p.Id, 3).Select(l => l.Row).ToList();
                    string odds = Forecast.FieldOddsText(w, soonest, p);
                    if (odds != null) sb.AppendLine("  " + odds);
                }
            }
            List<Lord> free = lords.Where(l => l.Order == "free").ToList();
            sb.AppendLine("- Your lords leading parties: " + lords.Count + "; free: " + free.Count + " (strength " + Text.N((int)free.Sum(l => l.Strength))
                          + "); holding your orders: " + (lords.Count - free.Count) + ". Travel hours are by road at the lord's own pace by the game's rules (a party covers its speed / 1.2 map units an hour, a quarter less by night); forest, fords and bridges on the way slow it further.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The look-up files: one per holding of the realm, lord of the realm, and enemy it knows of.</summary>
        public static Dictionary<string, string> Lookups(World w, Dictionary<string, ViewParty> lastSeen, Dictionary<string, ViewTrail> trails = null)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (object step in LookupsSteps(w, lastSeen, trails, files)) { }
            return files;
        }

        /// <summary>How many look-up pages one step of a write in steps makes at most (0.25.0, item 12: the war room's fresh book, Writes).</summary>
        public const int PagesAStep = 16;

        /// <summary>
        /// The look-up pages made into files, a few at a time (0.25.0, item 12): it yields after every PagesAStep pages, so the war room's fresh book, written in steps
        /// between the helper's other jobs (Writes), never holds an order up for long.
        /// </summary>
        internal static System.Collections.IEnumerable LookupsSteps(World w, Dictionary<string, ViewParty> lastSeen, Dictionary<string, ViewTrail> trails, Dictionary<string, string> files)
        {
            int made = 0;
            List<Lord> lords = OwnLords(w);
            List<SettlementRow> own = w.S.Settlements.Where(x => x.Faction == w.Realm).ToList();

            foreach (SettlementRow h in own)
            {
                var sb = new StringBuilder();
                sb.AppendLine(h.Name + " [" + h.Id + "], your " + h.Kind + (w.Besieged(h) ? ", UNDER SIEGE" : "") + (h.VillageState == "BeingRaided" ? ", being raided" : h.VillageState == "Looted" ? ", looted" : "")
                              + (World.IsFort(h) ? "; garrison " + Text.N(h.Garrison) + ", militia " + Text.N(h.Militia) : "") + ". As of " + Text.When(w.Hours) + ".");
                sb.AppendLine("Your lords, soonest first:");
                foreach (Lord l in Soonest(w, lords, h.Id, 15)) sb.AppendLine("- " + LordLine(w, l, h.Id));
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
                if (++made % PagesAStep == 0) yield return null;
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
                    foreach (Lord l in Soonest(w, lords, at, 8)) sb.AppendLine("- " + LordLine(w, l, at));
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
                string purse = PurseView.LordLine(r);
                if (purse != null)
                    sb.AppendLine("Purse: " + purse + "." + (!double.IsNaN(r.SaleHours) && r.SaleText != null ? " Last sale, " + Math.Round(w.Hours - r.SaleHours) + " hours ago: " + r.SaleText + "." : ""));
                sb.AppendLine("Hours to your holdings, nearest first:");
                foreach (SettlementRow h in NearestHoldings(w, r, own, 15)) sb.AppendLine("- " + h.Name + " [" + h.Id + "]: " + H(Hours(w, r, h.Id)));
                List<PartyRow> foes = w.EnemiesNear(r.Id, 60);
                sb.AppendLine(foes.Count > 0 ? "Enemies in view within 60 of this lord:" : "No enemy in view within 60 of this lord.");
                foreach (PartyRow e in foes.Take(10)) sb.AppendLine("- " + EnemyLine(w, e, r.Id) + ", " + H(Hours(w, r, e.Id)) + " to reach");
                files[l.Tag] = sb.ToString();
                if (++made % PagesAStep == 0) yield return null;
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
                foreach (Lord l in Soonest(w, lords, id, 8)) sb.AppendLine("- " + LordLine(w, l, id));
                files[tag] = sb.ToString();
                if (++made % PagesAStep == 0) yield return null;
            }

            files["README"] = "Look-ups for " + Views.FactionName(w.S, w.Realm) + ", as of " + Text.When(w.Hours) + ", rewritten with each poke.\n"
                              + "One file per id you see in [brackets]: lookups/<id>.txt.\n"
                              + "- a holding of yours: your lords soonest to it, enemies near it, enemy castles and towns near it, and for a castle or town the muster there (every lord by how soon they could come, with running totals);\n"
                              + "- a lord of yours: men, strength, ranks, food, hours to each of your holdings, enemies near the lord;\n"
                              + "- a lord of yours held prisoner: where and since when, and your lords who can reach that place soonest;\n"
                              + "- an enemy: where it is or was last seen, its strength, for an army its trail over the last two days, your holdings near it, your lords who can reach it soonest.\n"
                              + "lookups/captured.txt lists all your lords held prisoner, by where they are held.\n"
                              + (Ways.Proven ? "Travel hours follow the way the game itself would plan for the march, round mountains and water," : "Travel hours are by road where the game knows the way (else as the crow flies),")
                              + " at the lord's own pace by the game's rules (its speed / 1.2 map units an hour on open ground, night counted); forest, fords and bridges on the way slow it further.\n";
            // the human player (0.17.3): who he is, how his battles are fought, every battle of his the realm knows of, and what it saw him do
            if (w.S.PlayerParty != null && w.S.PartyById.TryGetValue(w.S.PlayerParty, out PartyRow hp))
            {
                List<string> seen = Agents.Of(w.Realm)?.Chronicle?.Mentioning(new[] { hp.Name, hp.Id }, 60);
                string text = InPerson.Lookup(w.S, w.Realm, seen);
                if (text != null) files[InPerson.LookupName] = text;
            }
        }

        /// <summary>
        /// The look-ups of the enemy's castles and towns the realm knows (0.14: there were none, and both of Dol Guldur's calls in the Nov 2
        /// example asked for one): those near its lands, as its report lists them, and those of a siege or battle it sees. What its eyes
        /// counted there, if they reach it; its lords by how soon they could be there, with the muster's running totals; the enemies in
        /// view near it.
        /// </summary>
        public static Dictionary<string, string> EnemyHoldings(World w, View v)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<Lord> lords = OwnLords(w);
            var ids = new List<string>(v.Frontier.Select(h => h.Id));
            foreach (ClockItem c in v.ClockItems) if (c.Place != null && !ids.Contains(c.Place)) ids.Add(c.Place);
            foreach (string id in ids)
            {
                SettlementRow h = w.Holding(id);
                if (h == null || h.Faction == w.Realm || !World.IsFort(h) || files.ContainsKey(id)) continue;
                ViewHolding seen = v.Frontier.FirstOrDefault(x => x.Id == id);
                var sb = new StringBuilder();
                string counted = seen != null && seen.Garrison >= 0
                    ? "garrison " + Text.N(seen.Garrison) + (seen.Militia >= 0 ? ", militia " + Text.N(seen.Militia) : "")
                      + (seen.WallLevel > 0 ? "; walls level " + seen.WallLevel + "; defenders' strength " + Text.N((int)Math.Round(seen.Defence))
                                              + (seen.LordsInside > 0 ? " (" + Text.N((int)Math.Round(seen.LordsInside)) + " of it the lords your realm sees inside)" : "")
                                              + ", so it holds against besiegers up to strength " + Text.N((int)Math.Round(seen.HoldsUpTo)) : "")
                    : "its garrison is not in your sight";
                string status = Views.Status(w.S, h);
                sb.AppendLine(h.Name + " [" + h.Id + "], a " + h.Kind + " of " + Views.FactionName(w.S, h.Faction) + (w.HostileFaction(h.Faction) ? " (at war with you)" : "") + ": " + counted
                              + (status != null ? "; " + status : "") + ". As of " + Text.When(w.Hours) + ".");
                sb.AppendLine("Your lords, soonest first:");
                foreach (Lord l in Soonest(w, lords, h.Id, 12)) sb.AppendLine("- " + LordLine(w, l, h.Id));
                List<PartyRow> foes = w.EnemiesNear(h.Id, 60);
                sb.AppendLine(foes.Count > 0 ? "Enemies in view within 60:" : "No enemy in view within 60.");
                foreach (PartyRow e in foes.Take(10)) sb.AppendLine("- " + EnemyLine(w, e, h.Id));
                sb.AppendLine("Muster here: every lord of yours by how soon they could be here, with running totals. * = a leader here could call them into an army at once; the others march first:");
                foreach (string line in Muster(w, h.Id)) sb.AppendLine("- " + line);
                files[h.Id] = sb.ToString();
            }
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
            foreach (string l in PurseView.Lines(s, w.Realm, 30)) sb.AppendLine("- " + l);
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
                string army = r.ArmyLeader == r.Id ? ", leads an army of " + Text.N(r.ArmyMen) + " men in " + r.ArmyParties + " parties" : r.ArmyLeader != null ? (r.Attached ? ", in " : ", called to ") + w.Name(r.ArmyLeader) + "'s army" : "";
                string clan = r.Clan != null && clanNames.TryGetValue(r.Clan, out ClanRow cr) ? "; clan " + cr.Name + (cr.Ruler ? " (yours)" : "") + ", influence " + Text.N((int)cr.Influence) : "";
                string purse = PurseView.LordLine(r);
                sb.AppendLine("- " + l.Name + " [" + l.Tag + "]: " + Text.N(r.Men) + ranks + " men" + army + ", strength " + Text.N((int)l.Strength)
                              + (r.FoodDays >= 0 ? ", food " + r.FoodDays + " days" : "") + ", speed " + Text.D(World.Speed(r)) + "; " + Views.Doing(s, r) + "; " + l.Order + clan
                              + (purse != null ? "; " + purse : "") + ".");
            }

            List<SettlementRow> forts = s.Settlements.Where(h => h.Faction == w.Realm && World.IsFort(h)).OrderBy(h => h.Name).ToList();
            if (forts.Count > 0)
            {
                sb.AppendLine().AppendLine("MUSTER AT EACH OF YOUR CASTLES AND TOWNS (running totals, rough; the full list per place: lookups/<id>.txt)");
                foreach (SettlementRow h in forts)
                    sb.AppendLine("- " + h.Name + " [" + h.Id + "]" + (w.Besieged(h) ? " (UNDER SIEGE)" : "") + ": " + MusterLine(w, h.Id) + ".");
            }

            string human = InPerson.Section(s, w.Realm);
            if (human != null) sb.AppendLine().AppendLine(human);

            List<ViewCaptive> captives = s.Captives.Select(c => Views.Captive(s, c)).ToList();
            sb.AppendLine().AppendLine("YOUR CAPTURED LORDS (" + captives.Count + (captives.Count > 0 ? ", by where they are held)" : ")"));
            if (captives.Count == 0) sb.AppendLine("- None.");
            foreach (string line in Views.CaptiveLines(captives)) sb.AppendLine("- " + line);
            return sb.ToString().TrimEnd();
        }
    }
}
