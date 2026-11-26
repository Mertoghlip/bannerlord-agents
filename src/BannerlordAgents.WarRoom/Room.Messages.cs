using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// War room 0.22.0 (Nov 26, the tooling audit of run 20261125-2341-C2): what every message opens with, before what woke the agent.
    /// - "Since your last answer:" (item 13): the big quiet news the game lists with each poke (poke.json's since_last: captures of the realm's lords, holdings lost or
    ///   taken, battles with more than 500 of its men lost), oldest first, for every poke since the last message, the ones that came during a call too (a watcher keeps
    ///   each poke's lines as it comes). On day 101, 18 lords were taken at Imnagath and the next wake named one of them.
    /// - "Not carried out from your last answer:" (item 15): each refusal in the game's receipts since the last message, with its full reason; a change to a watch that no
    ///   longer exists says when it ended. Refusals reached only the order-results page, so 13-14 refused orders were given again unchanged.
    /// - "Also since your last answer:" (item 17): the reasons of the wakes that came while the agent was answering another message and got no call of their own, oldest
    ///   first (a files agent's wake carried only its own reasons: "Lasgalor fell" came during a review and never reached it).
    /// And a watch's names are checked as the answer is read (the game's own rule), so a bad name is caught before it reaches the game.
    /// </summary>
    internal sealed partial class Room
    {
        // ---------- each poke's big news, kept as it comes ----------

        private readonly object _sinceGate = new object();
        private readonly SortedDictionary<int, List<string>> _sinceByPoke = new SortedDictionary<int, List<string>>();
        private int _sinceShownThrough = -1;
        private Timer _pokeWatch;
        private int _pokeWatchSeen = -1;

        /// <summary>Starts the watcher that keeps each poke's since_last lines as it comes, calls running or not (a call takes minutes; poke.json holds only the newest poke).</summary>
        internal void WatchPokes()
        {
            _sinceShownThrough = Math.Max(_sinceShownThrough, _lastAnswered);
            _pokeWatch ??= new Timer(_ => { try { NotePoke(ReadMeta(-1)); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException) { } }, null, 0, 300);
        }

        /// <summary>One poke's big news, kept by its number (the watcher, and every message that reads a poke).</summary>
        internal void NotePoke(PokeMeta meta)
        {
            if (meta == null || meta.Number < 0) return;
            lock (_sinceGate)
            {
                if (meta.Number == _pokeWatchSeen && _sinceByPoke.ContainsKey(meta.Number)) return;
                _pokeWatchSeen = Math.Max(_pokeWatchSeen, meta.Number);
                if (meta.SinceLast.Count > 0) _sinceByPoke[meta.Number] = meta.SinceLast.ToList();
                while (_sinceByPoke.Count > 400) _sinceByPoke.Remove(_sinceByPoke.Keys.First());
            }
        }

        /// <summary>The big news of every poke after the last message's, through this one, oldest first; the poke is then counted as shown.</summary>
        internal List<string> TakeSince(PokeMeta meta)
        {
            NotePoke(meta);
            int through = meta?.Number ?? _pokeWatchSeen;
            var lines = new List<string>();
            lock (_sinceGate)
            {
                foreach (KeyValuePair<int, List<string>> kv in _sinceByPoke)
                {
                    if (kv.Key <= _sinceShownThrough || kv.Key > through) continue;
                    foreach (string l in kv.Value) if (!lines.Contains(l)) lines.Add(l);
                }
                if (through > _sinceShownThrough) _sinceShownThrough = through;
            }
            return lines;
        }

        // ---------- the refusals in the game's receipts ----------

        private long _refusalMark = -1;
        /// <summary>Refusals the war room itself made as it read an answer (a watch's bad name), told with the game's.</summary>
        private readonly List<string> _ownRefusals = new List<string>();
        /// <summary>When each watch ended, as the game said it ("Your watch "x" ended: ..."), with the date of the poke that said so.</summary>
        private readonly Dictionary<string, string> _watchEnded = new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly Regex ReceiptRefusal = new Regex(@"^(?:\d\d:\d\d:\d\d\s+)?poke #(?<poke>-?\d+), (?<file>\S+) (?<what>order|watch|tool|letter|diplomacy) (?<i>\d+) \((?<who>.*)\): REFUSED\. (?<why>.*)$", RegexOptions.Compiled);
        private static readonly Regex WatchEndedLine = new Regex("Your watch \"(?<name>[a-z0-9_]+)\" ended: (?<why>[^\\n]+)", RegexOptions.Compiled);

        /// <summary>Where the receipts stand now: refusals before this are not the agent's to hear about again (the war room's start).</summary>
        private void MarkRefusals()
        {
            if (_refusalMark >= 0) return;
            string path = Path.Combine(_c.ChannelDir, "receipts.log");
            try { _refusalMark = File.Exists(path) ? new FileInfo(path).Length : 0; } catch (IOException) { _refusalMark = 0; }
        }

        /// <summary>The game's "Your watch ... ended" lines in a text a message read, each kept with when (for a later change to that watch, refused as gone).</summary>
        internal void NoteWatchEnds(string text, string date)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (Match m in WatchEndedLine.Matches(text))
                _watchEnded[m.Groups["name"].Value] = (string.IsNullOrWhiteSpace(date) ? "" : date.Trim() + ": ") + m.Groups["why"].Value.Trim().TrimEnd('.');
        }

        /// <summary>
        /// The refusals in the receipts since the last message (and the war room's own), each with its full reason, the receipts' bookkeeping (time, poke, file, how long the answer
        /// took) left out; a change to a watch that no longer exists with when it ended.
        /// </summary>
        internal List<string> TakeRefusals()
        {
            MarkRefusals();
            var lines = new List<string>(_ownRefusals);
            _ownRefusals.Clear();
            string path = Path.Combine(_c.ChannelDir, "receipts.log");
            string text = "";
            try
            {
                if (File.Exists(path))
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (_refusalMark > fs.Length) _refusalMark = 0;   // a new receipts file
                    fs.Position = _refusalMark;
                    using var sr = new StreamReader(fs, new UTF8Encoding(false));
                    text = sr.ReadToEnd();
                    int lastBreak = text.LastIndexOf('\n');
                    // only whole lines: a line still being written waits for the next message
                    if (lastBreak < 0) text = "";
                    else { _refusalMark += Encoding.UTF8.GetByteCount(text.Substring(0, lastBreak + 1)); text = text.Substring(0, lastBreak + 1); }
                }
            }
            catch (IOException) { }
            foreach (string raw in text.Split('\n'))
            {
                string l = raw.TrimEnd('\r').Trim();
                if (l.Length == 0) continue;
                Match m = ReceiptRefusal.Match(l);
                if (m.Success)
                {
                    string why = m.Groups["why"].Value;
                    foreach (string tail in new[] { " Answered ", " Carried out ", " (The file gave no poke number" }) { int t = why.IndexOf(tail, StringComparison.Ordinal); if (t > 0) why = why.Substring(0, t); }
                    why = why.Trim().TrimEnd('.');
                    string who = m.Groups["who"].Value.Trim(), kind = m.Groups["what"].Value;
                    if (kind == "watch" && why.Contains("no watch of that name") && _watchEnded.TryGetValue(who, out string ended)) why += " (it ended " + ended + ")";
                    else if (kind == "watch" && why.Contains("no watch of that name")) why += " (it had ended or was never kept: plan/watches lists your watches)";
                    lines.Add("- " + (m.Groups["file"].Value == "then" ? "the order that was to follow, " : "") + kind + " (" + who + "): " + why + ".");
                    continue;
                }
                if (l.Contains("REFUSED"))
                {
                    string body = Regex.Replace(l, @"^\d\d:\d\d:\d\d\s+", "");
                    lines.Add("- " + body.TrimEnd('.') + ".");
                }
            }
            return lines.Distinct().ToList();
        }

        // ---------- the wakes that got no call of their own ----------

        /// <summary>
        /// The reasons of the wakes that came after the agent's last answer and before this one (the poke's own WHAT CHANGED SINCE YOUR LAST ANSWER lists them, "- (#N) ..."),
        /// oldest first: those wakes got no call of their own, since the war room answers the newest.
        /// </summary>
        internal static List<string> SkippedWakes(string pokeText)
        {
            var lines = new List<string>();
            string what = Section(pokeText ?? "", "WHAT CHANGED SINCE YOUR LAST ANSWER");
            foreach (string raw in what.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("Quieter news", StringComparison.Ordinal) || l.StartsWith("What became of your orders", StringComparison.Ordinal)) break;
                if (Regex.IsMatch(l, @"^- \(#\d+\) ")) lines.Add(l);
            }
            return lines;
        }

        /// <summary>
        /// What a message opens with (war room 0.22.0): the big news since the last message, what of the last answer was not carried out, and (a files agent's wake, which carries
        /// only its own reasons) the reasons of the wakes that got no call. Empty when there is none of these.
        /// </summary>
        internal string NewsOpening(PokeMeta meta, string pokeText)
        {
            var sb = new StringBuilder();
            void Line(string s) => sb.Append(s).Append('\n');
            // what a message that never reached the conversation was to open with comes first
            if (_unsentNews != null) { Line(_unsentNews.TrimEnd()); Line(""); _unsentNews = null; }
            List<string> since = TakeSince(meta);
            if (since.Count > 0)
            {
                Line("Since your last answer:");
                foreach (string l in since) Line("- " + l.TrimStart('-', ' '));
            }
            List<string> refused = TakeRefusals();
            if (refused.Count > 0)
            {
                if (sb.Length > 0) Line("");
                Line("Not carried out from your last answer:");
                foreach (string l in refused) Line(l);
            }
            List<string> skipped = _c.Files && pokeText != null ? SkippedWakes(pokeText) : new List<string>();
            if (skipped.Count > 0)
            {
                if (sb.Length > 0) Line("");
                Line("Also since your last answer (wakes that came while you were answering another message):");
                foreach (string l in skipped) Line(l);
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() + "\n\n" : "";
        }

        private string _unsentNews;

        /// <summary>A message that never reached the conversation: what it opened with is told with the next one instead.</summary>
        private void KeepUnsentNews(string news)
        {
            if (string.IsNullOrWhiteSpace(news)) return;
            _unsentNews = string.IsNullOrEmpty(_unsentNews) ? news.Trim() : _unsentNews.TrimEnd() + "\n\n" + news.Trim();
        }

        // ---------- a watch's names, checked as the answer is read ----------

        /// <summary>The game's rule for a watch's name and the short names its conditions read (Watches.NameRule).</summary>
        private static readonly Regex WatchName = new Regex("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);

        /// <summary>
        /// An answer's watches with a name or a short name the game would refuse are taken out before the answer goes to the game, each told with the next message, in the game's own
        /// words (run 20261125-2341-C2: the watch on Baracharn was refused twice for one-letter names and never set).
        /// </summary>
        internal List<string> CheckWatchNames(JsonObject answer)
        {
            var refused = new List<string>();
            if (answer == null) return refused;
            JsonArray list = answer["watch"] as JsonArray ?? (answer["watch"] is JsonObject one ? new JsonArray(one.DeepClone()) : null);
            if (list == null) return refused;
            var kept = new JsonArray();
            foreach (JsonNode n in list)
            {
                if (n is not JsonObject w) { kept.Add(n?.DeepClone()); continue; }
                string name = w["name"] is JsonValue nv && nv.TryGetValue(out string s) ? s.Trim() : "";
                var problems = new List<string>();
                if (!WatchName.IsMatch(name)) problems.Add("its name must be 2 to 32 small letters, digits and underscores, starting with a letter");
                if (w["names"] is JsonObject names)
                    foreach (KeyValuePair<string, JsonNode> kv in names)
                        if (!WatchName.IsMatch(kv.Key)) problems.Add("names: '" + kv.Key + "' must be 2 to 32 small letters, digits and underscores, starting with a letter");
                if (problems.Count == 0) { kept.Add(w.DeepClone()); continue; }
                refused.Add("- watch (" + (name.Length > 0 ? name : "no name") + "): " + string.Join("; ", problems) + " (not sent to the game).");
            }
            if (refused.Count > 0)
            {
                answer["watch"] = kept;
                _ownRefusals.AddRange(refused);
            }
            return refused;
        }

        // ---------- reviews before routine wakes ----------

        /// <summary>Words that make a wake more than routine: a siege, a battle, a capture, a holding won or lost (item 22: an overdue review goes only ahead of the others).</summary>
        private static readonly string[] UrgentWords = { "besieg", "siege", "storm", "battle", "taken prisoner", "captured", "lords taken", "were taken", "was taken", "men lost", "fell", "fallen", "passed from",
                                                         "taken by", "lost", "attacked", "declared war", "made peace", "first look" };

        /// <summary>A wake that is not a siege, a battle, a capture or a holding won or lost: one an overdue review may go ahead of.</summary>
        internal static bool RoutineWake(string why) => !UrgentWords.Any(w => (why ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>How many in-game hours the next strategy review is overdue at this hour (0 or less: not overdue): past the hour set, or a day after the last.</summary>
        internal double ReviewOverdue(double nowHours)
        {
            if (_talk == null || !_talk.Started || double.IsNaN(nowHours)) return double.NaN;
            double due = double.NaN;
            if (!double.IsNaN(_talk.NextReviewHours)) due = _talk.NextReviewHours;
            if (_c.ReviewEveryHours > 0 && !double.IsNaN(_talk.LastReviewHours)) due = double.IsNaN(due) ? _talk.LastReviewHours + _c.ReviewEveryHours : Math.Min(due, _talk.LastReviewHours + _c.ReviewEveryHours);
            return double.IsNaN(due) ? double.NaN : nowHours - due;
        }

        /// <summary>How overdue a review must be to go ahead of a routine wake, in in-game hours.</summary>
        internal const double ReviewFirstHours = 12;

        /// <summary>A review more than 12 in-game hours overdue sits before a routine wake waiting (item 22: 40 gaps between reviews were over a day, the longest 84 hours).</summary>
        internal bool ReviewBeforeWake(string pokeText, double nowHours)
        {
            if (!_c.Council || (DateTime.Now - _reviewFailedAt).TotalMinutes < 5) return false;
            double over = ReviewOverdue(nowHours);
            return !double.IsNaN(over) && over > ReviewFirstHours && RoutineWake(Section(pokeText ?? "", "WHY YOU ARE ASKED NOW"));
        }
    }
}
