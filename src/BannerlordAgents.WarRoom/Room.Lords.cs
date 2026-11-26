using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// War room 0.20.7 (Nov 25, after run 20261125-0020-C2: the agent ordered at most a quarter of its lords, 94% of the 392 captures were of lords the game's AI was
    /// leading, it stopped reading its pages and set reviews four to seven days apart, and never once weighed gathering its strength against Dol Guldur's grand army
    /// with the right numbers). My rules since: every lord should be under an order of the agent's, it reviews its lords every day to take back control of each, it holds
    /// a strategy review every day, its review brings the money, and it always asks whether it can gather enough to beat each enemy army where it stands.
    /// - A files agent's review (and the first answer of a conversation) opens with a digest from its files: the realm in sum, the money, every lord with no order, what
    ///   became of its orders, its captive lords, and the question with each enemy army in view and the strength the realm could gather where it stands.
    /// War room 0.21.0 (Nov 25, after run 20261125-2318-C2): the war room never orders a lord itself. Its standing order (0.20.7 to 0.20.11: defend the nearest holding)
    /// overwrote the agent's own new orders five seconds after they landed, from a list made before they did (Ask's siege of Nanduhirion became "guard Rivendell"), and
    /// in run 20261125-2226-C2 took lords marching to join an army out of it. Now a lord found without an order wakes the agent, and the agent decides what becomes of him:
    /// - After a wake's answer, the lords still without an order that it was not asked about in the last in-game day are put to it at once, in the same conversation.
    /// - After a review's answer (and a conversation's first answer), the lords its digest listed that it left without an order are put to it once more.
    /// - Whatever it answers stands: an order given like any other, or the lord left to the game's own AI. Left lords are listed again at the next review.
    /// - A lord the agent ordered after the list was made (or within two minutes before, while the game may not yet have taken the order) or in the last 12 in-game hours
    ///   is not asked about: his order may not show yet. Lords marching to join an army of the realm are not on the list (Shelf.MarchesToArmy, 0.20.11).
    /// </summary>
    internal sealed partial class Room
    {
        internal sealed class Unordered
        {
            public string Id, Name, Line;
            public int Men;
            /// <summary>When the no-order page he was read from was written (UTC).</summary>
            public DateTime AsOf;
        }

        /// <summary>When the agent last ordered each lord: in-game hours of the call, and the wall clock (UTC) when the war room wrote the order.</summary>
        private readonly Dictionary<string, double> _orderedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _orderedWall = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        /// <summary>When each lord was last put to the agent as having no order (in-game hours).</summary>
        private readonly Dictionary<string, double> _askedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The realm's view file of the last build of its files (the digest's question reads it).</summary>
        private JsonObject _lastView;
        /// <summary>The lords with no order as the review's digest listed them (put to the agent again after its answer).</summary>
        private List<Unordered> _reviewUnordered;

        internal const double OrderGraceHours = 12;
        /// <summary>A lord the agent was asked about and left is not asked about again after a wake for this long (the daily review lists him again).</summary>
        internal const double AskAgainHours = 24;
        /// <summary>An order written this long before the no-order page may not have reached the game when the page's hour was copied.</summary>
        internal static readonly TimeSpan OrderLandMargin = TimeSpan.FromMinutes(2);

        /// <summary>
        /// The lords with no order, from the files' lords/no-order page (the game's own list, as of the last build). asOf: when the hour those files were built from was written (the
        /// book's own time), else the page's; a lord ordered after it may not show his order yet.
        /// </summary>
        internal static List<Unordered> NoOrderLords(string channelDir, DateTime? asOf = null)
        {
            var list = new List<Unordered>();
            string path = Path.Combine(channelDir, Shelf.DirName, "lords", "no-order.md");
            string text = ReadShared(path);
            if (text == null) return list;
            DateTime stamp = asOf.HasValue && asOf.Value > DateTime.MinValue.AddDays(1) ? asOf.Value : File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
                Match m = Regex.Match(line, @"^- (.+?) \[([^\]]+)\]");
                if (!m.Success) continue;
                Match men = Regex.Match(line, @"(\d[\d,]*) men");
                int c = line.IndexOf("): ", StringComparison.Ordinal);
                list.Add(new Unordered { Name = m.Groups[1].Value, Id = m.Groups[2].Value, AsOf = stamp, Line = c > 0 ? line.Substring(c + 3).Trim() : "",
                                         Men = men.Success ? int.Parse(men.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : 0 });
            }
            return list;
        }

        /// <summary>Every lord an answer's orders name: each order's lord, the lords it names (a group order orders each), and those of what follows it.</summary>
        internal static HashSet<string> OrderedIn(JsonObject answer)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Take(JsonNode n)
            {
                if (n is not JsonObject o) return;
                if (o["lord"] is JsonValue lv && lv.TryGetValue(out string l) && !string.IsNullOrWhiteSpace(l)) set.Add(l.Trim().Trim('[', ']'));
                if (o["lords"] is JsonArray la)
                    foreach (JsonNode x in la) if (x is JsonValue xv && xv.TryGetValue(out string s) && !string.IsNullOrWhiteSpace(s)) set.Add(s.Trim().Trim('[', ']'));
                if (o["then"] is JsonObject t) Take(t);
            }
            if (answer?["orders"] is JsonArray a) foreach (JsonNode o in a) Take(o);
            return set;
        }

        /// <summary>Notes the lords an answer orders, right after its orders were written: the call's in-game hour and the wall clock.</summary>
        private void NoteOrdered(JsonObject answer, double hours)
        {
            if (answer == null) return;
            DateTime now = DateTime.UtcNow;
            foreach (string id in OrderedIn(answer))
            {
                _orderedWall[id] = now;
                if (!double.IsNaN(hours)) _orderedAt[id] = hours;
            }
        }

        /// <summary>The agent ordered this lord after his no-order page was written, or so shortly before it that the game may not have taken the order yet.</summary>
        internal static bool OrderedSince(Unordered l, IReadOnlyDictionary<string, DateTime> wall) => wall.TryGetValue(l.Id, out DateTime w) && w >= l.AsOf - OrderLandMargin;

        /// <summary>
        /// The lords still to ask about: not ordered by this answer, nor after the files were built (or just before), nor in the last 12 in-game hours, and not left to the game's AI
        /// by the agent's own choice while that holds (war room 0.22.0, item 31). The review's digest uses the same rule (item 10).
        /// </summary>
        private List<Unordered> StillUnordered(IEnumerable<Unordered> lords, ISet<string> answered, double hours) =>
            lords.Where(l => !answered.Contains(l.Id) && !OrderedSince(l, _orderedWall)
                             && !(_orderedAt.TryGetValue(l.Id, out double h) && !double.IsNaN(hours) && hours - h < OrderGraceHours)
                             && !IsLeft(l, hours)).ToList();

        /// <summary>After a wake's answer: the lords on the no-order page that this answer did not order, nor a recent one, and that were not put to it in the last day, wake it.</summary>
        private void AskAfterWake(int number, JsonObject answer, PokeMeta meta)
        {
            if (!_c.Files || !_c.EveryLordOrdered || answer == null) return;
            double hours = meta?.Hours ?? _pokeHours;
            NoteOrdered(answer, hours);
            var left = StillUnordered(NoOrderLords(_c.ChannelDir, _shelf?.BuiltFromUtc), OrderedIn(answer), hours)
                .Where(l => !(_askedAt.TryGetValue(l.Id, out double a) && !double.IsNaN(hours) && hours - a < AskAgainHours)).ToList();
            if (left.Count > 0) AskAboutLords(number, left, meta, "wake", "after your answer to wake #" + number);
        }

        /// <summary>After a review's answer (or a conversation's first): the lords its digest listed with no order that it left so wake it once more.</summary>
        private void AskAfterReview(int number, JsonObject answer, PokeMeta meta, List<Unordered> atReview, string call)
        {
            if (atReview == null || answer == null) return;
            double hours = meta?.Hours ?? _pokeHours;
            NoteOrdered(answer, hours);
            List<Unordered> missing = StillUnordered(atReview, OrderedIn(answer), hours);
            if (missing.Count > 0) AskAboutLords(number, missing, meta, call, "after your " + (call == "opening" ? "first answer" : "review"));
        }

        /// <summary>The message that wakes the agent about lords with no order: each one with where he is and what he does (his whole line, war room 0.22.0), and that the choice is its own.</summary>
        internal static string LordsQuestion(int number, List<Unordered> lords, string when)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LORDS WITH NO ORDER OF YOURS (" + when + "): " + lords.Count + (lords.Count == 1 ? " lord holds" : " lords hold") + " no order of yours, so the game's own AI leads "
                          + (lords.Count == 1 ? "him" : "them") + ", and nobody else will order " + (lords.Count == 1 ? "him" : "them") + ":");
            foreach (Unordered l in lords)
                sb.AppendLine("- " + l.Name + " [" + l.Id + "]: " + (string.IsNullOrWhiteSpace(l.Line) ? l.Men.ToString("N0", CultureInfo.InvariantCulture) + " men" : l.Line));
            sb.Append("Decide what becomes of each: give him an order (a group order may cover several: name them in its lords), or leave him to the game's AI. "
                      + "To leave one to the game's AI without being asked about him again for a while, name him in \"leave_to_ai\" with until: in-game hours, or \"army ends\" for a lord leading an army of the game's AI. "
                      + "Reply with ONLY one JSON object that says \"poke\": " + number + " and holds your orders for them; an answer with no orders leaves them all to the game's AI.");
            return sb.ToString();
        }

        /// <summary>Wakes the agent about lords with no order, in the same conversation: its answer's orders are given like any other; the lords it leaves stay with the game's AI.</summary>
        private void AskAboutLords(int number, List<Unordered> lords, PokeMeta meta, string call, string when)
        {
            if (RunFinished()) return;
            double hours = meta?.Hours ?? _pokeHours;
            if (!double.IsNaN(hours)) foreach (Unordered l in lords) _askedAt[l.Id] = hours;
            // war room 0.22.0 (item 15): refusals of the answer just given open this question too, when the game has taken it already
            string news = NewsOpening(meta, null);
            string ask = news + LordsQuestion(number, lords, when);
            DateTime began = DateTime.Now;
            CallResult r = Converse(ask, call + "-lords", number, _c.ContinuousTimeoutSeconds, meta, out string folder, out bool stall);
            if (!Reached(r)) KeepUnsentNews(news);
            JsonObject more = null;
            string problem = r.Ok ? Extract(r.Text, number, out more) : r.Error;
            int given = 0;
            HashSet<string> named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (more != null)
            {
                more["kind"] = "council";
                CheckWatchNames(more);
                WriteOrders(number, more, "lords", folder);
                Remember(more, number, meta?.Date, meta);
                TakeLeaves(more, hours, meta?.Date ?? "");
                File.WriteAllText(Path.Combine(folder, "answer.json"), more.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                NoteOrdered(more, hours);
                named = OrderedIn(more);
                given = lords.Count(l => named.Contains(l.Id));
            }
            _talk.Save();
            LogCall("council", call + "-lords", r, number, meta?.Date, meta, folder, more, problem, false, stall, (DateTime.Now - began).TotalSeconds, ask.Length, false, "lords with no order");
            // war room 0.22.0 (item 41): the lords left to the game's AI are only those the answer did not order (59 of 89 lines listed every lord asked)
            List<Unordered> rest = lords.Where(l => !named.Contains(l.Id)).ToList();
            Say("Lords with no order put to the agent (" + when + "): " + lords.Count + "; it ordered " + given + (more == null ? " (its answer could not be read: " + problem + ")" : "")
                + (rest.Count == 0 ? "; none stays with the game's own AI." : ", the rest stay with the game's own AI: " + string.Join("; ", rest.Select(l => l.Name + " [" + l.Id + "]")) + "."));
        }

        // ---------- lords left to the game's AI, by the agent's choice (war room 0.22.0, item 31) ----------

        /// <summary>A lord the agent left to the game's AI for a while: until an in-game hour, or until the army he leads breaks up.</summary>
        internal sealed class LeftToAi
        {
            public string Id { get; set; }
            public string Name { get; set; }
            /// <summary>"hours" (until Until) or "army" (while he leads an army).</summary>
            public string Until { get; set; } = "hours";
            public double UntilHours { get; set; } = double.NaN;
            public double SetHours { get; set; } = double.NaN;
            public string SetDate { get; set; } = "";
            public string UntilDate { get; set; } = "";
        }

        private Dictionary<string, LeftToAi> _left;
        private string _leftDir;

        private string LeftFile => _mem == null ? null : Path.Combine(_mem.Dir, "left-to-ai.json");

        private static readonly System.Text.Json.JsonSerializerOptions LeftOpts = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        /// <summary>The marks of this campaign's memory, read once.</summary>
        private Dictionary<string, LeftToAi> Left()
        {
            if (_left != null && _leftDir == _mem?.Dir) return _left;
            _left = new Dictionary<string, LeftToAi>(StringComparer.OrdinalIgnoreCase);
            _leftDir = _mem?.Dir;
            try
            {
                if (LeftFile != null && File.Exists(LeftFile))
                    foreach (LeftToAi l in JsonSerializer.Deserialize<List<LeftToAi>>(File.ReadAllText(LeftFile), LeftOpts) ?? new List<LeftToAi>())
                        if (!string.IsNullOrEmpty(l.Id)) _left[l.Id] = l;
            }
            catch (Exception e) when (e is IOException || e is JsonException) { }
            return _left;
        }

        private void SaveLeft()
        {
            if (LeftFile == null) return;
            try { Memory.Write(LeftFile, JsonSerializer.Serialize(Left().Values.ToList(), LeftOpts)); } catch (IOException) { }
        }

        /// <summary>
        /// An answer's "leave_to_ai": [{"lord": id, "until": hours (1 to 240) or "army ends"}]: the lords it leaves to the game's AI for a while, not asked about again until then. A lord
        /// the same answer orders loses his mark, as does any lord ordered later.
        /// </summary>
        internal List<string> TakeLeaves(JsonObject answer, double hours, string date)
        {
            var said = new List<string>();
            if (answer == null || _mem == null) return said;
            Dictionary<string, LeftToAi> left = Left();
            bool changed = false;
            HashSet<string> ordered = OrderedIn(answer);
            foreach (string id in ordered) changed |= left.Remove(id);
            if (answer["leave_to_ai"] is JsonArray list)
                foreach (JsonNode n in list)
                {
                    string id = n is JsonObject o ? (o["lord"] as JsonValue)?.ToString() : (n as JsonValue)?.ToString();
                    id = id?.Trim().Trim('[', ']');
                    if (string.IsNullOrEmpty(id) || ordered.Contains(id)) continue;
                    var mark = new LeftToAi { Id = id, Name = id, SetHours = hours, SetDate = date ?? "" };
                    JsonNode until = (n as JsonObject)?["until"] ?? (n as JsonObject)?["hours"];
                    if (until is JsonValue uv && uv.TryGetValue(out double h)) { mark.Until = "hours"; mark.UntilHours = hours + Math.Max(1, Math.Min(240, h)); }
                    else if (until is JsonValue sv && sv.TryGetValue(out string s) && s.IndexOf("army", StringComparison.OrdinalIgnoreCase) >= 0) mark.Until = "army";
                    else { mark.Until = "hours"; mark.UntilHours = hours + 24; }
                    if (mark.Until == "hours" && !double.IsNaN(mark.UntilHours)) mark.UntilDate = Program.When(mark.UntilHours);
                    left[id] = mark;
                    changed = true;
                    said.Add(id);
                }
            if (changed) SaveLeft();
            return said;
        }

        /// <summary>The agent's mark on this lord holds now: its hour not come, or the army he leads not broken up (his line still says "army of").</summary>
        internal bool IsLeft(Unordered l, double hours)
        {
            if (_mem == null || !Left().TryGetValue(l.Id, out LeftToAi m)) return false;
            if (m.Until == "army") return (l.Line ?? "").IndexOf("army of", StringComparison.OrdinalIgnoreCase) >= 0;
            return double.IsNaN(hours) || double.IsNaN(m.UntilHours) || hours < m.UntilHours;
        }

        /// <summary>At a load of an earlier save: the marks set after the loaded hour were never set in that game.</summary>
        internal void ForgetLeavesAfter(double hours)
        {
            if (_mem == null || double.IsNaN(hours)) return;
            Dictionary<string, LeftToAi> left = Left();
            int n = left.Values.Count(m => !double.IsNaN(m.SetHours) && m.SetHours > hours + 1e-6);
            if (n == 0) return;
            foreach (string id in left.Where(kv => !double.IsNaN(kv.Value.SetHours) && kv.Value.SetHours > hours + 1e-6).Select(kv => kv.Key).ToList()) left.Remove(id);
            SaveLeft();
        }

        /// <summary>The lords left to the game's AI in a line each's words: "Gárjan [id] until his army breaks up".</summary>
        private static string LeftWords(Unordered l, LeftToAi m) => l.Name + " [" + l.Id + "] " + (m.Until == "army" ? "until his army breaks up" : "until " + (m.UntilDate.Length > 0 ? m.UntilDate : "the hour you set"));

        // ---------- the review's digest ----------

        /// <summary>The lords with no order the last digest listed (after the same filter as the follow-up question), put to the agent again after its answer.</summary>
        private List<Unordered> _digestUnordered;

        /// <summary>
        /// A review's digest (and a conversation's first answer's), from the files just built: the realm in sum, the money, every lord with no order, what became of its
        /// orders, its captive lords, and the question with each enemy army in view against what the realm could gather where it stands. War room 0.22.0: its header gives the
        /// game's hour the files were built from and how old that is (item 10: a median 9.7 hours, unlabelled); the lords with no order leave out those ordered since or in the
        /// last 12 hours (item 10) and those the agent left to the game's AI (item 31, on one line), and say how many march in the realm's armies (item 32); the money lists only when
        /// they changed since the last review (item 6).
        /// </summary>
        internal string ReviewDigest(double nowHours = double.NaN)
        {
            string dir = Path.Combine(_c.ChannelDir, Shelf.DirName);
            string Page(params string[] p) => ReadShared(Path.Combine(new[] { dir }.Concat(p).ToArray())) ?? "";
            IEnumerable<string> Bullets(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("- ", StringComparison.Ordinal));
            string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n).TrimEnd() + "…";
            var sb = new StringBuilder();
            double shelfHours = _shelf?.Hours ?? double.NaN;
            string shelfDate = _shelf?.Date ?? "";
            double age = !double.IsNaN(nowHours) && !double.IsNaN(shelfHours) ? nowHours - shelfHours : double.NaN;
            sb.AppendLine("REVIEW DIGEST, from your files as built from the game's hour of " + (shelfDate.Length > 0 ? shelfDate : "the last build")
                          + (double.IsNaN(age) ? "" : age >= 0.5 ? ", " + Math.Round(age).ToString(CultureInfo.InvariantCulture) + " in-game hour" + (Math.Round(age) == 1 ? "" : "s") + " before this review's hour" : ", this review's own hour")
                          + " (each page has more):");
            double viewHours = Num(_lastView?["hours"]);
            if (!double.IsNaN(viewHours) && !double.IsNaN(shelfHours) && viewHours > shelfHours + 0.5)
                sb.AppendLine("(THE QUESTION below reads the map of " + ((string)_lastView["date"] ?? Program.When(viewHours)) + ", newer than these files: where they differ, the newer is right.)");
            var sum = Bullets(Page("realm", "in-sum.md")).ToList();
            if (sum.Count > 0) { sb.AppendLine("YOUR REALM (realm/in-sum):"); foreach (string l in sum) sb.AppendLine(Cut(l, 400)); }
            var money = Bullets(Page("realm", "money.md")).Take(4).ToList();
            if (money.Count > 0)
            {
                string key = MoneyKey(money);
                if (_talk != null && _talk.DigestMoney == key)
                    sb.AppendLine("MONEY (realm/money): about as at your last review: no figure there has halved or doubled, and its lists name the same clans and lords. The page has today's figures.");
                else { sb.AppendLine("MONEY (realm/money):"); foreach (string l in money) sb.AppendLine(Cut(l, 900)); }
                if (_talk != null) _talk.DigestMoney = key;
            }
            _digestUnordered = null;
            if (_c.EveryLordOrdered)
            {
                List<Unordered> all = NoOrderLords(_c.ChannelDir, _shelf?.BuiltFromUtc);
                double h = !double.IsNaN(nowHours) ? nowHours : _pokeHours;
                var marks = _mem != null ? Left() : new Dictionary<string, LeftToAi>();
                List<Unordered> left = all.Where(l => IsLeft(l, h)).ToList();
                List<Unordered> none = StillUnordered(all, new HashSet<string>(), h);
                int recent = all.Count - left.Count - none.Count;
                int inArmies = _shelf?.InArmies ?? 0;
                _digestUnordered = none;
                sb.AppendLine("LORDS WITH NO ORDER OF YOURS: " + (inArmies + all.Count) + ". " + inArmies + (inArmies == 1 ? " marches" : " march") + " in your armies or to join one (they follow the army); "
                              + all.Count + (all.Count == 1 ? " is" : " are") + " led by the game's AI (lords/no-order)"
                              + (none.Count > 0 ? ": give each one listed below an order in this answer (a group order may cover several), unless you mean to leave him to the game's AI." : "."));
                foreach (Unordered l in none) sb.AppendLine("- " + l.Name + " [" + l.Id + "]: " + l.Men.ToString("N0", CultureInfo.InvariantCulture) + " men");
                if (recent > 0) sb.AppendLine("- (" + recent + (recent == 1 ? " more you ordered" : " more you ordered") + " after these files were built, or in the last 12 in-game hours, " + (recent == 1 ? "is" : "are") + " left out: " + (recent == 1 ? "his order" : "their orders") + " may not show yet.)");
                if (left.Count > 0) sb.AppendLine("- Left to the game's AI by your choice, not asked about until then: " + string.Join("; ", left.Select(l => LeftWords(l, marks[l.Id]))) + ".");
                if (all.Count == 0) sb.AppendLine("- Every lord leading his own party holds an order of yours now.");
            }
            var results = Bullets(Page("lords", "order-results.md")).Take(6).ToList();
            if (results.Count > 0) { sb.AppendLine("WHAT BECAME OF YOUR ORDERS (lords/order-results, newest first):"); foreach (string l in results) sb.AppendLine(Cut(l, 500)); }
            string captives = Page("realm", "captives.md");
            if (captives.Length > 0) sb.AppendLine("YOUR CAPTIVES (realm/captives): " + Cut(string.Join(" ", captives.Split('\n').Skip(2).Select(l => l.Trim()).Where(l => l.Length > 0)), 700));
            sb.Append(ArmyQuestion(_lastView, RouteTool.DefaultRoot()));
            return sb.ToString();
        }

        /// <summary>
        /// The money lists as the digest compares them from review to review: each line with its every number put in its band (a band doubles), so a list that only moved a little
        /// reads the same; a clan's gold halved or doubled, or a lord new to a list, reads differently.
        /// </summary>
        internal static string MoneyKey(IEnumerable<string> lines) =>
            string.Join("\n", lines.Select(l => Regex.Replace(l, @"\d[\d,]*(\.\d+)?", m =>
            {
                double v = double.TryParse(m.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
                return v < 16 ? "~" + Math.Round(v).ToString(CultureInfo.InvariantCulture) : "b" + Math.Floor(Math.Log(v, 2)).ToString(CultureInfo.InvariantCulture);
            })));

        /// <summary>
        /// The question the agent asks at every review: could it gather enough to beat each enemy army where it stands? Each enemy army in view (two parties or more), with
        /// the strength the realm's lords and armies could gather where it stands within 24, 48 and 72 hours (each at its own pace, in a straight line).
        /// </summary>
        internal static string ArmyQuestion(JsonObject view, string root = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("THE QUESTION, at every review: could you gather enough to beat each enemy army where it stands? Ask muster for who can be there by when, then battle_odds with those "
                          + "who arrive in time (how = strike for a siege camp at one of your places), and answer it in this review for each: strike now, gather to strike, or why not.");
            if (view == null) { sb.AppendLine("(The map view was not ready for this review: ask muster yourself.)"); return sb.ToString(); }
            double Z(JsonNode n) { double v = Num(n); return double.IsNaN(v) ? 0 : v; }
            var own = (view["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var armies = (view["seen"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(s => (string)s["relation"] == "enemy" && Z(s["army_parties"]) >= 2).OrderByDescending(s => Z(s["strength"])).Take(8).ToList();
            if (armies.Count == 0) { sb.AppendLine("- No enemy army in view now (intelligence/sightings has those seen earlier)."); return sb.ToString(); }
            bool byRoad = root != null;
            foreach (JsonObject a in armies)
            {
                double ax = Z(a["x"]), ay = Z(a["y"]);
                // war room 0.22.0 (item 5): each lord's hours by road at his pace, as the muster has them (the straight line only without the map)
                var hours = own.Select(o =>
                {
                    if (root == null)
                    {
                        double pace = Z(o["pace"]) > 0 ? Z(o["pace"]) : 2.8;
                        return Math.Sqrt((Z(o["x"]) - ax) * (Z(o["x"]) - ax) + (Z(o["y"]) - ay) * (Z(o["y"]) - ay)) / pace;
                    }
                    double h = RouteTool.OwnRoadHours(root, view, o, (float)ax, (float)ay, 72, out bool road);
                    byRoad &= road;
                    return h;
                }).ToList();
                double Within(double h, out int n)
                {
                    double s = 0;
                    n = 0;
                    for (int i = 0; i < own.Count; i++)
                    {
                        if (double.IsNaN(hours[i]) || hours[i] > h) continue;
                        n++;
                        s += Z(own[i]["army_strength"]) > 0 ? Z(own[i]["army_strength"]) : Z(own[i]["strength"]);
                    }
                    return s;
                }
                double s24 = Within(24, out int n24), s48 = Within(48, out int n48), s72 = Within(72, out int n72);
                sb.AppendLine("- " + (string)a["name"] + " [" + ((string)a["lord"] ?? (string)a["id"]) + "] (" + (string)a["faction_name"] + "): " + Z(a["army_parties"]).ToString("N0", CultureInfo.InvariantCulture)
                              + " parties, " + (Z(a["army_men"]) > 0 ? Z(a["army_men"]) : Z(a["men"])).ToString("N0", CultureInfo.InvariantCulture) + " men, strength " + Z(a["strength"]).ToString("N0", CultureInfo.InvariantCulture)
                              + "; " + ((string)a["doing"] ?? "doing what is not known") + (a["near"] is JsonValue ? " near " + (string)a["near"] : "")
                              + ". You could gather where it stands" + (byRoad ? " (by road, at each one's pace)" : "") + ": within 24 hours strength " + s24.ToString("N0", CultureInfo.InvariantCulture) + " (" + n24 + " lords and armies), within 48 hours "
                              + s48.ToString("N0", CultureInfo.InvariantCulture) + " (" + n48 + "), within 72 hours " + s72.ToString("N0", CultureInfo.InvariantCulture) + " (" + n72 + ").");
            }
            return sb.ToString();
        }
    }
}
