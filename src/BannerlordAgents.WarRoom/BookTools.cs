using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// A news wake's questions (0.14, docs/PLAN-news.md): look_up (one id in full), list (one kind of thing, all of it or in one region) and
    /// chronicle (the realm's chronicle about one lord, holding or realm), beside "ask the way" in the same in-process tool server. They read
    /// the poke's question book (book\poke-N.json in the channel, written by the bridge with the poke, from the same hour and the same view as
    /// the report, fog-checked like it), the realm's chronicle in the channel and the agent's own notes: so they can never tell the agent more
    /// than its realm knew at that hour. They replace the look-up files, which a news wake no longer reads.
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string LookName = "look_up", ListName = "list", ChronicleName = "chronicle";

        /// <summary>The poke's question book; null: the book questions are not offered.</summary>
        public string BookPath;
        /// <summary>Only the book questions (no view file or no map yet, or a replay).</summary>
        public bool BookOnly;
        /// <summary>check_tool is not offered (a news wake: tools are written at a council only).</summary>
        public bool NoCheck;
        /// <summary>The agent's notes as they stand (for list notes, and a look-up of an enemy it has a note on).</summary>
        public Func<string> Notes;
        /// <summary>Where the realm's chronicle is (the channel; a replay's scratch channel), for the chronicle question.</summary>
        public string ChronicleDir;
        /// <summary>The questions on the book asked in this call.</summary>
        public int AskedBook;

        private JsonObject _book;
        private bool _bookRead;

        private static readonly string[] Kinds = { "lords", "holdings", "enemies", "sieges", "orders", "captives", "allies", "diplomacy", "votes", "purse", "realm", "history", "notes", "regions" };

        private IEnumerable<string> ToolNames()
        {
            if (!BookOnly)
            {
                yield return ToolName; yield return OddsName; yield return RoadsName; yield return ChaseName; yield return EngageName;
                if (!NoCheck) yield return CheckName;
            }
            if (BookPath != null) { yield return LookName; yield return ListName; yield return ChronicleName; }
        }

        private JsonArray Definitions()
        {
            var list = new JsonArray();
            if (!BookOnly)
            {
                list.Add(Definition()); list.Add(OddsDefinition()); list.Add(RoadsDefinition()); list.Add(ChaseDefinition()); list.Add(EngageDefinition());
                if (!NoCheck) list.Add(CheckDefinition());
            }
            if (BookPath != null) { list.Add(LookDefinition()); list.Add(ListDefinition()); list.Add(ChronicleDefinition()); }
            return list;
        }

        public static JsonObject LookDefinition() => new JsonObject
        {
            ["name"] = LookName,
            ["description"] = "One thing your realm knows, in full, as of this report: a lord of yours (men, ranks, wounded, food, speed, purse, the hours to each of your holdings, the enemies near him), "
                              + "a holding of yours or an enemy castle or town near your lands (your lords by how soon they could be there, the enemies near it, the muster there with running totals), "
                              + "an enemy lord or army (where it is or was last seen, its trail, your holdings near it, your lords who can reach it soonest, your note on it), or a captured lord of yours. "
                              + "\"captured\" lists all your captured lords.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["id"] = new JsonObject { ["type"] = "string", ["description"] = "An id in [brackets] from your report or an answer, or a name." } },
                ["required"] = new JsonArray("id")
            }
        };

        public static JsonObject ListDefinition() => new JsonObject
        {
            ["name"] = ListName,
            ["description"] = "Everything of one kind your realm knows, as of this report, as the whole report would list it: lords (yours, with their armies), holdings (yours, and the enemy's near your lands), "
                              + "enemies (in view, seen earlier, and the enemy armies' trails), sieges (and battles, with their clocks), orders (yours in force), captives (your captured lords), allies (and joint plans), "
                              + "diplomacy, votes (open votes and offers), purse, realm (your realm in sum and the enemy realms), history (the last two days of your chronicle), notes (your notes on every enemy), "
                              + "or regions (your realm's regions and their holdings). Give a region's name to see only what is in it, and words to keep only the lines with them.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["what"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Kinds.Select(k => (JsonNode)k).ToArray()), ["description"] = "The kind of thing." },
                    ["region"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: a region of THE REST OF THE WAR, by its name (or the name or id of one of its holdings)." },
                    ["with"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: words or an id a line must have, such as \"free\", \"besieging\" or a name." }
                },
                ["required"] = new JsonArray("what")
            }
        };

        public static JsonObject ChronicleDefinition() => new JsonObject
        {
            ["name"] = ChronicleName,
            ["description"] = "Your chronicle's lines about one lord, holding or realm, oldest first, up to this report's hour: what your realm learned of it (sieges, battles, raids, captures, sightings) "
                              + "and your orders about it with what became of them. The last 40 lines, unless you give days.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["about"] = new JsonObject { ["type"] = "string", ["description"] = "An id in [brackets], or a name: of a lord, a holding or a realm." },
                    ["days"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: only the last this many in-game days." }
                },
                ["required"] = new JsonArray("about")
            }
        };

        private JsonObject Book()
        {
            if (_bookRead) return _book;
            _bookRead = true;
            try { _book = Room.ReadShared(BookPath ?? "") is string t ? JsonNode.Parse(t) as JsonObject : null; }
            catch (JsonException) { _book = null; }
            return _book;
        }

        private string BookAnswer(string tool, JsonObject args, Func<JsonObject, string> answer)
        {
            var sw = Stopwatch.StartNew();
            AskedBook++;
            JsonObject book = Book();
            string text = book == null ? "Your question book for this report is not there (it is written with each news wake); answer from the report." : Safe(() => answer(book));
            Log(new
            {
                at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool,
                args = args.ToJsonString(), chars = text.Length, ms = Math.Round(sw.Elapsed.TotalMilliseconds)
            });
            return text;
        }

        // ---------- look_up ----------

        public string LookUp(JsonObject args) => BookAnswer(LookName, args, book =>
        {
            string q = (Str(args["id"]) ?? "").Trim().Trim('[', ']').Trim();
            if (q.Length == 0) return "Name an id in [brackets] from your report, or a name.";
            JsonObject pages = book["pages"] as JsonObject ?? new JsonObject();
            string key = pages.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, q, StringComparison.OrdinalIgnoreCase));
            // by name: the page whose first line begins with it
            key ??= pages.Where(kv => kv.Value is JsonValue).Select(kv => kv.Key)
                         .FirstOrDefault(k => (Str(pages[k]) ?? "").StartsWith(q + " [", StringComparison.OrdinalIgnoreCase));
            // a party id of a lord whose page is by his own id: the list line that names both
            if (key == null)
                foreach (JsonObject item in Items(book).Select(x => x.Item2))
                {
                    List<string> ids = (item["ids"] as JsonArray)?.Select(x => Str(x)).Where(x => x != null).ToList() ?? new List<string>();
                    if (!ids.Contains(q, StringComparer.OrdinalIgnoreCase)) continue;
                    key = ids.FirstOrDefault(id => pages.ContainsKey(id));
                    if (key != null) break;
                }
            var sb = new StringBuilder();
            if (key != null) sb.Append((Str(pages[key]) ?? "").TrimEnd());
            else
            {
                // no page: the lines of the book's lists that name it
                List<string> lines = Items(book).Where(x => Names(x.Item2, q)).Select(x => Str(x.Item2["t"])).Where(x => x != null).Take(12).ToList();
                if (lines.Count == 0)
                    return "Your realm has no look-up for " + Show(q) + " as of " + Str(book["date"]) + ": it is not a lord of yours, a holding of yours, an enemy holding near your lands or in a siege you see, "
                           + "nor a lord or army your realm sees or saw lately. Use an id in [brackets] from your report or an answer; ask_the_way knows every holding on the map.";
                sb.AppendLine("No page of its own for " + Show(q) + "; the lines of your report's lists that name it, as of " + Str(book["date"]) + ":");
                foreach (string l in lines) sb.AppendLine(l);
            }
            string note = NoteOn(key != null ? FirstName(Str(pages[key])) : q);
            if (note != null) sb.Append("\n\nYour note on " + note);
            return sb.ToString().TrimEnd();
        });

        /// <summary>The name a page begins with ("Name [id] ...").</summary>
        private static string FirstName(string page)
        {
            if (page == null) return null;
            int b = page.IndexOf(" [", StringComparison.Ordinal);
            return b > 0 ? page.Substring(0, b).Trim() : null;
        }

        /// <summary>The agent's note on an enemy of that name, as "Name: note"; null if none.</summary>
        private string NoteOn(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Notes == null) return null;
            foreach (KeyValuePair<string, string> e in EnemyNotes(Notes()))
                if (string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase) || Room.Mentions(name, e.Key) || Room.Mentions(e.Key, name)) return e.Key + ": " + e.Value;
            return null;
        }

        internal static List<KeyValuePair<string, string>> EnemyNotes(string notes)
        {
            var list = new List<KeyValuePair<string, string>>();
            string enemies = Memory.Sections(notes ?? "").TryGetValue("Enemies", out string e) ? e : "";
            foreach (string raw in enemies.Replace("\r", "").Split('\n'))
            {
                string b = raw.Trim();
                if (!b.StartsWith("- ", StringComparison.Ordinal)) continue;
                b = b.Substring(2).Trim();
                int c = b.IndexOf(": ", StringComparison.Ordinal);
                list.Add(c > 0 ? new KeyValuePair<string, string>(b.Substring(0, c).Trim(), b.Substring(c + 2).Trim()) : new KeyValuePair<string, string>(b, ""));
            }
            return list;
        }

        // ---------- list ----------

        private const int MostChars = 24000;

        public string List(JsonObject args) => BookAnswer(ListName, args, book =>
        {
            string what = (Str(args["what"]) ?? "").Trim().ToLowerInvariant();
            if (what == "areas") what = "regions";
            string region = Str(args["region"]), with = Str(args["with"]);
            if (!Kinds.Contains(what)) return "Ask for one of: " + string.Join(", ", Kinds) + ".";
            var areas = (book["areas"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            if (what == "regions")
            {
                if (areas.Count == 0) return "Your realm holds no castle or town, so it has one region: the field.";
                var names = HoldingNames(book);
                return "YOUR REGIONS (" + areas.Count + "), each named after one of its castles or towns, as of " + Str(book["date"]) + ":\n"
                       + string.Join("\n", areas.Select(a => "- " + Str(a["name"]) + ": " + string.Join(", ", (a["holdings"] as JsonArray ?? new JsonArray()).Select(h => names.TryGetValue(Str(h) ?? "", out string n) ? n + " [" + Str(h) + "]" : Str(h)))));
            }
            if (what == "notes")
            {
                List<KeyValuePair<string, string>> notes = EnemyNotes(Notes?.Invoke());
                if (!string.IsNullOrWhiteSpace(with)) notes = notes.Where(n => Room.Mentions(n.Key + " " + n.Value, with.Trim())).ToList();
                return notes.Count == 0 ? "You have no notes on enemies" + (with != null ? " with \"" + with + "\"" : "") + "."
                    : "YOUR NOTES ON ENEMIES (" + notes.Count + ")\n" + string.Join("\n", notes.Select(n => "- " + n.Key + ": " + n.Value));
            }
            string area = null;
            if (!string.IsNullOrWhiteSpace(region))
            {
                area = FindArea(book, areas, region.Trim().Trim('[', ']'));
                if (area == null) return "Your realm has no region called " + Show(region) + ". Its regions: " + string.Join(", ", areas.Select(a => Str(a["name"]))) + ".";
            }
            JsonArray items = (book["lists"] as JsonObject)?[what] as JsonArray ?? new JsonArray();
            List<string> lines = items.OfType<JsonObject>()
                                      .Where(i => area == null || string.Equals(Str(i["area"]), area, StringComparison.OrdinalIgnoreCase))
                                      .Where(i => string.IsNullOrWhiteSpace(with) || Names(i, with.Trim()) || (Str(i["t"]) ?? "").IndexOf(with.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                                      .Select(i => Str(i["t"])).Where(t => t != null).ToList();
            string head = Str((book["heads"] as JsonObject)?[what]) ?? what.ToUpperInvariant();
            var sb = new StringBuilder(head + (area != null ? ", AROUND " + area.ToUpperInvariant() : "") + (with != null ? ", WITH \"" + with.Trim() + "\"" : "")
                                       + " (" + lines.Count + "), as of " + Str(book["date"]) + "\n");
            string note = Str((book["notes"] as JsonObject)?[what]);
            if (note != null && lines.Count > 0) sb.AppendLine(note);
            if (lines.Count == 0) sb.AppendLine("- None.");
            int shown = 0;
            foreach (string l in lines)
            {
                if (sb.Length + l.Length > MostChars) break;
                sb.AppendLine(l);
                shown++;
            }
            if (shown < lines.Count) sb.AppendLine("- (" + (lines.Count - shown) + " more: narrow it with a region or with words)");
            return sb.ToString().TrimEnd();
        });

        private static Dictionary<string, string> HoldingNames(JsonObject book)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode n in (book["lists"] as JsonObject)?["holdings"] as JsonArray ?? new JsonArray())
            {
                string t = Str(n?["t"]);
                if (t == null) continue;
                string s = t.StartsWith("- ENEMY ", StringComparison.Ordinal) ? t.Substring(8) : t.StartsWith("- ", StringComparison.Ordinal) ? t.Substring(2) : t;
                int a = s.IndexOf(" [", StringComparison.Ordinal), b = s.IndexOf(']', Math.Max(0, a));
                if (a > 0 && b > a) d[s.Substring(a + 2, b - a - 2)] = s.Substring(0, a);
            }
            return d;
        }

        /// <summary>A region by its name, or by the name or id of one of its holdings.</summary>
        private static string FindArea(JsonObject book, List<JsonObject> areas, string q)
        {
            JsonObject a = areas.FirstOrDefault(x => string.Equals(Str(x["name"]), q, StringComparison.OrdinalIgnoreCase))
                           ?? areas.FirstOrDefault(x => (Str(x["name"]) ?? "").StartsWith(q, StringComparison.OrdinalIgnoreCase));
            if (a != null) return Str(a["name"]);
            Dictionary<string, string> names = HoldingNames(book);
            string id = names.Keys.FirstOrDefault(k => string.Equals(k, q, StringComparison.OrdinalIgnoreCase))
                        ?? names.FirstOrDefault(kv => string.Equals(kv.Value, q, StringComparison.OrdinalIgnoreCase)).Key;
            if (id == null) return null;
            a = areas.FirstOrDefault(x => (x["holdings"] as JsonArray ?? new JsonArray()).Any(h => string.Equals(Str(h), id, StringComparison.OrdinalIgnoreCase)));
            return a != null ? Str(a["name"]) : null;
        }

        /// <summary>Every item of every list, with its list's name.</summary>
        private static IEnumerable<Tuple<string, JsonObject>> Items(JsonObject book)
        {
            if (book["lists"] is not JsonObject lists) yield break;
            foreach (KeyValuePair<string, JsonNode> kv in lists)
                foreach (JsonObject i in (kv.Value as JsonArray ?? new JsonArray()).OfType<JsonObject>()) yield return Tuple.Create(kv.Key, i);
        }

        /// <summary>A list item names an id or a name: among its ids, or as whole words in its text.</summary>
        private static bool Names(JsonObject item, string q)
        {
            if ((item["ids"] as JsonArray)?.Any(x => string.Equals(Str(x), q, StringComparison.OrdinalIgnoreCase)) == true) return true;
            return q.Length >= 3 && Room.Mentions(Str(item["t"]) ?? "", q);
        }

        // ---------- chronicle ----------

        public string Chronicle(JsonObject args) => BookAnswer(ChronicleName, args, book =>
        {
            string q = (Str(args["about"]) ?? "").Trim().Trim('[', ']').Trim();
            if (q.Length == 0) return "Name a lord, a holding or a realm: an id in [brackets] or a name.";
            double days = args["days"] is JsonValue dv && dv.TryGetValue(out double d) ? d : double.NaN;
            double now = Num(book["hours"]);
            // an id means its name too: chronicle lines name things by name
            var words = new List<string> { q };
            JsonObject pages = book["pages"] as JsonObject ?? new JsonObject();
            string page = pages.Where(kv => string.Equals(kv.Key, q, StringComparison.OrdinalIgnoreCase)).Select(kv => Str(kv.Value)).FirstOrDefault();
            string name = FirstName(page);
            if (name == null)
                foreach (JsonObject item in Items(book).Select(x => x.Item2))
                {
                    if ((item["ids"] as JsonArray)?.Any(x => string.Equals(Str(x), q, StringComparison.OrdinalIgnoreCase)) != true) continue;
                    string t = Str(item["t"]) ?? "";
                    int b = t.IndexOf(" [" + q + "]", StringComparison.OrdinalIgnoreCase);
                    if (b > 0) { string head = t.Substring(0, b); int sp = head.LastIndexOf("- ", StringComparison.Ordinal); name = (sp >= 0 ? head.Substring(sp + 2) : head).Replace("ENEMY ", "").Replace("SEEN EARLIER ", "").Trim(); break; }
                }
            if (!string.IsNullOrWhiteSpace(name)) words.Add(name);
            List<ChronicleLine> all = Memory.ReadChronicle(ChronicleDir ?? ChannelDir ?? "").Where(l => double.IsNaN(now) || l.Hours <= now + 1).ToList();
            List<ChronicleLine> about = all.Where(l => (double.IsNaN(days) || double.IsNaN(now) || l.Hours >= now - 24 * days)
                                                      && words.Any(w => l.Text.IndexOf("[" + w + "]", StringComparison.OrdinalIgnoreCase) >= 0 || (w.Length >= 3 && Room.Mentions(l.Text, w)))).ToList();
            if (about.Count == 0) return "Your chronicle has no line about " + Show(q) + (name != null && name != q ? " (" + name + ")" : "") + (double.IsNaN(days) ? "" : " in the last " + days.ToString("0.#", CultureInfo.InvariantCulture) + " days") + ".";
            int skip = double.IsNaN(days) ? Math.Max(0, about.Count - 40) : 0;
            var sb = new StringBuilder("YOUR CHRONICLE ABOUT " + (name ?? q).ToUpperInvariant() + " (" + about.Count + " lines" + (skip > 0 ? ", the last 40 shown" : "") + ", oldest first)\n");
            foreach (ChronicleLine l in about.Skip(skip))
            {
                if (sb.Length > MostChars) { sb.AppendLine("- (more lines: ask for fewer days)"); break; }
                sb.AppendLine("- " + l.Date + " · " + l.Text);
            }
            return sb.ToString().TrimEnd();
        });
    }
}
