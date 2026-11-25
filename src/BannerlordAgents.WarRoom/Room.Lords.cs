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
    /// with the right numbers). My rules since: every lord must be under an order of the agent's, it reviews its lords every day to take back control of each, it holds
    /// a strategy review every day, its review brings the money, and it always asks whether it can gather enough to beat each enemy army where it stands.
    /// - A files agent's review (and the first answer of a conversation) opens with a digest from its files: the realm in sum, the money, every lord with no order, what
    ///   became of its orders, its captive lords, and the question with each enemy army in view and the strength the realm could gather where it stands.
    /// - After a review's answer, the lords it left without an order are asked for again (twice at most); any still left get the war room's standing order (defend the
    ///   nearest holding of the realm, a weak lord one well back from any threat), and the agent is told at its next message.
    /// - After a wake's answer, a lord newly without an order (freed, his order ended, new) gets the standing order at once, so none is led by the game's AI between
    ///   reviews. A lord the agent ordered in the last 12 in-game hours is left alone (his order may still be on its way).
    /// </summary>
    internal sealed partial class Room
    {
        internal sealed class Unordered
        {
            public string Id, Name;
            public int Men;
        }

        /// <summary>When the agent last ordered each lord (in-game hours), and when the war room last gave him its standing order.</summary>
        private readonly Dictionary<string, double> _orderedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _standingAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        /// <summary>What the agent is told at its next message: the standing orders given since its last answer.</summary>
        private string _lordsNote;
        /// <summary>The realm's view file of the last build of its files (the digest's question reads it).</summary>
        private JsonObject _lastView;
        /// <summary>The lords with no order as the review's digest listed them (enforced after its answer).</summary>
        private List<Unordered> _reviewUnordered;

        internal const double OrderGraceHours = 12;
        internal const int WeakMen = 300;

        /// <summary>The lords with no order, from the files' lords/no-order page (the game's own list, as of the last build).</summary>
        internal static List<Unordered> NoOrderLords(string channelDir)
        {
            var list = new List<Unordered>();
            string text = ReadShared(Path.Combine(channelDir, Shelf.DirName, "lords", "no-order.md"));
            if (text == null) return list;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;
                Match m = Regex.Match(line, @"^- (.+?) \[([^\]]+)\]");
                if (!m.Success) continue;
                Match men = Regex.Match(line, @"(\d[\d,]*) men");
                list.Add(new Unordered { Name = m.Groups[1].Value, Id = m.Groups[2].Value,
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

        private void NoteOrdered(JsonObject answer, double hours)
        {
            if (answer == null || double.IsNaN(hours)) return;
            foreach (string id in OrderedIn(answer)) _orderedAt[id] = hours;
        }

        private List<Unordered> StillUnordered(IEnumerable<Unordered> lords, ISet<string> answered, double hours) =>
            lords.Where(l => !answered.Contains(l.Id) && !(_orderedAt.TryGetValue(l.Id, out double h) && !double.IsNaN(hours) && hours - h < OrderGraceHours)).ToList();

        /// <summary>
        /// The war room's standing order for lords with none: defend the nearest castle or town of the realm; for a weak lord (under 300 men) the nearest one well back
        /// from any threat, if not much farther. Lords not in the view (not in the field) are left out.
        /// </summary>
        internal static JsonArray StandingOrders(IEnumerable<Unordered> lords, JsonObject view)
        {
            var orders = new JsonArray();
            var own = (view?["own"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var holds = (view?["holdings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(h => (string)h["kind"] == "castle" || (string)h["kind"] == "town").ToList();
            if (holds.Count == 0) return orders;
            double Z(JsonNode n) { double v = Num(n); return double.IsNaN(v) ? 0 : v; }
            bool Calm(JsonObject h, double far, double many) => Z(h["threat_men"]) <= 0 || Z(h["threat_distance"]) >= far || Z(h["threat_men"]) < many;
            foreach (Unordered l in lords)
            {
                JsonObject me = own.FirstOrDefault(o => string.Equals((string)o["lord"], l.Id, StringComparison.OrdinalIgnoreCase) || string.Equals((string)o["id"], l.Id, StringComparison.OrdinalIgnoreCase));
                if (me == null) continue;
                double x = Z(me["x"]), y = Z(me["y"]);
                double D(JsonObject h) => Math.Sqrt((Z(h["x"]) - x) * (Z(h["x"]) - x) + (Z(h["y"]) - y) * (Z(h["y"]) - y));
                var near = holds.OrderBy(D).ToList();
                bool weak = l.Men < WeakMen;
                JsonObject pick = near.FirstOrDefault(h => (weak ? Calm(h, 40, 1) : Calm(h, 20, 2000)) && D(h) <= 3 * D(near[0]) + 20) ?? near[0];
                orders.Add(new JsonObject
                {
                    ["lord"] = l.Id, ["do"] = "defend", ["at"] = (string)pick["id"],
                    ["why"] = "standing order from the war room: he had no order of yours; your own order replaces it"
                });
            }
            return orders;
        }

        /// <summary>Gives the standing order to these lords (their file in the orders folder), keeps the note for the agent's next message, and says who.</summary>
        private void GiveStanding(int number, List<Unordered> lords, double hours, string when)
        {
            JsonArray orders = StandingOrders(lords, _lastView);
            if (orders.Count == 0) return;
            WriteOrders(number, new JsonObject { ["orders"] = orders }, "standing");
            var named = new List<string>();
            foreach (JsonObject o in orders.OfType<JsonObject>())
            {
                string id = (string)o["lord"];
                if (!double.IsNaN(hours)) _standingAt[id] = hours;
                named.Add((lords.FirstOrDefault(l => l.Id == id)?.Name ?? id) + " [" + id + "] at " + (string)o["at"]);
            }
            string note = "STANDING ORDERS FROM THE WAR ROOM (" + when + "): every lord must hold an order of yours, and these had none, so each now defends the nearest castle or town of yours "
                          + "(a weak lord one well back from any threat) until you give him your own: " + string.Join("; ", named) + ".";
            _lordsNote = _lordsNote == null ? note : _lordsNote + "\n" + note;
            Say("Standing orders for " + orders.Count + " lord" + (orders.Count == 1 ? "" : "s") + " with no order: " + string.Join("; ", named) + ".");
        }

        /// <summary>The note on standing orders for the agent's next message, once.</summary>
        private string TakeLordsNote()
        {
            string n = _lordsNote;
            _lordsNote = null;
            return n;
        }

        /// <summary>After a wake's answer: the lords on the no-order page that this answer did not order, nor a recent one, get the standing order at once.</summary>
        private void StandingAfterWake(int number, JsonObject answer, double hours)
        {
            if (!_c.Files || !_c.EveryLordOrdered || answer == null) return;
            NoteOrdered(answer, hours);
            var left = StillUnordered(NoOrderLords(_c.ChannelDir), OrderedIn(answer), hours)
                .Where(l => !(_standingAt.TryGetValue(l.Id, out double s) && !double.IsNaN(hours) && hours - s < OrderGraceHours)).ToList();
            if (left.Count > 0) GiveStanding(number, left, hours, "after your answer to wake #" + number);
        }

        /// <summary>
        /// After a review's answer: the lords its digest listed with no order that it left so, asked for again (twice at most) in the same conversation; any still left
        /// get the standing order.
        /// </summary>
        private void EnforceAfterReview(int number, JsonObject answer, PokeMeta meta, List<Unordered> atReview, string call)
        {
            if (atReview == null || answer == null) return;
            double hours = meta?.Hours ?? _pokeHours;
            NoteOrdered(answer, hours);
            var answered = OrderedIn(answer);
            List<Unordered> missing = StillUnordered(atReview, answered, hours);
            for (int round = 1; round <= 2 && missing.Count > 0; round++)
            {
                string ask = "EVERY LORD MUST HOLD AN ORDER OF YOURS. These " + missing.Count + " still have none, so the game's own AI leads them: "
                             + string.Join("; ", missing.Select(l => l.Name + " [" + l.Id + "] (" + l.Men.ToString("N0", CultureInfo.InvariantCulture) + " men)"))
                             + ". Give each of them an order now (a group order may cover several: name them in its lords). Reply with ONLY one JSON object that says \"poke\": " + number + " and holds those orders.";
                DateTime began = DateTime.Now;
                CallResult r = Converse(ask, call + "-lords", number, _c.ContinuousTimeoutSeconds, meta, out string folder, out bool stall);
                JsonObject more = null;
                string problem = r.Ok ? Extract(r.Text, number, out more) : r.Error;
                if (more != null)
                {
                    more["kind"] = "council";
                    WriteOrders(number, more, "lords" + round, folder);
                    Remember(more, number, meta?.Date, meta);
                    File.WriteAllText(Path.Combine(folder, "answer.json"), more.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    NoteOrdered(more, hours);
                    foreach (string id in OrderedIn(more)) answered.Add(id);
                }
                _talk.Save();
                LogCall("council", call + "-lords", r, number, meta?.Date, meta, folder, more, problem, false, stall, (DateTime.Now - began).TotalSeconds, ask.Length, false, "lords with no order");
                if (more == null) break;
                missing = StillUnordered(atReview, answered, hours);
            }
            if (missing.Count > 0) GiveStanding(number, missing, hours, "after your review of " + (meta?.Date ?? "now"));
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
                sb.AppendLine("LORDS WITH NO ORDER (" + none.Count + "; lords/no-order): every lord must hold an order of yours; give each one an order in this answer (a group order may cover several)."
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
