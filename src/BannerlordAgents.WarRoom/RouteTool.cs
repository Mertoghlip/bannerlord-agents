using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>One army or place the agent may ask about, as its realm knows it: where, how fast, since when.</summary>
    internal sealed class Known
    {
        public string Id, Lord, Name, What;
        public float X, Y, Pace;
        public string PaceFrom;
        /// <summary>The in-game hour this position is from (an enemy's last sighting); NaN for a holding.</summary>
        public double At = double.NaN;
        public bool Place, Own;
        public string Toward;
    }

    /// <summary>
    /// The agent's "ask the way" tool (0.7, docs/ROUTES.md), served by the war room inside each answer and council. It reads
    /// only the realm's own view file (live\(campaign)\view_(realm).json, written each hour by the bridge: its own lords with their
    /// pace, and only the enemies its report shows, where it last saw them), the holdings everyone knows, and the walking map.
    /// So it can never tell the agent more than its realm saw. Each question and answer goes to logs\warroom\routes.jsonl, which
    /// the dashboard shows beside the agent's moves. How marching works comes with the first answer in a call.
    /// </summary>
    internal sealed class RouteTool : IToolHost
    {
        public const string ToolName = "ask_the_way";
        private static readonly object Gate = new object();
        private static WalkMap _map;
        private static Finder _finder;
        private static string _mapCrc;

        private readonly string _root, _logDir, _campaign, _realm, _call;
        private readonly int _poke;
        private bool _toldRules;
        public int Asked;

        public string Server => "routes";
        public IEnumerable<string> Tools => new[] { ToolName };

        public RouteTool(string root, string logDir, string campaign, string realm, int poke, string call)
        {
            _root = root;
            _logDir = logDir;
            _campaign = campaign;
            _realm = realm;
            _poke = poke;
            _call = call;
        }

        public static string DefaultRoot()
        {
            string over = Environment.GetEnvironmentVariable("BANNERLORD_AGENTS_ROOT");
            return !string.IsNullOrWhiteSpace(over) ? over.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents");
        }

        /// <summary>The walking map's folder, once Ready found it.</summary>
        public string MapDir { get; private set; }

        public string ViewPath => Path.Combine(_root, "live", _campaign ?? "unknown", "view_" + (_realm ?? "unknown") + ".json");

        /// <summary>The tool can answer: the realm's view file is there and the map it names has been copied.</summary>
        public bool Ready(out string why)
        {
            why = null;
            if (_campaign == null || _realm == null) { why = "no campaign or realm yet"; return false; }
            JsonObject view = ReadView();
            if (view == null) { why = "no view file for the realm yet (" + ViewPath + ")"; return false; }
            MapDir = WalkMap.FindDir(_root, (string)view["map"]);
            if (MapDir == null) { why = "the walking map is not copied yet"; return false; }
            return true;
        }

        private JsonObject ReadView()
        {
            try { return Room.ReadShared(ViewPath) is string t ? JsonNode.Parse(t) as JsonObject : null; }
            catch (JsonException) { return null; }
        }

        private static WalkMap MapFor(string root, string crc)
        {
            lock (Gate)
            {
                // read again when the dashboard has taught the map more (learned.bin, from the game's own planned ways)
                DateTime learned = _map?.Dir != null && File.Exists(Path.Combine(_map.Dir, "learned.bin")) ? File.GetLastWriteTimeUtc(Path.Combine(_map.Dir, "learned.bin")) : DateTime.MinValue;
                if (_map != null && _mapCrc == crc && learned == _learnedAt) return _map;
                string dir = WalkMap.FindDir(root, crc);
                if (dir == null) return null;
                _map = WalkMap.Load(dir);
                _finder = new Finder(_map);
                _mapCrc = crc;
                string lp = Path.Combine(dir, "learned.bin");
                _learnedAt = File.Exists(lp) ? File.GetLastWriteTimeUtc(lp) : DateTime.MinValue;
                return _map;
            }
        }

        private static DateTime _learnedAt = DateTime.MinValue;

        // ---------- the protocol ----------

        public JsonNode Handle(JsonObject msg)
        {
            string method = (string)msg["method"];
            JsonNode id = msg["id"]?.DeepClone();
            if (id == null) return null;   // a notification
            JsonObject result;
            switch (method)
            {
                case "initialize":
                    result = new JsonObject
                    {
                        ["protocolVersion"] = (string)msg["params"]?["protocolVersion"] ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = Server, ["version"] = typeof(RouteTool).Assembly.GetName().Version.ToString(3) }
                    };
                    break;
                case "tools/list":
                    result = new JsonObject { ["tools"] = new JsonArray(Definition()) };
                    break;
                case "tools/call":
                {
                    string name = (string)msg["params"]?["name"];
                    JsonObject args = msg["params"]?["arguments"] as JsonObject ?? new JsonObject();
                    string text = name == ToolName ? Ask(args) : "There is no tool called " + name + ".";
                    result = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) };
                    break;
                }
                default:
                    result = new JsonObject();
                    break;
            }
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }

        public static JsonObject Definition() => new JsonObject
        {
            ["name"] = ToolName,
            ["description"] = "The way one army would march to a holding or to another army, found as the game itself finds ways: its length, the hours it takes at "
                              + "that army's pace (an enemy's from where your realm last saw it), the ground it crosses, and the bridges, fords and narrow passes on it with "
                              + "what closing each would cost, and spots [pX_Y] on the way where a watch can be posted. Optionally, who would get there first. How marching works is in the first answer, and in marching.txt.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["from"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of one of your lords or armies, of an enemy your report shows, or of a holding." },
                    ["to"] = new JsonObject { ["type"] = "string", ["description"] = "The id in [brackets] of a holding, or of a lord or army your report shows." },
                    ["also_from"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Optional: other lords' or armies' ids, to see who would get there first." }
                },
                ["required"] = new JsonArray("from", "to")
            }
        };

        // ---------- the answer ----------

        /// <summary>One question, answered in plain words, and logged.</summary>
        public string Ask(JsonObject args)
        {
            var sw = Stopwatch.StartNew();
            string fromQ = Str(args["from"]), toQ = Str(args["to"]);
            var also = (args["also_from"] as JsonArray)?.Select(Str).Where(s => !string.IsNullOrWhiteSpace(s)).Take(5).ToList() ?? new List<string>();
            var lines = new List<string>();
            string error = null;
            Known from = null, to = null;
            double hours = double.NaN;
            try
            {
                JsonObject view = ReadView();
                if (view == null) error = "Your realm's view is not ready yet; ask again in a moment.";
                WalkMap m = view == null ? null : MapFor(_root, (string)view["map"]);
                if (error == null && m == null) error = "The map is not ready yet, so the way cannot be worked out.";
                if (error == null)
                {
                    hours = Num(view["hours"]);
                    var known = Gather(view, m);
                    from = Find(known, m, fromQ);
                    to = Find(known, m, toQ);
                    if (from == null) error = "Your realm knows no lord, army or holding called " + Show(fromQ) + ". Use an id in [brackets] from your report.";
                    else if (to == null) error = "Your realm knows no holding, lord or army called " + Show(toQ) + ". Use an id in [brackets] from your report.";
                    else lines = Answer(m, view, known, from, to, also, hours);
                }
            }
            catch (Exception e) { error = "The way could not be worked out (" + e.Message + ")."; }
            Asked++;
            string text = error ?? string.Join("\n", lines);
            if (error == null && !_toldRules)
            {
                _toldRules = true;
                WalkMap m = _map;
                text += "\n\n" + Marching.Rules(m, SpeedFactors.Load(m?.Dir));
            }
            Log(new
            {
                at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call,
                hours = double.IsNaN(hours) ? (double?)null : hours, from = from?.Name ?? fromQ, from_id = from?.Id, to = to?.Name ?? toQ, to_id = to?.Id, also,
                lines = error == null ? lines : null, error, ms = Math.Round(sw.Elapsed.TotalMilliseconds)
            });
            return text;
        }

        private List<string> Answer(WalkMap m, JsonObject view, List<Known> known, Known from, Known to, List<string> also, double now)
        {
            Atlas atlas = Atlas.Load(m.Dir);
            SpeedFactors sf = SpeedFactors.Load(m.Dir);
            GameCalendar cal = Calendar();
            var lines = new List<string> { "THE WAY FROM " + Head(from) + " TO " + Head(to) };
            lines.Add("- " + Describe(from, now));
            if (!to.Place) lines.Add("- " + Describe(to, now) + " The way runs to where it was then.");
            RouteResult r;
            lock (Gate) r = _finder.Find(from.X, from.Y, to.X, to.Y);
            double start = double.IsNaN(from.At) ? now : from.At;
            List<string> way = Explain.Way(m, atlas, r, from.Pace, from.PaceFrom, start, sf, cal.When, out double _, out double arrive);
            foreach (string l in way) lines.Add("- " + l);
            if (r.Ok && !double.IsNaN(from.At) && from.At < now - 0.5)
                lines.Add("- That is if it marched on from where it was seen " + Words.N(now - from.At) + " hours ago, at once and without turning aside.");
            if (also.Count > 0 && r.Ok)
            {
                var race = new List<(string who, double arrive, string note)>();
                if (!double.IsNaN(arrive)) race.Add((from.Name, arrive, ""));
                foreach (string q in also)
                {
                    Known k = Find(known, m, q);
                    if (k == null || k.Place) { lines.Add("- (" + Show(q) + ": your realm knows no lord or army by that id.)"); continue; }
                    RouteResult rk;
                    lock (Gate) rk = _finder.Find(k.X, k.Y, to.X, to.Y);
                    if (!rk.Ok) { lines.Add("- " + k.Name + ": no way there (" + rk.Why + ")."); continue; }
                    double hk = Marching.Hours(rk, m, k.Pace, double.IsNaN(k.At) ? now : k.At, sf, out double ak);
                    race.Add((k.Name, ak, " (" + Words.N(rk.Length) + " by the shortest way at " + Words.D1(k.Pace) + " an hour, " + k.PaceFrom
                                          + (double.IsNaN(k.At) || k.At >= now - 0.5 ? "" : ", from where it was seen " + Words.N(now - k.At) + " hours ago") + ")"));
                    _ = hk;
                }
                if (race.Count > 1)
                {
                    lines.Add("WHO GETS THERE FIRST, marching at once by the shortest way");
                    foreach ((string who, double a, string note) in race.OrderBy(x => x.arrive))
                        lines.Add("- " + who + ": about " + cal.When(a) + note + ".");
                }
            }
            return lines;
        }

        private static string Head(Known k) => k.Name + " [" + (k.Lord ?? k.Id) + "]";

        private static string Describe(Known k, double now)
        {
            if (k.Place) return k.Name + " is a " + k.What + ".";
            if (k.Own) return k.Name + ": " + k.What + ".";
            bool fresh = double.IsNaN(k.At) || k.At >= now - 0.5;
            return k.Name + ": " + k.What + (fresh ? ", in view now." : ", as your realm last saw it " + Words.N(now - k.At) + " hours ago.");
        }

        private static string Show(string q) => string.IsNullOrWhiteSpace(q) ? "(nothing given)" : "\"" + q + "\"";

        /// <summary>Everything the realm knows of, from its view file: its own lords, the enemies its report shows (in view, seen earlier, trails), its holdings, and every place on the map.</summary>
        private static List<Known> Gather(JsonObject view, WalkMap m)
        {
            var list = new List<Known>();
            var ownPaces = new List<float>();
            foreach (JsonObject o in (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                float pace = F(o["pace"]);
                if (pace <= 0.05f) pace = F(o["final_speed"]);
                if (pace > 0.05f) ownPaces.Add(pace);
                int men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                list.Add(new Known
                {
                    Id = Str(o["id"]), Lord = Str(o["lord"]), Name = Str(o["name"]), X = F(o["x"]), Y = F(o["y"]), Pace = pace, PaceFrom = "its own pace", Own = true,
                    What = "your own" + (I(o["army_men"]) > 0 ? " army" : "") + ", " + Words.N(men) + " men, " + (Str(o["doing"]) ?? "")
                });
            }
            float usual = ownPaces.Count > 0 ? ownPaces.OrderBy(x => x).ElementAt(ownPaces.Count / 2) : 3.4f;
            var trails = (view["trails"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            void Enemy(JsonObject o, bool inView)
            {
                string id = Str(o["id"]);
                if (id == null || list.Any(k => k.Id == id)) return;
                (float pace, string from) = TrailPace(trails.FirstOrDefault(t => Str(t["id"]) == id), usual);
                int men = I(o["army_men"]) > 0 ? I(o["army_men"]) : I(o["men"]);
                list.Add(new Known
                {
                    Id = id, Lord = Str(o["lord"]), Name = Str(o["name"]), X = F(o["x"]), Y = F(o["y"]), At = Num(o["seen_hours"]), Pace = pace, PaceFrom = from, Toward = Str(o["toward"]),
                    What = (Str(o["relation"]) ?? "seen") + (I(o["army_men"]) > 0 ? " army" : "") + " of " + (Str(o["faction_name"]) ?? "?") + ", " + Words.N(men) + " men, " + (Str(o["doing"]) ?? "")
                           
                });
            }
            foreach (JsonObject o in (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()) Enemy(o, true);
            foreach (JsonObject o in (view["last_seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()) Enemy(o, false);
            foreach (Place p in m.Places)
            {
                if (p.Kind == "hideout") continue;
                list.Add(new Known { Id = p.Id, Name = p.Name, X = p.Gx != 0 || p.Gy != 0 ? p.Gx : p.X, Y = p.Gx != 0 || p.Gy != 0 ? p.Gy : p.Y, Place = true, What = p.Kind });
            }
            return list;
        }

        /// <summary>An enemy's pace from the trail the realm saw (map units marched over the hours between its first and last points), else the usual pace of the realm's own lords.</summary>
        private static (float, string) TrailPace(JsonObject trail, float usual)
        {
            var pts = (trail?["points"] as JsonArray)?.OfType<JsonObject>().ToList();
            if (pts != null && pts.Count >= 2)
            {
                double span = Num(pts[pts.Count - 1]["hours"]) - Num(pts[0]["hours"]);
                float d = 0;
                for (int i = 1; i < pts.Count; i++) d += WalkMap.Dist(F(pts[i - 1]["x"]), F(pts[i - 1]["y"]), F(pts[i]["x"]), F(pts[i]["y"]));
                if (span >= 2 && d >= 3) return ((float)Math.Max(0.5, d / span), "the pace its trail shows, errands included");
            }
            return (usual, "its own pace is not known; this is the usual pace of your lords");
        }

        private static Known Find(List<Known> known, WalkMap m, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return null;
            string id = q.Trim().Trim('[', ']').Trim();
            Known k = known.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
                      ?? known.FirstOrDefault(x => !x.Place && string.Equals(x.Lord, id, StringComparison.OrdinalIgnoreCase));
            if (k != null) return k;
            string plain = Words.Plain(id);
            return known.FirstOrDefault(x => Words.Plain(x.Name) == plain) ?? known.FirstOrDefault(x => x.Place && Words.Plain(x.Name).StartsWith(plain, StringComparison.Ordinal));
        }

        private GameCalendar Calendar()
        {
            try { return GameCalendar.From(JsonNode.Parse(Room.ReadShared(Path.Combine(_root, "live", "current.json")) ?? "{}")?["calendar"]); }
            catch (JsonException) { return new GameCalendar(); }
        }

        /// <summary>marching.txt in the agent's channel: how marching works, for it to read when it wants.</summary>
        public static void WriteRules(string root, string channelDir)
        {
            try
            {
                string dir = WalkMap.FindDir(root);
                WalkMap m = dir == null ? null : WalkMap.Load(dir);
                string text = Marching.Rules(m, SpeedFactors.Load(dir)) + "\n";
                string path = Path.Combine(channelDir, "marching.txt");
                if (Room.ReadShared(path) != text) File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidDataException) { }
        }

        private void Log(object record)
        {
            try
            {
                Directory.CreateDirectory(_logDir);
                lock (Gate) File.AppendAllText(Path.Combine(_logDir, "routes.jsonl"), JsonSerializer.Serialize(record) + "\n");
            }
            catch (IOException) { }
        }

        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
        private static float F(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) ? (float)d : 0f;
        private static int I(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) ? (int)d : 0;
        private static double Num(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;
    }
}
