using System.Diagnostics;

namespace BannerlordAgents.Routes
{
    /// <summary>
    /// What a step onto each kind of ground costs the way-finder. The game's pathfinder takes the shortest way over the
    /// walkable ground of its walking map (MobileParty.ComputePath, with an extra cost of 2 the engine applies to some faces
    /// we cannot read). Equal is that shortest way; the other two are the guesses the accuracy check weighs against the
    /// game's own routes.
    /// </summary>
    public sealed class Costs
    {
        public readonly string Name;
        public readonly float[] ByGround = new float[256];

        public Costs(string name, params (byte ground, float cost)[] dear)
        {
            Name = name;
            for (int i = 0; i < 256; i++) ByGround[i] = 1f;
            foreach ((byte g, float c) in dear) ByGround[g] = c;
        }

        public static readonly Costs Equal = new Costs("shortest");
        public static readonly Costs ForestDear = new Costs("forest counted twice", (Ground.Forest, 2f));
        public static readonly Costs CrossingDear = new Costs("fords and bridges counted twice", (Ground.Fording, 2f), (Ground.Bridge, 2f));
        public static readonly Costs[] Guesses = { Equal, ForestDear, CrossingDear };

        public static Costs ByName(string name) => Guesses.FirstOrDefault(c => c.Name == name) ?? Equal;
    }

    /// <summary>A bridge or ford the way passes over: where, what ground, and how far along the way it comes.</summary>
    public sealed class RouteCrossing
    {
        public byte Ground;
        public float X, Y, Length, At;
    }

    public sealed class RouteResult
    {
        public bool Ok;
        public string Why;
        public float FromX, FromY, ToX, ToY;
        public List<float[]> Points = new List<float[]>();
        public List<int> Cells = new List<int>();
        public float Length, Straight;
        /// <summary>Map units of the way on each kind of ground.</summary>
        public Dictionary<byte, float> OnGround = new Dictionary<byte, float>();
        public List<RouteCrossing> Crossings = new List<RouteCrossing>();
        /// <summary>The narrowest walkable width the way passes through (map units across), and where; +inf when it never narrows below 30.</summary>
        public float NarrowWidth = float.PositiveInfinity, NarrowX, NarrowY;
        public int Expanded;
        public double Ms;
    }

    /// <summary>
    /// The shortest way between two points across the walking map: A* over the cells (eight ways, no cutting a blocked
    /// corner), then pulled straight wherever the ground between two turns is open, as the game's own path is a string of
    /// straight legs between corners. Not thread-safe: one finder per thread.
    /// </summary>
    public sealed class Finder
    {
        private readonly WalkMap _m;
        private readonly float[] _g;
        private readonly int[] _parent, _seen, _closed;
        private int _gen;
        private readonly PriorityQueue<int, float> _open = new PriorityQueue<int, float>(1 << 16);
        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float Diag = (float)Math.Sqrt(2);

        public int MaxExpand = 6_000_000;

        public Finder(WalkMap m)
        {
            _m = m;
            int n = m.W * m.H;
            _g = new float[n];
            _parent = new int[n];
            _seen = new int[n];
            _closed = new int[n];
        }

        public WalkMap Map => _m;

