using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>What poke.json says about a poke: its in-game hour, the campaign and realm, and this load of the game.</summary>
    internal sealed class PokeMeta
    {
        public int Number = -1;
        public double Hours = double.NaN, LoadedAt = double.NaN;
        public string Date = "", Campaign, Realm, RealmName, LoadId, LoadedAtDate = "";
        /// <summary>The save's history, as the bridge found it (since 0.9.1): the game sessions its world lived through, each up to the hour it was saved at; null from an older bridge.</summary>
        public List<KeyValuePair<string, double>> Before;
        public string HistoryFound = "";
        /// <summary>The test run the poke belongs to, and its arm (0.13, the training record's tags); null outside a run or from an older bridge.</summary>
        public string Run, Arm;
        /// <summary>
        /// A news wake (0.14, docs/PLAN-news.md): the bridge's news mode for the agent (full, shadow, theatre), the theatre's recipe (theatre,
        /// whole, realm), the names and ids in it (the notes on enemies and the chronicle lines are cut to them), and its question book.
        /// </summary>
        public string NewsMode = "full", Recipe, Book;
        public List<string> Names = new List<string>();
        /// <summary>The realms at war with it and its allies: a note on a whole realm is kept in every news wake.</summary>
        public List<string> Realms = new List<string>();
        public bool Theatre => NewsMode == "theatre";
        /// <summary>Only what bears on the news is shown: the notes and the chronicle lines are cut to it too.</summary>
        public bool Focused => Theatre && Recipe == "theatre";
    }

    /// <summary>
    /// The live loop. The bridge writes each poke into the channel (poke.json, poke.txt, and a line in pokes.log). When a
    /// poke is newer than the last one answered and no call is running, the war room answers the newest poke with one
    /// call: the agent's instructions and the order library as the system prompt (the same every time, so it stays
    /// cached), and as the message the realm's memory (docs/MEMORY.md) and the poke. Pokes that come while a call runs
    /// are not lost: the next poke says what changed since the last answer. The answer's orders go into
    /// orders\poke-N.json, its plan, enemies and lessons into the notes, and every call is logged with its time and tokens.
    ///
    /// Memory, "written" (the default): every call is fresh, and its message starts with the story so far, the agent's
    /// notes, and the chronicle lines the story does not cover yet (the last two days are in the report itself). In a
    /// quiet moment, once enough new lines have come, the chronicler rewrites the story so far, beside the answers and
    /// never in their way. "conversation": one long conversation, resumed every poke, which starts again from the notes
    /// when it grows past conversation_limit; kept to compare the two. With no new pokes it does nothing at all.
    /// </summary>
    internal sealed class Room
    {
        private readonly Config _c;
        private readonly string _claude;
        private readonly string _logDir;
        private int _lastAnswered = -1;
        private string _systemFile, _systemHash, _chroniclerFile, _chroniclerHash;
        private Memory _mem;
        private Task _story;
        private CancellationTokenSource _storyStop;
        private DateTime _storyFailedAt = DateTime.MinValue, _lastCallEnded = DateTime.MinValue, _storyLooked = DateTime.MinValue;
        private double _pokeHours = double.NaN;
        private static readonly object LogGate = new object();
        private readonly object _systemGate = new object();

        // the council of war: its thread, why the next one is due, when the last one sat, and the lords the fast answers
        // ordered while it sat (their orders win over the council's)
        private Task _council;
        private CancellationTokenSource _councilStop;
        private string _councilDue;
        private bool _councilFirstLook;
        private double _lastCouncilHours = double.NaN, _nextCouncilHours = double.NaN;
        private DateTime _councilFailedAt = DateTime.MinValue;
        private readonly object _councilGate = new object();
        private bool _councilSitting;
        private readonly HashSet<string> _orderedMeanwhile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // the lords the fast answers ordered in the last minute (0.12.4): a council that sits right after one is briefed on a world
        // that may not have its orders in it yet (run 20261025-1831-C2: the first answer sent Aragorn to sell at Mount Gundabad the
        // second the first council sat; the council, briefed on him free, sent him to Hithaegrist and turned him back 75 from the town)
        private readonly List<KeyValuePair<DateTime, List<string>>> _recentOrders = new List<KeyValuePair<DateTime, List<string>>>();
        private const double RecentOrderSeconds = 60;
        private string _councilNote;
        // a news wake's tools, not kept (0.14): told to the next fast answer, and to the next council, where tools are written
        private string _newsNote, _toolsWanted;

        /// <summary>The most chronicle lines put before the poke; older ones wait for the story, or are read in chronicle.txt.</summary>
        private const int MaxGapLines = 80;

        private static readonly Regex PokeHead = new Regex(@"^POKE #(\d+), (.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

        public Room(Config c, string logDir = null)
        {
            _c = c;
            _claude = Claude.Find(c.ClaudePath);
            _logDir = logDir ?? (!string.IsNullOrWhiteSpace(c.LogDir) ? c.LogDir.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BannerlordAgents", "logs", "warroom"));
            Directory.CreateDirectory(_logDir);
        }

        public string ClaudePath => _claude;
        public Memory Mem => _mem;

        // ---------- the loop ----------

        public int Run(CancellationToken stop)
        {
            Say("War room " + typeof(Room).Assembly.GetName().Version + ": answering pokes in " + _c.ChannelDir);
            Say("Model " + _c.Model + ", effort " + _c.Effort + " (urgent " + _c.UrgentEffort + "), Claude Code at " + _claude);
            Say("Memory: " + (_c.Conversation ? "one long conversation (kept to compare), starting again from the notes past " + _c.ConversationLimit.ToString("N0", CultureInfo.InvariantCulture) + " tokens"
                                              : "written: the story so far, the notes and the chronicle, with a fresh call each poke") + ".");
            Say("It waits for the game; with no pokes it does nothing. Ctrl+C stops it.");
            _lastAnswered = LastAnsweredOnDisk();
            int waitingSaid = -2;
            while (!stop.IsCancellationRequested)
            {
                int newest = NewestPoke(out string text, out string when);
                if (newest > _lastAnswered && text != null)
                {
                    Answer(newest, when, text);
                    _lastAnswered = newest;
                    // a council due sits now, beside the next answers: a realm whose pokes never stop never has a quiet moment
                    // (0.11.4, run C2: the kingdom's council asked for in 12 hours sat 8.7 days later, after the victory at Lasgalor)
                    CouncilIfDue(stop);
                    continue;
                }
                StoryIfDue(stop);
                CouncilIfDue(stop);
                if (waitingSaid != _lastAnswered) { Say("Waiting for the next poke (last answered #" + _lastAnswered + ")."); waitingSaid = _lastAnswered; }
                stop.WaitHandle.WaitOne(250);
            }
            _storyStop?.Cancel();
            _councilStop?.Cancel();
            try { _story?.Wait(5000); } catch (AggregateException) { }
            try { _council?.Wait(5000); } catch (AggregateException) { }
            Say("Stopped.");
            return 0;
        }

        /// <summary>Answers one poke now (the loop, "once" and the replays). write: the orders and the memory are kept.</summary>
        public CallResult Answer(int number, string when, string pokeText, bool write = true, string effort = null, string tag = null, int? thinkingCap = null)
        {
            DateTime noticed = DateTime.Now;
            PokeMeta meta = ReadMeta(number);
            if (meta != null && !double.IsNaN(meta.Hours)) _pokeHours = meta.Hours;
            EnsureMemory(meta);
            if (meta != null) { _mem.Run = meta.Run; _mem.Arm = meta.Arm; }
            string why = Section(pokeText, "WHY YOU ARE ASKED NOW");
            bool urgent = _c.Urgent.Any(u => why.IndexOf(u, StringComparison.OrdinalIgnoreCase) >= 0);
            effort ??= urgent ? _c.UrgentEffort : _c.Effort;
            // a news wake in theatre mode (0.14): the news instructions, the question book instead of the look-up files, no tool writing
            bool news = meta != null && meta.Theatre;
            RouteTool routes = Routes(meta, number, "answer", write, news ? BookFor(meta, number) : null);
            if (routes != null && news) routes.NoCheck = true;
            bool read = !news && _c.Lookups;
            string system = SystemFile(routes != null && !routes.BookOnly, news, routes?.BookPath != null, read);
            bool conversation = _c.Conversation;
            string sessionId = null;
            bool resume = false;
            if (conversation)
            {
                if (string.IsNullOrEmpty(_mem.SessionId)) { _mem.SessionId = Guid.NewGuid().ToString(); _mem.SessionStarted = false; _mem.SessionTokens = 0; }
                sessionId = _mem.SessionId;
                resume = _mem.SessionStarted;
            }
            string prompt = Message(pokeText, meta, conversation, !resume, out int gapLines, out int storyWords, write ? Join(TakeCouncilNote(), TakeNewsNote()) : null);
            string folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-poke-" + number + (tag != null ? "-" + tag : ""));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), prompt);

            int cap = thinkingCap ?? (urgent ? _c.UrgentThinkingCap : _c.ThinkingCap);
            Say("Poke #" + number + " (" + when + ")" + (urgent ? ", urgent" : "") + ": asking " + _c.Model + " at effort " + effort + ", thinking up to " + cap + " tokens"
                + (conversation ? (resume ? ", in its conversation (" + _mem.SessionTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens so far)" : ", in a new conversation") : "") + "...");
            // the first look since a load reads the realm whole, often with no story yet: it gets the council's time (0.11.3: the kingdom's first answer was stopped at 122 s of 120)
            int timeout = why.IndexOf("First look", StringComparison.OrdinalIgnoreCase) >= 0 ? Math.Max(_c.TimeoutSeconds, _c.CouncilTimeoutSeconds) : _c.TimeoutSeconds;
            CallResult r = Claude.Run(_claude, system, prompt, _c.Model, effort, _c.ChannelDir, read, timeout, cap, sessionId, resume, tools: routes);
            if (r.HandshakeLate) Say("Poke #" + number + ": Claude Code took over 30 s to answer the tools' handshake; the message went anyway.");
            bool stallRetry = false;
            if (!r.Ok && (r.Stalled || r.Runaway))
            {
                // a call that goes quiet is asked once more; the time lost is counted
                File.WriteAllText(Path.Combine(folder, "raw-stalled.jsonl"), r.Raw ?? "");
                Say("Poke #" + number + ": the call " + (r.Runaway ? "ran away" : "stalled") + " after " + r.Seconds.ToString("0", CultureInfo.InvariantCulture) + " s; asking again.");
                double lost = r.Seconds;
                stallRetry = true;
                if (conversation && !resume) sessionId = _mem.SessionId = Guid.NewGuid().ToString();   // the first call of a conversation is begun again under a new id
                r = Claude.Run(_claude, system, prompt, _c.Model, effort, _c.ChannelDir, read, timeout, cap, sessionId, resume, tools: routes);
                r.Seconds += lost;
            }
            if (conversation && r.Ok)
            {
                _mem.SessionStarted = true;
                _mem.SessionTokens = r.InTokens + r.CacheRead + r.CacheWrite + r.OutTokens;
            }
            File.WriteAllText(Path.Combine(folder, "raw.jsonl"), r.Raw ?? "");
            JsonObject answer = null;
            string problem = r.Ok ? Extract(r.Text, number, out answer) : r.Error;
            bool retried = false;
            if (answer == null && r.Ok)
            {
                // one retry, outside any conversation: show it its own answer and the problem, at the lowest effort
                retried = true;
                string fix = "Your answer to poke #" + number + " could not be read: " + problem + ".\nHere it is:\n" + r.Text
                             + "\n\nReply again with ONLY the JSON object, nothing before or after it.";
                CallResult r2 = Claude.Run(_claude, system, fix, _c.Model, "low", _c.ChannelDir, false, _c.TimeoutSeconds, 1000, tools: routes);
                File.WriteAllText(Path.Combine(folder, "raw-retry.jsonl"), r2.Raw ?? "");
                problem = r2.Ok ? Extract(r2.Text, number, out answer) : r2.Error;
                r.Seconds += r2.Seconds;
                r.OutTokens += r2.OutTokens;
                r.Thinking += r2.Thinking;
            }
            int orders = answer?["orders"] is JsonArray arr ? arr.Count : 0;
            // tools are written at a council of war only (0.14, decision 3): a news wake's are not kept, and it and the next council are told
            List<string> toolsDropped = news && answer?["tools"] is JsonArray wrote && wrote.Count > 0 ? ToolNamesOf(wrote) : null;
            if (toolsDropped != null)
            {
                answer.Remove("tools");
                if (write) lock (_councilGate)
                {
                    _newsNote = "Your answer to poke #" + number + " wrote " + (toolsDropped.Count == 1 ? "a tool" : toolsDropped.Count + " tools") + " (" + string.Join(", ", toolsDropped)
                                + "): tools are written at a council of war only, so " + (toolsDropped.Count == 1 ? "it was" : "they were") + " not kept. Write it at your next council.";
                    _toolsWanted = "YOUR NEWS ANSWERS WANTED NEW TOOLS: poke #" + number + " wrote " + string.Join(", ", toolsDropped) + ", which were not kept, since tools are written at a council. Write them now if they are still needed.";
                }
            }
            string changed = "";
            if (answer != null && write)
            {
                WriteOrders(number, answer, call: folder);
                changed = Remember(answer, number, when, meta);
                AfterAnswer(pokeText, answer);
            }
            KeepThinking(folder, r);
            long context = r.InTokens + r.CacheRead + r.CacheWrite;
            if (write)
            {
                if (conversation && _mem.SessionTokens > _c.ConversationLimit)
                {
                    Say("Memory: the conversation reached " + _mem.SessionTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens; the next poke starts a new one from the notes.");
                    _mem.SessionId = null;
                    _mem.SessionStarted = false;
                }
                _mem.Save();
            }
            if (answer != null) File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            double seconds = (DateTime.Now - noticed).TotalSeconds;
            Log(new
            {
                kind = "answer", at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), poke = number, when, urgent, model = _c.Model, effort, thinking_cap = cap,
                ok = answer != null, problem, retried, stall_retry = stallRetry,
                seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2), api_ms = r.ApiMs, ttft_ms = r.TtftMs, turns = r.Turns,
                tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cost_usd_list = r.Cost,
                answer_began_ms = Math.Round(r.FirstTextMs), memory = _c.MemoryMode, context_tokens = context, story_words = storyWords, gap_lines = gapLines,
                notes_chars = _mem.Notes.Length, notes_changed = changed, session = sessionId, campaign = _mem.Campaign,
                orders, denied = r.Denied, folder, written = write && answer != null, ways_asked = routes?.Asked ?? 0, way_tool = routes != null,
                run = meta?.Run, arm = meta?.Arm, realm = meta?.Realm, system_hash = _systemHash, library_hash = LibraryHash(), thinking_chars = r.ThinkingText.Length,
                tools = Count(answer, "tools"), diplomacy = Count(answer, "diplomacy"), letters = Count(answer, "letters"),
                news_mode = meta?.NewsMode, recipe = meta?.Recipe, message_chars = prompt.Length, read_tool = read, book_asked = routes?.AskedBook ?? 0,
                tools_offered = routes?.Tools.Count() ?? 0, tools_dropped = toolsDropped
            });
            if (answer != null)
                Say("Poke #" + number + ": " + orders + " order" + (orders == 1 ? "" : "s") + " in " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s ("
                    + r.OutTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens written, " + r.Thinking.ToString("N0", CultureInfo.InvariantCulture) + " of them thinking; "
                    + r.Turns + " step" + (r.Turns == 1 ? "" : "s") + ")" + (retried ? " after one retry" : "") + "."
                    + (routes != null && routes.Asked > 0 ? " Asked the way " + routes.Asked + (routes.Asked == 1 ? " time." : " times.") : "")
                    + (routes != null && routes.AskedBook > 0 ? " Asked its book " + routes.AskedBook + (routes.AskedBook == 1 ? " time." : " times.") : "")
                    + (toolsDropped != null ? " Its tools were not kept (councils write tools)." : "")
                    + (changed.Length > 0 ? " Notes: " + changed + "." : ""));
            else Say("Poke #" + number + ": NO ANSWER (" + problem + ") after " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");
            _lastCallEnded = DateTime.Now;
            return r;
        }

        /// <summary>The message: the memory (the story so far, the notes, the chronicle not in the story yet), then the poke.
        /// before: a line for this message only, such as what became of the last council's orders.</summary>
        internal string Message(string pokeText, PokeMeta meta, bool conversation, bool newConversation, out int gapLines, out int storyWords, string before = null)
        {
            gapLines = storyWords = 0;
            _mem.View(out string story, out double storyHours, out string storyDate, out string notes, out _);
            var sb = new StringBuilder();
            if (conversation)
            {
                if (newConversation)
                {
                    sb.AppendLine("YOUR NOTES (from before this conversation; change them with \"plan\", \"enemies\" and \"lessons\" in your answers)");
                    sb.AppendLine(notes);
                    sb.AppendLine();
                    sb.AppendLine("This conversation is your memory from now on: every poke comes into it.");
                    sb.AppendLine();
                }
            }
            else if (ReplayMemory != null)
            {
                // a replay of a recorded call (0.14): the memory that call read, cut to the news when it is replayed as a news wake
                sb.Append(meta != null && meta.Focused ? FocusMemory(ReplayMemory, meta.Names, meta.Realms) : ReplayMemory.Replace("\r", ""));
            }
            else
            {
                storyWords = Words(story);
                sb.AppendLine("THE STORY SO FAR" + (story.Length > 0 ? " (as of " + storyDate + ", written by your chronicler from your chronicle)" : ""));
                sb.AppendLine(story.Length > 0 ? story.Trim()
                    : "(Not written yet: your chronicler writes it in a quiet moment. Until then, your notes, the chronicle lines below and the report's recent history are your memory.)");
                sb.AppendLine();
                // a news wake in theatre mode (0.14): the plan and the lessons whole, the notes on the enemies in this news only, and the chronicle's
                // lines about it; the rest is a question away (list notes, chronicle)
                bool focused = meta != null && meta.Focused;
                int hidden = 0;
                sb.AppendLine("YOUR NOTES (only you change them, with \"plan\", \"enemies\" and \"lessons\" in your answer)");
                sb.AppendLine(focused ? FocusNotes(notes, meta.Names.Concat(meta.Realms).ToList(), out hidden) : notes);
                if (hidden > 0) sb.AppendLine("(Your notes on " + hidden + " other " + (hidden == 1 ? "enemy" : "enemies") + ", not in this news, are kept as they are: list notes shows them all.)");
                sb.AppendLine();
                List<ChronicleLine> gap = Gap(storyHours, meta);
                gapLines = gap.Count;
                int elsewhere = 0;
                if (focused)
                {
                    List<ChronicleLine> here = gap.Where(l => meta.Names.Any(n => n.Length >= 4 && (l.Text.IndexOf("[" + n + "]", StringComparison.OrdinalIgnoreCase) >= 0 || Mentions(l.Text, n)))).ToList();
                    elsewhere = gap.Count - here.Count;
                    gap = here;
                }
                if (gap.Count > 0 || elsewhere > 0)
                {
                    sb.AppendLine("YOUR CHRONICLE SINCE THE STORY, BEFORE THE REPORT'S RECENT HISTORY (" + gap.Count + " lines" + (focused ? " about this news" : "") + ", oldest first)");
                    int skip = Math.Max(0, gap.Count - MaxGapLines);
                    if (skip > 0) sb.AppendLine("- (" + skip + " earlier lines are in " + (focused ? "your chronicle: ask chronicle" : "chronicle.txt") + ")");
                    foreach (ChronicleLine l in gap.Skip(skip)) sb.AppendLine("- " + l.Date + " · " + l.Text);
                    if (elsewhere > 0) sb.AppendLine("- (" + elsewhere + " more lines from elsewhere in your realm: ask chronicle about a lord, holding or realm)");
                    sb.AppendLine();
                }
            }
            if (!string.IsNullOrEmpty(before)) sb.AppendLine(before.TrimEnd()).AppendLine();
            sb.AppendLine(_lastAnswered >= 0 ? "Your last answer was to poke #" + _lastAnswered + "." : "You have not answered any poke yet in this run.");
            sb.AppendLine();
            sb.Append(pokeText);
            return sb.ToString();
        }

        /// <summary>The chronicle lines the story does not cover yet, less the ones the report's RECENT HISTORY already shows.</summary>
        internal List<ChronicleLine> Gap(double storyHours, PokeMeta meta)
        {
            if (meta == null || double.IsNaN(meta.Hours)) return new List<ChronicleLine>();
            List<ChronicleLine> all = Memory.ReadChronicle(_c.ChannelDir).Where(l => l.Hours <= meta.Hours + 1).ToList();
            // the lines the report's RECENT HISTORY shows, by the bridge's own rule (Common.RecentHistory, 0.13.16)
            var recent = new HashSet<ChronicleLine>(BannerlordAgents.Common.RecentHistory.Pick(all.Select(l => l.Hours).ToList(), all.Select(l => l.Kind).ToList(), meta.Hours).Select(i => all[i]));
            return all.Where(l => (double.IsNaN(storyHours) || l.Hours > storyHours + 1e-6) && !recent.Contains(l)).ToList();
        }

        /// <summary>The answer into the notes; a snapshot when they changed. Returns what changed, in a few words.</summary>
        private string Remember(JsonObject answer, int number, string when, PokeMeta meta)
        {
            string changed = _mem.Apply(answer);
            _mem.LastPoke = number;
            if (meta != null && !double.IsNaN(meta.Hours)) _mem.LastHours = meta.Hours;
            _mem.Save();
            if (changed.Length > 0) _mem.Snapshot(_mem.LastHours, meta?.Date ?? when, number, "answer");
            return changed;
        }

        // ---------- memory: which campaign, and a reload ----------

        /// <summary>
        /// The memory of the poke's campaign; and on a new load of the game from an earlier date than the memory reaches,
        /// the memory goes back to that date (the bridge cuts its chronicle back the same way).
        /// </summary>
        internal void EnsureMemory(PokeMeta meta)
        {
            // poke.json may already hold the next poke; its campaign is still this one
            string campaign = meta?.Campaign ?? LatestCampaign() ?? _mem?.Campaign ?? "unknown";
            if (_mem == null || _mem.Campaign != campaign)
            {
                _mem = Memory.Open(_c, campaign, Say, meta?.Hours ?? double.NaN);
                Say("Memory of campaign " + campaign + ": " + _mem.Dir + (_mem.Story.Length > 0 ? "; the story so far reaches " + _mem.StoryDate : "") + ".");
            }
            if (meta == null || string.IsNullOrEmpty(meta.LoadId) || meta.LoadId == _mem.LoadId) return;
            if (meta.Before != null)
            {
                // since 0.9.1 the bridge says which sessions this save's world lived through: the memory is as it was in that history
                string back = _mem.Restore(meta.Before, meta.LoadedAt);
                Say("Memory: a save was loaded (" + meta.LoadedAtDate + (meta.HistoryFound == "marked" ? ", its history from its note" : meta.HistoryFound == "new" ? ", a new game" : ", with no history note") + "), so the realm's memory is as it was " + (back.Length > 0 ? "at " + back : "at its beginning") + ".");
            }
            else if (!double.IsNaN(meta.LoadedAt) && !double.IsNaN(_mem.LastHours) && meta.LoadedAt < _mem.LastHours - 0.5)
            {
                string back = _mem.CutBack(meta.LoadedAt);
                Say("Memory: an earlier save was loaded (" + meta.LoadedAtDate + "), so the realm's memory went back to " + (back.Length > 0 ? back : "its beginning") + ".");
            }
            _mem.LoadId = meta.LoadId;
            _mem.Save();
        }

        private string LatestCampaign()
        {
            try { return JsonNode.Parse(ReadShared(Path.Combine(_c.ChannelDir, "poke.json")) ?? "") is JsonObject o ? (string)o["campaign"] : null; }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException) { return null; }
        }

        /// <summary>For the self-test: what poke.json says about a poke.</summary>
        internal PokeMeta ReadMetaForTest(int number) => ReadMeta(number);

        private PokeMeta ReadMeta(int number)
        {
            string text = ReadShared(Path.Combine(_c.ChannelDir, "poke.json"));
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                if (JsonNode.Parse(text) is not JsonObject o) return null;
                var m = new PokeMeta
                {
                    Number = (int?)o["number"] ?? -1, Hours = Num(o["hours"]), Date = (string)o["date"] ?? "", Campaign = (string)o["campaign"], Realm = (string)o["realm"],
                    RealmName = (string)o["realm_name"], LoadId = (string)o["load_id"], LoadedAt = Num(o["loaded_at"]), LoadedAtDate = (string)o["loaded_at_date"] ?? "",
                    Run = (string)o["run"], Arm = (string)o["arm"]
                };
                if (o["news"] is JsonObject nw)
                {
                    m.NewsMode = (string)nw["mode"] ?? "full";
                    m.Recipe = (string)nw["recipe"];
                    m.Book = (string)nw["book"];
                    if (nw["names"] is JsonArray na) m.Names = na.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    if (nw["realms"] is JsonArray ra) m.Realms = ra.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
                if (o["history"] is JsonObject h && h["before"] is JsonArray steps)
                {
                    m.HistoryFound = (string)h["found"] ?? "";
                    m.Before = new List<KeyValuePair<string, double>>();
                    foreach (JsonNode st in steps) m.Before.Add(new KeyValuePair<string, double>((string)st?["load"], Num(st?["until"])));
                }
                return number < 0 || m.Number == number ? m : null;   // -1: whatever poke.json holds (the council)
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is FormatException) { return null; }
        }

        // ---------- the chronicler ----------

        /// <summary>
        /// In a quiet moment, once enough new chronicle lines have come (story_every), or the oldest of them is a day and a
        /// half older than the newest poke: the story so far is rewritten on its own thread, beside the answers. A failed
        /// attempt waits five minutes.
        /// </summary>
        private void StoryIfDue(CancellationToken stop)
        {
            if (_c.Conversation || _mem == null || (_story != null && !_story.IsCompleted)) return;
            if ((DateTime.Now - _storyFailedAt).TotalMinutes < 5 || (DateTime.Now - _lastCallEnded).TotalSeconds < 2) return;
            if ((DateTime.Now - _storyLooked).TotalSeconds < 5) return;   // the chronicle is read every few seconds at most
            _storyLooked = DateTime.Now;
            List<ChronicleLine> fresh = DueStory(_pokeHours);
            if (fresh == null) return;
            _storyStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
            CancellationToken token = _storyStop.Token;
            Memory m = _mem;
            _story = Task.Run(() => WriteStory(m, fresh, token));
        }

        /// <summary>The chronicle lines for a new story so far, when one is due (nowHours: the newest poke's hour); else null.</summary>
        public List<ChronicleLine> DueStory(double nowHours)
        {
            if (_c.Conversation || _mem == null) return null;
            _mem.View(out _, out double storyHours, out _, out _, out _);
            List<ChronicleLine> fresh = Memory.ReadChronicle(_c.ChannelDir).Where(l => double.IsNaN(storyHours) || l.Hours > storyHours + 1e-6).ToList();
            if (fresh.Count == 0) return null;
            bool due = fresh.Count >= _c.StoryEvery || (!double.IsNaN(nowHours) && nowHours - fresh[0].Hours >= 36);
            return due ? fresh.Take(400).ToList() : null;
        }

        /// <summary>The chronicler's call: the story so far, the notes for context and the new lines, to one new story so far.</summary>
        public bool WriteStory(Memory m, List<ChronicleLine> fresh, CancellationToken cancel = default, string tag = null)
        {
            DateTime began = DateTime.Now;
            m.View(out string story, out _, out string storyDate, out string notes, out int generation);
            List<string> given = m.Lessons();
            bool merge = given.Count > _c.MaxLessons;
            var sb = new StringBuilder();
            sb.AppendLine("THE STORY SO FAR" + (story.Length > 0 ? " (as of " + storyDate + ")" : ""));
            sb.AppendLine(story.Length > 0 ? story.Trim() : "(none yet: this is the first)");
            sb.AppendLine();
            sb.AppendLine("THE RULER'S NOTES (for context only: do not rewrite them)");
            sb.AppendLine(notes);
            sb.AppendLine();
            sb.AppendLine("NEW IN THE CHRONICLE SINCE THEN (" + fresh.Count + " lines, oldest first)");
            foreach (ChronicleLine l in fresh) sb.AppendLine("- " + l.Date + " · " + l.Text);
            sb.AppendLine();
            sb.AppendLine("Write the new story so far, at most " + _c.StoryWords + " words."
                          + (merge ? " The ruler's lessons have grown to " + given.Count + ": also give \"lessons\", the ones still worth keeping, merged and shortened, at most " + _c.MaxLessons + "." : ""));
            sb.AppendLine("Reply with ONLY one JSON object: {\"story\": \"...\"" + (merge ? ", \"lessons\": [\"...\"]" : "") + "}");
            string model = string.IsNullOrWhiteSpace(_c.StoryModel) ? _c.Model : _c.StoryModel;
            string folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-story" + (tag != null ? "-" + tag : ""));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), sb.ToString());
            Say("Memory: the chronicler is rewriting the story so far (" + fresh.Count + " new lines)...");
            CallResult r = Claude.Run(_claude, ChroniclerFile(), sb.ToString(), model, _c.StoryEffort, _c.ChannelDir, false, Math.Max(_c.TimeoutSeconds, 240), _c.StoryThinkingCap, cancel: cancel);
            File.WriteAllText(Path.Combine(folder, "raw.jsonl"), r.Raw ?? "");
            string newStory = null;
            List<string> lessons = null;
            string problem = r.Ok ? null : r.Error;
            if (r.Ok)
            {
                foreach (string candidate in Objects(r.Text).Reverse())
                {
                    try
                    {
                        if (JsonNode.Parse(candidate) is JsonObject o && o["story"] is JsonValue sv && sv.TryGetValue(out string s) && s.Trim().Length > 0)
                        {
                            newStory = s.Trim();
                            if (o["lessons"] is JsonArray la) lessons = la.Select(x => x?.ToString() ?? "").Where(x => x.Trim().Length > 0).ToList();
                            break;
                        }
                    }
                    catch (JsonException) { }
                }
                if (newStory == null) problem = "no story in its answer";
                else if (Words(newStory) > _c.StoryWords * 2) { problem = "the story ran to " + Words(newStory) + " words"; newStory = null; }
            }
            bool kept = false;
            if (newStory != null)
            {
                ChronicleLine last = fresh[fresh.Count - 1];
                kept = m.SetStory(newStory, last.Hours, last.Date, generation);
                if (kept)
                {
                    if (merge && lessons != null && lessons.Count > 0) m.ReplaceLessons(given, lessons);
                    m.Save();
                    m.Snapshot(double.IsNaN(m.LastHours) ? last.Hours : Math.Max(m.LastHours, last.Hours), last.Date, m.LastPoke, "story");
                    File.WriteAllText(Path.Combine(folder, "story.md"), newStory);
                }
                else problem = "the memory was cut back while it wrote, so this story was thrown away";
            }
            double seconds = (DateTime.Now - began).TotalSeconds;
            Log(new
            {
                kind = "story", at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), model, effort = _c.StoryEffort, thinking_cap = _c.StoryThinkingCap,
                ok = kept, problem, lines = fresh.Count, words = newStory != null ? Words(newStory) : 0, lessons_merged = merge && lessons != null ? lessons.Count : (int?)null,
                seconds = Math.Round(seconds, 2), tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite,
                cost_usd_list = r.Cost, campaign = m.Campaign, folder
            });
            if (kept)
                Say("Memory: the story so far was rewritten in " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s (" + Words(newStory) + " words, up to " + fresh[fresh.Count - 1].Date
                    + (merge && lessons != null ? "; lessons merged from " + given.Count + " to " + lessons.Count : "") + ").");
            else if (!cancel.IsCancellationRequested)
            {
                _storyFailedAt = DateTime.Now;
                Say("Memory: the story so far was not rewritten (" + problem + "); it is tried again in five minutes.");
            }
            return kept;
        }

        // ---------- the council of war ----------

        /// <summary>
        /// After each fast answer: a poke whose reasons hold one of council_after's words calls a council; and while a council
        /// sits, the lords this answer ordered are noted, so the council's older view does not undo a newer order.
        /// </summary>
        internal void AfterAnswer(string pokeText, JsonObject answer)
        {
            lock (_councilGate)
            {
                if (_councilSitting && answer?["orders"] is JsonArray list)
                    foreach (string lord in OrderedLords(list)) _orderedMeanwhile.Add(lord);
                if (answer?["orders"] is JsonArray given)
                {
                    List<string> lords = OrderedLords(given).ToList();
                    if (lords.Count > 0) _recentOrders.Add(new KeyValuePair<DateTime, List<string>>(DateTime.Now, lords));
                }
                _recentOrders.RemoveAll(x => (DateTime.Now - x.Key).TotalSeconds > RecentOrderSeconds);
                if (!_c.Council || _c.Conversation) return;
                string reasons = Section(pokeText, "WHY YOU ARE ASKED NOW") + "\n" + Section(pokeText, "WHAT CHANGED SINCE YOUR LAST ANSWER");
                foreach (string w in _c.CouncilAfter)
                {
                    int i = reasons.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    string line = reasons.Substring(reasons.LastIndexOf('\n', i) + 1);
                    int end = line.IndexOf('\n');
                    _councilDue = (end > 0 ? line.Substring(0, end) : line).Trim().TrimStart('-', ' ');
                    if (w.StartsWith("First look", StringComparison.OrdinalIgnoreCase)) _councilFirstLook = true;
                    break;
                }
            }
        }

        /// <summary>The lords an answer's orders name: each order's lord, and the lords it calls (gather, add).</summary>
        internal static IEnumerable<string> OrderedLords(JsonArray orders)
        {
            foreach (JsonNode n in orders)
            {
                if (n is not JsonObject o) continue;
                if (o["lord"] is JsonValue lv && lv.TryGetValue(out string lord) && lord.Trim().Length > 0) yield return lord.Trim();
                if (o["lords"] is JsonArray ls)
                    foreach (JsonNode l in ls)
                        if (l is JsonValue v && v.TryGetValue(out string s) && s.Trim().Length > 0) yield return s.Trim();
            }
        }

        /// <summary>
        /// Why a council of war should sit now, or null: after the first look (at once), after a big event, or when its time
        /// has come (council_every_hours after the last, or the hour the last council asked for); never within council_gap_hours
        /// of the last one, except for the first look, and not while one sits. nowHours: the newest poke's in-game hour.
        /// </summary>
        internal string CouncilDue(double nowHours)
        {
            if (!_c.Council || _c.Conversation || double.IsNaN(nowHours)) return null;
            lock (_councilGate)
            {
                if (_councilSitting) return null;
                bool gapOk = double.IsNaN(_lastCouncilHours) || nowHours - _lastCouncilHours >= _c.CouncilGapHours - 1e-6;
                if (_councilDue != null && (_councilFirstLook || gapOk)) return _councilDue;
                if (!gapOk) return null;
                if (double.IsNaN(_lastCouncilHours)) return "no council has sat yet in this run of the war room";
                if (!double.IsNaN(_nextCouncilHours) && nowHours >= _nextCouncilHours - 1e-6)
                    return Math.Round(nowHours - _lastCouncilHours) + " hours have passed since the last council";
                return null;
            }
        }

        private void CouncilIfDue(CancellationToken stop)
        {
            if (_mem == null || (_council != null && !_council.IsCompleted)) return;
            if ((DateTime.Now - _councilFailedAt).TotalMinutes < 5) return;
            string why = CouncilDue(_pokeHours);
            if (why == null || !Fresh(out _)) return;
            _councilStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
            CancellationToken token = _councilStop.Token;
            lock (_councilGate) BeginSitting();
            _council = Task.Run(() =>
            {
                try { SitCouncil(why, token); }
                catch (Exception e) when (e is IOException || e is InvalidOperationException || e is JsonException)
                {
                    _councilFailedAt = DateTime.Now;
                    Say("Council of war: it could not sit (" + e.Message + "); tried again in five minutes.");
                }
                finally { lock (_councilGate) _councilSitting = false; }
            });
        }

        /// <summary>"WarRoom council": a council sits now, by hand (the game must be running: its briefing fresh).</summary>
        public int CouncilNow(string why)
        {
            PokeMeta meta = ReadMeta(-1);
            if (meta == null) { Say("No poke.json in " + _c.ChannelDir + " yet: load the campaign first."); return 1; }
            if (!Fresh(out string stale)) { Say("Council of war: not now: " + stale + "."); return 1; }
            _lastAnswered = Math.Max(_lastAnswered, LastAnsweredOnDisk());
            EnsureMemory(meta);
            return SitCouncil(why) ? 0 : 1;
        }

        /// <summary>The game is running: its latest poke was written within the last ten minutes (0.12.0: the briefing is written only when asked for).</summary>
        private bool Fresh(out string why)
        {
            string poke = Path.Combine(_c.ChannelDir, "poke.json");
            why = !File.Exists(poke) ? "no poke from the game yet (poke.json)" : null;
            if (why != null) return false;
            double age = (DateTime.Now - File.GetLastWriteTime(poke)).TotalMinutes;
            if (age > 10) { why = "the latest poke is " + Math.Round(age) + " minutes old: the game is closed or long paused"; return false; }
            return true;
        }

        /// <summary>The name the bridge looks for in the orders folder (its Channel.BriefingRequest), and how long a council waits for the briefing.</summary>
        internal const string BriefingRequest = "council-request";
        internal static TimeSpan BriefingWait = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Asks the bridge for the council's briefing and situation report from its newest hour (0.12.0: it wrote both every in-game
        /// hour for every agent, a large part of what made it fall behind the game), and waits for council.txt to be written. True
        /// when it came; false after BriefingWait, when the last one written is used, if any.
        /// </summary>
        internal bool RequestBriefing(out string note)
        {
            note = null;
            string orders = Path.Combine(_c.ChannelDir, "orders"), briefing = Path.Combine(_c.ChannelDir, "council.txt");
            Directory.CreateDirectory(orders);
            DateTime asked = DateTime.UtcNow;
            string name = BriefingRequest + "-" + asked.ToString("HHmmssfff", CultureInfo.InvariantCulture) + ".json";
            string tmp = Path.Combine(_c.ChannelDir, name + ".part");
            File.WriteAllText(tmp, "{\"asked\":\"" + asked.ToString("o", CultureInfo.InvariantCulture) + "\"}");
            File.Move(tmp, Path.Combine(orders, name));
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (wait.Elapsed < BriefingWait)
            {
                if (File.Exists(briefing) && File.GetLastWriteTimeUtc(briefing) >= asked) return true;
                Thread.Sleep(100);
            }
            note = "the game did not write a fresh briefing within " + (int)BriefingWait.TotalSeconds + " s";
            try { File.Delete(Path.Combine(orders, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }

        /// <summary>A council sits now (also "WarRoom council"): the reason given; true when its answer was read and written.</summary>
        public bool SitCouncil(string why, CancellationToken cancel = default, string tag = null, bool write = true)
        {
            DateTime began = DateTime.Now;
            PokeMeta meta = ReadMeta(-1);
            if (meta == null || _mem == null) { Say("Council of war: no poke yet in this run, so it waits."); return false; }
            int number = meta.Number;
            // the briefing from the game's newest hour, written for this council (0.12.0)
            if (!RequestBriefing(out string late))
            {
                if (!File.Exists(Path.Combine(_c.ChannelDir, "council.txt"))) { Say("Council of war: " + late + ", and there is none from before, so it waits."); return false; }
                Say("Council of war: " + late + "; the last one written is used.");
            }
            string briefing = ReadShared(Path.Combine(_c.ChannelDir, "council.txt")) ?? "";
            string situation = ReadShared(Path.Combine(_c.ChannelDir, "report.txt")) ?? "";
            string council = CouncilText(why, meta, number, briefing, situation);
            string prompt = Message(council, meta, false, true, out int gapLines, out int storyWords, write ? TakeToolsWanted() : null);
            string folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-council-" + number + (tag != null ? "-" + tag : ""));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), prompt);
            Say("Council of war (after poke #" + number + ", " + meta.Date + "): " + why + ". Asking " + _c.Model + " at effort " + _c.CouncilEffort + ", thinking up to " + _c.CouncilThinkingCap + " tokens...");
            RouteTool routes = Routes(meta, number, "council", write);
            CallResult r = Claude.Run(_claude, SystemFile(routes != null), prompt, _c.Model, _c.CouncilEffort, _c.ChannelDir, _c.Lookups, _c.CouncilTimeoutSeconds, _c.CouncilThinkingCap, cancel: cancel, tools: routes);
            File.WriteAllText(Path.Combine(folder, "raw.jsonl"), r.Raw ?? "");
            JsonObject answer = null;
            string problem = r.Ok ? Extract(r.Text, number, out answer) : r.Error;
            var dropped = new List<string>();
            int orders = 0;
            double? councilIn = null;
            string changed = "";
            if (answer != null)
            {
                lock (_councilGate)
                {
                    // a fast answer that ordered a lord while the council sat saw a newer world: its order stands
                    answer["orders"] = DropOrdered(answer["orders"] as JsonArray ?? new JsonArray(), _orderedMeanwhile, out dropped);
                }
                orders = ((JsonArray)answer["orders"]).Count;
                if (answer["council_in"] is JsonValue ci && ci.TryGetValue(out double h)) councilIn = Math.Max(4, Math.Min(72, h));
                answer["kind"] = "council";
                if (write)
                {
                    WriteOrders(number, answer, "council", folder);
                    changed = Remember(answer, number, meta.Date, meta);
                }
                File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            KeepThinking(folder, r);
            double seconds = (DateTime.Now - began).TotalSeconds;
            if (answer != null)
            {
                CouncilSat(meta.Hours, councilIn);
                if (dropped.Count > 0 && write)
                    lock (_councilGate)
                        _councilNote = "YOUR COUNCIL OF WAR (" + meta.Date + ") gave " + (orders + dropped.Count) + " orders; " + dropped.Count + " of them did not go out, because a faster answer of yours ordered those lords while it sat: "
                                       + string.Join("; ", dropped) + ".";
            }
            else _councilFailedAt = DateTime.Now;
            Log(new
            {
                kind = "council", at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), poke = number, when = meta.Date, why, model = _c.Model, effort = _c.CouncilEffort,
                thinking_cap = _c.CouncilThinkingCap, ok = answer != null, problem, seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2), api_ms = r.ApiMs, ttft_ms = r.TtftMs, turns = r.Turns,
                tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cost_usd_list = r.Cost,
                context_tokens = r.InTokens + r.CacheRead + r.CacheWrite, story_words = storyWords, gap_lines = gapLines, briefing_chars = briefing.Length, situation_chars = situation.Length,
                orders, dropped, council_in = councilIn, notes_changed = changed, campaign = _mem.Campaign, denied = r.Denied, folder, written = write && answer != null, ways_asked = routes?.Asked ?? 0, way_tool = routes != null,
                run = meta.Run, arm = meta.Arm, realm = meta.Realm, system_hash = _systemHash, library_hash = LibraryHash(), thinking_chars = r.ThinkingText.Length,
                tools = Count(answer, "tools"), diplomacy = Count(answer, "diplomacy"), letters = Count(answer, "letters")
            });
            if (answer != null)
                Say("Council of war: " + orders + " order" + (orders == 1 ? "" : "s") + " in " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s ("
                    + r.Thinking.ToString("N0", CultureInfo.InvariantCulture) + " tokens of thinking)" + (dropped.Count > 0 ? "; " + dropped.Count + " left out, the lords ordered meanwhile" : "")
                    + (changed.Length > 0 ? ". Notes: " + changed : "") + ". Next council by " + Program.When(_nextCouncilHours) + " at the latest.");
            else if (!cancel.IsCancellationRequested) Say("Council of war: NO ANSWER (" + problem + ") after " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s; tried again in five minutes.");
            return answer != null;
        }

        /// <summary>The council's part of the message: why it sits, then the bridge's whole-realm briefing, then the hourly situation report.</summary>
        internal static string CouncilText(string why, PokeMeta meta, int number, string briefing, string situation)
        {
            var sb = new StringBuilder();
            sb.AppendLine("COUNCIL OF WAR, " + meta.Date);
            sb.AppendLine("Why it sits now: " + why.TrimEnd('.') + ".");
            sb.AppendLine("This is not a poke: it is your council of war (see \"The council of war\" in your instructions). Answer with one JSON object that says \"poke\": " + number + ".");
            sb.AppendLine();
            sb.AppendLine(briefing.Trim());
            sb.AppendLine();
            sb.Append(situation.Trim());
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>The council's orders, less those for lords a faster answer ordered meanwhile (and those lords taken out of gather's and add's lists).</summary>
        internal static JsonArray DropOrdered(JsonArray orders, ISet<string> ordered, out List<string> dropped)
        {
            dropped = new List<string>();
            var kept = new JsonArray();
            foreach (JsonNode n in orders)
            {
                if (n is not JsonObject o) continue;
                string lord = o["lord"] is JsonValue lv && lv.TryGetValue(out string s) ? s.Trim() : "";
                if (ordered.Contains(lord)) { dropped.Add(lord + " " + ((string)(o["do"] as JsonValue) ?? "?")); continue; }
                if (o["lords"] is JsonArray ls)
                {
                    var keep = new JsonArray();
                    foreach (JsonNode l in ls)
                    {
                        string id = l is JsonValue v && v.TryGetValue(out string x) ? x.Trim() : "";
                        if (ordered.Contains(id)) dropped.Add(id + " (called by " + lord + ")");
                        else keep.Add(id);
                    }
                    if (keep.Count == 0 && ls.Count > 0) { dropped.Add(lord + " " + ((string)(o["do"] as JsonValue) ?? "?") + ", every lord it called was ordered meanwhile"); continue; }
                    o["lords"] = keep;
                }
                kept.Add(o.DeepClone());
            }
            return kept;
        }

        /// <summary>A council sat at this in-game hour: the next one is due council_in hours later (its own choice, 4 to 72), else council_every_hours.</summary>
        internal void CouncilSat(double hours, double? councilIn)
        {
            lock (_councilGate)
            {
                _lastCouncilHours = hours;
                _nextCouncilHours = hours + (councilIn ?? _c.CouncilEveryHours);
                _councilDue = null;
                _councilFirstLook = false;
            }
        }

        internal double NextCouncilHours { get { lock (_councilGate) return _nextCouncilHours; } }

        /// <summary>For the self-test: a council sitting (fast answers' lords are then noted), and the note for the next fast message.</summary>
        internal void CouncilSitting(bool sitting) { lock (_councilGate) { if (sitting) BeginSitting(); else _councilSitting = false; } }

        /// <summary>A council sits (under _councilGate): the lords ordered meanwhile start as those the fast answers ordered in the last minute.</summary>
        private void BeginSitting()
        {
            _councilSitting = true;
            _orderedMeanwhile.Clear();
            foreach (KeyValuePair<DateTime, List<string>> r in _recentOrders)
                if ((DateTime.Now - r.Key).TotalSeconds <= RecentOrderSeconds)
                    foreach (string lord in r.Value) _orderedMeanwhile.Add(lord);
        }
        internal HashSet<string> OrderedMeanwhile { get { lock (_councilGate) return new HashSet<string>(_orderedMeanwhile, StringComparer.OrdinalIgnoreCase); } }

        private string TakeCouncilNote()
        {
            lock (_councilGate)
            {
                string note = _councilNote;
                _councilNote = null;
                return note;
            }
        }

        /// <summary>The agent's notes as they stand, for the book questions (list notes; a look-up of an enemy it has a note on).</summary>
        private string NotesNow()
        {
            if (_mem == null) return "";
            _mem.View(out _, out _, out _, out string notes, out _);
            return notes;
        }

        private string TakeNewsNote() { lock (_councilGate) { string n = _newsNote; _newsNote = null; return n; } }
        private string TakeToolsWanted() { lock (_councilGate) { string n = _toolsWanted; _toolsWanted = null; return n; } }

        private static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a.TrimEnd() + "\n\n" + b;

        /// <summary>The names of the tools an answer wrote.</summary>
        private static List<string> ToolNamesOf(JsonArray tools)
            => tools.Select(t => t is JsonObject o ? (string)(o["name"] as JsonValue) : null).Select(n => string.IsNullOrWhiteSpace(n) ? "(unnamed)" : n.Trim()).ToList();

        /// <summary>A name in a text, as a whole word, whatever its case.</summary>
        internal static bool Mentions(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return false;
            int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int end = i + name.Length;
                bool after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (before && after) return true;
                i = text.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>
        /// The notes for a news wake (0.14): the plan and the lessons whole, and the notes on the enemies (lords or realms) the news names or
        /// its theatre holds; hidden: how many were left out.
        /// </summary>
        internal static string FocusNotes(string notes, List<string> names, out int hidden)
        {
            hidden = 0;
            Dictionary<string, string> s = Memory.Sections(notes ?? "");
            List<KeyValuePair<string, string>> enemies = RouteTool.EnemyNotes(notes);
            var keep = enemies.Where(e => names.Any(n => n.Length >= 3 && (string.Equals(e.Key, n, StringComparison.OrdinalIgnoreCase) || Mentions(e.Key, n) || Mentions(n, e.Key)))).ToList();
            hidden = enemies.Count - keep.Count;
            var sb = new StringBuilder();
            sb.Append("## Plan\n").Append(s.TryGetValue("Plan", out string plan) && plan.Length > 0 ? plan : "(not set yet)").Append("\n\n## Enemies (in this news)\n");
            if (keep.Count == 0) sb.Append(enemies.Count == 0 ? "(none noted yet)\n" : "(none of your notes is on an enemy in this news)\n");
            foreach (KeyValuePair<string, string> e in keep) sb.Append("- ").Append(e.Key).Append(": ").Append(e.Value).Append('\n');
            sb.Append("\n## Lessons\n").Append(s.TryGetValue("Lessons", out string lessons) && lessons.Length > 0 ? lessons : "(none yet)");
            return sb.ToString().TrimEnd();
        }

        /// <summary>A replay's memory: the original call's message up to its poke, read in place of this room's own (0.14, replay --calls).</summary>
        internal string ReplayMemory;

        /// <summary>
        /// A recorded call's memory cut to a news wake's (0.14): its notes cut to the enemies of the news, and its chronicle lines since the story
        /// to those about it, as a news wake in theatre mode has them.
        /// </summary>
        internal static string FocusMemory(string memory, List<string> names, List<string> realms)
        {
            string text = memory.Replace("\r", "");
            const string NotesHead = "YOUR NOTES (", GapHead = "YOUR CHRONICLE SINCE THE STORY";
            int n = text.IndexOf(NotesHead, StringComparison.Ordinal);
            if (n >= 0)
            {
                int body = text.IndexOf('\n', n) + 1;
                int end = text.IndexOf("\n\n" + GapHead, body, StringComparison.Ordinal);
                if (end < 0) end = text.TrimEnd().Length;
                string cut = FocusNotes(text.Substring(body, end - body), names.Concat(realms ?? new List<string>()).ToList(), out int hidden);
                text = text.Substring(0, body) + cut + (hidden > 0 ? "\n(Your notes on " + hidden + " other " + (hidden == 1 ? "enemy" : "enemies") + ", not in this news, are kept as they are: list notes shows them all.)" : "")
                       + text.Substring(end);
            }
            int g = text.IndexOf(GapHead, StringComparison.Ordinal);
            if (g >= 0)
            {
                int body = text.IndexOf('\n', g) + 1;
                int end = text.IndexOf("\n\n", body, StringComparison.Ordinal);
                if (end < 0) end = text.Length;
                List<string> lines = text.Substring(body, end - body).Split('\n').ToList();
                List<string> here = lines.Where(l => !l.StartsWith("- ", StringComparison.Ordinal) || names.Any(nm => nm.Length >= 4 && (l.IndexOf("[" + nm + "]", StringComparison.OrdinalIgnoreCase) >= 0 || Mentions(l, nm)))).ToList();
                int elsewhere = lines.Count - here.Count;
                if (elsewhere > 0) here.Add("- (" + elsewhere + " more lines from elsewhere in your realm: ask chronicle about a lord, holding or realm)");
                text = text.Substring(0, body) + string.Join("\n", here) + text.Substring(end);
            }
            return text;
        }

        /// <summary>A poke's question book in the channel (0.14), or null when there is none.</summary>
        private string BookFor(PokeMeta meta, int number)
        {
            string path = Path.Combine(_c.ChannelDir, meta?.Book ?? Path.Combine("book", "poke-" + number + ".json"));
            return File.Exists(path) ? path : null;
        }

        // ---------- reading the channel ----------

        /// <summary>The newest poke: its number from pokes.log, its text from poke.txt (when poke.txt already holds it).</summary>
        private int NewestPoke(out string text, out string when)
        {
            text = when = null;
            int fromLog = LastLogNumber(Path.Combine(_c.ChannelDir, "pokes.log"));
            if (fromLog <= _lastAnswered) return fromLog;
            string poke = ReadShared(Path.Combine(_c.ChannelDir, "poke.txt"));
            if (poke == null) return -1;
            Match m = PokeHead.Match(poke);
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out int n) || n < fromLog) return -1;   // poke.txt is written before its line; wait for it
            text = poke;
            when = m.Groups[2].Value.Trim();
            return n;
        }

        private static int LastLogNumber(string path)
        {
            string all = ReadShared(path);
            if (string.IsNullOrEmpty(all)) return -1;
            int best = -1;
            foreach (string line in all.Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith("#")) continue;
                int sp = l.IndexOf(' ');
                if (sp > 1 && int.TryParse(l.Substring(1, sp - 1), out int n) && n > best) best = n;
            }
            return best;
        }

        /// <summary>The newest poke already answered in the channel (orders\ and orders\done\), so a restart does not answer it twice.</summary>
        private int LastAnsweredOnDisk()
        {
            int best = -1;
            foreach (string dir in new[] { Path.Combine(_c.ChannelDir, "orders"), Path.Combine(_c.ChannelDir, "orders", "done") })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.GetFiles(dir, "poke-*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(f).Substring(5);
                    int dash = n.IndexOf('-');
                    if (dash > 0) n = n.Substring(0, dash);
                    if (int.TryParse(n, out int v) && v > best) best = v;
                }
            }
            // a poke left from before this run is answered only if it is fresh (the game is running and waiting); an old one
            // answers a game that has moved on, so the war room starts with the next one
            int logged = LastLogNumber(Path.Combine(_c.ChannelDir, "pokes.log"));
            string poke = Path.Combine(_c.ChannelDir, "poke.txt");
            bool fresh = File.Exists(poke) && (DateTime.Now - File.GetLastWriteTime(poke)).TotalSeconds < 120;
            return fresh ? best : Math.Max(best, logged);
        }

        public static string ReadShared(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            catch (IOException) { return null; }
        }

        /// <summary>
        /// A section of a report, from its heading to the blank line that ends it. The bridge's reports end their lines in \r\n (0.14.1: this
        /// looked for \n\n, never found one, and read every section to the end of the report; so in run 20261029-1903-A all of Dol Guldur's
        /// 375 answers and 280 of the orcs' 394 counted as urgent, on any word anywhere in the report, and old chronicle lines called councils).
        /// </summary>
        internal static string Section(string text, string head)
        {
            int i = text.IndexOf(head, StringComparison.Ordinal);
            if (i < 0) return "";
            string rest = text.Substring(i).Replace("\r", "");
            int end = rest.IndexOf("\n\n", StringComparison.Ordinal);
            return end < 0 ? rest : rest.Substring(0, end);
        }

        // ---------- the system prompts ----------

        /// <summary>
        /// The system prompt: the agent's instructions and the order library, rewritten only when either changes (so it
        /// stays cached). The memory section says how this memory works (written or conversation); without reading, the
        /// look-ups section goes.
        /// </summary>
        internal string SystemFile(bool routes = false, bool news = false, bool book = false, bool? read = null)
        {
            lock (_systemGate) return SystemFileLocked(routes, news, book, read ?? _c.Lookups);   // the council's thread asks for it too
        }

        /// <summary>
        /// The "ask the way" tool for one call (RouteTool), or null when it cannot be offered: switched off, no map copied yet, or
        /// no view file for the realm. Without it, its section of the instructions goes too.
        /// </summary>
        internal RouteTool Routes(PokeMeta meta, int number, string call, bool live, string book = null)
        {
            if (meta == null) return null;
            // a news wake's questions on its book need no map (0.14): offered alone when the way-finding cannot be
            RouteTool BookOnly() => book == null ? null : new RouteTool(RouteTool.DefaultRoot(), live ? _logDir : Path.Combine(_logDir, "replays"), meta.Campaign, meta.Realm, number, call)
            {
                BookOnly = true, BookPath = book, ChronicleDir = _c.ChannelDir, Notes = NotesNow
            };
            if (!_c.RouteTool) return BookOnly();
            string root = RouteTool.DefaultRoot();
            // a live call's tool can ask the bridge for a try of a tool being written (0.13, check_tool); a replay's cannot
            var tool = new RouteTool(root, live ? _logDir : Path.Combine(_logDir, "replays"), meta.Campaign, meta.Realm, number, call) { ChannelDir = live ? _c.ChannelDir : null };
            if (!tool.Ready(out string why))
            {
                if (_routesWhy != why) { Say("The \"ask the way\" tool is not offered: " + why + "."); _routesWhy = why; }
                return BookOnly();
            }
            tool.BookPath = book;
            tool.ChronicleDir = _c.ChannelDir;
            tool.Notes = NotesNow;
            if (_routesWhy != "ready") { Say("The \"ask the way\" tool is offered in every answer and council."); _routesWhy = "ready"; }
            if ((DateTime.Now - _rulesWritten).TotalMinutes >= 10)
            {
                RouteTool.WriteRules(root, _c.ChannelDir);
                _rulesWritten = DateTime.Now;
            }
            if (live) AtlasIfMissing(tool.MapDir);
            return tool;
        }

        private string _routesWhy;
        private DateTime _rulesWritten = DateTime.MinValue;
        private Thread _atlas;

        /// <summary>
        /// The crossing atlas, worked out in the background when the map has none and the dashboard is not at it, so the tool can
        /// say what closing a crossing would cost. Until then it says that is not worked out yet.
        /// </summary>
        private void AtlasIfMissing(string mapDir)
        {
            if (mapDir == null || File.Exists(BannerlordAgents.Routes.Atlas.PathIn(mapDir)) || (_atlas != null && _atlas.IsAlive)) return;
            _atlas = new Thread(() =>
            {
                try
                {
                    Say("Working out the crossing atlas for the walking map (once per map, in the background)...");
                    bool ok = BannerlordAgents.Routes.Atlas.BuildIfMissing(BannerlordAgents.Routes.WalkMap.Load(mapDir), null, out string said);
                    Say(ok ? "Crossing atlas ready: " + said + "." : "Crossing atlas: " + said + ".");
                }
                catch (Exception e) { Say("The crossing atlas failed: " + e.Message); }
            }) { IsBackground = true, Name = "atlas", Priority = ThreadPriority.Lowest };
            _atlas.Start();
        }

        private readonly Dictionary<string, string> _systemFiles = new Dictionary<string, string>();

        /// <summary>
        /// One of the system prompts, each kept in its own file by its fingerprint (so each stays cached: a news wake's and a council's
        /// differ since 0.14). routes: the way-finding questions are offered; news: a news wake in theatre mode, whose instructions take the
        /// sections of warroom-news.md in place of the same ones (tool writing at a council only, the questions in place of the look-up files);
        /// book: its questions on the book are offered; read: the Read tool (the look-up files).
        /// </summary>
        private string SystemFileLocked(bool routes, bool news, bool book, bool read)
        {
            string template = File.Exists(_c.SystemTemplate) ? File.ReadAllText(_c.SystemTemplate) : "You command a realm in Mount & Blade II: Bannerlord. Answer each poke with one JSON object of orders.";
            if (news) template = NewsInstructions(template);
            // never promise a tool the call does not have ("check a tool" is served beside "ask the way", 0.13)
            if (!routes)
            {
                template = ReplaceSection(template, "## Asking the way", null);
                template = ReplaceSection(template, "## Checking a tool", null);
            }
            if (_c.Conversation) template = ReplaceSection(template, "## Your memory", ConversationMemory);
            // a news wake's "## Look-ups" are its questions on the book; anyone else's, the Read tool's files
            if (news ? !book : !read) template = ReplaceSection(template, "## Look-ups", null);
            // never promise a tool the call does not have: the model would type tool calls as text instead
            if (!read && !routes && !book) template = template.TrimEnd() + "\n\nYou have no tools in this call: answer from the message alone.\n";
            string tools = ReadShared(Path.Combine(_c.ChannelDir, "tools.txt")) ?? "(the order library appears once the game has loaded a campaign)";
            string text = template.TrimEnd() + "\n\n" + tools.Trim() + "\n";
            string hash = Hash(text);
            _systemHash = hash;
            if (_systemFiles.TryGetValue(hash, out string known) && File.Exists(known)) return _systemFile = known;
            _systemFile = Path.Combine(_logDir, "system-" + hash + ".md");
            File.WriteAllText(_systemFile, text);
            _systemFiles[hash] = _systemFile;
            Say("System prompt " + hash + (news ? " (news wakes)" : "") + ": " + text.Length.ToString("N0", CultureInfo.InvariantCulture) + " characters.");
            return _systemFile;
        }

        /// <summary>
        /// The instructions for a news wake (0.14): each "## " section of warroom-news.md in the agent's folder takes the place of the same section
        /// of the instructions ("(removed)": the section goes; a section they lack is added at the end). Without that file, the tool-writing
        /// manual and the tool check go, since a news wake writes no tools.
        /// </summary>
        internal string NewsInstructions(string template)
        {
            string file = File.Exists(_c.NewsTemplate) ? File.ReadAllText(_c.NewsTemplate) : null;
            if (file == null)
            {
                template = ReplaceSection(template, "## Writing your own tools", "## Writing your own tools\n\nYou write and change tools of your own only at a council of war; in a poke, order the tools you have.");
                return ReplaceSection(template, "## Checking a tool", null);
            }
            foreach (KeyValuePair<string, string> s in NewsSections(file))
                template = ReplaceSection(template, s.Key, s.Value.Trim() == "(removed)" ? null : s.Key + "\n\n" + s.Value.Trim());
            return template;
        }

        /// <summary>The "## " sections of the news instructions, in order, heading to body (anything before the first heading is the file's own note).</summary>
        internal static List<KeyValuePair<string, string>> NewsSections(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            string head = null;
            var body = new StringBuilder();
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (head != null) list.Add(new KeyValuePair<string, string>(head, body.ToString()));
                    head = raw.TrimEnd();
                    body.Clear();
                    continue;
                }
                if (head != null) body.Append(raw).Append('\n');
            }
            if (head != null) list.Add(new KeyValuePair<string, string>(head, body.ToString()));
            return list;
        }

        private string ChroniclerFile()
        {
            string text = File.Exists(_c.ChroniclerTemplate) ? File.ReadAllText(_c.ChroniclerTemplate) : DefaultChronicler;
            string hash = Hash(text);
            if (hash == _chroniclerHash && _chroniclerFile != null && File.Exists(_chroniclerFile)) return _chroniclerFile;
            _chroniclerHash = hash;
            _chroniclerFile = Path.Combine(_logDir, "chronicler-" + hash + ".md");
            File.WriteAllText(_chroniclerFile, text);
            return _chroniclerFile;
        }

        /// <summary>A "## heading" section replaced by other text (null: removed), up to the next heading.</summary>
        private static string ReplaceSection(string text, string heading, string with)
        {
            int a = text.IndexOf(heading, StringComparison.Ordinal);
            if (a < 0) return with == null ? text : text.TrimEnd() + "\n\n" + with.TrimEnd() + "\n";
            int b = text.IndexOf("\n## ", a + 3, StringComparison.Ordinal);
            return text.Substring(0, a) + (with != null ? with.TrimEnd() + "\n\n" : "") + (b >= 0 ? text.Substring(b + 1) : "");
        }

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).Substring(0, 12);

        private const string ConversationMemory =
            "## Your memory\n\n"
            + "Your memory is this conversation: every poke comes into it, with your answers. When it grows too long, a new one begins from your notes, "
            + "so keep in your notes, through \"plan\", \"enemies\" and \"lessons\", what you will need later.\n";

        private const string DefaultChronicler =
            "# The chronicler\n\n"
            + "You keep the chronicle of a realm at war in a live game of Mount & Blade II: Bannerlord. Its ruler, another model, decides; you only remember. "
            + "You write the story so far: the one account of the war the ruler reads before every decision.\n\n"
            + "You get the story so far, the ruler's notes (for context), and the chronicle's new lines since the story was last written. Write the new story so far:\n"
            + "- One account of the whole war, within the words you are given: the oldest events most compressed, the last days in most detail.\n"
            + "- Keep the dates (season, day, year) of the turning points.\n"
            + "- Keep what a ruler needs to decide well: who the enemies are, where they strike from and how strong they are; holdings won, lost, besieged or raided, and how each siege ended; "
            + "lords captured, killed or freed; armies formed and broken; which orders worked and which failed, and why; the threats still open.\n"
            + "- Only facts from the story so far and the chronicle. Never invent, never guess numbers, never give advice.\n"
            + "- Plain sentences. Names as they appear, with their realm the first time.\n\n"
            + "Reply with ONLY one JSON object: {\"story\": \"...\"}. When asked, also \"lessons\": the ruler's lessons still worth keeping, merged and shortened, in the ruler's own words.\n";

        // ---------- the answer ----------

        /// <summary>The JSON object in the answer, checked: an object with an orders list whose orders name a lord and an order.</summary>
        internal static string Extract(string text, int number, out JsonObject answer)
        {
            answer = null;
            if (string.IsNullOrWhiteSpace(text)) return "the answer was empty";
            // every complete top-level object in the text; the last one that reads (an answer may correct itself) wins
            JsonObject o = null;
            string lastError = "there is no JSON object in it";
            foreach (string candidate in Objects(text).Reverse())
            {
                try
                {
                    if (JsonNode.Parse(candidate) is JsonObject found && (found["orders"] != null || found["poke"] != null)) { o = found; break; }
                }
                catch (JsonException e) { lastError = "it is not valid JSON (" + e.Message + ")"; }
            }
            if (o == null) return lastError;
            if (o["orders"] == null) o["orders"] = new JsonArray();
            if (o["orders"] is not JsonArray list) return "\"orders\" is not a list";
            int i = 0;
            foreach (JsonNode n in list)
            {
                i++;
                if (n is not JsonObject ord) return "order " + i + " is not an object";
                if (ord["lord"] == null || ord["do"] == null) return "order " + i + " needs \"lord\" and \"do\"";
            }
            o["poke"] = number;
            answer = o;
            return null;
        }

        /// <summary>
        /// Line breaks and tabs typed raw inside a JSON string, which the reader refuses, written as escapes (0.11.3: the orcs'
        /// chronicler put six real line breaks into its story's text, and the whole story was thrown away; an answer's orders
        /// would go the same way).
        /// </summary>
        internal static string EscapeBreaks(string json)
        {
            if (json.IndexOf('\n') < 0 && json.IndexOf('\r') < 0 && json.IndexOf('\t') < 0) return json;
            var sb = new StringBuilder(json.Length + 16);
            bool inString = false, escape = false;
            foreach (char c in json)
            {
                if (inString)
                {
                    if (escape) { escape = false; sb.Append(c); continue; }
                    if (c == '\\') { escape = true; sb.Append(c); continue; }
                    if (c == '"') { inString = false; sb.Append(c); continue; }
                    if (c == '\n') { sb.Append("\\n"); continue; }
                    if (c == '\r') { sb.Append("\\r"); continue; }
                    if (c == '\t') { sb.Append("\\t"); continue; }
                    sb.Append(c);
                    continue;
                }
                if (c == '"') inString = true;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The balanced { ... } spans at the top level of a text, quotes and escapes respected.</summary>
        private static IEnumerable<string> Objects(string text)
        {
            int depth = 0, start = -1;
            bool inString = false, escape = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"' && depth > 0) { inString = true; continue; }
                if (c == '{') { if (depth == 0) start = i; depth++; }
                else if (c == '}' && depth > 0)
                {
                    depth--;
                    if (depth == 0 && start >= 0) { yield return Room.EscapeBreaks(text.Substring(start, i - start + 1)); start = -1; }
                }
            }
        }

        /// <summary>
        /// The answer's orders into orders\poke-N.json (a council's: poke-N-council.json), swapped in whole for the bridge to read; since
        /// 0.13 with its tools, diplomatic acts and letters, and the folder of the call that wrote it (the episode index names it).
        /// </summary>
        private void WriteOrders(int number, JsonObject answer, string suffix = null, string call = null)
        {
            string dir = Path.Combine(_c.ChannelDir, "orders");
            Directory.CreateDirectory(dir);
            var file = new JsonObject { ["poke"] = number, ["orders"] = answer["orders"]?.DeepClone() ?? new JsonArray() };
            if (suffix != null) file["kind"] = suffix;
            if (call != null) file["call"] = call;
            foreach (string k in new[] { "tools", "diplomacy", "letters" })
                if (answer[k] is JsonArray list && list.Count > 0) file[k] = list.DeepClone();
            if (answer["note"] != null) file["note"] = answer["note"].DeepClone();
            else if (answer["plan"] != null) file["note"] = answer["plan"].DeepClone();
            string name = "poke-" + number + (suffix != null ? "-" + suffix + "-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) : "");
            string final = Path.Combine(dir, name + ".json");
            string temp = Path.Combine(dir, name + ".json.part");
            File.WriteAllText(temp, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, final, true);
        }

        // ---------- helpers and logs ----------

        /// <summary>The thinking's summary of a call (0.13), kept beside its prompt and answer as thinking.txt.</summary>
        internal static void KeepThinking(string folder, CallResult r)
        {
            if (folder == null || r == null || string.IsNullOrWhiteSpace(r.ThinkingText)) return;
            try { File.WriteAllText(Path.Combine(folder, "thinking.txt"), r.ThinkingText.Trim() + "\n", new UTF8Encoding(false)); }
            catch (IOException) { }
        }

        /// <summary>The order library's fingerprint as the call read it (tools.txt; 0.13: which tools a call had, the realm's own among them).</summary>
        private string LibraryHash()
        {
            string tools = ReadShared(Path.Combine(_c.ChannelDir, "tools.txt"));
            return tools == null ? null : Hash(tools);
        }

        private static int Count(JsonObject answer, string list) => answer?[list] is JsonArray a ? a.Count : 0;

        public static int Words(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

        private static double Num(JsonNode n)
        {
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return d;
                if (v.TryGetValue(out long l)) return l;
            }
            return double.NaN;
        }

        private void Log(object record)
        {
            string line = JsonSerializer.Serialize(record);
            lock (LogGate) File.AppendAllText(Path.Combine(_logDir, "calls.jsonl"), line + "\n");
        }

        public void Say(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            lock (LogGate)
            {
                Console.WriteLine(line);
                try { File.AppendAllText(Path.Combine(_logDir, "warroom-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + Environment.NewLine); }
                catch (IOException) { }
            }
        }
    }
}
