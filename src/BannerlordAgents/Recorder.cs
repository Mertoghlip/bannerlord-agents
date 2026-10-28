using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// Records the player's own decisions on the campaign map (never inside battles), each with what the player could
    /// see at that moment, in the same form as an agent's record. Decisions:
    /// - where the party goes: a holding, a point, a party to chase, a siege, a raid, a patrol, someone to follow;
    /// - attacks, storms, sallies, sieges and raids actually begun; fights declined or fled; how long the party stayed in a
    ///   holding; armies formed and joined, lords called into the player's army or sent from it, the army broken up;
    /// - in towns and castles (0.13): troops recruited, loot sold and goods bought, prisoners sold or given to a dungeon,
    ///   troops left in or taken from a garrison, the wage limits of the party and of the player's garrisons;
    /// - the realm's business: proposals made, votes cast and decided, fiefs given, mercenary clans hired and let go, the
    ///   answers to offers of peace, alliance and calls to war, and the orders given to vassals in conversation (PacReader;
    ///   Party AI Controls' own refill trips are kept apart, as not the player's);
    /// - what the player says to the lieutenant, and what the player does with its proposals.
    ///
    /// Since 0.13 (docs/PLAN-0.13.md C2) only while the player rules the player's realm himself (YouRule): in the test runs
    /// where an agent commands it, the game's player clan is still the player's, so the realm's own proposals and Party AI
    /// Controls' refills were credited to him. Every record carries the test run and its arm, and the worker keeps the realm's
    /// report of that hour with it (Brain.WriteDecision).
    ///
    /// Map clicks are frequent and many are corrections, so a movement counts as a decision only once it has held
    /// for a quarter of an in-game hour (game time only runs unpaused, so clicking around while paused never counts).
    /// Every raw change is kept separately in clicks.jsonl. Entering or leaving a settlement, being attacked and the
    /// realm's votes are kept as events, not decisions. Game's thread only; the worker writes the files.
    /// </summary>
    internal static class Recorder
    {
        private struct Intent
        {
            public AiBehavior B;
            public Settlement S;
            public MobileParty P;
            public float X, Y;
            public bool Point;
        }

        private const double SettleHours = 0.25;
        private const float SamePoint = 10f;

        private static Intent _current, _pendingIntent, _lastCommitted;
        private static bool _have, _pending, _haveCommitted;
        private static double _pendingSince, _lastCommittedAt;
        private static string _talkedTo;
        private static double _talkedAt = -1;
        private static bool _chain, _inArmy;
        private static double _chainStart;
        private static int _correctionsBase;

        public static int Clicks, Corrections, Committed;
        private static double _settle = -1;
        private static int _merged;

        // ---------- whose record it is (0.13) ----------

        private static bool _youRule;

        /// <summary>The player rules the player's realm himself, as last worked out (Refresh): the record is his.</summary>
        public static bool YouRuleNow => _youRule;

        private static bool On => Bridge.Running && Settings.Bridge && Settings.Record && _youRule;

        /// <summary>
        /// Does the player rule his realm himself? His own character leads it, or is a lord of a realm no live agent commands (or
        /// of no realm at all). Not while a wanderer stands in for him in the runs an agent commands his kingdom (agents.run
        /// playerless), nor while his kingdom's own agent is live.
        /// </summary>
        public static bool YouRule()
        {
            Hero me = Hero.MainHero;
            if (me == null) return false;
            Kingdom k = me.MapFaction as Kingdom;
            bool live = false;
            if (k != null)
                foreach (AgentConfig c in Bridge.Configs)
                    if (c.Realm == k.StringId && !c.IsLieutenant && c.Mode == "live") live = true;
            return RecordRules.Yours(me.IsAlive, me.Clan != null, k != null, k != null && k.Leader == me, me.IsLord, live);
        }

        /// <summary>Works out again whose the record is (at the load, every hour), and says so when it changes.</summary>
        public static void Refresh()
        {
            bool now;
            try { now = Campaign.Current != null && YouRule(); }
            catch (Exception e) { Diag.Swallowed("whether you rule your realm", e); now = false; }
            if (now == _youRule) return;
            _youRule = now;
            string why = now ? "you rule your realm: your moves are recorded" : "you do not rule your realm yourself now (an agent commands it, or another hero stands in for you): your moves are not recorded";
            Diag.Event("recorder", now ? "yours" : "not yours", new { why, hero = Hero.MainHero?.StringId });
            Log.Line("recorder: " + why);
        }

        public static void Reset()
        {
            _have = _pending = _haveCommitted = false;
            _talkedTo = null;
            _talkedAt = -1;
            _chain = _inArmy = false;
            Clicks = Corrections = Committed = 0;
            _basket = null;
            _inside = null;
            _met = null;
            _metBattle = false;
            ArmyMembers.Clear();
            _wageLimit = int.MinValue;
            GarrisonSettings.Clear();
            Offers.Clear();
            _youRule = false;
        }

        // ---------- movement, polled every campaign tick ----------

        public static void Poll()
        {
            if (!On) return;
            MobileParty mp = MobileParty.MainParty;
            if (mp == null || !mp.IsActive) return;
            // an encounter met in its menu and left without a battle (0.13): the fight declined
            if (_met != null) EncounterOver(mp);
            if (mp.AttachedTo != null)   // marching in someone else's army: not the player's call
            {
                if (!_inArmy) { _inArmy = true; Diag.Event("recorder", "paused", new { why = "you march in " + Names.Party(mp.AttachedTo) + "'s army" }); }
                _have = false;
                _pending = false;
                _chain = false;
                return;
            }
            if (_inArmy) { _inArmy = false; Diag.Event("recorder", "resumed"); }
            double t = CampaignTime.Now.ToHours;
            Intent now = Read(mp);
            if (!_have) { _current = now; _have = true; return; }
            if (!Same(now, _current))
            {
                _current = now;
                Clicks++;
                var click = new
                {
                    Type = "click", Hours = t, Wall = DateTime.Now.ToString("HH:mm:ss.fff"), Behavior = now.B.ToString(),
                    Settlement = now.S?.StringId, Party = now.P?.StringId, X = now.Point ? now.X : (float?)null, Y = now.Point ? now.Y : (float?)null, Run = RunTags.Id
                };
                Background.Post(() => Brain.WriteRecord("clicks.jsonl", click));
                bool repeat = _haveCommitted && Same(now, _lastCommitted) && t - _lastCommittedAt < 6;
                if (IsIntent(now) && !repeat)
                {
                    if (!_chain) { _chain = true; _chainStart = t; _correctionsBase = Corrections; }
                    if (_pending) Corrections++;
                    _pendingIntent = now;
                    _pendingSince = t;
                    _pending = true;
                }
            }
            if (_pending && t - _pendingSince >= SettleHours)
            {
                _pending = false;
                _lastCommitted = _pendingIntent;
                _lastCommittedAt = t;
                _haveCommitted = true;
                _settle = _chain ? t - _chainStart : SettleHours;
                _merged = _chain ? Corrections - _correctionsBase : 0;
                _chain = false;
                CommitMove(mp, _pendingIntent);
                _settle = -1;
            }
        }

        private static Intent Read(MobileParty mp)
        {
            var i = new Intent { B = mp.DefaultBehavior, S = mp.TargetSettlement, P = mp.TargetParty };
            if (i.B == AiBehavior.GoToPoint || (i.B == AiBehavior.PatrolAroundPoint && i.S == null))
            {
                CampaignVec2 tp = mp.TargetPosition;
                i.X = tp.X;
                i.Y = tp.Y;
                i.Point = true;
            }
            return i;
        }

        private static bool Same(Intent a, Intent b)
        {
            if (a.B != b.B || a.S != b.S || a.P != b.P || a.Point != b.Point) return false;
            if (!a.Point) return true;
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy < SamePoint * SamePoint;
        }

        private static bool IsIntent(Intent i)
        {
            switch (i.B)
            {
                case AiBehavior.GoToSettlement:
                case AiBehavior.BesiegeSettlement:
                case AiBehavior.AssaultSettlement:
                case AiBehavior.RaidSettlement:
                case AiBehavior.DefendSettlement:
                    return i.S != null;
                case AiBehavior.EngageParty:
                case AiBehavior.EscortParty:
                    return i.P != null;
                case AiBehavior.GoToPoint:
                    return i.Point;
                case AiBehavior.PatrolAroundPoint:
                    return i.S != null || i.Point;
                default:
                    return false;
            }
        }

        private static void CommitMove(MobileParty me, Intent i)
        {
            switch (i.B)
            {
                case AiBehavior.GoToSettlement:
                    Emit("go_to", "You set out for " + Describe(i.S) + ".", Target(i.S)); break;
                case AiBehavior.BesiegeSettlement:
                    Emit("march_to_besiege", "You marched to besiege " + Describe(i.S) + ".", Target(i.S)); break;
                case AiBehavior.AssaultSettlement:
                    Emit("assault", "You assaulted " + Describe(i.S) + ".", Target(i.S)); break;
                case AiBehavior.RaidSettlement:
                    Emit("march_to_raid", "You marched to raid " + Describe(i.S) + ".", Target(i.S)); break;
                case AiBehavior.DefendSettlement:
                    Emit("march_to_defend", "You marched to defend " + Describe(i.S) + ".", Target(i.S)); break;
                case AiBehavior.EngageParty:
                    Emit("chase", "You went after " + Describe(i.P, me) + ".", Target(i.P)); break;
                case AiBehavior.EscortParty:
                    Emit("follow", "You followed " + Describe(i.P, me) + ".", Target(i.P)); break;
                case AiBehavior.PatrolAroundPoint:
                    if (i.S != null) Emit("patrol", "You patrolled around " + Describe(i.S) + ".", Target(i.S));
                    else Emit("patrol", "You patrolled around a point " + NearText(i.X, i.Y) + ".", Point(i.X, i.Y));
                    break;
                case AiBehavior.GoToPoint:
                    Emit("go_to_point", "You marched toward a point " + NearText(i.X, i.Y) + ".", Point(i.X, i.Y)); break;
            }
        }

        // ---------- every in-game hour ----------

        /// <summary>The hour (game's thread): whose the record is, the recruits of a stop whose hour is over, wage limits changed, offers left unanswered.</summary>
        public static void Hourly()
        {
            Refresh();
            if (!On) return;
            double t = CampaignTime.Now.ToHours;
            if (_basket != null && t - _basket.Last >= 1) FlushBasket();
            try { Wages(); }
            catch (Exception e) { Diag.Swallowed("your wage limits for the record", e); }
            OffersUnanswered(t);
        }

        // ---------- decisions and events from the game's own events ----------

        public static void OnBattleStarted(MapEvent me, PartyBase attacker, PartyBase defender)
        {
            if (!On) return;
            PartyBase mine = MobileParty.MainParty?.Party;
            if (mine == null) return;
            if (_met != null && MobileParty.MainParty?.MapEvent == me) _metBattle = true;
            bool onAttack = me?.AttackerSide?.Parties != null && me.AttackerSide.Parties.Any(p => p.Party == mine);
            if (attacker == mine || (onAttack && (me.IsSiegeAssault || me.IsSallyOut)))
            {
                if (me != null && me.IsSiegeAssault)
                {
                    // a storm: how long the camp waited first, and what it had built (0.13)
                    SiegeEvent se = me.MapEventSettlement?.SiegeEvent;
                    double waited = se != null ? CampaignTime.Now.ToHours - se.SiegeStartTime.ToHours : double.NaN;
                    int built = 0;
                    try { if (me.MapEventSettlement != null) built = Campaign.Current.Models.CombatSimulationModel.GetNumberOfEquipmentsBuilt(me.MapEventSettlement); }
                    catch (Exception e) { Diag.Swallowed("the siege engines for the record", e); }
                    Emit("storm", "You stormed " + Describe(me.MapEventSettlement) + (double.IsNaN(waited) ? "" : " after " + Text.D((float)waited) + " in-game hours of siege") + ".",
                         Target(me.MapEventSettlement), detail: new { siege_hours = double.IsNaN(waited) ? (double?)null : Math.Round(waited, 1), engines_built = built });
                }
                else if (me != null && me.IsSallyOut) Emit("sally", "You sallied out of " + Describe(me.MapEventSettlement) + " against " + Describe(defender) + ".", Target(defender));
                else Emit("attack", "You attacked " + Describe(defender) + ".", Target(defender));
            }
            else if (defender == mine)
                Event("attacked", "You were attacked by " + Describe(attacker) + ".");
        }

        public static void OnSiegeStarted(SiegeEvent se)
        {
            if (!On || se?.BesiegerCamp?.LeaderParty != MobileParty.MainParty) return;
            Emit("besiege", "You laid siege to " + Describe(se.BesiegedSettlement) + ".", Target(se.BesiegedSettlement));
        }

        public static void OnVillageRaided(Village v)
        {
            if (!On || v?.Settlement == null) return;
            PartyBase raider = v.Settlement.Party?.MapEvent?.AttackerSide?.LeaderParty;
            if (raider == null || raider != MobileParty.MainParty?.Party) return;
            Emit("raid", "You raided " + Describe(v.Settlement) + ".", Target(v.Settlement));
        }

        public static void OnArmyCreated(Army a)
        {
            if (!On || a?.LeaderParty != MobileParty.MainParty) return;
            List<string> called = a.Parties.Where(p => p != a.LeaderParty).Select(Names.Party).ToList();
            foreach (MobileParty p in a.Parties) if (p != a.LeaderParty) ArmyMembers.Add(p.StringId);
            Emit("form_army", "You formed an army" + (called.Count > 0 ? " and called " + string.Join(", ", called) : "") + ".", null,
                 detail: new { lords = a.Parties.Where(p => p != a.LeaderParty).Select(p => p.LeaderHero?.StringId).ToList() });
        }

        public static void OnPartyJoinedArmy(MobileParty p)
        {
            if (!On || p?.Army == null) return;
            if (p == MobileParty.MainParty && p.Army.LeaderParty != p)
                Emit("join_army", "You joined the army of " + Names.Party(p.Army.LeaderParty) + ".", Target(p.Army.LeaderParty));
            else if (p.Army.LeaderParty == MobileParty.MainParty && p != MobileParty.MainParty)
            {
                // a lord called into the player's own army after it formed (0.13: a decision, the army's call)
                if (ArmyMembers.Add(p.StringId)) Emit("call_to_army", "You called " + Describe(p, MobileParty.MainParty) + " into your army.", Target(p), p.LeaderHero?.StringId, "add");
                else Event("joined_your_army", Names.Party(p) + " joined your army.");
            }
        }

        /// <summary>A lord leaving the player's army: sent away, or gone by the game's own rules.</summary>
        public static void OnPartyRemovedFromArmy(MobileParty p)
        {
            if (!On || p == null || p == MobileParty.MainParty || !ArmyMembers.Remove(p.StringId)) return;
            if (MobileParty.MainParty?.Army != null && MobileParty.MainParty.Army.LeaderParty == MobileParty.MainParty)
                Emit("sent_from_army", Names.Party(p) + " left your army.", Target(p), p.LeaderHero?.StringId, "release");
        }

        public static void OnArmyDispersed(Army a, Army.ArmyDispersionReason reason)
        {
            if (!On || a?.LeaderParty != MobileParty.MainParty) return;
            ArmyMembers.Clear();
            bool yours = reason == Army.ArmyDispersionReason.ObjectiveFinished || reason == Army.ArmyDispersionReason.DismissalRequestedWithInfluence || reason == Army.ArmyDispersionReason.Unknown;
            if (yours) Emit("army_disbanded", "You broke up your army.", null, verb: "disband", detail: new { reason = reason.ToString() });
            else Event("army_dispersed", "Your army broke up (" + reason + ").");
        }

        public static void OnSettlementEntered(MobileParty p, Settlement s, Hero h)
        {
            if (!On || p == null || p != MobileParty.MainParty) return;
            _inside = s;
            _insideSince = CampaignTime.Now.ToHours;
            Event("entered", "You entered " + Names.Settlement(s) + ".");
        }

        public static void OnSettlementLeft(MobileParty p, Settlement s)
        {
            if (!On || p == null || p != MobileParty.MainParty) return;
            FlushBasket();
            double stayed = _inside == s ? CampaignTime.Now.ToHours - _insideSince : 0;
            _inside = null;
            // a stay of more than an hour is a choice to wait there (0.13); a stop to pass through is only the event
            if (stayed >= 1) Emit("stayed", "You stayed " + Text.D((float)stayed) + " in-game hours in " + Describe(s) + " and left.", Target(s), detail: new { hours = Math.Round(stayed, 1) });
            else Event("left", "You left " + Names.Settlement(s) + ".");
        }

        public static void OnDecisionAdded(KingdomDecision d, bool isPlayerInvolved)
        {
            if (!On || d == null || d.ProposerClan != Clan.PlayerClan) return;
            Emit("proposal", "You proposed: " + (d.GetGeneralTitle()?.ToString() ?? d.GetType().Name) + ".", null, detail: new { decision = d.GetType().Name });
        }

        public static void OnDecisionConcluded(KingdomDecision d, DecisionOutcome outcome, bool isPlayerInvolved)
        {
            if (!On || d == null || d.Kingdom == null || d.Kingdom != Clan.PlayerClan?.Kingdom) return;
            string title = outcome?.GetDecisionTitle()?.ToString() ?? d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
            Event("realm_decided", "Your realm decided: " + title + (isPlayerInvolved ? " (you took part)." : "."));
        }

        /// <summary>
        /// The player's vote in the realm's council (KingdomElection.OnPlayerSupport, GameHooks.VotePatch), or the player's choice
        /// as ruler (chooser): which outcome, with how much weight (0.13).
        /// </summary>
        public static void OnPlayerVote(KingdomDecision d, DecisionOutcome o, Supporter.SupportWeights weight, bool chooser)
        {
            if (!On || d == null) return;
            string title = d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
            string option = o?.GetDecisionTitle()?.ToString() ?? "no option";
            if (chooser) Emit("decide", "As ruler you decided \"" + title + "\": " + option + ".", null, detail: new { decision = d.GetType().Name, title, option });
            else Emit("vote", "You voted on \"" + title + "\": " + option + " (" + weight + ").", null, detail: new { decision = d.GetType().Name, title, option, weight = weight.ToString() });
        }

        public static void OnPlayerAbstained(KingdomDecision d)
        {
            if (!On || d == null) return;
            Emit("abstain", "As ruler you left \"" + (d.GetGeneralTitle()?.ToString() ?? d.GetType().Name) + "\" to your council.", null, detail: new { decision = d.GetType().Name });
        }

        /// <summary>A holding changing hands within the player's realm: a fief the player gave (0.13).</summary>
        public static void OnOwnerChanged(Settlement s, Hero newOwner, Hero oldOwner, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (!On || s == null || newOwner?.MapFaction != oldOwner?.MapFaction || newOwner?.MapFaction != Hero.MainHero?.MapFaction) return;
            if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByGift && oldOwner == Hero.MainHero)
                Emit("fief_gift", "You gave " + Describe(s) + " to " + Names.Hero(newOwner) + ".", Target(s), newOwner?.StringId, detail: new { to_clan = newOwner?.Clan?.StringId });
            else if (detail == ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.ByKingDecision)
                Event("fief_granted", Names.Settlement(s) + " went to " + Names.Hero(newOwner) + " by your realm's vote.");
        }

        /// <summary>Clans coming and going: a mercenary clan hired into the player's realm, or let go (0.13).</summary>
        public static void OnClanChangedKingdom(Clan clan, Kingdom from, Kingdom to, ChangeKingdomAction.ChangeKingdomActionDetail detail)
        {
            if (!On || clan == null || clan == Clan.PlayerClan) return;
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (mine == null) return;
            if (to == mine && detail == ChangeKingdomAction.ChangeKingdomActionDetail.JoinAsMercenary)
                Emit("mercenary_hired", "You hired " + Names.Faction(clan) + " as mercenaries.", null, clan.Leader?.StringId, detail: new { clan = clan.StringId, lords = clan.Heroes.Count(h => h.IsLord && h.IsAlive) });
            else if (from == mine && detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveAsMercenary)
            {
                bool talked = clan.Leader != null && clan.Leader.StringId == _talkedTo && CampaignTime.Now.ToHours - _talkedAt < 1;
                if (talked) Emit("mercenary_released", "You let " + Names.Faction(clan) + " go from your service.", null, clan.Leader?.StringId, detail: new { clan = clan.StringId });
                else Event("mercenary_left", Names.Faction(clan) + " left your service.");
            }
        }

        public static void OnConversationEnded(IEnumerable<CharacterObject> characters)
        {
            if (!On || characters == null) return;
            Hero h = characters.Where(c => c != null && c.IsHero).Select(c => c.HeroObject).FirstOrDefault(x => x != null && x != Hero.MainHero);
            if (h != null) { _talkedTo = h.StringId; _talkedAt = CampaignTime.Now.ToHours; }
            PollVassalOrders();
        }

        /// <summary>Called after every conversation and every in-game hour.</summary>
        public static void PollVassalOrders()
        {
            if (!On) return;
            foreach (PacReader.Change c in PacReader.Poll())
            {
                // the lieutenant's orders shown there, and its own party_ai orders, are not the player's (0.11.1, PacLink)
                string hid = c.Hero?.StringId;
                if (c.After != null ? PacLink.IsOurs(hid, c.After) : PacLink.WasOurs(hid, c.Before)) continue;
                string who = c.Hero != null ? Names.Hero(c.Hero) : "A lord";
                bool justTalked = c.Hero != null && c.Hero.StringId == _talkedTo && CampaignTime.Now.ToHours - _talkedAt < 1;
                if (c.After != null)
                {
                    // Party AI Controls' own refill (0.13): a recruit order with no target it sets by itself for a lord below his share of ranks,
                    // with no word from the player (run C2, Oct 25: Aragorn's refills were credited as the player's orders)
                    if (RecordRules.PacRefill(c.After, c.Target != null, justTalked))
                    {
                        Event("pac_refill", "Party AI Controls sent " + who + " to fill their ranks by itself.");
                        continue;
                    }
                    TargetInfo t = c.Target is Settlement s ? Target(s) : c.Target is MobileParty p ? Target(p) : null;
                    Emit("vassal_order", "You ordered " + who + " to " + PacReader.Words(c.After, c.Target) + ".", t, hid, detail: new { party_ai = c.After, after_talking = justTalked });
                }
                else
                {
                    if (justTalked) Emit("vassal_release", "You released " + who + " to act as they see fit.", null, hid);
                    else Event("vassal_order_ended", who + "'s order ended (" + PacReader.Words(c.Before, null) + ").");
                }
            }
        }

        /// <summary>
        /// An order the player gave one of their own lords through the bridge (agents.vassal): recorded with the same
        /// verbs the agents use, so the player's orders and the agents' are directly comparable.
        /// </summary>
        public static void OnOwnOrder(Hero lord, string verb, Settlement place, MobileParty target, string text)
        {
            if (!On) return;
            Emit("vassal_order", text, place != null ? Target(place) : Target(target), lord?.StringId, verb);
        }

        // ---------- in towns and castles (0.13) ----------

        /// <summary>The recruits of one stop, kept together until the player leaves or an hour passes.</summary>
        private sealed class Basket
        {
            public Settlement At;
            public double Since, Last;
            public readonly Dictionary<CharacterObject, int> Troops = new Dictionary<CharacterObject, int>();
            public int Cost;
        }

        private static Basket _basket;

        /// <summary>A troop the player recruited (the game's OnUnitRecruited: the recruit screen, one troop at a time).</summary>
        public static void OnUnitRecruited(CharacterObject troop, int amount)
        {
            if (!On || troop == null || amount <= 0) return;
            MobileParty mp = MobileParty.MainParty;
            if (mp == null) return;
            Settlement at = mp.CurrentSettlement ?? Orders.Where(mp);
            double t = CampaignTime.Now.ToHours;
            if (_basket != null && _basket.At != at) FlushBasket();
            if (_basket == null) _basket = new Basket { At = at, Since = t };
            _basket.Troops.TryGetValue(troop, out int n);
            _basket.Troops[troop] = n + amount;
            _basket.Last = t;
            try { _basket.Cost += amount * Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(troop, Hero.MainHero).RoundedResultNumber; }
            catch (Exception e) { Diag.Swallowed("a recruit's price for the record", e); }
        }

        private static void FlushBasket()
        {
            Basket b = _basket;
            _basket = null;
            if (b == null || b.Troops.Count == 0 || !On) return;
            int men = b.Troops.Values.Sum();
            MobileParty mp = MobileParty.MainParty;
            string list = string.Join(", ", b.Troops.OrderByDescending(kv => kv.Value).Select(kv => kv.Value + " " + kv.Key.Name));
            Emit("recruit", "You recruited " + men + " men" + (b.At != null ? " at " + Describe(b.At) : "") + " for " + Text.N(b.Cost) + " denars: " + list + ".", Target(b.At), verb: "recruit",
                 detail: new
                 {
                     men, cost = b.Cost, ranks = mp != null && mp.Party.PartySizeLimit > 0 ? Math.Round((double)mp.Party.NumberOfAllMembers / mp.Party.PartySizeLimit, 3) : (double?)null,
                     troops = b.Troops.Select(kv => new { troop = kv.Key.StringId, name = kv.Key.Name?.ToString(), tier = kv.Key.Tier, count = kv.Value }).ToList()
                 });
        }

        /// <summary>The player's trade screen closed with goods sold or bought (PlayerInventoryExchangeEvent; the prices are totals).</summary>
        public static void OnInventoryExchange(List<(ItemRosterElement, int)> bought, List<(ItemRosterElement, int)> sold, bool isTrading)
        {
            if (!On || !isTrading) return;
            bought = bought ?? new List<(ItemRosterElement, int)>();
            sold = sold ?? new List<(ItemRosterElement, int)>();
            if (bought.Count == 0 && sold.Count == 0) return;
            int soldPieces = sold.Sum(x => x.Item1.Amount), soldFor = sold.Sum(x => x.Item2);
            int boughtPieces = bought.Sum(x => x.Item1.Amount), paid = bought.Sum(x => x.Item2);
            int food = bought.Where(x => x.Item1.EquipmentElement.Item?.IsFood == true).Sum(x => x.Item1.Amount);
            Settlement at = MobileParty.MainParty?.CurrentSettlement;
            var parts = new List<string>();
            if (soldPieces > 0) parts.Add("sold " + soldPieces + " goods for " + Text.N(soldFor) + " denars");
            if (boughtPieces > 0) parts.Add("bought " + boughtPieces + " goods" + (food > 0 ? " (" + food + " of them food)" : "") + " for " + Text.N(paid) + " denars");
            Emit("trade", "You " + string.Join(" and ", parts) + (at != null ? " at " + Describe(at) : "") + ".", Target(at), verb: "sell",
                 detail: new
                 {
                     sold_for = soldFor, paid, food,
                     sold = sold.Select(x => new { item = x.Item1.EquipmentElement.Item?.StringId, count = x.Item1.Amount, gold = x.Item2, kind = x.Item1.EquipmentElement.Item?.ItemType.ToString() }).ToList(),
                     bought = bought.Select(x => new { item = x.Item1.EquipmentElement.Item?.StringId, count = x.Item1.Amount, gold = x.Item2, food = x.Item1.EquipmentElement.Item?.IsFood == true }).ToList()
                 });
        }

        /// <summary>Prisoners the player sold (SellPrisonersAction; the ransom broker, a castle's or town's dungeon).</summary>
        public static void OnPrisonerSold(PartyBase seller, PartyBase buyer, TroopRoster prisoners)
        {
            if (!On || seller != MobileParty.MainParty?.Party || prisoners == null || prisoners.TotalManCount == 0) return;
            Settlement at = MobileParty.MainParty?.CurrentSettlement;
            Emit("prisoners_sold", "You sold " + prisoners.TotalManCount + " prisoners" + (at != null ? " at " + Describe(at) : "") + ".", Target(at), verb: "sell",
                 detail: new { men = prisoners.TotalManCount, lords = prisoners.TotalHeroes, buyer = buyer?.Id });
        }

        /// <summary>Prisoners the player put into a dungeon of the realm (an enemy lord given brings the realm influence).</summary>
        public static void OnPrisonerDonated(MobileParty party, FlattenedTroopRoster prisoners, Settlement s)
        {
            if (!On || party != MobileParty.MainParty || prisoners == null) return;
            int n = prisoners.Count(), lords = prisoners.Count(x => x.Troop != null && x.Troop.IsHero);
            if (n == 0) return;
            Emit("prisoners_to_dungeon", "You put " + n + " prisoners" + (lords > 0 ? " (" + lords + " of them lords)" : "") + " into the dungeon of " + Describe(s) + ".", Target(s), detail: new { men = n, lords });
        }

        /// <summary>Troops the player gave a holding's garrison through the game's own donation (OnTroopGivenToSettlement).</summary>
        public static void OnTroopGiven(Hero giver, Settlement s, TroopRoster roster)
        {
            if (!On || giver != Hero.MainHero || roster == null || roster.TotalManCount == 0) return;
            Emit("garrison", "You left " + roster.TotalManCount + " men in the garrison of " + Describe(s) + ".", Target(s), verb: "garrison", detail: new { men = roster.TotalManCount, given = true });
        }

        /// <summary>One troop moved on the party screen: how many more (or fewer) the other party has after it, as troops or as prisoners.</summary>
        internal struct Moved
        {
            public CharacterObject Troop;
            public int Delta;
            public bool Prisoner;
        }

        /// <summary>
        /// The party screen's Done with another party on its left (GameHooks.PartyScreenPatch): troops and prisoners moved between the
        /// player's party and a garrison or a clan party, counted from the screen's rosters before and after (0.13).
        /// </summary>
        public static void OnPartyScreen(PartyBase left, List<Moved> changes)
        {
            if (!On || left == null || changes == null || changes.Count == 0) return;
            int toLeft = 0, fromLeft = 0, prisonersToLeft = 0, prisonersFromLeft = 0;
            foreach (Moved d in changes)
            {
                if (d.Troop == null || d.Delta == 0) continue;
                // the left side's change: more there now than before means men given to it
                if (d.Prisoner) { if (d.Delta > 0) prisonersToLeft += d.Delta; else prisonersFromLeft -= d.Delta; }
                else { if (d.Delta > 0) toLeft += d.Delta; else fromLeft -= d.Delta; }
            }
            if (toLeft + fromLeft + prisonersToLeft + prisonersFromLeft == 0) return;
            Settlement s = left.MobileParty?.CurrentSettlement ?? left.Settlement;
            bool garrison = left.MobileParty != null && left.MobileParty.IsGarrison;
            string where = garrison ? "the garrison of " + Describe(s) : Names.Party(left.MobileParty) ?? "another party";
            var parts = new List<string>();
            if (toLeft > 0) parts.Add("left " + toLeft + " men with " + where);
            if (fromLeft > 0) parts.Add("took " + fromLeft + " men from " + where);
            if (prisonersToLeft > 0) parts.Add("handed " + prisonersToLeft + " prisoners to " + where);
            if (prisonersFromLeft > 0) parts.Add("took " + prisonersFromLeft + " prisoners from " + where);
            Emit(garrison ? "garrison" : "party_transfer", "You " + string.Join(", ", parts) + ".", s != null ? Target(s) : Target(left.MobileParty), left.LeaderHero?.StringId, garrison ? "garrison" : null,
                 detail: new
                 {
                     to_them = toLeft, from_them = fromLeft, prisoners_to_them = prisonersToLeft, prisoners_from_them = prisonersFromLeft, party = left.Id,
                     troops = changes.Where(d => d.Troop != null && d.Delta != 0).Select(d => new { troop = d.Troop.StringId, delta = d.Delta, prisoner = d.Prisoner }).ToList()
                 });
        }

        private static int _wageLimit = int.MinValue;
        private static readonly Dictionary<string, string> GarrisonSettings = new Dictionary<string, string>();

        /// <summary>The party's wage limit and the player's own holdings' garrison wage limits and auto-recruitment, changed since the last hour.</summary>
        private static void Wages()
        {
            MobileParty mp = MobileParty.MainParty;
            if (mp == null) return;
            int limit = mp.PaymentLimit;
            if (_wageLimit != int.MinValue && limit != _wageLimit)
                Emit("wages", "You set your party's wage limit to " + Text.N(limit) + " a day (it was " + Text.N(_wageLimit) + ").", null, verb: "wages", detail: new { cap = limit, was = _wageLimit, party = "yours" });
            _wageLimit = limit;
            foreach (Settlement s in Clan.PlayerClan?.Settlements ?? Enumerable.Empty<Settlement>())
            {
                if (s?.Town == null) continue;
                MobileParty g = s.Town.GarrisonParty;
                string now = (g != null ? g.PaymentLimit.ToString(Text.Inv) : "-") + "|" + (s.Town.GarrisonAutoRecruitmentIsEnabled ? "1" : "0");
                if (GarrisonSettings.TryGetValue(s.StringId, out string was) && was != now)
                {
                    string[] a = was.Split('|'), b = now.Split('|');
                    var what = new List<string>();
                    if (a[0] != b[0]) what.Add("its wage limit to " + b[0] + " a day (it was " + a[0] + ")");
                    if (a[1] != b[1]) what.Add(b[1] == "1" ? "its recruiting by itself on" : "its recruiting by itself off");
                    Emit("garrison_settings", "You set the garrison of " + Describe(s) + ": " + string.Join(", ", what) + ".", Target(s), verb: "wages",
                         detail: new { wage_limit = b[0], auto_recruit = b[1] == "1", was_wage_limit = a[0], was_auto_recruit = a[1] == "1" });
                }
                GarrisonSettings[s.StringId] = now;
            }
        }

        // ---------- on the map (0.13) ----------

        private static Settlement _inside;
        private static double _insideSince;
        private static MobileParty _met;
        private static bool _metBattle;
        private static double _metAt;
        private static readonly HashSet<string> ArmyMembers = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>A game menu opened: the encounter's menu names the party met (GameMenuOpened).</summary>
        public static void OnMenuOpened(string menu)
        {
            if (!On || menu != "encounter") return;
            try
            {
                MobileParty met = PlayerEncounter.Current != null ? PlayerEncounter.EncounteredMobileParty : null;
                if (met == null || met == _met) return;
                _met = met;
                _metBattle = MobileParty.MainParty?.MapEvent != null;
                _metAt = CampaignTime.Now.ToHours;
            }
            catch (Exception e) { Diag.Swallowed("the encounter for the record", e); }
        }

        /// <summary>The first tick after an encounter's menu: a hostile party met and left without a fight is a fight declined.</summary>
        private static void EncounterOver(MobileParty me)
        {
            if (PlayerEncounter.Current != null) return;
            MobileParty met = _met;
            _met = null;
            if (_metBattle || met == null || !met.IsActive) return;
            bool hostile = met.MapFaction != null && me.MapFaction != null && FactionManager.IsAtWarAgainstFaction(met.MapFaction, me.MapFaction);
            if (hostile) Emit("declined_fight", "You met " + Describe(met, me) + " and left without fighting.", Target(met), detail: new { hours_in_menu = Math.Round(CampaignTime.Now.ToHours - _metAt, 2) });
        }

        /// <summary>The player got away from a battle, leaving men behind (PlayerDesertedBattleEvent).</summary>
        public static void OnPlayerDeserted(int sacrificed)
        {
            if (!On) return;
            _metBattle = true;
            Emit("fled", "You got away from the battle, leaving " + sacrificed + " men behind.", null, detail: new { left_behind = sacrificed });
        }

        // ---------- offers made to the player (0.13) ----------

        private sealed class Offer
        {
            public string Kind, From, Against;
            public int Amount, Days;
            public double At;
        }

        private static readonly List<Offer> Offers = new List<Offer>();

        public static void OnPeaceOffered(IFaction from, int amount, int days)
        {
            if (!On || from == null) return;
            Offers.Add(new Offer { Kind = "peace", From = from.StringId, Amount = amount, Days = days, At = CampaignTime.Now.ToHours });
            Event("peace_offered", Names.Faction(from) + " offered you peace" + (amount != 0 ? " with a tribute of " + Text.N(Math.Abs(amount)) + " a day " + (amount > 0 ? "to you" : "from you") : "") + ".");
        }

        /// <summary>The alliance window shown to the player (AllianceCampaignBehavior.OnAllianceOfferedToPlayer, GameHooks.OfferPatch).</summary>
        public static void OnAllianceOffered(Kingdom from)
        {
            if (!On || from == null) return;
            Offers.Add(new Offer { Kind = "alliance", From = from.StringId, At = CampaignTime.Now.ToHours });
            Event("alliance_offered", Names.Faction(from) + " offered you an alliance.");
        }

        /// <summary>The call-to-war window shown to the player (AllianceCampaignBehavior.OnCallToWarAgreementProposedToPlayer).</summary>
        public static void OnCallOffered(Kingdom from, Kingdom against)
        {
            if (!On || from == null) return;
            Offers.Add(new Offer { Kind = "call", From = from.StringId, Against = against?.StringId, At = CampaignTime.Now.ToHours });
            Event("call_offered", Names.Faction(from) + " called you to war against " + Names.Faction(against) + ".");
        }

        /// <summary>Peace made, an alliance started, a call answered: an offer the player accepted.</summary>
        public static void OnAccepted(string kind, IFaction a, IFaction b)
        {
            if (!On) return;
            string mine = Clan.PlayerClan?.MapFaction?.StringId;
            string other = a?.StringId == mine ? b?.StringId : b?.StringId == mine ? a?.StringId : null;
            Offer o = Offers.FirstOrDefault(x => x.Kind == kind && x.From == (kind == "call" ? a?.StringId : other));
            if (o == null) return;
            Offers.Remove(o);
            Emit(kind + "_offer", "You accepted " + Names.Faction(Find(o.From)) + "'s " + Words(o) + ".", null, verb: "accept_" + kind,
                 detail: new { kind, from = o.From, against = o.Against, tribute = o.Amount, days = o.Days, answer = "accepted" });
        }

        /// <summary>A call to war the player turned down (AllianceCampaignBehavior.DenyCallToWarAgreement).</summary>
        public static void OnCallDenied(Kingdom calling, Kingdom called)
        {
            if (!On || called != Clan.PlayerClan?.Kingdom) return;
            Offer o = Offers.FirstOrDefault(x => x.Kind == "call" && x.From == calling?.StringId);
            if (o != null) Offers.Remove(o);
            Emit("call_offer", "You turned down " + Names.Faction(calling) + "'s call to war.", null, verb: "decline_call", detail: new { kind = "call", from = calling?.StringId, against = o?.Against, answer = "declined" });
        }

        /// <summary>Offers whose window closed (the game paused while it was open) with no peace or alliance: declined.</summary>
        private static void OffersUnanswered(double now)
        {
            if (Offers.Count == 0 || Hero.MainHero?.MapFaction?.Leader != Hero.MainHero) return;
            foreach (Offer o in Offers.Where(x => now - x.At >= 2 && x.Kind != "call").ToList())
            {
                Offers.Remove(o);
                Emit(o.Kind + "_offer", "You declined " + Names.Faction(Find(o.From)) + "'s " + Words(o) + ".", null, verb: "decline_" + o.Kind,
                     detail: new { kind = o.Kind, from = o.From, tribute = o.Amount, days = o.Days, answer = "declined" });
            }
        }

        private static string Words(Offer o) => o.Kind == "peace" ? "offer of peace" + (o.Amount != 0 ? " (tribute " + Text.N(Math.Abs(o.Amount)) + " a day " + (o.Amount > 0 ? "to you" : "from you") + ")" : "")
                                                : o.Kind == "alliance" ? "offer of an alliance" : "call to war against " + Names.Faction(Find(o.Against));

        private static IFaction Find(string id) => id == null ? null : (IFaction)Kingdom.All.FirstOrDefault(k => k.StringId == id) ?? Clan.All.FirstOrDefault(c => c.StringId == id);

        // ---------- the lieutenant (0.13) ----------

        /// <summary>What the player said to the lieutenant (its window), linked into the record by its number.</summary>
        public static void OnToLieutenant(int chat, string text)
        {
            if (!On) return;
            Emit("to_lieutenant", "You told your lieutenant: \"" + text + "\"", null, detail: new { chat, text });
        }

        /// <summary>The player's Approve or Decline on the lieutenant's proposal.</summary>
        public static void OnLieutenantAnswered(int chat, bool approved, string list)
        {
            if (!On) return;
            Emit(approved ? "lieutenant_approved" : "lieutenant_declined", approved ? "You approved your lieutenant's proposal" + (string.IsNullOrEmpty(list) ? "." : ":\n" + list) : "You declined your lieutenant's proposal.",
                 null, detail: new { chat, approved, list });
        }

        // ---------- writing ----------

        private static void Emit(string kind, string text, TargetInfo target, string lord = null, string verb = null, object detail = null)
        {
            MobileParty me = MobileParty.MainParty;
            double? settle = null;
            int? merged = null;
            if (_settle >= 0) { settle = Math.Round(_settle, 3); merged = _merged; _settle = -1; }
            var d = new DecisionRecord
            {
                Kind = kind,
                Text = text,
                Hours = CampaignTime.Now.ToHours,
                Date = Capture.DateText(),
                Wall = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Faction = Clan.PlayerClan?.MapFaction?.StringId,
                Lord = lord,
                Verb = verb,
                You = me != null ? Seen(me, me) : null,
                Target = target,
                SeenByYou = me != null ? SeenNow(me) : null,
                SettleHours = settle,
                Corrections = merged,
                Run = RunTags.Id,
                Arm = RunTags.Arm,
                Load = Bridge.LoadId,
                Detail = detail
            };
            Committed++;
            Diag.Event("decision", kind, new { text, settle_hours = settle, corrections = merged, seen = d.SeenByYou?.Count });
            Background.Post(() => Brain.WriteDecision(d));
        }

        private static void Event(string kind, string text)
        {
            var e = new { Type = "event", Kind = kind, Text = text, Hours = CampaignTime.Now.ToHours, Date = Capture.DateText(), Run = RunTags.Id, Load = Bridge.LoadId };
            Background.Post(() => Brain.WriteRecord("events.jsonl", e));
        }

        /// <summary>Everything the game currently shows the player, nearest first.</summary>
        private static List<SeenParty> SeenNow(MobileParty me)
        {
            var list = new List<SeenParty>();
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == null || p == me || !p.IsActive || !p.IsVisible) continue;
                list.Add(Seen(p, me));
            }
            return list.OrderBy(x => x.Distance).Take(60).ToList();
        }

        private static SeenParty Seen(MobileParty p, MobileParty me)
        {
            int army = p.Army != null && p.Army.LeaderParty == p ? p.Army.TotalHealthyMembers : 0;
            return new SeenParty
            {
                Id = p.StringId,
                Name = Names.Party(p),
                Faction = p.MapFaction?.StringId,
                Kind = p.IsLordParty ? "lord" : p.IsCaravan ? "caravan" : p.IsVillager ? "villager" : p.IsBandit ? "bandit" : "other",
                Doing = p.DefaultBehavior.ToString(),
                Men = p.Party.NumberOfHealthyMembers,
                ArmyMen = army,
                Strength = (float)Math.Round(p.Party.EstimatedStrength),
                X = Text.R1(p.Position.X),
                Y = Text.R1(p.Position.Y),
                Distance = p == me ? 0f : Text.R1(p.Position.Distance(me.Position)),
                Hostile = p != me && p.MapFaction != null && me.MapFaction != null && FactionManager.IsAtWarAgainstFaction(p.MapFaction, me.MapFaction)
            };
        }

        private static TargetInfo Target(Settlement s)
        {
            if (s == null) return null;
            return new TargetInfo { Kind = "settlement", Id = s.StringId, Name = Names.Settlement(s), Faction = s.MapFaction?.StringId, X = Text.R1(s.Position.X), Y = Text.R1(s.Position.Y) };
        }

        private static TargetInfo Target(MobileParty p)
        {
            if (p == null) return null;
            int men = p.Army != null && p.Army.LeaderParty == p ? p.Army.TotalHealthyMembers : p.Party.NumberOfHealthyMembers;
            return new TargetInfo { Kind = "party", Id = p.StringId, Name = Names.Party(p), Faction = p.MapFaction?.StringId, Men = men, X = Text.R1(p.Position.X), Y = Text.R1(p.Position.Y) };
        }

        private static TargetInfo Target(PartyBase pb)
        {
            if (pb == null) return null;
            if (pb.MobileParty != null) return Target(pb.MobileParty);
            if (pb.Settlement != null) return Target(pb.Settlement);
            return null;
        }

        private static TargetInfo Point(float x, float y) => new TargetInfo { Kind = "point", X = Text.R1(x), Y = Text.R1(y) };

        private static string Describe(Settlement s)
        {
            if (s == null) return "somewhere";
            string kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : "place";
            string owner = s.MapFaction == Clan.PlayerClan?.MapFaction ? "yours" : Names.Faction(s.MapFaction);
            return Names.Settlement(s) + " (" + kind + ", " + owner + ")";
        }

        private static string Describe(MobileParty p, MobileParty me)
        {
            if (p == null) return "a party";
            int men = p.Army != null && p.Army.LeaderParty == p ? p.Army.TotalHealthyMembers : p.Party.NumberOfHealthyMembers;
            int mine = me.Army != null && me.Army.LeaderParty == me ? me.Army.TotalHealthyMembers : me.Party.NumberOfHealthyMembers;
            return Names.Party(p) + " (" + Names.Faction(p.MapFaction) + ", " + Text.N(men) + " men against your " + Text.N(mine) + ")";
        }

        private static string Describe(PartyBase pb)
        {
            if (pb == null) return "someone";
            if (pb.MobileParty != null && MobileParty.MainParty != null) return Describe(pb.MobileParty, MobileParty.MainParty);
            if (pb.Settlement != null) return Describe(pb.Settlement);
            return pb.Name?.ToString() ?? "someone";
        }

        private static string NearText(float x, float y)
        {
            Settlement best = null;
            float bd = float.MaxValue;
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s.IsHideout) continue;
                float dx = s.Position.X - x, dy = s.Position.Y - y;
                float d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = s; }
            }
            return best == null ? "in the wilds" : Text.D((float)Math.Sqrt(bd)) + " from " + Names.Settlement(best);
        }
    }
}
