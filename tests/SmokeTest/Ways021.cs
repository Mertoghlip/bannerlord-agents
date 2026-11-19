using System;
using System.Collections.Generic;
using System.IO;
using BannerlordAgents.Common;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// The copy of the game's own way-finder (docs/PLAN-ways.md, Phase 2) on a made-up walking mesh: a three-by-three grid of squares, ten units each, the
        /// middle one of a ground the party may not walk. A clear straight line is one leg; across the closed middle the way goes round it, its bend pulled off the
        /// closed corner by the radius; separate islands have no way; the mesh survives saving and loading.
        /// </summary>
        private static void NavCopyTests()
        {
            NavMesh Grid(bool middleClosed, bool twoIslands)
            {
                // corners on a 4 x 4 lattice, 10 apart
                var vx = new List<float>(); var vy = new List<float>(); var vf = new List<byte>();
                for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) { vx.Add(x * 10); vy.Add(y * 10); vf.Add((byte)(x == 0 || y == 0 || x == 3 || y == 3 ? 0x80 : 0)); }
                int V(int x, int y) => y * 4 + x;
                var ea = new List<int>(); var eb = new List<int>(); var edges = new Dictionary<long, int>();
                int E(int a, int b)
                {
                    long key = Math.Min(a, b) * 1000L + Math.Max(a, b);
                    if (edges.TryGetValue(key, out int k)) return k;
                    edges[key] = ea.Count; ea.Add(a); eb.Add(b);
                    return ea.Count - 1;
                }
                int n = 9;
                var m = new NavMesh
                {
                    Crc = "test", FN = new byte[n], FEdge = new int[n * 4], FNbr = new int[n * 4], FCost = new float[n], FD8 = new float[n], FE4 = new float[n], FIsland = new int[n], FGroup = new int[n],
                    FAbility = new int[n], FSpecialEdge = new int[n], FOneWay = new byte[n], FFlags = new byte[n], FLinkStart = new int[n + 1], FLinks = new int[0], RadiusOn = true, AbilityMask = 1,
                    GroupRegion = new byte[8], PartyExcluded = new[] { 5 }
                };
                int F(int x, int y) => y * 3 + x;
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        int f = F(x, y);
                        m.FN[f] = 4;
                        // edges in order round the square: bottom, right, top, left; the neighbour across each
                        int[] e = { E(V(x, y), V(x + 1, y)), E(V(x + 1, y), V(x + 1, y + 1)), E(V(x + 1, y + 1), V(x, y + 1)), E(V(x, y + 1), V(x, y)) };
                        int[] nb = { y > 0 ? F(x, y - 1) : -1, x < 2 ? F(x + 1, y) : -1, y < 2 ? F(x, y + 1) : -1, x > 0 ? F(x - 1, y) : -1 };
                        for (int k = 0; k < 4; k++) { m.FEdge[f * 4 + k] = e[k]; m.FNbr[f * 4 + k] = nb[k]; }
                        m.FAbility[f] = 1; m.FFlags[f] = 1; m.FSpecialEdge[f] = -1;
                        m.FGroup[f] = f == 4 && middleClosed ? 5 : 1;
                        m.FIsland[f] = twoIslands && x == 2 ? 1 : 0;
                    }
                for (int f = 0; f <= n; f++) m.FLinkStart[f] = 0;
                m.VX = vx.ToArray(); m.VY = vy.ToArray(); m.VFlags = vf.ToArray(); m.EA = ea.ToArray(); m.EB = eb.ToArray(); m.EKind = new byte[ea.Count];
                return m;
            }
            NavMesh open = Grid(false, false), closed = Grid(true, false), split = Grid(false, true);
            NavQuery qOpen = NavQuery.Party(open), qClosed = NavQuery.Party(closed);
            List<float[]> straight = NavCopy.Path(open, 5, 5, 25, 25, qOpen);
            bool oneLeg = straight != null && straight.Count == 1 && Math.Abs(straight[0][0] - 25) < 1e-3 && Math.Abs(straight[0][1] - 25) < 1e-3;
            List<float[]> round = NavCopy.Path(closed, 5, 5, 25, 25, qClosed);
            // round the closed middle square past its corner (20,10) (or (10,20)): both doorways beside that corner are pulled 0.3 off it, so the taut way bends twice,
            // at (20, 9.7) and (20.3, 10), then runs to the end
            bool Near(float[] p, float x, float y) => Math.Abs(p[0] - x) < 1e-3 && Math.Abs(p[1] - y) < 1e-3;
            bool rounds = round != null && round.Count == 3 && Near(round[2], 25, 25)
                          && ((Near(round[0], 20, 9.7f) && Near(round[1], 20.3f, 10)) || (Near(round[0], 9.7f, 20) && Near(round[1], 10, 20.3f)));
            bool islands = NavCopy.Path(split, 5, 5, 25, 25, NavQuery.Party(split)) == null;
            bool faces = open.FaceAt(5, 5) == 0 && open.FaceAt(25, 25) == 8 && open.FaceAt(-1, 5) == -1;
            string file = Path.Combine(Path.GetTempPath(), "navmesh-test.bin");
            closed.Save(file);
            NavMesh back = NavMesh.Load(file);
            List<float[]> again = back != null ? NavCopy.Path(back, 5, 5, 25, 25, NavQuery.Party(back)) : null;
            bool saved = again != null && again.Count == round?.Count && Math.Abs(again[0][0] - round[0][0]) < 1e-5 && Math.Abs(again[0][1] - round[0][1]) < 1e-5 && back.PartyExcluded.Length == 1;
            try { File.Delete(file); } catch (Exception) { }
            Check(oneLeg && rounds && islands && faces && saved,
                  "the copy of the game's way-finder: a clear straight line is one leg (" + oneLeg + "); round a closed square the way bends twice, 0.3 off its corner on each side ("
                  + (round == null ? "none" : string.Join(" -> ", round.ConvertAll(p => "(" + p[0].ToString("0.###") + ", " + p[1].ToString("0.###") + ")"))) + ", " + rounds
                  + "); no way between islands (" + islands + "); faces found by point (" + faces + "); the mesh saved and read back (" + saved + ")");
        }
    }
}
