using System.Text.Json;
using System.Text.Json.Serialization;

namespace BannerlordAgents.Routes
{
    /// <summary>The game's ground types (TaleWorlds.Core.TerrainType, 1.4.8), and 0 where the map has no walking ground at all.</summary>
    public static class Ground
    {
        public const byte None = 0, Plain = 1, Desert = 2, Snow = 3, Forest = 4, Steppe = 5, Fording = 6, Mountain = 7, Lake = 8, Water = 10, River = 11,
            Canyon = 13, RuralArea = 14, Swamp = 15, Dune = 16, Bridge = 17, CoastalSea = 18, OpenSea = 19, Beach = 20, Cliff = 21, NonNavigableRiver = 22,
            LandRestriction = 23, SeaRestriction = 24, UnderBridge = 25;

        /// <summary>The ground a land party may walk on in the base game (DefaultPartyNavigationModel); the export records the game's own list.</summary>
        public static readonly byte[] DefaultWalkable = { Plain, Desert, Snow, Forest, Steppe, Swamp, Dune, Bridge, Fording, Beach };

        public static string Name(byte t)
        {
            switch (t)
            {
                case None: return "no ground";
                case Plain: return "plain";
                case Desert: return "desert";
                case Snow: return "snow";
                case Forest: return "forest";
                case Steppe: return "steppe";
                case Fording: return "ford";
                case Mountain: return "mountain";
                case Lake: return "lake";
                case Water: return "water";
                case River: return "river";
                case Canyon: return "canyon";
                case RuralArea: return "farmland";
                case Swamp: return "swamp";
                case Dune: return "dunes";
                case Bridge: return "bridge";
                case CoastalSea: return "coastal sea";
                case OpenSea: return "open sea";
                case Beach: return "beach";
                case Cliff: return "cliff";
                case NonNavigableRiver: return "river";
                case LandRestriction: return "closed land";
                case SeaRestriction: return "closed sea";
                case UnderBridge: return "under a bridge";
                default: return "ground " + t;
            }
        }

        public static bool IsCrossing(byte t) => t == Bridge || t == Fording;
    }

    /// <summary>meta.json beside the walking map: where it lies, how fine its cells are, and the game's rules read when it was made.</summary>
    public sealed class MapMeta
    {
        public int Version { get; set; }
        public string Crc { get; set; }
        public float MinX { get; set; }
        public float MinY { get; set; }
        public float MaxX { get; set; }
        public float MaxY { get; set; }
        public float Cell { get; set; } = 1f;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Faces { get; set; }
        public int[] Walkable { get; set; }
        public int SunRise { get; set; } = 2;
        public int SunSet { get; set; } = 22;
        public string SpeedModel { get; set; }
        public string NavigationModel { get; set; }
        public int HeightStep { get; set; } = 2;
        public int HeightWidth { get; set; }
        public int HeightHeight { get; set; }
        public float HeightMin { get; set; }
        public float HeightMax { get; set; }
        public string Made { get; set; }
        public double GameMs { get; set; }
        public double Seconds { get; set; }
        public Dictionary<string, int> FaceCounts { get; set; }
    }

