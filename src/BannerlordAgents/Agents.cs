using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents.Tools;

namespace BannerlordAgents
{
    /// <summary>
    /// An agent, as its folder declares it (since 0.9): the folder (outside this repository) holds everything that is the
    /// agent's own - its instructions (warroom-system.md, chronicler-system.md), its war room's settings (warroom.txt), its
    /// memory (memory\), its channel (warroom\) - and agent.txt, which tells the bridge who it is:
    ///   realm = &lt;kingdom id or name&gt;
    ///   mode = off | shadow | live
    ///   lords = all | hero ids or names, comma-separated (who obeys it in live mode)
    ///   channel = warroom (the folder in it the bridge and the war room talk through)
    ///   kind = realm | lieutenant (since 0.10: a lieutenant serves the player's own realm, "realm = player", through the
    ///   campaign map's chat box, and is never poked: it answers the player, and acts only on the player's yes; Lieutenant)
    /// The bridge's settings list the folders (agents = &lt;folder&gt;; &lt;folder&gt;). Without that list, the settings of
    /// 0.8 and before (watch, agent, agent_lords, agent_dir, agent_dir.&lt;realm&gt;) describe the agents.
    /// </summary>
    internal sealed class AgentConfig
    {
        public string Realm, Workspace, ChannelDir, LogDir;
        public string Mode = "shadow", Lords = "all";
        /// <summary>"realm" (an agent that plays a realm), or "lieutenant" (the player's own, since 0.10).</summary>
        public string Kind = "realm";
        public bool IsLieutenant => Kind == "lieutenant";
        /// <summary>
        /// A news wake's report (0.14, docs/PLAN-news.md; agent.txt's "news"): "full", the whole report as before; "theatre", the news, its clock and
        /// what bears on it, the rest in a few lines a region and by asking; "shadow", the whole report sent and the theatre's written beside it.
        /// </summary>
        public string News = "full";
        public bool Theatre => News == "theatre";
        /// <summary>
        /// What wakes the agent (0.15; agent.txt's "wake"): "rule", the wake rule sorts each line first (WakeRules; the default for every agent folder); "all", every line wakes it,
        /// as before; "self" (0.18, continuous agents), the agent's own watches and check-ins, the locked wakes and news of war, peace and offers wake it, and every other line
        /// rides its next wake (WakeRules.SortSelf, Watches). Only a realm's agent that is switched on goes by a rule.
        /// </summary>
        public string Wake = "rule";
        public bool WakeRule => (Wake == "rule" || Wake == "self") && On && !IsLieutenant;
        public bool WakeSelf => Wake == "self" && On && !IsLieutenant;
        /// <summary>What was wrong with the folder, for the self-check (null: nothing).</summary>
        public string Problem;

        public bool On => Mode == "shadow" || Mode == "live";
        public string AgentFile => Workspace != null ? Path.Combine(Workspace, "agent.txt") : null;

        /// <summary>The war room's own logs: log_dir in its warroom.txt, else the shared default.</summary>
        public static string DefaultLogDir => Path.Combine(Paths.Root, "logs", "warroom");

        /// <summary>An agent from its folder's agent.txt.</summary>
        public static AgentConfig Read(string workspace)
        {
            var c = new AgentConfig { Workspace = workspace.Trim() };
            string channel = "warroom";
            try
            {
                if (!File.Exists(c.AgentFile)) c.Problem = "no agent.txt in " + c.Workspace;
                else
                    foreach (KeyValuePair<string, string> kv in Pairs(c.AgentFile))
                        switch (kv.Key)
                        {
                            case "realm": c.Realm = kv.Value; break;
                            case "mode": c.Mode = Settings.Mode(kv.Value); break;
                            case "lords": c.Lords = kv.Value.Length > 0 ? kv.Value : "all"; break;
                            case "channel": if (kv.Value.Length > 0) channel = kv.Value; break;
                            case "kind": c.Kind = kv.Value.Trim().ToLowerInvariant() == "lieutenant" ? "lieutenant" : "realm"; break;
                            case "news": c.News = NewsMode(kv.Value); break;
                            case "wake": c.Wake = WakeMode(kv.Value); break;
                        }
            }
            catch (Exception e) { c.Problem = "agent.txt could not be read: " + e.Message; }
            if (c.Problem == null && string.IsNullOrWhiteSpace(c.Realm)) c.Problem = "agent.txt in " + c.Workspace + " names no realm";
            c.ChannelDir = Path.Combine(c.Workspace, channel);
            c.LogDir = LogDirOf(c.Workspace);
            return c;
        }

