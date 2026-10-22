using System.Text.Json;
using System.Text.Json.Nodes;
using BannerlordAgents.Routes;

namespace BannerlordAgents.Dashboard
{
    /// <summary>One load of a campaign: its frames (hours), and what loads.jsonl says of it (when it began, where each record file stood).</summary>
    internal sealed class LoadInfo
    {
        public string Campaign, LoadId, Wall, Realm;
        public double LoadedAt = double.NaN;
        public List<long> Hours = new List<long>();
        public Dictionary<string, long> Offsets = new Dictionary<string, long>();
        /// <summary>Where each record file stood when the next load began (the end of this load's lines), when there is one.</summary>
        public Dictionary<string, long> Ends = new Dictionary<string, long>();
    }

    /// <summary>
    /// What the dashboard reads, all of it written by others: live\current.json and the frames (the bridge's LiveMap), the
    /// walking map and the atlas (map\), the campaign's records (data\), and the war room's logs. Read-only, except the atlas,
    /// the picture and the measured speeds, which it adds beside the walking map.
    /// </summary>
    internal sealed class Store
    {
        public readonly string Root;
        private readonly object _gate = new object();
        private WalkMap _map;
        private Finder _finder;
        private readonly object _finderGate = new object();
        private Atlas _atlas;
        private DateTime _atlasTime;
        private SpeedFactors _speeds;
        private DateTime _speedsTime;

        public Store(string root)
        {
            Root = root;
            Log.Dir = Path.Combine(root, "logs", "dashboard");
            try { Directory.CreateDirectory(Log.Dir); } catch (IOException) { Log.Dir = null; }
        }

        public string LiveDir => Path.Combine(Root, "live");
        public string WarRoomLogs => Path.Combine(Root, "logs", "warroom");
        /// <summary>A second agent's war room logs (log_dir in its warroom.txt, set up by scriptssetup-agent.ps1).</summary>
        public string WarRoomLogsOf(string realm) => Path.Combine(Root, "logs", "warroom-" + realm);
        public string DataDir(string campaign) => Path.Combine(Root, "data", campaign ?? "unknown");

        public JsonObject Current()
        {
            try { return ReadJson(Path.Combine(LiveDir, "current.json")) as JsonObject; }
            catch (Exception e) when (e is IOException || e is JsonException) { return null; }
        }

        public static JsonNode ReadJson(string path)
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(fs);
        }

        /// <summary>Every campaign with frames, newest first.</summary>
        public List<string> Campaigns()
        {
            if (!Directory.Exists(LiveDir)) return new List<string>();
            return Directory.GetDirectories(LiveDir).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(Path.GetFileName).ToList();
        }

        /// <summary>A campaign's loads with frames, newest first, joined with loads.jsonl.</summary>
        public List<LoadInfo> Loads(string campaign)
        {
            var list = new List<LoadInfo>();
            string dir = Path.Combine(LiveDir, campaign ?? "");
            if (!Directory.Exists(dir)) return list;
            Dictionary<string, LoadInfo> marks = LoadMarks(campaign);
            foreach (string d in Directory.GetDirectories(dir).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
            {
                string id = Path.GetFileName(d);
                LoadInfo li = marks.TryGetValue(id, out LoadInfo m) ? m : new LoadInfo { Campaign = campaign, LoadId = id };
                li.Hours = FrameHours(campaign, id);
                if (li.Hours.Count > 0) list.Add(li);
            }
            return list;
        }

        public LoadInfo Load(string campaign, string loadId)
        {
            Dictionary<string, LoadInfo> marks = LoadMarks(campaign);
            LoadInfo li = marks.TryGetValue(loadId ?? "", out LoadInfo m) ? m : new LoadInfo { Campaign = campaign, LoadId = loadId };
            li.Hours = FrameHours(campaign, loadId);
            return li;
        }

        /// <summary>loads.jsonl: each load's start, and where the next one began (so one load's lines can be read alone).</summary>
        private Dictionary<string, LoadInfo> LoadMarks(string campaign)
        {
            var all = new List<LoadInfo>();
            string path = Path.Combine(DataDir(campaign), "loads.jsonl");
            foreach (string line in ReadLines(path))
            {
                try
                {
                    if (JsonNode.Parse(line) is not JsonObject o) continue;
                    var li = new LoadInfo { Campaign = campaign, LoadId = (string)o["load_id"], Wall = (string)o["wall"], Realm = (string)o["realm"] };
                    if (o["loaded_at"] is JsonValue la && la.TryGetValue(out double h)) li.LoadedAt = h;
                    if (o["offsets"] is JsonObject off)
                        foreach (KeyValuePair<string, JsonNode> kv in off)
                            if (kv.Value is JsonValue v && v.TryGetValue(out long n)) li.Offsets[kv.Key] = n;
                    all.Add(li);
                }
                catch (JsonException) { }
            }
            for (int i = 0; i + 1 < all.Count; i++) all[i].Ends = all[i + 1].Offsets;
            var d = new Dictionary<string, LoadInfo>();
            foreach (LoadInfo li in all) if (li.LoadId != null) d[li.LoadId] = li;
            return d;
        }

        public List<long> FrameHours(string campaign, string loadId)
        {
            string dir = Path.Combine(LiveDir, campaign ?? "", loadId ?? "", "frames");
            if (!Directory.Exists(dir)) return new List<long>();
            var hours = new List<long>();
            foreach (string f in Directory.GetFiles(dir, "h*.json.gz"))
            {
                string n = Path.GetFileName(f);
                if (long.TryParse(n.Substring(1, n.Length - 1 - ".json.gz".Length), out long h)) hours.Add(h);
            }
            hours.Sort();
            return hours;
        }

        public string FramePath(string campaign, string loadId, long hour) => Path.Combine(LiveDir, campaign, loadId, "frames", "h" + hour + ".json.gz");

        /// <summary>A frame, read whole (for the checks): its JSON, unzipped.</summary>
        public JsonObject ReadFrame(string path)
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
            return JsonNode.Parse(gz) as JsonObject;
        }

