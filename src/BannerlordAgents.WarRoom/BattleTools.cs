using System.Globalization;
using System.Text.Json.Nodes;
using BannerlordAgents.Common;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// Two more questions the agent may ask inside its calls (0.7.4; Mert, Oct 20: battle time estimates "both for siege battles
    /// and for field battles", and fixing the gap that a besieger sees so little of a relief coming):
    /// - battle_odds: how a battle would go and how long it would last, by the game's own battle rules (Common.GameBattle), between
    ///   lords of the realm and enemies it has seen, in the open or storming a castle or town (with the camp's preparation time
    ///   first), from the realm's own view file only: its own lords' men, strength, morale and Tactics; the enemy's men and
    ///   strength as seen; an enemy place's defenders where the realm's eyes reach; the enemy's morale run low, middling, high.
    /// - roads_in: the roads into a holding from the castles and towns of the realms at war with the realm (who holds which place
    ///   the map shows anyone), found as the game finds ways: where they come together, a spot on each some hours out where a
    ///   watch gives warning, and which places feed it.
    /// Both say nothing the realm could not know.
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string OddsName = "battle_odds", RoadsName = "roads_in";

        public static JsonObject OddsDefinition() => new JsonObject
        {
            ["name"] = OddsName,
            ["description"] = "How a battle would go and how long it would last, by the game's own battle rules: your lords or armies against enemies your report shows, in the open, "
                              + "or storming a castle or town (with how long the siege camp's preparations would take first). It gives each side's blows a round, the losses an hour, "
                              + "who wins, how (all down, morale broken, or a retreat), in how long, and with how many left; the enemy's morale you cannot see, so it is run low, "
                              + "middling and high. Lords seen marching to that place join their side as they arrive.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["ours"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Ids in [brackets] of your lords or armies who would fight together." },
                    ["theirs"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Ids of enemy lords or armies your report shows (leave empty for a storm of a castle or town with no lord outside)." },
                    ["at"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: a castle or town. An enemy one: your lords storm it. One of yours: their lords storm it, your lords and its garrison defending." }
                },
                ["required"] = new JsonArray("ours")
            }
        };

        public static JsonObject RoadsDefinition() => new JsonObject
        {
            ["name"] = RoadsName,
            ["description"] = "The roads into a holding from the castles and towns of the realms at war with you, found as the game finds ways: where they come together on the "
                              + "way in, a spot [pX_Y] on each road some hours out where a watch (the watch order) would see a relief coming, how many hours of warning that spot "
                              + "gives at a lone lord's pace and at an army's, and which of their places each road serves.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["to"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of a holding." },
                    ["hours_out"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: how many hours of a lone lord's march before the walls the watch spots should stand (default 12, from 3 to 48)." }
                },
                ["required"] = new JsonArray("to")
            }
        };

        // ---------- battle odds ----------

        private sealed class Force
        {
            public string Id, Name;
            public double Men, Strength;
            public float Morale = -1, Tactics;
        }

        public string Odds(JsonObject args)
        {
            JsonObject view = ReadView();
            if (view == null) return "Your realm's view is not ready yet; ask again in a moment.";
            double now = Num(view["hours"]);
            List<string> oursQ = (args["ours"] as JsonArray)?.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            List<string> theirsQ = (args["theirs"] as JsonArray)?.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            string atQ = Str(args["at"])?.Trim().Trim('[', ']');
            var own = (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var enemies = (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Concat((view["last_seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()).ToList();
            var lines = new List<string>();
            var ours = new List<Force>();
            foreach (string q in oursQ)
            {
                string id = q.Trim().Trim('[', ']');
                JsonObject o = own.FirstOrDefault(x => Eq(Str(x["id"]), id) || Eq(Str(x["lord"]), id));
                if (o == null) { lines.Add("(" + q + ": no lord or army of yours by that id.)"); continue; }
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                double str = F(o["army_strength"]) > 0 ? F(o["army_strength"]) : F(o["strength"]);
                ours.Add(new Force { Id = Str(o["lord"]) ?? Str(o["id"]), Name = Str(o["name"]), Men = men, Strength = str, Morale = o["morale"] is JsonValue ? F(o["morale"]) : -1, Tactics = F(o["tactics"]) });
            }
            var theirs = new List<Force>();
            foreach (string q in theirsQ)
            {
                string id = q.Trim().Trim('[', ']');
                JsonObject o = enemies.FirstOrDefault(x => Eq(Str(x["id"]), id) || Eq(Str(x["lord"]), id));
                if (o == null) { lines.Add("(" + q + ": your realm has seen no enemy by that id.)"); continue; }
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                theirs.Add(new Force { Id = Str(o["lord"]) ?? Str(o["id"]), Name = Str(o["name"]), Men = men, Strength = F(o["strength"]) });
            }
            if (ours.Count == 0) return string.Join("\n", lines.Prepend("Name at least one of your lords or armies in ours."));
            // a storm: an enemy place your lords would storm, or one of yours their lords would storm
            JsonObject enemyPlace = atQ == null ? null : (view["frontier"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(h => Eq(Str(h["id"]), atQ));
            JsonObject ownPlace = atQ == null ? null : (view["holdings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(h => Eq(Str(h["id"]), atQ));
            if (atQ != null && enemyPlace == null && ownPlace == null) lines.Add("(" + atQ + ": not a castle or town of yours, nor an enemy one near your lands; reckoned as a battle in the open.)");
            BattleSide Side(List<Force> fs, bool known)
            {
                double men = fs.Sum(f => f.Men), str = fs.Sum(f => f.Strength);
                double morale = known ? fs.Sum(f => (f.Morale >= 0 ? f.Morale : 50) * f.Men) / Math.Max(1, men) : 50;
                Force lead = fs.OrderByDescending(f => f.Men).FirstOrDefault();
                return new BattleSide { Men = men, Power = men > 0 ? (float)(str / men) : 1f, Advantage = 1f + (known && lead != null ? 0.001f * lead.Tactics : 0f), Morale = (float)morale };
            }
            string Names(List<Force> fs) => fs.Count == 0 ? "none" : string.Join(", ", fs.Select(f => f.Name + " [" + f.Id + "]"));
            BattleSide mine = Side(ours, true);
            if (enemyPlace != null || (ownPlace != null && theirs.Count > 0))
            {
                bool weStorm = enemyPlace != null;
                JsonObject place = weStorm ? enemyPlace : ownPlace;
                int garrison = I(place["garrison"]), militia = I(place["militia"]);
                if (weStorm && garrison < 0) return string.Join("\n", lines.Append("Your realm cannot see " + Str(place["name"]) + "'s garrison now: send a lord near it first."));
                // the defenders: its garrison and militia, with (an enemy place) the lords seen inside folded into their strength, or (yours)
                // your lords inside it already and the lords you name, who would shelter there
                double defMen = Math.Max(0, garrison) + Math.Max(0, militia);
                double defStr = F(place["defence"]);
                if (!weStorm) { defMen += ours.Sum(f => f.Men); defStr += ours.Sum(f => f.Strength); }
                BattleSide besiegers = weStorm ? mine : Side(theirs, false);
                var walls = new BattleSide { Men = defMen, Power = defMen > 0 ? (float)(defStr / defMen) : 1f, Advantage = 1f, Morale = weStorm ? 50 : mine.Morale, Walls = true };
                int level = Math.Max(1, I(place["wall_level"]));
                float worth = (float)((4.0 + level) / (1 + 0.25 + 0.16));   // the walls' worth once the first engine, a ram, is built
                besiegers.Advantage -= 0.1f;                               // the game's siege attackers' -0.1
                bool town = Str(place["kind"]) == "town";
                BattleOutcome[] runs = GameBattle.ByUnseenMorale("siege", town, worth, besiegers, walls, !weStorm, Joiners(view, atQ, now, weStorm));
                BattleOutcome o = runs[1];
                double prep = 24.0 * 48 / Math.Sqrt(Math.Max(1, besiegers.Men)), ram = 24.0 * 12 / Math.Sqrt(Math.Max(1, besiegers.Men));
                GameBattle.StrikesPerRound(true, town, worth, besiegers.Men, walls.Men, out double sA, out double sD);
                lines.Insert(0, "BATTLE ODDS: " + (weStorm ? Names(ours) + " storming " : Names(theirs) + " storming your ") + Str(place["name"]) + " [" + atQ + "]");
                lines.Add("- Besiegers: " + Words.N(besiegers.Men) + " men, strength " + Words.N(besiegers.Men * besiegers.Power) + "; defenders: " + Words.N(walls.Men) + " (garrison " + Math.Max(0, garrison)
                          + ", militia " + Math.Max(0, militia) + (weStorm ? "" : ", and your lords with them") + "), strength " + Words.N(walls.Men * walls.Power) + ", behind walls of level " + level + ".");
                lines.Add("- First the camp must finish its preparations, about " + Words.N(prep) + " hours for " + Words.N(besiegers.Men) + " men (48 man-days at the square root of its men a day; "
                          + "an engineer's skill shortens it), then build an engine, a ram about " + Words.N(ram) + " hours more: no storm can come before about " + Words.N(prep + ram) + " hours.");
                lines.Add("- A storm is one round an hour. The besiegers' blows grow only with the defenders' number: " + sA + " a round now; the defenders strike " + sD + " a round with the walls' worth "
                          + worth.ToString("0.0", CultureInfo.InvariantCulture) + " (once a ram is built; each further engine lowers it). At first the besiegers lose about " + Words.N(o.AttackerLossPerHour)
                          + " men an hour, the defenders about " + Words.N(o.DefenderLossPerHour) + ".");
                lines.Add("- Run ahead: " + Outcome(o, "the besiegers", "the defenders", runs, "besiegers", "defenders") + ". Nobody can leave a battle once in it.");
            }
            else
            {
                if (theirs.Count == 0) return string.Join("\n", lines.Append("Name the enemies in theirs, or a castle or town in at."));
                BattleSide enemy = Side(theirs, false);
                BattleOutcome[] runs = GameBattle.ByUnseenMorale("field", false, 1f, mine, enemy, false);
                BattleOutcome o = runs[1];
                GameBattle.StrikesPerRound(false, false, 1f, mine.Men, enemy.Men, out double sA, out double sD);
                lines.Insert(0, "BATTLE ODDS: " + Names(ours) + " against " + Names(theirs) + ", in the open");
                lines.Add("- Yours: " + Words.N(mine.Men) + " men, strength " + Words.N(mine.Men * mine.Power) + ", morale " + Words.N(mine.Morale) + "; theirs: " + Words.N(enemy.Men) + " men, strength "
                          + Words.N(enemy.Men * enemy.Power) + ", morale not known to you.");
                lines.Add("- In the open: two rounds an hour, each side's blows growing with its own men: yours " + sA + " a round, theirs " + sD + ". At first you lose about "
                          + Words.N(o.AttackerLossPerHour) + " men an hour, they about " + Words.N(o.DefenderLossPerHour) + ".");
                lines.Add("- Run ahead: " + Outcome(o, "you", "they", runs, "of yours", "of theirs") + ". Nobody can leave a battle once in it, but a side whose morale falls to 20 after losing four rounds running may retreat (nothing is captured then).");
            }
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = OddsName, ours = oursQ, theirs = theirsQ, place = atQ, lines });
            return string.Join("\n", lines);
        }

        private static string Outcome(BattleOutcome o, string a, string d, BattleOutcome[] runs, string aOf, string dOf)
        {
            if (o.Winner == null) return "after 5 days it has not ended (" + Words.N(o.AttackersLeft) + " " + aOf + " and " + Words.N(o.DefendersLeft) + " " + dOf + " still standing)";
            bool aWins = o.Winner == "attackers";
            string how = o.How == "rout" ? (aWins ? d : a) + " break" : o.How == "retreat" ? (aWins ? d : a) + " retreat" : (aWins ? d : a) + " are all down";
            return (aWins ? a : d) + " win, " + how + ", in " + GameBattle.HoursRange(runs) + ", with about " + Words.N(aWins ? o.AttackersLeft : o.DefendersLeft) + " " + (aWins ? aOf : dOf) + " still standing";
        }

        /// <summary>Lords the realm saw marching to the place (heading for it, with the hour they get there), joining their own side.</summary>
        private static List<BattleJoiner> Joiners(JsonObject view, string place, double now, bool weStorm)
        {
            var list = new List<BattleJoiner>();
            if (place == null) return list;
            foreach (JsonObject o in (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (!Eq(Str(o["toward"]), place)) continue;
                double at = o["arrives_at"] is JsonValue av && av.TryGetValue(out double ad) ? ad - now : double.NaN;
                if (double.IsNaN(at) || at > 24 * 5) continue;
                bool enemy = Str(o["relation"]) == "enemy";
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                // an enemy marching to an enemy place joins its defenders (we storm); to our place, its besiegers
                list.Add(new BattleJoiner { Attackers = weStorm ? !enemy : enemy, Men = men, At = Math.Max(0, at), Power = men > 0 ? F(o["strength"]) / (float)men : 0 });
            }
            return list;
        }

        private static bool Eq(string a, string b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>An answer, or what went wrong in words (never an exception into the call).</summary>
        private static string Safe(Func<string> answer)
        {
            try { return answer(); }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is NullReferenceException || e is IndexOutOfRangeException || e is FormatException || e is System.Text.Json.JsonException || e is IOException)
            { return "The answer could not be worked out (" + e.Message + ")."; }
        }

        // ---------- roads in ----------

        public string RoadsIn(JsonObject args)
        {
            JsonObject view = ReadView();
            if (view == null) return "Your realm's view is not ready yet; ask again in a moment.";
            WalkMap m = MapFor(_root, (string)view["map"]);
            if (m == null) return "The map is not ready yet, so the roads cannot be worked out.";
            string toQ = Str(args["to"])?.Trim().Trim('[', ']');
            Place target = m.Places.FirstOrDefault(p => Eq(p.Id, toQ)) ?? m.Places.FirstOrDefault(p => Words.Plain(p.Name) == Words.Plain(toQ ?? ""));
            if (target == null) return "No holding called " + (toQ ?? "(nothing)") + ".";
            double asked = Num(args["hours_out"]);
            double hoursOut = Math.Max(3, Math.Min(48, !double.IsNaN(asked) && asked > 0 ? asked : 12));
            Pace lone = Pace.FromSpeed(3.5f), army = Pace.FromSpeed(1.6f);
            float outUnits = (float)(hoursOut * GamePace.DayAverage(lone));
            float tx = target.Gx != 0 || target.Gy != 0 ? target.Gx : target.X, ty = target.Gx != 0 || target.Gy != 0 ? target.Gy : target.Y;
            // their castles and towns, nearest first
            var sources = new List<(Place p, string realm)>();
            if (view["enemy_holdings"] is JsonObject eh)
                foreach (KeyValuePair<string, JsonNode> kv in eh)
                    foreach (JsonNode n in kv.Value as JsonArray ?? new JsonArray())
                    {
                        Place p = m.Places.FirstOrDefault(x => Eq(x.Id, Str(n)));
                        if (p != null && !Eq(p.Id, target.Id)) sources.Add((p, kv.Key));
                    }
            if (sources.Count == 0) return "No castle or town of a realm at war with you is known.";
            var near = sources.OrderBy(s => WalkMap.Dist(s.p.X, s.p.Y, tx, ty)).Where(s => WalkMap.Dist(s.p.X, s.p.Y, tx, ty) <= 350).Take(30).ToList();
            var ways = new List<(Place p, string realm, float[] spot, float length)>();
            foreach ((Place p, string realm) in near)
            {
                RouteResult r;
                lock (Gate) r = _finder.Find(p.Gx != 0 || p.Gy != 0 ? p.Gx : p.X, p.Gx != 0 || p.Gy != 0 ? p.Gy : p.Y, tx, ty);
                if (r == null || !r.Ok || r.Length < outUnits * 0.6f) continue;
                ways.Add((p, realm, PointBack(r.Points, outUnits), r.Length));
            }
            if (ways.Count == 0) return "No road from their castles and towns reaches " + target.Name + " from far enough out.";
            // roads that pass the same spot are one road in
            var groups = new List<List<(Place p, string realm, float[] spot, float length)>>();
            foreach (var w in ways.OrderBy(w => w.length))
            {
                var g = groups.FirstOrDefault(x => WalkMap.Dist(x[0].spot[0], x[0].spot[1], w.spot[0], w.spot[1]) <= 10);
                if (g != null) g.Add(w); else groups.Add(new List<(Place, string, float[], float)> { w });
            }
            var lines = new List<string> { "THE ROADS INTO " + target.Name + " [" + target.Id + "] from the castles and towns of the realms at war with you (the " + ways.Count + " nearest that have a way there)" };
            foreach (var g in groups.OrderByDescending(x => x.Count).ThenBy(x => x[0].length).Take(6))
            {
                float sx = g[0].spot[0], sy = g[0].spot[1];
                string from = WalkMap.Compass(tx, ty, sx, sy);
                var places = g.OrderBy(x => x.length).Select(x => x.p.Name + " (" + x.realm + ", " + Words.N(x.length) + " by the way)").ToList();
                lines.Add("- From the " + from + ", by " + m.Describe(sx, sy) + " [" + Explain.SpotId(sx, sy) + "]: the way in from " + g.Count + (g.Count == 1 ? " place" : " places") + ": "
                          + string.Join("; ", places.Take(5)) + (places.Count > 5 ? "; and " + (places.Count - 5) + " more" : "") + ". A watch there sees a lone lord about "
                          + Words.N(hoursOut) + " hours before it reaches the walls, an army of 2,000 about " + Words.N(outUnits / GamePace.DayAverage(army)) + ".");
            }
            if (groups.Count > 6) lines.Add("- And " + (groups.Count - 6) + " more roads, each from fewer places.");
            lines.Add("(Hours at a lone lord's speed of 3.5 and an army's of 1.6, on open ground, night counted. A lord sees about 10 to 15 around it, an army farther.)");
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = RoadsName, to = target.Id, hours_out = hoursOut, lines });
            return string.Join("\n", lines);
        }

        /// <summary>The point a given length back along a way from its end.</summary>
        private static float[] PointBack(List<float[]> pts, float back)
        {
            float left = back;
            for (int i = pts.Count - 1; i > 0; i--)
            {
                float[] a = pts[i], b = pts[i - 1];
                float len = WalkMap.Dist(a[0], a[1], b[0], b[1]);
                if (len >= left) return new[] { a[0] + (b[0] - a[0]) * left / len, a[1] + (b[1] - a[1]) * left / len };
                left -= len;
            }
            return pts[0];
        }
    }
}
