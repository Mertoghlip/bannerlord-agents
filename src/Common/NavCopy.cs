using System;
using System.Collections.Generic;
using System.IO;

namespace BannerlordAgents.Common
{
    /// <summary>
    /// The campaign map's walking mesh as the engine holds it (docs/PLAN-ways.md, Phase 2): copied out of TaleWorlds.Native's memory once per map (the bridge's
    /// NavMeshDump), saved as navmesh.bin beside the walking map. Faces have up to four corners; each edge joins two corners; a face knows its neighbour across
    /// each edge, its extra cost, island, group (the ground type), ability bits and flags, as the engine's own way-finder reads them.
    /// </summary>
    public sealed class NavMesh
    {
        public const int Version = 1, MaxCorners = 4;
        public string Crc;
        // corners: position and the engine's flag byte (0x80: on the mesh's edge)
        public float[] VX, VY;
        public byte[] VFlags;
        // edges: their two corners and the engine's kind byte
        public int[] EA, EB;
        public byte[] EKind;
        // faces: corner count; MaxCorners edges and neighbours each (-1 none); the rest as the engine keeps them
        public byte[] FN;
        public int[] FEdge, FNbr;
        public float[] FCost, FD8, FE4;
        public int[] FIsland, FGroup, FAbility, FSpecialEdge;
        public byte[] FOneWay, FFlags;
        // extra links (special faces): for face f, FLinks[FLinkStart[f] .. FLinkStart[f + 1])
        public int[] FLinkStart, FLinks;
        // the mesh's own settings: the radius switch, the ability bits a search asks for, each group's region (land 0, sea 1)
        public bool RadiusOn;
        public int AbilityMask;
        public byte[] GroupRegion;
        // the ground a land party may not walk on (PartyNavigationModel's invalid terrain types for the default navigation), as copied
        public int[] PartyExcluded;
        // the region switch costs a land party's way passes (MobileParty.ComputePath: from sea to land as the cost into region 0, from land to sea into region 1)
        public int PartyCostTo0, PartyCostTo1;

        public int Faces => FN?.Length ?? 0;

        /// <summary>One of the two faces an edge lies between (side 0 or 1; -1 when it lies on the mesh's edge), as the engine keeps them at the edge's +0x10 and +0x18.</summary>
        public int EdgeFace(int e, int side)
        {
            if (_edgeFaces == null)
            {
                var ef = new int[EA.Length * 2];
                for (int i = 0; i < ef.Length; i++) ef[i] = -1;
                for (int f = 0; f < Faces; f++)
                    for (int i = 0; i < FN[f]; i++) { int x = FEdge[f * MaxCorners + i]; if (ef[x * 2] < 0) ef[x * 2] = f; else if (ef[x * 2] != f) ef[x * 2 + 1] = f; }
                _edgeFaces = ef;
            }
            return _edgeFaces[e * 2 + side];
        }
        private int[] _edgeFaces;

        public float CenterX(int f) { float s = 0; for (int i = 0; i < FN[f]; i++) s += VX[EA[FEdge[f * MaxCorners + i]]] + VX[EB[FEdge[f * MaxCorners + i]]]; return s / (2 * Math.Max(1, (int)FN[f])); }
        public float CenterY(int f) { float s = 0; for (int i = 0; i < FN[f]; i++) s += VY[EA[FEdge[f * MaxCorners + i]]] + VY[EB[FEdge[f * MaxCorners + i]]]; return s / (2 * Math.Max(1, (int)FN[f])); }

        /// <summary>
        /// The flag bit the engine gave the party's set of closed ground (its handle, given when the set was first used, so not fixed): the bit (0 to 6) whose corners
        /// cover the most corners of faces the party may not enter, the fewest others breaking ties; -1 when no bit covers any (the set was not used yet when copied).
        /// </summary>
        public int PartyCornerBit(NavQuery q)
        {
            if (_partyBit != -2) return _partyBit;
            var closed = new bool[VX.Length];
            for (int f = 0; f < Faces; f++)
                if (!NavCopy.Open(this, q, f)) for (int i = 0; i < FN[f]; i++) { int e = FEdge[f * MaxCorners + i]; closed[EA[e]] = true; closed[EB[e]] = true; }
            int best = -1, bestHit = 0, bestOther = int.MaxValue;
            for (int bit = 0; bit < 7; bit++)
            {
                int hit = 0, other = 0;
                for (int v = 0; v < VX.Length; v++) if ((VFlags[v] & (1 << bit)) != 0) { if (closed[v]) hit++; else other++; }
                if (hit > bestHit || (hit == bestHit && hit > 0 && other < bestOther)) { best = bit; bestHit = hit; bestOther = other; }
            }
            return _partyBit = best;
        }
        private int _partyBit = -2;

        /// <summary>
        /// Corner i of a face as the engine keeps it (+0x80 + i x 8): the corner its edge slots i - 1 and i share, so edge slot i runs from corner i to corner i + 1,
        /// counter-clockwise round the face (checked on the whole 1.4.8 campaign map, Nov 19: every face).
        /// </summary>
        public int Ring(int f, int i)
        {
            if (_ring == null) BuildRing();
            return _ring[f * MaxCorners + i];
        }
        private int[] _ring;

        private void BuildRing()
        {
            var ring = new int[Faces * MaxCorners];
            for (int f = 0; f < Faces; f++)
            {
                int n = FN[f];
                for (int i = 0; i < n; i++)
                {
                    int e0 = FEdge[f * MaxCorners + (i + n - 1) % n], e1 = FEdge[f * MaxCorners + i];
                    ring[f * MaxCorners + i] = EA[e1] == EA[e0] || EA[e1] == EB[e0] ? EA[e1] : EB[e1];
                }
            }
            _ring = ring;
        }

