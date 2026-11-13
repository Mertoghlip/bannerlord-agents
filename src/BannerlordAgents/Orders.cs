using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using BannerlordAgents.Tools;

namespace BannerlordAgents
{
    /// <summary>
    /// Orders for lords, from three sources:
    /// - "agent": the agent's answer files (AgentChannel), for the watched realm's lords;
    /// - "hand": the console (agents.order), the same orders typed by hand, for testing;
    /// - "you": the player ordering their own vassals directly (agents.vassal), recorded as the player's decision.
    ///
    /// The agent's and the console's orders are moves and tools of the behavior language (docs/TOOLS.md): the lord is
    /// held here, and the tool runner on the worker (Tools.Runner) decides each move from the realm's own view; this
    /// side makes the moves, re-issues them when the game turns the lord away, and ends the hold when the lord is lost.
    /// Armies are formed, joined, left and broken up here, by the game's own rules and influence prices. Vassal orders
    /// keep the simple one-step verbs.
    ///
    /// A held lord is moved with the game's own commands, and the game's AI is told to stop making his decisions. That
    /// switch is SAVED with the game (MobilePartyAi.DoNotMakeNewDecisions is a saveable property), so just before every
    /// save, autosaves included, every held lord is handed back to the game, checked, and taken back right after: a save
    /// never holds a lord frozen by us. Orders live only in memory and are cleared whenever a campaign is loaded.
    ///
    /// Every order is traced start to finish (docs/LOGGING.md). Game's thread only.
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
            /// <summary>For "toward": the point it marches to; for a patrol kept within Radius of its holding, the point it is walking to now.</summary>
            public float X, Y;
            /// <summary>A patrol's reach from its holding (the tool's radius); 0 leaves the patrol to the game, which ranges half a day's march out.</summary>
            public float Radius;
            /// <summary>A patrol's least distance from its holding's gate (0.19, a tool's standoff); 0 keeps it at a quarter of Radius.</summary>
            public float Radius0;
            public bool HasPoint;
            /// <summary>A tool order: its runner instance and its name (0 and null for a plain vassal order).</summary>
            public int ToolId;
            public string Tool;
            public string ToolText;
            /// <summary>The tool order's every part (ToolOrder.Signature), to keep it when the very same order comes again (0.12.0).</summary>
            public string Signature;
            /// <summary>The tool order has a next order (then), so that the news of its end can say what became of it (0.14.2).</summary>
            public bool HasNext;
            public double Since;
            public double ArrivedAt = -1;
            public int Reissued;
            public bool Arrived;
            public float BestDistance = float.MaxValue;
            public double LastProgressAt;
            /// <summary>Where he last stood more than a unit from the place before, and when (the stuck check: he must also have stood still).</summary>
            public bool HasLastSpot;
            public float LastSpotX, LastSpotY;
            public double LastMovedAt;
            public bool StuckAlarmed, FightAlarmed;
            /// <summary>A join order has seen its army (so an army missing later broke up, rather than not having formed yet).</summary>
            public bool JoinedSeen;
            public readonly List<double> ReissueTimes = new List<double>();
            /// <summary>A tool order's settings as given (0.13.19: garrison_floor for its recruiting stops, the supply order's days and keep_days); a default where one is not given.</summary>
            public Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The supply move's holding (0.13.19), while Place is the market he buys at on the way.</summary>
            public Settlement SupplyFor;
            /// <summary>The move is a step of a longer tool (0.17.4, Runner's Move.Step): a supply move that ends hands back to the tool's rules instead of ending the order.</summary>
            public bool Step;
            /// <summary>
            /// The holdings a tool order names (its at, then its places, 0.16): the hourly copy counts the volunteers he could take at each
            /// (Offers, the language's offers), so a loop can pass by a stop that has nothing for him.
            /// </summary>
            public Settlement[] Stops;

