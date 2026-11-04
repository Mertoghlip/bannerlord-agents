using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BannerlordAgents;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// "SmokeTest --coverage pokes_(realm).jsonl orders\done --run (id) [--out file.md]" (0.14, docs/PLAN-news.md, the plan's first test): each
        /// recorded poke of the run is built again into its theatre, from its recorded view alone (no game; a march's hours by the straight line
        /// times Theatre.RoadFactor at the lord's pace; a siege's or battle's clock only where the view recorded it, so a poke from before 0.14
        /// takes a day's march there), and its fast answer's orders are read: every lord, holding and enemy they named is looked for in the
        /// theatre. Prints, by kind of news, how much of what the agent ordered the theatre held, how big the theatre was, and the news report's
        /// size against the whole one's; and the misses, with how far beyond the horizon each lay.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Coverage(string[] args)
        {
            if (args.Length < 3) { Console.WriteLine("SmokeTest --coverage <pokes_realm.jsonl> <orders\\done> --run <id> [--out file.md]"); return 1; }
            string pokesFile = args[1], done = args[2];
            string run = Arg(args, "--run"), outFile = Arg(args, "--out");
            var rows = new List<Row>();
            int lines = 0, skipped = 0;
            foreach (string line in File.ReadLines(pokesFile))
            {
                lines++;
                if (line.Length < 50 || (run != null && line.IndexOf("\"run\":\"" + run + "\"", 0, Math.Min(line.Length, 2000), StringComparison.Ordinal) < 0)) { skipped++; continue; }
                Recorded p = ReadRecorded(line);
                if (p == null) { skipped++; continue; }
                string answer = Path.Combine(done, "poke-" + p.Number + ".json");
                JObject a = File.Exists(answer) ? JObject.Parse(File.ReadAllText(answer)) : null;
                rows.Add(Measure(p, a));
            }
            string text = CoverageText(rows, pokesFile, run, lines, skipped);
            Console.WriteLine(text);
            if (outFile != null) File.WriteAllText(outFile, text, new UTF8Encoding(false));
            return 0;
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>A recorded poke: its number, its news, its view, and the whole report it was sent (for the sections the view does not hold).</summary>
        private sealed class Recorded
        {
            public int Number;
            public string Faction, FactionName, Report;
            public double Hours;
            public List<Trigger> Triggers;
            public View View;
            public JObject Raw;
        }

        private static Recorded ReadRecorded(string line)
        {
            JObject o = JObject.Parse(line);
            if ((string)o["type"] != "poke" || o["view"] == null || o["report"] == null) return null;
            return new Recorded
            {
                Number = (int)o["number"], Faction = (string)o["faction"], FactionName = (string)o["faction_name"], Hours = (double)o["hours"], Report = (string)o["report"],
                Triggers = Json.Read<List<Trigger>>(o["triggers"].ToString()), View = Json.Read<View>(o["view"].ToString()), Raw = o
            };
        }

        /// <summary>A section of a recorded report, by its heading (up to the next blank line); null when it has none.</summary>
        private static string ReportSection(string report, string head)
        {
            string r = report.Replace("\r", "");
            int i = r.IndexOf("\n" + head, StringComparison.Ordinal);
            if (i < 0) return null;
            int body = r.IndexOf('\n', i + 1);
            if (body < 0) return null;
            int end = r.IndexOf("\n\n", body, StringComparison.Ordinal);
            return (end < 0 ? r.Substring(body + 1) : r.Substring(body + 1, end - body - 1)).TrimEnd();
        }

        private static readonly Regex OrderLine = new Regex(@"^- .*? \[([^\]]+)\]: ([a-z_0-9]+)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex LastAnswered = new Regex(@"WHAT CHANGED SINCE YOUR LAST ANSWER \(to poke #(\d+)\)", RegexOptions.Compiled);

        /// <summary>The report sections a news report takes from its poke, as the recorded report had them; and the lords' orders, from its orders in force.</summary>
        private static ReportExtras ExtrasOf(Recorded p)
        {
            var x = new ReportExtras
            {
                Since = ReportSection(p.Report, "WHAT CHANGED SINCE YOUR LAST ANSWER"), InForce = ReportSection(p.Report, "YOUR ORDERS IN FORCE"), Facts = ReportSection(p.Report, "READY-MADE FACTS")
            };
            Match m = LastAnswered.Match(p.Report);
            if (m.Success) x.LastAnswered = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Views.RecordedOrders = new Dictionary<string, string>();
            foreach (Match o in OrderLine.Matches(x.InForce ?? "")) Views.RecordedOrders[o.Groups[1].Value] = o.Groups[2].Value;
            return x;
        }

        private sealed class Row
        {
            public int Number, Orders, Named, Held, Lords, LordsAll, Others, Places, FullChars, NewsChars;
            public string Kind, Recipe;
            public bool Answered, Clock, AllHeld;
            public double Horizon;
            public List<string> Misses = new List<string>();
            public List<double> LordBeyond = new List<double>();
            public int LordOrdered, PlaceInForce, Shown;
            /// <summary>Ids the news report names that the poke's whole report does not: the fog check on recorded pokes.</summary>
            public List<string> NewIds = new List<string>();
        }

        /// <summary>The kind a poke is filed under: its first item of news that is not a quiet day.</summary>
        private static string KindOf(List<Trigger> triggers)
        {
            string k = triggers.Select(t => t.Kind).FirstOrDefault(x => x != "quiet_day") ?? triggers.FirstOrDefault()?.Kind ?? "?";
            switch (k)
            {
                case "raided": case "looted": case "battle": case "captured": case "released": case "lord_killed": case "party_destroyed": return "raid, battle or capture";
                case "besieged": case "siege_started": case "siege_ended": case "holding_changed": return "siege or holding";
                case "order_ended": case "order_arrived": return "order ended or arrived";
                case "army_dispersed": case "army_formed": return "army formed or broke up";
                case "war": case "peace": case "alliance": case "alliance_ended": case "call_to_war": case "call_to_war_ended": case "peace_offer": case "proposal":
                case "decided": case "vote": case "offer": case "offer_settled": case "vote_settled": case "vote_closing": return "diplomacy";
                case "tool": return "tool call-back";
                default: return k;
            }
        }

        private static Row Measure(Recorded p, JObject answer)
        {
            View v = p.View;
            Theatre t = Theatre.Build(v, p.Triggers, Theatre.ByLine(v), Theatre.ByLine(v));
            ReportExtras x = ExtrasOf(p);
            string news = Views.NewsReport(v, p.Triggers, x, t);
            var row = new Row
            {
                Number = p.Number, Kind = KindOf(p.Triggers), Recipe = t.Recipe, Answered = answer != null, FullChars = p.Report.Length, NewsChars = news.Length,
                Lords = t.Lords.Count, LordsAll = v.Forces.Count, Others = t.Others.Count, Places = t.Places.Count,
                Clock = t.Focuses.Any(f => !double.IsNaN(f.DecidedAt)), Horizon = t.Focuses.Count > 0 ? t.Focuses.Max(f => f.Horizon) : double.NaN
            };
            var inWhole = new HashSet<string>(Views.IdsIn(p.Report));
            foreach (string id in Views.IdsIn(news)) if (!inWhole.Contains(id) && id != "brackets") row.NewIds.Add(id);
            if (answer == null || !t.IsTheatre) return row;

            // what the theatre holds: its lords (and their armies' lords), the others, the focuses and the places
            var parties = new HashSet<string>(t.PartyIds());
            var heroes = new HashSet<string>();
            foreach (ViewParty f in v.Forces.Concat(v.Forces.Where(f => f.Members != null).SelectMany(f => f.Members)).Concat(v.Seen).Concat(v.LastSeen))
                if (parties.Contains(f.Id) && f.Lord != null) heroes.Add(f.Lord);
            foreach (Reach r in t.Lords) if (r.Party.Members != null) foreach (ViewParty m in r.Party.Members) { parties.Add(m.Id); if (m.Lord != null) heroes.Add(m.Lord); }
            // the lords of the realm by id and by name, and where each is, for how far a miss lay
            var own = new Dictionary<string, ViewParty>(StringComparer.OrdinalIgnoreCase);
            foreach (ViewParty f in v.Forces.Concat(v.Forces.Where(f => f.Members != null).SelectMany(f => f.Members)))
            {
                own[f.Id] = f;
                if (f.Lord != null) own[f.Lord] = f;
                if (f.Name != null && !own.ContainsKey(f.Name)) own[f.Name] = f;
            }
            var places = new HashSet<string>(v.Holdings.Concat(v.Frontier).Select(h => h.Id), StringComparer.OrdinalIgnoreCase);
            Func<ViewParty, Focus, double> hours = Theatre.ByLine(v);

            void Named(string id, string role)
            {
                if (string.IsNullOrWhiteSpace(id)) return;
                id = id.Trim().Trim('[', ']');
                if (BannerlordAgents.Tools.Spots.Is(id)) return;
                row.Named++;
                bool held = parties.Contains(id) || heroes.Contains(id) || t.Places.Contains(id) || t.Focuses.Any(f => f.Id == id);
                if (!held && own.TryGetValue(id, out ViewParty lord)) held = parties.Contains(lord.Id);
                if (held) { row.Held++; row.Shown++; return; }
                // not in the theatre in full, but named in the report all the same (the roster of THE REST OF THE WAR, the orders elsewhere)
                bool shown = news.IndexOf("[" + id + "]", StringComparison.Ordinal) >= 0
                             || (own.TryGetValue(id, out ViewParty sl) && (news.IndexOf("[" + sl.Lord + "]", StringComparison.Ordinal) >= 0 || news.IndexOf("[" + sl.Id + "]", StringComparison.Ordinal) >= 0));
                if (shown) row.Shown++;
                string why;
                bool ordered = Views.RecordedOrders != null && (Views.RecordedOrders.ContainsKey(id) || (own.TryGetValue(id, out ViewParty ol) && ol.Lord != null && Views.RecordedOrders.ContainsKey(ol.Lord)));
                bool inForce = x.InForce != null && x.InForce.IndexOf("[" + id + "]", StringComparison.Ordinal) >= 0;
                if (own.TryGetValue(id, out ViewParty l))
                {
                    double best = t.Focuses.Min(f => hours(l, f) - f.Horizon);
                    why = "a lord of yours, " + Math.Round(best) + " h beyond the horizon" + (ordered ? ", holding an order" : ", free") + (l.ArmyMen > 0 ? ", leads an army" : "");
                    row.LordBeyond.Add(best);
                    if (ordered) row.LordOrdered++;
                }
                else
                {
                    why = (places.Contains(id) ? "a holding outside the theatre" : "an enemy or place outside the theatre") + (inForce ? ", named by an order in force" : "");
                    if (inForce) row.PlaceInForce++;
                }
                row.Misses.Add("#" + p.Number + " (" + row.Kind + ", horizon " + Math.Round(row.Horizon) + " h; " + Short(p.Triggers.FirstOrDefault()?.Text) + "): " + role + " " + id + ": " + why);
            }
            foreach (JObject o in (answer["orders"] as JArray ?? new JArray()).OfType<JObject>())
            {
                row.Orders++;
                for (JObject n = o; n != null; n = n["then"] as JObject)
                {
                    Named((string)n["lord"], "ordered");
                    Named((string)n["at"], "at");
                    Named((string)n["target"], "target");
                    foreach (JToken l in n["lords"] as JArray ?? new JArray()) Named((string)l, "called");
                    foreach (JToken l in n["places"] as JArray ?? new JArray()) Named((string)l, "place");
                }
            }
            row.AllHeld = row.Named == row.Held;
            return row;
        }

        /// <summary>
        /// "SmokeTest --newspokes pokes_(realm).jsonl (out dir) --numbers 6,10,54" (0.14, the plan's second test): the recorded pokes built again
        /// as news wakes, for "WarRoom replay --news": (out dir)\pokes.jsonl with each one's news report as its report and its theatre's news
        /// for poke.json, and (out dir)\book\poke-N.json, its question book (its lists; no look-up pages, which the record does not keep).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int NewsPokes(string[] args)
        {
            string numbers = Arg(args, "--numbers");
            if (args.Length < 3 || numbers == null) { Console.WriteLine("SmokeTest --newspokes <pokes_realm.jsonl> <out dir> --numbers 6,10,54"); return 1; }
            var want = new HashSet<int>(numbers.Split(',').Select(n => int.Parse(n.Trim(), CultureInfo.InvariantCulture)));
            string outDir = args[2];
            Directory.CreateDirectory(Path.Combine(outDir, "book"));
            var lines = new List<string>();
            foreach (string line in File.ReadLines(args[1]))
            {
                if (line.Length < 50) continue;
                int at = line.IndexOf("\"number\":", StringComparison.Ordinal);
                if (at < 0 || at > 200) continue;
                int end = line.IndexOf(',', at);
                if (!int.TryParse(line.Substring(at + 9, end - at - 9), out int n) || !want.Contains(n)) continue;
                Recorded p = ReadRecorded(line);
                if (p == null) continue;
                View v = p.View;
                Theatre t = Theatre.Build(v, p.Triggers, Theatre.ByLine(v), Theatre.ByLine(v));
                ReportExtras x = ExtrasOf(p);
                List<Views.Area> areas = Views.Areas(v);
                string news = Views.NewsReport(v, p.Triggers, x, t, areas);
                File.WriteAllText(Path.Combine(outDir, "book", "poke-" + n + ".json"), Json.Line(Views.Book(v, x, n, areas, null)), new UTF8Encoding(false));
                lines.Add(Json.Line(new
                {
                    Type = "poke", Number = n, Faction = p.Faction, p.FactionName, Hours = p.Hours, Date = (string)p.Raw["date"], Triggers = p.Triggers, Report = news,
                    FullChars = p.Report.Length,
                    News = new { Mode = "theatre", t.Recipe, Book = "book\\poke-" + n + ".json", Names = t.IsTheatre ? t.Names(v, local: true) : new List<string>(), Realms = v.AtWar.Concat(v.Allies).ToList() }
                }));
                Console.WriteLine("#" + n + ": " + t.Recipe + ", " + string.Join(", ", p.Triggers.Select(tr => tr.Kind)) + "; whole report " + p.Report.Length.ToString("N0", CultureInfo.InvariantCulture)
                                  + " characters, news report " + news.Length.ToString("N0", CultureInfo.InvariantCulture));
            }
            File.WriteAllLines(Path.Combine(outDir, "pokes.jsonl"), lines, new UTF8Encoding(false));
            Console.WriteLine(lines.Count + " of " + want.Count + " pokes written to " + Path.Combine(outDir, "pokes.jsonl"));
            return lines.Count == want.Count ? 0 : 1;
        }

        private static string Short(string s) => s == null ? "" : s.Length <= 90 ? s : s.Substring(0, 90) + "...";

        private static string Pct(double a, double b) => b <= 0 ? "-" : Math.Round(100.0 * a / b) + "%";
        private static string Med(IEnumerable<double> xs)
        {
            List<double> l = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            return l.Count == 0 ? "-" : Math.Round(l[l.Count / 2]).ToString(CultureInfo.InvariantCulture);
        }

        private static string CoverageText(List<Row> rows, string file, string run, int lines, int skipped)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Coverage of the news theatre on recorded pokes");
            sb.AppendLine();
            sb.AppendLine("From " + Path.GetFileName(file) + (run != null ? ", run " + run : "") + ": " + rows.Count + " pokes (" + lines + " lines read, " + skipped + " of other runs or unreadable); "
                          + rows.Count(r => r.Answered) + " with a fast answer on file.");
            sb.AppendLine("Each poke was built again from its recorded view alone: a march's hours by the straight line times " + Theatre.RoadFactor.ToString("0.0", CultureInfo.InvariantCulture)
                          + " at the lord's own pace; a siege's or battle's clock only where the view recorded one (from 0.14 on), else a day's march.");
            sb.AppendLine();
            sb.AppendLine("| Kind of news | Pokes | Theatre / whole / realm | With a clock | Median horizon (h) | Lords shown of all (median) | Others shown (median) | Answered with orders | Named in orders | Of them in the theatre in full | Named in the news report at all | Answers fully in the theatre | News report against whole (median) |");
            sb.AppendLine("|---|---:|---|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (IGrouping<string, Row> g in rows.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()).Concat(new[] { rows.GroupBy(r => "ALL").First() }))
            {
                List<Row> l = g.ToList();
                List<Row> th = l.Where(r => r.Recipe == "theatre").ToList();
                List<Row> ordered = th.Where(r => r.Answered && r.Orders > 0).ToList();
                int named = ordered.Sum(r => r.Named), held = ordered.Sum(r => r.Held);
                sb.AppendLine("| " + g.Key + " | " + l.Count + " | " + th.Count + " / " + l.Count(r => r.Recipe == "whole") + " / " + l.Count(r => r.Recipe == "realm")
                              + " | " + Pct(th.Count(r => r.Clock), th.Count) + " | " + Med(th.Select(r => r.Horizon)) + " | " + Med(th.Select(r => (double)r.Lords)) + " of " + Med(th.Select(r => (double)r.LordsAll))
                              + " | " + Med(th.Select(r => (double)r.Others)) + " | " + ordered.Count + " | " + named + " | " + Pct(held, named) + " | " + Pct(ordered.Sum(r => r.Shown), named) + " | " + Pct(ordered.Count(r => r.AllHeld), ordered.Count)
                              + " | " + Med(th.Select(r => 100.0 * r.NewsChars / Math.Max(1, r.FullChars))) + "% |");
            }
            List<Row> all = rows.Where(r => r.Recipe == "theatre").ToList();
            sb.AppendLine();
            sb.AppendLine("Report sizes, theatre pokes: the whole report a median " + Med(all.Select(r => (double)r.FullChars)) + " characters, the news report " + Med(all.Select(r => (double)r.NewsChars))
                          + "; the largest news report " + (all.Count > 0 ? all.Max(r => r.NewsChars).ToString("N0", CultureInfo.InvariantCulture) : "-") + ".");
            List<string> newIds = rows.SelectMany(r => r.NewIds).Distinct().ToList();
            sb.AppendLine();
            sb.AppendLine("Ids a news report names that its whole report does not: " + rows.Sum(r => r.NewIds.Count) + " in " + rows.Count(r => r.NewIds.Count > 0) + " pokes" + (newIds.Count > 0 ? ": " + string.Join(", ", newIds.Take(12)) : "") + ".");
            List<string> misses = rows.SelectMany(r => r.Misses).ToList();
            sb.AppendLine();
            sb.AppendLine("## Misses (" + misses.Count + ")");
            sb.AppendLine();
            foreach (IGrouping<string, string> g in misses.GroupBy(m => m.Contains("a lord of yours") ? "lords of yours" : m.Contains("a holding") ? "holdings" : "enemies or places").OrderByDescending(g => g.Count()))
                sb.AppendLine("- " + g.Key + ": " + g.Count());
            sb.AppendLine();
            List<double> beyond = rows.SelectMany(r => r.LordBeyond).ToList();
            sb.AppendLine("- lords missed, by how far beyond the horizon: " + string.Join(", ", new[] { 12.0, 24, 48, 96, 1e9 }.Select((b, i) => (i == 0 ? "under 12 h" : b > 1e8 ? "over 96 h" : "under " + b + " h") + " "
                          + beyond.Count(d => d < b && (i == 0 || d >= new[] { 0.0, 12, 24, 48, 96 }[i])))) + "; of them holding an order in force: " + rows.Sum(r => r.LordOrdered)
                          + "; holdings or enemies missed that an order in force named: " + rows.Sum(r => r.PlaceInForce));
            sb.AppendLine();
            foreach (string m in misses.Take(400)) sb.AppendLine("- " + m);
            if (misses.Count > 400) sb.AppendLine("- (" + (misses.Count - 400) + " more)");
            return sb.ToString();
        }
    }
}
