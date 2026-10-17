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
        public static Kingdom Watched;
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
            Notices.Reset();
            Recorder.Reset();
            PacReader.Reset();
            Vision.Reset();
            LordWatch.Reset();
            Names.Clear();
            Perf.ResetCapture();
            Recruitment.Reset();
            Calendar.Capture();
            long t = Perf.Now();
            int warmed = Names.WarmUp();
            double warmMs = Perf.Ms(t);
            Watched = Resolve(Settings.Watch);
            string id = CampaignId;
            // the game's own history of the watched realm starts its chronicle (only for the time before our own lines)
            long th = Perf.Now();
            List<ChronicleEntry> history = GameHistory.Collect(Watched, out int scanned, out int unread);
            Diag.Event("chronicle", "game history read", new { realm = Watched?.StringId, entries = scanned, about_the_realm = history.Count, unread, ms = Math.Round(Perf.Ms(th), 1) });
            string realm = Watched?.StringId;
            double loadedAt = LoadedAt;
            Background.Start();
            Background.Post("new campaign", () => Brain.Reset(id, loadedAt, realm, history));
            AgentChannel.Start();
            GameHooks.Apply();
            MapExport.Start();
            LordWatch.Refresh();
            Log.Line("session started: campaign " + id + ", " + Kingdom.All.Count + " kingdoms, " + MobileParty.All.Count + " parties, "
                     + Settlement.All.Count + " settlements; " + warmed + " names prepared in " + warmMs.ToString("0", Text.Inv) + " ms");
            Log.Line("watching: " + WatchedSummary());
            Log.Line("at load: " + Orders.FrozenAtLoad());
            Log.Line("files for this campaign: " + Paths.Data(id));
            Log.Line("agent channel: " + AgentChannel.ModeText() + " Folder: " + Settings.AgentFolder);
            Diag.Event("session", "campaign started", new { campaign = id, watched = Watched?.StringId, agent = Settings.Agent, detail = Settings.Detail });
            SelfCheck.Run();
        }

        public static void Stop()
        {
            Running = false;
            Live.CampaignRunning = false;
            Watched = null;
            Diag.Event("session", "campaign ended");
            Background.Post("flush", Files.FlushAll);
            Background.Post("summary", () => Diag.Summary(false));
        }

        public static Kingdom Resolve(string watch)
        {
            if (string.IsNullOrWhiteSpace(watch) || Campaign.Current == null) return null;
            List<Kingdom> all = Kingdom.All.Where(k => k != null && !k.IsEliminated).ToList();
            Kingdom k1 = all.FirstOrDefault(k => string.Equals(k.StringId, watch, StringComparison.OrdinalIgnoreCase));
            if (k1 != null) return k1;
            string q = Text.Plain(watch);
            return all.FirstOrDefault(k => Text.Plain(Names.Faction(k)) == q) ?? all.FirstOrDefault(k => Text.Plain(Names.Faction(k)).Contains(q));
        }

        public static string WatchedSummary()
        {
            Kingdom k = Watched;
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
        private static readonly Diag.HookStat HourlyHook = Diag.Hook("hourly copy");
        private static readonly Diag.HookStat TickHook = Diag.Hook("per-tick work");

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
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
                Snapshot snap = Capture.Take(Bridge.Watched, true);
                if (_lastSightCheck < 0 || hours - _lastSightCheck >= 3)
                {
                    _lastSightCheck = hours;
                    snap.Sight = Vision.Audit();
                }
                Orders.Hourly();
                LordWatch.Refresh();
                Recorder.PollVassalOrders();
                Background.Post("hourly view and pokes", () => Brain.OnHour(snap));
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

        /// <summary>Every recruit in the world: a hot path, so no Guard (it would allocate); the watched realm's are counted.</summary>
        private void OnTroopRecruited(Hero recruiter, Settlement where, Hero source, CharacterObject troop, int amount)
        {
            if (!Bridge.Running) return;
            try { Recruitment.OnTroopRecruited(recruiter, where, source, troop, amount); }
            catch (Exception e) { Diag.Error("recruit count", e); }
        }

        private void OnBeforeSave() => Guard("save hand-back", Orders.HandBackForSave);
        private void OnSaveOver(bool saved, string name) => Guard("save take-back", () => Orders.TakeBackAfterSave(saved, name));

        private void OnWarDeclared(IFaction a, IFaction b, DeclareWarAction.DeclareWarDetail d) => Guard("war notice", () => Notices.OnWarDeclared(a, b, d));
        private void OnPeace(IFaction a, IFaction b, MakePeaceAction.MakePeaceDetail d) => Guard("peace notice", () => Notices.OnPeace(a, b, d));
        private void OnAllianceStarted(Kingdom a, Kingdom b) => Guard("alliance notice", () => Notices.OnAllianceStarted(a, b));
        private void OnAllianceEnded(Kingdom a, Kingdom b) => Guard("alliance notice", () => Notices.OnAllianceEnded(a, b));
        private void OnCallToWarStarted(Kingdom a, Kingdom b, Kingdom c) => Guard("call-to-war notice", () => Notices.OnCallToWarStarted(a, b, c));
        private void OnCallToWarEnded(Kingdom a, Kingdom b, Kingdom c) => Guard("call-to-war notice", () => Notices.OnCallToWarEnded(a, b, c));

        private void OnSiegeStarted(SiegeEvent se) => Guard("siege", () => { Notices.OnSiegeStarted(se); Recorder.OnSiegeStarted(se); });
        private void OnSiegeEnded(SiegeEvent se) => Guard("siege notice", () => Notices.OnSiegeEnded(se));
        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail d, bool notify) => Guard("death notice", () => Notices.OnHeroKilled(victim, killer, d, notify));
        private void OnPrisonerReleased(Hero h, PartyBase party, IFaction capturers, EndCaptivityDetail d, bool notify) => Guard("release notice", () => Notices.OnPrisonerReleased(h, party, capturers, d, notify));
        private void OnVillageRaided(Village v) => Guard("raid", () => { Notices.OnVillageRaided(v); Recorder.OnVillageRaided(v); });
        private void OnVillageLooted(Village v) => Guard("looting notice", () => Notices.OnVillageLooted(v));

        private void OnOwnerChanged(Settlement s, bool open, Hero newOwner, Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d)
            => Guard("holding notice", () => Notices.OnOwnerChanged(s, open, newOwner, oldOwner, capturer, d));

        private void OnArmyCreated(Army a) => Guard("army", () => { Recorder.OnArmyCreated(a); Notices.OnArmyCreated(a); });
        private void OnArmyDispersed(Army a, Army.ArmyDispersionReason r, bool mine) => Guard("army notice", () => Notices.OnArmyDispersed(a, r, mine));
        private void OnPartyJoinedArmy(MobileParty p) => Guard("army", () => Recorder.OnPartyJoinedArmy(p));
        private void OnPartyDestroyed(MobileParty p, PartyBase by) => Guard("party notice", () => Notices.OnPartyDestroyed(p, by));

        private void OnBattleStarted(MapEvent me, PartyBase attacker, PartyBase defender) => Guard("battle", () => { Notices.OnBattleStarted(me, attacker, defender); Recorder.OnBattleStarted(me, attacker, defender); });
        private void OnBattleEnded(MapEvent me) => Guard("battle notice", () => Notices.OnBattleEnded(me));
        private void OnPrisonerTaken(PartyBase capturer, Hero h) => Guard("prisoner notice", () => Notices.OnPrisonerTaken(capturer, h));

        private void OnDecisionAdded(KingdomDecision d, bool involved) => Guard("realm vote", () => { Notices.OnDecisionAdded(d, involved); Recorder.OnDecisionAdded(d, involved); });
        private void OnDecisionConcluded(KingdomDecision d, DecisionOutcome o, bool involved) => Guard("realm vote", () => { Notices.OnDecisionConcluded(d, o, involved); Recorder.OnDecisionConcluded(d, o, involved); });
        private void OnPeaceOffered(IFaction f, int amount, int days) => Guard("peace offer", () => Notices.OnPeaceOfferedToPlayer(f, amount, days));

        private void OnSettlementEntered(MobileParty p, Settlement s, Hero h) => Guard("settlement", () => Recorder.OnSettlementEntered(p, s, h));
        private void OnSettlementLeft(MobileParty p, Settlement s) => Guard("settlement", () => Recorder.OnSettlementLeft(p, s));
        private void OnConversationEnded(IEnumerable<CharacterObject> chars) => Guard("conversation", () => Recorder.OnConversationEnded(chars));
    }
}
