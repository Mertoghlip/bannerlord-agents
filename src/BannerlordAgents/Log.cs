using System;
using System.IO;

namespace BannerlordAgents
{
    /// <summary>
    /// The module's own log, bridge.log, in %LOCALAPPDATA%\BannerlordAgents\logs.
    ///
    /// Never the game's Documents folder: on the project laptop that folder is inside OneDrive, and a file that
    /// changes every few seconds there gets uploaded over and over while the game runs. The log carries across
    /// launches; past 8 MB the old one is kept as bridge_prev.log.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new object();
        private static bool _fresh = true;

        public static string Dir { get; } = MakeDir();
        public static string FilePath => Path.Combine(Dir, "bridge.log");

        private static string MakeDir()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
            string dir = Path.Combine(root, "BannerlordAgents", "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

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
