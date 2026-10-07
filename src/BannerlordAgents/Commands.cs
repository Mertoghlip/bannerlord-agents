using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>Console commands (open the console with Alt + ~). Every one starts with "agents.".</summary>
    public static class Commands
    {
        private const int Wait = 5000;

        [CommandLineFunctionality.CommandLineArgumentFunction("help", "agents")]
        public static string Help(List<string> args) =>
            "Bannerlord Agents commands:\n" +
            "  agents.status                    what the module is doing, and where its files are\n" +
            "  agents.view                      the watched realm's report as of now (and the file it is in)\n" +
            "  agents.pokes [n]                 the watched realm's last pokes\n" +
            "  agents.vision_check              our sight rule against the game's own, for your party\n" +
            "  agents.record [on|off|show [n]]  the recorder of your own decisions\n" +
            "  agents.perf [on|off]             performance summary; on/off for the once-a-second log\n" +
            "  agents.bridge on|off             all of the bridge's work, for the before/after comparison\n" +
            "  agents.watch <realm>             choose the realm under watch\n" +
            "  agents.notify on|off             on-screen messages for pokes and hand orders\n" +
            "  agents.order <lord> <verb> <target>, agents.orders, agents.release <lord>|all";

        [CommandLineFunctionality.CommandLineArgumentFunction("status", "agents")]
        public static string Status(List<string> args)
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
                lines.Add("  hand orders active: " + Orders.Count);
                lines.Add("  hourly copy on the game's thread: last " + Perf.CaptureLastMs.ToString("0.00", Text.Inv) + " ms, average "
                          + avg.ToString("0.00", Text.Inv) + " ms, max " + Perf.CaptureMaxMs.ToString("0.00", Text.Inv) + " ms over " + Perf.Captures + " hours");
                lines.Add("  files: " + Paths.Data(Bridge.CampaignId));
            }
            double eff = Background.EfficientShare;
            lines.Add("  worker: " + Background.Placement + "; jobs " + Background.Ran + ", queued " + Background.Queued + ", dropped " + Background.Dropped
                      + ", failed " + Background.Failed + (double.IsNaN(eff) ? "" : "; ran on efficiency cores " + (eff * 100).ToString("0", Text.Inv) + "% of the time"));
            lines.Add("  processor: " + Cpu.Description);
            lines.Add("  log: " + Log.FilePath);
            return string.Join("\n", lines);
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("view", "agents")]
        public static string View(List<string> args)
        {
            if (Campaign.Current == null || !Bridge.Running) return "Load a campaign first.";
            if (Bridge.Watched == null) return "No realm is under watch. Choose one with agents.watch.";
            Snapshot snap = Capture.Take(Bridge.Watched, true);
            return Background.Ask(() => Brain.ReportNow(snap), Wait, out string result) ? result : "The worker did not answer within 5 seconds; try again.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("pokes", "agents")]
        public static string Pokes(List<string> args)
        {
            int n = args != null && args.Count > 0 && int.TryParse(args[0], out int x) ? x : 10;
            return Background.Ask(() => Brain.ShowPokes(n), Wait, out string result) ? result : "The worker did not answer within 5 seconds.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("vision_check", "agents")]
        public static string VisionCheck(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            return Vision.CheckPlayer();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("record", "agents")]
        public static string Record(List<string> args)
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
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("perf", "agents")]
        public static string PerfCmd(List<string> args)
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
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("bridge", "agents")]
        public static string BridgeCmd(List<string> args)
        {
            if (args == null || args.Count == 0) return "The bridge is " + (Settings.Bridge ? "on" : "off") + ". Use agents.bridge on or agents.bridge off.";
            Settings.Bridge = Settings.On(args[0], Settings.Bridge);
            Settings.Save();
            return "Bridge " + (Settings.Bridge ? "on" : "off") + ". (Hand orders keep holding either way; release them with agents.release all.)";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("notify", "agents")]
        public static string Notify(List<string> args)
        {
            if (args == null || args.Count == 0) return "On-screen messages are " + (Settings.Notify ? "on" : "off") + ".";
            Settings.Notify = Settings.On(args[0], Settings.Notify);
            Settings.Save();
            return "On-screen messages " + (Settings.Notify ? "on." : "off.");
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("watch", "agents")]
        public static string Watch(List<string> args)
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
            return "Now watching " + Bridge.WatchedSummary();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("order", "agents")]
        public static string Order(List<string> args) => Orders.Give(args);

        [CommandLineFunctionality.CommandLineArgumentFunction("orders", "agents")]
        public static string OrdersList(List<string> args) => Orders.List();

        [CommandLineFunctionality.CommandLineArgumentFunction("release", "agents")]
        public static string Release(List<string> args) => Orders.Release(args);
    }
}
