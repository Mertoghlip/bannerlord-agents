using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BannerlordAgents
{
    /// <summary>
    /// A news wake's report (0.14, docs/PLAN-news.md): the news, its clock, and what bears on it in full (the theatre: Theatre.Build), the
    /// rest of the realm in a few lines a region, and what the agent can ask for the rest; and the question book its war room answers
    /// those questions from (book\poke-N.json in the channel): every list of the full report, item by item with its ids and region,
    /// and the look-up pages, from the same hour and the same view, fog-checked like the report. Worker thread; game-free.
    /// </summary>
    internal static partial class Views
    {
        private static readonly Regex BracketIds = new Regex(@"\[([^\]\s]+)\]", RegexOptions.Compiled);

        /// <summary>
        /// A recorded poke's orders in force by lord (hero id to tool), for building its reports again away from the game (the coverage
        /// check, replays), where the order runner holds none. Null in the game.
        /// </summary>
        internal static Dictionary<string, string> RecordedOrders;

        private static string OrderName(ViewParty p)
        {
            Tools.ToolInstance held = Tools.Runner.ForParty(p.Id);
            if (held != null) return held.Def.Name;
            return RecordedOrders != null && p.Lord != null && RecordedOrders.TryGetValue(p.Lord, out string tool) ? tool : null;
        }

        /// <summary>The ids in [brackets] in a line of text.</summary>
        internal static List<string> IdsIn(string line) => BracketIds.Matches(line ?? "").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

        public const string AskMenu =
            "WHAT YOU CAN ASK (each question costs a few seconds while the game runs, so ask only what your decision needs)\n"
            + "- look_up: one id in full: a lord of yours (men, ranks, food, purse, the hours to each of your holdings), a holding of yours or an enemy's near your lands "
            + "(who can get there soonest, the enemies near it, the muster there), an enemy lord or army (where it was seen, its trail, who can reach it), a captured lord of yours.\n"
            + "- list: everything of one kind, or only what is in one region of THE REST OF THE WAR: lords, holdings, enemies, sieges, orders, captives, allies, diplomacy, votes, purse, realm, history, notes, regions.\n"
            + "- chronicle: your chronicle's lines about one lord, holding or realm, oldest first.\n"
            + "- ask_the_way, can_catch, battle_odds, engagement and roads_in work as your instructions say.";

        /// <summary>
        /// A news wake's report. The whole report for a first look or a quiet day (with the questions it can ask); the realm's diplomacy,
        /// purse and sum for news of diplomacy alone; else the theatre's.
        /// </summary>
        public static string NewsReport(View v, List<Trigger> triggers, ReportExtras x, Theatre t, List<Area> areas = null)
        {
            areas = areas ?? Areas(v);
            if (t == null || t.Whole)
                return LookupFile.Replace(Report(v, triggers, x).Replace("(his look-up file names his clan)", "(look_up on him names his clan)"), "look_up $1").TrimEnd() + "\n\n" + AskMenu + "\n";

            var sb = new StringBuilder(8 * 1024);
            sb.AppendLine("REPORT FOR " + v.FactionName.ToUpperInvariant() + ": THE NEWS AND WHAT BEARS ON IT");
            sb.AppendLine("Ruler: " + (v.Ruler ?? "unknown") + ". " + Text.When(v.Hours) + ", " + (v.Night ? "night." : "daytime."));
            sb.AppendLine("At war with: " + (v.AtWar.Count > 0 ? string.Join(", ", v.AtWar) : "no one") + ". Allies: " + (v.Allies.Count > 0 ? string.Join(", ", v.Allies) : "none") + ".");
            sb.AppendLine("Distances are in map units: as the crow flies, then in brackets by road, the way the game reckons a march round mountains and rivers" + (Roads.Known ? "" : " (by road not known in this report)")
                          + ". A party is seen only within sight of your lords, castles and towns. Ids in [brackets] are what orders should name.");
            sb.AppendLine(t.IsTheatre
                ? "This report holds what bears on this news: where it happens, its clock, and everyone who can be there before it is decided. The rest of your realm is in THE REST OF THE WAR, a few lines a region; ask for any of it in full (WHAT YOU CAN ASK). Your other regions send their own news."
                : "This report is about your realm's diplomacy: its votes, offers and stances, its purse and its sum. Your lords and holdings are in THE REST OF THE WAR, a few lines a region; ask for any of them in full (WHAT YOU CAN ASK).");

            if (triggers != null && triggers.Count > 0)
            {
                sb.AppendLine().AppendLine("WHY YOU ARE ASKED NOW");
                foreach (Trigger tr in triggers) sb.AppendLine("- " + tr.Text);
            }
            if (t.IsTheatre) ClockSection(sb, v, t);
            if (x?.Since != null)
                sb.AppendLine().AppendLine("WHAT CHANGED SINCE YOUR LAST ANSWER" + (x.LastAnswered >= 0 ? " (to poke #" + x.LastAnswered + ")" : "")).AppendLine(x.Since);

            HashSet<string> parties = t.PartyIds();
            HashSet<string> lordIds = new HashSet<string>(parties);
            foreach (ViewParty p in v.Forces.Concat(v.Seen).Concat(v.LastSeen)) if (parties.Contains(p.Id) && p.Lord != null) lordIds.Add(p.Lord);
            var ids = new HashSet<string>(lordIds.Concat(t.Places));
            List<string> names = t.Names(v, local: true);

            if (t.IsTheatre)
            {
                // the engagement: the sieges and battles at the theatre's places, or with its parties in them
                var clocks = new List<string>();
                for (int i = 0; i < v.Clocks.Count; i++)
                {
                    ClockItem c = i < v.ClockItems.Count ? v.ClockItems[i] : null;
                    bool here = c != null ? (c.Place != null && t.Places.Contains(c.Place)) || c.Parties.Any(parties.Contains) : IdsIn(v.Clocks[i]).Any(ids.Contains);
                    if (here) clocks.Add(v.Clocks[i]);
                }
                if (clocks.Count > 0)
                {
                    sb.AppendLine().AppendLine("SIEGES AND BATTLES HERE (" + clocks.Count + " of " + v.Clocks.Count + "), run ahead by the game's own clock");
                    sb.AppendLine(ClocksNote);
                    foreach (string c in clocks) sb.AppendLine("- " + c);
                }
                LordsSection(sb, v, t);
                OthersSection(sb, v, t);
                HoldingsSection(sb, v, t);
            }
            if (x?.Facts != null && t.IsTheatre) sb.AppendLine().AppendLine("READY-MADE FACTS").AppendLine(LookupFile.Replace(x.Facts, "look_up $1"));
            OrdersSection(sb, x, ids, t);

            if (v.Open.Count > 0)
            {
                sb.AppendLine().AppendLine("OPEN VOTES AND OFFERS (" + v.Open.Count + "; answer them in \"diplomacy\": vote, decide, accept_..., decline_...)");
                foreach (string l in v.Open) sb.AppendLine("- " + l);
            }
            if (!t.IsTheatre || t.Diplomacy)
            {
                if (v.Sum != null) SumSection(sb, v.Sum);
                if (v.EnemyRealms.Count > 0)
                {
                    sb.AppendLine().AppendLine("ENEMY REALMS, AS THEIR ENCYCLOPEDIA PAGES SHOW ANY RULER");
                    foreach (string l in v.EnemyRealms) sb.AppendLine("- " + l);
                }
                if (v.Diplomacy.Count > 0)
                {
                    sb.AppendLine().AppendLine("DIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT");
                    foreach (string l in v.Diplomacy) sb.AppendLine("- " + l);
                }
            }
            AlliesSection(sb, v, t, ids, names);
            bool fullPurse = !t.IsTheatre || t.Kinds.Any(k => k == "order_ended" || k == "order_arrived");
            PurseSection(sb, v, lordIds, fullPurse);
            CaptivesSection(sb, v, t, ids, triggers);
            HistorySection(sb, v, ids, names);
            RestSection(sb, v, t, areas);
            sb.AppendLine().AppendLine(AskMenu);
            return sb.ToString();
        }

        private static void ClockSection(StringBuilder sb, View v, Theatre t)
        {
            sb.AppendLine().AppendLine("THE CLOCK");
            foreach (Focus f in t.Focuses)
            {
                string where = f.Name + " [" + (f.IsParty ? TagOf(v, f.Id) : f.Id) + "]";
                if (double.IsNaN(f.DecidedAt))
                    sb.AppendLine("- " + where + ": no clock of the game's runs on it, so shown is everyone within a day's march of it (" + Math.Round(f.Horizon) + " hours).");
                else
                {
                    double left = f.DecidedAt - v.Hours;
                    string shown = Math.Abs(f.Horizon - left) < 0.5 ? "within " + Math.Round(f.Horizon) + " hours of it"
                                 : left < f.Horizon ? "within " + Math.Round(f.Horizon) + " hours of it (at least " + Math.Round(f.MinHorizon) + ")"
                                 : "within " + Math.Round(f.Horizon) + " hours of it (at most " + Math.Round(Theatre.Ceiling) + ")";
                    sb.AppendLine("- " + where + ": decided by about " + Text.When(f.DecidedAt) + " (in " + Forecast.HoursText(Math.Max(0, left)) + ")"
                                  + (string.IsNullOrEmpty(f.How) ? "" : ": " + f.How) + ". Shown is everyone who can be there " + shown + ".");
                }
            }
            if (t.Focuses.Any(f => !double.IsNaN(f.DecidedAt)))
                sb.AppendLine("- Your answers' orders take effect about " + Math.Round(t.UsualLag) + " in-game hours after their report as a rule, and " + Math.Round(t.LateLag)
                              + " or more for one answer in ten, since the game runs while you think: each lord below is marked against the clock with that counted.");
        }

        private static string TagOf(View v, string partyId)
        {
            ViewParty p = v.Forces.Concat(v.Forces.Where(f => f.Members != null).SelectMany(f => f.Members)).Concat(v.Seen).Concat(v.LastSeen).FirstOrDefault(x => x.Id == partyId);
            return p?.Lord ?? partyId;
        }

        private static string ReachText(Theatre t, Reach r)
        {
            if (r == null || double.IsNaN(r.Hours)) return "";
            string to = r.Focus.Name;
            string h = r.Hours < 0.5 ? "there now" : "about " + Forecast.HoursText(r.Hours) + " from " + to;
            if (r.Hours < 0.5 && r.Focus.IsParty && r.Focus.Id == r.Party.Id) h = "the news is about it";
            string mark = t.Mark(r);
            return " (" + h + (mark != null ? ": " + mark : "") + ")";
        }

        private static void LordsSection(StringBuilder sb, View v, Theatre t)
        {
            int men = t.Lords.Sum(r => r.Party.ArmyMen > 0 ? r.Party.ArmyMen : r.Party.Men);
            sb.AppendLine().AppendLine("YOUR LORDS WHO CAN BE THERE IN TIME (" + t.Lords.Count + " of your " + v.Forces.Count + ", " + Text.N(men) + " men; soonest first, by the hours of their march)");
            if (t.Lords.Any(r => r.Party.Arrives != null)) sb.AppendLine(MarchNote);
            if (t.Lords.Count == 0) sb.AppendLine("- None of your lords can be there within the clock.");
            foreach (Reach r in t.Lords)
            {
                string line = LordLine(r.Party);
                sb.AppendLine(line.Substring(0, line.Length - 1) + ReachText(t, r) + ".");
                JoinerLines(sb, v, r.Party);
                MemberLines(sb, r.Party.Members);
            }
            if (t.Beyond.Count > 0 || t.LordsFurther > 0)
                sb.AppendLine("- Next, beyond the clock: " + string.Join("; ", t.Beyond.Select(r => r.Party.Name + Tag(r.Party) + " " + Text.N(r.Party.ArmyMen > 0 ? r.Party.ArmyMen : r.Party.Men) + " men, about "
                                                                                                       + Forecast.HoursText(r.Hours) + " from " + r.Focus.Name))
                              + (t.LordsFurther > 0 ? (t.Beyond.Count > 0 ? "; and " : "") + t.LordsFurther + " more further off (list lords)" : "") + ".");
        }

        private static void OthersSection(StringBuilder sb, View v, Theatre t)
        {
            if (t.Others.Count == 0)
            {
                sb.AppendLine().AppendLine("OTHERS WHO CAN BE THERE BEFORE IT IS DECIDED").AppendLine("- No enemy or ally your realm sees or saw lately can be there in time.");
                return;
            }
            sb.AppendLine().AppendLine("OTHERS WHO CAN BE THERE BEFORE IT IS DECIDED (" + t.Others.Count + "; in view now or seen earlier, enemies first; the hours from where each was seen, at its own pace)");
            var inView = new HashSet<string>(v.Seen.Select(p => p.Id));
            foreach (Reach r in t.Others)
            {
                string line = inView.Contains(r.Party.Id) ? SeenLine(r.Party) : "- SEEN EARLIER " + LastSeenLine(r.Party, v.Hours).Substring(2);
                string reach = t.HeadingIn.Contains(r.Party.Id) ? " (further off, but marching into this theatre" + (double.IsNaN(r.Hours) ? "" : ", about " + Forecast.HoursText(r.Hours) + " from " + r.Focus.Name) + ")"
                             : double.IsNaN(r.Hours) ? "" : r.Hours < 0.5 ? (r.Focus.IsParty && r.Focus.Id == r.Party.Id ? " (the news is about it)" : " (there now)")
                             : " (about " + Forecast.HoursText(r.Hours) + " from " + r.Focus.Name + ")";
                sb.AppendLine(line.Substring(0, line.Length - 1) + reach + ".");
                if (inView.Contains(r.Party.Id)) JoinerLines(sb, v, r.Party);
            }
            if (t.OthersLeft > 0)
                sb.AppendLine("- And " + t.OthersLeft + " more lords within the clock, further than these, strength " + Text.N((int)t.OthersLeftStrength) + " together (list enemies, with a region).");
            List<ViewTrail> trails = v.Trails.Where(tr => t.Others.Any(r => r.Party.Id == tr.Id)).ToList();
            if (trails.Count > 0)
            {
                sb.AppendLine("Their armies' trails, where your realm saw each in the last two days, oldest first:");
                foreach (ViewTrail tr in trails) sb.AppendLine("- " + TrailHead(tr, v.Hours) + ": " + string.Join(" -> ", TrailSteps(tr)) + ".");
            }
        }

        private static void HoldingsSection(StringBuilder sb, View v, Theatre t)
        {
            List<ViewHolding> own = v.Holdings.Where(h => t.Places.Contains(h.Id)).ToList();
            List<ViewHolding> theirs = v.Frontier.Where(h => t.Places.Contains(h.Id)).ToList();
            if (own.Count + theirs.Count == 0) return;
            sb.AppendLine().AppendLine("HOLDINGS HERE (" + own.Count + " of your " + v.Holdings.Count + ", and " + theirs.Count + " of the enemy's)");
            if (own.Any(h => h.WallLevel > 0)) sb.AppendLine(WallsNote);
            if (own.Any(h => h.Food >= 0)) sb.AppendLine(FoodNote.Replace("(his look-up file names his clan)", "(look_up on him names his clan)"));
            foreach (ViewHolding h in own) sb.AppendLine(HoldingLine(h));
            foreach (ViewHolding h in theirs) sb.AppendLine("- ENEMY " + FrontierLine(h).Substring(2));
        }

        private static void OrdersSection(StringBuilder sb, ReportExtras x, HashSet<string> ids, Theatre t)
        {
            if (x?.InForce == null) return;
            List<string> all = x.InForce.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (!t.IsTheatre || all.Count == 1 && all[0].StartsWith("- None", StringComparison.Ordinal))
            {
                int n = all.Count(l => l.StartsWith("- ", StringComparison.Ordinal) && !l.StartsWith("- None", StringComparison.Ordinal));
                sb.AppendLine().AppendLine("YOUR ORDERS IN FORCE").AppendLine(t.IsTheatre || n == 0 ? x.InForce : "- " + n + (n == 1 ? " order is" : " orders are") + " in force (list orders).");
                return;
            }
            List<string> here = all.Where(l => IdsIn(l).Any(ids.Contains)).ToList();
            List<string> rest = all.Where(l => !here.Contains(l) && l.StartsWith("- ", StringComparison.Ordinal)).ToList();
            sb.AppendLine().AppendLine("YOUR ORDERS IN FORCE HERE (" + here.Count + " of " + all.Count + ")");
            foreach (string l in here) sb.AppendLine(l);
            if (here.Count == 0) sb.AppendLine("- None of your orders in force names a lord or a place of this news.");
            // the others in a few words each: the agent adjusts and replaces its standing orders in news wakes too (the coverage check on run A)
            if (rest.Count > 0) sb.AppendLine("- Elsewhere (" + rest.Count + "; list orders for them in full): " + string.Join("; ", rest.Select(ShortOrder)) + ".");
        }

        /// <summary>An order in force in a few words: "Lord [id]: tool Place [id]" (its line up to the end of what it names, before "Now ...").</summary>
        private static string ShortOrder(string line)
        {
            string l = line.StartsWith("- ", StringComparison.Ordinal) ? line.Substring(2) : line;
            int now = l.IndexOf(". Now ", StringComparison.Ordinal);
            return (now > 0 ? l.Substring(0, now) : l).TrimEnd('.');
        }

        private static void SumSection(StringBuilder sb, RealmSum r)
        {
            sb.AppendLine().AppendLine("YOUR REALM IN SUM");
            sb.AppendLine("- In the field: " + r.Lords + " lords leading parties, " + Text.N(r.Men) + " men, strength " + Text.N((int)r.Strength)
                          + (r.Armies > 0 ? "; " + r.Armies + (r.Armies == 1 ? " army (" : " armies (") + r.ArmyLords + " parties)" : "; no army")
                          + ". Holding your orders: " + r.Ordered + "; the other " + (r.Lords - r.Ordered) + " are led by the game's own AI.");
            sb.AppendLine("- Castles and towns: " + r.Forts + " (garrisons " + Text.N(r.GarrisonMen) + " men, militia " + Text.N(r.Militia)
                          + (r.Besieged > 0 ? "; " + r.Besieged + " under siege" : "") + "). Villages: " + r.Villages
                          + (r.Looted + r.Raided > 0 ? " (" + string.Join(", ", new[] { r.Looted > 0 ? r.Looted + " looted" : null, r.Raided > 0 ? r.Raided + " being raided" : null }.Where(s => s != null)) + ")" : "") + ".");
            sb.AppendLine("- Prisoners of the enemy: " + (r.Captives == 0 ? "none of your lords." : r.Captives + " of your lords (list captives)."));
        }

        private static readonly Regex AtCap = new Regex(@"^At their wage cap, so they cannot recruit \((\d+)\)", RegexOptions.Compiled);
        private static readonly Regex YoursGold = new Regex(@"(?:: |; )([^;:]+?) \(yours\) ([\d,]+) \(([+-][\d,]+)\)", RegexOptions.Compiled);
        private static readonly Regex LookupFile = new Regex(@"lookups/([^\s]+?)\.txt", RegexOptions.Compiled);

        private static void PurseSection(StringBuilder sb, View v, HashSet<string> lordIds, bool full)
        {
            if (v.Purse.Count == 0) return;
            if (full)
            {
                sb.AppendLine().AppendLine("YOUR PURSE");
                foreach (string l in v.Purse) sb.AppendLine("- " + l);
                return;
            }
            // one line: the ruler's clan's gold, and who cannot recruit, those here by name
            string clan = v.Purse.FirstOrDefault(l => l.StartsWith("Your own clan", StringComparison.Ordinal));
            if (clan == null)
            {
                // "Clans' gold (...): Hîondrûs (yours) 653,675 (-5,929); Skûlgath ...": the ruler's own clan
                string clans = v.Purse.FirstOrDefault(l => l.StartsWith("Clans' gold", StringComparison.Ordinal));
                Match yours = clans != null ? YoursGold.Match(clans) : Match.Empty;
                clan = yours.Success ? "Your clan, " + yours.Groups[1].Value.Trim() + ", has " + yours.Groups[2].Value + " gold (" + yours.Groups[3].Value + " a day by the game's reckoning)" : null;
            }
            clan = clan ?? (v.Purse.FirstOrDefault(l => !AtCap.IsMatch(l) && !l.StartsWith("Clans' gold", StringComparison.Ordinal)) ?? "Your purse");
            string cap = v.Purse.FirstOrDefault(l => AtCap.IsMatch(l));
            string capText = "";
            if (cap != null)
            {
                List<string> mine = new List<string>();
                foreach (string part in cap.Substring(cap.IndexOf(':') + 1).Split(';'))
                {
                    List<string> pid = IdsIn(part);
                    if (pid.Any(lordIds.Contains)) mine.Add(part.Trim().TrimEnd('.'));
                }
                capText = " " + AtCap.Match(cap).Groups[1].Value + " of your lords are at their wage cap and cannot recruit" + (mine.Count > 0 ? ", of them here: " + string.Join("; ", mine) : "") + ".";
            }
            sb.AppendLine().AppendLine("YOUR PURSE IN ONE LINE (list purse for all of it)").AppendLine("- " + clan.TrimEnd('.') + "." + capText);
        }

        private static void AlliesSection(StringBuilder sb, View v, Theatre t, HashSet<string> ids, List<string> names)
        {
            if (v.Allied.Count == 0) return;
            if (!t.IsTheatre || t.Kinds.Contains("letter"))
            {
                sb.AppendLine().AppendLine("ALLIES AND JOINT PLANS (an ally knows only what it is told; write in \"letters\")");
                foreach (string l in v.Allied) sb.AppendLine("- " + l);
                return;
            }
            // the allies whose rulers read letters, and the letters and joint plans about this theatre
            List<string> keep = v.Allied.Where(l => l.Contains("its ruler reads letters") || (IsPlanOrLetter(l) && (IdsIn(l).Any(ids.Contains) || names.Any(n => n.Length >= 4 && Mentions(l, n))))).ToList();
            int rest = v.Allied.Count - keep.Count;
            if (keep.Count == 0 && rest == 0) return;
            sb.AppendLine().AppendLine("ALLIES AND JOINT PLANS HERE (an ally knows only what it is told; write in \"letters\")");
            foreach (string l in keep) sb.AppendLine("- " + l);
            if (rest > 0) sb.AppendLine("- " + rest + " more " + (rest == 1 ? "line" : "lines") + " on your allies (list allies).");
        }

        private static bool IsPlanOrLetter(string l) => l.StartsWith("Joint plan", StringComparison.Ordinal) || l.Contains(" letter ") || l.Contains("letter l");

        private static void CaptivesSection(StringBuilder sb, View v, Theatre t, HashSet<string> ids, List<Trigger> triggers)
        {
            if (v.Sum == null || v.Captives.Count == 0) return;
            if (!t.IsTheatre)
            {
                sb.AppendLine().AppendLine("YOUR CAPTURED LORDS (" + v.Captives.Count + ", by where they are held)");
                foreach (string line in CaptiveLines(v.Captives)) sb.AppendLine("- " + line);
                return;
            }
            string news = string.Join("\n", (triggers ?? new List<Trigger>()).Select(x => x.Text));
            List<ViewCaptive> here = v.Captives.Where(c => (c.Holding != null && ids.Contains(c.Holding)) || (c.PartyLord != null && ids.Contains(c.PartyLord))
                                                          || (!string.IsNullOrEmpty(c.Name) && Mentions(news, c.Name))).ToList();
            if (here.Count == 0) return;
            sb.AppendLine().AppendLine("YOUR CAPTURED LORDS HERE (" + here.Count + " of " + v.Captives.Count + (v.Captives.Count > here.Count ? "; list captives for all" : "") + ")");
            foreach (string line in CaptiveLines(here)) sb.AppendLine("- " + line);
        }

        private static void HistorySection(StringBuilder sb, View v, HashSet<string> ids, List<string> names)
        {
            if (v.News.Count == 0) return;
            List<string> here = v.News.Where(l => IdsIn(l).Any(ids.Contains) || names.Any(n => n.Length >= 4 && Mentions(l, n))).ToList();
            int rest = v.News.Count - here.Count;
            sb.AppendLine().AppendLine((v.NewsFromChronicle ? "RECENT HISTORY HERE, LAST TWO DAYS (from your chronicle; " : "NEWS HERE, LAST TWO DAYS (") + here.Count + " of " + v.News.Count + " lines)");
            foreach (string n in here) sb.AppendLine("- " + n);
            if (rest > 0) sb.AppendLine("- " + rest + " more " + (rest == 1 ? "line" : "lines") + " from elsewhere (list history, or chronicle).");
        }

        /// <summary>A name in a line, as a whole word.</summary>
        internal static bool Mentions(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return false;
            int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int end = i + name.Length;
                bool after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (before && after) return true;
                i = text.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // ---------- the regions: THE REST OF THE WAR ----------

        /// <summary>One region of the realm (0.14): its castles and towns around the one it is named after, and its villages.</summary>
        internal sealed class Area
        {
            public string Name, Anchor;
            public float X, Y;
            public List<string> Holdings = new List<string>();
        }

        /// <summary>The most regions THE REST OF THE WAR names.</summary>
        public const int MostAreas = 7;

        /// <summary>
        /// The realm's regions: up to seven of its castles and towns as far apart as can be (its towns first), each region named after one and
        /// holding the holdings nearest to it. A realm with no castle or town has one region, the field.
        /// </summary>
        public static List<Area> Areas(View v)
        {
            List<ViewHolding> forts = v.Holdings.Where(h => h.Kind == "town" || h.Kind == "castle").OrderBy(h => h.Kind == "town" ? 0 : 1).ThenBy(h => h.Name, StringComparer.Ordinal).ToList();
            var areas = new List<Area>();
            if (forts.Count == 0) return areas;
            var anchors = new List<ViewHolding> { forts[0] };
            while (anchors.Count < Math.Min(MostAreas, forts.Count))
            {
                // the fort farthest from every anchor so far, towns before castles when about as far
                ViewHolding next = forts.Where(h => !anchors.Contains(h))
                                        .OrderByDescending(h => anchors.Min(a => Dist(a.X, a.Y, h.X, h.Y)) * (h.Kind == "town" ? 1.25 : 1.0)).First();
                if (anchors.Min(a => Dist(a.X, a.Y, next.X, next.Y)) < 25) break;   // the rest are close together: one region each is enough
                anchors.Add(next);
            }
            foreach (ViewHolding a in anchors) areas.Add(new Area { Name = a.Name, Anchor = a.Id, X = a.X, Y = a.Y });
            foreach (ViewHolding h in v.Holdings) Nearest(areas, h.X, h.Y).Holdings.Add(h.Id);
            return areas;
        }

        internal static Area Nearest(List<Area> areas, float x, float y)
        {
            Area best = null;
            float d = float.MaxValue;
            foreach (Area a in areas)
            {
                float e = Dist(a.X, a.Y, x, y);
                if (e < d) { d = e; best = a; }
            }
            return best;
        }

        private static void RestSection(StringBuilder sb, View v, Theatre t, List<Area> areas)
        {
            if (areas.Count == 0) return;
            HashSet<string> shown = t.IsTheatre ? t.PartyIds() : new HashSet<string>();
            sb.AppendLine().AppendLine("THE REST OF THE WAR (" + areas.Count + (areas.Count == 1 ? " region" : " regions") + ", each named after one of your castles or towns; "
                                       + (t.IsTheatre ? "what is not shown above" : "your lords, the enemies your realm sees, the sieges and battles") + "; list with a region's name for any of it in full)");
            var lordsIn = new Dictionary<Area, List<ViewParty>>();
            var foesIn = new Dictionary<Area, List<ViewParty>>();
            var clocksIn = new Dictionary<Area, List<int>>();
            foreach (Area a in areas) { lordsIn[a] = new List<ViewParty>(); foesIn[a] = new List<ViewParty>(); clocksIn[a] = new List<int>(); }
            foreach (ViewParty p in v.Forces) if (!shown.Contains(p.Id)) lordsIn[Nearest(areas, p.X, p.Y)].Add(p);
            foreach (ViewParty p in v.Seen) if (p.Relation == "enemy" && p.Kind == "lord" && !shown.Contains(p.Id)) foesIn[Nearest(areas, p.X, p.Y)].Add(p);
            for (int i = 0; i < v.ClockItems.Count && i < v.Clocks.Count; i++)
            {
                ClockItem c = v.ClockItems[i];
                if (t.IsTheatre && ((c.Place != null && t.Places.Contains(c.Place)) || c.Parties.Any(shown.Contains))) continue;
                clocksIn[Nearest(areas, c.X, c.Y)].Add(i);
            }
            var byId = v.Holdings.ToDictionary(h => h.Id, h => h);
            foreach (Area a in areas)
            {
                List<ViewParty> lords = lordsIn[a], foes = foesIn[a];
                var parts = new List<string>();
                List<string> others = a.Holdings.Where(id => id != a.Anchor && byId.TryGetValue(id, out ViewHolding hh) && hh.Kind != "village").Select(id => byId[id].Name).ToList();
                int villages = a.Holdings.Count(id => byId.TryGetValue(id, out ViewHolding hh) && hh.Kind == "village");
                string head = "Around " + a.Name + (others.Count > 0 ? " (with " + string.Join(", ", others) + ")" : "") + (villages > 0 ? ", " + villages + (villages == 1 ? " village" : " villages") : "");
                // each lord by name and id, in a few words (the coverage check on run A: in a news wake the agent also orders lords far from the
                // news, most of them free; so every lord can be ordered from this report, and look_up gives one in full)
                parts.Add(lords.Count == 0 ? "none of your lords" + (t.IsTheatre ? " besides those above" : "")
                    : lords.Count + (lords.Count == 1 ? " lord of yours" : " lords of yours") + " (" + Text.N(lords.Sum(p => p.ArmyMen > 0 ? p.ArmyMen : p.Men)) + " men, strength "
                      + Text.N((int)lords.Sum(p => p.Strength)) + "): " + string.Join(", ", lords.OrderByDescending(p => p.ArmyMen > 0 ? p.ArmyMen : p.Men).Select(RosterEntry)));
                if (foes.Count > 0)
                {
                    List<ViewParty> big = foes.OrderByDescending(p => p.Strength).Take(3).ToList();
                    parts.Add("enemies in view: " + foes.Count + " (strength " + Text.N((int)foes.Sum(p => p.Strength)) + "), the strongest "
                              + string.Join(", ", big.Select(b => b.Name + Tag(b) + " " + (b.ArmyMen > 0 ? "army of " + Text.N(b.ArmyMen) : Text.N(b.Men)) + " men")));
                }
                List<string> trouble = a.Holdings.Where(id => byId.TryGetValue(id, out ViewHolding hh) && hh.Status != null && !(t.IsTheatre && t.Places.Contains(id)))
                                                 .Select(id => byId[id].Name + " [" + id + "] " + byId[id].Status).Take(4).ToList();
                if (trouble.Count > 0) parts.Add(string.Join("; ", trouble));
                List<string> clocks = clocksIn[a].Select(i => v.ClockItems[i]).Select(c => c.Kind + (c.Place != null ? " at " + (byId.TryGetValue(c.Place, out ViewHolding ch) ? ch.Name : v.Frontier.FirstOrDefault(f => f.Id == c.Place)?.Name ?? c.Place) + " [" + c.Place + "]" : " in the field")).Distinct().ToList();
                if (clocks.Count > 0) parts.Add((clocks.Count == 1 ? "a " : clocks.Count + ": ") + string.Join(", ", clocks) + " (list sieges)");
                sb.AppendLine("- " + head + ": " + string.Join("; ", parts) + ".");
            }
        }

        /// <summary>A lord in a few words, for THE REST OF THE WAR: his name and id, his men, and his order or "free".</summary>
        private static string RosterEntry(ViewParty p)
        {
            string order = OrderName(p);
            return p.Name + Tag(p) + " " + (p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) : Text.N(p.Men)) + (order != null ? " (" + order + ")" : " (free)");
        }

        // ---------- the question book ----------

        /// <summary>One line of a list in the question book: its text, the ids it names and the region it lies in.</summary>
        internal sealed class BookItem
        {
            public string T, Area;
            public List<string> Ids = new List<string>();
        }

        /// <summary>
        /// The question book of a poke (0.14): every list of the full report, item by item, with the ids each names and the region it lies in,
        /// the regions themselves, and the look-up pages (one per id, as the look-up files had them, and the enemy's holdings near the realm's
        /// lands too). Written as JSON for the war room's look_up, list and chronicle questions.
        /// </summary>
        public static object Book(View v, ReportExtras x, int poke, List<Area> areas, Dictionary<string, string> pages)
        {
            areas = areas ?? Areas(v);
            string AreaOf(float px, float py) => areas.Count == 0 ? null : Nearest(areas, px, py).Name;
            var lists = new Dictionary<string, List<BookItem>>();
            List<BookItem> L(string name) { if (!lists.TryGetValue(name, out List<BookItem> l)) lists[name] = l = new List<BookItem>(); return l; }
            BookItem Item(string text, string area) => new BookItem { T = text, Area = area, Ids = IdsIn(text) };

            foreach (ViewParty f in v.Forces)
            {
                var sb = new StringBuilder(LordLine(f)).AppendLine();
                JoinerLines(sb, v, f);
                MemberLines(sb, f.Members);
                L("lords").Add(Item(sb.ToString().TrimEnd(), AreaOf(f.X, f.Y)));
            }
            foreach (ViewHolding h in v.Holdings) L("holdings").Add(Item(HoldingLine(h), AreaOf(h.X, h.Y)));
            foreach (ViewHolding h in v.Frontier) L("holdings").Add(Item("- ENEMY " + FrontierLine(h).Substring(2), AreaOf(h.X, h.Y)));
            foreach (ViewParty p in v.Seen)
            {
                var sb = new StringBuilder(SeenLine(p)).AppendLine();
                JoinerLines(sb, v, p);
                L("enemies").Add(Item(sb.ToString().TrimEnd(), AreaOf(p.X, p.Y)));
            }
            foreach (ViewParty p in v.LastSeen) L("enemies").Add(Item("- SEEN EARLIER " + LastSeenLine(p, v.Hours).Substring(2), AreaOf(p.X, p.Y)));
            foreach (ViewTrail tr in v.Trails)
            {
                TrailPoint last = tr.Points.Count > 0 ? tr.Points[tr.Points.Count - 1] : null;
                L("enemies").Add(Item("- TRAIL " + TrailHead(tr, v.Hours) + ": " + string.Join(" -> ", TrailSteps(tr)) + ".", last != null ? AreaOf(last.X, last.Y) : null));
            }
            for (int i = 0; i < v.Clocks.Count; i++)
            {
                ClockItem c = i < v.ClockItems.Count ? v.ClockItems[i] : null;
                L("sieges").Add(Item("- " + v.Clocks[i], c != null ? AreaOf(c.X, c.Y) : null));
            }
            var lordArea = new Dictionary<string, string>();
            foreach (ViewParty f in v.Forces) if (f.Lord != null) lordArea[f.Lord] = AreaOf(f.X, f.Y);
            if (x?.InForce != null)
                foreach (string l in x.InForce.Replace("\r", "").Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)))
                {
                    List<string> lid = IdsIn(l);
                    L("orders").Add(new BookItem { T = l, Ids = lid, Area = lid.Count > 0 && lordArea.TryGetValue(lid[0], out string la) ? la : null });
                }
            var holdingArea = v.Holdings.ToDictionary(h => h.Id, h => AreaOf(h.X, h.Y));
            foreach (string l in CaptiveLines(v.Captives))
            {
                BookItem it = Item("- " + l, null);
                foreach (string id in it.Ids) if (holdingArea.TryGetValue(id, out string ha)) { it.Area = ha; break; }
                L("captives").Add(it);
            }
            foreach (string l in v.Allied) L("allies").Add(Item("- " + l, null));
            foreach (string l in v.Diplomacy) L("diplomacy").Add(Item("- " + l, null));
            foreach (string l in v.Open) L("votes").Add(Item("- " + l, null));
            foreach (string l in v.Purse) L("purse").Add(Item("- " + l, null));
            if (v.Sum != null)
            {
                var sb = new StringBuilder();
                SumSection(sb, v.Sum);
                foreach (string l in sb.ToString().Replace("\r", "").Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))) L("realm").Add(Item(l, null));
            }
            foreach (string l in v.EnemyRealms) L("realm").Add(Item("- " + l, null));
            foreach (string l in v.News) L("history").Add(Item("- " + l, null));

            var heads = new Dictionary<string, string>
            {
                ["lords"] = "YOUR LORDS AND ARMIES", ["holdings"] = "YOUR HOLDINGS, AND THE ENEMY'S NEAR YOUR LANDS", ["enemies"] = "IN VIEW, SEEN EARLIER AND THE ENEMY ARMIES' TRAILS",
                ["sieges"] = "SIEGES AND BATTLES, run ahead by the game's own clock", ["orders"] = "YOUR ORDERS IN FORCE", ["captives"] = "YOUR CAPTURED LORDS, by where they are held",
                ["allies"] = "ALLIES AND JOINT PLANS", ["diplomacy"] = "DIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT", ["votes"] = "OPEN VOTES AND OFFERS", ["purse"] = "YOUR PURSE",
                ["realm"] = "YOUR REALM IN SUM, AND THE ENEMY REALMS", ["history"] = v.NewsFromChronicle ? "RECENT HISTORY, LAST TWO DAYS (from your chronicle)" : "NEWS, LAST TWO DAYS"
            };
            var notes = new Dictionary<string, string>
            {
                ["lords"] = v.Forces.Any(p => p.Arrives != null) ? MarchNote : null,
                ["holdings"] = WallsNote + "\n" + FoodNote.Replace("(his look-up file names his clan)", "(look_up on him names his clan)"),
                ["sieges"] = v.Clocks.Count > 0 ? ClocksNote : null
            };
            return new
            {
                Poke = poke, Hours = v.Hours, Date = Text.When(v.Hours), Realm = v.Faction, RealmName = v.FactionName,
                Areas = areas.Select(a => new { a.Name, a.Anchor, Holdings = a.Holdings }).ToList(),
                Heads = heads, Notes = notes.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value),
                Lists = lists.ToDictionary(kv => kv.Key, kv => kv.Value.Select(i => new { i.T, i.Area, i.Ids }).ToList()),
                Pages = pages ?? new Dictionary<string, string>()
            };
        }

        /// <summary>All the text of a question book, for its fog check: every list's lines and every page.</summary>
        public static string BookText(View v, ReportExtras x, List<Area> areas, Dictionary<string, string> pages)
        {
            var sb = new StringBuilder();
            foreach (ViewParty f in v.Forces) { sb.AppendLine(LordLine(f)); JoinerLines(sb, v, f); MemberLines(sb, f.Members); }
            foreach (ViewHolding h in v.Holdings) sb.AppendLine(HoldingLine(h));
            foreach (ViewHolding h in v.Frontier) sb.AppendLine(FrontierLine(h));
            foreach (ViewParty p in v.Seen) { sb.AppendLine(SeenLine(p)); JoinerLines(sb, v, p); }
            foreach (ViewParty p in v.LastSeen) sb.AppendLine(LastSeenLine(p, v.Hours));
            foreach (ViewTrail tr in v.Trails) sb.AppendLine(TrailHead(tr, v.Hours) + ": " + string.Join(" -> ", TrailSteps(tr)));
            foreach (string l in v.Clocks.Concat(CaptiveLines(v.Captives)).Concat(v.Allied).Concat(v.Diplomacy).Concat(v.Open).Concat(v.Purse).Concat(v.EnemyRealms).Concat(v.News)) sb.AppendLine(l);
            if (x?.InForce != null) sb.AppendLine(x.InForce);
            if (pages != null) foreach (string p in pages.Values) sb.AppendLine(p);
            return sb.ToString();
        }
    }
}