        /// <summary>The way from one point to another. blocked: cells closed for this search (the crossing atlas closes a crossing to see the detour).</summary>
        public RouteResult Find(float x0, float y0, float x1, float y1, Costs costs = null, bool[] blocked = null, bool analyse = true)
        {
            costs ??= Costs.Equal;
            var sw = Stopwatch.StartNew();
            var r = new RouteResult { FromX = x0, FromY = y0, ToX = x1, ToY = y1, Straight = WalkMap.Dist(x0, y0, x1, y1) };
            int s = _m.NearestWalkable(x0, y0, 12, blocked), t = _m.NearestWalkable(x1, y1, 12, blocked);
            if (s < 0) { r.Why = "the start is not on walkable ground"; return r; }
            if (t < 0) { r.Why = "the destination is not on walkable ground"; return r; }
            if (!Search(s, t, costs, blocked, out int expanded))
            {
                r.Expanded = expanded;
                r.Why = expanded >= MaxExpand ? "the search gave up (too far)" : "no way there over walkable ground";
                r.Ms = sw.Elapsed.TotalMilliseconds;
                return r;
            }
            r.Expanded = expanded;
            for (int c = t; ; c = _parent[c])
            {
                r.Cells.Add(c);
                if (c == s) break;
            }
            r.Cells.Reverse();
            r.Points = Pull(r.Cells, x0, y0, x1, y1, costs, blocked);
            r.Length = 0;
            for (int i = 1; i < r.Points.Count; i++) r.Length += WalkMap.Dist(r.Points[i - 1][0], r.Points[i - 1][1], r.Points[i][0], r.Points[i][1]);
            r.Ok = true;
            if (analyse) Analyse(r);
            r.Ms = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        /// <summary>A way given by its corners (such as the one the game planned), described like a found one: its ground, crossings and narrowest place.</summary>
        public RouteResult FromPoints(List<float[]> pts)
        {
            var r = new RouteResult { Ok = pts != null && pts.Count >= 2, Points = pts ?? new List<float[]>() };
            if (!r.Ok) { r.Why = "a way needs two points"; return r; }
            r.FromX = pts[0][0]; r.FromY = pts[0][1];
            r.ToX = pts[pts.Count - 1][0]; r.ToY = pts[pts.Count - 1][1];
            r.Straight = WalkMap.Dist(r.FromX, r.FromY, r.ToX, r.ToY);
            for (int i = 1; i < pts.Count; i++) r.Length += WalkMap.Dist(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1]);
            Analyse(r);
            return r;
        }

        /// <summary>The length of the way only (no analysis), or NaN when there is none.</summary>
        public float Distance(float x0, float y0, float x1, float y1, Costs costs = null, bool[] blocked = null)
        {
            RouteResult r = Find(x0, y0, x1, y1, costs, blocked, false);
            return r.Ok ? r.Length : float.NaN;
        }