        public byte Region(int group) => GroupRegion != null && group >= 0 && group < GroupRegion.Length ? GroupRegion[group] : (byte)0;

        // ---------- saving and loading ----------

        public void Save(string path)
        {
            string tmp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write("NAVM"); w.Write(Version); w.Write(Crc ?? "");
                WriteF(w, VX); WriteF(w, VY); WriteB(w, VFlags);
                WriteI(w, EA); WriteI(w, EB); WriteB(w, EKind);
                WriteB(w, FN); WriteI(w, FEdge); WriteI(w, FNbr); WriteF(w, FCost); WriteF(w, FD8); WriteF(w, FE4);
                WriteI(w, FIsland); WriteI(w, FGroup); WriteI(w, FAbility); WriteI(w, FSpecialEdge); WriteB(w, FOneWay); WriteB(w, FFlags);
                WriteI(w, FLinkStart); WriteI(w, FLinks);
                w.Write(RadiusOn); w.Write(AbilityMask); WriteB(w, GroupRegion); WriteI(w, PartyExcluded); w.Write(PartyCostTo0); w.Write(PartyCostTo1);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static NavMesh Load(string path)
        {
            if (!File.Exists(path)) return null;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadString() != "NAVM" || r.ReadInt32() != Version) return null;
                var m = new NavMesh { Crc = r.ReadString() };
                m.VX = ReadF(r); m.VY = ReadF(r); m.VFlags = ReadB(r);
                m.EA = ReadI(r); m.EB = ReadI(r); m.EKind = ReadB(r);
                m.FN = ReadB(r); m.FEdge = ReadI(r); m.FNbr = ReadI(r); m.FCost = ReadF(r); m.FD8 = ReadF(r); m.FE4 = ReadF(r);
                m.FIsland = ReadI(r); m.FGroup = ReadI(r); m.FAbility = ReadI(r); m.FSpecialEdge = ReadI(r); m.FOneWay = ReadB(r); m.FFlags = ReadB(r);
                m.FLinkStart = ReadI(r); m.FLinks = ReadI(r);
                m.RadiusOn = r.ReadBoolean(); m.AbilityMask = r.ReadInt32(); m.GroupRegion = ReadB(r); m.PartyExcluded = ReadI(r); m.PartyCostTo0 = r.ReadInt32(); m.PartyCostTo1 = r.ReadInt32();
                return m;
            }
        }

        private static void WriteF(BinaryWriter w, float[] a) { w.Write(a?.Length ?? 0); if (a != null) foreach (float v in a) w.Write(v); }
        private static void WriteI(BinaryWriter w, int[] a) { w.Write(a?.Length ?? 0); if (a != null) foreach (int v in a) w.Write(v); }
        private static void WriteB(BinaryWriter w, byte[] a) { w.Write(a?.Length ?? 0); if (a != null) w.Write(a); }
        private static float[] ReadF(BinaryReader r) { int n = r.ReadInt32(); var a = new float[n]; for (int i = 0; i < n; i++) a[i] = r.ReadSingle(); return a; }
        private static int[] ReadI(BinaryReader r) { int n = r.ReadInt32(); var a = new int[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt32(); return a; }
        private static byte[] ReadB(BinaryReader r) { int n = r.ReadInt32(); return r.ReadBytes(n); }

        // ---------- which face holds a point ----------

        private List<int>[] _cells;
        private float _cx0, _cy0, _cs = 4f;
        private int _cw, _ch;

        /// <summary>The face holding a point (-1 none): the faces whose bounds cover its cell, tested corner by corner.</summary>
        public int FaceAt(float x, float y)
        {
            if (_cells == null) BuildCells();
            int cx = (int)Math.Floor((x - _cx0) / _cs), cy = (int)Math.Floor((y - _cy0) / _cs);
            if (cx < 0 || cy < 0 || cx >= _cw || cy >= _ch) return -1;
            List<int> list = _cells[cy * _cw + cx];
            if (list == null) return -1;
            foreach (int f in list) if (Inside(f, x, y)) return f;
            return -1;
        }

        /// <summary>The face's corners in order round it (from its edges, chained end to start).</summary>
        public List<int> Corners(int f)
        {
            var c = new List<int>();
            int n = FN[f];
            if (n == 0) return c;
            int e0 = FEdge[f * MaxCorners];
            c.Add(EA[e0]); c.Add(EB[e0]);
            for (int k = 1; k < n - 1; k++)
            {
                int last = c[c.Count - 1];
                for (int i = 1; i < n; i++)
                {
                    int e = FEdge[f * MaxCorners + i];
                    if (EA[e] == last && !c.Contains(EB[e])) { c.Add(EB[e]); break; }
                    if (EB[e] == last && !c.Contains(EA[e])) { c.Add(EA[e]); break; }
                }
            }
            return c;
        }

        private bool Inside(int f, float x, float y)
        {
            List<int> c = Corners(f);
            bool inside = false;
            for (int i = 0, j = c.Count - 1; i < c.Count; j = i++)
            {
                float xi = VX[c[i]], yi = VY[c[i]], xj = VX[c[j]], yj = VY[c[j]];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        private void BuildCells()
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int v = 0; v < VX.Length; v++) { x0 = Math.Min(x0, VX[v]); y0 = Math.Min(y0, VY[v]); x1 = Math.Max(x1, VX[v]); y1 = Math.Max(y1, VY[v]); }
            _cx0 = x0; _cy0 = y0;
            _cw = Math.Max(1, (int)Math.Ceiling((x1 - x0) / _cs) + 1); _ch = Math.Max(1, (int)Math.Ceiling((y1 - y0) / _cs) + 1);
            var cells = new List<int>[_cw * _ch];
            for (int f = 0; f < Faces; f++)
            {
                List<int> c = Corners(f);
                if (c.Count < 3) continue;
                float fx0 = float.MaxValue, fy0 = float.MaxValue, fx1 = float.MinValue, fy1 = float.MinValue;
                foreach (int v in c) { fx0 = Math.Min(fx0, VX[v]); fy0 = Math.Min(fy0, VY[v]); fx1 = Math.Max(fx1, VX[v]); fy1 = Math.Max(fy1, VY[v]); }
                for (int cy = (int)((fy0 - _cy0) / _cs); cy <= (int)((fy1 - _cy0) / _cs); cy++)
                    for (int cx = (int)((fx0 - _cx0) / _cs); cx <= (int)((fx1 - _cx0) / _cs); cx++)
                        (cells[cy * _cw + cx] ?? (cells[cy * _cw + cx] = new List<int>())).Add(f);
            }
            _cells = cells;
        }
    }

