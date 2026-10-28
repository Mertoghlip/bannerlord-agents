using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents.Tools
{
    /// <summary>One kept tool of a realm's library (0.13, docs/PLAN-0.13.md A6): its newest version, the versions before it, and its record.</summary>
    internal sealed class KeptTool
    {
        public string Name, About, Hash, File, Written, WrittenIn, Run, Arm, RetiredWall;
        public int Version, Poke = -1;
        public double Hours = double.NaN;
        public bool Retired;
        public List<KeptVersion> Before = new List<KeptVersion>();
        public ToolRecord Record = new ToolRecord();
    }

    internal sealed class KeptVersion
    {
        public int Version, Poke = -1;
        public string Hash, File, Written;
    }

    /// <summary>A kept tool's record over its runs (A7), all its versions together: how they ended, call-backs, what changed for its lords.</summary>
    internal sealed class ToolRecord
    {
        public int Runs, CallBacks, Fought, Won, LordsTaken, LordsLost, HoldingsGained, HoldingsLost;
        public double Hours;
        public long Men, Gold, Strength;
        public Dictionary<string, int> Ended = new Dictionary<string, int>();
        public Dictionary<string, int> ByVersion = new Dictionary<string, int>();
        /// <summary>The test runs it was used in (the comparisons tell what the kept tools added, A6).</summary>
        public List<string> OnRuns = new List<string>();
    }

    internal sealed class LibraryIndex
    {
        public string Realm;
        public List<KeptTool> Tools = new List<KeptTool>();
    }

    /// <summary>
    /// A realm's own tools (0.13, docs/PLAN-0.13.md A1, A6 to A8): the tools its agent wrote and kept, kept in the channel folder's
    /// library\ (index.json, and one file per version, &lt;name&gt;.v&lt;N&gt;.json), so they carry over between loads, campaigns and test runs.
    /// An answer's "tools" keeps, changes (a new version), retires or uses once (keep false) a tool; every one is checked first, and
    /// one that fails is refused with its problems. Orders already running keep the very version they started with. The player's
    /// realm has one library, shared by its own agent and the lieutenant. Each kept tool's record is summed here from its runs'
    /// outcomes. Worker thread, except Find and ByHash (the game's thread starts orders with them), under the lock.
    /// </summary>
    internal sealed class OwnLibrary
    {
        /// <summary>The most tools a realm may keep (A5).</summary>
        public const int MaxKept = 30;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, OwnLibrary> ByRealm = new Dictionary<string, OwnLibrary>(StringComparer.Ordinal);
        /// <summary>Every tool taken or read this session by its fingerprint: a next order (then) names the very tool its answer wrote.</summary>
        private static readonly Dictionary<string, ToolDef> Hashed = new Dictionary<string, ToolDef>(StringComparer.Ordinal);
        private static Dictionary<string, string> _homes = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> RecordsChanged = new HashSet<string>(StringComparer.Ordinal);

        public readonly string Realm, Dir;
        private LibraryIndex _index = new LibraryIndex();
        /// <summary>The usable kept tools by name (their newest versions), and this session's one-off tools by name.</summary>
        private readonly Dictionary<string, ToolDef> _defs = new Dictionary<string, ToolDef>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ToolDef> _oneOffs = new Dictionary<string, ToolDef>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Kept tools that no longer pass the checker (it grew stricter since they were kept), with why.</summary>
        private readonly Dictionary<string, string> _broken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private OwnLibrary(string realm, string dir)
        {
            Realm = realm;
            Dir = dir;
        }

        // ---------- which library ----------

        /// <summary>
        /// Where each realm's library lives, set at each load (game's thread): the channel folder of the realm's own agent (kind realm,
        /// switched on or not), else the lieutenant's, so the player's realm keeps one library whichever of the two plays it.
        /// </summary>
        public static void SetHomes(Dictionary<string, string> homes)
        {
            lock (Gate) _homes = new Dictionary<string, string>(homes ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        }

        /// <summary>A load (worker): every library is read again at its first use.</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                ByRealm.Clear();
                Hashed.Clear();
                RecordsChanged.Clear();
            }
        }

        /// <summary>The realm's library (read from its folder at first use), or null for a realm no agent serves.</summary>
        public static OwnLibrary For(string realm)
        {
            if (realm == null) return null;
            lock (Gate)
            {
                if (ByRealm.TryGetValue(realm, out OwnLibrary lib)) return lib;
                string channel = _homes.TryGetValue(realm, out string h) ? h : Agents.Serving(realm)?.Config.ChannelDir;
                if (channel == null) return null;
                lib = new OwnLibrary(realm, Path.Combine(channel, "library"));
                lib.Load();
                ByRealm[realm] = lib;
                return lib;
            }
        }

        /// <summary>A kept tool of the realm by name, its newest version, or a one-off tool of this session (then-orders); null for none.</summary>
        public static ToolDef Find(string realm, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            OwnLibrary lib = For(realm);
            if (lib == null) return null;
            lock (Gate) return lib._defs.TryGetValue(name.Trim(), out ToolDef d) ? d : lib._oneOffs.TryGetValue(name.Trim(), out ToolDef o) ? o : null;
        }

        /// <summary>The very tool taken this session with this fingerprint (a next order's), or null.</summary>
        public static ToolDef ByHash(string hash)
        {
            if (hash == null) return null;
            lock (Gate) return Hashed.TryGetValue(hash, out ToolDef d) ? d : null;
        }

        /// <summary>The names of the realm's usable kept tools.</summary>
        public static List<string> Names(string realm)
        {
            OwnLibrary lib = For(realm);
            if (lib == null) return new List<string>();
            lock (Gate) return lib._defs.Keys.OrderBy(k => k).ToList();
        }

        /// <summary>The kept tools, as they stand (the run's setup, the smoke test).</summary>
        public List<KeptTool> Kept { get { lock (Gate) return _index.Tools.Where(t => !t.Retired).ToList(); } }

        public ToolDef Get(string name) { lock (Gate) return name != null && _defs.TryGetValue(name, out ToolDef d) ? d : null; }

        // ---------- reading and writing the folder ----------

        private string IndexFile => Path.Combine(Dir, "index.json");

        private void Load()
        {
            _index = new LibraryIndex { Realm = Realm };
            _defs.Clear();
            _broken.Clear();
            try
            {
                if (!System.IO.File.Exists(IndexFile)) return;
                _index = Json.Read<LibraryIndex>(System.IO.File.ReadAllText(IndexFile)) ?? new LibraryIndex();
                _index.Realm = Realm;
                foreach (KeptTool k in _index.Tools.Where(t => !t.Retired))
                {
                    string path = Path.Combine(Dir, k.File ?? "");
                    if (!System.IO.File.Exists(path)) { _broken[k.Name] = "its file " + k.File + " is missing"; continue; }
                    ToolDef d = ToolDef.FromJson(System.IO.File.ReadAllText(path), k.Name, Realm);
                    d.Version = k.Version;
                    d.WrittenAt = k.Poke;
                    d.Hash = k.Hash;
                    if (!d.Valid) { _broken[k.Name] = string.Join("; ", d.Problems.Take(3)); continue; }
                    _defs[k.Name] = d;
                    if (k.Hash != null) Hashed[k.Hash] = d;
                }
                Diag.Event("tool", "library read", new { realm = Realm, dir = Dir, kept = _defs.Count, broken = _broken.Keys.ToList() });
            }
            catch (Exception e)
            {
                Diag.Error("reading " + Realm + "'s own tools (" + IndexFile + ")", e);
                Diag.Alarm("library_unreadable", "The library of " + Realm + "'s own tools could not be read (" + e.Message + "); its agent's kept tools are not offered this session.");
            }
        }

        private void Save()
        {
            Paths.Ensure(Dir);
            Files.Overwrite(IndexFile, Json.Line(_index));
        }

        // ---------- an answer's tools ----------

        /// <summary>What became of one tool of an answer: kept, changed, unchanged, one-off, retired or refused, and why; its definition when usable.</summary>
        internal sealed class Taken
        {
            public string Name, Status, Reason, Hash;
            public int Version;
            public ToolDef Def;
            public bool Usable => Def != null && Status != "refused" && Status != "retired";
        }

        private static Taken No(string name, string reason) => new Taken { Name = name, Status = "refused", Reason = reason };

        /// <summary>
        /// One tool of an answer (A8): {"name", "keep", "about", "definition"} (or the definition's parts at the top), or {"name", "retire": true}.
        /// Checked first; a tool that fails is refused with its problems. shadow: checked only, the library untouched.
        /// </summary>
        public Taken Take(JObject e, int poke, string kind, string file, double hours, bool shadow)
        {
            if (e == null) return No(null, "a tool is written as an object: {\"name\": ..., \"keep\": true, \"about\": ..., \"definition\": {...}}");
            string name = ((string)e["name"] ?? (string)e["tool"] ?? (string)(e["definition"] as JObject)?["name"] ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0) return No(null, "a tool needs a name, so your orders can name it");
            if ((bool?)e["retire"] == true) return Retire(name, poke, hours, shadow);
            JObject def = e["definition"] is JObject given ? (JObject)given.DeepClone() : (JObject)e.DeepClone();
            foreach (string k in new[] { "keep", "retire", "definition", "tool" }) def.Remove(k);
            def["name"] = name;
            if (def["about"] == null && e["about"] != null) def["about"] = e["about"].DeepClone();
            bool keep = (bool?)e["keep"] ?? false;
            string json = def.ToString(Formatting.Indented);
            string hash = RecordFiles.Hash(def.ToString(Formatting.None));
            ToolDef d = ToolDef.FromJson(json, name, Realm);
            d.WrittenAt = poke;
            d.Hash = hash;
            Book("tool_written", new { Poke = poke, Call = kind, File = file, Name = name, Keep = keep, Hash = hash, Definition = def.ToString(Formatting.None) }, hours);
            Book("tool_check", new { Poke = poke, Name = name, Hash = hash, d.Valid, Problems = d.Problems.ToList() }, hours);
            if (!d.Valid)
            {
                Diag.Count("tool:own refused");
                return Result("refused", name, 0, hash, null, "the checker found " + d.Problems.Count + " problem" + (d.Problems.Count == 1 ? "" : "s") + ": "
                              + string.Join("; ", d.Problems.Take(6)) + (d.Problems.Count > 6 ? "; and " + (d.Problems.Count - 6) + " more" : "") + ". Nothing was kept, and its orders are refused", poke, hours);
            }
            if (!keep)
            {
                d.OneOff = true;
                if (shadow) return Result("checked", name, 0, hash, d, "valid; it would be used once for this answer's orders (shadow mode: checked only)", poke, hours);
                lock (Gate) { _oneOffs[name] = d; Hashed[hash] = d; }
                Diag.Count("tool:own one-off");
                return Result("one_off", name, 0, hash, d, "used once, for this answer's orders; not kept (with \"keep\": true it goes into your library)", poke, hours);
            }
            KeptTool kept;
            lock (Gate) kept = _index.Tools.FirstOrDefault(t => t.Name == name);
            if (kept != null && !kept.Retired && kept.Hash == hash && Get(name) != null)
                return Result("unchanged", name, kept.Version, hash, Get(name), name + " v" + kept.Version + " is already in your library as written; nothing changed", poke, hours);
            int live = Kept.Count;
            if ((kept == null || kept.Retired) && live >= MaxKept)
                return Result("refused", name, 0, hash, null, "your library already holds " + live + " tools, the most it may keep; retire one first ({\"name\": \"...\", \"retire\": true})", poke, hours);
            int version = kept == null ? 1 : kept.Version + 1;
            d.Version = version;
            if (shadow) return Result("checked", name, version, hash, d, "valid; it would be kept as " + name + " v" + version + " (shadow mode: checked only, your library is unchanged)", poke, hours);
            string vfile = name + ".v" + version + ".json";
            bool was = kept != null && !kept.Retired, back = kept != null && kept.Retired;
            lock (Gate)
            {
                Paths.Ensure(Dir);
                Files.Overwrite(Path.Combine(Dir, vfile), json);
                if (kept == null) { kept = new KeptTool { Name = name }; _index.Tools.Add(kept); }
                else if (kept.File != null) kept.Before.Add(new KeptVersion { Version = kept.Version, Hash = kept.Hash, File = kept.File, Written = kept.Written, Poke = kept.Poke });
                kept.Version = version;
                kept.Hash = hash;
                kept.File = vfile;
                kept.About = d.About;
                kept.Poke = poke;
                kept.Hours = hours;
                kept.Written = Wall();
                kept.WrittenIn = kind;
                kept.Run = RunTags.Id;
                kept.Arm = RunTags.Arm;
                kept.Retired = false;
                kept.RetiredWall = null;
                _defs[name] = d;
                _broken.Remove(name);
                _oneOffs.Remove(name);
                Hashed[hash] = d;
                Save();
            }
            Diag.Count(was ? "tool:own changed" : "tool:own kept");
            string status = was ? "changed" : "kept";
            string reason = was ? name + " v" + version + " replaces v" + (version - 1) + " in your library; orders already running keep the version they started with, new orders get v" + version
                          : back ? name + " is back in your library, as v" + version
                          : name + " v" + version + " is kept in your library: order it by its name from now on, in this run and the next";
            return Result(status, name, version, hash, d, reason, poke, hours);
        }

        private Taken Retire(string name, int poke, double hours, bool shadow)
        {
            KeptTool k;
            lock (Gate) k = _index.Tools.FirstOrDefault(t => t.Name == name && !t.Retired);
            if (k == null) return Result("refused", name, 0, null, null, "no tool of yours is called " + name + (Library.Get(name) != null ? " (it is one of ours, which stay)" : ""), poke, hours);
            if (shadow) return Result("checked", name, k.Version, k.Hash, null, name + " v" + k.Version + " would leave your library (shadow mode: checked only)", poke, hours);
            lock (Gate)
            {
                k.Retired = true;
                k.RetiredWall = Wall();
                _defs.Remove(name);
                Save();
            }
            Diag.Count("tool:own retired");
            return Result("retired", name, k.Version, k.Hash, null, name + " v" + k.Version + " has left your library; orders running it finish as they are", poke, hours);
        }

        private Taken Result(string status, string name, int version, string hash, ToolDef d, string reason, int poke, double hours)
        {
            Book("tool_" + status, new { Poke = poke, Name = name, Version = version > 0 ? version : (int?)null, Hash = hash, Reason = reason }, hours);
            return new Taken { Name = name, Status = status, Version = version, Hash = hash, Def = d, Reason = reason };
        }

        private static readonly JsonSerializer Snake = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver { NamingStrategy = new Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore
        });

        /// <summary>A line of the toolbook (C3.4): every tool written, its check, and what became of it, with the realm and the run.</summary>
        private void Book(string type, object what, double hours)
        {
            JObject line = JObject.FromObject(what, Snake);
            line.AddFirst(new JProperty("hours", hours));
            line.AddFirst(new JProperty("wall", Wall()));
            if (RunTags.Arm != null) line.AddFirst(new JProperty("arm", RunTags.Arm));
            if (RunTags.Id != null) line.AddFirst(new JProperty("run", RunTags.Id));
            line.AddFirst(new JProperty("realm", Realm));
            line.AddFirst(new JProperty("type", type));
            Outcomes.Note(line);
        }

        private static string Wall() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", Text.Inv);

        // ---------- the record (A7) ----------

        /// <summary>A run of a kept tool ended (Outcomes, worker): its record gains it; tools.txt shows it from the next day's refresh.</summary>
        public static void Recorded(string realm, string tool, string version, string how, double hoursHeld, int callBacks, LordNumbers start, LordNumbers end,
                                    int fought, int won, int lordsTaken, int lordsLost, int gained, int lost)
        {
            OwnLibrary lib = For(realm);
            if (lib == null) return;
            lock (Gate)
            {
                KeptTool k = lib._index.Tools.FirstOrDefault(t => t.Name == tool);
                if (k == null) return;   // a one-off tool: its record is the toolbook's alone
                ToolRecord r = k.Record;
                r.Runs++;
                r.Ended[how ?? "ended"] = (r.Ended.TryGetValue(how ?? "ended", out int n) ? n : 0) + 1;
                r.ByVersion[version ?? "?"] = (r.ByVersion.TryGetValue(version ?? "?", out int v) ? v : 0) + 1;
                r.Hours += Math.Max(0, hoursHeld);
                r.CallBacks += callBacks;
                if (start != null && end != null)
                {
                    r.Men += end.Men - start.Men;
                    if (end.Gold >= 0 && start.Gold >= 0) r.Gold += end.Gold - start.Gold;
                    r.Strength += (long)Math.Round(end.Strength - start.Strength);
                }
                r.Fought += fought;
                r.Won += won;
                r.LordsTaken += lordsTaken;
                r.LordsLost += lordsLost;
                r.HoldingsGained += gained;
                r.HoldingsLost += lost;
                if (RunTags.Id != null && !r.OnRuns.Contains(RunTags.Id)) r.OnRuns.Add(RunTags.Id);
                lib.Save();
                RecordsChanged.Add(realm);
            }
        }

        /// <summary>Realms whose kept tools' records changed since tools.txt was last written (the day's refresh takes them).</summary>
        public static List<string> TakeChanged()
        {
            lock (Gate)
            {
                List<string> all = RecordsChanged.ToList();
                RecordsChanged.Clear();
                return all;
            }
        }

        // ---------- what the agent reads ----------

        /// <summary>The realm's section of tools.txt: YOUR OWN TOOLS, each kept tool with what it takes, its settings and its record.</summary>
        public static string TextFor(string realm)
        {
            OwnLibrary lib = For(realm);
            var sb = new StringBuilder();
            sb.AppendLine("YOUR OWN TOOLS (your realm's library: the tools you wrote and kept; order each by its name, as ours; each one's whole definition is in tools\\<name>.json)");
            List<KeptTool> kept = lib?.Kept ?? new List<KeptTool>();
            if (kept.Count == 0)
            {
                sb.AppendLine("- None yet. You may write tools of your own and keep the ones worth using again (see \"Writing your own tools\" in your instructions); at most " + MaxKept + ".");
                return sb.ToString();
            }
            foreach (KeptTool k in kept.OrderBy(t => t.Name))
            {
                ToolDef d = lib.Get(k.Name);
                string broken;
                lock (Gate) broken = lib._broken.TryGetValue(k.Name, out string b) ? b : null;
                if (d == null)
                {
                    sb.AppendLine("- **" + k.Name + "** (yours, v" + k.Version + "): NOT USABLE now: " + (broken ?? "it could not be read") + ". Write it again to fix it, or retire it.");
                    continue;
                }
                sb.AppendLine(d.Describe() + " " + RecordText(k));
            }
            sb.AppendLine("(" + kept.Count + " of the " + MaxKept + " your library may keep.)");
            return sb.ToString();
        }

        /// <summary>A kept tool's record in words: how often it was used, how its runs ended, and what changed for its lords over them.</summary>
        public static string RecordText(KeptTool k)
        {
            ToolRecord r = k.Record;
            string kept = "Kept at poke #" + k.Poke + (k.WrittenIn != null ? " (" + (k.WrittenIn == "council" ? "a council" : "an answer") + ")" : "") + ".";
            if (r.Runs == 0) return "Its record: not used yet. " + kept;
            string ends = string.Join(", ", r.Ended.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + " " + kv.Key));
            double days = r.Hours / 24;
            string calls = days >= 0.5 ? ToolDef.Num(Math.Round(r.CallBacks / days, 1)) + " call-backs a day" : r.CallBacks + " call-back" + (r.CallBacks == 1 ? "" : "s");
            string Signed(long v) => (v > 0 ? "+" : "") + Text.N((int)Math.Max(int.MinValue, Math.Min(int.MaxValue, v)));
            return "Its record: used " + r.Runs + " time" + (r.Runs == 1 ? "" : "s") + " over " + ToolDef.Num(Math.Round(days, 1)) + " days" + (r.OnRuns.Count > 0 ? " (in " + r.OnRuns.Count + " test run" + (r.OnRuns.Count == 1 ? "" : "s") + ")" : "")
                   + "; ended " + ends + "; " + calls + "; over its runs men " + Signed(r.Men) + ", gold " + Signed(r.Gold) + ", strength " + Signed(r.Strength)
                   + "; " + r.Fought + " battle" + (r.Fought == 1 ? "" : "s") + ", " + r.Won + " won; lords taken " + r.LordsTaken + ", lost " + r.LordsLost
                   + "; holdings gained " + r.HoldingsGained + ", lost " + r.HoldingsLost + ". " + kept;
        }

        /// <summary>The kept tools' whole definitions, as written, for the channel's tools\ folder beside ours (name, its file's text).</summary>
        public static Dictionary<string, string> Definitions(string realm)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            OwnLibrary lib = For(realm);
            if (lib == null) return d;
            lock (Gate)
                foreach (KeyValuePair<string, ToolDef> kv in lib._defs)
                    if (kv.Value.Json != null) d[kv.Key] = kv.Value.Json;
            return d;
        }

        /// <summary>Each realm's kept tools as a run begins (A6: names, versions, fingerprints), for the run's setup.</summary>
        public static Dictionary<string, List<object>> Setup(IEnumerable<string> realms)
        {
            var all = new Dictionary<string, List<object>>();
            foreach (string realm in realms.Where(r => r != null).Distinct())
            {
                OwnLibrary lib = For(realm);
                if (lib == null) continue;
                all[realm] = lib.Kept.OrderBy(k => k.Name).Select(k => (object)new { k.Name, k.Version, k.Hash, Usable = lib.Get(k.Name) != null, Runs = k.Record.Runs }).ToList();
            }
            return all;
        }
    }
}

