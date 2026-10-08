using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace BannerlordAgents
{
    /// <summary>
    /// Records the player's own decisions on the campaign map (never inside battles), each with what the player could
    /// see at that moment, in the same form as an agent's record. Decisions:
    /// - where the party goes: a holding, a point, a party to chase, a siege, a raid, a patrol, someone to follow;
    /// - attacks, sieges and raids actually begun; armies formed and joined;
    /// - the realm's business: proposals made, and the orders given to vassals in conversation (via PacReader).
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

        public static int Clicks, Corrections, Committed;

        private static bool On => Bridge.Running && Settings.Bridge && Settings.Record;

        public static void Reset()
        {
            _have = _pending = _haveCommitted = false;
            _talkedTo = null;
            _talkedAt = -1;
            Clicks = Corrections = Committed = 0;
        }

        // ---------- movement, polled every campaign tick ----------

        public static void Poll()
        {
            if (!On) return;
            MobileParty mp = MobileParty.MainParty;
            if (mp == null || !mp.IsActive) return;
            if (mp.AttachedTo != null) { _have = false; return; }   // marching in someone else's army: not the player's call
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
                    Settlement = now.S?.StringId, Party = now.P?.StringId, X = now.Point ? now.X : (float?)null, Y = now.Point ? now.Y : (float?)null
                };
                Background.Post(() => Brain.WriteRecord("clicks.jsonl", click));
                bool repeat = _haveCommitted && Same(now, _lastCommitted) && t - _lastCommittedAt < 6;
                if (IsIntent(now) && !repeat)
                {
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
                CommitMove(mp, _pendingIntent);
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

        // ---------- decisions and events from the game's own events ----------

        public static void OnBattleStarted(MapEvent me, PartyBase attacker, PartyBase defender)
        {
            if (!On) return;
            PartyBase mine = MobileParty.MainParty?.Party;
            if (mine == null) return;
            if (attacker == mine)
                Emit("attack", "You attacked " + Describe(defender) + ".", Target(defender));
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
            Emit("form_army", "You formed an army" + (called.Count > 0 ? " and called " + string.Join(", ", called) : "") + ".", null);
        }

        public static void OnPartyJoinedArmy(MobileParty p)
        {
            if (!On || p?.Army == null) return;
            if (p == MobileParty.MainParty && p.Army.LeaderParty != p)
                Emit("join_army", "You joined the army of " + Names.Party(p.Army.LeaderParty) + ".", Target(p.Army.LeaderParty));
            else if (p.Army.LeaderParty == MobileParty.MainParty && p != MobileParty.MainParty)
                Event("joined_your_army", Names.Party(p) + " joined your army.");
        }

        public static void OnSettlementEntered(MobileParty p, Settlement s, Hero h)
        {
            if (On && p != null && p == MobileParty.MainParty) Event("entered", "You entered " + Names.Settlement(s) + ".");
        }

        public static void OnSettlementLeft(MobileParty p, Settlement s)
        {
            if (On && p != null && p == MobileParty.MainParty) Event("left", "You left " + Names.Settlement(s) + ".");
        }

        public static void OnDecisionAdded(KingdomDecision d, bool isPlayerInvolved)
        {
            if (!On || d == null || d.ProposerClan != Clan.PlayerClan) return;
            Emit("proposal", "You proposed: " + (d.GetGeneralTitle()?.ToString() ?? d.GetType().Name) + ".", null);
        }

        public static void OnDecisionConcluded(KingdomDecision d, DecisionOutcome outcome, bool isPlayerInvolved)
        {
            if (!On || d == null || d.Kingdom == null || d.Kingdom != Clan.PlayerClan?.Kingdom) return;
            string title = outcome?.GetDecisionTitle()?.ToString() ?? d.GetGeneralTitle()?.ToString() ?? d.GetType().Name;
            Event("realm_decided", "Your realm decided: " + title + (isPlayerInvolved ? " (you took part)." : "."));
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
                string who = c.Hero != null ? Names.Hero(c.Hero) : "A lord";
                if (c.After != null)
                {
                    TargetInfo t = c.Target is Settlement s ? Target(s) : c.Target is MobileParty p ? Target(p) : null;
                    Emit("vassal_order", "You ordered " + who + " to " + PacReader.Words(c.After, c.Target) + ".", t);
                }
                else
                {
                    bool justTalked = c.Hero != null && c.Hero.StringId == _talkedTo && CampaignTime.Now.ToHours - _talkedAt < 1;
                    if (justTalked) Emit("vassal_release", "You released " + who + " to act as they see fit.", null);
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

        // ---------- writing ----------

        private static void Emit(string kind, string text, TargetInfo target, string lord = null, string verb = null)
        {
            MobileParty me = MobileParty.MainParty;
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
                SeenByYou = me != null ? SeenNow(me) : null
            };
            Committed++;
            Background.Post(() => Brain.WriteDecision(d));
        }

        private static void Event(string kind, string text)
        {
            var e = new { Type = "event", Kind = kind, Text = text, Hours = CampaignTime.Now.ToHours, Date = Capture.DateText() };
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
