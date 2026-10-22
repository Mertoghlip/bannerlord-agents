using System.Text.Json.Nodes;

namespace BannerlordAgents.Dashboard
{
    /// <summary>One line of the agent's log on the dashboard: what woke it, what it answered, what became of each order, and what it asked.</summary>
    internal sealed class LogEntry
    {
        /// <summary>woke, answer, council, no_answer, carried, refused, checked, ended, report, asked.</summary>
        public string Kind { get; set; }
        public double Hours { get; set; } = double.NaN;
        public string At { get; set; }
        public int Poke { get; set; } = -1;
        public string Text { get; set; }
        /// <summary>Party ids and hero ids the line names (the map finds either).</summary>
        public List<string> Ids { get; set; } = new List<string>();
        public List<string> Places { get; set; } = new List<string>();
        /// <summary>An answer's orders: lord, what, where or whom, why.</summary>
        public List<OrderLine> Orders { get; set; }
        public string Plan { get; set; }
        public double Seconds { get; set; } = double.NaN;
    }

    internal sealed class OrderLine
    {
        public string Lord { get; set; }
        public string Do { get; set; }
        public string Target { get; set; }
        public string At { get; set; }
        public string Why { get; set; }
        public List<string> Lords { get; set; }
        public string Then { get; set; }
    }

    /// <summary>
    /// The agent's log for one load of a campaign, gathered from the records others write: the realm's pokes (data\pokes_(realm).jsonl),
    /// the war room's answers and councils (logs\warroom\calls.jsonl and each call's answer.json), the receipts and the ends of
    /// orders (data\orders.jsonl), the tools' call-backs (data\notices.jsonl) and the routes it asked about (logs\warroom\routes.jsonl).
    /// One load's lines are told by where each file stood when it began (data\loads.jsonl). Each file is read on from where it
    /// was left, so a long session costs little.
    /// </summary>
    internal sealed class LogReader
    {
        private readonly Store _store;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Source> _sources = new Dictionary<string, Source>();

        private sealed class Source
        {
            public long Pos;
            public readonly List<LogEntry> Entries = new List<LogEntry>();
        }

        public LogReader(Store store) => _store = store;

        public List<LogEntry> Read(string campaign, LoadInfo load, string realm, int most = 500)
        {
            lock (_gate)
            {
                // a realm after the first has a war room of its own, with its own logs (logswarroom-<realm>, since 0.8)
                bool own = realm != null && load?.Realm != null && realm != load.Realm && Directory.Exists(_store.WarRoomLogsOf(realm));
                string data = _store.DataDir(campaign), war = own ? _store.WarRoomLogsOf(realm) : _store.WarRoomLogs;
                string keyOf(string f) => own ? "warroom-" + realm + "/" + f : f;
                DateTime since = DateTime.MinValue;
                if (load?.Wall != null) DateTime.TryParse(load.Wall, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out since);
                var all = new List<LogEntry>();
                Dictionary<int, double> pokeHours = new Dictionary<int, double>();
                if (realm != null)
                    foreach (LogEntry e in Take(Path.Combine(data, "pokes_" + realm + ".jsonl"), load, Poke))
                    {
                        all.Add(e);
                        if (e.Poke >= 0) pokeHours[e.Poke] = e.Hours;
                    }
                foreach (LogEntry e in Take(Path.Combine(war, "calls.jsonl"), load, l => Call(l, campaign, since), keyOf("calls.jsonl")))
                {
                    if (pokeHours.TryGetValue(e.Poke, out double h)) e.Hours = h + 0.01;
                    if (!double.IsNaN(e.Hours) || e.Poke < 0) all.Add(e);
                }
                all.AddRange(Take(Path.Combine(data, "orders.jsonl"), load, l => Order(l, realm)));
                all.AddRange(Take(Path.Combine(data, "notices.jsonl"), load, l => Notice(l, realm)));
                foreach (LogEntry e in Take(Path.Combine(war, "routes.jsonl"), load, l => Route(l, campaign, since), keyOf("routes.jsonl")))
                {
                    if (double.IsNaN(e.Hours) && pokeHours.TryGetValue(e.Poke, out double h)) e.Hours = h + 0.005;
                    if (!double.IsNaN(e.Hours)) all.Add(e);
                }
                return all.OrderBy(e => e.Hours).ThenBy(e => Rank(e.Kind)).Skip(Math.Max(0, all.Count - most)).ToList();
            }
        }