    /// <summary>A way question as the engine's call takes it (MobileParty.ComputePath for a land party: radius 0.3, its closed ground, extra cost multiplier 2, the region switch costs).</summary>
    public sealed class NavQuery
    {
        public float Radius = 0.3f;
        public HashSet<int> Excluded = new HashSet<int>();
        public float ExtraCostMultiplier = 2f;
        /// <summary>The cost added on entering a face of region 0 and of a region other than 0 (the engine's two region switch costs, as the call passes them).</summary>
        public int CostTo0, CostTo1;
        /// <summary>
        /// The corner flag bits that make a corner closed for this question, as the engine tests them (0x80, the mesh's edge, and the bit the engine gave this set of
        /// closed ground when it was first used: bts 0x80, handle); 0 when not known, and the corners of closed faces are worked out instead.
        /// </summary>
        public int CornerMask;

        public static NavQuery Party(NavMesh m)
        {
            var q = new NavQuery { Excluded = new HashSet<int>(m?.PartyExcluded ?? new int[0]), CostTo0 = m?.PartyCostTo0 ?? 0, CostTo1 = m?.PartyCostTo1 ?? 0 };
            int bit = m != null ? m.PartyCornerBit(q) : -1;
            if (bit >= 0) q.CornerMask = 0x80 | (1 << bit);
            return q;
        }
    }

    /// <summary>
    /// Our copy of the engine's own way-finder (TaleWorlds.Native 1.4.8, +0x4E8080 / +0x4F81B0 / +0x417970; docs/PLAN-ways.md): a straight line when the start
    /// face allows it and the line stays on open faces; else A* over the faces, each face reached once and never re-opened, its node the point where the way
    /// crosses into it (the nearest point of the edge, the edge shortened at corners on the mesh's edge by the radius), the step's cost that distance plus the two
    /// faces' extra costs halved times the multiplier plus the region switch cost, the estimate the straight distance to the end; done the moment the end face is
    /// reached; the way then pulled taut through the faces' shortened edges (the shortest way through them is unique).
    /// </summary>
    public static class NavCopy
    {
        /// <summary>
        /// The copy's version: a proof counts only for the version it checked, so a changed copy is proven again (2: the engine's own corner bits; 3: the engine's own
        /// straightening, its line test and its pass over the way, read from +0x4E8080, +0x4F8BC0 and +0x51C3F0).
        /// </summary>
        public const int Version = 3;

        private const float ShrinkBoth = 0.49f, ShrinkOne = 0.99f, CostScale = 0.5f, ShrinkLine = 0.4f;

        private struct Node { public int Face; public float G, F, X, Y; }

        /// <summary>The way from one point to another as the engine would plan it: its corners after the start, the end last; null when there is none.</summary>
        public static List<float[]> Path(NavMesh m, float sx, float sy, float ex, float ey, NavQuery q)
        {
            int s = m.FaceAt(sx, sy), t = m.FaceAt(ex, ey);
            if (s < 0 || t < 0) return null;
            return Path(m, s, t, sx, sy, ex, ey, q);
        }

        public static List<float[]> Path(NavMesh m, int s, int t, float sx, float sy, float ex, float ey, NavQuery q)
        {
            if (m.FIsland[s] != m.FIsland[t]) return null;
            if (s == t) return new List<float[]> { new[] { ex, ey } };
            // the start face allows a straight line unless it is special or flagged so (+0x414120)
            bool mustSearch = (m.FFlags[s] & 4) != 0 || (m.FFlags[s] & 8) != 0;
            if (!mustSearch && LineClear(m, q, s, t, sx, sy, ex - sx, ey - sy)) return new List<float[]> { new[] { ex, ey } };
            List<int> corridor = Search(m, s, t, sx, sy, ex, ey, q);
            if (corridor == null) return null;
            return Pull(m, corridor, sx, sy, ex, ey, q);
        }

        // ---------- the search ----------

