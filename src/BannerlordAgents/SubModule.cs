using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace BannerlordAgents
{
    /// <summary>
    /// Entry point of the Bannerlord Agents module, the game side of the CS 329M project.
    ///
    /// This is its own module on purpose, separate from TFAOMFixes: its own DLL, settings and logs, and no shared
    /// code, so the project's build never gets mixed up with the personal fixes.
    ///
    /// Version 0.3 adds the strategists' flight recorder (docs/LOGGING.md): heartbeats and a watchdog, a folder of
    /// detailed logs per launch, alarms, the black box, the freeze probe, and the two game hooks. All the heavy work
    /// runs on background threads kept on the efficiency cores; the game's thread only copies numbers.
    /// Version 0.4 adds the tools (docs/TOOLS.md); 0.5 the watched realm's chronicle, its memory of the war (docs/MEMORY.md).
    /// </summary>
    public class SubModule : MBSubModuleBase
    {
        public static string Version => typeof(SubModule).Assembly.GetName().Version.ToString(3);
        private static bool _modulesLogged;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Settings.Load();
            Cpu.Detect();
            string previous = Diag.StartLaunch();
            Background.Start();
            Watchdog.Start();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Diag.OnUnhandled(e.ExceptionObject as Exception, e.IsTerminating);
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Diag.EndLaunch("the game closed");
            Log.Line("Bannerlord Agents " + Version + " loaded. Processor: " + Cpu.Description + ". Settings: watch " + Settings.Watch
                     + ", bridge " + (Settings.Bridge ? "on" : "off") + ", recorder " + (Settings.Record ? "on" : "off")
                     + ", performance log " + (Settings.Perf ? "on" : "off") + ", agent " + Settings.Agent + ", logs " + Settings.Detail + " detail"
                     + (Settings.Unknown.Count > 0 ? "; settings lines not understood: " + string.Join(" | ", Settings.Unknown) : ""));
            Log.Line("this launch's log folder: " + (Diag.SessionDir ?? "(none: the detailed logs are off)"));
            if (previous != null) Diag.Alarm("previous_launch_unclean", previous);
            Diag.Event("session", "loaded", new { version = Version, detail = Settings.Detail, agent = Settings.Agent });
            string dll = typeof(SubModule).Assembly.Location;
            Background.Post("build stamp", () => Log.Line("build: " + BuildStamp(dll)));
        }

        /// <summary>Which build actually runs: its file time and a short fingerprint, so a stale install cannot fool us.</summary>
        private static string BuildStamp(string dll)
        {
            try
            {
                var info = new FileInfo(dll);
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(dll))
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                    return Path.GetFileName(dll) + " " + Version + ", built " + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + ", " + info.Length + " bytes, sha256 " + hash + "...";
                }
            }
            catch (Exception e)
            {
                Diag.Swallowed("build stamp", e);
                return "(could not be read: " + e.Message + ")";
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_modulesLogged) return;
            _modulesLogged = true;
            try
            {
                var mods = ModuleHelper.GetActiveModules().Select(m => m.Id + " " + m.Version).ToList();
                Log.Line("modules loaded (" + mods.Count + ", in load order): " + string.Join(", ", mods));
                Diag.Event("session", "modules", new { modules = mods });
            }
            catch (Exception e) { Diag.Swallowed("listing the modules", e); }
        }

        /// <summary>Every frame in every state: the heartbeat, and on-screen messages.</summary>
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            Watchdog.Frame();
            Background.DrainToUi();
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
            catch (Exception e) { Diag.Error("adding the bridge to the campaign", e); }
        }

        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            try
            {
                if (game?.GameType is Campaign)
                {
                    Bridge.Stop();
                    Log.Line("campaign ended");
                }
            }
            catch (Exception e) { Diag.Swallowed("ending the campaign", e); }
        }

        protected override void OnSubModuleUnloaded()
        {
            Diag.EndLaunch("the game closed");
            base.OnSubModuleUnloaded();
        }
    }
}
