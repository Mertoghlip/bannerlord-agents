using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>What a news line is about, in facts a wake rule can read (0.15): set where the line is made. Game-free.</summary>
    internal sealed class LineFacts
    {
        public double Strength = double.NaN, Men = double.NaN, OwnNear = double.NaN;
        /// <summary>What an enemy is doing, in one word (WakeRules.DoingOf).</summary>
        public string Doing;
        public string Realm, Near;
        public bool Army, Urgent;
    }

    /// <summary>
    /// The wake rule (0.15, docs/reports/2026-11-06-news-wake-study.md): the game sorts each line of news before any model call. The rules are data
    /// (Wake\wake-rules.json), read in order, written in the behavior language over a line's facts. Agents do not change them yet. Game-free; worker.
    /// </summary>
    internal static class WakeRules
    {
        public const string Now = "now", Wait = "wait", Digest = "digest";
        /// <summary>A map distance within which the realm's strength counts as near a raider on its way, as the study measured it.</summary>
        public const float Reach = 40f;

        internal sealed class Rule
        {
            public string Name, Lane, IfText;
            public bool Locked;
            public HashSet<string> Kinds = new HashSet<string>(StringComparer.Ordinal);
            public Expr If;
            public Dictionary<string, double> Settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly string[] FactNames = { "strength", "men", "own_near", "urgent", "army", "chasing", "inside", "besieging", "raiding", "on_way_to_raid", "fighting" };

        private static readonly List<KeyValuePair<string, Regex>> Sorts = new List<KeyValuePair<string, Regex>>();
        private static readonly List<Rule> List = new List<Rule>();
        private static readonly List<string> Wrong = new List<string>();
        public static double WaitHours { get; private set; } = 6;
        public static IReadOnlyList<Rule> Rules => List;
        /// <summary>What is wrong with the rule list (the smoke test and the self-check): empty when it reads.</summary>
        public static IReadOnlyList<string> Problems => Wrong;

        static WakeRules()
        {
            try
            {
                Assembly a = typeof(WakeRules).Assembly;
                string name = a.GetManifestResourceNames().First(n => n.EndsWith("wake-rules.json", StringComparison.OrdinalIgnoreCase));
                using (var r = new StreamReader(a.GetManifestResourceStream(name))) Load(JObject.Parse(r.ReadToEnd()));
            }
            catch (Exception e) { Wrong.Add("the rule list could not be read: " + e.Message); }
        }

        private static void Load(JObject o)
        {
            WaitHours = (double?)o["wait_hours"] ?? 6;
            foreach (JArray s in (o["sorts"] as JArray ?? new JArray()).OfType<JArray>())
                Sorts.Add(new KeyValuePair<string, Regex>((string)s[0], new Regex((string)s[1], RegexOptions.Compiled | RegexOptions.CultureInvariant)));
            foreach (JObject j in (o["rules"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var rule = new Rule { Name = (string)j["name"], Lane = (string)j["lane"], IfText = (string)j["if"], Locked = (bool?)j["locked"] ?? false };
                if (rule.Lane != Now && rule.Lane != Wait && rule.Lane != Digest) Wrong.Add(rule.Name + ": no such lane as " + rule.Lane);
                foreach (JToken k in j["kinds"] as JArray ?? new JArray()) rule.Kinds.Add((string)k);
                if (j["settings"] is JObject set) foreach (JProperty p in set.Properties()) rule.Settings[p.Name] = (double)p.Value;
                if (rule.IfText != null)
                {
                    try
                    {
                        rule.If = Expr.Parse(rule.IfText);
                        rule.If.Walk(n => { if (!FactNames.Contains(n) && !rule.Settings.ContainsKey(n)) Wrong.Add(rule.Name + ": no such fact as " + n); },
                                     (fn, _) => Wrong.Add(rule.Name + ": no such function as " + fn));
                    }
                    catch (FormatException e) { Wrong.Add(rule.Name + ": " + e.Message); }
                }
                List.Add(rule);
            }
        }

        /// <summary>A line's kind: its notice's kind, or for a sighting, a tool's words, a battle and an ended order the finer kind the study sorted by.</summary>
        public static string KindOf(Trigger t)
        {
            string text = t.Text ?? "";
            switch (t.Kind)
            {
                case "tool":
                    foreach (KeyValuePair<string, Regex> s in Sorts) if (s.Value.IsMatch(text)) return s.Key;
                    return "tool_other";
                case "enemy_in_view": return text.StartsWith("Enemy army", StringComparison.Ordinal) ? "enemy_army" : "enemy_lord";
                case "order_ended":
                    if (text.Contains("taken prisoner")) return "ended_captured";
                    if (text.Contains("handed over")) return "ended_supply";
                    if (text.Contains("wages") || text.Contains("too little")) return "ended_recruit_stop";
                    if (Regex.IsMatch(text.Length > 120 ? text.Substring(0, 120) : text, "recruit")) return "ended_recruit";
                    return "ended_other";
                case "battle": return text.StartsWith("Fight over a raid", StringComparison.Ordinal) ? "battle_raid" : "battle";
                default: return t.Kind;
            }
        }

        /// <summary>What an enemy is doing, in one word, from the words its line uses (the study's own sorting).</summary>
        public static string DoingOf(string doing)
        {
            string d = doing ?? "";
            if (d.Contains("raid")) return d.Contains("way") ? "on_way_to_raid" : "raiding";
            if (d.Contains("besieg")) return "besieging";
            if (d.StartsWith("chasing", StringComparison.Ordinal)) return "chasing";
            if (d.StartsWith("going to", StringComparison.Ordinal) || d.StartsWith("marching to", StringComparison.Ordinal)) return "going";
            if (d.StartsWith("fighting", StringComparison.Ordinal)) return "fighting";
            if (d.Contains("on the move")) return "moving";
            if (d.Contains("patrol") || d.Contains("holding") || d.Contains("defending")) return "patrolling";
            if (d.Contains("inside") || d.StartsWith("resting", StringComparison.Ordinal) || d.StartsWith("in ", StringComparison.Ordinal)) return "inside";
            return "other";
        }

        /// <summary>Gives a line its kind, its lane and the rule that gave it. A line no rule matches wakes the agent.</summary>
        public static void Sort(Trigger t)
        {
            t.Sub = KindOf(t);
            t.Lane = Now;
            t.Rule = null;
            var scope = new Scope(t.Facts);
            foreach (Rule r in List)
            {
                if (!r.Kinds.Contains(t.Sub)) continue;
                scope.Settings = r.Settings;
                if (r.If != null && !Expr.Truthy(r.If.Eval(scope))) continue;
                t.Lane = r.Lane;
                t.Rule = r.Name;
                return;
            }
        }

        private sealed class Scope : IScope
        {
            private readonly LineFacts _f;
            public Dictionary<string, double> Settings;
            public Scope(LineFacts f) { _f = f ?? new LineFacts(); }

            public object Get(string name)
            {
                if (Settings != null && Settings.TryGetValue(name, out double v)) return v;
                switch (name)
                {
                    case "strength": return double.IsNaN(_f.Strength) ? null : (object)_f.Strength;
                    case "men": return double.IsNaN(_f.Men) ? null : (object)_f.Men;
                    case "own_near": return double.IsNaN(_f.OwnNear) ? null : (object)_f.OwnNear;
                    case "urgent": return _f.Urgent;
                    case "army": return _f.Army;
                    case "chasing": case "inside": case "besieging": case "raiding": case "on_way_to_raid": case "fighting": return _f.Doing == name;
                    default: return null;
                }
            }

            public object Call(string fn, object[] args) => null;
        }
    }
}
