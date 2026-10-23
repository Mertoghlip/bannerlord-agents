using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BannerlordAgents
{
    /// <summary>
    /// At every campaign start, like the battle watcher's layout check: everything the mod relies on is tried once,
    /// and the log says "all passed" or names what failed. The game's half runs on the game's thread (it touches the
    /// game); the helper's half (the agent's folder, the cores) runs on the background helper.
    /// </summary>
    internal static class SelfCheck
    {
        public static void Run()
        {
            var results = new List<KeyValuePair<string, string>>();

            void Check(string name, Func<string> test)
            {
                string problem;
                try { problem = test(); }
                catch (Exception e)
                {
                    problem = "the check itself failed (" + e.GetType().Name + ": " + e.Message + ")";
                    Diag.Swallowed("self-check: " + name, e);
                }
                results.Add(new KeyValuePair<string, string>(name, problem));
            }

            Check("realm under watch", () =>
            {
                var bad = new List<string>(Bridge.Unusable);
                if (Bridge.Watched == null) return bad.Count > 0 ? "no agent can play: " + string.Join("; ", bad) : "no agent is set up (agents in settings.txt)";
                foreach (Kingdom k in Bridge.Seats)
                    if (!MobileParty.AllLordParties.Any(p => p != null && p.IsActive && p.MapFaction == k)) bad.Add(Names.Faction(k) + " has no lords leading parties");
                return bad.Count > 0 ? string.Join("; ", bad) : null;
            });
            Check("at war with you", () =>
            {
                IFaction mine = Clan.PlayerClan?.MapFaction;
                if (mine == null) return null;
                // your own realm (the lieutenant's, or its agent's) is never at war with you (0.11.1)
                List<string> peace = Bridge.Seats.Where(k => k != mine && !FactionManager.IsAtWarAgainstFaction(k, mine)).Select(Names.Faction).ToList();
                return peace.Count == 0 ? null : string.Join(", ", peace) + (peace.Count == 1 ? " is" : " are") + " at peace with you now";
            });
            Check("calendar", () =>
            {
                CampaignTime now = CampaignTime.Now;
                Calendar.Parts(now.ToHours, out int season, out int day, out long year, out int hour);
                bool same = season == (int)now.GetSeasonOfYear && day == now.GetDayOfSeason && year == now.GetYear && hour == now.GetHourOfDay;
                return same ? null : "our date (" + Calendar.When(now.ToHours) + ") differs from the game's (" + now + ")";
            });
            Check("sight rules", () =>
            {
                MobileParty me = MobileParty.MainParty;
                if (me == null) return "there is no player party";
                float range = me.SeeingRange;
                float ratio = Campaign.Current.Models.MapVisibilityModel.GetPartySpottingRatioForMainPartySeeingRange(me);
                return range > 0 && range <= 100 && ratio > 0 && ratio <= 5 ? null
                    : "odd values from the game: sight range " + range.ToString("0.00", Text.Inv) + ", spotting ratio " + ratio.ToString("0.00", Text.Inv);
            });
            Check("vassal orders reader", () =>
            {
                PacReader.Poll();
                return PacReader.Status.Contains("was not found") || PacReader.Status.Contains("not ready") ? PacReader.Status : null;
            });
            Check("game hooks", () =>
            {
                bool witness = Settings.Witness && Settings.Full, reasons = Settings.AiReasons;
                return (witness && !GameHooks.WitnessInstalled) || (reasons && !GameHooks.ReasonsInstalled) ? GameHooks.Status : null;
            });
            Check("held lords kept held", () => GameHooks.KeepHeldInstalled || !GameHooks.PartyAiControlsLoaded ? null : GameHooks.Status);
            Check("storm rule", () => GameHooks.StormInstalled ? null : GameHooks.Status);
            Check("settings", () => Settings.Unknown.Count == 0 ? null : "lines not understood: " + string.Join(" | ", Settings.Unknown));
            Check("log folder", () => Diag.SessionDir == null ? "there is no session folder, so the detailed logs are off" : null);
            Check("freeze probe", () => !Settings.FreezeProbe || File.Exists(Probe.Exe) ? null : "the probe is not installed (" + Probe.Exe + ")");
            Check("planned ways", () => Settings.Dashboard ? Capture.WayFieldsProblem : null);
            Check("walking map", () =>
            {
                if (!Settings.Dashboard) return null;
                string p = MapExport.Progress ?? "";
                return p.StartsWith("failed", StringComparison.Ordinal) || p.StartsWith("no map", StringComparison.Ordinal) ? p : null;
            });

            int failed = results.Count(r => r.Value != null);
            Log.Line("self-check (game's half): " + (results.Count - failed) + " of " + results.Count + " passed"
                     + (failed > 0 ? "; failed: " + string.Join("; ", results.Where(r => r.Value != null).Select(r => r.Key + ": " + r.Value)) : "."));
            Diag.Event("self_check", (results.Count - failed) + " of " + results.Count, new { results = results.Select(r => new { check = r.Key, problem = r.Value }).ToList() });
            foreach (KeyValuePair<string, string> r in results.Where(r => r.Value != null))
                Diag.Alarm("self_check", "Self-check: " + r.Key + ": " + r.Value + ".", r.Key == "realm under watch" || r.Key == "log folder");

            // every agent's folder whose channel is on (since 0.9 each agent is a folder)
            List<AgentConfig> on = Bridge.Configs.Where(c => c.On).ToList();
            List<string> agentDirs = on.Count > 0 ? on.Select(c => c.ChannelDir).ToList() : null;
            List<string> prompts = on.Where(c => c.Workspace != null).Select(c => Path.Combine(c.Workspace, "warroom-system.md")).ToList();
            Background.Post("self-check (helper's half)", () => HelperHalf(agentDirs, prompts));
        }

        private static void HelperHalf(List<string> agentDirs, List<string> prompts)
        {
            var problems = new List<string>();
            var notes = new List<string>();
            foreach (string p in prompts ?? new List<string>())
                if (!File.Exists(p)) problems.Add("an agent has no instructions: " + p + " is missing (scripts\\setup-agent.ps1 makes it)");
            foreach (string agentDir in agentDirs ?? new List<string>())
            {
                try
                {
                    Directory.CreateDirectory(Path.Combine(agentDir, "orders"));
                    string probe = Path.Combine(agentDir, "orders", ".write-check");
                    File.WriteAllText(probe, "check");
                    File.Delete(probe);
                    notes.Add("the agent's folder is writable");
                    if (agentDir.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                        problems.Add("the agent's folder is inside OneDrive, which uploads every change while the game runs (" + agentDir + ")");
                }
                catch (Exception e) { problems.Add("the agent's folder cannot be written (" + agentDir + "): " + e.Message); }
            }
            if (agentDirs == null) notes.Add("the agent channel is off");
            if (Cpu.Hybrid && Background.Placement.IndexOf("kept to the", StringComparison.Ordinal) < 0)
                problems.Add("the background helper is not kept to the efficiency cores (" + Background.Placement + ")");
            else notes.Add("the background helper runs " + Background.Placement);
            Log.Line("self-check (helper's half): " + (problems.Count == 0 ? "all passed; " + string.Join("; ", notes) + "." : "failed: " + string.Join("; ", problems) + "."));
            Diag.Event("self_check", "helper's half", new { problems, notes });
            foreach (string p in problems) Diag.Alarm("self_check", "Self-check: " + p + ".", p.StartsWith("the agent's folder cannot", StringComparison.Ordinal));
        }
    }
}
