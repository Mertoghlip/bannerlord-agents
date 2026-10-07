using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BannerlordAgents
{
    /// <summary>
    /// settings.txt in %LOCALAPPDATA%\BannerlordAgents: "key = value" lines, with # starting a comment. The console
    /// commands change these and save the file, so a choice survives a restart.
    /// </summary>
    internal static class Settings
    {
        /// <summary>The realm under watch: a kingdom id or name.</summary>
        public static string Watch = "mistymountainorcs";
        /// <summary>All of the bridge's work: views, pokes, the recorder and the order checks.</summary>
        public static bool Bridge = true;
        /// <summary>The recorder of the player's own decisions.</summary>
        public static bool Record = true;
        /// <summary>The once-a-second performance log.</summary>
        public static bool Perf = true;
        /// <summary>On-screen messages when the watched realm is poked or a hand order changes.</summary>
        public static bool Notify = true;

        private static string FilePath => Path.Combine(Paths.Root, "settings.txt");

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { Save(); return; }
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "watch": if (value.Length > 0) Watch = value; break;
                        case "bridge": Bridge = On(value, Bridge); break;
                        case "record": Record = On(value, Record); break;
                        case "perf": Perf = On(value, Perf); break;
                        case "notify": Notify = On(value, Notify); break;
                    }
                }
            }
            catch (Exception e) { Log.Line("settings could not be read, defaults used: " + e.Message); }
        }

        public static void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Bannerlord Agents settings. One \"key = value\" per line; the console commands change these.");
                sb.AppendLine("watch = " + Watch);
                sb.AppendLine("bridge = " + (Bridge ? "on" : "off"));
                sb.AppendLine("record = " + (Record ? "on" : "off"));
                sb.AppendLine("perf = " + (Perf ? "on" : "off"));
                sb.AppendLine("notify = " + (Notify ? "on" : "off"));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Log.Line("settings could not be saved: " + e.Message); }
        }

        public static bool On(string value, bool fallback)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "on": case "true": case "yes": case "1": return true;
                case "off": case "false": case "no": case "0": return false;
                default: return fallback;
            }
        }
    }
}