        /// <summary>A news wake's report as agent.txt names it: theatre, shadow or full (anything else).</summary>
        public static string NewsMode(string v)
        {
            string m = (v ?? "").Trim().ToLowerInvariant();
            return m == "theatre" || m == "theater" ? "theatre" : m == "shadow" ? "shadow" : "full";
        }

        /// <summary>What wakes the agent as agent.txt names it: all, self, or the wake rule (anything else).</summary>
        public static string WakeMode(string v)
        {
            string m = (v ?? "").Trim().ToLowerInvariant();
            return m == "all" ? "all" : m == "self" ? "self" : "rule";
        }

        /// <summary>An agent described by the settings of 0.8 and before (and the smoke test): a realm and its channel folder.</summary>
        public static AgentConfig FromSettings(string realm, string channelDir, string mode, string lords)
        {
            string workspace = null;
            try { workspace = Path.GetDirectoryName(channelDir.TrimEnd('\\', '/')); } catch { }
            return new AgentConfig
            {
                Realm = realm, ChannelDir = channelDir, Workspace = workspace, Mode = mode, Lords = string.IsNullOrWhiteSpace(lords) ? "all" : lords, Wake = "all",
                LogDir = workspace != null ? LogDirOf(workspace) : DefaultLogDir
            };
        }

        /// <summary>log_dir in the folder's warroom.txt (the war room writes there), else the default.</summary>
        private static string LogDirOf(string workspace)
        {
            try
            {
                string f = Path.Combine(workspace, "warroom.txt");
                if (File.Exists(f))
                    foreach (KeyValuePair<string, string> kv in Pairs(f))
                        if (kv.Key == "log_dir" && kv.Value.Length > 0) return kv.Value;
            }
            catch (Exception e) { Diag.Swallowed("reading an agent's warroom.txt", e); }
            return DefaultLogDir;
        }

