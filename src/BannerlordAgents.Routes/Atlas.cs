using System.Diagnostics;
using System.Text.Json.Serialization;

namespace BannerlordAgents.Routes
{
    /// <summary>One way between two places that a crossing's closing lengthens: the way's length before and after (NaN: no way at all).</summary>
    public sealed class AtlasExample
    {
        public string From { get; set; }
        public string To { get; set; }
        public float Before { get; set; }
        public float After { get; set; } = float.NaN;
    }

    /// <summary>A bridge, ford or narrow pass on the ways between places, and what closing it would cost those ways.</summary>
    public sealed class AtlasCrossing
    {
        public string Id { get; set; }
        /// <summary>bridge, ford or pass.</summary>
        public string Kind { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        /// <summary>The narrowest walkable width there, in map units across (a pass), or the crossing's own width (a bridge or ford).</summary>
        public float Width { get; set; }
        /// <summary>"the ford 8 east of Tharbad".</summary>
        public string Name { get; set; }
        /// <summary>How many of the ways between neighbouring holdings pass through it.</summary>
        public int Ways { get; set; }
        /// <summary>The most a way grows when it is closed, in map units (the way it cuts off entirely aside).</summary>
        public float Detour { get; set; }
        /// <summary>Some way between holdings has no other way at all.</summary>
        public bool CutsOff { get; set; }
        public bool Critical { get; set; }
        public List<AtlasExample> Examples { get; set; } = new List<AtlasExample>();
        /// <summary>The cells closed to test it (the whole bridge or ford, or the pass's cross-section).</summary>
        public List<int> Cells { get; set; } = new List<int>();

        [JsonIgnore] public string Short => Name + (CutsOff ? " (the only way)" : Detour >= 1 ? " (" + Words.N(Detour) + " longer round)" : "");
    }

    /// <summary>
    /// The crossings that matter on this map, worked out once outside the game and kept beside it (atlas.json): the ways
    /// between each town or castle and its nearest neighbours are found, every bridge, ford and narrow pass they go
    /// through is closed in turn, and the ways are found again. A crossing whose closing cuts a way off, or makes it much
    /// longer, is critical. The bridges and fords are the game's own ground types; a pass is where walkable ground
    /// narrows to under PassWidth map units across.
    /// </summary>
    public sealed class Atlas
    {
        public string Crc { get; set; }
        public string Made { get; set; }
        public double Seconds { get; set; }
        public int Places { get; set; }
        public int WaysFound { get; set; }
        public int Candidates { get; set; }
        public List<AtlasCrossing> Crossings { get; set; } = new List<AtlasCrossing>();

        public const float PassWidth = 12f;
        public const int Neighbours = 4;
        public const float NeighbourRange = 180f;
        public const int MostCandidates = 320, MostWaysTested = 6;

        public static string PathIn(string mapDir) => Path.Combine(mapDir, "atlas.json");

        /// <summary>
        /// Works out and keeps the atlas for a map that has none, unless another program is at it (a named lock per map, shared
        /// by the dashboard and the war room). True when the atlas is there afterwards. Slow (minutes on a big map): call it
        /// from a background thread.
        /// </summary>
        public static bool BuildIfMissing(WalkMap m, Action<string> progress, out string said)
        {
            said = null;
            if (m?.Dir == null) { said = "no map"; return false; }
            if (File.Exists(PathIn(m.Dir))) { said = "ready"; return true; }
            using var gate = new Mutex(false, @"Local\BannerlordAgents.Atlas." + m.Meta.Crc);
            bool mine;
            try { mine = gate.WaitOne(0); } catch (AbandonedMutexException) { mine = true; }
            if (!mine) { said = "being worked out by another program"; return false; }
            try
            {
                if (File.Exists(PathIn(m.Dir))) { said = "ready"; return true; }
                Atlas a = Build(m, progress);
                JsonText.Write(PathIn(m.Dir), a);
                said = a.Crossings.Count + " crossings that matter (" + a.Crossings.Count(c => c.Critical) + " critical) from " + a.WaysFound + " ways, in " + a.Seconds + " s";
                return true;
            }
            finally { gate.ReleaseMutex(); }
        }

