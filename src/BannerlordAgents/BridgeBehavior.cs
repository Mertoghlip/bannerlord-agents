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

        public static void Start()
        {
            Running = true;
            CampaignId = Campaign.Current?.UniqueGameId ?? "unknown";
            Orders.Reset();
            Recorder.Reset();
            PacReader.Reset();
            Vision.Reset();
            Names.Clear();
            Perf.ResetCapture();
            Calendar.Capture();
            long t = Perf.Now();
            int warmed = Names.WarmUp();
            double warmMs = Perf.Ms(t);
            Watched = Resolve(Settings.Watch);
            string id = CampaignId;
            Background.Start();
            Background.Post(() => Brain.Reset(id));
            Log.Line("session started: campaign " + id + ", " + Kingdom.All.Count + " kingdoms, " + MobileParty.All.Count + " parties, "
                     + Settlement.All.Count + " settlements; " + warmed + " names prepared in " + warmMs.ToString("0", Text.Inv) + " ms");
            Log.Line("watching: " + WatchedSummary());
            Log.Line("at load: " + Orders.FrozenAtLoad());
            Log.Line("files for this campaign: " + Paths.Data(id));
        }

        public static void Stop()
        {
            Running = false;
            Watched = null;
            Background.Post(Files.FlushAll);
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
    /// Wires the bridge into the campaign: the hourly copy, the per-tick recorder poll and performance count, the
    /// save hand-back, and every event the notices and the recorder listen to. Each handler is timed, so the bridge's
    /// whole cost on the game's thread shows in the performance log.
    /// </summary>
    public class BridgeBehavior : CampaignBehaviorBase
    {
        private DateTime _lastDayAt = DateTime.Now;
        private int _pokesAtDayStart, _decisionsAtDayStart;
        private static readonly HashSet<string> Failed = new HashSet<string>();

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
        }

        /// <summary>Nothing of ours goes into the save.</summary>
        public override void SyncData(IDataStore dataStore) { }

        private static void Guard(string what, Action a)
        {
            long t = Perf.Now();
            try { a(); }
            catch (Exception e)
            {
                if (Failed.Add(what)) Log.Line(what + " failed (logged once per session): " + e);
            }
            Perf.Add(t);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            Failed.Clear();
            _lastDayAt = DateTime.Now;
            Guard("session start", Bridge.Start);
        }

        private void OnHourlyTick()
        {
            if (!Bridge.Running || !Settings.Bridge) return;
            long t = Perf.Now();
            try
            {
                Snapshot snap = Capture.Take(Bridge.Watched, true);
                Orders.Hourly();
                Recorder.PollVassalOrders();
                Background.Post(() => Brain.OnHour(snap));
            }
            catch (Exception e)
            {
                if (Failed.Add("hourly")) Log.Line("hourly copy failed (logged once per session): " + e);
            }
            Perf.Capture(t);
        }

        private void OnTick(float dt)
        {
            Perf.OnTick();
            Background.DrainToMain();
            if (!Bridge.Running || !Settings.Bridge) return;
            Guard("recorder", Recorder.Poll);
        }

        private void OnDailyTick()
        {
            try
            {
                DateTime now = DateTime.Now;
                double seconds = (now - _lastDayAt).TotalSeconds;
                _lastDayAt = now;
                double avg = Perf.Captures > 0 ? Perf.CaptureSumMs / Perf.Captures : 0;
                int pokes = Brain.PokesWatched, decisions = Brain.DecisionsWritten;
                Log.Line(Text.When(CampaignTime.Now.ToHours) + ": " + seconds.ToString("0.0", Text.Inv) + " s since the previous day; hourly copy "
                         + avg.ToString("0.00", Text.Inv) + " ms on average (max " + Perf.CaptureMaxMs.ToString("0.00", Text.Inv) + "); "
                         + (pokes - _pokesAtDayStart) + " pokes for the watched realm and " + (decisions - _decisionsAtDayStart)
                         + " of your decisions recorded today; worker jobs " + Background.Ran + ", dropped " + Background.Dropped);
                _pokesAtDayStart = pokes;
                _decisionsAtDayStart = decisions;
            }
            catch { }
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
        private void OnVillageRaided(Village v) => Guard("raid", () => { Notices.OnVillageRaided(v); Recorder.OnVillageRaided(v); });
        private void OnVillageLooted(Village v) => Guard("looting notice", () => Notices.OnVillageLooted(v));

        private void OnOwnerChanged(Settlement s, bool open, Hero newOwner, Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail d)
            => Guard("holding notice", () => Notices.OnOwnerChanged(s, open, newOwner, oldOwner, capturer, d));

        private void OnArmyCreated(Army a) => Guard("army", () => Recorder.OnArmyCreated(a));
        private void OnArmyDispersed(Army a, Army.ArmyDispersionReason r, bool mine) => Guard("army notice", () => Notices.OnArmyDispersed(a, r, mine));
        private void OnPartyJoinedArmy(MobileParty p) => Guard("army", () => Recorder.OnPartyJoinedArmy(p));
        private void OnPartyDestroyed(MobileParty p, PartyBase by) => Guard("party notice", () => Notices.OnPartyDestroyed(p, by));

        private void OnBattleStarted(MapEvent me, PartyBase attacker, PartyBase defender) => Guard("battle", () => Recorder.OnBattleStarted(me, attacker, defender));
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
