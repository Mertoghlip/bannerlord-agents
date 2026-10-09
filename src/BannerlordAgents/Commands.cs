using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// Console commands (open the console with Alt + ~). Every one starts with "agents.". Each command typed is logged
    /// with the first line of its answer, so the logs show what was asked of the mod and when.
    /// </summary>
    public static class Commands
    {
        private const int Wait = 5000;

        /// <summary>Runs a command, logs it, and turns a failure into an answer instead of a silent nothing.</summary>
        private static string Cmd(string name, List<string> args, Func<string> body)
        {
            string typed = "agents." + name + (args != null && args.Count > 0 ? " " + string.Join(" ", args) : "");
            string answer;
            try { answer = body() ?? ""; }
            catch (Exception e)
            {
                Diag.Error("console command " + name, e);
                answer = "That command failed: " + e.GetType().Name + ": " + e.Message + ". It is in the log.";
            }
            string first = answer.Split('\n')[0];
            Diag.Event("command", typed, new { answer = first.Length > 200 ? first.Substring(0, 200) : first });
            Log.Line("you typed: " + typed + "  ->  " + (first.Length > 160 ? first.Substring(0, 160) + "..." : first));
            return answer;
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("help", "agents")]
        public static string Help(List<string> args) => Cmd("help", args, () =>
            "Bannerlord Agents commands:\n" +
            "  agents.status                    what the module is doing, and where its files are\n" +
            "  agents.health                    alarms so far and the state right now (the logs' own view)\n" +
            "  agents.view                      the watched realm's report as of now (and the file it is in)\n" +
            "  agents.pokes [n]                 the watched realm's last pokes\n" +
            "  agents.chronicle [n]             the last lines of the watched realm's chronicle, its memory of the war\n" +
            "  agents.vision_check              our sight rule against the game's own, for your party\n" +
            "  agents.record [on|off|show [n]]  the recorder of your own decisions\n" +
            "  agents.perf [on|off]             performance summary; on/off for the once-a-second log\n" +
            "  agents.bridge on|off             all of the bridge's work, for the before/after comparison\n" +
            "  agents.watch <realm>             choose the realm under watch\n" +
            "  agents.notify on|off             on-screen messages for pokes and hand orders\n" +
            "  agents.order <lord> <order> <place or lord> [setting=value] [lords=a,b]  the agent's orders and tools, by hand\n" +
            "  agents.tools [name]              every order and tool the agent can give, with its settings\n" +
            "  agents.vassal <lord> <verb> <target> an order for one of your own lords, recorded as your decision\n" +
            "  agents.orders, agents.release <lord>|all\n" +
            "  agents.agent [off|shadow|live [lords]|dir <folder>]  the agent channel (the agent chat's orders)\n" +
            "  agents.detail [full|everyday]    how much the logs record\n" +
            "  agents.hooks [witness|reasons] [on|off]  the two hooks into the game's code (who moved a lord; the AI's reasons)\n" +
            "  agents.alarms [on|off]           serious alarms on screen\n" +
            "  agents.blackbox                  write the black box now (the last 1,000 things the mod saw and did)\n" +
            "  agents.probe                     run the freeze probe once, to check it works");

        [CommandLineFunctionality.CommandLineArgumentFunction("status", "agents")]
        public static string Status(List<string> args) => Cmd("status", args, () =>
        {
            var lines = new List<string> { "Bannerlord Agents " + SubModule.Version };
            if (Campaign.Current == null || !Bridge.Running) lines.Add("  no campaign loaded");
            else
            {
                double avg = Perf.Captures > 0 ? Perf.CaptureSumMs / Perf.Captures : 0;
                lines.Add("  bridge: " + (Settings.Bridge ? "on" : "OFF") + " (views, pokes, recorder and order checks)");
                lines.Add("  watching: " + Bridge.WatchedSummary());
                lines.Add("  recorder: " + (Settings.Record ? "on" : "OFF") + "; this session " + Recorder.Committed + " decisions from "
                          + Recorder.Clicks + " changes of course (" + Recorder.Corrections + " quick corrections merged); written " + Brain.DecisionsWritten);
                lines.Add("  vassal orders: " + PacReader.Status);
                lines.Add("  pokes this session: " + Brain.PokesWatched + " for the watched realm, " + Brain.PokesPlayer + " for you");
                lines.Add("  orders active (hand, agent and yours): " + Orders.Count);
                lines.Add("  agent channel: " + AgentChannel.ModeText() + " Folder: " + Settings.AgentFolder + ". This session: "
                          + AgentChannel.AnswerFiles + " answer files, " + AgentChannel.OrdersReceived + " orders (" + AgentChannel.Applied + " applied, "
                          + AgentChannel.Checked + " checked only, " + AgentChannel.Refused + " refused)");
                lines.Add("  hourly copy on the game's thread: last " + Perf.CaptureLastMs.ToString("0.00", Text.Inv) + " ms, average "
                          + avg.ToString("0.00", Text.Inv) + " ms, max " + Perf.CaptureMaxMs.ToString("0.00", Text.Inv) + " ms over " + Perf.Captures + " hours");
                lines.Add("  files: " + Paths.Data(Bridge.CampaignId));
            }
            double eff = Background.EfficientShare;
            lines.Add("  worker: " + Background.Placement + "; jobs " + Background.Ran + ", queued " + Background.Queued + ", dropped " + Background.Dropped
                      + ", failed " + Background.Failed + (double.IsNaN(eff) ? "" : "; ran on efficiency cores " + (eff * 100).ToString("0", Text.Inv) + "% of the time"));
            lines.Add("  logs: " + Settings.Detail + " detail; game hooks: " + GameHooks.Status + "; lords watched: " + LordWatch.Count);
            lines.Add("  this launch's log folder: " + (Diag.SessionDir ?? "(none)"));
            lines.Add("  processor: " + Cpu.Description);
            lines.Add("  log: " + Log.FilePath);
            return string.Join("\n", lines);
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("health", "agents")]
        public static string Health(List<string> args) => Cmd("health", args, Diag.HealthText);

        [CommandLineFunctionality.CommandLineArgumentFunction("blackbox", "agents")]
        public static string BlackBox(List<string> args) => Cmd("blackbox", args, () =>
            Background.Ask(() => Diag.DumpBlackBox("you asked (agents.blackbox)"), Wait, out string path) && path != null
                ? "Black box written: " + path
                : "The black box could not be written (no log folder this launch, or the helper is busy).");

        [CommandLineFunctionality.CommandLineArgumentFunction("probe", "agents")]
        public static string ProbeCmd(List<string> args) => Cmd("probe", args, () =>
            "Freeze probe: " + Watchdog.RunProbe("you asked (agents.probe), to check it works") + "\nIt takes a few seconds; the file appears in this launch's log folder.");

        [CommandLineFunctionality.CommandLineArgumentFunction("detail", "agents")]
        public static string Detail(List<string> args) => Cmd("detail", args, () =>
        {
            if (args == null || args.Count == 0)
                return "The logs record " + Settings.Detail + " detail. agents.detail full | everyday (everyday leaves out the fast watch on ordered lords and the movement watcher).";
            Settings.Detail = Settings.DetailLevel(args[0]);
            Settings.Save();
            GameHooks.Apply();
            return "The logs now record " + Settings.Detail + " detail. Game hooks: " + GameHooks.Status;
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("hooks", "agents")]
        public static string Hooks(List<string> args) => Cmd("hooks", args, () =>
        {
            string which = args != null && args.Count > 0 ? args[0].ToLowerInvariant() : "";
            string onOff = args != null && args.Count > 1 ? args[1] : (which == "on" || which == "off" ? which : "");
            if (onOff.Length == 0)
                return "Game hooks: " + GameHooks.Status + ". Movement watcher " + (Settings.Witness ? "on" : "off") + " (works in full detail only), AI reasons "
                       + (Settings.AiReasons ? "on" : "off") + ".\nUsage: agents.hooks on|off, or agents.hooks witness|reasons on|off";
            bool on = Settings.On(onOff, true);
            if (which == "witness" || which == "on" || which == "off") Settings.Witness = on;
            if (which == "reasons" || which == "on" || which == "off") Settings.AiReasons = on;
            Settings.Save();
            GameHooks.Apply();
            return "Movement watcher " + (Settings.Witness ? "on" : "off") + ", AI reasons " + (Settings.AiReasons ? "on" : "off") + ". Game hooks: " + GameHooks.Status
                   + (on ? "" : "\n(Hooks already attached stay attached until the game restarts, but they record nothing while off.)");
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("alarms", "agents")]
        public static string Alarms(List<string> args) => Cmd("alarms", args, () =>
        {
            if (args == null || args.Count == 0) return "Serious alarms on screen: " + (Settings.ScreenAlarms ? "on" : "off") + ". agents.health lists them all.";
            Settings.ScreenAlarms = Settings.On(args[0], Settings.ScreenAlarms);
            Settings.Save();
            return "Serious alarms on screen: " + (Settings.ScreenAlarms ? "on." : "off.");
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("view", "agents")]
        public static string View(List<string> args) => Cmd("view", args, () =>
        {
            if (Campaign.Current == null || !Bridge.Running) return "Load a campaign first.";
            if (Bridge.Watched == null) return "No realm is under watch. Choose one with agents.watch.";
            Snapshot snap = Capture.Take(Bridge.Watched, true);
            return Background.Ask(() => Brain.ReportNow(snap), Wait, out string result) ? result : "The worker did not answer within 5 seconds; try again.";
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("pokes", "agents")]
        public static string Pokes(List<string> args) => Cmd("pokes", args, () =>
        {
            int n = args != null && args.Count > 0 && int.TryParse(args[0], out int x) ? x : 10;
            return Background.Ask(() => Brain.ShowPokes(n), Wait, out string result) ? result : "The worker did not answer within 5 seconds.";
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("chronicle", "agents")]
        public static string ChronicleLines(List<string> args) => Cmd("chronicle", args, () =>
        {
            int n = args != null && args.Count > 0 && int.TryParse(args[0], out int x) ? x : 15;
            return Background.Ask(() => Chronicle.Last(n), Wait, out string result) ? result : "The worker did not answer within 5 seconds.";
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("vision_check", "agents")]
        public static string VisionCheck(List<string> args) => Cmd("vision_check", args, () => Campaign.Current == null ? "Load a campaign first." : Vision.CheckPlayer());

        [CommandLineFunctionality.CommandLineArgumentFunction("record", "agents")]
        public static string Record(List<string> args) => Cmd("record", args, () =>
        {
            string what = args != null && args.Count > 0 ? args[0].ToLowerInvariant() : "status";
            if (what == "on" || what == "off")
            {
                Settings.Record = what == "on";
                Settings.Save();
                return "Recorder " + (Settings.Record ? "on." : "off.");
            }
            if (what == "show")
            {
                int n = args.Count > 1 && int.TryParse(args[1], out int x) ? x : 10;
                return Background.Ask(() => Brain.ShowDecisions(n), Wait, out string result) ? result : "The worker did not answer within 5 seconds.";
            }
            return "Recorder " + (Settings.Record ? "on" : "OFF") + ". This session: " + Recorder.Committed + " decisions from " + Recorder.Clicks
                   + " changes of course; vassal orders: " + PacReader.Status + ". Files: " + System.IO.Path.Combine(Paths.Data(Bridge.CampaignId), "record");
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("perf", "agents")]
        public static string PerfCmd(List<string> args) => Cmd("perf", args, () =>
        {
            if (args != null && args.Count > 0 && (args[0] == "on" || args[0] == "off"))
            {
                Settings.Perf = args[0] == "on";
                Settings.Save();
                return "Performance log " + args[0] + ".";
            }
            string summary = Background.Ask(PerfLog.Summary, Wait, out string s) ? s : "  (the worker did not answer)";
            double avg = Perf.Captures > 0 ? Perf.CaptureSumMs / Perf.Captures : 0;
            double eff = Background.EfficientShare;
            return "Performance, last 15 minutes of campaign map:\n" + summary + "\n"
                   + "  hourly copy: average " + avg.ToString("0.00", Text.Inv) + " ms, max " + Perf.CaptureMaxMs.ToString("0.00", Text.Inv) + " ms\n"
                   + "  worker on efficiency cores: " + (double.IsNaN(eff) ? "n/a" : (eff * 100).ToString("0", Text.Inv) + "%") + " (" + Background.Placement + ")\n"
                   + "  the full log is in " + System.IO.Path.Combine(Paths.Root, "perf");
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("bridge", "agents")]
        public static string BridgeCmd(List<string> args) => Cmd("bridge", args, () =>
        {
            if (args == null || args.Count == 0) return "The bridge is " + (Settings.Bridge ? "on" : "off") + ". Use agents.bridge on or agents.bridge off.";
            Settings.Bridge = Settings.On(args[0], Settings.Bridge);
            Settings.Save();
            return "Bridge " + (Settings.Bridge ? "on" : "off") + ". (Hand orders keep holding either way; release them with agents.release all.)";
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("notify", "agents")]
        public static string Notify(List<string> args) => Cmd("notify", args, () =>
        {
            if (args == null || args.Count == 0) return "On-screen messages are " + (Settings.Notify ? "on" : "off") + ".";
            Settings.Notify = Settings.On(args[0], Settings.Notify);
            Settings.Save();
            return "On-screen messages " + (Settings.Notify ? "on." : "off.");
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("watch", "agents")]
        public static string Watch(List<string> args) => Cmd("watch", args, () =>
        {
            if (args == null || args.Count == 0) return "Watching: " + (Bridge.Running ? Bridge.WatchedSummary() : Settings.Watch);
            string want = string.Join(" ", args);
            if (Campaign.Current == null) { Settings.Watch = want; Settings.Save(); return "Will watch '" + want + "' once a campaign is loaded."; }
            Kingdom k = Bridge.Resolve(want);
            if (k == null) return "No realm matches '" + want + "'. Realms: " + string.Join(", ", Kingdom.All.Where(x => !x.IsEliminated).Select(x => Names.Faction(x)));
            if (Orders.Count > 0) Orders.Release(new List<string> { "all" });
            Bridge.Watched = k;
            Settings.Watch = k.StringId;
            Settings.Save();
            LordWatch.Refresh();
            return "Now watching " + Bridge.WatchedSummary();
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("order", "agents")]
        public static string Order(List<string> args) => Cmd("order", args, () => Orders.Give(args));

        [CommandLineFunctionality.CommandLineArgumentFunction("tools", "agents")]
        public static string ToolsCmd(List<string> args) => Cmd("tools", args, () =>
        {
            List<string> problems = Tools.Library.Problems();
            string head = problems.Count == 0 ? "The tool library: " + Tools.Library.All.Count + " orders, all checked.\n"
                                              : "The tool library has " + problems.Count + " problems: " + string.Join("; ", problems) + "\n";
            if (args != null && args.Count > 0)
            {
                Tools.ToolDef d = Tools.Library.Get(args[0]);
                return d == null ? "No order called '" + args[0] + "'. agents.tools lists them all." : head + d.Describe();
            }
            return head + Tools.Library.Text();
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("vassal", "agents")]
        public static string Vassal(List<string> args) => Cmd("vassal", args, () => Orders.GiveVassal(args));

        [CommandLineFunctionality.CommandLineArgumentFunction("agent", "agents")]
        public static string Agent(List<string> args) => Cmd("agent", args, () =>
        {
            string what = args != null && args.Count > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "off":
                case "shadow":
                    Settings.Agent = what;
                    break;
                case "live":
                    Settings.Agent = "live";
                    Settings.AgentLords = args.Count > 1 ? string.Join(" ", args.Skip(1)) : "all";
                    break;
                case "dir":
                    if (args.Count < 2) return "Agent channel folder: " + Settings.AgentFolder;
                    Settings.AgentDir = string.Join(" ", args.Skip(1));
                    break;
                case "":
                    return "Agent channel: " + AgentChannel.ModeText() + "\n  folder: " + Settings.AgentFolder
                           + "\n  usage: agents.agent off | shadow | live [lord, lord, ...] | dir <folder>";
                default:
                    return "Usage: agents.agent off | shadow | live [lord, lord, ...] | dir <folder>";
            }
            Settings.Save();
            Log.Line("agent channel set: " + Settings.Agent + ", lords " + Settings.AgentLords + ", folder " + Settings.AgentFolder);
            AgentChannel.RequestScan();
            return "Agent channel: " + AgentChannel.ModeText() + "\n  folder: " + Settings.AgentFolder;
        });

        [CommandLineFunctionality.CommandLineArgumentFunction("orders", "agents")]
        public static string OrdersList(List<string> args) => Cmd("orders", args, Orders.List);

        [CommandLineFunctionality.CommandLineArgumentFunction("release", "agents")]
        public static string Release(List<string> args) => Cmd("release", args, () => Orders.Release(args));
    }
}