            public double Setting(string name, double otherwise) => Settings.TryGetValue(name, out double v) ? v : otherwise;
        }

        /// <summary>The order a party holds now, or null (the hourly copy, 0.16).</summary>
        internal static HandOrder HeldBy(MobileParty p)
        {
            for (int i = 0; i < Active.Count; i++) if (Active[i].Party == p) return Active[i];
            return null;
        }

        /// <summary>A tool order's at and places as the game's holdings, for the volunteers' count (0.16); spots and unknown ids left out.</summary>
        private static Settlement[] StopsOf(string at, IEnumerable<string> places)
        {
            var list = new List<Settlement>();
            foreach (string id in new[] { at }.Concat(places ?? Enumerable.Empty<string>()))
            {
                if (string.IsNullOrEmpty(id) || Spots.Is(id)) continue;
                Settlement s = Settlement.Find(id);
                if (s != null && !list.Contains(s)) list.Add(s);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        private sealed class Released
        {
            public MobileParty Party;
            public Hero Lord;
            public double At;
            public int Checks;
        }

        /// <summary>The one-step verbs of vassal orders (agents.vassal).</summary>
        public static readonly string[] Verbs = { "goto", "garrison", "patrol", "defend", "engage", "besiege", "raid", "recruit", "hold" };

        /// <summary>Order words that are not tools: they act at once.</summary>
        private static readonly string[] Plain = { "free", "release", "adjust", "add", "disband", "wages" };

        public const string Usage =
            "Usage: agents.order <lord> <order> <place or lord> [setting=value ...] [lords=name,name]\n" +
            "  moves: goto, patrol, engage, follow, leave, recruit, hold, free\n" +
            "  tools: defend, garrison, besiege, raid, hunt, screen, rest, sell; armies: gather, relieve, add, release, disband; adjust\n" +
            "  money: wages cap=<a day> | wages none | wages game\n" +
            "  examples: agents.order Throkmaw defend Caradhras radius=25\n" +
            "            agents.order Krumbash gather Methedras lords=Naglur,Vughza\n" +
            "  agents.tools lists every order with its settings. Names can be partial, accents optional, or ids.";

        private const string VassalUsage =
            "Usage: agents.vassal <your lord> <verb> <target>\n" +
            "  verbs: goto <place>, garrison <castle or town>, patrol <place>, defend <place>, engage <lord>, besiege <castle or town>,\n" +
            "         raid <village>, recruit <town, castle or village> (tours on from there), hold";

        private const double RecruitDays = 3;
        private const double StuckHours = 6;
        private static readonly List<HandOrder> Active = new List<HandOrder>();

        /// <summary>The orders held now, read only (0.16: the recruit tour passes by a stop another of the realm's lords is bound for). Game's thread.</summary>
        internal static IReadOnlyList<HandOrder> ActiveOrders => Active;
        /// <summary>The meeting place of an army being formed by an order, for the army's notice (the game fires it before the place is set).</summary>
        internal static Settlement FormingAt;
        /// <summary>Who gave that order (agent, you, hand), for a test run's record of the army (it read the leader's order, not yet in force: 0.11.4).</summary>
        internal static string FormingBy;
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
            RecruitTour.Reset();
            Offers.Reset();
            Tools.Threats.Reset();
            PlayerBattles.Reset();
            Supply.Reset();
            Mirror();
        }

        public static HashSet<MobileParty> HeldParties() => new HashSet<MobileParty>(Active.Where(o => o.Party != null).Select(o => o.Party));

        /// <summary>Does one of our orders hold this lord's party? (The money hooks ask on every settlement entered: no allocation.)</summary>
        public static bool Holds(MobileParty p)
        {
            for (int i = 0; i < Active.Count; i++) if (ReferenceEquals(Active[i].Party, p)) return true;
            return false;
        }

        /// <summary>Who gave the order holding this party (agent, you, hand), or null when none holds it (a test run's battles say who commanded).</summary>
        public static string SourceOf(MobileParty p)
        {
            for (int i = 0; i < Active.Count; i++) if (ReferenceEquals(Active[i].Party, p)) return Active[i].Source;
            return null;
        }

        /// <summary>For the lord watch: did we flip this party's switch since it last looked? (Consumes the mark.)</summary>
        public static bool TookSwitch(MobileParty p) => Toggled.Remove(p);

        /// <summary>The movement watcher named who changed an ordered lord's plan; kept for the next alarm about him.</summary>
        public static void Witnessed(string partyId, string who) => LastCulprit[partyId] = who;

        // ---------- the three doors ----------

        /// <summary>agents.order: the agent's orders, typed by hand for one of the watched realm's lords (always carried out).</summary>
        public static string Give(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            if (Bridge.Watched == null) return "No realm is under watch, so there is no one to order. Choose one with agents.watch.";
            if (args == null || args.Count < 2) return Usage;
            int vi = args.FindIndex(x => Library.Get(x) != null || Plain.Contains(x.ToLowerInvariant()));
            if (vi <= 0) return Usage;
            var a = new AgentChannel.AgentOrder { File = "console", Index = 1, Received = DateTime.Now, Lord = string.Join(" ", args.Take(vi)), Do = args[vi].ToLowerInvariant() };
            var target = new List<string>();
            foreach (string w in args.Skip(vi + 1))
            {
                int eq = w.IndexOf('=');
                if (eq > 0)
                {
                    string k = w.Substring(0, eq).Trim().ToLowerInvariant(), v = w.Substring(eq + 1).Trim();
                    if (k == "lords") a.Lords.AddRange(v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0));
                    else if (double.TryParse(v, System.Globalization.NumberStyles.Float, Text.Inv, out double d)) a.Settings[k] = d;
                    else return "'" + w + "': a setting takes a number, for example radius=25.";
                }
                else target.Add(w);
            }
            a.Target = target.Count > 0 ? string.Join(" ", target) : null;
            // the watched realm the named lord serves (the first realm's if none of them has him: its refusal says so)
            Kingdom realm = Bridge.Seats.FirstOrDefault(k => FindLord(k, a.Lord, out _) != null) ?? Bridge.Watched;
            a.Realm = realm.StringId;
            string status = "refused";
            string reason = Issue(a, realm, "hand", true, ref status);
            if (status == "posted") return "Sent to the tool runner; the answer appears in the agent's receipts and the log.";
            return (status == "applied" ? "Order given: " : "Refused: ") + reason + (status == "applied" ? ".\nagents.orders shows how it goes; agents.release " + a.Lord + " ends it." : "");
        }

        /// <summary>agents.vassal: the player orders one of their own lords directly, with the one-step verbs.</summary>
        public static string GiveVassal(List<string> args)
        {
            if (Campaign.Current == null) return "Load a campaign first.";
            Kingdom realm = Clan.PlayerClan?.Kingdom;
            if (realm == null) return "You are not in a realm, so there are no vassals to order.";
            if (!Split(args, out string lordWords, out string verb, out string targetWords))
                return VassalUsage.Replace("agents.order", "agents.vassal");
            Hero lord = FindLord(realm, lordWords, out string error);
            if (lord == null) return Refusal("you", lordWords, verb, targetWords, error);
            if (lord == Hero.MainHero) return "That is you.";
            string pac = PacReader.OrderOf(lord);
            if (pac != null)
                return Refusal("you", lordWords, verb, targetWords, Names.Hero(lord) + " already holds an order from your dialogue (Party AI Controls: " + pac
                       + "). Release the lord there first (\"Act as you see fit\"), so the two do not fight each hour.");
            HandOrder o = Prepare(lord, verb, targetWords, realm, out error);
            if (o == null) return Refusal("you", lordWords, verb, targetWords, error);
            o.Source = "you";
            Start(o, lordWords + " " + verb + " " + targetWords);
            Recorder.OnOwnOrder(o.Lord, o.Verb, o.Place, o.Target, "You ordered " + Describe(o) + ".");
            return "Order given: " + Describe(o) + ". Recorded as your decision. Release the lord with agents.release " + FirstWord(Names.Hero(lord)) + ".";
        }

        /// <summary>One order from the agent's answer file (AgentChannel). Posts the receipt's status and reason; told (the lieutenant's, 0.10) hears them too, at once.</summary>
        public static void FromAgent(AgentChannel.AgentOrder a, Action<string, string> told = null)
        {
            string status = "refused", reason;
            try
            {
                // an answer orders only the lords of the realm whose channel it came by
                Kingdom realm = a.Realm != null ? Bridge.SeatOf(a.Realm) : Bridge.Watched;
                reason = realm == null ? (a.Realm != null ? a.Realm + " is no longer under watch" : "no realm is under watch") : Issue(a, realm, "agent", false, ref status);
                if (status == "applied" && reason != null && realm != null)
                {
                    // the game takes a move given in a battle, but the party moves only once the battle ends (0.7.4, Naglur in the storm of Hollin Gate)
                    MobileParty p = FindLord(realm, a.Lord, out _)?.PartyBelongedTo;
                    if (p != null && p.MapEvent != null) reason += " (" + Names.Party(p) + " is in a battle now: no order moves it until the battle ends)";
                }
            }
            catch (Exception e)
            {
                reason = "it could not be carried out (" + e.Message + ")";
                Diag.Error("carrying out an agent's order", e);
            }
            try { told?.Invoke(status, reason); }
            catch (Exception e) { Diag.Error("telling the lieutenant what became of an order", e); }
            if (status == "posted") return;   // the tool runner writes this one's receipt
            double hours = CampaignTime.Now.ToHours;
            string s = status, r = reason;
            Background.Post("agent receipt", () => AgentChannel.Receipt(a, s, r, hours));
        }

        /// <summary>
        /// One order of the agent's (or typed by hand): checked against the game as it stands, then carried out, checked
        /// only (shadow mode), or refused. Sets status to applied, checked, refused, or posted (the runner answers).
        /// </summary>
        private static string Issue(AgentChannel.AgentOrder a, Kingdom realm, string source, bool forceLive, ref string status)
        {
            // the ruler's own army, formed or added to by the lieutenant (0.13.9): the order needs no lord of its own
            if (string.Equals((a.Do ?? "").Trim(), "my_army", StringComparison.OrdinalIgnoreCase)) return RulersArmy(a, realm, source, forceLive, ref status);
            if (string.IsNullOrWhiteSpace(a.Lord)) return "the order names no lord";
            // gold from one lord's purse to another's (0.18.3): the giver may lead no party (a clan's leader in a holding still has his purse)
            if (string.Equals((a.Do ?? "").Trim(), "gift", StringComparison.OrdinalIgnoreCase)) return GiftOrder(a, realm, forceLive, ref status);
            Hero lord = FindLord(realm, a.Lord, out string error);
            if (lord == null) return error;
            // the two guards the vassal door always had (Oct 22): never the player, never a lord on a Party AI Controls order; the
            // ruler's lieutenant's orders replace a Party AI Controls order instead, and show there (0.11.1, PacLink)
            if (lord == Hero.MainHero) return Names.Hero(lord) + " takes no orders from an agent";
            bool lieutenant = source == "agent" && Lieutenant.IsRealm(realm.StringId);
            string pacOrder = lieutenant ? null : PacReader.OrderOf(lord);
            if (pacOrder != null)
                return Names.Hero(lord) + " already follows an order from his ruler's own dialogue (" + pacOrder + "); the two would fight each hour, so it stands until the ruler releases him there";
            string verb = (a.Do ?? "").Trim().ToLowerInvariant();
            if (verb == "party_ai")
            {
                if (!lieutenant) return "party_ai is only for the ruler's lieutenant";
                MobileParty lp = lord.PartyBelongedTo;
                if (lp == null || lp.LeaderHero != lord || !lp.IsActive) return Names.Hero(lord) + " is not leading a party right now.";
                bool sh = !forceLive && ModeOf(realm) == "shadow";
                if (!forceLive && !sh && !(ModeOf(realm) == "live" && ObeysAgent(lord))) return NotYours(lord);
                return PacLink.Apply(lord, lp, a.Raw, realm, sh, ref status);
            }
            // the agent of the realm decides (its agent.txt, since 0.9): its mode, and who obeys it
            string mode = ModeOf(realm);
            bool live = forceLive || (mode == "live" && ObeysAgent(lord));
            bool shadow = !forceLive && mode == "shadow";
            MobileParty party = lord.PartyBelongedTo;
            string name = Names.Hero(lord);

            if (verb == "free" || (verb == "release" && a.Lords.Count == 0))
            {
                HandOrder held = Active.FirstOrDefault(x => x.Lord == lord);
                if (shadow) { status = "checked"; return "valid: " + name + " would be left to the game's own AI (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                if (held != null)
                {
                    End(held, "released by the " + (source == "hand" ? "console" : "agent"), "released");
                    Active.Remove(held);
                    Changed();
                }
                status = "applied";
                return name + " is led by the game's own AI again";
            }
            if (party == null || party.LeaderHero != lord || !party.IsActive) return name + " is not leading a party right now.";
            if (verb == "wages") return WagesOrder(lord, party, a, realm, shadow, live, ref status);
            bool armyVerb = verb == "adjust" || verb == "add" || verb == "release" || verb == "disband";
            // the ruler's lieutenant may order a lord marching in an army: he is taken out of it first, once his order has passed its
            // checks (0.13.8, Oct 29: lords that should be controlled by my lieutenant kept escaping to the game's AI); an agent
            // still orders the army's leader (release, disband)
            bool takeOut = lieutenant && !armyVerb && party.Army != null && party.Army.LeaderParty != party;
            if (!takeOut && party.AttachedTo != null && party.Army?.LeaderParty != party)
                return name + " marches in " + Names.Party(party.Army?.LeaderParty) + "'s army. Order the army's leader instead.";

            if (verb == "adjust")
            {
                HandOrder held = Active.FirstOrDefault(x => x.Lord == lord && x.ToolId > 0);
                if (held == null) return name + " holds no tool to adjust; give an order instead";
                if (shadow) { status = "checked"; return "valid: " + name + "'s " + held.Tool + " would get new settings (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                int id = held.ToolId;
                var settings = new Dictionary<string, double>(a.Settings, StringComparer.OrdinalIgnoreCase);
                Background.PostFirst("tool adjust", () => Runner.Adjust(id, settings, a));
                status = "posted";
                return null;
            }
            if (verb == "add" || verb == "release" || verb == "disband") return ArmyChange(verb, lord, party, a, realm, source, shadow, live, ref status);

            // the tool its answer wrote, else ours, else one the realm's agent kept (0.13: a next order or the console names it)
            ToolDef def = a.Tool ?? Library.Get(verb) ?? OwnLibrary.Find(realm.StringId, verb);
            if (def == null)
            {
                List<string> own = OwnLibrary.Names(realm.StringId);
                return "'" + verb + "' is not an order. Orders: " + string.Join(", ", Library.All.Keys.OrderBy(k => k)) + ", " + string.Join(", ", Plain)
                       + (own.Count > 0 ? "; and your own: " + string.Join(", ", own) : "") + ".";
            }
            ToolOrder o = PrepareTool(lord, party, def, a, realm, out error, out List<string> notes);
            if (o == null) return error;
            // a recruiting order he cannot pay for is refused up front, with why (0.16; test A1: 47 of Dol Guldur's 64 recruit orders ended at the wage cap or the
            // gold floor, 29 lords again and again)
            string unpaid = RecruitRefusal(def, party);
            if (unpaid != null)
            {
                Diag.Count("order:recruiting refused up front");
                Diag.Event("order", "recruiting refused", new { source, poke = a.Poke, lord = lord.StringId, tool = def.Name, bar = Offers.Bar(party), wages = party.TotalWage, cap = party.HasLimitedWage() ? party.PaymentLimit : -1, gold = lord.Gold });
                return name + "'s " + def.Name + " order was not given: " + unpaid;
            }
            o.Source = source;
            string words = ((a.Lord ?? "") + " " + verb + " " + (a.At ?? a.Target ?? "")).Trim();
            o.Words = words;
            if (takeOut && live && !shadow) notes.Add(LeaveArmy(party));
            if (def.Name == "gather") return Gather(lord, party, def, o, a, realm, shadow, live, notes, ref status);
            string text = DescribeTool(def, o);
            if (shadow)
            {
                status = "checked";
                Diag.Event("order", "checked", new { source, poke = a.Poke, lord = lord.StringId, tool = def.Name, at = o.At, target = o.Target, lords = o.Lords, settings = o.Settings });
                return "valid: " + text + Notes(notes) + " (shadow mode: not carried out)";
            }
            if (!live) return NotYours(lord);
            // the very same order again keeps the one already running (0.12.0, run 20261025-1417-C2: Beleg's attack was given four
            // times while the first waited to start, and each new copy started the wait over)
            string sig = o.Signature();
            HandOrder same = Active.FirstOrDefault(x => x.Party == party && x.ToolId > 0 && x.Signature == sig);
            if (same != null)
            {
                status = "applied";
                a.ToolRun = same.ToolId;
                Diag.Count("order:kept as it was");
                Diag.Event("order", "kept", new { id = same.Id, source, poke = a.Poke, lord = lord.StringId, tool = def.Name });
                return name + " already holds this very order (" + same.ToolText + "): it goes on as it was, not started again";
            }
            // our siege order again for the place he already besieges or marches on, with new numbers, partners or next order (0.17.5, Nov 8, test A1:
            // Dol Guldur gave its siege orders again three times in forty minutes to change them, and each new order took the lords out of the camp,
            // ending a siege just begun): the siege goes on where it stands and takes the new order's numbers, partners and next order
            if (Library.Get(def.Name) == def && def.Name == "besiege")
            {
                HandOrder running = Active.FirstOrDefault(x => x.Party == party && x.ToolId > 0 && x.Tool == "besiege" && x.Stops != null && x.Stops.Length > 0 && x.Stops[0]?.StringId == o.At);
                if (running != null)
                {
                    foreach (SettingDef sd in def.Settings.Values)
                        running.Settings[sd.Name] = o.Settings.TryGetValue(sd.Name, out double sv) ? Math.Max(sd.Min, Math.Min(sd.Max, sv)) : sd.Default;
                    running.Signature = sig;
                    running.HasNext = o.Then != null;
                    running.ToolText = text;
                    running.Poke = o.Poke;
                    int tid = running.ToolId;
                    o.Id = tid;
                    a.ToolRun = tid;
                    Background.PostFirst("siege order given again", () => Runner.GivenAgain(tid, o));
                    Diag.Count("order:siege kept with the new order's numbers");
                    Note(running, "given again", text, new { words, lords = o.Lords, settings = o.Settings, then = o.Then?.ToString(Newtonsoft.Json.Formatting.None) }, quiet: true);
                    status = "applied";
                    return text + Notes(notes) + ". He already holds a siege order for this place, so it goes on where it stands (not started over) with these numbers, partners and next order";
                }
            }
            string shown = StartTool(lord, party, def, o, realm, words);
            a.ToolRun = o.Id;
            status = "applied";
            return text + Notes(notes) + shown;
        }

        private static string Notes(List<string> notes) => notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "";

        /// <summary>Why a recruiting order (ToolDef.IsRecruiting) is refused up front: his wage cap, his gold or his clan's purse leaves him unable to pay for any recruit; null otherwise.</summary>
        private static string RecruitRefusal(ToolDef def, MobileParty party)
        {
            if (def == null || !def.IsRecruiting) return null;
            string bar = Offers.Bar(party);
            return bar == null ? null : Offers.BarWords(party, bar) + ".";
        }

        /// <summary>The mode of the agent playing that realm (off, shadow or live).</summary>
        private static string ModeOf(IFaction realm) => Agents.Of(realm?.StringId)?.Config.Mode ?? Settings.Agent;
        /// <summary>Who obeys the agent playing that realm (all, or ids and names).</summary>
        private static string LordsOf(IFaction realm) => Agents.Of(realm?.StringId)?.Config.Lords ?? Settings.AgentLords;

        private static bool ObeysAgent(Hero lord) => Obeys(LordsOf(lord.MapFaction), lord);

        /// <summary>Does this lord obey an agent whose agent.txt names these lords (all, or ids and names)?</summary>
        internal static bool Obeys(string lords, Hero lord)
        {
            string list = (lords ?? "all").Trim();
            if (list.Length == 0 || Text.Plain(list) == "all") return true;
            string name = Text.Plain(Names.Hero(lord));
            char[] separators = list.Contains(",") ? new[] { ',' } : new[] { ' ' };
            return list.Split(separators).Select(x => x.Trim()).Where(x => x.Length > 0)
                       .Any(x => string.Equals(x, lord.StringId, StringComparison.OrdinalIgnoreCase) || Text.Plain(x) == name);
        }

        private static string NotYours(Hero lord)
            => Names.Hero(lord) + " does not take your orders in this test; only " + LordsOf(lord.MapFaction) + " do"
               + (ModeOf(lord.MapFaction) == "off" ? " (the agent channel is off)" : "");

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
                "- #" + o.Id + " [" + o.Source + "] " + (o.ToolId > 0 ? o.ToolText + "; now " : "") + Describe(o) + "; for " + Math.Round(now - o.Since, 1).ToString(Text.Inv) + " in-game hours"
                + (o.Arrived ? ", arrived" : DistanceText(o)) + "; turned back to the order " + o.Reissued + " times; now: " + Doing(o.Party)));
        }

        // ---------- tools: checked here, run by the worker, moved here ----------

        /// <summary>Checks a tool order's lord, places, lords and numbers against the game as it stands; ids only leave.</summary>
        private static ToolOrder PrepareTool(Hero lord, MobileParty party, ToolDef def, AgentChannel.AgentOrder a, Kingdom realm, out string error, out List<string> notes)
        {
            error = null;
            notes = new List<string>();
            // an agent's own tool goes with the order, the very version it was given with (0.13); ours is found by name
            var o = new ToolOrder { Tool = def.Name, Def = def.Own ? def : null, Lord = lord.StringId, LordName = Names.Hero(lord), Party = party.StringId, Why = a.Why, Then = a.Then, Poke = a.Poke };
            if (def.At != null)
            {
                string words = a.At ?? a.Target;
                if (string.IsNullOrWhiteSpace(words)) { error = def.Name + " needs \"at\": " + ToolDef.Plain(def.At) + "."; return null; }
                if (def.At == "spot_or_holding" && Spots.TryParse(words, out float sx, out float sy))
                {
                    // a spot: it must be ground a party can stand on (the game's own test), or next to it
                    string spot = WalkableSpot(sx, sy, out error);
                    if (spot == null) return null;
                    if (spot != Spots.Id(sx, sy)) notes.Add(Spots.Id(sx, sy) + " is not walkable ground, so the post is the nearest spot that is, " + spot);
                    o.At = spot;
                }
                else
                {
                    Settlement s = FindSettlement(words, out error);
                    if (s == null) return null;
                    error = CheckAt(def.At, s, realm);
                    if (error != null) return null;
                    o.At = s.StringId;
                }
            }
            if (def.Target != null)
            {
                string words = a.Target ?? a.At;
                if (string.IsNullOrWhiteSpace(words)) { error = def.Name + " needs \"target\": " + ToolDef.Plain(def.Target) + "."; return null; }
                MobileParty t = FindParty(words, out error);
                if (t == null) return null;
                if (def.Target == "enemy_lord" && !FactionManager.IsAtWarAgainstFaction(t.MapFaction, realm)) { error = Names.Party(t) + " is not at war with " + Names.Faction(realm) + "."; return null; }
                if (def.Target == "own_lord" && (t.MapFaction != realm || t == party)) { error = Names.Party(t) + " is not another lord of " + Names.Faction(realm) + "."; return null; }
                if (t.AttachedTo != null && t.Army?.LeaderParty != null)
                {
                    notes.Add(Names.Party(t) + " marches in " + Names.Party(t.Army.LeaderParty) + "'s army, so the order is for that army");
                    t = t.Army.LeaderParty;
                }
                o.Target = t.StringId;
            }
            if (def.Lords)
                foreach (string w in a.Lords)
                {
                    Hero h = FindLord(realm, w, out error);
                    if (h == null) return null;
                    MobileParty p = h.PartyBelongedTo;
                    if (p == null || p.LeaderHero != h) { error = Names.Hero(h) + " is not leading a party right now."; return null; }
                    // the lord himself in his own list (0.13.16): left out with a note, not the whole order refused (Nov 1: the orcs' council gave 13
                    // lords each a besiege of Sirannon naming all 13 to wait for, the lord himself among them, and all 13 were refused)
                    if (p == party) { notes.Add(Names.Hero(h) + " was left out of \"lords\": he is the lord being ordered"); continue; }
                    if (!o.Lords.Contains(p.StringId)) o.Lords.Add(p.StringId);
                }
            else if (a.Lords.Count > 0) notes.Add(def.Name + " takes no lords, so they were left out");
            // a tool's list of holdings (0.13), each of the kind it takes, in the order given
            if (def.Places != null && a.Places.Count == 0 && def.PlacesDefault == "at_and_villages" && o.At != null && Settlement.Find(o.At) is Settlement home)
            {
                // no places given (0.16, the standard defend): the holding itself, then its villages, each next the nearest from the last
                o.Places.Add(home.StringId);
                var left = home.IsFortification && home.BoundVillages != null
                    ? home.BoundVillages.Select(v => v?.Settlement).Where(v => v != null && CheckAt(def.Places, v, realm) == null).ToList()
                    : new List<Settlement>();
                Vec2 from = home.GatePosition.ToVec2();
                while (left.Count > 0)
                {
                    Settlement next = left.OrderBy(v => v.GatePosition.ToVec2().DistanceSquared(from)).First();
                    o.Places.Add(next.StringId);
                    left.Remove(next);
                    from = next.GatePosition.ToVec2();
                }
                notes.Add("no places were given, so the loop is " + string.Join(", ", o.Places.Select(id => Names.Settlement(Settlement.Find(id)))));
            }
            else if (def.Places != null)
            {
                if (a.Places.Count == 0) { error = def.Name + " needs \"places\": a list, each " + ToolDef.Plain(def.Places) + "."; return null; }
                foreach (string w in a.Places)
                {
                    Settlement s = FindSettlement(w, out error);
                    if (s == null) { error = "places: " + error; return null; }
                    error = CheckAt(def.Places, s, realm);
                    if (error != null) { error = "places: " + error; return null; }
                    if (!o.Places.Contains(s.StringId)) o.Places.Add(s.StringId);
                }
            }
            else if (a.Places.Count > 0) notes.Add(def.Name + " takes no places, so they were left out");
            foreach (KeyValuePair<string, double> kv in a.Settings)
            {
                if (!def.Settings.TryGetValue(kv.Key, out SettingDef sd))
                {
                    error = def.Name + " has no setting '" + kv.Key + "'" + (def.Settings.Count > 0 ? "; it has " + string.Join(", ", def.Settings.Keys) : "") + ".";
                    return null;
                }
                double v = Math.Max(sd.Min, Math.Min(sd.Max, kv.Value));
                if (Math.Abs(v - kv.Value) > 1e-9) notes.Add(kv.Key + " kept to " + ToolDef.Num(v) + ", its range is " + ToolDef.Num(sd.Min) + " to " + ToolDef.Num(sd.Max));
                o.Settings[sd.Name] = v;
            }
            if (a.Then != null)
            {
                string next = ((string)a.Then["do"] ?? "").Trim().ToLowerInvariant();
                bool own = OwnLibrary.ByHash((string)a.Then["tool_hash"]) != null || OwnLibrary.Find(realm.StringId, next) != null;
                if (Library.Get(next) == null && !own && next != "free" && next != "disband") { error = "the next order (then) is not an order: '" + next + "'."; return null; }
            }
            return o;
        }

        /// <summary>A spot's id when a party can stand there, else the nearest walkable spot within 4 units; null (and why) when there is none.</summary>
        private static string WalkableSpot(float x, float y, out string error)
        {
            error = null;
            var nav = MobileParty.NavigationType.Default;
            var at = new CampaignVec2(new Vec2(x, y), true);
            if (NavigationHelper.IsPositionValidForNavigationType(at, nav)) return Spots.Id(x, y);
            CampaignVec2 near = Campaign.Current.MapSceneWrapper.GetAccessiblePointNearPosition(in at, 4f);
            if (near.IsValid() && NavigationHelper.IsPositionValidForNavigationType(near, nav) && near.ToVec2().Distance(at.ToVec2()) <= 4.5f) return Spots.Id(near.X, near.Y);
            error = Spots.Id(x, y) + " is not ground a party can stand on (mountain, water or off the map), and nothing walkable is within 4 of it.";
            return null;
        }

        /// <summary>"4 west of Western Moria": a point on the map by its nearest holding (game's thread).</summary>
        private static string SpotText(float x, float y)
        {
            Settlement best = null;
            float bd = float.MaxValue;
            foreach (Settlement s in Settlement.All)
            {
                if (s == null || s.IsHideout) continue;
                float d = s.Position.ToVec2().Distance(new Vec2(x, y));
                if (d < bd) { bd = d; best = s; }
            }
            if (best == null) return Spots.Id(x, y);
            return bd < 1.5f ? "at " + Names.Settlement(best) : Math.Round(bd).ToString("0", Text.Inv) + " " + Spots.Compass(best.Position.X, best.Position.Y, x, y) + " of " + Names.Settlement(best);
        }

        /// <summary>Is this holding the kind the tool asks for, for this realm? Null when it is; else why not.</summary>
        private static string CheckAt(string kind, Settlement s, Kingdom realm)
        {
            bool own = s.MapFaction == realm;
            bool allied = !own && s.MapFaction is Kingdom k && realm.AlliedKingdoms.Contains(k);
            bool hostile = s.MapFaction != null && FactionManager.IsAtWarAgainstFaction(s.MapFaction, realm);
            string n = Names.Settlement(s);
            switch (kind)
            {
                case "own_fort": return own && s.IsFortification ? null : n + " is not one of " + Names.Faction(realm) + "'s castles or towns.";
                case "own_holding": return own ? null : n + " is not " + Names.Faction(realm) + "'s.";
                case "own_town_or_village": return own && (s.IsTown || s.IsVillage) ? null : n + " is not one of " + Names.Faction(realm) + "'s towns or villages.";
                case "friendly_town_or_village": return (own || allied) && (s.IsTown || s.IsVillage) ? null : n + " is not a town or village of " + Names.Faction(realm) + " or an ally.";
                case "friendly_fort": return (own || allied) && s.IsFortification ? null : n + " is not a castle or town of " + Names.Faction(realm) + " or an ally.";
                case "friendly_holding": return own || allied ? null : n + " is not " + Names.Faction(realm) + "'s or an ally's.";
                case "enemy_fort": return hostile && s.IsFortification ? null : n + " is not an enemy castle or town.";
                case "enemy_village": return hostile && s.IsVillage ? null : n + " is not an enemy village.";
                case "enemy_holding": return hostile ? null : n + " is not an enemy holding.";
                default: return null;
            }
        }

        /// <summary>Holds the lord for a tool and hands the tool to the runner, which makes the first move.</summary>
        /// <summary>Starts a tool order; returns what Party AI Controls now shows for it, for the receipt (PacLink; "" for none).</summary>
        private static string StartTool(Hero lord, MobileParty party, ToolDef def, ToolOrder o, Kingdom realm, string words)
        {
            if (realm != null) o.Realm = realm.StringId;
            int id = Runner.NewId();
            o.Id = id;
            var h = new HandOrder
            {
                Id = ++_nextId, Party = party, Lord = lord, Realm = realm, Source = o.Source, Poke = o.Poke, Verb = "hold",
                ToolId = id, Tool = def.Name, ToolText = DescribeTool(def, o), Since = CampaignTime.Now.ToHours, Signature = o.Signature(), HasNext = o.Then != null,
                Stops = StopsOf(o.At, o.Places)
            };
            // its settings as the runner keeps them (0.13.19): each given one within its range, the tool's default for the rest
            foreach (SettingDef sd in def.Settings.Values)
                h.Settings[sd.Name] = o.Settings.TryGetValue(sd.Name, out double sv) ? Math.Max(sd.Min, Math.Min(sd.Max, sv)) : sd.Default;
            object before = State(party, null);
            ReplaceHeld(party, h.Id);
            LordTours.Drop(party);   // off TFAOMFixes' recruiting trip: ours alone steers him now (0.11.3)
            Apply(h);
            Active.Add(h);
            AfterRelease.RemoveAll(r => r.Party == party);
            Changed();
            h.LastProgressAt = CampaignTime.Now.ToHours;
            Diag.Count("order:issued");
            Diag.Count("order source:" + h.Source);
            Note(h, "issued", h.ToolText, new { words, before, tool = def.Name, at = o.At, target = o.Target, lords = o.Lords, settings = o.Settings, then = o.Then?.ToString(Newtonsoft.Json.Formatting.None) });
            if (OnScreen(h)) Say("Order: " + h.ToolText + ".", Colors.Cyan);
            Background.PostFirst("tool start", () => Runner.Start(id, o));
            string shown = null;
            try { shown = PacLink.OnOrderStarted(h.Id, lord, party, h.Source, realm, def.Name, o.At, o.Target); }
            catch (Exception e) { Diag.Error("showing an order in Party AI Controls", e); }
            return shown ?? "";
        }

        // ---------- carried across a reload of the same save (0.13.5, CarryOn) ----------

        /// <summary>The tool orders held now, as plain rows for the save's carry-on file (CarryOn; game's thread).</summary>
        public static List<JObject> KeepForSave()
        {
            var list = new List<JObject>();
            foreach (HandOrder o in Active)
            {
                if (o.ToolId <= 0 || o.Party == null || o.Lord == null) continue;
                list.Add(new JObject
                {
                    ["tool_id"] = o.ToolId, ["party"] = o.Party.StringId, ["lord"] = o.Lord.StringId, ["realm"] = o.Realm?.StringId, ["source"] = o.Source, ["poke"] = o.Poke,
                    ["tool"] = o.Tool, ["text"] = o.ToolText, ["signature"] = o.Signature, ["since"] = o.Since, ["arrived"] = o.Arrived, ["arrived_at"] = o.ArrivedAt,
                    ["settings"] = JObject.FromObject(o.Settings), ["has_next"] = o.HasNext
                });
            }
            return list;
        }

        /// <summary>
        /// A tool order carried on from the save just loaded (CarryOn, 0.13.5): checked as at its start (the lord alive, free, leading his
        /// party in the realm, obeying its agent, on no Party AI Controls order unless the lieutenant's, holding no other order), then
        /// held again under its old tool run's id; the runner takes the tool up where it was. Null when carried on, else why not, in
        /// the agent's words. at and target: the order's, for Party AI Controls' panel.
        /// </summary>
        public static string Resume(JObject row, string lords, string at, string target, IEnumerable<string> places = null)
        {
            string id = (string)row["lord"];
            Hero lord = string.IsNullOrEmpty(id) ? null : Hero.Find(id);
            string who = lord != null ? Names.Hero(lord) : id ?? "a lord";
            if (lord == null || !lord.IsAlive) return who + " is no longer alive";
            if (lord.IsPrisoner) return who + " is a prisoner";
            Kingdom realm = Bridge.SeatOf((string)row["realm"]);
            if (realm == null || lord.MapFaction != realm) return who + " no longer serves the realm";
            if (lord == Hero.MainHero) return who + " takes no orders from an agent";
            MobileParty party = lord.PartyBelongedTo;
            if (party == null || party.LeaderHero != lord || !party.IsActive) return who + " is not leading a party";
            string source = (string)row["source"] ?? "agent";
            if (!(source == "agent" && Lieutenant.IsRealm(realm.StringId)))
            {
                string pac = PacReader.OrderOf(lord);
                if (pac != null) return who + " follows an order from his ruler's own dialogue (" + pac + ")";
            }
            if (!Obeys(lords, lord)) return who + " is not among the lords who obey you";
            if (Active.Any(x => x.Party == party)) return who + " holds another order already";
            int toolId = (int?)row["tool_id"] ?? 0;
            if (toolId <= 0) return who + "'s order has no tool run";
            double now = CampaignTime.Now.ToHours;
            Runner.NoteUsedId(toolId);
            var h = new HandOrder
            {
                Id = ++_nextId, Party = party, Lord = lord, Realm = realm, Source = source, Poke = (int?)row["poke"] ?? -1, Verb = "hold", ToolId = toolId,
                Tool = (string)row["tool"], ToolText = (string)row["text"], Signature = (string)row["signature"], Since = (double?)row["since"] ?? now,
                Arrived = (bool?)row["arrived"] ?? false, ArrivedAt = (double?)row["arrived_at"] ?? -1, HasNext = (bool?)row["has_next"] ?? false,
                Stops = StopsOf(at, places)
            };
            // its settings (0.13.19; a save of 0.13.18 or before has none: garrison_floor 0, the supply order's defaults)
            if (row["settings"] is JObject set)
                foreach (JProperty sp in set.Properties())
                    if (sp.Value.Type == JTokenType.Integer || sp.Value.Type == JTokenType.Float) h.Settings[sp.Name] = (double)sp.Value;
            LordTours.Drop(party);   // off TFAOMFixes' recruiting trip, as at any order's start
            Apply(h);
            Active.Add(h);
            AfterRelease.RemoveAll(r => r.Party == party);
            Changed();
            h.LastProgressAt = now;
            Diag.Count("order:carried on");
            Diag.Event("order", "carried on", new { id = h.Id, tool_run = toolId, lord = lord.StringId, tool = h.Tool, source, since = h.Since, text = h.ToolText });
            try { PacLink.OnOrderStarted(h.Id, lord, party, source, realm, h.Tool, at, target); }
            catch (Exception e) { Diag.Error("showing a carried-on order in Party AI Controls", e); }
            return null;
        }

        /// <summary>
        /// The ruler's own army (0.13.9, Oct 29: the lieutenant should be able to form an army for me, under my name (so for example if I ask it to get
        /// every lord stationed at Imnagath to join my army, it should be able to form an army for me and automatically have them join it)): "my_army" with
        /// "lords" (and "at", where it gathers, else the ruler's home). The player's party leads it: formed as the game's own army screen forms one when the
        /// ruler leads none (Kingdom.CreateArmy, a defending army), else added to; each lord named is called in at the game's price
        /// (CalculatePartyInfluenceCost), paid from the ruler's clan's influence as that screen charges it (the game charges nothing itself for a lord joining
        /// the player's army); a lord held by an order is let go so the game marches him to the ruler; one marching in another lord's army is taken out of it
        /// first (never that army's own leader); one too far to be called yet marches to join and is called once close enough. Only the lieutenant.
        /// </summary>
        private static string RulersArmy(AgentChannel.AgentOrder a, Kingdom realm, string source, bool forceLive, ref string status)
        {
            if (!forceLive && !(source == "agent" && Lieutenant.IsRealm(realm.StringId))) return "my_army is only for the ruler's lieutenant";
            string mode = ModeOf(realm);
            bool shadow = !forceLive && mode == "shadow", live = forceLive || mode == "live";
            MobileParty me = MobileParty.MainParty;
            Hero ruler = Hero.MainHero;
            if (me == null || ruler == null || !me.IsActive || ruler.IsPrisoner || me.LeaderHero != ruler) return "the ruler leads no party now";
            if (me.MapFaction != realm) return "the ruler's party is not of " + Names.Faction(realm);
            if (me.Army != null && me.Army.LeaderParty != me) return "the ruler marches in " + Names.Party(me.Army.LeaderParty) + "'s army, so cannot lead one";
            if (me.Army == null && !Campaign.Current.Models.ArmyManagementCalculationModel.CanPlayerCreateArmy(out TaleWorlds.Localization.TextObject cannot))
                return "the game does not let the ruler form an army now: " + cannot;
            if (a.Lords.Count == 0) return "my_army needs the lords to call, for example \"lords\": [\"lord_X\", \"lord_Y\"].";
            var ids = new List<string>();
            foreach (string w in a.Lords)
            {
                Hero h = FindLord(realm, w, out string error);
                if (h == null) return error;
                if (h == ruler) continue;
                MobileParty p = h.PartyBelongedTo;
                if (p == null || p.LeaderHero != h || !p.IsActive) return Names.Hero(h) + " is not leading a party right now.";
                if (!forceLive && !shadow && !ObeysAgent(h)) return NotYours(h);
                if (!ids.Contains(p.StringId)) ids.Add(p.StringId);
            }
            List<MobileParty> members = Callable(me, ids, realm, out List<string> leftOut, out int cost, out List<MobileParty> far, fromOtherArmies: true);
            if (members.Count == 0 && far.Count == 0) return "none of these lords can come: " + string.Join("; ", leftOut) + ".";
            var notes = new List<string>();
            if (leftOut.Count > 0) notes.Add("cannot come: " + string.Join("; ", leftOut));
            float influence = ruler.Clan?.Influence ?? 0;
            if (cost > influence) return "calling them costs " + cost + " influence, and your clan has " + (int)influence + ". Call fewer lords, or nearer ones.";
            int farCost = CostNow(me, far);
            if (far.Count > 0)
                notes.Add("too far to be called yet, so they march to join and are called once close enough (about " + farCost + " influence more, less as they come nearer): "
                          + string.Join(", ", far.Select(m => Names.Hero(m.LeaderHero))));
            Settlement place = !string.IsNullOrWhiteSpace(a.At) ? Settlement.Find(a.At.Trim().Trim('[', ']')) : null;
            bool forms = me.Army == null;
            string who = members.Count > 0 ? string.Join(", ", members.Select(m => Names.Hero(m.LeaderHero))) : "no lord at once";
            string text = (forms ? "Your army is formed; " : "Into your army: ") + who + " called for " + cost + " influence (your clan had " + (int)influence + ")";
            if (shadow) { status = "checked"; return "valid: " + text + Notes(notes) + " (shadow mode: not carried out)"; }
            if (!live) return "your lieutenant is not in command";
            if (forms)
            {
                // where it gathers: the holding named, else the ruler's home (as the game's army screen), else the realm's nearest castle or town
                Settlement gatherAt = place ?? ruler.HomeSettlement ?? Settlement.All.Where(s => s != null && s.MapFaction == realm && s.IsFortification)
                                                                                    .OrderBy(s => s.GetPosition2D.DistanceSquared(me.GetPosition2D)).FirstOrDefault();
                if (gatherAt == null) return "the realm has no castle or town to name as your army's gathering place";
                Witness.Ours = true;
                FormingAt = gatherAt;
                FormingBy = source;
                try { realm.CreateArmy(ruler, gatherAt, Army.ArmyTypes.Defender); }   // as the game's army screen does
                finally
                {
                    Witness.Ours = false;
                    FormingAt = null;
                    FormingBy = null;
                }
                if (me.Army == null) return "the game did not form your army (no reason given)";
            }
            Army army = me.Army;
            foreach (MobileParty m in members)
            {
                if (m.Army != null && m.Army != army) notes.Add(Names.Hero(m.LeaderHero) + " " + LeaveArmy(m));
                ReleaseHeld(m, "called to your army");
                Witness.Ours = true;
                try { m.Army = army; }   // the game marches him to you
                finally { Witness.Ours = false; }
            }
            // the game charges nothing for lords joining the player's army: its army screen charges the clan, and so does this
            if (cost > 0) ChangeClanInfluenceAction.Apply(ruler.Clan, -cost);
            Diag.Count("army:ruler's", members.Count);
            Diag.Event("army", forms ? "ruler's formed" : "ruler's added", new { members = members.Select(m => m.LeaderHero?.StringId).ToList(), far = far.Select(m => m.LeaderHero?.StringId).ToList(), cost, influence_before = (int)influence, place = place?.StringId });
            if (far.Count > 0) SendToJoin(far, me, new ToolOrder { Poke = a.Poke, Source = source }, realm);
            status = "applied";
            return text + Notes(notes);
        }

        /// <summary>A lord taken out of the army he marches in, for an order of the lieutenant's (0.13.8); says so for the receipt.</summary>
        private static string LeaveArmy(MobileParty party)
        {
            string from = Names.Party(party.Army?.LeaderParty);
            party.Army = null;   // as release does
            Diag.Count("army:left for an order");
            Diag.Event("order", "left an army", new { lord = party.LeaderHero?.StringId, army = from });
            return "taken out of " + from + "'s army first";
        }

        /// <summary>Whatever order the party held before ends here, quietly: the new one replaces it.</summary>
        private static void ReplaceHeld(MobileParty party, int byId)
        {
            foreach (HandOrder old in Active.Where(x => x.Party == party).ToList())
            {
                Active.Remove(old);
                RecruitTour.Forget(old.Id);
                Diag.Event("order", "replaced", new { id = old.Id, by = byId, lord = old.Lord?.StringId, tool = old.Tool });
                if (old.ToolId > 0)
                {
                    int tid = old.ToolId;
                    Background.PostFirst("tool replaced", () => Runner.Ended(tid, "replaced by a new order"));
                }
            }
        }

        /// <summary>The runner's next move for a tool's lord (from the worker).</summary>
        public static void ToolMove(int toolId, Move m)
        {
            HandOrder o = Active.FirstOrDefault(x => x.ToolId == toolId);
            if (o == null || o.Party == null || !o.Party.IsActive) return;   // the order ended meanwhile
            o.Verb = m.Verb;
            o.Step = m.Step;
            o.Place = m.Place != null ? Settlement.Find(m.Place) : null;
            o.Target = m.Party != null ? FindPartyById(m.Party) : null;
            o.X = m.X;
            o.Y = m.Y;
            o.Radius = m.R;
            o.Radius0 = m.R0;
            o.HasPoint = false;
            o.Arrived = false;
            o.ArrivedAt = -1;
            o.ReissueTimes.Clear();
            o.StuckAlarmed = o.FightAlarmed = false;
            // the supply move (0.13.19): its holding stays while Place becomes a market on the way; it starts by choosing
            if (o.Verb == "supply")
            {
                o.SupplyFor = o.Place;
                Supply.Started(o);
            }
            if (!Possible(o)) o.Verb = "hold";
            Apply(o);
            o.BestDistance = Distance(o) ?? float.MaxValue;
            o.LastProgressAt = CampaignTime.Now.ToHours;
            Diag.Event("order", "moved", new { id = o.Id, tool = o.Tool, lord = o.Lord?.StringId, move = m.ToString(), made = Describe(o), took = OnCourse(o) });
            Log.Line("order #" + o.Id + " [" + o.Source + "] " + o.Tool + ": " + Describe(o));
            // a lord already close enough joins at once, rather than at the next hour
            if (o.Verb == "join" && TryJoin(o))
            {
                Active.Remove(o);
                Changed();
            }
        }

        /// <summary>A tool finished and frees its lord (from the worker).</summary>
        public static void ToolRelease(int toolId, string why)
        {
            HandOrder o = Active.FirstOrDefault(x => x.ToolId == toolId);
            if (o == null) return;
            End(o, why, "tool done");
            Active.Remove(o);
            Changed();
        }

        /// <summary>A tool finished and its next order (then) starts for the same lord, checked against the game as it stands now.</summary>
        public static void ToolThen(int toolId, JObject then)
        {
            HandOrder o = Active.FirstOrDefault(x => x.ToolId == toolId);
            if (o == null || o.Lord == null) return;
            StartNext(o.Lord, o.Realm ?? Bridge.Watched, o.Source, o.Poke, then, reason =>
            {
                // the next order no longer fits: the lord stops, and the agent hears why
                End(o, "its next order was refused: " + reason, "then refused");
                Active.Remove(o);
                Changed();
            });
        }

        /// <summary>
        /// A supply or recruit order ended and its next order (then) starts for the same lord (0.14.2; Runner.MoveEnded). Its hold is already over, so
        /// there is none to replace: a lord who holds another order by now (an answer that came in meanwhile) keeps that one. A refusal is told to
        /// the agent as news, since the lord is back with the game's own AI.
        /// </summary>
        public static void MoveThen(string lordId, string realmId, string source, int poke, JObject then)
        {
            Hero lord = string.IsNullOrEmpty(lordId) ? null : Hero.Find(lordId);
            if (lord == null) return;
            if (Active.Any(x => x.Lord == lord))
            {
                Diag.Event("order", "next order passed over", new { lord = lordId, why = "the lord holds a newer order", next = then.ToString(Newtonsoft.Json.Formatting.None) });
                return;
            }
            Kingdom realm = Bridge.SeatOf(realmId) ?? Bridge.Watched;
            StartNext(lord, realm, source, poke, then, reason =>
            {
                string line = Names.Hero(lord) + "'s next order was refused: " + reason;
                Log.Line("order: " + line);
                Diag.Event("order", "next order refused", new { lord = lordId, reason, next = then.ToString(Newtonsoft.Json.Formatting.None) });
                if (realm != null && Bridge.IsSeat(realm)) Notices.Order(realm, "order_ended", line + ".", lord.PartyBelongedTo?.StringId);
            });
        }

        /// <summary>A next order (then), checked against the game as it stands now; what to do when it is refused, before the agent's receipt.</summary>
        private static void StartNext(Hero lord, Kingdom realm, string source, int poke, JObject then, Action<string> refused)
        {
            var a = AgentChannel.OrderFrom(then, lord.StringId, poke, "then");
            a.Realm = realm?.StringId;
            string status = "refused";
            string reason;
            try { reason = realm == null ? "no realm is under watch" : Issue(a, realm, source, true, ref status); }
            catch (Exception e) { reason = "it could not be carried out (" + e.Message + ")"; Diag.Error("starting a next order", e); }
            if (status != "applied" && status != "posted") refused(reason);
            double hours = CampaignTime.Now.ToHours;
            if (source == "agent" && status != "posted")
            {
                string s = status, r = reason;
                Background.Post("agent receipt", () => AgentChannel.Receipt(a, s, r, hours));
            }
        }

        /// <summary>Can the current move still be made? If not, the lord holds and the tool decides next hour.</summary>
        private static bool Possible(HandOrder o)
        {
            Kingdom realm = o.Realm;
            switch (o.Verb)
            {
                case "engage": return o.Target != null && o.Target.IsActive && realm != null && FactionManager.IsAtWarAgainstFaction(o.Target.MapFaction, realm);
                case "follow":
                case "join": return o.Target != null && o.Target.IsActive && o.Target != o.Party;
                case "shadow": return o.Target != null && o.Target.IsActive;
                case "goto":
                case "enter":
                case "recruit":
                case "garrison":
                case "sell":
                    return o.Place != null && (realm == null || o.Place.MapFaction == null || !FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm));
                case "tour": return true;   // a stop that fell or emptied: the tour picks the next one itself
                // the supply move (0.13.19): its holding must still be friendly; Supply.Step ends it when the holding is lost or besieged
                case "supply":
                    Settlement fed = o.SupplyFor ?? o.Place;
                    return fed != null && (realm == null || fed.MapFaction == null || !FactionManager.IsAtWarAgainstFaction(fed.MapFaction, realm));
                case "wait": return true;
                case "besiege":
                case "raid":
                    return o.Place != null && realm != null && o.Place.MapFaction != null && FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm);
                case "near":
                case "patrol":
                case "defend":
                    return o.Place != null;
                default: return true;
            }
        }

        private static string DescribeTool(ToolDef def, ToolOrder o)
        {
            string lord = o.LordName;
            string at = o.At != null && !Spots.Is(o.At) ? Names.Settlement(Settlement.Find(o.At)) : null;
            string target = o.Target != null ? Names.Party(FindPartyById(o.Target)) : null;
            string with = o.Lords.Count > 0 ? string.Join(", ", o.Lords.Select(p => Names.Party(FindPartyById(p)))) : null;
            string text;
            switch (def.Name)
            {
                case "goto": text = lord + " goes to " + at; break;
                case "patrol": text = lord + " patrols around " + at; break;
                case "engage": text = lord + " attacks " + target; break;
                case "follow": text = lord + " follows " + target; break;
                case "leave": text = lord + " leaves the holding"; break;
                case "recruit": text = lord + " recruits from " + at + " onward, place by place, to 80% of the ranks and then the clan's own troop lines"; break;
                case "hold": text = lord + " holds position"; break;
                case "gather": text = lord + " gathers an army at " + at + " with " + with; break;
                case "join": text = lord + " marches to join " + target + "'s army"; break;
                case "watch": text = lord + " keeps watch " + (o.At != null && Spots.TryParse(o.At, out float wx, out float wy) ? SpotText(wx, wy) + " [" + o.At + "]" : "at " + at); break;
                case "shadow": text = lord + " shadows " + target; break;
                default:
                    text = lord + " " + def.Name + (at != null ? " " + at : "") + (target != null ? " " + target : "") + (with != null ? ", with " + with : ""); break;
            }
            List<string> set = o.Settings.Select(kv => kv.Key + " " + ToolDef.Num(kv.Value)).ToList();
            if (set.Count > 0) text += " (" + string.Join(", ", set) + ")";
            if (o.Then != null) text += ", then " + ((string)o.Then["do"] ?? "?") + (o.Then["at"] != null ? " " + (string)o.Then["at"] : o.Then["target"] != null ? " " + (string)o.Then["target"] : "");
            return text;
        }

        // ---------- armies ----------

        /// <summary>gather: forms an army under the lord at a meeting place, from named lords, at the game's influence price.</summary>
        private static string Gather(Hero lord, MobileParty lp, ToolDef def, ToolOrder o, AgentChannel.AgentOrder a, Kingdom realm, bool shadow, bool live,
                                     List<string> notes, ref string status)
        {
            string name = Names.Hero(lord);
            if (lp.Army != null) return name + " is already in an army" + (lp.Army.LeaderParty == lp ? " (and leads it: use add to call more lords)" : "") + ".";
            if (lord.Clan == null || lord.Clan.Kingdom != realm) return name + "'s clan is not a clan of " + Names.Faction(realm) + ".";
            if (lord.Clan.IsUnderMercenaryService) return name + "'s clan serves as mercenaries, and the game does not let mercenaries lead armies.";
            if (o.Lords.Count == 0) return "gather needs lords to call, for example \"lords\": [\"lord_X\", \"lord_Y\"].";
            List<MobileParty> members = Callable(lp, o.Lords, realm, out List<string> leftOut, out int cost, out List<MobileParty> far);
            if (members.Count == 0 && far.Count == 0) return "none of these lords can come: " + string.Join("; ", leftOut) + ".";
            if (leftOut.Count > 0) notes.Add("cannot come: " + string.Join("; ", leftOut));
            float influence = lord.Clan.Influence;
            if (cost > influence) return "calling " + members.Count + " lords costs " + cost + " influence, and " + name + "'s clan has " + (int)influence + ". Call fewer lords, or nearer ones.";
            int farCost = CostNow(lp, far);
            if (far.Count > 0)
                notes.Add("too far to be called yet, so they march to join and are called once close enough (about " + farCost + " influence more, less as they come nearer): "
                          + string.Join(", ", far.Select(m => Names.Hero(m.LeaderHero) + " (" + Text.N(m.Party.NumberOfHealthyMembers) + " men)")));
            if (cost + farCost > influence && far.Count > 0)
                notes.Add("the clan may not have the influence for all of them when they arrive (" + (int)influence + " now)");
            Settlement place = Settlement.Find(o.At);
            string who = members.Count > 0 ? string.Join(", ", members.Select(m => Names.Hero(m.LeaderHero))) : "no lord at once";
            int men = lp.Party.NumberOfHealthyMembers + members.Sum(m => m.Party.NumberOfHealthyMembers);
            int farMen = far.Sum(m => m.Party.NumberOfHealthyMembers);
            string text = name + " calls " + who + " to " + Names.Settlement(place) + " (" + (members.Count + 1) + " lords, " + Text.N(men) + " men now"
                          + (far.Count > 0 ? "; " + far.Count + " more lords, " + Text.N(farMen) + " men, march to join" : "") + ") for " + cost
                          + " influence; the clan has " + (int)influence;
            if (o.Then != null) text += "; then " + ((string)o.Then["do"] ?? "?") + (o.Then["at"] != null ? " " + (string)o.Then["at"] : o.Then["target"] != null ? " " + (string)o.Then["target"] : "");
            if (shadow)
            {
                status = "checked";
                return "valid: " + text + Notes(notes) + " (shadow mode: not carried out)";
            }
            if (!live) return NotYours(lord);
            foreach (MobileParty m in members) ReleaseHeld(m, "called to " + name + "'s army");
            Army.ArmyTypes type = ArmyTypeFor(o.Then);
            Witness.Ours = true;
            FormingAt = place;
            FormingBy = o.Source;
            try { realm.CreateArmy(lord, place, type, new MBList<MobileParty>(members)); }
            finally
            {
                Witness.Ours = false;
                FormingAt = null;
                FormingBy = null;
            }
            Army army = lp.Army;
            if (army == null) return "the game did not form the army (no reason given)";
            army.AiBehaviorObject = place;   // meet where the agent said, not where the game would choose
            Diag.Count("army:formed");
            Diag.Event("army", "formed", new { leader = lord.StringId, place = place.StringId, members = members.Select(m => m.LeaderHero.StringId).ToList(),
                                                far = far.Select(m => m.LeaderHero?.StringId).ToList(), cost, far_cost_now = farCost, influence_before = (int)influence, type = type.ToString() });
            string shown = StartTool(lord, lp, def, o, realm, o.Words);
            a.ToolRun = o.Id;
            SendToJoin(far, lp, o, realm);
            status = "applied";
            return text + Notes(notes) + shown;
        }

        /// <summary>add, release (with lords) and disband, for a lord who leads an army.</summary>
        /// <summary>
        /// The wages order (0.9.4: the caps should be the agent's call): the most a lord's party may pay in wages a day, which
        /// decides whether he may recruit: "cap" a number, or target "none" (no cap) or "game" (the game's own daily cap again).
        /// With lords, the same for those lords too. Kept until changed or the campaign is loaded again (Wages).
        /// </summary>
        private static string WagesOrder(Hero lord, MobileParty party, AgentChannel.AgentOrder a, Kingdom realm, bool shadow, bool live, ref string status)
        {
            string word = (a.Target ?? a.At ?? "").Trim().Trim('"').ToLowerInvariant();
            int? cap;
            if (a.Settings.TryGetValue("cap", out double c) && !double.IsNaN(c)) cap = (int)Math.Round(Math.Max(0, c));
            else if (word == "none" || word == "no cap" || word == "lift") cap = Wages.None;
            else if (word == "game" || word == "the game's") cap = null;
            else return "wages needs \"cap\" (the most his party may pay in wages a day), or \"target\": \"none\" (no cap) or \"game\" (the game's own cap again)";
            var parties = new List<MobileParty> { party };
            foreach (string w in a.Lords)
            {
                Hero h = FindLord(realm, w, out string error);
                if (h == null) return error;
                if (h == Hero.MainHero) return Names.Hero(h) + " takes no orders from an agent";
                MobileParty hp = h.PartyBelongedTo;
                if (hp == null || hp.LeaderHero != h || !hp.IsActive) return Names.Hero(h) + " is not leading a party right now.";
                if (!shadow && live && !ObeysAgent(h)) return NotYours(h);
                if (!parties.Contains(hp)) parties.Add(hp);
            }
            string what = cap == null ? "the game's own wage cap again" : cap.Value >= Wages.None ? "no wage cap" : "a wage cap of " + Text.N(cap.Value) + " a day";
            if (shadow) { status = "checked"; return "valid: " + string.Join(", ", parties.Select(p => Names.Hero(p.LeaderHero))) + " would get " + what + " (shadow mode: not carried out)"; }
            if (!live) return NotYours(lord);
            var said = parties.Select(p => Wages.Set(p, cap)).ToList();
            Diag.Event("order", "wages", new { lords = parties.Select(p => p.LeaderHero?.StringId).ToList(), cap, source = a.File, poke = a.Poke });
            status = "applied";
            return string.Join("; ", said);
        }

        /// <summary>
        /// The gift order (0.18.3; Nov 11: the player can move gold between the clans of his realm, so the agents may too): gold from one lord's own purse to another
        /// lord of the same realm. A clan's gold is its leader's purse, which tops the clan's lords up and sets their wage caps each day, so gold given to a poor clan's
        /// leader lets its lords recruit again; given to a lord himself, it pays his recruits now. The giver keeps at least "keep" (GiftKeep when none is given) and
        /// gives only what is above it. Never the player, on either side.
        /// </summary>
        private static string GiftOrder(AgentChannel.AgentOrder a, Kingdom realm, bool forceLive, ref string status)
        {
            Hero giver = FindHero(realm, a.Lord, out string error);
            if (giver == null) return error;
            string to = (a.Target ?? a.At ?? "").Trim();
            if (to.Length == 0) return "gift needs \"target\": the lord of your realm who gets the gold (a clan's leader, to fill his clan's purse), and \"amount\"";
            Hero getter = FindHero(realm, to, out error);
            if (getter == null) return error;
            if (giver == Hero.MainHero || getter == Hero.MainHero) return "the gift order never takes from or gives to " + Names.Hero(Hero.MainHero) + ", the human player";
            if (getter == giver) return Names.Hero(giver) + " cannot give gold to himself";
            if (!a.Settings.TryGetValue("amount", out double want) || double.IsNaN(want) || want < 1) return "gift needs \"amount\": how many denars to give";
            double keep = a.Settings.TryGetValue("keep", out double k) && !double.IsNaN(k) ? Math.Max(0, k) : ToolDef.GiftKeep;
            int has = giver.Gold, amount = (int)Math.Floor(Math.Min(Math.Round(want), has - keep));
            if (amount < 1) return Names.Hero(giver) + " has " + Text.N(has) + ": keeping " + Text.N((int)keep) + ", he has nothing to give";
            string mode = ModeOf(realm);
            bool live = forceLive || (mode == "live" && ObeysAgent(giver));
            bool shadow = !forceLive && mode == "shadow";
            string Who(Hero h) => Names.Hero(h) + " [" + h.StringId + "]" + (h.Clan != null ? (h.Clan.Leader == h ? ", leader of " : ", of ") + h.Clan.Name : "");
            string text = Who(giver) + " gives " + Text.N(amount) + " to " + Who(getter) + (amount < Math.Round(want) ? " (all he can spare, keeping " + Text.N((int)keep) + ")" : "");
            if (shadow) { status = "checked"; return "valid: " + text + " (shadow mode: not carried out)"; }
            if (!live) return NotYours(giver);
            GiveGoldAction.ApplyBetweenCharacters(giver, getter, amount, true);
            Diag.Event("order", "gift", new { giver = giver.StringId, getter = getter.StringId, amount, wanted = Math.Round(want), keep, giver_gold = giver.Gold, getter_gold = getter.Gold, source = a.File, poke = a.Poke });
            Diag.Count("order:gift");
            status = "applied";
            // the receipt adds its own full stop (Nov 11: "reckoning.." in the first gifts' receipts)
            return text + ": " + Names.Hero(getter) + " now has " + Text.N(getter.Gold) + ", " + Names.Hero(giver) + " " + Text.N(giver.Gold)
                   + (getter.Clan != null && getter.Clan.Leader == getter ? ". A clan's gold is its leader's purse: the game tops his clan's lords up from it and sets their wage caps from it at its next daily reckoning" : "");
        }

        /// <summary>A lord of the realm by name or id for the gift order: one leading a party, else any living lord of its clans (a clan's leader in a holding, a prisoner).</summary>
        private static Hero FindHero(Kingdom realm, string words, out string error)
        {
            Hero h = FindLord(realm, words, out error);
            if (h != null) return h;
            string w = (words ?? "").Trim().Trim('[', ']');
            Hero any = Hero.Find(w) ?? realm.Clans.SelectMany(c => c.Heroes).FirstOrDefault(x => x.IsAlive && string.Equals(Names.Hero(x), w, StringComparison.OrdinalIgnoreCase));
            if (any != null && any.IsAlive && any.MapFaction == realm) { error = null; return any; }
            return null;
        }

        private static string ArmyChange(string verb, Hero lord, MobileParty lp, AgentChannel.AgentOrder a, Kingdom realm, string source, bool shadow, bool live, ref string status)
        {
            string name = Names.Hero(lord);
            Army army = lp.Army;
            if (army == null || army.LeaderParty != lp) return name + " leads no army" + (verb == "add" ? "; use gather to form one" : "") + ".";
            if (verb == "disband")
            {
                if (shadow) { status = "checked"; return "valid: " + name + "'s army would break up (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                // who is in it, named in the receipt (0.13.8): each goes back to the game's own AI unless given an order of his own
                List<string> inIt = army.Parties.Where(p => p != null && p != lp && p.LeaderHero != null).Select(p => Names.Hero(p.LeaderHero) + " [" + p.LeaderHero.StringId + "]").ToList();
                Witness.Ours = true;
                try { DisbandArmyAction.ApplyByObjectiveFinished(army); }
                finally { Witness.Ours = false; }
                Diag.Count("army:disbanded");
                status = "applied";
                return name + "'s army breaks up. " + name + (inIt.Count > 0 ? " and its lords (" + string.Join(", ", inIt) + ")" : "")
                       + " go back to the game's own AI, unless each is given an order of his own";
            }
            if (a.Lords.Count == 0) return verb + " needs lords, for example \"lords\": [\"lord_X\"].";
            var ids = new List<string>();
            foreach (string w in a.Lords)
            {
                Hero h = FindLord(realm, w, out string error);
                if (h == null) return error;
                MobileParty p = h.PartyBelongedTo;
                if (p == null || p.LeaderHero != h) return Names.Hero(h) + " is not leading a party right now.";
                ids.Add(p.StringId);
            }
            if (verb == "add")
            {
                List<MobileParty> members = Callable(lp, ids, realm, out List<string> leftOut, out int cost, out List<MobileParty> far);
                if (members.Count == 0 && far.Count == 0) return "none of these lords can come: " + string.Join("; ", leftOut) + ".";
                var notes = new List<string>();
                if (leftOut.Count > 0) notes.Add("cannot come: " + string.Join("; ", leftOut));
                int farCost = CostNow(lp, far);
                if (far.Count > 0)
                    notes.Add("too far to be called yet, so they march to join and are called once close enough (about " + farCost + " influence more, less as they come nearer): "
                              + string.Join(", ", far.Select(m => Names.Hero(m.LeaderHero))));
                float influence = lord.Clan?.Influence ?? 0;
                if (cost > influence) return "calling them costs " + cost + " influence, and " + name + "'s clan has " + (int)influence + ".";
                string who = members.Count > 0 ? string.Join(", ", members.Select(m => Names.Hero(m.LeaderHero))) : "no lord at once";
                if (shadow) { status = "checked"; return "valid: " + name + " would call " + who + " into the army for " + cost + " influence" + Notes(notes) + " (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                foreach (MobileParty m in members)
                {
                    ReleaseHeld(m, "called to " + name + "'s army");
                    m.Army = army;   // the game charges the influence and marches them to the leader
                }
                Diag.Count("army:added", members.Count);
                if (far.Count > 0) SendToJoin(far, lp, new ToolOrder { Poke = a.Poke, Source = source }, realm);
                status = "applied";
                return name + " calls " + who + " into the army for " + cost + " influence" + Notes(notes);
            }
            // release: lords leave the army and decide for themselves again
            var leaving = new List<MobileParty>();
            foreach (string id in ids)
            {
                MobileParty p = FindPartyById(id);
                if (p == null || p.Army != army || p == lp) return Names.Party(p) + " is not in " + name + "'s army.";
                leaving.Add(p);
            }
            string gone = string.Join(", ", leaving.Select(m => Names.Hero(m.LeaderHero)));
            if (shadow) { status = "checked"; return "valid: " + gone + " would leave " + name + "'s army (shadow mode: not carried out)"; }
            if (!live) return NotYours(lord);
            foreach (MobileParty p in leaving) p.Army = null;
            Diag.Count("army:released", leaving.Count);
            status = "applied";
            return gone + " left " + name + "'s army";
        }

        /// <summary>
        /// The lords who can be called to the leader's army, by the game's own rules, and the influence they cost. Lords
        /// only too far away to be called yet come back in far (they can march to join); the others are left out, each
        /// with why: one lord in a battle no longer stops the whole army (first live test, Oct 9).
        /// </summary>
        private static List<MobileParty> Callable(MobileParty leader, List<string> partyIds, Kingdom realm, out List<string> leftOut, out int cost, out List<MobileParty> far, bool fromOtherArmies = false)
        {
            leftOut = new List<string>();
            far = new List<MobileParty>();
            cost = 0;
            var members = new List<MobileParty>();
            float range = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy;
            foreach (string id in partyIds)
            {
                MobileParty m = FindPartyById(id);
                string why = CannotCall(leader, m, realm, range, out bool tooFar, fromOtherArmies);
                if (why != null)
                {
                    if (!tooFar) leftOut.Add(why);
                    else if (!far.Contains(m)) far.Add(m);
                    continue;
                }
                if (members.Contains(m)) continue;
                members.Add(m);
                cost += Campaign.Current.Models.ArmyManagementCalculationModel.CalculatePartyInfluenceCost(leader, m);
            }
            return members;
        }

        /// <summary>Why a lord cannot be called into the leader's army now, or null. tooFar: the only reason is distance, so the lord can march to join.</summary>
        private static string CannotCall(MobileParty leader, MobileParty m, Kingdom realm, float range, out bool tooFar, bool fromOtherArmies = false)
        {
            tooFar = false;
            if (m == null || !m.IsActive || m.LeaderHero == null) return "one of them is not leading a party";
            string n = Names.Hero(m.LeaderHero);
            if (m == leader) return n + " leads the army";
            if (m.MapFaction != realm) return n + " is not of " + Names.Faction(realm);
            if (m.Army != null && m.Army == leader.Army) return n + " is already in this army";
            // the ruler's army (0.13.9) may take a lord out of another lord's army, never that army's own leader
            if (m.Army != null && (!fromOtherArmies || m.Army.LeaderParty == m)) return n + (m.Army.LeaderParty == m ? " leads an army of his own: break it up first (disband)" : " is already in an army");
            if (m.LeaderHero == realm.Leader) return n + " rules the realm and cannot be called into another lord's army";
            if (m.MapEvent != null) return n + " is in a battle";
            if (m.CurrentSettlement?.SiegeEvent != null) return n + " is inside a besieged holding";
            if (m.BesiegedSettlement != null) return n + " is besieging " + Names.Settlement(m.BesiegedSettlement);
            if (m.IsDisbanding) return n + "'s party is breaking up";
            float d = DistanceHelper.GetDistanceBetweenMobilePartyToMobileParty(m, leader, m.NavigationCapability, out _);
            if (d > range)
            {
                tooFar = true;
                return n + " is too far away to be called yet (" + (int)d + ", the game allows " + (int)range + ")";
            }
            return null;
        }

        /// <summary>
        /// Lords too far away to be called into the leader's army yet: each gets a join order (the library's join tool),
        /// marching toward the army until it can be called. Returns their names; the influence is paid when each joins.
        /// </summary>
        private static List<string> SendToJoin(List<MobileParty> far, MobileParty leader, ToolOrder from, Kingdom realm)
        {
            var sent = new List<string>();
            ToolDef def = Library.Get("join");
            if (def == null) { Diag.Alarm("tool_unknown", "The join order is missing from the library, so lords too far away were not sent to join the army."); return sent; }
            foreach (MobileParty m in far)
            {
                Hero h = m?.LeaderHero;
                if (h == null || !m.IsActive) continue;
                var o = new ToolOrder
                {
                    Tool = def.Name, Lord = h.StringId, LordName = Names.Hero(h), Party = m.StringId, Target = leader.StringId, Poke = from.Poke, Source = from.Source,
                    Why = "too far to be called into " + Names.Hero(leader.LeaderHero) + "'s army yet"
                };
                o.Words = h.StringId + " join " + (leader.LeaderHero?.StringId ?? leader.StringId);
                StartTool(h, m, def, o, realm, o.Words);
                sent.Add(Names.Hero(h));
            }
            if (sent.Count > 0) Diag.Count("army:sent to join", sent.Count);
            return sent;
        }

        /// <summary>The influence the far lords would cost if called now; less once they are closer (the game prices distance).</summary>
        private static int CostNow(MobileParty leader, List<MobileParty> far)
            => far.Sum(m => Campaign.Current.Models.ArmyManagementCalculationModel.CalculatePartyInfluenceCost(leader, m));

        /// <summary>
        /// A join order's lord, every hour and when its move is made: joins the target's army once the game would let it be
        /// called (paying the leader's clan's influence, like add), keeps marching while it is only too far or busy, and
        /// gives up for good when the army is gone or cannot pay. Returns true when the order ended (the caller drops it).
        /// </summary>
        private static bool TryJoin(HandOrder o)
        {
            MobileParty p = o.Party, target = o.Target;
            Kingdom realm = o.Realm;
            if (p == null || !p.IsActive || realm == null) return false;
            Army army = target?.Army;
            if (p.Army != null && p.Army == army) { End(o, "joined " + Names.Party(army.LeaderParty) + "'s army", "joined an army"); return true; }
            if (target == null || !target.IsActive) { End(o, "the army's leader is gone, so the lord could not join", "join failed"); return true; }
            if (army == null || army.LeaderParty == null)
            {
                // the army may be forming in the same answer: a few hours' grace before it counts as gone
                if (CampaignTime.Now.ToHours - o.Since < 3) return false;
                End(o, Names.Party(target) + " leads no army" + (o.JoinedSeen ? " any more (it broke up)" : "") + ", so the lord could not join", "join failed");
                return true;
            }
            o.JoinedSeen = true;
            MobileParty leader = army.LeaderParty;
            if (o.Target != leader) { o.Target = leader; Apply(o); }   // follow the army's leader, not a member of it
            float range = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy;
            string why = CannotCall(leader, p, realm, range, out bool tooFar);
            if (why != null)
            {
                if (tooFar || p.MapEvent != null || p.CurrentSettlement?.SiegeEvent != null) return false;   // still marching, or busy for now
                End(o, "it cannot join: " + why, "join failed");
                return true;
            }
            int cost = Campaign.Current.Models.ArmyManagementCalculationModel.CalculatePartyInfluenceCost(leader, p);
            float influence = leader.LeaderHero?.Clan?.Influence ?? 0;
            if (cost > influence)
            {
                End(o, "joining costs " + cost + " influence and " + Names.Hero(leader.LeaderHero) + "'s clan has " + (int)influence + ", so the lord could not join", "join failed");
                return true;
            }
            End(o, "joined " + Names.Party(leader) + "'s army for " + cost + " influence", "joined an army");
            Witness.Ours = true;
            try { p.Army = army; }   // the game charges the influence and marches the lord to the leader
            finally { Witness.Ours = false; }
            // the game charges nothing for a lord joining the player's army (Army.OnAddPartyInternal); its army screen charges the clan itself, and so does this (0.13.9)
            if (leader == MobileParty.MainParty && cost > 0) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -cost);
            Diag.Count("army:joined");
            Diag.Event("army", "joined", new { lord = o.Lord?.StringId, leader = leader.LeaderHero?.StringId, cost, influence_before = (int)influence });
            return true;
        }

        private static Army.ArmyTypes ArmyTypeFor(JObject then)
        {
            switch (((string)then?["do"] ?? "").ToLowerInvariant())
            {
                case "besiege": return Army.ArmyTypes.Besieger;
                case "raid": return Army.ArmyTypes.Raider;
                case "defend":
                case "relieve":
                case "garrison": return Army.ArmyTypes.Defender;
                default: return Army.ArmyTypes.Patrolling;
            }
        }

        /// <summary>A lord we hold who is called into an army: the hold ends, so the game can march him to it.</summary>
        private static void ReleaseHeld(MobileParty p, string why)
        {
            HandOrder held = Active.FirstOrDefault(x => x.Party == p);
            if (held == null) return;
            End(held, why, "called to an army");
            Active.Remove(held);
            Changed();
        }

        // ---------- plain orders: the one-step verbs (vassal orders) ----------

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
            if (targetWords.Length == 0) { error = "Which " + (verb == "engage" ? "lord" : "place") + "?\n" + VassalUsage; return null; }
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
            if (error == null && verb == "recruit") o.Verb = "tour";   // since 0.9.2 a recruit order tours from its place (RecruitTour)
            return error == null ? o : null;
        }

        private static void Start(HandOrder o, string words)
        {
            o.Id = ++_nextId;
            object before = State(o.Party, null);
            ReplaceHeld(o.Party, o.Id);
            LordTours.Drop(o.Party);
            Apply(o);
            bool took = OnCourse(o);
            Active.Add(o);
            AfterRelease.RemoveAll(r => r.Party == o.Party);
            Changed();
            try { PacLink.OnOrderStarted(o.Id, o.Lord, o.Party, o.Source, o.Realm, o.Verb, o.Place?.StringId, o.Target?.StringId); }   // not the lieutenant's: a mirror of its order left on him goes
            catch (Exception e) { Diag.Error("taking an order off Party AI Controls", e); }
            o.LastProgressAt = CampaignTime.Now.ToHours;
            o.BestDistance = Distance(o) ?? float.MaxValue;
            string text = Describe(o);
            Diag.Count("order:issued");
            Diag.Count("order source:" + o.Source);
            Note(o, "issued", text, new { words, before, after = State(o.Party, o), took });
            if (OnScreen(o)) Say("Order: " + text + ".", Colors.Cyan);
            if (!took)
            {
                Diag.Count("order:not taken");
                Diag.Alarm("order_not_taken", "The game did not take the order \"" + text + "\": right after it, " + Names.Hero(o.Lord) + " is " + Doing(o.Party) + ".",
                           false, new { id = o.Id });
            }
        }

        // ---------- every in-game hour ----------

        public static void Hourly()
        {
            double now = CampaignTime.Now.ToHours;
            bool changed = false;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                HandOrder o = Active[i];
                string end, key;
                try { end = o.ToolId > 0 ? LordGone(o, out key) : EndReason(o, now, out key); }
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
                Diag.Event("order", "hour", new { id = o.Id, lord = o.Lord?.StringId, tool = o.Tool, verb = o.Verb, arrived = o.Arrived, reissued = o.Reissued, state = State(p, o) });
                if (o.Verb == "join" && TryJoin(o))
                {
                    Active.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (p.MapEvent != null || (p.BesiegedSettlement != null && o.Verb != "besiege")) continue;   // busy fighting; look again next hour
                if (o.Verb == "supply")
                {
                    // the supply order (0.13.19): buys at a market on the way when he carries too little, then hands the food over
                    string supplyEnd = Supply.Step(o, now, out string supplyKey, out bool movedOn);
                    if (supplyEnd != null && o.Step && o.ToolId > 0)
                    {
                        // a step of a longer tool (0.17.4: defend's feed and brace): the food run is over, the tool goes on by its own rules
                        StepDone(o, supplyEnd, supplyKey, now);
                        changed = true;
                        continue;
                    }
                    if (supplyEnd != null)
                    {
                        EndMove(o, supplyEnd, supplyKey, Supply.Reached(supplyKey));
                        Active.RemoveAt(i);
                        changed = true;
                        continue;
                    }
                    if (movedOn)
                    {
                        o.Arrived = false;
                        o.ArrivedAt = -1;
                        o.ReissueTimes.Clear();
                        o.StuckAlarmed = o.FightAlarmed = false;
                        Apply(o);
                        o.BestDistance = Distance(o) ?? float.MaxValue;
                        o.LastProgressAt = now;
                        Note(o, "next stop", Describe(o), quiet: true);
                        changed = true;
                        continue;
                    }
                }
                if (o.Verb == "tour")
                {
                    // a recruit order (0.9.2): it takes what it may where it stands, ends when done, else rides on to the next place
                    string tourEnd = RecruitTour.Step(o, now, out string tourKey, out bool movedOn);
                    if (tourEnd != null)
                    {
                        // every end of a tour is a recruit order done as far as it can be: ranks full, nothing left, or no money for more
                        EndMove(o, tourEnd, tourKey, true);
                        Active.RemoveAt(i);
                        changed = true;
                        continue;
                    }
                    if (movedOn)
                    {
                        o.Arrived = false;
                        o.ArrivedAt = -1;
                        o.ReissueTimes.Clear();
                        o.StuckAlarmed = o.FightAlarmed = false;
                        Apply(o);
                        o.BestDistance = Distance(o) ?? float.MaxValue;
                        o.LastProgressAt = now;
                        Note(o, "next stop", Describe(o) + " (" + (100 * p.Party.NumberOfAllMembers / Math.Max(1, p.Party.PartySizeLimit)) + "% of its ranks)", quiet: true);
                        changed = true;
                        continue;
                    }
                }
                if (o.ToolId > 0 && !Possible(o))
                {
                    // the move cannot be made any more (its target is gone); the lord holds and the tool decides next hour
                    if (o.Verb != "hold") { o.Verb = "hold"; Apply(o); }
                    continue;
                }
                if ((o.Verb == "garrison" || o.Verb == "goto" || o.Verb == "recruit" || o.Verb == "enter" || o.Verb == "tour" || o.Verb == "sell" || o.Verb == "supply") && At(p, o.Place))
                {
                    if (o.Verb == "recruit") RecruitTour.SweepAt(o, now);   // a refill stop buys the same way as a tour (0.9.2)
                    if (!o.Arrived)
                    {
                        o.Arrived = true;
                        o.ArrivedAt = now;
                        Diag.Count("order:arrived");
                        if (o.ToolId > 0) Diag.Event("order", "arrived", new { id = o.Id, tool = o.Tool, lord = o.Lord?.StringId, place = o.Place?.StringId });
                        else
                        {
                            Note(o, "arrived", Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place), new { hours_taken = Math.Round(now - o.Since, 1) });
                            if (OnScreen(o)) Say(Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place) + ".", Colors.Cyan);
                        }
                    }
                    Hold(p);
                    continue;
                }
                if (o.Verb == "post" && p.Position.ToVec2().Distance(new Vec2(o.X, o.Y)) < 1.5f)
                {
                    // on its post: it stands there, watching
                    if (!o.Arrived)
                    {
                        o.Arrived = true;
                        o.ArrivedAt = now;
                        Diag.Count("order:arrived");
                        Diag.Event("order", "arrived", new { id = o.Id, tool = o.Tool, lord = o.Lord?.StringId, post = Spots.Id(o.X, o.Y) });
                    }
                    if (p.DefaultBehavior != AiBehavior.Hold) Hold(p);
                    continue;
                }
                if (o.Verb == "patrol" && o.Radius > 0f && o.HasPoint && p.Position.ToVec2().Distance(new Vec2(o.X, o.Y)) < 1.5f)
                {
                    // a patrol kept within its radius reached this point: on to the next
                    NextPatrolPoint(o, MobileParty.NavigationType.Default);
                    Apply(o);
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
                         new { was, who, took = OnCourse(o) }, quiet: o.ToolId > 0);
                    if (o.ReissueTimes.Count >= 3 && !o.FightAlarmed)
                    {
                        o.FightAlarmed = true;
                        Diag.Count("order:fought");
                        Diag.Alarm("order_fought", "Something keeps turning " + Names.Hero(o.Lord) + " from the order (\"" + Describe(o) + "\"): given again "
                                                   + o.ReissueTimes.Count + " times in " + StuckHours + " in-game hours" + (who != null ? "; last changed by " + who : "") + ".",
                                   false, new { id = o.Id });
                    }
                }
                else Hold(p);
            }
            CheckReleased(now);
            if (changed) Changed();
            try { PacLink.Hourly(); }
            catch (Exception e) { Diag.Error("the lieutenant's orders in Party AI Controls", e); }
        }

        private static void Hold(MobileParty p)
        {
            if (p.Ai.DoNotMakeNewDecisions) return;
            p.Ai.SetDoNotMakeNewDecisions(true);
            Toggled.Add(p);
        }

        /// <summary>
        /// Game's thread, right after Party AI Controls' hourly look at a party switched the game's AI back on for it (it does
        /// so for any lord it does not manage that stands still with the AI off): if one of our orders holds that lord,
        /// switch it off again before the game's own hourly decision can change the lord's plan. Not marked as our switch,
        /// since to the lord watch nothing changed.
        /// </summary>
        public static void KeepHeld(MobileParty p)
        {
            if (p == null || p.Ai.DoNotMakeNewDecisions || !Active.Any(o => o.Party == p)) return;
            p.Ai.SetDoNotMakeNewDecisions(true);
            Diag.Count("order:kept held against Party AI Controls");
        }

        /// <summary>
        /// Is he getting closer? No progress for 6 in-game hours, away from his target and out of battle, is an alarm. Since 0.11.3
        /// (run C1: 451 of 470 such alarms were lords on the move) "no progress" also needs him to have stood still: roads bend
        /// (a quarter of the recruit trips' legs ran 1.4 times the straight line), so a lord walking the road round a hill is not
        /// stuck; and a besieger at his siege camp, or a lord standing near his place, has arrived.
        /// </summary>
        private static void Progress(HandOrder o, float? d, double now)
        {
            if (!d.HasValue || o.Arrived || o.Verb == "hold" || o.Verb == "wait" || o.Verb == "patrol" || o.Verb == "follow" || o.Verb == "join" || o.Verb == "shadow") return;
            MobileParty p = o.Party;
            if (o.Verb == "besiege" && p.BesiegedSettlement == o.Place) return;
            if (o.Verb == "near" && d.Value < 12f) return;
            Vec2 at = p.Position.ToVec2();
            if (!o.HasLastSpot || at.Distance(new Vec2(o.LastSpotX, o.LastSpotY)) > 1f)
            {
                o.HasLastSpot = true;
                o.LastSpotX = at.x;
                o.LastSpotY = at.y;
                o.LastMovedAt = now;
            }
            if (d.Value < o.BestDistance - 0.5f)
            {
                o.BestDistance = d.Value;
                o.LastProgressAt = now;
                o.StuckAlarmed = false;
                return;
            }
            if (d.Value > 4f && now - o.LastProgressAt >= StuckHours && now - o.LastMovedAt >= StuckHours && !o.StuckAlarmed)
            {
                o.StuckAlarmed = true;
                Diag.Count("order:stuck");
                Diag.Alarm("order_stuck", Names.Hero(o.Lord) + " has not got any closer to " + TargetName(o) + " for " + StuckHours + " in-game hours (" + Doing(o.Party) + ", "
                                          + Text.D(d.Value) + " away). The lord may be stuck on the map.", false, new { id = o.Id, distance = Math.Round(d.Value, 1) });
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
                // TFAOMFixes' recruiting trip takes a lord below half his ranks the hour after our order ends (0.11.3, LordTours)
                Settlement tour = off && pac == null ? LordTours.TargetOf(p) : null;
                Diag.Lord(Text.When(now) + "  " + Names.Hero(r.Lord) + " [" + r.Lord.StringId + "], " + Math.Round(now - r.At) + " h after release: " + Doing(p)
                          + (off ? (pac != null ? "; held by Party AI Controls (" + pac + ")" : tour != null ? "; on TFAOMFixes' recruiting trip (to " + Names.Settlement(tour) + ")"
                                                : "; the game's AI is STILL switched off for this lord") : "; deciding for themselves") + ".");
                Diag.Event("order", "after release", new { lord = r.Lord.StringId, hours = Math.Round(now - r.At, 1), doing = Doing(p), ai_off = off, held_by_pac = pac, recruiting_trip = tour?.StringId });
                if (off && pac == null && tour == null && !Active.Any(x => x.Party == p))
                {
                    Diag.Count("order:held after release");
                    Diag.Alarm("held_after_release", Names.Hero(r.Lord) + " was released " + Math.Round(now - r.At) + " in-game hours ago, but the game's AI is still switched off, "
                                                     + "so this lord will never decide anything alone. agents.release " + FirstWord(Names.Hero(r.Lord)) + " frees them again.", true,
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
            // the lieutenant's orders shown in Party AI Controls never reach a save either (PacLink)
            PacLink.StripForSave();
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
                        Diag.Alarm("save_unclean", Names.Hero(o.Lord) + " could not be handed back to the game before the save; this save may hold the lord with the game's AI switched off.", true);
                    }
                }
                catch (Exception e)
                {
                    _handBackClean = false;
                    Diag.Error("handing a lord back before a save", e);
                    Diag.Alarm("save_unclean", "A lord could not be handed back to the game before the save (" + e.Message + "); this save may hold that lord frozen.", true);
                }
            }
        }

        public static void TakeBackAfterSave(bool saved, string name)
        {
            PacLink.RestoreAfterSave();
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
                   + (lords.Count > 0 ? "; lords among them: " + string.Join(", ", lords) : "") + ". No orders of ours are active yet (those written at the save are carried on next).";
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
                // a lord an order of ours holds now (carried on from the save, 0.13.5) is held on purpose, not caught by the save
                if (p.Ai.DoNotMakeNewDecisions && PacReader.OrderOf(h) == null && !Holds(p)) frozen.Add(Names.Hero(h));
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
            List<Kingdom> watched = Bridge.Seats.ToList();
            var other = new List<string>();
            var pac = new List<string>();
            var touring = new List<string>();
            foreach (MobileParty p in MobileParty.AllLordParties)
            {
                if (p == null || !p.IsActive || p.LeaderHero == null || ours.Contains(p) || !p.Ai.DoNotMakeNewDecisions) continue;
                if (PacReader.HoldsOrder(p.LeaderHero)) pac.Add(Names.Hero(p.LeaderHero));
                else if (OnRecruitingTour(p)) touring.Add(Names.Hero(p.LeaderHero) + " (" + Names.Faction(p.MapFaction) + ")");
                else other.Add(Names.Hero(p.LeaderHero) + " (" + Names.Faction(p.MapFaction) + ")");
            }
            Diag.Event("sweep", "daily", new { held_by_party_ai_controls = pac, on_tfaomfixes_recruiting_tour = touring, held_by_something_else = other });
            List<string> orcs = other.Where(x => watched.Any(k => x.EndsWith("(" + Names.Faction(k) + ")", StringComparison.Ordinal))).ToList();
            if (orcs.Count > 0)
                Diag.Alarm("held_by_other", "Something other than us has switched off the game's AI for " + orcs.Count + " lords of the watched realm: " + string.Join(", ", orcs) + ".");
        }

        // TFAOMFixes (my own mod) steers weak computer lords on recruiting tours with their AI off (Oct 22); asked by
        // name, as RecruitTour asks it for its offers, so those lords are not taken for something unknown holding them.
        private static System.Collections.IDictionary _tfaomTours;
        private static bool _toursLooked;

        private static bool OnRecruitingTour(MobileParty p)
        {
            if (!_toursLooked)
            {
                _toursLooked = true;
                Type t = HarmonyLib.AccessTools.TypeByName("TFAOMFixes.LordRecruiting");
                _tfaomTours = t != null ? HarmonyLib.AccessTools.Field(t, "_tours")?.GetValue(null) as System.Collections.IDictionary : null;
                Log.Line("daily sweep: TFAOMFixes' recruiting tours " + (_tfaomTours != null ? "found" : "not found (lords it steers count as held by something else)"));
            }
            return _tfaomTours != null && _tfaomTours.Contains(p);
        }

        // ---------- the move itself ----------

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
                    case "enter":
                    case "recruit":
                    case "sell":
                    case "supply":
                    case "tour": SetPartyAiAction.GetActionForVisitingSettlement(p, o.Place, nav, false, false); break;
                    // wait (0.13): he stands where he is, inside the holding when he is in one
                    case "wait": if (p.CurrentSettlement == null) p.SetMoveModeHold(); break;
                    case "near":
                        // 2 to 5 from the gate: inside the tools' "within 6" with room to spare (0.12.1; it was 3 to 6, measured against the centre)
                        p.SetMoveGoToPoint(NavigationHelper.FindReachablePointAroundPosition(o.Place.GatePosition, nav, 5f, 2f), p.NavigationCapability);
                        break;
                    case "toward":
                        p.SetMoveGoToPoint(NavigationHelper.FindReachablePointAroundPosition(new CampaignVec2(new Vec2(o.X, o.Y), true), nav, 2f), p.NavigationCapability);
                        break;
                    case "post":
                    case "shadow":
                        // to a point (a watch's post; the shadow's place this hour), and standing there once arrived
                        var pt = new CampaignVec2(new Vec2(o.X, o.Y), true);
                        p.SetMoveGoToPoint(NavigationHelper.IsPositionValidForNavigationType(pt, nav) ? pt : NavigationHelper.FindReachablePointAroundPosition(pt, nav, 2f), p.NavigationCapability);
                        break;
                    case "leave":
                        if (p.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(p);
                        p.SetMoveModeHold();
                        break;
                    case "patrol":
                        if (o.Radius > 0f)
                        {
                            // our own patrol: from point to point within the tool's radius of the holding (the game's would range half a day's march out)
                            if (!o.HasPoint) NextPatrolPoint(o, nav);
                            p.SetMoveGoToPoint(new CampaignVec2(new Vec2(o.X, o.Y), true), p.NavigationCapability);
                        }
                        else SetPartyAiAction.GetActionForPatrollingAroundSettlement(p, o.Place, nav, false, false);
                        break;
                    case "defend": SetPartyAiAction.GetActionForDefendingSettlement(p, o.Place, nav, false, false); break;
                    case "engage": SetPartyAiAction.GetActionForEngagingParty(p, o.Target, nav, false); break;
                    case "follow": SetPartyAiAction.GetActionForEscortingParty(p, o.Target, nav, false, false); break;
                    case "join": SetPartyAiAction.GetActionForEscortingParty(p, o.Target?.Army?.LeaderParty ?? o.Target, nav, false, false); break;
                    case "besiege": SetPartyAiAction.GetActionForBesiegingSettlement(p, o.Place, nav, false); break;
                    case "raid": SetPartyAiAction.GetActionForRaidingSettlement(p, o.Place, nav, false, false); break;
                    case "hold": p.SetMoveModeHold(); break;
                }
                p.Ai.SetDoNotMakeNewDecisions(true);
                Toggled.Add(p);
                // a sell order (or a tool's sell move, 0.13) for a lord already inside its place: the game sells only on entering, so the sale is made now (0.9.4)
                if (((o.Tool == "sell" && o.Verb == "enter") || o.Verb == "sell") && o.Place != null && At(p, o.Place)) Purse.CashIn(p, o.Place);
            }
            finally { Witness.Ours = false; }
        }

        /// <summary>
        /// The next point of a patrol kept within its radius: a reachable spot between a quarter and three quarters of the
        /// radius from the holding's gate, so the lord stays inside the area its tool watches.
        /// </summary>
        private static void NextPatrolPoint(HandOrder o, MobileParty.NavigationType nav)
        {
            // between a quarter and three quarters of its radius from the gate, or (0.19) between its standoff and its radius
            CampaignVec2 pt = o.Radius0 > 0f
                ? NavigationHelper.FindReachablePointAroundPosition(o.Place.GatePosition, nav, o.Radius, Math.Min(o.Radius0, o.Radius * 0.9f))
                : NavigationHelper.FindReachablePointAroundPosition(o.Place.GatePosition, nav, o.Radius * 0.75f, o.Radius * 0.25f);
            o.X = pt.X;
            o.Y = pt.Y;
            o.HasPoint = true;
        }

        private static bool OnCourse(HandOrder o)
        {
            MobileParty p = o.Party;
            AiBehavior b = p.DefaultBehavior;
            switch (o.Verb)
            {
                case "goto":
                case "garrison":
                case "enter":
                case "recruit":
                case "sell":
                case "supply":
                case "tour": return (b == AiBehavior.GoToSettlement && p.TargetSettlement == o.Place) || At(p, o.Place);
                case "wait": return p.CurrentSettlement != null || b == AiBehavior.Hold;
                // on the way to a spot close to the place (its target there), or standing close to it
                case "near": return o.Place != null && ((b == AiBehavior.GoToPoint && p.TargetPosition.Distance(o.Place.GatePosition) < 12f)
                                                        || ((b == AiBehavior.GoToPoint || b == AiBehavior.Hold) && p.Position.Distance(o.Place.GatePosition) < 12f));
                case "toward":
                case "post":
                case "shadow": return b == AiBehavior.GoToPoint || b == AiBehavior.Hold;
                case "leave": return p.CurrentSettlement == null;
                case "patrol": return o.Radius > 0f ? b == AiBehavior.GoToPoint || b == AiBehavior.Hold : b == AiBehavior.PatrolAroundPoint && p.TargetSettlement == o.Place;
                case "defend": return (b == AiBehavior.DefendSettlement && p.TargetSettlement == o.Place) || At(p, o.Place);
                case "engage": return b == AiBehavior.EngageParty && p.TargetParty == o.Target;
                case "follow": return b == AiBehavior.EscortParty && p.TargetParty == o.Target;
                case "join": return b == AiBehavior.EscortParty && (p.TargetParty == o.Target || p.TargetParty == o.Target?.Army?.LeaderParty);
                case "besiege": return p.BesiegedSettlement == o.Place || (b == AiBehavior.BesiegeSettlement && p.TargetSettlement == o.Place);
                case "raid": return (b == AiBehavior.RaidSettlement && p.TargetSettlement == o.Place) || (p.MapEvent != null && p.MapEvent.IsRaid);
                case "hold": return b == AiBehavior.Hold;
                default: return true;
            }
        }

        /// <summary>For a tool's lord: only the lord's own fate ends the order; the tool decides about its moves.</summary>
        private static string LordGone(HandOrder o, out string key)
        {
            key = null;
            Hero h = o.Lord;
            MobileParty p = o.Party;
            Kingdom realm = o.Realm;
            if (h == null || !h.IsAlive) { key = "died"; return "the lord died"; }
            if (h.IsPrisoner) { key = "captured"; return "the lord was taken prisoner"; }
            if (p == null || !p.IsActive || p.LeaderHero != h) { key = "party gone"; return "the lord's party is gone"; }
            if (realm == null || h.MapFaction != realm) { key = "left the realm"; return "the lord is no longer in " + Names.Faction(realm); }
            if (p.AttachedTo != null && p.Army?.LeaderParty != p) { key = "joined an army"; return "the lord joined another lord's army"; }
            return null;
        }

        private static string EndReason(HandOrder o, double now, out string key)
        {
            string gone = LordGone(o, out key);
            if (gone != null) return gone;
            Kingdom realm = o.Realm;
            MobileParty p = o.Party;
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
                        if (party.PartySizeLimit > 0 && party.NumberOfAllMembers >= party.PartySizeLimit * 0.98f) { key = "ranks full"; return "the ranks are full"; }
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
                    if (o.Target == null || !o.Target.IsActive) { key = "target gone"; return "the target is gone"; }
                    if (!FactionManager.IsAtWarAgainstFaction(o.Target.MapFaction, realm)) { key = "target no longer an enemy"; return "the target is no longer an enemy"; }
                    break;
            }
            return null;
        }

        /// <summary>A supply or recruit order ended here on the game's side: the news says what becomes of its next order, and the runner starts it when reached (0.14.2).</summary>
        /// <summary>
        /// A supply move that was one step of a longer tool is over (0.17.4). Test A1's second start, Nov 8: the standard defend's feed and brace
        /// ended the whole order when the food was handed over or the stores were full (5 of them in the first half day), since a supply move's end
        /// was taken for its tool's end, as it is for the supply order itself. Now the lord rides into the holding (or holds, where he no longer
        /// may), and the tool's rules read "arrived" and go on.
        /// </summary>
        private static void StepDone(HandOrder o, string why, string key, double now)
        {
            Supply.Forget(o.ToolId);
            Settlement h = o.SupplyFor;
            o.Verb = "enter";
            o.Place = h;
            o.Target = null;
            o.HasPoint = false;
            o.ReissueTimes.Clear();
            o.StuckAlarmed = o.FightAlarmed = false;
            if (h == null || !Possible(o)) { o.Verb = "hold"; o.Place = null; }
            o.Arrived = o.Verb == "hold" || (h != null && At(o.Party, h));
            o.ArrivedAt = o.Arrived ? now : -1;
            Apply(o);
            o.BestDistance = Distance(o) ?? float.MaxValue;
            o.LastProgressAt = now;
            Diag.Event("order", "step done", new { id = o.Id, tool = o.Tool, lord = o.Lord?.StringId, key, why, then = Describe(o) });
            Log.Line("order #" + o.Id + " [" + o.Source + "] " + o.Tool + ": food step over (" + why + "); " + Describe(o));
            int tid = o.ToolId;
            Background.PostFirst("tool step done", () => Runner.StepDone(tid, why));
        }

        private static void EndMove(HandOrder o, string why, string key, bool reached)
            => End(o, !o.HasNext ? why : why + (reached ? "; its next order starts" : ", so its next order does not start"), key, reached);

        private static void End(HandOrder o, string why, string key, bool? reached = null)
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
            RecruitTour.Forget(o.Id);
            Diag.Count("order:ended");
            Diag.Count("order end:" + (key ?? "other"));
            // a tool that finished has already called the agent back in its own words; a lord who joined an army as ordered,
            // or was called into one by the agent's own order, goes into the chronicle without waking the agent
            Note(o, "ended", why, new { key, hours_held = Math.Round(CampaignTime.Now.ToHours - o.Since, 1), state = State(o.Party, o) }, quiet: key == "tool done",
                 recordOnly: (key == "joined an army" && o.Verb == "join") || key == "called to an army");
            // the lieutenant's orders end in its own words, in the chat box (Lieutenant.Heard)
            if (!Lieutenant.IsRealm(o.Realm?.StringId) && OnScreen(o)) Say("Order for " + Names.Hero(o.Lord) + " ended: " + why + ".", Colors.Cyan);
            if (stillOff)
                Diag.Alarm("release_failed", Names.Hero(o.Lord) + "'s order ended, but the game's AI could not be switched back on for this lord.", true, new { id = o.Id });
            // a lord who left the realm is no longer ours to watch (0.11.3)
            if (o.Party != null && o.Party.IsActive && o.Lord != null && key != "left the realm")
                AfterRelease.Add(new Released { Party = o.Party, Lord = o.Lord, At = CampaignTime.Now.ToHours });
            if (o.ToolId > 0)
            {
                int tid = o.ToolId;
                string w = why;
                if (reached.HasValue)
                {
                    bool r = reached.Value;
                    Background.PostFirst("tool ended", () => Runner.MoveEnded(tid, w, r));
                }
                else Background.PostFirst("tool ended", () => Runner.Ended(tid, w));
            }
            // the lieutenant's order comes off Party AI Controls' panel, and his next order there starts (PacLink)
            try { PacLink.OnOrderEnded(o.Party, o.Id, key); }
            catch (Exception e) { Diag.Error("taking an order off Party AI Controls", e); }
        }

        /// <summary>Ends the order holding this party, if one does (PacLink: the ruler took the lord back in Party AI Controls; a party_ai order replaced it).</summary>
        public static bool EndFor(MobileParty p, string why, string key)
        {
            HandOrder o = Active.FirstOrDefault(x => x.Party == p);
            if (o == null) return false;
            End(o, why, key);
            Active.Remove(o);
            Changed();
            return true;
        }

        /// <summary>The tool an order holding this party runs, in words (the lieutenant's view of Party AI Controls), or null.</summary>
        internal static string ToolOf(MobileParty p) => Active.FirstOrDefault(x => x.Party == p) is HandOrder o ? (o.ToolText ?? Describe(o)) : null;

        /// <summary>
        /// Where a party is as the game's AI reckons it (0.11.3): the holding it is inside, or a village it stands at. A lord the
        /// game runs steps into a village and straight back out, left standing at it (EncounterManager.StartSettlementEncounter's
        /// last step), so "inside" alone never saw him arrive: in run C1, 1,606 of 1,771 orders given again were lords standing
        /// at their village, and recruit trips bought at 2 of 1,562 village stops. The game's own test is used.
        /// </summary>
        internal static Settlement Where(MobileParty p) => p == null ? null : Helpers.MobilePartyHelper.GetCurrentSettlementOfMobilePartyForAICalculation(p);

        internal static bool At(MobileParty p, Settlement s) => s != null && p != null && (p.CurrentSettlement == s || Where(p) == s);

        internal static Settlement SettlementFor(string words, out string error) => FindSettlement(words, out error);
        internal static MobileParty PartyFor(string words, out string error) => FindParty(words, out error);
        internal static MobileParty PartyById(string id) => FindPartyById(id);

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
                    if (!s.IsTown && !s.IsCastle && !s.IsVillage) return Names.Settlement(s) + " is not a town, castle or village.";
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
            if (found == null && pool.Count <= 1)
            {
                // a lord the realm has but who leads no party (a prisoner, or in a holding): say so plainly
                Hero h = Hero.Find((words ?? "").Trim().Trim('[', ']'));
                if (h != null && h.MapFaction == realm)
                {
                    error = Names.Hero(h) + (h.IsPrisoner ? " is a prisoner" : " is not leading a party right now") + ".";
                    Diag.Event("order", "lord lookup", new { words, found = (string)null, candidates = 0, why = error });
                    return null;
                }
            }
            error = found != null ? null
                : pool.Count > 1 ? "'" + words + "' matches several lords: " + string.Join(", ", pool.Take(12).Select(h => Names.Hero(h) + " (" + h.StringId + ")"))
                                   + ". Use more of the name, or the id in brackets."
                : "No lord of " + Names.Faction(realm) + " leading a party matches '" + words + "'.";
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
            string w = (words ?? "").Trim().Trim('[', ']');
            List<MobileParty> lords = MobileParty.AllLordParties.Where(p => p != null && p.IsActive && p.LeaderHero != null).ToList();
            MobileParty byHero = lords.FirstOrDefault(p => string.Equals(p.LeaderHero.StringId, w, StringComparison.OrdinalIgnoreCase));
            if (byHero != null) { error = null; return byHero; }
            MobileParty byId = FindPartyById(w);
            if (byId != null && byId.IsActive) { error = null; return byId; }
            MobileParty found = Pick(lords, w, p => Names.Party(p), p => p.StringId, out List<MobileParty> pool);
            if (found == null && pool.Count <= 1)
            {
                Hero h = Hero.Find(w);
                if (h != null && h.PartyBelongedTo?.LeaderHero != h)
                {
                    error = Names.Hero(h) + (h.IsPrisoner ? " is a prisoner" : h.IsDead ? " is dead" : " leads no party right now") + ", so there is no party to target.";
                    return null;
                }
            }
            error = found != null ? null
                : pool.Count > 1 ? "'" + words + "' matches several lords: " + string.Join(", ", pool.Take(12).Select(p => Names.Party(p) + " (" + p.LeaderHero.StringId + ")")) + "."
                : "No lord's party matches '" + words + "': the lord may have been beaten, captured, or left the map.";
            return found;
        }

        private static MobileParty FindPartyById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return Campaign.Current.CampaignObjectManager.Find<MobileParty>(id); }
            catch (Exception e)
            {
                Diag.Swallowed("finding a party by id", e);
                return MobileParty.All.FirstOrDefault(p => p.StringId == id);
            }
        }

        /// <summary>Exact name or id first, then names starting with the words, then names containing them.</summary>
        private static T Pick<T>(List<T> items, string words, Func<T, string> name, Func<T, string> id, out List<T> pool) where T : class
        {
            string w = (words ?? "").Trim().Trim('[', ']');
            string q = Text.Plain(w);
            pool = items.Where(x => Text.Plain(name(x)) == q || string.Equals(id(x), w, StringComparison.OrdinalIgnoreCase)).ToList();
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
                case "garrison":
                case "enter": return lord + " goes inside " + Names.Settlement(o.Place);
                case "near": return lord + " goes close to " + Names.Settlement(o.Place);
                // no party (0.13.12): a tool's lord falling back across open ground, to a point away from a danger
                case "toward":
                    if (o.Target != null) return lord + " marches to where " + Names.Party(o.Target) + " was last seen";
                    string spot = SpotText(o.X, o.Y);
                    return lord + " falls back across open ground, toward a point " + (spot.StartsWith("at ", StringComparison.Ordinal) ? "near " + spot.Substring(3) : spot);
                case "post": return lord + " keeps watch " + SpotText(o.X, o.Y);
                case "shadow": return lord + " shadows " + Names.Party(o.Target) + " from about " + Text.D(o.Radius);
                case "leave": return lord + " leaves the holding";
                case "patrol": return lord + " patrols around " + Names.Settlement(o.Place);
                case "defend": return lord + " defends " + Names.Settlement(o.Place);
                case "engage": return lord + " attacks " + Names.Party(o.Target);
                case "follow": return lord + " follows " + Names.Party(o.Target);
                case "besiege": return lord + " besieges " + Names.Settlement(o.Place);
                case "raid": return lord + " raids " + Names.Settlement(o.Place);
                case "recruit": return lord + " fills the ranks at " + Names.Settlement(o.Place);
                case "tour": return lord + " recruits place by place, now bound for " + Names.Settlement(o.Place);
                case "supply":
                    return o.SupplyFor != null && o.Place != o.SupplyFor
                        ? lord + " rides to " + Names.Settlement(o.Place) + " to buy food for " + Names.Settlement(o.SupplyFor)
                        : lord + " brings food to " + Names.Settlement(o.Place);
                case "sell": return lord + " goes to sell his loot at " + Names.Settlement(o.Place);
                case "wait": return lord + " waits " + ToolDef.Num(o.Radius) + " hours where he is";
                default: return lord + " holds position";
            }
        }

        private static string TargetName(HandOrder o) => o.Place != null ? Names.Settlement(o.Place) : o.Target != null ? Names.Party(o.Target) : "the target";

        private static float? Distance(HandOrder o)
        {
            MobileParty p = o.Party;
            if (p == null || !p.IsActive) return null;
            if (o.Verb == "toward" || o.Verb == "post") return p.Position.ToVec2().Distance(new Vec2(o.X, o.Y));
            if (o.Verb == "shadow") return null;   // it keeps its distance; it is not trying to arrive
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

        private static void Note(HandOrder o, string what, string text, object extra = null, bool quiet = false, bool recordOnly = false)
        {
            var rec = new
            {
                Type = "order", Event = what, Id = o.Id, o.Source, Poke = o.Poke >= 0 ? o.Poke : (int?)null, Hours = CampaignTime.Now.ToHours,
                Lord = o.Lord?.StringId, LordName = Names.Hero(o.Lord), Realm = o.Realm?.StringId, o.Verb, o.Tool, Place = o.Place?.StringId,
                Target = o.Target?.StringId, o.Reissued, Text = text, Run = RunTags.Id, ToolRun = o.ToolId > 0 ? o.ToolId : (int?)null
            };
            Background.Post("order record", () => Brain.WriteOrder(rec));
            Diag.Event("order", what, new { id = o.Id, source = o.Source, poke = o.Poke >= 0 ? o.Poke : (int?)null, lord = o.Lord?.StringId, verb = o.Verb, tool = o.Tool,
                                            place = o.Place?.StringId, target = o.Target?.StringId, text, extra });
            Log.Line("order #" + o.Id + " " + what + " [" + o.Source + "]: " + text);
            if (quiet) return;
            if (o.Source == "agent" && what != "issued")
            {
                string line = "order " + what + ": " + (what == "ended" ? Names.Hero(o.Lord) + "'s " + (o.Tool ?? "order") + " ended: " + text : text) + (what == "reissued" ? "" : ".");
                string realmId = o.Realm?.StringId;
                Background.Post("agent receipt", () => AgentChannel.Event(realmId, line));
            }
            // the watched realm hears when one of its orders finishes or fails, as a poke trigger
            if (o.Realm != null && Bridge.IsSeat(o.Realm) && (what == "arrived" || what == "ended"))
                Notices.Order(o.Realm, what == "arrived" ? "order_arrived" : "order_ended",
                              what == "arrived" ? text + " as ordered." : "The order \"" + (o.ToolText ?? Describe(o)) + "\" ended: " + text + ".", o.Party?.StringId, recordOnly);
        }

        /// <summary>Keeps the watchers in step with the orders held: the plain copy for the logs, the movement watcher and the lord watch.</summary>
        private static void Changed()
        {
            Live.OrdersHeld = Active.Count;
            Live.OrdersText = Active.Count == 0 ? "none" : string.Join("; ", Active.Select(o => "#" + o.Id + " [" + o.Source + "] " + (o.ToolText ?? Describe(o))));
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

        /// <summary>
        /// Is this order's line for the game's message log? (0.13.7, Oct 29: the other agents' orders are removed from the logs that show
        /// up in-game; I only want to see the stuff from my lieutenant and the normal game logs and updates in-game, and the dashboard should
        /// stay the same.) Only your lieutenant's orders and your own (the console's, your vassal orders); the agents' orders are in the
        /// logs and on the dashboard, not on screen.
        /// </summary>
        private static bool OnScreen(HandOrder o) => Shown(o.Source, Lieutenant.IsRealm(o.Realm?.StringId));

        internal static bool Shown(string source, bool lieutenantsRealm) => lieutenantsRealm || source == "hand" || source == "you";

        private static string FirstWord(string s) => string.IsNullOrEmpty(s) ? "" : s.Split(' ')[0];
    }
}
