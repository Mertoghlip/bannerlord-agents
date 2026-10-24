using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace BannerlordAgents
{
    /// <summary>The bridge's state for the campaign that is running.</summary>
    internal static class Bridge
    {
        /// <summary>The first watched realm (the console's views, the sight check).</summary>
        public static Kingdom Watched;
        /// <summary>
        /// Every watched realm, the first first (since 0.8: each with its own agent and channel). Made on first use, never in a
        /// static initialiser: the smoke test runs the worker without the game's assemblies.
        /// </summary>
        public static List<Kingdom> Seats { get => _seats ?? (_seats = new List<Kingdom>()); set => _seats = value; }
        private static List<Kingdom> _seats;
        /// <summary>The agents of this load, each realm resolved to its kingdom id (Agents.Configured), and what could not be used.</summary>
        public static List<AgentConfig> Configs = new List<AgentConfig>();
        public static readonly List<string> Unusable = new List<string>();
        /// <summary>Agents switched off and set aside for the one switched on for the same realm (0.11.1: as meant, logged only).</summary>
        public static readonly List<string> SetAside = new List<string>();
        /// <summary>Which records are this game's history (History, since 0.9.1): found at the load, noted at every save.</summary>
        public static History History;
        public static bool Running;
        public static string CampaignId = "unknown";
        /// <summary>This load of the campaign (a new one each time a save is loaded), and the in-game hour of the save: the war room tells a reload by them.</summary>
        public static string LoadId = "";
        public static double LoadedAt = double.NaN;

        public static void Start()
        {
            Running = true;
            Live.CampaignRunning = true;
            CampaignId = Campaign.Current?.UniqueGameId ?? "unknown";
            LoadId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", Text.Inv);
            LoadedAt = CampaignTime.Now.ToHours;
            Diag.Count("campaign loads");
            Watchdog.NewCampaign();
            Orders.Reset();
            Purse.Reset();
            Lieutenant.Reset();
            Notices.Reset();
            Recorder.Reset();
            PacReader.Reset();
            LordTours.Reset();
            Votes.Reset();
            Vision.Reset();
            LordWatch.Reset();
            Names.Clear();
            Perf.ResetCapture();
            Recruitment.Reset();
            Calendar.Capture();
            long t = Perf.Now();
            int warmed = Names.WarmUp();
            double warmMs = Perf.Ms(t);
            // the agents of this load: each folder's agent.txt (or the settings of 0.8), each realm resolved; none is special
            var configs = new List<AgentConfig>();
            var seats = new List<Kingdom>();
            Unusable.Clear();
            SetAside.Clear();
            var resolved = new List<KeyValuePair<AgentConfig, Kingdom>>();
            foreach (AgentConfig c in Agents.Configured())
            {
                Kingdom k = c.Problem == null ? Resolve(c.Realm) : null;
                if (k == null) { Unusable.Add(c.Problem ?? "no realm matches '" + c.Realm + "'" + (c.Workspace != null ? " (" + c.Workspace + ")" : "")); continue; }
                resolved.Add(new KeyValuePair<AgentConfig, Kingdom>(c, k));
            }
            // two agents for one realm: the one switched on plays it, else the first (0.11: the test runs keep every agent listed
            // and switch them on and off, so your kingdom's own agent and the lieutenant can both name it)
            foreach (KeyValuePair<AgentConfig, Kingdom> kv in resolved)
            {
                AgentConfig c = kv.Key;
                Kingdom k = kv.Value;
                AgentConfig first = resolved.Where(x => x.Value == k).Select(x => x.Key).OrderBy(x => x.On ? 0 : 1).First();
                if (first != c)
                {
                    // an agent switched off, set aside for the one switched on, is as meant (the test runs' arms): a note, not a problem
                    string why = Names.Faction(k) + " has two agents; " + (first.On ? "the one switched on" : "the first") + " plays it (" + c.Workspace + " does not)";
                    if (first.On && !c.On) SetAside.Add(why);
                    else Unusable.Add(why);
                    continue;
                }
                c.Realm = k.StringId;
                configs.Add(c);
                seats.Add(k);
            }
            Configs = configs;
            Seats = seats;
            Watched = Seats.FirstOrDefault();
            // the game's road distances, copied at load (a loading screen) rather than in the first hour's copy
            Capture.ResetRoads();
            long tr = Perf.Now();
            RoadBook roads = Capture.RoadsFor(Seats.Cast<IFaction>().ToList(), Clan.PlayerClan?.MapFaction);
            Log.Line("roads: the game's road distances copied: " + (roads?.Rows.Count ?? 0) + " holdings, " + (roads?.Pairs ?? 0) + " pairs, in " + Perf.Ms(tr).ToString("0", Text.Inv) + " ms");
            string id = CampaignId;
            // the game's own history of the watched realm starts its chronicle (only for the time before our own lines)
            long th = Perf.Now();
            var histories = new Dictionary<string, List<ChronicleEntry>>();
            foreach (Kingdom k in Seats)
            {
                List<ChronicleEntry> fromGame = GameHistory.Collect(k, out int scanned, out int unread);
                histories[k.StringId] = fromGame;
                Diag.Event("chronicle", "game history read", new { realm = k.StringId, entries = scanned, about_the_realm = fromGame.Count, unread, ms = Math.Round(Perf.Ms(th), 1) });
            }
            List<string> realms = Seats.Select(k => k.StringId).ToList();
            double loadedAt = LoadedAt;
            Background.Start();
            // which records are this game's history: its save's note in the campaign's saves.jsonl, found by name and hour (History)
            string save = null;
            try { save = TaleWorlds.Core.MBSaveLoad.ActiveSaveSlotName; }
            catch (Exception e) { Diag.Swallowed("reading the save's name", e); }
            History history = History.Of(LoadId, loadedAt, save, Paths.Data(id), save == null);
            History = history;
            // the lieutenant's conversation of this save, and its own Party AI Controls orders (0.11.1)
            try { Lieutenant.Restore(history); }
            catch (Exception e) { Diag.Error("the lieutenant's conversation at the load", e); }
            try { PacLink.Reset(history); }
            catch (Exception e) { Diag.Error("the lieutenant's Party AI Controls record at the load", e); }
            // a test run (0.11, docs/RUNS.md): the run's save readies one; one of a running run's own saves carries it on
            try { Run.OnLoad(save, loadedAt, LoadId); }
            catch (Exception e) { Diag.Error("the test run at the load", e); }
            Background.Post("new campaign", () => Brain.Reset(id, loadedAt, configs, histories, history));
            AgentChannel.Start();
            GameHooks.Apply();
            MapExport.Start();
            LordWatch.Refresh();
            Log.Line("session started: campaign " + id + ", " + Kingdom.All.Count + " kingdoms, " + MobileParty.All.Count + " parties, "
                     + Settlement.All.Count + " settlements; " + warmed + " names prepared in " + warmMs.ToString("0", Text.Inv) + " ms");
            Log.Line("watching: " + WatchedSummary());
            Log.Line("history: " + history);
            Log.Line("at load: " + Orders.FrozenAtLoad());
            Log.Line("files for this campaign: " + Paths.Data(id));
            for (int i = 0; i < configs.Count; i++)
                Log.Line("agent for " + Names.Faction(Seats[i]) + ": " + AgentChannel.ModeText(configs[i].Mode, configs[i].Lords) + " Folder: " + (configs[i].Workspace ?? configs[i].ChannelDir));
            foreach (string u in Unusable) Log.Line("agent not used: " + u);
            foreach (string u in SetAside) Log.Line("agent switched off, set aside: " + u);
            Diag.Event("session", "campaign started", new { campaign = id, watched = Watched?.StringId, agents = configs.Select(c => new { c.Realm, c.Mode, c.Workspace }).ToList(), unusable = Unusable.ToList(), detail = Settings.Detail });
            SelfCheck.Run();
        }

        public static void Stop()
        {
            Running = false;
            Live.CampaignRunning = false;
            Watched = null;
            Seats = new List<Kingdom>();
            Configs = new List<AgentConfig>();
            History = null;
            Diag.Event("session", "campaign ended");
            Background.Post("flush", Files.FlushAll);
            Background.Post("summary", () => Diag.Summary(false));
        }

        public static Kingdom Resolve(string watch)
        {
            if (string.IsNullOrWhiteSpace(watch) || Campaign.Current == null) return null;
            // the lieutenant's realm (0.10): whichever realm the player's clan is in at this load
            if (string.Equals(watch.Trim(), "player", StringComparison.OrdinalIgnoreCase)) return Clan.PlayerClan?.Kingdom is Kingdom pk && !pk.IsEliminated ? pk : null;
            List<Kingdom> all = Kingdom.All.Where(k => k != null && !k.IsEliminated).ToList();
            Kingdom k1 = all.FirstOrDefault(k => string.Equals(k.StringId, watch, StringComparison.OrdinalIgnoreCase));
            if (k1 != null) return k1;
            string q = Text.Plain(watch);
            return all.FirstOrDefault(k => Text.Plain(Names.Faction(k)) == q) ?? all.FirstOrDefault(k => Text.Plain(Names.Faction(k)).Contains(q));
        }

        /// <summary>Is this one of the watched realms?</summary>
        public static bool IsSeat(IFaction f) => f != null && Seats.Contains(f as Kingdom);
        public static bool IsSeat(string id) => id != null && Seats.Any(k => k.StringId == id);
        /// <summary>The watched realm with this id (null: none).</summary>
        public static Kingdom SeatOf(string id) => id == null ? null : Seats.FirstOrDefault(k => k.StringId == id);

        public static string WatchedSummary()
        {
            if (Seats.Count == 0) return Summary(null);
            return string.Join("; and ", Seats.Select(Summary));
        }

        private static string Summary(Kingdom k)
        {
            if (k == null) return "nothing: no realm matches '" + Settings.Watch + "'. Realms: " + string.Join(", ", Kingdom.All.Where(x => !x.IsEliminated).Select(x => Names.Faction(x) + " (" + x.StringId + ")"));
            int lords = MobileParty.AllLordParties.Count(p => p != null && p.IsActive && p.MapFaction == k);
            int parties = MobileParty.All.Count(p => p != null && p.IsActive && p.MapFaction == k);
            int forts = k.Settlements.Count(s => s.IsFortification);
            int villages = k.Settlements.Count(s => s.IsVillage);
            List<string> wars = Kingdom.All.Where(o => o != k && !o.IsEliminated && k.IsAtWarWith(o)).Select(Names.Faction).ToList();
            return Names.Faction(k) + " (" + k.StringId + "), ruled by " + Names.Hero(k.Leader) + ": " + lords + " lords leading parties, "
                   + parties + " parties in all, " + forts + " castles and towns, " + villages + " villages; at war with "
                   + (wars.Count > 0 ? string.Join(", ", wars) : "no one");
        }
    }

    /// <summary>
    /// Wires the bridge into the campaign: the hourly copy, the per-tick recorder poll and lord watch, the save
    /// hand-back, and every event the notices and the recorder listen to. Each handler is timed by name and marked
    /// as running (so a freeze can say whether our code was on the game's thread), and every error is counted.
    /// </summary>
    public class BridgeBehavior : CampaignBehaviorBase
    {
        private DateTime _lastDayAt = DateTime.Now;
        private int _pokesAtDayStart, _decisionsAtDayStart;
        private double _lastSightCheck = -1;
        private int _hoursThisSession;
        private static readonly Diag.HookStat HourlyHook = Diag.Hook("hourly copy", 33);
        private static readonly Diag.HookStat TickHook = Diag.Hook("per-tick work");

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.DailyTickClanEvent.AddNonSerializedListener(this, OnDailyClan);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, OnBeforeSave);
            CampaignEvents.OnSaveOverEvent.AddNonSerializedListener(this, OnSaveOver);

            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeace);
            CampaignEvents.OnAllianceStartedEvent.AddNonSerializedListener(this, OnAllianceStarted);
            CampaignEvents.OnAllianceEndedEvent.AddNonSerializedListener(this, OnAllianceEnded);
            CampaignEvents.OnCallToWarAgreementStartedEvent.AddNonSerializedListener(this, OnCallToWarStarted);
            CampaignEvents.OnCallToWarAgreementEndedEvent.AddNonSerializedListener(this, OnCallToWarEnded);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, OnSiegeEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnPrisonerReleased);
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageRaided);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnOwnerChanged);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, OnPartyJoinedArmy);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnPartyDestroyed);
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnBattleStarted);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnBattleEnded);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnPrisonerTaken);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnDecisionConcluded);
            CampaignEvents.OnPeaceOfferedToPlayerEvent.AddNonSerializedListener(this, OnPeaceOffered);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
        }

        /// <summary>Nothing of ours goes into the save.</summary>
        public override void SyncData(IDataStore dataStore) { }

        /// <summary>Runs one of our handlers: marked as running, timed by name, and any error counted with its trace.</summary>
        private static void Guard(string what, Action a)
        {
            long t = Perf.Now();
            string before = Watchdog.Doing;
            Watchdog.Doing = what;
            try { a(); }
            catch (Exception e) { Diag.Error(what, e); }
            finally
            {
                Watchdog.Doing = before;
                Perf.Add(t);
                Diag.Took(Diag.Hook(what), t, what != "session start");   // the load's one-off warm-up is not a stutter
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _lastDayAt = DateTime.Now;
            _lastSightCheck = -1;
            _hoursThisSession = 0;
            Guard("session start", Bridge.Start);
        }

        private void OnHourlyTick()
        {
            if (!Bridge.Running || !Settings.Bridge) return;
            long t = Perf.Now();
            Watchdog.Doing = "hourly copy";
            try
            {
                double hours = CampaignTime.Now.ToHours;
                Watchdog.HourlyBeat(hours);
                Snapshot snap = Capture.Take(Bridge.Seats, true);
                if (_lastSightCheck < 0 || hours - _lastSightCheck >= 3)
                {
                    _lastSightCheck = hours;
                    snap.Sight = Vision.Audit();
                }
                Orders.Hourly();
                Wages.Hourly();
                try { Votes.Hourly(); }
                catch (Exception e) { Diag.Error("the realms' votes", e); }
                LordWatch.Refresh();
                Recorder.PollVassalOrders();
                Background.Post("hourly view and pokes", () => Brain.OnHour(snap));
                try { Run.OnHour(snap); }
                catch (Exception e) { Diag.Error("the test run's hour", e); }
                if (++_hoursThisSession == 6 && Reasons.Active && Diag.CountOf("ai:thoughts") == 0 && LordWatch.Count > 0)
                    Diag.Alarm("ai_reasons_silent", "The AI-reasons hook is attached but recorded nothing in 6 in-game hours, although the watched realm has lords. "
                                                     + "Another mod may have replaced the game's decision step for them.");
            }
            catch (Exception e) { Diag.Error("hourly copy", e); }
            finally
            {
                Watchdog.Doing = null;
                Perf.Capture(t);
                Diag.Took(HourlyHook, t);
            }
        }

        private void OnTick(float dt)
        {
            if (Bridge.Running) Watchdog.CampaignBeat(CampaignTime.Now.ToHours);
            Perf.OnTick();
            long t = Perf.Now();
            Watchdog.Doing = "per-tick work";
            try
            {
                Background.DrainToMain();
                if (!Bridge.Running || !Settings.Bridge) return;
                Recorder.Poll();
                LordWatch.Tick();
            }
            catch (Exception e) { Diag.Error("per-tick work", e); }
            finally
            {
                Watchdog.Doing = null;
                Perf.Add(t);
                Diag.Took(TickHook, t);
            }
        }

        private void OnDailyTick()
        {
            Guard("daily line", () =>
            {
                DateTime now = DateTime.Now;
                double seconds = (now - _lastDayAt).TotalSeconds;
                _lastDayAt = now;
                double avg = Perf.Captures > 0 ? Perf.CaptureSumMs / Perf.Captures : 0;
                int pokes = Brain.PokesWatched, decisions = Brain.DecisionsWritten;
                Log.Line(Text.When(CampaignTime.Now.ToHours) + ": " + seconds.ToString("0.0", Text.Inv) + " s since the previous day (speed " + Watchdog.Speed + "); hourly copy "
                         + avg.ToString("0.00", Text.Inv) + " ms on average (max " + Perf.CaptureMaxMs.ToString("0.00", Text.Inv) + "); "
                         + (pokes - _pokesAtDayStart) + " pokes for the watched realm and " + (decisions - _decisionsAtDayStart)
                         + " of your decisions recorded today; worker jobs " + Background.Ran + ", dropped " + Background.Dropped);
                Diag.Event("day", Text.When(CampaignTime.Now.ToHours), new { real_seconds = Math.Round(seconds, 1), speed = Watchdog.Speed, pokes = pokes - _pokesAtDayStart,
                                                                             decisions = decisions - _decisionsAtDayStart, hourly_ms_avg = Math.Round(avg, 3) });
                _pokesAtDayStart = pokes;
                _decisionsAtDayStart = decisions;
            });
            if (Bridge.Running) Guard("daily sweep", Orders.DailySweep);
            if (Bridge.Running && Settings.Bridge) Guard("recruitment check", () => Recruitment.Daily());
        }

        /// <summary>Every clan's day (after the game's own daily tick for it, which sets its wage caps): a watched realm's clan's forecast, and our caps put back. Many a day, so no Guard.</summary>
        private void OnDailyClan(Clan clan)
        {
            if (!Bridge.Running) return;
            try { Purse.OnDailyClan(clan); }
            catch (Exception e) { Diag.Error("a clan's day", e); }
        }

        /// <summary>Every recruit in the world: a hot path, so no Guard (it would allocate); the watched realm's are counted.</summary>
        private void OnTroopRecruited(Hero recruiter, Settlement where, Hero source, CharacterObject troop, int amount)
        {
            if (!Bridge.Running) return;
            try
            {
                Recruitment.OnTroopRecruited(recruiter, where, source, troop, amount);
                Run.OnRecruited(recruiter, amount);
            }
            catch (Exception e) { Diag.Error("recruit count", e); }
        }

        private void OnBeforeSave() => Guard("save hand-back", () => { Orders.HandBackForSave(); Wages.HandBackForSave(); });
        private void OnSaveOver(bool saved, string name) => Guard("save take-back", () =>
        {
            Orders.TakeBackAfterSave(saved, name);
            Wages.TakeBackAfterSave();
            if (saved) Run.OnSaved(name);
            // the save's history note (History): which records its world has lived through, so loading it later finds exactly those
            if (!saved || Bridge.History == null || string.IsNullOrEmpty(name)) return;
            object note = Bridge.History.NoteFor(name, CampaignTime.Now.ToHours);
            string file = History.NotesFile(Paths.Data(Bridge.CampaignId));
            Background.Post("save history note", () => { Files.Append(file, Json.Line(note)); Files.FlushAll(); });
            Diag.Event("history", "save noted", new { save = name, load = Bridge.History.Label });
        });

        private void OnWarDeclared(IFaction a, IFaction b, DeclareWarAction.DeclareWarDetail d) => Guard("war notice", () => { Notices.OnWarDeclared(a, b, d); Run.OnStance(a, b, true); });
        private void OnPeace(IFaction a, IFaction b, MakePeaceAction.MakePeaceDetail d) => Guard("peace notice", () => { Notices.OnPeace(a, b, d); Run.OnStance(a, b, false); });
        private void OnAllianceStarted(Kingdom a, Kingdom b) => Guard("alliance notice", () => Notices.OnAllianceStarted(a, b));
        private void OnAllianceEnded(Kingdom a, Kingdom b) => Guard("alliance notice", () => Notices.OnAllianceEnded(a, b));
        private void OnCallToWarStarted(Kingdom a, Kingdom b, Kingdom c) => Guard("call-to-war notice", () => Notices.OnCallToWarStarted(a, b, c));
        private void OnCallToWarEnded(Kingdom a, Kingdom b, Kingdom c) => Guard("call-to-war notice", () => Notices.OnCallToWarEnded(a, b, c));

        private void OnSiegeStarted(SiegeEvent se) => Guard("siege", () => { Notices.OnSiegeStarted(se); Recorder.OnSiegeStarted(se); Run.OnSiege(se, true); });
        private void OnSiegeEnded(SiegeEvent se) => Guard("siege notice", () => { Notices.OnSiegeEnded(se); Run.OnSiege(se, false); });
        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail d, bool notify) => Guard("death notice", () => { Notices.OnHeroKilled(victim, killer, d, notify); Run.OnLordKilled(victim, killer, d); });
        private void OnPrisonerReleased(Hero h, PartyBase party, IFaction capturers, EndCaptivityDetail d, bool notify) => Guard("release notice", () => { Notices.OnPrisonerReleased(h, party, capturers, d, notify); Run.OnLordFreed(h, d); });
        private void OnVillageRaided(Village v) => Guard("raid", () => { Notices.OnVillageRaided(v); Recorder.OnVillageRaided(v); });
        private void OnVillageLooted(Village v) => Guard("looting notice", () => { Notices.OnVillageLooted(v); Run.OnVillageLooted(v); });

        private void OnOwnerChanged(Settlement s, bool open, Hero newOwner, Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d)
            => Guard("holding notice", () => { Notices.OnOwnerChanged(s, open, newOwner, oldOwner, capturer, d); Run.OnOwnerChanged(s, newOwner, oldOwner, d); });

        private void OnArmyCreated(Army a) => Guard("army", () => { Recorder.OnArmyCreated(a); Notices.OnArmyCreated(a); Run.OnArmy(a, true); });
        private void OnArmyDispersed(Army a, Army.ArmyDispersionReason r, bool mine) => Guard("army notice", () => { Notices.OnArmyDispersed(a, r, mine); Run.OnArmy(a, false, r.ToString()); });
        private void OnPartyJoinedArmy(MobileParty p) => Guard("army", () => Recorder.OnPartyJoinedArmy(p));
        private void OnPartyDestroyed(MobileParty p, PartyBase by) => Guard("party notice", () => Notices.OnPartyDestroyed(p, by));

        private void OnBattleStarted(MapEvent me, PartyBase attacker, PartyBase defender) => Guard("battle", () => { Notices.OnBattleStarted(me, attacker, defender); Recorder.OnBattleStarted(me, attacker, defender); Run.OnBattleStarted(me); });
        private void OnBattleEnded(MapEvent me) => Guard("battle notice", () => { Notices.OnBattleEnded(me); Run.OnBattleEnded(me); });
        private void OnClanChangedKingdom(Clan clan, Kingdom from, Kingdom to, TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction.ChangeKingdomActionDetail detail, bool notify) => Guard("clan moved", () => Run.OnClanChangedKingdom(clan, from, to));
        private void OnPrisonerTaken(PartyBase capturer, Hero h) => Guard("prisoner notice", () => { Notices.OnPrisonerTaken(capturer, h); Run.OnLordTaken(capturer, h); });

        private void OnDecisionAdded(KingdomDecision d, bool involved) => Guard("realm vote", () => { Notices.OnDecisionAdded(d, involved); Recorder.OnDecisionAdded(d, involved); });
        private void OnDecisionConcluded(KingdomDecision d, DecisionOutcome o, bool involved) => Guard("realm vote", () => { Notices.OnDecisionConcluded(d, o, involved); Recorder.OnDecisionConcluded(d, o, involved); });
        private void OnPeaceOffered(IFaction f, int amount, int days) => Guard("peace offer", () => Notices.OnPeaceOfferedToPlayer(f, amount, days));

        private void OnSettlementEntered(MobileParty p, Settlement s, Hero h) => Guard("settlement", () => Recorder.OnSettlementEntered(p, s, h));
        private void OnSettlementLeft(MobileParty p, Settlement s) => Guard("settlement", () => Recorder.OnSettlementLeft(p, s));
        private void OnConversationEnded(IEnumerable<CharacterObject> chars) => Guard("conversation", () => Recorder.OnConversationEnded(chars));
    }
}
