using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// Entry point of the Bannerlord Agents module, the game side of the CS 329M project.
    ///
    /// This is its own module on purpose, separate from TFAOMFixes: its own DLL, settings and logs, and no
    /// shared code, so the project's build never gets mixed up with the personal fixes. Measured runs load it
    /// with TAOM's modules only.
    ///
    /// Version 0.1 only proves that the module loads and that campaign events reach it. The bridge itself
    /// (faction views, pokes, orders, tools) is built on top of this week by week; see docs/PLAN.md.
    /// </summary>
    public class SubModule : MBSubModuleBase
    {
        public static string Version => typeof(SubModule).Assembly.GetName().Version.ToString(3);

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Log.Line("Bannerlord Agents " + Version + " loaded");
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            try
            {
                if (game?.GameType is Campaign && gameStarterObject is CampaignGameStarter starter)
                {
                    starter.AddBehavior(new BridgeBehavior());
                    Log.Line("campaign starting: bridge added");
                }
            }
            catch (Exception e) { Log.Line("OnGameStart failed: " + e); }
        }
    }
}
