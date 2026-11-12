using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// The earlier conversations word for word (0.18.9), with no calls: a made-up Claude Code transcript turned into exchanges, kept at a handover, shown only when its
        /// handover is in the loaded save's history, made later from a handover's mark when it was not made at the time, and read through the past question.
        /// </summary>
        private static void PastTests(string root)
        {
            string projects = Path.Combine(root, "claude-projects"), folder = Path.Combine(projects, "C--made-up-warroom");
            Directory.CreateDirectory(folder);
            string old = PastTalk.ProjectsRoot;
            PastTalk.ProjectsRoot = projects;
            try
            {
                string J(string s) => System.Text.Json.JsonSerializer.Serialize(s);
                string User(string text) => "{\"type\":\"user\",\"uuid\":\"u\",\"message\":{\"role\":\"user\",\"content\":" + J(text) + "}}";
                string Thinking(string text) => "{\"type\":\"assistant\",\"uuid\":\"a\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"thinking\",\"thinking\":" + J(text) + "}]}}";
                string Said(string text) => "{\"type\":\"assistant\",\"uuid\":\"a\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":" + J(text) + "}]}}";
                string Asked(string name, string input) => "{\"type\":\"assistant\",\"uuid\":\"a\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":" + J(name) + ",\"input\":" + input + "}]}}";
                string Result(string text) => "{\"type\":\"user\",\"uuid\":\"u\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":[{\"type\":\"text\",\"text\":" + J(text) + "}]}]}}";
                string longAnswer = string.Join("\n", Enumerable.Range(1, 2600).Select(i => "Line " + i + " of a long strategy review answer, about the fords below Erebor and the muster at Dale."));
                string[] lines =
                {
                    "{\"type\":\"queue-operation\",\"operation\":\"enqueue\"}",
                    User("A NEW CONVERSATION BEGINS: the first of your realm's conversations.\n\nPOKE #7, Summer 1, 1087, 10:00\nAnswer with one orders file."),
                    "{\"type\":\"attachment\",\"uuid\":\"x\"}",
                    "{\"type\":\"user\",\"isMeta\":true,\"uuid\":\"m\",\"message\":{\"role\":\"user\",\"content\":\"a note Claude Code adds, not a message\"}}",
                    Thinking("The band near Dale is small; Dain can hold."),
                    Asked("mcp__routes__look_up", "{\"id\":\"lord_dain\"}"),
                    Result("Dain at Erebor, 400 men, food for 9 days."),
                    Said("{\"poke\": 7, \"orders\": []}"),
                    User("STRATEGY REVIEW, Summer 2, 1087, 06:00\nWhy it sits now: the hour you set for it has come."),
                    Said(longAnswer),
                    User("WRITE YOUR HANDOVER NOW: the human player's battle has begun."),
                    Said("HANDOVER, Summer 2, 1087, 08:00. Hold the fords."),
                };
                File.WriteAllText(Path.Combine(folder, "sess-1.jsonl"), string.Join("\n", lines) + "\n");
                Check(PastTalk.SessionFile("sess-1") != null && PastTalk.SessionFile("nobody") == null, "Claude Code's transcript of a conversation is found by its id");

                var ex = PastTalk.Read(PastTalk.SessionFile("sess-1"));
                Check(ex.Count == 3 && ex[0].Kind == "the opening of the conversation, then wake (poke #7)" && ex[0].Date == "Summer 1, 1087, 10:00" && ex[1].Kind == "strategy review"
                      && ex[2].Kind == "the request for your handover" && ex[2].Date == "Summer 2, 1087, 06:00" && ex[1].Hours == PastTalk.HoursOf("Summer 2, 1087, 06:00")
                      && PastTalk.HoursOf("Summer 2, 1087, 06:00") == (1087 * 84 + 21 + 1) * 24 + 6,
                      "the transcript in exchanges: each message sent with what followed, what it was and its in-game date (a handover's request takes the last date)");
                Check(ex[0].Text.Contains("--- YOUR THINKING (as summarized) ---\nThe band near Dale") && ex[0].Text.Contains("--- YOU ASKED look_up: {\"id\":\"lord_dain\"} ---")
                      && ex[0].Text.Contains("--- ITS ANSWER ---\nDain at Erebor, 400 men") && ex[0].Text.Contains("--- YOUR ANSWER ---\n{\"poke\": 7") && !ex[0].Text.Contains("a note Claude Code adds"),
                      "an exchange holds the thinking, the questions with their answers and the answer, and nothing Claude Code added on its own");

                // kept at a handover, and shown only in the history that holds the handover
                string mem = Path.Combine(root, "past-mem");
                Directory.CreateDirectory(mem);
                Talk t = Talk.Load(mem);
                PastTalk.KeepEnded(t, mem, "sess-1", 25, "Summer 2, 1087, 08:00", "L1", "the human player's battle has begun", null);
                List<PastTalk.About> kept = PastTalk.All(mem);
                Check(kept.Count == 1 && t.Past.Count == 1 && t.Past[0] == kept[0].Id && kept[0].Exchanges == 3 && kept[0].From == "Summer 1, 1087, 10:00" && kept[0].To == "Summer 2, 1087, 06:00"
                      && kept[0].Load == "L1" && File.Exists(Path.Combine(mem, PastTalk.FolderName, kept[0].Id, "conversation.txt")),
                      "a handover keeps its conversation word for word, and the next conversation has it");
                var inIt = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("L0", 10), new KeyValuePair<string, double>("L1", 30) };
                var beforeIt = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("L1", 20) };
                var otherRun = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("L9", 50) };
                Check(PastTalk.Visible(mem, inIt, 30).Count == 1 && PastTalk.Visible(mem, beforeIt, 20).Count == 0 && PastTalk.Visible(mem, otherRun, 50).Count == 0
                      && PastTalk.Visible(mem, null, 26).Count == 1 && PastTalk.Visible(mem, null, 20).Count == 0,
                      "an earlier conversation is shown only in a save whose history holds its handover (not one from before it, not another run's)");
                t.PastTool = true;
                t.Reset("test");
                Check(t.Past.Count == 1 && !t.PastTool, "a new conversation keeps the list of earlier ones, and decides at its start whether it offers the question");

                // the question
                string Ask(string json, IList<string> ids = null) => PastTalk.Answer(mem, ids ?? t.Past, (JsonObject)JsonNode.Parse(json));
                string list = Ask("{}");
                Check(list.Contains("Earlier conversation 1 of 1: Summer 1, 1087, 10:00 to Summer 2, 1087, 06:00, 3 exchanges") && list.Contains("#1 · Summer 1, 1087, 10:00 · the opening of the conversation, then wake (poke #7)")
                      && list.Contains("#2 · Summer 2, 1087, 06:00 · strategy review"), "with nothing given: the earlier conversation and its exchanges by date");
                string one = Ask("{\"exchange\": 1}");
                Check(one.Contains("--- YOU ASKED look_up") && one.Contains("Dain at Erebor, 400 men") && !one.Contains("part 1 of"), "an exchange read whole");
                string p1 = Ask("{\"exchange\": 2}"), p2 = Ask("{\"exchange\": 2, \"part\": 2}");
                int parts = (ex[1].Text.Length + PastTalk.PartChars - 1) / PastTalk.PartChars;
                Check(parts >= 2 && p1.Contains("part 1 of " + parts) && p1.Contains("ask for part 2") && p2.Contains("part 2 of " + parts) && p1.Contains("Line 1 of") && !p2.Contains("Line 1 of a long")
                      && Ask("{\"exchange\": 2, \"part\": " + (parts + 1) + "}").Contains("has " + parts + " parts") && Ask("{\"exchange\": 9}").Contains("no exchange 9"),
                      "a long exchange comes in parts, cut at line ends; numbers beyond them are told");
                Check(Ask("{\"with\": \"food for 9\"}").Contains("#1 · Summer 1, 1087, 10:00 · Dain at Erebor, 400 men, food for 9 days.") && Ask("{\"with\": \"nothing like this\"}").Contains("No line holds"),
                      "words found, each line with its exchange");
                Check(Ask("{\"day\": \"Summer 2, 1087\"}").Contains("#2 · Summer 2, 1087") && !Ask("{\"day\": \"Summer 2, 1087\"}").Contains("#1 ·") && Ask("{\"day\": \"Winter 9, 1090\"}").Contains("No exchange of it is dated"),
                      "a day's exchanges");
                Check(Ask("{}", new List<string>()).Contains("no earlier conversation") && Ask("{\"conversation\": 4}").Contains("no earlier conversation 4"),
                      "no earlier conversation in this game's history: said so; a number beyond them told");
                string line = PastTalk.OpeningLine(mem, t.Past);
                Check(line != null && line.StartsWith("YOUR EARLIER CONVERSATIONS, WORD FOR WORD", StringComparison.Ordinal) && line.Contains("3 exchanges") && line.Contains("ask past")
                      && PastTalk.OpeningLine(mem, new List<string>()) == null, "the opening after a handover names them and the question");

                // the question in the tool server: offered only with the folder, answered without the game
                var tool = new RouteTool(root, Path.Combine(root, "past-logs"), "camp", "orcs", 1, "test") { BookOnly = true };
                JsonNode names = tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
                Check(!(names?.ToJsonString() ?? "").Contains("\"past\""), "the question is not offered without earlier conversations");
                tool.PastMem = mem;
                tool.PastIds = t.Past;
                names = tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));
                JsonNode answer = tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"past\",\"arguments\":{\"exchange\":3}}}"));
                Check((names?.ToJsonString() ?? "").Contains("\"past\"") && (answer?["result"]?["content"]?[0]?["text"]?.GetValue<string>() ?? "").Contains("HANDOVER, Summer 2, 1087, 08:00. Hold the fords."),
                      "with them, the tool server offers past and answers it");

                // made later from a handover's mark (one written before 0.18.9): its conversation named on the checkpoint before it, made once
                string mem2 = Path.Combine(root, "past-mend");
                Directory.CreateDirectory(mem2);
                Talk m2 = Talk.Load(mem2);
                m2.SessionId = "sess-1";
                m2.Started = true;
                m2.Checkpoint("u1", 24, "Summer 2, 1087, 00:00", "L1");
                m2.MarkHandover(25, "Summer 2, 1087, 08:00", "L1", "the game has been closed");
                int made = PastTalk.Mend(mem2, null), again = PastTalk.Mend(mem2, null);
                Check(made == 1 && again == 0 && PastTalk.All(mem2).Single().Session == "sess-1" && PastTalk.All(mem2).Single().Why == "the game has been closed" && PastTalk.Visible(mem2, inIt, 30).Count == 1,
                      "a handover's mark without its copy gets it at the next load, once, from the conversation named before it");
                m2.MarkHandover(40, "Summer 2, 1087, 23:00", "L2", "test", "sess-gone");
                Check(PastTalk.Mend(mem2, null) == 0 && File.ReadAllText(Path.Combine(mem2, Talk.CheckpointFile)).Contains("\"cp_session\":\"sess-gone\""),
                      "a handover's mark names its conversation; a transcript not found makes nothing");

                // the lieutenant: a load takes the earlier conversations of the save's history, and its opening names them
                string ldir = Path.Combine(root, "past-lieutenant"), lmem = Path.Combine(ldir, "memory", "camp");
                Directory.CreateDirectory(Path.Combine(ldir, "warroom"));
                Directory.CreateDirectory(lmem);
                PastTalk.Keep(lmem, "sess-1", 25, "Summer 2, 1087, 08:00", "L1", "your ruler's battle has begun", out _);
                var lroom = new LieutenantRoom(new Config { AgentDir = ldir, Channel = "warroom", MemoryMode = "continuous" }, Path.Combine(ldir, "logs"));
                Talk lt = Talk.Load(lmem);
                lroom.UseForTest(lt, lmem);
                JsonObject LChat(string load, double loadedAt, string historyLoad, double until) => (JsonObject)JsonNode.Parse(
                    "{\"number\":3,\"said\":\"Where are the orcs?\",\"date\":\"Summer 3, 1087, 10:00\",\"hours\":" + loadedAt + ",\"campaign\":\"camp\",\"realm\":\"player\",\"load_id\":\"" + load + "\",\"loaded_at\":" + loadedAt
                    + ",\"save_history\":{\"before\":[{\"load\":\"" + historyLoad + "\",\"until\":" + until + "}]},\"history\":[],\"history_total\":0}");
                lroom.EnsureTalk(lmem, LChat("L2", 30, "L1", 30), 30);
                Check(lt.Past.Count == 1, "the lieutenant: a save whose history holds the handover gives it its earlier conversation");
                string lopen = lroom.Opening(LChat("L2", 30, "L1", 30), lmem, "SITUATION AT Summer 3, 1087, 10:00\n\nYOUR LORDS AND ARMIES (1, 100 men)\n- Dain at Erebor.\n", 30);
                Check(lopen.Contains("YOUR EARLIER CONVERSATIONS, WORD FOR WORD"), "and its opening names them");
                lroom.EnsureTalk(lmem, LChat("L3", 20, "L1", 20), 20);
                Check(lt.Past.Count == 0, "a save from before the handover gives none");
            }
            finally { PastTalk.ProjectsRoot = old; }
        }
    }
}
