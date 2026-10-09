using System.Globalization;
using System.Text;

namespace BannerlordAgents.WarRoom
{
    /// <summary>
    /// The war room's settings, in warroom.txt in the agent's folder (one "key = value" per line; written with the
    /// defaults the first time). The model, how hard it thinks (normal and urgent pokes), how long a call may take,
    /// and where Claude Code is.
    /// </summary>
    internal sealed class Config
    {
        public string AgentDir = @"C:\Users\karab\Projects\orc-war-council";
        /// <summary>The agent channel, a folder inside the agent's: the bridge's agent_dir points here.</summary>
        public string Channel = "warroom";
        public string Model = "claude-opus-5-5";
        /// <summary>Claude Code's effort level for an ordinary poke, and for an urgent one (low, medium, high, xhigh, max).</summary>
        public string Effort = "high";
        public string UrgentEffort = "high";
        /// <summary>
        /// The most it may think, in tokens, for an ordinary poke and an urgent one (Claude Code's MAX_THINKING_TOKENS).
        /// Its answer time is almost exactly its thinking time, so this is the main dial for speed. 0: no cap.
        /// </summary>
        public int ThinkingCap = 3000;
        public int UrgentThinkingCap = 5000;
        /// <summary>Words in a poke's reasons that make it urgent.</summary>
        public string[] Urgent = { "is besieged", "laid siege", "passed from", "declared war", "Enemy army in view", "was taken prisoner", "army has formed", "fell", "striking the besiegers" };
        public int TimeoutSeconds = 240;
        /// <summary>Empty: the newest Claude Code that VS Code's extension carries, else "claude" on the path.</summary>
        public string ClaudePath = "";
        public int MaxLessons = 12;
        /// <summary>Let the agent read its look-up files (lookups\&lt;id&gt;.txt) during a call.</summary>
        public bool Lookups = true;

        public string ChannelDir => Path.Combine(AgentDir, Channel);
        public string FilePath => Path.Combine(AgentDir, "warroom.txt");
        public string Journal => Path.Combine(AgentDir, "journal.md");
        public string SystemTemplate => Path.Combine(AgentDir, "warroom-system.md");

        public static Config Load(string agentDir)
        {
            var c = new Config();
            if (!string.IsNullOrWhiteSpace(agentDir)) c.AgentDir = agentDir;
            if (!File.Exists(c.FilePath)) { c.Save(); return c; }
            foreach (string raw in File.ReadAllLines(c.FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "channel": if (v.Length > 0) c.Channel = v; break;
                    case "model": if (v.Length > 0) c.Model = v; break;
                    case "effort": if (v.Length > 0) c.Effort = v; break;
                    case "urgent_effort": if (v.Length > 0) c.UrgentEffort = v; break;
                    case "thinking_cap": if (int.TryParse(v, out int tc) && tc >= 0) c.ThinkingCap = tc; break;
                    case "urgent_thinking_cap": if (int.TryParse(v, out int utc) && utc >= 0) c.UrgentThinkingCap = utc; break;
                    case "urgent": if (v.Length > 0) c.Urgent = v.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); break;
                    case "timeout_seconds": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) && t > 10) c.TimeoutSeconds = t; break;
                    case "claude_path": c.ClaudePath = v; break;
                    case "max_lessons": if (int.TryParse(v, out int m) && m >= 0) c.MaxLessons = m; break;
                    case "lookups": c.Lookups = v == "on" || v == "true" || v == "yes"; break;
                }
            }
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(AgentDir);
            var sb = new StringBuilder();
            sb.AppendLine("# The war room's settings (scripts\\warroom.ps1 starts it). One \"key = value\" per line.");
            sb.AppendLine("channel = " + Channel);
            sb.AppendLine("model = " + Model);
            sb.AppendLine("# effort: low, medium, high, xhigh or max; urgent pokes (any of the words below in their reasons) get urgent_effort");
            sb.AppendLine("effort = " + Effort);
            sb.AppendLine("urgent_effort = " + UrgentEffort);
            sb.AppendLine("urgent = " + string.Join(" | ", Urgent));
            sb.AppendLine("# the most it may think, in tokens (0: no cap); its answer time follows its thinking almost exactly");
            sb.AppendLine("thinking_cap = " + ThinkingCap.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("urgent_thinking_cap = " + UrgentThinkingCap.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("timeout_seconds = " + TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# empty: the newest Claude Code in VS Code's extension folder, else claude on the path");
            sb.AppendLine("claude_path = " + ClaudePath);
            sb.AppendLine("max_lessons = " + MaxLessons.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("lookups = " + (Lookups ? "on" : "off"));
            File.WriteAllText(FilePath, sb.ToString());
        }
    }
}
