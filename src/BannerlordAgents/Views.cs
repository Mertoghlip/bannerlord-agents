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
        /// <summary>The game's clocks the realm can see (0.7.3, Forecast): each siege and each battle, run ahead by the game's own rules.</summary>
        public List<string> Clocks = new List<string>();
        /// <summary>The realms at war with it, as the encyclopedia shows any player (0.7.4): living lords, and their parties' strength in all, beside what of it is in sight.</summary>
        public List<string> EnemyRealms = new List<string>();
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
    }

    /// <summary>
    /// Where the realm saw an enemy army over the last two days, oldest first (Brain.Trails): so the agent can tell an
    /// army's road from its errands (Mert, Oct 16: an army turning aside to recruit, forage or strike has not changed
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
    }

    /// <summary>The sections a poke adds before the report: what changed since the agent's last answer, its orders in force, ready-made facts.</summary>
    internal sealed class ReportExtras
    {
        public string Since, InForce, Facts;
        /// <summary>The lieutenant's report only (0.10): the player's own party, the ruler it serves.</summary>
        public string Ruler;
        public int LastAnswered = -1;
        public bool Lookups;
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
                // the player's own party is never one of an agent's lords to order (Oct 22: a realm of the player's with an agent)
                if (r.Faction != fid || r.Kind != "lord" || r.Attached || r.Id == s.PlayerParty) continue;
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
            // what the cap of 40 cuts first: bandits and others before any enemy lord, and an enemy army never (Oct 16: 15 to 24
            // bands near the villages pushed Aragorn's army of 5,532 out of the list for days, though the realm saw it)
            v.Seen = v.Seen.OrderBy(SeenRank).ThenBy(Reach).ToList();
            if (v.Seen.Count > 40)
            {
                int keep = Math.Max(40, v.Seen.Count(x => SeenRank(x) == 0));
                v.SeenNotListed = v.Seen.Count - keep;
                v.Seen = v.Seen.Take(keep).ToList();
            }

            foreach (SettlementRow h in own)
            {
                ViewHolding vh = Holding(s, h, true);
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
                    v.News = Chronicle.For(fid).Recent(s.Hours, 48, 25);
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
            }
            v.Clocks = Clocks(s, fid, sees, allies);
            v.EnemyRealms = EnemyRealms(s, fid, sees);
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
        public static List<string> Clocks(Snapshot s, string fid, Dictionary<string, Sighting> sees, HashSet<string> allies)
        {
            var lines = new List<string>();
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
                string stormNow = g.Storming ? null : Forecast.StormNow(s, g, theirs, joiners);
                lines.Add(h.Name + " [" + h.Id + "] (" + h.Kind + ", " + FactionName(s, h.Faction) + "), besieged by " + PartyName(g.Besieger) + LordTag(g.Besieger)
                          + " (" + FactionName(s, g.BesiegerFaction) + ") since " + Text.When(g.StartHours) + " (" + Math.Round(s.Hours - g.StartHours) + " hours)"
                          + (g.Storming ? "; a storm is being fought now (below)" : "") + ": " + Forecast.SiegeText(s, g, c, ours, stormNow));
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
                      .Append(c.How == "rout" ? " when the other side's morale breaks" : c.How == "retreat" ? " when the other side retreats (nothing is captured then)" : " when the other side is all down")
                      .Append(", in ").Append(hours).Append(" (").Append(Text.When(c.EndsAt)).Append(c.Measured ? "), " : " at middling morale), ")
                      .Append(Text.N(c.Winner == "attackers" ? c.AttackersLeft : c.DefendersLeft)).Append(" of them still standing.");
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
            if (x?.Facts != null) sb.AppendLine().AppendLine("READY-MADE FACTS").AppendLine(x.Facts);

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
            if (v.Forces.Concat(v.Seen).Any(p => p.Arrives != null))
                sb.AppendLine("\"There in about N hours\", here and below, is the game's own reckoning of the march: along the way the game planned for that party, at its "
                              + "own speed (set by its men, horses, wounded, prisoners and loads), slowed by forest, fords and bridges and by night, as it comes. "
                              + "It holds while the party keeps that course.");
            foreach (ViewParty f in v.Forces)
            {
                string size = f.ArmyMen > 0 ? "army of " + Text.N(f.ArmyMen) + " men in " + f.ArmyParties + " parties" : Text.N(f.Men) + " men";
                Tools.ToolInstance held = Tools.Runner.ForParty(f.Id);
                sb.AppendLine("- " + f.Name + Tag(f) + ": " + size + ", strength " + Text.N((int)f.Strength) + "; " + Where(f) + "; " + f.Doing + (f.Arrives != null ? ", " + f.Arrives : "")
                              + (held != null ? " (your order: " + held.Def.Name + ")" : "") + ".");
            }

            sb.AppendLine().AppendLine("YOUR HOLDINGS (" + v.Holdings.Count + ")");
            if (v.Holdings.Any(h => h.WallLevel > 0))
                sb.AppendLine("A castle or town holds when its defenders' strength (garrison, militia and the lords inside) times what its walls are worth (2.4 at wall level 1, "
                              + "2.8 at level 2, 3.2 at level 3) is at least its besiegers' strength: the game's own test for storming, once the besiegers have a ram and "
                              + "siege towers built. Lords you shelter inside add their strength. Besiegers above that may storm, the sooner the stronger they are; "
                              + "below it they wait, but after four days of siege the bar falls hour by hour, to the defenders' bare strength by about the sixth day. "
                              + "SIEGES AND BATTLES runs each siege's clock.");
            foreach (ViewHolding h in v.Holdings)
            {
                var line = new StringBuilder("- " + h.Name + " [" + h.Id + "] (" + h.Kind + ")");
                if (h.Kind != "village") line.Append(": garrison " + Text.N(Math.Max(0, h.Garrison)) + ", militia " + Text.N(h.Militia));
                if (h.WallLevel > 0)
                    line.Append("; walls level " + h.WallLevel + "; defenders' strength " + Text.N((int)Math.Round(h.Defence))
                                + (h.LordsInside > 0 ? " (" + Text.N((int)Math.Round(h.LordsInside)) + " of it your lords inside)" : "")
                                + ", so it holds against besiegers up to strength " + Text.N((int)Math.Round(h.HoldsUpTo)));
                if (!string.IsNullOrEmpty(h.Status)) line.Append("; " + h.Status);
                if (h.Threat != null) line.Append("; nearest enemy in view: " + h.Threat + ", " + Text.N(h.ThreatMen) + " men at " + Roads.Both(h.ThreatDistance, h.ThreatRoad));
                sb.AppendLine(line.Append('.').ToString());
            }

            if (v.EnemyRealms.Count > 0)
            {
                sb.AppendLine().AppendLine("ENEMY REALMS, AS THEIR ENCYCLOPEDIA PAGES SHOW ANY RULER");
                foreach (string l in v.EnemyRealms) sb.AppendLine("- " + l);
            }

            if (v.Sum != null && v.Captives.Count > 0)
            {
                sb.AppendLine().AppendLine("YOUR CAPTURED LORDS (" + v.Captives.Count + ", by where they are held)");
                foreach (string line in CaptiveLines(v.Captives)) sb.AppendLine("- " + line);
            }

            if (v.Clocks.Count > 0)
            {
                sb.AppendLine().AppendLine("SIEGES AND BATTLES (" + v.Clocks.Count + "), run ahead by the game's own clock");
                sb.AppendLine("The game's rules: a siege camp builds its preparations, then one engine at a time; it may storm only once the preparations are done and an "
                              + "engine is built, and its commander then tries about three times an hour, the likelier the more the besiegers outweigh the defenders "
                              + "with their walls, and hardly at all while an engine stands half built. A place out of food loses a tenth of its garrison to hunger each "
                              + "day (wounded, not killed); it never surrenders. A storm is fought one round an hour, any other battle two; in a storm the attackers' "
                              + "blows grow only with the defenders' numbers, so a few defenders hold a great host for hours. A side that loses a round by much loses "
                              + "morale and breaks at none, but men behind walls never break. Nobody can leave a battle once in it, and lords who reach it join it. "
                              + "Each line counts what you can see as it is now.");
                foreach (string c in v.Clocks) sb.AppendLine("- " + c);
            }

            sb.AppendLine().AppendLine("IN VIEW (" + v.Seen.Count + (v.SeenNotListed > 0 ? ", and " + v.SeenNotListed + " more further off" : "") + ")");
            if (v.Seen.Count == 0) sb.AppendLine("- No lords or enemies in view.");
            foreach (ViewParty p in v.Seen)
            {
                string size = p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) + " men" : Text.N(p.Men) + " men";
                string where = p.NearestHolding == null ? Where(p)
                             : Roads.Both(p.HoldingDistance, p.HoldingRoad) + " from your " + p.NearestHolding + (p.Near != p.NearestHolding ? "; " + Where(p) : "");
                sb.AppendLine("- " + p.Relation.ToUpperInvariant() + " " + p.Name + Tag(p) + " (" + p.FactionName + "): " + size + ", strength " + Text.N((int)p.Strength)
                    + "; " + where + "; " + p.Doing + (p.Arrives != null ? ", " + p.Arrives : "") + "; seen by " + p.SeenBy + ".");
            }
            if (v.OthersInView > 0) sb.AppendLine("- Also in view: " + v.OthersInView + " caravans, villagers and other small parties.");

            if (v.LastSeen.Count > 0)
            {
                sb.AppendLine().AppendLine("SEEN EARLIER, NOW OUT OF SIGHT (" + v.LastSeen.Count + ")");
                foreach (ViewParty p in v.LastSeen)
                    sb.AppendLine("- " + p.Name + Tag(p) + " (" + p.FactionName + "): " + (p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) : Text.N(p.Men)) + " men, strength "
                        + Text.N((int)p.Strength) + ", last seen " + Where(p)
                        + ", " + Math.Round(v.Hours - p.SeenHours) + " hours ago, " + p.Doing
                        + (!double.IsNaN(p.ArrivesAt) && p.ArrivesAt > v.Hours ? " (if it kept on, there about " + Text.When(p.ArrivesAt) + ")" : "") + ".");
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
                foreach (ViewHolding h in v.Frontier)
                    sb.AppendLine("- " + h.Name + " [" + h.Id + "] (" + h.Kind + ", " + h.FactionName + "): "
                        + (h.Garrison >= 0 ? "garrison " + Text.N(h.Garrison) + (h.Militia >= 0 ? ", militia " + Text.N(h.Militia) : "") : "garrison not in sight")
                        + (string.IsNullOrEmpty(h.Status) ? "" : "; " + h.Status) + ".");
            }

            if (v.News.Count > 0)
            {
                sb.AppendLine().AppendLine(v.NewsFromChronicle ? "RECENT HISTORY, LAST TWO DAYS (from your chronicle)" : "NEWS, LAST TWO DAYS");
                foreach (string n in v.News) sb.AppendLine("- " + n);
            }
            return sb.ToString();
        }

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
            return p.Name + " [" + me.Id + "]: " + size + ", strength " + Text.N((int)p.Strength) + "; " + Where(p) + "; " + p.Doing
                   + ". The ruler leads their own party: never give it orders.";
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

        private static string Tag(ViewParty p) => " [" + (p.Lord ?? p.Id) + "]";

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
