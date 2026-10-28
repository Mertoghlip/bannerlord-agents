using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents.Tools
{
    /// <summary>One number a tool lets the agent set: its default and the range it is kept to.</summary>
    internal sealed class SettingDef
    {
        public string Name, About;
        public double Default, Min, Max;
    }

    /// <summary>A rule of a state: when the condition holds, go to another state, maybe calling the agent back.</summary>
    internal sealed class TransitionDef
    {
        public string IfText, Go, Poke;
        public Expr If;
        /// <summary>Its place in its state's list, from 1 (the checker's messages name it: "state recruit, rule 2").</summary>
        public int Number;
        /// <summary>What it remembers: the name, and its value; SetAt holds the key of a table's entry ("visited[stop]"), null for a plain name.</summary>
        public readonly List<KeyValuePair<string, Expr>> Set = new List<KeyValuePair<string, Expr>>();
        public readonly List<Expr> SetAt = new List<Expr>();
        public readonly List<string> SetText = new List<string>();
    }

    /// <summary>The move a state makes when it is entered: a verb and what it acts on.</summary>
    internal sealed class ActionDef
    {
        public string Verb, ArgText;
        public Expr Arg;
        public override string ToString() => Verb + (ArgText != null ? " " + ArgText : "");
    }

    internal sealed class StateDef
    {
        public string Name, About, End;
        public ActionDef Do;
        public readonly List<TransitionDef> When = new List<TransitionDef>();
    }

    /// <summary>
    /// A tool of the behavior language (docs/TOOLS.md): a small state machine a lord follows between the agent's
    /// answers. Each state makes one move when it is entered and has ordered rules to leave it; a rule may call the
    /// agent back (a poke) with a sentence. The agent sets the tool's numbers within their ranges. Tools are JSON, so
    /// the same checker reads ours and, since 0.13 (docs/PLAN-0.13.md Part A), the agents' own. Game-free.
    /// </summary>
    internal sealed class ToolDef
    {
        public string Name, Kind = "tool", About, CallsBack, At, Target, Start;
        /// <summary>The kind of holding each of an order's places must be (0.13): a tool that takes a list of places has one.</summary>
        public string Places;
        public bool Lords, Safety = true;
        /// <summary>
        /// Who wrote it (0.13): "ours" for the library that ships with the mod, else the realm whose agent wrote it; its version (1 for
        /// ours; an agent's tool counts up each time it is changed) and the poke or council it was written at.
        /// </summary>
        public string Author = "ours";
        public int Version = 1, WrittenAt = -1;
        /// <summary>An agent's tool used once, for its answer's orders, and not kept (0.13); its fingerprint as written (OwnLibrary).</summary>
        public bool OneOff;
        public string Hash;
        /// <summary>"ours", "v2" for the second version of an agent's own tool, or "one-off".</summary>
        public string VersionText => Author == "ours" ? "ours" : OneOff ? "one-off" : "v" + Version;
        /// <summary>The order words that are not tools (Orders.Plain, the lieutenant's party_ai): no tool of an agent's may take them.</summary>
        public static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "free", "release", "adjust", "add", "disband", "wages", "party_ai", "then" };
        /// <summary>What each thing the tool takes is, in the writer's words ("takes"), shown in the library beside its kind.</summary>
        public readonly Dictionary<string, string> TakesAbout = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>The tool as written (an agent's own, kept whole in the library and the toolbook).</summary>
        public string Json;
        public readonly Dictionary<string, SettingDef> Settings = new Dictionary<string, SettingDef>(StringComparer.OrdinalIgnoreCase);
        public readonly List<KeyValuePair<string, Expr>> Init = new List<KeyValuePair<string, Expr>>();
        public readonly Dictionary<string, StateDef> States = new Dictionary<string, StateDef>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Problems = new List<string>();

        /// <summary>The limits on an agent's own tool (0.13, docs/PLAN-0.13.md A5); ours are not held to them.</summary>
        public const int MaxStates = 12, MaxRules = 40, MaxExpression = 600, MaxSettings = 8;

        /// <summary>The moves a state can make, and what each acts on: a holding, a party, a spot, a number of hours, or nothing.</summary>
        public static readonly Dictionary<string, string> Verbs = new Dictionary<string, string>
        {
            ["goto"] = "holding", ["enter"] = "holding", ["near"] = "holding", ["patrol"] = "holding", ["defend"] = "holding",
            ["besiege"] = "holding", ["raid"] = "holding", ["recruit"] = "holding", ["tour"] = "holding",
            // 0.13: into a town or castle to sell his loot there (the sell order's sale), and to wait where he stands
            ["sell"] = "holding",
            ["engage"] = "party", ["follow"] = "party", ["toward"] = "party", ["join"] = "party", ["shadow"] = "party",
            ["post"] = "spot",
            ["wait"] = "number",
            ["leave"] = "none", ["hold"] = "none", ["recruit_here"] = "none"
        };

        /// <summary>The moves in words, as the instructions and the library list them (0.13).</summary>
        public static readonly Dictionary<string, string> VerbWords = new Dictionary<string, string>
        {
            ["goto"] = "goes to a holding: into your or an ally's, close to anyone else's", ["enter"] = "goes into a holding of yours or an ally's and stays there",
            ["near"] = "goes close to a holding and stands outside it", ["patrol"] = "patrols around a holding (within the tool's radius setting, when it has one)",
            ["defend"] = "goes to defend a holding, the game's way", ["besiege"] = "besieges an enemy castle or town", ["raid"] = "raids an enemy village",
            ["recruit"] = "goes to one town, castle or village and takes its volunteers there, by the recruit order's rules (his gold, his standing, his wage cap, his clan's troop lines past 80% of his ranks); it stays there taking new ones as they come until another move; write it \"recruit at X\" or \"recruit X\"",
            ["recruit_here"] = "takes the volunteers where he stands, the same way", ["tour"] = "the recruit order's tour: from a place onward, place by place",
            ["sell"] = "goes into a town or castle of yours or an ally's and sells his loot there (a town buys loot, a castle does not; prisoners are handed over by the game's rule); write it \"sell at X\"",
            ["engage"] = "attacks a lord, army or band", ["follow"] = "follows a party", ["toward"] = "marches to where a party is, or was last seen",
            ["join"] = "marches to join a lord's army", ["shadow"] = "trails a party at a distance (the tool's keep setting)", ["post"] = "stands at a spot [pX_Y] or a holding's gate, watching",
            ["wait"] = "stays where he is (inside a holding if he is in one) for that many hours; arrived turns true once they have passed; write it \"wait 6\"",
            ["leave"] = "leaves the holding he is in", ["hold"] = "stands where he is"
        };

        /// <summary>What "at", "target" and "places" may be, as the order is checked against the game.</summary>
        public static readonly string[] AtKinds = { "own_fort", "own_holding", "own_town_or_village", "friendly_town_or_village", "friendly_fort", "friendly_holding", "enemy_fort", "enemy_village", "enemy_holding", "any_holding", "spot_or_holding" };
        public static readonly string[] TargetKinds = { "enemy_lord", "own_lord" };

        /// <summary>Built-in names every tool can read ("lords" and "places" only a tool that takes them).</summary>
        public static readonly HashSet<string> BuiltIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "me", "strength", "men", "ranks", "wounded", "food_days", "pace", "hours_in_state", "hours_running", "in_army", "leads_army",
            "army_lords", "army_coming", "army_complete", "army_joining", "in_settlement", "besieging", "in_battle", "at", "target", "caution", "danger_radius", "now",
            // 0.13: his money and his loot, the last stop's recruits and sale, whether the move has arrived, the hour of the day and the day
            "gold", "loot_value", "prisoners", "ransom", "wage_cap_reached", "ranks_full", "recruited_last", "sold_last", "arrived", "clan_gold", "hour_of_day", "day"
        };

        /// <summary>The functions, with how many values each takes.</summary>
        public static readonly Dictionary<string, int> Functions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["distance"] = 2, ["hours_to"] = 1, ["inside"] = 1, ["within"] = 2,
            ["besieged"] = 1, ["siege_strength"] = 1, ["inside_strength"] = 1, ["relief_strength"] = 1, ["besieger"] = 1, ["raided"] = 1, ["looted"] = 1,
            ["ours"] = 1, ["hostile"] = 1, ["friendly"] = 1,
            ["enemies_near"] = 2, ["enemy_strength_near"] = 2, ["strongest_enemy_near"] = 2, ["weakest_enemy_near"] = 2, ["nearest_enemy"] = 2, ["army_near"] = 2,
            ["company"] = 1, ["easiest_enemy_near"] = 2, ["side_strength"] = 1,
            ["threat_near"] = 2, ["enemy_threat_near"] = 2, ["other_fort"] = 1, ["refuge"] = 2, ["would_hold"] = 2,
            ["strength_of"] = 1, ["men_of"] = 1, ["in_view"] = 1, ["seen_ago"] = 1, ["gone"] = 1,
            ["nearest_fort"] = 0, ["nearest_supply"] = 0, ["next_village"] = 2, ["fort"] = 1,
            ["lords_arrived"] = 0, ["lords_count"] = 0, ["min"] = 2, ["max"] = 2, ["pick"] = 3, ["round"] = 1,
            ["doing_of"] = 1, ["toward_of"] = 1, ["where_of"] = 1,
            ["relief_coming"] = 2, ["relief_count"] = 2, ["relief_of"] = 2, ["relief_within"] = 3, ["prep_hours"] = 1, ["storm_text"] = 1,
            ["sale_text"] = 1,
            // 0.13: lists, time, bandits, towns
            ["count"] = 1, ["first"] = 1, ["last"] = 1, ["item"] = 2, ["contains"] = 2, ["next"] = 2, ["nearest_of"] = 1, ["next_due"] = 3,
            ["hours_since"] = 1, ["weakest_bandit_near"] = 2, ["bandits_near"] = 2, ["nearest_town"] = 0, ["nearest_town_of_realm"] = 0
        };

        /// <summary>The functions in words, with what they take, for the instructions' and the library's account of the language (0.13).</summary>
        public static readonly Dictionary<string, string> FunctionWords = new Dictionary<string, string>
        {
            ["count"] = "count(list): how many items", ["first"] = "first(list): its first item, none when empty", ["last"] = "last(list): its last item",
            ["item"] = "item(list, n): the item at position n, counting from 0 (list[n] is the same)", ["contains"] = "contains(list, x): whether x is in it",
            ["next"] = "next(list, x): the item after x, going round to the first after the last (the first when x is none or not in it)",
            ["nearest_of"] = "nearest_of(list): the item nearest the lord", ["next_due"] = "next_due(places, table, hours): the place of the list nearest the lord whose entry in the table is none or at least that many hours old (none when every one is done)",
            ["hours_since"] = "hours_since(x): hours from the hour x (a remembered now) to now, none when x is none",
            ["weakest_bandit_near"] = "weakest_bandit_near(place, r): the weakest band of bandits in view within r of a place", ["bandits_near"] = "bandits_near(place, r): how many bands of bandits are in view within r",
            ["nearest_town"] = "nearest_town(): the nearest town of yours or an ally's not under siege", ["nearest_town_of_realm"] = "nearest_town_of_realm(): the nearest town of your own, not under siege"
        };

        private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);

        public bool Valid => Problems.Count == 0;

        /// <summary>One of the agents' own (0.13), not ours.</summary>
        public bool Own => Author != "ours";

        // ---------- reading ----------

        public static ToolDef FromJson(string json, string source) => FromJson(json, source, "ours");

        /// <summary>A tool from its JSON (author: "ours", or the realm whose agent wrote it: its tools are held to the limits).</summary>
        public static ToolDef FromJson(string json, string source, string author)
        {
            var d = new ToolDef { Author = author ?? "ours", Json = json };
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception e) { d.Name = source; d.Problems.Add("not valid JSON: " + e.Message); return d; }
            d.Name = ((string)o["tool"] ?? (string)o["name"] ?? source ?? "").Trim().ToLowerInvariant();
            d.Kind = ((string)o["kind"] ?? "tool").Trim().ToLowerInvariant();
            d.About = (string)o["about"];
            d.CallsBack = (string)o["calls_back"];
            d.At = (string)o["at"];
            d.Target = (string)o["target"];
            d.Lords = (bool?)o["lords"] ?? false;
            d.Places = (string)o["places"];
            d.Safety = (bool?)o["safety"] ?? true;
            d.Start = (string)o["start"];
            // what the tool takes, each with its kind and the writer's own words: "places": "friendly_holding: the towns to recruit at" (0.13)
            if (o["takes"] is JObject takes)
                foreach (JProperty p in takes.Properties())
                {
                    string kind = null, about = null;
                    if (p.Value is JObject spec) { kind = (string)spec["kind"]; about = (string)spec["about"]; }
                    else if (p.Value.Type == JTokenType.String)
                    {
                        string text = ((string)p.Value).Trim();
                        int colon = text.IndexOf(':');
                        string head = (colon > 0 ? text.Substring(0, colon) : text).Trim();
                        if (AtKinds.Contains(head) || TargetKinds.Contains(head) || head == "own_lords") { kind = head; about = colon > 0 ? text.Substring(colon + 1).Trim() : null; }
                        else about = text;
                    }
                    else if (p.Value.Type == JTokenType.Boolean && p.Name == "lords" && (bool)p.Value) kind = "own_lords";
                    string what = p.Name.Trim().ToLowerInvariant();
                    if (about != null) d.TakesAbout[what] = about;
                    switch (what)
                    {
                        case "at": if (kind != null) d.At = kind; else d.Problems.Add(TakesProblem("at", AtKinds)); break;
                        case "target": if (kind != null) d.Target = kind; else d.Problems.Add(TakesProblem("target", TargetKinds)); break;
                        case "places": if (kind != null) d.Places = kind; else d.Problems.Add(TakesProblem("places", AtKinds)); break;
                        case "lords": d.Lords = true; break;
                        default: d.Problems.Add("takes " + p.Name + ": a tool takes at (a holding), target (a lord), places (a list of holdings) or lords (a list of your lords)"); break;
                    }
                }
            if (o["settings"] is JObject set)
                foreach (JProperty p in set.Properties())
                {
                    if (!(p.Value is JArray a) || a.Count < 3 || !Numeric(a[0]) || !Numeric(a[1]) || !Numeric(a[2])) { d.Problems.Add("setting " + p.Name + ": write it as [default, min, max, \"what it is\"]"); continue; }
                    d.Settings[p.Name] = new SettingDef
                    {
                        Name = p.Name, Default = (double)a[0], Min = (double)a[1], Max = (double)a[2], About = a.Count > 3 ? (string)a[3] : null
                    };
                }
            if (o["init"] is JObject init)
                foreach (JProperty p in init.Properties())
                    d.Init.Add(new KeyValuePair<string, Expr>(p.Name, d.Parse(Text(p.Value), "init " + p.Name)));
            if (o["states"] is JObject states)
                foreach (JProperty p in states.Properties())
                {
                    if (!(p.Value is JObject so)) { d.Problems.Add("state " + p.Name + " is not an object"); continue; }
                    var st = new StateDef { Name = p.Name, About = (string)so["about"], End = (string)so["end"] };
                    string doText = (string)so["do"];
                    if (doText != null) st.Do = d.ParseAction(doText, "state " + p.Name);
                    if (so["when"] is JArray rules)
                    {
                        int n = 0;
                        foreach (JToken t in rules)
                        {
                            n++;
                            if (!(t is JObject r)) { d.Problems.Add("state " + p.Name + ", rule " + n + ": write it as {\"if\": ..., \"go\": ...}"); continue; }
                            var tr = new TransitionDef { IfText = Text(r["if"]), Go = (string)r["go"], Poke = (string)r["poke"], Number = n };
                            string where = "state " + p.Name + ", rule " + n;
                            tr.If = d.Parse(tr.IfText, where + " (\"" + tr.IfText + "\")");
                            if (r["set"] is JObject sets)
                                foreach (JProperty sp in sets.Properties())
                                {
                                    // "visited[stop]": a table's entry, keyed by what is between the brackets (0.13)
                                    string name = sp.Name.Trim();
                                    Expr at = null;
                                    int b = name.IndexOf('[');
                                    if (b > 0 && name.EndsWith("]", StringComparison.Ordinal))
                                    {
                                        at = d.Parse(name.Substring(b + 1, name.Length - b - 2), where + ", set " + name);
                                        name = name.Substring(0, b).Trim();
                                    }
                                    tr.Set.Add(new KeyValuePair<string, Expr>(name, d.Parse(Text(sp.Value), where + ", set " + sp.Name)));
                                    tr.SetAt.Add(at);
                                    tr.SetText.Add(sp.Name + " = " + Text(sp.Value));
                                }
                            st.When.Add(tr);
                        }
                    }
                    d.States[p.Name] = st;
                }
            d.Check(o);
            // the safety rule's numbers, on every tool that carries it: ours and, since 0.13, the agents' own
            if (d.Safety)
            {
                if (!d.Settings.ContainsKey("caution")) d.Settings["caution"] = Library.Caution;
                if (!d.Settings.ContainsKey("danger_radius")) d.Settings["danger_radius"] = Library.DangerRadius;
                if (!d.Settings.ContainsKey("join_hours")) d.Settings["join_hours"] = Library.JoinHours;
            }
            return d;
        }

        private static bool Numeric(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);

        /// <summary>An expression as written: a string, or a number or true/false written bare.</summary>
        private static string Text(JToken t) => t == null || t.Type == JTokenType.Null ? null : t.Type == JTokenType.String ? (string)t : t.ToString(Newtonsoft.Json.Formatting.None).ToLowerInvariant();

        private static string TakesProblem(string what, string[] kinds) =>
            "takes " + what + ": say what kind it is first, one of " + string.Join(", ", kinds) + " (for example \"" + kinds[0] + ": what it is for\")";

        private Expr Parse(string text, string where)
        {
            if (string.IsNullOrWhiteSpace(text)) { Problems.Add(where + ": empty"); return null; }
            if (Own && text.Length > MaxExpression) Problems.Add(where + ": " + text.Length + " characters, more than the " + MaxExpression + " an expression may have");
            try { return Expr.Parse(text); }
            catch (FormatException e) { Problems.Add(where + ": " + e.Message); return null; }
        }

        private ActionDef ParseAction(string text, string where)
        {
            text = text.Trim();
            int sp = text.IndexOf(' ');
            var a = new ActionDef { Verb = (sp < 0 ? text : text.Substring(0, sp)).ToLowerInvariant() };
            if (sp > 0)
            {
                string arg = text.Substring(sp + 1).Trim();
                // "recruit at stop", "sell at nearest_town()" (0.13): "at" is a word of the move when more follows it ("enter at" is the order's place)
                if (arg.StartsWith("at ", StringComparison.Ordinal) && arg.Length > 3)
                {
                    string rest = arg.Substring(3).Trim();
                    try { Expr.Parse(rest); arg = rest; }
                    catch (FormatException) { }
                }
                a.ArgText = arg;
                a.Arg = Parse(a.ArgText, where + ", do \"" + text + "\"");
            }
            return a;
        }

        // ---------- the checker ----------

        /// <summary>Everything a tool must get right before it may run: the same checks read the agents' own tools, with their limits.</summary>
        private void Check(JObject o)
        {
            if (string.IsNullOrEmpty(Name)) Problems.Add("the tool has no name");
            else if (Own && !NamePattern.IsMatch(Name)) Problems.Add("its name, '" + Name + "', must be 2 to 32 small letters, digits and underscores, starting with a letter");
            else if (Own && (Library.Get(Name) != null || Reserved.Contains(Name))) Problems.Add("'" + Name + "' is the name of one of our orders, which stay as they are; give yours a name of its own");
            if (Kind != "tool" && Kind != "move" && Kind != "army") Problems.Add("kind must be tool, move or army");
            if (Own && Kind == "army") Problems.Add("an army's tools are ours only (gather); write a tool or a move, and order an army's leader");
            if (Own && o?["safety"] != null && !Safety) Problems.Add("a tool of your own always carries the safety and food rules (caution 0 on an order switches the safety rule off for it)");
            if (At != null && !AtKinds.Contains(At)) Problems.Add("at must be one of " + string.Join(", ", AtKinds));
            if (Target != null && !TargetKinds.Contains(Target)) Problems.Add("target must be one of " + string.Join(", ", TargetKinds));
            if (Places != null && (!AtKinds.Contains(Places) || Places == "spot_or_holding")) Problems.Add("places must be one of " + string.Join(", ", AtKinds.Where(k => k != "spot_or_holding")));
            foreach (SettingDef s in Settings.Values)
            {
                if (s.Min > s.Max) Problems.Add("setting " + s.Name + ": min is above max");
                if (s.Default < s.Min || s.Default > s.Max) Problems.Add("setting " + s.Name + ": the default is outside its range");
                if (BuiltIns.Contains(s.Name) && s.Name != "caution" && s.Name != "danger_radius") Problems.Add("setting " + s.Name + " hides a built-in name");
                if (Functions.ContainsKey(s.Name)) Problems.Add("setting " + s.Name + " has the name of a function");
            }
            if (Own)
            {
                int own = Settings.Keys.Count(k => k != "caution" && k != "danger_radius" && k != "join_hours");
                if (own > MaxSettings) Problems.Add(own + " settings, more than the " + MaxSettings + " a tool of your own may have");
                if (States.Count > MaxStates) Problems.Add(States.Count + " states, more than the " + MaxStates + " a tool of your own may have");
                int rules = States.Values.Sum(st => st.When.Count);
                if (rules > MaxRules) Problems.Add(rules + " rules in all, more than the " + MaxRules + " a tool of your own may have");
            }
            if (Start == null || !States.ContainsKey(Start)) Problems.Add("start must name one of the states");
            var memory = new HashSet<string>(Init.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
            foreach (StateDef st in States.Values)
                foreach (TransitionDef t in st.When)
                    foreach (var kv in t.Set) memory.Add(kv.Key);
            foreach (string m in memory)
            {
                if (BuiltIns.Contains(m) && m != "target") Problems.Add("'" + m + "' is a built-in name; remember it under another name");
                else if (Settings.ContainsKey(m)) Problems.Add("'" + m + "' is a setting; remember it under another name");
            }

            void Names(Expr e, string where)
            {
                e?.Walk(n =>
                {
                    if (!BuiltIns.Contains(n) && !Settings.ContainsKey(n) && !memory.Contains(n) && !(n == "lords" && Lords) && !(n == "places" && Places != null))
                        Problems.Add(where + ": '" + n + "' is not a setting, a remembered value or a built-in name" + (n == "places" ? " (this tool takes no places)" : n == "lords" ? " (this tool takes no lords)" : ""));
                    if (n == "at" && At == null) Problems.Add(where + ": this tool takes no 'at'");
                    if (n == "target" && Target == null && !memory.Contains("target")) Problems.Add(where + ": this tool takes no 'target'");
                }, (f, n) =>
                {
                    if (!Functions.TryGetValue(f, out int want)) Problems.Add(where + ": there is no function " + f);
                    else if (want != n) Problems.Add(where + ": " + f + " takes " + want + " value" + (want == 1 ? "" : "s") + ", given " + n);
                });
            }

            foreach (var kv in Init) Names(kv.Value, "init " + kv.Key);
            foreach (StateDef st in States.Values)
            {
                string where = "state " + st.Name;
                if (st.End != null && st.End != "free" && st.End != "hold" && st.End != "fail" && st.End != "stop") Problems.Add(where + ": end must be free, hold, fail or stop");
                // a state with no move only decides (0.13): the lord keeps the move he has; with no rules either, he would stand there for ever
                if (st.Do == null && st.End == null && st.When.Count == 0) Problems.Add(where + ": it has no move (do), no end and no rules, so a lord in it would never leave it");
                if (st.Do != null)
                {
                    if (!Verbs.TryGetValue(st.Do.Verb, out string arg)) Problems.Add(where + ": '" + st.Do.Verb + "' is not a move (moves: " + string.Join(", ", Verbs.Keys) + ")");
                    else if (arg == "none" && st.Do.ArgText != null) Problems.Add(where + ": " + st.Do.Verb + " takes nothing to act on");
                    else if (arg != "none" && st.Do.ArgText == null) Problems.Add(where + ": " + st.Do.Verb + " needs " + (arg == "number" ? "a number of hours" : "a " + arg));
                    Names(st.Do.Arg, where + ", do");
                }
                foreach (TransitionDef t in st.When)
                {
                    string rule = where + ", rule " + t.Number + " (\"" + t.IfText + "\")";
                    Names(t.If, rule);
                    for (int i = 0; i < t.Set.Count; i++)
                    {
                        Names(t.Set[i].Value, rule + ", set " + t.Set[i].Key);
                        Names(t.SetAt[i], rule + ", set " + t.Set[i].Key + "[...]");
                    }
                    if (t.Go == null || !States.ContainsKey(t.Go)) Problems.Add(rule + ": go must name one of the states");
                }
            }
            // every state reachable from the start (0.13): a state no rule leads to is a mistake
            if (Start != null && States.ContainsKey(Start))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Start };
                var todo = new Queue<string>(new[] { Start });
                while (todo.Count > 0)
                    foreach (TransitionDef t in States[todo.Dequeue()].When)
                        if (t.Go != null && States.ContainsKey(t.Go) && seen.Add(t.Go)) todo.Enqueue(t.Go);
                foreach (StateDef st in States.Values)
                    if (!seen.Contains(st.Name)) Problems.Add("state " + st.Name + " can never be reached from the start (" + Start + "): no rule goes to it");
            }
        }

        // ---------- for the agent ----------

        public double SettingOrDefault(Dictionary<string, double> given, string name)
            => given != null && given.TryGetValue(name, out double v) ? v : Settings.TryGetValue(name, out SettingDef s) ? s.Default : double.NaN;

        /// <summary>The tool as the agent's library lists it.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            string Words(string what, string plain) => TakesAbout.TryGetValue(what, out string about) && !string.IsNullOrWhiteSpace(about) ? plain + ", " + about : plain;
            string takes = string.Join(", ", new[]
            {
                At != null ? "at: " + Words("at", Plain(At)) : null,
                Target != null ? "target: " + Words("target", Plain(Target)) : null,
                Places != null ? "places: " + Words("places", "a list, each " + Plain(Places)) : null,
                Lords ? "lords: " + Words("lords", "a list of your lords") : null
            }.Where(x => x != null));
            sb.Append("- **" + Name + "**" + (Own ? " (yours, " + VersionText + ")" : "") + (takes.Length > 0 ? " (" + takes + ")" : "") + ": " + About);
            if (!string.IsNullOrEmpty(CallsBack)) sb.Append(" Calls you back when " + CallsBack + ".");
            var shown = Settings.Values.Where(s => s.Name != "caution" && s.Name != "danger_radius" && s.Name != "join_hours").ToList();
            if (shown.Count > 0)
                sb.Append(" Settings: " + string.Join("; ", shown.Select(s => s.Name + " " + Num(s.Default) + " (" + Num(s.Min) + " to " + Num(s.Max) + ")" + (s.About != null ? ": " + s.About : ""))) + ".");
            return sb.ToString();
        }

        public static string Plain(string kind)
        {
            switch (kind)
            {
                case "own_fort": return "one of your castles or towns";
                case "own_holding": return "one of your holdings";
                case "own_town_or_village": return "one of your towns or villages";
                case "friendly_town_or_village": return "your or an ally's town or village";
                case "friendly_fort": return "your or an ally's castle or town";
                case "friendly_holding": return "your or an ally's holding";
                case "enemy_fort": return "an enemy castle or town";
                case "enemy_village": return "an enemy village";
                case "enemy_holding": return "an enemy holding";
                case "any_holding": return "any holding";
                case "spot_or_holding": return "a spot [pX_Y] named in an \"ask the way\" answer (a crossing or a point on a way), or any holding";
                case "enemy_lord": return "an enemy lord or army";
                case "own_lord": return "one of your lords";
                default: return kind;
            }
        }

        public static string Num(double d) => Math.Abs(d - Math.Round(d)) < 1e-9 ? Math.Round(d).ToString("0", CultureInfo.InvariantCulture) : d.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>The tools that ship with the mod (Tools/Library/*.json, built into the DLL), checked when first used.</summary>
    internal static class Library
    {
        private static Dictionary<string, ToolDef> _all;
        private static readonly object Gate = new object();

        /// <summary>The safety rule every tool and move carries unless it opts out.</summary>
        public static readonly SettingDef Caution = new SettingDef { Name = "caution", Default = 1.5, Min = 0, Max = 10, About = "when anything hostile comes close to the lord, so a fight may start where he is, and the enemy lords, armies and bandits close to him, with every enemy lord and army in view that could reach him within join_hours, are this many times stronger together, shelter in the nearest castle or town of yours or an ally's that the lord can reach first and whose garrison, militia and lords, with this one, would hold it against them and everything else near it (the game's own test for whether besiegers should assault); if none would, fall back toward one farther from the enemy and stay outside rather than be trapped (lords camped in a siege the order is striking do not count; a lord inside a place that would not hold leaves it); 0 never shelters" };
        public static readonly SettingDef DangerRadius = new SettingDef { Name = "danger_radius", Default = 8, Min = 2, Max = 30, About = "how close counts as close for caution, in map units" };
        /// <summary>The hours window of a fight (0.12.3; Mert chose "Hours window only"): who could join it, by the agent's own reckoning of how long it lasts.</summary>
        public static readonly SettingDef JoinHours = new SettingDef { Name = "join_hours", Default = 8, Min = 0, Max = 96, About = "how many hours ahead the order's fights count the enemy lords and armies in view that could join them: every one that could march to the fight within this many hours, at its own pace as the crow flies, counts with it, for caution (marching to the lord) and for a strike, a hunt or the threat near a holding (marching to the target or the holding); 0 counts only those already close. The engagement question says how long a fight would last and who could reach it in time" };

        public static Dictionary<string, ToolDef> All
        {
            get
            {
                lock (Gate)
                {
                    if (_all != null) return _all;
                    var all = new Dictionary<string, ToolDef>(StringComparer.OrdinalIgnoreCase);
                    Assembly asm = typeof(Library).Assembly;
                    foreach (string res in asm.GetManifestResourceNames().Where(r => r.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && r.Contains(".Tools.Library.")).OrderBy(r => r))
                    {
                        string json;
                        using (Stream s = asm.GetManifestResourceStream(res))
                        using (var sr = new StreamReader(s, Encoding.UTF8)) json = sr.ReadToEnd();
                        ToolDef d = ToolDef.FromJson(json, Path.GetFileNameWithoutExtension(res));
                        all[d.Name] = d;
                    }
                    return _all = all;
                }
            }
        }

        public static ToolDef Get(string name) => name != null && All.TryGetValue(name.Trim(), out ToolDef d) ? d : null;

        /// <summary>Every problem the checker found in the library (should be none).</summary>
        public static List<string> Problems() => All.Values.SelectMany(d => d.Problems.Select(p => d.Name + ": " + p)).ToList();

        /// <summary>The agent's library of orders, grouped as it reads them (written to tools.txt in the channel).</summary>
        public static string Text()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ORDERS YOU CAN GIVE");
            sb.AppendLine();
            sb.AppendLine("Moves (one step; when it is done, the lord stops or goes back to the game's own AI):");
            foreach (ToolDef d in All.Values.Where(x => x.Kind == "move").OrderBy(x => x.Name)) sb.AppendLine(d.Describe());
            sb.AppendLine("- **free**: the lord goes back to the game's own AI.");
            sb.AppendLine();
            sb.AppendLine("Tools (they keep a lord acting between your answers, and call you back when they need you):");
            foreach (ToolDef d in All.Values.Where(x => x.Kind == "tool").OrderBy(x => x.Name)) sb.AppendLine(d.Describe());
            sb.AppendLine();
            sb.AppendLine("Armies (order the army's leader; the leader's moves and tools move the whole army):");
            foreach (ToolDef d in All.Values.Where(x => x.Kind == "army").OrderBy(x => x.Name)) sb.AppendLine(d.Describe());
            sb.AppendLine("- **add** (lords): calls more lords into the leader's army, at the game's influence price. Lords too far away to be called yet are sent marching to join it (a join order each).");
            sb.AppendLine("- **release** (lords): lets lords go from the leader's army; they go back to the game's own AI. Without lords, release is the same as free.");
            sb.AppendLine("- **disband**: breaks up the leader's army.");
            sb.AppendLine("- The game's own rules still hold: an army left idle (holding, or waiting at a friendly place) for about two days breaks up, and so does one whose cohesion runs out.");
            sb.AppendLine();
            sb.AppendLine("Money:");
            sb.AppendLine("- **wages** (cap, or target \"none\" or \"game\"; lords): the most this lord's party may pay in wages a day, which decides whether he may recruit: a lord at his cap cannot, whatever his gold. "
                          + "cap: a number of denars a day; target \"none\": no cap; target \"game\": the game's own cap again, which it sets each day from the gold of his clan's leader. "
                          + "With lords, the same for those lords too. It does not pay anyone: each lord still pays his men from his own purse. Your cap stands until you change it or the campaign is loaded again.");
            sb.AppendLine("- **sell** (above, a tool): sends a lord to cash in his loot and prisoners.");
            sb.AppendLine();
            sb.AppendLine("Changing orders:");
            sb.AppendLine("- Give a lord a new order to replace the old one. Lords you do not mention keep their orders.");
            sb.AppendLine("- **adjust** (settings): changes only the numbers of a lord's current tool, keeping its state.");
            sb.AppendLine("- An answer with no orders is a valid answer: everything stands.");
            sb.AppendLine("- **then**: an order can carry a next order, started as soon as the first one finishes, e.g. gather, then relieve. It does not start when the first one fails (an army that broke up before it formed): that lord is set free, and you are told.");
            sb.AppendLine();
            sb.AppendLine("Every move and tool also takes caution (default " + ToolDef.Num(Caution.Default) + ") and danger_radius (default " + ToolDef.Num(DangerRadius.Default)
                          + "): " + Caution.About + ". Lords holding your orders do not flee by themselves, so keep caution on unless you mean them to stand.");
            sb.AppendLine("And join_hours (default " + ToolDef.Num(JoinHours.Default) + "): " + JoinHours.About + ".");
            return sb.ToString();
        }
    }
}