        // ---------- the walking map, its atlas and the measured speeds ----------

        public string MapDir(string crc) => WalkMap.FindDir(Root, crc);

        /// <summary>The walking map (one kept in memory: a campaign uses one map), or null while the bridge has not copied it yet.</summary>
        public WalkMap Map(string crc)
        {
            lock (_gate)
            {
                if (_map != null && (crc == null || _map.Meta.Crc == crc)) return _map;
                string dir = MapDir(crc);
                if (dir == null) return null;
                try
                {
                    _map = WalkMap.Load(dir);
                    _finder = null;
                    _atlas = null;
                    _speeds = null;
                    Log.Say("Walking map " + _map.Meta.Crc + " loaded: " + _map.W + " x " + _map.H + " cells, " + _map.Places.Count + " places.");
                    return _map;
                }
                catch (Exception e) { Log.Say("The walking map in " + dir + " could not be read: " + e.Message); return null; }
            }
        }

        /// <summary>The one way-finder (its working memory is large), used under its lock by the routes asked for and by the checks.</summary>
        public T WithFinder<T>(WalkMap m, Func<Finder, T> job)
        {
            lock (_finderGate)
            {
                if (_finder == null || _finder.Map != m) _finder = new Finder(m);
                return job(_finder);
            }
        }

        public Atlas Atlas(WalkMap m)
        {
            if (m?.Dir == null) return null;
            string path = Routes.Atlas.PathIn(m.Dir);
            lock (_gate)
            {
                DateTime t = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_atlas != null && t == _atlasTime) return _atlas;
                if (t == DateTime.MinValue) return null;
                try { _atlas = Routes.Atlas.Load(m.Dir); _atlasTime = t; }
                catch (Exception e) { Log.Say("The atlas could not be read: " + e.Message); _atlas = null; }
                return _atlas;
            }
        }

        public SpeedFactors Speeds(WalkMap m)
        {
            string path = m?.Dir == null ? null : Path.Combine(m.Dir, "speed_factors.json");
            lock (_gate)
            {
                DateTime t = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_speeds != null && t == _speedsTime) return _speeds;
                _speeds = SpeedFactors.Load(m?.Dir);
                _speedsTime = t;
                return _speeds;
            }
        }

        /// <summary>The whole lines between two byte offsets of a file (a line still being written at the end is left for later); end: where they stop.</summary>
        public static List<string> ReadLines(string path, long from, long to, out long end)
        {
            end = from;
            var lines = new List<string>();
            byte[] data;
            try
            {
                if (!File.Exists(path)) return lines;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Min(Math.Max(0, from), fs.Length), stop = Math.Min(to, fs.Length);
                if (stop <= start) return lines;
                data = new byte[stop - start];
                fs.Seek(start, SeekOrigin.Begin);
                int got = 0;
                while (got < data.Length)
                {
                    int n = fs.Read(data, got, data.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
                if (got < data.Length) Array.Resize(ref data, got);
                end = start;
            }
            catch (IOException) { return lines; }
            int a = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != (byte)'\n') continue;
                if (i > a) lines.Add(System.Text.Encoding.UTF8.GetString(data, a, i - a).TrimEnd('\r'));
                a = i + 1;
            }
            end += a;
            return lines;
        }

        public static List<string> ReadLines(string path) => ReadLines(path, 0, long.MaxValue, out _);
    }
}