        /// <summary>The faces from start to end the engine's A* would pass through, or null.</summary>
        public static List<int> Search(NavMesh m, int s, int t, float sx, float sy, float ex, float ey, NavQuery q)
        {
            int n = m.Faces;
            var parent = new int[n];
            var stamp = new byte[n];   // 0 unseen, 1 open, 2 closed
            for (int i = 0; i < n; i++) parent[i] = -1;
            var heap = new List<Node>();
            byte goalRegion = m.Region(m.FGroup[t]);
            parent[s] = s;
            stamp[s] = 1;
            HeapPush(heap, new Node { Face = s, G = 0, F = Dist(sx, sy, ex, ey), X = sx, Y = sy });
            while (stamp[t] == 0)
            {
                if (heap.Count == 0) return null;
                Node cur = HeapPop(heap);
                int f = cur.Face;
                int skip = OneWayIndex(m, f);
                int nc = m.FN[f];
                for (int i = 0; i < nc; i++)
                {
                    if (i == skip) continue;
                    int nb = m.FNbr[f * NavMesh.MaxCorners + i];
                    if (nb < 0) continue;
                    Try(m, q, heap, parent, stamp, cur, nb, m.FEdge[f * NavMesh.MaxCorners + i], ex, ey, goalRegion);
                }
                if (m.FLinkStart != null && f + 1 < m.FLinkStart.Length)
                    for (int k = m.FLinkStart[f]; k < m.FLinkStart[f + 1]; k++)
                    {
                        int nb = m.FLinks[k];
                        if (nb < 0) continue;
                        int e = (m.FFlags[f] & 4) != 0 ? m.FSpecialEdge[f] : -1;
                        if (!((m.FFlags[f] & 4) != 0 && (m.FFlags[nb] & 4) != 0 && e >= 0)) e = m.FSpecialEdge[nb];
                        if (e < 0) continue;
                        Try(m, q, heap, parent, stamp, cur, nb, e, ex, ey, goalRegion);
                    }
                stamp[f] = 2;
            }
            var corridor = new List<int>();
            for (int f = t; ; f = parent[f])
            {
                corridor.Add(f);
                if (f == s || corridor.Count > n) break;
            }
            corridor.Reverse();
            return corridor[0] == s ? corridor : null;
        }

        /// <summary>The one-way face's edge it is entered by (+0x414180): -1 for a face that is not one-way.</summary>
        private static int OneWayIndex(NavMesh m, int f)
        {
            byte kind = m.FOneWay[f];
            if (kind == 0) return -1;
            int count = 0;
            for (int i = 0; i < m.FN[f]; i++)
            {
                if (m.EKind[m.FEdge[f * NavMesh.MaxCorners + i]] != 2) continue;
                count++;
                if ((kind == 1 && count == 2) || (kind != 1 && count == 1)) return i;
            }
            return -1;
        }

        /// <summary>Whether a face may be entered at all under the question (the expansion's tests, +0x4F8716 to +0x4F87C1, but for the stamp).</summary>
        public static bool Open(NavMesh m, NavQuery q, int nb)
        {
            if ((m.FAbility[nb] & m.AbilityMask) == 0) return false;
            if (m.FGroup[nb] > 0 && q.Excluded.Contains(m.FGroup[nb])) return false;
            byte fl = m.FFlags[nb];
            return (fl & 1) != 0 && (fl & 2) == 0;
        }

        private static void Try(NavMesh m, NavQuery q, List<Node> heap, int[] parent, byte[] stamp, Node cur, int nb, int edge, float ex, float ey, byte goalRegion)
        {
            if (m.FOneWay[nb] != 0)
            {
                int entry = OneWayIndex(m, nb);
                if (entry < 0 || m.FEdge[nb * NavMesh.MaxCorners + entry] != edge) return;
            }
            if (!Open(m, q, nb) || stamp[nb] != 0) return;
            Portal(m, q, edge, out float ax, out float ay, out float bx, out float by, out float dx, out float dy, out float len);
            // the crossing: the portal's point nearest the current node's point
            float t = (cur.X - ax) * dx + (cur.Y - ay) * dy;
            float px, py;
            if (t < 0) { px = ax; py = ay; }
            else if (t > len) { px = bx; py = by; }
            else { px = ax + dx * t; py = ay + dy * t; }
            float g = Dist(cur.X, cur.Y, px, py) + cur.G + (m.FCost[nb] + m.FCost[cur.Face]) * CostScale * q.ExtraCostMultiplier;
            byte rFrom = m.Region(m.FGroup[cur.Face]), rTo = m.Region(m.FGroup[nb]);
            if (rFrom != rTo) g += rFrom != 0 ? q.CostTo1 : q.CostTo0;
            float f = Dist(px, py, ex, ey) + g;
            if (goalRegion != rTo) f += rTo != 0 ? q.CostTo1 : q.CostTo0;
            parent[nb] = cur.Face;
            stamp[nb] = 1;
            HeapPush(heap, new Node { Face = nb, G = g, F = f, X = px, Y = py });
        }

        /// <summary>
        /// An edge as a portal (+0x4F87C7): from corner A to corner B, each end on the mesh's edge (its flag 0x80, or a corner of a closed face for this question)
        /// moved toward the other by min(k x the edge's length, the radius), k = 0.49 when both ends are so, 0.99 when one is; dx, dy the edge's direction; len what remains.
        /// </summary>
        public static void Portal(NavMesh m, NavQuery q, int edge, out float ax, out float ay, out float bx, out float by, out float dx, out float dy, out float len)
        {
            int a = m.EA[edge], b = m.EB[edge];
            ax = m.VX[a]; ay = m.VY[a]; bx = m.VX[b]; by = m.VY[b];
            float full = Dist(ax, ay, bx, by);
            dx = full > 0 ? (bx - ax) / full : 0; dy = full > 0 ? (by - ay) / full : 0;
            len = full;
            if (!m.RadiusOn) return;
            bool fa = Boundary(m, q, a), fb = Boundary(m, q, b);
            float cut = Math.Min((fa && fb ? ShrinkBoth : ShrinkOne) * full, q.Radius);
            if (cut <= 0) return;
            if (fa) { ax += cut * dx; ay += cut * dy; len -= cut; }
            if (fb) { bx -= cut * dx; by -= cut * dy; len -= cut; }
        }

        // a corner's closedness for a question: its own flag (the mesh's edge), or a face round it closed by the question (worked out once per question set)
        private static readonly Dictionary<NavMesh, Dictionary<string, bool[]>> BoundaryCache = new Dictionary<NavMesh, Dictionary<string, bool[]>>();

