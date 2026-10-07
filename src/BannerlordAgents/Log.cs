using System;
using System.IO;

namespace BannerlordAgents
{
    /// <summary>
    /// Where everything the module writes lives: %LOCALAPPDATA%\BannerlordAgents. Never the game's Documents
    /// folder: on the project laptop that folder is inside OneDrive, and a file that changes every few seconds
    /// there gets uploaded over and over while the game runs.
    /// </summary>
    internal static class Paths
    {
        public static readonly string Root = MakeRoot();

        private static string MakeRoot()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
            return Ensure(Path.Combine(local, "BannerlordAgents"));
        }

        public static string Logs => Ensure(Path.Combine(Root, "logs"));

        /// <summary>One folder per campaign, named by the game's own id for it.</summary>
        public static string Data(string campaign) => Ensure(Path.Combine(Root, "data", Safe(campaign)));

        public static string Ensure(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }
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
    /// The module's own human-readable log, logs\bridge.log. It carries across launches; past 8 MB the old one is
    /// kept as bridge_prev.log. Safe to call from any thread.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static bool _fresh = true;

        public static string Dir => Paths.Logs;
        public static string FilePath => Path.Combine(Dir, "bridge.log");

        public static void Line(string text)
        {
            try
            {
                lock (Gate)
                {
                    if (_fresh)
                    {
                        _fresh = false;
                        var info = new FileInfo(FilePath);
                        if (info.Exists && info.Length > 8L * 1024 * 1024)
                        {
                            string prev = Path.Combine(Dir, "bridge_prev.log");
                            if (File.Exists(prev)) File.Delete(prev);
                            File.Move(FilePath, prev);
                        }
                        File.AppendAllText(FilePath, Environment.NewLine + "==== game started "
                            + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====" + Environment.NewLine);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
