using System;
using System.IO;
using System.Text;

namespace BannerlordAgents
{
    /// <summary>
    /// Where everything the module writes lives: %LOCALAPPDATA%\BannerlordAgents. Never the game's Documents
    /// folder: on the project laptop that folder is inside OneDrive, and a file that changes every few seconds
    /// there gets uploaded over and over while the game runs. The smoke test points BANNERLORD_AGENTS_ROOT at its
    /// own folder, so test runs never mix with the game's files.
    /// </summary>
    internal static class Paths
    {
        public static readonly string Root = MakeRoot();

        private static string MakeRoot()
        {
            string over = Environment.GetEnvironmentVariable("BANNERLORD_AGENTS_ROOT");
            if (!string.IsNullOrWhiteSpace(over)) return Ensure(over.Trim());
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
            return Ensure(Path.Combine(local, "BannerlordAgents"));
        }

        public static string Logs => Ensure(Path.Combine(Root, "logs"));

        /// <summary>One folder per game launch, for the detailed logs (Diag).</summary>
        public static string Sessions => Ensure(Path.Combine(Root, "logs", "sessions"));

        /// <summary>The test runs (0.11, docs/RUNS.md): one folder each, and current.txt naming the one that is open.</summary>
        public static string Runs => Ensure(Path.Combine(Root, "runs"));

        /// <summary>One folder per campaign, named by the game's own id for it.</summary>
        public static string Data(string campaign) => Ensure(Path.Combine(Root, "data", Safe(campaign)));

        public static string Ensure(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { /* the write that follows reports it */ }
            return dir;
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }

    /// <summary>
    /// The module's readable story: logs\bridge.log across launches, and story.log in this launch's session folder.
    /// Any thread may write a line. While the background helper runs, lines are queued and the helper writes them
    /// about once a second, so the game's thread never waits on the disk. Before the helper starts and after it
    /// stops (and in the smoke test), lines are written straight away. Past 8 MB at launch, bridge.log is kept as
    /// bridge_prev.log and a new one begins.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static StreamWriter _main, _session;
        private static string _sessionDir;
        private static bool _opened, _closed;

        public static string Dir => Paths.Logs;
        public static string FilePath => Path.Combine(Dir, "bridge.log");

        public static void Line(string text)
        {
            long wall = DateTime.Now.Ticks;
            Diag.Remember(text);
            if (Background.Running && !Background.IsWorker) Diag.QueueLine(wall, text);
            else Write(wall, text);
        }

        /// <summary>Writes one line now. The background helper's path, and the direct path when it is not running.</summary>
        internal static void Write(long wall, string text)
        {
            try
            {
                lock (Gate)
                {
                    if (_closed) return;
                    Open();
                    string line = new DateTime(wall).ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text;
                    _main?.WriteLine(line);
                    _session?.WriteLine(line);
                    if (!Background.Running || !Background.IsWorker) { _main?.Flush(); _session?.Flush(); }
                }
            }
            catch { /* the log itself cannot report its own failure anywhere better */ }
        }

        internal static void Flush()
        {
            lock (Gate)
            {
                try { _main?.Flush(); } catch { /* see Write */ }
                try { _session?.Flush(); } catch { /* see Write */ }
            }
        }

        internal static void Close()
        {
            lock (Gate)
            {
                try { _main?.Dispose(); } catch { /* see Write */ }
                try { _session?.Dispose(); } catch { /* see Write */ }
                _main = _session = null;
                _closed = true;
                _sessionDir = null;
            }
        }

        private static void Open()
        {
            if (!_opened)
            {
                _opened = true;
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 8L * 1024 * 1024)
                {
                    string prev = Path.Combine(Dir, "bridge_prev.log");
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(FilePath, prev);
                }
                _main = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8);
                _main.WriteLine();
                _main.WriteLine("==== game started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
            }
            string dir = Diag.SessionDir;
            if (dir != null && dir != _sessionDir)
            {
                _sessionDir = dir;
                try { _session?.Dispose(); } catch { /* see Write */ }
                _session = new StreamWriter(new FileStream(Path.Combine(dir, "story.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8);
            }
        }
    }
}
