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
    /// before every save, autosaves included, every ordered lord is handed back to the game, checked, and taken back
    /// right after: a save never holds a lord frozen by us, and removing the mod is always safe. Orders live only in
    /// memory and are cleared whenever a campaign is loaded; at each load the mod checks that no lord it handed back
    /// at an earlier save came back frozen.
    ///
    /// Every order is traced start to finish (docs/LOGGING.md): the words it came from, the lord's state before and
    /// after the command (did the game take it?), its distance to the target every hour, every time the game turned
    /// him away and who did it, the end and its reason, and the first hours after release.
    /// Game's thread only.
    /// </summary>
    internal static class Orders
    {
        internal sealed class HandOrder
        {
            public int Id;
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
            public float BestDistance = float.MaxValue;
            public double LastProgressAt;
            public bool StuckAlarmed, FightAlarmed;
            public readonly List<double> ReissueTimes = new List<double>();
        }

        private sealed class Released
        {
            public MobileParty Party;
            public Hero Lord;
            public double At;
            public int Checks;
        }

        public static readonly string[] Verbs = { "goto", "garrison", "patrol", "defend", "engage", "besiege", "raid", "recruit", "hold" };

        public const string Usage =
            "Usage: agents.order <lord> <verb> <target>\n" +
            "  verbs: goto <place>, garrison <castle or town>, patrol <place>, defend <place>, engage <lord>, besiege <castle or town>,\n" +
            "         raid <village>, recruit <town or village>, hold\n" +
            "  example: agents.order Throkmaw garrison Caradhras    (names or ids; names can be partial, accents optional)";

        private const double RecruitDays = 3;
        private const double StuckHours = 6;
        private static readonly List<HandOrder> Active = new List<HandOrder>();
        private static readonly List<Released> AfterRelease = new List<Released>();
        private static readonly HashSet<MobileParty> Toggled = new HashSet<MobileParty>();
        private static readonly Dictionary<string, string> LastCulprit = new Dictionary<string, string>();
        private static readonly List<string> HandedBack = new List<string>();
        private static bool _handBackClean = true;
        private static int _nextId;

        public static int Count => Active.Count;

        public static void Reset()
        {
            Active.Clear();
            AfterRelease.Clear();
            Toggled.Clear();
            LastCulprit.Clear();
            HandedBack.Clear();
            Mirror();
        }

        public static HashSet<MobileParty> HeldParties() => new HashSet<MobileParty>(Active.Where(o => o.Party != null).Select(o => o.Party));

        /// <summary>For the lord watch: did we flip this party's switch since it last looked? (Consumes the mark.)</summary>
        public static bool TookSwitch(MobileParty p) => Toggled.Remove(p);

        /// <summary>The movement watcher named who changed an ordered lord's plan; kept for the next alarm about him.</summary>
        public static void Witnessed(string partyId, string who) => LastCulprit[partyId] = who;

        // ---------- the three doors ----------

        /// <summary>agents.order: a hand order for one of the watched realm's lords.</summary>
        public static string Give(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Kingdom realm = Bridge.Watched;
            if (realm == null) return "No realm is under watch, so there is no one to order. Choose one with agents.watch.";
            if (!Split(args, out string lordWords, out string verb, out string targetWords)) return Usage;
            Hero lord = FindLord(realm, lordWords, out string error);
            if (lord == null) return Refusal("hand", lordWords, verb, targetWords, error);
            HandOrder o = Prepare(lord, verb, targetWords, realm, out error);
            if (o == null) return Refusal("hand", lordWords, verb, targetWords, error);
            Start(o, lordWords + " " + verb + " " + targetWords);
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
            if (lord == null) return Refusal("you", lordWords, verb, targetWords, error);
            if (lord == Hero.MainHero) return "That is you.";
            string pac = PacReader.OrderOf(lord);
            if (pac != null)
                return Refusal("you", lordWords, verb, targetWords, Names.Hero(lord) + " already holds an order from your dialogue (Party AI Controls: " + pac
                       + "). Release him there first (\"Act as you see fit\"), so the two do not fight each hour.");
            HandOrder o = Prepare(lord, verb, targetWords, realm, out error);
            if (o == null) return Refusal("you", lordWords, verb, targetWords, error);
            o.Source = "you";
            Start(o, lordWords + " " + verb + " " + targetWords);
            Recorder.OnOwnOrder(o.Lord, o.Verb, o.Place, o.Target, "You ordered " + Describe(o) + ".");
            return "Order given: " + Describe(o) + ". Recorded as your decision. Release him with agents.release " + FirstWord(Names.Hero(lord)) + ".";
        }

        /// <summary>One order from the agent chat (AgentChannel). Posts the receipt's status and reason.</summary>
        public static void FromAgent(AgentChannel.AgentOrder a)
        {
            string status = "refused", reason;
            try { reason = AgentIssue(a, ref status); }
            catch (Exception e)
            {
                reason = "it could not be carried out (" + e.Message + ")";
                Diag.Error("carrying out an agent's order", e);
            }
            double hours = CampaignTime.Now.ToHours;
            string s = status, r = reason;
            Background.Post("agent receipt", () => AgentChannel.Receipt(a, s, r, hours));
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
                if (held != null)
                {
                    End(held, "released by the agent", "released by the agent");
                    Active.Remove(held);
                    Changed();
                }
                status = "applied";
                return Names.Hero(lord) + " follows his own judgment again";
            }

            HandOrder o = Prepare(lord, verb, a.Target, realm, out error);
            if (o == null) return error;
            if (shadow)
            {
                status = "checked";
                Diag.Event("order", "checked", new { source = "agent", poke = a.Poke, lord = lord.StringId, verb, place = o.Place?.StringId, target = o.Target?.StringId, state = State(o.Party, o) });
                return "valid: " + Describe(o) + " (shadow mode: not carried out)";
            }
            if (!live) return NotYours(lord);
            o.Source = "agent";
            o.Poke = a.Poke;
            Start(o, (a.Lord ?? "") + " " + (a.Do ?? "") + " " + (a.Target ?? ""));
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
                End(o, "released by you", "released by you");
                Active.Remove(o);
            }
            Changed();
            return "Released: " + string.Join(", ", chosen.Select(o => Names.Hero(o.Lord))) + ". The game's AI decides for them again.";
        }

        public static string List()
        {
            if (Active.Count == 0) return "No lord holds an order from us.";
            double now = CampaignTime.Now.ToHours;
            return string.Join("\n", Active.Select(o =>
                "- #" + o.Id + " [" + o.Source + "] " + Describe(o) + "; for " + Math.Round(now - o.Since, 1).ToString(Text.Inv) + " in-game hours"
                + (o.Arrived ? ", arrived" : DistanceText(o)) + "; turned back to the order " + o.Reissued + " times; now: " + Doing(o.Party)));
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

        private static void Start(HandOrder o, string words)
        {
            o.Id = ++_nextId;
            object before = State(o.Party, null);
            foreach (HandOrder old in Active.Where(x => x.Party == o.Party).ToList())
            {
                Active.Remove(old);
                Diag.Event("order", "replaced", new { id = old.Id, by = o.Id, lord = old.Lord?.StringId });
            }
            Apply(o);
            bool took = OnCourse(o);
            Active.Add(o);
            AfterRelease.RemoveAll(r => r.Party == o.Party);
            Changed();
            o.LastProgressAt = CampaignTime.Now.ToHours;
            o.BestDistance = Distance(o) ?? float.MaxValue;
            string text = Describe(o);
            Diag.Count("order:issued");
            Diag.Count("order source:" + o.Source);
            Note(o, "issued", text, new { words, before, after = State(o.Party, o), took });
            Say("Order: " + text + ".", Colors.Cyan);
            if (!took)
            {
                Diag.Count("order:not taken");
                Diag.Alarm("order_not_taken", "The game did not take the order \"" + text + "\": right after it, " + Names.Hero(o.Lord) + " is " + Doing(o.Party) + ".",
                           false, new { id = o.Id });
            }
        }

        /// <summary>Once an in-game hour.</summary>
        public static void Hourly()
        {
            double now = CampaignTime.Now.ToHours;
            bool changed = false;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                HandOrder o = Active[i];
                string end, key;
                try { end = EndReason(o, now, out key); }
                catch (Exception e)
                {
                    end = "it could not be checked (" + e.Message + ")";
                    key = "could not be checked";
                    Diag.Error("checking an order", e);
                }
                if (end != null)
                {
                    End(o, end, key);
                    Active.RemoveAt(i);
                    changed = true;
                    continue;
                }
                MobileParty p = o.Party;
                float? d = Distance(o);
                Diag.Event("order", "hour", new { id = o.Id, lord = o.Lord?.StringId, verb = o.Verb, arrived = o.Arrived, reissued = o.Reissued, state = State(p, o) });
                if (p.MapEvent != null || (p.BesiegedSettlement != null && o.Verb != "besiege")) continue;   // busy fighting; look again next hour
                if ((o.Verb == "garrison" || o.Verb == "goto" || o.Verb == "recruit") && p.CurrentSettlement == o.Place)
                {
                    if (!o.Arrived)
                    {
                        o.Arrived = true;
                        o.ArrivedAt = now;
                        Diag.Count("order:arrived");
                        Note(o, "arrived", Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place), new { hours_taken = Math.Round(now - o.Since, 1) });
                        Say(Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place) + ".", Colors.Cyan);
                    }
                    Hold(p);
                    continue;
                }
                Progress(o, d, now);
                if (!OnCourse(o))
                {
                    string was = Doing(p);
                    o.Reissued++;
                    Diag.Count("order:reissued");
                    o.ReissueTimes.Add(now);
                    o.ReissueTimes.RemoveAll(t => now - t > StuckHours);
                    Apply(o);
                    LastCulprit.TryGetValue(p.StringId, out string who);
                    Note(o, "reissued", "the game had " + Names.Hero(o.Lord) + " " + was + "; order given again (" + o.Reissued + ")" + (who != null ? "; last changed by " + who : ""),
                         new { was, who, took = OnCourse(o) });
                    if (o.ReissueTimes.Count >= 3 && !o.FightAlarmed)
                    {
                        o.FightAlarmed = true;
                        Diag.Count("order:fought");
                        Diag.Alarm("order_fought", "Something keeps turning " + Names.Hero(o.Lord) + " from his order (\"" + Describe(o) + "\"): given again "
                                                   + o.ReissueTimes.Count + " times in " + StuckHours + " in-game hours" + (who != null ? "; last changed by " + who : "") + ".",
                                   false, new { id = o.Id });
                    }
                }
                else Hold(p);
            }
            CheckReleased(now);
            if (changed) Changed();
        }

        private static void Hold(MobileParty p)
        {
            if (p.Ai.DoNotMakeNewDecisions) return;
            p.Ai.SetDoNotMakeNewDecisions(true);
            Toggled.Add(p);
        }

        /// <summary>Is he getting closer? No progress for 6 in-game hours, away from his target and out of battle, is an alarm.</summary>
        private static void Progress(HandOrder o, float? d, double now)
        {
            if (!d.HasValue || o.Arrived || o.Verb == "hold") return;
            if (d.Value < o.BestDistance - 0.5f)
            {
                o.BestDistance = d.Value;
                o.LastProgressAt = now;
                o.StuckAlarmed = false;
                return;
            }
            if (d.Value > 4f && now - o.LastProgressAt >= StuckHours && !o.StuckAlarmed)
            {
                o.StuckAlarmed = true;
                Diag.Count("order:stuck");
                Diag.Alarm("order_stuck", Names.Hero(o.Lord) + " has not got any closer to " + TargetName(o) + " for " + StuckHours + " in-game hours (" + Doing(o.Party) + ", "
                                          + Text.D(d.Value) + " away). He may be stuck on the map.", false, new { id = o.Id, distance = Math.Round(d.Value, 1) });
            }
        }

        /// <summary>The first three in-game hours after a release: is he deciding for himself again?</summary>
        private static void CheckReleased(double now)
        {
            for (int i = AfterRelease.Count - 1; i >= 0; i--)
            {
                Released r = AfterRelease[i];
                MobileParty p = r.Party;
                r.Checks++;
                if (p == null || !p.IsActive || p.LeaderHero != r.Lord) { AfterRelease.RemoveAt(i); continue; }
                bool off = p.Ai.DoNotMakeNewDecisions;
                string pac = off ? PacReader.OrderOf(r.Lord) : null;
                Diag.Lord(Text.When(now) + "  " + Names.Hero(r.Lord) + " [" + r.Lord.StringId + "], " + Math.Round(now - r.At) + " h after release: " + Doing(p)
                          + (off ? (pac != null ? "; held by Party AI Controls (" + pac + ")" : "; the game's AI is STILL switched off for him") : "; deciding for himself") + ".");
                Diag.Event("order", "after release", new { lord = r.Lord.StringId, hours = Math.Round(now - r.At, 1), doing = Doing(p), ai_off = off, held_by_pac = pac });
                if (off && pac == null)
                {
                    Diag.Count("order:held after release");
                    Diag.Alarm("held_after_release", Names.Hero(r.Lord) + " was released " + Math.Round(now - r.At) + " in-game hours ago, but the game's AI is still switched off for him, "
                                                     + "so he will never decide anything for himself. agents.release " + FirstWord(Names.Hero(r.Lord)) + " frees him again.", true,
                               new { lord = r.Lord.StringId });
                    AfterRelease.RemoveAt(i);
                    continue;
                }
                if (r.Checks >= 3) AfterRelease.RemoveAt(i);
            }
        }

        // ---------- saves and loads ----------

        public static void HandBackForSave()
        {
            HandedBack.Clear();
            _handBackClean = true;
            foreach (HandOrder o in Active)
            {
                try
                {
                    if (o.Party == null || !o.Party.IsActive) continue;
                    o.Party.Ai.SetDoNotMakeNewDecisions(false);
                    Toggled.Add(o.Party);
                    HandedBack.Add(o.Lord?.StringId);
                    if (o.Party.Ai.DoNotMakeNewDecisions)
                    {
                        _handBackClean = false;
                        Diag.Alarm("save_unclean", Names.Hero(o.Lord) + " could not be handed back to the game before the save; this save may hold him with the game's AI switched off.", true);
                    }
                }
                catch (Exception e)
                {
                    _handBackClean = false;
                    Diag.Error("handing a lord back before a save", e);
                    Diag.Alarm("save_unclean", "A lord could not be handed back to the game before the save (" + e.Message + "); this save may hold him frozen.", true);
                }
            }
        }

        public static void TakeBackAfterSave(bool saved, string name)
        {
            int taken = 0;
            foreach (HandOrder o in Active)
            {
                try
                {
                    if (o.Party == null || !o.Party.IsActive) continue;
                    o.Party.Ai.SetDoNotMakeNewDecisions(true);
                    Toggled.Add(o.Party);
                    if (o.Party.Ai.DoNotMakeNewDecisions) taken++;
                }
                catch (Exception e) { Diag.Error("taking a lord back after a save", e); }
            }
            Diag.Count("save:count");
            Diag.Count("save:handed back", HandedBack.Count);
            if (!_handBackClean) Diag.Count("save:failures");
            var lords = HandedBack.ToList();
            bool clean = _handBackClean;
            Diag.Event("save", saved ? "saved" : "save failed", new { name, handed_back = lords, all_free_while_saving = clean, taken_back = taken });
            if (lords.Count > 0)
            {
                Log.Line("save" + (saved ? "" : " (failed)") + " '" + name + "': " + lords.Count + " ordered lords were handed back to the game for the save"
                         + (clean ? ", each checked free," : ", NOT ALL FREE,") + " and " + taken + " taken back after");
                var rec = new { Type = "handback", Wall = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Hours = CampaignTime.Now.ToHours, Save = name, Saved = saved, Lords = lords, Clean = clean };
                Background.Post("save record", () => Brain.WriteData("handbacks.jsonl", rec));
            }
            HandedBack.Clear();
        }

        /// <summary>
        /// At load: every party in the game whose AI is switched off, realm by realm (quests and Party AI Controls do
        /// this too, so the list says who), and a check of the lords we handed back at earlier saves.
        /// </summary>
        public static string FrozenAtLoad()
        {
            int total = 0;
            var byRealm = new Dictionary<string, int>();
            var lords = new List<string>();
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == null || !p.IsActive || p.Ai == null || !p.Ai.DoNotMakeNewDecisions) continue;
                total++;
                string realm = p.MapFaction != null ? Names.Faction(p.MapFaction) : "no realm";
                byRealm.TryGetValue(realm, out int n);
                byRealm[realm] = n + 1;
                if (p.LeaderHero != null && lords.Count < 25) lords.Add(Names.Hero(p.LeaderHero) + " (" + realm + ")");
            }
            Diag.Count("load:checks");
            Diag.Count("load:held lords", lords.Count);
            Diag.Event("load", "switch sweep", new { parties = total, by_realm = byRealm, lords });
            string campaign = Bridge.CampaignId;
            Background.Post("load check", () =>
            {
                List<string> ids = Brain.ReadHandbackIds(campaign);
                if (ids.Count > 0) Background.ToMain.Enqueue(() => CheckPastHandbacks(ids));
            });
            return total + " parties in the game have the game's AI switched off in this save"
                   + (total > 0 ? " (" + string.Join(", ", byRealm.Select(kv => kv.Key + " " + kv.Value)) + ")" : "")
                   + (lords.Count > 0 ? "; lords among them: " + string.Join(", ", lords) : "") + ". No orders of ours are active after a load.";
        }

        private static void CheckPastHandbacks(List<string> ids)
        {
            var frozen = new List<string>();
            int found = 0;
            foreach (string id in ids)
            {
                Hero h = Hero.FindFirst(x => x.StringId == id);
                MobileParty p = h?.PartyBelongedTo;
                if (p == null || p.LeaderHero != h) continue;
                found++;
                if (p.Ai.DoNotMakeNewDecisions && PacReader.OrderOf(h) == null) frozen.Add(Names.Hero(h));
            }
            Diag.Event("load", "past hand-backs checked", new { lords = ids.Count, leading_parties = found, still_frozen = frozen });
            if (frozen.Count == 0) Log.Line("load check: all " + found + " lords we handed back at earlier saves (and who still lead parties) are free.");
            else Diag.Alarm("load_frozen", "After loading, " + frozen.Count + " lords we once held have the game's AI switched off: " + string.Join(", ", frozen)
                                           + ". A save may have caught them. agents.release cannot free them (no order of ours); tell me.", true);
        }

        /// <summary>Once a day: lords whose AI is switched off by someone other than us.</summary>
        public static void DailySweep()
        {
            HashSet<MobileParty> ours = HeldParties();
            Kingdom watched = Bridge.Watched;
            var other = new List<string>();
            var pac = new List<string>();
            foreach (MobileParty p in MobileParty.AllLordParties)
            {
                if (p == null || !p.IsActive || p.LeaderHero == null || ours.Contains(p) || !p.Ai.DoNotMakeNewDecisions) continue;
                if (PacReader.HoldsOrder(p.LeaderHero)) pac.Add(Names.Hero(p.LeaderHero));
                else other.Add(Names.Hero(p.LeaderHero) + " (" + Names.Faction(p.MapFaction) + ")");
            }
            Diag.Event("sweep", "daily", new { held_by_party_ai_controls = pac, held_by_something_else = other });
            List<string> orcs = other.Where(x => watched != null && x.EndsWith("(" + Names.Faction(watched) + ")", StringComparison.Ordinal)).ToList();
            if (orcs.Count > 0)
                Diag.Alarm("held_by_other", "Something other than us has switched off the game's AI for " + orcs.Count + " lords of the watched realm: " + string.Join(", ", orcs) + ".");
        }

        // ---------- the order itself ----------

        private static void Apply(HandOrder o)
        {
            MobileParty p = o.Party;
            var nav = MobileParty.NavigationType.Default;
            Witness.Ours = true;
            try
            {
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
                Toggled.Add(p);
            }
            finally { Witness.Ours = false; }
        }

        private static bool OnCourse(HandOrder o)
        {
            MobileParty p = o.Party;
            AiBehavior b = p.DefaultBehavior;
            switch (o.Verb)
            {
                case "goto":
                case "garrison":
                case "recruit": return (b == AiBehavior.GoToSettlement && p.TargetSettlement == o.Place) || p.CurrentSettlement == o.Place;
                case "patrol": return b == AiBehavior.PatrolAroundPoint && p.TargetSettlement == o.Place;
                case "defend": return (b == AiBehavior.DefendSettlement && p.TargetSettlement == o.Place) || p.CurrentSettlement == o.Place;
                case "engage": return b == AiBehavior.EngageParty && p.TargetParty == o.Target;
                case "besiege": return p.BesiegedSettlement == o.Place || (b == AiBehavior.BesiegeSettlement && p.TargetSettlement == o.Place);
                case "raid": return (b == AiBehavior.RaidSettlement && p.TargetSettlement == o.Place) || (p.MapEvent != null && p.MapEvent.IsRaid);
                case "hold": return b == AiBehavior.Hold;
                default: return true;
            }
        }

        private static string EndReason(HandOrder o, double now, out string key)
        {
            key = null;
            Hero h = o.Lord;
            MobileParty p = o.Party;
            Kingdom realm = o.Realm;
            if (h == null || !h.IsAlive) { key = "died"; return "he died"; }
            if (h.IsPrisoner) { key = "captured"; return "he was taken prisoner"; }
            if (p == null || !p.IsActive || p.LeaderHero != h) { key = "party gone"; return "his party is gone"; }
            if (realm == null || h.MapFaction != realm) { key = "left the realm"; return "he is no longer in " + Names.Faction(realm); }
            if (p.AttachedTo != null && p.Army?.LeaderParty != p) { key = "joined an army"; return "he joined another lord's army"; }
            switch (o.Verb)
            {
                case "garrison":
                case "defend":
                case "recruit":
                    if (o.Place.MapFaction != null && FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm))
                    {
                        key = "place fell";
                        return Names.Settlement(o.Place) + " fell to " + Names.Faction(o.Place.MapFaction);
                    }
                    if (o.Verb == "recruit")
                    {
                        PartyBase party = p.Party;
                        if (party.PartySizeLimit > 0 && party.NumberOfAllMembers >= party.PartySizeLimit * 0.98f) { key = "ranks full"; return "his ranks are full"; }
                        if (o.Place.IsUnderSiege || (o.Place.IsVillage && o.Place.Village.VillageState != Village.VillageStates.Normal))
                        {
                            key = "no recruits there";
                            return Names.Settlement(o.Place) + " can no longer give recruits";
                        }
                        if (o.ArrivedAt >= 0 && now - o.ArrivedAt >= RecruitDays * Calendar.HoursInDay)
                        {
                            key = "three days";
                            return "three days at " + Names.Settlement(o.Place) + ", ranks at " + (party.PartySizeLimit > 0 ? (100 * party.NumberOfAllMembers / party.PartySizeLimit) : 0) + "%";
                        }
                    }
                    break;
                case "besiege":
                case "raid":
                    if (o.Place.MapFaction == realm) { key = "taken"; return Names.Settlement(o.Place) + " was taken"; }
                    if (o.Place.MapFaction == null || !FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm))
                    {
                        key = "no longer an enemy";
                        return Names.Settlement(o.Place) + " is no longer an enemy holding";
                    }
                    if (o.Verb == "raid" && o.Place.Village != null && o.Place.Village.VillageState == Village.VillageStates.Looted)
                    {
                        key = "looted";
                        return Names.Settlement(o.Place) + " has been looted";
                    }
                    break;
                case "engage":
                    if (o.Target == null || !o.Target.IsActive) { key = "target gone"; return "his target is gone"; }
                    if (!FactionManager.IsAtWarAgainstFaction(o.Target.MapFaction, realm)) { key = "target no longer an enemy"; return "his target is no longer an enemy"; }
                    break;
            }
            return null;
        }

        private static void End(HandOrder o, string why, string key)
        {
            bool stillOff = false;
            try
            {
                if (o.Party != null && o.Party.IsActive)
                {
                    o.Party.Ai.SetDoNotMakeNewDecisions(false);
                    Toggled.Add(o.Party);
                    stillOff = o.Party.Ai.DoNotMakeNewDecisions;
                }
            }
            catch (Exception e) { Diag.Error("releasing a lord", e); stillOff = true; }
            Diag.Count("order:ended");
            Diag.Count("order end:" + (key ?? "other"));
            Note(o, "ended", why, new { key, hours_held = Math.Round(CampaignTime.Now.ToHours - o.Since, 1), state = State(o.Party, o) });
            Say("Order for " + Names.Hero(o.Lord) + " ended: " + why + ".", Colors.Cyan);
            if (stillOff)
                Diag.Alarm("release_failed", Names.Hero(o.Lord) + "'s order ended, but the game's AI could not be switched back on for him.", true, new { id = o.Id });
            if (o.Party != null && o.Party.IsActive && o.Lord != null)
                AfterRelease.Add(new Released { Party = o.Party, Lord = o.Lord, At = CampaignTime.Now.ToHours });
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

        /// <summary>An order refused at the door is traced too: the words, and why.</summary>
        private static string Refusal(string source, string lordWords, string verb, string targetWords, string why)
        {
            Diag.Count("order:refused at the door");
            Diag.Event("order", "refused", new { source, words = (lordWords + " " + verb + " " + targetWords).Trim(), why });
            return why;
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
            Diag.Event("order", "lord lookup", new { words, found = found?.StringId, candidates = pool.Count });
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

        private static string TargetName(HandOrder o) => o.Place != null ? Names.Settlement(o.Place) : o.Target != null ? Names.Party(o.Target) : "his target";

        private static float? Distance(HandOrder o)
        {
            MobileParty p = o.Party;
            if (p == null || !p.IsActive) return null;
            if (o.Place != null) return p.Position.Distance(o.Place.Position);
            if (o.Target != null && o.Target.IsActive) return p.Position.Distance(o.Target.Position);
            return null;
        }

        private static string DistanceText(HandOrder o)
        {
            float? d = Distance(o);
            return d.HasValue ? ", " + Text.D(d.Value) + " from " + TargetName(o) : "";
        }

        private static string Doing(MobileParty p)
        {
            if (p == null) return "gone";
            string target = p.TargetSettlement != null ? " " + Names.Settlement(p.TargetSettlement) : p.TargetParty != null ? " " + Names.Party(p.TargetParty) : "";
            string inside = p.CurrentSettlement != null ? " (inside " + Names.Settlement(p.CurrentSettlement) + ")" : "";
            return p.DefaultBehavior + target + inside;
        }

        /// <summary>A lord's state as plain values, for the trace: where he is and what the game has him doing.</summary>
        private static object State(MobileParty p, HandOrder o)
        {
            if (p == null) return null;
            float? d = o != null ? Distance(o) : null;
            return new
            {
                x = Text.R1(p.Position.X), y = Text.R1(p.Position.Y), men = p.Party?.NumberOfHealthyMembers, doing = p.DefaultBehavior.ToString(),
                short_term = p.ShortTermBehavior.ToString(), target = p.TargetSettlement?.StringId ?? p.TargetParty?.StringId, inside = p.CurrentSettlement?.StringId,
                army = p.Army?.LeaderParty?.StringId, battle = p.MapEvent != null, ai_off = p.Ai?.DoNotMakeNewDecisions,
                distance = d.HasValue ? Math.Round(d.Value, 1) : (double?)null
            };
        }

        private static void Note(HandOrder o, string what, string text, object extra = null)
        {
            var rec = new
            {
                Type = "order", Event = what, Id = o.Id, o.Source, Poke = o.Poke >= 0 ? o.Poke : (int?)null, Hours = CampaignTime.Now.ToHours,
                Lord = o.Lord?.StringId, LordName = Names.Hero(o.Lord), Realm = o.Realm?.StringId, o.Verb, Place = o.Place?.StringId,
                Target = o.Target?.StringId, o.Reissued, Text = text
            };
            Background.Post("order record", () => Brain.WriteOrder(rec));
            Diag.Event("order", what, new { id = o.Id, source = o.Source, poke = o.Poke >= 0 ? o.Poke : (int?)null, lord = o.Lord?.StringId, verb = o.Verb,
                                            place = o.Place?.StringId, target = o.Target?.StringId, text, extra });
            Log.Line("order #" + o.Id + " " + what + " [" + o.Source + "]: " + text);
            if (o.Source == "agent" && what != "issued")
            {
                string line = "order " + what + ": " + text + (what == "reissued" ? "" : ".");
                Background.Post("agent receipt", () => AgentChannel.Event(line));
            }
            // the watched realm hears when one of its orders finishes or fails, as a poke trigger
            if (o.Realm != null && o.Realm == Bridge.Watched && (what == "arrived" || what == "ended"))
                Notices.Order(what == "arrived" ? "order_arrived" : "order_ended",
                              what == "arrived" ? text + " as ordered." : "The order \"" + Describe(o) + "\" ended: " + text + ".", o.Party?.StringId);
        }

        /// <summary>Keeps the watchers in step with the orders held: the plain copy for the logs, the movement watcher and the lord watch.</summary>
        private static void Changed()
        {
            Live.OrdersHeld = Active.Count;
            Live.OrdersText = Active.Count == 0 ? "none" : string.Join("; ", Active.Select(o => "#" + o.Id + " [" + o.Source + "] " + Describe(o)));
            Witness.Watch(Active.Where(o => o.Party != null).Select(o => o.Party).Distinct().ToArray());
            LordWatch.Refresh();
        }

        private static void Mirror()
        {
            Live.OrdersHeld = 0;
            Live.OrdersText = "none";
            Witness.Watch(new MobileParty[0]);
        }

        private static void Say(string text, Color color)
        {
            if (Settings.Notify) Ui.Say(text, color);
        }

        private static string FirstWord(string s) => string.IsNullOrEmpty(s) ? "" : s.Split(' ')[0];
    }
}
