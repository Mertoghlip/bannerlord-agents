using System;
using System.Collections.Generic;
using System.Globalization;
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

        // ---- the logs (docs/LOGGING.md) ----

        /// <summary>
        /// How much the logs record: "full" (everything; for the first tests) or "everyday", which leaves out the two
        /// pieces that touch the game many times a second: the fast watch on ordered lords and the movement watcher.
        /// </summary>
        public static string Detail = "full";
        /// <summary>The movement watcher: names whatever changes the plan of a lord holding our order (full detail only).</summary>
        public static bool Witness = true;
        /// <summary>Records the game's own AI weighing its options, for the watched realm's lords.</summary>
        public static bool AiReasons = true;
        /// <summary>Serious alarms also appear on screen, at most one a minute.</summary>
        public static bool ScreenAlarms = true;
        /// <summary>Minutes without any answer from the agent, after a poke, before the alarm.</summary>
        public static double AnswerAlarmMinutes = 5;
        /// <summary>When the game's thread stops moving, run the outside probe that says where it is stuck.</summary>
        public static bool FreezeProbe = true;

        /// <summary>Lines of settings.txt that were not understood, for the start-up self-check.</summary>
        public static readonly List<string> Unknown = new List<string>();

        public static bool Full => Detail == "full";

        public static string AgentFolder => string.IsNullOrWhiteSpace(AgentDir) ? Path.Combine(Paths.Root, "agent") : AgentDir.Trim();

        private static string FilePath => Path.Combine(Paths.Root, "settings.txt");

        public static void Load()
        {
            Unknown.Clear();
            try
            {
                if (!File.Exists(FilePath)) { Save(); return; }
                bool missing = false;
                var seen = new HashSet<string>();
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { Unknown.Add(line); continue; }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    seen.Add(key);
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
                        case "detail": Detail = DetailLevel(value); break;
                        case "witness": Witness = On(value, Witness); break;
                        case "ai_reasons": AiReasons = On(value, AiReasons); break;
                        case "screen_alarms": ScreenAlarms = On(value, ScreenAlarms); break;
                        case "answer_alarm_minutes":
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && m > 0) AnswerAlarmMinutes = m;
                            else Unknown.Add(line);
                            break;
                        case "freeze_probe": FreezeProbe = On(value, FreezeProbe); break;
                        default: Unknown.Add(line); break;
                    }
                }
                // a file from an older version gets the new keys written in, so they can be found and changed
                foreach (string k in new[] { "detail", "witness", "ai_reasons", "screen_alarms", "answer_alarm_minutes", "freeze_probe" })
                    if (!seen.Contains(k)) missing = true;
                if (missing) Save();
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
                sb.AppendLine("# logs (docs/LOGGING.md): detail = full or everyday; witness and ai_reasons are the two game hooks");
                sb.AppendLine("detail = " + Detail);
                sb.AppendLine("witness = " + (Witness ? "on" : "off"));
                sb.AppendLine("ai_reasons = " + (AiReasons ? "on" : "off"));
                sb.AppendLine("screen_alarms = " + (ScreenAlarms ? "on" : "off"));
                sb.AppendLine("answer_alarm_minutes = " + AnswerAlarmMinutes.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("freeze_probe = " + (FreezeProbe ? "on" : "off"));
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Log.Line("settings could not be saved: " + e.Message); }
        }

        public static string Mode(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            return v == "shadow" || v == "live" ? v : "off";
        }

        public static string DetailLevel(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            return v == "everyday" ? "everyday" : "full";
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