        private static int Rank(string kind) => kind == "woke" ? 0 : kind == "asked" ? 1 : kind == "answer" || kind == "council" || kind == "no_answer" ? 2 : 3;

        /// <summary>A file's lines from where this load began to where the next began, read on from where they were left.</summary>
        private List<LogEntry> Take(string path, LoadInfo load, Func<string, LogEntry> parse, string offsetKey = null)
        {
            string name = offsetKey ?? Path.GetFileName(path);
            long from = load != null && load.Offsets.TryGetValue(name, out long a) ? a : 0;
            long to = load != null && load.Ends.TryGetValue(name, out long b) ? b : long.MaxValue;
            string key = path + "|" + from;
            if (!_sources.TryGetValue(key, out Source src)) _sources[key] = src = new Source { Pos = from };
            if (src.Pos < to)
            {
                List<string> lines = Store.ReadLines(path, src.Pos, to, out long end);
                foreach (string line in lines)
                {
                    try
                    {
                        LogEntry e = parse(line);
                        if (e != null) src.Entries.Add(e);
                    }
                    catch (Exception ex) when (ex is System.Text.Json.JsonException || ex is InvalidOperationException || ex is FormatException) { }
                }
                if (end > src.Pos) src.Pos = end;
            }
            return src.Entries;
        }

        private static double Num(JsonNode n, double fallback = double.NaN) => n is JsonValue v && v.TryGetValue(out double d) ? d : fallback;
        private static int Int(JsonNode n) => n is JsonValue v && v.TryGetValue(out int i) ? i : (n is JsonValue v2 && v2.TryGetValue(out double d) ? (int)d : -1);
        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;

        private static LogEntry Poke(string line)
        {
            // a poke's line carries the whole view and report too: only its head is read
            if (JsonNode.Parse(line) is not JsonObject o || Str(o["type"]) != "poke") return null;
            var e = new LogEntry { Kind = "woke", Poke = Int(o["number"]), Hours = Num(o["hours"]) };
            var texts = new List<string>();
            if (o["triggers"] is JsonArray ts)
                foreach (JsonNode t in ts)
                {
                    string text = Str(t?["text"]);
                    if (text != null) texts.Add(text);
                    string party = Str(t?["party"]), place = Str(t?["place"]);
                    if (party != null) e.Ids.Add(party);
                    if (place != null) e.Places.Add(place);
                }
            e.Text = string.Join(" ", texts.Take(5)) + (texts.Count > 5 ? " (and " + (texts.Count - 5) + " more)" : "");
            return e;
        }

        private static LogEntry Call(string line, string campaign, DateTime since)
        {
            if (JsonNode.Parse(line) is not JsonObject o) return null;
            string kind = Str(o["kind"]);
            if (kind != "answer" && kind != "council") return null;
            string c = Str(o["campaign"]);
            if (c != null && campaign != null && c != campaign) return null;
            string at = Str(o["at"]);
            if (at != null && DateTime.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime when) && when < since) return null;
            if (o["written"] is JsonValue wv && wv.TryGetValue(out bool written) && !written && o["ok"] is JsonValue okv && okv.TryGetValue(out bool okw) && okw) return null;   // a replay
            bool ok = o["ok"] is JsonValue okv2 && okv2.TryGetValue(out bool okb) && okb;
            var e = new LogEntry { Kind = ok ? kind : "no_answer", Poke = Int(o["poke"]), At = at, Seconds = Num(o["seconds"]) };
            if (!ok) { e.Text = (kind == "council" ? "Council of war: no answer: " : "No answer: ") + (Str(o["problem"]) ?? "?"); return e; }
            string folder = Str(o["folder"]);
            JsonObject answer = null;
            try { if (folder != null) answer = Store.ReadJson(Path.Combine(folder, "answer.json")) as JsonObject; }
            catch (Exception ex) when (ex is IOException || ex is System.Text.Json.JsonException) { }
            e.Orders = new List<OrderLine>();
            if (answer?["orders"] is JsonArray orders)
                foreach (JsonNode n in orders)
                {
                    if (n is not JsonObject ord) continue;
                    var ol = new OrderLine { Lord = Str(ord["lord"]), Do = Str(ord["do"]), Target = Str(ord["target"]), At = Str(ord["at"]), Why = Str(ord["why"]) };
                    if (ord["lords"] is JsonArray ls) ol.Lords = ls.Select(Str).Where(x => x != null).ToList();
                    if (ord["then"] is JsonObject th) { string tt = Str(th["at"]) ?? Str(th["target"]); ol.Then = (Str(th["do"]) ?? "?") + (tt != null ? " " + tt : ""); }
                    e.Orders.Add(ol);
                    if (ol.Lord != null) e.Ids.Add(ol.Lord);
                    if (ol.Target != null) e.Ids.Add(ol.Target);
                    if (ol.At != null) e.Places.Add(ol.At);
                }
            e.Plan = Str(answer?["plan"]) ?? Str(answer?["note"]);
            e.Text = (kind == "council" ? "Council of war: " : "Answered: ") + e.Orders.Count + (e.Orders.Count == 1 ? " order" : " orders")
                     + (double.IsNaN(e.Seconds) ? "" : " in " + Math.Round(e.Seconds) + " s");
            return e;
        }

