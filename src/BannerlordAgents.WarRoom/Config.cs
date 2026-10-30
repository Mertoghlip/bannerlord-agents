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
        /// <summary>Where this war room writes its logs (empty: %LOCALAPPDATA%\BannerlordAgents\logs\warroom). A second agent's war room keeps its own (since 0.8).</summary>
        public string LogDir = "";
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
        /// <summary>Lessons kept in the notes; past this, the chronicler merges them.</summary>
        public int MaxLessons = 15;
        /// <summary>Let the agent read its look-up files (lookups\&lt;id&gt;.txt) and its chronicle during a call.</summary>
        public bool Lookups = true;
        /// <summary>Offer the agent the "ask the way" tool in its answers and councils (0.7, docs/ROUTES.md), once the walking map is copied.</summary>
        public bool RouteTool = true;
        /// <summary>
        /// How the realm remembers (docs/MEMORY.md): "written", a fresh call each poke with the story so far, the notes and
        /// the chronicle; or "conversation", one long conversation resumed each poke, for comparison.
        /// </summary>
        public string MemoryMode = "written";
        /// <summary>New chronicle lines before the chronicler rewrites the story so far, in a quiet moment.</summary>
        public int StoryEvery = 12;
        public int StoryWords = 450;
        /// <summary>The chronicler's model (empty: the agent's), effort and thinking cap.</summary>
        public string StoryModel = "";
        public string StoryEffort = "medium";
        public int StoryThinkingCap = 2000;
        /// <summary>In the one-long-conversation mode: past this many tokens, the next poke starts a new conversation from the notes.</summary>
        public int ConversationLimit = 180000;
        /// <summary>
        /// The council of war (docs/WARROOM.md): a slower call on its own thread that sees the whole realm (council.txt) and
        /// may reorder any lord. It sits after the first look, after the big events below, and at least every
        /// council_every_hours in-game hours, never sooner than council_gap_hours after the last one (the first look excepted).
        /// The fast answers go on meanwhile; one of them that orders a lord first wins over the council's order for that lord.
        /// </summary>
        public bool Council = true;
        public double CouncilEveryHours = 24;
        public double CouncilGapHours = 6;
        public string[] CouncilAfter = { "First look since the campaign was loaded", "passed from", "declared war on", "made peace", "is besieged by", "army has formed", "offers peace", "offers an alliance", "offers a call to war" };
        public string CouncilEffort = "high";
        public int CouncilThinkingCap = 12000;
        public int CouncilTimeoutSeconds = 420;
        /// <summary>
        /// How each call asks for the model's thinking (0.13, the training record): "summarized" keeps the API's summary of it in the
        /// call's raw.jsonl and thinking.txt; "omitted" leaves it out; empty asks nothing (Claude Code's own default).
        /// </summary>
        public string ThinkingDisplay = "summarized";

        /// <summary>What the folder's agent is (its agent.txt's kind, since 0.10): "realm" answers pokes; "lieutenant" answers the player's chat (LieutenantRoom).</summary>
        public string Kind = "realm";
        public bool IsLieutenant => Kind == "lieutenant";

        public string ChannelDir => Path.Combine(AgentDir, Channel);
        public string FilePath => Path.Combine(AgentDir, "warroom.txt");
        /// <summary>The single journal from before memory was kept per campaign; it becomes the first campaign's notes.</summary>
        public string Journal => Path.Combine(AgentDir, "journal.md");
        public string MemoryDir => Path.Combine(AgentDir, "memory");
        public string SystemTemplate => Path.Combine(AgentDir, "warroom-system.md");
        public string ChroniclerTemplate => Path.Combine(AgentDir, "chronicler-system.md");
        public bool Conversation => string.Equals(MemoryMode, "conversation", StringComparison.OrdinalIgnoreCase);

        public static Config Load(string agentDir)
        {
            var c = new Config();
            if (!string.IsNullOrWhiteSpace(agentDir)) c.AgentDir = agentDir;
            // the kind of agent, from the folder's agent.txt (the bridge's file; the war room only reads it)
            string agentTxt = Path.Combine(c.AgentDir, "agent.txt");
            if (File.Exists(agentTxt))
                foreach (string raw in File.ReadAllLines(agentTxt))
                {
                    string l = raw.Trim();
                    int e = l.IndexOf('=');
                    if (e > 0 && !l.StartsWith("#") && l.Substring(0, e).Trim().Equals("kind", StringComparison.OrdinalIgnoreCase))
                        c.Kind = l.Substring(e + 1).Trim().Equals("lieutenant", StringComparison.OrdinalIgnoreCase) ? "lieutenant" : "realm";
                }
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
                    case "log_dir": c.LogDir = v; break;
                    case "model": if (v.Length > 0) c.Model = v; break;
                    case "effort": if (v.Length > 0) c.Effort = v; break;
                    case "urgent_effort": if (v.Length > 0) c.UrgentEffort = v; break;
                    case "thinking_cap": if (int.TryParse(v, out int tc) && tc >= 0) c.ThinkingCap = tc; break;
                    case "urgent_thinking_cap": if (int.TryParse(v, out int utc) && utc >= 0) c.UrgentThinkingCap = utc; break;
                    case "urgent": if (v.Length > 0) c.Urgent = v.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); break;
                    case "timeout_seconds": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) && (t > 10 || t == 0)) c.TimeoutSeconds = t; break;   // 0: no limit (0.13.5)
                    case "claude_path": c.ClaudePath = v; break;
                    case "max_lessons": if (int.TryParse(v, out int m) && m >= 0) c.MaxLessons = m; break;
                    case "lookups": c.Lookups = v == "on" || v == "true" || v == "yes"; break;
                    case "route_tool": c.RouteTool = v == "on" || v == "true" || v == "yes"; break;
                    case "memory": if (v == "written" || v == "conversation") c.MemoryMode = v; break;
                    case "story_every": if (int.TryParse(v, out int se) && se > 0) c.StoryEvery = se; break;
                    case "story_words": if (int.TryParse(v, out int sw) && sw >= 100) c.StoryWords = sw; break;
                    case "story_model": c.StoryModel = v; break;
                    case "story_effort": if (v.Length > 0) c.StoryEffort = v; break;
                    case "story_thinking_cap": if (int.TryParse(v, out int stc) && stc >= 0) c.StoryThinkingCap = stc; break;
                    case "conversation_limit": if (int.TryParse(v, out int cl) && cl >= 20000) c.ConversationLimit = cl; break;
                    case "council": c.Council = v == "on" || v == "true" || v == "yes"; break;
                    case "council_every_hours": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double ce) && ce >= 1) c.CouncilEveryHours = ce; break;
                    case "council_gap_hours": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double cg) && cg >= 0) c.CouncilGapHours = cg; break;
                    case "council_after": c.CouncilAfter = v.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(); break;
                    case "council_effort": if (v.Length > 0) c.CouncilEffort = v; break;
                    case "council_thinking_cap": if (int.TryParse(v, out int ctc) && ctc >= 0) c.CouncilThinkingCap = ctc; break;
                    case "council_timeout_seconds": if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ct) && ct > 10) c.CouncilTimeoutSeconds = ct; break;
                    case "thinking_display": c.ThinkingDisplay = v == "summarized" || v == "omitted" ? v : ""; break;
                }
            }
            string text = File.ReadAllText(c.FilePath);
            // a settings file from before memory, the council or the thinking's summary: the new keys are added
            if (!text.Contains("memory =") || !text.Contains("council =") || !text.Contains("route_tool =") || !text.Contains("thinking_display =")) c.Save();
            Claude.ThinkingDisplay = c.ThinkingDisplay;
            return c;
        }

        public void Save()
        {
            Directory.CreateDirectory(AgentDir);
            var sb = new StringBuilder();
            sb.AppendLine("# The war room's settings (scripts\\warroom.ps1 starts it). One \"key = value\" per line.");
            sb.AppendLine("channel = " + Channel);
            sb.AppendLine("# where its logs go (empty: %LOCALAPPDATA%\\BannerlordAgents\\logs\\warroom); a second agent's war room keeps its own");
            sb.AppendLine("log_dir = " + LogDir);
            sb.AppendLine("model = " + Model);
            sb.AppendLine("# effort: low, medium, high, xhigh or max; urgent pokes (any of the words below in their reasons) get urgent_effort");
            sb.AppendLine("effort = " + Effort);
            sb.AppendLine("urgent_effort = " + UrgentEffort);
            sb.AppendLine("urgent = " + string.Join(" | ", Urgent));
            sb.AppendLine("# the most it may think, in tokens (0: no cap); its answer time follows its thinking almost exactly");
            sb.AppendLine("thinking_cap = " + ThinkingCap.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("urgent_thinking_cap = " + UrgentThinkingCap.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# the longest a call may take, in seconds (0: no limit; a call that goes silent for " + Claude.StallSeconds + " s is stopped all the same)");
            sb.AppendLine("timeout_seconds = " + TimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# empty: the newest Claude Code in VS Code's extension folder, else claude on the path");
            sb.AppendLine("claude_path = " + ClaudePath);
            sb.AppendLine("max_lessons = " + MaxLessons.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# let the agent read its look-up files and its whole chronicle during a call");
            sb.AppendLine("lookups = " + (Lookups ? "on" : "off"));
            sb.AppendLine("# offer the agent the \"ask the way\" tool (the way an army would march, its time and its crossings) once the walking map is copied");
            sb.AppendLine("route_tool = " + (RouteTool ? "on" : "off"));
            sb.AppendLine("# memory: written (a fresh call each poke, with the story so far, the notes and the chronicle) or conversation (one long conversation, for comparison)");
            sb.AppendLine("memory = " + MemoryMode);
            sb.AppendLine("# the chronicler rewrites the story so far after this many new chronicle lines, in a quiet moment, in at most story_words words");
            sb.AppendLine("story_every = " + StoryEvery.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("story_words = " + StoryWords.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# the chronicler's model (empty: the agent's), effort and thinking cap");
            sb.AppendLine("story_model = " + StoryModel);
            sb.AppendLine("story_effort = " + StoryEffort);
            sb.AppendLine("story_thinking_cap = " + StoryThinkingCap.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# conversation memory only: past this many tokens, the next poke starts a new conversation from the notes");
            sb.AppendLine("conversation_limit = " + ConversationLimit.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# the council of war: a slower call beside the answers that sees the whole realm and may reorder any lord; it sits after the first look,");
            sb.AppendLine("# after any of the council_after words in a poke's reasons, and at least every council_every_hours in-game hours (never within council_gap_hours of the last)");
            sb.AppendLine("council = " + (Council ? "on" : "off"));
            sb.AppendLine("council_every_hours = " + CouncilEveryHours.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("council_gap_hours = " + CouncilGapHours.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("council_after = " + string.Join(" | ", CouncilAfter));
            sb.AppendLine("council_effort = " + CouncilEffort);
            sb.AppendLine("council_thinking_cap = " + CouncilThinkingCap.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("council_timeout_seconds = " + CouncilTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# the model's thinking: summarized (the API's summary of it is kept with each call, for the training record), omitted, or empty (Claude Code's default)");
            sb.AppendLine("thinking_display = " + ThinkingDisplay);
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
