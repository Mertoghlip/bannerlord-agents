using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BannerlordAgents
{
    /// <summary>
    /// Real hours for the helper's speed test (0.12.0). Once a launch, from perf_record_after in-game hours into a load, the next
    /// perf_record_hours hourly copies of the world are saved, each whole, to perf\snapshots\(campaign)-(load)\h(hour).json.gz
    /// (the road table once, in roads.json.gz), so "SmokeTest --perf (folder)" can run the helper's whole hour on them with every
    /// agent and time each part, without the game. Saved on the helper's regular lane, never its urgent one. Worker thread only.
    /// </summary>
    internal static class PerfRecord
    {
        private static string _load;
        private static int _queued;
        private static double _first = double.NaN;

        /// <summary>Each hour (Brain.Decide): queues this copy to be saved when it falls in the window.</summary>
        public static void Maybe(Snapshot s)
        {
            if (Settings.PerfRecordHours <= 0 || s == null || string.IsNullOrEmpty(s.LoadId) || string.IsNullOrEmpty(s.Campaign)) return;
            if (_load != s.LoadId)
            {
                _load = s.LoadId;
                _queued = 0;
                _first = s.Hours;
            }
            if (_queued >= Settings.PerfRecordHours || s.Hours - _first < Settings.PerfRecordAfter) return;
            _queued++;
            Snapshot copy = s;
            Background.Post("perf: an hour saved for the speed test", () => Save(copy, FolderFor(copy)));
        }

        public static string FolderFor(Snapshot s) => Path.Combine(Paths.Root, "perf", "snapshots", Safe(s.Campaign) + "-" + Safe(s.LoadId));

        /// <summary>Saves one hourly copy (and the road table, once per folder). Returns the hour's file.</summary>
        public static string Save(Snapshot s, string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string roads = Path.Combine(folder, "roads.json.gz");
                if (s.Roads != null && !File.Exists(roads)) Write(roads, s.Roads);
                string path = Path.Combine(folder, "h" + Math.Round(s.Hours).ToString("0", Text.Inv) + ".json.gz");
                long t = System.Diagnostics.Stopwatch.GetTimestamp();
                Write(path, s);
                Diag.Event("perf", "hour saved", new { file = path, hours = s.Hours, parties = s.Parties.Count, bytes = new FileInfo(path).Length,
                                                        ms = Math.Round((System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, 1) });
                return path;
            }
            catch (Exception e)
            {
                Diag.Error("saving an hour for the speed test", e);
                return null;
            }
        }

        /// <summary>The saved hours of a folder, oldest first, each as the helper had it (its indexes rebuilt, the road table back).</summary>
        public static List<Snapshot> Load(string folder)
        {
            string roadsFile = Path.Combine(folder, "roads.json.gz");
            RoadBook roads = File.Exists(roadsFile) ? Read<RoadBook>(roadsFile) : null;
            var list = new List<Snapshot>();
            foreach (string f in Directory.GetFiles(folder, "h*.json.gz"))
            {
                Snapshot s = Read<Snapshot>(f);
                if (s == null) continue;
                s.PartyById.Clear();
                foreach (PartyRow r in s.Parties) if (r?.Id != null) s.PartyById[r.Id] = r;
                s.SettlementById.Clear();
                foreach (SettlementRow r in s.Settlements) if (r?.Id != null) s.SettlementById[r.Id] = r;
                s.Roads = roads;
                list.Add(s);
            }
            return list.OrderBy(x => x.Hours).ToList();
        }

        private static readonly JsonSerializerSettings Options = new JsonSerializerSettings
        {
            ContractResolver = new Fields(), NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None,
            FloatParseHandling = FloatParseHandling.Double, ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        private static void Write(string path, object o)
        {
            string tmp = path + ".part";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
                JsonSerializer.Create(Options).Serialize(w, o);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static T Read<T>(string path) where T : class
        {
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var r = new StreamReader(gz, Encoding.UTF8))
            using (var jr = new JsonTextReader(r))
                return JsonSerializer.Create(Options).Deserialize<T>(jr);
        }

        /// <summary>Every public field, the read-only ones too (filled back on reading); the indexes and the road table are left out of an hour.</summary>
        private sealed class Fields : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty p = base.CreateProperty(member, memberSerialization);
                if (member is FieldInfo) p.Writable = true;
                if (member.DeclaringType == typeof(Snapshot) && (member.Name == nameof(Snapshot.PartyById) || member.Name == nameof(Snapshot.SettlementById) || member.Name == nameof(Snapshot.Roads)))
                    p.Ignored = true;
                return p;
            }
        }

        private static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
