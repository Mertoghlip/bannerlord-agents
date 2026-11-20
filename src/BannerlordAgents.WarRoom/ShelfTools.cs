using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The questions on the realm's files (war room 0.20, docs/FILES.md): open (pages whole, brought up to the newest hour first), find (the lines of every page that hold some
    /// words) and history (the old versions of the agent's own pages). The agent may also read any page by its path with the Read tool; both ways mark the page read.
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string OpenName = "open", FindName = "find", HistoryName = "history";

        /// <summary>The realm's files; null: the files questions are not offered (a report agent).</summary>
        public Shelf Shelf;
        /// <summary>Brings the shelf up to the game's newest hour before an open (the war room asks the game for a fresh book and builds again when it is newer).</summary>
        public Action ShelfRefresh;
        /// <summary>The campaign's memory folder, for history.</summary>
        public string HistoryMem;
        public int AskedShelf;

        public static JsonObject OpenDefinition() => new JsonObject
        {
            ["name"] = OpenName,
            ["description"] = "Pages of your realm's files, whole, as of your realm's newest hour when you ask: each by its path (\"settlements/hiltbolt\", \"lords/index\", \"plan/the-plan\"), a folder's name (its contents) "
                              + "or \"shelf\". Several at once. The same pages you can read as files/<folder>/<page>.md; either way the page's changes are no longer new to you.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["pages"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "The pages, by path (folder/page), folder name, or a name or id a page is about." } },
                ["required"] = new JsonArray("pages")
            }
        };

        public static JsonObject FindDefinition() => new JsonObject
        {
            ["name"] = FindName,
            ["description"] = "The lines of every page of your files that hold all the words you give (a name, an id, \"besieged\", \"no order\"), by page: to find where something is before you open it.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["words"] = new JsonObject { ["type"] = "string", ["description"] = "Words or an id; a line must hold all of them." } },
                ["required"] = new JsonArray("words")
            }
        };

        public static JsonObject HistoryDefinition() => new JsonObject
        {
            ["name"] = HistoryName,
            ["description"] = "The old versions of one of your own pages (plan, notes, intelligence) or your chronicler's, newest first, each with when it was replaced and why; give a number for one whole. With no page, which pages have old versions.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["page"] = new JsonObject { ["type"] = "string", ["description"] = "The page, as \"plan/the-plan\"; leave it out for the list of pages with old versions." },
                    ["version"] = new JsonObject { ["type"] = "number", ["description"] = "Optional: the version's number in the list (1 = the newest old one)." }
                }
            }
        };

        private string OpenAnswer(JsonObject args)
        {
            var sw = Stopwatch.StartNew();
            var names = new List<string>();
            if (args["pages"] is JsonArray a) names.AddRange(a.Select(n => n?.ToString()).Where(n => !string.IsNullOrWhiteSpace(n)));
            else if (args["pages"]?.ToString() is string one && one.Length > 0) names.AddRange(one.Split(',').Select(x => x.Trim()));
            else if (args["page"]?.ToString() is string p && p.Length > 0) names.Add(p);
            try { ShelfRefresh?.Invoke(); } catch (Exception e) when (e is IOException || e is JsonException || e is InvalidOperationException) { }
            string text = Shelf.Open(names);
            LogShelf(OpenName, args, text, sw.Elapsed.TotalMilliseconds);
            return text;
        }

        private string FindAnswer(JsonObject args)
        {
            var sw = Stopwatch.StartNew();
            string text = Shelf.Find(args["words"]?.ToString() ?? args["word"]?.ToString() ?? "");
            LogShelf(FindName, args, text, sw.Elapsed.TotalMilliseconds);
            return text;
        }

        private string HistoryAnswer(JsonObject args)
        {
            var sw = Stopwatch.StartNew();
            int version = args["version"] is JsonValue v && v.TryGetValue(out double d) ? (int)d : 0;
            string text = Shelf.History(HistoryMem, args["page"]?.ToString() ?? "", version);
            LogShelf(HistoryName, args, text, sw.Elapsed.TotalMilliseconds);
            return text;
        }

        private void LogShelf(string tool, JsonObject args, string text, double ms)
        {
            AskedShelf++;
            try
            {
                var record = new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), call = _call, poke = _poke, realm = _realm, tool, args = args.ToJsonString(), chars = text?.Length ?? 0, ms = Math.Round(ms, 1) };
                lock (Gate) File.AppendAllText(Path.Combine(_logDir, "routes.jsonl"), JsonSerializer.Serialize(record) + "\n");
            }
            catch (IOException) { }
        }
    }
}