        private bool Search(int s, int t, Costs costs, bool[] blocked, out int expanded)
        {
            expanded = 0;
            if (++_gen == int.MaxValue) { Array.Clear(_seen); Array.Clear(_closed); _gen = 1; }
            int gen = _gen, w = _m.W, h = _m.H;
            float cell = _m.Cell;
            int tx = t % w, ty = t / w;
            bool[] walk = _m.Walk;
            byte[] ground = _m.Ground;
            float[] cost = costs.ByGround;
            _open.Clear();
            _g[s] = 0;
            _parent[s] = s;
            _seen[s] = gen;
            _open.Enqueue(s, Heur(s % w, s / w, tx, ty) * cell);
            while (_open.TryDequeue(out int c, out _))
            {
                if (_closed[c] == gen) continue;
                _closed[c] = gen;
                if (c == t) return true;
                if (++expanded >= MaxExpand) return false;
                int cx = c % w, cy = c / w;
                float gc = _g[c];
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], ny = cy + Dy[k];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int n = ny * w + nx;
                    if (!walk[n] || (blocked != null && blocked[n]) || _closed[n] == gen) continue;
                    if (k >= 4)
                    {
                        // no cutting a corner past blocked ground
                        int a = cy * w + nx, b = ny * w + cx;
                        if (!walk[a] || !walk[b] || (blocked != null && (blocked[a] || blocked[b]))) continue;
                    }
                    float step = (k >= 4 ? Diag : 1f) * cell * cost[ground[n]];
                    float gn = gc + step;
                    if (_seen[n] == gen && gn >= _g[n]) continue;
                    _seen[n] = gen;
                    _g[n] = gn;
                    _parent[n] = c;
                    _open.Enqueue(n, gn + Heur(nx, ny, tx, ty) * cell);
                }
            }
            return false;
        }

        private static float Heur(int x, int y, int tx, int ty)
        {
            int dx = Math.Abs(x - tx), dy = Math.Abs(y - ty);
            return dx > dy ? dx + (Diag - 1) * dy : dy + (Diag - 1) * dx;
        }

        /// <summary>The cells' staircase pulled into straight legs, each leg only over open ground no dearer than the stretch it replaces.</summary>
        private List<float[]> Pull(List<int> cells, float x0, float y0, float x1, float y1, Costs costs, bool[] blocked)
        {
            var pts = new List<float[]>(cells.Count);
            for (int i = 0; i < cells.Count; i++) pts.Add(new[] { _m.CenterX(cells[i]), _m.CenterY(cells[i]) });
            // the true ends, when they stand on walkable ground themselves
            if (_m.WalkableAt(x0, y0)) pts[0] = new[] { x0, y0 };
            if (_m.WalkableAt(x1, y1)) pts[pts.Count - 1] = new[] { x1, y1 };
            if (pts.Count <= 2) return pts;
            float[] cost = costs.ByGround;
            var outPts = new List<float[]> { pts[0] };
            int anchor = 0;
            float dearest = 0;
            int i2 = anchor + 1;
            while (i2 < pts.Count)
            {
                dearest = Math.Max(dearest, cost[_m.Ground[cells[i2]]]);
                if (Clear(pts[anchor], pts[i2], dearest, cost, blocked)) { i2++; continue; }
                // the last point still seen from the anchor becomes a corner
                int corner = Math.Max(anchor + 1, i2 - 1);
                outPts.Add(pts[corner]);
                anchor = corner;
                dearest = cost[_m.Ground[cells[anchor]]];
                i2 = anchor + 1;
            }
            outPts.Add(pts[pts.Count - 1]);
            return outPts;
        }

        /// <summary>Is the straight line between two points all walkable ground, no dearer than the limit? Checked every quarter cell.</summary>
        public bool Clear(float[] a, float[] b, float limit, float[] cost, bool[] blocked)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1];
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(len / (_m.Cell * 0.25f)));
            for (int k = 0; k <= steps; k++)
            {
                float f = k / (float)steps;
                int i = _m.Index(a[0] + dx * f, a[1] + dy * f);
                if (i < 0 || !_m.Walk[i] || (blocked != null && blocked[i])) return false;
                if (cost != null && cost[_m.Ground[i]] > limit + 1e-4f) return false;
            }
            return true;
        }

        /// <summary>The ground along the way, the bridges and fords it crosses, and the narrowest place it passes.</summary>
        private void Analyse(RouteResult r)
        {
            const float step = 0.5f;
            float along = 0;
            RouteCrossing open = null;
            float nextNarrow = 3f;
            for (int i = 1; i < r.Points.Count; i++)
            {
                float[] a = r.Points[i - 1], b = r.Points[i];
                float dx = b[0] - a[0], dy = b[1] - a[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) continue;
                float ux = dx / len, uy = dy / len;
                for (float d = 0; d < len; d += step)
                {
                    float x = a[0] + ux * d, y = a[1] + uy * d, piece = Math.Min(step, len - d);
                    byte gr = _m.GroundAt(x, y);
                    r.OnGround[gr] = (r.OnGround.TryGetValue(gr, out float v) ? v : 0) + piece;
                    if (Ground.IsCrossing(gr))
                    {
                        if (open == null || open.Ground != gr) { open = new RouteCrossing { Ground = gr, At = along + d }; r.Crossings.Add(open); }
                        open.X += x * piece;
                        open.Y += y * piece;
                        open.Length += piece;
                    }
                    else open = null;
                    float pos = along + d;
                    if (pos >= nextNarrow && pos <= r.Length - 3f && _m.WalkableAt(x, y))
                    {
                        nextNarrow = pos + 2f;
                        float wdt = Across(x, y, -uy, ux, 30f);
                        if (wdt < r.NarrowWidth) { r.NarrowWidth = wdt; r.NarrowX = x; r.NarrowY = y; }
                    }
                }
                along += len;
            }
            foreach (RouteCrossing c in r.Crossings)
                if (c.Length > 0) { c.X /= c.Length; c.Y /= c.Length; }
        }

        /// <summary>How wide the walkable ground is across a point, along a direction (both ways, each up to most).</summary>
        public float Across(float x, float y, float nx, float ny, float most)
        {
            return Reach(x, y, nx, ny, most) + Reach(x, y, -nx, -ny, most);
        }

        private float Reach(float x, float y, float nx, float ny, float most)
        {
            const float step = 0.5f;
            for (float d = step; d <= most; d += step)
                if (!_m.WalkableAt(x + nx * d, y + ny * d)) return d - step * 0.5f;
            return most;
        }
    }
}
