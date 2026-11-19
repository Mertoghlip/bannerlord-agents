using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.Dashboard
{
    /// <summary>One load of a test run as run.json has it: the game loaded, from which in-game hour to which, and why.</summary>
    internal sealed class RunSeg
    {
        public string Load { get; set; }
        public string How { get; set; }
        public double From { get; set; } = double.NaN;
        public double To { get; set; } = double.NaN;
        /// <summary>The hours of this load that are the run's own: from its start up to where the next load took over, as frames on disk.</summary>
        public List<long> Hours { get; set; } = new List<long>();
        /// <summary>Where the kept stretch ends in hours (exclusive): the next load's start, else past the end.</summary>
        public double Until { get; set; } = double.PositiveInfinity;
    }

    /// <summary>A test run (runs\(id)\run.json) and the line it kept across its loads: each in-game hour once, from the load whose stretch it is.</summary>
    internal sealed class RunInfo
    {
        public string Id { get; set; }
        public string Arm { get; set; }
        public string Save { get; set; }
        public string Status { get; set; }
        public string Campaign { get; set; }
        public string Kingdom { get; set; }
        public string KingdomName { get; set; }
        public string Started { get; set; }
        public string Finished { get; set; }
        public int Days { get; set; }
        public Dictionary<string, string> Who { get; set; } = new Dictionary<string, string>();
        public List<string> Saves { get; set; } = new List<string>();
        public List<RunSeg> Segments { get; set; } = new List<RunSeg>();
        public int HourCount => Segments.Sum(s => s.Hours.Count);
        public long First => Segments.SelectMany(s => s.Hours).DefaultIfEmpty(0).Min();
        public long Last => Segments.SelectMany(s => s.Hours).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// Whole test runs for playing back (docs/DASHBOARD.md, "Recordings", since 0.10): the run's kept line across its loads, a light copy of
    /// each hour for smooth playback (made from the bridge's frame, kept in dashboard\slim\), and the run's news and the agents' decisions
    /// along the line. Only reads what the bridge, the war rooms and the run keeper write; its own copies go under dashboard\.
    /// </summary>
    internal static class Recording
    {
        // ---------- the runs and their kept lines ----------

        public static string RunsDir(Store store) => Path.Combine(store.Root, "runs");

        /// <summary>Every run that began and has hours on disk, newest first.</summary>
        public static List<RunInfo> Runs(Store store)
        {
            var list = new List<RunInfo>();
            string dir = RunsDir(store);
            if (!Directory.Exists(dir)) return list;
            foreach (string d in Directory.GetDirectories(dir))
            {
                RunInfo r = Read(store, Path.GetFileName(d));
                if (r != null && r.HourCount > 0) list.Add(r);
            }
            return list.OrderByDescending(r => r.Started, StringComparer.Ordinal).ToList();
        }

        /// <summary>One run with its kept line, or null when it never began.</summary>
        public static RunInfo Read(Store store, string id)
        {
            if (string.IsNullOrEmpty(id) || id.Contains("..")) return null;
            string file = Path.Combine(RunsDir(store), id, "run.json");
            JsonObject o;
            try { o = Store.ReadJson(file) as JsonObject; }
            catch (Exception e) when (e is IOException || e is JsonException) { return null; }
            if (o == null || string.IsNullOrEmpty(Str(o["started"]))) return null;
            var r = new RunInfo
            {
                Id = Str(o["id"]) ?? id, Arm = Str(o["arm"]), Save = Str(o["save"]), Status = Str(o["status"]), Campaign = Str(o["campaign"]),
                Kingdom = Str(o["kingdom"]), KingdomName = Str(o["kingdom_name"]), Started = Str(o["started"]), Finished = Str(o["finished"]),
                Days = o["days"] is JsonValue dv && dv.TryGetValue(out int days) ? days : 0
            };
            if (o["who"] is JsonObject who) foreach (KeyValuePair<string, JsonNode> kv in who) if (Str(kv.Value) is string w) r.Who[kv.Key] = w;
            if (o["saves"] is JsonArray saves) r.Saves = saves.Select(Str).Where(s => s != null).ToList();
            if (o["segments"] is JsonArray segs)
                foreach (JsonNode n in segs)
                    if (n is JsonObject s && Str(s["load"]) is string load)
                        r.Segments.Add(new RunSeg { Load = load, How = Str(s["how"]), From = Num(s["from"]), To = Num(s["to"]) });
            KeepLine(r, load => store.FrameHours(r.Campaign, load));
            return r;
        }

        /// <summary>
        /// The run's kept line: each load's frames from its start, up to the first frame of the load that took over from it. A load of an
        /// earlier save of the run drops what the load before played past that save (never saved, so not the run's); a load of a later save
        /// leaves the one before whole. Frames are named by the in-game hour they were written in.
        /// </summary>
        public static void KeepLine(RunInfo r, Func<string, List<long>> hoursOf)
        {
            var mine = r.Segments.Select(s => (hoursOf(s.Load) ?? new List<long>()).Where(h => double.IsNaN(s.From) || h >= Math.Floor(s.From)).ToList()).ToList();
            for (int i = 0; i < r.Segments.Count; i++)
            {
                RunSeg s = r.Segments[i];
                double until = double.PositiveInfinity;
                // the next load with frames on disk takes over (one whose frames are gone, from before 0.21.3 kept them, cuts nothing)
                for (int j = i + 1; j < r.Segments.Count; j++)
                    if (mine[j].Count > 0) { until = mine[j][0]; break; }
                s.Until = until;
                s.Hours = mine[i].Where(h => h < until && (double.IsNaN(s.To) || h <= Math.Floor(s.To) + 1)).ToList();
            }
        }

        // ---------- the light copy of an hour ----------

        public static string SlimPath(Store store, string campaign, string load, long hour)
            => Path.Combine(store.Root, "dashboard", "slim", "v" + SlimVersion.ToString(CultureInfo.InvariantCulture), Safe(campaign), Safe(load), "h" + hour.ToString(CultureInfo.InvariantCulture) + ".json.gz");

        /// <summary>The light copy's layout: a change makes them again (kept apart by folder).</summary>
        public const int SlimVersion = 2;

        /// <summary>The light copy of an hour (gzipped JSON), made from the bridge's frame the first time it is asked for; null when that frame is gone.</summary>
        public static byte[] Slim(Store store, string campaign, string load, long hour)
        {
            string path = SlimPath(store, campaign, load, hour);
            try { if (File.Exists(path)) return File.ReadAllBytes(path); }
            catch (IOException) { }
            string frame = store.FramePath(campaign, load, hour);
            if (!File.Exists(frame)) return null;
            byte[] data;
            using (var fs = new FileStream(frame, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (JsonDocument doc = JsonDocument.Parse(gz))
                data = Zip(SlimJson(doc.RootElement));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".part";
                File.WriteAllBytes(tmp, data);
                try { File.Move(tmp, path, true); } catch (IOException) { File.Delete(tmp); }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Log.Say("A light copy could not be kept (" + path + "): " + e.Message); }
            return data;
        }

        /// <summary>
        /// An hour's light copy, in the frame's own shape but with only what the map draws while a run plays: parties as rows
        /// (id, name, kind, faction, x, y, men, strength, army leader, army men, army strength, fighting, inside, leader, doing, target holding,
        /// besieging, raiding, riding in an army), holdings as rows, each realm's view (in sight, seen earlier, cut, counted, its orders), sieges
        /// and battles. Garrisons, militias, and small parties inside a holding or an army are left out (the map never draws them).
        /// </summary>
        public static byte[] SlimJson(JsonElement f)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteNumber("v", 1);
                w.WriteBoolean("slim", true);
                foreach (string k in new[] { "campaign", "load_id", "hours", "date", "night", "player", "player_party", "watched", "map" })
                    if (f.TryGetProperty(k, out JsonElement v)) { w.WritePropertyName(k); v.WriteTo(w); }
                if (f.TryGetProperty("factions", out JsonElement facs))
                {
                    w.WriteStartArray("factions");
                    foreach (JsonElement x in facs.EnumerateArray())
                    {
                        w.WriteStartObject();
                        foreach (string k in new[] { "id", "name", "color", "kingdom", "at_war", "allies" })
                            if (x.TryGetProperty(k, out JsonElement v)) { w.WritePropertyName(k); v.WriteTo(w); }
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                }
                if (f.TryGetProperty("settlements", out JsonElement sets))
                {
                    w.WriteStartArray("settlements");
                    foreach (JsonElement s in sets.EnumerateArray())
                        Row(w, s, "id", "name", "kind", "faction", "x", "y", "besieged_by", "village_state");
                    w.WriteEndArray();
                }
                if (f.TryGetProperty("parties", out JsonElement parts))
                {
                    w.WriteStartArray("parties");
                    foreach (JsonElement p in parts.EnumerateArray())
                    {
                        string kind = S(p, "kind");
                        if (kind == "garrison" || kind == "militia" || (kind != "lord" && B(p, "attached"))) continue;
                        if (kind != "lord" && S(p, "in_settlement") != null) continue;
                        Row(w, p, "id", "name", "kind", "faction", "x", "y", "men", "strength", "army_leader", "army_men", "army_strength",
                            "in_battle", "in_settlement", "leader", "doing", "target_settlement", "besieging", "raiding", "attached");
                    }
                    w.WriteEndArray();
                }
                if (f.TryGetProperty("realms", out JsonElement realms) && realms.ValueKind == JsonValueKind.Object)
                {
                    w.WriteStartObject("realms");
                    foreach (JsonProperty rp in realms.EnumerateObject())
                    {
                        JsonElement r = rp.Value;
                        w.WriteStartObject(rp.Name);
                        foreach (string k in new[] { "name", "agent", "player" })
                            if (r.TryGetProperty(k, out JsonElement v)) { w.WritePropertyName(k); v.WriteTo(w); }
                        foreach (string k in new[] { "cut", "counted_only", "in_sight" })
                            if (r.TryGetProperty(k, out JsonElement v)) { w.WritePropertyName(k); v.WriteTo(w); }
                        if (r.TryGetProperty("orders", out JsonElement ord)) { w.WritePropertyName("orders"); ord.WriteTo(w); }
                        if (r.TryGetProperty("remembered", out JsonElement rem)) ViewRows(w, "remembered", rem);
                        if (r.TryGetProperty("view", out JsonElement view))
                        {
                            w.WriteStartObject("view");
                            if (view.TryGetProperty("seen", out JsonElement seen)) ViewRows(w, "seen", seen);
                            if (view.TryGetProperty("last_seen", out JsonElement last)) ViewRows(w, "last_seen", last);
                            if (view.TryGetProperty("holdings", out JsonElement hold))
                            {
                                w.WriteStartArray("holdings");
                                foreach (JsonElement h in hold.EnumerateArray()) { w.WriteStartObject(); w.WriteString("id", S(h, "id")); w.WriteEndObject(); }
                                w.WriteEndArray();
                            }
                            w.WriteEndObject();
                        }
                        w.WriteEndObject();
                    }
                    w.WriteEndObject();
                }
                if (f.TryGetProperty("sieges", out JsonElement sieges))
                {
                    w.WriteStartArray("sieges");
                    foreach (JsonElement s in sieges.EnumerateArray()) Row(w, s, "settlement", "besieger", "besieger_faction", "start_hours");
                    w.WriteEndArray();
                }
                if (f.TryGetProperty("battles", out JsonElement battles))
                {
                    w.WriteStartArray("battles");
                    foreach (JsonElement b in battles.EnumerateArray())
                    {
                        w.WriteStartArray();
                        foreach (string k in new[] { "key", "kind", "settlement", "x", "y", "start_hours" }) Val(w, b, k);
                        foreach (string side in new[] { "attackers", "defenders" })
                            if (b.TryGetProperty(side, out JsonElement sd)) foreach (string k in new[] { "leader", "faction", "healthy", "start_healthy" }) Val(w, sd, k);
                            else for (int i = 0; i < 4; i++) w.WriteNullValue();
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            return ms.ToArray();
        }

        /// <summary>What a realm saw of a party: id, lord, name, faction, relation, kind, doing, men, army men, strength, x, y, when seen.</summary>
        private static void ViewRows(Utf8JsonWriter w, string name, JsonElement list)
        {
            w.WriteStartArray(name);
            if (list.ValueKind == JsonValueKind.Array)
                foreach (JsonElement vp in list.EnumerateArray())
                    Row(w, vp, "id", "lord", "name", "faction", "relation", "kind", "doing", "men", "army_men", "strength", "x", "y", "seen_hours");
            w.WriteEndArray();
        }

        private static void Row(Utf8JsonWriter w, JsonElement o, params string[] keys)
        {
            w.WriteStartArray();
            foreach (string k in keys) Val(w, o, k);
            w.WriteEndArray();
        }

        private static void Val(Utf8JsonWriter w, JsonElement o, string k)
        {
            if (!o.TryGetProperty(k, out JsonElement v) || v.ValueKind == JsonValueKind.Null) { w.WriteNullValue(); return; }
            // positions to a tenth and counts whole: the map needs no more
            if (v.ValueKind == JsonValueKind.Number && (k == "x" || k == "y")) { w.WriteNumberValue(Math.Round(v.GetDouble(), 1)); return; }
            if (v.ValueKind == JsonValueKind.Number && (k == "strength" || k == "army_strength")) { w.WriteNumberValue(Math.Round(v.GetDouble())); return; }
            if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) { w.WriteNumberValue(v.GetBoolean() ? 1 : 0); return; }
            v.WriteTo(w);
        }

        private static string S(JsonElement o, string k) => o.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static bool B(JsonElement o, string k) => o.TryGetProperty(k, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        private static byte[] Zip(byte[] raw)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        // ---------- making a run's light copies ahead, in the background ----------

        private sealed class Making { public int Done, Total; public bool Over; }
        private static readonly ConcurrentDictionary<string, Making> _making = new ConcurrentDictionary<string, Making>();

        /// <summary>Starts making every light copy of a run's line (three at a time) unless under way; how far it has come.</summary>
        public static object Prepare(Store store, RunInfo r)
        {
            var hours = r.Segments.SelectMany(s => s.Hours.Select(h => (s.Load, h))).ToList();
            Making m = _making.GetOrAdd(r.Id, _ =>
            {
                var mk = new Making { Total = hours.Count };
                var t = new Thread(() =>
                {
                    try
                    {
                        Parallel.ForEach(hours, new ParallelOptions { MaxDegreeOfParallelism = 3 }, x =>
                        {
                            try { Slim(store, r.Campaign, x.Load, x.h); }
                            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidDataException) { Log.Say("Hour " + x.h + " of " + x.Load + " could not be lightened: " + e.Message); }
                            Interlocked.Increment(ref mk.Done);
                        });
                    }
                    finally { mk.Over = true; }
                }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "recording " + r.Id };
                t.Start();
                return mk;
            });
            // a run still being played gets new hours: make those too, once the first pass is over
            if (m.Over && m.Total < hours.Count) { _making.TryRemove(r.Id, out _); return Prepare(store, r); }
            return new { done = m.Done, total = m.Total, over = m.Over };
        }

        // ---------- the run's news and the agents' decisions along its line ----------

        private static readonly HashSet<string> NewsKinds = new HashSet<string>
        {
            "battle", "captured", "lord_killed", "siege_started", "siege_ended", "holding_changed", "raided", "looted", "army_formed", "war", "peace", "alliance", "letter"
        };
        private static readonly Regex BattleText = new Regex(@"^Battle: (.+?) \((.+?)\) against (.+?) \((.+?)\); (.+?) won\.", RegexOptions.Compiled);
        private static readonly Regex Trade = new Regex(@"\b(Caravan|Villagers)\b", RegexOptions.Compiled);

        /// <summary>
        /// The game's news along the run's line for its realms (data\notices.jsonl, each load's lines by loads.jsonl, within its kept hours):
        /// battles between realms (not with bands), captures, deaths, sieges begun and lifted, holdings taken, raids, armies formed, wars,
        /// peace, alliances and the agents' letters; each once.
        /// </summary>
        public static List<object> News(Store store, RunInfo r, ISet<string> kingdomNames)
        {
            var realms = new HashSet<string>(r.Who.Keys);
            var seen = new HashSet<string>();
            var list = new List<(double h, object o)>();
            string path = Path.Combine(store.DataDir(r.Campaign), "notices.jsonl");
            foreach (RunSeg s in r.Segments)
            {
                if (s.Hours.Count == 0) continue;
                LoadInfo li = store.Load(r.Campaign, s.Load);
                long from = li.Offsets.TryGetValue("notices.jsonl", out long a) ? a : 0;
                long to = li.Ends.TryGetValue("notices.jsonl", out long b) ? b : long.MaxValue;
                double lo = Math.Max(double.IsNaN(s.From) ? double.NegativeInfinity : s.From, s.Hours[0] - 1), hi = Math.Min(s.Until, s.Hours[s.Hours.Count - 1] + 1);
                foreach (string line in Store.ReadLines(path, from, to, out _))
                {
                    JsonObject o;
                    try { o = JsonNode.Parse(line) as JsonObject; } catch (JsonException) { continue; }
                    string kind = Str(o?["kind"]);
                    if (kind == null || !NewsKinds.Contains(kind)) continue;
                    double h = Num(o["hours"]);
                    if (double.IsNaN(h) || h < lo || h >= hi) continue;
                    if (o["factions"] is JsonArray fs && !fs.Select(Str).Any(x => x != null && realms.Contains(x))) continue;
                    string text = Str(o["text"]) ?? "";
                    if (kind == "battle")
                    {
                        Match m = BattleText.Match(text);
                        if (!m.Success || !kingdomNames.Contains(m.Groups[2].Value) || !kingdomNames.Contains(m.Groups[4].Value)) continue;
                        // a lord against a caravan or villagers is no battle between realms for the run's story
                        if (Trade.IsMatch(m.Groups[1].Value) || Trade.IsMatch(m.Groups[3].Value)) continue;
                    }
                    if (!seen.Add(kind + "|" + text + "|" + Math.Round(h, 1).ToString(CultureInfo.InvariantCulture))) continue;
                    list.Add((h, new { hours = h, kind, text, party = Str(o["party"]), place = Str(o["place"]), factions = o["factions"] is JsonArray fa ? fa.Select(Str).ToList() : null }));
                }
            }
            return list.OrderBy(x => x.h).Select(x => x.o).ToList();
        }

        /// <summary>A realm's log along the run's line (the side log's lines of each load within its kept hours); all kinds, or only decisions.</summary>
        public static List<LogEntry> RealmLog(Store store, LogReader reader, RunInfo r, string realm, bool decisionsOnly)
        {
            var all = new List<LogEntry>();
            foreach (RunSeg s in r.Segments)
            {
                if (s.Hours.Count == 0) continue;
                LoadInfo li = store.Load(r.Campaign, s.Load);
                double lo = Math.Max(double.IsNaN(s.From) ? double.NegativeInfinity : s.From, s.Hours[0] - 1), hi = Math.Min(s.Until, s.Hours[s.Hours.Count - 1] + 1);
                foreach (LogEntry e in reader.Read(r.Campaign, li, realm, int.MaxValue))
                {
                    if (double.IsNaN(e.Hours) || e.Hours < lo || e.Hours >= hi) continue;
                    if (decisionsOnly && e.Kind != "answer" && e.Kind != "council") continue;
                    all.Add(e);
                }
            }
            return all.OrderBy(e => e.Hours).ToList();
        }

        /// <summary>The names of the realms (kingdoms) in the run's first hour, so a battle between two of them can be told from one with a band.</summary>
        public static HashSet<string> KingdomNames(Store store, RunInfo r)
        {
            var set = new HashSet<string>();
            RunSeg s = r.Segments.FirstOrDefault(x => x.Hours.Count > 0);
            if (s == null) return set;
            byte[] slim = Slim(store, r.Campaign, s.Load, s.Hours[0]);
            if (slim == null) return set;
            using var gz = new GZipStream(new MemoryStream(slim), CompressionMode.Decompress);
            using JsonDocument doc = JsonDocument.Parse(gz);
            if (doc.RootElement.TryGetProperty("factions", out JsonElement fs))
                foreach (JsonElement f in fs.EnumerateArray())
                    if (f.TryGetProperty("kingdom", out JsonElement k) && k.ValueKind == JsonValueKind.True && S(f, "name") is string n) set.Add(n);
            return set;
        }

        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
        private static double Num(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
