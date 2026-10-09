using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BannerlordAgents
{
    /// <summary>
    /// Checks the bridge runs on its own output, on the worker (docs/LOGGING.md):
    /// - the fog check: nothing in a report that the realm could not know;
    /// - the sight check's result (our rule against the game's own view, for the player's party);
    /// - the realm's sight, hour by hour: blind for a day while at war, or seeing far more than its parties could;
    /// - the hourly copy: impossible values (negative men, armies with no members, parties of unknown realms);
    /// - each report's size, section by section.
    /// Worker thread only.
    /// </summary>
    internal static class Audit
    {
        private static readonly Regex Brackets = new Regex(@"\[([^\]\s]+)\]", RegexOptions.Compiled);
        private static double _lastSawEnemy = -1;
        private static bool _blindAlarmed;

        public static void Reset()
        {
            _lastSawEnemy = -1;
            _blindAlarmed = false;
        }

        /// <summary>The fog check of one report: every line must be something the realm owns, sees, remembers or knows from the map.</summary>
        public static List<string> Fog(View v, string report, Dictionary<string, Sighting> sees, HashSet<string> inspected, IEnumerable<string> known = null)
        {
            var problems = new List<string>();
            foreach (ViewParty p in v.Seen)
                if (!sees.ContainsKey(p.Id)) problems.Add("listed as in view but not in sight: " + p.Name + " [" + (p.Lord ?? p.Id) + "]");
            foreach (ViewParty p in v.LastSeen)
            {
                if (sees.ContainsKey(p.Id)) problems.Add("listed as seen earlier while still in sight: " + p.Name);
                if (p.SeenHours > v.Hours) problems.Add("seen earlier at a time still to come: " + p.Name);
            }
            foreach (ViewHolding h in v.Frontier)
                if (h.Garrison >= 0 && (inspected == null || !inspected.Contains(h.Id))) problems.Add("a garrison shown for a holding out of sight: " + h.Name);
            foreach (ViewParty f in v.Forces)
                if (f.Faction != v.Faction) problems.Add("a party of another realm among its own lords: " + f.Name);
            foreach (ViewHolding h in v.Holdings)
                if (h.Faction != v.Faction) problems.Add("a holding of another realm among its own: " + h.Name);
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            foreach (ViewParty p in v.Forces.Concat(v.Seen).Concat(v.LastSeen))
            {
                allowed.Add(p.Id);
                if (p.Lord != null) allowed.Add(p.Lord);
            }
            foreach (ViewHolding h in v.Holdings.Concat(v.Frontier)) allowed.Add(h.Id);
            // the realm's own lords held prisoner, and where and by whom: the game's public records (the encyclopedia)
            foreach (string id in Views.CaptiveIds(v)) allowed.Add(id);
            // beyond the report's own lists: everything in sight (past the 40-party list), remembered, or named by an order in force
            if (known != null) foreach (string id in known) if (id != null) allowed.Add(id);
            foreach (Match m in Brackets.Matches(report ?? ""))
            {
                string id = m.Groups[1].Value;
                if (id == "brackets") continue;   // the report's own Ids in [brackets] line
                if (!allowed.Contains(id)) problems.Add("an id it should not know, [" + id + "], in the line \"" + LineAt(report, m.Index) + "\"");
            }
            return problems;
        }

        /// <summary>Runs the fog check and raises the alarm for anything that leaked.</summary>
        public static void CheckReport(View v, string report, Dictionary<string, Sighting> sees, HashSet<string> inspected, string which, IEnumerable<string> known = null)
        {
            List<string> problems = Fog(v, report, sees, inspected, known);
            Diag.Count("fog:reports");
            if (problems.Count == 0) return;
            Diag.Count("fog:leaks", problems.Count);
            Diag.Event("fog", "leak", new { which, faction = v.Faction, problems = problems.Take(20).ToList() });
            foreach (string p in problems.Take(3))
                Diag.Alarm("fog_leak", "Fog check: the " + which + " for " + v.FactionName + " shows something the realm could not know: " + p + ".", true);
        }

        /// <summary>A report's size, in tokens (about four characters each), section by section.</summary>
        public static Dictionary<string, int> Sizes(string report, out int tokens)
        {
            var sizes = new Dictionary<string, int>();
            tokens = (report?.Length ?? 0) / 4;
            if (string.IsNullOrEmpty(report)) return sizes;
            string section = "HEADER";
            foreach (string line in report.Replace("\r", "").Split('\n'))
            {
                if (line.Length > 3 && line == line.ToUpperInvariant() && char.IsLetter(line[0]) && !line.StartsWith("- ", StringComparison.Ordinal))
                {
                    int paren = line.IndexOf(" (", StringComparison.Ordinal);
                    section = paren > 0 ? line.Substring(0, paren) : line;
                }
                sizes.TryGetValue(section, out int n);
                sizes[section] = n + (line.Length + 1) / 4;
            }
            return sizes;
        }

        /// <summary>Impossible values in the hourly copy.</summary>
        public static void CheckSnapshot(Snapshot s)
        {
            var odd = new List<string>();
            if (s.Watched != null && !s.Parties.Any(r => r.Faction == s.Watched && r.Kind == "lord"))
                odd.Add("the watched realm has no lords leading parties");
            foreach (PartyRow r in s.Parties)
            {
                if (odd.Count >= 10) break;
                if (r.Men < 0) odd.Add(r.Id + " has " + r.Men + " men");
                if (float.IsNaN(r.X) || float.IsNaN(r.Y) || float.IsInfinity(r.X) || float.IsInfinity(r.Y)) odd.Add(r.Id + " has no position");
                if (r.ArmyMen > 0 && r.ArmyParties == 0) odd.Add(r.Id + " leads an army with no parties");
                if (r.ArmyLeader != null && !s.PartyById.ContainsKey(r.ArmyLeader)) odd.Add(r.Id + " is in the army of a party that does not exist (" + r.ArmyLeader + ")");
                if (r.Faction != null && !s.FactionNames.ContainsKey(r.Faction)) odd.Add(r.Id + " belongs to a realm with no name (" + r.Faction + ")");
            }
            if (odd.Count == 0) return;
            Diag.Event("snapshot", "odd values", new { problems = odd });
            Diag.Alarm("snapshot_odd", "The hourly copy of the map has values that should not happen: " + string.Join("; ", odd.Take(3)) + (odd.Count > 3 ? " (and more)" : "") + ".");
        }

        /// <summary>The realm's sight this hour: what it saw, and whether that looks like blindness or omniscience.</summary>
        public static void Sight(Snapshot s)
        {
            if (s.Watched == null || s.WatchedSees == null) return;
            int total = 0, seen = 0;
            foreach (PartyRow r in s.Parties)
            {
                if (r.Kind != "lord" || r.Attached || !s.HostileToWatched.Contains(r.Faction ?? "")) continue;
                total++;
                if (s.WatchedSees.ContainsKey(r.Id)) seen++;
            }
            if (_lastSawEnemy < 0 || seen > 0) _lastSawEnemy = s.Hours;
            if (seen > 0) _blindAlarmed = false;
            VisionStats v = s.Vision ?? new VisionStats();
            Diag.Event("vision", "hour", new
            {
                eyes = v.Eyes, holdings_looked_over = v.Inspected, parties_seen = s.WatchedSees.Count, enemy_lords_seen = seen, enemy_lords = total,
                spotting_asked = v.RatioAsked, spotting_cached = v.RatioCached, spotting_failed = v.RatioFailed, sight_failed = v.RangeFailed,
                parts_ms = s.PartsMs?.Select(x => Math.Round(x, 3)).ToArray()
            });
            if (v.RatioFailed > 0 || v.RangeFailed > 0)
                Diag.Alarm("sight_fallback", "The game's sight rules failed for " + (v.RatioFailed + v.RangeFailed) + " parties this hour, so a stand-in value was used for them.");
            bool atWar = s.Factions.TryGetValue(s.Watched, out FactionRow fr) && fr.AtWar.Count > 0;
            if (atWar && total > 0 && s.Hours - _lastSawEnemy >= 24 && !_blindAlarmed)
            {
                _blindAlarmed = true;
                Diag.Alarm("realm_blind", "The watched realm has seen no enemy lord for a whole day while at war (" + total + " enemy lords on the map). Its sight may be broken.");
            }
            if (total >= 10 && seen > total * 0.6)
                Diag.Alarm("realm_sees_too_much", "The watched realm sees " + seen + " of the " + total + " enemy lords on the map. Its sight may be too generous.");
        }

        /// <summary>The sight check for the player's party: our rule against the game's own view.</summary>
        public static void PlayerSight(SightAudit a, double hours)
        {
            if (a == null) return;
            Diag.Count("sight:runs");
            Diag.Count("sight:parties", a.Total);
            Diag.Count("sight:agree", a.Agree);
            CultureInfo inv = CultureInfo.InvariantCulture;
            Diag.Event("sight_check", a.Agree + " of " + a.Total, new
            {
                range = Math.Round(a.Range, 2), night = a.Night,
                disagreements = a.Disagree.Take(12).Select(c => new
                {
                    c.Id, c.Name, c.Kind, c.Men, distance = Math.Round(c.Distance, 2), ratio = Math.Round(c.Ratio, 3), ours = c.Ours ? "seen" : "hidden", game = c.Game ? "seen" : "hidden",
                    in_settlement = c.InSettlement, army_member = c.ArmyMember, besieging = c.Besieging, raiding = c.Raiding
                }).ToList()
            });
            if (a.Total >= 10 && a.Agree < a.Total * 0.95)
                Diag.Alarm("sight_disagrees", "Our sight rule agreed with the game on only " + a.Agree + " of " + a.Total + " parties near you (" + (100.0 * a.Agree / a.Total).ToString("0", inv)
                                              + "%). The trace lists each disagreement with its numbers.");
        }

        private static string LineAt(string text, int index)
        {
            int a = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            int b = text.IndexOf('\n', index);
            string line = (b < 0 ? text.Substring(a) : text.Substring(a, b - a)).Trim();
            return line.Length > 160 ? line.Substring(0, 160) + "..." : line;
        }
    }
}
