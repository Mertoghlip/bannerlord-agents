using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BannerlordAgents.Runs.RunData;

namespace BannerlordAgents.Runs
{
    /// <summary>
    /// The run's bundle (0.13, docs/PLAN-0.13.md C5): everything of one run in one packed file in its folder, bundle.zip, for the
    /// dataset builder of November. It holds:
    /// - the run's own folder (run.json, setup.json, days.jsonl, events.jsonl, its report);
    /// - for each agent: the war room's calls in the run (calls.jsonl lines, each call's whole folder: prompt, event stream, answer,
    ///   thinking), its questions (routes.jsonl), the system prompts the calls used, its memory's versions (snapshots.jsonl), its
    ///   library of tools;
    /// - the campaign's records in the run: the pokes with their views and reports, the episode index, the toolbook, the diplomacy
    ///   book, the orders and receipts, the notices;
    /// - your own record in the run: your moves, clicks, events, the hourly reports, the lieutenant's conversation.
    /// Lines are taken by the run's tag where they carry one (0.13 on), else by the run's loads (the byte offsets loads.jsonl kept
    /// at each load) and its hours, else by its wall-clock windows. A manifest lists every file with its lines, bytes, fingerprint
    /// and span, and the check says what is missing: an answer's call folder, a poke the index names, a move's report.
    /// </summary>
    internal static class Bundle
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Entry
        {
            public string Name, Source, Kind;
            public int Lines;
            public long Bytes;
            public string Sha, First, Last;
        }

