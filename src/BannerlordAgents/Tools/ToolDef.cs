using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
        public readonly List<KeyValuePair<string, Expr>> Set = new List<KeyValuePair<string, Expr>>();
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
    /// the same checker can later read the agent's own. Game-free.
    /// </summary>
    internal sealed class ToolDef
    {
        public string Name, Kind = "tool", About, CallsBack, At, Target, Start;
        public bool Lords, Safety = true;
        public readonly Dictionary<string, SettingDef> Settings = new Dictionary<string, SettingDef>(StringComparer.OrdinalIgnoreCase);
        public readonly List<KeyValuePair<string, Expr>> Init = new List<KeyValuePair<string, Expr>>();
        public readonly Dictionary<string, StateDef> States = new Dictionary<string, StateDef>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Problems = new List<string>();

        /// <summary>The moves a state can make, and what each acts on: a holding, a party, or nothing.</summary>
        public static readonly Dictionary<string, string> Verbs = new Dictionary<string, string>
        {
            ["goto"] = "holding", ["enter"] = "holding", ["near"] = "holding", ["patrol"] = "holding", ["defend"] = "holding",
            ["besiege"] = "holding", ["raid"] = "holding", ["recruit"] = "holding",
            ["engage"] = "party", ["follow"] = "party", ["toward"] = "party", ["join"] = "party",
            ["leave"] = "none", ["hold"] = "none"
        };

        /// <summary>What "at" and "target" may be, as the order is checked against the game.</summary>
        public static readonly string[] AtKinds = { "own_fort", "own_holding", "own_town_or_village", "friendly_town_or_village", "friendly_fort", "friendly_holding", "enemy_fort", "enemy_village", "enemy_holding", "any_holding" };
        public static readonly string[] TargetKinds = { "enemy_lord", "own_lord" };

        /// <summary>Built-in names every tool can read.</summary>
        public static readonly HashSet<string> BuiltIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "me", "strength", "men", "ranks", "wounded", "food_days", "hours_in_state", "hours_running", "in_army", "leads_army",
            "army_lords", "army_coming", "army_complete", "army_joining", "in_settlement", "besieging", "at", "target", "caution", "danger_radius"
        };

        /// <summary>The functions, with how many arguments each takes.</summary>
        public static readonly Dictionary<string, int> Functions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["distance"] = 2, ["hours_to"] = 1, ["inside"] = 1, ["within"] = 2,
            ["besieged"] = 1, ["siege_strength"] = 1, ["inside_strength"] = 1, ["relief_strength"] = 1, ["besieger"] = 1, ["raided"] = 1, ["looted"] = 1,
            ["ours"] = 1, ["hostile"] = 1, ["friendly"] = 1,
            ["enemies_near"] = 2, ["enemy_strength_near"] = 2, ["strongest_enemy_near"] = 2, ["weakest_enemy_near"] = 2, ["nearest_enemy"] = 2, ["army_near"] = 2,
            ["threat_near"] = 2, ["other_fort"] = 1, ["refuge"] = 2, ["would_hold"] = 2,
            ["strength_of"] = 1, ["men_of"] = 1, ["in_view"] = 1, ["seen_ago"] = 1, ["gone"] = 1,
            ["nearest_fort"] = 0, ["nearest_supply"] = 0, ["next_village"] = 2, ["fort"] = 1,
            ["lords_arrived"] = 0, ["lords_count"] = 0, ["min"] = 2, ["max"] = 2, ["pick"] = 3, ["round"] = 1
        };

        public bool Valid => Problems.Count == 0;

        // ---------- reading ----------

        public static ToolDef FromJson(string json, string source)
        {
            var d = new ToolDef();
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception e) { d.Name = source; d.Problems.Add("not valid JSON: " + e.Message); return d; }
            d.Name = ((string)o["tool"] ?? source ?? "").Trim().ToLowerInvariant();
            d.Kind = ((string)o["kind"] ?? "tool").Trim().ToLowerInvariant();
            d.About = (string)o["about"];
            d.CallsBack = (string)o["calls_back"];
            d.At = (string)o["at"];
            d.Target = (string)o["target"];
            d.Lords = (bool?)o["lords"] ?? false;
            d.Safety = (bool?)o["safety"] ?? true;
            d.Start = (string)o["start"];
            if (o["settings"] is JObject set)
                foreach (JProperty p in set.Properties())
                {
                    if (!(p.Value is JArray a) || a.Count < 3) { d.Problems.Add("setting " + p.Name + ": write it as [default, min, max, \"what it is\"]"); continue; }
                    d.Settings[p.Name] = new SettingDef
                    {
                        Name = p.Name, Default = (double)a[0], Min = (double)a[1], Max = (double)a[2], About = a.Count > 3 ? (string)a[3] : null
                    };
                }
            if (o["init"] is JObject init)
                foreach (JProperty p in init.Properties())
                    d.Init.Add(new KeyValuePair<string, Expr>(p.Name, d.Parse((string)p.Value, "init " + p.Name)));
            if (o["states"] is JObject states)
                foreach (JProperty p in states.Properties())
                {
                    if (!(p.Value is JObject so)) { d.Problems.Add("state " + p.Name + " is not an object"); continue; }
                    var st = new StateDef { Name = p.Name, About = (string)so["about"], End = (string)so["end"] };
                    string doText = (string)so["do"];
                    if (doText != null) st.Do = d.ParseAction(doText, "state " + p.Name);
                    if (so["when"] is JArray rules)
                        foreach (JToken t in rules)
                        {
                            if (!(t is JObject r)) continue;
                            var tr = new TransitionDef { IfText = (string)r["if"], Go = (string)r["go"], Poke = (string)r["poke"] };
                            tr.If = d.Parse(tr.IfText, "state " + p.Name + ", rule \"" + tr.IfText + "\"");
                            if (r["set"] is JObject sets)
                                foreach (JProperty sp in sets.Properties())
                                {
                                    tr.Set.Add(new KeyValuePair<string, Expr>(sp.Name, d.Parse((string)sp.Value, "state " + p.Name + ", set " + sp.Name)));
                                    tr.SetText.Add(sp.Name + " = " + (string)sp.Value);
                                }
                            st.When.Add(tr);
                        }
                    d.States[p.Name] = st;
                }
            d.Check();
            return d;
        }

        private Expr Parse(string text, string where)
        {
            if (string.IsNullOrWhiteSpace(text)) { Problems.Add(where + ": empty"); return null; }
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
                a.ArgText = text.Substring(sp + 1).Trim();
                a.Arg = Parse(a.ArgText, where + ", do \"" + text + "\"");
            }
            return a;
        }

        // ---------- the checker ----------

        /// <summary>Everything a tool must get right before it may run: the same checks will read the agent's own tools.</summary>
        private void Check()
        {
            if (string.IsNullOrEmpty(Name)) Problems.Add("the tool has no name");
            if (Kind != "tool" && Kind != "move" && Kind != "army") Problems.Add("kind must be tool, move or army");
            if (At != null && !AtKinds.Contains(At)) Problems.Add("at must be one of " + string.Join(", ", AtKinds));
            if (Target != null && !TargetKinds.Contains(Target)) Problems.Add("target must be one of " + string.Join(", ", TargetKinds));
            foreach (SettingDef s in Settings.Values)
            {
                if (s.Min > s.Max) Problems.Add("setting " + s.Name + ": min is above max");
                if (s.Default < s.Min || s.Default > s.Max) Problems.Add("setting " + s.Name + ": the default is outside its range");
                if (BuiltIns.Contains(s.Name) && s.Name != "caution" && s.Name != "danger_radius") Problems.Add("setting " + s.Name + " hides a built-in name");
            }
            if (Start == null || !States.ContainsKey(Start)) Problems.Add("start must name one of the states");
            var memory = new HashSet<string>(Init.Select(kv => kv.Key), StringComparer.OrdinalIgnoreCase);
            foreach (StateDef st in States.Values)
                foreach (TransitionDef t in st.When)
                    foreach (var kv in t.Set) memory.Add(kv.Key);

            void Names(Expr e, string where)
            {
                e?.Walk(n =>
                {
                    if (!BuiltIns.Contains(n) && !Settings.ContainsKey(n) && !memory.Contains(n) && !(n == "lords" && Lords))
                        Problems.Add(where + ": '" + n + "' is not a setting, a remembered value or a built-in name");
                    if (n == "at" && At == null) Problems.Add(where + ": this tool takes no 'at'");
                    if (n == "target" && Target == null && !memory.Contains("target")) Problems.Add(where + ": this tool takes no 'target'");
                }, (f, n) =>
                {
                    if (!Functions.TryGetValue(f, out int want)) Problems.Add(where + ": there is no function " + f);
                    else if (want != n) Problems.Add(where + ": " + f + " takes " + want + " value" + (want == 1 ? "" : "s") + ", not " + n);
                });
            }

            foreach (var kv in Init) Names(kv.Value, "init " + kv.Key);
            foreach (StateDef st in States.Values)
            {
                string where = "state " + st.Name;
                if (st.End != null && st.End != "free" && st.End != "hold" && st.End != "fail") Problems.Add(where + ": end must be free, hold or fail");
                if (st.Do == null && st.End == null) Problems.Add(where + ": a state needs a move (do) unless it ends the tool");
                if (st.Do != null)
                {
                    if (!Verbs.TryGetValue(st.Do.Verb, out string arg)) Problems.Add(where + ": '" + st.Do.Verb + "' is not a move (moves: " + string.Join(", ", Verbs.Keys) + ")");
                    else if (arg == "none" && st.Do.ArgText != null) Problems.Add(where + ": " + st.Do.Verb + " takes nothing to act on");
                    else if (arg != "none" && st.Do.ArgText == null) Problems.Add(where + ": " + st.Do.Verb + " needs a " + arg);
                    Names(st.Do.Arg, where + ", do");
                }
                foreach (TransitionDef t in st.When)
                {
                    Names(t.If, where + ", rule \"" + t.IfText + "\"");
                    foreach (var kv in t.Set) Names(kv.Value, where + ", set " + kv.Key);
                    if (t.Go == null || !States.ContainsKey(t.Go)) Problems.Add(where + ", rule \"" + t.IfText + "\": go must name one of the states");
                }
            }
        }

        // ---------- for the agent ----------

        public double SettingOrDefault(Dictionary<string, double> given, string name)
            => given != null && given.TryGetValue(name, out double v) ? v : Settings.TryGetValue(name, out SettingDef s) ? s.Default : double.NaN;

        /// <summary>The tool as the agent's library lists it.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            string takes = string.Join(", ", new[]
            {
                At != null ? "at: " + Plain(At) : null,
                Target != null ? "target: " + Plain(Target) : null,
                Lords ? "lords: a list of your lords" : null
            }.Where(x => x != null));
            sb.Append("- **" + Name + "**" + (takes.Length > 0 ? " (" + takes + ")" : "") + ": " + About);
            if (!string.IsNullOrEmpty(CallsBack)) sb.Append(" Calls you back when " + CallsBack + ".");
            var shown = Settings.Values.Where(s => s.Name != "caution" && s.Name != "danger_radius").ToList();
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
        public static readonly SettingDef Caution = new SettingDef { Name = "caution", Default = 1.5, Min = 0, Max = 10, About = "when an enemy lord, army or band of bandits this many times stronger comes close, shelter in the nearest castle or town of yours or an ally's that the lord can reach first and whose garrison, militia and lords, with this one, would hold it against that enemy and everything else near it (the game's own test for whether besiegers should assault); if none would, fall back toward one farther from the enemy and stay outside rather than be trapped (lords camped in a siege the order is striking do not count; a lord inside a place that would not hold leaves it); 0 never shelters" };
        public static readonly SettingDef DangerRadius = new SettingDef { Name = "danger_radius", Default = 8, Min = 2, Max = 30, About = "how close counts as close for caution, in map units" };

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
                        if (d.Safety)
                        {
                            if (!d.Settings.ContainsKey("caution")) d.Settings["caution"] = Caution;
                            if (!d.Settings.ContainsKey("danger_radius")) d.Settings["danger_radius"] = DangerRadius;
                        }
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
            sb.AppendLine("Changing orders:");
            sb.AppendLine("- Give a lord a new order to replace the old one. Lords you do not mention keep their orders.");
            sb.AppendLine("- **adjust** (settings): changes only the numbers of a lord's current tool, keeping its state.");
            sb.AppendLine("- An answer with no orders is a valid answer: everything stands.");
            sb.AppendLine("- **then**: an order can carry a next order, started as soon as the first one finishes, e.g. gather, then relieve. It does not start when the first one fails (an army that broke up before it formed): that lord is set free, and you are told.");
            sb.AppendLine();
            sb.AppendLine("Every move and tool also takes caution (default " + ToolDef.Num(Caution.Default) + ") and danger_radius (default " + ToolDef.Num(DangerRadius.Default)
                          + "): " + Caution.About + ". Lords holding your orders do not flee by themselves, so keep caution on unless you mean them to stand.");
            return sb.ToString();
        }
    }
}
