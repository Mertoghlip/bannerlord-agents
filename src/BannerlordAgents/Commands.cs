using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>Console commands (open the console with Alt + ~). Every one starts with "agents.".</summary>
    public static class Commands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("status", "agents")]
        public static string Status(List<string> args)
        {
            string campaign = Campaign.Current == null ? "no campaign loaded" : "campaign loaded, " + CampaignTime.Now;
            return "Bannerlord Agents " + SubModule.Version + "\n"
                 + "  " + campaign + "\n"
                 + "  log: " + Log.FilePath + "\n"
                 + "  war room: not built yet (week 3)";
        }
    }
}