        public static Atlas Load(string mapDir) => mapDir == null ? null : JsonText.Read<Atlas>(PathIn(mapDir));

        /// <summary>The crossing nearest a point within a distance, or null.</summary>
        public AtlasCrossing Near(float x, float y, float within)
        {
            AtlasCrossing best = null;
            float bd = within;
            foreach (AtlasCrossing c in Crossings)
            {
                float d = WalkMap.Dist(c.X, c.Y, x, y);
                if (d <= bd) { bd = d; best = c; }
            }
            return best;
        }

        private sealed class Way
        {
            public Place A, B;
            public int[] Cells;
            public float Length;
        }

        private sealed class Candidate
        {
            public readonly List<int> Cells = new List<int>();
            public readonly HashSet<int> Ways = new HashSet<int>();
            public float Width = float.MaxValue;
            public int CrossingCells;
            public byte CrossingGround;
            public readonly List<(int cell, float nx, float ny)> Across = new List<(int, float, float)>();
        }

        public static Atlas Build(WalkMap m, Action<string> progress = null, CancellationToken cancel = default)
        {
            var sw = Stopwatch.StartNew();
            var finder = new Finder(m) { MaxExpand = 3_000_000 };
            List<Place> places = m.Places.Where(p => p.Kind == "town" || p.Kind == "castle").ToList();
            if (places.Count < 8) places = m.Places.Where(p => p.Kind != "hideout").ToList();
            var atlas = new Atlas { Crc = m.Meta.Crc, Places = places.Count };

            // 1. the ways between each holding and its nearest neighbours
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < places.Count; i++)
            {
                foreach (int j in Enumerable.Range(0, places.Count).Where(j => j != i)
                             .Select(j => (j, d: WalkMap.Dist(places[i].X, places[i].Y, places[j].X, places[j].Y)))
                             .Where(t => t.d <= NeighbourRange).OrderBy(t => t.d).Take(Neighbours).Select(t => t.j))
                    pairs.Add(i < j ? (i, j) : (j, i));
            }
            var ways = new List<Way>();
            int done = 0;
            foreach ((int i, int j) in pairs)
            {
                cancel.ThrowIfCancellationRequested();
                Place a = places[i], b = places[j];
                RouteResult r = finder.Find(Gx(a), Gy(a), Gx(b), Gy(b), Costs.Equal, null, false);
                if (r.Ok) ways.Add(new Way { A = a, B = b, Cells = r.Cells.ToArray(), Length = r.Length });
                if (++done % 50 == 0) progress?.Invoke("ways between holdings: " + done + " of " + pairs.Count);
            }
            atlas.WaysFound = ways.Count;

            // 2. the cells on those ways where the ground is a bridge or ford, or narrows to a pass
            var cand = new Dictionary<int, (float width, float nx, float ny)>();
            var waysAt = new Dictionary<int, HashSet<int>>();
            for (int w = 0; w < ways.Count; w++)
            {
                int[] cells = ways[w].Cells;
                for (int k = 0; k < cells.Length; k++)
                {
                    int c = cells[k];
                    bool known = cand.ContainsKey(c);
                    if (!known)
                    {
                        int a = cells[Math.Max(0, k - 3)], b = cells[Math.Min(cells.Length - 1, k + 3)];
                        float dx = m.CenterX(b) - m.CenterX(a), dy = m.CenterY(b) - m.CenterY(a), len = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (len <= 0) continue;
                        float nx = -dy / len, ny = dx / len;
                        bool crossing = Ground.IsCrossing(m.Ground[c]);
                        float width = finder.Across(m.CenterX(c), m.CenterY(c), nx, ny, 20f);
                        if (!crossing && width >= PassWidth) continue;
                        cand[c] = (width, nx, ny);
                    }
                    if (!waysAt.TryGetValue(c, out HashSet<int> set)) waysAt[c] = set = new HashSet<int>();
                    set.Add(w);
                }
            }