        private static bool Boundary(NavMesh m, NavQuery q, int v)
        {
            if (q.CornerMask != 0) return (m.VFlags[v] & q.CornerMask) != 0;
            bool[] set;
            string key = string.Join(",", SortedExcluded(q));
            lock (BoundaryCache)
            {
                if (!BoundaryCache.TryGetValue(m, out Dictionary<string, bool[]> byQ)) BoundaryCache[m] = byQ = new Dictionary<string, bool[]>();
                if (!byQ.TryGetValue(key, out set))
                {
                    set = new bool[m.VX.Length];
                    for (int i = 0; i < set.Length; i++) set[i] = (m.VFlags[i] & 0x80) != 0;
                    for (int f = 0; f < m.Faces; f++)
                    {
                        if (m.FGroup[f] <= 0 || !q.Excluded.Contains(m.FGroup[f])) continue;
                        for (int i = 0; i < m.FN[f]; i++) { int e = m.FEdge[f * NavMesh.MaxCorners + i]; set[m.EA[e]] = true; set[m.EB[e]] = true; }
                    }
                    byQ[key] = set;
                }
            }
            return set[v];
        }

        private static List<int> SortedExcluded(NavQuery q) { var l = new List<int>(q.Excluded); l.Sort(); return l; }

        // ---------- the heap, as MSVC's push_heap / pop_heap with greater on f (ties go to the right child) ----------

        private static void HeapPush(List<Node> h, Node v)
        {
            h.Add(v);
            int hole = h.Count - 1;
            while (hole > 0)
            {
                int idx = (hole - 1) >> 1;
                if (!(h[idx].F > v.F)) break;
                h[hole] = h[idx];
                hole = idx;
            }
            h[hole] = v;
        }

        private static Node HeapPop(List<Node> h)
        {
            Node top = h[0];
            int bottom = h.Count - 1;
            Node val = h[bottom];
            h.RemoveAt(bottom);
            if (bottom == 0) return top;
            int hole = 0, idx = 0, maxNonLeaf = (bottom - 1) >> 1;
            while (idx < maxNonLeaf)
            {
                idx = 2 * idx + 2;
                if (h[idx].F > h[idx - 1].F) idx--;
                h[hole] = h[idx];
                hole = idx;
            }
            if (idx == maxNonLeaf && bottom % 2 == 0) { h[hole] = h[bottom - 1]; hole = bottom - 1; }
            while (hole > 0)
            {
                int p = (hole - 1) >> 1;
                if (!(h[p].F > val.F)) break;
                h[hole] = h[p];
                hole = p;
            }
            h[hole] = val;
            return top;
        }

        // ---------- the straight line (+0x50F320, its walk +0x51C3F0; the search's own direct line +0x50F230 walks by the same rules) ----------

        // the walk's visit marks: a fresh generation per line, as the engine's search slot bumps its own (+0x4178C0, by 3)
        private sealed class Marks { public int[] Stamp; public int Gen; }
        [ThreadStatic] private static Dictionary<NavMesh, Marks> _marks;

        private static Marks MarksFor(NavMesh m)
        {
            if (_marks == null) _marks = new Dictionary<NavMesh, Marks>();
            if (!_marks.TryGetValue(m, out Marks k)) _marks[m] = k = new Marks { Stamp = new int[m.Faces] };
            if (++k.Gen == int.MaxValue) { Array.Clear(k.Stamp, 0, k.Stamp.Length); k.Gen = 1; }
            return k;
        }

        /// <summary>
        /// Is the straight line from a point in one face, along a direction, open to the face of its end (+0x50F320)? Its end inside the first face, or the two faces
        /// one, is open at once; otherwise the line is walked face by face through the shortened portals it leaves each face by (+0x51C3F0).
        /// </summary>
        public static bool LineClear(NavMesh m, NavQuery q, int baseFace, int endFace, float bx, float by, float dx, float dy)
        {
            if (baseFace < 0) return false;
            Marks k = MarksFor(m);
            float tx = dx + bx, ty = dy + by;
            bool inside = PointInFace(m, baseFace, tx, ty);
            if (baseFace == endFace) return true;
            if (endFace < 0) { if (inside) return true; }
            else if (inside && dy * dy + dx * dx < 1e-5f) return true;
            if (dy * dy + dx * dx >= 1e-5f) return Walk(m, q, k, baseFace, endFace, bx, by, dx, dy, !PointInFace(m, baseFace, bx, by));
            // too short to walk: the end inside a neighbour the question may enter
            for (int i = 0; i < m.FN[baseFace]; i++)
            {
                int nb = m.FNbr[baseFace * NavMesh.MaxCorners + i];
                if (nb >= 0 && Enterable(m, q, nb, m.FEdge[baseFace * NavMesh.MaxCorners + i]) && PointInFace(m, nb, tx, ty)) return true;
            }
            return false;
        }

