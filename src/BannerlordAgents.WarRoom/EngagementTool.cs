using System.Globalization;
using System.Text.Json.Nodes;
using BannerlordAgents.Common;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The engagement question (0.12.3, Oct 25, after Alenon's army was lost to a host split into a dozen parties: before an
    /// engagement, the agent should be able to forecast how long it will take its selected lord/lords to begin and end the
    /// engagement, and which enemy lords in view are close enough in terms of marching distance to that engagement to participate
    /// in it as it progresses, so it can decide accordingly and set rules accordingly):
    /// - engagement: the realm's chosen lords or armies against one enemy lord or army in view: when each of them gets to where it
    ///   is now (the game's way, at each one's pace), the fight from the first one's arrival with the others joining as they come,
    ///   by the game's own battle rules; every other enemy lord and army in view with the hour it could get there, joining the
    ///   fight as it comes if it is still going on; the fight run both ways; and the join_hours that makes the orders' rules count
    ///   the lords who could join it.
    /// From the realm's own view file only, like the other questions: it says nothing the realm could not know.
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string EngageName = "engagement";

        /// <summary>The other enemy lords and armies whose way to the fight is worked out (the nearest by the crow's flight).</summary>
        private const int MostJoinersRouted = 30;

        public static JsonObject EngageDefinition() => new JsonObject
        {
            ["name"] = EngageName,
            ["description"] = "Before an attack: when each of your chosen lords or armies would reach an enemy lord or army in view (by the way the game would march, at each one's pace), "
                              + "the fight from the first one's arrival with the others joining as they come, how long it would last and how it would end by the game's own battle rules, "
                              + "and every other enemy lord and army in view with the hour it could march there, joining the fight as it comes while the fight is still going on. "
                              + "It runs the fight without them and with them, and says what join_hours to give your orders so their rules count the lords who could join it.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["ours"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Ids in [brackets] of your lords or armies who would march on it, each from where it is now." },
                    ["target"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of an enemy lord or army in view." }
                },
                ["required"] = new JsonArray("ours", "target")
            }
        };

        /// <summary>One party marching to the fight: its strength, and the hours from now until it gets there.</summary>
        private sealed class Marcher
        {
            public string Id, Name;
            public double Men, Strength, Hours = double.NaN, Way = double.NaN;
            public float Morale = -1, Tactics;
        }

        public string Engagement(JsonObject args)
        {
            JsonObject view = ReadView();
            if (view == null) return "Your realm's view is not ready yet; ask again in a moment.";
            WalkMap m = MapFor(_root, (string)view["map"]);
            if (m == null) return "The map is not ready yet, so the engagement cannot be worked out.";
            double now = Num(view["hours"]);
            List<Known> known = Gather(view, m);
            var own = (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var seen = (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            string targetQ = Str(args["target"]);
            Known q = Find(known, m, targetQ);
            if (q == null || q.Place || q.Own) return "Name an enemy lord or army in view in target, by its id in [brackets] (" + Show(targetQ) + " is not one).";
            JsonObject qo = seen.FirstOrDefault(o => Eq(Str(o["id"]), q.Id));
            if (qo == null) return q.Name + " is not in view now: an engagement is reckoned only against an enemy your realm can see (can_catch says where it was last seen and how fast it goes).";
            GameCalendar cal = Calendar();
            var lines = new List<string>();

            // your lords: each one's way and hours to where the target is now
            var ours = new List<Marcher>();
            foreach (string s in (args["ours"] as JsonArray)?.Select(Str).Where(x => !string.IsNullOrWhiteSpace(x)) ?? Enumerable.Empty<string>())
            {
                Known k = Find(known, m, s);
                JsonObject o = k == null ? null : own.FirstOrDefault(x => Eq(Str(x["id"]), k.Id));
                if (k == null || !k.Own || o == null) { lines.Add("(" + s + ": no lord or army of yours by that id.)"); continue; }
                if (k.March == null) { lines.Add("(" + k.Name + "'s pace is not known, so it is left out.)"); continue; }
                RouteResult r;
                lock (Gate) r = _finder.Find(k.X, k.Y, q.X, q.Y);
                double h = r.Ok ? Marching.Hours(r, m, k.March, now, out _) : double.NaN;
                if (double.IsNaN(h)) { lines.Add("(" + k.Name + ": no way to " + q.Name + " is known" + (r.Ok ? "" : " (" + r.Why + ")") + ", so it is left out.)"); continue; }
                ours.Add(new Marcher
                {
                    Id = Str(o["lord"]) ?? k.Id, Name = k.Name, Hours = h, Way = r.Length,
                    Men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]), Strength = F(o["army_strength"]) > 0 ? F(o["army_strength"]) : F(o["strength"]),
                    Morale = o["morale"] is JsonValue ? F(o["morale"]) : -1, Tactics = F(o["tactics"])
                });
            }
            if (ours.Count == 0) return string.Join("\n", lines.Prepend("Name at least one of your lords or armies in ours."));
            ours = ours.OrderBy(x => x.Hours).ToList();
            double begins = ours[0].Hours;

            // every other enemy lord and army in view: its way and hours to the fight, the nearest by the crow's flight worked out
            var theirs = new List<Marcher>();
            var candidates = new List<(Known k, JsonObject o, double crow)>();
            foreach (JsonObject o in seen)
            {
                if (Str(o["relation"]) != "enemy" || Str(o["kind"]) == "bandit" || Eq(Str(o["id"]), q.Id)) continue;
                Known k = known.FirstOrDefault(x => !x.Place && Eq(x.Id, Str(o["id"])));
                if (k?.March == null) continue;
                candidates.Add((k, o, WalkMap.Dist(k.X, k.Y, q.X, q.Y)));
            }
            foreach (var (k, o, crow) in candidates.OrderBy(c => c.crow).Take(MostJoinersRouted))
            {
                RouteResult r;
                lock (Gate) r = _finder.Find(k.X, k.Y, q.X, q.Y);
                double h = r.Ok ? Marching.Hours(r, m, k.March, now, out _) : double.NaN;
                if (double.IsNaN(h)) continue;
                theirs.Add(new Marcher { Id = Str(o["lord"]) ?? k.Id, Name = k.Name, Hours = h, Way = r.Length, Men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]), Strength = F(o["strength"]) });
            }
            theirs = theirs.OrderBy(x => x.Hours).ToList();

            // the fight: yours from the first arrival (those within the hour with it), the rest of yours and theirs joining as they come
            List<Marcher> first = ours.Where(x => x.Hours <= begins + 0.5).ToList();
            BattleSide mine = SideOf(first, true);
            double targetMen = I(qo["army_men"]) > 0 ? I(qo["army_men"]) : I(qo["men"]);
            var target = new Marcher { Men = targetMen, Strength = F(qo["strength"]) };
            BattleSide enemy = SideOf(new List<Marcher> { target }, false);
            List<BattleJoiner> ourJoiners = ours.Skip(first.Count).Select(x => Joiner(x, true, begins)).ToList();
            BattleOutcome[] alone = GameBattle.ByUnseenMorale("field", false, 1f, mine, enemy, false, ourJoiners);
            BattleOutcome[] all = GameBattle.ByUnseenMorale("field", false, 1f, mine, enemy, false, ourJoiners.Concat(theirs.Select(x => Joiner(x, false, begins))).ToList());
            double endsAlone = alone[1].Hours, ends = all[1].Hours;

            lines.Insert(0, "ENGAGEMENT: " + string.Join(", ", ours.Select(x => x.Name + " [" + x.Id + "]")) + " against " + Head(q));
            lines.Add("- " + q.Name + ": " + Words.N(targetMen) + " men, strength " + Words.N(F(qo["strength"])) + ", " + (Str(qo["doing"]) ?? "doing what is not known") + "."
                      + (q.Toward != null ? " It is marching somewhere, so the fight may come later and elsewhere than reckoned here (can_catch says whether and when it can be caught)." : ""));
            lines.Add("- Your lords reach where it is now, by the way the game would march at each one's pace: "
                      + string.Join("; ", ours.Select(x => x.Name + " in about " + Words.N(x.Hours) + " hours (" + cal.When(now + x.Hours) + "), " + Words.N(x.Way) + " by the way")) + ".");
            lines.Add("- The fight begins when " + string.Join(" and ", first.Select(x => x.Name)) + " get" + (first.Count == 1 ? "s" : "") + " there, about " + cal.When(now + begins)
                      + (ours.Count > first.Count ? "; your others join as they arrive" : "") + ". A fight in the open is two rounds an hour, and nobody leaves it once in it.");
            double bringsA = Brought(first, ours, begins, endsAlone), bringsD = targetMen;
            lines.Add("- Against " + q.Name + " alone: " + Outcome(alone[1], alone, "you", "they", "your", "their", bringsA, bringsD)
                      + (double.IsNaN(endsAlone) ? "." : ", ending about " + cal.When(now + begins + endsAlone) + "."));
            // who could join it: soonest first, each with when it gets there and whether the fight is still going on then
            double horizon = double.IsNaN(ends) ? 24 * 5 : ends;
            List<Marcher> inTime = theirs.Where(x => x.Hours - begins <= horizon).ToList();
            if (theirs.Count == 0) lines.Add("- No other enemy lord or army in view could march there.");
            else
            {
                var shown = theirs.Take(15).Select(x =>
                {
                    double after = x.Hours - begins;
                    string when = after <= 0 ? "there before the fight begins (" + Words.N(x.Hours) + " hours from now)" : "about " + Words.N(after) + " hours after it begins (" + cal.When(now + x.Hours) + ")";
                    string verdict = after <= horizon ? "in time to join it" : "too late: the fight is over by then";
                    return x.Name + " [" + x.Id + "], " + Words.N(x.Men) + " men, strength " + Words.N(x.Strength) + ", " + Words.N(x.Way) + " by the way: " + when + ", " + verdict;
                }).ToList();
                lines.Add("- Other enemy lords and armies in view that could march there (by the way the game would march, at each one's pace; whether each would come is the game's choice), soonest first: "
                          + string.Join("; ", shown) + (theirs.Count > 15 ? "; and " + (theirs.Count - 15) + " more, farther off" : "") + ".");
                if (inTime.Count == 0) lines.Add("- None of them gets there before the fight ends.");
                else
                {
                    double bringsAll = bringsD + inTime.Sum(x => x.Men);
                    lines.Add("- With the " + inTime.Count + " who could join in time (strength " + Words.N(inTime.Sum(x => x.Strength)) + " together): "
                              + Outcome(all[1], all, "you", "they", "your", "their", Brought(first, ours, begins, ends), bringsAll)
                              + (double.IsNaN(ends) ? "." : ", ending about " + cal.When(now + begins + ends) + "."));
                }
            }
            // for the orders' rules: the hours from now until it would end
            double until = begins + (double.IsNaN(ends) ? double.IsNaN(endsAlone) ? 24 * 5 : endsAlone : ends);
            lines.Add("- For your orders: the fight would end about " + Words.N(until) + " hours from now. To have an order's rules (its caution, a strike's or a hunt's weighing of its target) count the enemy lords who could join it, "
                      + "give that order join_hours " + Words.N(Math.Ceiling(until)) + " (each counts those who could march to the fight within that many hours from the hour it looks); a smaller number counts only those nearer.");
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = EngageName, ours = ours.Select(x => x.Id).ToList(), target = q.Id, lines });
            return string.Join("\n", lines);
        }

        /// <summary>A side from the marchers on it: men, strength, morale (yours as known, theirs middling) and the first one's Tactics.</summary>
        private static BattleSide SideOf(List<Marcher> fs, bool known)
        {
            double men = fs.Sum(f => f.Men), str = fs.Sum(f => f.Strength);
            double morale = known ? fs.Sum(f => (f.Morale >= 0 ? f.Morale : 50) * f.Men) / Math.Max(1, men) : 50;
            Marcher lead = fs.OrderByDescending(f => f.Men).FirstOrDefault();
            return new BattleSide { Men = men, Power = men > 0 ? (float)(str / men) : 1f, Advantage = 1f + (known && lead != null ? 0.001f * lead.Tactics : 0f), Morale = (float)morale };
        }

        /// <summary>A marcher joining the fight: on your side (the attackers) or theirs, at the hour after its start it gets there.</summary>
        private static BattleJoiner Joiner(Marcher x, bool ours, double begins) =>
            new BattleJoiner { Attackers = ours, Men = x.Men, At = Math.Max(0, x.Hours - begins), Power = x.Men > 0 ? (float)(x.Strength / x.Men) : 0 };

        /// <summary>The men your side brings to a fight that ends after so many hours: the first ones, and those who get there before it ends.</summary>
        private static double Brought(List<Marcher> first, List<Marcher> ours, double begins, double ends) =>
            first.Sum(x => x.Men) + ours.Skip(first.Count).Where(x => double.IsNaN(ends) || x.Hours - begins <= ends).Sum(x => x.Men);
    }
}