            // 3. neighbouring candidate cells form one crossing
            List<Candidate> groups = Group(m, cand, waysAt);
            atlas.Candidates = groups.Count;
            groups = groups.Where(g => g.CrossingCells > 0 || g.Cells.Count >= 3 || g.Width < 8f)
                           .OrderByDescending(g => g.CrossingCells > 0 ? 1 : 0).ThenByDescending(g => g.Ways.Count).ThenBy(g => g.Width)
                           .Take(MostCandidates).ToList();

            // 4. each closed in turn, and the ways through it found again
            var blocked = new bool[m.W * m.H];
            int tested = 0;
            foreach (Candidate g in groups)
            {
                cancel.ThrowIfCancellationRequested();
                List<int> closed = Closing(m, g);
                foreach (int c in closed) blocked[c] = true;
                var crossing = new AtlasCrossing
                {
                    Kind = g.CrossingCells > 0 ? (g.CrossingGround == Ground.Bridge ? "bridge" : "ford") : "pass",
                    Width = g.CrossingCells > 0 ? CrossingWidth(m, g) : g.Width,
                    Ways = g.Ways.Count,
                    Cells = closed
                };
                crossing.X = g.Cells.Average(c => m.CenterX(c));
                crossing.Y = g.Cells.Average(c => m.CenterY(c));
                foreach (int w in g.Ways.OrderBy(w => ways[w].Length).Take(MostWaysTested))
                {
                    Way way = ways[w];
                    float after = finder.Distance(Gx(way.A), Gy(way.A), Gx(way.B), Gy(way.B), Costs.Equal, blocked);
                    var ex = new AtlasExample { From = way.A.Name, To = way.B.Name, Before = (float)Math.Round(way.Length, 1), After = float.IsNaN(after) ? float.NaN : (float)Math.Round(after, 1) };
                    crossing.Examples.Add(ex);
                    if (float.IsNaN(after)) crossing.CutsOff = true;
                    else crossing.Detour = Math.Max(crossing.Detour, after - way.Length);
                }
                foreach (int c in closed) blocked[c] = false;
                crossing.Examples = crossing.Examples.OrderByDescending(e => float.IsNaN(e.After) ? float.MaxValue : e.After - e.Before).Take(3).ToList();
                crossing.Detour = (float)Math.Round(crossing.Detour, 1);
                float before = crossing.Examples.Count > 0 ? crossing.Examples[0].Before : 0;
                crossing.Critical = crossing.CutsOff || (crossing.Detour >= 30f && crossing.Detour >= 0.4f * Math.Max(1, before));
                if (crossing.CutsOff || crossing.Detour >= 10f) atlas.Crossings.Add(crossing);
                if (++tested % 20 == 0) progress?.Invoke("crossings tested: " + tested + " of " + groups.Count);
            }

            // 5. one entry for crossings a few units apart that serve the same ways; names; most important first
            atlas.Crossings = Merge(atlas.Crossings);
            int n = 0;
            foreach (AtlasCrossing c in atlas.Crossings.OrderByDescending(c => c.CutsOff).ThenByDescending(c => c.Detour))
            {
                c.Id = "x" + (++n);
                c.Name = "the " + c.Kind + " " + m.Describe(c.X, c.Y);
                c.X = (float)Math.Round(c.X, 1);
                c.Y = (float)Math.Round(c.Y, 1);
                c.Width = (float)Math.Round(c.Width, 1);
            }
            atlas.Crossings = atlas.Crossings.OrderBy(c => int.Parse(c.Id.Substring(1))).ToList();
            atlas.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
            atlas.Made = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return atlas;
        }

        private static float Gx(Place p) => p.Gx != 0 || p.Gy != 0 ? p.Gx : p.X;
        private static float Gy(Place p) => p.Gx != 0 || p.Gy != 0 ? p.Gy : p.Y;

