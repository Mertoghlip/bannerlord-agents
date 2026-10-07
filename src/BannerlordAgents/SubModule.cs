using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// Entry point of the Bannerlord Agents module, the game side of the CS 329M project.
    ///
    /// This is its own module on purpose, separate from TFAOMFixes: its own DLL, settings and logs, and no shared
    /// code, so the project's build never gets mixed up with the personal fixes.
    ///
    /// Version 0.2 is the first install of the first tests (docs/FIRST-TESTS.md, phases 0-4): the watched realm's
    /// view and pokes, the recorder of the player's own decisions, the performance log, and hand orders. All the
    /// heavy work runs on one background thread kept on the efficiency cores; the game's thread only copies numbers.
    /// </summary>
    public class SubModule : MBSubModuleBase
    {
        public static string Version => typeof(SubModule).Assembly.GetName().Version.ToString(3);

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Settings.Load();
            Cpu.Detect();
            Log.Line("Bannerlord Agents " + Version + " loaded. Processor: " + Cpu.Description + ". Settings: watch " + Settings.Watch
                     + ", bridge " + (Settings.Bridge ? "on" : "off") + ", recorder " + (Settings.Record ? "on" : "off")
                     + ", performance log " + (Settings.Perf ? "on" : "off"));
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

        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            try
            {
                if (game?.GameType is Campaign) { Bridge.Stop(); Log.Line("campaign ended"); }
            }
            catch { }
        }
    }
}
