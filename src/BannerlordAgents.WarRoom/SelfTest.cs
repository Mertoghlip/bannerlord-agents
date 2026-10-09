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

        private static Program.Trigger T(string kind, string text) => new Program.Trigger { Kind = kind, Text = text };

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  pass: " : "  FAIL: ") + what);
            if (!ok) _failures++;
        }
    }
}
