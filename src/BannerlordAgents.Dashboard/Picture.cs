using System.IO.Compression;
using BannerlordAgents.Routes;

namespace BannerlordAgents.Dashboard
{
    /// <summary>
    /// The map picture the dashboard draws everything on: one pixel per cell of the walking map, coloured by the game's ground
    /// type, the ground no party can cross a little darker, with the land's relief shaded from the north-west. Made once per
    /// map and kept beside it (picture-v1.png). North is up: the picture's top row is the map's largest y.
    /// </summary>
    internal static class Picture
    {
        public const string FileName = "picture-v1.png";

        private static readonly Dictionary<byte, uint> Colours = new Dictionary<byte, uint>
        {
            [Ground.None] = 0x4a4f4c, [Ground.Plain] = 0xc9c49b, [Ground.Desert] = 0xdcc58e, [Ground.Snow] = 0xeef0f1, [Ground.Forest] = 0x7e9a63,
            [Ground.Steppe] = 0xbfb57d, [Ground.Fording] = 0xe39a3b, [Ground.Mountain] = 0x8f8274, [Ground.Lake] = 0x7d9db3, [Ground.Water] = 0x7896aa,
            [Ground.River] = 0x6c91ab, [Ground.Canyon] = 0x9a7a60, [Ground.RuralArea] = 0xb9bb8b, [Ground.Swamp] = 0x8e9a77, [Ground.Dune] = 0xe2cf98,
            [Ground.Bridge] = 0xc1462f, [Ground.CoastalSea] = 0x86a7bc, [Ground.OpenSea] = 0x6b8da5, [Ground.Beach] = 0xe6d9a8, [Ground.Cliff] = 0x6e645b,
            [Ground.NonNavigableRiver] = 0x6c91ab, [Ground.LandRestriction] = 0x5f5b56, [Ground.SeaRestriction] = 0x5e6b76, [Ground.UnderBridge] = 0x6c91ab
        };

        public static uint Colour(byte g) => Colours.TryGetValue(g, out uint c) ? c : 0x9a9a8a;

        public static string Ensure(WalkMap m)
        {
            string path = Path.Combine(m.Dir, FileName);
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(Path.Combine(m.Dir, "ground.bin"))) return path;
            byte[] png = Render(m);
            string tmp = path + ".part";
            File.WriteAllBytes(tmp, png);
            File.Move(tmp, path, true);
            Log.Say("Map picture made: " + m.W + " x " + m.H + " pixels, " + (png.Length / 1024) + " KB.");
            return path;
        }

        public static byte[] Render(WalkMap m)
        {
            int w = m.W, h = m.H;
            var rgb = new byte[w * h * 3];
            float[] hs = m.Heights;
            int hw = m.Meta.HeightWidth, hh = m.Meta.HeightHeight, step = Math.Max(1, m.Meta.HeightStep);
            float cell = m.Cell;
            for (int row = 0; row < h; row++)
            {
                int cy = h - 1 - row;
                for (int cx = 0; cx < w; cx++)
                {
                    int i = cy * w + cx;
                    byte g = m.Ground[i];
                    uint c = Colour(g);
                    double k = m.Walk[i] ? 1.0 : (g == Ground.None ? 1.0 : 0.86);
                    if (hs != null && hw > 2 && hh > 2)
                    {
                        int hx = Math.Min(hw - 2, Math.Max(1, cx / step)), hy = Math.Min(hh - 2, Math.Max(1, cy / step));
                        float dzdx = (hs[hy * hw + hx + 1] - hs[hy * hw + hx - 1]) / (2 * step * cell);
                        float dzdy = (hs[(hy + 1) * hw + hx] - hs[(hy - 1) * hw + hx]) / (2 * step * cell);
                        // light from the north-west, a little above the horizon
                        double nx = -dzdx, ny = -dzdy, nz = 1, len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        double lx = -0.55, ly = 0.55, lz = 0.63;
                        double lit = (nx * lx + ny * ly + nz * lz) / len;
                        k *= 0.72 + 0.45 * Math.Max(0, Math.Min(1, lit));
                    }
                    int o = (row * w + cx) * 3;
                    rgb[o] = Clamp(((c >> 16) & 0xff) * k);
                    rgb[o + 1] = Clamp(((c >> 8) & 0xff) * k);
                    rgb[o + 2] = Clamp((c & 0xff) * k);
                }
            }
            return Png.Rgb(w, h, rgb);
        }

        private static byte Clamp(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));
    }

    /// <summary>A plain PNG writer: 8-bit RGB, no filtering, deflated.</summary>
    internal static class Png
    {
        private static readonly uint[] Crc = MakeCrc();

        public static byte[] Rgb(int w, int h, byte[] rgb)
        {
            using var outMs = new MemoryStream();
            outMs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13];
            Be(ihdr, 0, (uint)w);
            Be(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            Chunk(outMs, "IHDR", ihdr);
            using (var raw = new MemoryStream())
            {
                using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
                {
                    var line = new byte[w * 3 + 1];
                    for (int y = 0; y < h; y++)
                    {
                        line[0] = 0;
                        Buffer.BlockCopy(rgb, y * w * 3, line, 1, w * 3);
                        z.Write(line, 0, line.Length);
                    }
                }
                Chunk(outMs, "IDAT", raw.ToArray());
            }
            Chunk(outMs, "IEND", Array.Empty<byte>());
            return outMs.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            Be(len, 0, (uint)data.Length);
            s.Write(len);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t);
            s.Write(data);
            uint c = 0xffffffff;
            foreach (byte b in t) c = Crc[(c ^ b) & 0xff] ^ (c >> 8);
            foreach (byte b in data) c = Crc[(c ^ b) & 0xff] ^ (c >> 8);
            var crc = new byte[4];
            Be(crc, 0, c ^ 0xffffffff);
            s.Write(crc);
        }

        private static void Be(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        private static uint[] MakeCrc()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }
    }
}