namespace BannerlordAgents.Tools
{
    /// <summary>
    /// The bridge's half of the war room's "check a tool" (0.13, docs/PLAN-0.13.md A4): its request (the definition, the lord, and what the
    /// order would take: at, target, places, lords, settings, as ids or names), checked and tried on the realm's newest hour (Runner.Try).
    /// The war room checks the definition itself; this adds what only the game's newest hour can say. Each try goes to the toolbook. Worker.
    /// </summary>
    internal static class ToolTry
    {
        public static string Answer(RealmAgent agent, string request)
        {
            JObject q = JObject.Parse(request);
            string realm = agent?.Realm;
            Snapshot s = agent?.Latest;
            if (s?.Watched == null) return "THE TRY: your realm has no hour yet in this game session (the game is loading, or has just loaded); only the checker's result stands.";
            string json = q["definition"] is JObject jo ? jo.ToString(Formatting.Indented) : (string)q["definition"];
            string name = ((string)q["name"] ?? (string)(q["definition"] as JObject)?["name"] ?? "tool").Trim().ToLowerInvariant();
            ToolDef def = ToolDef.FromJson(json ?? "", name, realm ?? "agent");
            if (!def.Valid) return "THE TRY was not made: the checker found " + def.Problems.Count + " problem" + (def.Problems.Count == 1 ? "" : "s") + " (" + string.Join("; ", def.Problems.Take(4)) + ").";
            string lordQ = (string)q["lord"];
            PartyRow lord = Lord(s, realm, lordQ);
            if (lord == null) return "THE TRY: your realm has no lord leading a party called '" + lordQ + "' in its newest hour (give his id in [brackets] from your report).";
            var o = new ToolOrder { Tool = def.Name, Def = def, Lord = lord.Leader ?? lord.Id, LordName = lord.Name, Party = lord.Id, Realm = realm, Source = "try" };
            var notes = new List<string>();
            string at = (string)q["at"];
            if (at != null)
            {
                SettlementRow h = Holding(s, at);
                if (h == null) notes.Add("at: no holding called '" + at + "' is known");
                else { o.At = h.Id; Kind(s, realm, def.At, h, "at", notes); }
            }
            string target = (string)q["target"];
            if (target != null)
            {
                PartyRow p = Party(s, target);
                if (p == null) notes.Add("target: no party called '" + target + "' is in your realm's view");
                else o.Target = p.Id;
            }
            foreach (string pl in Strings(q["places"]))
            {
                SettlementRow h = Holding(s, pl);
                if (h == null) { notes.Add("places: no holding called '" + pl + "' is known"); continue; }
                o.Places.Add(h.Id);
                Kind(s, realm, def.Places, h, "places", notes);
            }
            foreach (string l in Strings(q["lords"]))
            {
                PartyRow p = Lord(s, realm, l);
                if (p == null) notes.Add("lords: your realm has no lord called '" + l + "'");
                else o.Lords.Add(p.Id);
            }
            if (q["settings"] is JObject set)
                foreach (JProperty p in set.Properties())
                    if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float) o.Settings[p.Name] = (double)p.Value;
            if (def.At != null && o.At == null) notes.Add("it takes at (" + ToolDef.Plain(def.At) + "), and none was given, so at is none in the try");
            if (def.Places != null && o.Places.Count == 0) notes.Add("it takes places, and none were given, so the list is empty in the try");
            string text = Runner.Try(def, o, s) + (notes.Count > 0 ? "\nNotes on the order as given: " + string.Join("; ", notes) + "." : "");
            Outcomes.Note(new { Type = "tool_try", Realm = realm, Run = RunTags.Id, Arm = RunTags.Arm, Hours = s.Hours, Name = def.Name, Hash = RecordFiles.Hash(json), Lord = o.Lord, o.At, Places = o.Places.ToList(), Text = text });
            return text;
        }

