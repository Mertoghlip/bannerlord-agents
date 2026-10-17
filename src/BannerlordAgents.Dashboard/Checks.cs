using System.Text.Json;
using System.Text.Json.Nodes;
using BannerlordAgents.Routes;

namespace BannerlordAgents.Dashboard
{
    /// <summary>
    /// The dashboard's own work in the background, at low priority:
    /// - the crossing atlas, worked out once per walking map when it is missing (unless the war room is at it);
    /// - the accuracy check: for a few marching lords in each new frame, our way-finder's way against the way the game itself
    ///   planned (its length, and how far apart the two run), under each guess about what the game counts dearer
    ///   (logs\dashboard\accuracy.jsonl);
    /// - the speeds: each marching lord's speed with the ground counted against its speed on open ground, by the ground under
    ///   it and by day and night, so the slowing the agent is told is this game's (speed_factors.json beside the map).
    /// </summary>
    internal sealed class Checks
    {
        private readonly Store _store;
        private string _lastFrame;
        private int _framesChecked;
        private readonly object _gate = new object();
        private readonly Dictionary<string, List<float>> _gaps = new Dictionary<string, List<float>>();
        private readonly Dictionary<string, List<float>> _offs = new Dictionary<string, List<float>>();
        private readonly Dictionary<byte, List<float>> _day = new Dictionary<byte, List<float>>();
        private readonly List<float> _nightPlain = new List<float>();
        private int _speedFrames;
        public volatile string AtlasProgress = "not started";
        private Thread _atlasThread;

        public Checks(Store store) => _store = store;

        public void Start(CancellationToken stop)
        {
            var t = new Thread(() => Loop(stop)) { IsBackground = true, Name = "dashboard checks", Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        private void Loop(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try { Once(); }
                catch (Exception e) { Log.Say("The checks stumbled: " + e.Message); }
                stop.WaitHandle.WaitOne(2000);
            }
        }

        private void Once()
        {
            JsonObject cur = _store.Current();
            if (cur == null) return;
            string crc = (string)cur["map"];
            WalkMap m = _store.Map(crc);
            if (m == null) return;
            EnsureAtlas(m);
            string campaign = (string)cur["campaign"], load = (string)cur["load_id"];
            if (campaign == null || load == null) return;
            List<long> hours = _store.FrameHours(campaign, load);
            if (hours.Count == 0) return;
            string frame = _store.FramePath(campaign, load, hours[hours.Count - 1]);
            if (frame == _lastFrame || !File.Exists(frame)) return;
            _lastFrame = frame;
            JsonObject f;
            try { f = _store.ReadFrame(frame); }
            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidDataException) { return; }
            if (f == null) return;
            _framesChecked++;
            Accuracy(m, f);
            Speeds(m, f);
        }

        // ---------- the atlas ----------

        private void EnsureAtlas(WalkMap m)
        {
            if (File.Exists(Atlas.PathIn(m.Dir))) { AtlasProgress = "ready"; return; }
            if (_atlasThread != null && _atlasThread.IsAlive) return;
            _atlasThread = new Thread(() =>
            {
                try
                {
                    AtlasProgress = "starting";
                    bool ok = Atlas.BuildIfMissing(m, s => AtlasProgress = s, out string said);
                    AtlasProgress = ok ? "ready" : said;
                    if (ok && said != "ready") Log.Say("Crossing atlas for map " + m.Meta.Crc + " ready: " + said + ".");
                }
                catch (Exception e) { AtlasProgress = "failed: " + e.Message; Log.Say("The crossing atlas failed: " + e); }
            }) { IsBackground = true, Name = "atlas", Priority = ThreadPriority.Lowest };
            _atlasThread.Start();
        }

        // ---------- our ways against the game's ----------

        private void Accuracy(WalkMap m, JsonObject f)
        {
            if (f["parties"] is not JsonArray parties) return;
            var marching = new List<(string id, float x, float y, List<float[]> pts)>();
            foreach (JsonNode n in parties)
            {
                if (n is not JsonObject p || (string)p["kind"] != "lord" || p["route"] is not JsonArray r || r.Count < 4) continue;
                float x = F(p["x"]), y = F(p["y"]);
                var pts = new List<float[]> { new[] { x, y } };
                for (int i = 0; i + 1 < r.Count; i += 2) pts.Add(new[] { F(r[i]), F(r[i + 1]) });
                if (Length(pts) < 15f) continue;
                marching.Add(((string)p["id"], x, y, pts));
            }
            if (marching.Count == 0) return;
            double hours = f["hours"] is JsonValue hv && hv.TryGetValue(out double hh) ? hh : 0;
            int start = (int)(Math.Abs((long)hours) % Math.Max(1, marching.Count));
            foreach ((string id, float x, float y, List<float[]> pts) in Enumerable.Range(0, Math.Min(5, marching.Count)).Select(k => marching[(start + k) % marching.Count]))
            {
                float game = Length(pts);
                float[] end = pts[pts.Count - 1];
                var row = new Dictionary<string, object> { ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ["hours"] = Math.Round(hours, 2), ["party"] = id, ["game"] = Math.Round(game, 1) };
                foreach (Costs c in Costs.Guesses)
                {
                    RouteResult ours = _store.WithFinder(m, fd => fd.Find(x, y, end[0], end[1], c, null, false));
                    if (!ours.Ok) { row[c.Name] = "no way: " + ours.Why; continue; }
                    float gap = (ours.Length - game) / game * 100f, off = Apart(ours.Points, pts);
                    lock (_gate)
                    {
                        Add(_gaps, c.Name, gap, 400);
                        Add(_offs, c.Name, off, 400);
                    }
                    row[c.Name] = new { length = Math.Round(ours.Length, 1), gap_pct = Math.Round(gap, 1), apart = Math.Round(off, 2), ms = Math.Round(ours.Ms) };
                }
                Log.Line("accuracy.jsonl", JsonSerializer.Serialize(row));
            }
        }

