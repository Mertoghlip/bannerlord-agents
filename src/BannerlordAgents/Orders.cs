using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// Orders for lords, from three sources that share one path:
    /// - "hand": the console (agents.order), for the watched realm's lords (first tests, phase 4);
    /// - "agent": the agent chat's order files, for the watched realm's lords (phases 5-7, see AgentChannel);
    /// - "you": the player ordering their own vassals directly (agents.vassal), recorded as the player's decision.
    ///
    /// A lord holding an order is moved with the game's own commands, and the game's AI is told to stop making his
    /// decisions for him. This is exactly how Party AI Controls carries out its own orders, and how the game's quests
    /// script their parties: the only thing in the game that honours the switch is the hourly decision step
    /// (AiPartyThinkBehavior), which skips the party.
    ///
    /// That switch is SAVED with the game (MobilePartyAi.DoNotMakeNewDecisions is a saveable property). So just
    /// before every save, autosaves included, every ordered lord is handed back to the game, and taken back right
    /// after: a save never holds a lord frozen by us, and removing the mod is always safe. Orders live only in memory
    /// and are cleared whenever a campaign is loaded.
    ///
    /// Once an in-game hour each order is checked. It ends, with the reason, if the lord dies, is captured, loses his
    /// party, leaves the realm or joins someone else's army, or if the target is gone. If the game has turned him
    /// from his order, the order is given again and counted, which shows how hard the game's AI pulls.
    /// Game's thread only.
    /// </summary>
    internal static class Orders
    {
        internal sealed class HandOrder
        {
            public MobileParty Party;
            public Hero Lord;
            public Kingdom Realm;
            public string Source = "hand";
            public int Poke = -1;
            public string Verb;
            public Settlement Place;
            public MobileParty Target;
            public double Since;
            public double ArrivedAt = -1;
            public int Reissued;
            public bool Arrived;
        }

        public static readonly string[] Verbs = { "goto", "garrison", "patrol", "defend", "engage", "besiege", "raid", "recruit", "hold" };

        public const string Usage =
            "Usage: agents.order <lord> <verb> <target>\n" +
            "  verbs: goto <place>, garrison <castle or town>, patrol <place>, defend <place>, engage <lord>, besiege <castle or town>,\n" +
            "         raid <village>, recruit <town or village>, hold\n" +
            "  example: agents.order Throkmaw garrison Caradhras    (names or ids; names can be partial, accents optional)";

        private const double RecruitDays = 3;
        private static readonly List<HandOrder> Active = new List<HandOrder>();
        private static int _handedBack;

        public static int Count => Active.Count;

        public static void Reset()
        {
            Active.Clear();
            _handedBack = 0;
        }

        // ---------- the three doors ----------

        /// <summary>agents.order: a hand order for one of the watched realm's lords.</summary>
        public static string Give(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Kingdom realm = Bridge.Watched;
            if (realm == null) return "No realm is under watch, so there is no one to order. Choose one with agents.watch.";
            if (!Split(args, out string lordWords, out string verb, out string targetWords)) return Usage;
            Hero lord = FindLord(realm, lordWords, out string error);
            if (lord == null) return error;
            HandOrder o = Prepare(lord, verb, targetWords, realm, out error);
            if (o == null) return error;
            Start(o);
            return "Order given: " + Describe(o) + ".\nThe game's AI is held off him until the order ends or you release him (agents.release "
                   + FirstWord(Names.Hero(lord)) + ").";
        }

        /// <summary>agents.vassal: the player orders one of their own lords directly, with the same verbs as the agents.</summary>
        public static string GiveVassal(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Kingdom realm = Clan.PlayerClan?.Kingdom;
            if (realm == null) return "You are not in a realm, so there are no vassals to order.";
            if (!Split(args, out string lordWords, out string verb, out string targetWords))
                return Usage.Replace("agents.order", "agents.vassal").Replace("Throkmaw garrison Caradhras", "<your lord> defend <your castle>");
            Hero lord = FindLord(realm, lordWords, out string error);
            if (lord == null) return error;
            if (lord == Hero.MainHero) return "That is you.";
            string pac = PacReader.OrderOf(lord);
            if (pac != null)
                return Names.Hero(lord) + " already holds an order from your dialogue (Party AI Controls: " + pac + "). Release him there first (\"Act as you see fit\"), so the two do not fight each hour.";
            HandOrder o = Prepare(lord, verb, targetWords, realm, out error);
            if (o == null) return error;
            o.Source = "you";
            Start(o);
            Recorder.OnOwnOrder(o.Lord, o.Verb, o.Place, o.Target, "You ordered " + Describe(o) + ".");
            return "Order given: " + Describe(o) + ". Recorded as your decision. Release him with agents.release " + FirstWord(Names.Hero(lord)) + ".";
        }

        /// <summary>One order from the agent chat (AgentChannel). Returns the receipt's status and reason.</summary>
        public static void FromAgent(AgentChannel.AgentOrder a)
        {
            string status = "refused", reason;
            try { reason = AgentIssue(a, ref status); }
            catch (Exception e) { reason = "it could not be carried out (" + e.Message + ")"; }
            double hours = CampaignTime.Now.ToHours;
            string s = status, r = reason;
            Background.Post(() => AgentChannel.Receipt(a, s, r, hours));
        }

        private static string AgentIssue(AgentChannel.AgentOrder a, ref string status)
        {
            Kingdom realm = Bridge.Watched;
            if (realm == null) return "no realm is under watch";
            if (string.IsNullOrWhiteSpace(a.Lord)) return "the order names no lord";
            Hero lord = FindLord(realm, a.Lord, out string error);
            if (lord == null) return error;
            string verb = (a.Do ?? "").Trim().ToLowerInvariant();
            bool live = Settings.Agent == "live" && ObeysAgent(lord);
            bool shadow = Settings.Agent == "shadow";

            if (verb == "free" || verb == "release")
            {
                HandOrder held = Active.FirstOrDefault(x => x.Lord == lord);
                if (shadow) { status = "checked"; return "valid: " + Names.Hero(lord) + " would be left to his own judgment (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                if (held != null) { End(held, "released by the agent"); Active.Remove(held); }
                status = "applied";
                return Names.Hero(lord) + " follows his own judgment again";
            }

            HandOrder o = Prepare(lord, verb, a.Target, realm, out error);
            if (o == null) return error;
            if (shadow) { status = "checked"; return "valid: " + Describe(o) + " (shadow mode: not carried out)"; }
            if (!live) return NotYours(lord);
            o.Source = "agent";
            o.Poke = a.Poke;
            Start(o);
            status = "applied";
            return Describe(o);
        }

        private static bool ObeysAgent(Hero lord)
        {
            string list = (Settings.AgentLords ?? "all").Trim();
            if (list.Length == 0 || Text.Plain(list) == "all") return true;
            string name = Text.Plain(Names.Hero(lord));
            char[] separators = list.Contains(",") ? new[] { ',' } : new[] { ' ' };
            return list.Split(separators).Select(x => x.Trim()).Where(x => x.Length > 0)
                       .Any(x => string.Equals(x, lord.StringId, StringComparison.OrdinalIgnoreCase) || Text.Plain(x) == name);
        }

        private static string NotYours(Hero lord)
            => Names.Hero(lord) + " does not take your orders in this test; only " + Settings.AgentLords + " do"
               + (Settings.Agent == "off" ? " (the agent channel is off)" : "");

        public static string Release(List<string> args)
        {
            if (Active.Count == 0) return "No lord holds an order from us.";
            string words = string.Join(" ", args ?? new List<string>()).Trim();
            List<HandOrder> chosen;
            if (words.Length == 0 || Text.Plain(words) == "all") chosen = Active.ToList();
            else
            {
                string q = Text.Plain(words);
                chosen = Active.Where(o => Text.Plain(Names.Hero(o.Lord)).Contains(q) || string.Equals(o.Lord.StringId, words, StringComparison.OrdinalIgnoreCase)).ToList();
                if (chosen.Count == 0) return "No ordered lord matches '" + words + "'. Ordered: " + string.Join(", ", Active.Select(o => Names.Hero(o.Lord))) + ".";
            }
            foreach (HandOrder o in chosen)
            {
                End(o, "released by you");
                Active.Remove(o);
            }
            return "Released: " + string.Join(", ", chosen.Select(o => Names.Hero(o.Lord))) + ". The game's AI decides for them again.";
        }

        public static string List()
        {
            if (Active.Count == 0) return "No lord holds an order from us.";
            double now = CampaignTime.Now.ToHours;
            return string.Join("\n", Active.Select(o =>
                "- [" + o.Source + "] " + Describe(o) + "; for " + Math.Round(now - o.Since, 1).ToString(Text.Inv) + " in-game hours"
                + (o.Arrived ? ", arrived" : "") + "; turned back to the order " + o.Reissued + " times; now: " + Doing(o.Party)));
        }

        // ---------- shared steps ----------

        private static bool Split(List<string> args, out string lordWords, out string verb, out string targetWords)
        {
            lordWords = verb = targetWords = null;
            if (args == null || args.Count < 2) return false;
            int vi = args.FindIndex(a => Verbs.Contains(a.ToLowerInvariant()));
            if (vi <= 0) return false;
            lordWords = string.Join(" ", args.Take(vi));
            verb = args[vi].ToLowerInvariant();
            targetWords = string.Join(" ", args.Skip(vi + 1)).Trim();
            return true;
        }

        /// <summary>Checks a lord, a verb and a target, and returns the order ready to start (or null and why not).</summary>
        private static HandOrder Prepare(Hero lord, string verb, string targetWords, Kingdom realm, out string error)
        {
            error = null;
            verb = (verb ?? "").Trim().ToLowerInvariant();
            if (!Verbs.Contains(verb)) { error = "'" + verb + "' is not an order. Orders: " + string.Join(", ", Verbs) + ", free."; return null; }
            MobileParty party = lord.PartyBelongedTo;
            if (party == null || party.LeaderHero != lord || !party.IsActive) { error = Names.Hero(lord) + " is not leading a party right now."; return null; }
            if (party.AttachedTo != null && party.Army?.LeaderParty != party)
            {
                error = Names.Hero(lord) + " marches in " + Names.Party(party.Army?.LeaderParty) + "'s army. Order the army's leader instead.";
                return null;
            }
            var o = new HandOrder { Party = party, Lord = lord, Realm = realm, Verb = verb, Since = CampaignTime.Now.ToHours };
            targetWords = (targetWords ?? "").Trim();
            if (verb == "hold") return o;
            if (targetWords.Length == 0) { error = "Which " + (verb == "engage" ? "lord" : "place") + "?\n" + Usage; return null; }
            if (verb == "engage")
            {
                o.Target = FindParty(targetWords, out error);
                if (o.Target == null) return null;
                if (!FactionManager.IsAtWarAgainstFaction(o.Target.MapFaction, realm)) { error = Names.Party(o.Target) + " is not at war with " + Names.Faction(realm) + "."; return null; }
                return o;
            }
            o.Place = FindSettlement(targetWords, out error);
            if (o.Place == null) return null;
            error = Refused(verb, o.Place, realm);
            return error == null ? o : null;
        }

        private static void Start(HandOrder o)
        {
            foreach (HandOrder old in Active.Where(x => x.Party == o.Party).ToList()) Active.Remove(old);
            Apply(o);
            Active.Add(o);
            string text = Describe(o);
            Note(o, "issued", text);
            Say("Order: " + text + ".", Colors.Cyan);
        }

        /// <summary>Once an in-game hour.</summary>
        public static void Hourly()
        {
            double now = CampaignTime.Now.ToHours;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                HandOrder o = Active[i];
                string end;
                try { end = EndReason(o, now); } catch (Exception e) { end = "it could not be checked (" + e.Message + ")"; }
                if (end != null) { End(o, end); Active.RemoveAt(i); continue; }
                MobileParty p = o.Party;
                if (p.MapEvent != null || (p.BesiegedSettlement != null && o.Verb != "besiege")) continue;   // busy fighting; look again next hour
                if ((o.Verb == "garrison" || o.Verb == "goto" || o.Verb == "recruit") && p.CurrentSettlement == o.Place)
                {
                    if (!o.Arrived)
                    {
                        o.Arrived = true;
                        o.ArrivedAt = now;
                        Note(o, "arrived", Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place));
                        Say(Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place) + ".", Colors.Cyan);
                    }
                    if (!p.Ai.DoNotMakeNewDecisions) p.Ai.SetDoNotMakeNewDecisions(true);
                    continue;
                }
                if (!OnCourse(o))
                {
                    string was = Doing(p);
                    o.Reissued++;
                    Apply(o);
                    Note(o, "reissued", "the game had " + Names.Hero(o.Lord) + " " + was + "; order given again (" + o.Reissued + ")");
                }
                else if (!p.Ai.DoNotMakeNewDecisions) p.Ai.SetDoNotMakeNewDecisions(true);
            }
        }

        public static void HandBackForSave()
        {
            _handedBack = 0;
            foreach (HandOrder o in Active)
            {
                try
                {
                    if (o.Party != null && o.Party.IsActive) { o.Party.Ai.SetDoNotMakeNewDecisions(false); _handedBack++; }
                }
                catch { }
            }
        }

        public static void TakeBackAfterSave(bool saved, string name)
        {
            foreach (HandOrder o in Active)
            {
                try
                {
                    if (o.Party != null && o.Party.IsActive) o.Party.Ai.SetDoNotMakeNewDecisions(true);
                }
                catch { }
            }
            if (_handedBack > 0)
                Log.Line("save" + (saved ? "" : " (failed)") + " '" + name + "': " + _handedBack + " ordered lords were handed back to the game for the save and taken back after");
            _handedBack = 0;
        }

        /// <summary>After loading: how many of the watched realm's parties carry the "no decisions of their own" flag.</summary>
        public static string FrozenAtLoad()
        {
            Kingdom realm = Bridge.Watched;
            if (realm == null) return "no realm under watch";
            int frozen = MobileParty.All.Count(p => p != null && p.IsActive && p.MapFaction == realm && p.Ai != null && p.Ai.DoNotMakeNewDecisions);
            return frozen + " of " + Names.Faction(realm) + "'s parties carry the game's 'no decisions of their own' flag in this save (no orders of ours are active after a load)";
        }

        // ---------- the order itself ----------

        private static void Apply(HandOrder o)
        {
            MobileParty p = o.Party;
            var nav = MobileParty.NavigationType.Default;
            switch (o.Verb)
            {
                case "goto":
                case "garrison":
                case "recruit": SetPartyAiAction.GetActionForVisitingSettlement(p, o.Place, nav, false, false); break;
                case "patrol": SetPartyAiAction.GetActionForPatrollingAroundSettlement(p, o.Place, nav, false, false); break;
                case "defend": SetPartyAiAction.GetActionForDefendingSettlement(p, o.Place, nav, false, false); break;
                case "engage": SetPartyAiAction.GetActionForEngagingParty(p, o.Target, nav, false); break;
                case "besiege": SetPartyAiAction.GetActionForBesiegingSettlement(p, o.Place, nav, false); break;
                case "raid": SetPartyAiAction.GetActionForRaidingSettlement(p, o.Place, nav, false, false); break;
                case "hold": p.SetMoveModeHold(); break;
            }
            p.Ai.SetDoNotMakeNewDecisions(true);
        }

        private static bool OnCourse(HandOrder o)
        {
            MobileParty p = o.Party;
            AiBehavior b = p.DefaultBehavior;
            switch (o.Verb)
            {
                case "goto":
                case "garrison":
                case "recruit": return b == AiBehavior.GoToSettlement && p.TargetSettlement == o.Place;
                case "patrol": return b == AiBehavior.PatrolAroundPoint && p.TargetSettlement == o.Place;
                case "defend": return (b == AiBehavior.DefendSettlement && p.TargetSettlement == o.Place) || p.CurrentSettlement == o.Place;
                case "engage": return b == AiBehavior.EngageParty && p.TargetParty == o.Target;
                case "besiege": return p.BesiegedSettlement == o.Place || (b == AiBehavior.BesiegeSettlement && p.TargetSettlement == o.Place);
                case "raid": return (b == AiBehavior.RaidSettlement && p.TargetSettlement == o.Place) || (p.MapEvent != null && p.MapEvent.IsRaid);
                case "hold": return b == AiBehavior.Hold;
                default: return true;
            }
        }

        private static string EndReason(HandOrder o, double now)
        {
            Hero h = o.Lord;
            MobileParty p = o.Party;
            Kingdom realm = o.Realm;
            if (h == null || !h.IsAlive) return "he died";
            if (h.IsPrisoner) return "he was taken prisoner";
            if (p == null || !p.IsActive || p.LeaderHero != h) return "his party is gone";
            if (realm == null || h.MapFaction != realm) return "he is no longer in " + Names.Faction(realm);
            if (p.AttachedTo != null && p.Army?.LeaderParty != p) return "he joined another lord's army";
            switch (o.Verb)
            {
                case "garrison":
                case "defend":
                case "recruit":
                    if (o.Place.MapFaction != null && FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm))
                        return Names.Settlement(o.Place) + " fell to " + Names.Faction(o.Place.MapFaction);
                    if (o.Verb == "recruit")
                    {
                        PartyBase party = p.Party;
                        if (party.PartySizeLimit > 0 && party.NumberOfAllMembers >= party.PartySizeLimit * 0.98f) return "his ranks are full";
                        if (o.Place.IsUnderSiege || (o.Place.IsVillage && o.Place.Village.VillageState != Village.VillageStates.Normal))
                            return Names.Settlement(o.Place) + " can no longer give recruits";
                        if (o.ArrivedAt >= 0 && now - o.ArrivedAt >= RecruitDays * Calendar.HoursInDay)
                            return "three days at " + Names.Settlement(o.Place) + ", ranks at " + (party.PartySizeLimit > 0 ? (100 * party.NumberOfAllMembers / party.PartySizeLimit) : 0) + "%";
                    }
                    break;
                case "besiege":
                case "raid":
                    if (o.Place.MapFaction == realm) return Names.Settlement(o.Place) + " was taken";
                    if (o.Place.MapFaction == null || !FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm))
                        return Names.Settlement(o.Place) + " is no longer an enemy holding";
                    if (o.Verb == "raid" && o.Place.Village != null && o.Place.Village.VillageState == Village.VillageStates.Looted)
                        return Names.Settlement(o.Place) + " has been looted";
                    break;
                case "engage":
                    if (o.Target == null || !o.Target.IsActive) return "his target is gone";
                    if (!FactionManager.IsAtWarAgainstFaction(o.Target.MapFaction, realm)) return "his target is no longer an enemy";
                    break;
            }
            return null;
        }

        private static void End(HandOrder o, string why)
        {
            try
            {
                if (o.Party != null && o.Party.IsActive) o.Party.Ai.SetDoNotMakeNewDecisions(false);
            }
            catch { }
            Note(o, "ended", why);
            Say("Order for " + Names.Hero(o.Lord) + " ended: " + why + ".", Colors.Cyan);
        }

        private static string Refused(string verb, Settlement s, Kingdom realm)
        {
            bool hostile = s.MapFaction != null && FactionManager.IsAtWarAgainstFaction(s.MapFaction, realm);
            bool friendly = s.MapFaction == realm || realm.AlliedKingdoms.Contains(s.MapFaction as Kingdom);
            switch (verb)
            {
                case "garrison":
                    if (!s.IsFortification) return Names.Settlement(s) + " is not a castle or town.";
                    if (!friendly) return Names.Settlement(s) + " is not " + Names.Faction(realm) + "'s or an ally's.";
                    break;
                case "recruit":
                    if (!s.IsTown && !s.IsVillage) return Names.Settlement(s) + " is not a town or village (the game only lets lords recruit in those).";
                    if (hostile) return Names.Settlement(s) + " belongs to an enemy.";
                    if (s.IsUnderSiege || (s.IsVillage && s.Village.VillageState != Village.VillageStates.Normal)) return Names.Settlement(s) + " cannot give recruits right now.";
                    break;
                case "defend":
                    if (hostile) return Names.Settlement(s) + " belongs to an enemy; use besiege or raid.";
                    break;
                case "besiege":
                    if (!s.IsFortification) return Names.Settlement(s) + " is not a castle or town; use raid for villages.";
                    if (!hostile) return Names.Settlement(s) + " is not an enemy holding.";
                    break;
                case "raid":
                    if (!s.IsVillage) return Names.Settlement(s) + " is not a village.";
                    if (!hostile) return Names.Settlement(s) + " is not an enemy village.";
                    break;
            }
            return null;
        }

        // ---------- names and ids ----------

        private static Hero FindLord(Kingdom realm, string words, out string error)
        {
            List<Hero> lords = MobileParty.AllLordParties
                .Where(p => p != null && p.IsActive && p.LeaderHero != null && p.MapFaction == realm)
                .Select(p => p.LeaderHero).Distinct().ToList();
            Hero found = Pick(lords, words, h => Names.Hero(h), h => h.StringId, out List<Hero> pool);
            // some lords share a name (the orcs have two Dushgars), so the game's id is shown too and is accepted
            error = found != null ? null
                : pool.Count > 1 ? "'" + words + "' matches several lords: " + string.Join(", ", pool.Take(12).Select(h => Names.Hero(h) + " (" + h.StringId + ")"))
                                   + ". Use more of the name, or the id in brackets."
                : "No lord of " + Names.Faction(realm) + " leading a party matches '" + words + "'. Some of them: "
                  + string.Join(", ", lords.Take(25).Select(h => Names.Hero(h) + " (" + h.StringId + ")")) + ".";
            return found;
        }

        private static Settlement FindSettlement(string words, out string error)
        {
            List<Settlement> all = Settlement.All.Where(s => s != null && !s.IsHideout).ToList();
            Settlement found = Pick(all, words, s => Names.Settlement(s), s => s.StringId, out List<Settlement> pool);
            error = found != null ? null
                : pool.Count > 1 ? "'" + words + "' matches several places: " + string.Join(", ", pool.Take(12).Select(s => Names.Settlement(s) + " (" + s.StringId + ")")) + "."
                : "No place matches '" + words + "'.";
            return found;
        }

        /// <summary>A lord's party by its lord's name or id, or by the party's own id.</summary>
        private static MobileParty FindParty(string words, out string error)
        {
            List<MobileParty> lords = MobileParty.AllLordParties.Where(p => p != null && p.IsActive && p.LeaderHero != null).ToList();
            MobileParty byHero = lords.FirstOrDefault(p => string.Equals(p.LeaderHero.StringId, words, StringComparison.OrdinalIgnoreCase));
            if (byHero != null) { error = null; return byHero; }
            MobileParty found = Pick(lords, words, p => Names.Party(p), p => p.StringId, out List<MobileParty> pool);
            error = found != null ? null
                : pool.Count > 1 ? "'" + words + "' matches several lords: " + string.Join(", ", pool.Take(12).Select(p => Names.Party(p) + " (" + p.LeaderHero.StringId + ")")) + "."
                : "No lord's party matches '" + words + "'.";
            return found;
        }

        /// <summary>Exact name or id first, then names starting with the words, then names containing them.</summary>
        private static T Pick<T>(List<T> items, string words, Func<T, string> name, Func<T, string> id, out List<T> pool) where T : class
        {
            string q = Text.Plain(words);
            pool = items.Where(x => Text.Plain(name(x)) == q || string.Equals(id(x), words.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (pool.Count == 1) return pool[0];
            if (pool.Count == 0) pool = items.Where(x => Text.Plain(name(x)).StartsWith(q, StringComparison.Ordinal)).ToList();
            if (pool.Count == 1) return pool[0];
            if (pool.Count == 0) pool = items.Where(x => Text.Plain(name(x)).Contains(q)).ToList();
            return pool.Count == 1 ? pool[0] : null;
        }

        private static string Describe(HandOrder o)
        {
            string lord = Names.Hero(o.Lord);
            switch (o.Verb)
            {
                case "goto": return lord + " goes to " + Names.Settlement(o.Place);
                case "garrison": return lord + " garrisons " + Names.Settlement(o.Place);
                case "patrol": return lord + " patrols around " + Names.Settlement(o.Place);
                case "defend": return lord + " defends " + Names.Settlement(o.Place);
                case "engage": return lord + " attacks " + Names.Party(o.Target);
                case "besiege": return lord + " besieges " + Names.Settlement(o.Place);
                case "raid": return lord + " raids " + Names.Settlement(o.Place);
                case "recruit": return lord + " fills his ranks at " + Names.Settlement(o.Place);
                default: return lord + " holds his position";
            }
        }

        private static string Doing(MobileParty p)
        {
            if (p == null) return "gone";
            string target = p.TargetSettlement != null ? " " + Names.Settlement(p.TargetSettlement) : p.TargetParty != null ? " " + Names.Party(p.TargetParty) : "";
            string inside = p.CurrentSettlement != null ? " (inside " + Names.Settlement(p.CurrentSettlement) + ")" : "";
            return p.DefaultBehavior + target + inside;
        }

        private static void Note(HandOrder o, string what, string text)
        {
            var rec = new
            {
                Type = "order", Event = what, o.Source, Poke = o.Poke >= 0 ? o.Poke : (int?)null, Hours = CampaignTime.Now.ToHours,
                Lord = o.Lord?.StringId, LordName = Names.Hero(o.Lord), Realm = o.Realm?.StringId, o.Verb, Place = o.Place?.StringId,
                Target = o.Target?.StringId, o.Reissued, Text = text
            };
            Background.Post(() => Brain.WriteOrder(rec));
            Log.Line("order " + what + " [" + o.Source + "]: " + text);
            if (o.Source == "agent" && what != "issued")
            {
                string line = "order " + what + ": " + text + (what == "reissued" ? "" : ".");
                Background.Post(() => AgentChannel.Event(line));
            }
            // the watched realm hears when one of its orders finishes or fails, as a poke trigger
            if (o.Realm != null && o.Realm == Bridge.Watched && (what == "arrived" || what == "ended"))
                Notices.Order(what == "arrived" ? "order_arrived" : "order_ended",
                              what == "arrived" ? text + " as ordered." : "The order \"" + Describe(o) + "\" ended: " + text + ".", o.Party?.StringId);
        }

        private static void Say(string text, Color color)
        {
            if (Settings.Notify) Ui.Say(text, color);
        }

        private static string FirstWord(string s) => string.IsNullOrEmpty(s) ? "" : s.Split(' ')[0];
    }
}
