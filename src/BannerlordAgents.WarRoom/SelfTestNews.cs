using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// A news wake on the war room's side (0.14, docs/PLAN-news.md): the news instructions in place of the same sections; the notes cut to
        /// the enemies of the news; the questions on the book (look_up, list, chronicle), with no check_tool, offered alone without a map; and
        /// a poke.json's news read.
        /// </summary>
        private static void NewsTests(string root)
        {
            string dir = Path.Combine(root, "news-agent");
            string channel = Path.Combine(dir, "warroom");
            Directory.CreateDirectory(Path.Combine(channel, "book"));
            File.WriteAllText(Path.Combine(dir, "warroom-system.md"),
                "# War council\n\n## How you are asked\n\nThe whole report.\n\n## Writing your own tools\n\nThe long manual.\n\n## Checking a tool\n\ncheck_tool.\n\n## Look-ups\n\nRead lookups/<id>.txt.\n\n## Asking the way\n\nask_the_way.\n\n## Rules\n\nOrder only your own lords.\n");
            File.WriteAllText(Path.Combine(dir, "warroom-news.md"),
                "<!-- a note -->\n\n## How you are asked\n\nThe news and what bears on it.\n\n## Writing your own tools\n\nOnly at a council.\n\n## Checking a tool\n\n(removed)\n\n## Look-ups\n\nAsk look_up, list and chronicle.\n");
            var c = new Config { AgentDir = dir, Channel = "warroom" };
            var room = new Room(c, Path.Combine(dir, "logs"));
            string news = File.ReadAllText(room.SystemFile(true, true, true, false));
            string whole = File.ReadAllText(room.SystemFile(true));
            Check(news.Contains("The news and what bears on it.") && news.Contains("Only at a council.") && !news.Contains("The long manual.") && !news.Contains("## Checking a tool")
                  && news.Contains("Ask look_up, list and chronicle.") && !news.Contains("lookups/<id>.txt") && news.Contains("ask_the_way.") && news.Contains("Order only your own lords.")
                  && whole.Contains("The long manual.") && whole.Contains("Read lookups/<id>.txt.") && !whole.Contains("Only at a council."),
                  "a news wake's instructions take warroom-news.md's sections in place of the same ones (tool writing at a council only, the questions for the look-up files); a council's are as they were");
            string bookOnly = File.ReadAllText(room.SystemFile(false, true, true, false));
            Check(!bookOnly.Contains("ask_the_way.") && bookOnly.Contains("Ask look_up") && !bookOnly.Contains("You have no tools"), "with the book questions only, the way-finding section goes and the questions stay");

            string notes = "## Plan\nHold the pass.\n\n## Enemies\n- Aragorn: Strikes from the west.\n- Rohan: Rides in crowds.\n- Galenor: Picks off lone lords.\n\n## Lessons\n- Mass first.";
            string cut = Room.FocusNotes(notes, new List<string> { "Aragorn", "Orc Castle", "Kingdom of Arnor and Gondor", "Rohan" }, out int hidden);
            Check(cut.Contains("Hold the pass.") && cut.Contains("- Aragorn: Strikes from the west.") && cut.Contains("- Rohan: Rides in crowds.") && !cut.Contains("Galenor") && cut.Contains("Mass first.") && hidden == 1,
                  "a news wake's notes: the plan and the lessons whole, the notes on the enemies and realms of the news only (" + hidden + " left out)");

            // the book: as the bridge writes it (snake_case)
            string book = Path.Combine(channel, "book", "poke-12.json");
            File.WriteAllText(book, new JsonObject
            {
                ["poke"] = 12, ["hours"] = 1000.0, ["date"] = "Summer 3, 1085, 10:00", ["realm"] = "orcs",
                ["areas"] = new JsonArray(new JsonObject { ["name"] = "Orc Castle", ["anchor"] = "castle_orc", ["holdings"] = new JsonArray("castle_orc", "village_orc") },
                                          new JsonObject { ["name"] = "Orc Keep", ["anchor"] = "castle_two", ["holdings"] = new JsonArray("castle_two") }),
                ["heads"] = new JsonObject { ["lords"] = "YOUR LORDS AND ARMIES" },
                ["notes"] = new JsonObject { ["lords"] = "\"There in about N hours\" is the game's own reckoning." },
                ["lists"] = new JsonObject
                {
                    ["lords"] = new JsonArray(
                        new JsonObject { ["t"] = "- Bolg [lord_bolg]: 300 men, strength 420; at Orc Castle; holding position.", ["area"] = "Orc Castle", ["ids"] = new JsonArray("lord_bolg") },
                        new JsonObject { ["t"] = "- Ugluk [lord_ugluk]: 400 men, strength 560; 9.0 from Orc Keep; besieging Fornost.", ["area"] = "Orc Keep", ["ids"] = new JsonArray("lord_ugluk") }),
                    ["holdings"] = new JsonArray(new JsonObject { ["t"] = "- Orc Castle [castle_orc] (castle): garrison 450.", ["area"] = "Orc Castle", ["ids"] = new JsonArray("castle_orc") },
                                                 new JsonObject { ["t"] = "- Orc Keep [castle_two] (castle): garrison 200.", ["area"] = "Orc Keep", ["ids"] = new JsonArray("castle_two") })
                },
                ["pages"] = new JsonObject { ["lord_bolg"] = "Bolg [lord_bolg]: 300 men, strength 420. As of Summer 3.\nHours to your holdings, nearest first:\n- Orc Keep [castle_two]: about 9 h\n" }
            }.ToJsonString());
            File.WriteAllText(Path.Combine(channel, "chronicle.jsonl"),
                "{\"hours\":990,\"date\":\"Summer 2, 1085, 24:00\",\"kind\":\"sighting\",\"text\":\"Enemy army in view: Aragorn, 3,000 men.\"}\n"
                + "{\"hours\":995,\"date\":\"Summer 3, 1085, 05:00\",\"kind\":\"order\",\"text\":\"You ordered: Bolg [lord_bolg] defend Orc Castle.\"}\n"
                + "{\"hours\":1500,\"date\":\"Summer 24, 1085, 05:00\",\"kind\":\"order\",\"text\":\"You ordered: Bolg [lord_bolg] goes home.\"}\n");
            var tool = new RouteTool(root, Path.Combine(dir, "logs"), "camp-n", "orcs", 12, "answer")
            {
                BookOnly = true, BookPath = book, NoCheck = true, ChronicleDir = channel, Notes = () => notes
            };
            string Ask(string name, string args) => (string)tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + args + "}}"))?["result"]?["content"]?[0]?["text"];
            JsonArray offered = tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"))?["result"]?["tools"] as JsonArray;
            List<string> names = offered?.Select(t => (string)t?["name"]).ToList() ?? new List<string>();
            Check(names.SequenceEqual(new[] { "look_up", "list", "chronicle" }) && tool.Tools.SequenceEqual(names) && Ask("check_tool", "{}").StartsWith("There is no tool called check_tool"),
                  "without a map the book's questions are offered alone, and a news wake never offers check_tool: " + string.Join(", ", names));
            string bolg = Ask("look_up", "{\"id\": \"[lord_bolg]\"}"), byName = Ask("look_up", "{\"id\": \"Bolg\"}"), none = Ask("look_up", "{\"id\": \"castle_far\"}"), line = Ask("look_up", "{\"id\": \"castle_two\"}");
            Check(bolg.StartsWith("Bolg [lord_bolg]: 300 men") && byName == bolg && none.StartsWith("Your realm has no look-up for \"castle_far\"") && line.Contains("- Orc Keep [castle_two] (castle): garrison 200."),
                  "look_up: a page by id or by name; an id it has no page for, by the list lines that name it; one it does not know, said so");
            string all = Ask("list", "{\"what\": \"lords\"}"), keep = Ask("list", "{\"what\": \"lords\", \"region\": \"Orc Keep\"}"), with = Ask("list", "{\"what\": \"lords\", \"with\": \"besieging\"}");
            string regions = Ask("list", "{\"what\": \"regions\"}"), mine = Ask("list", "{\"what\": \"notes\"}"), byHolding = Ask("list", "{\"what\": \"holdings\", \"region\": \"castle_two\"}");
            Check(all.StartsWith("YOUR LORDS AND ARMIES (2), as of Summer 3") && all.Contains("game's own reckoning") && keep.Contains("Ugluk") && !keep.Contains("Bolg") && with.Contains("Ugluk") && !with.Contains("Bolg")
                  && regions.Contains("- Orc Castle: Orc Castle [castle_orc]") && mine.Contains("- Galenor: Picks off lone lords.") && byHolding.Contains("Orc Keep [castle_two]") && !byHolding.Contains("Orc Castle [castle_orc]"),
                  "list: one kind of thing, all of it, in one region (by its name or one of its holdings), or with some words; the regions; the notes on every enemy");
            string chron = Ask("chronicle", "{\"about\": \"lord_bolg\"}"), aragorn = Ask("chronicle", "{\"about\": \"Aragorn\"}");
            Check(chron.Contains("defend Orc Castle") && !chron.Contains("goes home") && aragorn.Contains("Enemy army in view: Aragorn") && tool.AskedBook == 12,
                  "chronicle: the lines about a lord (by id, and so by name) or a name, up to the report's hour only: " + chron.Replace("\n", " | "));

            // 0.14.1: a section ends at its blank line, whichever way the report's lines end; a word only in the report's history calls no council
            const string Crlf = "POKE #12, Summer 3\r\n\r\nREPORT\r\n\r\nWHY YOU ARE ASKED NOW\r\n- Enemy lord in view: Bolg.\r\n\r\nWHAT CHANGED SINCE YOUR LAST ANSWER\r\n- Nothing new.\r\n\r\n"
                                + "RECENT HISTORY\r\n- Summer 2 · Narsha's army has formed at Laer Loth.\r\n- Summer 2 · Gwigar is besieged by Alenon.\r\n- A wall fell.\r\n";
            Check(Room.Section(Crlf, "WHY YOU ARE ASKED NOW") == "WHY YOU ARE ASKED NOW\n- Enemy lord in view: Bolg."
                  && Room.Section(Crlf.Replace("\r", ""), "WHY YOU ARE ASKED NOW") == "WHY YOU ARE ASKED NOW\n- Enemy lord in view: Bolg."
                  && Room.Section(Crlf, "WHAT CHANGED SINCE YOUR LAST ANSWER") == "WHAT CHANGED SINCE YOUR LAST ANSWER\n- Nothing new."
                  && Room.Section(Crlf, "NO SUCH HEADING") == "",
                  "a report's section ends at its blank line, with its lines ending in \\r\\n or in \\n (urgency and a council's reasons read no further)");
            var council = new Room(new Config { AgentDir = dir, Channel = "warroom", CouncilGapHours = 6 }, Path.Combine(dir, "logs"));
            council.CouncilSat(1000, null);
            var noOrders = new JsonObject { ["orders"] = new JsonArray() };
            council.AfterAnswer(Crlf, noOrders);
            string quiet = council.CouncilDue(1010);
            council.AfterAnswer(Crlf.Replace("Enemy lord in view: Bolg.", "Orc Keep is besieged by Aragorn."), noOrders);
            string due = council.CouncilDue(1010);
            Check(quiet == null && due != null && due.Contains("Orc Keep is besieged by Aragorn"),
                  "a word only in the report's history calls no council; the same word in the news does (" + due + ")");

            // poke.json's news, as the bridge writes it
            File.WriteAllText(Path.Combine(channel, "poke.json"), "{\"number\":12,\"hours\":1000,\"campaign\":\"camp-n\",\"realm\":\"orcs\",\"news\":{\"mode\":\"theatre\",\"recipe\":\"theatre\",\"book\":\"book\\\\poke-12.json\",\"names\":[\"Aragorn\",\"Orc Castle\"]}}");
            PokeMeta meta = room.ReadMetaForTest(12);
            Check(meta != null && meta.Theatre && meta.Focused && meta.Book == "book\\poke-12.json" && meta.Names.Contains("Aragorn"), "poke.json's news is read: theatre mode, the theatre's recipe, its book and its names");
        }
    }
}