        /// <summary>How far apart two ways run: the mean distance from points along ours (every 2 units) to the game's way.</summary>
        private static float Apart(List<float[]> ours, List<float[]> game)
        {
            var gr = new RouteResult { Ok = true, Points = game };
            float sum = 0;
            int n = 0;
            for (int i = 1; i < ours.Count; i++)
            {
                float[] a = ours[i - 1], b = ours[i];
                float len = WalkMap.Dist(a[0], a[1], b[0], b[1]);
                for (float d = 0; d < len; d += 2f)
                {
                    float t = len <= 0 ? 0 : d / len;
                    Explain.Along(gr, a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, out float off);
                    sum += off;
                    n++;
                }
            }
            return n == 0 ? 0 : sum / n;
        }

        private static float Length(List<float[]> pts)
        {
            float l = 0;
            for (int i = 1; i < pts.Count; i++) l += WalkMap.Dist(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1]);
            return l;
        }

        // ---------- the speeds ----------

        private void Speeds(WalkMap m, JsonObject f)
        {
            if (f["parties"] is not JsonArray parties) return;
            bool night = f["night"] is JsonValue nv && nv.TryGetValue(out bool nb) && nb;
            lock (_gate)
            {
                foreach (JsonNode n in parties)
                {
                    if (n is not JsonObject p || (string)p["kind"] != "lord") continue;
                    if (p["attached"] != null || p["in_settlement"] != null || p["in_battle"] != null || p["besieging"] != null) continue;
                    if (p["route"] == null && p["straight"] == null) continue;
                    float speed = F(p["speed"]), final = F(p["final_speed"]);
                    if (speed < 0.1f || final < 0.05f) continue;
                    byte g = m.GroundAt(F(p["x"]), F(p["y"]));
                    float ratio = final / speed;
                    if (night) { if (g == Ground.Plain) Add(_nightPlain, ratio, 3000); }
                    else
                    {
                        if (!_day.TryGetValue(g, out List<float> l)) _day[g] = l = new List<float>();
                        Add(l, ratio, 3000);
                    }
                }
                if (++_speedFrames % 12 != 0 || !_day.TryGetValue(Ground.Plain, out List<float> plain) || plain.Count < 30) return;
                float basis = Median(plain);
                if (basis <= 0.05f) return;
                var sf = new SpeedFactors { Source = "measured in this game's marches", Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
                foreach (KeyValuePair<byte, List<float>> kv in _day)
                {
                    sf.ByGround[kv.Key.ToString()] = (float)Math.Round(Median(kv.Value) / basis, 3);
                    sf.Samples[kv.Key.ToString()] = kv.Value.Count;
                }
                if (_nightPlain.Count > 0) { sf.Night = (float)Math.Round(Median(_nightPlain) / basis, 3); sf.NightSamples = _nightPlain.Count; }
                try { JsonText.Write(Path.Combine(m.Dir, "speed_factors.json"), sf); }
                catch (IOException e) { Log.Say("The measured speeds could not be written: " + e.Message); }
            }
        }

        // ---------- for the page ----------

        public object Summary()
        {
            lock (_gate)
            {
                return new
                {
                    atlas = AtlasProgress,
                    frames_checked = _framesChecked,
                    accuracy = Costs.Guesses.Select(c => new
                    {
                        guess = c.Name,
                        samples = _gaps.TryGetValue(c.Name, out List<float> g) ? g.Count : 0,
                        median_gap_pct = g != null && g.Count > 0 ? Math.Round(Median(g.Select(Math.Abs).ToList()), 1) : (double?)null,
                        mean_gap_pct = g != null && g.Count > 0 ? Math.Round(g.Average(), 1) : (double?)null,
                        median_apart = _offs.TryGetValue(c.Name, out List<float> o) && o.Count > 0 ? Math.Round(Median(o), 2) : (double?)null
                    }).ToList(),
                    speeds = _day.OrderByDescending(kv => kv.Value.Count).Select(kv => new { ground = Ground.Name(kv.Key), samples = kv.Value.Count, median_ratio = Math.Round(Median(kv.Value), 3) }).ToList(),
                    night_plain_samples = _nightPlain.Count
                };
            }
        }

        private static void Add(Dictionary<string, List<float>> d, string k, float v, int most)
        {
            if (!d.TryGetValue(k, out List<float> l)) d[k] = l = new List<float>();
            Add(l, v, most);
        }

        private static void Add(List<float> l, float v, int most)
        {
            l.Add(v);
            if (l.Count > most) l.RemoveAt(0);
        }

        public static float Median(List<float> l)
        {
            if (l.Count == 0) return 0;
            var s = l.OrderBy(x => x).ToList();
            return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        }

        private static float F(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) ? (float)d : 0f;
    }
}
