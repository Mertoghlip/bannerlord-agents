using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BannerlordAgents.Common;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using Path = System.IO.Path;

namespace BannerlordAgents
{
    /// <summary>
    /// The door for ways (docs/PLAN-ways.md, Phase 2): the copy of the game's own way-finder (Common/NavCopy.cs) over the walking mesh copied out of the engine
    /// (NavMeshDump), once its proof against the game's own ways has passed (NavProof: at least 999 in 1,000 identical); until then a straight line, which every
    /// estimate marks as rough. Loaded at each campaign start from the walking map's folder.
    /// </summary>
    internal static class Ways
    {
        public static NavMesh Mesh { get; private set; }
        public static NavQuery PartyQuery { get; private set; }
        /// <summary>The copy matched the game's own ways in its proof (navcopy-proof.json beside the mesh, its rate at least ProofBar).</summary>
        public static bool Proven { get; private set; }
        public static string Status = "no walking mesh copied yet";
        public const double ProofBar = 0.999;

        public static void Load(string mapDir)
        {
            Mesh = null; PartyQuery = null; Proven = false;
            ForgetWays();
            try
            {
                if (mapDir == null) { Status = "no walking map yet"; return; }
                NavMesh m = NavMesh.Load(Path.Combine(mapDir, "navmesh.bin"));
                if (m == null) { Status = "the walking mesh is not copied yet (it is, after the walking map)"; return; }
                m.FaceAt(0, 0);   // builds the face lookup now, on one thread
                Mesh = m;
                PartyQuery = NavQuery.Party(m);
                string proof = Path.Combine(mapDir, "navcopy-proof.json");
                double rate = ProofFor(proof) ? ReadRate(proof) : double.NaN;
                Proven = !double.IsNaN(rate) && rate >= ProofBar;
                Status = "walking mesh of " + m.Faces + " faces; " + (double.IsNaN(rate) ? "the copy's proof not run yet" : "the copy matched " + (rate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "% of the game's own ways")
                         + (Proven ? ": ways by the copy of the game's way-finder" : ": ways by the straight line until it passes " + (ProofBar * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");
            }
            catch (Exception e) { Diag.Swallowed("loading the walking mesh", e); Status = "the walking mesh could not be read: " + e.Message; }
        }

        public static void SetProven(double rate) { Proven = Mesh != null && rate >= ProofBar; }

        /// <summary>Is the proof file there and of this copy's version (NavCopy.Version)? A proof of another version is run again.</summary>
        public static bool ProofFor(string path)
        {
            try { return File.Exists(path) && File.ReadAllText(path).Contains("\"copy_version\":" + NavCopy.Version + ","); }
            catch (Exception) { return false; }
        }

        private static double ReadRate(string path)
        {
            if (!File.Exists(path)) return double.NaN;
            string s = File.ReadAllText(path);
            int i = s.IndexOf("\"rate\":", StringComparison.Ordinal);
            if (i < 0) return double.NaN;
            int j = i + 7, k = j;
            while (k < s.Length && (char.IsDigit(s[k]) || s[k] == '.' || s[k] == '-' || s[k] == 'E' || s[k] == 'e')) k++;
            return double.TryParse(s.Substring(j, k - j), NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : double.NaN;
        }

        /// <summary>
        /// The way a land party would march from one point to another: by the copy of the game's way-finder when proven (null when the game finds no way), else the
        /// straight line (real = false). The first point is the start.
        /// </summary>
        public static List<float[]> Between(float sx, float sy, float ex, float ey, out bool real)
        {
            real = false;
            if (Proven && Mesh != null)
            {
                real = true;
                float[][] pts = Kept(sx, sy, ex, ey);
                if (pts == null) return null;
                var p = new List<float[]>(pts.Length + 1) { new[] { sx, sy } };
                p.AddRange(pts);
                return p;
            }
            return new List<float[]> { new[] { sx, sy }, new[] { ex, ey } };
        }

        // 0.21.4 (Nov 20, run 20261120-0212 fell 7 in-game hours behind with the reports): a way depends only on its two ends and the mesh, so each one worked out
        // is kept, by the exact ends, in two generations: the newer takes every way asked for, and once it holds KeepWays the older is dropped and the newer becomes
        // the older. Ways from lords standing still (inside a holding, camped, holding a post) and between holdings are asked again every hour and found here.
        private const int KeepWays = 20000;
        private static Dictionary<WayEnds, float[][]> _newer = new Dictionary<WayEnds, float[][]>(), _older = new Dictionary<WayEnds, float[][]>();
        private static readonly object _keptLock = new object();
        /// <summary>Ways asked for, ways worked out (not found kept), and the milliseconds spent working them out, since the start (the speed test reads them).</summary>
        public static long Asked, Worked;
        public static double WorkedMs;

        private static float[][] Kept(float sx, float sy, float ex, float ey)
        {
            var k = new WayEnds(sx, sy, ex, ey);
            lock (_keptLock)
            {
                Asked++;
                if (_newer.TryGetValue(k, out float[][] kept)) return kept;
                if (_older.TryGetValue(k, out kept)) { Keep(k, kept); return kept; }
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            List<float[]> p = NavCopy.Path(Mesh, sx, sy, ex, ey, PartyQuery);
            float[][] pts = p?.ToArray();
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            lock (_keptLock) { Worked++; WorkedMs += ms; Keep(k, pts); }
            return pts;
        }

        private static void Keep(WayEnds k, float[][] pts)
        {
            if (_newer.Count >= KeepWays) { _older = _newer; _newer = new Dictionary<WayEnds, float[][]>(); }
            _newer[k] = pts;
        }

        /// <summary>Forgets every kept way (a new mesh, or the speed test's fresh start).</summary>
        public static void ForgetWays() { lock (_keptLock) { _newer = new Dictionary<WayEnds, float[][]>(); _older = new Dictionary<WayEnds, float[][]>(); } }

        private readonly struct WayEnds : IEquatable<WayEnds>
        {
            private readonly float _sx, _sy, _ex, _ey;
            public WayEnds(float sx, float sy, float ex, float ey) { _sx = sx; _sy = sy; _ex = ex; _ey = ey; }
            public bool Equals(WayEnds o) => _sx.Equals(o._sx) && _sy.Equals(o._sy) && _ex.Equals(o._ex) && _ey.Equals(o._ey);
            public override bool Equals(object obj) => obj is WayEnds o && Equals(o);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _sx.GetHashCode();
                    h = h * 397 ^ _sy.GetHashCode();
                    h = h * 397 ^ _ex.GetHashCode();
                    return h * 397 ^ _ey.GetHashCode();
                }
            }
        }
    }

    /// <summary>
    /// Copies the campaign map's walking mesh out of TaleWorlds.Native's memory (1.4.8 layout, read from its way-finder: docs/PLAN-ways.md), once per map, after
    /// checking the first faces against the game's own answers (their island and group from GetNavMeshPathFaceRecord, their centre from GetNavMeshCenterPosition);
    /// nothing is read further when they disagree. Writes navmesh.bin and navmesh.json beside the walking map.
    /// </summary>
    internal static class NavMeshDump
    {
        private const int FaceTable = 0x3218, MeshObject = 0x2DA0, MeshSettings = 0x570, RadiusSwitch = 0x48, AbilityBits = 0x98BE8, RegionTable = 0x5A8;
        private const int FEdges = 0x60, FNeighbours = 0xB0, FCorners = 0xA8, FD8 = 0xD8, FE4 = 0xE4, FLinksBegin = 0xF8, FLinksEnd = 0x100, FSpecial = 0x118,
                          FCost = 0x120, FIsland = 0x128, FGroup = 0x130, FAbility = 0x134, FOneWay = 0x138, FFlags = 0x139;
        private const int EA = 0x0, EB = 0x8, EKind = 0x20, VX = 0x0, VY = 0x4, VFlags = 0x10;

        private static float F(IntPtr p, int off) => BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(p, off)), 0);

        public static string Run(string dir)
        {
            try
            {
                IMapScene ms = Campaign.Current?.MapSceneWrapper;
                if (ms == null || dir == null) return "no map scene or no walking map folder";
                FieldInfo sf = ms.GetType().GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance);
                Scene scene = sf?.GetValue(ms) as Scene;
                if (scene == null) return "the map scene's _scene field was not found";
                IntPtr sp = new IntPtr((long)scene.Pointer.ToUInt64());
                int n = scene.GetNavMeshFaceCount();
                IntPtr table = Marshal.ReadIntPtr(sp, FaceTable);
                if (n <= 0 || table == IntPtr.Zero) return "no faces (" + n + ") or no face table";
                var facePtr = new IntPtr[n];
                var faceIndex = new Dictionary<long, int>(n);
                for (int i = 0; i < n; i++) { facePtr[i] = Marshal.ReadIntPtr(table, i * 8); if (facePtr[i] != IntPtr.Zero) faceIndex[facePtr[i].ToInt64()] = i; }
                // the check before reading on: the first faces' island, group and centre against the game's own answers
                string bad = Check(scene, facePtr, Math.Min(n, 50));
                if (bad != null) return "not copied: the engine's layout is not what was read (" + bad + ")";

                var vIndex = new Dictionary<long, int>(); var vx = new List<float>(); var vy = new List<float>(); var vf = new List<byte>();
                var eIndex = new Dictionary<long, int>(); var ea = new List<int>(); var eb = new List<int>(); var ek = new List<byte>();
                int Vertex(IntPtr v)
                {
                    long key = v.ToInt64();
                    if (vIndex.TryGetValue(key, out int k)) return k;
                    vIndex[key] = vx.Count; vx.Add(F(v, VX)); vy.Add(F(v, VY)); vf.Add(Marshal.ReadByte(v, VFlags));
                    return vx.Count - 1;
                }
                int Edge(IntPtr e)
                {
                    if (e == IntPtr.Zero) return -1;
                    long key = e.ToInt64();
                    if (eIndex.TryGetValue(key, out int k)) return k;
                    int a = Vertex(Marshal.ReadIntPtr(e, EA)), b = Vertex(Marshal.ReadIntPtr(e, EB));
                    eIndex[key] = ea.Count; ea.Add(a); eb.Add(b); ek.Add(Marshal.ReadByte(e, EKind));
                    return ea.Count - 1;
                }
                var m = new NavMesh
                {
                    Crc = MapExport.Crc, FN = new byte[n], FEdge = new int[n * NavMesh.MaxCorners], FNbr = new int[n * NavMesh.MaxCorners], FCost = new float[n], FD8 = new float[n], FE4 = new float[n],
                    FIsland = new int[n], FGroup = new int[n], FAbility = new int[n], FSpecialEdge = new int[n], FOneWay = new byte[n], FFlags = new byte[n], FLinkStart = new int[n + 1]
                };
                var links = new List<int>();
                int oneWay = 0, special = 0, withLinks = 0, maxGroup = 0;
                for (int i = 0; i < n; i++)
                {
                    IntPtr f = facePtr[i];
                    for (int k = 0; k < NavMesh.MaxCorners; k++) { m.FEdge[i * NavMesh.MaxCorners + k] = -1; m.FNbr[i * NavMesh.MaxCorners + k] = -1; }
                    m.FLinkStart[i] = links.Count;
                    m.FSpecialEdge[i] = -1;
                    if (f == IntPtr.Zero) continue;
                    int corners = Marshal.ReadByte(f, FCorners);
                    if (corners > NavMesh.MaxCorners) return "not copied: face " + i + " has " + corners + " corners";
                    m.FN[i] = (byte)corners;
                    for (int k = 0; k < corners; k++)
                    {
                        m.FEdge[i * NavMesh.MaxCorners + k] = Edge(Marshal.ReadIntPtr(f, FEdges + 8 * k));
                        IntPtr nb = Marshal.ReadIntPtr(f, FNeighbours + 8 * k);
                        m.FNbr[i * NavMesh.MaxCorners + k] = nb != IntPtr.Zero && faceIndex.TryGetValue(nb.ToInt64(), out int ni) ? ni : -1;
                    }
                    m.FCost[i] = F(f, FCost); m.FD8[i] = F(f, FD8); m.FE4[i] = F(f, FE4);
                    m.FIsland[i] = Marshal.ReadInt32(f, FIsland); m.FGroup[i] = Marshal.ReadInt32(f, FGroup); m.FAbility[i] = Marshal.ReadInt32(f, FAbility);
                    m.FOneWay[i] = Marshal.ReadByte(f, FOneWay); m.FFlags[i] = Marshal.ReadByte(f, FFlags);
                    maxGroup = Math.Max(maxGroup, m.FGroup[i]);
                    if (m.FOneWay[i] != 0) oneWay++;
                    if ((m.FFlags[i] & 4) != 0) special++;
                    IntPtr sEdge = Marshal.ReadIntPtr(f, FSpecial);
                    if (sEdge != IntPtr.Zero && (m.FFlags[i] & 4) != 0) m.FSpecialEdge[i] = Edge(sEdge);
                    IntPtr lb = Marshal.ReadIntPtr(f, FLinksBegin), le = Marshal.ReadIntPtr(f, FLinksEnd);
                    long count = lb != IntPtr.Zero && le.ToInt64() > lb.ToInt64() ? (le.ToInt64() - lb.ToInt64()) / 8 : 0;
                    if (count > 64) return "not copied: face " + i + " has " + count + " extra links";
                    if (count > 0) withLinks++;
                    for (int k = 0; k < count; k++)
                    {
                        IntPtr lf = Marshal.ReadIntPtr(lb, 8 * k);
                        links.Add(lf != IntPtr.Zero && faceIndex.TryGetValue(lf.ToInt64(), out int li) ? li : -1);
                    }
                }
                m.FLinkStart[n] = links.Count;
                m.FLinks = links.ToArray();
                // special faces' own edges may point at the special links' faces: make sure each special edge's corners are copied (done above in Edge)
                m.VX = vx.ToArray(); m.VY = vy.ToArray(); m.VFlags = vf.ToArray();
                m.EA = ea.ToArray(); m.EB = eb.ToArray(); m.EKind = ek.ToArray();
                IntPtr mesh = IntPtr.Add(sp, MeshObject);
                IntPtr settings = Marshal.ReadIntPtr(mesh, MeshSettings);
                m.RadiusOn = settings != IntPtr.Zero && Marshal.ReadByte(settings, RadiusSwitch) != 0;
                m.AbilityMask = settings != IntPtr.Zero ? Marshal.ReadInt32(settings, AbilityBits) : -1;
                IntPtr regions = Marshal.ReadIntPtr(mesh, RegionTable);
                m.GroupRegion = new byte[maxGroup + 1];
                if (regions != IntPtr.Zero) for (int g = 0; g <= maxGroup; g++) m.GroupRegion[g] = Marshal.ReadByte(regions, g);
                m.PartyExcluded = Campaign.Current.Models.PartyNavigationModel.GetInvalidTerrainTypesForNavigationType(MobileParty.NavigationType.Default) ?? new int[0];
                m.PartyCostTo0 = Campaign.Current.Models.MapDistanceModel.RegionSwitchCostFromSeaToLand;
                m.PartyCostTo1 = Campaign.Current.Models.MapDistanceModel.RegionSwitchCostFromLandToSea;
                m.Save(Path.Combine(dir, "navmesh.bin"));
                var groups = new Dictionary<int, int>();
                for (int i = 0; i < n; i++) groups[m.FGroup[i]] = groups.TryGetValue(m.FGroup[i], out int c) ? c + 1 : 1;
                File.WriteAllText(Path.Combine(dir, "navmesh.json"), "{\"crc\":\"" + m.Crc + "\",\"faces\":" + n + ",\"corners\":" + m.VX.Length + ",\"edges\":" + m.EA.Length
                    + ",\"one_way\":" + oneWay + ",\"special\":" + special + ",\"with_links\":" + withLinks + ",\"links\":" + links.Count + ",\"radius_on\":" + (m.RadiusOn ? "true" : "false")
                    + ",\"ability_mask\":" + m.AbilityMask + ",\"regions\":[" + string.Join(",", m.GroupRegion) + "],\"party_excluded\":[" + string.Join(",", m.PartyExcluded) + "]"
                    + ",\"cost_to_land\":" + m.PartyCostTo0 + ",\"cost_to_sea\":" + m.PartyCostTo1
                    + ",\"groups\":{" + string.Join(",", groups.OrderBy(kv => kv.Key).Select(kv => "\"" + kv.Key + "\":" + kv.Value)) + "}"
                    + ",\"flags\":{" + string.Join(",", m.FFlags.GroupBy(x => x).OrderBy(x => x.Key).Select(x => "\"" + x.Key + "\":" + x.Count())) + "}"
                    + ",\"made\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\"}");
                return "copied: " + n + " faces, " + m.VX.Length + " corners, " + m.EA.Length + " edges (" + oneWay + " one-way, " + special + " special, " + links.Count + " extra links)";
            }
            catch (Exception e)
            {
                Diag.Swallowed("copying the walking mesh", e);
                return "failed: " + e.GetType().Name + ": " + e.Message;
            }
        }

