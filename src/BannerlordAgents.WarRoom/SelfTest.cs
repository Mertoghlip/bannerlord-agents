using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// "WarRoom selftest": the realm's memory checked in a scratch folder, without calling Claude (docs/MEMORY.md). The old
    /// journal becoming a campaign's notes, the notes an answer changes, a reload cutting the memory back, which chronicle
    /// lines a message carries, both memories' messages and instructions, when the story is due, and the replay's view of
    /// which recorded fights the bridge now keeps quiet. Prints ALL CHECKS PASSED, or what failed.
    /// </summary>
    internal static class SelfTest
    {
        private static int _failures;

        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "warroom-selftest");
            try { Directory.Delete(root, true); } catch (DirectoryNotFoundException) { }
            string channel = Path.Combine(root, "warroom");
            Directory.CreateDirectory(channel);
            File.WriteAllText(Path.Combine(root, "warroom-system.md"),
                "# War council\n\n## How you are asked\n\nPokes.\n\n## Your memory\n\nThe story so far and your notes.\n\n## Look-ups\n\nRead lookups/<id>.txt and chronicle.txt.\n\n## Rules\n\nOrder only your own lords.\n");
            File.WriteAllText(Path.Combine(root, "journal.md"),
                "# Journal\n\n## Standing plan\nHold Western Moria.\n\n## Lords and their tasks\n- Shelza: inside Western Moria.\n\n## Lessons\n- Never fight Aragorn alone.\n");
            var c = new Config { AgentDir = root, Channel = "warroom", StoryEvery = 12, MaxLessons = 3 };
            var said = new List<string>();

            // the old journal becomes the first campaign's notes: the plan and the lessons, not the lords' tasks
            Memory m = Memory.Open(c, "camp1", said.Add);
            Check(m.Notes.Contains("## Plan\nHold Western Moria.") && m.Notes.Contains("- Never fight Aragorn alone.") && !m.Notes.Contains("Shelza"),
                  "the old journal's plan and lessons become the notes; the lords' tasks do not (the orders in force say them)");
            Check(!File.Exists(c.Journal) && Directory.GetFiles(root, "journal-before-memory-*.md").Length == 1, "the old journal is kept under a new name, so it is never read twice");
            Check(Memory.Open(c, "camp2", said.Add).Notes.Contains("(not set yet)"), "another campaign starts with empty notes");

            // an answer changes the notes: the plan replaced, an enemy noted by name, lessons added
            var answer = (JsonObject)JsonNode.Parse("{\"plan\": \"Relieve Western Moria.\", \"enemies\": {\"Aragorn\": \"Strikes from the west with 5,000.\"}, \"lessons\": [\"Mass before striking.\"]}");
            string changed = m.Apply(answer);
            Check(changed == "plan, 1 enemy, 1 lesson" && m.Notes.Contains("- Aragorn: Strikes from the west with 5,000.") && m.Notes.Contains("Relieve Western Moria."),
                  "an answer's plan, enemies and lessons go into the notes (" + changed + ")");
            m.Apply((JsonObject)JsonNode.Parse("{\"enemies\": {\"aragorn\": \"Retreated west.\", \"Galenor\": \"Picks off lone lords.\"}}"));
            Check(m.Notes.Contains("- Aragorn: Retreated west.") && m.Notes.Contains("- Galenor: Picks off lone lords.") && !m.Notes.Contains("5,000"),
                  "a note on an enemy replaces the old one on that name");
            m.Apply((JsonObject)JsonNode.Parse("{\"enemies\": {\"Galenor\": \"\"}, \"lessons\": [\"Mass before striking.\"]}"));
            Check(!m.Notes.Contains("Galenor") && m.Lessons().Count == 2, "an empty note removes an enemy, and a lesson already kept is not added twice");
            Check(m.Apply((JsonObject)JsonNode.Parse("{\"orders\": []}")) == "", "an answer without plan, enemies or lessons leaves the notes as they are");

            // a reload of an earlier save cuts the memory back to the last snapshot from no later than its date
            m.LastHours = 100;
            m.Save();
            m.Snapshot(100, "Summer 5", 10, "answer");
            string notesAt100 = m.Notes;
            m.Apply((JsonObject)JsonNode.Parse("{\"plan\": \"Besiege Hollin Gate.\"}"));
            m.View(out _, out _, out _, out _, out int gen);
            Check(m.SetStory("The war began.", 150, "Summer 7", gen), "a story is kept when nothing was cut meanwhile");
            m.LastHours = 200;
            m.Snapshot(200, "Summer 9", 20, "answer");
            string back = m.CutBack(120);
            Check(m.Notes == notesAt100 && m.Story == "" && back == "Summer 5" && m.LastHours == 120, "an earlier save cuts the notes and the story back to that date (" + back + ")");
            Check(!m.SetStory("A story from lines now cut.", 150, "Summer 7", gen), "a story begun before the cut is thrown away");
            Check(File.ReadAllLines(Path.Combine(m.Dir, "snapshots.jsonl")).Length == 2, "the snapshots after the cut are dropped (the seed and the one at 100 remain)");
            m.CutBack(10);
            Check(m.Notes.Contains("Hold Western Moria."), "cut back before any answer, the notes are the seed again");
            // since 0.9.1 each copy is labelled with the game session that wrote it and none is dropped: any save, earlier, later or
            // on another branch of play, gets its own memory back from the history the bridge found for it
            Memory hm = Memory.Open(c, "camp-branches", said.Add, double.NaN);
            hm.LoadId = "A";
            hm.Apply((JsonObject)JsonNode.Parse("{\"plan\": \"Plan of A at 110.\"}"));
            hm.Snapshot(110, "A 110", 1, "answer");
            hm.Apply((JsonObject)JsonNode.Parse("{\"plan\": \"Plan of A at 130.\"}"));
            hm.Snapshot(130, "A 130", 2, "answer");
            hm.LoadId = "B";
            hm.Apply((JsonObject)JsonNode.Parse("{\"plan\": \"Plan of B at 105.\"}"));
            hm.Snapshot(105, "B 105", 3, "answer");
            var KV = new Func<string, double, KeyValuePair<string, double>>((l, u) => new KeyValuePair<string, double>(l, u));
            string toA = hm.Restore(new List<KeyValuePair<string, double>> { KV(null, 100), KV("A", 120) }, 120);
            Check(hm.Notes.Contains("Plan of A at 110.") && !hm.Notes.Contains("at 130") && !hm.Notes.Contains("of B") && toA == "A 110",
                  "a save made in session A at 120 gets A's memory as it was then, not B's, though B came later (" + toA + ")");
            hm.Restore(new List<KeyValuePair<string, double>> { KV(null, 100), KV("B", 140) }, 140);
            Check(hm.Notes.Contains("Plan of B at 105.") && !hm.Notes.Contains("of A"), "a save of session B gets B's");
            hm.Restore(new List<KeyValuePair<string, double>> { KV(null, 100) }, 100);
            Check(!hm.Notes.Contains("Plan of"), "the save both began from gets the memory from before either");
            Check(File.ReadAllLines(Path.Combine(hm.Dir, "snapshots.jsonl")).Length >= 4, "and no copy is dropped on the way");
            Check(Memory.InHistory(new List<KeyValuePair<string, double>> { KV(null, 100) }, null, 90) && !Memory.InHistory(new List<KeyValuePair<string, double>> { KV(null, 100) }, "A", 90),
                  "a copy from before the labels counts only for the history from before them");

            // the old journal belonged to another run of the game: a save from before the memory began starts afresh
            File.WriteAllText(c.Journal, "# Journal\n\n## Standing plan\nRelieve Western Moria, besieged since Summer 17.\n\n## Lessons\n- A lesson from Summer 17.\n");
            Memory late = Memory.Open(c, "camp4", said.Add, 500);
            Check(late.Notes.Contains("besieged since Summer 17"), "the old journal becomes the notes at the first poke");
            late.CutBack(400);
            Check(late.Notes.Contains("(not set yet)") && !late.Notes.Contains("Summer 17"), "a save from before that poke starts without them");

            // the chronicle lines a message carries: after the story, and not the ones in the report's RECENT HISTORY
            var lines = new List<string>();
            for (int h = 1; h <= 100; h++)
                lines.Add(new JsonObject { ["hours"] = 1000 + h * 2, ["date"] = "Day " + h, ["kind"] = "battle", ["text"] = "Line " + h }.ToJsonString());
            File.WriteAllLines(Path.Combine(channel, "chronicle.jsonl"), lines);
            var room = new Room(c, Path.Combine(root, "logs"));
            room.EnsureMemory(new PokeMeta { Number = 5, Hours = 1200, Campaign = "camp3", LoadId = "load1", LoadedAt = 1000 });
            var meta = new PokeMeta { Number = 5, Hours = 1200, Date = "Day 100", Campaign = "camp3" };
            List<ChronicleLine> gap = room.Gap(1100, meta);
            // after the story (hours 1100): lines 51..100; the last 25 within 48 hours (lines 76..100) are in the report
            Check(gap.Count == 25 && gap[0].Text == "Line 51" && gap[gap.Count - 1].Text == "Line 75", "the message carries the lines after the story that the report's recent history does not (" + gap.Count + ")");
            Check(room.Gap(double.NaN, meta).Count == 75, "with no story yet, every line older than the recent history");

            string msg = room.Message("POKE #5, Day 100\nWHY YOU ARE ASKED NOW\n- test", meta, false, true, out int gapLines, out _);
            Check(msg.StartsWith("THE STORY SO FAR") && msg.Contains("YOUR NOTES") && msg.Contains("YOUR CHRONICLE SINCE THE STORY") && msg.Contains("- Day 1 · Line 1")
                  && msg.IndexOf("POKE #5", StringComparison.Ordinal) > msg.IndexOf("YOUR NOTES", StringComparison.Ordinal),
                  "a written-memory message: the story so far, the notes, the chronicle not in the story, then the poke");
            Check(room.DueStory(1200)?.Count == 100, "a story is due once enough new lines have come");
            var tidy = new List<string> { new JsonObject { ["hours"] = 1199, ["date"] = "Day 100", ["kind"] = "war", ["text"] = "One line" }.ToJsonString() };
            File.WriteAllLines(Path.Combine(channel, "chronicle.jsonl"), tidy);
            Check(room.DueStory(1200) == null && room.DueStory(1240) != null, "a single new line waits, until it is a day and a half old");

            // the one-long-conversation memory: notes in its first message only; its own memory section in the instructions
            var cc = new Config { AgentDir = root, Channel = "warroom", MemoryMode = "conversation" };
            var conv = new Room(cc, Path.Combine(root, "logs"));
            conv.EnsureMemory(meta);
            string first = conv.Message("POKE #5", meta, true, true, out _, out _), next = conv.Message("POKE #6", meta, true, false, out _, out _);
            Check(first.Contains("YOUR NOTES") && !first.Contains("THE STORY SO FAR") && !next.Contains("YOUR NOTES") && next.Contains("POKE #6"),
                  "a conversation's first message brings the notes; the later ones only the poke");
            string sysConv = File.ReadAllText(conv.SystemFile()), sysWritten = File.ReadAllText(room.SystemFile());
            Check(sysConv.Contains("Your memory is this conversation") && !sysConv.Contains("The story so far and your notes.") && sysWritten.Contains("The story so far and your notes."),
                  "each memory's instructions say how that memory works");
            var noRead = new Config { AgentDir = root, Channel = "warroom", Lookups = false };
            string sysNoRead = File.ReadAllText(new Room(noRead, Path.Combine(root, "logs")).SystemFile());
            Check(!sysNoRead.Contains("chronicle.txt") && sysNoRead.Contains("You have no tools in this call") && sysNoRead.Contains("## Rules"),
                  "without reading, the look-ups and the chronicle file are not promised");

            // an answer's notes come through the answer's checks
            string problem = Room.Extract("{\"poke\": 5, \"orders\": [], \"enemies\": {\"Aragorn\": \"x\"}}", 5, out JsonObject ans);
            Check(problem == null && ans?["enemies"] is JsonObject, "an answer with only notes and no orders is read");

            // the council of war: when it sits, what its message holds, and the fast answers' orders winning over its older view
            var cw = new Config { AgentDir = root, Channel = "warroom", CouncilGapHours = 6, CouncilEveryHours = 24 };
            var council = new Room(cw, Path.Combine(root, "logs"));
            Check(council.CouncilDue(1000) == "no council has sat yet in this run of the war room", "a council sits once in each run of the war room, even with no event");
            council.CouncilSat(1000, null);
            Check(council.CouncilDue(1003) == null && council.NextCouncilHours == 1024, "after a council, none sits until its time (" + council.NextCouncilHours + ")");
            council.AfterAnswer("POKE #7\nWHY YOU ARE ASKED NOW\n- Western Moria is besieged by Aragorn (Kingdom of Arnor and Gondor), 5,532 men.\n\nREPORT", new JsonObject { ["orders"] = new JsonArray() });
            Check(council.CouncilDue(1003) == null && council.CouncilDue(1006)?.StartsWith("Western Moria is besieged by Aragorn", StringComparison.Ordinal) == true,
                  "a big event calls a council, but not within council_gap_hours of the last one");
            council.AfterAnswer("POKE #8\nWHY YOU ARE ASKED NOW\n- First look since the campaign was loaded.\n\nREPORT", new JsonObject { ["orders"] = new JsonArray() });
            Check(council.CouncilDue(1001) == "First look since the campaign was loaded.", "the first look calls a council at once, gap or not");
            council.CouncilSat(1001, 10);
            Check(council.CouncilDue(1010) == null && council.CouncilDue(1011) == "10 hours have passed since the last council", "a council may set when the next one sits (council_in)");
            council.CouncilSitting(true);
            council.AfterAnswer("POKE #9\nWHY YOU ARE ASKED NOW\n- test\n\nREPORT", (JsonObject)JsonNode.Parse("{\"orders\": [{\"lord\": \"lord_A\", \"do\": \"defend\"}, {\"lord\": \"lord_B\", \"do\": \"gather\", \"lords\": [\"lord_C\"]}]}"));
            Check(council.OrderedMeanwhile.SetEquals(new[] { "lord_A", "lord_B", "lord_C" }) && council.CouncilDue(2000) == null, "while a council sits, the lords a fast answer orders are noted, and no second council sits");
            council.CouncilSitting(false);
            // 0.12.4: an order given the moment before a council sits counts too (its briefing may not have it yet): Aragorn's sale at Mount Gundabad
            council.AfterAnswer("POKE #10\nWHY YOU ARE ASKED NOW\n- test\n\nREPORT", (JsonObject)JsonNode.Parse("{\"orders\": [{\"lord\": \"named_companion_aragorn\", \"do\": \"sell\", \"at\": \"town_G1\"}]}"));
            council.CouncilSitting(true);
            Check(council.OrderedMeanwhile.Contains("named_companion_aragorn"), "an order given the minute before a council sits stands against the council's older view (Aragorn sent back from Mount Gundabad)");
            council.CouncilSitting(false);
            var councilOrders = (JsonArray)JsonNode.Parse("[{\"lord\": \"lord_A\", \"do\": \"join\", \"target\": \"lord_X\"}, {\"lord\": \"lord_X\", \"do\": \"gather\", \"at\": \"town_1\", \"lords\": [\"lord_C\", \"lord_D\"]}, {\"lord\": \"lord_E\", \"do\": \"gather\", \"at\": \"town_2\", \"lords\": [\"lord_C\"]}]");
            JsonArray keptOrders = Room.DropOrdered(councilOrders, new HashSet<string>(new[] { "lord_A", "lord_C" }, StringComparer.OrdinalIgnoreCase), out List<string> droppedOrders);
            Check(keptOrders.Count == 1 && (string)keptOrders[0]["lord"] == "lord_X" && ((JsonArray)keptOrders[0]["lords"]).Count == 1 && droppedOrders.Count == 4,
                  "the council's orders for lords ordered meanwhile do not go out, and those lords leave its gathers (" + string.Join("; ", droppedOrders) + ")");
            string ct = Room.CouncilText("First look since the campaign was loaded.", new PokeMeta { Number = 8, Date = "Summer 9, 1087, 12:00" }, 8, "COUNCIL BRIEFING FOR X\n\nYOUR PURSE", "SITUATION AT Summer 9\nREPORT FOR X");
            Check(ct.StartsWith("COUNCIL OF WAR, Summer 9, 1087, 12:00") && ct.Contains("\"poke\": 8") && ct.IndexOf("YOUR PURSE", StringComparison.Ordinal) < ct.IndexOf("REPORT FOR X", StringComparison.Ordinal),
                  "the council's message: why it sits, then the whole-realm briefing, then the situation");
            council.EnsureMemory(meta);
            Check(council.Message("POKE #10", meta, false, true, out _, out _, "YOUR COUNCIL OF WAR gave 3 orders; 1 did not go out.").Contains("YOUR COUNCIL OF WAR gave 3 orders"),
                  "what became of the council's orders goes into the next fast message");
            cw.Save();
            Config cl = Config.Load(root);
            Check(cl.Council && cl.CouncilGapHours == 6 && cl.CouncilAfter.Contains("is besieged by") && File.ReadAllText(cw.FilePath).Contains("council_thinking_cap = 12000"),
                  "the council's settings are written and read back");

            RouteToolTests(root, c);
            LieutenantTests(root);
            BriefingTests(c, Path.Combine(root, "logs"));
            RecordTests(root);
            ToolCheckTests(root);

            // the replay's view of what the bridge now keeps quiet
            Check(Program.Quiet(T("battle", "Battle: Bolg (Misty Mountain Orcs) against Looters (Looters); Bolg won.")), "replay: a fight with bandits is kept quiet");
            Check(Program.Quiet(T("battle", "Battle: Galenor (Kingdom of Arnor and Gondor) against Villagers of Celebloth (Misty Mountain Orcs); Galenor won.")), "replay: a fight with villagers is kept quiet");
            Check(!Program.Quiet(T("battle", "Battle: Galenor (Kingdom of Arnor and Gondor) against Grakmaw's Party (Misty Mountain Orcs); Galenor won.")), "replay: a battle between lords is history");
            Check(!Program.Quiet(T("enemy_in_view", "Enemy army in view: Aragorn (Kingdom of Arnor and Gondor), 5,532 men")) && Program.Quiet(T("enemy_in_view", "Enemy lord in view: Haldir")),
                  "replay: an enemy army sighted is history, a lord passing by is not");
            Check(Program.When(2192097.4) == "Summer 9, 1087, 09:00", "replay: the game's date from in-game hours (" + Program.When(2192097.4) + ")");

            Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : "\n" + _failures + " CHECK(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Version 0.7, the "ask the way" tool on the made-up map (a river with one bridge): the protocol, a lord's way over the
        /// bridge with how marching works told once, an enemy from where it was seen with its trail's pace and who gets there
        /// first, a party the realm never saw refused, every question logged, and its section of the instructions only when offered.
        /// </summary>
        private static void RouteToolTests(string root, Config c)
        {
            string data = Path.Combine(root, "agents-data");
            Routes.WalkMap m = Routes.Samples.SmallMap();
            string mapDir = Path.Combine(data, "map", "test");
            Directory.CreateDirectory(mapDir);
            File.WriteAllBytes(Path.Combine(mapDir, "ground.bin"), m.Ground);
            Routes.JsonText.Write(Path.Combine(mapDir, "places.json"), m.Places);
            Routes.JsonText.Write(Path.Combine(mapDir, "meta.json"), m.Meta);
            m.Dir = mapDir;
            Routes.JsonText.Write(Routes.Atlas.PathIn(mapDir), Routes.Atlas.Build(m));
            string live = Path.Combine(data, "live");
            Directory.CreateDirectory(Path.Combine(live, "camp5"));
            File.WriteAllText(Path.Combine(live, "current.json"), "{\"calendar\":{\"hours_in_day\":24,\"days_in_season\":21,\"seasons_in_year\":4,\"season_names\":[\"Spring\",\"Summer\",\"Autumn\",\"Winter\"]}}");
            File.WriteAllText(Path.Combine(live, "camp5", "view_orcs.json"),
                "{\"hours\":1000,\"map\":\"test\",\"own\":[{\"id\":\"p_bolg\",\"lord\":\"lord_bolg\",\"name\":\"Bolg\",\"x\":20,\"y\":29,\"men\":300,\"strength\":400,\"morale\":60,\"tactics\":50,\"pace\":4,\"doing\":\"holding position\"}],"
                + "\"seen\":[{\"id\":\"p_ar\",\"lord\":\"lord_ar\",\"name\":\"Aragorn\",\"relation\":\"enemy\",\"faction_name\":\"Arnor\",\"men\":900,\"army_men\":5000,\"strength\":12000,\"x\":80,\"y\":29,\"doing\":\"going to Edoras\",\"toward\":\"castle_e\",\"seen_hours\":1000},"
                + "{\"id\":\"p_sc\",\"lord\":\"lord_sc\",\"name\":\"Hirgon\",\"relation\":\"enemy\",\"faction_name\":\"Arnor\",\"men\":30,\"strength\":40,\"x\":30,\"y\":29,\"doing\":\"holding position\",\"seen_hours\":1000}],"
                + "\"takings\":{\"battles\":4,\"fallen\":400,\"loot\":8000,\"prisoners\":160,\"ransom\":3200,\"loot_per_fallen\":20,\"prisoners_per_fallen\":0.4,\"ransom_per_prisoner\":20},\"takings_enough\":3,"
                + "\"last_seen\":[],\"trails\":[{\"id\":\"p_ar\",\"points\":[{\"hours\":990,\"x\":90,\"y\":29},{\"hours\":1000,\"x\":80,\"y\":29}]}],\"holdings\":[],"
                + "\"frontier\":[{\"id\":\"castle_e\",\"name\":\"Edoras\",\"kind\":\"castle\",\"garrison\":100,\"militia\":50,\"defence\":260,\"wall_level\":1}],"
                + "\"enemy_holdings\":{\"Arnor\":[\"town_b\",\"castle_d\"]}}");
            string logs = Path.Combine(root, "logs");
            var tool = new RouteTool(data, logs, "camp5", "orcs", 12, "answer");
            Check(tool.Ready(out string why), "the tool is offered once the map and the realm's view are there (" + (why ?? "ready") + ")");
            JsonNode init = tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}"));
            JsonNode list = tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));
            Check(init?["result"]?["capabilities"]?["tools"] != null && (string)list?["result"]?["tools"]?[0]?["name"] == "ask_the_way"
                  && tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}")) == null,
                  "the tool answers Claude Code's handshake and lists itself; a notification gets no answer");
            string Call(string args) => (string)tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"ask_the_way\",\"arguments\":" + args + "}}"))?["result"]?["content"]?[0]?["text"];
            string a1 = Call("{\"from\":\"lord_bolg\",\"to\":\"town_b\"}");
            Check(a1 != null && a1.StartsWith("THE WAY FROM Bolg [lord_bolg] TO Bree [town_b]") && a1.Contains("bridge") && a1.Contains("no other way") && a1.Contains(", so about ") && a1.Contains("Spots on the way") && System.Text.RegularExpressions.Regex.IsMatch(a1, @"the bridge [^[]*[pd+_d+]")
                  && a1.Contains("HOW MARCHING WORKS"), "a lord's way over the bridge, its time at its own pace, the bridge the only way, and how marching works: " + a1?.Replace("\n", " | "));
            string a2 = Call("{\"from\":\"p_ar\",\"to\":\"Edoras\",\"also_from\":[\"p_bolg\"]}");
            Check(a2 != null && a2.Contains("pace its trail shows") && a2.Contains("WHO GETS THERE FIRST") && !a2.Contains("HOW MARCHING WORKS"),
                  "an enemy from where it was seen, at the pace its trail shows, against one of the realm's lords; the rules told once a call: " + a2?.Replace("\n", " | "));
            string a3 = Call("{\"from\":\"p_hidden\",\"to\":\"town_b\"}");
            Check(a3 != null && a3.Contains("knows no lord, army or holding called \"p_hidden\""), "a party the realm never saw is refused: " + a3);
            string[] logged = File.Exists(Path.Combine(logs, "routes.jsonl")) ? File.ReadAllLines(Path.Combine(logs, "routes.jsonl")) : new string[0];
            Check(logged.Length == 3 && logged[0].Contains("\"poke\":12") && logged[0].Contains("\"from_id\":\"p_bolg\"") && logged[2].Contains("\"error\""), "every question is logged with its poke (" + logged.Length + ")");
            // 0.7.4: the battle odds and the roads in, served beside the way
            string Ask(string name, string args) => (string)tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + args + "}}"))?["result"]?["content"]?[0]?["text"];
            Check((list?["result"]?["tools"] as JsonArray)?.Count == 6 && (string)list?["result"]?["tools"]?[1]?["name"] == "battle_odds" && (string)list?["result"]?["tools"]?[2]?["name"] == "roads_in"
                  && (string)list?["result"]?["tools"]?[3]?["name"] == "can_catch" && (string)list?["result"]?["tools"]?[4]?["name"] == "engagement" && (string)list?["result"]?["tools"]?[5]?["name"] == "check_tool",
                  "the server lists the battle odds, the roads in, the chase, the engagement and (0.13) checking a tool beside the way");
            // 0.12.3: the engagement: when Bolg gets to Hirgon, the fight, and Aragorn's army (50 off at the pace its trail shows) too late to join it
            string e1 = Ask("engagement", "{\"ours\":[\"lord_bolg\"],\"target\":\"lord_sc\"}");
            Check(e1 != null && e1.StartsWith("ENGAGEMENT: Bolg [lord_bolg] against Hirgon [lord_sc]") && System.Text.RegularExpressions.Regex.IsMatch(e1, @"Bolg in about \d+ hours")
                  && e1.Contains("Against Hirgon alone: you win") && System.Text.RegularExpressions.Regex.IsMatch(e1, @"Aragorn \[lord_ar\][^;]*too late") && e1.Contains("None of them gets there before the fight ends")
                  && e1.Contains("give that order join_hours"),
                  "the engagement: when Bolg reaches Hirgon, the fight by the game's rules, Aragorn's army too far to join, and the join_hours for the orders: " + e1?.Replace("\n", " | "));
            string e2 = Ask("engagement", "{\"ours\":[\"lord_bolg\"],\"target\":\"lord_ar\"}");
            Check(e2 != null && e2.Contains("It is marching somewhere") && e2.Contains("Against Aragorn alone: they win"), "against an army on the march: it says the fight may come later, and Bolg loses: " + e2?.Replace("\n", " | "));
            string e3 = Ask("engagement", "{\"ours\":[\"lord_bolg\"],\"target\":\"lord_nobody\"}");
            Check(e3 != null && e3.StartsWith("Name an enemy lord or army in view in target"), "an enemy the realm never saw is refused: " + e3);
            // 0.9.3: the chase question before a hunt (Bolg at his own pace against Aragorn's army at the pace its trail shows)
            string k1 = Ask("can_catch", "{\"hunter\":\"lord_bolg\",\"quarry\":\"lord_ar\"}");
            Check(k1 != null && k1.StartsWith("CAN Bolg [lord_bolg] CATCH Aragorn [lord_ar]?") && k1.Contains("Bolg is the faster") && k1.Contains("caught in about") && k1.Contains("It is marching to Edoras [castle_e]"),
                  "the chase: each side's pace, the way between them, how long to run it down, and who gets first to where it is marching: " + k1?.Replace("\n", " | "));
            string k2 = Ask("can_catch", "{\"hunter\":\"lord_ar\",\"quarry\":\"lord_bolg\"}");
            Check(k2 != null && k2.StartsWith("Name one of your own lords or armies in hunter"), "the chase is asked only for the realm's own hunters: " + k2);
            string o1 = Ask("battle_odds", "{\"ours\":[\"lord_bolg\"],\"theirs\":[\"lord_ar\"]}");
            Check(o1 != null && o1.StartsWith("BATTLE ODDS: Bolg [lord_bolg] against Aragorn [lord_ar], in the open") && o1.Contains("they win") && o1.Contains("two rounds an hour"),
                  "a field battle by the game's rules: Bolg's 300 against Aragorn's army of 5,000 loses: " + o1?.Replace("\n", " | "));
            // 0.9.4: a win's takings, by the realm's own won battles on record (in its view file)
            string o3 = Ask("battle_odds", "{\"ours\":[\"lord_bolg\"],\"theirs\":[\"lord_sc\"]}");
            Check(o3 != null && o3.Contains("you win") && o3.Contains("What a win would bring in, by your realm's 4 won battles on record: about 30 of theirs fall, leaving about 600 in loot and 12 prisoners worth about 240 in ransom"),
                  "a field battle won says what the win would bring in, by the realm's own battles: " + o3?.Replace("\n", " | "));
            // 0.11.6: the run's end says what each side has left of what it brought, the loser too
            Check(o3 != null && System.Text.RegularExpressions.Regex.IsMatch(o3, @"you win in about [^;]*, with about [\d,]+ of your [\d,]+ still standing; (they (break|retreat) with about [\d,]+ of their [\d,]+|all [\d,]+ of their men are down)")
                  && o1 != null && System.Text.RegularExpressions.Regex.IsMatch(o1, @"they win in about [^;]*, with about [\d,]+ of their [\d,]+ still standing; (you (break|retreat) with about [\d,]+ of your [\d,]+|all [\d,]+ of your men are down)"),
                  "the run ahead says what the winner and the loser each have left of what they brought: " + o3?.Replace("\n", " | "));
            string o2 = Ask("battle_odds", "{\"ours\":[\"lord_bolg\"],\"at\":\"castle_e\"}");
            Check(o2 != null && o2.StartsWith("BATTLE ODDS: Bolg [lord_bolg] storming Edoras [castle_e]") && System.Text.RegularExpressions.Regex.IsMatch(o2, @"preparations, about \d+ hours for 300 men")
                  && o2.Contains("grow only with the defenders' number") && o2.Contains("Run ahead:"),
                  "a storm by the game's rules, with the camp's preparation time first: " + o2?.Replace("\n", " | "));
            string r1 = Ask("roads_in", "{\"to\":\"town_a\",\"hours_out\":3}");
            Check(r1 != null && r1.StartsWith("THE ROADS INTO Aldburg [town_a]") && r1.Contains("Bree (Arnor") && System.Text.RegularExpressions.Regex.IsMatch(r1, @"\[p\d+_\d+\]") && r1.Contains("hours before it reaches the walls"),
                  "the roads into a holding from the enemy's places, with a watch spot and its warning: " + r1?.Replace("\n", " | "));
            File.AppendAllText(c.SystemTemplate, "\n## Asking the way\n\nAsk with ask_the_way.\n");
            var room = new Room(c, logs);
            string with = File.ReadAllText(room.SystemFile(true)), without = File.ReadAllText(room.SystemFile(false));
            Check(with.Contains("ask_the_way") && !without.Contains("ask_the_way") && without.Contains("## Rules"), "the instructions speak of the tool only in a call that has it");
            RouteTool.WriteRules(data, c.ChannelDir);
            Check(File.Exists(Path.Combine(c.ChannelDir, "marching.txt")) && File.ReadAllText(Path.Combine(c.ChannelDir, "marching.txt")).StartsWith("HOW MARCHING WORKS"), "marching.txt is written for the agent to read");
        }

        /// <summary>
        /// Version 0.10, the lieutenant's war room without a call: its kind read from the folder's agent.txt, its message, its
        /// answer read and written for the bridge (with the waiting proposal's number from chat.json, never the model's).
        /// </summary>
        private static void LieutenantTests(string root)
        {
            string dir = Path.Combine(root, "lieutenant");
            string channel = Path.Combine(dir, "warroom");
            Directory.CreateDirectory(channel);
            File.WriteAllText(Path.Combine(dir, "agent.txt"), "realm = player\nmode = live\nlords = all\nchannel = warroom\nkind = lieutenant\n");
            Check(Config.Load(dir).IsLieutenant, "a folder whose agent.txt says kind = lieutenant gets the lieutenant's war room");
            var chat = (JsonObject)JsonNode.Parse("{\"number\":7,\"said\":\"yes, send them\",\"earlier\":[\"hold on\"],\"notes\":[\"The ruler declined your proposal of message #5 with the button; nothing was given.\"],\"date\":\"Summer 3, 1087, 14:00\",\"campaign\":\"camp5\",\"realm\":\"new_kingdom\","
                                                  + "\"pending\":{\"chat\":6,\"say\":\"Send Dain and Nori to Goblin Town?\",\"orders\":[{\"lord\":\"lord_dain\",\"do\":\"defend\",\"at\":\"town_gt\"}]}}");
            var history = new List<JsonObject> { (JsonObject)JsonNode.Parse("{\"date\":\"Summer 3, 1087, 13:00\",\"said\":\"hold Goblin Town\",\"say\":\"Send Dain and Nori to Goblin Town?\",\"proposed\":\"lord_dain defend town_gt\"}") };
            string m = LieutenantRoom.Message(chat, history, "REPORT FOR X", new List<string> { "order applied: lord_dain defend" });
            Check(m.Contains("THE CONVERSATION SO FAR") && m.Contains("Ruler: hold Goblin Town") && m.Contains("[you proposed: lord_dain defend town_gt]") && m.Contains("YOUR PROPOSAL WAITING FOR YOUR RULER'S ANSWER (from message #6)")
                  && m.IndexOf("REPORT FOR X", StringComparison.Ordinal) < m.IndexOf("YOUR RULER SAYS", StringComparison.Ordinal) && m.Contains("(said just before) \"hold on\"") && m.TrimEnd().EndsWith("\"yes, send them\"")
                  && m.Contains("WHAT YOUR RULER DID SINCE YOUR LAST ANSWER") && m.Contains("- The ruler declined your proposal of message #5")
                  && m.IndexOf("WHAT YOUR RULER DID", StringComparison.Ordinal) < m.IndexOf("YOUR PROPOSAL WAITING", StringComparison.Ordinal),
                  "the lieutenant's message: the conversation, what became of its orders, what the ruler did with the buttons, the waiting proposal, the realm, then the ruler's words");
            // 0.11.3: raw line breaks typed inside a JSON string (the orcs' chronicler did it) are read, not thrown away
            string broken = "{\"story\": \"First part.\n\nSecond part.\", \"lessons\": [\"one\"]}";
            JsonObject fixedStory = JsonNode.Parse(Room.EscapeBreaks(broken)) as JsonObject;
            string fine = LieutenantRoom.ExtractAnswer("{\"say\": \"Two lines:\nhere.\"}", out JsonObject twoLines);
            Check(fixedStory != null && ((string)fixedStory["story"]).Contains("\n\nSecond part.") && fine == null && twoLines != null && ((string)twoLines["say"]).Contains("\nhere."),
                  "line breaks typed raw inside an answer's text are kept as line breaks, and the answer is read");
            string bad = LieutenantRoom.ExtractAnswer("{\"say\":\"ok\",\"orders\":[{\"lord\":\"x\"}]}", out JsonObject none);
            string good = LieutenantRoom.ExtractAnswer("thinking it over... {\"say\":\"Riding out.\",\"carry_out\":true,\"orders\":[{\"lord\":\"lord_dain\",\"do\":\"defend\",\"at\":\"town_gt\"}],\"pending_chat\":99}", out JsonObject ok);
            Check(bad != null && none == null && good == null && ok != null, "an answer is read from the last JSON object with \"say\", and an order without \"do\" is refused (" + bad + ")");
            LieutenantRoom.WriteAnswer(channel, 7, ok, 6);
            var written = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(channel, "orders", "chat-7.json")));
            Check((int)written["pending_chat"] == 6 && (int)written["chat"] == 7 && (bool)written["carry_out"] && LieutenantRoom.LastAnsweredOnDisk(channel) == 7,
                  "the answer file carries the waiting proposal's number from chat.json, never the model's, and counts as answered");
            // 0.11.1: the save's conversation from the bridge, and Party AI Controls now
            chat["history"] = new JsonArray("Summer 2, 1087, 09:00 · Ruler: keep the elves at Imnagath", "  You: They hold there now.");
            chat["history_total"] = 80;
            chat["party_ai"] = "Managed now (1):\n- Thalion [lord_m5_3]: order: defend Imnagath (yours)";
            string m2 = LieutenantRoom.Message(chat, history, "REPORT FOR X", new List<string>());
            Check(m2.Contains("THE CONVERSATION IN THIS SAVE SO FAR") && m2.Contains("its last 2 of 80 lines") && m2.Contains("conversation.txt") && m2.Contains("Ruler: keep the elves at Imnagath")
                  && !m2.Contains("[you proposed: lord_dain defend town_gt]") && m2.Contains("PARTY AI CONTROLS NOW") && m2.Contains("Thalion [lord_m5_3]")
                  && m2.IndexOf("PARTY AI CONTROLS NOW", StringComparison.Ordinal) < m2.IndexOf("YOUR RULER SAYS", StringComparison.Ordinal),
                  "with the save's conversation from the bridge, the message has its latest lines (not the war room's own memory) and Party AI Controls before the ruler's words");
            // 0.13 (B5): letters from the ruler's allies, a proposal's tools, diplomacy and letters waiting for the same yes, and the answer file carrying them
            chat["letters"] = new JsonArray("A letter from the ruler of Rohan (l4): \"We ride for Lasgalor on the 12th.\"");
            ((JsonObject)chat["pending"])["letters"] = new JsonArray(JsonNode.Parse("{\"to\": \"rohan\", \"text\": \"We come too.\"}"));
            string m3 = LieutenantRoom.Message(chat, history, "REPORT FOR X", new List<string>());
            Check(m3.Contains("LETTERS FROM YOUR RULER'S ALLIES SINCE YOUR LAST ANSWER") && m3.Contains("We ride for Lasgalor") && m3.Contains("letters: [{\"to\":\"rohan\"")
                  && m3.Contains("and its tools, diplomacy and letters, as they stand or changed"),
                  "the lieutenant hears its ruler's allies' letters, and its proposal's letters, tools and diplomacy wait for the same yes as its orders");
            LieutenantRoom.ExtractAnswer("{\"say\": \"Writing to Rohan.\", \"carry_out\": true, \"orders\": [], \"letters\": [{\"to\": \"rohan\", \"text\": \"We come too.\"}], \"tools\": [{\"name\": \"x\"}], \"diplomacy\": [{\"act\": \"propose_trade\", \"with\": \"rohan\"}]}", out JsonObject withAll);
            LieutenantRoom.WriteAnswer(channel, 8, withAll, 7);
            var written2 = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(channel, "orders", "chat-8.json")));
            Check(written2["letters"] is JsonArray wl && wl.Count == 1 && written2["tools"] is JsonArray && written2["diplomacy"] is JsonArray, "the answer file carries the lieutenant's tools, diplomacy and letters to the bridge");
        }

        private static Program.Trigger T(string kind, string text) => new Program.Trigger { Kind = kind, Text = text };

        /// <summary>
        /// Version 0.12.0: a council asks the game for its briefing (a request in the orders folder), and has it when the game writes
        /// council.txt; with no game to answer, it gives up after its wait, says so and takes the request back. A stand-in thread
        /// plays the bridge.
        /// </summary>
        private static void BriefingTests(Config c, string logs)
        {
            var room = new Room(c, logs);
            string orders = Path.Combine(c.ChannelDir, "orders");
            TimeSpan wait = Room.BriefingWait;
            Room.BriefingWait = TimeSpan.FromSeconds(5);
            Task<bool> bridge = Task.Run(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    string[] asked = Directory.Exists(orders) ? Directory.GetFiles(orders, Room.BriefingRequest + "*.json") : new string[0];
                    if (asked.Length > 0)
                    {
                        File.Delete(asked[0]);
                        File.WriteAllText(Path.Combine(c.ChannelDir, "report.txt"), "SITUATION AT Summer 9, 1087, 09:00");
                        File.WriteAllText(Path.Combine(c.ChannelDir, "council.txt"), "COUNCIL BRIEFING FOR THE ORCS");
                        return true;
                    }
                    Thread.Sleep(30);
                }
                return false;
            });
            bool came = room.RequestBriefing(out string note1);
            Check(came && bridge.Result && note1 == null && !Directory.GetFiles(orders, Room.BriefingRequest + "*").Any(),
                  "a council asks the game for its briefing in the orders folder, and has it when the game writes it");
            Thread.Sleep(100);
            Room.BriefingWait = TimeSpan.FromMilliseconds(400);
            bool none = room.RequestBriefing(out string note2);
            Check(!none && note2 != null && note2.Contains("did not write a fresh briefing") && !Directory.GetFiles(orders, Room.BriefingRequest + "*").Any(),
                  "with no game to answer, it gives up after its wait, says so, and takes its request back: " + note2);
            Room.BriefingWait = wait;
        }

        /// <summary>
        /// 0.13, the training record: the thinking's summary read from Claude Code's stream (and nothing from a block left empty), the
        /// setting that asks for it, and a call's thinking kept beside its prompt.
        /// </summary>
        /// <summary>
        /// 0.13, "check a tool" (docs/PLAN-0.13.md A4): the game's own checker in the war room, an answer's tool form read, the try asked of a
        /// stand-in bridge through the channel and the checker's result alone when none answers; and the runaway limit raised for tools.
        /// </summary>
        private static void ToolCheckTests(string root)
        {
            const string Watch = "{\"name\": \"watch_home\", \"about\": \"patrols around its castle\", \"takes\": {\"at\": \"own_fort: the castle\"}, \"settings\": {\"radius\": [12, 3, 30, \"how far\"]},"
                                 + " \"start\": \"a\", \"states\": {\"a\": {\"do\": \"patrol at\", \"when\": [{\"if\": \"hours_in_state >= 24\", \"go\": \"b\"}]}, \"b\": {\"end\": \"free\"}}}";
            var tool = new RouteTool(root, Path.Combine(root, "check-logs"), "camp-t", "orcs", 7, "answer");
            string Ask(string args) => (string)tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"check_tool\",\"arguments\":" + args + "}}"))?["result"]?["content"]?[0]?["text"];
            string ok = Ask("{\"definition\": " + Watch + "}");
            Check(ok != null && ok.StartsWith("THE CHECKER: watch_home passes (2 states, 1 rule") && ok.Contains("Name one of your lords"), "check_tool: a sound tool passes the game's own checker: " + ok);
            string bad = Ask("{\"definition\": {\"name\": \"bad\", \"start\": \"a\", \"states\": {\"a\": {\"do\": \"patrol at\", \"when\": [{\"if\": \"next_due(places, 24)\", \"go\": \"z\"}]}}}}");
            Check(bad != null && bad.Contains("problems, so it would be refused") && bad.Contains("state a, rule 1 (\"next_due(places, 24)\"): next_due takes 3 values, given 2") && bad.Contains("go must name one of the states"),
                  "check_tool: each problem with its place in the definition: " + bad?.Replace("\n", " | "));
            string entry = Ask("{\"definition\": {\"name\": \"watch_home\", \"keep\": true, \"definition\": " + Watch.Replace("\"name\": \"watch_home\", ", "") + "}}");
            Check(entry != null && entry.StartsWith("THE CHECKER: watch_home passes"), "check_tool reads a tool written as in an answer's tools too ({name, keep, definition})");
            Check(Ask("{\"definition\": " + Watch + ", \"lord\": \"lord_bolg\"}").Contains("THE TRY is not available in this call"), "a replay's call (no channel) has the checker only");

            // the try, asked of a stand-in bridge through the channel folder, as the game answers it
            string channel = Path.Combine(root, "check-channel");
            Directory.CreateDirectory(Path.Combine(channel, "orders"));
            tool.ChannelDir = channel;
            string seen = null;
            var stop = new CancellationTokenSource();
            var bridge = new Thread(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    foreach (string f in Directory.GetFiles(Path.Combine(channel, "orders"), "toolcheck-request-*.json"))
                    {
                        JsonObject q = JsonNode.Parse(File.ReadAllText(f)) as JsonObject;
                        File.Delete(f);
                        seen = (string)q?["lord"] + " " + (string)q?["at"] + " " + ((string)q?["definition"])?.Length;
                        File.WriteAllText(Path.Combine(channel, "toolcheck-" + (string)q?["id"] + ".txt"), "THE TRY: watch_home for Bolg (a stand-in bridge).");
                    }
                    Thread.Sleep(20);
                }
            }) { IsBackground = true };
            bridge.Start();
            string tried = Ask("{\"definition\": " + Watch + ", \"lord\": \"lord_bolg\", \"at\": \"castle_orc\"}");
            stop.Cancel();
            bridge.Join(1000);
            Check(tried != null && tried.Contains("THE TRY: watch_home for Bolg (a stand-in bridge).") && seen != null && seen.StartsWith("lord_bolg castle_orc ") && Directory.GetFiles(channel, "toolcheck-*.txt").Length == 0,
                  "with a lord, the try is asked of the bridge through the channel with the order's parts, and its answer read and tidied away: " + tried?.Replace("\n", " | "));
            TimeSpan wait = RouteTool.TryWait;
            RouteTool.TryWait = TimeSpan.FromMilliseconds(300);
            string late = Ask("{\"definition\": " + Watch + ", \"lord\": \"lord_bolg\"}");
            RouteTool.TryWait = wait;
            Check(late.Contains("THE TRY was not available: the game did not answer") && Directory.GetFiles(Path.Combine(channel, "orders")).Length == 0,
                  "with no bridge answering, the checker's result stands alone, and the request is taken back");
            string[] logged = File.ReadAllLines(Path.Combine(root, "check-logs", "routes.jsonl"));
            Check(logged.Count(l => l.Contains("\"tool\":\"check_tool\"")) == 6 && logged.Any(l => l.Contains("\"valid\":false") && l.Contains("next_due takes 3 values")),
                  "every check is logged with the definition, its problems and the answer (" + logged.Length + ")");
            Check(Claude.RunawayLimit("{\"poke\": 3, \"orders\": []}") == Claude.RunawayChars && Claude.RunawayLimit("{\"poke\": 3, \"tools\": [{\"name\":") == Claude.RunawayCharsWithTools,
                  "the runaway limit is raised for an answer that writes tools (" + Claude.RunawayCharsWithTools + " characters)");
            // 0.13.5 (Mert, Oct 29: "remove the time limit on the liutenant's thinking"): a time limit of 0 is none, and the lieutenant's
            // proposal, which may name every lord of the realm, gets the longer runaway limit
            string limits = Path.Combine(root, "limits");
            Directory.CreateDirectory(limits);
            File.WriteAllText(Path.Combine(limits, "warroom.txt"), "timeout_seconds = 0\nthinking_cap = 0\n");
            Config none = Config.Load(limits);
            Check(none.TimeoutSeconds == 0 && none.ThinkingCap == 0 && Claude.RunawayLimit("{\"say\": \"My proposal\", \"propose\": [{\"lord\":") == Claude.RunawayCharsWithTools,
                  "a time limit and a thinking cap of 0 are read as none, and a proposal gets the longer runaway limit");
        }

        private static void RecordTests(string root)
        {
            var sb = new System.Text.StringBuilder();
            Claude.Thoughts("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"thinking\",\"thinking\":\"The orcs must hold the pass.\",\"signature\":\"x\"},{\"type\":\"text\",\"text\":\"{}\"}]}}", sb);
            Claude.Thoughts("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"y\"}]}}", sb);
            Check(sb.ToString().Trim() == "The orcs must hold the pass.", "the thinking's summary is read from each assistant message of the stream; an empty block adds nothing");
            string dir = Path.Combine(root, "think-agent");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "warroom.txt"), "model = claude-opus-5-5\nthinking_display = omitted\n");
            Config t = Config.Load(dir);
            Check(t.ThinkingDisplay == "omitted" && Claude.ThinkingDisplay == "omitted", "warroom.txt's thinking_display sets how each call asks for the thinking");
            File.Delete(Path.Combine(dir, "warroom.txt"));
            Config d = Config.Load(dir);
            Check(d.ThinkingDisplay == "summarized" && File.ReadAllText(Path.Combine(dir, "warroom.txt")).Contains("thinking_display = summarized"), "by default the summary is asked for, and the setting is written for the agent's folder");
            string folder = Path.Combine(root, "call-folder");
            Directory.CreateDirectory(folder);
            Room.KeepThinking(folder, new CallResult { ThinkingText = "Mass first." });
            Check(File.ReadAllText(Path.Combine(folder, "thinking.txt")).Trim() == "Mass first.", "a call's thinking is kept beside its prompt and answer (thinking.txt)");
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  pass: " : "  FAIL: ") + what);
            if (!ok) _failures++;
        }
    }
}
