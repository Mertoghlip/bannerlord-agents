using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents.Tools
{
    /// <summary>
    /// One move for the game's thread: a verb and what it acts on (a holding, a party, or a point for toward). R: for
    /// patrol, how far from the holding the lord may roam (the tool's own radius setting); 0 leaves it to the game.
    /// </summary>
    internal struct Move
    {
        public string Verb, Place, Party;
        public float X, Y, R;

        public bool Same(Move o) => Verb == o.Verb && Place == o.Place && Party == o.Party && Math.Abs(R - o.R) < 0.01f
                                    && ((Verb != "toward" && Verb != "post" && Verb != "shadow") || (Math.Abs(X - o.X) < 1f && Math.Abs(Y - o.Y) < 1f));

        public override string ToString() => Verb + (Place != null ? " " + Place : "") + (Party != null ? " " + Party : "") + (Verb == "toward" || Verb == "post" || Verb == "shadow" ? " (" + X + ", " + Y + ")" : "")
                                             + (R > 0 ? " within " + R : "");
    }

    /// <summary>A tool order as the game's thread checked it: ids only, so the worker can run it.</summary>
    internal sealed class ToolOrder
    {
        public string Tool, Lord, LordName, Party, At, Target, Why, Source = "agent", Words;
        /// <summary>The watched realm whose lord holds it (null: the first realm's; since 0.8).</summary>
        public string Realm;
        public readonly List<string> Lords = new List<string>();
        public readonly Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public JObject Then;
        public int Poke = -1;
    }

    internal sealed class ToolInstance
    {
        public int Id;
        /// <summary>The agent whose lord holds it: the tool lives in its orders in force (RealmAgent.Tools, since 0.9).</summary>
        public RealmAgent Owner;
        public ToolOrder Order;
        public ToolDef Def;
        public readonly Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, object> Memory = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public string State;
        public double StateSince, Started;
        public bool Entered, Finished;
        public Move Current, Pending;
        public bool HasCurrent, HasPending;
        /// <summary>The safety rule: where the lord shelters and from whom (Running: no refuge would hold, so it falls back outside); or where it buys food.</summary>
        public string Shelter, ShelterFrom, SupplyAt;
        public bool Running;
        public double AsideSince;
        public int Steps;
        public string LastStep;
    }

    /// <summary>
    /// Runs every tool and move the agent (or the console) has given the watched realm's lords: once an in-game hour,
    /// after the hourly copy, each lord's rules are checked against the realm's own view (World) and its move changed
    /// when a rule fires. The moves are made on the game's thread (Orders.ToolMove); the call-backs become notices,
    /// so they reach the agent with the next poke. Two rules hold for every order:
    /// - safety: a held lord does not flee by itself (the game switches its quick reactions off), so when an enemy
    ///   caution times stronger comes within danger_radius, it shelters in the nearest friendly castle or town and
    ///   resumes when the danger has passed;
    /// - food: with under two days of food it goes to the nearest friendly town or village to buy more, then resumes.
    /// Worker thread only (docs/TOOLS.md).
    /// </summary>
    internal static class Runner
    {
        // every tool by id, whoever holds it, for the game's messages about one (ended, adjusted); each agent keeps its own in RealmAgent.Tools
        private static readonly Dictionary<int, ToolInstance> ById = new Dictionary<int, ToolInstance>();
        private static int _lastId;

        // The game's thread, as the runner reaches it. The smoke test puts its own catchers here, so the runner can be
        // tried on a made-up world without the game.
        public static Action<int, Move> MoveSink = (id, m) => Background.ToMain.Enqueue(() => Orders.ToolMove(id, m));
        public static Action<int, string> ReleaseSink = (id, why) => Background.ToMain.Enqueue(() => Orders.ToolRelease(id, why));
        public static Action<int, JObject> ThenSink = (id, then) => Background.ToMain.Enqueue(() => Orders.ToolThen(id, then));
        /// <summary>The storm rules for the game's storm hook (StormGate is safe to write from the worker); a null party clears them all.</summary>
        public static Action<string, StormRule> StormSink = StormGate.Set;

        public static int Count => ById.Count;

        public static void Reset()
        {
            if (ById.Count > 0) Diag.Event("tool", "cleared", new { count = ById.Count });
            ById.Clear();
            NamesSeen.Clear();
            StormSink(null, null);
        }

        /// <summary>The names of the parties tools act on, kept for the call-back once a party is gone and the world no longer knows it.</summary>
        private static readonly Dictionary<string, string> NamesSeen = new Dictionary<string, string>();

        private static void RememberName(string id, World w)
        {
            if (id == null || w.Party(id) == null) return;
            string name = w.Name(id);
            if (name != id) NamesSeen[id] = name;
        }

        /// <summary>A fresh id for a tool instance (game's thread, when it takes an order).</summary>
        public static int NewId() => System.Threading.Interlocked.Increment(ref _lastId);

        public static ToolInstance ForParty(string partyId) => ById.Values.FirstOrDefault(t => t.Order.Party == partyId);

        public static List<ToolInstance> All() => ById.Values.ToList();

        // ---------- messages from the game's thread ----------

        /// <summary>The game's thread took a tool order (the lord is held): start it, with the latest view if there is one.</summary>
        public static void Start(int id, ToolOrder o)
        {
            ToolDef def = Library.Get(o.Tool);
            if (def == null) { Diag.Alarm("tool_unknown", "A tool order named a tool that does not exist: " + o.Tool + "."); return; }
            foreach (ToolInstance old in ById.Values.Where(t => t.Order.Party == o.Party && t.Id != id).ToList())
            {
                ById.Remove(old.Id);
                old.Owner?.Tools.Remove(old.Id);
                StormSink(old.Order.Party, null);
                Diag.Event("tool", "replaced", new { id = old.Id, by = id, lord = o.Lord, tool = old.Def.Name });
            }
            // the named realm's own agent (or the smoke test's stand-in), never the first one listed: a tool for a realm with no
            // agent would otherwise run on another realm's agent, its view and its call-backs (found Oct 22, planning the
            // lieutenant). An order naming no realm (a test's) keeps the first agent, as Agents.For says.
            RealmAgent owner = o.Realm == null ? Agents.For(null) : Agents.Serving(o.Realm);
            if (owner == null)
            {
                Diag.Alarm("tool_no_agent", "A " + def.Name + " order for " + o.Lord + " came for a realm no agent serves (" + o.Realm + "); the lord is let go.");
                ReleaseSink(id, "no agent serves that realm");
                return;
            }
            var t = new ToolInstance { Id = id, Order = o, Def = def, Started = Diag.Hours, Owner = owner };
            foreach (SettingDef s in def.Settings.Values) t.Settings[s.Name] = o.Settings.TryGetValue(s.Name, out double v) ? Math.Max(s.Min, Math.Min(s.Max, v)) : s.Default;
            ById[id] = t;
            owner.Tools[id] = t;
            Diag.Count("tool:started");
            Diag.Count("tool started:" + def.Name);
            Diag.Event("tool", "started", new { id, tool = def.Name, lord = o.Lord, party = o.Party, at = o.At, target = o.Target, lords = o.Lords.ToList(), settings = new Dictionary<string, double>(t.Settings), then = o.Then?.ToString(Newtonsoft.Json.Formatting.None), poke = o.Poke, source = o.Source });
            Snapshot s0 = owner.Latest ?? Brain.LatestFor(o.Realm);
            if (s0 != null && s0.Watched != null)
            {
                World w = WorldFor(s0);
                Step(t, w);
            }
        }

        /// <summary>The game's thread ended the hold (the lord was captured, released, replaced...): forget the tool.</summary>
        public static void Ended(int id, string why)
        {
            if (!ById.TryGetValue(id, out ToolInstance t)) return;
            ById.Remove(id);
            t.Owner?.Tools.Remove(id);
            StormSink(t.Order.Party, null);
            Diag.Count("tool:ended");
            Diag.Event("tool", "ended", new { id, tool = t.Def.Name, lord = t.Order.Lord, why, state = t.State, steps = t.Steps, hours = Math.Round(Diag.Hours - t.Started, 1) });
        }

        /// <summary>adjust: new numbers for a lord's current tool, checked against its ranges; the state is kept.</summary>
        public static void Adjust(int id, Dictionary<string, double> settings, AgentChannel.AgentOrder a)
        {
            double hours = Diag.Hours;
            if (!ById.TryGetValue(id, out ToolInstance t)) { AgentChannel.Receipt(a, "refused", "that lord holds no tool to adjust any more", hours); return; }
            if (settings.Count == 0) { AgentChannel.Receipt(a, "refused", "adjust needs settings, for example {\"radius\": 25}. " + t.Def.Name + " has: " + SettingNames(t.Def), hours); return; }
            var notes = new List<string>();
            foreach (KeyValuePair<string, double> kv in settings)
            {
                if (!t.Def.Settings.TryGetValue(kv.Key, out SettingDef sd)) { AgentChannel.Receipt(a, "refused", t.Def.Name + " has no setting '" + kv.Key + "'; it has " + SettingNames(t.Def), hours); return; }
                double v = Math.Max(sd.Min, Math.Min(sd.Max, kv.Value));
                if (Math.Abs(v - kv.Value) > 1e-9) notes.Add(kv.Key + " kept to " + ToolDef.Num(v) + " (its range is " + ToolDef.Num(sd.Min) + " to " + ToolDef.Num(sd.Max) + ")");
                t.Settings[sd.Name] = v;
            }
            Diag.Event("tool", "adjusted", new { id, tool = t.Def.Name, lord = t.Order.Lord, settings = new Dictionary<string, double>(t.Settings) });
            // a move that takes a number from the settings (a patrol's radius) is made again with the new one
            Snapshot s0 = t.Owner?.Latest ?? Brain.LatestFor(t.Order.Realm);
            if (s0 != null && s0.Watched != null && t.Entered && !t.Finished && t.Shelter == null && t.SupplyAt == null && t.State != null && t.Def.States.TryGetValue(t.State, out StateDef cur) && cur.Do != null)
            {
                World w0 = WorldFor(s0);
                t.Pending = Resolve(t, cur.Do, new Scope(t, w0), w0);
                t.HasPending = true;
                Issue(t, w0);
            }
            AgentChannel.Receipt(a, "applied", t.Order.LordName + "'s " + t.Def.Name + " now has " + string.Join(", ", settings.Keys.Select(k => k + " " + ToolDef.Num(t.Settings[k])))
                                               + (notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : ""), hours);
        }

        private static string SettingNames(ToolDef d) => string.Join(", ", d.Settings.Keys);

        // ---------- every in-game hour ----------

        /// <summary>An agent's hour (s is its realm's view): the tools its lords hold take their steps.</summary>
        public static void OnHour(Snapshot s, RealmAgent agent)
        {
            if (agent == null || agent.Tools.Count == 0 || s.Watched == null) return;
            World w = WorldFor(s);
            foreach (ToolInstance t in agent.Tools.Values.ToList())
            {
                try { Step(t, w); }
                catch (Exception e)
                {
                    Diag.Count("tool:errors");
                    Diag.Error("running " + t.Def.Name + " for " + t.Order.LordName, e);
                }
            }
        }

        /// <summary>The orders in force of the agent whose realm's view this world is (none for a realm without an agent).</summary>
        private static IEnumerable<ToolInstance> InForceOf(World w) => Agents.Serving(w.Realm)?.Tools.Values ?? Enumerable.Empty<ToolInstance>();

        public static World WorldFor(Snapshot s) => new World(s, s.Watched, s.WatchedSees, Brain.Get(s.Watched).LastSeen);

        private static void Step(ToolInstance t, World w)
        {
            PartyRow me = w.Party(t.Order.Party);
            if (me == null || me.Attached) return;   // gone (the game's thread ends the order and tells us), or marching in another's army
            var scope = new Scope(t, w);
            RememberName(t.Order.Target, w);
            if (t.Memory.TryGetValue("target", out object mt)) RememberName(mt as string, w);
            if (!t.Entered)
            {
                foreach (var kv in t.Def.Init) t.Memory[kv.Key] = kv.Value.Eval(scope);
                Enter(t, t.Def.Start, scope, w);
                Log(t, "start", t.Def.Start, null);
            }
            // the rules run in the hour a tool starts too, so a first step that decides at once (go in, or stop close) does
            if (!Safety(t, me, w, scope) && !Supply(t, me, w, scope) && !t.Finished)
            {
                Rules(t, scope, w);
                // a shadow follows its target: its point is worked out again every hour (sent only when it moved)
                if (!t.Finished && t.Shelter == null && t.SupplyAt == null && t.Def.States.TryGetValue(t.State, out StateDef live) && live.Do?.Verb == "shadow")
                {
                    t.Pending = Resolve(t, live.Do, scope, w);
                    t.HasPending = true;
                }
            }
            Issue(t, w);
            if (t.Settings.ContainsKey("storm")) StormSink(t.Order.Party, StormRuleOf(t));
        }

        /// <summary>
        /// A besieging tool's storm rule this hour (0.7.4): only while it besieges (its state's move is besiege), from its storm
        /// settings, and storm at once when its relief bar has been met (storm_now). Null otherwise, which clears it.
        /// </summary>
        /// <summary>The storm rule of the besiege order a party holds now (null: none, or the game decides).</summary>
        public static StormRule StormRuleFor(string party)
        {
            ToolInstance t = ForParty(party);
            return t != null && t.Settings.ContainsKey("storm") ? StormRuleOf(t) : null;
        }

        private static StormRule StormRuleOf(ToolInstance t)
        {
            if (t.Finished || t.Shelter != null || t.State == null || !t.Def.States.TryGetValue(t.State, out StateDef st) || st.Do?.Verb != "besiege") return null;
            double Num(string k) => t.Settings.TryGetValue(k, out double v) ? v : 0;
            bool now = t.Memory.TryGetValue("storm_now", out object sn) && Expr.Truthy(sn) && Expr.Number(sn) > 0;
            return new StormRule { Mode = (int)Math.Round(Num("storm")), Engines = (int)Math.Round(Num("storm_engines")), Ratio = (float)Num("storm_ratio"), Now = now };
        }

        private static void Rules(ToolInstance t, Scope scope, World w)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t.State };
            for (int hop = 0; hop < 3 && !t.Finished; hop++)
            {
                StateDef st = t.Def.States[t.State];
                TransitionDef fired = null;
                foreach (TransitionDef tr in st.When)
                    if (tr.If != null && Expr.Truthy(tr.If.Eval(scope))) { fired = tr; break; }
                if (fired == null) return;
                foreach (var kv in fired.Set) t.Memory[kv.Key] = kv.Value.Eval(scope);
                if (fired.Poke != null) Poke(t, Template.Fill(fired.Poke, scope, v => Show(v, w)), w);
                string from = t.State;
                if (string.Equals(fired.Go, from, StringComparison.OrdinalIgnoreCase))
                {
                    // a rule that stays in its state only remembers something (and maybe calls back): once an hour
                    Log(t, from, from, fired.IfText);
                    return;
                }
                Enter(t, fired.Go, scope, w);
                Log(t, from, fired.Go, fired.IfText);
                if (!visited.Add(fired.Go)) return;
            }
        }

        private static void Enter(ToolInstance t, string state, Scope scope, World w)
        {
            t.State = t.Def.States.ContainsKey(state) ? t.Def.States[state].Name : state;
            t.StateSince = w.Hours;
            t.Entered = true;
            StateDef st = t.Def.States[t.State];
            if (st.End != null) { Finish(t, st.End, w); return; }
            t.Pending = Resolve(t, st.Do, scope, w);
            t.HasPending = true;
        }

        /// <summary>The move a state makes, worked out now: a holding, a party, or the point a party was last seen at.</summary>
        private static Move Resolve(ToolInstance t, ActionDef a, Scope scope, World w)
        {
            if (a == null) return new Move { Verb = "hold" };
            ToolDef.Verbs.TryGetValue(a.Verb, out string kind);
            if (kind == "none") return new Move { Verb = a.Verb };
            object v = a.Arg?.Eval(scope);
            string id = v as string;
            if (kind == "holding")
            {
                if (w.Holding(id) != null)
                {
                    // a patrol keeps within the tool's own radius (defend's watch area, screen's), when it has one; else the game ranges it
                    float r = a.Verb == "patrol" && t.Settings.TryGetValue("radius", out double rr) ? (float)rr : 0f;
                    return new Move { Verb = a.Verb, Place = id, R = r };
                }
            }
            else if (kind == "spot")
            {
                // a post: a spot on the map, or a holding's gate (standing outside, not going in)
                SettlementRow h = w.Holding(id);
                if (h != null) return new Move { Verb = "post", X = h.Gx != 0 || h.Gy != 0 ? h.Gx : h.X, Y = h.Gx != 0 || h.Gy != 0 ? h.Gy : h.Y };
                if (Spots.TryParse(id, out float sx, out float sy)) return new Move { Verb = "post", X = sx, Y = sy };
            }
            else if (a.Verb == "toward")
            {
                if (id != null && w.Where(id, out float x, out float y)) return new Move { Verb = "toward", Party = id, X = x, Y = y };
            }
            else if (a.Verb == "shadow")
            {
                if (id != null && (w.Party(id) != null || w.Where(id, out _, out _))) return Shadow(t, w, id);
            }
            else if (id != null && w.Party(id) != null) return new Move { Verb = a.Verb, Party = w.Lead(w.Party(id)).Id };
            Diag.Count("tool:no target");
            Diag.Event("tool", "no target", new { id = t.Id, tool = t.Def.Name, state = t.State, @do = a.ToString(), value = Expr.Show(v) });
            return new Move { Verb = "hold" };
        }

        /// <summary>
        /// The shadow's move this hour (0.7.1): a point keep map units from its target, on its own side; nearer than that by two,
        /// it steps back to keep + 3; within two of keep, it stays where it is. The target out of sight: where it was last seen.
        /// Worked out again every hour, as the target moves.
        /// </summary>
        private static Move Shadow(ToolInstance t, World w, string id)
        {
            PartyRow me = w.Party(t.Order.Party);
            PartyRow target = w.Lead(w.Party(id));
            string tid = target?.Id ?? id;
            if (me == null || !w.Where(tid, out float tx, out float ty)) return new Move { Verb = "hold" };
            if (!w.InView(tid)) return new Move { Verb = "toward", Party = tid, X = tx, Y = ty };
            double keep = t.Settings.TryGetValue("keep", out double k) ? k : 8;
            double dx = me.X - tx, dy = me.Y - ty, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 0.01) { dx = 1; dy = 0; d = 1; }
            double at;
            if (d > keep + 2) at = keep;
            else if (d < keep - 2) at = keep + 3;
            else return new Move { Verb = "shadow", Party = tid, X = me.X, Y = me.Y, R = (float)keep };
            return new Move { Verb = "shadow", Party = tid, X = (float)(tx + dx / d * at), Y = (float)(ty + dy / d * at), R = (float)keep };
        }

        /// <summary>A state that ends the tool: the next order (then) starts, or the lord is freed, or holds where it is.</summary>
        private static void Finish(ToolInstance t, string end, World w)
        {
            t.Finished = true;
            if (t.Settings.ContainsKey("storm")) StormSink(t.Order.Party, null);
            if (end == "stop")
            {
                // stopped short of its aim (a siege lifted by its relief bar): the lord holds where it is, and its next order, which
                // assumed the aim was won, does not start
                if (t.Order.Then != null) Diag.Event("tool", "then skipped", new { id = t.Id, tool = t.Def.Name, lord = t.Order.Lord, next = t.Order.Then.ToString(Newtonsoft.Json.Formatting.None) });
                t.Pending = new Move { Verb = "hold" };
                t.HasPending = true;
                return;
            }
            if (end == "fail")
            {
                // a tool that failed (an army that broke up) does not lead to its next order: that order assumed it worked
                if (t.Order.Then != null) Diag.Event("tool", "then skipped", new { id = t.Id, tool = t.Def.Name, lord = t.Order.Lord, next = t.Order.Then.ToString(Newtonsoft.Json.Formatting.None) });
                ReleaseSink(t.Id, "the " + t.Def.Name + " could not be done" + (t.Order.Then != null ? ", so its next order did not start" : ""));
                return;
            }
            if (t.Order.Then != null)
            {
                Diag.Event("tool", "then", new { id = t.Id, tool = t.Def.Name, lord = t.Order.Lord, next = t.Order.Then.ToString(Newtonsoft.Json.Formatting.None) });
                ThenSink(t.Id, t.Order.Then);
                return;
            }
            if (end == "free")
            {
                ReleaseSink(t.Id, "the " + t.Def.Name + " is done");
                return;
            }
            t.Pending = new Move { Verb = "hold" };
            t.HasPending = true;
        }

        private static void Issue(ToolInstance t, World w)
        {
            if (!t.HasPending) return;
            t.HasPending = false;
            Move m = t.Pending;
            if (t.HasCurrent && t.Current.Same(m)) return;
            t.Current = m;
            t.HasCurrent = true;
            MoveSink(t.Id, m);
        }

        // ---------- the two rules every order carries ----------

        /// <summary>The shelter of a lord running straight away from a danger, with no place to make for.</summary>
        private const string Away = "(away)";

        private static bool Safety(ToolInstance t, PartyRow me, World w, Scope scope)
        {
            double caution = t.Def.Safety && t.Settings.TryGetValue("caution", out double c) ? c : 0;
            double radius = t.Settings.TryGetValue("danger_radius", out double r) ? r : Library.DangerRadius.Default;
            double mine = World.StrengthOf(me);
            var spare = new HashSet<string> { t.Order.Target, t.Current.Party, scope.Get("target") as string };
            // striking a siege camp: every lord camped in that siege is part of the fight the order chose, not a danger to flee
            string siege = t.Current.Verb == "engage" ? w.Party(t.Current.Party)?.Besieging : null;
            // a shadow keeps its own distance from its target, but a target that turns to chase it is a danger like any other
            bool shadowing = t.Current.Verb == "shadow";
            bool Spared(PartyRow e) => (spare.Contains(e.Id) && !(shadowing && e.Doing == "EngageParty" && e.TargetParty == me.Id)) || (siege != null && e.Besieging == siege);
            PartyRow Danger(double within) => w.ThreatsInView(me.Id, within).Where(e => !Spared(e) && World.StrengthOf(e) >= caution * Math.Max(1, mine))
                                                 .OrderByDescending(World.StrengthOf).FirstOrDefault();
            // the order's own holding: always the shelter with stay_inside 1 (garrison's default); tried first, and kept only
            // if it would hold, with stay_inside 0 (defend's default)
            SettlementRow own = w.Holding(t.Order.At);
            bool hasStay = t.Settings.TryGetValue("stay_inside", out double si);
            bool alwaysOwn = hasStay && si > 0 && World.IsFort(own) && !w.Besieged(own);
            string preferred = hasStay && World.IsFort(own) ? own.Id : null;
            if (t.Shelter != null)
            {
                PartyRow still = caution > 0 ? Danger(radius * 1.5) : null;
                if (still == null && w.Hours - t.AsideSince >= 2)
                {
                    string was = t.Shelter;
                    t.Shelter = t.ShelterFrom = null;
                    t.Running = false;
                    Log(t, "sheltering", t.State, "the danger passed");
                    Resume(t, scope, w);
                    Diag.Event("tool", "resumed", new { id = t.Id, lord = t.Order.Lord, from = was });
                    return false;
                }
                // running from a danger no refuge would hold against: keep running from where the lord is now, and take a
                // refuge that would hold as soon as one is in reach
                if (still != null && t.Running && me.InSettlement == null && !me.InBattle) Shelter(t, me, w, still, mine, alwaysOwn ? own : null, preferred, again: true);
                return true;
            }
            if (caution <= 0 || me.InBattle || me.Besieging != null) return false;
            SettlementRow inside = w.Holding(me.InSettlement);
            if (inside != null && World.IsFort(inside) && w.Besieged(inside)) return false;   // inside a besieged place: it cannot leave
            PartyRow danger = Danger(radius);
            if (danger == null) return false;
            if (inside != null && World.IsFort(inside))
            {
                // inside a castle or town: stay if it would hold (or the order is told to stay in its own holding); else run
                if ((alwaysOwn && inside.Id == own.Id) || w.Holds(inside, me.Id, World.StrengthOf(danger) + w.ThreatStrength(inside.Id, World.HoldRadius, danger.Id))) return false;
            }
            return Shelter(t, me, w, danger, mine, alwaysOwn ? own : null, preferred, again: false);
        }

        /// <summary>
        /// The lord takes shelter from a danger, or runs from it (World.ChooseRefuge): inside the first castle or town it can
        /// reach whose defenders, with it, would hold against the danger and every force near that place; if none would,
        /// toward the nearest one that takes it farther from the danger, stopping outside. The agent is told which, and why.
        /// </summary>
        private static bool Shelter(ToolInstance t, PartyRow me, World w, PartyRow danger, double mine, SettlementRow forced, string preferred, bool again)
        {
            bool holds = forced != null, running = false;
            SettlementRow fort = forced ?? w.ChooseRefuge(me.Id, preferred, World.HoldRadius, danger, out holds, out running);
            if (fort == null)
            {
                // nowhere it can reach before the danger: straight away from it, twice the danger radius, again each hour
                double dx = me.X - danger.X, dy = me.Y - danger.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 0.01) { dx = 1; dy = 0; len = 1; }
                double far = Math.Max(12, 2 * (t.Settings.TryGetValue("danger_radius", out double dr) ? dr : Library.DangerRadius.Default));
                bool first = t.Shelter == null;
                t.Shelter = Away;
                t.ShelterFrom = danger.Id;
                t.Running = true;
                if (!again) t.AsideSince = w.Hours;
                t.Pending = new Move { Verb = "toward", X = (float)(me.X + dx / len * far), Y = (float)(me.Y + dy / len * far) };
                t.HasPending = true;
                if (first)
                {
                    Diag.Count("tool:ran");
                    Poke(t, t.Order.LordName + " is falling back, away from " + w.Name(danger.Id) + " (strength " + Text.N((int)World.StrengthOf(danger)) + " against " + Text.N((int)mine)
                            + "): no castle or town it can reach before the enemy would hold against it, so it keeps out of reach in the open. The order resumes once the danger has passed.", w);
                    Log(t, t.State, "running", "no refuge it could reach before the danger");
                }
                return true;
            }
            if (again && fort.Id == t.Shelter && running == t.Running) return true;   // the same plan as last hour
            t.Shelter = fort.Id;
            t.ShelterFrom = danger.Id;
            t.Running = running;
            if (!again) t.AsideSince = w.Hours;
            t.Pending = new Move { Verb = running ? "near" : "enter", Place = fort.Id };
            t.HasPending = true;
            Diag.Count(running ? "tool:ran" : "tool:sheltered");
            double opposing = World.StrengthOf(danger) + w.ThreatStrength(fort.Id, World.HoldRadius, danger.Id);
            string why = running
                ? t.Order.LordName + " is falling back toward " + w.Name(fort.Id) + ", away from " + w.Name(danger.Id) + " (strength " + Text.N((int)World.StrengthOf(danger)) + " against "
                  + Text.N((int)mine) + "): no castle or town it could reach first would hold against it, so it stays out of reach rather than be trapped inside."
                : t.Order.LordName + " is sheltering in " + w.Name(fort.Id) + " from " + w.Name(danger.Id) + " (strength " + Text.N((int)World.StrengthOf(danger)) + " against "
                  + Text.N((int)mine) + "): " + (forced != null ? "its order keeps it inside." : "its defenders with " + t.Order.LordName + ", strength " + Text.N((int)w.DefenceStrength(fort, me.Id))
                  + " behind walls worth " + ToolDef.Num(Math.Round(World.WallFactor(fort), 1)) + " times that, would hold against " + Text.N((int)opposing) + ".");
            Poke(t, why + " The order resumes once the danger has passed.", w);
            Log(t, t.State, running ? "running" : "sheltering", "an enemy stronger than its caution allows, within its danger radius");
            return true;
        }

        private static bool Supply(ToolInstance t, PartyRow me, World w, Scope scope)
        {
            if (t.SupplyAt != null)
            {
                bool inside = me.InSettlement == t.SupplyAt;
                if (!(inside && (me.FoodDays >= 6 || w.Hours - t.AsideSince >= 8)) && w.Hours - t.AsideSince < 24) return true;
                Log(t, "buying food", t.State, inside ? "food bought (" + me.FoodDays + " days)" : "gave up after a day");
                t.SupplyAt = null;
                Resume(t, scope, w);
                return false;
            }
            if (me.FoodDays < 0 || me.FoodDays >= 2 || me.InSettlement != null || me.InBattle || me.Besieging != null) return false;
            SettlementRow at = w.NearestSupply(me.Id);
            if (at == null) return false;
            t.SupplyAt = at.Id;
            t.AsideSince = w.Hours;
            t.Pending = new Move { Verb = "enter", Place = at.Id };
            t.HasPending = true;
            Diag.Count("tool:food runs");
            Log(t, t.State, "buying food", me.FoodDays + " days of food left");
            return true;
        }

        /// <summary>Back to the order after a detour: the state's own move again (or holding, if the tool had finished).</summary>
        private static void Resume(ToolInstance t, Scope scope, World w)
        {
            StateDef st = t.Def.States[t.State];
            t.Pending = t.Finished || st.Do == null ? new Move { Verb = "hold" } : Resolve(t, st.Do, scope, w);
            t.HasPending = true;
            t.HasCurrent = false;
        }

        // ---------- what the agent hears ----------

        private static void Poke(ToolInstance t, string text, World w)
        {
            Diag.Count("tool:call-backs");
            Brain.ToolNotice(text, t.Order.Party, w.Hours, t.Order.At, t.Owner?.Realm ?? t.Order.Realm ?? w.Realm);
            (t.Owner?.Channel ?? AgentChannel.For(w.Realm)).Event("tool: " + text);
        }

        private static void Log(ToolInstance t, string from, string to, string why)
        {
            t.Steps++;
            t.LastStep = from + " -> " + to + (why != null ? " (" + why + ")" : "");
            Diag.Count("tool:steps");
            Diag.Event("tool", "step", new { id = t.Id, tool = t.Def.Name, lord = t.Order.Lord, from, to, why, move = t.HasPending ? t.Pending.ToString() : null });
        }

        /// <summary>A value as the agent reads it: names for ids, whole numbers with separators.</summary>
        public static string Show(object v, World w)
        {
            if (v is string id)
            {
                string name = w.Name(id);
                return name == id && NamesSeen.TryGetValue(id, out string known) ? known : name;
            }
            if (v is double d && !double.IsNaN(d)) return Math.Abs(d) >= 10 ? Text.N((int)Math.Round(d)) : Expr.Show(d);
            return Expr.Show(v);
        }

        /// <summary>The orders in force, for every poke: each lord's tool, where it stands, and its numbers if not the defaults.</summary>
        public static string InForce(World w)
        {
            List<ToolInstance> mine = InForceOf(w).ToList();
            if (mine.Count == 0) return "- None. Every lord is led by the game's own AI.";
            var sb = new StringBuilder();
            foreach (ToolInstance t in mine.OrderBy(x => x.Order.LordName)) sb.AppendLine("- " + Describe(t, w));
            return sb.ToString().TrimEnd();
        }

        /// <summary>Each ordered party's order in force, as the report words it (for the dashboard).</summary>
        public static Dictionary<string, string> ByParty(World w)
        {
            var d = new Dictionary<string, string>();
            foreach (ToolInstance t in InForceOf(w))
                if (t.Order.Party != null) d[t.Order.Party] = Describe(t, w);
            return d;
        }

        /// <summary>One order in force: who, what, what it is doing now and since when, its settings and what comes after.</summary>
        private static string Describe(ToolInstance t, World w)
        {
            var scope = new Scope(t, w);
            StateDef st = t.State != null && t.Def.States.TryGetValue(t.State, out StateDef s) ? s : null;
            string doing = t.Shelter != null ? (t.Shelter == Away ? "falling back, away from " + w.Name(t.ShelterFrom) : (t.Running ? "falling back toward " : "sheltering in ") + w.Name(t.Shelter) + " from " + w.Name(t.ShelterFrom)) + " (resumes once it has passed)"
                         : t.SupplyAt != null ? "buying food at " + w.Name(t.SupplyAt) + " (resumes after)"
                         : st?.About != null ? Template.Fill(st.About, scope, v => Show(v, w)) : (t.State ?? "starting");
            var what = new StringBuilder(t.Def.Name);
            if (t.Order.At != null) what.Append(" " + w.Name(t.Order.At) + " [" + t.Order.At + "]");
            if (t.Order.Target != null) what.Append(" " + w.Name(t.Order.Target) + " [" + w.Tag(t.Order.Target) + "]");
            if (t.Order.Lords.Count > 0) what.Append(" with " + string.Join(", ", t.Order.Lords.Select(p => w.Name(p) + " [" + w.Tag(p) + "]")));
            List<string> changed = t.Settings.Where(kv => t.Def.Settings.TryGetValue(kv.Key, out SettingDef sd) && Math.Abs(sd.Default - kv.Value) > 1e-9)
                                             .Select(kv => kv.Key + " " + ToolDef.Num(kv.Value)).ToList();
            PartyRow me = w.Party(t.Order.Party);
            string army = me != null && me.ArmyLeader == me.Id
                ? "; leads an army of " + Text.N(World.MenOf(me)) + " men" + (scope.Get("army_coming") is double c && c > 0 ? " (" + c + " lords still coming)" : "")
                : "";
            return t.Order.LordName + " [" + t.Order.Lord + "]: " + what + ". Now " + doing + "; since " + Text.When(t.StateSince) + army
                   + (changed.Count > 0 ? "; settings " + string.Join(", ", changed) : "")
                   + (t.Order.Then != null ? "; then " + ThenText(t.Order.Then, w) : "") + ".";
        }

        private static string ThenText(JObject then, World w)
        {
            string id = (string)then["at"] ?? (string)then["target"];
            string shown = id == null ? "" : w.Holding(id) != null ? " " + w.Name(id) + " [" + id + "]" : " " + id;
            return ((string)then["do"] ?? "?") + shown;
        }

        /// <summary>Every id the orders in force name, so the fog check knows the agent was shown them.</summary>
        public static IEnumerable<string> Ids(World w)
        {
            foreach (ToolInstance t in InForceOf(w))
            {
                yield return t.Order.Lord;
                if (t.Order.At != null) yield return t.Order.At;
                if (t.Order.Target != null) yield return w.Tag(t.Order.Target);
                foreach (string p in t.Order.Lords) yield return w.Tag(p);
            }
        }

        // ---------- what a tool's rules can read ----------

        private sealed class Scope : IScope
        {
            private readonly ToolInstance _t;
            private readonly World _w;

            public Scope(ToolInstance t, World w)
            {
                _t = t;
                _w = w;
            }

            private PartyRow Me => _w.Party(_t.Order.Party);

            private string Id(object v) => v as string;
            private SettlementRow H(object v) => _w.Holding(v as string);

            private double ArmyCount(bool comingOnly)
            {
                PartyRow me = Me;
                if (me == null || me.ArmyLeader != me.Id) return 0;
                int n = 0;
                foreach (PartyRow r in _w.S.Parties)
                    if (r.ArmyLeader == me.Id && (!comingOnly || (!r.Attached && r.Id != me.Id))) n++;
                return n;
            }

            public object Get(string name)
            {
                PartyRow me = Me;
                switch (name.ToLowerInvariant())
                {
                    case "me": return _t.Order.Party;
                    case "at": return _t.Order.At;
                    case "target": return _t.Memory.TryGetValue("target", out object m) ? m : _t.Order.Target;
                    case "strength": return World.StrengthOf(me);
                    case "men": return (double)World.MenOf(me);
                    case "ranks": return me != null && me.SizeLimit > 0 ? (double)me.Men / me.SizeLimit : 1.0;
                    case "wounded": return me != null && me.Men + me.Wounded > 0 ? (double)me.Wounded / (me.Men + me.Wounded) : 0.0;
                    case "food_days": return me != null && me.FoodDays >= 0 ? (double)me.FoodDays : (object)null;
                    case "hours_in_state": return _w.Hours - _t.StateSince;
                    case "hours_running": return _w.Hours - _t.Started;
                    case "in_army": return me?.ArmyLeader != null;
                    case "leads_army": return me != null && me.ArmyLeader == me.Id;
                    case "army_lords": return ArmyCount(false);
                    case "army_coming": return ArmyCount(true);
                    case "army_complete": return me != null && me.ArmyLeader == me.Id && ArmyCount(true) == 0 && ArmyCount(false) > 1;
                    case "army_joining": return (double)ById.Values.Count(x => x.Def.Name == "join" && x.Order.Target == _t.Order.Party);
                    case "in_settlement": return me?.InSettlement != null;
                    case "besieging": return me?.Besieging != null;
                    case "in_battle": return me != null && me.InBattle;
                    case "lords": return (double)_t.Order.Lords.Count;
                    case "now": return _w.Hours;
                }
                if (_t.Settings.TryGetValue(name, out double v)) return v;
                if (_t.Memory.TryGetValue(name, out object o)) return o;
                return null;
            }

            public object Call(string fn, object[] a)
            {
                PartyRow me = Me;
                switch (fn.ToLowerInvariant())
                {
                    case "distance": return Nan(_w.Distance(Id(a[0]), Id(a[1])));
                    case "hours_to": { double d = _w.Distance(_t.Order.Party, Id(a[0])); return double.IsNaN(d) ? null : (object)(d / _w.PerHour(me)); }
                    case "inside": return me != null && Id(a[0]) != null && me.InSettlement == Id(a[0]);
                    case "within": { double d = _w.Distance(_t.Order.Party, Id(a[0])); return !double.IsNaN(d) && d <= Expr.Number(a[1]); }
                    case "besieged": return _w.Besieged(H(a[0]));
                    case "siege_strength": return _w.SiegeStrength(H(a[0]));
                    case "inside_strength": return _w.InsideStrength(H(a[0]), me?.Id);
                    case "relief_strength": return _w.ReliefStrength(H(a[0]), World.StrengthOf(me), me?.Id);
                    case "besieger":
                        {
                            SettlementRow h = H(a[0]);
                            if (h?.Besieger == null) return null;
                            return h.Faction == _w.Realm || _w.InView(h.Besieger) ? h.Besieger : null;
                        }
                    case "raided": return H(a[0])?.VillageState == "BeingRaided";
                    case "looted": return H(a[0])?.VillageState == "Looted";
                    case "ours": return H(a[0])?.Faction == _w.Realm;
                    case "hostile": return _w.HostileFaction(H(a[0])?.Faction);
                    case "friendly": { SettlementRow h = H(a[0]); return h != null && (h.Faction == _w.Realm || _w.Ally(h.Faction)); }
                    case "fort": return World.IsFort(H(a[0]));
                    case "enemies_near": return (double)_w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).Count;
                    case "enemy_strength_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).Sum(World.StrengthOf);
                    case "threat_near": return _w.ThreatStrength(Id(a[0]), Expr.Number(a[1]));
                    case "enemy_threat_near": return _w.EnemyThreat(Id(a[0]), Expr.Number(a[1]));
                    case "other_fort": return _w.OtherFort(Id(a[0]), _t.Order.Party)?.Id;
                    case "refuge": return _w.ChooseRefuge(_t.Order.Party, Id(a[0]), Expr.Number(a[1]), null, out _, out _)?.Id;
                    case "would_hold": { SettlementRow h = H(a[0]); return h != null && World.IsFort(h) && _w.Holds(h, _t.Order.Party, _w.ThreatStrength(h.Id, Expr.Number(a[1]))); }
                    case "strongest_enemy_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).OrderByDescending(World.StrengthOf).FirstOrDefault()?.Id;
                    case "weakest_enemy_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).OrderBy(World.StrengthOf).FirstOrDefault()?.Id;
                    case "nearest_enemy": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).FirstOrDefault()?.Id;
                    case "army_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).Where(e => e.ArmyMen > 0).OrderByDescending(World.StrengthOf).FirstOrDefault()?.Id;
                    // enemy lords and armies near a place or heading for it, counted together (0.7.2): their strength, and who they are
                    case "relief_coming": return _w.ReliefComing(Id(a[0]), Expr.Number(a[1])).Sum(c => c.Strength);
                    case "relief_count": return (double)_w.ReliefComing(Id(a[0]), Expr.Number(a[1])).Count;
                    case "relief_of": return _w.ReliefText(Id(a[0]), Expr.Number(a[1]));
                    // the relief due within so many hours (by the game's marching rules), the hours until a camp may storm, and that in words (0.7.4)
                    case "relief_within": return _w.ReliefWithin(Id(a[0]), Expr.Number(a[1]), Expr.Number(a[2]));
                    case "prep_hours": return Nan(_w.PrepHours(Id(a[0])));
                    case "storm_text": return _w.StormText(Id(a[0]));
                    // what a lord's last sale brought, if he made it while this order ran (0.9.4, the sell order's call-back)
                    case "sale_text":
                        {
                            PartyRow r = _w.Party(Id(a[0]));
                            if (r == null) return "nothing known";
                            return !double.IsNaN(r.SaleHours) && r.SaleHours >= _t.Started - 0.01 && r.SaleText != null ? r.SaleText : "he had nothing to sell there";
                        }
                    case "strength_of": return _w.KnownStrength(Id(a[0]));
                    case "men_of": { int? n = _w.KnownMen(Id(a[0])); return n.HasValue ? (object)(double)n.Value : null; }
                    case "in_view": return _w.InView(Id(a[0]));
                    case "seen_ago": return _w.SeenAgo(Id(a[0]));
                    case "gone": return _w.Gone(Id(a[0]));
                    case "nearest_fort": return _w.NearestFort(_t.Order.Party)?.Id;
                    case "nearest_supply": return _w.NearestSupply(_t.Order.Party)?.Id;
                    case "next_village": return _w.NextVillage(Id(a[0]), Expr.Number(a[1]))?.Id;
                    case "lords_count": return (double)_t.Order.Lords.Count(p => _w.Party(p) != null);
                    case "lords_arrived":
                        {
                            int n = 0;
                            foreach (string p in _t.Order.Lords)
                            {
                                PartyRow r = _w.Party(p);
                                if (r == null) continue;
                                if ((me != null && r.ArmyLeader == me.Id) || _w.Distance(p, _t.Order.Party) <= 8) n++;
                            }
                            return (double)n;
                        }
                    // what a party is doing, where it is heading and where it is, as the realm knows it (in view now, else as last seen)
                    case "doing_of": return _w.DoingOf(Id(a[0]));
                    case "toward_of": return _w.TowardOf(Id(a[0]));
                    case "where_of": return _w.WhereOf(Id(a[0]));
                    case "min": return Nan(Math.Min(Expr.Number(a[0]), Expr.Number(a[1])));
                    case "max": return Nan(Math.Max(Expr.Number(a[0]), Expr.Number(a[1])));
                    case "pick": return Expr.Truthy(a[0]) ? a[1] : a[2];
                    case "round": return Nan(Math.Round(Expr.Number(a[0])));
                }
                throw new InvalidOperationException("no function " + fn);
            }

            private static object Nan(double d) => double.IsNaN(d) ? null : (object)d;
        }
    }
}
