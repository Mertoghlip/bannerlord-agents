using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BannerlordAgents
{
    /// <summary>
    /// Two short lists in every report of a news wake (0.15, docs/reports/2026-11-06-news-wake-study.md): the lords who hold no order (360 of run A's 905 orders went
    /// to them, about 4 at a time), and the chances to strike (65 of its 176 strikes were on a target such a list holds). Worker; game-free.
    /// </summary>
    internal static partial class Views
    {
        /// <summary>A chance: a lone enemy lord under this strength, with this many times his strength of ours within these hours of march.</summary>
        public const double ChanceStrength = 700, ChanceMargin = 1.5, ChanceHours = 13;
        public const int MostChances = 5, MostFree = 12, NearLords = 3, MostInFight = 6;

        private static string SizeWords(ViewParty p) => p.ArmyMen > 0 ? "army of " + Text.N(p.ArmyMen) + " men in " + p.ArmyParties + " parties" : Text.N(p.Men) + " men";

        /// <summary>The realm's lords holding no order, strongest first, in a line each. Null when every lord holds one.</summary>
        internal static List<string> NoOrderLines(View v)
        {
            List<ViewParty> free = v.Forces.Where(p => (p.Men > 0 || p.ArmyMen > 0) && OrderName(p) == null).OrderByDescending(p => p.Strength).ToList();
            if (free.Count == 0) return null;
            List<string> lines = free.Take(MostFree)
                .Select(p => "- " + p.Name + Tag(p) + ": " + SizeWords(p) + ", strength " + Text.N((int)p.Strength) + "; " + Where(p) + "; " + p.Doing + (p.Poor != null ? "; " + p.Poor : "") + ".").ToList();
            if (free.Count > MostFree) lines.Add("- And " + (free.Count - MostFree) + " more (list lords).");
            return lines;
        }

        /// <summary>
        /// The chances to strike: lone enemy lords in view under ChanceStrength, raiding, on their way to raid or fighting, with ChanceMargin times their strength of the realm's within
        /// ChanceHours' march. Those fighting near one holding for one realm are one line, measured together (run A's #6248 gave 15 lines for one crowd of Rohan's lords). The nearest
        /// chance first, at most MostChances. hours: the march from one of the realm's lords to an enemy.
        /// </summary>
        internal static List<string> ChanceLines(View v, Func<ViewParty, ViewParty, double> hours)
        {
            var groups = new List<List<ViewParty>>();
            var fights = new Dictionary<string, List<ViewParty>>(StringComparer.Ordinal);
            foreach (ViewParty e in v.Seen)
            {
                if (e.Relation != "enemy" || e.Kind != "lord" || e.ArmyMen > 0 || e.Strength >= ChanceStrength) continue;
                string doing = WakeRules.DoingOf(e.Doing);
                if (doing == "raiding" || doing == "on_way_to_raid") groups.Add(new List<ViewParty> { e });
                else if (doing == "fighting")
                {
                    string key = e.Faction + "|" + e.Near;
                    if (!fights.TryGetValue(key, out List<ViewParty> g)) { fights[key] = g = new List<ViewParty>(); groups.Add(g); }
                    g.Add(e);
                }
            }
            var found = new List<KeyValuePair<double, string>>();
            foreach (List<ViewParty> g in groups)
            {
                ViewParty lead = g.OrderByDescending(p => p.Strength).First();
                double strength = g.Sum(p => (double)p.Strength);
                if (strength >= ChanceStrength * (g.Count > 1 ? 2 : 1)) continue;
                List<KeyValuePair<double, ViewParty>> near = v.Forces.Where(p => p.Men > 0 || p.ArmyMen > 0).Select(p => new KeyValuePair<double, ViewParty>(hours(p, lead), p))
                    .Where(kv => !double.IsNaN(kv.Key) && kv.Key <= ChanceHours).OrderBy(kv => kv.Key).ToList();
                if (near.Sum(kv => (double)kv.Value.Strength) < ChanceMargin * strength) continue;
                string yours = "Yours near: " + string.Join(", ", near.Take(NearLords).Select(kv =>
                {
                    string order = OrderName(kv.Value);
                    return kv.Value.Name + Tag(kv.Value) + " " + Text.N((int)kv.Value.Strength) + ", about " + Forecast.HoursText(kv.Key) + " away (" + (order ?? "no order") + ")";
                })) + (near.Count > NearLords ? ", and " + (near.Count - NearLords) + " more" : "");
                string line = g.Count == 1
                    ? "- " + lead.Name + Tag(lead) + " (" + lead.FactionName + "): " + Text.N(lead.Men) + " men, strength " + Text.N((int)lead.Strength) + "; " + lead.Doing + (lead.Arrives != null ? ", " + lead.Arrives : "") + ". " + yours + "."
                    : "- " + g.Count + " of " + lead.FactionName + " fighting near " + (lead.Near ?? "nowhere known") + ", strength " + Text.N((int)strength) + " together: "
                      + string.Join(", ", g.OrderByDescending(p => p.Strength).Take(MostInFight).Select(p => p.Name + Tag(p) + " " + Text.N((int)p.Strength))) + (g.Count > MostInFight ? ", and " + (g.Count - MostInFight) + " more" : "") + ". " + yours + ".";
                found.Add(new KeyValuePair<double, string>(near[0].Key, line));
            }
            List<string> lines = found.OrderBy(kv => kv.Key).Take(MostChances).Select(kv => kv.Value).ToList();
            if (found.Count > MostChances) lines.Add("- And " + (found.Count - MostChances) + " more (list enemies).");
            return lines;
        }

        /// <summary>The two lists, where a report's extras ask for them.</summary>
        private static void ListsSection(StringBuilder sb, View v, ReportExtras x)
        {
            if (x?.Chances == null) return;
            List<string> free = NoOrderLines(v);
            if (free != null)
            {
                int n = v.Forces.Count(p => (p.Men > 0 || p.ArmyMen > 0) && OrderName(p) == null);
                sb.AppendLine().AppendLine("LORDS WITH NO ORDER (" + n + " of your " + v.Forces.Count + "; the game's own AI leads each until you order him)");
                foreach (string l in free) sb.AppendLine(l);
            }
            if (x.Chances.Count > 0)
            {
                sb.AppendLine().AppendLine("CHANCES TO STRIKE (lone enemy lords under strength " + Text.N((int)ChanceStrength) + ", raiding, on their way to raid or fighting, with "
                                           + ChanceMargin.ToString("0.#", Text.Inv) + " times their strength of yours within about " + ChanceHours.ToString("0", Text.Inv) + " hours' march)");
                foreach (string l in x.Chances) sb.AppendLine(l);
            }
        }
    }
}