    /// <summary>A place from places.json: a holding or hideout, where it stands and where its gate is (parties march to the gate).</summary>
    public sealed class Place
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Gx { get; set; }
        public float Gy { get; set; }
    }

    /// <summary>A bridge or ford face of the game's own walking map (crossing_faces.json): its centre and ground type.</summary>
    public sealed class CrossingFace
    {
        public float X { get; set; }
        public float Y { get; set; }
        public int T { get; set; }
    }

    public static class JsonText
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(fs, Options);
        }

        /// <summary>Written beside the old file and swapped in, so a reader never sees half of it.</summary>
        public static void Write(string path, object o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".part";
            File.WriteAllText(tmp, JsonSerializer.Serialize(o, o.GetType(), Options));
            File.Move(tmp, path, true);
        }
    }

    /// <summary>
    /// The walking map the bridge exported (MapExport): one cell per map unit (meta.cell), each holding the game's ground
    /// type at the cell's centre, row by row from the south-west corner. Which ground can be walked on comes from the
    /// game's own navigation model at export time. Also the places, the bridge and ford faces, and the heights for shading.
    /// </summary>
    public sealed class WalkMap
    {
        public readonly MapMeta Meta;
        public readonly byte[] Ground;
        public readonly bool[] Walk;
        public readonly int W, H;
        public readonly float Cell, X0, Y0;
        public List<Place> Places = new List<Place>();
        public List<CrossingFace> CrossingFaces = new List<CrossingFace>();
        public float[] Heights;
        public string Dir;
        private readonly bool[] _walkable = new bool[256];

        public WalkMap(MapMeta meta, byte[] ground)
        {
            Meta = meta;
            Ground = ground;
            W = meta.Width;
            H = meta.Height;
            Cell = meta.Cell <= 0 ? 1f : meta.Cell;
            X0 = meta.MinX;
            Y0 = meta.MinY;
            foreach (int t in meta.Walkable ?? Routes.Ground.DefaultWalkable.Select(b => (int)b).ToArray())
                if (t >= 0 && t < 256) _walkable[t] = true;
            Walk = new bool[ground.Length];
            for (int i = 0; i < ground.Length; i++) Walk[i] = _walkable[ground[i]];
        }

        public bool IsWalkable(byte t) => _walkable[t];

        /// <summary>The newest complete map under root\map (meta.json, ground.bin), or the one named.</summary>
        public static string FindDir(string root, string crc = null)
        {
            string maps = Path.Combine(root, "map");
            if (!Directory.Exists(maps)) return null;
            if (!string.IsNullOrEmpty(crc))
            {
                string d = Path.Combine(maps, crc);
                return File.Exists(Path.Combine(d, "meta.json")) && File.Exists(Path.Combine(d, "ground.bin")) ? d : null;
            }
            return Directory.GetDirectories(maps)
                .Where(d => File.Exists(Path.Combine(d, "meta.json")) && File.Exists(Path.Combine(d, "ground.bin")))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "meta.json")))
                .FirstOrDefault();
        }

        public static WalkMap Load(string dir)
        {
            MapMeta meta = JsonText.Read<MapMeta>(Path.Combine(dir, "meta.json"));
            if (meta == null) throw new FileNotFoundException("no meta.json in " + dir);
            byte[] ground = File.ReadAllBytes(Path.Combine(dir, "ground.bin"));
            if (ground.Length != meta.Width * meta.Height) throw new InvalidDataException("ground.bin holds " + ground.Length + " cells, meta.json says " + meta.Width + " x " + meta.Height);
            var m = new WalkMap(meta, ground) { Dir = dir };
            m.Places = JsonText.Read<List<Place>>(Path.Combine(dir, "places.json")) ?? new List<Place>();
            m.CrossingFaces = JsonText.Read<List<CrossingFace>>(Path.Combine(dir, "crossing_faces.json")) ?? new List<CrossingFace>();
            string hp = Path.Combine(dir, "height.bin");
            if (File.Exists(hp) && meta.HeightWidth > 0 && meta.HeightHeight > 0)
            {
                byte[] raw = File.ReadAllBytes(hp);
                if (raw.Length == meta.HeightWidth * meta.HeightHeight * 4)
                {
                    m.Heights = new float[meta.HeightWidth * meta.HeightHeight];
                    Buffer.BlockCopy(raw, 0, m.Heights, 0, raw.Length);
                }
            }
            m.StampCrossingFaces();
            return m;
        }

        /// <summary>
        /// A bridge or ford narrower than a cell can fall between cell centres, and the river then looks closed. Every bridge
        /// and ford face's centre is walkable in the game, so its cell is made walkable with that ground.
        /// </summary>
        private void StampCrossingFaces()
        {
            foreach (CrossingFace f in CrossingFaces)
            {
                int i = Index(f.X, f.Y);
                if (i < 0) continue;
                if (!Walk[i])
                {
                    Walk[i] = true;
                    Ground[i] = (byte)f.T;
                }
            }
        }

        public int Index(float x, float y)
        {
            int cx = (int)Math.Floor((x - X0) / Cell), cy = (int)Math.Floor((y - Y0) / Cell);
            return cx < 0 || cy < 0 || cx >= W || cy >= H ? -1 : cy * W + cx;
        }

        public void CellOf(float x, float y, out int cx, out int cy)
        {
            cx = (int)Math.Floor((x - X0) / Cell);
            cy = (int)Math.Floor((y - Y0) / Cell);
        }

        public float CenterX(int i) => X0 + ((i % W) + 0.5f) * Cell;
        public float CenterY(int i) => Y0 + ((i / W) + 0.5f) * Cell;

        public byte GroundAt(float x, float y)
        {
            int i = Index(x, y);
            return i < 0 ? Routes.Ground.None : Ground[i];
        }

        public bool WalkableAt(float x, float y)
        {
            int i = Index(x, y);
            return i >= 0 && Walk[i];
        }

        /// <summary>The walkable cell nearest a point, searched in rings out to maxCells (a gate or a party may sit on a cell's edge).</summary>
        public int NearestWalkable(float x, float y, int maxCells = 12, bool[] blocked = null)
        {
            CellOf(x, y, out int cx, out int cy);
            for (int r = 0; r <= maxCells; r++)
            {
                int best = -1;
                float bestD = float.MaxValue;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int i = ny * W + nx;
                        if (!Walk[i] || (blocked != null && blocked[i])) continue;
                        float ddx = CenterX(i) - x, ddy = CenterY(i) - y, d = ddx * ddx + ddy * ddy;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                if (best >= 0) return best;
            }
            return -1;
        }

        public Place FindPlace(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;
            string q = idOrName.Trim().Trim('[', ']');
            Place p = Places.FirstOrDefault(x => string.Equals(x.Id, q, StringComparison.OrdinalIgnoreCase));
            if (p != null) return p;
            string plain = Words.Plain(q);
            return Places.FirstOrDefault(x => Words.Plain(x.Name) == plain)
                   ?? Places.Where(x => x.Kind != "hideout").FirstOrDefault(x => Words.Plain(x.Name).StartsWith(plain, StringComparison.Ordinal));
        }

        /// <summary>The nearest town, castle or village to a point, and how far.</summary>
        public Place NearestPlace(float x, float y, out float distance, bool holdingsOnly = true)
        {
            Place best = null;
            distance = float.MaxValue;
            foreach (Place p in Places)
            {
                if (holdingsOnly && p.Kind != "town" && p.Kind != "castle" && p.Kind != "village") continue;
                float d = Dist(p.X, p.Y, x, y);
                if (d < distance) { distance = d; best = p; }
            }
            return best;
        }

        /// <summary>"8 east of Tharbad", or "at Tharbad" when within a unit and a half.</summary>
        public string Describe(float x, float y)
        {
            Place p = NearestPlace(x, y, out float d);
            if (p == null) return "in the wilds";
            if (d < 1.5f) return "at " + p.Name;
            return Math.Round(d).ToString("0") + " " + Compass(p.X, p.Y, x, y) + " of " + p.Name;
        }

        /// <summary>Which way a point lies from another: map y grows to the north, x to the east.</summary>
        public static string Compass(float fromX, float fromY, float x, float y)
        {
            double a = Math.Atan2(y - fromY, x - fromX) * 180 / Math.PI;   // 0 = east, 90 = north
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            int k = (int)Math.Round(((a % 360) + 360) % 360 / 45.0) % 8;
            return names[k];
        }

        public static float Dist(float x1, float y1, float x2, float y2) => (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
    }

    public static class Words
    {
        /// <summary>Lower case with the accents removed, so "Nazgûl" and "nazgul" match.</summary>
        public static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(d.Length);
            foreach (char c in d)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC).Trim();
        }

        public static string N(double n) => Math.Round(n).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        public static string D1(double n) => n.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        public static string Pct(double share) => Math.Round(share * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }
}
