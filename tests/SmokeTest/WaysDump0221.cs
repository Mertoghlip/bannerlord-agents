using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BannerlordAgents.Common;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// SmokeTest --waysdump &lt;map crc&gt; &lt;out file&gt; [pairs] (module 0.22.1): the copy of the game's way-finder on the real walking mesh, for fixed pairs of points
        /// (the same every run), each way written to a file; two builds' files compared show a change to the copy changed no way. It also says how long the ways
        /// took and how much memory they handed out (0.22.1: each search had made two arrays the size of the whole mesh).
        /// </summary>
        private static int WaysDump(string[] args)
        {
            string crc = args[1], output = args[2];
            int pairs = args.Length > 3 && int.TryParse(args[3], out int p) ? p : 5000;
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents", "map", crc, "navmesh.bin");
            NavMesh m = NavMesh.Load(path);
            if (m == null) { Console.WriteLine("No walking mesh at " + path); return 1; }
            m.FaceAt(0, 0);
            NavQuery q = NavQuery.Party(m);
            var rng = new Random(20261121);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            AppDomain.MonitoringIsEnabled = true;
            long a0 = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            var sw = Stopwatch.StartNew();
            int found = 0, none = 0;
            for (int i = 0; i < pairs; i++)
            {
                Centre(m, rng.Next(m.Faces), out float sx, out float sy);
                Centre(m, rng.Next(m.Faces), out float ex, out float ey);
                List<float[]> way = NavCopy.Path(m, sx, sy, ex, ey, q);
                sb.Append(i.ToString(inv)).Append(':');
                if (way == null) { none++; sb.Append(" none\n"); continue; }
                found++;
                foreach (float[] c in way) sb.Append(' ').Append(c[0].ToString("R", inv)).Append(',').Append(c[1].ToString("R", inv));
                sb.Append('\n');
            }
            sw.Stop();
            long handed = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - a0;
            File.WriteAllText(output, sb.ToString());
            Console.WriteLine("ways for " + pairs + " pairs on a mesh of " + m.Faces + " faces: " + found + " found, " + none + " with no way; "
                              + sw.Elapsed.TotalMilliseconds.ToString("0", inv) + " ms, memory handed out " + (handed / 1048576.0).ToString("0.0", inv) + " MB (with the file's text); written to " + output);
            return 0;
        }

        private static void Centre(NavMesh m, int f, out float x, out float y)
        {
            x = 0; y = 0;
            int n = m.FN[f];
            for (int i = 0; i < n; i++)
            {
                int e = m.FEdge[f * NavMesh.MaxCorners + i];
                x += m.VX[m.EA[e]]; y += m.VY[m.EA[e]];
            }
            if (n > 0) { x /= n; y /= n; }
        }
    }
}
