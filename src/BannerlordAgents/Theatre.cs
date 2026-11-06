using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BannerlordAgents
{
    /// <summary>One thing a news wake is about (0.14, Theatre): a place or a party, and when the game's own clock says it is decided.</summary>
    internal sealed class Focus
    {
        public string Id, Name, Trigger;
        public bool IsParty, OurHolding;
        public float X, Y;
        /// <summary>The in-game hour it is decided by the game's own clock (NaN: no clock), and how, in a few words.</summary>
        public double DecidedAt = double.NaN;
        public string How;
        /// <summary>How far the theatre reaches around it, in hours of marching: the clock, kept between Floor (or MinHorizon) and Ceiling, or NoClock.</summary>
        public double Horizon;
        /// <summary>The least horizon for this focus: Floor, or a day for an enemy only passing near (the coverage check on run A: its clock, the hours
        /// it needs to reach the nearest holding, gave the orcs a median of one lord in its theatre of 52, and its answers named lords far beyond).</summary>
        public double MinHorizon = Theatre.Floor;
    }

    /// <summary>How soon a lord or an enemy could be at one of the theatre's focuses.</summary>
    internal sealed class Reach
    {
        public ViewParty Party;
        public Focus Focus;
        public double Hours;
    }

    /// <summary>
    /// The theatre of a news wake (0.14, Nov 2-3: "here are the lords 1 day away from this engagement, here are the lords in
    /// the engagement", the agent able to query for all lords if it needs them, and the horizon set by the "clock" for
    /// the specific new development). A poke's news names its focus (where it happens, who is in it); the game's own clock for it
    /// (an enemy's march, a siege's storm or hunger, a battle's end, a raid's looting, a holding's food) says how long there is to act,
    /// kept between 6 hours and 3 days, one day where there is no clock; and the theatre is everyone who can be there in that time:
    /// the realm's lords, by the hours their march takes, marked against the clock with the agent's own answer time counted, and
    /// the enemies and allies who could join before it is decided, wherever they are now, and any enemy army heading there. A first
    /// look and a quiet day keep the whole report (decision 2); votes, offers, wars and peace get the realm's diplomacy, purse
    /// and sum ("realm"); a letter's theatre is the places it names (answer 4). Built from the realm's view alone, so the same fog
    /// holds, and so a recorded poke can be built again (the coverage check, replays). Worker thread; game-free.
    /// </summary>
    internal sealed class Theatre
    {
        public const double Floor = 6, Ceiling = 72, NoClock = 24;
        /// <summary>The most focuses one poke's news gets (the rest of its items are told in WHY YOU ARE ASKED NOW only).</summary>
        public const int MostFocuses = 4;
        /// <summary>A road's length over the straight line, for a march whose road the game's table does not know (the coverage check's guess).</summary>
        public const double RoadFactor = 1.3;
        /// <summary>The holdings shown around a focus: those within this many hours of it at the realm's usual pace, at most.</summary>
        public const double HoldingHours = 24;

        /// <summary>theatre, whole (the full report: a first look, a quiet day, or news with no place in it) or realm (votes, offers, wars and peace only).</summary>
        public string Recipe = "theatre";
        public string WhyRecipe;
        public double Now;
        public List<Focus> Focuses = new List<Focus>();
        /// <summary>The poke's items the theatre was built from, by trigger kind; the diplomacy among them puts the realm's diplomacy in.</summary>
        public List<string> Kinds = new List<string>();
        public bool Diplomacy;
        /// <summary>The realm's lords who can be at a focus within its horizon, soonest first; then the next few beyond it, and how many more.</summary>
        public List<Reach> Lords = new List<Reach>();
        public List<Reach> Beyond = new List<Reach>();
        public int LordsFurther;
        /// <summary>Lords and armies in view or seen earlier that could join before it is decided, or enemy armies heading into the theatre.</summary>
        public List<Reach> Others = new List<Reach>();
        /// <summary>The others within the clock not listed (beyond MostOthers, soonest first), and their strength together.</summary>
        public int OthersLeft;
        public double OthersLeftStrength;
        public const int MostOthers = 15;
        public HashSet<string> HeadingIn = new HashSet<string>();
        /// <summary>The holdings in the theatre: the focuses, and the realm's and its enemies' holdings near them.</summary>
        public HashSet<string> Places = new HashSet<string>();
        /// <summary>The agent's answer time, in in-game hours from the report to its orders taking effect: as a rule, and one answer in ten.</summary>
        public double UsualLag = DefaultUsualLag, LateLag = DefaultLateLag;
        public const double DefaultUsualLag = 7, DefaultLateLag = 20;

        private static readonly HashSet<string> WholeKinds = new HashSet<string> { "first_look", "quiet_day", "letter" };
        private static readonly HashSet<string> RealmKinds = new HashSet<string>
        {
            "war", "peace", "alliance", "alliance_ended", "call_to_war", "call_to_war_ended", "peace_offer", "proposal", "decided",
            "vote", "offer", "offer_settled", "vote_settled", "vote_closing"
        };
        private static readonly Regex Ids = new Regex(@"\[([^\]\s]+)\]", RegexOptions.Compiled);

        public bool Whole => Recipe == "whole";
        public bool IsTheatre => Recipe == "theatre";

        /// <summary>Every party id the theatre shows (own lords and their army's lords, the others).</summary>
        public HashSet<string> PartyIds()
        {
            var ids = new HashSet<string>(Lords.Select(r => r.Party.Id));
            foreach (Reach r in Lords) if (r.Party.Members != null) foreach (ViewParty m in r.Party.Members) ids.Add(m.Id);
            foreach (Reach r in Others) ids.Add(r.Party.Id);
            foreach (Focus f in Focuses) if (f.IsParty) ids.Add(f.Id);
            return ids;
        }

        /// <summary>
        /// The names and ids of everything in the theatre, and the realms at war with it and its allies, for the war room's own filters: the
        /// notes on enemies (a note on a whole realm is about every front) and the chronicle lines. local: without the realms (the report's own
        /// filters, where a realm's name is in nearly every line).
        /// </summary>
        public List<string> Names(View v, bool local = false)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string s) { if (!string.IsNullOrWhiteSpace(s)) names.Add(s.Trim()); }
            foreach (Focus f in Focuses) { Add(f.Id); Add(f.Name); }
            foreach (Reach r in Lords.Concat(Others)) { Add(r.Party.Id); Add(r.Party.Lord); Add(r.Party.Name); }
            foreach (ViewHolding h in v.Holdings.Concat(v.Frontier)) if (Places.Contains(h.Id)) { Add(h.Id); Add(h.Name); }
            foreach (string p in Places) Add(p);
            if (!local) foreach (string r in v.AtWar.Concat(v.Allies)) Add(r);
            return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A lord's march against the focus's clock, with the agent's answer time counted.</summary>
        public string Mark(Reach r)
        {
            if (r?.Focus == null || double.IsNaN(r.Focus.DecidedAt)) return null;
            double left = r.Focus.DecidedAt - Now;
            if (r.Hours + LateLag <= left) return "in time even if your orders land late";
            if (r.Hours + UsualLag <= left) return "in time if your orders land as usual";
            return "likely too late";
        }

        // ---------- building it ----------

        /// <summary>
        /// The theatre of one poke. ownHours: hours for one of the realm's lords to reach a focus; otherHours: the same for a party in view or
        /// seen earlier, from where it was seen; place: where a holding the view does not list is, and its name (the map is public), or null.
        /// </summary>
        public static Theatre Build(View v, List<Trigger> triggers, Func<ViewParty, Focus, double> ownHours, Func<ViewParty, Focus, double> otherHours,
                                    Func<string, Tuple<float, float, string>> place = null, double usualLag = DefaultUsualLag, double lateLag = DefaultLateLag)
        {
            var t = new Theatre { Now = v.Hours, UsualLag = usualLag, LateLag = lateLag };
            triggers = triggers ?? new List<Trigger>();
            t.Kinds = triggers.Select(x => x.Kind ?? "").ToList();
            if (triggers.Any(x => WholeKinds.Contains(x.Kind ?? "")))
            {
                t.Recipe = "whole";
                t.WhyRecipe = triggers.Any(x => x.Kind == "first_look") ? "a first look reads the whole realm"
                            : triggers.Any(x => x.Kind == "letter") ? "a letter is about the whole war: the study's replays saved 13% on a small report and took twice as long"
                            : "a quiet day: the whole realm, to review";
                return t;
            }
            t.Diplomacy = triggers.Any(x => RealmKinds.Contains(x.Kind ?? ""));
            var index = new Index(v);
            foreach (Trigger tr in triggers)
            {
                if (t.Focuses.Count >= MostFocuses) break;
                if (RealmKinds.Contains(tr.Kind ?? "")) continue;
                foreach (Focus f in FocusesOf(v, tr, index, place))
                {
                    if (t.Focuses.Count >= MostFocuses) break;
                    // one focus per place or party: a second item about it takes the sooner clock
                    Focus same = t.Focuses.FirstOrDefault(x => x.Id == f.Id);
                    if (same != null)
                    {
                        if (!double.IsNaN(f.DecidedAt) && (double.IsNaN(same.DecidedAt) || f.DecidedAt < same.DecidedAt)) { same.DecidedAt = f.DecidedAt; same.How = f.How; }
                        continue;
                    }
                    t.Focuses.Add(f);
                }
            }
            if (t.Focuses.Count == 0)
            {
                // news with no place in it: votes and the like get the realm's diplomacy; anything else the whole report, to be safe
                t.Recipe = t.Diplomacy ? "realm" : "whole";
                t.WhyRecipe = t.Diplomacy ? "news of diplomacy: the realm's diplomacy, purse and sum" : "the news names no place or party the realm can find";
                return t;
            }
            foreach (Focus f in t.Focuses)
                f.Horizon = double.IsNaN(f.DecidedAt) ? NoClock : Math.Max(f.MinHorizon, Math.Min(Ceiling, f.DecidedAt - v.Hours));

            // the realm's lords: each one's nearest focus by hours over that focus's horizon
            var reaches = new List<Reach>();
            foreach (ViewParty p in v.Forces)
            {
                Reach best = null;
                foreach (Focus f in t.Focuses)
                {
                    double h = f.IsParty && f.Id == p.Id ? 0 : ownHours(p, f);
                    if (double.IsNaN(h)) continue;
                    if (best == null || h / f.Horizon < best.Hours / best.Focus.Horizon) best = new Reach { Party = p, Focus = f, Hours = h };
                }
                if (best != null) reaches.Add(best);
            }
            reaches = reaches.OrderBy(r => r.Hours / r.Focus.Horizon).ThenBy(r => r.Hours).ToList();
            t.Lords = reaches.Where(r => r.Hours <= r.Focus.Horizon + 1e-6).ToList();
            List<Reach> past = reaches.Where(r => r.Hours > r.Focus.Horizon + 1e-6).ToList();
            t.Beyond = past.Take(3).ToList();
            t.LordsFurther = past.Count - t.Beyond.Count;

            // the holdings near each focus, at the realm's usual pace
            double pace = UsualPace(v);
            foreach (Focus f in t.Focuses)
            {
                if (!f.IsParty && f.Id != null) t.Places.Add(f.Id);
                double radius = Math.Min(f.Horizon, HoldingHours) * pace;
                foreach (ViewHolding h in v.Holdings.Concat(v.Frontier))
                {
                    if (h.Kind == "village" && !(h.Status != null || h.Id == f.Id)) continue;   // villages only when raided, looted or the news
                    if (Views.Dist(h.X, h.Y, f.X, f.Y) <= radius) t.Places.Add(h.Id);
                }
            }
            // hunger (the plan's recipe): the realm's towns within three days of the hungry holding, whose markets sell food
            foreach (Focus f in t.Focuses.Where(f => f.Trigger == "low_food" && !f.IsParty))
                foreach (ViewHolding h in v.Holdings.Where(h => h.Kind == "town" && Views.Dist(h.X, h.Y, f.X, f.Y) <= 72 * pace)) t.Places.Add(h.Id);
            // the holdings the theatre's lords are in or marching to, and each one's two nearest castles or towns of the realm: where he would
            // fall back to or refill (the coverage check on run A: "regroup at Westoft", 40 hours off, was outside the theatre)
            List<ViewHolding> forts = v.Holdings.Where(h => h.Kind == "town" || h.Kind == "castle").ToList();
            foreach (Reach r in t.Lords)
            {
                if (r.Party.Toward != null) t.Places.Add(r.Party.Toward);
                foreach (ViewHolding h in forts.OrderBy(h => Views.Dist(h.X, h.Y, r.Party.X, r.Party.Y)).Take(2)) t.Places.Add(h.Id);
            }

            // the others: lords and armies in view or seen earlier that could be at a focus before it is decided (bandits only close by),
            // the parties in the clocks of the theatre's places, and enemy armies heading into the theatre from further off
            var inClocks = new HashSet<string>();
            for (int i = 0; i < v.ClockItems.Count; i++)
            {
                ClockItem c = v.ClockItems[i];
                if (c.Place != null && t.Places.Contains(c.Place)) foreach (string p in c.Parties) inClocks.Add(p);
            }
            var seen = new HashSet<string>();
            foreach (ViewParty p in v.Seen.Concat(v.LastSeen))
            {
                if (!seen.Add(p.Id) || p.Relation == "own") continue;
                Reach best = null;
                foreach (Focus f in t.Focuses)
                {
                    double h = f.IsParty && f.Id == p.Id ? 0 : otherHours(p, f);
                    if (double.IsNaN(h)) continue;
                    if (best == null || h / f.Horizon < best.Hours / best.Focus.Horizon) best = new Reach { Party = p, Focus = f, Hours = h };
                }
                bool focus = t.Focuses.Any(f => f.IsParty && f.Id == p.Id);
                bool bandit = p.Kind == "bandit";
                double limit(Reach r) => bandit ? Math.Min(r.Focus.Horizon, Floor) : r.Focus.Horizon;
                bool within = best != null && best.Hours <= limit(best) + 1e-6;
                bool heading = !bandit && p.Relation == "enemy" && p.ArmyMen > 0 && p.Toward != null && t.Places.Contains(p.Toward);
                if (p.Relation == "neutral" && !focus) continue;
                if (!(focus || within || heading || inClocks.Contains(p.Id))) continue;
                if (best == null) best = new Reach { Party = p, Focus = t.Focuses[0], Hours = double.NaN };
                t.Others.Add(best);
                if (heading && !within) t.HeadingIn.Add(p.Id);
            }
            t.Others = t.Others.OrderBy(r => r.Party.Relation == "enemy" ? 0 : 1).ThenBy(r => r.Party.Kind == "bandit" ? 1 : 0)
                               .ThenBy(r => double.IsNaN(r.Hours) ? double.MaxValue : r.Hours).ToList();
            // at most MostOthers, soonest first; never an enemy army, nor the news's own (the orcs saw a median 26 lords within a day of a
            // Rohan crowd in run A, half their news report): the rest are counted, and a question away
            var keep = new List<Reach>();
            foreach (Reach r in t.Others)
                if (keep.Count < MostOthers || r.Party.ArmyMen > 0 && r.Party.Relation == "enemy" || t.Focuses.Any(f => f.IsParty && f.Id == r.Party.Id)) keep.Add(r);
            t.OthersLeft = t.Others.Count - keep.Count;
            t.OthersLeftStrength = t.Others.Except(keep).Sum(r => r.Party.Strength);
            t.Others = keep;
            // the holdings the theatre's enemies are marching on
            foreach (Reach r in t.Others) if (r.Party.Relation == "enemy" && r.Party.Toward != null && index.HoldingIds.Contains(r.Party.Toward)) t.Places.Add(r.Party.Toward);
            return t;
        }

        /// <summary>The realm's usual pace: its lords' median map units an hour (3.4 speed when not known).</summary>
        public static double UsualPace(View v)
        {
            List<double> paces = v.Forces.Select(PerHour).Where(x => x > 0.05).OrderBy(x => x).ToList();
            return paces.Count > 0 ? paces[paces.Count / 2] : Common.GamePace.PerHour(3.4f);
        }

        /// <summary>Map units a party covers an hour over a day on open ground, by the game's rules, from the march in its view (0.05 when not known).</summary>
        public static double PerHour(ViewParty p)
        {
            if (p?.March != null) return Common.GamePace.DayAverage(p.March);
            return 0.05;
        }

        /// <summary>Hours from the view alone: the straight line times RoadFactor over the party's pace (the realm's usual pace when its own is not known).</summary>
        public static Func<ViewParty, Focus, double> ByLine(View v)
        {
            double usual = UsualPace(v);
            return (p, f) =>
            {
                double per = PerHour(p);
                if (per <= 0.06) per = usual;
                return Views.Dist(p.X, p.Y, f.X, f.Y) * RoadFactor / per;
            };
        }

        // ---------- what each item of news is about ----------

        /// <summary>The view's names and ids, to find what a piece of news names.</summary>
        private sealed class Index
        {
            public readonly Dictionary<string, ViewParty> Parties = new Dictionary<string, ViewParty>(StringComparer.Ordinal);
            public readonly Dictionary<string, ViewHolding> Holdings = new Dictionary<string, ViewHolding>(StringComparer.Ordinal);
            public readonly HashSet<string> HoldingIds = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<KeyValuePair<string, object>> ByName = new List<KeyValuePair<string, object>>();

            public Index(View v)
            {
                foreach (ViewParty p in v.Forces.Concat(v.Forces.Where(f => f.Members != null).SelectMany(f => f.Members)).Concat(v.Seen).Concat(v.LastSeen))
                {
                    if (!Parties.ContainsKey(p.Id)) Parties[p.Id] = p;
                    if (p.Lord != null && !Parties.ContainsKey(p.Lord)) Parties[p.Lord] = p;
                    if (!string.IsNullOrWhiteSpace(p.Name) && p.Name.Length >= 3) ByName.Add(new KeyValuePair<string, object>(p.Name, p));
                }
                foreach (ViewHolding h in v.Holdings.Concat(v.Frontier))
                {
                    if (!Holdings.ContainsKey(h.Id)) Holdings[h.Id] = h;
                    HoldingIds.Add(h.Id);
                    if (!string.IsNullOrWhiteSpace(h.Name) && h.Name.Length >= 3) ByName.Add(new KeyValuePair<string, object>(h.Name, h));
                }
                // the longest names first, so "Mount Gundabad" is found before "Gundabad"
                ByName.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            }
        }

        private static Focus OfParty(ViewParty p, string trigger)
            => new Focus { Id = p.Id, Name = p.Name, IsParty = true, X = p.X, Y = p.Y, Trigger = trigger };

        private static Focus OfHolding(ViewHolding h, View v, string trigger)
            => new Focus { Id = h.Id, Name = h.Name, X = h.X, Y = h.Y, Trigger = trigger, OurHolding = h.Faction == v.Faction };

        /// <summary>The focuses of one item of news: its place and its party when it names them, else the lords and holdings its words name.</summary>
        private static List<Focus> FocusesOf(View v, Trigger tr, Index index, Func<string, Tuple<float, float, string>> place)
        {
            var list = new List<Focus>();
            string kind = tr.Kind ?? "";
            ViewHolding at = tr.Place != null && index.Holdings.TryGetValue(tr.Place, out ViewHolding vh) ? vh : null;
            ViewParty party = tr.Party != null && index.Parties.TryGetValue(tr.Party, out ViewParty vp) ? vp : null;
            Focus placeFocus = null;
            if (at != null) placeFocus = OfHolding(at, v, kind);
            else if (tr.Place != null)
            {
                // a holding the view does not list (an enemy's village raided by the realm's own lords): where the map has it
                ClockItem c = v.ClockItems.FirstOrDefault(x => x.Place == tr.Place);
                Tuple<float, float, string> where = c != null ? Tuple.Create(c.X, c.Y, (string)null) : place?.Invoke(tr.Place);
                if (where != null) placeFocus = new Focus { Id = tr.Place, Name = where.Item3 ?? tr.Place, X = where.Item1, Y = where.Item2, Trigger = kind };
            }

            if (kind == "enemy_in_view" && party != null)
            {
                // an enemy marching on one of the realm's holdings: the holding is the focus, decided when it arrives and the holding
                // would fall (the storm once its camp is ready, or hunger, whichever is sooner); else the enemy itself, decided when it could
                // reach the realm's nearest holding
                ViewHolding target = party.Toward != null && index.Holdings.TryGetValue(party.Toward, out ViewHolding th) ? th : null;
                if (target != null && !double.IsNaN(party.ArrivesAt) && party.ArrivesAt > v.Hours)
                {
                    // an enemy marching on a holding the realm knows: decided when it gets there, and for one of the realm's own, when that would fall
                    // (Nov 3, poke #2041: Aragorn marching to relieve Torech Emel, which the realm besieged, was "decided" when he could reach its
                    // nearest holding, 29 hours, not when he reached Torech Emel, 39)
                    Focus f = OfHolding(target, v, kind);
                    string how = null;
                    double hold = target.Faction == v.Faction ? HoldsFor(target, party, out how) : 0;
                    f.DecidedAt = party.ArrivesAt + hold;
                    f.How = party.Name + " reaches " + target.Name + " about " + Text.When(party.ArrivesAt) + (hold > 0 ? ", and " + how : "");
                    list.Add(f);
                    // and the enemy itself, until it gets there: who could meet it on the way
                    Focus on = OfParty(party, kind);
                    on.DecidedAt = party.ArrivesAt;
                    on.How = "it reaches " + target.Name + " then";
                    list.Add(on);
                    return list;
                }
                Focus e = OfParty(party, kind);
                e.MinHorizon = NoClock;
                double per = PerHour(party);
                double reach = Views.Reach(party);
                if (per > 0.06 && !float.IsNaN((float)reach) && reach > 0 && party.NearestHolding != null)
                {
                    e.DecidedAt = v.Hours + reach / per;
                    e.How = "at its pace " + party.Name + " could reach your " + party.NearestHolding + " by then";
                }
                list.Add(e);
                return list;
            }

            if (placeFocus != null)
            {
                // the game's clock at the place: a siege's storm or hunger, a battle's end, a raid's looting
                ClockItem c = v.ClockItems.Where(x => x.Place == placeFocus.Id && !double.IsNaN(x.DecidedAt)).OrderBy(x => x.DecidedAt).FirstOrDefault();
                if (c != null) { placeFocus.DecidedAt = c.DecidedAt; placeFocus.How = c.Why; }
                if (kind == "low_food" && at != null && at.FoodPerDay > 0 && at.Food >= 0)
                {
                    double food = v.Hours + 24 * at.Food / at.FoodPerDay;
                    if (double.IsNaN(placeFocus.DecidedAt) || food < placeFocus.DecidedAt) { placeFocus.DecidedAt = food; placeFocus.How = "its food runs out under siege"; }
                }
                list.Add(placeFocus);
            }
            if (party != null && (placeFocus == null || kind == "tool" || kind == "low_food"))
            {
                Focus p = OfParty(party, kind);
                if (kind == "tool" && party.Relation == "own" && Marching(party) && !double.IsNaN(party.ArrivesAt) && party.ArrivesAt > v.Hours)
                {
                    p.DecidedAt = party.ArrivesAt;
                    p.How = party.Name + " gets where he is going";
                }
                // a battle in the open has no place: its clock is the battle's, found by who is in it
                ClockItem c = v.ClockItems.Where(x => x.Place == null && x.Parties.Contains(party.Id) && !double.IsNaN(x.DecidedAt)).OrderBy(x => x.DecidedAt).FirstOrDefault();
                if (c != null) { p.DecidedAt = c.DecidedAt; p.How = c.Why; }
                list.Add(p);
            }
            if (list.Count > 0) return list;

            // the words of the news: the ids in [brackets] it names, then the names of lords and holdings in it (a capture, a battle in the
            // open, an army broken up, a letter), the realm's own lords last, since news about one of them is about where the others are
            string text = tr.Text ?? "";
            var found = new List<object>();
            foreach (Match m in Ids.Matches(text))
            {
                string id = m.Groups[1].Value;
                if (index.Holdings.TryGetValue(id, out ViewHolding h)) found.Add(h);
                else if (index.Parties.TryGetValue(id, out ViewParty p)) found.Add(p);
            }
            foreach (KeyValuePair<string, object> kv in index.ByName)
                if (!found.Contains(kv.Value) && Names(text, kv.Key)) found.Add(kv.Value);
            foreach (object o in found.OrderBy(o => o is ViewParty vp2 && vp2.Relation == "own" ? 1 : 0).Take(kind == "letter" ? 3 : 2))
            {
                Focus f = o is ViewHolding fh ? OfHolding(fh, v, kind) : OfParty((ViewParty)o, kind);
                ClockItem c = v.ClockItems.Where(x => (x.Place == f.Id || x.Parties.Contains(f.Id)) && !double.IsNaN(x.DecidedAt)).OrderBy(x => x.DecidedAt).FirstOrDefault();
                if (c != null) { f.DecidedAt = c.DecidedAt; f.How = c.Why; }
                list.Add(f);
            }
            return list;
        }

        /// <summary>A lord on the march, as its report words it (not fighting, besieging, raiding, inside a holding or holding still).</summary>
        private static bool Marching(ViewParty p)
        {
            string d = p.Doing ?? "";
            return d.StartsWith("going to", StringComparison.Ordinal) || d.StartsWith("on the move", StringComparison.Ordinal) || d.StartsWith("marching", StringComparison.Ordinal)
                   || d.StartsWith("defending", StringComparison.Ordinal) || d.StartsWith("on the way", StringComparison.Ordinal) || d.StartsWith("joining", StringComparison.Ordinal);
        }

        /// <summary>A name in a piece of news, as a whole word.</summary>
        private static bool Names(string text, string name)
        {
            int i = text.IndexOf(name, StringComparison.Ordinal);
            while (i >= 0)
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int end = i + name.Length;
                bool after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (before && after) return true;
                i = text.IndexOf(name, i + 1, StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>
        /// How long one of the realm's castles or towns would hold once an enemy reaches it: an enemy stronger than its walls hold against
        /// may storm once its camp has built its preparations and a ram (48 and 12 man-days at the square root of its men a day); a weaker one
        /// waits until the bar falls after four days, or the food runs out, whichever is sooner. 0 for a village or anything else.
        /// </summary>
        public static double HoldsFor(ViewHolding h, ViewParty enemy, out string how)
        {
            how = null;
            if (h == null || h.WallLevel <= 0) return 0;
            int men = Math.Max(1, enemy.ArmyMen > 0 ? enemy.ArmyMen : enemy.Men);
            double camp = 24.0 * 60 / Math.Sqrt(men) + 3;
            double food = h.Food >= 0 && h.FoodPerDay > 0 ? 24 * h.Food / h.FoodPerDay : double.NaN;
            if (enemy.Strength > h.HoldsUpTo)
            {
                how = "its walls hold it only until the camp is ready to storm, about " + Math.Round(camp) + " hours later";
                return !double.IsNaN(food) && food < camp ? food : camp;
            }
            if (!double.IsNaN(food) && food < 96) { how = "its food lasts about " + Math.Round(food) + " hours of siege"; return food; }
            how = "its walls hold against it until the bar falls after four days of siege";
            return 96;
        }
    }
}
