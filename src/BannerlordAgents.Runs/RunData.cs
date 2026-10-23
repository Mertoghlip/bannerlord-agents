using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BannerlordAgents.Runs
{
    /// <summary>One test run as its files hold it: run.json, setup.json, days.jsonl, events.jsonl, and its agents' war room calls.</summary>
    internal sealed class RunData
    {
        public string Id, Dir;
        public JsonObject State, Setup;
        public List<JsonObject> Days = new List<JsonObject>();
        public List<JsonObject> Events = new List<JsonObject>();
        public List<Call> Calls = new List<Call>();
        public List<AgentInfo> Agents = new List<AgentInfo>();
        public List<(DateTime From, DateTime To)> Windows = new List<(DateTime, DateTime)>();

        public string Arm => Str(State, "arm") ?? "(not begun)";
        public string Status => Str(State, "status");
        public string Kingdom => Str(State, "kingdom");
        public List<string> Rivals => (State?["rivals"] as JsonArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        public List<string> Realms => new[] { Kingdom }.Where(x => x != null).Concat(Rivals).Distinct().ToList();
        public int DaysPlanned => Int(State, "days", 60);
        public JsonObject LastDay => Days.LastOrDefault();
        public JsonObject FirstDay => Days.FirstOrDefault();

        public static string Root
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("BANNERLORD_AGENTS_ROOT");
                string root = !string.IsNullOrWhiteSpace(over) ? over.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents");
                return root;
            }
        }

        public static string RunsDir => Path.Combine(Root, "runs");

        /// <summary>The run named, or the open one (current.txt), or the newest.</summary>
        public static string Pick(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && id != "latest") return id;
            string cur = Path.Combine(RunsDir, "current.txt");
            if (File.Exists(cur))
            {
                string c = ReadShared(cur).Trim();
                if (c.Length > 0 && Directory.Exists(Path.Combine(RunsDir, c))) return c;
            }
            return All().LastOrDefault();
        }

        /// <summary>Every run folder with a run.json, oldest first.</summary>
        public static List<string> All() =>
            !Directory.Exists(RunsDir) ? new List<string>()
            : Directory.GetDirectories(RunsDir).Where(d => File.Exists(Path.Combine(d, "run.json"))).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList();

        public static RunData Load(string id, bool calls = true)
        {
            var r = new RunData { Id = id, Dir = Path.Combine(RunsDir, id) };
            r.State = Obj(Path.Combine(r.Dir, "run.json"));
            r.Setup = Obj(Path.Combine(r.Dir, "setup.json"));
            r.Days = Lines(Path.Combine(r.Dir, "days.jsonl"));
            r.Events = Lines(Path.Combine(r.Dir, "events.jsonl"));
            foreach (JsonNode seg in r.State?["segments"] as JsonArray ?? new JsonArray())
            {
                DateTime from = Wall(Str(seg, "wall_from")), to = Wall(Str(seg, "wall_to"));
                if (from == DateTime.MinValue) continue;
                r.Windows.Add((from, to == DateTime.MinValue ? DateTime.Now.AddMinutes(1) : to));
            }
            foreach (JsonNode a in r.Setup?["agents"] as JsonArray ?? new JsonArray())
            {
                string logDir = Str(a, "log_dir");
                if (string.IsNullOrEmpty(logDir)) logDir = Path.Combine(Root, "logs", "warroom");
                r.Agents.Add(new AgentInfo { Realm = Str(a, "realm"), Kind = Str(a, "kind") ?? "realm", Mode = Str(a, "mode"), LogDir = logDir });
            }
            if (calls) foreach (AgentInfo a in r.Agents) r.Calls.AddRange(ReadCalls(a, 0, out _).Where(c => r.InRun(c.At)));
            return r;
        }

        public bool InRun(DateTime t) => Windows.Any(w => t >= w.From && t <= w.To);

        /// <summary>A war room's calls from a byte offset on (the watcher's cursor); next: where the file ends now.</summary>
        public static List<Call> ReadCalls(AgentInfo a, long from, out long next)
        {
            var list = new List<Call>();
            string path = Path.Combine(a.LogDir, "calls.jsonl");
            next = from;
            if (!File.Exists(path)) return list;
            foreach (JsonObject o in LinesFrom(path, from, out next))
            {
                var c = new Call
                {
                    Agent = a, Kind = Str(o, "kind") ?? "?", At = Wall(Str(o, "at")), Ok = o["ok"]?.GetValue<bool>() ?? true, Problem = Str(o, "problem"),
                    Seconds = Dbl(o, "seconds"), In = Lng(o, "tokens_in"), Out = Lng(o, "tokens_out"), Thinking = Lng(o, "thinking"),
                    CacheRead = Lng(o, "cache_read"), CacheWrite = Lng(o, "cache_write"), Cost = Dbl(o, "cost_usd_list"), Poke = Int(o, "poke", -1), Campaign = Str(o, "campaign")
                };
                list.Add(c);
            }
            return list;
        }

        /// <summary>The run's day a moment of real time fell in: the day after the last day line written before it.</summary>
        public int DayAt(DateTime t)
        {
            int day = 1;
            foreach (JsonObject d in Days)
            {
                DateTime w = Wall(Str(d, "wall"));
                if (w <= t) day = Int(d, "day", 0) + 1;
                else break;
            }
            return Math.Min(day, DaysPlanned);
        }

        // ---------- reading ----------

        public static string ReadShared(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        public static JsonObject Obj(string path)
        {
            try { return File.Exists(path) ? JsonNode.Parse(ReadShared(path)) as JsonObject : null; }
            catch (Exception) { return null; }
        }

        public static List<JsonObject> Lines(string path) => File.Exists(path) ? LinesFrom(path, 0, out _) : new List<JsonObject>();

        /// <summary>Whole lines from a byte offset; a line still being written (no line end yet) waits for the next look.</summary>
        public static List<JsonObject> LinesFrom(string path, long from, out long next)
        {
            var list = new List<JsonObject>();
            next = from;
            if (!File.Exists(path)) return list;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (from > fs.Length) from = 0;   // the file was started anew
            fs.Seek(from, SeekOrigin.Begin);
            var buf = new byte[fs.Length - from];
            int n = 0;
            while (n < buf.Length)
            {
                int got = fs.Read(buf, n, buf.Length - n);
                if (got <= 0) break;
                n += got;
            }
            int start = 0;
            for (int i = 0; i < n; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                string line = Encoding.UTF8.GetString(buf, start, i - start).Trim();
                start = i + 1;
                if (line.Length == 0) continue;
                try { if (JsonNode.Parse(line) is JsonObject o) list.Add(o); }
                catch (Exception) { /* a broken line is skipped */ }
            }
            next = from + start;
            return list;
        }

        public static DateTime Wall(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset dto)) return dto.LocalDateTime;
            return DateTime.MinValue;
        }

        public static string Str(JsonNode o, string key)
        {
            try { JsonNode v = o?[key]; return v == null ? null : v is JsonValue ? v.ToString() : v.ToJsonString(); }
            catch (Exception) { return null; }
        }

        public static int Int(JsonNode o, string key, int fallback = 0) => Num(o, key, out double d) ? (int)Math.Round(d) : fallback;

        public static long Lng(JsonNode o, string key) => Num(o, key, out double d) ? (long)Math.Round(d) : 0;

        public static double Dbl(JsonNode o, string key) => Num(o, key, out double d) ? d : 0;

        /// <summary>
        /// A number, whether it was read from a file (any numeric type converts) or put in by this program (a value made from a
        /// long or an int converts only to its own type, so it is read through its text).
        /// </summary>
        private static bool Num(JsonNode o, string key, out double d)
        {
            d = 0;
            try
            {
                if (o?[key] is not JsonValue v) return false;
                if (v.TryGetValue(out double x)) { d = x; return true; }
                if (v.TryGetValue(out long l)) { d = l; return true; }
                if (v.TryGetValue(out int i)) { d = i; return true; }
                return double.TryParse(v.ToJsonString().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            }
            catch (Exception) { return false; }
        }
    }

    internal sealed class AgentInfo
    {
        public string Realm, Kind, Mode, LogDir;
        public string Name => Kind == "lieutenant" ? "lieutenant" : Realm;
    }

    /// <summary>One call of a war room: an answer, a council, a chronicler's story, a lieutenant's chat.</summary>
    internal sealed class Call
    {
        public AgentInfo Agent;
        public string Kind, Problem, Campaign;
        public DateTime At;
        public bool Ok;
        public double Seconds, Cost;
        public long In, Out, Thinking, CacheRead, CacheWrite;
        public int Poke;

        /// <summary>Every token the call read (fresh, from the cache, and written to the cache) and wrote.</summary>
        public long Total => In + CacheRead + CacheWrite + Out;
        public bool LimitHit => !Ok && Problem != null && (Problem.Contains("limit", StringComparison.OrdinalIgnoreCase) || Problem.Contains("rate", StringComparison.OrdinalIgnoreCase)
                                                            || Problem.Contains("usage", StringComparison.OrdinalIgnoreCase) || Problem.Contains("429") || Problem.Contains("quota", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Token and time sums over a set of calls.</summary>
    internal sealed class Usage
    {
        public int Calls, Failed, Limits;
        public long In, Out, Thinking, CacheRead, CacheWrite;
        public double Cost;
        public List<double> Seconds = new List<double>();

        public static Usage Of(IEnumerable<Call> calls)
        {
            var u = new Usage();
            foreach (Call c in calls)
            {
                u.Calls++;
                if (!c.Ok) u.Failed++;
                if (c.LimitHit) u.Limits++;
                u.In += c.In; u.Out += c.Out; u.Thinking += c.Thinking; u.CacheRead += c.CacheRead; u.CacheWrite += c.CacheWrite; u.Cost += c.Cost;
                if (c.Ok && c.Seconds > 0) u.Seconds.Add(c.Seconds);
            }
            return u;
        }

        public long Read => In + CacheRead + CacheWrite;
        public double Median => Pct(0.5);
        public double P90 => Pct(0.9);

        public double Pct(double p)
        {
            if (Seconds.Count == 0) return double.NaN;
            List<double> s = Seconds.OrderBy(x => x).ToList();
            return s[Math.Min(s.Count - 1, (int)Math.Floor(p * s.Count))];
        }

        /// <summary>The most tokens any rolling window of this many hours took (the subscription's limits run on 5-hour windows).</summary>
        public static long PeakWindow(IEnumerable<Call> calls, double hours, out DateTime from)
        {
            List<Call> s = calls.OrderBy(c => c.At).ToList();
            long best = 0, sum = 0;
            from = DateTime.MinValue;
            int a = 0;
            for (int b = 0; b < s.Count; b++)
            {
                sum += s[b].Total;
                while (s[b].At - s[a].At > TimeSpan.FromHours(hours)) sum -= s[a++].Total;
                if (sum > best) { best = sum; from = s[a].At; }
            }
            return best;
        }
    }
}
