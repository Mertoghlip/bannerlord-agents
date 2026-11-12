using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// A continuous agent's conversation (0.18), as the war room keeps it between calls, in the campaign's memory folder (conversation.json): Claude Code's session,
    /// the system prompt it began with (kept for its whole life, so the cache holds), whether it was offered the way-finding questions, its size, the sections of the
    /// report it has already been shown, its own tools as it was last told them, its wakes by in-game day, and its strategy reviews' hours.
    /// </summary>
    internal sealed class Talk
    {
        public string SessionId, SystemFile, SystemHash, LoadId;
        public bool Started, Routes;
        public long Tokens;
        /// <summary>
        /// The conversation's cost so far as Claude Code reports it: a resumed session's total_cost_usd is the whole session's (seen Nov 10: two calls, 0.086 then 0.094),
        /// so each call's own cost is the difference.
        /// </summary>
        public double CostSoFar;
        public int Messages, Wakes, Reviews, Number;
        public double StartedHours = double.NaN, LastReviewHours = double.NaN, NextReviewHours = double.NaN;
        /// <summary>The continuous lieutenant (0.18.4): the latest in-game hour its conversations reached in this save's line, kept through a handover, so an earlier save's load is known.</summary>
        public double LastHours = double.NaN;
        public string StartedWall = "", StartedWhy = "";
        /// <summary>Why the next conversation begins (the handover that ended the last one, a reload), told at its start.</summary>
        public string NextWhy;
        /// <summary>Each section of a report shown in this conversation, by heading: its text's fingerprint and the in-game hour it was last shown.</summary>
        public Dictionary<string, string> Sent = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, double> SentAt = new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>The long explanatory lines already shown (a section's rules of the game, the same every time), by fingerprint.</summary>
        public HashSet<string> Static = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>The agent's own tools as this conversation knows them ("name vN").</summary>
        public List<string> OwnTools = new List<string>();
        /// <summary>Wakes by in-game day (the day's number), for "wakes today".</summary>
        public Dictionary<string, int> WakesByDay = new Dictionary<string, int>(StringComparer.Ordinal);

        [System.Text.Json.Serialization.JsonIgnore] public string Path;

        public static Talk Load(string dir)
        {
            string path = System.IO.Path.Combine(dir, "conversation.json");
            Talk t = null;
            try { if (File.Exists(path)) t = JsonSerializer.Deserialize<Talk>(File.ReadAllText(path), Options); }
            catch (JsonException) { t = null; }
            t ??= new Talk();
            t.Path = path;
            t.Sent ??= new Dictionary<string, string>(StringComparer.Ordinal);
            t.SentAt ??= new Dictionary<string, double>(StringComparer.Ordinal);
            t.Static ??= new HashSet<string>(StringComparer.Ordinal);
            t.OwnTools ??= new List<string>();
            t.WakesByDay ??= new Dictionary<string, int>(StringComparer.Ordinal);
            return t;
        }

        public void Save()
        {
            if (Path == null) return;
            Memory.Write(Path, JsonSerializer.Serialize(this, Options));
        }

        /// <summary>What the conversation has been shown, copied before a message is built: a call that never reached the conversation gives it back (Restore).</summary>
        public object[] Mark() => new object[] { new Dictionary<string, string>(Sent), new Dictionary<string, double>(SentAt), new HashSet<string>(Static), new List<string>(OwnTools) };

        public void Restore(object[] mark)
        {
            Sent = (Dictionary<string, string>)mark[0];
            SentAt = (Dictionary<string, double>)mark[1];
            Static = (HashSet<string>)mark[2];
            OwnTools = (List<string>)mark[3];
        }

        /// <summary>The conversation ends (a handover, a reload): the next wake begins a new one, told why.</summary>
        public void Reset(string why)
        {
            SessionId = SystemFile = SystemHash = null;
            Started = Routes = false;
            Tokens = 0;
            CostSoFar = 0;
            Messages = Wakes = Reviews = 0;
            StartedHours = LastReviewHours = NextReviewHours = double.NaN;
            StartedWall = StartedWhy = "";
            Sent.Clear();
            SentAt.Clear();
            Static.Clear();
            OwnTools.Clear();
            NextWhy = why;
            Save();
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true, IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
    }

    /// <summary>
    /// Continuous agents (0.18, docs/WARROOM.md#continuous-agents-since-018): one conversation per realm instead of a fresh call per poke. The game wakes the agent only
    /// on its own watches, their check-ins, the lords they follow and the locked wakes (agent.txt: wake = self); each wake's message is the news and what bears on it,
    /// less what the conversation already holds (Slim). Strategy reviews sit in the same conversation, at the hour the agent sets (review_in), or at once after a big event.
    /// The conversation ends only with the agent's handover, written as the player's battle begins (the clock stands still) or as the game closes, once it has
    /// grown past its size (compact_at_tokens, compact_at_close_tokens), or at any wake past compact_hard_tokens; the next wake begins a new conversation from the
    /// handover, the notes, the chronicle since and a fresh briefing. Every call of a conversation has the same system prompt, tools, model, effort and thinking cap,
    /// so each reads the conversation from the cache (the one-hour cache, Claude Code's own compaction off).
    /// </summary>
    internal sealed partial class Room
    {
        private Talk _talk;
        /// <summary>A big event's line, when one in a wake's news calls a strategy review (council_after); cleared when a review sits.</summary>
        private string _reviewWanted;
        // the newest hour asked of the game before a question (FreshNow): the fresh book, and when it came
        private string _freshPath;
        private DateTime _freshAt = DateTime.MinValue;
        private DateTime _reviewFailedAt = DateTime.MinValue, _gameLooked = DateTime.MinValue, _spareWord = DateTime.MinValue;
        private bool _gameSeen;

        /// <summary>The settings every call of a continuous conversation carries: the one-hour cache, and Claude Code's own compaction off.</summary>
        internal static readonly Dictionary<string, string> ContinuousEnv = new Dictionary<string, string> { ["CLAUDE_CODE_PROMPT_CACHE_TTL"] = "1h", ["DISABLE_AUTO_COMPACT"] = "1" };

        // ---------- the loop ----------

        public int RunContinuous(CancellationToken stop)
        {
            Say("War room " + typeof(Room).Assembly.GetName().Version + ": a continuous agent in " + _c.ChannelDir);
            Say("Model " + _c.ContinuousModel + ", effort " + _c.ContinuousEffort + ", thinking up to " + _c.ContinuousThinkingCap + " tokens a call, the same for every call of a conversation.");
            Say("One conversation, handed over at the player's battle past " + _c.CompactAtTokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens, at the game's close past "
                + _c.CompactAtCloseTokens.ToString("N0", CultureInfo.InvariantCulture) + ", at any wake past " + _c.CompactHardTokens.ToString("N0", CultureInfo.InvariantCulture) + ". Ctrl+C stops it.");
            _lastAnswered = LastAnsweredOnDisk();
            int waitingSaid = -2;
            while (!stop.IsCancellationRequested)
            {
                int newest = NewestPoke(out string text, out string when);
                if (newest > _lastAnswered && text != null)
                {
                    try { Wake(newest, when, text); }
                    catch (Exception e) when (e is IOException || e is InvalidOperationException || e is JsonException || e is UnauthorizedAccessException)
                    {
                        Say("Wake #" + newest + ": it could not be answered (" + e.Message + ").");
                    }
                    _lastAnswered = newest;
                    continue;
                }
                try
                {
                    HandoverIfDue();
                    ReviewIfDue();
                }
                catch (Exception e) when (e is IOException || e is InvalidOperationException || e is JsonException || e is UnauthorizedAccessException)
                {
                    _reviewFailedAt = DateTime.Now;
                    Say("Continuous: " + e.Message + "; tried again in five minutes.");
                }
                if (waitingSaid != _lastAnswered) { Say("Waiting for the next wake (last answered #" + _lastAnswered + (_talk != null && _talk.Started ? "; the conversation holds " + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens" : "") + ")."); waitingSaid = _lastAnswered; }
                stop.WaitHandle.WaitOne(250);
            }
            Say("Stopped.");
            return 0;
        }

        // ---------- a wake ----------

        /// <summary>One wake answered in the conversation: begun with the opening when there is none yet (or it was handed over).</summary>
        internal CallResult Wake(int number, string when, string pokeText)
        {
            DateTime noticed = DateTime.Now;
            PokeMeta meta = ReadMeta(number);
            if (meta != null && !double.IsNaN(meta.Hours)) _pokeHours = meta.Hours;
            string carried = EnsureContinuous(meta);
            if (meta != null) { _mem.Run = meta.Run; _mem.Arm = meta.Arm; }
            string why = Section(pokeText, "WHY YOU ARE ASKED NOW");
            // past the hard ceiling: the handover first, and this wake opens the next conversation
            if (_talk.Started && _talk.Tokens >= _c.CompactHardTokens) Handover("this conversation is near the end of what one conversation can hold", meta);
            bool opening = !_talk.Started;
            bool firstLook = why.IndexOf("First look", StringComparison.OrdinalIgnoreCase) >= 0;
            if (opening) { _talk.Sent.Clear(); _talk.SentAt.Clear(); _talk.Static.Clear(); }
            object[] mark = _talk.Mark();
            var sb = new StringBuilder();
            if (opening) sb.Append(Opening(meta, number, firstLook ? null : "the wake that brought you here", out _));
            string notes = Join(carried, OwnToolsNote());
            if (!string.IsNullOrEmpty(notes)) sb.AppendLine(notes.TrimEnd()).AppendLine();
            sb.Append(Slim(pokeText, meta?.Hours ?? _pokeHours, opening));
            string day = DayKey(meta?.Hours ?? _pokeHours);
            _talk.WakesByDay.TryGetValue(day, out int today);
            sb.AppendLine().AppendLine("Wakes today (this in-game day): " + (today + 1) + ", this one included; keep to about " + _c.WakeBudget + " a day.");
            string prompt = sb.ToString().Replace("\r\n", "\n");

            CallResult r = Converse(prompt, opening ? "opening" : "wake", number, opening || firstLook ? Math.Max(_c.ContinuousTimeoutSeconds, _c.CouncilTimeoutSeconds) : _c.ContinuousTimeoutSeconds, meta, out string folder, out bool retriedStall);
            if (!Reached(r)) _talk.Restore(mark);
            JsonObject answer = null;
            string problem = r.Ok ? Extract(r.Text, number, out answer) : r.Error;
            bool retried = false;
            if (answer == null && r.Ok)
            {
                retried = true;
                CallResult r2 = Converse("Your answer to wake #" + number + " could not be read: " + problem + ". Reply again with ONLY the JSON object, nothing before or after it.", "wake-retry", number, _c.ContinuousTimeoutSeconds, meta, out _, out _);
                problem = r2.Ok ? Extract(r2.Text, number, out answer) : r2.Error;
                r.Seconds += r2.Seconds;
                r.OutTokens += r2.OutTokens;
                r.Thinking += r2.Thinking;
                r.Cost += r2.Cost;
            }
            string changed = "";
            int orders = answer?["orders"] is JsonArray arr ? arr.Count : 0;
            if (answer != null)
            {
                WriteOrders(number, answer, call: folder);
                changed = Remember(answer, number, when, meta);
                AfterContinuous(answer, meta?.Hours ?? _pokeHours, opening);
                File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            // a big event calls a strategy review at once (council_after; not the first look, whose report is the whole realm already): kept in 0.18.2's agent-set reviews
            if (!firstLook)
                foreach (string w in _c.CouncilAfter)
                {
                    if (w.StartsWith("First look", StringComparison.OrdinalIgnoreCase)) continue;
                    int i = why.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    string line = why.Substring(why.LastIndexOf('\n', i) + 1);
                    int end = line.IndexOf('\n');
                    _reviewWanted = (end > 0 ? line.Substring(0, end) : line).Trim().TrimStart('-', ' ');
                    break;
                }
            _talk.WakesByDay[day] = today + 1;
            if (_talk.WakesByDay.Count > 30) foreach (string k in _talk.WakesByDay.Keys.OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).Take(_talk.WakesByDay.Count - 30).ToList()) _talk.WakesByDay.Remove(k);
            _talk.Wakes++;
            _talk.Number = number;
            _talk.Save();
            _mem.Save();
            double seconds = (DateTime.Now - noticed).TotalSeconds;
            LogCall("answer", "wake", r, number, when, meta, folder, answer, problem, retried, retriedStall, seconds, prompt.Length, opening, why: null, changed: changed);
            if (answer != null)
                Say("Wake #" + number + (opening ? " (a new conversation)" : "") + ": " + orders + " order" + (orders == 1 ? "" : "s") + (Count(answer, "watch") > 0 ? ", " + Count(answer, "watch") + " watch" + (Count(answer, "watch") == 1 ? "" : "es") : "")
                    + " in " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s; the conversation holds " + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens" + (changed.Length > 0 ? ". Notes: " + changed : "") + ".");
            else Say("Wake #" + number + ": NO ANSWER (" + problem + ") after " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");
            _lastCallEnded = DateTime.Now;
            return r;
        }

        /// <summary>The range of review_in, in in-game hours: at most one review an hour, and none set further than ten days ahead.</summary>
        internal const double ReviewInMin = 1, ReviewInMax = 240;

        /// <summary>
        /// What an answer sets beyond its orders: the next strategy review's hour (review_in, or council_in), and for a review's answer, that a review sat. Since 0.18.2 only the
        /// agent sets when a review sits: a review that sets no next hour leaves none set (one set earlier by a wake's answer stands).
        /// </summary>
        private void AfterContinuous(JsonObject answer, double hours, bool review)
        {
            double? next = null;
            foreach (string k in new[] { "review_in", "council_in" })
                if (answer[k] is JsonValue v && v.TryGetValue(out double h)) { next = Math.Max(ReviewInMin, Math.Min(ReviewInMax, h)); break; }
            if (double.IsNaN(hours)) return;
            if (review)
            {
                _talk.LastReviewHours = hours;
                _talk.Reviews++;
                _talk.NextReviewHours = next != null ? hours + next.Value : double.NaN;
                _reviewWanted = null;
            }
            else if (next != null) _talk.NextReviewHours = hours + next.Value;
        }

        // ---------- the conversation's calls ----------

        /// <summary>
        /// One call in the conversation: begun under a new session (the first) or resumed, with the system prompt and tools it began with, its one effort and thinking cap,
        /// the one-hour cache and Claude Code's compaction off. A call that stalls or runs away is asked once more. The conversation's size is kept after each.
        /// </summary>
        private CallResult Converse(string prompt, string kind, int number, int timeout, PokeMeta meta, out string folder, out bool retriedStall)
        {
            retriedStall = false;
            bool fresh = !_talk.Started;
            if (fresh)
            {
                _talk.SessionId = Guid.NewGuid().ToString();
                _mem.SessionId = _talk.SessionId;
                _mem.SessionStarted = false;
                RouteTool probe = Routes(meta ?? ReadMeta(-1), number, "opening", true);
                _talk.Routes = probe != null && !probe.BookOnly;
                _talk.SystemFile = SystemFile(_talk.Routes, false, true, false);
                _talk.SystemHash = _systemHash;
                _talk.StartedHours = meta?.Hours ?? _pokeHours;
                _talk.StartedWall = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _talk.StartedWhy = _talk.NextWhy ?? "";
                _talk.LoadId = meta?.LoadId ?? _mem.LoadId;
                _talk.OwnTools = OwnToolNames(ReadShared(Path.Combine(_c.ChannelDir, "tools.txt")) ?? "");
                _talk.Save();
            }
            else if (_talk.SystemFile == null || !File.Exists(_talk.SystemFile))
            {
                // its system prompt's file was cleared away: the same text written again keeps the cache only if nothing in it changed
                _talk.SystemFile = SystemFile(_talk.Routes, false, true, false);
                if (_systemHash != _talk.SystemHash) Say("Continuous: the system prompt this conversation began with is gone and its text has changed since; the next call writes the conversation into the cache again.");
                _talk.SystemHash = _systemHash;
            }
            RouteTool tools = ContinuousTools(meta ?? ReadMeta(-1), number, kind);
            folder = Path.Combine(_logDir, "calls", DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss") + "-" + kind + "-" + number);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "prompt.txt"), prompt);
            Say((fresh ? "A new conversation" : "Conversation (" + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens)") + ": " + kind + " #" + number + "...");
            CallResult r = Claude.Run(_claude, _talk.SystemFile, prompt, _c.ContinuousModel, _c.ContinuousEffort, _c.ChannelDir, false, timeout, _c.ContinuousThinkingCap,
                                      _talk.SessionId, !fresh, tools: tools, env: ContinuousEnv, stallSeconds: kind.StartsWith("handover", StringComparison.Ordinal) ? Claude.HandoverStallSeconds : 0);
            if (r.HandshakeLate) Say(kind + " #" + number + ": Claude Code took over 30 s to answer the tools' handshake; the message went anyway.");
            if (!r.Ok && (r.Stalled || r.Runaway))
            {
                File.WriteAllText(Path.Combine(folder, "raw-stalled.jsonl"), r.Raw ?? "");
                Say(kind + " #" + number + ": the call " + (r.Runaway ? "ran away" : "stalled") + " after " + r.Seconds.ToString("0", CultureInfo.InvariantCulture) + " s; asking again.");
                double lost = r.Seconds;
                retriedStall = true;
                if (fresh) { _talk.SessionId = Guid.NewGuid().ToString(); _mem.SessionId = _talk.SessionId; }   // a conversation's first call is begun again under a new id
                r = Claude.Run(_claude, _talk.SystemFile, prompt, _c.ContinuousModel, _c.ContinuousEffort, _c.ChannelDir, false, timeout, _c.ContinuousThinkingCap,
                               _talk.SessionId, !fresh, tools: tools, env: ContinuousEnv, stallSeconds: kind.StartsWith("handover", StringComparison.Ordinal) ? Claude.HandoverStallSeconds : 0);
                r.Seconds += lost;
            }
            File.WriteAllText(Path.Combine(folder, kind.EndsWith("retry", StringComparison.Ordinal) ? "raw-retry.jsonl" : "raw.jsonl"), r.Raw ?? "");
            KeepThinking(folder, r);
            // the session's running total, as Claude Code reports a resumed session's cost: this call's own is what it added
            if (r.Cost > 0)
            {
                double total = r.Cost;
                r.Cost = total >= _talk.CostSoFar - 1e-9 ? total - _talk.CostSoFar : total;
                _talk.CostSoFar = total;
            }
            if (r.Ok || (!fresh && r.Raw != null && r.Raw.Contains("\"type\":\"assistant\"")))
            {
                _talk.Started = true;
                _mem.SessionStarted = true;
                _talk.Messages++;
                long size = r.LastContext > 0 ? r.LastContext : r.InTokens + r.CacheRead + r.CacheWrite + r.OutTokens;
                if (size > 0) _talk.Tokens = size;
                _mem.SessionTokens = _talk.Tokens;
            }
            _talk.Save();
            _lastCallEnded = DateTime.Now;
            return r;
        }

        /// <summary>
        /// The questions every call of the conversation is offered, the same set each time (a different set would make the next call write the conversation into the cache
        /// again): the way-finding questions when the conversation began with them, check_tool, and the question book's, read from the newest book in the channel.
        /// </summary>
        private RouteTool ContinuousTools(PokeMeta meta, int number, string call)
        {
            if (meta == null) return null;
            return new RouteTool(RouteTool.DefaultRoot(), _logDir, meta.Campaign, meta.Realm, number, call)
            {
                ChannelDir = _c.ChannelDir, BookPath = NewestBook(meta, number), ChronicleDir = _c.ChannelDir, Notes = NotesNow, BookOnly = !_talk.Routes, Refresh = FreshNow
            };
        }

        /// <summary>The name the bridge looks for in the orders folder (its Channel.BookRequest), how long a question waits for the game, and how long a fresh book serves the next questions.</summary>
        internal const string BookRequest = "book-request";
        internal static TimeSpan FreshWait = TimeSpan.FromSeconds(8);
        internal const double FreshReuseSeconds = 2;

        /// <summary>
        /// The game's newest hour before a question of the agent's (0.18.1): asks the game to write the question book and the view file afresh from the newest hour it has
        /// copied (book-request in the orders folder), and waits for book\now.txt to name the request. The fresh book's path, or null when the game is not running or did not
        /// answer in time (the call's own book and view then answer). Questions asked together within two seconds share one fresh book.
        /// </summary>
        internal string FreshNow()
        {
            if (_freshPath != null && (DateTime.Now - _freshAt).TotalSeconds < FreshReuseSeconds) return _freshPath;
            if (!Fresh(out _) || !GameRunning()) return null;
            string orders = Path.Combine(_c.ChannelDir, "orders"), book = Path.Combine(_c.ChannelDir, "book");
            Directory.CreateDirectory(orders);
            string id = BookRequest + "-" + DateTime.UtcNow.ToString("HHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string tmp = Path.Combine(_c.ChannelDir, id + ".json.part"), request = Path.Combine(orders, id + ".json");
            File.WriteAllText(tmp, "{\"asked\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}");
            File.Move(tmp, request);
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < FreshWait)
            {
                string[] stamp = (ReadShared(Path.Combine(book, "now.txt")) ?? "").Replace("\r", "").Split('\n');
                if (stamp.Length > 0 && stamp[0] == id)
                {
                    string problem = stamp.Length > 3 ? stamp[3].Trim() : "";
                    if (problem.Length > 0) { Say("Fresh book: the game could not write it (" + problem + ")."); return null; }
                    _freshPath = Path.Combine(book, "now.json");
                    _freshAt = DateTime.Now;
                    return _freshPath;
                }
                Thread.Sleep(50);
            }
            try { File.Delete(request); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Say("Fresh book: the game did not answer within " + (int)FreshWait.TotalSeconds + " s; the question is answered from the book the call began with.");
            return null;
        }

        /// <summary>The question book of this poke, else the newest one in the channel, else this poke's path (the book's questions then say none is written yet).</summary>
        private string NewestBook(PokeMeta meta, int number)
        {
            string own = BookFor(meta, number);
            if (own != null) return own;
            string dir = Path.Combine(_c.ChannelDir, "book");
            try
            {
                if (Directory.Exists(dir))
                {
                    string newest = Directory.GetFiles(dir, "poke-*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                    if (newest != null) return newest;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Path.Combine(dir, "poke-" + number + ".json");
        }

        // ---------- the opening of a conversation ----------

        /// <summary>
        /// The first message of a new conversation, before its wake or review: why it begins, the agent's handover (or the old story so far), its notes, its chronicle since
        /// the handover, and the whole realm now (the briefing, and the situation report unless the message that follows is itself the whole report: a first look).
        /// </summary>
        internal string Opening(PokeMeta meta, int number, string follows, out int gapLines)
        {
            gapLines = 0;
            _mem.View(out string story, out double storyHours, out string storyDate, out string notes, out _);
            var sb = new StringBuilder();
            string why = string.IsNullOrWhiteSpace(_talk.NextWhy) ? "the first of your realm's conversations" : _talk.NextWhy.Trim().TrimEnd('.');
            sb.AppendLine("A NEW CONVERSATION BEGINS: " + why + ".");
            sb.AppendLine("From here on this conversation is your memory: every wake, every strategy review and every answer of yours stays in it until your next handover.");
            sb.AppendLine();
            bool handover = story.StartsWith("HANDOVER", StringComparison.Ordinal);
            if (story.Length > 0)
            {
                sb.AppendLine(handover ? "YOUR HANDOVER (written by you, as of " + storyDate + ")" : "THE STORY SO FAR (as of " + storyDate + ", written by your chronicler before you kept your own memory)");
                sb.AppendLine(story.Trim());
            }
            else sb.AppendLine("(No handover yet: this is your realm's first conversation. Your notes and the report below are your memory.)");
            sb.AppendLine();
            sb.AppendLine("YOUR NOTES (your plan, what you know of each enemy, your lessons; change them with \"plan\", \"enemies\" and \"lessons\")");
            sb.AppendLine(notes);
            sb.AppendLine();
            List<ChronicleLine> gap = Gap(storyHours, meta);
            gapLines = gap.Count;
            if (gap.Count > 0)
            {
                sb.AppendLine("YOUR CHRONICLE SINCE " + (handover ? "THE HANDOVER" : story.Length > 0 ? "THE STORY" : "IT BEGAN") + " (" + gap.Count + " lines, oldest first)");
                int skip = Math.Max(0, gap.Count - MaxGapLines);
                if (skip > 0) sb.AppendLine("- (" + skip + " earlier lines: ask chronicle about a lord, holding or realm)");
                foreach (ChronicleLine l in gap.Skip(skip)) sb.AppendLine("- " + l.Date + " · " + l.Text);
                sb.AppendLine();
            }
            bool briefed = Fresh(out _) && BriefingFor(false);
            string briefing = briefed ? ReadShared(Path.Combine(_c.ChannelDir, "council.txt")) ?? "" : "";
            string situation = briefed && follows != null ? ReadShared(Path.Combine(_c.ChannelDir, "report.txt")) ?? "" : "";
            if (briefing.Length > 0 || situation.Length > 0)
            {
                sb.AppendLine("THE WHOLE REALM NOW (written for the start of this conversation)");
                if (briefing.Length > 0) sb.AppendLine(briefing.Trim()).AppendLine();
                if (situation.Length > 0) sb.AppendLine(Trim(situation, meta?.Hours ?? _pokeHours, review: false, opening: true).Trim()).AppendLine();
            }
            sb.AppendLine("Begin with a strategy review in this answer (see \"Strategy reviews\" in your instructions): set your watches, and \"review_in\" for your next review (nothing else calls one). " + (follows != null ? "The " + follows + " follows." : "Your first look follows."));
            sb.AppendLine();
            return sb.ToString();
        }

        // ---------- what a wake's message keeps ----------

        /// <summary>Sections a wake never repeats (the conversation already holds them): the chronicle's last two days.</summary>
        private static readonly string[] WakeDrops = { "RECENT HISTORY" };
        /// <summary>Sections a wake shows only this many in-game hours after it last showed them (unless they are the news's own): each region in a line, the purse.</summary>
        private static readonly Dictionary<string, double> WakeEvery = new Dictionary<string, double> { ["THE REST OF THE WAR"] = 12, ["YOUR PURSE"] = 24 };
        /// <summary>Sections a review leaves out when the briefing already has them.</summary>
        private static readonly string[] ReviewDups = { "YOUR PURSE", "YOUR ENEMY, THE HUMAN PLAYER", "YOUR CAPTURED LORDS" };

        /// <summary>
        /// A wake's message (0.18): the poke less what this conversation already holds. Kept whole: why it is woken, its watches, the clock and what changed. Left out: the
        /// chronicle's last two days. Shown again only after some hours: each region in a line, the purse. Any other section exactly as last shown becomes one line, and the
        /// long rule paragraphs a section opens with are shown once a conversation.
        /// </summary>
        internal string Slim(string pokeText, double hours, bool opening)
        {
            var sb = new StringBuilder();
            List<KeyValuePair<string, string>> blocks = Blocks(pokeText);
            foreach (KeyValuePair<string, string> b in blocks)
            {
                string head = b.Key, body = b.Value;
                if (head == null) { sb.AppendLine(body.TrimEnd()).AppendLine(); continue; }
                string key = Key(head);
                if (!opening && WakeDrops.Any(d => key.StartsWith(d, StringComparison.Ordinal))) continue;
                if (!opening && Always(key) == false)
                {
                    KeyValuePair<string, double> every = WakeEvery.FirstOrDefault(kv => key.StartsWith(kv.Key, StringComparison.Ordinal));
                    if (every.Key != null && _talk.SentAt.TryGetValue(every.Key, out double at) && !double.IsNaN(hours) && hours - at < every.Value)
                    {
                        sb.AppendLine(head + ": not repeated (shown " + Math.Round(hours - at) + " hours ago in this conversation; ask list for it now).").AppendLine();
                        continue;
                    }
                }
                sb.AppendLine(Shown(head, body, hours, mayShort: !opening && Always(key) == false)).AppendLine();
            }
            return sb.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
        }

        /// <summary>The situation report for a review or an opening: less what the briefing has (a review), less the chronicle's last two days (a review: the conversation holds them).</summary>
        internal string Trim(string report, double hours, bool review, bool opening)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> b in Blocks(report))
            {
                if (b.Key == null) { sb.AppendLine(b.Value.TrimEnd()).AppendLine(); continue; }
                string key = Key(b.Key);
                if (review && (ReviewDups.Any(d => key.StartsWith(d, StringComparison.Ordinal)) || key.StartsWith("RECENT HISTORY", StringComparison.Ordinal))) continue;
                sb.AppendLine(Shown(b.Key, b.Value, hours, mayShort: review && !opening)).AppendLine();
            }
            return sb.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
        }

        /// <summary>A section as this message shows it: one line when it is exactly as the conversation last saw it (mayShort), and without the rule paragraphs it already saw.</summary>
        private string Shown(string head, string body, double hours, bool mayShort) => ShownIn(_talk, head, body, hours, mayShort);

        /// <summary>Shown, for any conversation (the continuous lieutenant's too, 0.18.4).</summary>
        internal static string ShownIn(Talk talk, string head, string body, double hours, bool mayShort)
        {
            string key = Key(head);
            string hash = Hash(body.Trim());
            if (mayShort && talk.Sent.TryGetValue(key, out string was) && was == hash)
                return head + ": as in your last message that showed it.";
            talk.Sent[key] = hash;
            talk.SentAt[key] = hours;
            var sb = new StringBuilder();
            sb.Append(head);
            foreach (string line in body.Split('\n'))
            {
                string l = line.TrimEnd();
                if (l.Length == 0) continue;
                // a rule paragraph (a long line that is no list item): once a conversation
                if (l.Length >= 160 && !l.StartsWith("-", StringComparison.Ordinal) && !l.StartsWith(" ", StringComparison.Ordinal))
                {
                    string h = Hash(l);
                    if (!talk.Static.Add(h)) continue;
                }
                sb.Append('\n').Append(l);
            }
            return sb.ToString();
        }

        /// <summary>The sections every wake shows whole (true), or that may be shortened (false).</summary>
        private static bool Always(string key) =>
            key.StartsWith("WHY YOU ARE ASKED NOW", StringComparison.Ordinal) || key.StartsWith("YOUR WATCHES", StringComparison.Ordinal) || key.StartsWith("THE CLOCK", StringComparison.Ordinal)
            || key.StartsWith("WHAT CHANGED", StringComparison.Ordinal) || key.StartsWith("REPORT FOR", StringComparison.Ordinal) || key.StartsWith("POKE #", StringComparison.Ordinal);

        /// <summary>A section's heading without its counts and dates in brackets, so "YOUR ORDERS IN FORCE HERE (18 of 50)" and "(17 of 50)" are one section.</summary>
        internal static string Key(string head)
        {
            int b = head.IndexOf(" (", StringComparison.Ordinal);
            string k = (b > 0 ? head.Substring(0, b) : head).Trim();
            int c = k.IndexOf(',');
            if (k.StartsWith("REPORT FOR", StringComparison.Ordinal) || k.StartsWith("POKE #", StringComparison.Ordinal)) return c > 0 ? k.Substring(0, c) : k;
            return k;
        }

        /// <summary>A report's blocks, split at its blank lines: each with its heading (its first line, when that is a heading) and the rest, or a null heading and all of it.</summary>
        internal static List<KeyValuePair<string, string>> Blocks(string text)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (string block in (text ?? "").Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string b = block.Trim('\n');
                if (b.Trim().Length == 0) continue;
                int nl = b.IndexOf('\n');
                string first = (nl < 0 ? b : b.Substring(0, nl)).Trim();
                list.Add(IsHeading(first) ? new KeyValuePair<string, string>(first, nl < 0 ? "" : b.Substring(nl + 1)) : new KeyValuePair<string, string>(null, b));
            }
            return list;
        }

        /// <summary>A report's section heading: its words before any bracket in capitals ("HOLDINGS HERE (3 of your 55, ...)"); never a list item or the poke's own first line.</summary>
        internal static bool IsHeading(string line)
        {
            if (line.Length < 4 || line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("POKE #", StringComparison.Ordinal)) return false;
            int b = line.IndexOf(" (", StringComparison.Ordinal);
            string words = b > 0 ? line.Substring(0, b) : line;
            return words.Count(char.IsLetter) >= 4 && !words.Any(char.IsLower);
        }

        // ---------- its own tools, told when they change ----------

        private static readonly Regex OwnTool = new Regex(@"^- \*\*([a-z0-9_]+)\*\* \(yours, (v\d+|one-off)\)", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>The agent's own tools in the order library ("name vN"), from its YOUR OWN TOOLS.</summary>
        internal static List<string> OwnToolNames(string library)
        {
            int i = library.IndexOf("YOUR OWN TOOLS", StringComparison.Ordinal);
            if (i < 0) return new List<string>();
            return OwnTool.Matches(library.Substring(i)).Select(m => m.Groups[1].Value + " " + m.Groups[2].Value).ToList();
        }

        /// <summary>
        /// When the agent's own tools changed since the conversation began (its system prompt keeps the library as it was then, so the cache holds): the tools kept or changed
        /// since, each with its line in the library, and those retired.
        /// </summary>
        private string OwnToolsNote()
        {
            string library = ReadShared(Path.Combine(_c.ChannelDir, "tools.txt"));
            if (library == null || !_talk.Started) return null;
            return OwnToolsChange(_talk, library);
        }

        /// <summary>OwnToolsNote, for any conversation (the continuous lieutenant's too, 0.18.4): null when its own tools are as the conversation knows them.</summary>
        internal static string OwnToolsChange(Talk talk, string library)
        {
            List<string> now = OwnToolNames(library);
            if (now.SequenceEqual(talk.OwnTools)) return null;
            var added = now.Except(talk.OwnTools).ToList();
            var gone = talk.OwnTools.Where(t => !now.Any(n => n.Split(' ')[0] == t.Split(' ')[0])).Select(t => t.Split(' ')[0]).ToList();
            talk.OwnTools = now;
            var sb = new StringBuilder();
            sb.AppendLine("YOUR OWN TOOLS CHANGED (your instructions keep the library as it was when this conversation began; this is what changed since)");
            int at = library.IndexOf("YOUR OWN TOOLS", StringComparison.Ordinal);
            string own = at >= 0 ? library.Substring(at).Replace("\r", "") : "";
            foreach (string t in added)
            {
                string name = t.Split(' ')[0];
                string line = own.Split('\n').FirstOrDefault(l => l.StartsWith("- **" + name + "**", StringComparison.Ordinal));
                sb.AppendLine(line ?? "- **" + name + "** (" + t.Split(' ')[1] + ")");
            }
            if (gone.Count > 0) sb.AppendLine("- Retired: " + string.Join(", ", gone) + ".");
            return sb.ToString().TrimEnd();
        }

        // ---------- the memory of the campaign, and a reload ----------

        /// <summary>
        /// The memory of the poke's campaign and its conversation. A new load of the game at the hour the conversation reached carries the conversation on (told); a load of an
        /// earlier save puts the memory back to that history, as the written memory does, and the next message begins a new conversation from the handover written before it.
        /// </summary>
        internal string EnsureContinuous(PokeMeta meta)
        {
            string campaign = meta?.Campaign ?? LatestCampaign() ?? _mem?.Campaign ?? "unknown";
            if (_mem == null || _mem.Campaign != campaign)
            {
                _mem = Memory.Open(_c, campaign, Say, meta?.Hours ?? double.NaN);
                _talk = Talk.Load(_mem.Dir);
                if (_talk.SessionId != null && _talk.SessionId != _mem.SessionId) _talk.Reset("your memory was found without the conversation it belonged to");
                Say("Memory of campaign " + campaign + ": " + _mem.Dir + (_talk.Started ? "; its conversation holds " + _talk.Tokens.ToString("N0", CultureInfo.InvariantCulture) + " tokens" : "; no conversation yet") + ".");
            }
            if (meta == null || string.IsNullOrEmpty(meta.LoadId) || meta.LoadId == _mem.LoadId) return null;
            bool samePoint = _talk.Started && _talk.SessionId == _mem.SessionId && !double.IsNaN(_mem.LastHours) && !double.IsNaN(meta.LoadedAt) && meta.LoadedAt >= _mem.LastHours - 0.5;
            if (samePoint)
            {
                _mem.LoadId = meta.LoadId;
                _mem.Save();
                _talk.LoadId = meta.LoadId;
                _talk.Save();
                Say("Memory: the game was loaded again at " + meta.LoadedAtDate + ", where the conversation left off; it carries on.");
                return "THE GAME WAS LOADED AGAIN at " + meta.LoadedAtDate + ", where this conversation left off. Your orders in force and your watches carry on as the news says.";
            }
            bool had = _talk.Started;
            EnsureMemory(meta);
            if (had || _talk.SessionId != null)
                _talk.Reset("the game was loaded from a save of " + meta.LoadedAtDate + ", earlier than your last conversation reached, so what it held after that day did not happen; it begins from what you knew then");
            return null;
        }

        // ---------- strategy reviews ----------

        /// <summary>
        /// Why a strategy review should sit now, or null: a big event in a wake's news (council_after), at once, or the hour the agent set for it (0.18.2). No daily default
        /// and no gap between reviews: without an hour set and no big event, none sits.
        /// </summary>
        internal string ReviewDue(double nowHours)
        {
            if (!_c.Council || _talk == null || !_talk.Started || double.IsNaN(nowHours)) return null;
            if (_reviewWanted != null) return _reviewWanted;
            return !double.IsNaN(_talk.NextReviewHours) && nowHours >= _talk.NextReviewHours - 1e-6 ? "the hour you set for it has come" : null;
        }

        private void ReviewIfDue()
        {
            if (_mem == null || _talk == null || !_talk.Started || (DateTime.Now - _reviewFailedAt).TotalMinutes < 5) return;
            ReadClock();
            string why = ReviewDue(_pokeHours);
            if (why == null || !Fresh(out _)) return;
            if (!Review(why)) _reviewFailedAt = DateTime.Now;
        }

        /// <summary>A strategy review in the conversation (0.18): why it sits, the briefing of the whole realm from the game's newest hour, and the situation report less what the briefing and the conversation already hold.</summary>
        internal bool Review(string why)
        {
            DateTime began = DateTime.Now;
            PokeMeta meta = ReadMeta(-1);
            if (meta == null) return false;
            int number = meta.Number;
            if (!double.IsNaN(_pokeHours) && _pokeHours > meta.Hours && _clockDate != null) { meta.Hours = _pokeHours; meta.Date = _clockDate; }
            if (!BriefingFor(false)) return false;
            string briefing = ReadShared(Path.Combine(_c.ChannelDir, "council.txt")) ?? "";
            string situation = ReadShared(Path.Combine(_c.ChannelDir, "report.txt")) ?? "";
            var sb = new StringBuilder();
            object[] mark = _talk.Mark();
            string notes = OwnToolsNote();
            if (!string.IsNullOrEmpty(notes)) sb.AppendLine(notes).AppendLine();
            sb.AppendLine("STRATEGY REVIEW, " + meta.Date);
            sb.AppendLine("Why it sits now: " + why.TrimEnd('.') + ".");
            sb.AppendLine("This is not a wake: it is your strategy review (see \"Strategy reviews\" in your instructions). Answer with one JSON object that says \"poke\": " + number + ".");
            sb.AppendLine();
            sb.AppendLine(briefing.Trim()).AppendLine();
            sb.Append(Trim(situation, meta.Hours, review: true, opening: false));
            string prompt = sb.ToString().Replace("\r\n", "\n");
            CallResult r = Converse(prompt, "review", number, _c.CouncilTimeoutSeconds, meta, out string folder, out bool stall);
            if (!Reached(r)) _talk.Restore(mark);
            JsonObject answer = null;
            string problem = r.Ok ? Extract(r.Text, number, out answer) : r.Error;
            bool retried = false;
            if (answer == null && r.Ok)
            {
                retried = true;
                CallResult r2 = Converse("Your review's answer could not be read: " + problem + ". Reply again with ONLY the JSON object, nothing before or after it.", "review-retry", number, _c.ContinuousTimeoutSeconds, meta, out _, out _);
                problem = r2.Ok ? Extract(r2.Text, number, out answer) : r2.Error;
                r.Seconds += r2.Seconds;
                r.Cost += r2.Cost;
            }
            string changed = "";
            if (answer != null)
            {
                answer["kind"] = "council";
                WriteOrders(number, answer, "council", folder);
                changed = Remember(answer, number, meta.Date, meta);
                AfterContinuous(answer, meta.Hours, true);
                File.WriteAllText(Path.Combine(folder, "answer.json"), answer.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            _talk.Save();
            _mem.Save();
            double seconds = (DateTime.Now - began).TotalSeconds;
            LogCall("council", "review", r, number, meta.Date, meta, folder, answer, problem, retried, stall, seconds, prompt.Length, false, why, changed, briefing.Length, situation.Length);
            int orders = answer?["orders"] is JsonArray a ? a.Count : 0;
            if (answer != null) Say("Strategy review: " + orders + " order" + (orders == 1 ? "" : "s") + " in " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s; next by " + Program.When(_talk.NextReviewHours) + ".");
            else Say("Strategy review: NO ANSWER (" + problem + ") after " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s.");
            return answer != null;
        }

        // ---------- the handover ----------

        /// <summary>
        /// The moments of a handover: the player's own battle beginning (spare.txt fresh; the clock stands still), once the conversation holds compact_at_tokens; the game closing
        /// (its program gone, after it was seen running), once it holds compact_at_close_tokens and the conversation's cache is still warm.
        /// </summary>
        private void HandoverIfDue()
        {
            bool battle = SpareWord();
            bool closed = GameClosed();
            if (_mem == null || _talk == null || !_talk.Started) return;
            if (battle && _talk.Tokens >= _c.CompactAtTokens) Handover("the human player's battle has begun, and the clock stands still while it lasts", null);
            else if (closed && _talk.Tokens >= _c.CompactAtCloseTokens && (DateTime.Now - _lastCallEnded).TotalMinutes < 55) Handover("the game has been closed", null);
        }

        /// <summary>The game has just said the player's own battle began (spare.txt), once each time; a word older than two minutes (one from before this war room began) is let go.</summary>
        private bool SpareWord()
        {
            string file = Path.Combine(_c.ChannelDir, "spare.txt");
            try
            {
                if (!File.Exists(file)) return false;
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written == _spareWord) return false;
                _spareWord = written;
                return (DateTime.UtcNow - written).TotalSeconds <= SpareFreshSeconds;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static readonly Regex GameProcess = new Regex(@"^(Bannerlord(\.Native)?|Bannerlord\.BLSE\.\w+)$", RegexOptions.Compiled);

        /// <summary>Is the game's program running (under BLSE, Bannerlord.BLSE.Launcher)?</summary>
        internal static bool GameRunning()
        {
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { return true; }   // not known: not taken as closed
            bool found = false;
            foreach (Process p in all)
            {
                try { if (!found && GameProcess.IsMatch(p.ProcessName)) found = true; }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
            return found;
        }

        /// <summary>The game's program has gone, having been seen running by this war room: true once. Looked at every five seconds.</summary>
        private bool GameClosed()
        {
            if ((DateTime.Now - _gameLooked).TotalSeconds < 5) return false;
            _gameLooked = DateTime.Now;
            bool running = GameRunning();
            if (running) { _gameSeen = true; return false; }
            if (!_gameSeen) return false;
            _gameSeen = false;
            Say("Continuous: the game has been closed.");
            return true;
        }

        /// <summary>
        /// The agent writes its handover in the conversation (0.18), in the shape its instructions give; it is kept as the memory's story (with its snapshot, so a reload of an
        /// earlier save finds the handover of its day) and as handover-*.md, and the conversation ends: the next wake begins a new one from it. True when it was written.
        /// </summary>
        internal bool Handover(string why, PokeMeta meta)
        {
            if (_talk == null || !_talk.Started) return false;
            DateTime began = DateTime.Now;
            meta ??= ReadMeta(-1);
            ReadClock();
            double hours = !double.IsNaN(_pokeHours) ? _pokeHours : meta?.Hours ?? double.NaN;
            string date = _clockDate ?? meta?.Date ?? "";
            int number = meta?.Number ?? Math.Max(0, _lastAnswered);
            long before = _talk.Tokens;
            // the game's own list of the orders in force, the lords with none and the watches, for the handover's parts 4 and 5 (0.18.1): fresh when the game runs, else the last it wrote
            string facts = HandoverFacts(GameRunning() && Fresh(out _));
            string prompt = "WRITE YOUR HANDOVER NOW: " + why.TrimEnd('.') + ". This conversation ends with it. Your next message begins a new conversation that holds only your instructions, "
                            + "this handover, your notes, your chronicle since and a fresh report of your realm, so put in it everything you will need that those do not hold. Write it as "
                            + "\"Your handover\" in your instructions says, in its parts and in your own voice, as plain text beginning with the word HANDOVER and the date (" + date + "). No JSON and no orders: "
                            + "your orders in force and your watches carry on as they are."
                            + (facts != null ? " Write part 4 (orders in force) and part 5 (watches) from the game's own list below, adding what only you know: why each group has its order, what would end it, and which orders are on trial.\n\n" + facts : "");
            Say("Handover (" + why + "): the conversation holds " + before.ToString("N0", CultureInfo.InvariantCulture) + " tokens...");
            CallResult r = Converse(prompt, "handover", number, Math.Max(_c.CouncilTimeoutSeconds, 600), meta, out string folder, out bool stall);
            string text = r.Ok ? HandoverText(r.Text) : null;
            bool retried = false;
            if (text == null && r.Ok)
            {
                retried = true;
                CallResult r2 = Converse("That is not a handover in the shape your instructions give. Write it again: plain text beginning with HANDOVER, every part in it.", "handover-retry", number, Math.Max(_c.CouncilTimeoutSeconds, 600), meta, out _, out _);
                text = r2.Ok ? HandoverText(r2.Text) : null;
                r.Seconds += r2.Seconds;
                r.OutTokens += r2.OutTokens;
                r.Cost += r2.Cost;
            }
            bool kept = false;
            if (text != null)
            {
                _mem.View(out _, out _, out _, out _, out int generation);
                kept = _mem.SetStory(text, hours, date, generation);
                if (kept)
                {
                    _mem.Save();
                    _mem.Snapshot(hours, date, _mem.LastPoke, "handover");
                    File.WriteAllText(Path.Combine(_mem.Dir, "handover-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".md"), text + "\n", new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(folder, "handover.md"), text + "\n", new UTF8Encoding(false));
                    _talk.Reset("your last conversation ended with the handover below (" + why.TrimEnd('.') + ", " + date + ")");
                    _mem.SessionId = null;
                    _mem.SessionStarted = false;
                    _mem.SessionTokens = 0;
                    _mem.Save();
                }
            }
            double seconds = (DateTime.Now - began).TotalSeconds;
            Log(new
            {
                kind = "handover", continuous = true, at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), poke = number, when = date, why, model = _c.ContinuousModel,
                effort = _c.ContinuousEffort, thinking_cap = _c.ContinuousThinkingCap, ok = kept, retried, stall_retry = stall, seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2),
                tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cache_write_1h = r.CacheWrite1h, cost_usd_list = r.Cost,
                conversation_tokens = before, handover_words = Words(text), facts_chars = facts?.Length ?? 0, campaign = _mem.Campaign, folder, run = meta?.Run, arm = meta?.Arm, realm = meta?.Realm, problem = kept ? null : r.Error ?? "no handover in its answer"
            });
            Say(kept ? "Handover written (" + Words(text) + " words, " + seconds.ToString("0", CultureInfo.InvariantCulture) + " s); the next wake begins a new conversation from it."
                     : "Handover: NOT WRITTEN (" + (r.Error ?? "no handover in its answer") + "); the conversation carries on.");
            return kept;
        }

        /// <summary>The sections of a report the handover's list takes: every order in force (here and elsewhere), the lords with none, the watches.</summary>
        private static readonly string[] FactSections = { "YOUR ORDERS IN FORCE", "LORDS WITH NO ORDER", "YOUR WATCHES" };

        /// <summary>
        /// The game's own list for the handover's parts 4 and 5 (0.18.1): the orders in force, the lords with none and the watches, from the newest the game wrote. When the game
        /// runs (live), a fresh briefing is asked for first, so the situation report holds every order from its newest hour; at its close, the newer of the last situation report
        /// and the last wake's report (whose orders section names every other order in a line). Null when neither holds them.
        /// </summary>
        internal string HandoverFacts(bool live)
        {
            if (live) RequestBriefing(out _);
            string pokeFile = Path.Combine(_c.ChannelDir, "poke.txt"), reportFile = Path.Combine(_c.ChannelDir, "report.txt"), councilFile = Path.Combine(_c.ChannelDir, "council.txt");
            DateTime pokeAt = File.Exists(pokeFile) ? File.GetLastWriteTimeUtc(pokeFile) : DateTime.MinValue, reportAt = File.Exists(reportFile) ? File.GetLastWriteTimeUtc(reportFile) : DateTime.MinValue;
            if (pokeAt == DateTime.MinValue && reportAt == DateTime.MinValue) return null;
            bool fromReport = reportAt > pokeAt;
            string source = ReadShared(fromReport ? reportFile : pokeFile) ?? "";
            string council = fromReport ? ReadShared(councilFile) ?? "" : "";
            string asOf = fromReport ? DateAfter(council, "COUNCIL BRIEFING FOR") : DateAfter(source, "POKE #");
            var parts = new List<string>();
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (string text in new[] { source, council })
                foreach (KeyValuePair<string, string> b in Blocks(text))
                {
                    if (b.Key == null) continue;
                    string key = Key(b.Key), which = FactSections.FirstOrDefault(f => key.StartsWith(f, StringComparison.Ordinal));
                    if (which == null || !taken.Add(which)) continue;
                    parts.Add((b.Key + "\n" + b.Value.TrimEnd()).Trim());
                }
            if (!parts.Any(p => p.StartsWith("YOUR ORDERS IN FORCE", StringComparison.Ordinal))) return null;
            return "THE GAME'S OWN LIST FOR YOUR HANDOVER (as of " + (asOf.Length > 0 ? asOf : "its last report") + (live ? "" : ", the last the game wrote before it closed")
                   + "): your orders in force, the lords with none, and your watches. Orders you gave, or that ended, after that are in this conversation.\n\n" + string.Join("\n\n", parts) + "\n";
        }

        /// <summary>The date on a report's first line that begins so ("POKE #12, Autumn 3, 1087, 10:00"): what follows its first comma; empty when there is none.</summary>
        private static string DateAfter(string text, string prefix)
        {
            foreach (string line in (text ?? "").Replace("\r", "").Split('\n'))
            {
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                int c = line.IndexOf(", ", StringComparison.Ordinal);
                return c > 0 ? line.Substring(c + 2).Trim() : "";
            }
            return "";
        }

        /// <summary>The handover in an answer: from the word HANDOVER on, without code fences; null when there is none or it is too short to be one.</summary>
        internal static string HandoverText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Replace("\r", "");
            int i = t.IndexOf("HANDOVER", StringComparison.Ordinal);
            if (i < 0) return null;
            t = t.Substring(i).Replace("```", "").Trim();
            return t.Length >= 400 ? t : null;
        }

        // ---------- for the self-test ----------

        internal Talk TalkNow => _talk;
        internal void UseForTest(Memory m, Talk t) { _mem = m; _talk = t; }
        internal void WantReview(string why) => _reviewWanted = why;
        /// <summary>For the self-test: the next review's hour an answer would leave (review true: the answer is a review's).</summary>
        internal double Review_In(string answer, double hours, bool review) { _talk.NextReviewHours = double.NaN; AfterContinuous((JsonObject)JsonNode.Parse(answer), hours, review); return _talk.NextReviewHours; }

        // ---------- helpers ----------

        /// <summary>Did the message reach the conversation (an answer, or at least the model began one)?</summary>
        private static bool Reached(CallResult r) => r.Ok || (r.Raw != null && r.Raw.Contains("\"type\":\"assistant\""));

        private static string DayKey(double hours) => double.IsNaN(hours) ? "?" : Math.Floor(hours / 24).ToString(CultureInfo.InvariantCulture);

        private void LogCall(string kind, string call, CallResult r, int number, string when, PokeMeta meta, string folder, JsonObject answer, string problem, bool retried, bool stall, double seconds,
                             int messageChars, bool opening, string why = null, string changed = "", int briefingChars = 0, int situationChars = 0)
        {
            Log(new
            {
                kind, call, continuous = true, at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), poke = number, when, why, model = _c.ContinuousModel,
                effort = _c.ContinuousEffort, thinking_cap = _c.ContinuousThinkingCap, ok = answer != null, problem, retried, stall_retry = stall,
                seconds = Math.Round(seconds, 2), call_seconds = Math.Round(r.Seconds, 2), api_ms = r.ApiMs, ttft_ms = r.TtftMs, turns = r.Turns,
                tokens_in = r.InTokens, tokens_out = r.OutTokens, thinking = r.Thinking, cache_read = r.CacheRead, cache_write = r.CacheWrite, cache_write_1h = r.CacheWrite1h, cost_usd_list = r.Cost,
                answer_began_ms = Math.Round(r.FirstTextMs), memory = "continuous", context_tokens = r.LastContext, conversation_tokens = _talk.Tokens, conversation_messages = _talk.Messages,
                new_conversation = opening, session = _talk.SessionId, notes_chars = _mem.Notes.Length, notes_changed = changed, campaign = _mem.Campaign,
                orders = answer?["orders"] is JsonArray a ? a.Count : 0, watches = Count(answer, "watch"), review_in = answer?["review_in"]?.ToString(), denied = r.Denied, folder, written = answer != null,
                run = meta?.Run, arm = meta?.Arm, realm = meta?.Realm, system_hash = _talk.SystemHash, library_hash = LibraryHash(), thinking_chars = r.ThinkingText.Length,
                tools = Count(answer, "tools"), diplomacy = Count(answer, "diplomacy"), letters = Count(answer, "letters"), message_chars = messageChars,
                briefing_chars = briefingChars, situation_chars = situationChars, wakes_today = _talk.WakesByDay.TryGetValue(DayKey(meta?.Hours ?? _pokeHours), out int wt) ? wt : 0
            });
        }
    }
}
