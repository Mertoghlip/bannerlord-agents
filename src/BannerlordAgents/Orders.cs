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
            public bool HasPoint;
            /// <summary>A tool order: its runner instance and its name (0 and null for a plain vassal order).</summary>
            public int ToolId;
            public string Tool;
            public string ToolText;
            public double Since;
            public double ArrivedAt = -1;
            public int Reissued;
            public bool Arrived;
            public float BestDistance = float.MaxValue;
            public double LastProgressAt;
            public bool StuckAlarmed, FightAlarmed;
            /// <summary>A join order has seen its army (so an army missing later broke up, rather than not having formed yet).</summary>
            public bool JoinedSeen;
            public readonly List<double> ReissueTimes = new List<double>();
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
        private static readonly string[] Plain = { "free", "release", "adjust", "add", "disband" };

        public const string Usage =
            "Usage: agents.order <lord> <order> <place or lord> [setting=value ...] [lords=name,name]\n" +
            "  moves: goto, patrol, engage, follow, leave, recruit, hold, free\n" +
            "  tools: defend, garrison, besiege, raid, hunt, screen, rest; armies: gather, relieve, add, release, disband; adjust\n" +
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
        /// <summary>The meeting place of an army being formed by an order, for the army's notice (the game fires it before the place is set).</summary>
        internal static Settlement FormingAt;
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
            Mirror();
        }

        public static HashSet<MobileParty> HeldParties() => new HashSet<MobileParty>(Active.Where(o => o.Party != null).Select(o => o.Party));

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

        /// <summary>One order from the agent's answer file (AgentChannel). Posts the receipt's status and reason.</summary>
        public static void FromAgent(AgentChannel.AgentOrder a)
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
            if (string.IsNullOrWhiteSpace(a.Lord)) return "the order names no lord";
            Hero lord = FindLord(realm, a.Lord, out string error);
            if (lord == null) return error;
            // the two guards the vassal door always had (Oct 22): never the player, never a lord on a Party AI Controls order
            if (lord == Hero.MainHero) return Names.Hero(lord) + " takes no orders from an agent";
            string pacOrder = PacReader.OrderOf(lord);
            if (pacOrder != null)
                return Names.Hero(lord) + " already follows an order from his ruler's own dialogue (" + pacOrder + "); the two would fight each hour, so it stands until the ruler releases him there";
            string verb = (a.Do ?? "").Trim().ToLowerInvariant();
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
            if (party.AttachedTo != null && party.Army?.LeaderParty != party)
                return name + " marches in " + Names.Party(party.Army?.LeaderParty) + "'s army. Order the army's leader instead.";

            if (verb == "adjust")
            {
                HandOrder held = Active.FirstOrDefault(x => x.Lord == lord && x.ToolId > 0);
                if (held == null) return name + " holds no tool to adjust; give an order instead";
                if (shadow) { status = "checked"; return "valid: " + name + "'s " + held.Tool + " would get new settings (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                int id = held.ToolId;
                var settings = new Dictionary<string, double>(a.Settings, StringComparer.OrdinalIgnoreCase);
                Background.Post("tool adjust", () => Runner.Adjust(id, settings, a));
                status = "posted";
                return null;
            }
            if (verb == "add" || verb == "release" || verb == "disband") return ArmyChange(verb, lord, party, a, realm, source, shadow, live, ref status);

            ToolDef def = Library.Get(verb);
            if (def == null)
                return "'" + verb + "' is not an order. Orders: " + string.Join(", ", Library.All.Keys.OrderBy(k => k)) + ", " + string.Join(", ", Plain) + ".";
            ToolOrder o = PrepareTool(lord, party, def, a, realm, out error, out List<string> notes);
            if (o == null) return error;
            o.Source = source;
            string words = ((a.Lord ?? "") + " " + verb + " " + (a.At ?? a.Target ?? "")).Trim();
            o.Words = words;
            if (def.Name == "gather") return Gather(lord, party, def, o, a, realm, shadow, live, notes, ref status);
            string text = DescribeTool(def, o);
            if (shadow)
            {
                status = "checked";
                Diag.Event("order", "checked", new { source, poke = a.Poke, lord = lord.StringId, tool = def.Name, at = o.At, target = o.Target, lords = o.Lords, settings = o.Settings });
                return "valid: " + text + Notes(notes) + " (shadow mode: not carried out)";
            }
            if (!live) return NotYours(lord);
            StartTool(lord, party, def, o, realm, words);
            status = "applied";
            return text + Notes(notes);
        }

        private static string Notes(List<string> notes) => notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : "";

        /// <summary>The mode of the agent playing that realm (off, shadow or live).</summary>
        private static string ModeOf(IFaction realm) => Agents.Of(realm?.StringId)?.Config.Mode ?? Settings.Agent;
        /// <summary>Who obeys the agent playing that realm (all, or ids and names).</summary>
        private static string LordsOf(IFaction realm) => Agents.Of(realm?.StringId)?.Config.Lords ?? Settings.AgentLords;

        private static bool ObeysAgent(Hero lord)
        {
            string list = (LordsOf(lord.MapFaction) ?? "all").Trim();
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
            var o = new ToolOrder { Tool = def.Name, Lord = lord.StringId, LordName = Names.Hero(lord), Party = party.StringId, Why = a.Why, Then = a.Then, Poke = a.Poke };
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
                    if (p == party) { error = Names.Hero(h) + " is the lord being ordered."; return null; }
                    if (!o.Lords.Contains(p.StringId)) o.Lords.Add(p.StringId);
                }
            else if (a.Lords.Count > 0) notes.Add(def.Name + " takes no lords, so they were left out");
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
                if (Library.Get(next) == null && next != "free" && next != "disband") { error = "the next order (then) is not an order: '" + next + "'."; return null; }
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
                case "friendly_town_or_village": return (own || allied) && (s.IsTown || s.IsVillage) ? null : n + " is not a town or village of " + Names.Faction(realm) + " or an ally (only those give recruits).";
                case "friendly_fort": return (own || allied) && s.IsFortification ? null : n + " is not a castle or town of " + Names.Faction(realm) + " or an ally.";
                case "friendly_holding": return own || allied ? null : n + " is not " + Names.Faction(realm) + "'s or an ally's.";
                case "enemy_fort": return hostile && s.IsFortification ? null : n + " is not an enemy castle or town.";
                case "enemy_village": return hostile && s.IsVillage ? null : n + " is not an enemy village.";
                case "enemy_holding": return hostile ? null : n + " is not an enemy holding.";
                default: return null;
            }
        }

        /// <summary>Holds the lord for a tool and hands the tool to the runner, which makes the first move.</summary>
        private static void StartTool(Hero lord, MobileParty party, ToolDef def, ToolOrder o, Kingdom realm, string words)
        {
            if (realm != null) o.Realm = realm.StringId;
            int id = Runner.NewId();
            var h = new HandOrder
            {
                Id = ++_nextId, Party = party, Lord = lord, Realm = realm, Source = o.Source, Poke = o.Poke, Verb = "hold",
                ToolId = id, Tool = def.Name, ToolText = DescribeTool(def, o), Since = CampaignTime.Now.ToHours
            };
            object before = State(party, null);
            ReplaceHeld(party, h.Id);
            Apply(h);
            Active.Add(h);
            AfterRelease.RemoveAll(r => r.Party == party);
            Changed();
            h.LastProgressAt = CampaignTime.Now.ToHours;
            Diag.Count("order:issued");
            Diag.Count("order source:" + h.Source);
            Note(h, "issued", h.ToolText, new { words, before, tool = def.Name, at = o.At, target = o.Target, lords = o.Lords, settings = o.Settings, then = o.Then?.ToString(Newtonsoft.Json.Formatting.None) });
            Say("Order: " + h.ToolText + ".", Colors.Cyan);
            Background.Post("tool start", () => Runner.Start(id, o));
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
                    Background.Post("tool replaced", () => Runner.Ended(tid, "replaced by a new order"));
                }
            }
        }

        /// <summary>The runner's next move for a tool's lord (from the worker).</summary>
        public static void ToolMove(int toolId, Move m)
        {
            HandOrder o = Active.FirstOrDefault(x => x.ToolId == toolId);
            if (o == null || o.Party == null || !o.Party.IsActive) return;   // the order ended meanwhile
            o.Verb = m.Verb;
            o.Place = m.Place != null ? Settlement.Find(m.Place) : null;
            o.Target = m.Party != null ? FindPartyById(m.Party) : null;
            o.X = m.X;
            o.Y = m.Y;
            o.Radius = m.R;
            o.HasPoint = false;
            o.Arrived = false;
            o.ArrivedAt = -1;
            o.ReissueTimes.Clear();
            o.StuckAlarmed = o.FightAlarmed = false;
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
            Kingdom realm = o.Realm ?? Bridge.Watched;
            var a = AgentChannel.OrderFrom(then, o.Lord.StringId, o.Poke, "then");
            a.Realm = realm?.StringId;
            string status = "refused";
            string reason;
            try { reason = realm == null ? "no realm is under watch" : Issue(a, realm, o.Source, true, ref status); }
            catch (Exception e) { reason = "it could not be carried out (" + e.Message + ")"; Diag.Error("starting a next order", e); }
            if (status != "applied" && status != "posted")
            {
                // the next order no longer fits: the lord stops, and the agent hears why
                End(o, "its next order was refused: " + reason, "then refused");
                Active.Remove(o);
                Changed();
            }
            double hours = CampaignTime.Now.ToHours;
            if (o.Source == "agent" && status != "posted")
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
                    return o.Place != null && (realm == null || o.Place.MapFaction == null || !FactionManager.IsAtWarAgainstFaction(o.Place.MapFaction, realm));
                case "tour": return true;   // a stop that fell or emptied: the tour picks the next one itself
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
            try { realm.CreateArmy(lord, place, type, new MBList<MobileParty>(members)); }
            finally
            {
                Witness.Ours = false;
                FormingAt = null;
            }
            Army army = lp.Army;
            if (army == null) return "the game did not form the army (no reason given)";
            army.AiBehaviorObject = place;   // meet where the agent said, not where the game would choose
            Diag.Count("army:formed");
            Diag.Event("army", "formed", new { leader = lord.StringId, place = place.StringId, members = members.Select(m => m.LeaderHero.StringId).ToList(),
                                                far = far.Select(m => m.LeaderHero?.StringId).ToList(), cost, far_cost_now = farCost, influence_before = (int)influence, type = type.ToString() });
            StartTool(lord, lp, def, o, realm, o.Words);
            SendToJoin(far, lp, o, realm);
            status = "applied";
            return text + Notes(notes);
        }

        /// <summary>add, release (with lords) and disband, for a lord who leads an army.</summary>
        private static string ArmyChange(string verb, Hero lord, MobileParty lp, AgentChannel.AgentOrder a, Kingdom realm, string source, bool shadow, bool live, ref string status)
        {
            string name = Names.Hero(lord);
            Army army = lp.Army;
            if (army == null || army.LeaderParty != lp) return name + " leads no army" + (verb == "add" ? "; use gather to form one" : "") + ".";
            if (verb == "disband")
            {
                if (shadow) { status = "checked"; return "valid: " + name + "'s army would break up (shadow mode: not carried out)"; }
                if (!live) return NotYours(lord);
                Witness.Ours = true;
                try { DisbandArmyAction.ApplyByObjectiveFinished(army); }
                finally { Witness.Ours = false; }
                Diag.Count("army:disbanded");
                status = "applied";
                return name + "'s army breaks up; its lords go back to the game's own AI";
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
        private static List<MobileParty> Callable(MobileParty leader, List<string> partyIds, Kingdom realm, out List<string> leftOut, out int cost, out List<MobileParty> far)
        {
            leftOut = new List<string>();
            far = new List<MobileParty>();
            cost = 0;
            var members = new List<MobileParty>();
            float range = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy;
            foreach (string id in partyIds)
            {
                MobileParty m = FindPartyById(id);
                string why = CannotCall(leader, m, realm, range, out bool tooFar);
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
        private static string CannotCall(MobileParty leader, MobileParty m, Kingdom realm, float range, out bool tooFar)
        {
            tooFar = false;
            if (m == null || !m.IsActive || m.LeaderHero == null) return "one of them is not leading a party";
            string n = Names.Hero(m.LeaderHero);
            if (m == leader) return n + " leads the army";
            if (m.MapFaction != realm) return n + " is not of " + Names.Faction(realm);
            if (m.Army != null) return n + " is already in an army";
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
                if (o.Verb == "tour")
                {
                    // a recruit order (0.9.2): it takes what it may where it stands, ends when done, else rides on to the next place
                    string tourEnd = RecruitTour.Step(o, now, out string tourKey, out bool movedOn);
                    if (tourEnd != null)
                    {
                        End(o, tourEnd, tourKey);
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
                if ((o.Verb == "garrison" || o.Verb == "goto" || o.Verb == "recruit" || o.Verb == "enter" || o.Verb == "tour") && p.CurrentSettlement == o.Place)
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
                            Say(Names.Hero(o.Lord) + " reached " + Names.Settlement(o.Place) + ".", Colors.Cyan);
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

        /// <summary>Is he getting closer? No progress for 6 in-game hours, away from his target and out of battle, is an alarm.</summary>
        private static void Progress(HandOrder o, float? d, double now)
        {
            if (!d.HasValue || o.Arrived || o.Verb == "hold" || o.Verb == "patrol" || o.Verb == "follow" || o.Verb == "join" || o.Verb == "shadow") return;
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
                Diag.Lord(Text.When(now) + "  " + Names.Hero(r.Lord) + " [" + r.Lord.StringId + "], " + Math.Round(now - r.At) + " h after release: " + Doing(p)
                          + (off ? (pac != null ? "; held by Party AI Controls (" + pac + ")" : "; the game's AI is STILL switched off for this lord") : "; deciding for themselves") + ".");
                Diag.Event("order", "after release", new { lord = r.Lord.StringId, hours = Math.Round(now - r.At, 1), doing = Doing(p), ai_off = off, held_by_pac = pac });
                if (off && pac == null && !Active.Any(x => x.Party == p))
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

        // TFAOMFixes (Mert's own mod) steers weak computer lords on recruiting tours with their AI off (Oct 22); asked by
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
                    case "tour": SetPartyAiAction.GetActionForVisitingSettlement(p, o.Place, nav, false, false); break;
                    case "near":
                        p.SetMoveGoToPoint(NavigationHelper.FindReachablePointAroundPosition(o.Place.GatePosition, nav, 6f, 3f), p.NavigationCapability);
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
            }
            finally { Witness.Ours = false; }
        }

        /// <summary>
        /// The next point of a patrol kept within its radius: a reachable spot between a quarter and three quarters of the
        /// radius from the holding's gate, so the lord stays inside the area its tool watches.
        /// </summary>
        private static void NextPatrolPoint(HandOrder o, MobileParty.NavigationType nav)
        {
            CampaignVec2 pt = NavigationHelper.FindReachablePointAroundPosition(o.Place.GatePosition, nav, o.Radius * 0.75f, o.Radius * 0.25f);
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
                case "tour": return (b == AiBehavior.GoToSettlement && p.TargetSettlement == o.Place) || p.CurrentSettlement == o.Place;
                // on the way to a spot close to the place (its target there), or standing close to it
                case "near": return o.Place != null && ((b == AiBehavior.GoToPoint && p.TargetPosition.Distance(o.Place.GatePosition) < 12f)
                                                        || ((b == AiBehavior.GoToPoint || b == AiBehavior.Hold) && p.Position.Distance(o.Place.GatePosition) < 12f));
                case "toward":
                case "post":
                case "shadow": return b == AiBehavior.GoToPoint || b == AiBehavior.Hold;
                case "leave": return p.CurrentSettlement == null;
                case "patrol": return o.Radius > 0f ? b == AiBehavior.GoToPoint || b == AiBehavior.Hold : b == AiBehavior.PatrolAroundPoint && p.TargetSettlement == o.Place;
                case "defend": return (b == AiBehavior.DefendSettlement && p.TargetSettlement == o.Place) || p.CurrentSettlement == o.Place;
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
            RecruitTour.Forget(o.Id);
            Diag.Count("order:ended");
            Diag.Count("order end:" + (key ?? "other"));
            // a tool that finished has already called the agent back in its own words; a lord who joined an army as ordered,
            // or was called into one by the agent's own order, goes into the chronicle without waking the agent
            Note(o, "ended", why, new { key, hours_held = Math.Round(CampaignTime.Now.ToHours - o.Since, 1), state = State(o.Party, o) }, quiet: key == "tool done",
                 recordOnly: (key == "joined an army" && o.Verb == "join") || key == "called to an army");
            Say("Order for " + Names.Hero(o.Lord) + " ended: " + why + ".", Colors.Cyan);
            if (stillOff)
                Diag.Alarm("release_failed", Names.Hero(o.Lord) + "'s order ended, but the game's AI could not be switched back on for this lord.", true, new { id = o.Id });
            if (o.Party != null && o.Party.IsActive && o.Lord != null)
                AfterRelease.Add(new Released { Party = o.Party, Lord = o.Lord, At = CampaignTime.Now.ToHours });
            if (o.ToolId > 0)
            {
                int tid = o.ToolId;
                string w = why;
                Background.Post("tool ended", () => Runner.Ended(tid, w));
            }
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
                case "toward": return lord + " marches to where " + Names.Party(o.Target) + " was last seen";
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
                Target = o.Target?.StringId, o.Reissued, Text = text
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

        private static string FirstWord(string s) => string.IsNullOrEmpty(s) ? "" : s.Split(' ')[0];
    }
}
