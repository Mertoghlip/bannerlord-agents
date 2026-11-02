using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// <summary>The holdings a tool that takes "places" works through, in the order given (0.13, docs/PLAN-0.13.md A2).</summary>
        public readonly List<string> Places = new List<string>();
        public readonly Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public JObject Then;
        public int Poke = -1;
        /// <summary>
        /// The tool itself (0.13): ours from the library, or the agent's own (kept or one-off), the very version the order was given
        /// with, so an order keeps its version when the tool is changed later. Null: the library's tool by name.
        /// </summary>
        public ToolDef Def;
        /// <summary>The tool run's id, once the game's thread started it (the receipt names it).</summary>
        public int Id;

        /// <summary>The order's every part (its tool, place, target, lords, settings and next order), to tell the same order given again (0.12.0).</summary>
        public string Signature()
        {
            string lords = string.Join(",", Lords.OrderBy(x => x, StringComparer.Ordinal));
            string settings = string.Join(",", Settings.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                                       .Select(kv => kv.Key.ToLowerInvariant() + "=" + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            // the places in their order, and the tool's own version (0.13: an agent's tool changed and given again is a new order)
            return string.Join("|", Tool, Def != null ? Def.Author + "/" + Def.VersionText : "", At ?? "", Target ?? "", lords, string.Join(",", Places), settings, Then?.ToString(Newtonsoft.Json.Formatting.None) ?? "");
        }
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
        /// <summary>The relief rule (0.13.15): the fight the lord rides to relieve (its battle's key) and the enemy party he strikes there.</summary>
        public string ReliefOf, ReliefTarget;
        public bool Running;
        public double AsideSince;
        public int Steps;
        public string LastStep;
        /// <summary>Where and since when the lord has stood still on his way (NaN: moving, or not on his way), and the stand already told.</summary>
        public float StillX, StillY;
        public double StillSince = double.NaN, StallTold = double.NaN;
        /// <summary>When the move now in force was given (0.13: a wait's hours count from it, and so does "arrived").</summary>
        public double MoveSince = double.NaN;
        /// <summary>The order's places as the language reads them (a list, 0.13), made once.</summary>
        public List<object> PlacesList, LordsList;
        /// <summary>An agent's own tool (0.13, docs/PLAN-0.13.md A5): its call-backs this in-game day, those held past the day's limit, and its hours in a row over the time limit.</summary>
        public long CallBackDay = long.MinValue;
        public int DayCallBacks, SlowHours;
        public double LastMs;
        public readonly List<string> Held = new List<string>();
    }

    /// <summary>
    /// Runs every tool and move the agent (or the console) has given the watched realm's lords: once an in-game hour,
    /// after the hourly copy, each lord's rules are checked against the realm's own view (World) and its move changed
    /// when a rule fires. The moves are made on the game's thread (Orders.ToolMove); the call-backs become notices,
    /// so they reach the agent with the next poke. Two rules hold for every order:
    /// - safety: a held lord does not flee by itself (the game switches its quick reactions off), so when the enemies
    ///   within danger_radius, with the hosts they march with (counted together since 0.12.2), are caution times
    ///   stronger, it shelters in the nearest friendly castle or town and resumes when the danger has passed;
    /// - food: with under two days of food it goes to the nearest friendly town or village to buy more, then resumes.
    /// - relief (0.13.15, before both): it rides to relieve a lord of the realm's or an ally's fighting in the field, when it can get
    ///   there before the fight ends (the battle clock) and the side there, with every held lord who can get there in time, outweighs
    ///   the enemies in the fight and every enemy lord and army in view who could join it by then; it resumes once the fight is over.
    ///   Since 0.13.16 the clock runs with those enemies coming in (and bands of the enemy's own kind when it is bandits fighting), and a
    ///   rider must get there with time to spare (Library.ReliefSpare).
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

        /// <summary>
        /// The guards on an agent's own tool (0.13, docs/PLAN-0.13.md A5; to be confirmed in testing): at most CallBacksPerDay call-backs an
        /// in-game day (the rest are held and summed in one when the day is out); stopped when it takes over SlowMs of the helper's time
        /// an hour for SlowHoursToStop hours running. A tool changes state at most 3 times an hour (Rules), as ours do.
        /// </summary>
        public const int CallBacksPerDay = 6, SlowHoursToStop = 3;
        public static double SlowMs = 5;

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
            // the very tool the order was given with (0.13: an agent's own, kept or one-off, at its version), else ours by name
            ToolDef def = o.Def ?? Library.Get(o.Tool);
            if (def == null) { Diag.Alarm("tool_unknown", "A tool order named a tool that does not exist: " + o.Tool + "."); return; }
            foreach (ToolInstance old in ById.Values.Where(t => t.Order.Party == o.Party && t.Id != id).ToList())
            {
                ById.Remove(old.Id);
                old.Owner?.Tools.Remove(old.Id);
                StormSink(old.Order.Party, null);
                Diag.Event("tool", "replaced", new { id = old.Id, by = id, lord = o.Lord, tool = old.Def.Name });
                Outcomes.End(old.Id, "replaced", "replaced by a new order", RowOf(old), Diag.Hours);
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
            Diag.Event("tool", "started", new { id, tool = def.Name, version = def.VersionText, author = def.Author, lord = o.Lord, party = o.Party, at = o.At, target = o.Target, lords = o.Lords.ToList(), places = o.Places.ToList(), settings = new Dictionary<string, double>(t.Settings), then = o.Then?.ToString(Newtonsoft.Json.Formatting.None), poke = o.Poke, source = o.Source });
            Outcomes.Started(t, RowOf(t), Diag.Hours);
            Snapshot s0 = owner.Latest ?? Brain.LatestFor(o.Realm);
            if (s0 != null && s0.Watched != null)
            {
                World w = WorldFor(s0);
                Step(t, w);
            }
        }

        // ---------- carried across a reload of the same save (0.13.5, CarryOn) ----------

        /// <summary>A tool run's id from an earlier session carried on now: new runs are numbered past it (any thread).</summary>
        public static void NoteUsedId(int id)
        {
            int seen;
            while ((seen = _lastId) < id && System.Threading.Interlocked.CompareExchange(ref _lastId, id, seen) != seen) { }
        }

        /// <summary>
        /// A tool run as it stands, for the save's carry-on file (CarryOn): its order, the very definition it runs, its settings, its state
        /// and remembered values, the move in force and the safety and food rules' places. Null when there is no such run.
        /// </summary>
        public static JObject Export(int id)
        {
            if (!ById.TryGetValue(id, out ToolInstance t) || t.Finished || t.Def?.Json == null || t.State == null) return null;
            ToolOrder o = t.Order;
            var settings = new JObject();
            foreach (var kv in o.Settings) settings[kv.Key] = kv.Value;
            var live = new JObject();
            foreach (var kv in t.Settings) live[kv.Key] = kv.Value;
            return new JObject
            {
                ["id"] = t.Id, ["owner"] = t.Owner?.Config?.ChannelDir, ["realm"] = t.Owner?.Realm ?? o.Realm,
                ["def"] = new JObject
                {
                    ["json"] = t.Def.Json, ["name"] = t.Def.Name, ["author"] = t.Def.Author, ["version"] = t.Def.Version, ["written_at"] = t.Def.WrittenAt, ["one_off"] = t.Def.OneOff,
                    ["hash"] = t.Def.Hash, ["given_with"] = o.Def != null
                },
                ["order"] = new JObject
                {
                    ["tool"] = o.Tool, ["lord"] = o.Lord, ["lord_name"] = o.LordName, ["party"] = o.Party, ["at"] = o.At, ["target"] = o.Target, ["why"] = o.Why, ["source"] = o.Source,
                    ["words"] = o.Words, ["realm"] = o.Realm, ["lords"] = new JArray(o.Lords), ["places"] = new JArray(o.Places), ["settings"] = settings, ["then"] = o.Then, ["poke"] = o.Poke
                },
                ["settings"] = live, ["memory"] = ToJson(new Dictionary<string, object>(t.Memory, StringComparer.OrdinalIgnoreCase)),
                ["state"] = t.State, ["state_since"] = Num(t.StateSince), ["started"] = Num(t.Started), ["move_since"] = Num(t.MoveSince),
                ["current"] = t.HasCurrent ? MoveJson(t.Current) : null, ["pending"] = t.HasPending ? MoveJson(t.Pending) : null,
                ["shelter"] = t.Shelter, ["shelter_from"] = t.ShelterFrom, ["supply_at"] = t.SupplyAt, ["running"] = t.Running, ["aside_since"] = Num(t.AsideSince),
                ["relief_of"] = t.ReliefOf, ["relief_target"] = t.ReliefTarget,
                ["steps"] = t.Steps, ["last_step"] = t.LastStep, ["still_x"] = t.StillX, ["still_y"] = t.StillY, ["still_since"] = Num(t.StillSince), ["stall_told"] = Num(t.StallTold),
                ["call_back_day"] = t.CallBackDay, ["day_call_backs"] = t.DayCallBacks, ["slow_hours"] = t.SlowHours, ["held"] = new JArray(t.Held)
            };
        }

        /// <summary>
        /// A tool run carried on from the save just loaded (CarryOn): the game's thread already holds its lord again under the same id;
        /// here the tool takes up where it was, its move in force given again. Null when carried on, else why not (the caller lets
        /// the lord go).
        /// </summary>
        public static string Resume(JObject j, JObject record)
        {
            int id = (int?)j?["id"] ?? 0;
            if (id <= 0 || !(j["def"] is JObject dj) || !(j["order"] is JObject oj)) return "its record is incomplete";
            string state = (string)j["state"];
            ToolDef def = ToolDef.FromJson((string)dj["json"] ?? "", (string)dj["name"], (string)dj["author"] ?? "ours");
            if (!def.Valid) return "its tool no longer reads (" + def.Problems[0] + ")";
            def.Version = (int?)dj["version"] ?? 1;
            def.WrittenAt = (int?)dj["written_at"] ?? -1;
            def.OneOff = (bool?)dj["one_off"] ?? false;
            def.Hash = (string)dj["hash"];
            if (state == null || !def.States.ContainsKey(state)) return "its state (" + (state ?? "none") + ") is not one of its tool's";
            var o = new ToolOrder
            {
                Tool = (string)oj["tool"], Lord = (string)oj["lord"], LordName = (string)oj["lord_name"], Party = (string)oj["party"], At = (string)oj["at"], Target = (string)oj["target"],
                Why = (string)oj["why"], Source = (string)oj["source"] ?? "agent", Words = (string)oj["words"], Realm = (string)oj["realm"], Then = oj["then"] as JObject,
                Poke = (int?)oj["poke"] ?? -1, Id = id,
                // ours by name stay so (the order's fingerprint is as it was given); an agent's own keeps the version it was given with
                Def = (bool?)dj["given_with"] == true ? def : null
            };
            foreach (JToken x in oj["lords"] as JArray ?? new JArray()) o.Lords.Add((string)x);
            foreach (JToken x in oj["places"] as JArray ?? new JArray()) o.Places.Add((string)x);
            if (oj["settings"] is JObject os) foreach (JProperty p in os.Properties()) o.Settings[p.Name] = (double)p.Value;
            RealmAgent owner = o.Realm == null ? Agents.For(null) : Agents.Serving(o.Realm);
            if (owner == null) return "no agent serves the realm";
            var t = new ToolInstance
            {
                Id = id, Order = o, Def = def, Owner = owner, State = def.States[state].Name, Entered = true,
                StateSince = Dbl(j["state_since"]), Started = Dbl(j["started"]), MoveSince = Dbl(j["move_since"]),
                Shelter = (string)j["shelter"], ShelterFrom = (string)j["shelter_from"], SupplyAt = (string)j["supply_at"], Running = (bool?)j["running"] ?? false,
                ReliefOf = (string)j["relief_of"], ReliefTarget = (string)j["relief_target"],
                AsideSince = Dbl(j["aside_since"]), Steps = (int?)j["steps"] ?? 0, LastStep = (string)j["last_step"],
                StillX = (float?)j["still_x"] ?? 0, StillY = (float?)j["still_y"] ?? 0, StillSince = Dbl(j["still_since"]), StallTold = Dbl(j["stall_told"]),
                CallBackDay = (long?)j["call_back_day"] ?? long.MinValue, DayCallBacks = (int?)j["day_call_backs"] ?? 0, SlowHours = (int?)j["slow_hours"] ?? 0
            };
            foreach (SettingDef s in def.Settings.Values)
                t.Settings[s.Name] = j["settings"]?[s.Name] is JValue v && v.Type != JTokenType.Null ? (double)v : o.Settings.TryGetValue(s.Name, out double g) ? g : s.Default;
            if (FromJson(j["memory"]) is Dictionary<string, object> mem) foreach (var kv in mem) t.Memory[kv.Key] = kv.Value;
            foreach (JToken x in j["held"] as JArray ?? new JArray()) t.Held.Add((string)x);
            foreach (ToolInstance old in ById.Values.Where(x => x.Order.Party == o.Party && x.Id != id).ToList())
            {
                ById.Remove(old.Id);
                old.Owner?.Tools.Remove(old.Id);
            }
            ById[id] = t;
            owner.Tools[id] = t;
            NoteUsedId(id);
            // the move in force at the save (or the one about to be given) is given again: the lord was only held until now
            Move? m = MoveOf(j["pending"]) ?? MoveOf(j["current"]);
            if (m != null)
            {
                t.Current = m.Value;
                t.HasCurrent = true;
                MoveSink(id, m.Value);
            }
            Diag.Count("tool:carried on");
            Diag.Event("tool", "carried on", new { id, tool = def.Name, version = def.VersionText, author = def.Author, lord = o.Lord, party = o.Party, state = t.State, move = m?.ToString() });
            Outcomes.Resume(t, record, Diag.Hours);
            return null;
        }

        private static JToken Num(double d) => double.IsNaN(d) || double.IsInfinity(d) ? JValue.CreateNull() : new JValue(d);
        private static double Dbl(JToken t) => t is JValue v && v.Type != JTokenType.Null ? (double)v : double.NaN;

        private static JObject MoveJson(Move m) => new JObject { ["verb"] = m.Verb, ["place"] = m.Place, ["party"] = m.Party, ["x"] = m.X, ["y"] = m.Y, ["r"] = m.R };

        private static Move? MoveOf(JToken t)
        {
            if (!(t is JObject o) || (string)o["verb"] == null) return null;
            return new Move { Verb = (string)o["verb"], Place = (string)o["place"], Party = (string)o["party"], X = (float?)o["x"] ?? 0, Y = (float?)o["y"] ?? 0, R = (float?)o["r"] ?? 0 };
        }

        /// <summary>A remembered value as JSON (none, yes/no, a number, words, a list, a table) and back: a table is the only JSON object.</summary>
        internal static JToken ToJson(object v)
        {
            switch (v)
            {
                case null: return JValue.CreateNull();
                case bool b: return new JValue(b);
                case double d: return Num(d);
                case string s: return new JValue(s);
                case List<object> l: return new JArray(l.Select(ToJson));
                case Dictionary<string, object> table:
                    var o = new JObject();
                    foreach (var kv in table) o[kv.Key] = ToJson(kv.Value);
                    return o;
                default: return new JValue(Expr.Show(v));
            }
        }

        internal static object FromJson(JToken t)
        {
            switch (t?.Type)
            {
                case null:
                case JTokenType.Null: return null;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer:
                case JTokenType.Float: return (double)t;
                case JTokenType.String: return (string)t;
                case JTokenType.Array: return ((JArray)t).Select(FromJson).ToList();
                case JTokenType.Object:
                    var table = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (JProperty p in ((JObject)t).Properties()) table[p.Name] = FromJson(p.Value);
                    return table;
                default: return (string)t;
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
            Outcomes.End(id, HowOf(why), why, RowOf(t), Diag.Hours);
        }

        /// <summary>The tool's lord's row in its realm's newest hour (null when gone).</summary>
        private static PartyRow RowOf(ToolInstance t)
        {
            Snapshot s = t?.Owner?.Latest ?? Brain.LatestFor(t?.Order.Realm);
            return s != null && t?.Order.Party != null && s.PartyById.TryGetValue(t.Order.Party, out PartyRow r) ? r : null;
        }

        /// <summary>How a tool run ended, in one word, from the game's reason (Orders.LordGone, End): for the outcome's record.</summary>
        internal static string HowOf(string why)
        {
            string w = (why ?? "").ToLowerInvariant();
            if (w.Contains("prisoner") || w.Contains("captured")) return "captured";
            if (w.Contains("died")) return "killed";
            if (w.Contains("replaced")) return "replaced";
            if (w.Contains("released by") || w.Contains("led by the game")) return "released";
            if (w.Contains("party is gone")) return "party gone";
            if (w.Contains("army")) return "joined an army";
            if (w.Contains("no longer in")) return "left the realm";
            if (w.Contains("could not be done") || w.Contains("refused")) return "failed";
            if (w.Contains("is done")) return "done";
            return "ended";
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
                long began = Stopwatch.GetTimestamp();
                try { Step(t, w); }
                catch (Exception e)
                {
                    Diag.Count("tool:errors");
                    Diag.Error("running " + t.Def.Name + " for " + t.Order.LordName, e);
                }
                if (t.Def.Own) Timed(t, w, (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
            }
        }

        /// <summary>An agent's own tool's hour, timed (A5): over SlowMs for SlowHoursToStop hours running, it is stopped, the lord let go and the agent told why.</summary>
        private static void Timed(ToolInstance t, World w, double ms)
        {
            t.LastMs = ms;
            if (t.Finished) return;
            t.SlowHours = ms > SlowMs ? t.SlowHours + 1 : 0;
            if (t.SlowHours < SlowHoursToStop) return;
            t.Finished = true;
            Diag.Count("tool:own stopped, too slow");
            Diag.Alarm("tool_slow", t.Order.LordName + "'s " + t.Def.Name + " (" + t.Def.Author + "'s own tool) took over " + ToolDef.Num(SlowMs) + " ms an hour for " + SlowHoursToStop
                                    + " hours running (" + ToolDef.Num(Math.Round(ms, 1)) + " ms the last), so it was stopped.");
            Tell(t, t.Order.LordName + "'s " + t.Def.Name + " took over " + ToolDef.Num(SlowMs) + " ms of the game's helper each hour for " + SlowHoursToStop + " hours running ("
                    + ToolDef.Num(Math.Round(ms, 1)) + " ms the last), so it was stopped and " + t.Order.LordName + " was let go to the game's own AI. Make it lighter (fewer rules, fewer look-ups over the whole map each hour), check it again and give the order anew.", w);
            ReleaseSink(t.Id, "its tool " + t.Def.Name + " was stopped: it took too long each hour");
        }

        /// <summary>The orders in force of the agent whose realm's view this world is (none for a realm without an agent).</summary>
        private static IEnumerable<ToolInstance> InForceOf(World w) => Agents.Serving(w.Realm)?.Tools.Values ?? Enumerable.Empty<ToolInstance>();

        public static World WorldFor(Snapshot s) => new World(s, s.Watched, s.WatchedSees, Brain.Get(s.Watched).LastSeen);

        private static void Step(ToolInstance t, World w)
        {
            PartyRow me = w.Party(t.Order.Party);
            if (me == null || me.Attached) return;   // gone (the game's thread ends the order and tells us), or marching in another's army
            // an agent's own tool's call-backs held past yesterday's limit, summed in one now the day is out (0.13, A5)
            if (t.Def.Own && t.Held.Count > 0 && (long)Math.Floor(w.Hours / 24) != t.CallBackDay) SumHeld(t, w);
            var scope = new Scope(t, w);
            RememberName(t.Order.Target, w);
            if (t.Memory.TryGetValue("target", out object mt)) RememberName(mt as string, w);
            if (!t.Entered)
            {
                foreach (var kv in t.Def.Init) t.Memory[kv.Key] = kv.Value.Eval(scope);
                Enter(t, t.Def.Start, scope, w);
                Log(t, "start", t.Def.Start, null);
            }
            // the rules run in the hour a tool starts too, so a first step that decides at once (go in, or stop close) does;
            // riding to relieve a lord in a fight comes before both (0.13.15)
            if (!Relief(t, me, w, scope) && !Safety(t, me, w, scope) && !Supply(t, me, w, scope) && !t.Finished)
            {
                Rules(t, scope, w);
                // a shadow follows its target: its point is worked out again every hour (sent only when it moved)
                if (!t.Finished && t.Shelter == null && t.SupplyAt == null && t.Def.States.TryGetValue(t.State, out StateDef live) && live.Do?.Verb == "shadow")
                {
                    t.Pending = Resolve(t, live.Do, scope, w);
                    t.HasPending = true;
                }
                if (!t.Finished && t.Shelter == null && t.SupplyAt == null) Stalled(t, me, w, scope);
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
                for (int i = 0; i < fired.Set.Count; i++) Remember(t, fired.Set[i].Key, fired.SetAt[i], fired.Set[i].Value, scope);
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

        /// <summary>A rule's set: a plain name, or a table's entry ("visited[stop]", 0.13), the table made when it is not one yet.</summary>
        private static void Remember(ToolInstance t, string name, Expr at, Expr value, Scope scope)
        {
            object v = value?.Eval(scope);
            if (at == null) { t.Memory[name] = v; return; }
            string key = Expr.Key(at.Eval(scope));
            if (key == null) { Diag.Count("tool:no key"); return; }   // an entry for none is not kept
            if (!(t.Memory.TryGetValue(name, out object o) && o is Dictionary<string, object> table))
                t.Memory[name] = table = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            table[key] = v;
        }

        private static void Enter(ToolInstance t, string state, Scope scope, World w)
        {
            t.State = t.Def.States.ContainsKey(state) ? t.Def.States[state].Name : state;
            t.StateSince = w.Hours;
            t.MoveSince = w.Hours;   // a state's move begins when it is entered, even the same move again (a wait counts its hours anew)
            t.Entered = true;
            StateDef st = t.Def.States[t.State];
            if (st.End != null) { Finish(t, st.End, w); return; }
            // a state with no move only decides (0.13): the lord keeps the move he has (a tool's first state holds him)
            if (st.Do == null && t.HasCurrent) return;
            t.Pending = Resolve(t, st.Do, scope, w);
            t.HasPending = true;
        }

        /// <summary>A lord this many in-game hours standing still on his way to a place, still farther from it than StallNear, is stuck.</summary>
        public const double StallHours = 6, StallNear = 6;

        /// <summary>
        /// A march that stopped short (0.12.1): a lord whose move takes him to a place (near, goto, enter, post, toward; since 0.13 recruit and sell) and who
        /// has not moved for StallHours while still far from it is called back once (run 20261025-1524-C2: three sieges stood
        /// still outside their castles for up to 15 days and the agent was never told). The order is kept: the lord holds where
        /// he is until the agent gives another.
        /// </summary>
        private static void Stalled(ToolInstance t, PartyRow me, World w, Scope scope)
        {
            string verb = t.HasCurrent ? t.Current.Verb : null;
            // 0.13: a stop to recruit or sell at is a place to get to as well
            bool toPlace = verb == "near" || verb == "goto" || verb == "enter" || verb == "recruit" || verb == "sell";
            bool toSpot = verb == "post" || verb == "toward";
            double d = double.NaN;
            if ((toPlace || toSpot) && !me.InBattle && me.InSettlement == null && me.Besieging == null)
                d = toPlace ? w.Near(me.Id, t.Current.Place) : Math.Sqrt((me.X - t.Current.X) * (me.X - t.Current.X) + (me.Y - t.Current.Y) * (me.Y - t.Current.Y));
            if (double.IsNaN(d) || d <= (toPlace ? StallNear : 3))
            {
                t.StillSince = double.NaN;
                return;
            }
            double dx = me.X - t.StillX, dy = me.Y - t.StillY;
            if (double.IsNaN(t.StillSince) || t.StillSince < t.StateSince || dx * dx + dy * dy > 1)
            {
                t.StillSince = w.Hours;
                t.StillX = me.X;
                t.StillY = me.Y;
                return;
            }
            if (w.Hours - t.StillSince < StallHours || t.StallTold == t.StillSince) return;
            t.StallTold = t.StillSince;
            string about = t.Def.States.TryGetValue(t.State, out StateDef st) && st.About != null ? Template.Fill(st.About, scope, v => Show(v, w)) : t.Def.Name;
            string where = toPlace ? w.Name(t.Current.Place) : Spots.Describe(w.S, t.Current.X, t.Current.Y);
            Diag.Count("tool:stalled");
            Diag.Event("tool", "stalled", new { id = t.Id, tool = t.Def.Name, lord = t.Order.Lord, state = t.State, move = t.Current.ToString(), distance = Math.Round(d, 1), hours = Math.Round(w.Hours - t.StillSince, 1) });
            Poke(t, t.Order.LordName + " has not moved for " + Math.Round(w.Hours - t.StillSince) + " hours while " + about + ", still " + Text.D((float)d) + " from " + where
                    + ": he may be stuck on the map. The order is kept and he holds where he is; give him another order if he should go elsewhere.", w);
        }

        /// <summary>The move a state makes, worked out now: a holding, a party, or the point a party was last seen at. problems: a try's (it says, instead of counting).</summary>
        private static Move Resolve(ToolInstance t, ActionDef a, Scope scope, World w, List<string> problems = null)
        {
            if (a == null) return new Move { Verb = "hold" };
            ToolDef.Verbs.TryGetValue(a.Verb, out string kind);
            if (a.Verb == "recruit_here")
            {
                // where he stands (0.13): the holding he is in, or the village he stands at
                PartyRow me = w.Party(t.Order.Party);
                string here = me?.InSettlement ?? w.S.Settlements.Where(h => me != null && w.Near(me.Id, h.Id) <= 2).OrderBy(h => w.Near(me.Id, h.Id)).FirstOrDefault()?.Id;
                if (here == null) problems?.Add("recruit_here: he stands in no holding and at no village now, so he would hold where he is");
                return here != null ? new Move { Verb = "recruit", Place = here } : new Move { Verb = "hold" };
            }
            if (kind == "none") return new Move { Verb = a.Verb };
            object v = a.Arg?.Eval(scope);
            string id = v as string;
            if (kind == "number")
            {
                // wait N (0.13): he stays where he is for N hours (kept to 0 to 240)
                double n = Expr.Number(v);
                return new Move { Verb = "wait", R = double.IsNaN(n) ? 0f : (float)Math.Max(0, Math.Min(240, n)) };
            }
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
            if (problems != null)
            {
                problems.Add(a + ": " + a.ArgText + " is " + (v == null ? "none" : "'" + Expr.Show(v) + "', which is no " + (kind == "holding" || kind == "spot" ? "holding" : "party") + " your realm knows") + " now, so he would hold where he is");
                return new Move { Verb = "hold" };
            }
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
            // the tool run's outcome is taken here, where the tool itself ends, not later when the lord's hold ends (0.13)
            Outcomes.End(t.Id, end == "fail" ? "failed" : end == "stop" ? "stopped" : t.Order.Then != null && end != "stop" ? "done, next order" : end == "hold" ? "done, holds" : "done",
                         "the tool reached its end (" + end + ")", w.Party(t.Order.Party), w.Hours);
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
            t.MoveSince = w.Hours;
            MoveSink(t.Id, m);
        }

        /// <summary>
        /// Has the lord got where his move takes him (0.13, the language's "arrived")? Into or at its holding (goto, enter, recruit, sell,
        /// tour: inside, or within 2 of it, as a lord stands at a village), close to it (near: within 6), at its point (post, toward,
        /// shadow: within 1.5), or, for wait, its hours passed. A patrol, a strike, a chase or a siege never arrives.
        /// </summary>
        private static bool Arrived(ToolInstance t, PartyRow me, World w)
        {
            // the state's own move: the one about to be given when a state was just entered, else the one in force
            if ((!t.HasCurrent && !t.HasPending) || me == null) return false;
            Move m = t.HasPending ? t.Pending : t.Current;
            switch (m.Verb)
            {
                case "goto":
                case "enter":
                case "recruit":
                case "sell":
                case "tour":
                    return m.Place != null && (me.InSettlement == m.Place || w.Near(me.Id, m.Place) <= 2);
                case "near": return m.Place != null && w.Near(me.Id, m.Place) <= StallNear;
                case "post":
                case "toward":
                case "shadow": return Math.Sqrt((me.X - m.X) * (me.X - m.X) + (me.Y - m.Y) * (me.Y - m.Y)) <= 1.5;
                case "wait": return !double.IsNaN(t.MoveSince) && w.Hours - t.MoveSince >= m.R - 1e-6;
                case "hold":
                case "leave": return true;
                default: return false;
            }
        }

        // ---------- "check a tool"'s try (0.13) ----------

        /// <summary>
        /// The try (docs/PLAN-0.13.md A4): a tool on the realm's newest hour for one lord, moving no one and telling no one. Its start state's
        /// move, each of that state's rules with whether it would fire now (and its names that are none now), and for the first that would,
        /// what it remembers, its call-back and where it leads, as the runner goes in an hour (at most three changes of state). A move whose
        /// place or party comes out none, and anything that cannot be worked out, is said where it is. Only the try itself is recorded.
        /// </summary>
        public static string Try(ToolDef def, ToolOrder o, Snapshot s)
        {
            World w = WorldFor(s);
            if (w.Party(o.Party) == null) return "THE TRY: " + (o.LordName ?? o.Lord) + "'s party is not in your realm's newest hour, so the tool cannot be tried on him.";
            var t = new ToolInstance { Id = 0, Order = o, Def = def, Started = w.Hours, StateSince = w.Hours, MoveSince = w.Hours };
            foreach (SettingDef sd in def.Settings.Values) t.Settings[sd.Name] = o.Settings.TryGetValue(sd.Name, out double v) ? Math.Max(sd.Min, Math.Min(sd.Max, v)) : sd.Default;
            var scope = new Scope(t, w);
            var lines = new List<string> { "THE TRY: " + def.Name + " for " + o.LordName + " on your realm's newest hour (" + Text.When(w.Hours) + "); no one moves." };
            foreach (KeyValuePair<string, Expr> kv in def.Init)
            {
                try { t.Memory[kv.Key] = kv.Value.Eval(scope); }
                catch (Exception e) { lines.Add("- init " + kv.Key + ": cannot be worked out (" + e.Message + ")"); }
            }
            t.State = def.Start;
            // as the runner's hour goes (Enter, then Rules): the start state's move, then at most three changes of state, each new state's move
            // worked out as it is entered, stopping at a state already entered this hour
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { t.State };
            if (!Entered(t, def.States[t.State], scope, w, lines)) { lines.Add(SafetyNote); return string.Join("\n", lines); }
            for (int hop = 0; hop < 3; hop++)
            {
                StateDef st = def.States[t.State];
                TransitionDef fired = null;
                foreach (TransitionDef tr in st.When)
                {
                    string value;
                    bool yes = false;
                    try
                    {
                        yes = Expr.Truthy(tr.If.Eval(scope));
                        value = yes ? "true" : "false";
                    }
                    catch (Exception e) { value = "cannot be worked out (" + e.Message + ")"; }
                    var nones = new List<string>();
                    tr.If.Walk(n => { if (!nones.Contains(n) && scope.Get(n) == null) nones.Add(n); }, (f, n) => { });
                    lines.Add("  rule " + tr.Number + " (\"" + tr.IfText + "\"): " + value + (nones.Count > 0 ? "; none now: " + string.Join(", ", nones) : "") + (yes && fired == null ? ". It fires." : "."));
                    if (yes && fired == null) fired = tr;
                }
                if (fired == null) { lines.Add("- No rule fires now: he would make that move, and the rules are looked at again each hour."); break; }
                for (int i = 0; i < fired.Set.Count; i++)
                {
                    try { Remember(t, fired.Set[i].Key, fired.SetAt[i], fired.Set[i].Value, scope); }
                    catch (Exception e) { lines.Add("  ! " + fired.SetText[i] + ": cannot be worked out (" + e.Message + ")."); }
                }
                if (fired.SetText.Count > 0) lines.Add("- It remembers " + string.Join(", ", fired.SetText) + ".");
                if (fired.Poke != null) lines.Add("- It would call you back: \"" + Template.Fill(fired.Poke, scope, x => Show(x, w)) + "\"");
                if (string.Equals(fired.Go, t.State, StringComparison.OrdinalIgnoreCase)) { lines.Add("- It stays in " + t.State + " (a rule that stays only remembers, once an hour)."); break; }
                t.State = def.States[fired.Go].Name;
                t.StateSince = w.Hours;
                t.MoveSince = w.Hours;
                lines.Add("- Then to state " + t.State + ":");
                if (!Entered(t, def.States[t.State], scope, w, lines)) break;
                if (!visited.Add(t.State)) { lines.Add("- " + t.State + " was already entered this hour, so its rules wait for the next hour."); break; }
                if (hop == 2) lines.Add("- Its rules are looked at the next hour: a tool changes state at most 3 times an hour.");
            }
            lines.Add(SafetyNote);
            return string.Join("\n", lines);
        }

        private const string SafetyNote = "(The safety and food rules are not tried here: they hold for every order, yours included.)";

        /// <summary>A state entered in a try: its move worked out and said, with what comes out none; false when the state ends the tool.</summary>
        private static bool Entered(ToolInstance t, StateDef st, Scope scope, World w, List<string> lines)
        {
            if (st.End != null) { lines.Add("- State " + st.Name + " ends the tool (" + st.End + ")."); return false; }
            var problems = new List<string>();
            if (st.Do != null)
            {
                t.Pending = Resolve(t, st.Do, scope, w, problems);
                t.HasPending = true;
                lines.Add("- State " + st.Name + ": its move \"" + st.Do + "\" is " + Plain(t.Pending, w) + ".");
            }
            else lines.Add("- State " + st.Name + ": no move of its own, it only decides" + (t.HasPending ? " (he keeps " + Plain(t.Pending, w) + ")" : " (he holds where he is)") + ".");
            foreach (string p in problems) lines.Add("  ! " + p + ".");
            return true;
        }

        /// <summary>A move in words for the try: what it does and to what, names with their ids.</summary>
        private static string Plain(Move m, World w)
        {
            string place = m.Place != null ? " " + Show(m.Place, w) + " [" + m.Place + "]" : "";
            string party = m.Party != null ? " " + Show(m.Party, w) + " [" + m.Party + "]" : "";
            string point = m.Verb == "post" || m.Verb == "toward" || m.Verb == "shadow" ? " at " + Spots.Describe(w.S, m.X, m.Y) : "";
            string r = m.Verb == "wait" ? " " + ToolDef.Num(m.R) + " hours" : m.R > 0 ? " within " + ToolDef.Num(m.R) : "";
            return m.Verb + place + party + point + r;
        }

        // ---------- the three rules every order carries ----------

        /// <summary>One fight a realm's held lords could ride to relieve, worked out once for the realm's hour (0.13.15).</summary>
        private sealed class ReliefPlan
        {
            public BattleRow Battle;
            /// <summary>The strongest lord of ours or an ally's in it (named in the call-back), and the enemy party the riders strike: its side's leader, else its strongest.</summary>
            public PartyRow Friend;
            public string Enemy;
            /// <summary>Hours left by the battle clock (at most Library.MaxReliefHours).</summary>
            public double EndsIn;
            /// <summary>Our side: the parties in it and every rider; theirs: the parties in it and every enemy lord and army in view who could join it within EndsIn.</summary>
            public double Ours, Theirs;
            public int Joiners;
            /// <summary>Each held lord sent to it: his tool's id, and his hours to it.</summary>
            public readonly Dictionary<int, double> Riders = new Dictionary<int, double>();
            /// <summary>Of the riders, those free to ride (FreeToRide) and those sent from other orders to tip the balance (0.13.17).</summary>
            public int Free, Busy;
            /// <summary>Would the lords in it on our side not win alone: under Library.ReliefWinBar times the enemy with its joiners (0.13.17)?</summary>
            public bool Needed;
            public bool Outweighs => Ours > Theirs;
        }

        [ThreadStatic] private static World _reliefFor;
        [ThreadStatic] private static List<ReliefPlan> _reliefPlans;

        /// <summary>
        /// The fights the realm's held lords could ride to this hour (0.13.15; Mert, Oct 30: "lords under any order and not in an army should
        /// always ride with all haste to relieve lords in a fight if their combined strength beats the enemies' in the area's combined strength
        /// for the expected duration of the fight ... Armies should still try to relieve lords if they are close enough to reach them before the
        /// fight is done and combined strength is stronger ... but should never break up because of this"). A fight in the field (or at a
        /// siege camp, or a raid) with a lord of ours or an ally's on one side and an enemy realm's on the other; its hours left by the battle
        /// clock (Forecast: as measured once it has run two rounds, else by the rules at middling morale); the riders, every held lord out of
        /// an army or leading one (CanRide) who can get there before it ends at his own pace; our side there, the parties in it and every
        /// rider, against theirs, the parties in it and every enemy lord and army in view who could join it within those hours. Once a world.
        /// </summary>
        private static List<ReliefPlan> ReliefPlans(World w)
        {
            if (ReferenceEquals(_reliefFor, w) && _reliefPlans != null) return _reliefPlans;
            _reliefFor = w;
            _reliefPlans = new List<ReliefPlan>();
            if (w.S.Battles == null || w.S.Battles.Count == 0) return _reliefPlans;
            List<ToolInstance> held = InForceOf(w).Where(x => !x.Finished).ToList();
            if (held.Count == 0) return _reliefPlans;
            bool Friendly(BattleSideRow side) => side.Faction != null && (side.Faction == w.Realm || w.Ally(side.Faction));
            List<PartyRow> Rows(BattleSideRow side) => side.Parties.Select(id => w.S.PartyById.TryGetValue(id, out PartyRow r) ? r : null).Where(r => r != null).ToList();
            foreach (BattleRow b in w.S.Battles)
            {
                if (b.Kind != "field" && b.Kind != "siege_outside" && b.Kind != "raid") continue;   // not a storm or a sally behind walls
                bool att = Friendly(b.Attackers), def = Friendly(b.Defenders);
                if (att == def) continue;
                BattleSideRow ours = att ? b.Attackers : b.Defenders, theirs = att ? b.Defenders : b.Attackers;
                if (!w.HostileFaction(theirs.Faction)) continue;
                List<PartyRow> ourRows = Rows(ours), theirRows = Rows(theirs);
                PartyRow friend = ourRows.Where(r => r.Kind == "lord" && (w.Own(r) || (w.Ally(r.Faction) && w.Visible(r)))).OrderByDescending(r => r.Strength).FirstOrDefault();
                string enemy = theirRows.Any(r => r.Id == theirs.Leader) ? theirs.Leader : theirRows.OrderByDescending(r => r.Strength).FirstOrDefault()?.Id;
                if (friend == null || enemy == null) continue;
                // its clock run first as it stands, then with the enemies who could join it within those hours coming in as they would arrive
                // (0.13.16): the enemy lords and armies in view, and bands of the enemy's own kind when it is one (World.BandsOf)
                double alone = EndsIn(b, w, att, null);
                if (alone < 0.5) continue;
                var inIt = new HashSet<string>(b.Attackers.Parties.Concat(b.Defenders.Parties));
                List<PartyRow> joiners = w.Joiners(b.X, b.Y, alone).Concat(w.BandsOf(theirs.Faction, b.X, b.Y, alone)).Where(e => !inIt.Contains(e.Id)).ToList();
                double endsIn = joiners.Count == 0 ? alone
                    : Math.Min(alone, EndsIn(b, w, att, joiners.Select(e => Tuple.Create(!att, World.MenOf(e), w.Hours + w.HoursTo(e, b.X, b.Y))).ToList()));
                if (endsIn < 0.5) continue;
                // each party in a fight is counted by itself (an army's lords fight in it party by party); outside it, a lord with his army
                var p = new ReliefPlan { Battle = b, Friend = friend, Enemy = enemy, EndsIn = endsIn, Joiners = joiners.Count,
                                         Ours = ourRows.Sum(r => r.Strength), Theirs = theirRows.Sum(r => r.Strength) + joiners.Sum(World.StrengthOf) };
                // who can get there in time with time to spare
                var can = new List<Tuple<ToolInstance, PartyRow, double>>();
                foreach (ToolInstance x in held)
                {
                    PartyRow r = w.Party(x.Order.Party);
                    if (r == null || inIt.Contains(r.Id) || !CanRide(x, r, w)) continue;
                    double hours = w.HoursTo(r, b.X, b.Y);
                    if (hours > endsIn - Library.ReliefSpare(endsIn)) continue;
                    can.Add(Tuple.Create(x, r, hours));
                }
                // who is sent (0.13.17; Mert, Nov 1): none while our side there, with every one of them, would not outweigh the enemy; else every
                // lord free to ride, however the fight goes ("a crushing victory reduces losses compared to a close victory"); and, only while the
                // lords in it would not win alone, lords from other orders, nearest first (one already riding to it first), until our side is
                // Library.ReliefWinBar times the enemy ("make sure the logic assigns enough lords to tip the balance")
                double bar = Library.ReliefWinBar * p.Theirs;
                p.Needed = p.Ours < bar;
                if (p.Ours + can.Sum(c => World.StrengthOf(c.Item2)) > p.Theirs)
                {
                    foreach (Tuple<ToolInstance, PartyRow, double> c in can.Where(c => FreeToRide(c.Item1)))
                    {
                        p.Riders[c.Item1.Id] = c.Item3;
                        p.Ours += World.StrengthOf(c.Item2);
                        p.Free++;
                    }
                    foreach (Tuple<ToolInstance, PartyRow, double> c in can.Where(c => !FreeToRide(c.Item1)).OrderByDescending(c => c.Item1.ReliefOf == b.Key).ThenBy(c => c.Item3))
                    {
                        if (!p.Needed || p.Ours >= bar) break;
                        p.Riders[c.Item1.Id] = c.Item3;
                        p.Ours += World.StrengthOf(c.Item2);
                        p.Busy++;
                    }
                }
                _reliefPlans.Add(p);
            }
            return _reliefPlans;
        }

        /// <summary>
        /// A fight's hours left by its clock, at most Library.MaxReliefHours (also when it would not end within the clock's five days): as
        /// measured once it has run two rounds, else by the rules at middling morale; joiners (side, men, the hour each arrives) run in.
        /// </summary>
        private static double EndsIn(BattleRow b, World w, bool ownAttackers, List<Tuple<bool, int, double>> joiners)
        {
            Forecast.BattleClock c = Forecast.Measured(b, w.Hours, ownAttackers, joiners) ?? Forecast.Battle(b, w.Hours, joiners, 50f, 50f);
            return double.IsNaN(c.EndsAt) ? Library.MaxReliefHours : Math.Min(Library.MaxReliefHours, c.EndsAt - w.Hours);
        }

        /// <summary>
        /// Is a held lord free to ride to any fight his side can win, however it goes (0.13.17; Mert, Nov 1: "lords that have a
        /// defend/patrol/recruit order (so lords that are basically not doing anything important) still go to relieve another lord regardless
        /// of how the battle is going, since a crushing victory reduces losses compared to a close victory")? An order to defend, patrol, hold,
        /// recruit or rest, or a realm's own recruiting tool (one with "recruit" in its name: recruit_round, home_recruit, safe_recruit). Any
        /// other order rides only when the lords in the fight would not win alone (ReliefPlans).
        /// </summary>
        internal static bool FreeToRide(ToolInstance t)
        {
            string tool = (t?.Order?.Tool ?? "").Trim().ToLowerInvariant();
            return tool == "defend" || tool == "patrol" || tool == "hold" || tool == "rest" || tool.Contains("recruit");
        }

        /// <summary>
        /// Can a held lord ride to a fight (0.13.15)? Out of an army or leading one (a lord marching in another's army goes where it goes, and
        /// an army moves whole, never broken up), not in a fight already, not camped in a siege (leaving it would lift his part of it), not
        /// inside a besieged castle or town (he cannot leave), and not on his way to buy food with under two days of it.
        /// </summary>
        private static bool CanRide(ToolInstance t, PartyRow me, World w)
        {
            if (me == null || me.Kind != "lord" || me.Attached || me.InBattle || me.Besieging != null || t.SupplyAt != null || t.Finished) return false;
            SettlementRow inside = w.Holding(me.InSettlement);
            return !(inside != null && World.IsFort(inside) && w.Besieged(inside));
        }

        /// <summary>
        /// The relief rule (0.13.15): a held lord rides with all haste to the nearest fight he can relieve (ReliefPlans: he gets there before it
        /// ends, and our side there with every lord who can get there in time outweighs the enemies in it and those who could join it by then),
        /// striking the enemy side's leader; sheltering or not. In the fight, the game fights it and the order waits. Once the fight is over, or
        /// no longer outweighed, or out of his reach, the order resumes.
        /// </summary>
        private static bool Relief(ToolInstance t, PartyRow me, World w, Scope scope)
        {
            if (t.ReliefOf != null && me.InBattle) return true;
            ReliefPlan best = null;
            double bestHours = double.MaxValue;
            if (CanRide(t, me, w))
                foreach (ReliefPlan p in ReliefPlans(w))
                    if (p.Outweighs && p.Riders.TryGetValue(t.Id, out double h) && h < bestHours) { best = p; bestHours = h; }
            if (best == null)
            {
                if (t.ReliefOf == null) return false;
                string was = t.ReliefOf;
                bool over = w.S.Battles == null || !w.S.Battles.Any(b => b.Key == was);
                t.ReliefOf = t.ReliefTarget = null;
                Log(t, "relieving", t.State, over ? "the fight is over" : "the fight is no longer one to relieve (our side no longer outweighs, it is out of reach, or he is no longer needed to tip the balance)");
                Diag.Event("tool", "relief over", new { id = t.Id, lord = t.Order.Lord, battle = was, over });
                Resume(t, scope, w);
                return false;
            }
            bool first = t.ReliefOf != best.Battle.Key;
            if (t.Shelter != null) { t.Shelter = t.ShelterFrom = null; t.Running = false; }
            t.ReliefOf = best.Battle.Key;
            if (first || t.ReliefTarget != best.Enemy || !(t.HasCurrent && t.Current.Verb == "engage" && t.Current.Party == best.Enemy))
            {
                t.ReliefTarget = best.Enemy;
                t.Pending = new Move { Verb = "engage", Party = best.Enemy };
                t.HasPending = true;
            }
            if (!first) return true;
            t.AsideSince = w.Hours;
            Diag.Count("tool:reliefs");
            Diag.Event("tool", "relief", new { id = t.Id, lord = t.Order.Lord, battle = best.Battle.Key, enemy = best.Enemy, hours = Math.Round(bestHours, 1), ends_in = Math.Round(best.EndsIn, 1),
                                               ours = Math.Round(best.Ours), theirs = Math.Round(best.Theirs), riders = best.Riders.Count, joiners = best.Joiners,
                                               free = best.Free, busy = best.Busy, needed = best.Needed, free_to_ride = FreeToRide(t) });
            string where = best.Battle.Settlement != null ? "at " + w.Name(best.Battle.Settlement) : FightPlace(Spots.Describe(w.S, best.Battle.X, best.Battle.Y));
            // why he goes (0.13.17): a lord free to ride goes to a fight his side wins, to win it bigger; one from another order only to tip the balance
            string why = FreeToRide(t)
                ? (best.Needed ? "" : " Your lords there should win alone; he goes so they win it bigger, with fewer losses.")
                : " He leaves his order for it: your lords there would not win alone" + (best.Busy > 1 ? ", and he is one of the " + best.Busy + " nearest lords sent from other orders to tip the balance" : "") + ".";
            Poke(t, t.Order.LordName + " rides with all haste to relieve " + w.Name(best.Friend.Id) + ", fighting " + w.Name(best.Enemy) + " " + where
                    + " (there in about " + Forecast.HoursText(bestHours) + "; the fight should last about " + Forecast.HoursText(best.EndsIn) + " more): your side there, with the "
                    + best.Riders.Count + " of your lords riding to it, " + Text.N((int)best.Ours) + ", against " + Text.N((int)best.Theirs)
                    + (best.Joiners > 0 ? " with the " + best.Joiners + " enemy lords, armies or bands who could join it by then" : "") + "." + why + " The order resumes once the fight is over.", w);
            Log(t, t.State, "relieving", "a lord of ours fighting, and our side with every lord who can reach it before it ends outweighs the enemies there and those who could join it");
            return true;
        }

        /// <summary>
        /// A fight's place after "fighting X" (0.13.16): "the spot 2 north-west of Düglar-mauz" read "fighting Mirkwood Stalkers the spot 2
        /// north-west of Düglar-mauz"; now "2 north-west of Düglar-mauz", "at Brak-gash", or "at the spot p673_994".
        /// </summary>
        internal static string FightPlace(string spot)
        {
            const string head = "the spot ";
            if (spot == null || !spot.StartsWith(head, StringComparison.Ordinal)) return spot;
            string rest = spot.Substring(head.Length);
            if (rest.StartsWith("at ", StringComparison.Ordinal)) return rest;
            return rest.Length > 0 && char.IsDigit(rest[0]) ? rest : "at " + spot;
        }

        /// <summary>The shelter of a lord running straight away from a danger, with no place to make for.</summary>
        private const string Away = "(away)";

        private static bool Safety(ToolInstance t, PartyRow me, World w, Scope scope)
        {
            double caution = t.Def.Safety && t.Settings.TryGetValue("caution", out double c) ? c : 0;
            double radius = t.Settings.TryGetValue("danger_radius", out double r) ? r : Library.DangerRadius.Default;
            // our side, counted as the danger is (0.12.4): the lord and every lord of the realm or an ally who could join a fight where
            // he stands within join_hours, and those camped in his siege
            var with = new List<PartyRow>();
            double mine = w.SideStrength(me, me.X, me.Y, JoinHoursOf(t), me.Besieging ?? CampOf(t), Objectives(t), with);
            string ours = SideText(t, me, mine, with);
            var spare = new HashSet<string> { t.Order.Target, t.Current.Party, scope.Get("target") as string };
            // striking a siege camp: every lord camped in that siege is part of the fight the order chose, not a danger to flee
            string siege = t.Current.Verb == "engage" ? w.Party(t.Current.Party)?.Besieging : null;
            // a shadow keeps its own distance from its target, but a target that turns to chase it is a danger like any other
            bool shadowing = t.Current.Verb == "shadow";
            // ... and so are the lords marching with the army it shadows (0.12.2, the danger counted with hosts), unless they chase it
            string shadowed = shadowing ? w.HeadOf(w.Party(t.Current.Party))?.Id : null;
            bool Chasing(PartyRow e) => e.Doing == "EngageParty" && e.TargetParty == me.Id;
            bool Spared(PartyRow e) => (spare.Contains(e.Id) && !(shadowing && Chasing(e))) || (siege != null && e.Besieging == siege)
                                       || (shadowed != null && w.HeadOf(e)?.Id == shadowed && !Chasing(e));
            // the danger counted as a group (0.12.2): everything in view within reach of the lord and the hosts those lords march
            // with, together (World.DangerAround); the strongest of them stands for where it is and how fast it comes
            Danger Find(double within)
            {
                List<PartyRow> g = w.DangerAround(me.Id, within, JoinHoursOf(t), Spared);
                double sum = g.Sum(World.StrengthOf);
                if (g.Count == 0 || sum < caution * Math.Max(1, mine)) return null;
                return new Danger { Lead = g.OrderByDescending(World.StrengthOf).First(), Strength = sum, Count = g.Count, Ids = new HashSet<string>(g.Select(x => x.Id)), Ours = ours };
            }
            // the order's own holding: always the shelter with stay_inside 1 (garrison's default); tried first, and kept only
            // if it would hold, with stay_inside 0 (defend's default)
            SettlementRow own = w.Holding(t.Order.At);
            bool hasStay = t.Settings.TryGetValue("stay_inside", out double si);
            bool alwaysOwn = hasStay && si > 0 && World.IsFort(own) && !w.Besieged(own);
            string preferred = hasStay && World.IsFort(own) ? own.Id : null;
            if (t.Shelter != null)
            {
                Danger still = caution > 0 ? Find(radius * 1.5) : null;
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
                // still on the way, sheltering or running: plan again from where the lord is now, against the danger as it is now, and
                // take a refuge that would hold as soon as one is in reach (0.12.4: only a running lord planned again, so Haldir, Zlatka
                // and Alvar, marching to a refuge chosen hours before, were caught on the way by parties that had come up since)
                if (still != null && me.InSettlement == null && !me.InBattle) Shelter(t, me, w, still, mine, alwaysOwn ? own : null, preferred, again: true);
                return true;
            }
            if (caution <= 0 || me.InBattle || me.Besieging != null) return false;
            SettlementRow inside = w.Holding(me.InSettlement);
            if (inside != null && World.IsFort(inside) && w.Besieged(inside)) return false;   // inside a besieged place: it cannot leave
            Danger danger = Find(radius);
            if (danger == null) return false;
            if (inside != null && World.IsFort(inside))
            {
                // inside a castle or town: stay if it would hold (or the order is told to stay in its own holding); else run
                if ((alwaysOwn && inside.Id == own.Id) || w.Holds(inside, me.Id, danger.Strength + w.ThreatStrength(inside.Id, World.HoldRadius, danger.Ids))) return false;
            }
            return Shelter(t, me, w, danger, mine, alwaysOwn ? own : null, preferred, again: false);
        }

        /// <summary>The siege an order's lord is in or making for: a besiege order's place (0.12.4).</summary>
        private static string CampOf(ToolInstance t) => t.Def.Name == "besiege" || (t.HasCurrent && t.Current.Verb == "besiege") ? (t.HasCurrent && t.Current.Verb == "besiege" ? t.Current.Place : t.Order.At) : null;

        /// <summary>What an order is aimed at: its place, its target, its current move's, and those named (0.12.4, World.Friends).</summary>
        private static HashSet<string> Objectives(ToolInstance t, params string[] more)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            void Add(string s) { if (!string.IsNullOrEmpty(s)) set.Add(s); }
            Add(t.Order.At);
            Add(t.Order.Target);
            if (t.HasCurrent) { Add(t.Current.Place); Add(t.Current.Party); }
            if (t.Memory.TryGetValue("target", out object mt)) Add(mt as string);
            foreach (string s in more) Add(s);
            return set;
        }

        /// <summary>Our side in words: the lord's own strength, and with his friends when any could join.</summary>
        private static string SideText(ToolInstance t, PartyRow me, double side, List<PartyRow> with) =>
            with.Count == 0 ? Text.N((int)World.StrengthOf(me))
                            : t.Order.LordName + "'s " + Text.N((int)World.StrengthOf(me)) + ", " + Text.N((int)side) + " with the " + with.Count + " of your lords who could join within " + ToolDef.Num(JoinHoursOf(t)) + " hours";

        /// <summary>The hours ahead an order's fights count the enemy lords and armies who could join them (its join_hours, 0.12.3).</summary>
        public static double JoinHoursOf(ToolInstance t) => t.Settings.TryGetValue("join_hours", out double h) ? h : Library.JoinHours.Default;

        /// <summary>A danger to a lord, counted as a group (0.12.2): its strongest party, and all of it together.</summary>
        private sealed class Danger
        {
            public PartyRow Lead;
            public double Strength;
            public int Count;
            public HashSet<string> Ids;
            /// <summary>Our side in words (SideText): "600" or "Imák's 1,800, 6,400 with the 4 of your lords who could join within 10 hours".</summary>
            public string Ours;

            /// <summary>"Bolga and 11 more with it (strength 13,000 together" or "Bolga (strength 1,094"; the caller closes it.</summary>
            public string Say(World w) => w.Name(Lead.Id) + (Count > 1 ? " and " + (Count - 1) + " more with it (strength " + Text.N((int)Math.Round(Strength)) + " together"
                                                                         : " (strength " + Text.N((int)Math.Round(Strength)));
        }

        /// <summary>
        /// The lord takes shelter from a danger, or runs from it (World.ChooseRefuge): inside the first castle or town it can
        /// reach whose defenders, with it, would hold against the danger and every force near that place; if none would,
        /// toward the nearest one that takes it farther from the danger, stopping outside. The agent is told which, and why.
        /// </summary>
        private static bool Shelter(ToolInstance t, PartyRow me, World w, Danger group, double mine, SettlementRow forced, string preferred, bool again)
        {
            PartyRow danger = group.Lead;
            bool holds = forced != null, running = false;
            SettlementRow fort = forced ?? w.ChooseRefuge(me.Id, preferred, World.HoldRadius, danger, out holds, out running, group.Strength, group.Ids);
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
                    Poke(t, t.Order.LordName + " is falling back, away from " + group.Say(w) + " against " + (group.Ours ?? Text.N((int)mine))
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
            double opposing = group.Strength + w.ThreatStrength(fort.Id, World.HoldRadius, group.Ids);
            string why = running
                ? t.Order.LordName + " is falling back toward " + w.Name(fort.Id) + ", away from " + group.Say(w) + " against "
                  + (group.Ours ?? Text.N((int)mine)) + "): no castle or town it could reach first would hold against it, so it stays out of reach rather than be trapped inside."
                : t.Order.LordName + " is sheltering in " + w.Name(fort.Id) + " from " + group.Say(w) + " against "
                  + (group.Ours ?? Text.N((int)mine)) + "): " + (forced != null ? "its order keeps it inside." : "its defenders with " + t.Order.LordName + ", strength " + Text.N((int)w.DefenceStrength(fort, me.Id))
                  + " behind walls worth " + ToolDef.Num(Math.Round(World.WallFactor(fort), 1)) + " times that, would hold against " + Text.N((int)opposing) + ".");
            Poke(t, why + " The order resumes once the danger has passed.", w);
            Log(t, t.State, running ? "running" : "sheltering", "enemies within its danger radius, with their hosts, stronger together than its caution allows");
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
            // an agent's own tool calls back at most CallBacksPerDay times an in-game day; the rest are held, and summed when the day is out (0.13, A5)
            if (t.Def.Own)
            {
                long day = (long)Math.Floor(w.Hours / 24);
                if (day != t.CallBackDay)
                {
                    SumHeld(t, w);
                    t.CallBackDay = day;
                    t.DayCallBacks = 0;
                }
                if (++t.DayCallBacks > CallBacksPerDay)
                {
                    t.Held.Add(text);
                    Diag.Count("tool:call-backs held");
                    Outcomes.CallBack(t, "(held, past " + CallBacksPerDay + " today) " + text, w.Hours);
                    return;
                }
            }
            Tell(t, text, w);
        }

        private static void Tell(ToolInstance t, string text, World w)
        {
            Diag.Count("tool:call-backs");
            Brain.ToolNotice(text, t.Order.Party, w.Hours, t.Order.At, t.Owner?.Realm ?? t.Order.Realm ?? w.Realm);
            (t.Owner?.Channel ?? AgentChannel.For(w.Realm)).Event("tool: " + text);
            Outcomes.CallBack(t, text, w.Hours);
        }

        /// <summary>The call-backs an agent's own tool held past a day's limit, in one, once that day is out (A5).</summary>
        private static void SumHeld(ToolInstance t, World w)
        {
            t.CallBackDay = (long)Math.Floor(w.Hours / 24);
            t.DayCallBacks = 0;
            if (t.Held.Count == 0) return;
            int n = t.Held.Count;
            string last = t.Held[n - 1];
            t.Held.Clear();
            Tell(t, t.Order.LordName + "'s " + t.Def.Name + " would have called you back " + n + " more time" + (n == 1 ? "" : "s") + " the day before, past the " + CallBacksPerDay
                    + " a day a tool of yours may call; they were held (each is in its record). The last of them: \"" + last.TrimEnd('.') + ".\"", w);
        }

        private static void Log(ToolInstance t, string from, string to, string why)
        {
            t.Steps++;
            t.LastStep = from + " -> " + to + (why != null ? " (" + why + ")" : "");
            Diag.Count("tool:steps");
            Diag.Event("tool", "step", new { id = t.Id, tool = t.Def.Name, lord = t.Order.Lord, from, to, why, move = t.HasPending ? t.Pending.ToString() : null });
            Outcomes.Step(t, from, to, why, Diag.Hours);
        }

        /// <summary>A value as the agent reads it: names for ids, whole numbers with separators.</summary>
        public static string Show(object v, World w)
        {
            if (v is List<object> list) return list.Count == 0 ? "none" : string.Join(", ", list.Select(x => Show(x, w)));
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
            string doing = t.ReliefOf != null ? "riding to relieve a fight against " + w.Name(t.ReliefTarget) + " (resumes once it is over)"
                         : t.Shelter != null ? (t.Shelter == Away ? "falling back, away from " + w.Name(t.ShelterFrom) : (t.Running ? "falling back toward " : "sheltering in ") + w.Name(t.Shelter) + " from " + w.Name(t.ShelterFrom)) + " (resumes once it has passed)"
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
                // the next orders it carries, as the agent wrote them (0.13.16: Dol Guldur's "Dramthak gather at Caradhras, then besiege
                // [castle_MM7]", Sirannon's id from its ally's joint plan, flagged every report from Nov 1, 16:32)
                for (JObject n = t.Order.Then; n != null; n = n["then"] as JObject)
                {
                    if (n["at"] is JValue at && at.Type == JTokenType.String) yield return (string)at;
                    if (n["target"] is JValue tg && tg.Type == JTokenType.String) yield return (string)tg;
                    if (n["lords"] is JArray ls) foreach (JToken l in ls) if (l.Type == JTokenType.String) yield return (string)l;
                }
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

            /// <summary>The hours ahead a fight counts those who could join it: the order's join_hours (0.12.3).</summary>
            private double JoinHours => JoinHoursOf(_t);

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
                    // map units an hour over a day, night counted, by the game's marching rules (0.11.6: defend's early shelter)
                    case "pace": return me != null ? _w.PerHour(me) : (object)null;
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
                    // 0.13: the order's lords and places as lists
                    case "lords": return _t.LordsList ?? (_t.LordsList = _t.Order.Lords.Cast<object>().ToList());
                    case "places": return _t.PlacesList ?? (_t.PlacesList = _t.Order.Places.Cast<object>().ToList());
                    case "now": return _w.Hours;
                    // 0.13: his money and his loot, the last stop's recruits and sale while this order ran, the move arrived, the day
                    case "gold": return me != null && me.Gold >= 0 ? (double)me.Gold : (object)null;
                    case "loot_value": return me != null ? (double)me.Loot : (object)null;
                    case "prisoners": return me != null ? (double)me.Prisoners : (object)null;
                    case "ransom": return me != null ? (double)me.Ransom : (object)null;
                    case "wage_cap_reached": return me != null && me.WageCap >= 0 && me.Wages >= me.WageCap;
                    case "ranks_full": return me != null && me.SizeLimit > 0 && me.Men >= me.SizeLimit;
                    case "recruited_last": return me != null && !double.IsNaN(me.RecruitedLastHours) && me.RecruitedLastHours >= _t.Started - 0.01 ? (double)me.RecruitedLast : 0.0;
                    case "sold_last": return me != null && !double.IsNaN(me.SaleHours) && me.SaleHours >= _t.Started - 0.01 ? (double)me.SaleGold : 0.0;
                    case "arrived": return Arrived(_t, me, _w);
                    case "clan_gold": { ClanRow c = me?.Clan != null ? _w.S.Clans?.FirstOrDefault(x => x.Id == me.Clan) : null; return c != null && c.Gold >= 0 ? (double)c.Gold : (object)null; }
                    case "hour_of_day": return Math.Floor(((_w.Hours % 24) + 24) % 24);
                    case "day": return Math.Floor(_w.Hours / 24);
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
                    case "within": { double d = _w.Near(_t.Order.Party, Id(a[0])); return !double.IsNaN(d) && d <= Expr.Number(a[1]); }
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
                    // a castle's or town's food (0.13.20): the realm's own only (another's is not known), none for a village
                    case "food_at": { SettlementRow h = H(a[0]); return h != null && h.Faction == _w.Realm && h.Food >= 0 ? (object)(double)h.Food : null; }
                    case "food_days_at":
                        {
                            SettlementRow h = H(a[0]);
                            if (h == null || h.Faction != _w.Realm || h.Food < 0) return null;
                            return h.FoodPerDay > 0 ? (object)Math.Round(h.Food / h.FoodPerDay, 1) : 999.0;
                        }
                    case "raided": return H(a[0])?.VillageState == "BeingRaided";
                    case "looted": return H(a[0])?.VillageState == "Looted";
                    case "ours": return H(a[0])?.Faction == _w.Realm;
                    case "hostile": return _w.HostileFaction(H(a[0])?.Faction);
                    case "friendly": { SettlementRow h = H(a[0]); return h != null && (h.Faction == _w.Realm || _w.Ally(h.Faction)); }
                    case "fort": return World.IsFort(H(a[0]));
                    case "enemies_near": return (double)_w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).Count;
                    case "enemy_strength_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).Sum(World.StrengthOf);
                    case "threat_near": return _w.ThreatStrength(Id(a[0]), Expr.Number(a[1]), JoinHours);
                    case "enemy_threat_near": return _w.EnemyThreat(Id(a[0]), Expr.Number(a[1]));
                    case "other_fort": return _w.OtherFort(Id(a[0]), _t.Order.Party)?.Id;
                    case "refuge": return _w.ChooseRefuge(_t.Order.Party, Id(a[0]), Expr.Number(a[1]), null, out _, out _)?.Id;
                    case "would_hold": { SettlementRow h = H(a[0]); return h != null && World.IsFort(h) && _w.Holds(h, _t.Order.Party, _w.ThreatStrength(h.Id, Expr.Number(a[1]))); }
                    case "strongest_enemy_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).OrderByDescending(World.StrengthOf).FirstOrDefault()?.Id;
                    case "weakest_enemy_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).OrderBy(World.StrengthOf).FirstOrDefault()?.Id;
                    // a party with what it would be fought with: every enemy who could join a fight with it within join_hours (World.Company)
                    case "company": return _w.CompanyStrength(Id(a[0]), JoinHours);
                    case "easiest_enemy_near": return _w.EnemiesNear(Id(a[0]), Expr.Number(a[1])).OrderBy(e => _w.CompanyStrength(e.Id, JoinHours)).FirstOrDefault()?.Id;
                    // our side of a fight there (0.12.4, World.SideStrength): the lord and every lord of ours who could get there within
                    // join_hours, and, at a holding, every one camped in our siege of it
                    case "side_strength":
                        {
                            string id = Id(a[0]);
                            if (me == null || !_w.Where(id, out float sx, out float sy)) return World.StrengthOf(me);
                            SettlementRow sh = _w.Holding(id);
                            if (sh != null && (sh.Gx != 0 || sh.Gy != 0)) { sx = sh.Gx; sy = sh.Gy; }
                            string camp = sh != null && _w.HostileFaction(sh.Faction) ? sh.Id : me.Besieging;
                            return _w.SideStrength(me, sx, sy, JoinHours, camp, Objectives(_t, id == me.Id ? null : id));
                        }
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
                    case "ruler_target": return _w.RulerTarget();
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
                    // ---------- 0.13: lists, tables, time, bandits, towns ----------
                    case "count": return a[0] is List<object> cl ? (double)cl.Count : a[0] is Dictionary<string, object> ct ? (double)ct.Count : (object)null;
                    case "first": return a[0] is List<object> fl && fl.Count > 0 ? fl[0] : null;
                    case "last": return a[0] is List<object> ll && ll.Count > 0 ? ll[ll.Count - 1] : null;
                    case "item":
                        {
                            double n = Expr.Number(a[1]);
                            return a[0] is List<object> il && !double.IsNaN(n) && n >= 0 && n < il.Count ? il[(int)Math.Floor(n)] : null;
                        }
                    case "contains": return a[0] is List<object> hl && hl.Any(x => Same(x, a[1]));
                    case "next":
                        {
                            if (!(a[0] is List<object> nl) || nl.Count == 0) return null;
                            int i = nl.FindIndex(x => Same(x, a[1]));
                            return nl[i < 0 ? 0 : (i + 1) % nl.Count];
                        }
                    case "nearest_of": return a[0] is List<object> ol ? ol.Where(x => x is string).OrderBy(x => Far(_w.Near(_t.Order.Party, (string)x))).FirstOrDefault(x => !double.IsNaN(_w.Near(_t.Order.Party, (string)x))) : null;
                    case "next_due":
                        {
                            // the nearest place of the list not done in the last so many hours (by the table's entries, set to now when done)
                            if (!(a[0] is List<object> dl)) return null;
                            Dictionary<string, object> done = a[1] as Dictionary<string, object>;
                            double hours = Expr.Number(a[2]);
                            if (double.IsNaN(hours)) return null;
                            return dl.Where(x => x is string).Where(x =>
                            {
                                if (done == null || !done.TryGetValue((string)x, out object when)) return true;
                                double h = Expr.Number(when);
                                return double.IsNaN(h) || _w.Hours - h >= hours - 1e-6;
                            }).OrderBy(x => Far(_w.Near(_t.Order.Party, (string)x))).FirstOrDefault();
                        }
                    case "hours_since": { double h = Expr.Number(a[0]); return double.IsNaN(h) ? null : (object)(_w.Hours - h); }
                    case "weakest_bandit_near": return _w.BanditsNear(Id(a[0]), Expr.Number(a[1])).OrderBy(World.StrengthOf).FirstOrDefault()?.Id;
                    case "bandits_near": return (double)_w.BanditsNear(Id(a[0]), Expr.Number(a[1])).Count;
                    case "nearest_town": return _w.NearestTown(_t.Order.Party, true)?.Id;
                    case "nearest_town_of_realm": return _w.NearestTown(_t.Order.Party, false)?.Id;
                }
                throw new InvalidOperationException("no function " + fn);
            }

            /// <summary>Two values the same (ids without regard to case), as the language's == has it.</summary>
            private static bool Same(object x, object y)
            {
                if (x == null || y == null) return x == null && y == null;
                if (x is string sx && y is string sy) return string.Equals(sx, sy, StringComparison.OrdinalIgnoreCase);
                double a = Expr.Number(x), b = Expr.Number(y);
                return !double.IsNaN(a) && !double.IsNaN(b) ? Math.Abs(a - b) < 1e-9 : Equals(x, y);
            }

            /// <summary>A distance to sort by, the unknown last.</summary>
            private static double Far(double d) => double.IsNaN(d) ? double.MaxValue : d;

            private static object Nan(double d) => double.IsNaN(d) ? null : (object)d;
        }
    }
}