        private static List<Candidate> Group(WalkMap m, Dictionary<int, (float width, float nx, float ny)> cand, Dictionary<int, HashSet<int>> waysAt)
        {
            var groups = new List<Candidate>();
            var seen = new HashSet<int>();
            foreach (int start in cand.Keys)
            {
                if (!seen.Add(start)) continue;
                var g = new Candidate();
                var q = new Queue<int>();
                q.Enqueue(start);
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    g.Cells.Add(c);
                    (float width, float nx, float ny) = cand[c];
                    g.Width = Math.Min(g.Width, width);
                    g.Across.Add((c, nx, ny));
                    if (Ground.IsCrossing(m.Ground[c])) { g.CrossingCells++; g.CrossingGround = m.Ground[c]; }
                    if (waysAt.TryGetValue(c, out HashSet<int> ws)) g.Ways.UnionWith(ws);
                    int cx = c % m.W, cy = c / m.W;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx2 = cx + dx, ny2 = cy + dy;
                            if (nx2 < 0 || ny2 < 0 || nx2 >= m.W || ny2 >= m.H) continue;
                            int n = ny2 * m.W + nx2;
                            if (cand.ContainsKey(n) && seen.Add(n)) q.Enqueue(n);
                        }
                }
                groups.Add(g);
            }
            return groups;
        }

        /// <summary>The cells to close: the whole bridge or ford (its ground, flood-filled), or the pass's walkable cross-section at each of its cells.</summary>
        private static List<int> Closing(WalkMap m, Candidate g)
        {
            var closed = new HashSet<int>();
            if (g.CrossingCells > 0)
            {
                var q = new Queue<int>(g.Cells.Where(c => Ground.IsCrossing(m.Ground[c])));
                foreach (int c in q) closed.Add(c);
                while (q.Count > 0 && closed.Count < 4000)
                {
                    int c = q.Dequeue(), cx = c % m.W, cy = c / m.W;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= m.W || ny >= m.H) continue;
                            int n = ny * m.W + nx;
                            if (Ground.IsCrossing(m.Ground[n]) && closed.Add(n)) q.Enqueue(n);
                        }
                }
                return closed.ToList();
            }
            foreach ((int c, float nx, float ny) in g.Across)
            {
                float x = m.CenterX(c), y = m.CenterY(c);
                closed.Add(c);
                foreach (int sign in new[] { 1, -1 })
                    for (float d = 0.5f; d <= 20f; d += 0.5f)
                    {
                        int i = m.Index(x + sign * nx * d, y + sign * ny * d);
                        if (i < 0 || !m.Walk[i]) break;
                        closed.Add(i);
                    }
            }
            return closed.ToList();
        }

        private static float CrossingWidth(WalkMap m, Candidate g)
        {
            var cells = g.Cells.Where(c => Ground.IsCrossing(m.Ground[c])).ToList();
            if (cells.Count == 0) return 0;
            float minX = cells.Min(c => m.CenterX(c)), maxX = cells.Max(c => m.CenterX(c)), minY = cells.Min(c => m.CenterY(c)), maxY = cells.Max(c => m.CenterY(c));
            return Math.Max(m.Cell, Math.Min(maxX - minX, maxY - minY) + m.Cell);
        }

        private static List<AtlasCrossing> Merge(List<AtlasCrossing> all)
        {
            var kept = new List<AtlasCrossing>();
            foreach (AtlasCrossing c in all.OrderByDescending(c => c.CutsOff).ThenByDescending(c => c.Detour))
            {
                AtlasCrossing twin = kept.FirstOrDefault(k => k.Kind == c.Kind && WalkMap.Dist(k.X, k.Y, c.X, c.Y) < 6f);
                if (twin != null) { twin.Ways = Math.Max(twin.Ways, c.Ways); continue; }
                kept.Add(c);
            }
            return kept;
        }
    }
}