        private static string Check(Scene scene, IntPtr[] facePtr, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr f = facePtr[i];
                if (f == IntPtr.Zero) return "face " + i + " has no pointer";
                PathFaceRecord rec = scene.GetNavMeshPathFaceRecord(i);
                int island = Marshal.ReadInt32(f, FIsland), group = Marshal.ReadInt32(f, FGroup);
                if (rec.FaceIslandIndex != island || rec.FaceGroupIndex != group) return "face " + i + ": island " + island + " group " + group + " read, the game says " + rec.FaceIslandIndex + " and " + rec.FaceGroupIndex;
                int corners = Marshal.ReadByte(f, FCorners);
                if (corners < 3 || corners > NavMesh.MaxCorners) return "face " + i + ": " + corners + " corners read";
                // the game's centre of the face must lie within the corners read (its bounds, a hair wider): however the game takes a centre, a wrong layout fails this
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int k = 0; k < corners; k++)
                {
                    IntPtr e = Marshal.ReadIntPtr(f, FEdges + 8 * k);
                    if (e == IntPtr.Zero) return "face " + i + ": edge " + k + " missing";
                    foreach (IntPtr v in new[] { Marshal.ReadIntPtr(e, EA), Marshal.ReadIntPtr(e, EB) })
                    {
                        if (v == IntPtr.Zero) return "face " + i + ": a corner of edge " + k + " missing";
                        float x = F(v, VX), y = F(v, VY);
                        x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    }
                }
                Vec3 c = Vec3.Zero;
                scene.GetNavMeshCenterPosition(i, ref c);
                if (c.x < x0 - 0.01f || c.x > x1 + 0.01f || c.y < y0 - 0.01f || c.y > y1 + 0.01f || x1 - x0 > 200 || y1 - y0 > 200)
                    return "face " + i + ": corners read span (" + x0.ToString("0.00", CultureInfo.InvariantCulture) + ", " + y0.ToString("0.00", CultureInfo.InvariantCulture) + ") to ("
                           + x1.ToString("0.00", CultureInfo.InvariantCulture) + ", " + y1.ToString("0.00", CultureInfo.InvariantCulture) + "), the game puts its centre at (" + c.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + c.y.ToString("0.00", CultureInfo.InvariantCulture) + ")";
            }
            return null;
        }
    }

    /// <summary>
    /// Sets the ways up at each campaign once the walking map is there (on the campaign map, on the game's thread): copies the walking mesh out of the engine when
    /// this map has none yet, loads it, and runs the copy's proof when it has not been run on this map.
    /// </summary>
    internal static class NavSetup
    {
        private static string _doneFor;
        public static string DumpResult = "not tried";

        public static void Frame()
        {
            try
            {
                if (!Bridge.Running || Campaign.Current == null || TaleWorlds.MountAndBlade.Mission.Current != null
                    || !(TaleWorlds.Core.GameStateManager.Current?.ActiveState is TaleWorlds.CampaignSystem.GameState.MapState)) return;
                NavProof.Frame();
                string dir = MapExport.Folder;
                if (dir == null || !MapExport.Ready || _doneFor == dir) return;
                _doneFor = dir;
                if (!File.Exists(Path.Combine(dir, "navmesh.bin")))
                {
                    DumpResult = NavMeshDump.Run(dir);
                    Diag.Event("ways", "mesh copied", new { result = DumpResult });
                    Log.Line("walking mesh: " + DumpResult);
                }
                Ways.Load(dir);
                if (Ways.Mesh != null && !Ways.ProofFor(Path.Combine(dir, "navcopy-proof.json"))) Log.Line("way-finder copy: " + NavProof.Start(dir, 2000));
                Log.Line("ways: " + Ways.Status);
            }
            catch (Exception e) { Diag.Swallowed("setting up the ways", e); }
        }

        /// <summary>A new campaign or load: set the ways up again.</summary>
        public static void Reset() { _doneFor = null; }
    }

    /// <summary>
    /// The copy's proof (docs/PLAN-ways.md): random pairs of faces a land party may walk, on one island, their centres 3 to 600 apart, asked of the game's own way-finder
    /// exactly as MobileParty.ComputePath asks it (radius 0.3, the party's closed ground, extra cost multiplier 2, its region switch costs) and of the copy; identical when
    /// both give the same number of points, each within 0.01. A slice of pairs each frame on the campaign map. Writes navcopy-proof.json (the rate) and waycheck.jsonl
    /// (the first differences, both ways in full) beside the mesh.
    /// </summary>
    internal static class NavProof
    {
        public static volatile string Progress = "not run";
        private static List<(int A, int B)> _pairs;
        private static int _next, _same, _gameNone, _copyNone, _bothNone;
        private static readonly List<double> _gaps = new List<double>();
        private static string _dir;
        private static StreamWriter _log, _all;
        private static int _logged;
        private const int PerFrame = 20, MostLogged = 200;

        public static bool Running => _pairs != null && _next < _pairs.Count;

        public static string Start(string dir, int count, int seed = 1234)
        {
            NavMesh m = Ways.Mesh;
            if (m == null || dir == null) return "no walking mesh copied";
            NavQuery q = Ways.PartyQuery;
            var open = new List<int>();
            for (int f = 0; f < m.Faces; f++) if (m.FN[f] >= 3 && NavCopy.Open(m, q, f)) open.Add(f);
            if (open.Count < 2) return "no faces a party may walk";
            var rng = new Random(seed);
            var pairs = new List<(int, int)>();
            for (int tries = 0; pairs.Count < count && tries < count * 50; tries++)
            {
                int a = open[rng.Next(open.Count)], b = open[rng.Next(open.Count)];
                if (a == b || m.FIsland[a] != m.FIsland[b]) continue;
                float d = (float)Math.Sqrt(Math.Pow(m.CenterX(a) - m.CenterX(b), 2) + Math.Pow(m.CenterY(a) - m.CenterY(b), 2));
                if (d < 3 || d > 600) continue;
                pairs.Add((a, b));
            }
            _pairs = pairs; _next = _same = _gameNone = _copyNone = _bothNone = 0; _gaps.Clear(); _dir = dir; _logged = 0;
            try { _log?.Dispose(); _log = new StreamWriter(Path.Combine(dir, "waycheck.jsonl"), false); } catch (Exception e) { Diag.Swallowed("opening waycheck.jsonl", e); _log = null; }
            // every pair's two ways, for working on the copy with the game closed (waycheck-all.jsonl)
            try { _all?.Dispose(); _all = new StreamWriter(Path.Combine(dir, "waycheck-all.jsonl"), false); } catch (Exception e) { Diag.Swallowed("opening waycheck-all.jsonl", e); _all = null; }
            Progress = "0 of " + pairs.Count + " pairs";
            return "the proof begins: " + pairs.Count + " pairs";
        }

        /// <summary>Every frame on the campaign map: the next slice of pairs.</summary>
        public static void Frame()
        {
            if (!Running || Campaign.Current?.MapSceneWrapper == null) return;
            try
            {
                IMapScene ms = Campaign.Current.MapSceneWrapper;
                NavMesh m = Ways.Mesh;
                NavQuery q = Ways.PartyQuery;
                int[] excluded = m.PartyExcluded;
                for (int k = 0; k < PerFrame && _next < _pairs.Count; k++, _next++)
                {
                    (int a, int b) = _pairs[_next];
                    Vec2 ca = ms.GetNavigationMeshCenterPosition(a), cb = ms.GetNavigationMeshCenterPosition(b);
                    var nav = new NavigationPath();
                    bool gameOk = ms.GetPathBetweenAIFaces(ms.GetFaceAtIndex(a), ms.GetFaceAtIndex(b), ca, cb, 0.3f, nav, excluded, 2f, m.PartyCostTo0, m.PartyCostTo1);
                    var game = new List<float[]>();
                    if (gameOk) for (int i = 0; i < nav.Size; i++) game.Add(new[] { nav.PathPoints[i].x, nav.PathPoints[i].y });
                    List<float[]> copy = NavCopy.Path(m, a, b, ca.x, ca.y, cb.x, cb.y, q);
                    if (!gameOk && copy == null) { _bothNone++; _same++; continue; }
                    if (!gameOk) { _gameNone++; Log(a, b, ca, cb, game, copy, "the game found no way"); continue; }
                    if (copy == null) { _copyNone++; Log(a, b, ca, cb, game, copy, "the copy found no way"); continue; }
                    // the game's points may begin with the start: line them up
                    if (game.Count > 0 && Math.Abs(game[0][0] - ca.x) < 0.01f && Math.Abs(game[0][1] - ca.y) < 0.01f && !(copy.Count > 0 && Math.Abs(copy[0][0] - ca.x) < 0.01f && Math.Abs(copy[0][1] - ca.y) < 0.01f))
                        game.RemoveAt(0);
                    All(a, b, ca, cb, game);
                    double gl = Length(ca, game), cl = Length(ca, copy);
                    if (gl > 0) _gaps.Add((cl - gl) / gl * 100);
                    bool same = game.Count == copy.Count;
                    for (int i = 0; same && i < game.Count; i++) same = Math.Abs(game[i][0] - copy[i][0]) <= 0.01f && Math.Abs(game[i][1] - copy[i][1]) <= 0.01f;
                    if (same) _same++;
                    else Log(a, b, ca, cb, game, copy, "different");
                }
                Progress = _next + " of " + _pairs.Count + " pairs, " + _same + " identical";
                if (_next >= _pairs.Count) Finish();
            }
            catch (Exception e) { Diag.Swallowed("the way-finder copy's proof", e); Progress = "failed: " + e.Message; _pairs = null; }
        }

        private static double Length(Vec2 start, List<float[]> pts)
        {
            double len = 0; float x = start.x, y = start.y;
            foreach (float[] p in pts) { len += Math.Sqrt((p[0] - x) * (p[0] - x) + (p[1] - y) * (p[1] - y)); x = p[0]; y = p[1]; }
            return len;
        }

        private static void All(int a, int b, Vec2 ca, Vec2 cb, List<float[]> game)
        {
            if (_all == null) return;
            _all.WriteLine("{\"a\":" + a + ",\"b\":" + b + ",\"from\":[" + ca.x.ToString("R", CultureInfo.InvariantCulture) + "," + ca.y.ToString("R", CultureInfo.InvariantCulture) + "],\"to\":["
                           + cb.x.ToString("R", CultureInfo.InvariantCulture) + "," + cb.y.ToString("R", CultureInfo.InvariantCulture) + "],\"game\":["
                           + string.Join(",", game.Select(p => "[" + p[0].ToString("R", CultureInfo.InvariantCulture) + "," + p[1].ToString("R", CultureInfo.InvariantCulture) + "]")) + "]}");
        }

        private static void Log(int a, int b, Vec2 ca, Vec2 cb, List<float[]> game, List<float[]> copy, string what)
        {
            if (_log == null || _logged >= MostLogged) return;
            _logged++;
            string P(List<float[]> l) => l == null ? "null" : "[" + string.Join(",", l.Select(p => "[" + p[0].ToString("0.###", CultureInfo.InvariantCulture) + "," + p[1].ToString("0.###", CultureInfo.InvariantCulture) + "]")) + "]";
            _log.WriteLine("{\"a\":" + a + ",\"b\":" + b + ",\"from\":[" + ca.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + ca.y.ToString("0.###", CultureInfo.InvariantCulture) + "],\"to\":["
                           + cb.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + cb.y.ToString("0.###", CultureInfo.InvariantCulture) + "],\"what\":\"" + what + "\",\"game\":" + P(game) + ",\"copy\":" + P(copy) + "}");
            _log.Flush();
        }

        private static void Finish()
        {
            int n = _pairs.Count;
            double rate = n > 0 ? (double)_same / n : 0;
            double medGap = _gaps.Count > 0 ? _gaps.Select(Math.Abs).OrderBy(x => x).ElementAt(_gaps.Count / 2) : double.NaN;
            try
            {
                File.WriteAllText(Path.Combine(_dir, "navcopy-proof.json"), "{\"copy_version\":" + NavCopy.Version + ",\"pairs\":" + n + ",\"identical\":" + _same + ",\"rate\":" + rate.ToString("0.#####", CultureInfo.InvariantCulture)
                    + ",\"game_none\":" + _gameNone + ",\"copy_none\":" + _copyNone + ",\"both_none\":" + _bothNone
                    + ",\"median_length_gap_pct\":" + (double.IsNaN(medGap) ? "null" : medGap.ToString("0.###", CultureInfo.InvariantCulture))
                    + ",\"made\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\"}");
            }
            catch (Exception e) { Diag.Swallowed("writing navcopy-proof.json", e); }
            try { _log?.Dispose(); } catch (Exception) { }
            try { _all?.Dispose(); } catch (Exception) { }
            _log = null; _all = null;
            Ways.SetProven(rate);
            Progress = "done: " + _same + " of " + n + " identical (" + (rate * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%), median length gap " + (double.IsNaN(medGap) ? "?" : medGap.ToString("0.00", CultureInfo.InvariantCulture) + "%")
                       + (Ways.Proven ? "; the copy now plans the ways" : "; the copy does not plan ways yet");
            Diag.Event("ways", "proof", new { pairs = n, identical = _same, rate, game_none = _gameNone, copy_none = _copyNone, median_length_gap = medGap });
            Ways.Status = Progress;
            _pairs = null;
        }
    }
}