        public static int Run(string id)
        {
            string pick = Pick(id);
            if (pick == null) { Console.WriteLine("No test run yet (" + RunsDir + ")."); return 2; }
            RunData r = Load(pick);
            string campaign = Str(r.State, "campaign") ?? "unknown";
            string data = Path.Combine(Root, "data", Safe(campaign));
            var windows = r.Windows;
            var segments = (r.State?["segments"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Select(s => (Load: Str(s, "load"), From: Dbl(s, "from"), To: s["to"] != null ? Dbl(s, "to") : double.MaxValue)).ToList();
            List<JsonObject> loads = Lines(Path.Combine(data, "loads.jsonl"));
            var entries = new List<Entry>();
            var missing = new List<string>();
            string zipPath = Path.Combine(r.Dir, "bundle.zip"), tmp = zipPath + ".part";
            if (File.Exists(tmp)) File.Delete(tmp);
            var calls = new List<JsonObject>();
            var pokesKept = new HashSet<string>();
            var episodes = new List<JsonObject>();
            var decisions = new List<JsonObject>();
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                // the run's own folder
                foreach (string f in new[] { "run.json", "setup.json", "days.jsonl", "events.jsonl", "report.md", "report.json" })
                    AddFile(zip, entries, Path.Combine(r.Dir, f), "run/" + f, "run");

                // the campaign's records in the run
                foreach (string realm in r.Realms.Concat(r.Agents.Select(a => a.Realm)).Where(x => x != null).Distinct())
                {
                    List<string> kept = Select(Path.Combine(data, "pokes_" + realm + ".jsonl"), r, segments, loads, windows, "pokes_" + realm + ".jsonl");
                    AddLines(zip, entries, kept, Path.Combine(data, "pokes_" + realm + ".jsonl"), "campaign/pokes_" + realm + ".jsonl", "pokes");
                    foreach (string l in kept) { JsonObject o = Parse(l); if (o != null) pokesKept.Add(realm + "#" + Str(o, "number")); }
                }
                foreach (string f in new[] { "episodes.jsonl", "toolbook.jsonl", "diplomacy.jsonl", "allies.jsonl", "orders.jsonl", "notices.jsonl" })
                {
                    List<string> kept = Select(Path.Combine(data, f), r, segments, loads, windows, f);
                    AddLines(zip, entries, kept, Path.Combine(data, f), "campaign/" + f, "campaign");
                    if (f == "episodes.jsonl") episodes.AddRange(kept.Select(Parse).Where(o => o != null));
                }
                // your own record in the run
                foreach (string f in new[] { "decisions.jsonl", "clicks.jsonl", "events.jsonl", "hours.jsonl" })
                {
                    List<string> kept = Select(Path.Combine(data, "record", f), r, segments, loads, windows, null);
                    AddLines(zip, entries, kept, Path.Combine(data, "record", f), "you/" + f, "you");
                    if (f == "decisions.jsonl" || f == "hours.jsonl") decisions.AddRange(kept.Select(Parse).Where(o => o != null));
                }
                foreach (string rel in decisions.Select(d => Str(d, "report")).Where(x => x != null).Distinct())
                {
                    string src = Path.Combine(data, "record", rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(src)) AddFile(zip, entries, src, "you/" + rel, "report");
                    else missing.Add("the report of one of your moves: " + rel);
                }
                List<string> chat = SelectWall(Path.Combine(data, "lieutenant", "chat.jsonl"), windows, "w");
                AddLines(zip, entries, chat, Path.Combine(data, "lieutenant", "chat.jsonl"), "you/lieutenant-chat.jsonl", "you");

                // each agent: its calls, their folders, its questions, its prompts, its memory, its library
                foreach (JsonNode a in r.Setup?["agents"] as JsonArray ?? new JsonArray())
                {
                    string realm = Str(a, "realm"), kind = Str(a, "kind") ?? "realm", mode = Str(a, "mode"), name = kind == "lieutenant" ? "lieutenant" : realm;
                    string logDir = Str(a, "log_dir");
                    if (string.IsNullOrEmpty(logDir)) logDir = Path.Combine(Root, "logs", "warroom");
                    string workspace = Str(a, "workspace");
                    List<string> callLines = SelectWall(Path.Combine(logDir, "calls.jsonl"), windows, "at", id);
                    AddLines(zip, entries, callLines, Path.Combine(logDir, "calls.jsonl"), "agents/" + name + "/calls.jsonl", "calls");
                    var hashes = new HashSet<string>();
                    foreach (string l in callLines)
                    {
                        JsonObject c = Parse(l);
                        if (c == null) continue;
                        calls.Add(c);
                        string folder = Str(c, "folder");
                        if (Str(c, "system_hash") is string h) hashes.Add(h);
                        if (folder == null) continue;
                        if (!Directory.Exists(folder)) { missing.Add(name + "'s call folder " + Path.GetFileName(folder) + " (" + Str(c, "kind") + ", " + Str(c, "at") + ")"); continue; }
                        foreach (string f in Directory.GetFiles(folder))
                            AddFile(zip, entries, f, "agents/" + name + "/calls/" + Path.GetFileName(Path.GetDirectoryName(folder)) + "/" + Path.GetFileName(folder) + "/" + Path.GetFileName(f), "call");
                    }
                    AddLines(zip, entries, SelectWall(Path.Combine(logDir, "routes.jsonl"), windows, "at", id), Path.Combine(logDir, "routes.jsonl"), "agents/" + name + "/routes.jsonl", "questions");
                    // the system prompts used: by their fingerprints (0.13), else every one written before the run ended
                    DateTime end = windows.Count > 0 ? windows.Max(w => w.To) : DateTime.MaxValue;
                    if (Directory.Exists(logDir))
                        foreach (string f in Directory.GetFiles(logDir, "system-*.md").Concat(Directory.GetFiles(logDir, "chronicler-*.md")))
                        {
                            string h = Path.GetFileNameWithoutExtension(f).Substring(Path.GetFileNameWithoutExtension(f).IndexOf('-') + 1);
                            if (hashes.Count > 0 ? hashes.Contains(h) || f.Contains("chronicler-") : File.GetLastWriteTime(f) <= end)
                                AddFile(zip, entries, f, "agents/" + name + "/prompts/" + Path.GetFileName(f), "prompt");
                        }
                    if (workspace != null)
                    {
                        string mem = Path.Combine(workspace, "memory", Safe(campaign), "snapshots.jsonl");
                        AddLines(zip, entries, SelectWall(mem, windows, "at", id), mem, "agents/" + name + "/memory-snapshots.jsonl", "memory");
                        foreach (string f in new[] { "agent.txt", "warroom.txt", "warroom-system.md", "chronicler-system.md" })
                            AddFile(zip, entries, Path.Combine(workspace, f), "agents/" + name + "/folder/" + f, "folder");
                        string channel = Path.Combine(workspace, "warroom");
                        string library = Path.Combine(channel, "library");
                        if (Directory.Exists(library))
                            foreach (string f in Directory.GetFiles(library, "*", SearchOption.AllDirectories))
                                AddFile(zip, entries, f, "agents/" + name + "/library/" + Path.GetRelativePath(library, f).Replace('\\', '/'), "library");
                        AddFile(zip, entries, Path.Combine(channel, "tools.txt"), "agents/" + name + "/tools.txt", "library");
                    }
                    _ = mode;
                }

                // the check: every answer's call is there, every poke the index names, every receipt's answer
                foreach (JsonObject e in episodes)
                {
                    string type = Str(e, "type");
                    if (type == "answer" && Str(e, "call") is string folder && !Directory.Exists(folder)) missing.Add("the call folder of " + Str(e, "realm") + "'s answer to poke #" + Str(e, "poke"));
                    if (type == "poke" && Str(e, "to_agent") == "true" && !pokesKept.Contains(Str(e, "realm") + "#" + Str(e, "poke"))) missing.Add(Str(e, "realm") + "'s poke #" + Str(e, "poke") + " (named in the episode index, not in its pokes file)");
                }
                var answered = new HashSet<string>(episodes.Where(e => Str(e, "type") == "answer").Select(e => Str(e, "realm") + "#" + Str(e, "file")));
                foreach (JsonObject e in episodes.Where(e => Str(e, "type") == "receipt" && Str(e, "file") is string f && f != "console" && f != "then" && !f.StartsWith("chat-")))
                    if (!answered.Contains(Str(e, "realm") + "#" + Str(e, "file"))) { missing.Add("the answer file " + Str(e, "file") + " behind a receipt of " + Str(e, "realm")); break; }
                foreach (JsonObject c in calls.Where(c => (Str(c, "kind") == "answer" || Str(c, "kind") == "council") && Str(c, "ok") == "true" && Str(c, "written") == "true" && Str(c, "realm") != null))
                    if (!pokesKept.Contains(Str(c, "realm") + "#" + Str(c, "poke"))) { missing.Add("the poke #" + Str(c, "poke") + " a call of " + Str(c, "realm") + " answered"); }

                // the manifest and the check go in too
                var manifest = new JsonObject
                {
                    ["run"] = r.Id, ["arm"] = r.Arm, ["campaign"] = campaign, ["made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv), ["status"] = r.Status,
                    ["windows"] = new JsonArray(windows.Select(w => (JsonNode)new JsonObject { ["from"] = w.From.ToString("yyyy-MM-dd HH:mm:ss", Inv), ["to"] = w.To.ToString("yyyy-MM-dd HH:mm:ss", Inv) }).ToArray()),
                    ["files"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject
                    {
                        ["name"] = e.Name, ["source"] = e.Source, ["kind"] = e.Kind, ["lines"] = e.Lines, ["bytes"] = e.Bytes, ["sha256"] = e.Sha, ["first"] = e.First, ["last"] = e.Last
                    }).ToArray()),
                    ["missing"] = new JsonArray(missing.Distinct().Select(m => (JsonNode)m).ToArray()),
                    ["counts"] = new JsonObject
                    {
                        ["calls"] = calls.Count, ["episodes"] = episodes.Count, ["your_records"] = decisions.Count,
                        ["episode_types"] = new JsonObject(episodes.GroupBy(e => Str(e, "type") ?? "?").Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count()))),
                        ["your_kinds"] = new JsonObject(decisions.GroupBy(d => Str(d, "kind") ?? Str(d, "type") ?? "?").Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count())))
                    }
                };
                PutText(zip, "manifest.json", manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                PutText(zip, "missing.txt", missing.Count == 0 ? "Nothing expected is missing.\n" : string.Join("\n", missing.Distinct()) + "\n");
                File.WriteAllText(Path.Combine(r.Dir, "bundle-manifest.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);
            long size = new FileInfo(zipPath).Length, raw = entries.Sum(e => e.Bytes);
            Console.WriteLine("Bundle of run " + r.Id + " (" + r.Arm + "): " + entries.Count + " files, " + Text_.Mb(raw) + " raw, " + Text_.Mb(size) + " packed: " + zipPath);
            Console.WriteLine("  " + calls.Count + " calls, " + episodes.Count + " episode lines, " + decisions.Count + " of your records; " + entries.Count(e => e.Kind == "call") + " files of call folders.");
            List<string> gone = missing.Distinct().ToList();
            if (gone.Count == 0) Console.WriteLine("  Nothing expected is missing.");
            else
            {
                Console.WriteLine("  MISSING (" + gone.Count + "):");
                foreach (string m in gone.Take(20)) Console.WriteLine("  ! " + m);
                if (gone.Count > 20) Console.WriteLine("  ! ... and " + (gone.Count - 20) + " more (missing.txt in the bundle)");
            }
            return gone.Count == 0 ? 0 : 1;
        }

        /// <summary>
        /// The lines of a campaign file that belong to the run: those tagged with its id, when any line of the file carries a run tag
        /// at all; else those its loads wrote (from loads.jsonl's offset at each of the run's loads to the next load's) within the
        /// run's hours; else, for a file loads.jsonl does not know, those within its hours (a line's "hours").
        /// </summary>
        internal static List<string> Select(string path, RunData r, List<(string Load, double From, double To)> segments, List<JsonObject> loads, List<(DateTime From, DateTime To)> windows, string offsetKey)
        {
            var all = Raw(path);
            if (all.Count == 0) return all;
            bool tagged = all.Any(l => l.Contains("\"run\":"));
            if (tagged)
            {
                string tag = "\"run\":\"" + r.Id + "\"";
                return all.Where(l => l.Contains(tag)).ToList();
            }
            if (offsetKey != null && segments.Count > 0 && loads.Count > 0)
            {
                var ranges = new List<(long From, long To, double H0, double H1)>();
                foreach (var seg in segments)
                {
                    int i = loads.FindIndex(x => Str(x, "load_id") == seg.Load);
                    if (i < 0) continue;
                    long from = Lng(loads[i]["offsets"], offsetKey);
                    long to = i + 1 < loads.Count ? Lng(loads[i + 1]["offsets"], offsetKey) : long.MaxValue;
                    ranges.Add((from, to, seg.From, seg.To));
                }
                if (ranges.Count > 0) return ByOffsets(path, ranges);
            }
            double h0 = segments.Count > 0 ? segments.Min(s => s.From) : double.MinValue, h1 = segments.Count > 0 ? segments.Max(s => s.To) : double.MaxValue;
            return all.Where(l => { JsonObject o = Parse(l); double h = o != null ? Dbl(o, "hours") : 0; return o != null && h >= h0 - 1e-6 && h <= h1 + 1; }).ToList();
        }

        /// <summary>The lines within byte ranges, and within each range's hours.</summary>
        private static List<string> ByOffsets(string path, List<(long From, long To, double H0, double H1)> ranges)
        {
            var list = new List<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            long pos = 0;
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                long at = pos;
                pos += Encoding.UTF8.GetByteCount(line) + 1;
                if (line.Length == 0) continue;
                foreach (var rg in ranges)
                {
                    if (at < rg.From || at >= rg.To) continue;
                    JsonObject o = Parse(line);
                    double h = o != null && o["hours"] != null ? Dbl(o, "hours") : double.NaN;
                    if (double.IsNaN(h) || (h >= rg.H0 - 1e-6 && h <= rg.H1 + 1)) { list.Add(line); break; }
                }
            }
            return list;
        }

        /// <summary>The lines within the run's wall-clock windows (by the line's own time field), or tagged with its id.</summary>
        internal static List<string> SelectWall(string path, List<(DateTime From, DateTime To)> windows, string field, string runId = null)
        {
            var all = Raw(path);
            if (runId != null && all.Any(l => l.Contains("\"run\":\"" + runId + "\""))) return all.Where(l => l.Contains("\"run\":\"" + runId + "\"")).ToList();
            return all.Where(l =>
            {
                JsonObject o = Parse(l);
                DateTime t = o != null ? Wall(Str(o, field)) : DateTime.MinValue;
                return t != DateTime.MinValue && windows.Any(w => t >= w.From && t <= w.To);
            }).ToList();
        }

        private static List<string> Raw(string path)
        {
            if (!File.Exists(path)) return new List<string>();
            return ReadShared(path).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
        }

        internal static JsonObject Parse(string line)
        {
            try { return JsonNode.Parse(line) as JsonObject; }
            catch (Exception) { return null; }
        }

        private static void AddLines(ZipArchive zip, List<Entry> entries, List<string> lines, string source, string name, string kind)
        {
            if (lines.Count == 0) return;
            string text = string.Join("\n", lines) + "\n";
            PutText(zip, name, text);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            JsonObject first = Parse(lines[0]), last = Parse(lines[lines.Count - 1]);
            entries.Add(new Entry
            {
                Name = name, Source = source, Kind = kind, Lines = lines.Count, Bytes = bytes.Length, Sha = Sha(bytes),
                First = first != null ? (Str(first, "hours") ?? Str(first, "at") ?? Str(first, "wall")) : null, Last = last != null ? (Str(last, "hours") ?? Str(last, "at") ?? Str(last, "wall")) : null
            });
        }

        private static void AddFile(ZipArchive zip, List<Entry> entries, string path, string name, string kind)
        {
            if (!File.Exists(path)) return;
            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream()) { fs.CopyTo(ms); bytes = ms.ToArray(); }
            ZipArchiveEntry z = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = z.Open()) s.Write(bytes, 0, bytes.Length);
            int lines = name.EndsWith(".jsonl", StringComparison.Ordinal) ? bytes.Count(b => b == (byte)'\n') : 0;
            entries.Add(new Entry { Name = name, Source = path, Kind = kind, Lines = lines, Bytes = bytes.Length, Sha = Sha(bytes), Last = File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss", Inv) });
        }

        private static void PutText(ZipArchive zip, string name, string text)
        {
            ZipArchiveEntry z = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(z.Open(), new UTF8Encoding(false));
            w.Write(text);
        }

        private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 16).ToLowerInvariant();

        /// <summary>The campaign's folder name, as the bridge makes it (Paths.Safe).</summary>
        public static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }

    /// <summary>Sizes in words for the console.</summary>
    internal static class Text_
    {
        public static string Mb(long bytes) => (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " MB";
    }

    /// <summary>
    /// The marks on your old records (0.13, docs/PLAN-0.13.md C2): before 0.13 the recorder kept going in the runs an agent commanded
    /// your kingdom, crediting you with your realm's own proposals and Party AI Controls' refills there. Each record made inside such
    /// a run's wall-clock windows is marked "not yours" in record\marks.jsonl beside decisions.jsonl; nothing is deleted.
    /// </summary>
    internal static class Marks
    {
        public static int Run(string campaign)
        {
            string data = Path.Combine(Root, "data");
            List<string> campaigns = campaign != null ? new List<string> { Bundle.Safe(campaign) } : Directory.Exists(data) ? Directory.GetDirectories(data).Select(Path.GetFileName).ToList() : new List<string>();
            // the runs whose kingdom someone other than you commanded, with their windows
            var agentRuns = new List<(string Id, string Arm, string Campaign, List<(DateTime From, DateTime To)> Windows)>();
            foreach (string id in All())
            {
                RunData r = Load(id, calls: false);
                string who = Str(r.State?["who"], r.Kingdom ?? "");
                if (who == "you" || r.Arm == "A" || r.Arm == "B") continue;
                agentRuns.Add((r.Id, r.Arm, Bundle.Safe(Str(r.State, "campaign")), r.Windows));
            }
            int marked = 0, total = 0;
            foreach (string c in campaigns)
            {
                string path = Path.Combine(data, c, "record", "decisions.jsonl");
                if (!File.Exists(path)) continue;
                var marks = new List<string>();
                int line = 0;
                foreach (string l in ReadShared(path).Split('\n'))
                {
                    if (l.Trim().Length == 0) continue;
                    line++;
                    total++;
                    JsonObject o;
                    try { o = JsonNode.Parse(l) as JsonObject; } catch (Exception) { continue; }
                    if (o == null) continue;
                    if (Str(o, "run") != null) continue;   // tagged since 0.13: the recorder itself keeps to your own runs
                    DateTime t = Wall(Str(o, "wall"));
                    var hit = agentRuns.FirstOrDefault(r => r.Campaign == c && r.Windows.Any(w => t >= w.From && t <= w.To));
                    if (hit.Id == null) continue;
                    marked++;
                    marks.Add(new JsonObject
                    {
                        ["line"] = line, ["wall"] = Str(o, "wall"), ["hours"] = o["hours"]?.DeepClone(), ["kind"] = Str(o, "kind"), ["text"] = Str(o, "text"), ["run"] = hit.Id, ["arm"] = hit.Arm,
                        ["mark"] = "not yours", ["why"] = "made while an agent or the game's AI commanded your kingdom in run " + hit.Id + " (" + hit.Arm + "): the recorder before 0.13 credited you with it"
                    }.ToJsonString());
                }
                string outPath = Path.Combine(data, c, "record", "marks.jsonl");
                File.WriteAllText(outPath, marks.Count > 0 ? string.Join("\n", marks) + "\n" : "", new UTF8Encoding(false));
                Console.WriteLine(c + ": " + marks.Count + " of " + line + " records marked not yours (" + outPath + ")");
            }
            Console.WriteLine("In all " + marked + " of " + total + " records marked.");
            return 0;
        }
    }
}
