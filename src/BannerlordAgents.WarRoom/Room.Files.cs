using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// A files agent (war room 0.20, Nov 20; docs/FILES.md): its messages say only what woke it, a review only that it sits, a new conversation only that it begins and why;
    /// everything else is in its realm's files, which the war room builds before each call from the game's question book of the hour, and which the agent reads by path or
    /// asks for. Its answers write its own pages (plan, notes, intelligence); a chronicler writes the chronicle's days in quiet moments.
    /// </summary>
    internal sealed partial class Room
    {
        private Shelf _shelf;
        private List<string> _lastWriteNotes = new List<string>();
        private (int reads, int steps, int opens, List<string> pages) _lastReads = (0, 0, 0, new List<string>());
        private double _lastShelfMs;
        private int _lastShelfChanges;
        /// <summary>During a wake's build: today's wakes with this one counted (its shelf says so); -1 otherwise.</summary>
        private int _wakesTodayForShelf = -1;

        internal string ContextMode => _c.Files ? "files" : "report";
        internal Shelf ShelfNow => _shelf;

        /// <summary>The order library's place in a files agent's instructions: a pointer to its Orders file.</summary>
        internal const string FilesOrdersPointer =
            "YOUR ORDERS\n\nEvery order you can give, ours and your own, with its settings, its rules and examples, is in your Orders file: files/orders/index.md lists them, one line each, and each has its page "
            + "(files/orders/<name>.md; its whole definition is tools/<name>.json). The settings every order takes (caution, join_hours, garrison_floor, food, riding to relieve a fight) are on "
            + "files/orders/common-settings.md, and how to write and check your own orders on files/orders/writing-your-own.md. Read an order's page before you first use it in a conversation, "
            + "or whenever you are unsure of a setting. You may keep as many orders of your own as you like.";

        /// <summary>A files agent's instructions: each "## " section of warroom-files.md over the continuous instructions (as they overlay warroom-system.md).</summary>
        internal string FilesInstructions(string template)
        {
            if (!File.Exists(_c.FilesTemplate))
            {
                if (!_filesMissingSaid) { Say("No warroom-files.md in " + _c.AgentDir + ": a files agent's instructions still describe the reports (scripts\\setup-agent.ps1 installs it)."); _filesMissingSaid = true; }
                return template;
            }
            return Overlay(template.Replace("\r", ""), File.ReadAllText(_c.FilesTemplate)).Replace("{files}", Path.Combine(_c.ChannelDir, Shelf.DirName));
        }

        private bool _filesMissingSaid;

        /// <summary>One section of the instructions as the continuous agent has them (before the files overlay): the manual for writing tools, the tool check.</summary>
        private string TemplateSection(string heading)
        {
            try
            {
                string t = File.Exists(_c.SystemTemplate) ? File.ReadAllText(_c.SystemTemplate).Replace("\r", "") : "";
                if (_c.Continuous) t = ContinuousInstructions(t);
                foreach (KeyValuePair<string, string> s in NewsSections(t))
                    if (s.Key == heading) return (s.Key.Substring(3) + "\n\n" + s.Value.Trim()).Trim();
            }
            catch (IOException) { }
            return "";
        }

        private static JsonObject ReadJsonFile(string path)
        {
            try { return path != null && ReadShared(path) is string t ? JsonNode.Parse(t) as JsonObject : null; }
            catch (JsonException) { return null; }
        }

        /// <summary>
        /// The realm's files built afresh from a question book: a wake's (with its poke text), a review's or an open's (the newest hour, with the newest situation report and
        /// briefing). Null when this agent does not keep files. The shelf flags what changed since the last build.
        /// </summary>
        internal Shelf BuildShelf(PokeMeta meta, string bookPath, string pokeText, string reportText = null, string councilText = null, string why = "", string fate = null)
        {
            if (!_c.Files || _mem == null) return null;
            _shelf ??= new Shelf(_c.ChannelDir) { NoHumanPlayer = !_c.HumanPlayer, NoChancesPage = !_c.ChancesPage };
            JsonObject book = ReadJsonFile(bookPath);
            if (book == null && _shelf.Pages.Count > 0) return _shelf;   // nothing new to build from: the last build stands
            string campaign = meta?.Campaign ?? _mem.Campaign;
            string realm = meta?.Realm ?? (string)book?["realm"] ?? "";
            string loadId = meta?.LoadId ?? _mem.LoadId;
            double bookHours = book?["hours"] is JsonValue bhv && bhv.TryGetValue(out double bh0) ? bh0 : meta?.Hours ?? double.NaN;
            JsonObject view = ViewFor(campaign, realm, loadId, bookHours, out string stale);
            if (view != null) _lastView = view;
            // war room 0.22.0 (item 9): every build reads the realm's battles under way, so their measured edge is kept even when no battle question is asked
            if (view != null) RouteTool.RecordEdge(EdgeFile(), view);
            var x = new ShelfInput
            {
                Book = book, View = view, ViewStale = stale,
                PokeText = pokeText, ReportText = reportText, CouncilText = councilText, Poke = meta?.Number ?? -1,
                Campaign = campaign, LoadId = meta?.LoadId ?? _mem.LoadId, RealmName = meta?.RealmName ?? (string)book?["realm_name"] ?? realm,
                Date = (string)book?["date"] ?? meta?.Date ?? "", Hours = bookHours,
                Chronicle = Memory.ReadChronicle(_c.ChannelDir), AgentPages = _mem.PagesNow(),
                ToolsText = ReadShared(Path.Combine(_c.ChannelDir, "tools.txt")) ?? "", OwnToolsManual = TemplateSection("## Writing your own tools"), CheckManual = TemplateSection("## Checking a tool"),
                Why = why ?? "", Fate = fate, WakeBudget = _c.WakeBudget, WriteNotes = _lastWriteNotes.ToList(),
                NextReview = _talk != null && !double.IsNaN(_talk.NextReviewHours) ? "by " + Program.When(_talk.NextReviewHours) + " (the hour you set)" : "",
                // war room 0.22.0: when the book was written (the no-order filter, item 10), and the game's receipts since the last build (item 19)
                BookWrittenUtc = bookPath != null && File.Exists(bookPath) ? File.GetLastWriteTimeUtc(bookPath) : DateTime.MinValue,
                Receipts = NewReceipts()
            };
            _mem.View(out string story, out _, out string storyDate, out _, out _);
            if (story.StartsWith("HANDOVER", StringComparison.Ordinal)) { x.Handover = story; x.HandoverDate = storyDate; }
            if (_talk != null) x.WakesToday = _wakesTodayForShelf >= 0 ? _wakesTodayForShelf : _talk.WakesOn(x.Hours);
            try { _lastShelfChanges = _shelf.Build(x); }
            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidOperationException || e is UnauthorizedAccessException)
            {
                Say("The realm's files could not be built: " + e.Message);
            }
            _lastShelfMs = _shelf.LastBuildMs;
            _lastWriteNotes.Clear();
            if (_shelf.LastBuildMs > 3000) Say("The realm's files took " + (_shelf.LastBuildMs / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s to build.");
            return _shelf;
        }

        private long _shelfReceiptsMark = -1;

        /// <summary>
        /// The game's receipts written since the last build (war room 0.22.0, item 19), each line without its time, whole lines only; none at the first build (what came before is in
        /// the next wake's WHAT CHANGED).
        /// </summary>
        private List<string> NewReceipts()
        {
            var lines = new List<string>();
            string path = Path.Combine(_c.ChannelDir, "receipts.log");
            try
            {
                if (!File.Exists(path)) return lines;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (_shelfReceiptsMark < 0 || _shelfReceiptsMark > fs.Length) { _shelfReceiptsMark = fs.Length; return lines; }
                fs.Position = _shelfReceiptsMark;
                using var sr = new StreamReader(fs, new UTF8Encoding(false));
                string text = sr.ReadToEnd();
                int last = text.LastIndexOf('\n');
                if (last < 0) return lines;
                _shelfReceiptsMark += Encoding.UTF8.GetByteCount(text.Substring(0, last + 1));
                foreach (string raw in text.Substring(0, last + 1).Split('\n'))
                {
                    string l = System.Text.RegularExpressions.Regex.Replace(raw.TrimEnd('\r'), @"^\d\d:\d\d:\d\d\s+", "").Trim();
                    if (l.Length > 0) lines.Add(l);
                }
            }
            catch (IOException) { }
            return lines;
        }

        /// <summary>
        /// The realm's view file for a build (war room 0.20.1): only one of this load, no more than two in-game hours older than the book. The game writes it at the end of
        /// each hour's work, after that hour's wakes, so the first wake after a load found the previous session's (Nov 20: its first front page flagged "Hiltbolt: food 6 → 1
        /// days", the 6 from the session before). A stale one is asked of the game afresh (the fresh book writes the view too); still stale, the build goes without it and
        /// says why (stale), and the shelf takes its next build with a view as a new start rather than flag the view's facts as changes.
        /// </summary>
        private JsonObject ViewFor(string campaign, string realm, string loadId, double bookHours, out string stale)
        {
            string path = Path.Combine(RouteTool.DefaultRoot(), "live", campaign ?? "unknown", "view_" + realm + ".json");
            JsonObject v = ReadJsonFile(path);
            stale = ViewStaleness(v, loadId, bookHours);
            if (stale == null) return v;
            if (FreshNow() != null)
            {
                v = ReadJsonFile(path);
                stale = ViewStaleness(v, loadId, bookHours);
                if (stale == null) return v;
            }
            Say("The realm's files were built without the map view: " + stale + ".");
            return null;
        }

        /// <summary>Why a view file cannot serve a build of this load and hour, or null when it can.</summary>
        internal static string ViewStaleness(JsonObject v, string loadId, double bookHours)
        {
            if (v == null) return "no view file";
            string vl = (string)v["load_id"] ?? "";
            if (!string.IsNullOrEmpty(loadId) && vl.Length > 0 && vl != loadId) return "the view file is of another load (" + vl + ")";
            double vh = v["hours"] is JsonValue hv && hv.TryGetValue(out double h) ? h : double.NaN;
            if (!double.IsNaN(bookHours) && !double.IsNaN(vh) && vh < bookHours - 2) return "the view file is " + (bookHours - vh).ToString("0", CultureInfo.InvariantCulture) + " in-game hours older than the book";
            return null;
        }

        /// <summary>Before an open: the game's newest hour, and the shelf built again from it when it is newer than the last build.</summary>
        private void FilesRefresh()
        {
            if (_shelf == null) return;
            string fresh = FreshNow();
            if (fresh == null) return;
            JsonObject book = ReadJsonFile(fresh);
            double h = book?["hours"] is JsonValue hv && hv.TryGetValue(out double bh) ? bh : double.NaN;
            if (double.IsNaN(h) || !double.IsNaN(_shelf.Hours) && h <= _shelf.Hours + 1e-6) return;
            BuildShelf(ReadMeta(-1), fresh, null);
        }

        /// <summary>A files agent's wake: what woke it, word for word, and nothing else.</summary>
        internal static string FilesWake(int number, string why)
        {
            var sb = new StringBuilder();
            sb.Append("WAKE #" + number + " (answer with one JSON object that says \"poke\": " + number + ")").Append('\n');
            foreach (string l in (why ?? "").Replace("\r", "").Split('\n'))
            {
                string t = l.TrimEnd();
                if (t.Length == 0 || t.StartsWith("WHY YOU ARE ASKED NOW", StringComparison.Ordinal)) continue;
                sb.Append(t.StartsWith("-", StringComparison.Ordinal) ? t : "- " + t).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>A files agent's new conversation: that it begins, and why.</summary>
        internal string FilesOpening()
        {
            string why = string.IsNullOrWhiteSpace(_talk?.NextWhy) ? "the first of your realm's conversations" : _talk.NextWhy.Trim().TrimEnd('.');
            return "A NEW CONVERSATION BEGINS: " + why + ". Answer this first message as a strategy review.\n\n";
        }

        /// <summary>A files agent's strategy review: that it sits, and why.</summary>
        internal static string FilesReview(int number, string why) =>
            "STRATEGY REVIEW (answer with one JSON object that says \"poke\": " + number + "): " + (why ?? "").Trim().TrimEnd('.') + ".\n";

        /// <summary>A files agent's handover request.</summary>
        internal static string FilesHandoverPrompt(string why, string date) =>
            "WRITE YOUR HANDOVER NOW: " + why.TrimEnd('.') + ". This conversation ends with it, and your next one begins from your files, so first bring your own pages up to date "
            + "(your plan, notes and intelligence: anything you know that they do not hold yet). Then write your handover: only what your files do not hold (what you were about to do and why, "
            + "what you are unsure of, anything half done), in your own voice, beginning with the word HANDOVER and the date (" + date + "). No orders: your orders in force and your watches carry on. "
            + "Reply with ONLY one JSON object: {\"pages\": [{\"page\": \"plan/...\", \"text\": \"...\", \"why\": \"...\"}], \"handover\": \"HANDOVER " + date + " ...\"}";

        /// <summary>A files agent's handover from its answer: its pages written, and the handover's text (null when there is none).</summary>
        private string FilesHandover(string text, double hours, string date)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (string candidate in Objects(text).Reverse())
            {
                try
                {
                    if (JsonNode.Parse(candidate) is not JsonObject o || o["handover"] is not JsonValue hv || !hv.TryGetValue(out string h) || h.Trim().Length < 100) continue;
                    if (o["pages"] != null)
                    {
                        List<string> notes = _mem.ApplyPages(o, hours, date);
                        if (notes.Count > 0) Say("Handover: its pages: " + string.Join("; ", notes) + ".");
                    }
                    h = h.Trim();
                    return h.StartsWith("HANDOVER", StringComparison.Ordinal) ? h : "HANDOVER " + date + "\n" + h;
                }
                catch (JsonException) { }
            }
            return HandoverText(text);
        }

        /// <summary>
        /// A conversation of the other kind (begun on the reports, now on the files, or back) is not carried on: the next message begins a new one, told why. Returns the
        /// reload's note to carry, or null when the conversation was ended.
        /// </summary>
        private string SwitchContext(string carried)
        {
            if (_talk == null || !_talk.Started || (_talk.Context ?? "report") == ContextMode) return carried;
            Say("Continuous: this conversation began on the " + (_talk.Context ?? "report") + "s and this agent now learns the war from its " + (_c.Files ? "files" : "reports") + "; the next message begins a new conversation.");
            _talk.Reset(_c.Files ? "your realm's memory now lives in its files: each message says only what woke you, and you read what you need from your files" : "your messages bring the reports again");
            _mem.SessionId = null;
            _mem.SessionStarted = false;
            _mem.SessionTokens = 0;
            _mem.Save();
            return null;
        }

        /// <summary>"WarRoom shelf --dir (agent) [--out dir] [--again]": the files built from the newest poke into a folder of their own (twice with --again, to see what a second build flags), and each folder's pages with their sizes.</summary>
        internal int ShelfOnce(string outDir, bool again, int from = 0)
        {
            PokeMeta meta = ReadMeta(-1);
            if (meta == null) { Say("No poke.json in " + _c.ChannelDir + " yet."); return 1; }
            EnsureMemory(meta);
            _talk ??= Talk.Load(_mem.Dir);
            Directory.CreateDirectory(outDir);
            _shelf = new Shelf(outDir) { NoHumanPlayer = !_c.HumanPlayer, NoChancesPage = !_c.ChancesPage };
            string pokeText = ReadShared(Path.Combine(_c.ChannelDir, "poke.txt")) ?? "";
            string book = BookFor(meta, meta.Number) ?? NewestBook(meta, meta.Number);
            bool was = _c.Files;
            _c.Context = "files";
            try
            {
                if (from > 0)
                {
                    // an earlier wake's book first (its own text is gone), so the newest build flags what changed since
                    PokeMeta then = ReadMeta(-1);
                    then.Number = from;
                    BuildShelf(then, Path.Combine(_c.ChannelDir, "book", "poke-" + from + ".json"), null);
                    Say("Build from wake #" + from + ": " + _shelf.LastBuildMs.ToString("0", CultureInfo.InvariantCulture) + " ms, " + _shelf.LastWritten + " pages written.");
                }
                for (int i = 0; i < (again ? 2 : 1); i++)
                {
                    BuildShelf(meta, book, pokeText, why: Section(pokeText, "WHY YOU ARE ASKED NOW"));
                    Say("Build " + (i + 1) + ": " + _shelf.LastBuildMs.ToString("0", CultureInfo.InvariantCulture) + " ms, " + _shelf.LastChanges + " changes flagged, " + _shelf.LastWritten + " pages written, from " + book + ".");
                }
            }
            finally { if (!was) _c.Context = "report"; }
            foreach (IGrouping<string, KeyValuePair<string, string>> g in _shelf.Pages.GroupBy(kv => kv.Key.Contains('/') ? kv.Key.Substring(0, kv.Key.IndexOf('/')) : kv.Key).OrderBy(g => g.Key, StringComparer.Ordinal))
                Say("  " + g.Key + ": " + g.Count() + " pages, " + g.Sum(kv => kv.Value.Length).ToString("N0", CultureInfo.InvariantCulture) + " characters (largest " + g.OrderByDescending(kv => kv.Value.Length).First().Key + ", " + g.Max(kv => kv.Value.Length).ToString("N0", CultureInfo.InvariantCulture) + ")");
            Say("Written to " + _shelf.Dir);
            // the files agent's instructions as they would be composed, and a wake's message
            _c.Context = "files";
            try
            {
                string sys = SystemFile(true, false, true, true);
                Say("Files instructions: " + new FileInfo(sys).Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes (" + sys + ").");
                Say("A wake's message:" + Environment.NewLine + FilesWake(meta.Number, Section(pokeText, "WHY YOU ARE ASKED NOW")));
            }
            finally { if (!was) _c.Context = "report"; }
            return 0;
        }

        /// <summary>After a files agent's call: what it read (each page marked read), for its record.</summary>
        private void AfterFilesCall(CallResult r)
        {
            if (_shelf == null || r == null) { _lastReads = (0, 0, 0, new List<string>()); return; }
            _lastReads = _shelf.TrackReads(r.Raw);
        }

        // ---------- the chronicler's pages ----------

        private const string DefaultChroniclerFiles =
            "# The chronicler\n\n"
            + "You keep the chronicle of a realm at war in a live campaign of Mount & Blade II: Bannerlord with the TAOM Lord of the Rings pack. Its ruler, another model, makes every decision "
            + "and reads your pages when it needs the war's history; you only remember.\n\n"
            + "You get the pages as you last wrote them (the war so far, the turning points, and what worked and failed of the realm's orders), the ruler's plan for context only "
            + "(never rewrite it), and the realm's record of one or more whole in-game days, oldest first: every line the realm was told or saw, and its orders with what became of them.\n\n"
            + "Write:\n"
            + "- one page for each day given: what happened that day, from its record, in plain sentences in the past tense, at most 250 words: battles and who won, sieges begun and how they "
            + "ended, holdings won or lost, lords taken or freed, armies formed and broken, enemy armies seen, and the realm's orders that mattered and what became of them;\n"
            + "- the war so far, at most 450 words: the oldest events most compressed, the last days in most detail, with the dates of turning points;\n"
            + "- the turning points: one dated line each, oldest first, at most 25 lines;\n"
            + "- our orders, what worked and what failed: at most 250 words, with the evidence.\n"
            + "Only facts from the record and your pages: never invent, never guess numbers, never give advice. Names as they appear, with their realm the first time.\n\n"
            + "Reply with ONLY one JSON object: {\"days\": {\"<the day as given>\": \"...\"}, \"the_war_so_far\": \"...\", \"turning_points\": \"...\", \"our_orders\": \"...\"}\n";

        private string _chroniclerFilesFile;

        private string ChroniclerFilesFile()
        {
            string path = Path.Combine(_c.AgentDir, "chronicler-files.md");
            string text = File.Exists(path) ? File.ReadAllText(path) : DefaultChroniclerFiles;
            string file = Path.Combine(_logDir, "chronicler-files-" + Hash(text) + ".md");
            if (_chroniclerFilesFile != file || !File.Exists(file)) { File.WriteAllText(file, text); _chroniclerFilesFile = file; }
            return file;
        }

        /// <summary>The whole in-game days of the record the chronicler has not written yet (the last two only, the first time), oldest first, at most three; null when none is due.</summary>
        internal List<List<ChronicleLine>> DueChronicleDays(double nowHours)
        {
            if (_mem == null || double.IsNaN(nowHours)) return null;
            double through = _mem.ChronicleThrough();
            double today = Math.Floor(nowHours / 24) * 24;
            if (double.IsNaN(through)) through = today - 48;
            List<List<ChronicleLine>> days = Memory.ReadChronicle(_c.ChannelDir).Where(l => l.Hours > through + 1e-6 && l.Hours < today - 1e-6)
                .GroupBy(l => Math.Floor(l.Hours / 24)).OrderBy(g => g.Key).Select(g => g.ToList()).Take(3).ToList();
            return days.Count > 0 ? days : null;
        }

        private void ChronicleIfDue(CancellationToken stop)
        {
            if (!_c.Files || _mem == null || (_story != null && !_story.IsCompleted)) return;
            if ((DateTime.Now - _storyFailedAt).TotalMinutes < 5 || (DateTime.Now - _lastCallEnded).TotalSeconds < 2) return;
            if (RunFinished()) return;
            if ((DateTime.Now - _storyLooked).TotalSeconds < 10) return;
            _storyLooked = DateTime.Now;
            ReadClock();
            List<List<ChronicleLine>> due = DueChronicleDays(_pokeHours);
            if (due == null) return;
            _storyStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
            CancellationToken token = _storyStop.Token;
            Memory m = _mem;
            _story = Task.Run(() => WriteChronicle(m, due, token));
        }

        private static string DayOf(string date)
        {
            // "Autumn 2, 1087, 10:00" -> "Autumn 2, 1087"
            int last = (date ?? "").LastIndexOf(", ", StringComparison.Ordinal);
            return last > 0 && date.Substring(last + 2).Contains(':') ? date.Substring(0, last) : date ?? "";
        }

        /// <summary>
        /// The chronicler's pages from its answer: the last whole JSON object with "days", else the answer's one object closed where it ends (war room 0.20.1, Nov 20:
        /// Dol Guldur's chronicler once wrote every page and left off the last closing bracket; the answer was thrown away and the days written again five minutes later).
        /// A page the answer put inside "days" by the same slip is taken as the page it is.
        /// </summary>
        internal static Dictionary<string, string> ChroniclerPages(string text, out bool mended)
        {
            mended = false;
            var written = new Dictionary<string, string>(StringComparer.Ordinal);
            var candidates = Objects(text ?? "").Reverse().ToList();
            string closed = CloseOpen(text ?? "");
            if (closed != null) candidates.Add(closed);
            foreach (string candidate in candidates)
            {
                try
                {
                    if (JsonNode.Parse(candidate) is not JsonObject o || o["days"] is not JsonObject dj) continue;
                    var top = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string k in new[] { "the_war_so_far", "turning_points", "our_orders" })
                        if ((o[k] ?? dj[k])?.ToString() is string v && v.Trim().Length > 0) top[k] = v;
                    foreach (KeyValuePair<string, JsonNode> kv in dj)
                        if (!top.ContainsKey(kv.Key) && kv.Value is JsonValue && (kv.Value.ToString() ?? "").Trim().Length > 0) written["day-" + kv.Key] = kv.Value.ToString();
                    if (top.TryGetValue("the_war_so_far", out string ws)) written["the-war-so-far"] = ws;
                    if (top.TryGetValue("turning_points", out string tps)) written["turning-points"] = tps;
                    if (top.TryGetValue("our_orders", out string ord)) written["our-orders-worked-and-failed"] = ord;
                    mended = ReferenceEquals(candidate, closed);
                    break;
                }
                catch (JsonException) { written.Clear(); }
            }
            return written;
        }

        /// <summary>The chronicler's call: its pages as they stand, the ruler's plan for context, and the record of the days due; its new pages kept in the memory.</summary>
        internal bool WriteChronicle(Memory m, List<List<ChronicleLine>> days, CancellationToken cancel = default)
        {
            DateTime began = DateTime.Now;
            Dictionary<string, PageRec> pages = m.PagesNow();
            string P(string k) => pages.TryGetValue(k, out PageRec p) ? p.Text : "";
            m.View(out string story, out _, out _, out _, out _);
            string sofar = P("chronicle/the-war-so-far");
            if (sofar.Length == 0 && story.Length > 0 && !story.StartsWith("HANDOVER", StringComparison.Ordinal)) sofar = story;
            var sb = new StringBuilder();
            sb.AppendLine("THE WAR SO FAR (as you last wrote it)").AppendLine(sofar.Length > 0 ? sofar : "(none yet)").AppendLine();
            sb.AppendLine("TURNING POINTS (as you last wrote them)").AppendLine(P("chronicle/turning-points") is string tp && tp.Length > 0 ? tp : "(none yet)").AppendLine();
            sb.AppendLine("OUR ORDERS, WHAT WORKED AND WHAT FAILED (as you last wrote it)").AppendLine(P("chronicle/our-orders-worked-and-failed") is string oo && oo.Length > 0 ? oo : "(none yet)").AppendLine();
            sb.AppendLine("THE RULER'S PLAN (for context only: do not rewrite it)");
            foreach (KeyValuePair<string, PageRec> kv in pages.Where(kv => kv.Key.StartsWith("plan/", StringComparison.Ordinal))) sb.AppendLine("[" + kv.Key + "] " + kv.Value.Text);
            sb.AppendLine();
            var dayNames = new List<string>();
            foreach (List<ChronicleLine> d in days)
            {
                string day = DayOf(d[0].Date);
                dayNames.Add(day);
                sb.AppendLine("THE RECORD OF " + day + " (" + d.Count + " lines, oldest first)");
                foreach (ChronicleLine l in d.Take(400)) sb.AppendLine("- " + l.Date + " · " + l.Text);
                sb.AppendLine();
            }
            sb.AppendLine("Write a page for each day given (" + string.Join("; ", dayNames) + "), the war so far, the turning points and our orders. Reply with ONLY the JSON object.");
            string model = string.IsNullOrWhiteSpace(_c.StoryModel) ? _c.Model : _c.StoryModel;
            string folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-chronicle");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), sb.ToString());
            Say("Chronicle: the chronicler writes " + string.Join(", ", dayNames) + "...");
            CallResult r = Guarded("chronicle", () => Claude.Run(_claude, ChroniclerFilesFile(), sb.ToString(), model, _c.StoryEffort, _c.ChannelDir, false, Math.Max(_c.TimeoutSeconds, 300), _c.StoryThinkingCap, cancel: cancel));
            File.WriteAllText(Path.Combine(folder, "raw.jsonl"), r.Raw ?? "");
            string problem = r.Ok ? null : r.Error;
            var written = new Dictionary<string, string>(StringComparer.Ordinal);
            if (r.Ok)
            {
                written = ChroniclerPages(r.Text, out bool mended);
                if (written.Count == 0) problem = "no pages in its answer";
                else if (mended) Say("Chronicle: its answer lacked a closing bracket; mended and kept.");
            }
            bool kept = false;
            if (written.Count > 0)
            {
                ChronicleLine last = days[days.Count - 1][days[days.Count - 1].Count - 1];
                double through = (Math.Floor(last.Hours / 24) + 1) * 24 - 1e-3;
                m.SetChroniclePages(written, last.Hours, DayOf(last.Date), through);
                m.Save();
                m.Snapshot(double.IsNaN(m.LastHours) ? last.Hours : Math.Max(m.LastHours, last.Hours), last.Date, m.LastPoke, "chronicle");
                kept = true;
            }
            double seconds = (DateTime.Now - began).TotalSeconds;
            Log(new
            {
                kind = "chronicle", at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), model, effort = _c.StoryEffort, ok = kept, problem, days = dayNames,
                lines = days.Sum(d => d.Count), pages = written.Keys.ToList(), seconds = Math.Round(seconds, 2), tokens_in = r.InTokens, tokens_out = r.OutTokens, cache_read = r.CacheRead,
                cache_write = r.CacheWrite, cost_usd_list = r.Cost, campaign = m.Campaign, folder,
                // war room 0.22.0 (item 40): the chronicler's calls carry the run too, so the archive and the costs hold them
                run = RunTag(null, out string chArm), arm = chArm, realm = ReadMeta(-1)?.Realm
            });
            if (kept) Say("Chronicle: written (" + string.Join(", ", written.Keys) + ") in " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s.");
            else if (!cancel.IsCancellationRequested) { _storyFailedAt = DateTime.Now; Say("Chronicle: not written (" + problem + "); tried again in five minutes."); }
            return kept;
        }
    }
}
