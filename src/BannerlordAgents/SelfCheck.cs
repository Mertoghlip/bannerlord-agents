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

            Check("realm under watch", () => Bridge.Watched == null ? "no realm matches '" + Settings.Watch + "'"
                : MobileParty.AllLordParties.Any(p => p != null && p.IsActive && p.MapFaction == Bridge.Watched) ? null : "it has no lords leading parties");
            Check("at war with you", () =>
            {
                IFaction mine = Clan.PlayerClan?.MapFaction;
                if (Bridge.Watched == null || mine == null) return null;
                return FactionManager.IsAtWarAgainstFaction(Bridge.Watched, mine) ? null : Names.Faction(Bridge.Watched) + " is not at war with you, which the first tests assume";
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
            Check("settings", () => Settings.Unknown.Count == 0 ? null : "lines not understood: " + string.Join(" | ", Settings.Unknown));
            Check("log folder", () => Diag.SessionDir == null ? "there is no session folder, so the detailed logs are off" : null);
            Check("freeze probe", () => !Settings.FreezeProbe || File.Exists(Probe.Exe) ? null : "the probe is not installed (" + Probe.Exe + ")");

            int failed = results.Count(r => r.Value != null);
            Log.Line("self-check (game's half): " + (results.Count - failed) + " of " + results.Count + " passed"
                     + (failed > 0 ? "; failed: " + string.Join("; ", results.Where(r => r.Value != null).Select(r => r.Key + ": " + r.Value)) : "."));
            Diag.Event("self_check", (results.Count - failed) + " of " + results.Count, new { results = results.Select(r => new { check = r.Key, problem = r.Value }).ToList() });
            foreach (KeyValuePair<string, string> r in results.Where(r => r.Value != null))
                Diag.Alarm("self_check", "Self-check: " + r.Key + ": " + r.Value + ".", r.Key == "realm under watch" || r.Key == "log folder");

            string agentDir = AgentChannel.On ? Settings.AgentFolder : null;
            Background.Post("self-check (helper's half)", () => HelperHalf(agentDir));
        }

        private static void HelperHalf(string agentDir)
        {
            var problems = new List<string>();
            var notes = new List<string>();
            if (agentDir != null)
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
            else notes.Add("the agent channel is off");
            if (Cpu.Hybrid && Background.Placement.IndexOf("kept to the", StringComparison.Ordinal) < 0)
                problems.Add("the background helper is not kept to the efficiency cores (" + Background.Placement + ")");
            else notes.Add("the background helper runs " + Background.Placement);
            Log.Line("self-check (helper's half): " + (problems.Count == 0 ? "all passed; " + string.Join("; ", notes) + "." : "failed: " + string.Join("; ", problems) + "."));
            Diag.Event("self_check", "helper's half", new { problems, notes });
            foreach (string p in problems) Diag.Alarm("self_check", "Self-check: " + p + ".", p.StartsWith("the agent's folder cannot", StringComparison.Ordinal));
        }
    }
}
