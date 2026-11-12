namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// The campaign's one conversation taken back to an earlier save (0.18.7), with no calls: a checkpoint after each answer, the conversation's state put back
        /// from the last checkpoint at or before the save's hour, its next call resuming the old session just after that answer as a new session, the later
        /// checkpoints dropped, and nothing to go back to before the first.
        /// </summary>
        private static void RollbackTests(string root)
        {
            string dir = Path.Combine(root, "rollback");
            Directory.CreateDirectory(dir);
            Talk t = Talk.Load(dir);
            t.SessionId = "s1";
            t.Started = true;
            t.Tokens = 100;
            t.Sent["YOUR LORDS"] = "a";
            t.Checkpoint("u1", 10, "Summer 1, 1087, 10:00", "L1");
            t.Tokens = 200;
            t.Sent["IN VIEW"] = "b";
            t.NextReviewHours = 40;
            t.Checkpoint("u2", 20, "Summer 1, 1087, 20:00", "L1");
            t.Tokens = 300;
            t.Sent["YOUR PURSE"] = "c";
            t.Checkpoint("u3", 30, "Summer 2, 1087, 06:00", "L1");
            t.Checkpoint(null, 31, "no message id: not kept", "L1");
            t.Save();
            string file = Path.Combine(dir, Talk.CheckpointFile);
            Check(File.ReadAllLines(file).Length == 3, "a checkpoint after each answer, none without the answer's message id");

            Check(t.RollBack(null, 25, out string back) && back == "Summer 1, 1087, 20:00" && t.Tokens == 200 && t.ForkFrom == "s1" && t.ForkAt == "u2" && t.SessionId != "s1" && t.Started
                  && t.Sent.ContainsKey("IN VIEW") && !t.Sent.ContainsKey("YOUR PURSE") && t.NextReviewHours == 40,
                  "a save from between two answers takes the conversation back to the earlier one: its state then, the old session resumed just after it as a new session");
            Check(File.ReadAllLines(file).Length == 2, "the checkpoints after it are dropped: what followed did not happen");
            Talk reread = Talk.Load(dir);
            Check(reread.ForkFrom == "s1" && reread.ForkAt == "u2" && reread.SessionId == t.SessionId && reread.Tokens == 200, "the taken-back conversation is kept, for the next call to resume");
            Check(!t.RollBack(null, 5, out _), "a save older than the first answer has nothing to go back to (a new conversation begins)");
            Check(t.RollBack(null, 20.3, out string same) && same == "Summer 1, 1087, 20:00", "a save in the same hour as an answer keeps that answer");
            // by the save's history: a checkpoint of another session (a new run from the same start save) is not this save's, even at the same hour
            var history = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("L0", 9), new KeyValuePair<string, double>("L1", 12) };
            Check(t.RollBack(history, 12, out string inHistory) && inHistory == "Summer 1, 1087, 10:00", "within the save's history: the last answer its sessions reached by the hour it left them");
            Check(!t.RollBack(new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("L9", 50) }, 50, out _), "a save of another history has nothing of this conversation to go back to");
            // 0.18.8: a handover's mark: a save after it begins the conversation that follows it; a save before it still goes back into the one it ended
            string hdir = Path.Combine(root, "rollback-handover");
            Directory.CreateDirectory(hdir);
            Talk h = Talk.Load(hdir);
            h.SessionId = "old"; h.Started = true; h.Tokens = 650000;
            h.Checkpoint("w16", 16, "Autumn 6, 1087, 16:00", "A");
            h.Checkpoint("w18", 18, "Autumn 6, 1087, 18:00", "B");
            h.MarkHandover(18.5, "Autumn 6, 1087, 18:00", "B", "the human player's battle has begun");
            h.Reset("your last conversation ended with the handover below");
            var afterIt = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("A", 18), new KeyValuePair<string, double>("B", 21) };
            Check(!h.RollBack(afterIt, 21, out string hd) && h.AfterHandover && !h.Started && h.SessionId == null && (h.NextWhy ?? "").Contains("the handover below")
                  && (h.NextWhy ?? "").Contains("battle has begun") && hd == "Autumn 6, 1087, 18:00",
                  "a save made after a handover begins the conversation that follows it, from the handover, not the one it ended");
            var beforeIt = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("A", 18) };
            Check(h.RollBack(beforeIt, 18, out string bd) && !h.AfterHandover && bd == "Autumn 6, 1087, 16:00" && h.ForkFrom == "old" && h.ForkAt == "w16" && h.Tokens == 650000,
                  "a save from before the handover still goes back into the conversation it ended, to its last answer in that history");
            t.Reset("test");
            Check(t.ForkFrom == null && t.ForkAt == null && !t.Started, "a new conversation carries no taken-back session");
        }
    }
}