        /// <summary>
        /// The engine's walk of a straight line (+0x51C3F0): from a face, every edge whose portal (each closed corner moved min(0.4 x its length, the radius) toward
        /// the other) the line crosses on its way out, into the face beyond when the question may enter it, its ground has no extra cost and the step needs no region
        /// switch that costs; open as soon as it reaches the end face with the end inside it (or, the end lying outside, leaves the end face by any portal). A start
        /// outside its own face (outside: true) first looks for the portal the line enters it by.
        /// </summary>
        private static bool Walk(NavMesh m, NavQuery q, Marks k, int face, int endFace, float bx, float by, float dx, float dy, bool outside)
        {
            if (k.Stamp[face] >= k.Gen) return false;
            bool atEnd = false;
            if (!outside) { k.Stamp[face] = k.Gen; atEnd = face == endFace; }
            int skip = OneWayIndex(m, face), n = m.FN[face];
            for (int i = 0; i < n; i++)
            {
                if (i == skip) continue;
                int nb = m.FNbr[face * NavMesh.MaxCorners + i];
                if (nb < 0 && !atEnd) continue;
                int edge = m.FEdge[face * NavMesh.MaxCorners + i];
                if (!outside && !atEnd && m.EKind[edge] <= 1) continue;
                int other = outside || atEnd ? face : nb;
                if (!Enterable(m, q, other, edge)) continue;
                byte rf = m.Region(m.FGroup[face]), ro = m.Region(m.FGroup[other]);
                if (rf != ro && (rf != 0 ? q.CostTo1 : q.CostTo0) != 0) continue;
                int a = m.EA[edge], b = m.EB[edge];
                float ax = m.VX[a], ay = m.VY[a], bxx = m.VX[b], byy = m.VY[b];
                ShortenLine(m, q, a, b, ref ax, ref ay, ref bxx, ref byy);
                if (!Hits(ax, ay, bxx - ax, byy - ay, bx, by, dx, dy)) continue;
                float tx = dx + bx, ty = dy + by;
                bool inside = PointInFace(m, other, tx, ty), found;
                if ((inside && endFace < 0) || other == endFace) found = true;
                else
                {
                    found = false;
                    if (0f < m.FCost[other]) continue;
                }
                // only an edge the line leaves the face by (or, from outside, enters it by)
                float ddx = dx, ddy = dy;
                if (outside && !found) { ddx = -dx; ddy = -dy; }
                int ci = m.Ring(face, i), cn = m.Ring(face, (i + 1) % n);
                float dot = (m.VY[ci] - m.VY[cn]) * ddx + (m.VX[cn] - m.VX[ci]) * ddy;
                if (dot > 0.0001f) continue;
                if (found) return atEnd || inside || Walk(m, q, k, endFace, endFace, bx, by, dx, dy, false);
                if (Walk(m, q, k, other, endFace, bx, by, dx, dy, false)) return true;
            }
            return false;
        }

        /// <summary>Whether a face may be entered across an edge (+0x41F8D0): a one-way face only by its own entry edge, and the question's ground tests.</summary>
        private static bool Enterable(NavMesh m, NavQuery q, int f, int edge)
        {
            if (m.FOneWay[f] != 0)
            {
                int entry = OneWayIndex(m, f);
                if (entry < 0 || m.FEdge[f * NavMesh.MaxCorners + entry] != edge) return false;
            }
            return Open(m, q, f);
        }

        /// <summary>
        /// Does a point lie in a face (+0x416370)? On the inner side of every edge, counter-clockwise from corner i to corner i + 1, within 0.0025 (as twice the
        /// area). The engine first tries two circles round the face's centre (inside the inner, outside the outer); they only save time.
        /// </summary>
        public static bool PointInFace(NavMesh m, int f, float x, float y)
        {
            int n = m.FN[f];
            for (int i = 0; i < n; i++)
            {
                int ci = m.Ring(f, i), cn = m.Ring(f, (i + 1) % n);
                float cx = m.VX[ci], cy = m.VY[ci];
                if ((m.VX[cn] - cx) * (y - cy) - (m.VY[cn] - cy) * (x - cx) < -0.0025f) return false;
            }
            return n >= 3;
        }

        /// <summary>
        /// Do two segments meet (+0x41F970): from (px, py) along (ex, ey), and from (qx, qy) along (fx, fy), each within 0.001 of its length past its ends. Lying on
        /// one line, they meet when either's direction projected on the other falls within it, as the engine has it.
        /// </summary>
        private static bool Hits(float px, float py, float ex, float ey, float qx, float qy, float fx, float fy)
        {
            float den = ex * fy - fx * ey;
            float wx = qx - px, wy = qy - py;
            float nt = wx * fy - wy * fx;
            if (den != 0f)
            {
                float inv = 1f / den;
                float t = inv * nt, u = (wx * ey - wy * ex) * inv;
                if (t < -0.001f || t > 1.001f || u < -0.001f || u > 1.001f) return false;
                if (Math.Min(u, t) > 1f) return false;
                return Math.Max(u, t) >= 0f;
            }
            if (nt != 0f || wx * ey - wy * ex != 0f) return false;
            float ff = fy * fy + fx * fx, rf = Rsqrt(ff);
            float pe = fy * rf * ey + fx * rf * ex;
            if (pe >= 0f && pe <= rf * ff) return true;
            float ee = ey * ey + ex * ex, re = Rsqrt(ee);
            float pf = ex * re * fx + ey * re * fy;
            return pf >= 0f && pf <= re * ee;
        }

        /// <summary>The engine's 1/sqrt: its quick estimate refined once (rsqrtss and one Newton step); the estimate here is exact, the step the engine's own.</summary>
        private static float Rsqrt(float x)
        {
            float r = (float)(1.0 / Math.Sqrt(x));
            return 0.5f * r * (3f - x * (r * r));
        }

        /// <summary>
        /// A portal as the line test and the straightening narrow it (+0x51C833, +0x4F8DFB): each closed corner moved toward the other by min(0.4 x the edge's
        /// length, the radius), whether one end or both are closed (the search's own crossing points use 0.49 and 0.99 instead).
        /// </summary>
        private static void ShortenLine(NavMesh m, NavQuery q, int a, int b, ref float ax, ref float ay, ref float bx, ref float by)
        {
            if (!m.RadiusOn) return;
            bool fa = Boundary(m, q, a), fb = Boundary(m, q, b);
            if (!fa && !fb) return;
            float dx = ax - bx, dy = ay - by, len2 = dx * dx + dy * dy, r = Rsqrt(len2);
            float ux = dx * r, uy = dy * r, cut = Math.Min(r * len2 * ShrinkLine, q.Radius);
            if (fa) { ax -= cut * ux; ay -= cut * uy; }
            if (fb) { bx += cut * ux; by += cut * uy; }
        }