        private static IEnumerable<string> Strings(JToken t)
        {
            if (t is JArray a) return a.Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim());
            if (t != null && t.Type == JTokenType.String) return ((string)t).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
            return Enumerable.Empty<string>();
        }

        private static string Id(string q) => (q ?? "").Trim().Trim('[', ']').Trim();

        private static PartyRow Lord(Snapshot s, string realm, string q)
        {
            string id = Id(q);
            if (id.Length == 0) return null;
            List<PartyRow> mine = s.Parties.Where(p => p.Kind == "lord" && p.Faction == realm).ToList();
            return mine.FirstOrDefault(p => string.Equals(p.Leader, id, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                   ?? mine.FirstOrDefault(p => Text.Plain(p.Name) == Text.Plain(id));
        }

        private static PartyRow Party(Snapshot s, string q)
        {
            string id = Id(q);
            return s.Parties.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Leader, id, StringComparison.OrdinalIgnoreCase))
                   ?? s.Parties.FirstOrDefault(p => Text.Plain(p.Name) == Text.Plain(id));
        }

        private static SettlementRow Holding(Snapshot s, string q)
        {
            string id = Id(q);
            return s.SettlementById.TryGetValue(id, out SettlementRow h) ? h : s.Settlements.FirstOrDefault(x => Text.Plain(x.Name) == Text.Plain(id));
        }

        /// <summary>Is the holding of the kind the tool takes, by the realm's newest hour? Said in the notes when not (the order itself is checked by the game).</summary>
        private static void Kind(Snapshot s, string realm, string kind, SettlementRow h, string what, List<string> notes)
        {
            if (kind == null) return;
            bool own = h.Faction == realm;
            bool ally = !own && s.Factions.TryGetValue(realm ?? "", out FactionRow fr) && fr.Allies.Contains(h.Faction ?? "");
            bool enemy = s.HostileToWatched != null && s.HostileToWatched.Contains(h.Faction ?? "");
            bool fort = h.Kind == "town" || h.Kind == "castle";
            bool ok;
            switch (kind)
            {
                case "own_fort": ok = own && fort; break;
                case "own_holding": ok = own; break;
                case "own_town_or_village": ok = own && h.Kind != "castle"; break;
                case "friendly_town_or_village": ok = (own || ally) && h.Kind != "castle"; break;
                case "friendly_fort": ok = (own || ally) && fort; break;
                case "friendly_holding": ok = own || ally; break;
                case "enemy_fort": ok = enemy && fort; break;
                case "enemy_village": ok = enemy && h.Kind == "village"; break;
                case "enemy_holding": ok = enemy; break;
                default: ok = true; break;
            }
            if (!ok) notes.Add(what + ": " + h.Name + " is not " + ToolDef.Plain(kind) + ", so the order would be refused");
        }
    }
}
