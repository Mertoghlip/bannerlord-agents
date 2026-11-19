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

        public float CenterX(int f) { float s = 0; for (int i = 0; i < FN[f]; i++) s += VX[EA[FEdge[f * MaxCorners + i]]] + VX[EB[FEdge[f * MaxCorners + i]]]; return s / (2 * Math.Max(1, (int)FN[f])); }
        public float CenterY(int f) { float s = 0; for (int i = 0; i < FN[f]; i++) s += VY[EA[FEdge[f * MaxCorners + i]]] + VY[EB[FEdge[f * MaxCorners + i]]]; return s / (2 * Math.Max(1, (int)FN[f])); }

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

        public static NavQuery Party(NavMesh m) => new NavQuery { Excluded = new HashSet<int>(m?.PartyExcluded ?? new int[0]), CostTo0 = m?.PartyCostTo0 ?? 0, CostTo1 = m?.PartyCostTo1 ?? 0 };
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
        private const float ShrinkBoth = 0.49f, ShrinkOne = 0.99f, CostScale = 0.5f;

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
            if (!mustSearch && LineClear(m, s, t, sx, sy, ex, ey, q)) return new List<float[]> { new[] { ex, ey } };
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

        // ---------- the straight line ----------

        /// <summary>
        /// Does the straight line from start to end stay on faces the question may enter, crossing each edge inside its shortened portal (+0x50F230, as read)?
        /// Walked face by face from the start: the edge the line leaves by, the face beyond.
        /// </summary>
        public static bool LineClear(NavMesh m, int s, int t, float sx, float sy, float ex, float ey, NavQuery q)
        {
            int f = s, from = -1;
            for (int guard = 0; guard < m.Faces && f != t; guard++)
            {
                int next = -1, nextEdge = -1;
                for (int i = 0; i < m.FN[f]; i++)
                {
                    int e = m.FEdge[f * NavMesh.MaxCorners + i], nb = m.FNbr[f * NavMesh.MaxCorners + i];
                    if (nb == from) continue;
                    if (!Crosses(m, e, sx, sy, ex, ey, out float u)) continue;
                    if (nb < 0 || !Open(m, q, nb)) return false;
                    Portal(m, q, e, out float ax, out float ay, out float bx, out float by, out _, out _, out float len);
                    if (len <= 0) return false;
                    // the crossing must fall inside the shortened portal
                    float cx = sx + (ex - sx) * u, cy = sy + (ey - sy) * u;
                    float along = (cx - ax) * (bx - ax) + (cy - ay) * (by - ay), span = (bx - ax) * (bx - ax) + (by - ay) * (by - ay);
                    if (along < 0 || along > span) return false;
                    next = nb; nextEdge = e;
                    break;
                }
                if (next < 0) return false;
                from = f;
                f = next;
            }
            return f == t;
        }

        /// <summary>Does the segment cross the edge (u: where along the segment, 0 to 1)?</summary>
        private static bool Crosses(NavMesh m, int e, float sx, float sy, float ex, float ey, out float u)
        {
            float ax = m.VX[m.EA[e]], ay = m.VY[m.EA[e]], bx = m.VX[m.EB[e]], by = m.VY[m.EB[e]];
            float rx = ex - sx, ry = ey - sy, qx = bx - ax, qy = by - ay;
            float den = rx * qy - ry * qx;
            u = 0;
            if (Math.Abs(den) < 1e-9f) return false;
            float wx = ax - sx, wy = ay - sy;
            u = (wx * qy - wy * qx) / den;
            float v = (wx * ry - wy * rx) / den;
            return u > 1e-6f && u <= 1f + 1e-6f && v >= -1e-6f && v <= 1f + 1e-6f;
        }

        // ---------- pulling the way taut ----------

        /// <summary>The shortest way from start to end through the corridor's shortened portals (the simple stupid funnel); the corners after the start, the end last.</summary>
        public static List<float[]> Pull(NavMesh m, List<int> corridor, float sx, float sy, float ex, float ey, NavQuery q)
        {
            var lefts = new List<float[]>(); var rights = new List<float[]>();
            lefts.Add(new[] { sx, sy }); rights.Add(new[] { sx, sy });
            for (int i = 0; i + 1 < corridor.Count; i++)
            {
                int a = corridor[i], b = corridor[i + 1];
                int edge = SharedEdge(m, a, b);
                if (edge < 0) return null;
                Portal(m, q, edge, out float ax, out float ay, out float bx, out float by, out _, out _, out _);
                // left and right as seen going from a to b
                float cax = m.CenterX(a), cay = m.CenterY(a), cbx = m.CenterX(b), cby = m.CenterY(b);
                float dxx = cbx - cax, dyy = cby - cay;
                float sideA = dxx * (ay - cay) - dyy * (ax - cax), sideB = dxx * (by - cay) - dyy * (bx - cax);
                if (sideA >= sideB) { lefts.Add(new[] { ax, ay }); rights.Add(new[] { bx, by }); }
                else { lefts.Add(new[] { bx, by }); rights.Add(new[] { ax, ay }); }
            }
            lefts.Add(new[] { ex, ey }); rights.Add(new[] { ex, ey });

            var path = new List<float[]>();
            float[] apex = lefts[0], left = lefts[0], right = rights[0];
            int apexI = 0, leftI = 0, rightI = 0;
            for (int i = 1; i < lefts.Count; i++)
            {
                float[] l = lefts[i], r = rights[i];
                // the right side narrows the funnel
                if (Area(apex, right, r) <= 0)
                {
                    if (Same(apex, right) || Area(apex, left, r) > 0) { right = r; rightI = i; }
                    else
                    {
                        path.Add(left); apex = left; apexI = leftI; left = apex; right = apex; leftI = rightI = apexI;
                        i = apexI;
                        continue;
                    }
                }
                if (Area(apex, left, l) >= 0)
                {
                    if (Same(apex, left) || Area(apex, right, l) < 0) { left = l; leftI = i; }
                    else
                    {
                        path.Add(right); apex = right; apexI = rightI; left = apex; right = apex; leftI = rightI = apexI;
                        i = apexI;
                        continue;
                    }
                }
            }
            if (path.Count == 0 || !Same(path[path.Count - 1], new[] { ex, ey })) path.Add(new[] { ex, ey });
            return path;
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
