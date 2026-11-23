using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using SandBox;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// The autopilot (module 0.23.0, Nov 23; docs/AUTOPILOT.md): the test runs an agent plays without me, run with nobody at the game (Nov 22: I want the capability for
    /// you to launch the game, launch the test start save, switch my character to a wanderer and move into a safe settlement, start waiting there at normal speed,
    /// create the save for the playerless save, and do everything else needed, like pausing the game, saving to the test save and quitting, so I can run those
    /// tests fully autonomously). scripts\autopilot.ps1 starts the game and drives it through files in %LOCALAPPDATA%\BannerlordAgents\autopilot:
    /// - in\*.txt, one command each, carried out in name order, one at a time; out\(same name).json says how each ended. Commands: status, load (save), playerless
    ///   [go], wait [stopped], speed stop|play|fast, pause, resume, save (name), check, console agents.(...), menu, quit.
    /// - status.json, rewritten every two seconds: where the game is (main menu, loading, the map, a menu, waiting), the clock, the date, the stand-in and his town,
    ///   the run, the game's memory, the pop-ups answered.
    /// - events.jsonl: every command, pop-up answered, clock restarted, town changed and save made.
    /// - on.json, written by the script with the game's process id: while it names this process, the keepers below work. Without it (my own play) none does.
    /// The keepers: a pop-up keeper (questions answered "no" or "cancel" unless answers.txt says otherwise, one-button notices closed, full-screen pictures
    /// closed; Nov 23: yes, that's the right default), the incidents of waiting in a town held back, the wait kept up at normal speed, the stand-in moved to a
    /// quiet town when his town is besieged or his kingdom goes to war with its owner, the run saved every in-game day under one name, and at the run's end its
    /// TEST END save made without asking. Game's thread, from SubModule.OnApplicationTick and the bridge's ticks; files written off it.
    /// </summary>
    internal static class Autopilot
    {
        private static readonly double Freq = Stopwatch.Frequency;
        public static string Dir => Paths.Ensure(Path.Combine(Paths.Root, "autopilot"));
        private static string InDir => Paths.Ensure(Path.Combine(Dir, "in"));
        private static string OutDir => Paths.Ensure(Path.Combine(Dir, "out"));
        private static string StatusFile => Path.Combine(Dir, "status.json");
        private static string EventsFile => Path.Combine(Dir, "events.jsonl");
        private static string PlanFile => Path.Combine(Dir, "on.json");
        private static string AnswersFile => Path.Combine(Dir, "answers.txt");

        public const string WaitMenuTown = "town_wait_menus", WaitMenuVillage = "village_wait_menus";

        /// <summary>What the script asks of this launch (on.json).</summary>
        internal sealed class Plan
        {
            public int Pid;
            /// <summary>Keep the stand-in waiting in his town with the clock running (false while setting up, so nothing starts the run's clock).</summary>
            public bool Wait;
            /// <summary>play or fast, while waiting.</summary>
            public string Speed = "play";
            /// <summary>The run's own save, made every SaveHours in-game hours while a run is going (0: none).</summary>
            public string SaveName;
            public int SaveHours = 24;
            /// <summary>Move the stand-in to a quiet town when his own is no longer safe.</summary>
            public bool Playerless = true;
            public string Note;
        }

        private static Plan _plan;
        private static DateTime _planTime = DateTime.MinValue;
        private static long _nextLook, _nextStatus, _nextPlanLook;
        private static int _pid;
        private static bool _hold;
        private static readonly object WriteGate = new object();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static bool On => _plan != null && _plan.Pid == Pid;
        private static int Pid { get { if (_pid == 0) using (Process p = Process.GetCurrentProcess()) _pid = p.Id; return _pid; } }

        // ---------- every frame ----------

        public static void Frame()
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _nextLook) return;
            _nextLook = now + (long)(0.5 * Freq);
            try
            {
                if (now >= _nextPlanLook) { _nextPlanLook = now + (long)(2 * Freq); ReadPlan(); }
                if (_job != null) StepJob();
                else NextCommand();
                if (On || _loadAsked != null) Popups(now);
                if (On)
                {
                    Scenes(now);
                    DoMove();
                    KeepWaiting(now);
                    EndSave();
                }
                if (now >= _nextStatus) { _nextStatus = now + (long)(2 * Freq); WriteStatus(); }
            }
            catch (Exception e) { Diag.Error("the autopilot", e); }
        }

        /// <summary>Bridge.Start: a new load forgets the last one's keepers' state (the plan and the hold stay: they are the script's).</summary>
        public static void OnLoad()
        {
            _lastSaveHour = double.NaN;
            _endSave = null;
            EndSaveState = null;
            _moveTo = null;
            _standingSince = 0;
            _prisonerSaid = null;
        }

        private static void ReadPlan()
        {
            try
            {
                if (!File.Exists(PlanFile)) { if (_plan != null) { _plan = null; _planTime = DateTime.MinValue; Event("plan", "switched off (on.json removed)"); } return; }
                DateTime t = File.GetLastWriteTimeUtc(PlanFile);
                if (t == _planTime) return;
                _planTime = t;
                Plan p = Json.Read<Plan>(File.ReadAllText(PlanFile));
                _plan = p;
                Event("plan", On ? "on: wait " + (p.Wait ? "yes" : "no") + ", speed " + p.Speed + ", save " + (p.SaveName ?? "none") + " every " + p.SaveHours + " h" + (p.Note != null ? ", " + p.Note : "")
                                 : "on.json names process " + p?.Pid + ", not this one (" + Pid + "): the keepers stay off");
            }
            catch (IOException) { _planTime = DateTime.MinValue; }   // being written: next look
            catch (Exception e) { Diag.Swallowed("reading the autopilot's on.json", e); }
        }

        // ---------- the commands ----------

        private sealed class Job
        {
            public string File, Line, Verb, Arg;
            public DateTime Started = DateTime.Now;
            public double Timeout = 30;
            public bool Finished;
            /// <summary>Null while it goes on; else its answer (a failure begins with "FAILED").</summary>
            public Func<string> Step;
        }

        private static Job _job;

        private static void NextCommand()
        {
            string[] files;
            try { files = Directory.GetFiles(InDir, "*.txt"); }
            catch (Exception e) { Diag.Swallowed("listing the autopilot's commands", e); return; }
            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.Ordinal);
            string f = files[0];
            string line;
            try { line = File.ReadAllText(f).Trim(); File.Delete(f); }
            catch (IOException) { return; }   // still being written: next look
            var job = new Job { File = Path.GetFileNameWithoutExtension(f), Line = line };
            int sp = line.IndexOf(' ');
            job.Verb = (sp < 0 ? line : line.Substring(0, sp)).Trim().ToLowerInvariant();
            job.Arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            Event("command", line);
            try { Begin(job); }
            catch (Exception e) { Diag.Error("autopilot " + job.Verb, e); Finish(job, "FAILED: " + e.GetType().Name + ": " + e.Message); return; }
            if (!job.Finished && job.Step != null) { _job = job; StepJob(); }
            else if (!job.Finished) Finish(job, "FAILED: the command did nothing");
        }

        private static void Begin(Job job)
        {
            switch (job.Verb)
            {
                case "status": Finish(job, "status written"); return;
                case "check": Finish(job, Run.CheckText()); return;
                case "playerless":
                {
                    var args = new List<string> { "playerless" };
                    args.AddRange(job.Arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    bool go = args.Count > 1 && args[1].Equals("go", StringComparison.OrdinalIgnoreCase);
                    string answer = Playerless.Command(args);
                    Kingdom mine = Clan.PlayerClan?.Kingdom;
                    bool done = mine != null && mine.Leader != Hero.MainHero;
                    Finish(job, go && !done ? "FAILED: " + answer : answer);
                    return;
                }
                case "speed": Finish(job, SetSpeed(job.Arg)); return;
                case "pause": _hold = true; Finish(job, SetSpeed("stop") + "; held: the keepers leave the clock stopped until resume"); return;
                case "resume":
                    _hold = false;
                    Finish(job, (WaitActive() ? SetSpeed(_plan?.Speed ?? "play") : "not waiting now (the keepers put him back to waiting)") + "; released");
                    return;
                case "console": Finish(job, ConsoleCommand(job.Arg)); return;
                case "load": BeginLoad(job); return;
                case "wait": BeginWait(job); return;
                case "save": BeginSave(job); return;
                case "menu": BeginMenu(job, false); return;
                case "quit": BeginMenu(job, true); return;
                case "move": BeginMove(job); return;
                case "selftest": BeginSelfTest(job); return;
                default: Finish(job, "FAILED: unknown command '" + job.Verb + "' (status, load, playerless, wait, speed, pause, resume, save, check, console, move, selftest, menu, quit)"); return;
            }
        }

        private static void StepJob()
        {
            Job job = _job;
            if (job == null) return;
            string answer;
            try { answer = job.Step(); }
            catch (Exception e) { answer = "FAILED: " + e.GetType().Name + ": " + e.Message; Diag.Error("autopilot " + job.Verb, e); }
            if (answer == null && (DateTime.Now - job.Started).TotalSeconds > job.Timeout) answer = "FAILED: no end after " + job.Timeout + " s";
            if (answer != null) Finish(job, answer);
        }

        private static void Finish(Job job, string answer)
        {
            if (_job == job) _job = null;
            if (job.Finished) return;
            job.Finished = true;
            bool ok = !answer.StartsWith("FAILED", StringComparison.Ordinal);
            string text = Json.Line(new { command = job.Line, ok, answer, started = job.Started.ToString("yyyy-MM-dd HH:mm:ss"), finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            WriteFile(Path.Combine(OutDir, job.File + ".json"), text, now: true);
            Event(ok ? "done" : "failed", job.Line + " -> " + First(answer));
            Log.Line("autopilot: " + job.Line + " -> " + First(answer));
            _nextStatus = 0;
        }

        private static string First(string s) { string f = (s ?? "").Split('\n')[0]; return f.Length > 300 ? f.Substring(0, 300) + "..." : f; }

        private static string ConsoleCommand(string line)
        {
            int sp = line.IndexOf(' ');
            string name = (sp < 0 ? line : line.Substring(0, sp)).Trim();
            if (!name.StartsWith("agents.", StringComparison.OrdinalIgnoreCase)) return "FAILED: only the agents.* commands can be sent this way";
            List<string> args = (sp < 0 ? "" : line.Substring(sp + 1)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string answer = CommandLineFunctionality.CallFunction(name, args, out bool found);
            return found ? answer : "FAILED: no command " + name;
        }

        // ---- load: the game's own Continue, by name (SandBoxViewSubModule.ContinueCampaign) ----

        private static string _loadAsked;

        private static void BeginLoad(Job job)
        {
            string name = job.Arg;
            if (!(GameStateManager.Current?.ActiveState is InitialState)) { Finish(job, "FAILED: loads are made from the main menu (the game is at: " + Watchdog.State + ")"); return; }
            var info = MBSaveLoad.GetSaveFileWithName(name);
            if (info == null) { Finish(job, "FAILED: no save named '" + name + "'"); return; }
            if (SandBoxSaveHelper.GetIsDisabledWithReason(info, out TextObject reason)) { Finish(job, "FAILED: the game will not load it: " + reason); return; }
            string loadBefore = Bridge.LoadId;
            bool cancelled = false;
            long mapSince = 0;
            _loadAsked = name;
            job.Timeout = 900;
            job.Step = () =>
            {
                if (cancelled) { _loadAsked = null; return "FAILED: the load was cancelled"; }
                if (!Bridge.Running || Bridge.LoadId == loadBefore) return null;
                if (!OnMapScreen()) { mapSince = 0; return null; }
                if (mapSince == 0) { mapSince = Stopwatch.GetTimestamp(); return null; }
                if ((Stopwatch.GetTimestamp() - mapSince) / Freq < 3) return null;   // the map's first frames
                _loadAsked = null;
                RunState r = Run.Current;
                if (r != null && r.Status == "armed" && Clan.PlayerClan?.Kingdom != null && Clan.PlayerClan.Kingdom.Leader != Hero.MainHero)
                    Run.SetUp("loaded by the autopilot from " + name + ", where the playerless switch was made: you are " + Names.Hero(Hero.MainHero)
                              + (Hero.MainHero.CurrentSettlement != null ? " in " + Names.Settlement(Hero.MainHero.CurrentSettlement) : ""));
                return "loaded " + name + " (" + (r == null ? "no run readied" : "run " + r.Id + " " + r.Status) + ", " + Text.When(CampaignTime.Now.ToHours) + ", menu " + (MenuId() ?? "none") + ")";
            };
            SandBoxSaveHelper.TryLoadSave(info, res => MBGameManager.StartNewGame(new SandBoxGameManager(res)), () => cancelled = true);
        }

        private static bool OnMapScreen()
        {
            if (Campaign.Current == null || Mission.Current != null || !(GameStateManager.Current?.ActiveState is MapState)) return false;
            return ScreenManager.TopScreen?.GetType().Name == "MapScreen";
        }

        // ---- wait: into the town's wait menu, at the plan's speed (the game starts every wait at fast-forward, GameMenu.StartWait) ----

        private static void BeginWait(Job job)
        {
            bool stopped = job.Arg.Equals("stopped", StringComparison.OrdinalIgnoreCase);
            job.Timeout = 60;
            job.Step = () =>
            {
                string why = CannotWait();
                if (why != null) return why.StartsWith("later", StringComparison.Ordinal) ? null : "FAILED: " + why;
                string menu = MenuId();
                if (menu == WaitMenuTown || menu == WaitMenuVillage)
                {
                    string sp = SetSpeed(stopped || _hold ? "stop" : _plan?.Speed ?? "play");
                    return "waiting in " + Names.Settlement(MobileParty.MainParty.CurrentSettlement) + "; " + sp;
                }
                EnterWait();
                if (stopped) Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;   // the same frame, before any tick: a run's clock must not start
                return null;
            };
        }

        /// <summary>Null when the stand-in can be put to waiting; "later ..." while something passing is in the way; else why not.</summary>
        private static string CannotWait()
        {
            if (Campaign.Current == null || !Bridge.Running) return "no campaign is loaded";
            if (Mission.Current != null) return "later: a battle or scene is up";
            if (!(GameStateManager.Current?.ActiveState is MapState)) return "later: the map is not up";
            if (Hero.MainHero.IsPrisoner) return "you are a prisoner";
            if (MobileParty.MainParty.CurrentSettlement == null) return "you are in no town, castle or village";
            if (Campaign.Current.SaveHandler.IsSaving) return "later: saving";
            if (InformationManager.IsAnyInquiryActive()) return "later: a question is up";
            if (Campaign.Current.ConversationManager.IsConversationFlowActive) return "later: a conversation is up";
            return null;
        }

        private static void EnterWait()
        {
            Settlement s = MobileParty.MainParty.CurrentSettlement;
            if (Campaign.Current.CurrentMenuContext == null)
            {
                GameMenu.ActivateGameMenu(s.IsVillage ? "village" : s.IsCastle ? "castle" : "town");
                return;   // the menu opens on a later frame
            }
            GameMenu.SwitchToMenu(s.IsVillage ? WaitMenuVillage : WaitMenuTown);
        }

        public static string MenuId()
        {
            try { return Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId; }
            catch { return null; }
        }

        private static bool WaitActive()
        {
            try { return Campaign.Current?.CurrentMenuContext?.GameMenu?.IsWaitActive == true; }
            catch { return false; }
        }

        private static string SetSpeed(string speed)
        {
            Campaign c = Campaign.Current;
            if (c == null) return "FAILED: no campaign is loaded";
            bool wait = WaitActive();
            switch ((speed ?? "").Trim().ToLowerInvariant())
            {
                case "stop": case "stopped": case "pause": c.TimeControlMode = CampaignTimeControlMode.Stop; return "the clock stopped";
                case "play": case "normal": case "": c.TimeControlMode = wait ? CampaignTimeControlMode.UnstoppablePlay : CampaignTimeControlMode.StoppablePlay; return "the clock at normal speed";
                case "fast": c.TimeControlMode = wait ? CampaignTimeControlMode.UnstoppableFastForward : CampaignTimeControlMode.StoppableFastForward; return "the clock at fast-forward";
                default: return "FAILED: speed is stop, play or fast";
            }
        }

        // ---- saves: the game's SaveAs; done when the game says the save is over (CampaignEvents.OnSaveOverEvent) ----

        private static string _saving;       // the save asked of the game and not over yet
        private static Job _savingFor;       // the command waiting on it (none for the keepers' own saves)
        private static bool? _savedOk;

        private static void BeginSave(Job job)
        {
            string name = job.Arg.Trim();
            if (name.Length == 0) { Finish(job, "FAILED: save needs a name"); return; }
            job.Timeout = 300;
            bool started = false;
            job.Step = () =>
            {
                if (!started)
                {
                    if (Campaign.Current == null || !(GameStateManager.Current?.ActiveState is MapState) || Mission.Current != null) return "FAILED: saves are made on the campaign map";
                    if (!StartSave(name, job)) return null;   // another save first (the keepers' own): this one waits for it
                    started = true;
                    return null;
                }
                if (_savingFor == job || _savedOk == null) return null;
                bool ok = _savedOk.Value;
                _savedOk = null;
                return ok ? "saved as " + name : "FAILED: the game could not save as " + name;
            };
        }

        private static bool StartSave(string name, Job forJob)
        {
            if (_saving != null || Campaign.Current.SaveHandler.IsSaving) return false;
            _saving = name;
            _savingFor = forJob;
            _savedOk = null;
            Campaign.Current.SaveHandler.SaveAs(name);
            return true;
        }

        /// <summary>BridgeBehavior.OnSaveOver: every save the game makes.</summary>
        public static void SaveOver(bool saved, string name)
        {
            bool ours = _saving != null && string.Equals(_saving, name, StringComparison.OrdinalIgnoreCase);
            if (ours || On) Event("save", (saved ? "saved " : "NOT saved ") + name);
            if (!ours) return;
            if (_savingFor != null) _savedOk = saved;
            _saving = null;
            _savingFor = null;
        }

        // ---- menu and quit: the game's own order (Module.ShutDownWithDelay: EndGame, then QuitGame) ----

        private static void BeginMenu(Job job, bool quit)
        {
            job.Timeout = 300;
            bool asked = false;
            job.Step = () =>
            {
                if (Campaign.Current != null && Campaign.Current.SaveHandler.IsSaving) return null;
                if (!(GameStateManager.Current?.ActiveState is InitialState))
                {
                    if (!asked && Game.Current != null) { asked = true; MBGameManager.EndGame(); }
                    return null;
                }
                if (!quit) return "at the main menu";
                WriteStatus(true, "quitting");
                Finish(job, "quitting");
                TaleWorlds.Engine.Utilities.QuitGame();
                return "quitting";
            };
        }

        // ---- move: the stand-in to the quietest other town, as the hourly keeper does when his own is unsafe (for the dry run, and by hand) ----

        private static void BeginMove(Job job)
        {
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (Campaign.Current == null || !Bridge.Running || mine == null) { Finish(job, "FAILED: no campaign with your kingdom is loaded"); return; }
            if (mine.Leader == Hero.MainHero) { Finish(job, "FAILED: you rule your kingdom yourself; the move is for the playerless stand-in"); return; }
            Settlement at = MobileParty.MainParty.CurrentSettlement;
            Settlement to = Playerless.QuietTowns(mine).FirstOrDefault(s => s != at);
            if (to == null) { Finish(job, "FAILED: no quiet town to go to"); return; }
            _moveTo = to;
            _moveWhy = "moved by the command";
            job.Timeout = 30;
            DateTime asked = DateTime.Now;
            job.Step = () =>
            {
                if (_moveTo != null && On) return null;   // the keepers' frame does it
                if (_moveTo != null) DoMove();
                if (MobileParty.MainParty.CurrentSettlement != to && (DateTime.Now - asked).TotalSeconds < 10) return null;
                return MobileParty.MainParty.CurrentSettlement == to ? "moved to " + Names.Settlement(to) + " (" + Names.Faction(to.MapFaction) + "); the wait is to be entered again"
                                                                     : "FAILED: he is not in " + Names.Settlement(to) + " (at " + (MobileParty.MainParty.CurrentSettlement != null ? Names.Settlement(MobileParty.MainParty.CurrentSettlement) : "no town") + ")";
            };
        }

        // ---- selftest: a question, a one-button notice and a list, each answered by the keeper (the dry run's proof; needs the keepers on) ----

        private static void BeginSelfTest(Job job)
        {
            if (!On) { Finish(job, "FAILED: the keepers are off (no on.json naming this process)"); return; }
            string said = null, listed = null;
            bool noticed = false;
            InformationManager.ShowInquiry(new InquiryData("Autopilot self-test", "A question with two buttons: the keeper should answer no.", true, true, "Yes", "No",
                () => said = "yes", () => said = "no"), false);
            InformationManager.ShowInquiry(new InquiryData("Autopilot self-test notice", "A notice with one button: the keeper should close it.", true, false, "OK", null,
                () => noticed = true, null), false);
            var items = new List<InquiryElement> { new InquiryElement("a", "First choice", null), new InquiryElement("b", "Second choice", null) };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Autopilot self-test list", "A list with a cancel button: the keeper should cancel.", items, true, 1, 1, "Choose", "Cancel",
                l => listed = "chose " + string.Join(", ", l.Select(e => e.Title)), l => listed = "cancelled"), false);
            job.Timeout = 30;
            job.Step = () =>
            {
                if (said == null || !noticed || listed == null) return null;
                bool ok = said == "no" && listed == "cancelled";
                return (ok ? "" : "FAILED: ") + "the question was answered " + said + ", the notice closed, the list " + listed;
            };
        }

        // ---------- the pop-up keeper ----------

        private static FieldInfo _activeData, _activeSource;
        private static bool _queryFieldsTried;
        private static object _seenData;
        private static long _seenAt;
        private static string _mismatchTitle;
        public static int Answered;
        public static string LastPopup;

        private static void Popups(long now)
        {
            if (!_queryFieldsTried)
            {
                _queryFieldsTried = true;
                Type t = Type.GetType("TaleWorlds.MountAndBlade.GauntletUI.GauntletQueryManager, TaleWorlds.MountAndBlade.GauntletUI");
                _activeData = t?.GetField("_activeQueryData", BindingFlags.NonPublic | BindingFlags.Static);
                _activeSource = t?.GetField("_activeDataSource", BindingFlags.NonPublic | BindingFlags.Static);
                if (_activeData == null || _activeSource == null)
                    Diag.Alarm("autopilot_popups_blind", "The autopilot cannot see the game's pop-ups (GauntletQueryManager's fields were not found), so a question would stop an unattended run.", true);
            }
            if (_activeData == null) return;
            object data = _activeData.GetValue(null);
            if (data == null) { _seenData = null; return; }
            if (!ReferenceEquals(data, _seenData)) { _seenData = data; _seenAt = now; return; }
            if ((now - _seenAt) / Freq < 1.0) return;   // a second on screen first
            _seenData = null;
            Answer(data);
        }

        private static void Answer(object data)
        {
            if (data is MultiSelectionInquiryData mq) { if (On) AnswerList(mq); return; }
            string title, text, yes, no;
            bool yesShown, noShown;
            if (data is InquiryData q) { title = q.TitleText; text = q.Text; yes = q.AffirmativeText; no = q.NegativeText; yesShown = q.IsAffirmativeOptionShown; noShown = q.IsNegativeOptionShown; }
            else if (data is TextInquiryData tq) { title = tq.TitleText; text = tq.Text; yes = tq.AffirmativeText; no = tq.NegativeText; yesShown = tq.IsAffirmativeOptionShown; noShown = tq.IsNegativeOptionShown; }
            else return;
            if (_mismatchTitle == null) _mismatchTitle = new TextObject("{=r7xdYj4q}Module Mismatch").ToString();
            bool sayYes;
            string rule;
            if (_loadAsked != null && title == _mismatchTitle) { sayYes = true; rule = "the save the autopilot was told to load"; }
            else if (!On) return;
            else if (Rule(title, text, out bool ruled)) { sayYes = ruled; rule = "answers.txt"; }
            else { sayYes = !noShown; rule = noShown ? "the default: no" : "the only button"; }
            if (sayYes && !yesShown) sayYes = false;
            if (!sayYes && !noShown) sayYes = true;
            string answer = sayYes ? (yes ?? "yes") : (no ?? "no");
            object vm = _activeSource.GetValue(null);
            MethodInfo m = vm?.GetType().GetMethod(sayYes ? "ExecuteAffirmativeAction" : "ExecuteNegativeAction", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m == null) { InformationManager.HideInquiry(); answer += " (closed: no button found)"; }
            else m.Invoke(vm, null);
            Answered++;
            LastPopup = Clip(title, 80) + " -> " + answer;
            Event("popup", LastPopup, new { title, text = Clip(text, 600), yes, no, rule });
            Log.Line("autopilot: pop-up \"" + Clip(title, 80) + "\" answered " + answer + " (" + rule + ")");
        }

        private static void AnswerList(MultiSelectionInquiryData mq)
        {
            var chosen = new List<InquiryElement>();
            string answer;
            if (mq.IsExitShown)
            {
                answer = mq.NegativeText ?? "cancel";
                InformationManager.HideInquiry();
                mq.NegativeAction?.Invoke(chosen);
            }
            else
            {
                chosen = (mq.InquiryElements ?? new List<InquiryElement>()).Where(e => e != null && e.IsEnabled).Take(Math.Max(1, mq.MinSelectableOptionCount)).ToList();
                answer = (mq.AffirmativeText ?? "done") + " with " + (chosen.Count == 0 ? "nothing" : string.Join(", ", chosen.Select(e => e.Title)));
                InformationManager.HideInquiry();
                mq.AffirmativeAction?.Invoke(chosen);
            }
            Answered++;
            LastPopup = Clip(mq.TitleText, 80) + " -> " + answer;
            Event("popup", LastPopup, new { title = mq.TitleText, text = Clip(mq.DescriptionText, 600), choices = (mq.InquiryElements ?? new List<InquiryElement>()).Select(e => e?.Title).Take(30).ToList(),
                                            rule = mq.IsExitShown ? "the default: cancel" : "no cancel: the first choices" });
            Log.Line("autopilot: list \"" + Clip(mq.TitleText, 80) + "\" answered " + answer);
        }

        /// <summary>answers.txt: lines "yes | words in the title or text" or "no | words"; the first that matches decides.</summary>
        private static bool Rule(string title, string text, out bool yes)
        {
            yes = false;
            try
            {
                if (!File.Exists(AnswersFile)) return false;
                string hay = ((title ?? "") + "\n" + (text ?? "")).ToLowerInvariant();
                foreach (string raw in File.ReadAllLines(AnswersFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    int bar = line.IndexOf('|');
                    if (bar < 0) continue;
                    string verdict = line.Substring(0, bar).Trim().ToLowerInvariant(), words = line.Substring(bar + 1).Trim().ToLowerInvariant();
                    if (words.Length == 0 || !hay.Contains(words)) continue;
                    yes = verdict == "yes";
                    return true;
                }
            }
            catch (Exception e) { Diag.Swallowed("reading the autopilot's answers.txt", e); }
            return false;
        }

        private static string Clip(string s, int n) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "..." : s;

        // ---------- the full-screen pictures (births, deaths, coronations in your clan) ----------

        private static FieldInfo _sceneActive;
        private static bool _sceneTried;
        private static long _sceneSince;

        private static void Scenes(long now)
        {
            if (!_sceneTried)
            {
                _sceneTried = true;
                _sceneActive = typeof(MBInformationManager).GetField("IsAnySceneNotificationActive", BindingFlags.NonPublic | BindingFlags.Static);
                if (_sceneActive == null) Diag.Alarm("autopilot_pictures_blind", "The autopilot cannot see the game's full-screen pictures, so one (a birth or a death in your clan) could stop an unattended run.", true);
                MBInformationManager.OnShowSceneNotification += d => { if (On) Event("picture", "shown: " + d?.GetType().Name); };
            }
            bool active = false;
            if (_sceneActive?.GetValue(null) is Func<bool> f)
                foreach (Delegate g in f.GetInvocationList())
                    if (g is Func<bool> h && h()) { active = true; break; }
            if (!active) { _sceneSince = 0; return; }
            if (_sceneSince == 0) { _sceneSince = now; return; }
            if ((now - _sceneSince) / Freq < 2) return;
            _sceneSince = 0;
            MBInformationManager.HideSceneNotification();
            Answered++;
            LastPopup = "a full-screen picture closed";
            Event("picture", "closed");
            Log.Line("autopilot: a full-screen picture closed");
        }

        // ---------- the wait kept up ----------

        private static long _standingSince;
        public static int ClockRestarts;

        private static void KeepWaiting(long now)
        {
            if (_plan == null || !_plan.Wait || _hold || _job != null || _moveTo != null) { _standingSince = 0; return; }
            string why = CannotWait();
            if (why != null) { _standingSince = 0; return; }   // something passing, or no town: the hourly keeper moves him
            string menu = MenuId();
            CampaignTimeControlMode mode = Campaign.Current.TimeControlMode;
            bool waiting = menu == WaitMenuTown || menu == WaitMenuVillage;
            if (waiting && mode != CampaignTimeControlMode.Stop && mode != CampaignTimeControlMode.FastForwardStop) { _standingSince = 0; return; }
            if (_standingSince == 0) { _standingSince = now; return; }
            if ((now - _standingSince) / Freq < 5) return;
            _standingSince = 0;
            if (!waiting) EnterWait();
            menu = MenuId();
            if (menu == WaitMenuTown || menu == WaitMenuVillage) SetSpeed(_plan.Speed);
            ClockRestarts++;
            Event("clock", (waiting ? "restarted in the wait at " : "back to waiting at ") + Names.Settlement(MobileParty.MainParty.CurrentSettlement) + " (menu was " + (waiting ? "the wait" : MenuOr(menu)) + ")");
        }

        private static string MenuOr(string m) => m ?? "none";

        // ---------- hourly: incidents, the safe town, the run's own save ----------

        private static double _lastSaveHour = double.NaN;
        public static int IncidentsHeld, TownMoves;

        /// <summary>Every campaign tick (BridgeBehavior.OnTick) and hour: an incident of waiting (1.4.8: one hour in seven, 8 to 15 days apart) held back while the autopilot is on.</summary>
        public static void CampaignTick()
        {
            if (!On) return;
            MapState ms = GameStateManager.Current?.LastOrDefault<MapState>();
            if (ms?.NextIncident == null) return;
            string id = ms.NextIncident.StringId;
            ms.NextIncident = null;
            IncidentsHeld++;
            Event("incident", "held back: " + id);
        }

        /// <summary>The bridge's hourly tick, whatever the bridge's own switch.</summary>
        public static void Hourly()
        {
            if (!On) return;
            try
            {
                CampaignTick();
                if (_plan.Playerless) KeepSafe();
                if (_plan.SaveHours > 0 && !string.IsNullOrEmpty(_plan.SaveName) && Run.Active && !_hold)
                {
                    double h = CampaignTime.Now.ToHours;
                    if (double.IsNaN(_lastSaveHour) || h < _lastSaveHour) _lastSaveHour = h;   // counted from the load
                    else if (h - _lastSaveHour >= _plan.SaveHours - 0.01 && _job == null && StartSave(_plan.SaveName, null))
                    {
                        _lastSaveHour = h;
                        Event("save", "the run's own save, every " + _plan.SaveHours + " in-game hours: " + _plan.SaveName);
                    }
                }
            }
            catch (Exception e) { Diag.Error("the autopilot's hour", e); }
        }

        private static string _prisonerSaid, _moveWhy;
        private static Settlement _moveTo;

        private static void KeepSafe()
        {
            Hero me = Hero.MainHero;
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (me == null || mine == null || mine.Leader == me) return;   // not the playerless stand-in
            if (me.IsPrisoner)
            {
                string by = Names.Party(me.PartyBelongedToAsPrisoner?.MobileParty);
                if (_prisonerSaid != by) { _prisonerSaid = by; Event("stand-in", "taken prisoner by " + by + ": the game keeps the clock running until he is free"); }
                return;
            }
            _prisonerSaid = null;
            Settlement at = MobileParty.MainParty.CurrentSettlement;
            string why = at == null ? "he is out on the map" : at.IsUnderSiege ? Names.Settlement(at) + " is besieged"
                       : at.MapFaction != null && FactionManager.IsAtWarAgainstFaction(at.MapFaction, mine) ? Names.Settlement(at) + "'s realm is at war with " + Names.Faction(mine)
                       : null;
            if (why == null || MobileParty.MainParty.MapEvent != null) return;
            Settlement to = Playerless.QuietTowns(mine).FirstOrDefault(s => s != at);
            if (to == null) { Event("stand-in", why + ", and no quiet town is left to go to"); return; }
            _moveTo = to;
            _moveWhy = why;
        }

        /// <summary>The move itself, on a frame of its own rather than inside the game's hourly step.</summary>
        private static void DoMove()
        {
            Settlement to = _moveTo;
            if (to == null || Mission.Current != null || !(GameStateManager.Current?.ActiveState is MapState) || InformationManager.IsAnyInquiryActive()) return;
            _moveTo = null;
            try
            {
                Settlement at = MobileParty.MainParty.CurrentSettlement;
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                if (PlayerEncounter.Current != null) { if (at != null) PlayerEncounter.LeaveSettlement(); PlayerEncounter.Finish(true); }
                else if (at != null) LeaveSettlementAction.ApplyForParty(MobileParty.MainParty);
                MobileParty.MainParty.Position = to.GatePosition;
                EncounterManager.StartSettlementEncounter(MobileParty.MainParty, to);
                TownMoves++;
                Event("stand-in", _moveWhy + ": moved to " + Names.Settlement(to) + " (" + Names.Faction(to.MapFaction) + ")");
                Log.Line("autopilot: " + _moveWhy + ", so the stand-in moved to " + Names.Settlement(to));
            }
            catch (Exception e) { Diag.Error("moving the stand-in to " + Names.Settlement(to), e); }
        }

        // ---------- the run's end ----------

        private static string _endSave;
        public static string EndSaveState;

        /// <summary>Run.Finish while the autopilot is on: the run's TEST END save is made without asking.</summary>
        public static void RunEnded(string saveName)
        {
            _endSave = saveName;
            EndSaveState = "to be made: " + saveName;
            Event("run", "over: " + saveName + " to be saved");
        }

        private static void EndSave()
        {
            if (_endSave == null || _job != null || _saving != null) return;
            if (Campaign.Current == null || Campaign.Current.SaveHandler.IsSaving || !(GameStateManager.Current?.ActiveState is MapState) || Mission.Current != null) return;
            string name = _endSave;
            var job = new Job { File = "run-end-" + DateTime.Now.ToString("HHmmss"), Line = "save " + name + " (the run's end)", Verb = "save", Arg = name, Timeout = 300 };
            if (!StartSave(name, job)) return;
            _endSave = null;
            EndSaveState = "saving " + name;
            job.Step = () =>
            {
                if (_savingFor == job || _savedOk == null) return null;
                bool ok = _savedOk.Value;
                _savedOk = null;
                EndSaveState = (ok ? "saved " : "NOT saved ") + name;
                return ok ? "saved as " + name : "FAILED: the game could not save as " + name;
            };
            _job = job;
        }

        // ---------- status and events ----------

        private static void WriteStatus(bool now = false, string doing = null)
        {
            try
            {
                Campaign c = Campaign.Current;
                bool campaign = c != null && Bridge.Running;
                Hero me = campaign ? Hero.MainHero : null;
                Settlement at = campaign ? MobileParty.MainParty?.CurrentSettlement : null;
                RunState r = Run.Current;
                MemoryTidy.Memory(out long priv, out long ws);
                var s = new
                {
                    at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    pid = Pid,
                    module = SubModule.Version,
                    state = Watchdog.State,
                    autopilot = On,
                    wait_wanted = _plan?.Wait,
                    hold = _hold,
                    busy = doing ?? _job?.Line,
                    menu = campaign ? MenuId() : null,
                    waiting = campaign && WaitActive(),
                    clock = campaign ? Watchdog.Speed : null,
                    clock_running = campaign && Watchdog.ClockRunning,
                    save = campaign ? SafeSaveName() : null,
                    load = campaign ? Bridge.LoadId : null,
                    hours = campaign ? Math.Round(CampaignTime.Now.ToHours, 2) : (double?)null,
                    date = campaign ? Text.When(CampaignTime.Now.ToHours) : null,
                    hero = me != null ? Names.Hero(me) : null,
                    rules_kingdom = me != null && Clan.PlayerClan?.Kingdom?.Leader == me,
                    prisoner = me?.IsPrisoner,
                    place = at != null ? Names.Settlement(at) : null,
                    place_id = at?.StringId,
                    run = r == null ? null : new { id = r.Id, arm = r.Arm, status = r.Status, day = r.LastDay, days = r.Days, saves = r.Saves.Count, last_save = r.Saves.LastOrDefault() },
                    end_save = EndSaveState,
                    memory_mb = priv,
                    working_set_mb = ws,
                    popups_answered = Answered,
                    last_popup = LastPopup,
                    incidents_held = IncidentsHeld,
                    clock_restarts = ClockRestarts,
                    town_moves = TownMoves
                };
                WriteFile(StatusFile, Json.Line(s), now);
            }
            catch (Exception e) { Diag.Swallowed("the autopilot's status", e); }
        }

        private static string SafeSaveName()
        {
            try { return MBSaveLoad.ActiveSaveSlotName; } catch { return null; }
        }

        private static void Event(string kind, string what, object data = null)
        {
            double? hours = null;
            try { if (Campaign.Current != null && Bridge.Running) hours = Math.Round(CampaignTime.Now.ToHours, 2); } catch { }
            string line = Json.Line(new { t = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), kind, what, hours, data });
            string path = EventsFile;
            ThreadPool.QueueUserWorkItem(_ => { lock (WriteGate) { try { File.AppendAllText(path, line + "\n", Utf8); } catch (Exception e) { Diag.Swallowed("the autopilot's events", e); } } });
            Diag.Event("autopilot", kind, new { what, data });
        }

        /// <summary>A whole small file swapped in; off the game's thread unless asked now (a command's answer, the status at the quit).</summary>
        private static void WriteFile(string path, string text, bool now)
        {
            void Write() { lock (WriteGate) Files.Overwrite(path, text); }
            if (now) Write();
            else ThreadPool.QueueUserWorkItem(_ => Write());
        }
    }
}