        private static LogEntry Order(string line, string realm)
        {
            if (JsonNode.Parse(line) is not JsonObject o) return null;
            string type = Str(o["type"]);
            if (type == "agent_receipt")
            {
                if (realm != null && Str(o["realm"]) is string rr && rr != realm) return null;
                string status = Str(o["status"]);
                string kind = status == "applied" ? "carried" : status == "refused" ? "refused" : status == "checked" ? "checked" : null;
                if (kind == null) return null;
                var e = new LogEntry { Kind = kind, Hours = Num(o["hours"]), Poke = Int(o["poke"]) };
                string lord = Str(o["lord"]);
                if (lord != null) e.Ids.Add(lord);
                string what = (Str(o["do"]) ?? "?") + (Str(o["why"]) is string why ? " (" + why + ")" : "");
                e.Text = (kind == "carried" ? "Carried out: " : kind == "refused" ? "Refused: " : "Checked, not carried out (shadow): ") + (Str(o["reason"]) ?? what);
                return e;
            }
            if (type == "order" && Str(o["source"]) == "agent" && Str(o["event"]) == "ended")
            {
                if (realm != null && Str(o["realm"]) is string r && r != realm) return null;
                var e = new LogEntry { Kind = "ended", Hours = Num(o["hours"]), Poke = Int(o["poke"]) };
                string lord = Str(o["lord"]);
                if (lord != null) e.Ids.Add(lord);
                string target = Str(o["target"]);
                if (target != null) e.Ids.Add(target);
                e.Text = "Order ended: " + (Str(o["lord_name"]) ?? lord ?? "?") + " (" + (Str(o["tool"]) ?? Str(o["verb"]) ?? "?") + "): " + (Str(o["text"]) ?? "");
                return e;
            }
            return null;
        }

        private static LogEntry Notice(string line, string realm)
        {
            if (!line.Contains("\"kind\":\"tool\"")) return null;
            if (JsonNode.Parse(line) is not JsonObject o || Str(o["kind"]) != "tool") return null;
            if (realm != null && o["factions"] is JsonArray fs && !fs.Any(f => Str(f) == realm)) return null;
            var e = new LogEntry { Kind = "report", Hours = Num(o["hours"]), Text = "Reported back: " + (Str(o["text"]) ?? "") };
            if (Str(o["party"]) is string p) e.Ids.Add(p);
            if (Str(o["place"]) is string pl) e.Places.Add(pl);
            return e;
        }

        private static LogEntry Route(string line, string campaign, DateTime since)
        {
            if (JsonNode.Parse(line) is not JsonObject o) return null;
            string c = Str(o["campaign"]);
            if (c != null && campaign != null && c != campaign) return null;
            string at = Str(o["at"]);
            if (at != null && DateTime.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime when) && when < since) return null;
            var e = new LogEntry { Kind = "asked", At = at, Poke = Int(o["poke"]), Hours = Num(o["hours"]) };
            string from = Str(o["from"]) ?? "?", to = Str(o["to"]) ?? "?";
            if (Str(o["from_id"]) is string fi) e.Ids.Add(fi);
            if (Str(o["to_id"]) is string ti) { e.Ids.Add(ti); e.Places.Add(ti); }
            string first = o["lines"] is JsonArray ls && ls.Count > 0 ? Str(ls[0]) : Str(o["error"]);
            e.Text = "Asked the way" + (Str(o["call"]) == "council" ? " (council)" : "") + ": " + from + " to " + to + (first != null ? ". " + first : "");
            return e;
        }
    }
}