        // ---------- pulling the way taut (+0x417970 lays out the doorways, +0x4E8080 runs the straightening +0x4F8BC0 over them and keeps the bends) ----------

        private struct Door { public int Edge, Face; public float X, Y; public bool Bend; }

        /// <summary>
        /// The way through the corridor as the engine draws it: its bends after the start, the end last. One entry per doorway (the edge between corridor faces k and
        /// k + 1, held by face k) and one for the end; the straightening marks bends, the pass keeps only them.
        /// </summary>
        public static List<float[]> Pull(NavMesh m, List<int> corridor, float sx, float sy, float ex, float ey, NavQuery q)
        {
            int n = corridor.Count;
            if (n == 0) return null;
            var d = new Door[n];
            for (int k = 0; k + 1 < n; k++)
            {
                int edge = SharedEdge(m, corridor[k], corridor[k + 1]);
                if (edge < 0) return null;
                d[k] = new Door { Edge = edge, Face = corridor[k] };
            }
            d[n - 1] = new Door { Edge = -1, Face = corridor[n - 1], X = ex, Y = ey, Bend = true };
            Sections(m, q, d, sx, sy);
            var path = new List<float[]>();
            foreach (Door x in d) if (x.Bend) path.Add(new[] { x.X, x.Y });
            return path;
        }

        private static bool Flag8(NavMesh m, int f) => f >= 0 && (m.FFlags[f] & 8) != 0;

        private static void Fix(NavMesh m, Door[] d, int i)
        {
            if (d[i].Edge >= 0) { int a = m.EA[d[i].Edge], b = m.EB[d[i].Edge]; d[i].X = (m.VX[a] + m.VX[b]) * 0.5f; d[i].Y = (m.VY[a] + m.VY[b]) * 0.5f; }
            else if (d[i].Face >= 0) { d[i].X = m.CenterX(d[i].Face); d[i].Y = m.CenterY(d[i].Face); }
        }

        /// <summary>
        /// The pass over the doorways (+0x4E8080): a doorway into or out of a face flagged 8 is a fixed point at its edge's middle (the end, in such a face, its centre);
        /// the straightening runs on each stretch between fixed points, from the last one.
        /// </summary>
        private static void Sections(NavMesh m, NavQuery q, Door[] d, float sx, float sy)
        {
            int count = d.Length;
            if (count <= 1) return;
            int from = 0, last = 0;
            float px = sx, py = sy;
            while (true)
            {
                int fixedAt = -1;
                for (int i = from + 1; i < count; i++)
                {
                    last = i;
                    bool atEdge = d[i].Edge >= 0 && (Flag8(m, m.EdgeFace(d[i].Edge, 0)) || Flag8(m, m.EdgeFace(d[i].Edge, 1)));
                    if (atEdge || Flag8(m, d[i].Face))
                    {
                        fixedAt = i;
                        if (atEdge) { int a = m.EA[d[i].Edge], b = m.EB[d[i].Edge]; d[i].X = (m.VX[a] + m.VX[b]) * 0.5f; d[i].Y = (m.VY[a] + m.VY[b]) * 0.5f; }
                        else { d[i].X = m.CenterX(d[i].Face); d[i].Y = m.CenterY(d[i].Face); }
                        break;
                    }
                }
                if (fixedAt >= 0)
                {
                    if (from + 1 < fixedAt) Straighten(m, q, d, from, fixedAt, px, py);
                    if (!d[fixedAt].Bend) { d[fixedAt].Bend = true; Fix(m, d, fixedAt); }
                    px = d[fixedAt].X; py = d[fixedAt].Y;
                    last = fixedAt - 1;
                }
                else if (from != last)
                {
                    Straighten(m, q, d, from, last + 1, px, py);
                    if (!d[last].Bend) { d[last].Bend = true; Fix(m, d, last); }
                    px = d[last].X; py = d[last].Y;
                }
                else if (!d[last].Bend && d[last].Face >= 0) { d[last].Bend = true; Fix(m, d, last); }
                from = last + 1;
                last = from;
                if (from >= count) break;
            }
        }

