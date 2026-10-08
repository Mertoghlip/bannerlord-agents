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
        /// <summary>The agent channel: off, shadow (orders checked and logged, not carried out) or live.</summary>
        public static string Agent = "off";
        /// <summary>In live mode, which of the watched realm's lords obey the agent: "all", or hero ids/names, comma-separated.</summary>
        public static string AgentLords = "all";
        /// <summary>Where the agent channel lives; empty means %LOCALAPPDATA%\BannerlordAgents\agent.</summary>
        public static string AgentDir = "";

        public static string AgentFolder => string.IsNullOrWhiteSpace(AgentDir) ? Path.Combine(Paths.Root, "agent") : AgentDir.Trim();

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
                        case "agent": Agent = Mode(value); break;
                        case "agent_lords": AgentLords = value.Length > 0 ? value : "all"; break;
                        case "agent_dir": AgentDir = value; break;
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
                sb.AppendLine("# agent channel: off, shadow (orders checked, not carried out) or live; agent_lords = all or hero ids/names");
                sb.AppendLine("agent = " + Agent);
                sb.AppendLine("agent_lords = " + AgentLords);
                sb.AppendLine("agent_dir = " + AgentDir);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Log.Line("settings could not be saved: " + e.Message); }
        }

        public static string Mode(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            return v == "shadow" || v == "live" ? v : "off";
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
