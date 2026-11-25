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

        /// <summary>The lords with no order, from the files' lords/no-order page (the game's own list, as of the last build).</summary>
        internal static List<Unordered> NoOrderLords(string channelDir)
        {
            var list = new List<Unordered>();
            string path = Path.Combine(channelDir, Shelf.DirName, "lords", "no-order.md");
            string text = ReadShared(path);
            if (text == null) return list;
            DateTime asOf = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
                Match m = Regex.Match(line, @"^- (.+?) \[([^\]]+)\]");
                if (!m.Success) continue;
                Match men = Regex.Match(line, @"(\d[\d,]*) men");
                int c = line.IndexOf("): ", StringComparison.Ordinal);
                list.Add(new Unordered { Name = m.Groups[1].Value, Id = m.Groups[2].Value, AsOf = asOf, Line = c > 0 ? line.Substring(c + 3).Trim() : "",
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

        private List<Unordered> StillUnordered(IEnumerable<Unordered> lords, ISet<string> answered, double hours) =>
            lords.Where(l => !answered.Contains(l.Id) && !OrderedSince(l, _orderedWall)
                             && !(_orderedAt.TryGetValue(l.Id, out double h) && !double.IsNaN(hours) && hours - h < OrderGraceHours)).ToList();

        /// <summary>After a wake's answer: the lords on the no-order page that this answer did not order, nor a recent one, and that were not put to it in the last day, wake it.</summary>
        private void AskAfterWake(int number, JsonObject answer, PokeMeta meta)
        {
            if (!_c.Files || !_c.EveryLordOrdered || answer == null) return;
            double hours = meta?.Hours ?? _pokeHours;
            NoteOrdered(answer, hours);
            var left = StillUnordered(NoOrderLords(_c.ChannelDir), OrderedIn(answer), hours)
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

        /// <summary>The message that wakes the agent about lords with no order: each one with where he is and what he does, and that the choice is its own.</summary>
        internal static string LordsQuestion(int number, List<Unordered> lords, string when)
        {
            var sb = new StringBuilder();
            sb.AppendLine("LORDS WITH NO ORDER OF YOURS (" + when + "): " + lords.Count + (lords.Count == 1 ? " lord holds" : " lords hold") + " no order of yours, so the game's own AI leads "
                          + (lords.Count == 1 ? "him" : "them") + ", and nobody else will order " + (lords.Count == 1 ? "him" : "them") + ":");
            foreach (Unordered l in lords)
                sb.AppendLine("- " + l.Name + " [" + l.Id + "]: " + (string.IsNullOrWhiteSpace(l.Line) ? l.Men.ToString("N0", CultureInfo.InvariantCulture) + " men" : l.Line));
            sb.Append("Decide what becomes of each: give him an order (a group order may cover several: name them in its lords), or leave him to the game's AI. "
                      + "Reply with ONLY one JSON object that says \"poke\": " + number + " and holds your orders for them; an answer with no orders leaves them all to the game's AI.");
            return sb.ToString();
        }

        /// <summary>Wakes the agent about lords with no order, in the same conversation: its answer's orders are given like any other; the lords it leaves stay with the game's AI.</summary>
        private void AskAboutLords(int number, List<Unordered> lords, PokeMeta meta, string call, string when)
        {
            double hours = meta?.Hours ?? _pokeHours;
            if (!double.IsNaN(hours)) foreach (Unordered l in lords) _askedAt[l.Id] = hours;
            string ask = LordsQuestion(number, lords, when);
            DateTime began = DateTime.Now;
            CallResult r = Converse(ask, call + "-lords", number, _c.ContinuousTimeoutSeconds, meta, out string folder, out bool stall);
            JsonObject more = null;
            string problem = r.Ok ? Extract(r.Text, number, out more) : r.Error;
            int given = 0;
            if (more != null)
            {
                more["kind"] = "council";
                WriteOrders(number, more, "lords", folder);
                Remember(more, number, meta?.Date, meta);
                File.WriteAllText(Path.Combine(folder, "answer.json"), more.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                NoteOrdered(more, hours);
                HashSet<string> named = OrderedIn(more);
                given = lords.Count(l => named.Contains(l.Id));
            }
            _talk.Save();
            LogCall("council", call + "-lords", r, number, meta?.Date, meta, folder, more, problem, false, stall, (DateTime.Now - began).TotalSeconds, ask.Length, false, "lords with no order");
            Say("Lords with no order put to the agent (" + when + "): " + lords.Count + "; it ordered " + given + (more == null ? " (its answer could not be read: " + problem + ")" : "")
                + ", the rest stay with the game's own AI: " + string.Join("; ", lords.Select(l => l.Name + " [" + l.Id + "]")) + ".");
        }

        /// <summary>
        /// A review's digest (and a conversation's first answer's), from the files just built: the realm in sum, the money, every lord with no order, what became of its
        /// orders, its captive lords, and the question with each enemy army in view against what the realm could gather where it stands.
        /// </summary>
        internal string ReviewDigest()
        {
            string dir = Path.Combine(_c.ChannelDir, Shelf.DirName);
            string Page(params string[] p) => ReadShared(Path.Combine(new[] { dir }.Concat(p).ToArray())) ?? "";
            IEnumerable<string> Bullets(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("- ", StringComparison.Ordinal));
            string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n).TrimEnd() + "…";
            var sb = new StringBuilder();
            sb.AppendLine("REVIEW DIGEST, from your files as just built (each page has more):");
            var sum = Bullets(Page("realm", "in-sum.md")).ToList();
            if (sum.Count > 0) { sb.AppendLine("YOUR REALM (realm/in-sum):"); foreach (string l in sum) sb.AppendLine(Cut(l, 400)); }
            var money = Bullets(Page("realm", "money.md")).Take(4).ToList();
            if (money.Count > 0) { sb.AppendLine("MONEY (realm/money):"); foreach (string l in money) sb.AppendLine(Cut(l, 900)); }
            if (_c.EveryLordOrdered)
            {
                List<Unordered> none = NoOrderLords(_c.ChannelDir);
                sb.AppendLine("LORDS WITH NO ORDER (" + none.Count + "; lords/no-order): the game's own AI leads them until you order them, and nobody else will: give each one an order in this answer "
                              + "(a group order may cover several), unless you mean to leave him to the game's AI."
                              + (none.Count == 0 ? " Every lord holds one now." : ""));
                foreach (Unordered l in none) sb.AppendLine("- " + l.Name + " [" + l.Id + "]: " + l.Men.ToString("N0", CultureInfo.InvariantCulture) + " men");
            }
            var results = Bullets(Page("lords", "order-results.md")).Take(6).ToList();
            if (results.Count > 0) { sb.AppendLine("WHAT BECAME OF YOUR ORDERS (lords/order-results, newest first):"); foreach (string l in results) sb.AppendLine(Cut(l, 500)); }
            string captives = Page("realm", "captives.md");
            if (captives.Length > 0) sb.AppendLine("YOUR CAPTIVES (realm/captives): " + Cut(string.Join(" ", captives.Split('\n').Skip(2).Select(l => l.Trim()).Where(l => l.Length > 0)), 700));
            sb.Append(ArmyQuestion(_lastView));
            return sb.ToString();
        }

        /// <summary>
        /// The question the agent asks at every review: could it gather enough to beat each enemy army where it stands? Each enemy army in view (two parties or more), with
        /// the strength the realm's lords and armies could gather where it stands within 24, 48 and 72 hours (each at its own pace, in a straight line).
        /// </summary>
        internal static string ArmyQuestion(JsonObject view)
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
            foreach (JsonObject a in armies)
            {
                double ax = Z(a["x"]), ay = Z(a["y"]);
                double Within(double h, out int n)
                {
                    double s = 0;
                    n = 0;
                    foreach (JsonObject o in own)
                    {
                        double pace = Z(o["pace"]) > 0 ? Z(o["pace"]) : 2.8;
                        double d = Math.Sqrt((Z(o["x"]) - ax) * (Z(o["x"]) - ax) + (Z(o["y"]) - ay) * (Z(o["y"]) - ay));
                        if (d / pace > h) continue;
                        n++;
                        s += Z(o["army_strength"]) > 0 ? Z(o["army_strength"]) : Z(o["strength"]);
                    }
                    return s;
                }
                double s24 = Within(24, out int n24), s48 = Within(48, out int n48), s72 = Within(72, out int n72);
                sb.AppendLine("- " + (string)a["name"] + " [" + ((string)a["lord"] ?? (string)a["id"]) + "] (" + (string)a["faction_name"] + "): " + Z(a["army_parties"]).ToString("N0", CultureInfo.InvariantCulture)
                              + " parties, " + (Z(a["army_men"]) > 0 ? Z(a["army_men"]) : Z(a["men"])).ToString("N0", CultureInfo.InvariantCulture) + " men, strength " + Z(a["strength"]).ToString("N0", CultureInfo.InvariantCulture)
                              + "; " + ((string)a["doing"] ?? "doing what is not known") + (a["near"] is JsonValue ? " near " + (string)a["near"] : "")
                              + ". You could gather where it stands: within 24 hours strength " + s24.ToString("N0", CultureInfo.InvariantCulture) + " (" + n24 + " lords and armies), within 48 hours "
                              + s48.ToString("N0", CultureInfo.InvariantCulture) + " (" + n48 + "), within 72 hours " + s72.ToString("N0", CultureInfo.InvariantCulture) + " (" + n72 + ").");
            }
            return sb.ToString();
        }
    }
}