        /// <summary>
        /// The engine's straightening of a stretch of doorways [from, to) from a start point (+0x4F8BC0), in three passes: the funnel through the portals (narrowed
        /// as the line test narrows them) marks a bend at each corner the way must turn round; each bend the straight line from the bend before last reaches is dropped,
        /// and the line then tried from up to ten doorways further back; and a region change on the way gets a point where the line crosses it.
        /// </summary>
        private static void Straighten(NavMesh m, NavQuery q, Door[] d, int from, int to, float sx, float sy)
        {
            int total = d.Length, last = to - 1;
            // 1. the funnel, apex-relative
            float apx = sx, apy = sy, lx = 0, ly = 0, rx = 0, ry = 0;
            int leftI = -1, rightI = -1, init = from;
            for (int i = from; i < to;)
            {
                float p1x, p1y, p2x, p2y;
                if (i < last)
                {
                    int a = m.EA[d[i].Edge], b = m.EB[d[i].Edge];
                    float ax = m.VX[a] - apx, ay = m.VY[a] - apy, bx = m.VX[b] - apx, by = m.VY[b] - apy;
                    if ((ax == 0f && ay == 0f) || (bx == 0f && by == 0f)) { init++; i++; continue; }
                    ShortenLine(m, q, a, b, ref ax, ref ay, ref bx, ref by);
                    if (by * ax - bx * ay >= 0f) { p1x = bx; p1y = by; p2x = ax; p2y = ay; }
                    else { p1x = ax; p1y = ay; p2x = bx; p2y = by; }
                }
                else
                {
                    float px, py;
                    if (i == total - 1) { px = d[i].X; py = d[i].Y; }
                    else if (d[i].Edge >= 0) { int a = m.EA[d[i].Edge], b = m.EB[d[i].Edge]; px = (m.VX[a] + m.VX[b]) * 0.5f; py = (m.VY[a] + m.VY[b]) * 0.5f; }
                    else if (d[i].Face >= 0) { px = m.CenterX(d[i].Face); py = m.CenterY(d[i].Face); }
                    else { i++; continue; }
                    p1x = p2x = px - apx; p1y = p2y = py - apy;
                }
                if (i == init) { lx = p1x; ly = p1y; leftI = i; rx = p2x; ry = p2y; rightI = i; i++; continue; }
                bool turnLeft;
                if (p2y * lx - p2x * ly > 0f) turnLeft = true;
                else if (p1y * rx - p1x * ry >= 0f)
                {
                    if (p1y * lx - p1x * ly < 0f) { lx = p1x; ly = p1y; leftI = i; }
                    if (p2y * rx - p2x * ry > 0f) { rx = p2x; ry = p2y; rightI = i; }
                    i++;
                    continue;
                }
                else turnLeft = false;
                int at = turnLeft ? leftI : rightI;
                d[at].X = apx + (turnLeft ? lx : rx); d[at].Y = apy + (turnLeft ? ly : ry); d[at].Bend = true;
                apx = d[at].X; apy = d[at].Y;
                if (at + 1 == last) { i++; continue; }
                init = at + 1;
                i = at + 1;
            }
            // 2. dropping the bends the straight line saves
            int anchor = -1, prev = -1;
            for (int i = from; i < to; i++)
            {
                if (!d[i].Bend) continue;
                if (prev < 0) { prev = i; continue; }
                float bx = anchor >= 0 ? d[anchor].X : sx, by = anchor >= 0 ? d[anchor].Y : sy;
                int baseFace = anchor >= 0 ? d[anchor].Face : d[0].Face;
                if (!LineClear(m, q, baseFace, d[i].Face, bx, by, d[i].X - bx, d[i].Y - by)) { anchor = prev; prev = i; continue; }
                d[prev].Bend = false;
                prev = i;
                for (int k = anchor - 1; k >= Math.Max(anchor - 10, 0); k--)
                {
                    if (!d[k].Bend) continue;
                    if (!LineClear(m, q, d[k].Face, d[i].Face, d[k].X, d[k].Y, d[i].X - d[k].X, d[i].Y - d[k].Y)) break;
                    d[anchor].Bend = false;
                    anchor = k;
                }
            }
            // 3. a point where the way changes region (land and sea)
            float ppx = 0, ppy = 0;
            int prevGroup = -1;
            for (int i = from; i < to; i++)
            {
                int f = d[i].Face;
                if (f < 0) continue;
                if (d[i].Bend || prevGroup == -1)
                {
                    prevGroup = m.FGroup[f];
                    if (d[i].Bend) { ppx = d[i].X; ppy = d[i].Y; } else { ppx = sx; ppy = sy; }
                    continue;
                }
                if (prevGroup < 0 || m.Region(m.FGroup[f]) == m.Region(prevGroup)) continue;
                int next = -1;
                for (int c = i + 1; c < to; c++) if (d[c].Bend) { next = c; break; }
                if (next >= 0 && d[i].Edge >= 0)
                {
                    d[i].Bend = true;
                    int a = m.EA[d[i].Edge], b = m.EB[d[i].Edge];
                    float ax = m.VX[a], ay = m.VY[a], bx = m.VX[b], by = m.VY[b];
                    if (m.RadiusOn && (Boundary(m, q, a) || Boundary(m, q, b)))
                    {
                        float ux = bx - ax, uy = by - ay, len2 = ux * ux + uy * uy, r = Rsqrt(len2);
                        float cut = Math.Min(len2 * r * ShrinkLine, q.Radius);
                        ux *= r; uy *= r;
                        if (Boundary(m, q, a)) { ax += cut * ux; ay += cut * uy; }
                        if (Boundary(m, q, b)) { bx -= cut * ux; by -= cut * uy; }
                    }
                    float wx = bx - ax, wy = by - ay, nx = d[next].X - ppx, ny = d[next].Y - ppy;
                    float den = wx * ny - wy * nx;
                    float t = den == 0f ? -1f : ((ppx - ax) * ny - (ppy - ay) * nx) / den;
                    if (t >= 0f) { t = Math.Min(t, 1f); d[i].X = t * wx + ax; d[i].Y = t * wy + ay; }
                    else { d[i].X = ax; d[i].Y = ay; }
                }
                prevGroup = m.FGroup[f];
                ppx = d[i].X; ppy = d[i].Y;
            }
        }

        private static int SharedEdge(NavMesh m, int a, int b)
        {
            for (int i = 0; i < m.FN[a]; i++) if (m.FNbr[a * NavMesh.MaxCorners + i] == b) return m.FEdge[a * NavMesh.MaxCorners + i];
            if (m.FLinkStart != null && a + 1 < m.FLinkStart.Length)
                for (int k = m.FLinkStart[a]; k < m.FLinkStart[a + 1]; k++)
                    if (m.FLinks[k] == b) return (m.FFlags[a] & 4) != 0 && m.FSpecialEdge[a] >= 0 ? m.FSpecialEdge[a] : m.FSpecialEdge[b];
            return -1;
        }

        // twice the signed area of the triangle (positive: c to the left of a -> b), as the funnel uses it
        private static float Area(float[] a, float[] b, float[] c) => (c[0] - a[0]) * (b[1] - a[1]) - (b[0] - a[0]) * (c[1] - a[1]);
        private static bool Same(float[] a, float[] b) => Math.Abs(a[0] - b[0]) < 1e-5f && Math.Abs(a[1] - b[1]) < 1e-5f;
        private static float Dist(float ax, float ay, float bx, float by) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
    }
}
