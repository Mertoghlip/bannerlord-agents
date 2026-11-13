using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The agent's own copy of each of its conversations' Claude Code transcripts (0.18.10). Claude Code keeps a conversation only in its own folder
    /// (projects\(the call's working folder)\(session).jsonl) and deletes every transcript left untouched for 30 days (its cleanupPeriodDays) each time it starts, in any
    /// project; a conversation whose transcript is gone cannot be carried on (--resume finds nothing), nor taken back to a checkpoint. Nov 12: a player who stops playing
    /// for a while and comes back should find the agents carrying on where they left off. So after every call the war room brings a copy in the campaign's memory folder up to
    /// date (transcripts\(session).jsonl, appending only what was added), and before a call carries a conversation on, or takes it back to a checkpoint, it puts the copy back
    /// where Claude Code looks when Claude Code no longer has it. Every conversation the conversation file, a checkpoint or a handover mark names is kept, so an old save
    /// loads as well as the latest.
    /// </summary>
    internal static class KeptTalk
    {
        public const string FolderName = "transcripts";

        public enum State { Present, Restored, Missing }

        private static readonly object Gate = new object();
        private static readonly Regex SessionRx = new Regex("\"cp_session\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>The kept copy of a session's transcript: (memory folder)\transcripts\(session).jsonl.</summary>
        public static string KeptFile(string memDir, string session) => Path.Combine(memDir ?? "", FolderName, session + ".jsonl");

        /// <summary>
        /// The folder Claude Code keeps a working folder's conversations in: (its projects)\(the folder's full path, every character but a letter or digit made "-"); an
        /// existing folder of that name in another letter case is the one.
        /// </summary>
        public static string ProjectFolder(string cwd)
        {
            string root = PastTalk.Projects();
            string full = Path.GetFullPath(cwd);
            if (full.Length > 3) full = full.TrimEnd('\\', '/');
            string name = new string(full.Select(ch => ch < 128 && char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
            if (Directory.Exists(root))
                foreach (string d in Directory.EnumerateDirectories(root))
                    if (string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)) return d;
            return Path.Combine(root, name);
        }

        /// <summary>
        /// The kept copy of a session brought up to date from Claude Code's transcript: what was added appended (the transcript only grows), else written whole, an older copy
        /// that is not its beginning set aside beside it. A copy longer than Claude Code's is left as it is. False when Claude Code has no transcript of it, or it could not be copied (told).
        /// </summary>
        public static bool Keep(string memDir, string session, Action<string> say)
        {
            if (string.IsNullOrEmpty(memDir) || string.IsNullOrEmpty(session)) return false;
            string src = PastTalk.SessionFile(session);
            if (src == null) return false;
            string dst = KeptFile(memDir, session);
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    using var from = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    long len = from.Length, have = File.Exists(dst) ? new FileInfo(dst).Length : -1;
                    if (have >= len) return true;
                    if (have > 0 && SameStart(from, dst, have))
                    {
                        from.Position = have;
                        using var to = new FileStream(dst, FileMode.Append, FileAccess.Write, FileShare.Read);
                        Pump(from, to, len - have);
                        return true;
                    }
                    from.Position = 0;
                    string part = dst + ".part";
                    using (var to = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None)) Pump(from, to, len);
                    if (have > 0) File.Move(dst, dst + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".earlier");
                    File.Move(part, dst, true);
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    say?.Invoke("Could not keep the agent's own copy of conversation " + session + ": " + e.Message);
                    return false;
                }
            }
        }

        /// <summary>
        /// Before a call carries session on (or takes it back to a checkpoint): Present when Claude Code has its transcript (the copy brought up to date), Restored when it had
        /// not and the kept copy was put back where Claude Code looks for the call's working folder (told), Missing when neither has it.
        /// </summary>
        public static State Ensure(string memDir, string session, string cwd, Action<string> say)
        {
            if (string.IsNullOrEmpty(session)) return State.Present;
            if (PastTalk.SessionFile(session) != null) { Keep(memDir, session, say); return State.Present; }
            string kept = KeptFile(memDir, session);
            if (!File.Exists(kept)) return State.Missing;
            lock (Gate)
            {
                try
                {
                    string dir = ProjectFolder(cwd);
                    Directory.CreateDirectory(dir);
                    string to = Path.Combine(dir, session + ".jsonl"), part = to + ".part";
                    File.Copy(kept, part, true);
                    File.Move(part, to, true);
                    say?.Invoke("Conversation " + session + ": Claude Code no longer had its transcript (it deletes conversations left unused for 30 days), so the agent's own copy ("
                                + (new FileInfo(kept).Length / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB) was put back and it carries on.");
                    return State.Restored;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    say?.Invoke("Conversation " + session + ": Claude Code no longer has its transcript, and the agent's own copy could not be put back: " + e.Message);
                    return State.Missing;
                }
            }
        }

        /// <summary>
        /// Every conversation of a campaign's memory folder kept: the one the conversation file names (and the one it is to be taken back into), every checkpoint's and
        /// handover mark's, and every earlier conversation's (past\). How many copies were made or grew; the sessions neither Claude Code nor the copies hold.
        /// </summary>
        public static int KeepAll(string memDir, Talk talk, out List<string> missing, Action<string> say)
        {
            missing = new List<string>();
            if (string.IsNullOrEmpty(memDir) || !Directory.Exists(memDir)) return 0;
            var sessions = new List<string>();
            void Add(string s) { if (!string.IsNullOrEmpty(s) && !sessions.Contains(s)) sessions.Add(s); }
            Add(talk?.SessionId);
            Add(talk?.ForkFrom);
            string cps = Room.ReadShared(Path.Combine(memDir, Talk.CheckpointFile));
            if (cps != null) foreach (Match m in SessionRx.Matches(cps)) Add(m.Groups[1].Value);
            foreach (PastTalk.About a in PastTalk.All(memDir)) Add(a.Session);
            int grew = 0;
            foreach (string s in sessions)
            {
                string kept = KeptFile(memDir, s);
                long before = File.Exists(kept) ? new FileInfo(kept).Length : -1;
                if (Keep(memDir, s, say)) { if (new FileInfo(kept).Length != before) grew++; }
                else if (!File.Exists(kept)) missing.Add(s);
            }
            return grew;
        }

        /// <summary>Every campaign of an agent's memory with a conversation file, kept (at the war room's start, so the copies exist before the first call); told when any grew.</summary>
        public static void KeepEvery(string memoryRoot, Action<string> say)
        {
            if (string.IsNullOrEmpty(memoryRoot) || !Directory.Exists(memoryRoot)) return;
            foreach (string dir in Directory.GetDirectories(memoryRoot))
            {
                if (!File.Exists(Path.Combine(dir, "conversation.json")) && !File.Exists(Path.Combine(dir, Talk.CheckpointFile))) continue;
                int grew = KeepAll(dir, Talk.Load(dir), out List<string> missing, say);
                if (grew > 0) say?.Invoke("Campaign " + Path.GetFileName(dir) + ": the agent's own copies of its conversations brought up to date (" + grew + " of them), in " + Path.Combine(dir, FolderName) + ".");
                if (missing.Count > 0) say?.Invoke("Campaign " + Path.GetFileName(dir) + ": " + missing.Count + " conversation" + (missing.Count == 1 ? "" : "s") + " named in its memory "
                                                   + (missing.Count == 1 ? "has" : "have") + " no transcript left, in Claude Code or copied (" + string.Join(", ", missing) + ").");
            }
        }

        // ---------- copying ----------

        /// <summary>The first bytes and the last before have are the same in the source and the copy: the copy is the source's beginning.</summary>
        private static bool SameStart(FileStream from, string dst, long have)
        {
            int n = (int)Math.Min(have, 8192);
            byte[] a = new byte[n], b = new byte[n];
            using var to = new FileStream(dst, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            foreach (long at in have > n ? new[] { 0L, have - n } : new[] { 0L })
            {
                from.Position = at;
                to.Position = at;
                if (ReadFull(from, a, n) != n || ReadFull(to, b, n) != n || !a.AsSpan().SequenceEqual(b)) return false;
            }
            return true;
        }

        private static int ReadFull(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int k = s.Read(buf, got, n - got);
                if (k <= 0) break;
                got += k;
            }
            return got;
        }

        private static void Pump(Stream from, Stream to, long count)
        {
            byte[] buf = new byte[1 << 20];
            while (count > 0)
            {
                int k = from.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (k <= 0) break;
                to.Write(buf, 0, k);
                count -= k;
            }
        }
    }
}