        /// <summary>The "key = value" lines of a settings file (# starts a comment).</summary>
        private static IEnumerable<KeyValuePair<string, string>> Pairs(string path)
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                yield return new KeyValuePair<string, string>(line.Substring(0, eq).Trim().ToLowerInvariant(), line.Substring(eq + 1).Trim());
            }
        }

        /// <summary>Writes the folder's agent.txt (the console changes the mode with it).</summary>
        public void Save()
        {
            if (AgentFile == null) return;
            File.WriteAllLines(AgentFile, new[]
            {
                "# This agent: the realm it commands and how. The game's bridge reads this at every load (docs/WARROOM.md).",
                "realm = " + Realm,
                "# off, shadow (orders checked and answered, none carried out) or live",
                "mode = " + Mode,
                "# in live mode, who obeys: all, or hero ids or names, comma-separated",
                "lords = " + Lords,
                "# the folder in this one the bridge and the war room talk through",
                "channel = " + Path.GetFileName(ChannelDir.TrimEnd('\\', '/')),
                "# realm (it plays a realm) or lieutenant (it serves the player's own realm through the campaign map's chat box)",
                "kind = " + Kind,
                "# a news wake's report: full, shadow or theatre (the news, its clock and what bears on it; the rest by asking)",
                "news = " + News,
                "# what wakes the agent: rule (the wake rule sorts each line of news first), all (every line wakes it) or self (its own watches and check-ins, and the locked wakes)",
                "wake = " + Wake
            });
        }
    }

    /// <summary>
    /// One agent in the game (since 0.9): everything the bridge keeps for it, held together - its channel, its chronicle,
    /// its memory of what it saw, its orders in force (the tools its lords hold) and its latest view of the world. Made from
    /// its folder (AgentConfig) at each load. Every agent is made the same way; none is special.
    /// </summary>
    internal sealed class RealmAgent
    {
        public readonly AgentConfig Config;
        public readonly Channel Channel;
        public readonly ChronicleBook Chronicle;
        public readonly Brain.Memory Memory = new Brain.Memory();
        /// <summary>The orders in force: the tools its lords hold, by tool id (the runner keeps them here).</summary>
        public readonly Dictionary<int, ToolInstance> Tools = new Dictionary<int, ToolInstance>();
        /// <summary>The latest hour as this agent's realm has it.</summary>
        public Snapshot Latest;
        /// <summary>Its own watches (0.18, Watches): what wakes it besides the locked wakes, when its folder says wake = self. Worker.</summary>
        public readonly List<Watch> Watches = new List<Watch>();
        /// <summary>The hour of the question book last written on the war room's asking (0.18.1, Brain.FreshBook): asked again within it, the book stands. Worker.</summary>
        public double FreshBookHours = double.NaN;

        public RealmAgent(AgentConfig config)
        {
            Config = config;
            Channel = new Channel(this);
            Chronicle = new ChronicleBook(config.Realm);
        }

        public string Realm => Config.Realm;
        public bool On => Config.On;
    }

    /// <summary>The agents of this load (since 0.9), in the order the settings list them.</summary>
    internal static class Agents
    {
        private static readonly object Gate = new object();
        private static List<RealmAgent> _all = new List<RealmAgent>();
        private static RealmAgent _lone;

        /// <summary>
        /// The agents the settings describe: each folder in "agents", or else, as in 0.8 and before, each realm in "watch"
        /// with agent_dir (agent_dir.&lt;realm&gt;) and the one agent/agent_lords for all. Any thread (reads files only).
        /// </summary>
        public static List<AgentConfig> Configured()
        {
            if (Settings.AgentFolders.Count > 0) return Settings.AgentFolders.Select(AgentConfig.Read).ToList();
            List<string> realms = Settings.WatchList();
            return realms.Select((r, i) => AgentConfig.FromSettings(r, Settings.AgentFolderFor(r, i == 0), Settings.Agent, Settings.AgentLords)).ToList();
        }

        /// <summary>Every agent of this load.</summary>
        public static List<RealmAgent> All { get { lock (Gate) return _all.Count > 0 ? _all.ToList() : new List<RealmAgent> { Lone() }; } }

        /// <summary>The agent of that realm, exactly (null: none).</summary>
        public static RealmAgent Of(string realm)
        {
            if (realm == null) return null;
            lock (Gate) return _all.FirstOrDefault(a => a.Realm == realm);
        }

        /// <summary>
        /// The agent of that realm; for none named (the console, a test) the one listed first. Before a campaign or with none
        /// configured, one agent in agent_dir stands in, so the console and the smoke test always have one.
        /// </summary>
        public static RealmAgent For(string realm)
        {
            lock (Gate)
            {
                if (_all.Count == 0) return Lone();
                return (realm == null ? null : _all.FirstOrDefault(a => a.Realm == realm)) ?? _all[0];
            }
        }

        private static RealmAgent Lone() => _lone ?? (_lone = new RealmAgent(AgentConfig.FromSettings(null, Settings.AgentFolder, Settings.Agent, Settings.AgentLords)));

        /// <summary>The agent serving that realm: its own, or the stand-in agent configured without a realm (the smoke test); null for none.</summary>
        public static RealmAgent Serving(string realm) => Of(realm) ?? All.FirstOrDefault(a => a.Realm == null);

        /// <summary>Is any agent's channel on (shadow or live)?</summary>
        public static bool AnyOn => All.Any(a => a.On);

        /// <summary>
        /// A campaign was loaded (worker): the agents of this load, each new, each forgetting the last session. The ids file
        /// of 0.8 and before (ids_shown.txt) belonged to the one realm then watched, which is listed first: it is renamed for it.
        /// </summary>
        public static void Reset(string dataDir, double loadedAt, List<AgentConfig> configs, Dictionary<string, List<ChronicleEntry>> histories, History history = null)
        {
            var fresh = (configs ?? new List<AgentConfig>()).Where(c => c != null && c.Realm != null).Select(c => new RealmAgent(c)).ToList();
            if (fresh.Count == 0) fresh.Add(new RealmAgent(AgentConfig.FromSettings(null, Settings.AgentFolder, Settings.Agent, Settings.AgentLords)));
            MigrateIds(dataDir, fresh[0].Realm);
            List<RealmAgent> old;
            lock (Gate) { old = _all; _all = fresh; _lone = null; }
            foreach (RealmAgent a in old) { a.Channel.Close(); a.Chronicle.Close(); }
            foreach (RealmAgent a in fresh)
            {
                a.Channel.Reset(dataDir);
                a.Chronicle.Reset(dataDir, loadedAt, a.Realm, histories != null && a.Realm != null && histories.TryGetValue(a.Realm, out List<ChronicleEntry> h) ? h : null, history);
            }
        }

        private static void MigrateIds(string dataDir, string first)
        {
            if (dataDir == null || first == null) return;
            try
            {
                string legacy = Path.Combine(dataDir, "ids_shown.txt"), now = Path.Combine(dataDir, "ids_shown_" + first + ".txt");
                if (File.Exists(legacy) && !File.Exists(now)) File.Move(legacy, now);
            }
            catch (Exception e) { Diag.Swallowed("renaming the ids shown to the agent", e); }
        }
    }
}
