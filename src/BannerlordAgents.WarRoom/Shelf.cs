using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BannerlordAgents.Routes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>One change the shelf flags: what changed, on which page, how much it weighs (3 the most), when, and its number in the shelf's order of changes.</summary>
    internal sealed class ShelfChange
    {
        public long Seq;
        public double Hours = double.NaN;
        public string Date = "", File = "", Page = "", Text = "", At = "";
        public int Weight = 1;
    }

    /// <summary>What a shelf build is made from: the game's question book of the hour, the report texts it has, the realm's view file, and what the war room keeps.</summary>
    internal sealed class ShelfInput
    {
        public JsonObject Book, View;
        /// <summary>The newest report texts there are (a wake's poke text; a review's situation report and briefing): their sections fill the watches, the clock and the like.</summary>
        public string PokeText, ReportText, CouncilText;
        public int Poke = -1;
        public string Campaign = "", LoadId = "", RealmName = "", Date = "";
        public double Hours = double.NaN;
        public List<ChronicleLine> Chronicle = new List<ChronicleLine>();
        public Dictionary<string, PageRec> AgentPages = new Dictionary<string, PageRec>();
        public string Handover = "", HandoverDate = "";
        public string ToolsText = "", OwnToolsManual = "", CheckManual = "";
        /// <summary>What woke the agent (its wake's lines), for the watches that fired.</summary>
        public string Why = "";
        /// <summary>The game's receipts of an answer a review waited for (Room.AnswerFate), kept with the order results.</summary>
        public string Fate;
        public int WakesToday, WakeBudget = 12;
        public string NextReview = "";
        /// <summary>The agent's own page writes and their refusals in its last answer, told on the shelf.</summary>
        public List<string> WriteNotes = new List<string>();
    }

    /// <summary>A page the agent (or its chronicler) wrote, with when, why and by whom.</summary>
    internal sealed class PageRec
    {
        public string Text = "", Date = "", Why = "", By = "you";
        public double Hours = double.NaN;
    }

    /// <summary>
    /// The realm's files (war room 0.20, Nov 20; docs/FILES.md): the shelf of pages an agent reads instead of a report pushed to it. Each file is a folder of the channel's
    /// files\ and each tab a small page in it, so a page can be read by its path (the Read tool) or asked for (open, find, history). The game's own pages (lords,
    /// settlements, realm, orders, and a few in plan, intelligence and chronicle) are rebuilt from the question book of the hour; the agent's own pages (plan, notes,
    /// intelligence) and the chronicler's come from the memory, which keeps them with the save they belong to. Every meaningful change is flagged three ways: on the shelf
    /// (shelf.md: what is new to the agent in each file, the weightiest first), on each folder's changes page (the last three days), and at the top of the page it is on.
    /// The war room sees every read in the call's stream, so "new to you" clears when the agent has read the page, whichever way it read it.
    /// </summary>
    internal sealed class Shelf
    {
        public const string DirName = "files";
        /// <summary>The standard pace the shelf's march hours are counted at, and the reach of a place's surroundings: three days' march at it, by a way over land; a day for the near enemy.</summary>
        public const double StandardPace = 2.0, RingHours = 72, DayHours = 24;
        public static readonly string[] Files = { "lords", "settlements", "realm", "orders", "plan", "notes", "intelligence", "chronicle" };
        public static readonly string[] AgentFiles = { "plan", "notes", "intelligence" };
        /// <summary>Pages of the agent's folders that the game (or the handover, or the chronicler) writes: the agent's answers cannot write them.</summary>
        public static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            "plan/watches", "plan/plan-check", "intelligence/sightings", "intelligence/enemy-holdings", "intelligence/human-player-record", "intelligence/chances-to-strike",
            "notes/handover", "plan/contents", "plan/changes", "notes/contents", "notes/changes", "intelligence/contents", "intelligence/changes"
        };
        private static readonly Dictionary<string, string> About = new Dictionary<string, string>
        {
            ["lords"] = "your lords and armies: where each is and is heading, strength, ranks, food, gold, and the order each holds and who gave it (kept by the game)",
            ["settlements"] = "your castles and towns: garrison, militia, food, sieges, the lords tied to each, the places around it and the enemy near it (kept by the game)",
            ["realm"] = "your realm in sum and the clock, money, captives both ways, allies, letters, votes and the enemy realms' public numbers (kept by the game)",
            ["orders"] = "every order you can give, ours and your own, with its settings and how to write your own (kept by the game)",
            ["plan"] = "your strategy, written by you; the game keeps your watches and a check of the plan against the war here too",
            ["notes"] = "what you have learned, written by you; your last handover is here too",
            ["intelligence"] = "the enemy: your read of each enemy you track (written by you), and what your realm saw (kept by the game)",
            ["chronicle"] = "the war's history as your realm saw it: the record as things happen (kept by the game), and the days written up by your chronicler"
        };

        public readonly string Dir;
        private readonly string _stateFile, _changesFile, _ringsFile;
        private ShelfState _s;
        private readonly Dictionary<string, (double len, bool byWay)> _wayCache = new Dictionary<string, (double, bool)>(StringComparer.Ordinal);
        private Dictionary<string, List<RingEntry>> _rings;
        private string _ringsMap;
        private static readonly object WriteGate = new object();

        /// <summary>The last build's page texts (path to text, without the folder's .md), for open and find.</summary>
        public Dictionary<string, string> Pages { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public double Hours => _s.Hours;
        public string Date => _s.Date ?? "";
        public double LastBuildMs { get; private set; }
        public int LastChanges { get; private set; }
        public int LastWritten { get; private set; }

        public Shelf(string channelDir)
        {
            Dir = Path.Combine(channelDir, DirName);
            Directory.CreateDirectory(Dir);
            _stateFile = Path.Combine(Dir, ".state.json");
            _changesFile = Path.Combine(Dir, ".changes.jsonl");
            _ringsFile = Path.Combine(Dir, ".rings.json");
            _s = LoadState();
        }

        // ---------- the state kept between builds ----------

        private sealed class ShelfState
        {
            public long Seq { get; set; }
            public string Campaign { get; set; } = "";
            public string LoadId { get; set; } = "";
            public string Date { get; set; } = "";
            public double Hours { get; set; } = double.NaN;
            public Dictionary<string, string> Facts { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, long> PageSeq { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
            public Dictionary<string, long> Reads { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
            public Dictionary<string, long> ChangesSeen { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
            public Dictionary<string, string> Sections { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<string> Results { get; set; } = new List<string>();
            public int ResultsPoke { get; set; } = -1;
            public Dictionary<string, string> AgentHash { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<string> FormerHoldings { get; set; } = new List<string>();
            public List<string> LastWrites { get; set; } = new List<string>();
            public int FiredPoke { get; set; } = -1;
        }

        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

        private ShelfState LoadState()
        {
            try { if (File.Exists(_stateFile)) return JsonSerializer.Deserialize<ShelfState>(File.ReadAllText(_stateFile), Opts) ?? new ShelfState(); }
            catch (JsonException) { }
            catch (IOException) { }
            return new ShelfState();
        }

        private void SaveState()
        {
            try { Memory.Write(_stateFile, JsonSerializer.Serialize(_s, Opts)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private List<ShelfChange> ReadChanges()
        {
            var list = new List<ShelfChange>();
            try
            {
                if (!File.Exists(_changesFile)) return list;
                foreach (string line in File.ReadAllLines(_changesFile))
                {
                    if (line.Trim().Length == 0) continue;
                    try { ShelfChange c = JsonSerializer.Deserialize<ShelfChange>(line, Opts); if (c != null) list.Add(c); } catch (JsonException) { }
                }
            }
            catch (IOException) { }
            return list;
        }

        // ---------- a build ----------

        private sealed class Page
        {
            public string Title = "", Desc = "", Body = "";
            public bool Agent;
            /// <summary>The page's own first line says what Desc would (a lord's, a holding's): Desc goes only into its folder's contents.</summary>
            public bool Plain;
        }

        private sealed class Fact
        {
            public string Key, Value, Page;
            public int Weight;
            public Func<string, string, string> Say;
        }

        /// <summary>
        /// Builds the shelf from one hour's book: every page, the changes since the last build (none on the first build after a load: that world is new), the pages written
        /// where their text changed, each folder's contents and changes, and the shelf itself. Returns how many changes it flagged.
        /// </summary>
        public int Build(ShelfInput x, Func<string, string, double> wayLength = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            lock (WriteGate)
            {
                bool fresh = _s.LoadId != (x.LoadId ?? "") || _s.Campaign != (x.Campaign ?? "");
                if (fresh)
                {
                    _s = new ShelfState { Campaign = x.Campaign ?? "", LoadId = x.LoadId ?? "" };
                    try { if (File.Exists(_changesFile)) File.Delete(_changesFile); } catch (IOException) { }
                }
                _s.Hours = x.Hours;
                _s.Date = x.Date ?? "";
                TakeSections(x);
                var pages = new SortedDictionary<string, Page>(StringComparer.Ordinal);
                var facts = new List<Fact>();
                var extra = new List<ShelfChange>();
                var names = new Dictionary<string, string>(StringComparer.Ordinal);   // id -> name, every id the book names
                BuildLords(x, pages, facts, names);
                BuildSettlements(x, pages, facts, names, wayLength);
                BuildRealm(x, pages, facts);
                BuildIntelligence(x, pages, facts, extra, fresh);
                BuildOrders(x, pages, facts);
                BuildAgentPages(x, pages, extra);
                BuildPlanGame(x, pages, facts, extra, names);
                BuildChronicle(x, pages, facts);

                // the changes: each fact against the last build's, and the facts gone since
                var changes = new List<ShelfChange>();
                var now = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Fact f in facts)
                {
                    now[f.Key] = f.Value;
                    if (fresh) continue;
                    _s.Facts.TryGetValue(f.Key, out string old);
                    if (old == f.Value) continue;
                    string said = f.Say(old, f.Value);
                    if (said != null) changes.Add(Change(f.Page, said, f.Weight, x));
                }
                if (!fresh)
                    foreach (KeyValuePair<string, string> kv in _s.Facts)
                    {
                        if (now.ContainsKey(kv.Key) || !kv.Key.EndsWith(":here", StringComparison.Ordinal)) continue;
                        string said = Gone(kv.Key, kv.Value);
                        if (said != null) changes.Add(Change(GonePage(kv.Key), said, kv.Key.StartsWith("holding:", StringComparison.Ordinal) || kv.Key.StartsWith("lord:", StringComparison.Ordinal) ? 3 : 2, x));
                    }
                if (!fresh) changes.AddRange(extra);
                _s.Facts = now;
                foreach (ShelfChange c in changes)
                {
                    c.Seq = ++_s.Seq;
                    _s.PageSeq[c.File + "/" + c.Page] = c.Seq;
                }
                if (changes.Count > 0)
                {
                    try { File.AppendAllLines(_changesFile, changes.Select(c => JsonSerializer.Serialize(c, Opts)), new UTF8Encoding(false)); } catch (IOException) { }
                }
                // the agent's own writes are not news to it
                foreach (ShelfChange c in changes.Where(c => c.Text.StartsWith("you wrote", StringComparison.Ordinal) || c.Text.StartsWith("you removed", StringComparison.Ordinal)))
                    _s.Reads[c.File + "/" + c.Page] = Math.Max(_s.Reads.TryGetValue(c.File + "/" + c.Page, out long r) ? r : 0, c.Seq);
                List<ShelfChange> all = Prune(ReadChanges(), x.Hours);
                Write(x, pages, all);
                LastChanges = changes.Count;
                SaveState();
            }
            LastBuildMs = sw.Elapsed.TotalMilliseconds;
            return LastChanges;
        }

        private static ShelfChange Change(string page, string text, int weight, ShelfInput x)
        {
            int slash = page.IndexOf('/');
            return new ShelfChange { File = slash > 0 ? page.Substring(0, slash) : page, Page = slash > 0 ? page.Substring(slash + 1) : "index", Text = text, Weight = weight, Hours = x.Hours, Date = x.Date ?? "", At = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
        }

        /// <summary>The changes of the last three in-game days (the changes log cut to them now and then).</summary>
        private List<ShelfChange> Prune(List<ShelfChange> all, double hours)
        {
            if (double.IsNaN(hours)) return all;
            List<ShelfChange> kept = all.Where(c => double.IsNaN(c.Hours) || hours - c.Hours <= 72 + 1e-6).ToList();
            if (kept.Count < all.Count && all.Count - kept.Count > 200)
            {
                try { File.WriteAllLines(_changesFile, kept.Select(c => JsonSerializer.Serialize(c, Opts)), new UTF8Encoding(false)); } catch (IOException) { }
            }
            return kept;
        }

        // ---------- reading: what the agent has read ----------

        /// <summary>A page read (its path under files\, with or without .md, or a whole path): its changes are no longer new to the agent. A folder's changes page clears the whole folder's.</summary>
        public void MarkRead(string page)
        {
            string p = PageOf(page);
            if (p == null) return;
            lock (WriteGate)
            {
                if (p.EndsWith("/changes", StringComparison.Ordinal)) _s.ChangesSeen[p.Substring(0, p.Length - 8)] = _s.Seq;
                else _s.Reads[p] = _s.Seq;
                SaveState();
            }
        }

        /// <summary>The page a path names (files\settlements\hiltbolt.md, settlements/hiltbolt, ...), or null when it is none of the shelf's.</summary>
        public string PageOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string p = path.Replace('\\', '/').Trim();
            string dir = Dir.Replace('\\', '/').TrimEnd('/') + "/";
            if (p.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) p = p.Substring(dir.Length);
            else
            {
                int i = p.IndexOf("/" + DirName + "/", StringComparison.OrdinalIgnoreCase);
                if (i >= 0) p = p.Substring(i + DirName.Length + 2);
                else if (p.StartsWith(DirName + "/", StringComparison.OrdinalIgnoreCase)) p = p.Substring(DirName.Length + 1);
            }
            if (p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) p = p.Substring(0, p.Length - 3);
            p = p.Trim('/').ToLowerInvariant();
            return p.Length == 0 ? null : p;
        }

        private bool NewToYou(ShelfChange c)
        {
            string page = c.File + "/" + c.Page;
            long read = Math.Max(_s.Reads.TryGetValue(page, out long r) ? r : 0, _s.ChangesSeen.TryGetValue(c.File, out long cs) ? cs : 0);
            return c.Seq > read;
        }

        /// <summary>
        /// What one call read, from its stream (raw.jsonl): each Read of a shelf page and each open question, marked read; and the counts for the call's record: reads of pages,
        /// steps that asked anything, and the pages by name.
        /// </summary>
        public (int reads, int steps, int opens, List<string> pages) TrackReads(string raw)
        {
            var read = new List<string>();
            int steps = 0, opens = 0;
            foreach (string line in (raw ?? "").Split('\n'))
            {
                if (line.IndexOf("\"tool_use\"", StringComparison.Ordinal) < 0 || line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) continue;
                try
                {
                    if (JsonNode.Parse(line) is not JsonObject o || o["message"]?["content"] is not JsonArray content) continue;
                    bool asked = false;
                    foreach (JsonNode c in content)
                    {
                        if ((string)c?["type"] != "tool_use") continue;
                        asked = true;
                        string name = (string)c["name"] ?? "";
                        if (name == "Read" && (string)c["input"]?["file_path"] is string fp) { string pg = PageOf(fp); if (pg != null && Pages.ContainsKey(pg) || pg != null && pg.EndsWith("/changes", StringComparison.Ordinal)) read.Add(pg); }
                        else if (name.EndsWith("__open", StringComparison.Ordinal)) opens++;
                    }
                    if (asked) steps++;
                }
                catch (JsonException) { }
            }
            foreach (string p in read.Distinct()) MarkRead(p);
            return (read.Count, steps, opens, read.Distinct().ToList());
        }

        // ---------- the sections of the report texts ----------

        private static readonly string[] SectionHeads = { "YOUR WATCHES", "YOUR ENEMY, THE HUMAN PLAYER", "CHANCES TO STRIKE", "THE CLOCK", "ALLIES AND JOINT PLANS" };

        private void TakeSections(ShelfInput x)
        {
            foreach (string text in new[] { x.CouncilText, x.ReportText, x.PokeText })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                foreach (KeyValuePair<string, string> b in Room.Blocks(text))
                {
                    if (b.Key == null) continue;
                    string key = Room.Key(b.Key);
                    string head = SectionHeads.FirstOrDefault(h => key.StartsWith(h, StringComparison.Ordinal));
                    if (head == null) continue;
                    string body = b.Value.TrimEnd();
                    if (head == "THE CLOCK")
                    {
                        string lag = body.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Contains("orders take effect"));
                        if (lag != null) _s.Sections["THE CLOCK"] = lag.TrimStart('-', ' ').Trim();
                        continue;
                    }
                    _s.Sections[head] = (b.Key + "\n" + body).Trim();
                }
            }
            // the receipts of the answers: the poke's WHAT CHANGED SINCE YOUR LAST ANSWER, once a poke, newest first
            if (!string.IsNullOrWhiteSpace(x.PokeText) && x.Poke > _s.ResultsPoke)
            {
                foreach (KeyValuePair<string, string> b in Room.Blocks(x.PokeText))
                    if (b.Key != null && Room.Key(b.Key).StartsWith("WHAT CHANGED SINCE YOUR LAST ANSWER", StringComparison.Ordinal) && b.Value.Trim().Length > 0)
                    {
                        _s.Results.Insert(0, "As of " + (x.Date ?? "") + " (wake #" + x.Poke + "): " + b.Key.Substring(b.Key.IndexOf('(') >= 0 ? b.Key.IndexOf('(') : 0).Trim() + "\n" + b.Value.TrimEnd());
                        break;
                    }
                _s.ResultsPoke = x.Poke;
            }
            if (!string.IsNullOrWhiteSpace(x.Fate)) _s.Results.Insert(0, "As of " + (x.Date ?? "") + ": what became of your last answer, as the game answered it\n" + x.Fate.TrimEnd());
            if (_s.Results.Count > 30) _s.Results.RemoveRange(30, _s.Results.Count - 30);
        }

        // ---------- lords ----------

        private static readonly Regex Head = new Regex(@"^- (?:ENEMY |SEEN EARLIER |TRAIL )?(.+?) \[([^\]]+)\]", RegexOptions.Compiled);
        private static readonly Regex Num = new Regex(@"(-?[\d,]+(?:\.\d+)?)", RegexOptions.Compiled);

        private static IEnumerable<JsonObject> Items(ShelfInput x, string list) => (x.Book?["lists"]?[list] as JsonArray ?? new JsonArray()).OfType<JsonObject>();
        private static string T(JsonObject item) => ((string)item?["t"] ?? "").Replace("\r", "");
        private static List<string> Ids(JsonObject item) => (item?["ids"] as JsonArray ?? new JsonArray()).Select(n => (string)n).Where(s => s != null).ToList();
        private static string BookPage(ShelfInput x, string id) => id != null && x.Book?["pages"]?[id] is JsonValue v && v.TryGetValue(out string s) ? s.Replace("\r", "").Trim() : null;

        private static double? NumAfter(string text, string word)
        {
            if (text == null) return null;
            int i = text.IndexOf(word, StringComparison.Ordinal);
            if (i < 0) return null;
            Match m = Num.Match(text, i + word.Length);
            if (!m.Success || m.Index - (i + word.Length) > 3) return null;
            return double.TryParse(m.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
        }

        private static double? NumBefore(string text, string word)
        {
            if (text == null) return null;
            int i = text.IndexOf(word, StringComparison.Ordinal);
            if (i <= 0) return null;
            int j = i - 1;
            while (j >= 0 && text[j] == ' ') j--;
            int end = j + 1;
            while (j >= 0 && (char.IsDigit(text[j]) || text[j] == ',' || text[j] == '.')) j--;
            string s = text.Substring(j + 1, end - j - 1).Replace(",", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
        }

        private static string Fmt(double d) => d.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>What a lord is doing, in one of a few words, for the changes worth flagging (moving about is not).</summary>
        internal static string StateOf(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            if (t.Contains("prisoner")) return "taken prisoner";
            if (t.Contains("shelter")) return "sheltering";
            if (t.Contains("falling back") || t.Contains("falls back")) return "falling back";
            if (t.Contains("besieging") || t.Contains("siege camp") || t.Contains("lays siege")) return "besieging";
            if (t.Contains("relieve")) return "riding to relieve a fight";
            if (t.Contains("fighting") || t.Contains("in a battle")) return "fighting";
            if (t.Contains("raiding") || t.Contains("to raid")) return "raiding";
            if (t.Contains("buying food") || t.Contains("to buy food")) return "buying food";
            return "about";
        }

        private void BuildLords(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts, Dictionary<string, string> names)
        {
            var orders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonObject it in Items(x, "orders"))
            {
                List<string> ids = Ids(it);
                if (ids.Count == 0) continue;
                string t = T(it);
                int c = t.IndexOf("]: ", StringComparison.Ordinal);
                orders[ids[0]] = c > 0 ? t.Substring(c + 3).Trim() : t.TrimStart('-', ' ');
            }
            var used = new HashSet<string>(StringComparer.Ordinal);
            var index = new StringBuilder();
            var byArea = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var noOrder = new StringBuilder();
            int armies = 0, forces = 0;
            foreach (JsonObject it in Items(x, "lords"))
            {
                string t = T(it);
                Match h = Head.Match(t);
                if (!h.Success) continue;
                string name = h.Groups[1].Value, id = h.Groups[2].Value;
                names[id] = name;
                forces++;
                string first = t.Split('\n')[0].TrimStart('-', ' ').Trim();
                bool army = first.Contains("army of");
                if (army) armies++;
                string slug = Unique(used, (army ? "army-" : "") + Slug(name), id);
                string area = (string)it["area"] ?? "elsewhere";
                string look = BookPage(x, id);
                orders.TryGetValue(id, out string order);
                string orderName = order == null ? "none" : FirstClause(order);
                string doing = first;
                string state = StateOf((order ?? "") + " " + doing);
                double? men = NumBefore(first, " men"), strength = NumAfter(first, "strength "), ranks = NumAfter(look, "ranks "), food = NumAfter(look, "food for "), gold = NumAfter(look, "gold ");
                string line = "- " + name + " [" + id + "] (" + slug + "): " + Short(first.Substring(first.IndexOf("]: ", StringComparison.Ordinal) >= 0 ? first.IndexOf("]: ", StringComparison.Ordinal) + 3 : 0), 220)
                              + (order == null ? " · no order: the game's own AI leads him" : "");
                index.AppendLine(line);
                if (!byArea.TryGetValue(area, out List<string> al)) byArea[area] = al = new List<string>();
                al.Add(line);
                if (order == null) noOrder.AppendLine("- " + name + " [" + id + "] (" + slug + "): " + Short(first.Substring(first.IndexOf("]: ", StringComparison.Ordinal) >= 0 ? first.IndexOf("]: ", StringComparison.Ordinal) + 3 : 0), 200));
                var body = new StringBuilder();
                body.AppendLine(t.TrimStart('-', ' ').Trim());
                body.AppendLine();
                body.AppendLine(order != null ? "Your order: " + order : "Order: none. The game's own AI leads him until you order him.");
                body.AppendLine("Given by: " + (order != null ? (order.Contains("Now sheltering") || order.Contains("falls back") || order.Contains("falling back") ? "you; the safety rule has him away from it for now (the order resumes once the danger has passed)" : "you") : "the game's own AI"));
                body.AppendLine("Region: " + area);
                if (look != null) body.AppendLine().AppendLine(look);
                // an army's members, each in a line, from their own pages
                foreach (string mid in army ? Ids(it).Skip(1).Distinct() : Enumerable.Empty<string>())
                {
                    string mp = BookPage(x, mid);
                    if (mp == null || !mid.StartsWith("lord", StringComparison.Ordinal) && !mid.StartsWith("Character", StringComparison.Ordinal)) continue;
                    if (body.Length < 12000) body.AppendLine().AppendLine("In or joining this army: " + mp.Split('\n')[0]);
                }
                pages["lords/" + slug] = new Page { Title = (army ? "Army of " : "") + name + " [" + id + "]", Desc = Short(first, 160), Body = body.ToString(), Plain = true };
                string pg = "lords/" + slug;
                facts.Add(new Fact { Key = "lord:" + id + ":here", Value = name + "|" + slug, Page = pg, Weight = 2, Say = (o, n) => o == null ? name + " is now among your lords in the field" : null });
                facts.Add(new Fact { Key = "lord:" + id + ":order", Value = orderName, Page = pg, Weight = 2, Say = (o, n) => o == null ? null : n == "none" ? name + ": order " + o + " ended; the game's own AI leads him" : o == "none" ? name + ": new order " + n : name + ": order " + o + " → " + n });
                facts.Add(new Fact { Key = "lord:" + id + ":state", Value = state, Page = pg, Weight = state == "taken prisoner" ? 3 : 2, Say = (o, n) => o == null ? null : n == "about" ? name + ": no longer " + o : name + ": " + n + (o != "about" ? " (was " + o + ")" : "") });
                if (strength != null) facts.Add(new Fact { Key = "lord:" + id + ":strength", Value = Math.Round(strength.Value).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = 1, Say = (o, n) => Moved(o, n, 0.10, 30) ? name + ": strength " + Fmt(D(o)) + " → " + Fmt(D(n)) : null });
                if (ranks != null) facts.Add(new Fact { Key = "lord:" + id + ":ranks", Value = Math.Round(ranks.Value).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = 1, Say = (o, n) => o != null && (Math.Abs(D(n) - D(o)) >= 10 || (D(o) < 50) != (D(n) < 50)) ? name + ": ranks " + D(o) + "% → " + D(n) + "%" : null });
                if (food != null) facts.Add(new Fact { Key = "lord:" + id + ":food", Value = Math.Floor(food.Value).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = food <= 1 ? 3 : 2, Say = (o, n) => o != null && Crossed(D(o), D(n), 3, 1) ? name + ": food for " + D(n) + " days (was " + D(o) + ")" : null });
                facts.Add(new Fact { Key = "lord:" + id + ":army", Value = army ? "leads" : "", Page = pg, Weight = 1, Say = (o, n) => o == null ? null : n == "leads" ? name + " now leads an army" : name + " no longer leads an army" });
                if (gold != null) facts.Add(new Fact { Key = "lord:" + id + ":poor", Value = look != null && look.Contains("oo poor") ? "poor" : "", Page = pg, Weight = 1, Say = (o, n) => o == null ? null : n == "poor" ? name + " is too poor to recruit" : name + " can pay to recruit again" });
            }
            var front = new StringBuilder();
            foreach (KeyValuePair<string, List<string>> kv in byArea)
            {
                front.AppendLine("## " + kv.Key + " (" + kv.Value.Count + ")");
                foreach (string l in kv.Value) front.AppendLine(l);
                front.AppendLine();
            }
            string held = string.Join("\n", Items(x, "captives").Select(T));
            pages["lords/index"] = new Page { Title = "Your lords and armies", Desc = forces + " in the field, " + armies + " leading armies; one line each, with its page", Body = "Each line: name [id] (its page), then where it is, what it does and its order. A lord marching in an army is named only on its leader's page.\n\n" + index };
            pages["lords/by-front"] = new Page { Title = "Your lords by region", Desc = "the same lines, by the region of your realm each is in", Body = front.ToString() };
            pages["lords/no-order"] = new Page { Title = "Lords with no order", Desc = "led by the game's own AI until you order them", Body = noOrder.Length > 0 ? noOrder.ToString() : "Every lord in the field holds an order of yours." };
            pages["lords/held-by-the-enemy"] = new Page { Title = "Your lords held by the enemy", Desc = "your captured lords, by where they are held", Body = held.Length > 0 ? held : "None of your lords is held." };
            pages["lords/order-results"] = new Page { Title = "What became of your orders", Desc = "the game's receipts of your answers and the quieter news since each, newest first", Body = _s.Results.Count > 0 ? string.Join("\n\n", _s.Results) : "Nothing yet in this game session." };
            foreach (JsonObject it in Items(x, "captives"))
                foreach (string id in Ids(it).Where(i => i.StartsWith("lord", StringComparison.Ordinal) || i.StartsWith("Character", StringComparison.Ordinal)))
                    facts.Add(new Fact { Key = "captive:" + id, Value = "held", Page = "lords/held-by-the-enemy", Weight = 3, Say = (o, n) => o == null ? CaptiveName(T(it), id, names) + " is held by the enemy: " + Short(T(it).TrimStart('-', ' '), 160) : null });
            facts.Add(new Fact { Key = "results", Value = _s.Results.Count > 0 ? Hash(_s.Results[0]) : "", Page = "lords/order-results", Weight = 2, Say = (o, n) => n.Length > 0 && o != n ? "the game's receipts of your answers since the last: " + Short(_s.Results[0].Substring(_s.Results[0].IndexOf((char)10) + 1), 160) : null });
        }

        /// <summary>A captured lord's name, from his line ("Rashid [CharacterObject_5554]") or the names the book gave.</summary>
        private static string CaptiveName(string line, string id, Dictionary<string, string> names)
        {
            Match m = Regex.Match(line ?? "", @"([^:;,\[\]]+?) \[" + Regex.Escape(id) + @"\]");
            if (m.Success) return m.Groups[1].Value.Trim();
            return names.TryGetValue(id, out string nm) ? nm : id;
        }

        private static string FirstClause(string order)
        {
            string t = order;
            foreach (string stop in new[] { ". Now", ". ", "; since" }) { int i = t.IndexOf(stop, StringComparison.Ordinal); if (i > 0) t = t.Substring(0, i); }
            t = Regex.Replace(t, @" \[[^\]]+\]", "");
            return Short(t.Trim(), 90);
        }

        // ---------- settlements ----------

        private sealed class Place { public string Id, Name, Kind; public float X, Y; }
        private sealed class RingEntry { public string Id { get; set; } public double Length { get; set; } }

        private void BuildSettlements(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts, Dictionary<string, string> names, Func<string, string, double> unusedWay)
        {
            var typed = (x.View?["holdings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToDictionary(h => (string)h["id"] ?? "", h => h, StringComparer.Ordinal);
            var places = LoadPlaces(x);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            if (x.View?["owners"] is JsonObject ow) foreach (KeyValuePair<string, JsonNode> kv in ow) owners[kv.Key] = (string)kv.Value ?? "";
            if (x.View?["enemy_holdings"] is JsonObject eh) foreach (KeyValuePair<string, JsonNode> kv in eh) foreach (JsonNode n in kv.Value as JsonArray ?? new JsonArray()) if ((string)n is string hid && !owners.ContainsKey(hid)) owners[hid] = kv.Key;
            var own = new List<(string id, string name, string kind, string line, string area)>();
            var villages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (JsonObject it in Items(x, "holdings"))
            {
                string t = T(it);
                if (t.StartsWith("- ENEMY", StringComparison.Ordinal)) continue;
                Match h = Head.Match(t);
                if (!h.Success) continue;
                string id = h.Groups[2].Value, name = h.Groups[1].Value;
                names[id] = name;
                string kind = typed.TryGetValue(id, out JsonObject ty) ? (string)ty["kind"] ?? "" : id.Contains("village") ? "village" : id.StartsWith("town", StringComparison.Ordinal) ? "town" : "castle";
                owners[id] = x.RealmName;
                if (kind == "village")
                {
                    string bound = BoundTo(id);
                    if (!villages.TryGetValue(bound, out List<string> vl)) villages[bound] = vl = new List<string>();
                    vl.Add(t.TrimStart('-', ' ').Trim());
                    continue;
                }
                own.Add((id, name, kind, t.TrimStart('-', ' ').Trim(), (string)it["area"] ?? "elsewhere"));
            }
            // enemies in view now, and seen within the last day, for the strength around each place
            var foes = new List<(string id, string name, string realm, float px, float py, double strength, bool inView)>();
            foreach (string list in new[] { "seen", "last_seen" })
                foreach (JsonObject p in (x.View?[list] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    if ((string)p["relation"] != "enemy") continue;
                    double seenH = (double?)p["seen_hours"] ?? double.NaN;
                    if (list == "last_seen" && !double.IsNaN(x.Hours) && !double.IsNaN(seenH) && x.Hours - seenH > DayHours) continue;
                    foes.Add(((string)p["id"] ?? "", (string)p["name"] ?? "", (string)p["faction_name"] ?? "", (float?)p["x"] ?? 0, (float?)p["y"] ?? 0, (double?)p["strength"] ?? 0, list == "seen"));
                }
            var used = new HashSet<string>(StringComparer.Ordinal);
            var index = new StringBuilder();
            var byArea = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            var sieges = Items(x, "sieges").ToList();
            string map = (string)x.View?["map"] ?? "";
            LoadRings(map);
            bool ringsChanged = false;
            foreach (var h in own)
            {
                string slug = Unique(used, Slug(h.name), h.id);
                string pg = "settlements/" + slug;
                typed.TryGetValue(h.id, out JsonObject ty);
                double garrison = (double?)ty?["garrison"] ?? NumAfter(h.line, "garrison ") ?? 0, militia = (double?)ty?["militia"] ?? NumAfter(h.line, "militia ") ?? 0;
                double foodNow = (double?)ty?["food"] ?? double.NaN, perDay = (double?)ty?["food_per_day"] ?? double.NaN;
                double days = !double.IsNaN(foodNow) && perDay > 0 ? Math.Floor(foodNow / perDay) : NumBefore(h.line, " days") ?? double.NaN;
                string status = ((string)ty?["status"] ?? "").Trim();
                bool besieged = sieges.Any(s => Ids(s).Contains(h.id) && T(s).Contains("besieged"));
                if (besieged) status = "besieged";
                Place me = places.TryGetValue(h.id, out Place pl) ? pl : null;
                // the places around it: within three days' march at the standard pace, by a way over land; else the closest
                List<RingEntry> ring = me != null ? Ring(me, places, ref ringsChanged) : new List<RingEntry>();
                var around = new StringBuilder();
                foreach (RingEntry r in ring.OrderBy(r => r.Length))
                {
                    places.TryGetValue(r.Id, out Place other);
                    string owner = owners.TryGetValue(r.Id, out string o) && o.Length > 0 ? o : "a realm your realm has not seen";
                    around.AppendLine("- " + (other?.Name ?? r.Id) + " [" + r.Id + "], " + (other?.Kind ?? "") + " of " + owner + ": about " + Math.Round(r.Length / StandardPace) + " h at speed 2 (" + r.Length.ToString("0", CultureInfo.InvariantCulture) + " by the way over land)");
                }
                bool closestOnly = ring.Count == 1 && ring[0].Length > RingHours * StandardPace;
                // the enemy around it: seen within that reach, and within a day
                double ringSum = 0, daySum = 0;
                var near = new List<string>();
                if (me != null)
                    foreach (var f in foes)
                    {
                        double straight = Math.Sqrt((f.px - me.X) * (f.px - me.X) + (f.py - me.Y) * (f.py - me.Y));
                        if (straight > RingHours * StandardPace) continue;
                        double len = Way(f.id, f.px, f.py, me, out bool byWay);
                        if (double.IsNaN(len) || len > RingHours * StandardPace) continue;
                        ringSum += f.strength;
                        if (len <= DayHours * StandardPace) daySum += f.strength;
                        near.Add("- " + f.name + " [" + f.id.Replace("_party_1", "") + "] (" + f.realm + "), strength " + Fmt(f.strength) + ", about " + Math.Round(len / StandardPace) + " h at speed 2" + (byWay ? "" : " (as the crow flies)") + (f.inView ? ", in view" : ", seen within the last day"));
                    }
                string look = BookPage(x, h.id);
                var tied = new List<string>();
                var inADay = new List<string>();
                if (look != null)
                {
                    bool inList = false;
                    foreach (string l in look.Split('\n'))
                    {
                        if (l.StartsWith("Your lords, soonest first", StringComparison.Ordinal)) { inList = true; continue; }
                        if (!l.StartsWith("- ", StringComparison.Ordinal)) { if (inList && l.Trim().Length > 0 && !l.StartsWith("  ", StringComparison.Ordinal)) inList = false; continue; }
                        if (!inList) continue;
                        if (l.Contains(" " + h.name + ")") || l.Contains(" " + h.name + ",") || l.EndsWith(" " + h.name, StringComparison.Ordinal)) tied.Add(l);
                        double? hh = l.Contains("under an hour") ? 0 : NumAfter(l, "about ");
                        if (hh != null && hh <= DayHours) inADay.Add(l);
                    }
                }
                var body = new StringBuilder();
                body.AppendLine(h.line);
                body.AppendLine();
                body.AppendLine("Food: " + (double.IsNaN(days) ? "not known" : days + " days at what it eats now") + (double.IsNaN(foodNow) ? "" : " (" + Fmt(foodNow) + " in store" + (perDay > 0 ? ", eating " + Fmt(perDay) + " a day" : "") + ")") + ".");
                foreach (JsonObject s in sieges.Where(s => Ids(s).Contains(h.id))) body.AppendLine("Siege: " + T(s).TrimStart('-', ' '));
                body.AppendLine();
                body.AppendLine("Lords whose orders are tied to it (" + tied.Count + "), with their march here at their own pace:");
                body.AppendLine(tied.Count > 0 ? string.Join("\n", tied) : "- none");
                body.AppendLine();
                body.AppendLine("Your lords who can get here within a day (" + inADay.Count + "):");
                body.AppendLine(inADay.Count > 0 ? string.Join("\n", inADay) : "- none");
                body.AppendLine();
                body.AppendLine("Enemy strength around it (as your realm has seen it, in view now or within the last day, at speed 2 over land): within 3 days' march " + Fmt(ringSum) + "; within a day's march " + Fmt(daySum) + ".");
                if (near.Count > 0) body.AppendLine(string.Join("\n", near.Take(25)));
                body.AppendLine();
                body.AppendLine(closestOnly ? "Around it: no castle or town within 3 days' march at speed 2; the closest:" : "Around it: every castle and town within 3 days' march at speed 2, by a way over land (" + ring.Count + "):");
                body.AppendLine(around.Length > 0 ? around.ToString().TrimEnd() : "- not worked out yet (the walking map is not copied)");
                if (villages.TryGetValue(h.id, out List<string> vs)) body.AppendLine().AppendLine("Its villages:").AppendLine(string.Join("\n", vs.Select(v => "- " + v)));
                if (look != null) body.AppendLine().AppendLine("The game's look-up of it:").AppendLine(look);
                pages[pg] = new Page { Title = h.name + " [" + h.id + "]", Desc = Short(h.line, 160), Body = body.ToString(), Plain = true };
                string idx = "- " + h.name + " [" + h.id + "] (" + slug + "): " + h.kind + ", garrison " + Fmt(garrison) + ", militia " + Fmt(militia) + ", food " + (double.IsNaN(days) ? "?" : days + " days")
                             + (status.Length > 0 ? ", " + status : "") + "; enemy around: " + Fmt(ringSum) + " within 3 days, " + Fmt(daySum) + " within a day";
                index.AppendLine(idx);
                if (!byArea.TryGetValue(h.area, out List<string> al)) byArea[h.area] = al = new List<string>();
                al.Add(idx);
                string name = h.name;
                facts.Add(new Fact { Key = "holding:" + h.id + ":here", Value = name + "|" + slug, Page = pg, Weight = 3, Say = (o, n) => o == null ? name + " is now yours" : null });
                facts.Add(new Fact { Key = "holding:" + h.id + ":garrison", Value = Math.Round(garrison).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = 1, Say = (o, n) => Moved(o, n, 0.10, 20) ? name + ": garrison " + Fmt(D(o)) + " → " + Fmt(D(n)) : null });
                facts.Add(new Fact { Key = "holding:" + h.id + ":militia", Value = Math.Round(militia).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = 1, Say = (o, n) => Moved(o, n, 0.15, 20) ? name + ": militia " + Fmt(D(o)) + " → " + Fmt(D(n)) : null });
                if (!double.IsNaN(days)) facts.Add(new Fact { Key = "holding:" + h.id + ":food", Value = days.ToString(CultureInfo.InvariantCulture), Page = pg, Weight = days <= 1 ? 3 : 2, Say = (o, n) => o != null && (Crossed(D(o), D(n), 3, 1) || D(o) <= 3 && D(n) > 3) ? name + ": food " + D(o) + " → " + D(n) + " days" : null });
                facts.Add(new Fact { Key = "holding:" + h.id + ":status", Value = status, Page = pg, Weight = status == "besieged" ? 3 : 2, Say = (o, n) => o == null ? null : n.Length == 0 ? name + ": no longer " + o : name + ": " + n });
                facts.Add(new Fact { Key = "holding:" + h.id + ":enemy3d", Value = Math.Round(ringSum).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = 2, Say = (o, n) => Swing(o, n) ? name + ": enemy within 3 days' march " + Fmt(D(o)) + " → " + Fmt(D(n)) : null });
                facts.Add(new Fact { Key = "holding:" + h.id + ":enemy1d", Value = Math.Round(daySum).ToString(CultureInfo.InvariantCulture), Page = pg, Weight = 3, Say = (o, n) => Swing(o, n) ? name + ": enemy within a day's march " + Fmt(D(o)) + " → " + Fmt(D(n)) : null });
                if (!_s.FormerHoldings.Contains(name)) _s.FormerHoldings.Add(name);
            }
            if (ringsChanged) SaveRings(map);
            var front = new StringBuilder();
            foreach (KeyValuePair<string, List<string>> kv in byArea)
            {
                front.AppendLine("## " + kv.Key + " (" + kv.Value.Count + ")");
                foreach (string l in kv.Value) front.AppendLine(l);
                front.AppendLine();
            }
            pages["settlements/index"] = new Page { Title = "Your castles and towns", Desc = own.Count + " castles and towns, " + villages.Values.Sum(v => v.Count) + " villages under them", Body = "Each line: name [id] (its page), then garrison, militia, food, siege, and the enemy strength seen around it (within 3 days' and a day's march at speed 2, by a way over land).\n\n" + index };
            pages["settlements/by-front"] = new Page { Title = "Your castles and towns by region", Desc = "the same lines, by region", Body = front.ToString() };
        }

        /// <summary>The castle or town a village belongs to, from its id (castle_village_V4_1: castle_V4; village_V6_1: town_V6).</summary>
        internal static string BoundTo(string village)
        {
            Match m = Regex.Match(village ?? "", @"^castle_village_(.+)_\d+$");
            if (m.Success) return "castle_" + m.Groups[1].Value;
            m = Regex.Match(village ?? "", @"^village_(.+)_\d+$");
            return m.Success ? "town_" + m.Groups[1].Value : village;
        }

        private Dictionary<string, Place> _places;
        private string _placesMap;

        private Dictionary<string, Place> LoadPlaces(ShelfInput x)
        {
            string map = (string)x.View?["map"] ?? "";
            if (_places != null && _placesMap == map) return _places;
            var d = new Dictionary<string, Place>(StringComparer.Ordinal);
            try
            {
                string dir = WalkMap.FindDir(RouteTool.DefaultRoot(), map);
                string file = dir != null ? Path.Combine(dir, "places.json") : null;
                if (file != null && File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonArray arr)
                    foreach (JsonObject p in arr.OfType<JsonObject>())
                    {
                        string kind = (string)p["kind"];
                        if (kind != "castle" && kind != "town") continue;
                        d[(string)p["id"]] = new Place { Id = (string)p["id"], Name = (string)p["name"], Kind = kind, X = (float?)p["gx"] ?? (float?)p["x"] ?? 0, Y = (float?)p["gy"] ?? (float?)p["y"] ?? 0 };
                    }
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException) { }
            _places = d;
            _placesMap = map;
            return d;
        }

        private void LoadRings(string map)
        {
            if (_rings != null && _ringsMap == map) return;
            _rings = new Dictionary<string, List<RingEntry>>(StringComparer.Ordinal);
            _ringsMap = map;
            try
            {
                if (File.Exists(_ringsFile) && JsonNode.Parse(File.ReadAllText(_ringsFile)) is JsonObject o && (string)o["map"] == map && o["rings"] is JsonObject r)
                    _rings = JsonSerializer.Deserialize<Dictionary<string, List<RingEntry>>>(r.ToJsonString()) ?? _rings;
            }
            catch (Exception e) when (e is IOException || e is JsonException) { }
        }

        private void SaveRings(string map)
        {
            try { Memory.Write(_ringsFile, new JsonObject { ["map"] = map, ["rings"] = JsonNode.Parse(JsonSerializer.Serialize(_rings)) }.ToJsonString()); } catch (IOException) { }
        }

        /// <summary>A place's surroundings: every castle and town within three days' march at speed 2 by a way over land, else the closest; worked out once per map (the ways never change).</summary>
        private List<RingEntry> Ring(Place me, Dictionary<string, Place> places, ref bool changed)
        {
            if (_rings.TryGetValue(me.Id, out List<RingEntry> known)) return known;
            double reach = RingHours * StandardPace;
            var list = new List<RingEntry>();
            var near = places.Values.Where(p => p.Id != me.Id).Select(p => (p, d: Math.Sqrt((p.X - me.X) * (p.X - me.X) + (p.Y - me.Y) * (p.Y - me.Y)))).OrderBy(t => t.d).ToList();
            bool anyWay = false;
            foreach (var (p, d) in near.Where(t => t.d <= reach))
            {
                double len = WayBetween(me.X, me.Y, p.X, p.Y, out bool byWay);
                anyWay |= byWay;
                if (!double.IsNaN(len) && len <= reach) list.Add(new RingEntry { Id = p.Id, Length = Math.Round(len, 1) });
            }
            if (list.Count == 0)
            {
                RingEntry best = null;
                foreach (var (p, d) in near.Take(8))
                {
                    double len = WayBetween(me.X, me.Y, p.X, p.Y, out _);
                    if (!double.IsNaN(len) && (best == null || len < best.Length)) best = new RingEntry { Id = p.Id, Length = Math.Round(len, 1) };
                }
                if (best != null) list.Add(best);
            }
            if (anyWay || list.Count > 0 && FinderReady) { _rings[me.Id] = list; changed = true; }
            return list;
        }

        private bool FinderReady => _finderMap != null;
        private string _finderMap;

        /// <summary>The length of the way over land between two points (the game's own way-finder copy); straight when the way-finder is not ready (byWay false); NaN when there is no way.</summary>
        private double WayBetween(float x0, float y0, float x1, float y1, out bool byWay)
        {
            byWay = false;
            double straight = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (_ringsMap == null) return straight;
            double len = double.NaN;
            bool ok = RouteTool.WithFinder(RouteTool.DefaultRoot(), _ringsMap, f =>
            {
                RouteResult r = f.Find(x0, y0, x1, y1, analyse: false);
                len = r.Ok ? r.Length : double.NaN;
            });
            if (!ok) return straight;
            _finderMap = _ringsMap;
            byWay = true;
            return len;
        }

        private double Way(string id, float px, float py, Place me, out bool byWay)
        {
            string key = id + "|" + Math.Round(px) + "|" + Math.Round(py) + "|" + me.Id;
            if (_wayCache.TryGetValue(key, out var known)) { byWay = known.byWay; return known.len; }
            double len = WayBetween(px, py, me.X, me.Y, out byWay);
            if (_wayCache.Count > 20000) _wayCache.Clear();
            if (byWay) _wayCache[key] = (len, true);
            return len;
        }

        // ---------- realm ----------

        private void BuildRealm(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts)
        {
            List<string> realm = Items(x, "realm").Select(T).ToList();
            List<string> enemyRealms = (x.View?["enemy_holdings"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? new List<string>();
            List<string> enemyLines = realm.Where(l => enemyRealms.Any(r => l.StartsWith("- " + r, StringComparison.Ordinal))).ToList();
            List<string> sum = realm.Except(enemyLines).ToList();
            string lag = _s.Sections.TryGetValue("THE CLOCK", out string lg) ? lg : "";
            pages["realm/in-sum"] = new Page { Title = "Your realm in sum, and the clock", Desc = "the realm's numbers, the hour and how long your orders take to land", Body = "As of " + x.Date + "." + (lag.Length > 0 ? "\n" + lag : "") + "\n\n" + string.Join("\n", sum) };
            pages["realm/money"] = new Page { Title = "Money", Desc = "the clans' gold, wages, loot and prisoners", Body = string.Join("\n", Items(x, "purse").Select(T)) };
            var cap = new StringBuilder();
            cap.AppendLine("Your lords held by others:");
            List<string> held = Items(x, "captives").Select(T).ToList();
            cap.AppendLine(held.Count > 0 ? string.Join("\n", held) : "- none");
            string prisoners = Items(x, "purse").Select(T).FirstOrDefault(l => l.Contains("Holding prisoners"));
            cap.AppendLine().AppendLine("Prisoners your lords hold:").AppendLine(prisoners ?? "- none");
            pages["realm/captives"] = new Page { Title = "Captives", Desc = "your lords held by others, and the prisoners your lords hold", Body = cap.ToString() };
            List<string> allies = Items(x, "allies").Select(T).ToList(), diplomacy = Items(x, "diplomacy").Select(T).ToList(), votes = Items(x, "votes").Select(T).ToList();
            string plans = _s.Sections.TryGetValue("ALLIES AND JOINT PLANS", out string ap) ? ap : "";
            pages["realm/allies-and-letters"] = new Page
            {
                Title = "Allies, letters, diplomacy and votes", Desc = "your allies and joint plans, diplomacy as your ruler's screens show it, open votes and offers",
                Body = "Allies and joint plans:\n" + (allies.Count > 0 ? string.Join("\n", allies) : "- none") + (plans.Length > 0 ? "\n\n" + plans : "") + "\n\nDiplomacy:\n" + string.Join("\n", diplomacy) + "\n\nOpen votes and offers:\n" + (votes.Count > 0 ? string.Join("\n", votes) : "- none")
            };
            pages["realm/enemy-realms"] = new Page { Title = "The enemy realms", Desc = "their public numbers, as their encyclopedia pages show any ruler", Body = enemyLines.Count > 0 ? string.Join("\n", enemyLines) : string.Join("\n", realm) };
            foreach (string v in votes) { string k = Hash(Regex.Replace(v, @"\d", "")); facts.Add(new Fact { Key = "vote:" + k, Value = "open", Page = "realm/allies-and-letters", Weight = 2, Say = (o, n) => o == null ? "open: " + Short(v.TrimStart('-', ' '), 160) : null }); }
            foreach (string a in allies) { string k = Hash(Regex.Replace(a, @"\d", "")); facts.Add(new Fact { Key = "ally:" + k, Value = "1", Page = "realm/allies-and-letters", Weight = 1, Say = (o, n) => o == null ? "allies: " + Short(a.TrimStart('-', ' '), 160) : null }); }
        }

        // ---------- intelligence: the game's pages ----------

        private void BuildIntelligence(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts, List<ShelfChange> extra, bool fresh)
        {
            // the enemies the agent tracks: the names of its own intelligence pages, and the names in their first lines
            var tracked = x.AgentPages.Where(kv => kv.Key.StartsWith("intelligence/", StringComparison.Ordinal)).Select(kv => kv.Key.Substring(13)).ToList();
            bool Tracked(string name) => tracked.Any(t => Slug(name).StartsWith(t, StringComparison.Ordinal) || t.StartsWith(Slug(name), StringComparison.Ordinal) && Slug(name).Length >= 4);
            var inView = new StringBuilder();
            var earlier = new StringBuilder();
            var trails = new StringBuilder();
            var star = new StringBuilder();
            int quiet = 0;
            foreach (JsonObject it in Items(x, "enemies"))
            {
                string t = T(it);
                Match h = Head.Match(t);
                string name = h.Success ? h.Groups[1].Value : "", id = h.Success ? h.Groups[2].Value : "";
                bool tr = name.Length > 0 && Tracked(name);
                string line = (tr ? "★ " : "") + t.TrimStart('-', ' ').Trim();
                if (t.StartsWith("- ENEMY", StringComparison.Ordinal))
                {
                    inView.AppendLine("- " + line);
                    if (tr) star.AppendLine("- " + line);
                    double strength = NumAfter(t, "strength ") ?? 0;
                    bool army = t.Contains("army of") || t.Contains(" parties");
                    if (id.Length > 0 && (tr || strength >= 300 || army))
                        facts.Add(new Fact { Key = "seen:" + id, Value = "1", Page = "intelligence/sightings", Weight = tr ? 2 : 1, Say = (o, n) => o == null ? "in view: " + Short(line.Replace("ENEMY ", ""), 200) : null });
                    else if (id.Length > 0)
                    {
                        if (!_s.Facts.ContainsKey("seen:" + id)) quiet++;
                        facts.Add(new Fact { Key = "seen:" + id, Value = "1", Page = "intelligence/sightings", Weight = 1, Say = (o, n) => null });
                    }
                }
                else if (t.StartsWith("- SEEN EARLIER", StringComparison.Ordinal)) { earlier.AppendLine("- " + line.Replace("SEEN EARLIER ", "")); if (tr) star.AppendLine("- " + line); }
                else if (t.StartsWith("- TRAIL", StringComparison.Ordinal)) trails.AppendLine("- " + line.Replace("TRAIL ", ""));
            }
            pages["intelligence/sightings"] = new Page
            {
                Title = "What your realm sees of the enemy", Desc = "kept by the game: in view now, seen earlier, and enemy armies' trails; ★ the enemies you track (your own pages here)",
                Body = (star.Length > 0 ? "The enemies you track (★):\n" + star + "\n" : "") + "In view now:\n" + (inView.Length > 0 ? inView.ToString() : "- none\n") + "\nSeen earlier, now out of sight:\n" + (earlier.Length > 0 ? earlier.ToString() : "- none\n") + "\nEnemy armies' trails:\n" + (trails.Length > 0 ? trails.ToString() : "- none\n")
            };
            var holdings = new StringBuilder();
            foreach (JsonObject it in Items(x, "holdings"))
            {
                string t = T(it);
                if (!t.StartsWith("- ENEMY", StringComparison.Ordinal)) continue;
                Match h = Head.Match(t);
                holdings.AppendLine(t.Replace("- ENEMY ", "- "));
                if (!h.Success) continue;
                string id = h.Groups[2].Value, name = h.Groups[1].Value;
                string look = BookPage(x, id);
                // its look-up, cut to its first lines: your lords soonest, the enemies near it (the muster's running totals are on your own holdings' pages)
                if (look != null) holdings.AppendLine("  " + string.Join("\n  ", look.Split('\n').Skip(1).Where(l => !l.StartsWith("Muster", StringComparison.Ordinal) && !l.StartsWith("- within", StringComparison.Ordinal) && !l.StartsWith("- later", StringComparison.Ordinal)).Take(10)));
                double? garrison = NumAfter(t, "garrison ");
                if (garrison != null) facts.Add(new Fact { Key = "enemyholding:" + id + ":garrison", Value = Math.Round(garrison.Value).ToString(CultureInfo.InvariantCulture), Page = "intelligence/enemy-holdings", Weight = 1, Say = (o, n) => Moved(o, n, 0.10, 20) ? name + " (enemy): garrison " + Fmt(D(o)) + " → " + Fmt(D(n)) : null });
            }
            pages["intelligence/enemy-holdings"] = new Page { Title = "Enemy castles and towns near your lands", Desc = "kept by the game: garrison, walls and food as your realm last saw them", Body = holdings.Length > 0 ? holdings.ToString() : "None near your lands." };
            string human = _s.Sections.TryGetValue("YOUR ENEMY, THE HUMAN PLAYER", out string hp) ? hp : "";
            pages["intelligence/human-player-record"] = new Page { Title = "The human player's record", Desc = "kept by the game: his battles and who won", Body = human.Length > 0 ? human : "Not known yet in this game session." };
            facts.Add(new Fact { Key = "human", Value = Hash(Regex.Replace(human, @"\d+ (hours?|days?) ago", "")), Page = "intelligence/human-player-record", Weight = 2, Say = (o, n) => o != null ? "the human player's record changed" : null });
            string chances = _s.Sections.TryGetValue("CHANCES TO STRIKE", out string ch) ? ch : "";
            pages["intelligence/chances-to-strike"] = new Page { Title = "Chances to strike", Desc = "kept by the game: lone enemy lords under strength 700 your lords could beat", Body = chances.Length > 0 ? chances : "None found at the last wake." };
            // the smaller enemy parties come into view, in one line
            if (!fresh && quiet > 0) extra.Add(Change("intelligence/sightings", quiet + " smaller enemy part" + (quiet == 1 ? "y" : "ies") + " came into view", 1, x));
        }

        // ---------- orders ----------

        private void BuildOrders(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts)
        {
            string text = (x.ToolsText ?? "").Replace("\r", "");
            var index = new StringBuilder();
            var common = new StringBuilder();
            string category = "";
            string name = null;
            var current = new StringBuilder();
            bool own = false;
            int count = 0;
            void Flush()
            {
                if (name == null) return;
                string body = current.ToString().Trim();
                string slug = Slug(name);
                // what it does: past its name in bold and the bracket of what it takes
                string what = body;
                int bold = what.IndexOf("**", 2, StringComparison.Ordinal);
                if (what.StartsWith("**", StringComparison.Ordinal) && bold > 0) what = what.Substring(bold + 2);
                what = what.TrimStart();
                if (what.StartsWith("(", StringComparison.Ordinal))
                {
                    int depth = 0, k = 0;
                    for (; k < what.Length; k++) { if (what[k] == '(') depth++; else if (what[k] == ')' && --depth == 0) break; }
                    if (k < what.Length) what = what.Substring(k + 1);
                }
                what = what.TrimStart(' ', ':');
                string first = Short(what, 200);
                index.AppendLine("- " + name + (own ? " (yours)" : "") + " (" + slug + "): " + first);
                pages["orders/" + slug] = new Page
                {
                    Title = name + (own ? " (your own order)" : ""), Desc = Short(first, 140),
                    Body = (category.Length > 0 ? "Kind: " + category.TrimEnd(':') + "\n\n" : "") + body + "\n\nIts whole definition (every setting, rule and call-back): read tools/" + name + ".json in your channel. The settings every order takes are on the page common-settings."
                };
                facts.Add(new Fact { Key = "order:" + slug, Value = own ? Hash(body.Length > 300 ? body.Substring(0, 300) : body) : "ours", Page = "orders/" + slug, Weight = own ? 2 : 1, Say = (o, n) => o == null ? (own ? "your own order " + name + " is kept" : null) : (own ? "your own order " + name + " changed" : null) });
                count++;
                name = null;
                current.Clear();
            }
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("- **", StringComparison.Ordinal))
                {
                    Flush();
                    int e = line.IndexOf("**", 4, StringComparison.Ordinal);
                    name = e > 4 ? line.Substring(4, e - 4) : line.Substring(4);
                    current.AppendLine(line.Substring(2));
                    continue;
                }
                if (line.StartsWith("ORDERS YOU CAN GIVE", StringComparison.Ordinal)) continue;
                if (line.StartsWith("YOUR OWN TOOLS", StringComparison.Ordinal)) { Flush(); own = true; category = "Your own orders"; index.AppendLine().AppendLine("Your own orders (your realm's library; no limit on how many you keep):"); continue; }
                if (line.Length == 0) { continue; }
                if (name != null && (line.StartsWith("  ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal))) { current.AppendLine(line); continue; }
                if (line.EndsWith(":", StringComparison.Ordinal) && line.Length < 200) { Flush(); category = line; index.AppendLine().AppendLine(line); continue; }
                if (line.StartsWith("(", StringComparison.Ordinal) && (line.Contains("your library may keep") || line.Contains("in your library;"))) { Flush(); continue; }
                Flush();
                common.AppendLine(line).AppendLine();
            }
            Flush();
            pages["orders/index"] = new Page { Title = "Every order you can give", Desc = count + " orders, ours and your own, one line each with its page", Body = "Each line: name (its page): what it does. Read an order's page before you first use it in a conversation, or when unsure of a setting.\n" + index };
            pages["orders/common-settings"] = new Page { Title = "Settings every order takes", Desc = "caution, danger, join_hours, garrison_floor, food, and riding to relieve a fight", Body = common.Length > 0 ? common.ToString() : "(none listed)" };
            pages["orders/writing-your-own"] = new Page { Title = "Writing your own orders", Desc = "the language of your own tools, its functions, the checks, and checking one before you keep it", Body = ((x.OwnToolsManual ?? "") + "\n\n" + (x.CheckManual ?? "")).Trim() };
        }

        // ---------- the agent's own pages, and the chronicler's ----------

        private void BuildAgentPages(ShelfInput x, SortedDictionary<string, Page> pages, List<ShelfChange> extra)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, PageRec> kv in x.AgentPages)
            {
                if (kv.Key.StartsWith(".", StringComparison.Ordinal) || kv.Key.IndexOf('/') <= 0) continue;
                string file = kv.Key.Substring(0, kv.Key.IndexOf('/'));
                if (!Files.Contains(file)) continue;
                PageRec p = kv.Value;
                pages[kv.Key] = new Page { Title = kv.Key.Substring(file.Length + 1).Replace('-', ' '), Desc = "written by " + (p.By == "chronicler" ? "your chronicler" : "you") + (p.Date.Length > 0 ? " at " + p.Date : ""), Body = p.Text, Agent = true };
                seen.Add(kv.Key);
                string h = Hash(p.Text);
                if (_s.AgentHash.TryGetValue(kv.Key, out string old) && old == h) continue;
                bool first = !_s.AgentHash.ContainsKey(kv.Key);
                _s.AgentHash[kv.Key] = h;
                if (first && p.By != "chronicler" && double.IsNaN(p.Hours)) continue;   // a page known before this game session
                extra.Add(Change(kv.Key, (p.By == "chronicler" ? "your chronicler wrote " : "you wrote ") + kv.Key.Substring(file.Length + 1) + (p.Why.Length > 0 ? ": " + Short(p.Why, 120) : ""), p.By == "chronicler" ? 1 : 1, x));
            }
            foreach (string gone in _s.AgentHash.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _s.AgentHash.Remove(gone);
                extra.Add(Change(gone, "you removed " + gone.Substring(gone.IndexOf('/') + 1), 1, x));
            }
            if (!string.IsNullOrWhiteSpace(x.Handover))
                pages["notes/handover"] = new Page { Title = "Your last handover", Desc = "written by you" + (x.HandoverDate.Length > 0 ? " at " + x.HandoverDate : "") + "; what your last conversation passed on", Body = x.Handover.Trim() };
            if (!pages.Keys.Any(k => k.StartsWith("plan/", StringComparison.Ordinal) && pages[k].Agent))
                pages["plan/the-plan"] = new Page { Title = "the plan", Desc = "not written yet", Body = "(Not written yet. Write your plan's pages in your answer's \"pages\".)", Agent = true };
        }

        // ---------- plan: the game's pages ----------

        private void BuildPlanGame(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts, List<ShelfChange> extra, Dictionary<string, string> names)
        {
            string watches = _s.Sections.TryGetValue("YOUR WATCHES", out string w) ? w : "";
            string planText = string.Join("\n", pages.Where(kv => kv.Key.StartsWith("plan/", StringComparison.Ordinal) && kv.Value.Agent).Select(kv => kv.Value.Body));
            var names2 = new List<string>();
            foreach (string l in watches.Replace("\r", "").Split('\n'))
            {
                if (!l.StartsWith("- ", StringComparison.Ordinal)) continue;
                string n = l.Substring(2);
                int e = n.IndexOfAny(new[] { ' ', ':', '(' });
                n = e > 0 ? n.Substring(0, e) : n;
                names2.Add(n);
                int trueNow = Regex.Matches(l, @"\[true now\]").Count;
                string wn = n;
                facts.Add(new Fact { Key = "watch:" + n + ":here", Value = wn, Page = "plan/watches", Weight = 1, Say = (o, nn) => o == null ? "watch " + wn + " set" : null });
                facts.Add(new Fact { Key = "watch:" + n + ":true", Value = trueNow.ToString(CultureInfo.InvariantCulture), Page = "plan/watches", Weight = 2, Say = (o, nn) => o != null && D(nn) > D(o) ? "watch " + wn + ": a condition now holds" : o != null && D(nn) < D(o) ? "watch " + wn + ": a condition no longer holds" : null });
            }
            // the watches that woke it, once a wake
            if (x.Poke > _s.FiredPoke)
            {
                foreach (string l in (x.Why ?? "").Replace("\r", "").Split('\n'))
                {
                    string t = l.TrimStart('-', ' ').Trim();
                    if (t.StartsWith("Your watch", StringComparison.Ordinal) || t.StartsWith("Check-in", StringComparison.OrdinalIgnoreCase) || t.Contains("checks in"))
                        extra.Add(Change("plan/watches", "fired: " + Short(t, 200), 3, x));
                }
                if (!string.IsNullOrWhiteSpace(x.Why)) _s.FiredPoke = x.Poke;
            }
            pages["plan/watches"] = new Page { Title = "Your watches and check-ins", Desc = "kept by the game: each watch, whether each condition holds now, its check-in and its end", Body = watches.Length > 0 ? watches + "\n\nYour next strategy review: " + (x.NextReview.Length > 0 ? x.NextReview : "none set (only a big event calls one)") + "." : "No watch set." };
            // the plan's check: names it gives that no longer fit, orders it never mentions, watches it never names
            var check = new StringBuilder();
            var findings = new List<string>();
            string Plain(string s) => (s ?? "").ToLowerInvariant();
            string plan = Plain(planText);
            var heldNames = Items(x, "captives").SelectMany(it => Ids(it).Where(id => id.StartsWith("lord", StringComparison.Ordinal) || id.StartsWith("Character", StringComparison.Ordinal)).Select(id => CaptiveName(T(it), id, names))).ToList();
            foreach (string n in heldNames.Where(n => n.Length >= 3 && plan.Contains(Plain(n)))) findings.Add("names " + n + ", who is held by the enemy");
            var ownNow = pages.Where(kv => kv.Key.StartsWith("settlements/", StringComparison.Ordinal) && kv.Key != "settlements/index" && kv.Key != "settlements/by-front").Select(kv => kv.Value.Title.Split(" [")[0]).ToHashSet(StringComparer.Ordinal);
            foreach (string n in _s.FormerHoldings.Where(n => !ownNow.Contains(n) && n.Length >= 3 && plan.Contains(Plain(n)))) findings.Add("names " + n + ", no longer yours");
            var ordered = pages.Where(kv => kv.Key.StartsWith("lords/", StringComparison.Ordinal) && kv.Value.Body.Contains("Your order: ")).Select(kv => kv.Value.Title.Replace("Army of ", "").Split(" [")[0]).ToList();
            List<string> unmentioned = ordered.Where(n => n.Length >= 3 && !plan.Contains(Plain(n))).ToList();
            List<string> loose = names2.Where(n => !plan.Contains(Plain(n))).ToList();
            check.AppendLine(findings.Count > 0 ? "Names in your plan that no longer fit:\n" + string.Join("\n", findings.Select(f => "- " + f)) : "Every lord and place your plan names still fits.");
            check.AppendLine().AppendLine(unmentioned.Count > 0 ? "Lords holding your orders whom no plan page names (" + unmentioned.Count + "): " + string.Join(", ", unmentioned.Take(60)) + "." : "Every lord holding your orders is named in your plan.");
            check.AppendLine().AppendLine(loose.Count > 0 ? "Watches no plan page names: " + string.Join(", ", loose) + "." : names2.Count > 0 ? "Every watch is named in your plan." : "You have no watches.");
            pages["plan/plan-check"] = new Page { Title = "Your plan, checked against the war", Desc = "kept by the game: names that no longer fit, orders and watches your plan does not name", Body = check.ToString() };
            facts.Add(new Fact { Key = "plancheck", Value = Hash(string.Join("|", findings)), Page = "plan/plan-check", Weight = 2, Say = (o, n) => o != null && findings.Count > 0 ? "your plan " + string.Join("; ", findings.Take(3)) : null });
            if (x.WriteNotes.Count > 0) _s.LastWrites = x.WriteNotes.ToList();
        }

        // ---------- chronicle: the record ----------

        private void BuildChronicle(ShelfInput x, SortedDictionary<string, Page> pages, List<Fact> facts)
        {
            // the last day in one page, the two before it in another: each small enough to read whole
            List<ChronicleLine> day = x.Chronicle.Where(l => double.IsNaN(x.Hours) || x.Hours - l.Hours <= 24).OrderByDescending(l => l.Hours).ToList();
            List<ChronicleLine> before = double.IsNaN(x.Hours) ? new List<ChronicleLine>() : x.Chronicle.Where(l => x.Hours - l.Hours > 24 && x.Hours - l.Hours <= 72).OrderByDescending(l => l.Hours).ToList();
            pages["chronicle/record"] = new Page
            {
                Title = "The record, the last day", Desc = "kept by the game as things happen: every line your realm was told or saw, and your orders with what became of them, the last 24 in-game hours, newest first (the two days before: record-earlier)",
                Body = day.Count > 0 ? string.Join("\n", day.Take(300).Select(l => "- " + l.Date + " · " + l.Text)) + (day.Count > 300 ? "\n- (" + (day.Count - 300) + " older lines of the day: ask chronicle about a lord, holding or realm)" : "") : "Nothing recorded in the last day."
            };
            pages["chronicle/record-earlier"] = new Page
            {
                Title = "The record, the two days before", Desc = "the record from 24 to 72 in-game hours ago, newest first",
                Body = before.Count > 0 ? string.Join("\n", before.Take(300).Select(l => "- " + l.Date + " · " + l.Text)) + (before.Count > 300 ? "\n- (" + (before.Count - 300) + " older lines: ask chronicle about a lord, holding or realm)" : "") : "Nothing recorded in those days."
            };
            int total = x.Chronicle.Count;
            facts.Add(new Fact { Key = "record", Value = total.ToString(CultureInfo.InvariantCulture), Page = "chronicle/record", Weight = 1, Say = (o, n) => o != null && D(n) > D(o) ? (D(n) - D(o)) + " new line" + (D(n) - D(o) == 1 ? "" : "s") + " in the record" : null });
        }

        // ---------- writing the shelf ----------

        private void Write(ShelfInput x, SortedDictionary<string, Page> pages, List<ShelfChange> all)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var byPage = all.GroupBy(c => c.File + "/" + c.Page).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Seq).ToList(), StringComparer.Ordinal);
            int written = 0;
            foreach (KeyValuePair<string, Page> kv in pages)
            {
                var sb = new StringBuilder();
                sb.Append("# ").AppendLine(kv.Value.Title);
                sb.AppendLine((kv.Value.Agent ? "" : "As of " + x.Date + ". ") + (kv.Value.Desc.Length > 0 && !kv.Value.Plain ? Cap(kv.Value.Desc) + "." : ""));
                if (byPage.TryGetValue(kv.Key, out List<ShelfChange> cs))
                {
                    List<ShelfChange> recent = cs.Where(c => double.IsNaN(x.Hours) || double.IsNaN(c.Hours) || x.Hours - c.Hours <= 24).Take(12).ToList();
                    if (recent.Count > 0) { sb.AppendLine("Changed in the last day:"); foreach (ShelfChange c in recent) sb.AppendLine("- ▲ " + c.Date + ": " + c.Text); }
                }
                sb.AppendLine();
                sb.AppendLine(kv.Value.Body.TrimEnd());
                texts[kv.Key] = sb.ToString();
            }
            // each folder's contents and changes
            foreach (string file in Files)
            {
                List<KeyValuePair<string, Page>> mine = pages.Where(kv => kv.Key.StartsWith(file + "/", StringComparison.Ordinal)).ToList();
                List<ShelfChange> fc = all.Where(c => c.File == file).OrderByDescending(c => c.Seq).ToList();
                var con = new StringBuilder();
                con.AppendLine("# " + Cap(file) + ": contents").AppendLine("As of " + x.Date + ". " + Cap(About[file]) + ".").AppendLine();
                con.AppendLine("Pages (read one as files/" + file + "/<page>.md, or ask open; ▲ = changed since you last read it):");
                foreach (KeyValuePair<string, Page> p in mine.OrderBy(p => Order(p.Key)).ThenBy(p => p.Key, StringComparer.Ordinal))
                {
                    string page = p.Key.Substring(file.Length + 1);
                    ShelfChange latest = byPage.TryGetValue(p.Key, out List<ShelfChange> pc) ? pc.FirstOrDefault(NewToYou) : null;
                    con.AppendLine("- " + page + ": " + Short(p.Value.Desc, 150) + (latest != null ? " ▲ " + latest.Date + ": " + Short(latest.Text, 120) : ""));
                }
                con.AppendLine().AppendLine("Changes: files/" + file + "/changes.md (the last three days, newest first; NEW = not yet seen by you).");
                texts[file + "/contents"] = con.ToString();
                var ch = new StringBuilder();
                ch.AppendLine("# " + Cap(file) + ": changes").AppendLine("As of " + x.Date + ". The last three in-game days, newest first; NEW = changed since you last read its page or this list.").AppendLine();
                if (fc.Count == 0) ch.AppendLine("No changes in the last three days.");
                foreach (ShelfChange c in fc.Take(300)) ch.AppendLine("- " + (NewToYou(c) ? "NEW " : "") + c.Date + " · " + c.Page + ": " + c.Text);
                texts[file + "/changes"] = ch.ToString();
            }
            // the shelf
            var sh = new StringBuilder();
            sh.AppendLine("# Shelf: " + x.RealmName + ", as of " + x.Date + (x.Poke >= 0 ? " (wake #" + x.Poke + ")" : ""));
            if (_s.Sections.TryGetValue("THE CLOCK", out string lag) && lag.Length > 0) sh.AppendLine(lag);
            sh.AppendLine("Wakes this in-game day: " + x.WakesToday + " (keep to about " + x.WakeBudget + "). Next strategy review: " + (x.NextReview.Length > 0 ? x.NextReview : "none set") + ".");
            sh.AppendLine();
            sh.AppendLine("New to you since you last read each page (the weightiest first; every change is on its folder's changes page and at the top of its page):");
            foreach (string file in Files)
            {
                List<ShelfChange> news = all.Where(c => c.File == file && NewToYou(c)).OrderByDescending(c => c.Weight).ThenByDescending(c => c.Seq).ToList();
                sh.AppendLine("- " + file + (news.Count > 0 ? " (▲ " + news.Count + "): " + string.Join("; ", news.Take(6).Select(c => (c.Page != "index" ? c.Page + ": " : "") + Short(c.Text, 110))) + (news.Count > 6 ? "; and " + (news.Count - 6) + " more" : "") : ": nothing new"));
            }
            if (_s.LastWrites.Count > 0) sh.AppendLine().AppendLine("Your last answer's pages: " + string.Join("; ", _s.LastWrites) + ".");
            sh.AppendLine();
            sh.AppendLine("Your files: " + string.Join("; ", Files.Select(f => f + " (" + About[f] + ")")) + ".");
            sh.AppendLine("Each folder has contents.md (its pages) and changes.md. Read any page as files/<folder>/<page>.md, or ask open, find or history.");
            texts["shelf"] = sh.ToString();
            // write what changed; take away pages of the game's folders that are gone (a holding lost, a lord taken)
            var keep = new HashSet<string>(texts.Keys, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in texts)
            {
                string path = Path.Combine(Dir, kv.Key.Replace('/', Path.DirectorySeparatorChar) + ".md");
                string h = Hash(kv.Value);
                if (File.Exists(path) && _hashes.TryGetValue(kv.Key, out string old) && old == h) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string tmp = path + ".part";
                    File.WriteAllText(tmp, kv.Value, new UTF8Encoding(false));
                    File.Move(tmp, path, true);
                    _hashes[kv.Key] = h;
                    written++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (string file in Files)
            {
                string d = Path.Combine(Dir, file);
                if (!Directory.Exists(d)) continue;
                foreach (string f in Directory.GetFiles(d, "*.md"))
                {
                    string key = file + "/" + Path.GetFileNameWithoutExtension(f);
                    if (keep.Contains(key)) continue;
                    try { File.Delete(f); _hashes.Remove(key); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            Pages = texts.Where(kv => kv.Key != "shelf").ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            Pages["shelf"] = texts["shelf"];
            LastWritten = written;
        }

        private readonly Dictionary<string, string> _hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        private static int Order(string key)
        {
            string p = key.Substring(key.IndexOf('/') + 1);
            string[] first = { "index", "the-plan", "by-front", "in-sum", "record", "the-war-so-far", "sightings", "order-results", "watches", "plan-check", "lessons", "handover" };
            int i = Array.IndexOf(first, p);
            return i >= 0 ? i : 100;
        }

        // ---------- open, find, history ----------

        /// <summary>Pages by their names, whole, as the last build wrote them (open); each read is marked. A name may be a folder (its contents) or "shelf".</summary>
        public string Open(IEnumerable<string> names)
        {
            var sb = new StringBuilder();
            foreach (string raw in names.Where(n => !string.IsNullOrWhiteSpace(n)).Take(12))
            {
                string p = PageOf(raw.Trim()) ?? raw.Trim().ToLowerInvariant();
                if (Files.Contains(p)) p += "/contents";
                if (!Pages.TryGetValue(p, out string text))
                {
                    // a page by a name or id it holds
                    string hit = Pages.Keys.FirstOrDefault(k => k.EndsWith("/" + Slug(raw), StringComparison.Ordinal)) ?? Pages.FirstOrDefault(kv => kv.Value.Contains("[" + raw.Trim() + "]") && !kv.Key.EndsWith("/index", StringComparison.Ordinal) && !kv.Key.EndsWith("/contents", StringComparison.Ordinal) && !kv.Key.EndsWith("/changes", StringComparison.Ordinal) && !kv.Key.EndsWith("/by-front", StringComparison.Ordinal) && kv.Value.StartsWith("# ", StringComparison.Ordinal) && kv.Value.Substring(0, Math.Min(200, kv.Value.Length)).Contains("[" + raw.Trim() + "]")).Key;
                    if (hit == null) { sb.AppendLine("== " + raw + ": no such page (files/<folder>/contents.md lists each folder's pages)").AppendLine(); continue; }
                    p = hit;
                    text = Pages[p];
                }
                MarkRead(p);
                sb.AppendLine("== files/" + p + ".md").AppendLine(text.TrimEnd()).AppendLine();
            }
            return sb.Length > 0 ? sb.ToString() : "Name one page or more (\"settlements/hiltbolt\", \"lords/index\", \"plan\").";
        }

        /// <summary>The lines of every page that hold all the words given, by page (find).</summary>
        public string Find(string words)
        {
            string[] w = (words ?? "").ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0) return "Give a word or more to find.";
            var sb = new StringBuilder();
            int hits = 0;
            foreach (KeyValuePair<string, string> kv in Pages.Where(kv => !kv.Key.EndsWith("/changes", StringComparison.Ordinal) && kv.Key != "shelf").OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var lines = kv.Value.Split('\n').Where(l => { string ll = l.ToLowerInvariant(); return w.All(ll.Contains); }).Take(4).ToList();
                if (lines.Count == 0) continue;
                sb.AppendLine("files/" + kv.Key + ".md:");
                foreach (string l in lines) sb.AppendLine("  " + Short(l.Trim(), 240));
                if (++hits >= 30) { sb.AppendLine("(more pages hold them: give more words)"); break; }
            }
            return hits > 0 ? sb.ToString() : "No page holds all of: " + string.Join(" ", w) + ".";
        }

        /// <summary>The old versions of one of the agent's pages (history): the list, newest first, or one whole by its number.</summary>
        public static string History(string memoryDir, string page, int version)
        {
            string p = (page ?? "").Replace('\\', '/').Trim().Trim('/').ToLowerInvariant();
            if (p.EndsWith(".md", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 3);
            if (p.StartsWith(DirName + "/", StringComparison.Ordinal)) p = p.Substring(DirName.Length + 1);
            string dir = Path.Combine(memoryDir ?? "", "history", p.Replace('/', Path.DirectorySeparatorChar));
            if (p.Length == 0 || !Directory.Exists(dir))
            {
                string root = Path.Combine(memoryDir ?? "", "history");
                if (!Directory.Exists(root)) return "No old versions are kept yet.";
                var all = Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Where(d => Directory.GetFiles(d, "*.md").Length > 0)
                    .Select(d => Path.GetRelativePath(root, d).Replace('\\', '/') + " (" + Directory.GetFiles(d, "*.md").Length + ")").OrderBy(s => s, StringComparer.Ordinal).ToList();
                return (p.Length > 0 ? "No old versions of " + p + ". " : "") + (all.Count > 0 ? "Pages with old versions: " + string.Join(", ", all) + "." : "No old versions are kept yet.");
            }
            List<string> files = Directory.GetFiles(dir, "*.md").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
            if (version > 0)
            {
                if (version > files.Count) return p + " has " + files.Count + " old version" + (files.Count == 1 ? "" : "s") + ".";
                return File.ReadAllText(files[version - 1]);
            }
            var sb = new StringBuilder();
            sb.AppendLine("Old versions of " + p + ", newest first (ask history with its number for one whole):");
            for (int i = 0; i < files.Count && i < 60; i++)
            {
                string first = File.ReadLines(files[i]).FirstOrDefault() ?? "";
                sb.AppendLine((i + 1) + ". " + first.TrimStart('#', ' '));
            }
            return sb.ToString();
        }

        // ---------- small helpers ----------

        private static string Gone(string key, string value)
        {
            string name = value.Contains('|') ? value.Substring(0, value.IndexOf('|')) : value;
            if (key.StartsWith("lord:", StringComparison.Ordinal)) return name + " is no longer among your lords in the field (taken, killed, gone from the realm, or now in an army)";
            if (key.StartsWith("holding:", StringComparison.Ordinal)) return name + " is no longer yours";
            if (key.StartsWith("watch:", StringComparison.Ordinal)) return "watch " + name + " ended";
            return null;
        }

        private static string GonePage(string key)
        {
            if (key.StartsWith("lord:", StringComparison.Ordinal)) return "lords/index";
            if (key.StartsWith("holding:", StringComparison.Ordinal)) return "settlements/index";
            if (key.StartsWith("watch:", StringComparison.Ordinal)) return "plan/watches";
            return "shelf/index";
        }

        private static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

        private static bool Moved(string o, string n, double share, double least)
        {
            if (o == null) return false;
            double a = D(o), b = D(n);
            return Math.Abs(b - a) >= least && Math.Abs(b - a) > share * Math.Max(Math.Abs(a), 1);
        }

        /// <summary>A number falling to or below a mark (3, then 1).</summary>
        private static bool Crossed(double o, double n, double first, double second) => (o > first && n <= first) || (o > second && n <= second);

        /// <summary>The enemy near a place appearing, doubling, halving or going.</summary>
        private static bool Swing(string o, string n)
        {
            if (o == null) return false;
            double a = D(o), b = D(n);
            if (a == b) return false;
            if (a < 1 && b >= 100) return true;
            if (b < 1 && a >= 100) return true;
            return a >= 100 && (b >= 2 * a || b <= a / 2);
        }

        internal static string Slug(string name)
        {
            string s = (name ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                char l = char.ToLowerInvariant(c);
                if (l >= 'a' && l <= 'z' || l >= '0' && l <= '9') sb.Append(l);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string r = sb.ToString().Trim('-');
            if (r.Length > 48) r = r.Substring(0, 48).Trim('-');
            return r.Length > 0 ? r : "page";
        }

        private static string Unique(HashSet<string> used, string slug, string id)
        {
            string s = slug;
            if (!used.Add(s)) { s = slug + "-" + Slug(id); used.Add(s); }
            return s;
        }

        private static string Short(string s, int n)
        {
            s = (s ?? "").Replace("\n", " ").Trim();
            return s.Length <= n ? s : s.Substring(0, n - 1).TrimEnd() + "…";
        }

        private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        internal static string Hash(string s)
        {
            byte[] b = SHA1.HashData(Encoding.UTF8.GetBytes(s ?? ""));
            return Convert.ToHexString(b, 0, 6);
        }
    }
}
