using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BannerlordAgents
{
    /// <summary>
    /// settings.txt in %LOCALAPPDATA%\BannerlordAgents: "key = value" lines, with # starting a comment. The console
    /// commands change these and save the file, so a choice survives a restart.
    /// </summary>
    internal static class Settings
    {
        /// <summary>
        /// The realms under watch: kingdom ids or names, comma-separated (since 0.8: more than one realm can have its own agent,
        /// each with its own channel folder). The first is the main one: the console's views and the sight check are its.
        /// </summary>
        public static string Watch = "mistymountainorcs";
        /// <summary>
        /// The agents' folders (since 0.9), each with its own agent.txt (realm, mode, lords, channel): "agents = &lt;folder&gt;; &lt;folder&gt;".
        /// When set, the agents are these, and watch, agent, agent_lords and agent_dir below are not used (Agents.Configured).
        /// </summary>
        public static readonly List<string> AgentFolders = new List<string>();
        /// <summary>Channel folders for the realms after the first ("agent_dir.&lt;realm id&gt; = path"); the first uses agent_dir.</summary>
        public static readonly Dictionary<string, string> AgentDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The watched realms as written, in order.</summary>
        public static List<string> WatchList()
        {
            var list = new List<string>();
            foreach (string w in (Watch ?? "").Split(','))
                if (w.Trim().Length > 0 && !list.Contains(w.Trim(), StringComparer.OrdinalIgnoreCase)) list.Add(w.Trim());
            return list;
        }

        /// <summary>A realm's channel folder: agent_dir for the first realm, agent_dir.&lt;realm&gt; (or agent_&lt;realm&gt; beside the settings) for the others.</summary>
        public static string AgentFolderFor(string realm, bool first)
        {
            if (first || string.IsNullOrEmpty(realm)) return AgentFolder;
            return AgentDirs.TryGetValue(realm, out string d) && !string.IsNullOrWhiteSpace(d) ? d.Trim() : Path.Combine(Paths.Root, "agent_" + realm);
        }
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
        /// <summary>
        /// Enemy lords seen further than this (map units) from all of the realm's holdings poke only when they head for
        /// one of them or chase one of its parties. Armies always poke.
        /// </summary>
        public static float SightingDistance = 30f;

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

        // ---- the dashboard (0.7, docs/DASHBOARD.md) ----

        /// <summary>
        /// The live map: the walking map exported once per map, each lord's planned way in the hourly copy, and every hour a
        /// picture of the world and of what each realm sees, for the dashboard and the agent's "ask the way" tool.
        /// </summary>
        public static bool Dashboard = true;
        /// <summary>The lieutenant (0.10): the game's clock stops while the player types to it in the campaign map's chat box, and runs again when the box closes.</summary>
        public static bool LieutenantPause = true;
        /// <summary>How many loads of a campaign keep their hourly pictures (for rewinding on the dashboard).</summary>
        public static int KeepLoads = 12;
        /// <summary>
        /// Test runs (0.11, docs/RUNS.md): the save a run starts from, how many in-game days it lasts, the rival realms it keeps
        /// numbers for, and the kingdom whose commander is tested (empty: the player's realm when the run's save is loaded).
        /// </summary>
        public static string RunSave = "TEST START";
        public static int RunDays = 60;
        public static List<string> RunRivals = new List<string> { "mistymountainorcs", "dolguldur" };
        public static string RunKingdom = "";

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
                    if (key.StartsWith("agent_dir.", StringComparison.Ordinal) && key.Length > 10) { AgentDirs[key.Substring(10)] = value; continue; }
                    if (key == "agents") { AgentFolders.Clear(); AgentFolders.AddRange(value.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0)); continue; }
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
                        case "sighting_distance":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float sd) && sd > 0) SightingDistance = sd;
                            else Unknown.Add(line);
                            break;
                        case "detail": Detail = DetailLevel(value); break;
                        case "witness": Witness = On(value, Witness); break;
                        case "ai_reasons": AiReasons = On(value, AiReasons); break;
                        case "screen_alarms": ScreenAlarms = On(value, ScreenAlarms); break;
                        case "answer_alarm_minutes":
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && m > 0) AnswerAlarmMinutes = m;
                            else Unknown.Add(line);
                            break;
                        case "freeze_probe": FreezeProbe = On(value, FreezeProbe); break;
                        case "dashboard": Dashboard = On(value, Dashboard); break;
                        case "lieutenant_pause": LieutenantPause = On(value, LieutenantPause); break;
                        case "keep_loads":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int kl) && kl >= 1) KeepLoads = kl;
                            else Unknown.Add(line);
                            break;
                        case "run_save": if (value.Length > 0) RunSave = value; break;
                        case "run_days":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rd) && rd >= 1) RunDays = rd;
                            else Unknown.Add(line);
                            break;
                        case "run_rivals": RunRivals = value.Split(';', ',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); break;
                        case "run_kingdom": RunKingdom = value; break;
                        default: Unknown.Add(line); break;
                    }
                }
                // a file from an older version gets the new keys written in, so they can be found and changed
                foreach (string k in new[] { "sighting_distance", "detail", "witness", "ai_reasons", "screen_alarms", "answer_alarm_minutes", "freeze_probe", "dashboard", "keep_loads", "lieutenant_pause",
                                             "run_save", "run_days", "run_rivals", "run_kingdom" })
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
                sb.AppendLine("# the agents: their folders, each with its own agent.txt (realm, mode, lords); when set, watch, agent, agent_lords and agent_dir are not used");
                sb.AppendLine("agents = " + string.Join("; ", AgentFolders));
                sb.AppendLine("watch = " + Watch);
                sb.AppendLine("bridge = " + (Bridge ? "on" : "off"));
                sb.AppendLine("record = " + (Record ? "on" : "off"));
                sb.AppendLine("perf = " + (Perf ? "on" : "off"));
                sb.AppendLine("notify = " + (Notify ? "on" : "off"));
                sb.AppendLine("# agent channel: off, shadow (orders checked, not carried out) or live; agent_lords = all or hero ids/names");
                sb.AppendLine("agent = " + Agent);
                sb.AppendLine("agent_lords = " + AgentLords);
                sb.AppendLine("agent_dir = " + AgentDir);
                foreach (KeyValuePair<string, string> kv in AgentDirs) sb.AppendLine("agent_dir." + kv.Key + " = " + kv.Value);
                sb.AppendLine("# pokes: enemy lords further than this from all the realm's holdings poke only when heading for one or chasing its parties");
                sb.AppendLine("sighting_distance = " + SightingDistance.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("# logs (docs/LOGGING.md): detail = full or everyday; witness and ai_reasons are the two game hooks");
                sb.AppendLine("detail = " + Detail);
                sb.AppendLine("witness = " + (Witness ? "on" : "off"));
                sb.AppendLine("ai_reasons = " + (AiReasons ? "on" : "off"));
                sb.AppendLine("screen_alarms = " + (ScreenAlarms ? "on" : "off"));
                sb.AppendLine("answer_alarm_minutes = " + AnswerAlarmMinutes.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("freeze_probe = " + (FreezeProbe ? "on" : "off"));
                sb.AppendLine("# the dashboard (docs/DASHBOARD.md): the walking map, planned ways and the hourly pictures; keep_loads = loads of a campaign kept for rewinding");
                sb.AppendLine("dashboard = " + (Dashboard ? "on" : "off"));
                sb.AppendLine("keep_loads = " + KeepLoads.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("# the lieutenant (docs/LIEUTENANT.md): the game's clock stops while you type to it in the campaign map's chat box");
                sb.AppendLine("lieutenant_pause = " + (LieutenantPause ? "on" : "off"));
                sb.AppendLine("# test runs (docs/RUNS.md): the save a run starts from, its length in in-game days, the rival realms it keeps numbers for, and the kingdom tested (empty: yours at the save)");
                sb.AppendLine("run_save = " + RunSave);
                sb.AppendLine("run_days = " + RunDays.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("run_rivals = " + string.Join("; ", RunRivals));
                sb.AppendLine("run_kingdom = " + RunKingdom);
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
