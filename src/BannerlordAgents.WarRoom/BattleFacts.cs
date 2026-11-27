using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BannerlordAgents.Common;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// War room 0.22.0 (Nov 26, the tooling audit of run 20261125-2341-C2): what the battle questions share.
    /// - Road hours (items 4, 5): a party's hours to a point by our copy of the game's way-finder and the march clock, as the engagement question already had them; a road is
    ///   never shorter than the straight line, so only parties whose straight-line time could fall inside the hours asked are routed. The muster had divided the straight line by
    ///   each lord's pace (roads run a median 1.12 times longer, up to 2.07): at wake #13244 it said 18 lords within a day where the place's page said 11.
    /// - Lords of the realm not in the field (item 30): one riding in another lord's army (counted with it), a prisoner, or one with no party, said so rather than "no lord or
    ///   army of yours by that id".
    /// - The siege's own clock (item 28): what the place's page shows (preparations, engines, the earliest storm) for a siege under way, rather than a camp that has just arrived.
    /// - The enemy's measured edge (item 9): how hard each enemy realm's blows landed in the realm's own battles against the rules' expectation, so that after 5 such battles every
    ///   forecast made before a battle shows both figures (15 strikes were all won, at a median 1.9 times the losses forecast).
    /// </summary>
    internal sealed partial class RouteTool
    {
        /// <summary>The file the realm's measured enemy edge is kept in (the campaign's memory folder; set by the war room); null: not kept.</summary>
        public string EdgeFile;
        /// <summary>The usual order delay in in-game hours, as the game measures wake answers (set by the war room; NaN when not known).</summary>
        public Func<double> LagHours;

        // ---------- road hours ----------

        /// <summary>
        /// Hours from now until a party could be at a point by the game's way and its pace (the march clock for one seen earlier), with the straight line as a floor: a party whose
        /// straight line at its fastest already passes cap is not routed and gets that floor (more than cap). NaN: no way over land. byRoad false: the map is not ready, so the
        /// straight line at its pace by day on open ground.
        /// </summary>
        internal static double RoadHoursFrom(string root, string mapCrc, float x, float y, Pace march, ClockParty unseen, float tx, float ty, double now, double cap, out bool byRoad)
        {
            byRoad = false;
            double dist = Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y));
            Pace p = march ?? Pace.FromSpeed(MarchClock.FloorSpeed);
            float fastest = unseen != null ? MarchClock.FastestUnits(unseen, MarchClock.BasisAt(unseen, now)) : MarchClock.FastestUnits(new ClockParty { Pace = p, InView = true }, PaceBasis.Exact);
            if (fastest <= 0.01f) fastest = GamePace.PerHour(MarchClock.FloorSpeed);
            double floor = dist / fastest;
            if (dist < 0.5) { byRoad = true; return 0; }
            WalkMap m = string.IsNullOrEmpty(mapCrc) ? null : MapFor(root, mapCrc);
            if (m == null || _finder == null) return dist / Math.Max(0.05, GamePace.PerHour(p.OpenSpeed));
            if (!double.IsNaN(cap) && floor > cap) { byRoad = true; return floor; }
            RouteResult r;
            lock (Gate) r = _finder.Find(x, y, tx, ty);
            byRoad = true;
            if (r == null || !r.Ok) return double.NaN;
            if (unseen != null)
            {
                MarchTime mt = Marching.Clock(r, m, unseen, now);
                return mt.Reached ? mt.Hours : double.NaN;
            }
            double h = Marching.Hours(r, m, p, now, out _);
            return double.IsNaN(h) ? double.NaN : Math.Max(h, floor * 0.999);
        }

        /// <summary>An own lord's (or army's) march from the view file: the game's parts of its speed, else its speed.</summary>
        private static Pace OwnMarch(JsonObject o)
        {
            Pace p = MarchOf(o?["march"]);
            if (p != null) return p;
            float pace = F(o?["pace"]);
            if (pace <= 0.05f) pace = F(o?["final_speed"]);
            return pace > 0.05f ? Pace.FromSpeed(pace) : null;
        }

        /// <summary>An own lord's road hours to a point (see RoadHoursFrom).</summary>
        internal static double OwnRoadHours(string root, JsonObject view, JsonObject own, float tx, float ty, double cap, out bool byRoad) =>
            RoadHoursFrom(root, (string)view?["map"], F(own["x"]), F(own["y"]), OwnMarch(own), null, tx, ty, Num(view?["hours"]), cap, out byRoad);

        /// <summary>
        /// The point parties march to for a castle, town or village: its gate (the walk map's places, the game's GatePosition), else the point given, unchanged
        /// (war room 0.22.3, Nov 27, run 20261127-1453-C2). The questions aimed at a holding's centre, which for 19 of the front's 64 holdings lies on the holding's
        /// own faces of the walking mesh, which a marching party may not cross, so the copy of the game's way-finder found no way there for nearly every lord: the
        /// muster at Goblin Town 69 of 70, at Nanduhirion 23 of 23, and those lords were left out of its totals; the battle question took lords inside such a
        /// holding (who stand at its gate) for lords outside, and dropped reliefs and defending enemies there. A party inside a holding stands at its gate, so a
        /// lord's own point needs nothing.
        /// </summary>
        internal static void GateOf(string root, string mapCrc, string placeId, ref float x, ref float y)
        {
            if (string.IsNullOrEmpty(placeId) || string.IsNullOrEmpty(mapCrc)) return;
            Place p = MapFor(root, mapCrc)?.Places?.FirstOrDefault(q => Eq(q.Id, placeId));
            if (p != null && (p.Gx != 0 || p.Gy != 0)) { x = p.Gx; y = p.Gy; }
        }

        /// <summary>A holding's gate from its line in the view file (GateOf).</summary>
        private (float X, float Y) PlaceGate(JsonObject view, string id, JsonObject place)
        {
            float x = F(place?["x"]), y = F(place?["y"]);
            GateOf(_root, (string)view?["map"], id, ref x, ref y);
            return (x, y);
        }

        /// <summary>An enemy's road hours to a point from where the realm saw it (the march clock when seen earlier).</summary>
        private double EnemyRoadHours(JsonObject view, JsonObject seen, float tx, float ty, double cap, out bool byRoad)
        {
            double now = Num(view["hours"]);
            Pace march = MarchOf(seen["march"]);
            double seenAt = Num(seen["seen_hours"]);
            ClockParty unseen = null;
            if (march != null && !double.IsNaN(seenAt) && seenAt < now - 0.5)
            {
                unseen = new ClockParty { Id = Str(seen["id"]), X = F(seen["x"]), Y = F(seen["y"]), SeenHours = seenAt, InView = false, Pace = march };
                int men = I(seen["men"]), army = I(seen["army_men"]);
                if (army > men && men > 0) unseen.AlonePace = MarchClock.AlonePace(march, army, men);
            }
            return RoadHoursFrom(_root, (string)view["map"], F(seen["x"]), F(seen["y"]), march, unseen, tx, ty, now, cap, out byRoad);
        }

        // ---------- a lord of the realm not in the field ----------

        private static readonly Regex NamedId = new Regex(@"(?:^|\s|- )([^\[\]:;,\n]+?) \[([^\]]+)\]", RegexOptions.Compiled);

        /// <summary>A name the book gives an id, from any line or page that writes "Name [id]"; null when none.</summary>
        private string NameInBook(string id)
        {
            JsonObject book = Book();
            if (book == null) return null;
            if ((book["pages"] as JsonObject)?[id] is JsonValue pv && FirstName(Str(pv)) is string fn) return fn;
            foreach (JsonObject it in Items(book).Select(x => x.Item2))
                foreach (Match m in NamedId.Matches(Str(it["t"]) ?? ""))
                    if (Eq(m.Groups[2].Value, id)) return m.Groups[1].Value.Trim().TrimStart('-', ' ').Replace("ENEMY ", "").Replace("SEEN EARLIER ", "");
            return null;
        }

        /// <summary>
        /// Why a lord of the realm named in a question is not among its lords in the field (item 30), from the question book: riding in another's army (and that army's leader in the
        /// view, to count it), a prisoner (and where), or a lord with no party now; null when the realm knows no lord of its own by that id.
        /// </summary>
        internal string NotInField(string id, List<JsonObject> own, out JsonObject armyOf)
        {
            armyOf = null;
            JsonObject book = Book();
            if (book == null) return null;
            string name = NameInBook(id) ?? id;
            foreach (JsonObject it in (book["lists"]?["lords"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                List<string> ids = (it["ids"] as JsonArray)?.Select(Str).Where(x => x != null).ToList() ?? new List<string>();
                if (ids.Count < 2 || !ids.Skip(1).Any(x => Eq(x, id))) continue;
                Match h = Regex.Match(Str(it["t"]) ?? "", @"^- (.+?) \[([^\]]+)\]");
                if (!h.Success || !(Str(it["t"]) ?? "").Contains("army of")) continue;
                string leader = h.Groups[2].Value;
                armyOf = own.FirstOrDefault(o => Eq(Str(o["lord"]), leader) || Eq(Str(o["id"]), leader));
                return name + " [" + id + "] rides in " + h.Groups[1].Value + "'s army" + (armyOf != null ? ": counted with it" : "");
            }
            foreach (JsonObject it in (book["lists"]?["captives"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (!((it["ids"] as JsonArray)?.Any(x => Eq(Str(x), id)) ?? false)) continue;
                string t = (Str(it["t"]) ?? "").TrimStart('-', ' ');
                int colon = t.IndexOf("): ", StringComparison.Ordinal);
                string where = colon > 0 ? t.Substring(0, colon + 1) : t;
                return name + " [" + id + "] is a prisoner (" + where.Trim() + "): left out";
            }
            if ((book["pages"] as JsonObject)?[id] != null || NameInBook(id) != null)
                return name + " [" + id + "] has no party now: left out";
            return null;
        }

        // ---------- the siege's own clock ----------

        /// <summary>
        /// The siege of a place as its page shows it (item 28): the camp's preparations, its engines built and next, and when a storm could come (or the agent's storm rule's hour);
        /// null when the realm sees no siege of it under way.
        /// </summary>
        internal string SiegeClockOf(string placeId)
        {
            JsonObject book = Book();
            if (book == null || placeId == null) return null;
            foreach (JsonObject it in (book["lists"]?["sieges"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                string t = Str(it["t"]) ?? "";
                if (!t.Contains("besieged by") || !((it["ids"] as JsonArray)?.Any(x => Eq(Str(x), placeId)) ?? false)) continue;
                int p = t.IndexOf("preparations", StringComparison.Ordinal);
                if (p < 0) return null;
                var parts = new List<string>();
                foreach (string s in Regex.Split(t.Substring(p), @"(?<=\.)\s+"))
                {
                    string x = s.Trim();
                    if (parts.Count == 0) { parts.Add(x.TrimEnd('.')); continue; }
                    if (x.StartsWith("A storm could come", StringComparison.Ordinal) || x.StartsWith("Your storm rule", StringComparison.Ordinal) || x.StartsWith("No storm is likely", StringComparison.Ordinal)
                        || x.StartsWith("At this strength", StringComparison.Ordinal))
                    {
                        int b = x.IndexOf(" (besiegers ", StringComparison.Ordinal);
                        parts.Add((b > 0 ? x.Substring(0, b) : x).TrimEnd('.'));
                        break;
                    }
                }
                return string.Join(". ", parts);
            }
            return null;
        }

        // ---------- the enemy's measured edge ----------

        /// <summary>The realm whose lords a battle's other side is: the realm of the first of its parties the view shows (seen now or earlier).</summary>
        internal static string EnemyRealmOf(JsonObject view, JsonObject battle)
        {
            var seen = (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Concat((view["last_seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()).ToList();
            foreach (string p in ((battle["them"] as JsonObject)?["parties"] as JsonArray ?? new JsonArray()).Select(Str).Where(x => x != null))
            {
                JsonObject o = seen.FirstOrDefault(s => Eq(Str(s["id"]), p) || Eq(Str(s["lord"]), p));
                if (o != null && Str(o["faction_name"]) is string f && f.Length > 0) return f;
            }
            string led = Str((battle["them"] as JsonObject)?["led_by"]);
            return seen.Where(s => Eq(Str(s["name"]), led)).Select(s => Str(s["faction_name"])).FirstOrDefault(f => !string.IsNullOrEmpty(f));
        }

        private static readonly object EdgeGate = new object();

        /// <summary>
        /// The realm's battles under way, each kept by its key with its enemy realm and how hard that side's blows landed against the rules' expectation over the hours measured
        /// (the battle question's own measure; the newest reading of each battle stands). From the realm's own view file only.
        /// </summary>
        internal static void RecordEdge(string file, JsonObject view)
        {
            if (string.IsNullOrEmpty(file) || view == null) return;
            var battles = (view["battles"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(b => b["measured"] is JsonObject m && F(m["hours"]) >= 3 && F(m["hit_them"]) > 0).ToList();
            if (battles.Count == 0) return;
            lock (EdgeGate)
            {
                JsonObject all = ReadEdge(file);
                JsonObject kept = all["battles"] as JsonObject ?? new JsonObject();
                bool changed = false;
                foreach (JsonObject b in battles)
                {
                    string key = Str(b["key"]), realm = EnemyRealmOf(view, b);
                    if (key == null || realm == null) continue;
                    JsonObject m = (JsonObject)b["measured"];
                    var entry = new JsonObject { ["realm"] = realm, ["hit"] = Math.Round(F(m["hit_them"]), 3), ["hours"] = Math.Round(F(m["hours"]), 1), ["started"] = Num(b["started"]), ["seen"] = Num(view["hours"]) };
                    if (kept[key] is JsonObject old && F(old["hit"]) == F(entry["hit"]) && F(old["hours"]) == F(entry["hours"])) continue;
                    kept[key] = entry;
                    changed = true;
                }
                if (!changed) return;
                all["battles"] = kept.DeepClone();
                try { Memory.Write(file, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); } catch (IOException) { }
            }
        }

        private static JsonObject ReadEdge(string file)
        {
            try { return File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject o ? o : new JsonObject(); }
            catch (Exception e) when (e is IOException || e is JsonException) { return new JsonObject(); }
        }

        /// <summary>The battles a forecast needs before it shows the measured edge.</summary>
        internal const int EdgeEnough = 5;

        /// <summary>The middle value of an enemy realm's measured blows in the realm's battles begun by this hour; NaN with fewer than EdgeEnough. n: how many.</summary>
        internal static double MeasuredEdge(string file, string realm, double now, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(realm)) return double.NaN;
            List<double> hits;
            lock (EdgeGate)
                hits = ((ReadEdge(file)["battles"] as JsonObject) ?? new JsonObject()).Select(kv => kv.Value as JsonObject)
                    .Where(o => o != null && string.Equals(Str(o["realm"]), realm, StringComparison.OrdinalIgnoreCase) && (double.IsNaN(now) || !(Num(o["started"]) > now + 0.01)))
                    .Select(o => (double)F(o["hit"])).Where(h => h > 0).OrderBy(h => h).ToList();
            n = hits.Count;
            if (n < EdgeEnough) return double.NaN;
            return n % 2 == 1 ? hits[n / 2] : (hits[n / 2 - 1] + hits[n / 2]) / 2;
        }

        /// <summary>The enemy realm most of the named enemies' men belong to (their faction as seen).</summary>
        private static string RealmOfForces(List<JsonObject> seen, IEnumerable<string> ids)
        {
            var men = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
            {
                JsonObject o = seen.FirstOrDefault(s => Eq(Str(s["id"]), id) || Eq(Str(s["lord"]), id));
                string f = o != null ? Str(o["faction_name"]) : null;
                if (string.IsNullOrEmpty(f)) continue;
                men[f] = (men.TryGetValue(f, out double v) ? v : 0) + Math.Max(1, I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]));
            }
            return men.Count == 0 ? null : men.OrderByDescending(kv => kv.Value).First().Key;
        }

        /// <summary>
        /// The second figure of a forecast made before a battle (item 9): the realm's losses at the game's rules and at the enemy realm's measured edge, when it has fought enough
        /// battles against that realm; null otherwise. ourLoss: the share of the realm's men lost when the enemy's blows land at the given multiple of the rules'.
        /// </summary>
        private string EdgeLine(string realm, double now, Func<double, double> ourLoss)
        {
            double edge = MeasuredEdge(EdgeFile, realm, now, out int n);
            if (double.IsNaN(edge)) return null;
            double rules = ourLoss(1), measured = ourLoss(edge);
            if (double.IsNaN(rules) || double.IsNaN(measured)) return null;
            return "- At the game's rules you lose about " + Pct(rules) + " of your men; at what " + realm + "'s armies have shown in " + n + " of your battles (their blows landing at about "
                   + Pct(edge) + " of what the rules expect, the middle of those battles' measures), about " + Pct(measured) + ". Both rest on what your realm saw, the second on its own battles.";
        }
    }
}
