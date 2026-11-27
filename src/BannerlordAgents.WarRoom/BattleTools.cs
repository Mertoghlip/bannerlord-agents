using System.Globalization;
using System.Text.Json.Nodes;
using BannerlordAgents.Common;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// Two more questions the agent may ask inside its calls (0.7.4, Oct 20: battle time estimates both for siege battles
    /// and for field battles, and fixing the gap that a besieger sees so little of a relief coming):
    /// - battle_odds: how a battle would go and how long it would last, by the game's own battle rules (Common.GameBattle), between
    ///   lords of the realm and enemies it has seen, in the open or storming a castle or town (with the camp's preparation time
    ///   first), from the realm's own view file only: its own lords' men, strength, morale and Tactics; the enemy's men and
    ///   strength as seen; an enemy place's defenders where the realm's eyes reach; the enemy's morale run at full and lower, its leader as skilled as the realm's own (0.20.10).
    /// - roads_in: the roads into a holding from the castles and towns of the realms at war with the realm (who holds which place
    ///   the map shows anyone), found as the game finds ways: where they come together, a spot on each some hours out where a
    ///   watch gives warning, and which places feed it.
    /// Both say nothing the realm could not know.
    /// War room 0.22.0 (Nov 26, the tooling audit of run 20261125-2341-C2): a battle under way counts the enemies marching in and the named ones not yet in it, and the
    /// realm's named lords by road, with the order delay for those whose orders do not take them there yet, leaving out those who cannot come (item 4); the muster times
    /// lords by road (item 5); a forecast before a battle shows the enemy's measured edge once the realm has fought it enough (item 9); a storm question with nothing under
    /// way at the place answers that storm (item 18); the totals count only those who arrive before the end (item 23); a siege under way is reckoned from its own clock
    /// (item 28); a lord riding in an army, a prisoner or a lord with no party is said so (item 30).
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string OddsName = "battle_odds", RoadsName = "roads_in";

        public static JsonObject OddsDefinition() => new JsonObject
        {
            ["name"] = OddsName,
            ["description"] = "How a battle would go and how long it would last, by the game's own battle rules: your lords or armies against enemies your report shows, in the open, "
                              + "or storming a castle or town (with how long the siege camp's preparations would take first, or, for a siege under way, its own clock as the place's page shows it). "
                              + "It gives each side's blows a round, the losses an hour, "
                              + "who wins, how (all down, morale broken, or a retreat, which only a battle in the open has), in how long, and with how many left; the enemy's morale you cannot see, "
                              + "so it is run at full and lower, and its leader is taken to be as skilled as your lords. Once your realm has fought 5 battles or more against an enemy realm, "
                              + "it also gives your losses at how hard that realm's blows have landed in them. Lords seen marching to that place join their side as they arrive (each counted once); "
                              + "lords who arrive after it ends are named apart and left out of its numbers. "
                              + "Only lords inside a castle or town defend behind its walls: a relief from outside that joins a storm turns it into a battle outside the walls, where the walls no longer count "
                              + "for anyone, the garrison included; the place still leads that side, so it has no leader's edge and the lords' own captain bonuses do not count. "
                              + "With how = strike and at one of your castles or towns under siege while the camp is not storming, it reckons your named lords, gathered, striking the siege camp together, "
                              + "led by the first of them to arrive, the garrison coming out beside them by the game's rule; the muster question says who can be there by when. "
                              + "When a battle is under way at the place, or (no place given) lords you name are already in one, it runs that battle on from now at the rates measured in it: first as it stands, "
                              + "then with the joiners: enemies seen marching to it and the enemies you name, by road from where your realm saw them; your lords you name, by road, those whose orders do not "
                              + "take them there yet only after your order would land. Lords who cannot come (in another fight, shut in a besieged place, prisoners) are named and left out.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["ours"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Ids in [brackets] of your lords or armies who would fight together." },
                    ["theirs"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Ids of enemy lords or armies your report shows (leave empty for a storm of a castle or town with no lord outside)." },
                    ["at"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: a castle or town. An enemy one: your lords storm it. One of yours: their lords storm it; those of your lords inside it defend with its garrison behind the walls, those outside come as a relief, and the first to join turns the storm into a battle outside the walls." },
                    ["how"] = new JsonObject { ["type"] = "string", ["description"] = "Optional, with at one of your castles or towns that enemies (theirs) besiege: strike = your lords in ours, gathered, attack the siege camp together, all at once, by the rules of a battle in the open; the garrison and your lords inside come out to join once they and yours are more than 1.5 times the camp. Without it, their storm of the place is reckoned." }
                },
                ["required"] = new JsonArray("ours")
            }
        };

        public const string MusterName = "muster";

        /// <summary>War room 0.20.7 (Nov 25): who of the realm's lords can be at a place by when, and the strength gathered there by each hour (run 20261125-0020-C2: the agent never weighed gathering everything against Lorgath's camp with the right numbers).</summary>
        public static JsonObject MusterDefinition() => new JsonObject
        {
            ["name"] = MusterName,
            ["description"] = "Who of your lords and armies could be at a place by when: each one's hours to it by the way the game would march, at its own pace, sorted, "
                              + "and the strength you could have gathered there within 12, 24, 48 and 72 hours. Ask it before deciding whether you can beat an enemy army where it stands, "
                              + "then ask battle_odds with the lords who arrive in time (how = strike for a siege camp at one of your places).",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["at"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of a castle, town or village (any realm's), or of a lord or army your report shows." },
                    ["lords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Optional: only these lords or armies of yours (default: all in the field)." },
                    ["hours"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: list those who arrive within this many hours (default 72, from 6 to 240)." }
                },
                ["required"] = new JsonArray("at")
            }
        };

        public string Muster(JsonObject args)
        {
            JsonObject view = ReadView();
            if (view == null) return "Your realm's view is not ready yet; ask again in a moment.";
            string at = Str(args["at"])?.Trim().Trim('[', ']');
            if (string.IsNullOrEmpty(at)) return "Name the place or lord in at.";
            double within = args["hours"] is JsonValue hv && hv.TryGetValue(out double hd) ? Math.Max(6, Math.Min(240, hd)) : 72;
            var own = (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            IEnumerable<JsonObject> Arr(string k) => (view[k] as JsonArray ?? new JsonArray()).OfType<JsonObject>();
            JsonObject spot = Arr("holdings").Concat(Arr("frontier")).Concat(Arr("enemy_holdings")).Concat(own).Concat(Arr("seen")).Concat(Arr("last_seen"))
                .FirstOrDefault(o => Eq(Str(o["id"]), at) || Eq(Str(o["lord"]), at));
            float px, py;
            string spotName;
            if (spot != null && spot["x"] is JsonValue) { px = F(spot["x"]); py = F(spot["y"]); spotName = Str(spot["name"]) ?? at; }
            else
            {
                // any castle, town or village on the map, as the way question knows them
                WalkMap wm = MapFor(_root, (string)view["map"]);
                Place pl = wm?.Places.FirstOrDefault(p => Eq(p.Id, at));
                if (pl == null) return "Your realm knows no place or lord by the id " + at + ".";
                px = pl.Gx != 0 || pl.Gy != 0 ? pl.Gx : pl.X; py = pl.Gx != 0 || pl.Gy != 0 ? pl.Gy : pl.Y; spotName = pl.Name;
            }
            var only = (args["lords"] as JsonArray)?.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().Trim('[', ']')).ToList();
            var notes = new List<string>();
            if (only != null)
                foreach (string q in only.Where(q => !own.Any(o => Eq(q, Str(o["lord"])) || Eq(q, Str(o["id"])))))
                    notes.Add("(" + (NotInField(q, own, out _) ?? q + ": no lord or army of yours by that id") + ".)");
            // war room 0.22.0 (item 5): by road, timed by the march clock; only lords whose straight line could fall inside the bands are routed (a road is never shorter)
            double cap = Math.Max(within, 72);
            bool anyStraight = false;
            var rows = new List<(string Name, string Id, double Men, double Str, double Hours, string Doing)>();
            var noWay = new List<string>();
            foreach (JsonObject o in own)
            {
                string id = Str(o["lord"]) ?? Str(o["id"]);
                if (only != null && only.Count > 0 && !only.Any(q => Eq(q, id) || Eq(q, Str(o["id"])))) continue;
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                double str = F(o["army_strength"]) > 0 ? F(o["army_strength"]) : F(o["strength"]);
                double h = OwnRoadHours(_root, view, o, px, py, cap, out bool byRoad);
                if (!byRoad) anyStraight = true;
                if (double.IsNaN(h)) { noWay.Add(Str(o["name"]) + " [" + id + "]"); continue; }
                rows.Add((Str(o["name"]), id, men, str, h, Str(o["doing"])));
            }
            rows = rows.OrderBy(r => r.Hours).ToList();
            var lines = new List<string> { "MUSTER AT " + spotName + " [" + at + "]: who could be there by when (each " + (anyStraight ? "in a straight line at its own pace, since the map is not ready; by road it is longer" : "by the way the game would march, at its own pace") + ")" };
            lines.AddRange(notes);
            foreach (var r in rows.Where(r => r.Hours <= within))
                lines.Add("- " + r.Name + " [" + r.Id + "]: " + Words.N(r.Men) + " men, strength " + Words.N(r.Str) + ", about " + Words.N(Math.Max(0, Math.Round(r.Hours))) + " hours" + (string.IsNullOrEmpty(r.Doing) ? "" : " (" + r.Doing + ")"));
            if (!rows.Any(r => r.Hours <= within)) lines.Add("- Nobody of yours could be there within " + Words.N(within) + " hours.");
            foreach (double h in new[] { 12.0, 24, 48, 72 })
            {
                var by = rows.Where(r => r.Hours <= h).ToList();
                lines.Add("- Gathered within " + Words.N(h) + " hours: " + by.Count + " lords and armies, " + Words.N(by.Sum(r => r.Men)) + " men, strength " + Words.N(by.Sum(r => r.Str)) + ".");
            }
            if (noWay.Count > 0) lines.Add("- No way over land to it: " + string.Join(", ", noWay) + ".");
            lines.Add("- All your lords and armies in the field: " + (rows.Count + noWay.Count) + ", strength " + Words.N(rows.Sum(r => r.Str)) + ". Lords marching alone can be caught on the way: gather where the enemy is not, or march them as one army.");
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = MusterName, place = at, lines });
            return string.Join("\n", lines);
        }

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
            /// <summary>Where it stands and its pace (0.20.4: a lord of ours outside a place under storm comes as a relief).</summary>
            public double X, Y, Pace;
            /// <summary>Its leader's captain bonus in its strength (war room 0.20.10): under a holding's lead the troops fight without it.</summary>
            public float Captain;
            public string PartyId;
            /// <summary>Its strength without its own leader's captain bonus, as when it fights under a holding.</summary>
            public double UnderPlace => Captain > 0 ? Strength / (1 + Captain) : Strength;
            /// <summary>Its line in the view file (war room 0.22.0: its order's march, the place it is in, its road hours).</summary>
            public JsonObject View;
        }

        /// <summary>
        /// The advantage of an enemy leader the realm cannot see (war room 0.20.10, Nov 25): 1 + 0.001 x the median Tactics of the realm's own lords.
        /// Was 1, none, as if every enemy army were led by nobody: in run 20261125-1457-C2's two lost battles Dol Guldur's army fought at 1.33.
        /// </summary>
        private static float UnseenEdge(JsonObject view)
        {
            List<float> t = (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(o => F(o["tactics"])).Where(x => x > 0).OrderBy(x => x).ToList();
            return t.Count == 0 ? 1f : 1f + 0.001f * (float)t[t.Count / 2];
        }

        public string Odds(JsonObject args)
        {
            JsonObject view = ReadView();
            if (view == null) return "Your realm's view is not ready yet; ask again in a moment.";
            double now = Num(view["hours"]);
            RecordEdge(EdgeFile, view);
            List<string> oursQ = (args["ours"] as JsonArray)?.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            List<string> theirsQ = (args["theirs"] as JsonArray)?.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            string atQ = Str(args["at"])?.Trim().Trim('[', ']');
            string howQ = Str(args["how"])?.Trim().ToLowerInvariant();
            bool strike = howQ == "strike" || howQ == "strike_camp";
            // war room 0.20.7 (Nov 25): every force named on either side, so the lords seen marching to the place never count it a second time (run 20261125-0020-C2:
            // Lorgath's army, named as the besieger, was added again as a joiner, "22,640 of the besiegers' 11,478 still standing")
            var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var own = (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var enemies = (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Concat((view["last_seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()).ToList();
            var lines = new List<string>();
            var ours = new List<Force>();
            bool human = false;
            Force OwnForce(JsonObject o)
            {
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                double str = F(o["army_strength"]) > 0 ? F(o["army_strength"]) : F(o["strength"]);
                return new Force { Id = Str(o["lord"]) ?? Str(o["id"]), Name = Str(o["name"]), Men = men, Strength = str, Morale = o["morale"] is JsonValue ? F(o["morale"]) : -1, Tactics = F(o["tactics"]),
                                   X = F(o["x"]), Y = F(o["y"]), Pace = F(o["pace"]), Captain = F(o["captain"]), PartyId = Str(o["id"]), View = o };
            }
            foreach (string q in oursQ)
            {
                string id = q.Trim().Trim('[', ']');
                JsonObject o = own.FirstOrDefault(x => Eq(Str(x["id"]), id) || Eq(Str(x["lord"]), id));
                if (o == null)
                {
                    // war room 0.22.0 (item 30): a lord riding in an army is counted with it; a prisoner or a lord with no party is said so
                    string why = NotInField(id, own, out JsonObject armyOf);
                    if (armyOf != null && !ours.Any(f => Eq(f.PartyId, Str(armyOf["id"])))) { o = armyOf; lines.Add("(" + why + ".)"); }
                    else { lines.Add("(" + (why ?? q + ": no lord or army of yours by that id") + ".)"); continue; }
                    if (Str(o["id"]) != null && counted.Contains(Str(o["id"]))) continue;
                }
                ours.Add(OwnForce(o));
                if (Str(o["id"]) != null) counted.Add(Str(o["id"]));
                if (Str(o["lord"]) != null) counted.Add(Str(o["lord"]));
                if (InPerson(o)) human = true;
            }
            var theirs = new List<Force>();
            foreach (string q in theirsQ)
            {
                string id = q.Trim().Trim('[', ']');
                JsonObject o = enemies.FirstOrDefault(x => Eq(Str(x["id"]), id) || Eq(Str(x["lord"]), id));
                if (o == null) { lines.Add("(" + q + ": your realm has seen no enemy by that id.)"); continue; }
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                theirs.Add(new Force { Id = Str(o["lord"]) ?? Str(o["id"]), Name = Str(o["name"]), Men = men, Strength = F(o["strength"]), X = F(o["x"]), Y = F(o["y"]), PartyId = Str(o["id"]), View = o });
                if (Str(o["id"]) != null) counted.Add(Str(o["id"]));
                if (Str(o["lord"]) != null) counted.Add(Str(o["lord"]));
                if (InPerson(o)) human = true;
            }
            if (ours.Count == 0) return string.Join("\n", lines.Prepend("Name at least one of your lords or armies in ours."));
            // a storm: an enemy place your lords would storm, or one of yours their lords would storm
            JsonObject enemyPlace = atQ == null ? null : (view["frontier"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(h => Eq(Str(h["id"]), atQ));
            JsonObject ownPlace = atQ == null ? null : (view["holdings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(h => Eq(Str(h["id"]), atQ));
            if (atQ != null && enemyPlace == null && ownPlace == null) lines.Add("(" + atQ + ": not a castle or town of yours, nor an enemy one near your lands; reckoned as a battle in the open.)");
            float unseenEdge = UnseenEdge(view);
            // war room 0.20.10: the enemy's side gets its unseen leader's edge (UnseenEdge), and its morale is run at full and lower (GameBattle.ByUnseenMorale)
            BattleSide Side(List<Force> fs, bool known, Force lead = null)
            {
                double men = fs.Sum(f => f.Men), str = fs.Sum(f => f.Strength);
                double morale = known ? fs.Sum(f => (f.Morale >= 0 ? f.Morale : 50) * f.Men) / Math.Max(1, men) : 100;
                lead = lead ?? fs.OrderByDescending(f => f.Men).FirstOrDefault();
                return new BattleSide { Men = men, Power = men > 0 ? (float)(str / men) : 1f, Advantage = known ? 1f + (lead != null ? 0.001f * lead.Tactics : 0f) : unseenEdge, Morale = (float)morale };
            }
            // war room 0.20.10 (Nov 25): a battle the realm's lords are already in, or one under way at the place asked about, is run on from now, as measured
            // (run 20261125-1457-C2: two days into the battle at Lasgalor the question re-reckoned a fresh one from the named lords and kept saying they would win)
            // war room 0.22.0 (item 18): a question about a castle or town with nothing under way there is about that storm, not a named lord's fight elsewhere
            bool placeAsked = enemyPlace != null || ownPlace != null;
            JsonObject under = UnderWay(view, ours, theirs, atQ, placeAsked);
            if (under != null) return UnderWayOdds(view, under, ours, theirs, now, unseenEdge, lines, human, oursQ, theirsQ, atQ);
            if (placeAsked) foreach (string other in OtherFights(view, ours)) lines.Add("(" + other + ")");
            string Names(List<Force> fs) => fs.Count == 0 ? "none" : string.Join(", ", fs.Select(f => f.Name + " [" + f.Id + "]"));
            BattleSide mine = Side(ours, true);
            if (strike && (ownPlace == null || theirs.Count == 0))
                lines.Add("(how = strike needs at = one of your castles or towns and the besiegers in theirs; reckoned as asked otherwise.)");
            string theirRealm = RealmOfForces(enemies, theirs.Select(f => f.PartyId ?? f.Id));
            if (strike && ownPlace != null && theirs.Count > 0)
            {
                // war room 0.20.7: your named lords, gathered, attack the siege camp together: a battle outside the walls by the field rule (the game's SiegeOutside),
                // the garrison and your lords inside coming out beside them once they and yours are more than 1.5 times the camp (the game's sally beside a relief)
                double px = F(ownPlace["x"]), py = F(ownPlace["y"]);
                var inside = ours.Where(f => Math.Sqrt((f.X - px) * (f.X - px) + (f.Y - py) * (f.Y - py)) <= 1.5).ToList();
                var strikers = ours.Except(inside).ToList();
                if (strikers.Count == 0) return string.Join("\n", lines.Append("Name at least one lord of yours outside " + Str(ownPlace["name"]) + " to strike its siege camp."));
                double gMen = Math.Max(0, I(ownPlace["garrison"])) + Math.Max(0, I(ownPlace["militia"])) + inside.Sum(f => f.Men);
                double gStr = Math.Max(0, F(ownPlace["defence"])) + inside.Sum(f => f.UnderPlace);
                double campStr = theirs.Sum(f => f.Strength), strikeStr = strikers.Sum(f => f.Strength);
                bool sally = strikeStr + gStr > 1.5 * campStr;
                var side = new List<Force>(strikers);
                if (sally && gMen > 0) side.Add(new Force { Id = atQ, Name = "the garrison" + (inside.Count > 0 ? " and " + Names(inside) : ""), Men = gMen, Strength = gStr, Morale = 50 });
                // war room 0.20.10: the strike is led by the first of yours to reach the camp (the game's side leader is the party that begins the battle), not the biggest
                Force first = strikers.OrderBy(f => Math.Sqrt((f.X - px) * (f.X - px) + (f.Y - py) * (f.Y - py)) / (f.Pace > 0 ? f.Pace : 2.8)).First();
                BattleSide us = Side(side, true, first);
                BattleSide camp = Side(theirs, false);
                // enemies seen marching to the place join the camp as they arrive; others of the realm's side (allies) join yours
                List<BattleJoiner> sj = Joiners(view, atQ, now, false, counted, out List<string> sjNames);
                foreach (BattleJoiner j in sj) j.Attackers = !j.Attackers;
                // war room 0.20.10: a battle outside the walls (the game's SiegeOutside): no retreat for either side, unlike the open field
                BattleOutcome[] sruns = GameBattle.ByUnseenMorale("siege_outside", false, 1f, us, camp, false, sj);
                BattleOutcome so = sruns[1];
                GameBattle.StrikesPerRound(false, false, 1f, us.Men, camp.Men, out double ssA, out double ssD);
                (double usTot, double campTot, List<string> sLate) = Totals(us.Men, camp.Men, sj, sjNames, so);
                lines.Insert(0, "BATTLE ODDS: " + Names(strikers) + " strike the siege camp of " + Names(theirs) + " at your " + Str(ownPlace["name"]) + " [" + atQ + "], all together, in the open");
                lines.Add("- Yours, gathered and striking at once: " + Words.N(strikers.Sum(f => f.Men)) + " men, strength " + Words.N(strikeStr) + "; the camp: " + Words.N(camp.Men) + " men, strength " + Words.N(campStr) + ".");
                lines.Add(sally ? "- The garrison" + (inside.Count > 0 ? " and your lords inside" : "") + " (" + Words.N(gMen) + " men, strength " + Words.N(gStr) + ") come out to join you: you and they are more than 1.5 times the camp (the game's rule)."
                                : "- The garrison" + (inside.Count > 0 ? " and your lords inside" : "") + " (" + Words.N(gMen) + " men, strength " + Words.N(gStr) + ") stay inside: you and they are not more than 1.5 times the camp (the game's rule).");
                // war room 0.22.0 (item 28): the siege's own clock, as the place's page shows it
                string sclock = SiegeClockOf(atQ);
                if (sclock != null) lines.Add("- The siege's own clock, as your place's page shows it: " + sclock + ". A strike must land before the camp storms; once it storms, each lord who comes joins the defenders as he arrives.");
                if (sjNames.Count > 0) lines.Add("- Also marching there, joining as they arrive: " + string.Join(", ", sjNames) + ".");
                lines.Add("- In the open: two rounds an hour, each side's blows growing with its own men: yours " + ssA + " a round, the camp's " + ssD + ". At first you lose about "
                          + Words.N(so.AttackerLossPerHour) + " men an hour, the camp about " + Words.N(so.DefenderLossPerHour) + ".");
                lines.Add("- Led by " + first.Name + ", the first of yours to reach the camp (a side's leader is the party that begins its battle; his Tactics give your side its edge). The camp's leader is taken to be as skilled as your lords.");
                lines.Add("- Run ahead: " + Outcome(so, sruns, "you", "the besiegers", "your", "the besiegers'", usTot, campTot) + ". Nobody can leave a battle once in it, and in a battle outside the walls neither side can retreat: it ends when one side is down or breaks.");
                if (sLate.Count > 0) lines.Add("- Arriving after it ends, so not in these numbers: " + string.Join("; ", sLate) + ".");
                if (Impossible(so, usTot, campTot)) lines.Add("- (These numbers do not add up: a side loses more men than it had in the fight, or keeps more than came. Do not rely on this forecast.)");
                string sEdge = EdgeLine(theirRealm, now, hit => LossShare("siege_outside", false, 1f, us, Hit(camp, hit), false, sj, true, usTot));
                if (sEdge != null) lines.Add(sEdge);
                if (so.Winner == "attackers") lines.Add(TakingsLine(view, camp.Men - so.DefendersLeft));
                lines.Add("- This reckons your lords already gathered and striking together while the camp is not storming. Once the camp storms the place, a strike together cannot happen: "
                          + "each lord who comes joins the defenders under the castle as he arrives, with no leader's edge and none of his own captain's bonus (ask again then: it reckons that). "
                          + "Lords marching one by one arrive and fight one by one: gather them first (the muster question says who can be where by when).");
                if (human) lines.Add(HumanNote);
                Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = OddsName, how = "strike", ours = oursQ, theirs = theirsQ, place = atQ, lines });
                return string.Join("\n", lines);
            }
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
                // war room 0.20.4: only your lords inside the place defend behind its walls; one outside comes as a relief, and the first to join the storm turns it
                // into a battle outside the walls (the game's MapEvent.AddInvolvedParty), where the walls count for nobody (Nov 24: Gaín's relief of Goblin Town)
                var inside = new List<Force>();
                var outside = new List<(Force F, double At, bool ByRoad)>();
                if (!weStorm)
                {
                    double px = F(place["x"]), py = F(place["y"]);
                    foreach (Force f in ours)
                    {
                        double d = Math.Sqrt((f.X - px) * (f.X - px) + (f.Y - py) * (f.Y - py));
                        if (d <= 1.5) { inside.Add(f); continue; }
                        // war room 0.22.0 (item 5): a relief's hours by road at its pace
                        double h;
                        bool byRoad = false;
                        if (f.View != null) h = OwnRoadHours(_root, view, f.View, (float)px, (float)py, double.NaN, out byRoad);
                        else h = d / (f.Pace > 0 ? f.Pace : 2.8);
                        if (double.IsNaN(h)) { lines.Add("(" + f.Name + " has no way over land to " + Str(place["name"]) + ": left out.)"); continue; }
                        outside.Add((f, h, byRoad));
                    }
                    defMen += inside.Sum(f => f.Men);
                    defStr += inside.Sum(f => f.UnderPlace);
                }
                BattleSide besiegers = weStorm ? mine : Side(theirs, false);
                var walls = new BattleSide { Men = defMen, Power = defMen > 0 ? (float)(defStr / defMen) : 1f, Advantage = 1f, Morale = weStorm ? 50 : mine.Morale, Walls = true, LedByPlace = true };
                int level = Math.Max(1, I(place["wall_level"]));
                float worth = (float)((4.0 + level) / (1 + 0.25 + 0.16));   // the walls' worth once the first engine, a ram, is built
                besiegers.Advantage -= 0.1f;                               // the game's siege attackers' -0.1
                bool town = Str(place["kind"]) == "town";
                List<BattleJoiner> js = Joiners(view, atQ, now, weStorm, counted, out List<string> jsNames);
                // war room 0.20.10: lords who come to relieve it fight under the place: their own captain's bonus does not count, and they bring their own morale
                foreach ((Force f, double at, bool _) in outside) { js.Add(new BattleJoiner { Attackers = false, Men = f.Men, At = at, Power = f.Men > 0 ? (float)(f.UnderPlace / f.Men) : 0, Morale = f.Morale }); jsNames.Add(f.Name + " [" + f.Id + "] (in about " + Words.N(Math.Max(1, Math.Round(at))) + " hours)"); }
                BattleOutcome[] runs = GameBattle.ByUnseenMorale("siege", town, worth, besiegers, walls, !weStorm, js);
                BattleOutcome o = runs[1];
                (double aTot, double dTot, List<string> late) = Totals(besiegers.Men, walls.Men, js, jsNames, o);
                double prep = 24.0 * 48 / Math.Sqrt(Math.Max(1, besiegers.Men)), ram = 24.0 * 12 / Math.Sqrt(Math.Max(1, besiegers.Men));
                GameBattle.StrikesPerRound(true, town, worth, besiegers.Men, walls.Men, out double sA, out double sD);
                lines.Insert(0, "BATTLE ODDS: " + (weStorm ? Names(ours) + " storming " : Names(theirs) + " storming your ") + Str(place["name"]) + " [" + atQ + "]");
                lines.Add("- Besiegers: " + Words.N(besiegers.Men) + " men, strength " + Words.N(besiegers.Men * besiegers.Power) + "; defenders: " + Words.N(walls.Men) + " (garrison " + Math.Max(0, garrison)
                          + ", militia " + Math.Max(0, militia) + (weStorm || inside.Count == 0 ? "" : ", and your lords inside: " + Names(inside)) + "), strength " + Words.N(walls.Men * walls.Power) + ", behind walls of level " + level + ".");
                if (outside.Count > 0)
                    lines.Add("- Outside the walls: " + Names(outside.Select(x => x.F).ToList()) + ". They come as a relief, the first in about " + Words.N(Math.Max(1, Math.Round(outside.Min(x => x.At))))
                              + " hours (" + (outside.All(x => x.ByRoad) ? "by the way the game would march, at each one's pace" : "in a straight line at its pace; by road it is longer") + "). Only lords already inside defend behind the walls.");
                // war room 0.22.0 (item 28): a siege under way by its own clock (the place's page), not as if the camp had just arrived
                string clock = SiegeClockOf(atQ);
                if (clock != null)
                    lines.Add("- The siege's own clock, as " + (weStorm ? "the place's" : "your place's") + " page shows it: " + clock + ". The run ahead below begins when the storm does.");
                else
                    lines.Add("- First the camp must finish its preparations, about " + Words.N(prep) + " hours for " + Words.N(besiegers.Men) + " men (48 man-days at the square root of its men a day; "
                              + "an engineer's skill shortens it), then build an engine, a ram about " + Words.N(ram) + " hours more: no storm can come before about " + Words.N(prep + ram) + " hours.");
                lines.Add("- A storm is one round an hour. The besiegers' blows grow only with the defenders' number: " + sA + " a round now; the defenders strike " + sD + " a round with the walls' worth "
                          + worth.ToString("0.0", CultureInfo.InvariantCulture) + " (once a ram is built; each further engine lowers it). At first the besiegers lose about " + Words.N(o.AttackerLossPerHour)
                          + " men an hour, the defenders about " + Words.N(o.DefenderLossPerHour) + ".");
                if (!double.IsNaN(o.OutsideAt))
                    lines.Add("- About " + Words.N(Math.Max(1, Math.Round(o.OutsideAt))) + " hours in, the first relief joins the defenders from outside: from then the storm is a battle outside the walls, "
                              + "where the walls no longer count for anyone, the garrison included, and both sides strike two rounds an hour by the field rule.");
                var marching = jsNames.Take(jsNames.Count - outside.Count).ToList();
                if (marching.Count > 0) lines.Add("- Also marching there, joining their side as they arrive: " + string.Join(", ", marching) + ".");
                if (!weStorm && (inside.Count > 0 || outside.Count > 0))
                    lines.Add("- The place leads its defenders, before and after a relief joins: your lords inside and those who come fight under it, with no leader's edge (their side's advantage is 1) and none of their own captain's bonus. The besiegers' leader is taken to be as skilled as your lords.");
                lines.Add("- Run ahead: " + Outcome(o, runs, "the besiegers", "the defenders", "the besiegers'", "the defenders'", aTot, dTot) + ". Nobody can leave a battle once in it.");
                if (late.Count > 0) lines.Add("- Arriving after it ends, so not in these numbers: " + string.Join("; ", late) + ".");
                if (Impossible(o, aTot, dTot)) lines.Add("- (These numbers do not add up: a side loses more men than it had in the fight, or keeps more than came. Do not rely on this forecast.)");
                // war room 0.22.0 (item 9): the enemy realm's measured edge, once the realm has fought it enough
                string edgeRealm = weStorm ? Str(place["faction_name"]) ?? theirRealm : theirRealm;
                string edge = weStorm ? EdgeLine(edgeRealm, now, hit => LossShare("siege", town, worth, besiegers, Hit(walls, hit), false, js, true, aTot))
                                      : EdgeLine(edgeRealm, now, hit => LossShare("siege", town, worth, Hit(besiegers, hit), walls, true, js, false, dTot));
                if (edge != null) lines.Add(edge);
                if (!weStorm && theirs.Count > 0 && ours.Count > 0)
                    lines.Add("- This reckons their storm, with your lords outside arriving one by one. To reckon your lords gathered and striking the camp together, ask again with how = strike.");
                // what a win would bring in (0.9.4): storming, the defenders who fall; defending, the besiegers who fall
                if (weStorm && o.Winner == "attackers") lines.Add(TakingsLine(view, walls.Men - o.DefendersLeft));
                else if (!weStorm && o.Winner == "defenders") lines.Add(TakingsLine(view, besiegers.Men - o.AttackersLeft));
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
                          + Words.N(o.AttackerLossPerHour) + " men an hour, they about " + Words.N(o.DefenderLossPerHour) + ". Their leader is taken to be as skilled as yours, their morale at full.");
                lines.Add("- Run ahead: " + Outcome(o, runs, "you", "they", "your", "their", mine.Men, enemy.Men) + ". Nobody can leave a battle once in it, but a side whose morale falls to 20 after losing four rounds running may retreat.");
                string edge = EdgeLine(theirRealm, now, hit => LossShare("field", false, 1f, mine, Hit(enemy, hit), false, null, true, mine.Men));
                if (edge != null) lines.Add(edge);
                if (o.Winner == "attackers") lines.Add(TakingsLine(view, enemy.Men - o.DefendersLeft));
            }
            // 0.17.3: a battle of the human player's is fought on the spot; 0.19.3: he is worth about a quarter more than his strength (the bar, his record moves it)
            if (human) lines.Add(HumanNote);
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = OddsName, ours = oursQ, theirs = theirsQ, place = atQ, lines });
            return string.Join("\n", lines);
        }

        /// <summary>A side's copy with its blows landing at a multiple of the rules' (item 9).</summary>
        private static BattleSide Hit(BattleSide s, double hit) { BattleSide c = Copy(s); c.Hit = hit; return c; }

        /// <summary>The share of the realm's men lost in a battle run at the other side's full morale (the headline), the realm's side being the attackers or not.</summary>
        private static double LossShare(string kind, bool town, float walls, BattleSide att, BattleSide def, bool attackersUnseen, List<BattleJoiner> js, bool weAttack, double ourTotal)
        {
            BattleOutcome o = GameBattle.ByUnseenMorale(kind, town, walls, att, def, attackersUnseen, js)[1];
            double left = weAttack ? o.AttackersLeft : o.DefendersLeft;
            double end = double.IsNaN(o.Hours) ? 24 * 5 : o.Hours;
            double tot = (weAttack ? att.Men : def.Men) + (js ?? new List<BattleJoiner>()).Where(j => j.Attackers == weAttack && j.At <= end).Sum(j => j.Men);
            return tot > 0 ? Math.Max(0, Math.Min(1, (tot - left) / tot)) : double.NaN;
        }

        /// <summary>
        /// The men each side brings to a battle that ends when it ends (war room 0.22.0, item 23): its own and its joiners who arrive before the end; those due after it, named apart
        /// ("They win ... all 4,137 of your men are down" had counted lords due after the end as fallen).
        /// </summary>
        private static (double aTot, double dTot, List<string> late) Totals(double aMen, double dMen, List<BattleJoiner> js, List<string> names, BattleOutcome o)
        {
            double end = double.IsNaN(o.Hours) ? 24 * 5 : o.Hours;
            double aTot = aMen, dTot = dMen;
            var late = new List<string>();
            for (int i = 0; i < (js?.Count ?? 0); i++)
            {
                BattleJoiner j = js[i];
                if (j.At <= end + 1e-6) { if (j.Attackers) aTot += j.Men; else dTot += j.Men; }
                else late.Add((names != null && i < names.Count ? names[i] : Words.N(j.Men) + " men") + ", due in about " + Words.N(Math.Round(j.At)) + " hours after it begins, arrives after it ends");
            }
            return (aTot, dTot, late);
        }

        /// <summary>A forecast whose numbers cannot be: a side with more left than came, or that lost more than it had in the fight (item 23).</summary>
        private static bool Impossible(BattleOutcome o, double aTot, double dTot) =>
            o.AttackersLeft > aTot + 0.5 || o.DefendersLeft > dTot + 0.5 || aTot - o.AttackersLeft > aTot + 0.5 || dTot - o.DefendersLeft > dTot + 0.5;

        /// <summary>The fights the named lords of the realm are in elsewhere (item 18): one line each, to ask about by naming it.</summary>
        private static List<string> OtherFights(JsonObject view, List<Force> ours)
        {
            var said = new List<string>();
            foreach (JsonObject b in (view["battles"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var parties = ((b["us"] as JsonObject)?["parties"] as JsonArray ?? new JsonArray()).Select(Str).Where(p => p != null).ToList();
                foreach (Force f in ours.Where(f => (f.PartyId != null && parties.Any(p => Eq(p, f.PartyId))) || parties.Any(p => Eq(p, f.Id))))
                    said.Add(f.Name + " is also fighting " + (Str(b["place_name"]) != null ? "at " + Str(b["place_name"]) : "in a battle against " + (Str((b["them"] as JsonObject)?["led_by"]) ?? "an enemy"))
                             + "; ask about it by naming " + (Str(b["place"]) != null ? "it in at" : "the enemy in theirs") + ".");
            }
            return said.Distinct().ToList();
        }

        /// <summary>
        /// The realm's battle under way (view "battles", module 0.24.7) that the question is about; null when none: the one at the place asked about; else, with enemies
        /// named, one they fight in (war room 0.21.1: a named lord of yours in a skirmish elsewhere no longer takes the question over; in run 20261125-2341-C2 six of the
        /// agent's twelve questions about Lorgath's army, each naming 30 to 45 lords, came back as forecasts of small fights with raiders); else one a named lord is in.
        /// War room 0.22.0 (item 18): a question about a castle or town with nothing under way there takes no other battle (a storm question about Lasgalor came back as the
        /// Torech Emel fight twice).
        /// </summary>
        private static JsonObject UnderWay(JsonObject view, List<Force> ours, List<Force> theirs, string atQ, bool placeAsked)
        {
            var battles = (view["battles"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            List<string> Parties(JsonObject b, string side) => ((b[side] as JsonObject)?["parties"] as JsonArray ?? new JsonArray()).Select(Str).Where(p => p != null).ToList();
            bool In(List<string> parties, Force f) => (f.PartyId != null && parties.Any(p => Eq(p, f.PartyId))) || parties.Any(p => Eq(p, f.Id));
            if (atQ != null)
            {
                JsonObject there = battles.FirstOrDefault(b => Eq(Str(b["place"]), atQ));
                if (there != null) return there;
                if (placeAsked) return null;
            }
            if (theirs != null && theirs.Count > 0)
                return battles.FirstOrDefault(b => theirs.Any(f => In(Parties(b, "them"), f) || Eq(Str((b["them"] as JsonObject)?["led_by"]), f.Name)));
            return battles.FirstOrDefault(b => ours.Any(f => In(Parties(b, "us"), f)));
        }

        /// <summary>
        /// A battle under way run on from now (war room 0.20.10): each side's men standing and mean power in the battle, your side's advantage and morale as they are,
        /// the other side's leader taken as skilled as your lords and its morale at full; each side's blows scaled by what they knocked down over the last hours against
        /// the rules' expectation (the module's measure, Forecast.Measured).
        /// War room 0.22.0 (item 4: at Imnagath, day 100, naming 36 lords turned "they win in about 39 hours" into "you win in about 69 hours", and the relief broke off on the
        /// engage order's own strength check, which counted the enemy joiners the forecast left out): first the battle as it stands; then with the joiners: every enemy the realm
        /// sees marching to it, and each enemy named who is not in it yet, by road from where the realm saw him; each lord of the realm named, by road, and, when his order does
        /// not take him there yet, only after the usual order delay ("if ordered now"); a lord who cannot come (in another fight, shut in a besieged place) named and left out; and
        /// when a lord of the realm would find the enemy with its joiners more than 1.5 times his side as he arrives, so that an order's default caution would turn him back.
        /// </summary>
        private string UnderWayOdds(JsonObject view, JsonObject b, List<Force> ours, List<Force> theirsNamed, double now, float unseenEdge, List<string> lines, bool human, List<string> oursQ, List<string> theirsQ, string atQ)
        {
            JsonObject us = b["us"] as JsonObject ?? new JsonObject(), them = b["them"] as JsonObject ?? new JsonObject(), m = b["measured"] as JsonObject;
            bool weAttack = Str(b["ours"]) == "attackers";
            string kind = Str(b["kind"]) ?? "field";
            bool storm = kind == "siege", ledByPlace = us["settlement_led"] is JsonValue && (bool)us["settlement_led"];
            string place = Str(b["place_name"]);
            if (place == null && atQ != null && kind != "field")
                place = (view["holdings"] as JsonArray ?? new JsonArray()).Concat(view["frontier"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(h => Eq(Str(h["id"]), atQ)).Select(h => Str(h["name"])).FirstOrDefault();
            var parties = (us["parties"] as JsonArray ?? new JsonArray()).Select(Str).Where(p => p != null).ToList();
            var themParties = (them["parties"] as JsonArray ?? new JsonArray()).Select(Str).Where(p => p != null).ToList();
            var inIt = ours.Where(f => (f.PartyId != null && parties.Any(p => Eq(p, f.PartyId))) || parties.Any(p => Eq(p, f.Id))).ToList();
            var coming = ours.Except(inIt).ToList();
            float bx = F(b["x"]), by = F(b["y"]);
            var mine = new BattleSide { Men = I(us["healthy"]), Power = Math.Max(0.01f, (float)F(us["power"])), Advantage = Math.Max(0.1f, (float)F(us["advantage"])), Morale = (float)F(us["morale"]), Walls = !weAttack && ledByPlace && storm, LedByPlace = ledByPlace };
            var theirs = new BattleSide { Men = I(them["healthy"]), Power = Math.Max(0.01f, (float)F(them["power"])), Advantage = unseenEdge - (storm && !weAttack ? 0.1f : 0f), Morale = 100, LedByPlace = them["settlement_led"] is JsonValue && (bool)them["settlement_led"] };
            if (theirs.LedByPlace) { theirs.Advantage = 1f; theirs.Walls = storm && weAttack; }
            // measured with the advantage the battle shows their side (the measure's own base), so its rates carry over; never shown to the agent
            else if (m != null && F(them["advantage"]) > 0) theirs.Advantage = (float)F(them["advantage"]);
            double hitUs = m != null ? F(m["hit_us"]) : 1, hitThem = m != null ? F(m["hit_them"]) : 1;
            if (hitUs > 0) mine.Hit = hitUs;
            if (hitThem > 0) theirs.Hit = hitThem;
            double lag = LagHours?.Invoke() ?? double.NaN;
            var battles = (view["battles"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var holdings = (view["holdings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var js = new List<BattleJoiner>();
            var jn = new List<string>();
            var jStr = new List<double>();
            var leftOut = new List<string>();
            bool allByRoad = true;
            // your named lords not in it: by road, after the order delay unless their order takes them there already; those who cannot come, left out
            foreach (Force f in coming)
            {
                JsonObject other = battles.FirstOrDefault(ob => !ReferenceEquals(ob, b) && (((ob["us"] as JsonObject)?["parties"] as JsonArray ?? new JsonArray()).Select(Str).Any(p => Eq(p, f.PartyId) || Eq(p, f.Id))));
                if (other != null) { leftOut.Add(f.Name + " is fighting " + (Str(other["place_name"]) != null ? "at " + Str(other["place_name"]) : "another battle")); continue; }
                string inPlace = Str(f.View?["in_settlement"]);
                JsonObject shut = inPlace == null ? null : holdings.FirstOrDefault(h => Eq(Str(h["id"]), inPlace) && (Str(h["status"]) ?? "").IndexOf("besieged", StringComparison.OrdinalIgnoreCase) >= 0);
                if (shut != null && !Eq(inPlace, Str(b["place"]))) { leftOut.Add(f.Name + " is shut in besieged " + Str(shut["name"])); continue; }
                double h;
                bool byRoad = false;
                if (f.View != null) h = OwnRoadHours(_root, view, f.View, bx, by, double.NaN, out byRoad);
                else h = Math.Sqrt((f.X - bx) * (f.X - bx) + (f.Y - by) * (f.Y - by)) / (f.Pace > 0 ? f.Pace : 2.8);
                if (double.IsNaN(h)) { leftOut.Add(f.Name + " has no way over land to it"); continue; }
                allByRoad &= byRoad;
                // his order takes him there: the point his march aims at is the battle's, or his doings name the place
                bool onWay = f.View != null && ((F(f.View["tx"]) != 0 || F(f.View["ty"]) != 0) && Math.Sqrt((F(f.View["tx"]) - bx) * (F(f.View["tx"]) - bx) + (F(f.View["ty"]) - by) * (F(f.View["ty"]) - by)) <= 3
                                                || place != null && (Str(f.View["doing"]) ?? "").IndexOf(place, StringComparison.OrdinalIgnoreCase) >= 0);
                double at = h + (onWay || double.IsNaN(lag) ? 0 : lag);
                double str = ledByPlace ? f.UnderPlace : f.Strength;
                js.Add(new BattleJoiner { Attackers = weAttack, Men = f.Men, At = at, Power = f.Men > 0 ? (float)(str / f.Men) : 0, Morale = f.Morale });
                jn.Add(f.Name + " [" + f.Id + "]" + (onWay ? " (on his way)" : double.IsNaN(lag) ? " (if he marches now)" : " (if ordered now: about " + Words.N(Math.Round(lag)) + " hours for your order to land, then the march)"));
                jStr.Add(str);
            }
            int friendly = js.Count;
            // the enemies who would join: seen marching to it, and the ones named who are not in it, from where the realm saw them (only what the realm has seen)
            var counted = new HashSet<string>(themParties, StringComparer.OrdinalIgnoreCase);
            var seenAll = (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            foreach (JsonObject s in seenAll)
            {
                if (Str(s["relation"]) != "enemy" || counted.Contains(Str(s["id"]) ?? "") || counted.Contains(Str(s["lord"]) ?? "")) continue;
                string toward = Str(s["toward"]);
                if (toward == null || !(Eq(toward, Str(b["place"])) || themParties.Any(p => Eq(p, toward)) || parties.Any(p => Eq(p, toward)))) continue;
                double at = s["arrives_at"] is JsonValue av && av.TryGetValue(out double ad) ? Math.Max(0, ad - now) : EnemyRoadHours(view, s, bx, by, double.NaN, out _);
                if (double.IsNaN(at) || at > 24 * 5) continue;
                double men = I(s["army_men"]) > 0 ? I(s["army_men"]) : I(s["men"]);
                js.Add(new BattleJoiner { Attackers = !weAttack, Men = men, At = at, Power = men > 0 ? F(s["strength"]) / (float)men : 0 });
                jn.Add(Str(s["name"]) + " [" + (Str(s["lord"]) ?? Str(s["id"])) + "] (seen marching to it)");
                jStr.Add(F(s["strength"]));
                counted.Add(Str(s["id"]) ?? "");
            }
            foreach (Force f in theirsNamed)
            {
                if (counted.Contains(f.PartyId ?? "") || counted.Contains(f.Id ?? "") || themParties.Any(p => Eq(p, f.PartyId) || Eq(p, f.Id))) continue;
                double at = f.View != null ? EnemyRoadHours(view, f.View, bx, by, double.NaN, out _) : double.NaN;
                if (double.IsNaN(at)) { leftOut.Add(f.Name + " (theirs) has no way your realm knows to it"); continue; }
                js.Add(new BattleJoiner { Attackers = !weAttack, Men = f.Men, At = at, Power = f.Men > 0 ? (float)(f.Strength / f.Men) : 0 });
                jn.Add(f.Name + " [" + f.Id + "] (named, from where your realm saw it)");
                jStr.Add(f.Strength);
                counted.Add(f.PartyId ?? f.Id ?? "");
            }
            BattleSide att = weAttack ? mine : theirs, def = weAttack ? theirs : mine;
            bool town = b["town"] is JsonValue && b["town"].ToString() == "true";
            float worth = Math.Max(1f, (float)F(b["walls_worth"]));
            // the battle as it stands, without anyone joining
            BattleOutcome bare = m != null ? GameBattle.Run(kind, town, worth, Copy(att), Copy(def)) : GameBattle.ByUnseenMorale(kind, town, worth, att, def, !weAttack)[1];
            BattleOutcome[] runs = GameBattle.ByUnseenMorale(kind, town, worth, att, def, !weAttack, js);
            BattleOutcome o = m != null ? GameBattle.Run(kind, town, worth, Copy(att), Copy(def), js) : runs[1];
            (double aTot, double dTot, List<string> late) = Totals(att.Men, def.Men, js, jn, o);
            string kindWords = kind == "siege" ? "a storm of the walls" : kind == "siege_outside" ? "a battle outside the walls" : kind == "sally" ? "a sally" : kind == "raid" ? "a raid" : "a battle in the open";
            string pid = Str(b["place"]) ?? (place != null ? atQ : null);
            lines.Insert(0, "BATTLE ODDS: a battle already under way" + (place != null ? " at " + place + (pid != null ? " [" + pid + "]" : "") : "") + ", " + kindWords + ", "
                            + Words.N(Math.Max(0, F(b["hours_in"]))) + " hours in" + (inIt.Count > 0 ? "; in it: " + string.Join(", ", inIt.Select(f => f.Name + " [" + f.Id + "]")) : ""));
            lines.Add("- Your side is led by " + (Str(us["led_by"]) ?? "?") + (ledByPlace ? " (a holding: no leader's edge, and your lords in it fight without their own captain's bonus; a side led by a holding never breaks)" : "")
                      + "; theirs by " + (Str(them["led_by"]) ?? "?") + ".");
            lines.Add("- Standing now: yours " + Words.N(mine.Men) + " men (strength in this battle about " + Words.N(mine.Men * mine.Power) + ", morale " + Words.N(mine.Morale) + "), " + Words.N(I(us["casualties"])) + " fallen so far; theirs "
                      + Words.N(theirs.Men) + " men (strength about " + Words.N(theirs.Men * theirs.Power) + "), " + Words.N(I(them["casualties"])) + " fallen so far.");
            if (m != null)
                lines.Add("- Measured over its last " + Words.N(F(m["hours"])) + " hours, your side's blows landed " + Pct(hitUs) + " and theirs " + Pct(hitThem) + " of what the game's rules expect with their leader as skilled as yours and their morale at full; the run ahead keeps those rates.");
            else
                lines.Add("- Too early to measure: run by the game's rules with their leader as skilled as yours and their morale at full or lower.");
            // as it stands, then who could join and when
            bool bareWin = bare.Winner != null && (bare.Winner == "attackers") == weAttack;
            double firstOurs = js.Take(friendly).Select(j => j.At).DefaultIfEmpty(double.NaN).Min(), firstTheirs = js.Skip(friendly).Select(j => j.At).DefaultIfEmpty(double.NaN).Min();
            lines.Add("- Without joiners: " + (bare.Winner == null ? "it does not end within 5 days" : bareWin ? "your side wins in about " + Words.N(Math.Max(1, Math.Round(bare.Hours))) + " hours" : "your side falls in about " + Words.N(Math.Max(1, Math.Round(bare.Hours))) + " hours")
                      + "; " + (double.IsNaN(firstOurs) ? "no lord of yours you named could join it" : "the first lord of yours you named arrives in about " + Words.N(Math.Max(1, Math.Round(firstOurs))) + " hours")
                      + ", " + (double.IsNaN(firstTheirs) ? "and no enemy your realm sees is marching to it." : "the first enemy joiner in about " + Words.N(Math.Max(1, Math.Round(firstTheirs))) + " hours."));
            string Arrivals(int from, int to) => string.Join("; ", Enumerable.Range(from, to - from).OrderBy(i => js[i].At).Select(i => jn[i] + " in about " + Words.N(Math.Max(1, Math.Round(js[i].At))) + " hours"));
            if (friendly > 0)
                lines.Add("- Joining your side as they arrive (" + (allByRoad ? "by the way the game would march, at each one's pace" : "in a straight line at each one's pace; by road it is longer") + "): " + Arrivals(0, friendly) + "."
                          + (storm && !weAttack ? " The first to join turns the storm into a battle outside the walls; the place still leads your side." : ""));
            if (js.Count > friendly) lines.Add("- Joining their side as they arrive: " + Arrivals(friendly, js.Count) + ".");
            if (leftOut.Count > 0) lines.Add("- Left out, since they cannot come: " + string.Join("; ", leftOut) + ".");
            // a lord of yours who would find the enemy with its joiners more than 1.5 times his side as he arrives: an order's default caution would turn him back
            double usNow = mine.Men * mine.Power, themNow = theirs.Men * theirs.Power;
            var turned = new List<string>();
            for (int i = 0; i < friendly; i++)
            {
                double t = js[i].At;
                double ourStr = usNow + Enumerable.Range(0, friendly).Where(k => js[k].At <= t + 1e-6).Sum(k => jStr[k]);
                double theirStr = themNow + Enumerable.Range(friendly, js.Count - friendly).Where(k => js[k].At <= t + 1e-6).Sum(k => jStr[k]);
                if (ourStr > 0 && theirStr > 1.5 * ourStr) turned.Add(jn[i] + " at about " + Words.N(Math.Round(t)) + " hours: the enemy there about " + Pct(theirStr / ourStr) + " of your side with him");
            }
            if (turned.Count > 0)
                lines.Add("- The enemy with its joiners would still be more than 1.5 times your side as these lords arrive, so an order with the default caution (1.5) would turn them back on the way: " + string.Join("; ", turned) + ".");
            lines.Add("- First hour: you lose about " + Words.N(weAttack ? o.AttackerLossPerHour : o.DefenderLossPerHour) + " men, they about " + Words.N(weAttack ? o.DefenderLossPerHour : o.AttackerLossPerHour) + ".");
            string outcome = Outcome(o, m != null ? new[] { o, o, o } : runs, weAttack ? "you" : "they", weAttack ? "they" : "you", weAttack ? "your" : "their", weAttack ? "their" : "your", aTot, dTot);
            lines.Add("- Run ahead from now" + (js.Count > 0 ? ", with the joiners" : "") + ": " + outcome + ". Lords already in a battle cannot leave it; lords not yet in it can still keep out.");
            if (late.Count > 0) lines.Add("- Arriving after it ends, so not in these numbers: " + string.Join("; ", late) + ".");
            if (Impossible(o, aTot, dTot)) lines.Add("- (These numbers do not add up: a side loses more men than it had in the fight, or keeps more than came. Do not rely on this forecast.)");
            if (human) lines.Add(HumanNote);
            Log(new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = OddsName, how = "under way", ours = oursQ, theirs = theirsQ, place = atQ, battle = Str(b["key"]), lines });
            return string.Join("\n", lines);
        }

        private static string Pct(double k) => Words.N(Math.Round(k * 100)) + "%";

        private static BattleSide Copy(BattleSide s) => new BattleSide { Men = s.Men, Power = s.Power, Advantage = s.Advantage, Morale = s.Morale, Walls = s.Walls, LedByPlace = s.LedByPlace, Hit = s.Hit };

        /// <summary>
        /// What a win would bring in (0.9.4): the enemy's fallen, times what each fallen man left the realm in its own won battles on
        /// record (the bridge's takings, in the view file), or why there is no reckoning yet.
        /// </summary>
        private static string TakingsLine(JsonObject view, double fallen)
        {
            JsonObject t = view["takings"] as JsonObject;
            int battles = I(t?["battles"]), enough = Math.Max(1, I(view["takings_enough"]));
            if (t == null || battles < Math.Max(3, enough) || F(t["fallen"]) <= 0)
                return "- What a win would bring in: your realm has won " + battles + (battles == 1 ? " battle" : " battles") + " on record, too few to reckon it (it takes " + Math.Max(3, enough) + ").";
            fallen = Math.Max(0, fallen);
            double loot = fallen * Num(t["loot_per_fallen"]), prisoners = fallen * Num(t["prisoners_per_fallen"]), ransom = prisoners * Num(t["ransom_per_prisoner"]);
            return "- What a win would bring in, by your realm's " + battles + " won battles on record: about " + Words.N(fallen) + " of theirs fall, leaving about " + Words.N(loot) + " in loot and "
                   + Words.N(prisoners) + " prisoners worth about " + Words.N(ransom) + " in ransom, into the winning lords' purses once sold (loot in a town, prisoners in a castle or town).";
        }

        /// <summary>
        /// The run's end in words: who wins, in how long, and what each side has left of what it brought (0.11.6: the losing
        /// side's remnant too, so a win that leaves the enemy a third of his army reads as such; and that a side which retreats
        /// gets away with its lords).
        /// </summary>
        private static string Outcome(BattleOutcome o, BattleOutcome[] runs, string a, string d, string aOf, string dOf, double aMen, double dMen)
        {
            if (o.Winner == null) return "after 5 days it has not ended (about " + Words.N(o.AttackersLeft) + " of " + aOf + " " + Words.N(aMen) + " and " + Words.N(o.DefendersLeft) + " of " + dOf + " " + Words.N(dMen) + " still standing)";
            bool aWins = o.Winner == "attackers";
            string loser = aWins ? d : a, loserOf = aWins ? dOf : aOf;
            double loserLeft = aWins ? o.DefendersLeft : o.AttackersLeft, loserMen = aWins ? dMen : aMen;
            string how = o.How == "rout" ? loser + " break with about " + Words.N(loserLeft) + " of " + loserOf + " " + Words.N(loserMen) + " left"
                       : o.How == "retreat" ? loser + " retreat with about " + Words.N(loserLeft) + " of " + loserOf + " " + Words.N(loserMen) + " (a side that retreats gets away with its lords, and nothing is captured)"
                       : "all " + Words.N(loserMen) + " of " + loserOf + " men are down";
            return (aWins ? a : d) + " win in " + GameBattle.HoursRange(runs) + ", with about " + Words.N(aWins ? o.AttackersLeft : o.DefendersLeft) + " of " + (aWins ? aOf : dOf) + " "
                   + Words.N(aWins ? aMen : dMen) + " still standing; " + how;
        }

        /// <summary>Lords the realm saw marching to the place (heading for it, with the hour they get there), joining their own side.</summary>
        private static List<BattleJoiner> Joiners(JsonObject view, string place, double now, bool weStorm, ISet<string> counted, out List<string> names)
        {
            var list = new List<BattleJoiner>();
            names = new List<string>();
            if (place == null) return list;
            foreach (JsonObject o in (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (!Eq(Str(o["toward"]), place)) continue;
                // war room 0.20.7: a force named on either side is not a joiner too (an army in view stands for its members, so its leader's ids suffice)
                if ((Str(o["id"]) != null && counted.Contains(Str(o["id"]))) || (Str(o["lord"]) != null && counted.Contains(Str(o["lord"])))) continue;
                double at = o["arrives_at"] is JsonValue av && av.TryGetValue(out double ad) ? ad - now : double.NaN;
                if (double.IsNaN(at) || at > 24 * 5) continue;
                bool enemy = Str(o["relation"]) == "enemy";
                double men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                // an enemy marching to an enemy place joins its defenders (we storm); to our place, its besiegers
                list.Add(new BattleJoiner { Attackers = weStorm ? !enemy : enemy, Men = men, At = Math.Max(0, at), Power = men > 0 ? F(o["strength"]) / (float)men : 0 });
                names.Add(Str(o["name"]) + " [" + (Str(o["lord"]) ?? Str(o["id"])) + "] (" + Words.N(men) + " men, in about " + Words.N(Math.Max(1, Math.Round(at))) + " hours)");
            }
            return list;
        }

        private static bool Eq(string a, string b) => a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>A party of the view that is the human player's, or an army he leads (0.17.3: the bridge marks it in_person).</summary>
        internal static bool InPerson(JsonObject o) => o?["in_person"] is JsonValue v && v.TryGetValue(out bool b) && b;

        /// <summary>The note a battle forecast adds when the human player fights in it (0.17.3).</summary>
        internal const string HumanNote = "- The human player fights this one in person (his party, or an army he leads, is on a side): it is fought on the spot, so only the lords and bands within 3 of it when it begins, and the armies of its leaders, take part (3.75 while he is in a siege; a storm he leads or defends: the siege camp and those inside), and nobody else arrives. These rules do not count him: he is worth about a quarter more than his side's strength (at even strength he wins; with yours a quarter above his it could go either way; above that you will probably win), and his record (lookups/the_human_player.txt) moves that bar.";

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
