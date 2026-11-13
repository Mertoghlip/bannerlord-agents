namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// The agent's own copies of its conversations (0.18.10), with no calls: a made-up Claude Code transcript kept, grown and kept again by appending, put back where Claude
        /// Code looks once Claude Code has deleted it, every conversation the memory names kept at once, an earlier conversation's copy made from the kept copy, and a
        /// conversation lost both ways begun again from the handover.
        /// </summary>
        private static void KeptTests(string root)
        {
            string projects = Path.Combine(root, "kept-projects"), cwd = Path.Combine(root, "kept-agent", "warroom");
            Directory.CreateDirectory(cwd);
            string old = PastTalk.ProjectsRoot;
            PastTalk.ProjectsRoot = projects;
            try
            {
                string folder = KeptTalk.ProjectFolder(cwd);
                Check(Path.GetDirectoryName(folder) == projects && !Path.GetFileName(folder).Contains('\\') && !Path.GetFileName(folder).Contains(':') && Path.GetFileName(folder).EndsWith("-kept-agent-warroom", StringComparison.Ordinal),
                      "Claude Code's folder for a working folder: its path with every character but a letter or digit made \"-\"");
                Directory.CreateDirectory(folder);
                string J(string s) => System.Text.Json.JsonSerializer.Serialize(s);
                string User(string text) => "{\"type\":\"user\",\"uuid\":\"u\",\"message\":{\"role\":\"user\",\"content\":" + J(text) + "}}";
                string Said(string text) => "{\"type\":\"assistant\",\"uuid\":\"a\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":" + J(text) + "}]}}";
                string src = Path.Combine(folder, "sess-k.jsonl");
                File.WriteAllText(src, User("POKE #1, Summer 1, 1087, 10:00\nAnswer.") + "\n" + Said("{\"poke\": 1, \"orders\": []}") + "\n");

                string mem = Path.Combine(root, "kept-agent", "memory", "camp");
                Directory.CreateDirectory(mem);
                string kept = KeptTalk.KeptFile(mem, "sess-k");
                Check(KeptTalk.Keep(mem, "sess-k", null) && File.ReadAllText(kept) == File.ReadAllText(src), "after a call: the agent's own copy of the conversation, word for word");
                File.AppendAllText(src, User("POKE #2, Summer 1, 1087, 14:00\nAnswer.") + "\n" + Said("{\"poke\": 2, \"orders\": []}") + "\n");
                Check(KeptTalk.Keep(mem, "sess-k", null) && File.ReadAllText(kept) == File.ReadAllText(src) && !Directory.GetFiles(Path.GetDirectoryName(kept), "*.earlier").Any(),
                      "the conversation grew: what was added appended to the copy");
                Check(!KeptTalk.Keep(mem, "nobody", null) && !File.Exists(KeptTalk.KeptFile(mem, "nobody")), "no transcript, no copy");

                // Claude Code deleted it (30 days unused): put back before the next call, then found as before
                string whole = File.ReadAllText(src);
                File.Delete(src);
                Check(PastTalk.SessionFile("sess-k") == null && PastTalk.SessionFile("sess-k", mem) == kept, "Claude Code's transcript gone: the copy stands in for reading");
                var said = new List<string>();
                Check(KeptTalk.Ensure(mem, "sess-k", cwd, said.Add) == KeptTalk.State.Restored && File.Exists(src) && File.ReadAllText(src) == whole && said.Any(s => s.Contains("put back")),
                      "before the next call: the copy put back where Claude Code looks, and told");
                Check(KeptTalk.Ensure(mem, "sess-k", cwd, null) == KeptTalk.State.Present && PastTalk.SessionFile("sess-k") == src, "then Claude Code has it again");
                Check(KeptTalk.Ensure(mem, "sess-never", cwd, null) == KeptTalk.State.Missing, "a conversation neither holds is missing");

                // every conversation the memory names: the checkpoints' (an old save's), the handover marks', the earlier ones'
                File.WriteAllText(Path.Combine(folder, "sess-old.jsonl"), User("POKE #0, Spring 20, 1087, 10:00\nAnswer.") + "\n" + Said("{\"poke\": 0}") + "\n");
                Talk t = Talk.Load(mem);
                t.SessionId = "sess-old";
                t.Started = true;
                t.Checkpoint("a", 10, "Spring 20, 1087, 10:00", "L1");
                t.MarkHandover(12, "Spring 20, 1087, 12:00", "L1", "test", "sess-old");
                t.MarkHandover(13, "Spring 20, 1087, 13:00", "L1", "test", "sess-lost");
                t.SessionId = "sess-k";
                t.Save();
                int grew = KeptTalk.KeepAll(mem, t, out List<string> missing, null);
                Check(grew == 1 && File.Exists(KeptTalk.KeptFile(mem, "sess-old")) && missing.SequenceEqual(new[] { "sess-lost" }),
                      "at a load: every conversation the checkpoints and marks name is kept; one held nowhere is named");
                File.Delete(Path.Combine(folder, "sess-old.jsonl"));
                PastTalk.About a = PastTalk.Keep(mem, "sess-old", 12, "Spring 20, 1087, 12:00", "L1", "test", out string problem);
                Check(a != null && problem == null && a.Exchanges == 1, "an earlier conversation's word-for-word copy is made from the agent's copy when Claude Code has it no more");
                said.Clear();
                KeptTalk.KeepEvery(Path.Combine(root, "kept-agent", "memory"), said.Add);
                Check(said.Count == 1 && said[0].Contains("sess-lost") && said[0].Contains("no transcript left"), "at the war room's start: the copies brought up to date, a conversation held nowhere told");

                // lost both ways: the wake begins a new conversation instead of failing call after call
                var c = new Config { AgentDir = Path.Combine(root, "kept-agent"), Channel = "warroom", MemoryMode = "continuous" };
                var room = new Room(c, Path.Combine(root, "kept-logs"));
                Memory m = Memory.Open(c, "camp", _ => { });
                t.SessionId = "sess-k";
                m.SessionId = "sess-k";
                room.UseForTest(m, t);
                room.CheckTranscriptForTest();
                Check(t.Started && t.SessionId == "sess-k", "a conversation whose transcript is there carries on");
                File.Delete(src);
                File.Delete(kept);
                room.CheckTranscriptForTest();
                Check(!t.Started && t.SessionId == null && (t.NextWhy ?? "").Contains("lost") && m.SessionId == null,
                      "a conversation whose transcript is gone both ways: the next wake begins a new one from the handover and notes");
            }
            finally { PastTalk.ProjectsRoot = old; }
        }

        /// <summary>
        /// A review right after an answer (0.19): what became of that answer, as the game's receipts tell it, found by the answer's file name among the lines written after it went out;
        /// none when the game has not answered yet.
        /// </summary>
        private static void ReviewFateTests(string root)
        {
            string agent = Path.Combine(root, "fate-agent"), channel = Path.Combine(agent, "warroom");
            Directory.CreateDirectory(channel);
            var c = new Config { AgentDir = agent, Channel = "warroom", MemoryMode = "continuous" };
            var room = new Room(c, Path.Combine(root, "fate-logs"));
            string receipts = Path.Combine(channel, "receipts.log");
            File.WriteAllText(receipts, "20:10:00  poke #2821, poke-2821.json order 1 (lord_a garrison): APPLIED. old answer\n");
            long mark = new FileInfo(receipts).Length;
            Check(room.AnswerFate("poke-2822.json", mark, 300) == null, "a review whose last answer the game has not taken yet is told so, not given old receipts");
            File.AppendAllText(receipts, "20:16:18  poke #2822, poke-2822.json order 1 (lord_d11_2 garrison): APPLIED. Mokrash garrison Caras Laerolin.\n"
                                         + "20:16:18  poke #2822, poke-2822.json watch 2 (cl_food): ENDED. ended, as you asked.\n"
                                         + "20:17:25  poke #2822, poke-2822-council-201724.json: RECEIVED 8 orders. The game's clock is stopped right now.\n");
            string fate = room.AnswerFate("poke-2822.json", mark, 300);
            Check(fate != null && fate.Contains("APPLIED. Mokrash garrison") && fate.Contains("ENDED") && !fate.Contains("council") && !fate.Contains("old answer"),
                  "what became of the last answer: its own receipt lines, written after it went out, and no other file's");
            string paused = room.AnswerFate("poke-2822-council-201724.json", mark, 300);
            Check(paused != null && paused.Contains("RECEIVED 8 orders"), "an answer the game holds until its clock runs again is told as received");
        }
    }
}
