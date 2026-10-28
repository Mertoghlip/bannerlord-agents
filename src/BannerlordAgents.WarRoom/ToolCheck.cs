using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using BannerlordAgents.Tools;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// "check a tool" (0.13, docs/PLAN-0.13.md A1, A4): the checker on a tool the agent is writing, the very one the game runs on every tool
    /// before it runs it (the game module's own code, linked in), each problem with its place in the definition; and, for a named lord, the
    /// try: the bridge works the tool out on the realm's newest hour (his first move, which rule would fire now and where it leads, anything
    /// that comes out none), moving no one. The agent may fix it and ask again within the same call. Each question and answer goes to
    /// routes.jsonl, as the other questions do.
    /// </summary>
    internal sealed partial class RouteTool
    {
        public const string CheckName = "check_tool";

        /// <summary>How long the try may take the bridge (it answers on its urgent lane); past it, the checker's result stands alone.</summary>
        internal static TimeSpan TryWait = TimeSpan.FromSeconds(10);

        /// <summary>The agent's channel folder, through which the try is asked of the bridge; null (a replay): the checker only.</summary>
        public string ChannelDir;

        private static int _tries;

        public static JsonObject CheckDefinition() => new JsonObject
        {
            ["name"] = CheckName,
            ["description"] = "Checks a tool you are writing as the game will before it runs it: its parts, its moves, facts and functions, every state reachable, and the limits on "
                              + "your own tools; each problem says where it is (\"state recruit, rule 2: ...\"). With a lord, it is also tried on your realm's newest hour for him, "
                              + "moving no one: his first move, which rule would fire now and where it leads, and anything that comes out none. Fix it and ask again as often as you need.",
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["definition"] = new JsonObject { ["type"] = "object", ["description"] = "The tool as you would write it in your answer's \"tools\": name, about, takes, settings, init, start, states." },
                    ["lord"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: the id in [brackets] of one of your lords, to try the tool on him now." },
                    ["at"] = new JsonObject { ["type"] = "string", ["description"] = "Optional, with lord: the order's at, a holding's id." },
                    ["target"] = new JsonObject { ["type"] = "string", ["description"] = "Optional, with lord: the order's target, a lord's or party's id." },
                    ["places"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Optional, with lord: the order's places, holdings' ids in order." },
                    ["lords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Optional, with lord: the order's lords, your lords' ids." },
                    ["settings"] = new JsonObject { ["type"] = "object", ["description"] = "Optional, with lord: settings as the order would give them, for example {\"radius\": 20}." }
                },
                ["required"] = new JsonArray("definition")
            }
        };

        /// <summary>One check: the checker's result, then the try when a lord is named and the tool passes.</summary>
        public string CheckTool(JsonObject args)
        {
            var sw = Stopwatch.StartNew();
            string json = ToolJson(args["definition"]);
            if (json == null) return "Give the tool in definition: {\"name\": ..., \"about\": ..., \"takes\": ..., \"settings\": ..., \"init\": ..., \"start\": ..., \"states\": ...}.";
            ToolDef d = ToolDef.FromJson(json, "tool", _realm ?? "agent");
            int rules = d.States.Values.Sum(s => s.When.Count);
            var lines = new List<string>();
            if (d.Valid)
                lines.Add("THE CHECKER: " + d.Name + " passes (" + d.States.Count + " state" + (d.States.Count == 1 ? "" : "s") + ", " + rules + " rule" + (rules == 1 ? "" : "s")
                          + "; the limits on a tool of yours: " + ToolDef.MaxStates + " states, " + ToolDef.MaxRules + " rules, " + ToolDef.MaxSettings + " settings, " + ToolDef.MaxExpression + " characters an expression).");
            else
            {
                lines.Add("THE CHECKER: " + (string.IsNullOrEmpty(d.Name) ? "the tool" : d.Name) + " has " + d.Problems.Count + " problem" + (d.Problems.Count == 1 ? "" : "s") + ", so it would be refused:");
                foreach (string p in d.Problems) lines.Add("- " + p);
            }
            string lord = Str(args["lord"]);
            string tried = null;
            if (lord != null) tried = d.Valid ? AskTry(json, d.Name, args) : "THE TRY was not made: fix the checker's problems first.";
            if (tried != null) lines.Add(tried);
            else if (d.Valid) lines.Add("(Name one of your lords in lord to try it on your realm's newest hour.)");
            string text = string.Join("\n", lines);
            Log(new
            {
                at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), campaign = _campaign, realm = _realm, poke = _poke, call = _call, tool = CheckName,
                name = d.Name, valid = d.Valid, problems = d.Problems, lord, tried = tried != null, definition = json, answer = text, ms = Math.Round(sw.Elapsed.TotalMilliseconds)
            });
            return text;
        }

        /// <summary>The definition as the checker reads it: given whole, or as an answer's tool ({"name", "keep", "about", "definition": {...}}).</summary>
        internal static string ToolJson(JsonNode n)
        {
            JsonObject o = n as JsonObject;
            if (o == null && n is JsonValue v && v.TryGetValue(out string s))
            {
                try { o = JsonNode.Parse(s) as JsonObject; }
                catch (System.Text.Json.JsonException) { return s; }   // the checker says what is wrong with it
            }
            if (o == null) return null;
            if (o["definition"] is JsonObject inner)
            {
                var merged = (JsonObject)inner.DeepClone();
                if (merged["name"] == null && o["name"] != null) merged["name"] = o["name"].DeepClone();
                if (merged["about"] == null && o["about"] != null) merged["about"] = o["about"].DeepClone();
                o = merged;
            }
            else o = (JsonObject)o.DeepClone();
            o.Remove("keep");
            o.Remove("retire");
            return o.ToJsonString();
        }

        /// <summary>
        /// The try, asked of the bridge (it alone has the realm's newest hour): orders\toolcheck-request-(id).json, answered as
        /// toolcheck-(id).txt in the channel folder within TryWait, else the checker's result stands alone.
        /// </summary>
        private string AskTry(string json, string name, JsonObject args)
        {
            if (ChannelDir == null) return "THE TRY is not available in this call; only the checker's result stands.";
            string orders = Path.Combine(ChannelDir, "orders");
            Directory.CreateDirectory(orders);
            string id = DateTime.UtcNow.ToString("HHmmssfff", CultureInfo.InvariantCulture) + "-" + Interlocked.Increment(ref _tries).ToString(CultureInfo.InvariantCulture);
            var req = new JsonObject { ["id"] = id, ["name"] = name, ["definition"] = json, ["realm"] = _realm, ["poke"] = _poke, ["call"] = _call };
            foreach (string k in new[] { "lord", "at", "target", "places", "lords", "settings" })
                if (args[k] != null) req[k] = args[k].DeepClone();
            string request = Path.Combine(orders, "toolcheck-request-" + id + ".json"), answer = Path.Combine(ChannelDir, "toolcheck-" + id + ".txt");
            string tmp = Path.Combine(ChannelDir, "toolcheck-request-" + id + ".json.part");
            File.WriteAllText(tmp, req.ToJsonString());
            File.Move(tmp, request);
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TryWait)
            {
                string text = File.Exists(answer) ? Room.ReadShared(answer) : null;
                if (text != null)
                {
                    try { File.Delete(answer); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return text.Trim();
                }
                Thread.Sleep(100);
            }
            try { File.Delete(request); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return "THE TRY was not available: the game did not answer within " + (int)TryWait.TotalSeconds + " s (it may be loading, or stopped in a menu); only the checker's result stands.";
        }
    }
}
