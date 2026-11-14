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
        /// <summary>Who the human player is and how his battles are fought, with his record in a line (0.17.3, InPerson.Lines).</summary>
        public List<string> HumanPlayer = new List<string>();
        /// <summary>Each enemy army's trail over the last two days, as the realm saw it (0.6.4).</summary>
        public List<ViewTrail> Trails = new List<ViewTrail>();
        public List<ViewHolding> Frontier = new List<ViewHolding>();
        public List<string> News = new List<string>();
        /// <summary>The news comes from the realm's chronicle (its recent history), not only from its notices.</summary>
        public bool NewsFromChronicle;
        public int OthersInView;
        /// <summary>The watched realm only: its lords held prisoner, and the whole realm in a few numbers.</summary>
        public List<ViewCaptive> Captives = new List<ViewCaptive>();
        public RealmSum Sum;
        /// <summary>The watched realm only: its purse in a few lines (0.9.4, PurseView).</summary>
        public List<string> Purse = new List<string>();
        /// <summary>The leaders of the realm's own clans (0.18.3): YOUR PURSE names each beside his clan's gold, for the gift order.</summary>
        public List<string> ClanLeaders = new List<string>();
        /// <summary>The game's clocks the realm can see (0.7.3, Forecast): each siege and each battle, run ahead by the game's own rules.</summary>
        public List<string> Clocks = new List<string>();
        /// <summary>Each clock's place and the hour it decides things (0.14, a news wake's theatre): one per line of Clocks, in the same order.</summary>
        public List<ClockItem> ClockItems = new List<ClockItem>();
        /// <summary>The realms at war with it, as the encyclopedia shows any player (0.7.4): living lords, and their parties' strength in all, beside what of it is in sight.</summary>
        public List<string> EnemyRealms = new List<string>();
        /// <summary>Its diplomacy, and its open votes and the offers made to it (0.13, DiploRules): for a realm an agent serves.</summary>
        public List<string> Diplomacy = new List<string>();
        public List<string> Open = new List<string>();
        /// <summary>Its allies, whether their rulers read letters, their last letters, and the joint plans between them (0.13, Allies).</summary>
        public List<string> Allied = new List<string>();
        /// <summary>The watched realm only (0.19.4, Views.Borders): the enemy realms that border it and their strength, what of it was seen near its lands, its friendly and neutral neighbours, and its castles and towns that face no enemy.</summary>
        public List<string> Borders = new List<string>();
    }

    /// <summary>
    /// Where one siege or battle of the report is, and when the game's own rules say it is decided (0.14, Theatre): a siege when a
    /// storm is likeliest or the food runs out, whichever comes first; a battle when it ends; a raid when the village is looted.
    /// NaN when not within the five days the clock runs ahead.
    /// </summary>
    internal sealed class ClockItem
    {
        public string Kind, Place, Why;
        public float X, Y;
        public double DecidedAt = double.NaN;
        /// <summary>The realm's own holding (a siege of it, a battle at it) or its own lords in it.</summary>
        public bool Ours;
        public List<string> Parties = new List<string>();
    }

    internal sealed class ViewCaptive
    {
        public string Hero, Name, Holding, HoldingName, HoldingKind, HoldingFaction, PartyLord, PartyName, PartyFaction;
        public double Since = double.NaN;
    }

    /// <summary>The whole realm in a few numbers: its field forces, its castles and towns, its villages, its captured lords.</summary>
    internal sealed class RealmSum
    {
        public int Lords, Men, Armies, ArmyLords, Ordered, Forts, GarrisonMen, Militia, Villages, Looted, Raided, Besieged, Captives;
        public float Strength;
    }

    internal sealed class ViewParty
    {
        public string Id, Lord, Name, Faction, FactionName, Relation, Kind, Doing, Near, SeenBy, NearestHolding;
        /// <summary>One of the realm's own lords, when his gold is near the floor for recruiting (0.15) or his wages near their cap (0.16): PurseView.RecruitNote.</summary>
        public string Poor;
        /// <summary>The human player's own party, or an army he leads or a lord marching in it (0.17.3): his battles are fought on the spot.</summary>
        public bool InPerson;
        /// <summary>The holding its doing names (going to, besieging, raiding, defending, patrolling around), when it names one: for the dashboard and the "ask the way" tool.</summary>
        public string Toward;
        public int Men, ArmyMen, ArmyParties;
        public float Strength, X, Y, NearDistance, HoldingDistance;
        public double SeenHours;
        /// <summary>The same two distances by road, as the game reckons a march (Roads; NaN when not known), and the entrance of the ground it stood on (0.7.1).</summary>
        public float NearRoad = float.NaN, HoldingRoad = float.NaN;
        public string Entrance;
        /// <summary>
        /// When it gets where it is going, by the game's own marching rules along the way the game planned for it ("there in about
        /// 14 hours (Summer 19, 14:00)"), and that hour (NaN when not reckoned); and its march, the parts of its speed sum, for the
        /// "ask the way" tool (0.7.3, Forecast, GamePace).
        /// </summary>
        public string Arrives;
        public double ArrivesAt = double.NaN;
        public Common.Pace March;
        /// <summary>
        /// The army it is marching to join (0.12.3: every lord marching to an army within view should be listed under that
        /// army with how far away they are from it): the army's party id, how far from it as the crow flies, and the hours to it
        /// at its own pace while the army holds where it is. Set only when that army is in view or the realm's own.
        /// </summary>
        public string Joining;
        public float JoiningDistance = float.NaN;
        public double JoiningHours = double.NaN;
        /// <summary>
        /// The lords marching in its army, when it leads one of the realm's own (0.13.8, Oct 29: the lieutenant could not see
        /// lords in armies): the report named an army's size only, so neither the lieutenant nor an agent could order its lords by name.
        /// </summary>
        public List<ViewParty> Members;
    }

    /// <summary>
    /// Where the realm saw an enemy army over the last two days, oldest first (Brain.Trails): so the agent can tell an
    /// army's road from its errands (Oct 16: an army turning aside to recruit, forage or strike has not changed
    /// its aim).
    /// </summary>
    internal sealed class ViewTrail
    {
        public string Id, Lord, Name, FactionName;
        public int ArmyMen, ArmyParties;
        public float Strength;
        public bool InView;
        public double LastSeenHours = double.NaN;
        public List<TrailPoint> Points = new List<TrailPoint>();
    }

    internal sealed class TrailPoint
    {
        public double Hours;
        public float X, Y, NearDistance, HoldingDistance;
        public string Near, NearestHolding, Doing;
        public float NearRoad = float.NaN, HoldingRoad = float.NaN;
        /// <summary>Hours out of sight just before this point; 0 when it was in view the hour before.</summary>
        public double AfterGap;
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
        public float ThreatRoad = float.NaN;
        /// <summary>The realm's own castles and towns: wall level, the defenders' strength (garrison, militia, lords inside), and the besiegers' strength it holds against.</summary>
        public int WallLevel;
        public float Defence, LordsInside, HoldsUpTo;
        /// <summary>The realm's own castles and towns only (0.13.19): the garrison's strength, and its food now, its most and what it eats a day under siege (-1: not shown).</summary>
        public float GarrisonStrength = -1, Food = -1, FoodCap, FoodPerDay;
        /// <summary>The realm's own castles and towns only (0.13.21): the clan that owns it, and the food its supply orders are bringing.</summary>
        public string Clan, FoodComing;
        /// <summary>The realm's own castles and towns only (0.19.5): the food its own lords inside carry and what they eat a day, which a siege draws on once the place's own runs out.</summary>
        public float LordsFood, LordsEat;
    }

    /// <summary>The sections a poke adds before the report: what changed since the agent's last answer, its orders in force, ready-made facts.</summary>
    internal sealed class ReportExtras
    {
        public string Since, InForce, Facts;
        /// <summary>The lieutenant's report only (0.10): the player's own party, the ruler it serves.</summary>
        public string Ruler;
        public int LastAnswered = -1;
        public bool Lookups;
        /// <summary>The chances to strike (0.15, Views.ChanceLines); with it, the report holds both short lists. Null: neither.</summary>
        public List<string> Chances;
        /// <summary>A council's report: the news the wake rule held back, which it reads instead of being woken for it (0.15).</summary>
        public string Held;
    }

    /// <summary>Builds views and reports on the worker, from a snapshot. No game objects here.</summary>
    internal static partial class Views
    {
        /// <summary>
        /// A section put into a report right after another (0.18: YOUR WATCHES after WHY YOU ARE ASKED NOW): after the blank line that ends the section named, else at the
        /// report's end. The report's own line ends (\r\n) are kept.
        /// </summary>
        public static string AfterSection(string report, string heading, string section)
        {
            if (string.IsNullOrEmpty(section)) return report;
            report = report ?? "";
            int i = report.IndexOf(heading, StringComparison.Ordinal);
            int end = i < 0 ? -1 : report.IndexOf("\r\n\r\n", i, StringComparison.Ordinal);
            if (end < 0 && i >= 0) end = report.IndexOf("\n\n", i, StringComparison.Ordinal);
            if (end < 0) return report.TrimEnd() + "\r\n\r\n" + section.TrimEnd() + "\r\n";
            string gap = string.CompareOrdinal(report, end, "\r\n\r\n", 0, 4) == 0 ? "\r\n\r\n" : "\n\n";
            return report.Substring(0, end) + gap + section.TrimEnd() + report.Substring(end);
        }

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
                // the player's own party is never one of an agent's lords to order (Oct 22: a realm of the player's with an agent)
                if (r.Faction != fid || r.Kind != "lord" || r.Attached || r.Id == s.PlayerParty) continue;
                v.Forces.Add(Party(s, r, null, "own", own));
            }
            v.Forces = v.Forces.OrderByDescending(x => x.ArmyMen > 0 ? x.ArmyMen : x.Men).ToList();
            // each army's own lords, under it (0.13.8)
            foreach (ViewParty f in v.Forces) if (f.ArmyMen > 0) f.Members = ArmyMembers(s, f.Id, fid);

            foreach (KeyValuePair<string, Sighting> kv in sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r) || r.Attached) continue;
                bool enemy = hostile.Contains(r.Faction ?? "") || r.Kind == "bandit";
                if (!enemy && r.Kind != "lord") { v.OthersInView++; continue; }
                string relation = enemy ? "enemy" : allies.Contains(r.Faction ?? "") ? "ally" : "neutral";
                v.Seen.Add(Party(s, r, kv.Value, relation, own));
            }
            // what the cap of 40 cuts first: bandits and others before any enemy lord, and an enemy army never (Oct 16: 15 to 24
            // bands near the villages pushed Aragorn's army of 5,532 out of the list for days, though the realm saw it)
            v.Seen = v.Seen.OrderBy(SeenRank).ThenBy(Reach).ToList();
            if (v.Seen.Count > 40)
            {
                int keep = Math.Max(40, v.Seen.Count(x => SeenRank(x) == 0));
                v.SeenNotListed = v.Seen.Count - keep;
                v.Seen = v.Seen.Take(keep).ToList();
            }
            // the lords marching to join an army, with how far they are from it (0.12.3): only toward an army the realm can see
            foreach (ViewParty p in v.Forces.Concat(v.Seen))
            {
                if (!s.PartyById.TryGetValue(p.Id, out PartyRow r)) continue;
                PartyRow head = JoiningHead(s, r);
                if (head == null || !(head.Faction == fid || sees.ContainsKey(head.Id))) continue;
                float d = Dist(head.X, head.Y, r.X, r.Y);
                float per = Forecast.DayPerHour(s, r);
                p.Joining = head.Id;
                p.JoiningDistance = Text.R1(d);
                p.JoiningHours = per > 0.05f ? Math.Round(d / per, 1) : double.NaN;
            }

            foreach (SettlementRow h in own)
            {
                ViewHolding vh = Holding(s, h, true);
                // the realm's own only (0.13.19): its garrison's strength (what a garrison_floor is weighed in) and its food (the supply order)
                if (Tools.World.IsFort(h))
                {
                    vh.GarrisonStrength = h.GarrisonStrength;
                    vh.Food = h.Food;
                    vh.FoodCap = h.FoodCap;
                    vh.FoodPerDay = h.FoodPerDay;
                    vh.Clan = h.OwnerClanName;
                    vh.FoodComing = FoodComing(s, fid, h);
                    SiegeFoodRules.OwnLordsInside(s, fid, h.Id, out vh.LordsFood, out vh.LordsEat);
                }
                // the enemy in view that could reach it soonest: by road where the game's table knows the way
                float ThreatRoad(ViewParty x) => s.PartyById.TryGetValue(x.Id, out PartyRow xr) ? Roads.To(s, xr, h) : float.NaN;
                ViewParty threat = v.Seen.Where(x => x.Relation == "enemy" && x.Kind == "lord")
                                         .OrderBy(x => float.IsNaN(ThreatRoad(x)) ? Dist(x.X, x.Y, h.X, h.Y) + 1e6f : ThreatRoad(x)).FirstOrDefault();
                if (threat != null)
                {
                    vh.Threat = threat.Name;
                    vh.ThreatMen = threat.ArmyMen > 0 ? threat.ArmyMen : threat.Men;
                    vh.ThreatDistance = Text.R1(Dist(threat.X, threat.Y, h.X, h.Y));
                    vh.ThreatRoad = R1(ThreatRoad(threat));
                }
                v.Holdings.Add(vh);
            }
            v.Holdings = v.Holdings.OrderBy(x => x.Kind == "village" ? 1 : 0).ThenBy(x => x.Name).ToList();

            if (mem != null)
            {
                // the cap of 25 cuts by the same order as IN VIEW: an enemy army is never cut, then enemy lords, then bandits and
                // others, the most recently seen first within each (Oct 16: bandits seen later pushed Aragorn's army of 5,532 out)
                List<ViewParty> earlier = mem.LastSeen.Values.Where(last => !sees.ContainsKey(last.Id) && s.Hours - last.SeenHours >= 1)
                                                             .OrderBy(SeenRank).ThenBy(x => s.Hours - x.SeenHours).ToList();
                int keepEarlier = Math.Max(25, earlier.Count(x => SeenRank(x) == 0));
                v.LastSeen = earlier.Take(keepEarlier).ToList();
                // a trail of one point says no more than the lists above
                v.Trails = mem.Trails.Values.Where(t => t.Points.Count >= 2).OrderByDescending(t => t.Strength).ToList();
                if (Chronicle.Holds(fid))
                {
                    // the chronicle's last two days: the notices, and also the enemy armies sighted and the orders given
                    v.News = Chronicle.For(fid).Recent(s.Hours, Common.RecentHistory.Hours, Common.RecentHistory.Lines);
                    v.NewsFromChronicle = true;
                }
                else
                {
                    foreach (Notice n in mem.News.Where(n => s.Hours - n.Hours <= 48))
                        v.News.Add(Text.When(n.Hours) + ": " + n.Text);
                    if (v.News.Count > 20) v.News = v.News.Skip(v.News.Count - 20).ToList();
                }
            }

            if (own.Count > 0)
            {
                foreach (SettlementRow h in s.Settlements)
                {
                    if (h.Kind == "village" || h.Kind == "other" || h.Faction == null || !hostile.Contains(h.Faction)) continue;
                    float d = own.Min(o => Dist(o.X, o.Y, h.X, h.Y));
                    if (d > 60f) continue;
                    bool counted = s.InspectedByWatched != null && fid == s.Watched && s.InspectedByWatched.Contains(h.Id);
                    ViewHolding fh = Holding(s, h, counted);
                    if (counted && fh.WallLevel > 0)
                    {
                        // an enemy's place: its garrison and militia, and only the lords inside it that the realm sees (0.7.4)
                        float inside = 0;
                        foreach (PartyRow r in s.Parties)
                            if (r.Kind == "lord" && !r.Attached && r.InSettlement == h.Id && sees != null && sees.ContainsKey(r.Id)) inside += r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength;
                        fh.LordsInside = inside;
                        fh.Defence = h.GarrisonStrength + h.MilitiaStrength + inside;
                        fh.HoldsUpTo = (float)(fh.Defence * Tools.World.WallFactor(h));
                    }
                    v.Frontier.Add(fh);
                }
                v.Frontier = v.Frontier.OrderBy(h => own.Min(o => Dist(o.X, o.Y, h.X, h.Y))).Take(20).ToList();
            }
            if (fid == s.Watched)
            {
                foreach (CaptiveRow c in s.Captives) v.Captives.Add(Captive(s, c));
                v.Sum = Sum(s, fid, own);
                v.Purse = PurseView.Lines(s, fid);
                v.ClanLeaders = s.Clans.Where(c => !string.IsNullOrEmpty(c.Leader)).Select(c => c.Leader).ToList();
                // the enemies that border it and their strength (0.19.4)
                v.Borders = Borders(s, fid, v, hostile);
            }
            // who the human player is and how his battles are fought (0.17.3): for every realm, told in plain words
            v.HumanPlayer = InPerson.Lines(s, fid);
            v.Clocks = Clocks(s, fid, sees, allies, v.ClockItems);
            v.EnemyRealms = EnemyRealms(s, fid, sees);
            // its diplomacy and its open votes and offers, as its ruler's screens show them (0.13, B3)
            if (s.Diplomacy != null && s.Diplomacy.TryGetValue(fid, out DiploView dv))
            {
                v.Diplomacy = DiploRules.Diplomacy(dv, s.Hours, Text.When);
                v.Open = DiploRules.Open(dv, Text.When);
            }
            // its allies, their letters and the joint plans between them (0.13, B5)
            v.Allied = Allies.Section(s, fid);
            return v;
        }

        /// <summary>
        /// Each realm at war with this one, as its encyclopedia page shows it to any player (EncyclopediaFactionPageVM: the living lords,
        /// and "Strength", the sum of its lord parties' strength), beside how many of those parties, and how much of that strength, the
        /// realm has in sight now: how much of the enemy it cannot see. The parties' strength here is the hourly copy's (EstimatedStrength).
        /// </summary>
        public static List<string> EnemyRealms(Snapshot s, string fid, Dictionary<string, Sighting> sees)
        {
            var lines = new List<string>();
            if (!s.Factions.TryGetValue(fid ?? "", out FactionRow me)) return lines;
            foreach (string enemy in me.AtWar)
            {
                if (!s.Factions.TryGetValue(enemy, out FactionRow fr)) continue;
                List<PartyRow> parties = s.Parties.Where(r => r.Kind == "lord" && r.Faction == enemy).ToList();
                double all = parties.Sum(r => r.Strength);
                List<PartyRow> seen = parties.Where(r => sees != null && (sees.ContainsKey(r.Id) || (r.Attached && r.ArmyLeader != null && sees.ContainsKey(r.ArmyLeader)))).ToList();
                lines.Add(fr.Name + ": " + fr.Lords + " lords; their parties' strength " + Text.N((int)Math.Round(all)) + " in all; in your sight now " + seen.Count + (seen.Count == 1 ? " party" : " parties")
                          + ", strength " + Text.N((int)Math.Round(seen.Sum(r => r.Strength))) + ".");
            }
            return lines;
        }

        /// <summary>The doings whose holding a march ends at (not a patrol round it, nor a chase).</summary>
        private static bool MarchesTo(PartyRow r) => r.Doing == "GoToSettlement" || r.Doing == "BesiegeSettlement" || r.Doing == "RaidSettlement" || r.Doing == "DefendSettlement" || r.Doing == "AssaultSettlement";

        /// <summary>
        /// The sieges and battles the realm can see, each with its clock run ahead (Forecast): a siege of its own holding or by
        /// its own or an ally's lords, or in its sight (the camp's leader seen, or the place inspected); a battle with any of its
        /// lords in it, at its holding, or with any party in it in sight. Its own side's morale is known; another's is not, and
        /// is taken as middling (50).
        /// </summary>
        public static List<string> Clocks(Snapshot s, string fid, Dictionary<string, Sighting> sees, HashSet<string> allies, List<ClockItem> items = null)
        {
            var lines = new List<string>();
            items?.Clear();
            // each line's place and the hour it decides things, for a news wake's theatre (0.14): the lists stay in step
            void Item(string kind, string place, float x, float y, bool ours, double at, string why, IEnumerable<string> parties)
            {
                if (items == null) return;
                var it = new ClockItem { Kind = kind, Place = place, X = x, Y = y, Ours = ours, DecidedAt = at, Why = why };
                foreach (string p in parties) if (p != null) it.Parties.Add(p);
                items.Add(it);
            }
            double Soonest(params double[] hours) { double best = double.NaN; foreach (double h in hours) if (!double.IsNaN(h) && h > s.Hours - 1e-6 && (double.IsNaN(best) || h < best)) best = h; return best; }
            // the realm's own (0.7.4: its allies' sieges and battles far off are not in its sight, and leaked; the fog check caught them)
            bool Friend(string f) => f != null && f == fid;
            bool Sees(string party) => party != null && sees != null && sees.ContainsKey(party);
            bool Mine(string party) => party != null && s.PartyById.TryGetValue(party, out PartyRow pr) && pr.Faction == fid;
            string PartyName(string id) => id != null && s.PartyById.TryGetValue(id, out PartyRow p) ? p.Name ?? id : id != null && s.SettlementById.TryGetValue(id, out SettlementRow h) ? h.Name : "?";
            string LordTag(string id) => id != null && s.PartyById.TryGetValue(id, out PartyRow p) && p.Faction == fid && p.Leader != null ? " [" + p.Leader + "]" : "";
            foreach (SiegeRow g in s.Sieges)
            {
                if (!s.SettlementById.TryGetValue(g.Settlement, out SettlementRow h)) continue;
                bool ours = Friend(h.Faction), theirs = Friend(g.BesiegerFaction);
                bool inspected = s.InspectedByWatched != null && fid == s.Watched && s.InspectedByWatched.Contains(h.Id);
                if (!ours && !theirs && !Sees(g.Besieger) && !inspected) continue;
                // the agent's own storm rule, for a siege its besiege order leads (StormGate)
                StormRule rule = fid == s.Watched && g.BesiegerFaction == fid ? Tools.Runner.StormRuleFor(g.Besieger) : null;
                Forecast.SiegeClock c = Forecast.Siege(g, s.Hours, rule);
                List<Tuple<bool, int, double>> joiners = Joiners(s, fid, sees, g.Settlement, g.BesiegerFaction, h.Faction);
                // a siege seen only by its camp: what is inside the walls (the defenders, the food, so when a storm comes) is not seen (0.11.3)
                bool known = ours || theirs || inspected;
                string stormNow = g.Storming || !known ? null : Forecast.StormNow(s, g, theirs, joiners);
                lines.Add(h.Name + " [" + h.Id + "] (" + h.Kind + ", " + FactionName(s, h.Faction) + "), besieged by " + PartyName(g.Besieger) + LordTag(g.Besieger)
                          + " (" + FactionName(s, g.BesiegerFaction) + ") since " + Text.When(g.StartHours) + " (" + Math.Round(s.Hours - g.StartHours) + " hours)"
                          + (g.Storming ? "; a storm is being fought now (below)" : "") + ": " + Forecast.SiegeText(s, g, c, ours, stormNow, known));
                // decided: by the storm (the agent's own rule, when it leads the siege, else the likeliest hour) or by hunger, whichever comes first;
                // a siege seen only from outside its walls has no clock the realm can read
                double storm = c.Rule != null ? c.RuleStormAt : c.MedianAt;
                double decided = g.Storming || !known ? double.NaN : Soonest(storm, c.FoodOutAt);
                string why = double.IsNaN(decided) ? null : !double.IsNaN(c.FoodOutAt) && Math.Abs(decided - c.FoodOutAt) < 1e-6 ? "its food runs out" : "a storm is likeliest by then";
                Item("siege", h.Id, h.X, h.Y, ours || theirs, decided, why, new[] { g.Besieger });
            }
            foreach (BattleRow b in s.Battles)
            {
                bool att = Friend(b.Attackers.Faction), def = Friend(b.Defenders.Faction);
                SettlementRow at = b.Settlement != null && s.SettlementById.TryGetValue(b.Settlement, out SettlementRow hh) ? hh : null;
                bool seen = b.Attackers.Parties.Concat(b.Defenders.Parties).Any(Sees);
                bool mineIn = b.Attackers.Parties.Concat(b.Defenders.Parties).Any(Mine);
                if (!att && !def && !mineIn && !(at != null && at.Faction == fid) && !seen) continue;
                List<Tuple<bool, int, double>> joiners = at != null ? Joiners(s, fid, sees, at.Id, b.Attackers.Faction, b.Defenders.Faction) : new List<Tuple<bool, int, double>>();
                var sb = new StringBuilder();
                sb.Append(at != null ? "At " + at.Name + " [" + at.Id + "]" : "In the field near " + Near(s, b.X, b.Y)).Append(": ").Append(Forecast.KindText(b.Kind))
                  .Append(" since ").Append(Text.When(b.StartHours)).Append(" (").Append(Math.Round(s.Hours - b.StartHours)).Append(" hours). ");
                sb.Append("Attackers: ").Append(SideText(s, fid, b.Attackers, PartyName, LordTag)).Append("; defenders: ").Append(SideText(s, fid, b.Defenders, PartyName, LordTag)).Append(". ");
                if (b.Kind == "raid" && b.Defenders.Healthy <= 0 && at != null)
                {
                    // the looting: no battle is left to run ahead, the village's health runs down (0.7.4)
                    double left = Forecast.RaidHoursLeft(at, s.Hours, b.Attackers.Healthy, out double perHour, out bool measured);
                    sb.Append("Nobody is left to defend it: the village is looted when its health runs out (each hour the raiders take the square root of their number + 5, over 900, of it; some peoples' and lords' gifts take more). ")
                      .Append("It has ").Append(Math.Round(at.HitPoints * 100)).Append("% left, falling about ").Append((perHour * 100).ToString("0.#", Text.Inv)).Append("% an hour")
                      .Append(measured ? " (measured over the last hour)" : " (by the rule)").Append(", so it is looted in about ").Append(Forecast.HoursText(left)).Append(" (").Append(Text.When(s.Hours + left)).Append(") unless the raiders are driven off.");
                    lines.Add(sb.ToString());
                    Item("raid", at.Id, at.X, at.Y, at.Faction == fid || mineIn, double.IsNaN(left) ? double.NaN : s.Hours + left, "the village is looted", b.Attackers.Parties.Concat(b.Defenders.Parties));
                    continue;
                }
                // a battle under way two rounds or more: at the rates measured in it (0.7.4); else by the rules, the morale the realm
                // can see being its own side's and the other side's run low, middling and high
                Forecast.BattleClock c = Forecast.Measured(b, s.Hours, att ? true : def ? false : (bool?)null, joiners);
                string hours;
                if (c != null) hours = double.IsNaN(c.EndsAt) ? "not within 5 days" : "about " + Forecast.HoursText(c.EndsAt - s.Hours);
                else if (att || def) c = Forecast.BattleByMorale(b, s.Hours, def, joiners, out hours);
                else
                {
                    c = Forecast.Battle(b, s.Hours, joiners, 50f, 50f);
                    hours = double.IsNaN(c.EndsAt) ? "not within 5 days" : "about " + Forecast.HoursText(c.EndsAt - s.Hours);
                }
                sb.Append(c.Measured ? "At the rates measured over its " + Forecast.HoursText(c.MeasuredHours) + " so far, the attackers lose about " : "At the game's rates now the attackers lose about ")
                  .Append(Math.Round(c.AttackerLossPerHour)).Append(" men an hour, the defenders about ").Append(Math.Round(c.DefenderLossPerHour)).Append(". ");
                string runAhead = c.Measured ? "Run ahead at those rates" : "Run ahead";
                if (!double.IsNaN(c.EndsAt))
                    sb.Append(runAhead).Append(", the ").Append(c.Winner).Append(" win")
                      .Append(c.How == "rout" ? " when the other side's morale breaks" : c.How == "retreat" ? " when the other side retreats (it gets away with its lords; nothing is captured)" : " when the other side is all down")
                      .Append(", in ").Append(hours).Append(" (").Append(Text.When(c.EndsAt)).Append(c.Measured ? "), " : " at middling morale), ")
                      .Append(Text.N(c.Winner == "attackers" ? c.AttackersLeft : c.DefendersLeft)).Append(" of them still standing")
                      // what the other side keeps (0.11.6)
                      .Append((c.Winner == "attackers" ? c.DefendersLeft : c.AttackersLeft) > 0 ? " and about " + Text.N(c.Winner == "attackers" ? c.DefendersLeft : c.AttackersLeft) + " of the other side left." : ".");
                else sb.Append(runAhead).Append(" five days, it has not ended (").Append(Text.N(c.AttackersLeft)).Append(" attackers, ").Append(Text.N(c.DefendersLeft)).Append(" defenders left).");
                if (joiners.Count > 0)
                {
                    int toA = joiners.Where(j => j.Item1).Sum(j => j.Item2), toD = joiners.Where(j => !j.Item1).Sum(j => j.Item2);
                    double firstAt = joiners.Min(j => j.Item3);
                    sb.Append(" Marching to it: ").Append(joiners.Count).Append(joiners.Count == 1 ? " lord" : " lords").Append(toA > 0 ? ", " + Text.N(toA) + " men for the attackers" : "")
                      .Append(toD > 0 ? ", " + Text.N(toD) + " men for the defenders" : "").Append(", the first in about ").Append(Forecast.HoursText(Math.Max(0, firstAt - s.Hours))).Append(" (counted in).");
                }
                if (att || def)
                {
                    BattleSideRow mine = att ? b.Attackers : b.Defenders;
                    sb.Append(" Your side's morale ").Append(Math.Round(mine.Morale))
                      .Append(!mine.SettlementLed ? "" : at != null && at.Kind == "village" ? " (led by the village: it never breaks)" : " (behind walls: it never breaks)").Append("; the other side's you cannot see.");
                }
                lines.Add(sb.ToString());
                Item("battle", at?.Id, at != null ? at.X : b.X, at != null ? at.Y : b.Y, att || def || mineIn || (at != null && at.Faction == fid), c.EndsAt, "the battle ends",
                     b.Attackers.Parties.Concat(b.Defenders.Parties));
            }
            return lines;
        }

        /// <summary>
        /// The lords marching to a holding, as joiners of a siege's storm or a battle there: parties the realm sees or its own,
        /// heading for it and not yet in a battle, each with the hour it arrives (the game's marching rules) and the side it
        /// would join (its own realm's, or an ally's).
        /// </summary>
        public static List<Tuple<bool, int, double>> Joiners(Snapshot s, string fid, Dictionary<string, Sighting> sees, string place, string attackerFaction, string defenderFaction)
        {
            var joiners = new List<Tuple<bool, int, double>>();
            bool Side(string sideFaction, string f) => f == sideFaction || (sideFaction != null && s.Factions.TryGetValue(sideFaction, out FactionRow fr) && fr.Allies.Contains(f ?? ""));
            foreach (PartyRow r in s.Parties)
            {
                if (r.Kind != "lord" || r.Attached || r.InBattle || r.InSettlement == place || r.TargetSettlement != place || !MarchesTo(r)) continue;
                if (r.Faction != fid && (sees == null || !sees.ContainsKey(r.Id))) continue;
                bool toAttackers = Side(attackerFaction, r.Faction), toDefenders = Side(defenderFaction, r.Faction);
                if (toAttackers == toDefenders) continue;
                // only a lord walking there now, by a way that ends there (0.7.4: Asta, "defending" Hollin Gate from 5.5 away, stood
                // still through the whole storm, but was counted as joining within the hour)
                if (!Forecast.Marching(r) || !s.SettlementById.TryGetValue(place, out SettlementRow h) || !Forecast.EndsAt(r, h)) continue;
                Forecast.Arrival a = Forecast.ArrivalOf(s, r, 72);
                if (a == null || !a.Reached) continue;
                joiners.Add(Tuple.Create(toAttackers, r.ArmyMen > 0 ? r.ArmyMen : r.Men, a.At));
            }
            return joiners;
        }

        private static string SideText(Snapshot s, string fid, BattleSideRow side, Func<string, string> name, Func<string, string> tag)
        {
            string who = side.SettlementLed ? "the holding's garrison and those with it" : name(side.Leader) + tag(side.Leader) + (side.Parties.Count > 1 ? " and " + (side.Parties.Count - 1) + " more parties" : "");
            return who + " (" + FactionName(s, side.Faction) + "), " + Text.N(side.Healthy) + " men standing" + (side.Casualties > 0 ? ", " + Text.N(side.Casualties) + " fallen so far" : "");
        }

        private static string Near(Snapshot s, float x, float y)
        {
            SettlementRow h = NearestSettlement(s, x, y, out float d);
            return h == null ? "nowhere known" : h.Name + " (" + Text.D(d) + " away)";
        }

        /// <summary>A captured lord as the realm knows it: where they are held (a holding, or the party holding them) and since when.</summary>
        public static ViewCaptive Captive(Snapshot s, CaptiveRow c)
        {
            var vc = new ViewCaptive { Hero = c.Hero, Name = c.Name ?? c.Hero, Holding = c.Holding, PartyLord = c.PartyLord, Since = c.Since };
            if (c.Holding != null && s.SettlementById.TryGetValue(c.Holding, out SettlementRow h))
            {
                vc.HoldingName = h.Name;
                vc.HoldingKind = h.Kind;
                vc.HoldingFaction = FactionName(s, h.Faction);
            }
            if (c.Party != null)
            {
                vc.PartyName = s.PartyById.TryGetValue(c.Party, out PartyRow p) ? p.Name ?? "a party" : "a party";
                vc.PartyFaction = FactionName(s, c.Faction);
            }
            return vc;
        }

        public static RealmSum Sum(Snapshot s, string fid, List<SettlementRow> own)
        {
            var sum = new RealmSum { Captives = s.Captives.Count };
            foreach (PartyRow r in s.Parties)
            {
                if (r.Faction != fid || r.Kind != "lord") continue;
                sum.Lords++;
                if (Tools.Runner.ForParty(r.Id) != null) sum.Ordered++;
                if (r.Attached) continue;   // the game counts a party marching in an army with its leader (Army.TotalHealthyMembers)
                sum.Men += r.ArmyMen > 0 ? r.ArmyMen : r.Men;
                sum.Strength += r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength;
                if (r.ArmyLeader == r.Id)
                {
                    sum.Armies++;
                    sum.ArmyLords += r.ArmyParties;
                }
            }
            foreach (SettlementRow h in own)
            {
                if (h.Kind == "village")
                {
                    sum.Villages++;
                    if (h.VillageState == "Looted") sum.Looted++;
                    else if (h.VillageState == "BeingRaided") sum.Raided++;
                    continue;
                }
                if (h.Kind != "town" && h.Kind != "castle") continue;
                sum.Forts++;
                sum.GarrisonMen += h.Garrison;
                sum.Militia += h.Militia;
                if (h.BesiegedBy != null) sum.Besieged++;
            }
            return sum;
        }

        /// <summary>The ids the report names beyond its own lists: the captured lords, and where and by whom they are held.</summary>
        public static IEnumerable<string> CaptiveIds(View v)
        {
            foreach (ViewCaptive c in v.Captives)
            {
                yield return c.Hero;
                if (c.Holding != null) yield return c.Holding;
                if (c.Holding == null && c.PartyLord != null) yield return c.PartyLord;
            }
        }

        public static string Report(View v, List<Trigger> triggers, ReportExtras x = null)
        {
            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine("REPORT FOR " + v.FactionName.ToUpperInvariant());
            sb.AppendLine("Ruler: " + (v.Ruler ?? "unknown") + ". " + Text.When(v.Hours) + ", " + (v.Night ? "night." : "daytime."));
            sb.AppendLine("At war with: " + (v.AtWar.Count > 0 ? string.Join(", ", v.AtWar) : "no one") + ". Allies: " + (v.Allies.Count > 0 ? string.Join(", ", v.Allies) : "none") + ".");
            sb.AppendLine("Distances are in map units: as the crow flies, then in brackets by road, the way the game reckons a march round mountains and rivers" + (Roads.Known ? "" : " (by road not known in this report)")
                          + ". A party is seen only within sight of your lords, castles and towns. Ids in [brackets] are what orders should name."
                          + (x != null && x.Lookups ? " More on any id: lookups/<id>.txt." : ""));

            if (x?.Ruler != null) sb.AppendLine().AppendLine("YOUR RULER (the player, whom you serve)").AppendLine("- " + x.Ruler);
            if (triggers != null && triggers.Count > 0)
            {
                sb.AppendLine().AppendLine("WHY YOU ARE ASKED NOW");
                foreach (Trigger t in triggers) sb.AppendLine("- " + t.Text);
            }
            if (x?.Since != null)
                sb.AppendLine().AppendLine("WHAT CHANGED SINCE YOUR LAST ANSWER" + (x.LastAnswered >= 0 ? " (to poke #" + x.LastAnswered + ")" : "")).AppendLine(x.Since);
            if (x?.InForce != null) sb.AppendLine().AppendLine("YOUR ORDERS IN FORCE").AppendLine(x.InForce);
            if (x?.Held != null) sb.AppendLine().AppendLine("NEWS THAT DID NOT CALL YOU, SINCE YOUR LAST ANSWER").AppendLine(x.Held);
            if (x?.Facts != null) sb.AppendLine().AppendLine("READY-MADE FACTS").AppendLine(x.Facts);
            ListsSection(sb, v, x);

            if (v.Sum != null)
            {
                RealmSum r = v.Sum;
                sb.AppendLine().AppendLine("YOUR REALM IN SUM");
                sb.AppendLine("- In the field: " + r.Lords + " lords leading parties, " + Text.N(r.Men) + " men, strength " + Text.N((int)r.Strength)
                              + (r.Armies > 0 ? "; " + r.Armies + (r.Armies == 1 ? " army (" : " armies (") + r.ArmyLords + " parties)" : "; no army")
                              + ". Holding your orders: " + r.Ordered + "; the other " + (r.Lords - r.Ordered) + " are led by the game's own AI.");
                sb.AppendLine("- Castles and towns: " + r.Forts + " (garrisons " + Text.N(r.GarrisonMen) + " men, militia " + Text.N(r.Militia)
                              + (r.Besieged > 0 ? "; " + r.Besieged + " under siege" : "") + "). Villages: " + r.Villages
                              + (r.Looted + r.Raided > 0 ? " (" + string.Join(", ", new[] { r.Looted > 0 ? r.Looted + " looted" : null, r.Raided > 0 ? r.Raided + " being raided" : null }.Where(x => x != null)) + ")" : "") + ".");
                sb.AppendLine("- Prisoners of the enemy: " + (r.Captives == 0 ? "none of your lords." : r.Captives + " of your lords (YOUR CAPTURED LORDS)."));
            }
            if (v.Purse.Count > 0)
            {
                sb.AppendLine().AppendLine("YOUR PURSE");
                foreach (string l in v.Purse) sb.AppendLine("- " + l);
            }

            sb.AppendLine().AppendLine("YOUR LORDS AND ARMIES (" + v.Forces.Count + ", " + Text.N(v.Forces.Sum(f => f.ArmyMen > 0 ? f.ArmyMen : f.Men)) + " men)");
            if (v.Forces.Concat(v.Seen).Any(p => p.Arrives != null)) sb.AppendLine(MarchNote);
            foreach (ViewParty f in v.Forces)
            {
                sb.AppendLine(LordLine(f));
                JoinerLines(sb, v, f);
                MemberLines(sb, f.Members);
            }

            sb.AppendLine().AppendLine("YOUR HOLDINGS (" + v.Holdings.Count + ")");
            if (v.Holdings.Any(h => h.WallLevel > 0)) sb.AppendLine(WallsNote);
            if (v.Holdings.Any(h => h.Food >= 0)) sb.AppendLine(FoodNote);
            foreach (ViewHolding h in v.Holdings) sb.AppendLine(HoldingLine(h));

            HumanPlayerSection(sb, v);
            if (v.EnemyRealms.Count > 0)
            {
                sb.AppendLine().AppendLine("ENEMY REALMS, AS THEIR ENCYCLOPEDIA PAGES SHOW ANY RULER");
                foreach (string l in v.EnemyRealms) sb.AppendLine("- " + l);
            }
            if (v.Borders.Count > 0)
            {
                sb.AppendLine().AppendLine("YOUR BORDERS");
                sb.AppendLine(BordersNote);
                foreach (string l in v.Borders) sb.AppendLine("- " + l);
            }

            // 0.13 (docs/PLAN-0.13.md B3): the realm's open votes and the offers made to it, then its diplomacy, as its ruler's screens show them
            if (v.Open.Count > 0)
            {
                sb.AppendLine().AppendLine("OPEN VOTES AND OFFERS (" + v.Open.Count + "; answer them in \"diplomacy\": vote, decide, accept_..., decline_...)");
                foreach (string l in v.Open) sb.AppendLine("- " + l);
            }
            if (v.Diplomacy.Count > 0)
            {
                sb.AppendLine().AppendLine("DIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT");
                foreach (string l in v.Diplomacy) sb.AppendLine("- " + l);
            }
            if (v.Allied.Count > 0)
            {
                sb.AppendLine().AppendLine("ALLIES AND JOINT PLANS (an ally knows only what it is told; write in \"letters\")");
                foreach (string l in v.Allied) sb.AppendLine("- " + l);
            }

            if (v.Sum != null && v.Captives.Count > 0)
            {
                sb.AppendLine().AppendLine("YOUR CAPTURED LORDS (" + v.Captives.Count + ", by where they are held)");
                foreach (string line in CaptiveLines(v.Captives)) sb.AppendLine("- " + line);
            }

            if (v.Clocks.Count > 0)
            {
                sb.AppendLine().AppendLine("SIEGES AND BATTLES (" + v.Clocks.Count + "), run ahead by the game's own clock");
                sb.AppendLine(ClocksNote);
                foreach (string c in v.Clocks) sb.AppendLine("- " + c);
            }

            sb.AppendLine().AppendLine("IN VIEW (" + v.Seen.Count + (v.SeenNotListed > 0 ? ", and " + v.SeenNotListed + " more further off" : "") + ")");
            if (v.Seen.Count == 0) sb.AppendLine("- No lords or enemies in view.");
            foreach (ViewParty p in v.Seen)
            {
                sb.AppendLine(SeenLine(p));
                JoinerLines(sb, v, p);
            }
            if (v.OthersInView > 0) sb.AppendLine("- Also in view: " + v.OthersInView + " caravans, villagers and other small parties.");

            if (v.LastSeen.Count > 0)
            {
                sb.AppendLine().AppendLine("SEEN EARLIER, NOW OUT OF SIGHT (" + v.LastSeen.Count + ")");
                foreach (ViewParty p in v.LastSeen) sb.AppendLine(LastSeenLine(p, v.Hours));
            }

            if (v.Trails.Count > 0)
            {
                sb.AppendLine().AppendLine("ENEMY ARMIES' TRAILS (" + v.Trails.Count + "): where your realm saw each enemy army in the last two days, oldest first; a new point when its course changed, "
                                           + "after every 10 map units, every 6 hours, or when it came back into sight");
                foreach (ViewTrail t in v.Trails)
                    sb.AppendLine("- " + TrailHead(t, v.Hours) + ": " + string.Join(" -> ", TrailSteps(t)) + ".");
            }

            if (v.Frontier.Count > 0)
            {
                sb.AppendLine().AppendLine("ENEMY HOLDINGS NEAR YOUR LANDS (" + v.Frontier.Count + ")");
                foreach (ViewHolding h in v.Frontier) sb.AppendLine(FrontierLine(h));
            }

            if (v.News.Count > 0)
            {
                sb.AppendLine().AppendLine(v.NewsFromChronicle ? "RECENT HISTORY, LAST TWO DAYS (from your chronicle)" : "NEWS, LAST TWO DAYS");
                foreach (string n in v.News) sb.AppendLine("- " + n);
            }
            return sb.ToString();
        }

        // ---------- the report's lines and notes, shared with a news wake's report (0.14, Views.News) ----------

        internal const string MarchNote = "\"There in about N hours\", here and below, is the game's own reckoning of the march: along the way the game planned for that party, at its "
                                          + "own speed (set by its men, horses, wounded, prisoners and loads), slowed by forest, fords and bridges and by night, as it comes. "
                                          + "It holds while the party keeps that course.";

        internal const string WallsNote = "A castle or town holds when its defenders' strength (garrison, militia and the lords inside) times what its walls are worth (2.4 at wall level 1, "
                                          + "2.8 at level 2, 3.2 at level 3) is at least its besiegers' strength: the game's own test for storming, once the besiegers have a ram and "
                                          + "siege towers built. Lords you shelter inside add their strength. Besiegers above that may storm, the sooner the stronger they are; "
                                          + "below it they wait, but after four days of siege the bar falls hour by hour, to the defenders' bare strength by about the sixth day. "
                                          + "SIEGES AND BATTLES runs each siege's clock.";

        internal const string FoodNote = "A castle's or town's food comes from its lands and its unraided villages each day, and none while it is besieged; its garrison and its people eat it, "
                                         + "and once it is out a tenth of the garrison falls to hunger each day. The supply order brings it food. "
                                         + "Lords sheltering inside never eat a holding's food: each lord's men eat what he carries. A town's market sells them food every hour while it has any, "
                                         + "besieged or not; a castle has no market, so lords shut in a castle live on what they brought. "
                                         + "While a castle or town is besieged, once its own store would run out it takes what it lacks each day from the food its lords inside carry "
                                         + "(they go on eating their own meanwhile), so a lord who comes in well fed makes it last longer; when that is gone too, garrison and lords go hungry together. "
                                         + "The clan named with each one owns it: a lord's garrison_floor takes men only from his own clan's (his look-up file names his clan).";

        internal const string ClocksNote = "The game's rules: a siege camp builds its preparations, then one engine at a time; it may storm only once the preparations are done and an "
                                           + "engine is built, and its commander then tries about three times an hour, the likelier the more the besiegers outweigh the defenders "
                                           + "with their walls, and hardly at all while an engine stands half built. A place out of food loses a tenth of its garrison to hunger each "
                                           + "day (wounded, not killed); it never surrenders. A storm is fought one round an hour, any other battle two; in a storm the attackers' "
                                           + "blows grow only with the defenders' numbers, so a few defenders hold a great host for hours. A side that loses a round by much loses "
                                           + "morale and breaks at none, but men behind walls never break. Nobody can leave a battle once in it, and lords who reach it join it. "
                                           + "Each line counts what you can see as it is now.";

        /// <summary>One of the realm's lords or armies, as YOUR LORDS AND ARMIES lists it.</summary>
        internal static string LordLine(ViewParty f)
        {
            string size = f.ArmyMen > 0 ? "army of " + Text.N(f.ArmyMen) + " men in " + f.ArmyParties + " parties" : Text.N(f.Men) + " men";
            string held = OrderName(f);
            return "- " + f.Name + Tag(f) + ": " + size + ", strength " + Text.N((int)f.Strength) + "; " + Where(f) + "; " + f.Doing + JoiningText(f) + (f.Arrives != null ? ", " + f.Arrives : "")
                   + (f.Poor != null ? "; " + f.Poor : "") + (held != null ? " (your order: " + held + ")" : "") + ".";
        }

        /// <summary>One of the realm's holdings, as YOUR HOLDINGS lists it.</summary>
        internal static string HoldingLine(ViewHolding h)
        {
            var line = new StringBuilder("- " + h.Name + " [" + h.Id + "] (" + h.Kind + (string.IsNullOrEmpty(h.Clan) ? "" : " of " + h.Clan) + ")");
            if (h.Kind != "village") line.Append(": garrison " + Text.N(Math.Max(0, h.Garrison)) + (h.GarrisonStrength >= 0 ? " (strength " + Text.N((int)Math.Round(h.GarrisonStrength)) + ")" : "") + ", militia " + Text.N(h.Militia));
            if (h.Food >= 0)
                line.Append("; food " + Text.N((int)Math.Floor(h.Food)) + " of " + Text.N((int)h.FoodCap)
                            + (h.FoodPerDay > 0 ? ", about " + Text.N((int)Math.Floor(h.Food / h.FoodPerDay)) + " days under siege (it eats " + Text.N((int)Math.Ceiling(h.FoodPerDay)) + " a day)" : "")
                            + (h.FoodPerDay > 0 && h.LordsFood > 0 && SiegeFoodRules.Days(h.Food, h.FoodPerDay, h.LordsFood, h.LordsEat) > h.Food / h.FoodPerDay + 0.05
                               ? ", about " + Text.N((int)Math.Floor(SiegeFoodRules.Days(h.Food, h.FoodPerDay, h.LordsFood, h.LordsEat))) + " with the " + Text.N((int)h.LordsFood) + " food your lords inside carry" : "")
                            + (h.FoodComing != null ? "; food coming: " + h.FoodComing : ""));
            if (h.WallLevel > 0)
                line.Append("; walls level " + h.WallLevel + "; defenders' strength " + Text.N((int)Math.Round(h.Defence))
                            + (h.LordsInside > 0 ? " (" + Text.N((int)Math.Round(h.LordsInside)) + " of it your lords inside)" : "")
                            + ", so it holds against besiegers up to strength " + Text.N((int)Math.Round(h.HoldsUpTo)));
            if (!string.IsNullOrEmpty(h.Status)) line.Append("; " + h.Status);
            if (h.Threat != null) line.Append("; nearest enemy in view: " + h.Threat + ", " + Text.N(h.ThreatMen) + " men at " + Roads.Both(h.ThreatDistance, h.ThreatRoad));
            return line.Append('.').ToString();
        }

        /// <summary>A party in view, as IN VIEW lists it.</summary>
        internal static string SeenLine(ViewParty p)
        {
            string size = p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) + " men" : Text.N(p.Men) + " men";
            string where = p.NearestHolding == null ? Where(p)
                         : Roads.Both(p.HoldingDistance, p.HoldingRoad) + " from your " + p.NearestHolding + (p.Near != p.NearestHolding ? "; " + Where(p) : "");
            return "- " + p.Relation.ToUpperInvariant() + " " + p.Name + Tag(p) + " (" + p.FactionName + "): " + size + ", strength " + Text.N((int)p.Strength)
                   + "; " + where + "; " + p.Doing + JoiningText(p) + (p.Arrives != null ? ", " + p.Arrives : "") + "; seen by " + p.SeenBy + ".";
        }

        /// <summary>A party seen earlier and now out of sight, as SEEN EARLIER lists it.</summary>
        internal static string LastSeenLine(ViewParty p, double now)
            => "- " + p.Name + Tag(p) + " (" + p.FactionName + "): " + (p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) : Text.N(p.Men)) + " men, strength "
               + Text.N((int)p.Strength) + ", last seen " + Where(p)
               + ", " + Math.Round(now - p.SeenHours) + " hours ago, " + p.Doing
               + (!double.IsNaN(p.ArrivesAt) && p.ArrivesAt > now ? " (if it kept on, there about " + Text.When(p.ArrivesAt) + ")" : "") + ".";

        /// <summary>An enemy holding near the realm's lands, as ENEMY HOLDINGS NEAR YOUR LANDS lists it.</summary>
        internal static string FrontierLine(ViewHolding h)
            => "- " + h.Name + " [" + h.Id + "] (" + h.Kind + ", " + h.FactionName + "): "
               + (h.Garrison >= 0 ? "garrison " + Text.N(h.Garrison) + (h.Militia >= 0 ? ", militia " + Text.N(h.Militia) : "") : "garrison not in sight")
               + (string.IsNullOrEmpty(h.Status) ? "" : "; " + h.Status) + ".";

        /// <summary>The captured lords, one line per place they are held (a holding, or the party holding them), most first.</summary>
        public static List<string> CaptiveLines(List<ViewCaptive> captives)
        {
            var lines = new List<string>();
            foreach (IGrouping<string, ViewCaptive> g in captives.GroupBy(c => c.Holding ?? "party:" + (c.PartyLord ?? c.PartyName ?? "?"))
                                                                  .OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                ViewCaptive first = g.First();
                string where = first.Holding != null
                    ? "In " + (first.HoldingName ?? first.Holding) + " [" + first.Holding + "] (" + (first.HoldingKind ?? "holding") + " of " + (first.HoldingFaction ?? "?") + ")"
                    : "Held by " + (first.PartyName ?? "a party") + (first.PartyLord != null ? " [" + first.PartyLord + "]" : "") + " (" + (first.PartyFaction ?? "?") + "), on the march";
                string who = string.Join("; ", g.OrderBy(c => double.IsNaN(c.Since) ? double.MaxValue : c.Since)
                                                .Select(c => c.Name + " [" + c.Hero + "]" + (double.IsNaN(c.Since) ? "" : " since " + Text.When(c.Since))));
                lines.Add(where + ": " + g.Count() + (g.Count() == 1 ? " lord" : " lords") + ": " + who + ".");
            }
            return lines;
        }

        /// <summary>The lieutenant's report (0.10): where the player's own party is and what it is doing, as the realm's own.</summary>
        public static string RulerLine(Snapshot s)
        {
            if (s.PlayerParty == null || !s.PartyById.TryGetValue(s.PlayerParty, out PartyRow me)) return "Where your ruler's party is cannot be seen this hour.";
            List<SettlementRow> own = s.Settlements.Where(h => h.Faction == me.Faction).ToList();
            ViewParty p = Party(s, me, null, "own", own);
            string size = p.ArmyMen > 0 ? "an army of " + Text.N(p.ArmyMen) + " men in " + p.ArmyParties + " parties" : Text.N(p.Men) + " men";
            string line = p.Name + " [" + me.Id + "]: " + size + ", strength " + Text.N((int)p.Strength) + "; " + Where(p) + "; " + p.Doing
                          + ". The ruler leads their own party: never give it orders.";
            // the lords in the ruler's own army, by name (0.13.8)
            List<ViewParty> members = p.ArmyMen > 0 ? ArmyMembers(s, me.Id, me.Faction) : null;
            if (members == null || members.Count == 0) return line;
            var sb = new StringBuilder(line);
            MemberLines(sb, members, "In the ruler's army");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The realm's lords marching in this party's army (attached to it), strongest first (0.13.8).</summary>
        public static List<ViewParty> ArmyMembers(Snapshot s, string leader, string fid)
        {
            return s.Parties.Where(r => r.Kind == "lord" && r.Attached && r.ArmyLeader == leader && r.Id != leader && r.Faction == fid)
                    .Select(r => new ViewParty { Id = r.Id, Lord = r.Leader, Name = r.Name ?? r.Id, Faction = r.Faction, Relation = "own", Kind = r.Kind, Men = r.Men, Strength = r.Strength, X = r.X, Y = r.Y })
                    .OrderByDescending(x => x.Strength).ToList();
        }

        /// <summary>Under an army's line: each lord marching in it, with his men and strength; an order to one takes him out of the army first.</summary>
        private static void MemberLines(StringBuilder sb, List<ViewParty> members, string head = "In the army")
        {
            if (members == null || members.Count == 0) return;
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();
            sb.AppendLine("  " + head + " (" + members.Count + (members.Count == 1 ? " lord" : " lords") + " beside its leader, marching with it):");
            foreach (ViewParty x in members)
            {
                string held = OrderName(x);
                sb.AppendLine("  - " + x.Name + Tag(x) + ": " + Text.N(x.Men) + " men, strength " + Text.N((int)x.Strength) + (held != null ? " (your order: " + held + ")" : "") + ".");
            }
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

        /// <summary>
        /// The army a lord is marching to join (0.12.3): the army he belongs to but has not reached yet, or the army or lord of his
        /// own realm he follows (the game sends a lord called to an army that way); null otherwise.
        /// </summary>
        public static PartyRow JoiningHead(Snapshot s, PartyRow r)
        {
            if (r == null || r.Kind != "lord" || r.Attached) return null;
            PartyRow head = null;
            if (r.ArmyLeader != null && r.ArmyLeader != r.Id) s.PartyById.TryGetValue(r.ArmyLeader, out head);
            else if ((r.Doing == "EscortParty" || r.Doing == "JoinParty") && r.TargetParty != null && s.PartyById.TryGetValue(r.TargetParty, out PartyRow led) && led.Faction == r.Faction)
                head = led.Attached && led.ArmyLeader != null && s.PartyById.TryGetValue(led.ArmyLeader, out PartyRow l) ? l : led;
            return head != null && head.Id != r.Id ? head : null;
        }

        /// <summary>On a joiner's own line: ", 3.2 from it (about 2 hours)".</summary>
        private static string JoiningText(ViewParty p) => p.Joining == null ? "" : ", " + Text.D(p.JoiningDistance) + " from it" + (double.IsNaN(p.JoiningHours) ? "" : " (about " + Forecast.HoursText(p.JoiningHours) + ")");

        /// <summary>The lords marching to join an army, under its line: each one's men, strength, distance and hours to it, soonest first.</summary>
        private static void JoinerLines(StringBuilder sb, View v, ViewParty army)
        {
            List<ViewParty> coming = v.Forces.Concat(v.Seen).Where(x => x.Joining == army.Id)
                                      .OrderBy(x => double.IsNaN(x.JoiningHours) ? double.MaxValue : x.JoiningHours).ThenBy(x => x.JoiningDistance).ToList();
            if (coming.Count == 0) return;
            sb.AppendLine("  Marching to join it (" + coming.Count + (coming.Count == 1 ? " lord" : " lords") + ", strength " + Text.N((int)coming.Sum(x => x.Strength)) + " together), soonest first:");
            foreach (ViewParty x in coming)
                sb.AppendLine("  - " + x.Name + Tag(x) + ": " + Text.N(x.Men) + " men, strength " + Text.N((int)x.Strength) + ", " + Text.D(x.JoiningDistance) + " from it"
                              + (double.IsNaN(x.JoiningHours) ? "" : ", about " + Forecast.HoursText(x.JoiningHours) + " at its pace while the army holds where it is") + ".");
        }

        /// <summary>The order of the IN VIEW list: enemy armies, enemy lords, bandits, then allies and neutrals.</summary>
        private static int SeenRank(ViewParty x) => x.Relation != "enemy" ? 3 : x.Kind == "bandit" ? 2 : x.ArmyMen > 0 ? 0 : 1;

        public static ViewParty Party(Snapshot s, PartyRow r, Sighting seen, string relation, List<SettlementRow> own)
        {
            var vp = new ViewParty
            {
                Id = r.Id, Lord = r.Leader, Name = r.Name ?? r.Id, Faction = r.Faction, FactionName = FactionName(s, r.Faction), Relation = relation,
                Kind = r.Kind, Men = r.Men, ArmyMen = r.ArmyMen, ArmyParties = r.ArmyParties, Strength = r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength,
                X = r.X, Y = r.Y, Doing = Doing(s, r), SeenBy = seen?.By, SeenHours = s.Hours
            };
            vp.Toward = Toward(r);
            if (relation == "own") vp.Poor = PurseView.RecruitNote(r);
            vp.InPerson = s.PlayerParty != null && (r.Id == s.PlayerParty || r.ArmyLeader == s.PlayerParty);
            vp.Entrance = r.InSettlement ?? r.Entrance;
            if (r.Kind == "lord")
            {
                Common.Pace pace = Forecast.PaceOf(s, r);
                if (pace != null) { pace.PerkValues = null; vp.March = pace; }
                // arrival: a march to a holding (not a patrol round it), or one of the realm's own lords walking to a point of its order
                bool toHolding = MarchesTo(r) && r.TargetSettlement != null;
                if (toHolding || (relation == "own" && r.Doing == "GoToPoint"))
                {
                    // to a holding: along its way only when that way ends there, else by road at its pace (0.7.4: Latif walking to a
                    // point 7 ahead was "going to Starkmoore, there in about 3 hours", 364 away)
                    Forecast.Arrival a = toHolding && s.SettlementById.TryGetValue(r.TargetSettlement, out SettlementRow th) ? Forecast.ToHolding(s, r, th) : Forecast.ArrivalOf(s, r);
                    if (a != null && (toHolding || a.Length > 3))
                    {
                        vp.Arrives = Forecast.ArrivalText(a);
                        vp.ArrivesAt = a.Reached ? a.At : double.NaN;
                    }
                }
            }
            SettlementRow near = NearestSettlement(s, r.X, r.Y, out float nd);
            if (near != null) { vp.Near = near.Name; vp.NearDistance = Text.R1(nd); vp.NearRoad = R1(Roads.To(s, r, near)); }
            if (own != null && own.Count > 0 && relation != "own")
            {
                // the realm's holding it can reach soonest: nearest by road where the game's table knows the way, else as the crow flies
                SettlementRow byRoad = null;
                float best = float.MaxValue;
                foreach (SettlementRow o in own)
                {
                    float rd = Roads.To(s, r, o);
                    if (!float.IsNaN(rd) && rd < best) { best = rd; byRoad = o; }
                }
                SettlementRow h = byRoad ?? own.OrderBy(o => Dist(o.X, o.Y, r.X, r.Y)).First();
                vp.NearestHolding = h.Name;
                vp.HoldingDistance = Text.R1(Dist(h.X, h.Y, r.X, r.Y));
                vp.HoldingRoad = byRoad != null ? Text.R1(best) : float.NaN;
            }
            return vp;
        }

        /// <summary>
        /// The food on its way to an own castle or town (0.13.21, Nov 1: Dol Guldur's agent left Gwígar as "no food" while its own
        /// Dûrnia was seven hours from handing over 219, since the report said only "bringing food to Gwígar"): each of the realm's supply
        /// orders to it, with what the lord can spare beyond his keep_days (when he rides there now), or what it needs (when he buys on the
        /// way first), and when he gets there. Null when none.
        /// </summary>
        private static string FoodComing(Snapshot s, string fid, SettlementRow h)
        {
            var parts = new List<string>();
            foreach (Tools.ToolInstance t in Tools.Runner.All())
            {
                if (t.Finished || t.Order?.Realm != fid || t.Def?.Name != "supply" || t.Order.At != h.Id) continue;
                if (!s.PartyById.TryGetValue(t.Order.Party ?? "", out PartyRow r)) continue;
                double days = t.Settings.TryGetValue("days", out double dd) ? dd : 10, keep = t.Settings.TryGetValue("keep_days", out double kd) ? kd : 3;
                int need = Math.Max(0, (int)Math.Ceiling(Math.Min(h.FoodCap, days * h.FoodPerDay) - h.Food));
                int spare = r.FoodItems >= 0 ? Math.Max(0, r.FoodItems - (int)Math.Ceiling(keep * r.FoodEats)) : -1;
                string who = r.Name ?? t.Order.LordName ?? r.Id;
                if (r.TargetSettlement == h.Id || r.InSettlement == h.Id)
                {
                    int brings = spare >= 0 ? Math.Min(spare, need) : need;
                    Forecast.Arrival a = r.InSettlement == h.Id ? null : Forecast.ToHolding(s, r, h);
                    parts.Add(who + " with about " + Text.N(brings) + (h.FoodPerDay > 0 ? " (about " + Text.N((int)Math.Floor(brings / h.FoodPerDay)) + " days more)" : "")
                              + (a != null && a.Reached ? ", there about " + Text.When(a.At) : ""));
                }
                else
                    parts.Add(who + ", buying food on the way first, with up to " + Text.N(need) + (spare > 0 ? " (he carries " + Text.N(spare) + " to spare now)" : ""));
            }
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }

        public static ViewHolding Holding(Snapshot s, SettlementRow h, bool counted)
        {
            var vh = new ViewHolding
            {
                Id = h.Id, Name = h.Name, Kind = h.Kind, Faction = h.Faction, FactionName = FactionName(s, h.Faction),
                // garrison and militia alike only where the realm's eyes reach (an enemy's out of sight: -1, not known)
                Garrison = counted ? h.Garrison : -1, Militia = counted ? h.Militia : -1, X = h.X, Y = h.Y, Status = Status(s, h)
            };
            if (counted && Tools.World.IsFort(h))
            {
                // the would-it-hold test of the tools (World.Holds), for the lords inside now: garrison, militia and the
                // realm's and its allies' lords sheltering there, times what the walls are worth
                HashSet<string> allies = s.Factions.TryGetValue(h.Faction ?? "", out FactionRow fr) ? new HashSet<string>(fr.Allies) : new HashSet<string>();
                float inside = 0;
                foreach (PartyRow r in s.Parties)
                    if (r.Kind == "lord" && !r.Attached && r.InSettlement == h.Id && (r.Faction == h.Faction || allies.Contains(r.Faction ?? "")))
                        inside += r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength;
                vh.WallLevel = Math.Max(1, h.WallLevel);
                vh.LordsInside = inside;
                vh.Defence = h.GarrisonStrength + h.MilitiaStrength + inside;
                vh.HoldsUpTo = (float)(vh.Defence * Tools.World.WallFactor(h));
            }
            return vh;
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

        /// <summary>The holding Doing names, if any (the same order of checks).</summary>
        public static string Toward(PartyRow r)
        {
            if (r.Raiding != null) return r.Raiding;
            if (r.InBattle) return null;
            if (r.Besieging != null) return r.Besieging;
            if (r.InSettlement != null) return null;
            switch (r.Doing)
            {
                case "GoToSettlement": case "BesiegeSettlement": case "AssaultSettlement": case "RaidSettlement": case "DefendSettlement": case "PatrolAroundPoint":
                    return r.TargetSettlement;
                default: return null;
            }
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

        private static string Tag(ViewParty p) => " [" + (p.Lord ?? p.Id) + "]" + (p.InPerson ? " (the human player, in person)" : "");

        /// <summary>The report's section on the human player (0.17.3): who he is, how his battles are fought, his record in a line.</summary>
        internal static void HumanPlayerSection(StringBuilder sb, View v)
        {
            if (v.HumanPlayer == null || v.HumanPlayer.Count == 0) return;
            sb.AppendLine().AppendLine(v.HumanPlayer[0].StartsWith("You, ", StringComparison.Ordinal) ? "YOUR OWN BATTLES, THE HUMAN PLAYER'S" : "YOUR ENEMY, THE HUMAN PLAYER");
            foreach (string l in v.HumanPlayer) sb.AppendLine("- " + l);
        }

        /// <summary>An enemy army's trail, its first line: who, how strong, and whether it is in sight now.</summary>
        public static string TrailHead(ViewTrail t, double now)
            => t.Name + " [" + (t.Lord ?? t.Id) + "] (" + t.FactionName + "), army of " + Text.N(t.ArmyMen) + " men, strength " + Text.N((int)Math.Round(t.Strength))
               + (t.InView ? ", in view now" : ", last seen " + Math.Round(now - t.LastSeenHours) + " hours ago");

        /// <summary>The points of an enemy army's trail, oldest first: when, where (and how far from the realm's nearest holding), and what it was doing.</summary>
        public static IEnumerable<string> TrailSteps(ViewTrail t)
        {
            foreach (TrailPoint p in t.Points)
            {
                string where = p.Near == null ? "in the wilds" : p.NearDistance < 1f ? "at " + p.Near : Roads.Both(p.NearDistance, p.NearRoad) + " from " + p.Near;
                string fromOurs = p.NearestHolding != null && p.NearestHolding != p.Near ? ", " + Roads.Both(p.HoldingDistance, p.HoldingRoad) + " from your " + p.NearestHolding : "";
                yield return (p.AfterGap >= 1.5 ? "(out of sight " + Math.Round(p.AfterGap) + " h) " : "") + Calendar.WhenShort(p.Hours) + ", " + where + fromOurs + ", " + p.Doing;
            }
        }

        private static string Where(ViewParty p) => p.Near != null ? (p.NearDistance < 1f ? "at " + p.Near : Roads.Both(p.NearDistance, p.NearRoad) + " from " + p.Near) : "in the wilds";

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
        private static float R1(float v) => float.IsNaN(v) ? float.NaN : Text.R1(v);

        /// <summary>How far a party is from the realm's reach: by road where known, else as the crow flies (the poke rule's distance).</summary>
        public static float Reach(ViewParty p) => float.IsNaN(p.HoldingRoad) ? p.HoldingDistance : p.HoldingRoad;
    }
}
