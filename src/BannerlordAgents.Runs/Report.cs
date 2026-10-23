using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BannerlordAgents.Runs.RunData;
using static BannerlordAgents.Runs.Text;

namespace BannerlordAgents.Runs
{
    /// <summary>A run's numbers in full (report.md and report.json in its folder), and every run side by side (compare.md).</summary>
    internal static class Report
    {
        private static readonly string[] Keys = { "towns", "castles", "villages", "lords", "men", "strength", "garrisons", "lords_captive", "enemy_lords_held", "clans_gold", "influence" };
        private static readonly string[] KeyWords = { "towns", "castles", "villages", "lords in the field", "men in the field", "strength in the field", "men in garrisons", "own lords held", "enemy lords held", "clans' gold", "influence" };

        public static int Run(string id)
        {
            string pick = Pick(id);
            if (pick == null) { Console.WriteLine("No test run yet."); return 0; }
            RunData r = Load(pick);
            JsonObject json = Build(r);
            File.WriteAllText(Path.Combine(r.Dir, "report.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            string md = Markdown(r, json);
            File.WriteAllText(Path.Combine(r.Dir, "report.md"), md);
            Console.WriteLine(md);
            Console.WriteLine("Written: " + Path.Combine(r.Dir, "report.md") + " and report.json");
            return 0;
        }

        /// <summary>Everything the report says, as data (the checkpoint's page and the comparison read it).</summary>
        public static JsonObject Build(RunData r)
        {
            JsonObject first = r.FirstDay, last = r.LastDay;
            var o = new JsonObject
            {
                ["run"] = r.Id, ["arm"] = r.Arm, ["arm_words"] = ArmWords(r.Arm), ["status"] = r.Status, ["kingdom"] = r.Kingdom,
                ["days_reached"] = last != null ? Int(last, "day") : 0, ["days_planned"] = r.DaysPlanned,
                ["started"] = Str(r.State, "started"), ["finished"] = Str(r.State, "finished"),
                ["real_hours"] = Math.Round(r.Windows.Sum(w => (w.To - w.From).TotalHours), 2), ["loads"] = r.Windows.Count,
                ["flags"] = r.State?["flags"]?.DeepClone(), ["who"] = r.State?["who"]?.DeepClone(), ["lieutenant"] = r.State?["lieutenant"]?.DeepClone(),
                ["save"] = r.Setup?["save"]?.DeepClone(), ["module_version"] = Str(r.Setup, "module_version")
            };
            var realms = new JsonArray();
            foreach (string id in r.Realms)
            {
                JsonObject a = Realm(first, id), b = Realm(last, id);
                var row = new JsonObject { ["realm"] = id, ["name"] = Str(b ?? a, "name") ?? id, ["who"] = Str(b ?? a, "who") };
                foreach (string k in Keys) row[k] = new JsonObject { ["start"] = Lng(a, k), ["end"] = Lng(b, k), ["change"] = Lng(b, k) - Lng(a, k) };
                row["tally"] = r.State?["tally"]?[id]?.DeepClone();
                realms.Add(row);
            }
            o["realms"] = realms;
            // the game's own war tally, its change over the run
            var wars = new JsonArray();
            foreach (JsonObject w in (last?["wars"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                JsonObject w0 = (r.State?["war_at_start"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(x => Str(x, "a") == Str(w, "a") && Str(x, "b") == Str(w, "b"));
                var row = new JsonObject { ["a"] = Str(w, "a"), ["b"] = Str(w, "b"), ["at_war"] = Str(w, "at_war") == "true" };
                foreach (string k in new[] { "casualties_a", "casualties_b", "sieges_a", "sieges_b", "town_sieges_a", "town_sieges_b", "raids_a", "raids_b", "tribute_a", "tribute_b" })
                    row[k] = Lng(w, k) - Lng(w0, k);
                wars.Add(row);
            }
            o["wars"] = wars;
            // the run's events
            List<JsonObject> run = r.Events.Where(e => Str(e, "k") == "run").ToList();
            o["holdings"] = new JsonArray(run.Where(e => Str(e, "w") == "holding").Select(e => e["d"].DeepClone()).ToArray());
            List<JsonNode> battles = run.Where(e => Str(e, "w") == "battle").Select(e => e["d"]).ToList();
            o["battles"] = new JsonObject
            {
                ["all"] = battles.Count, ["between_lords"] = battles.Count(LordBattle),
                ["biggest"] = new JsonArray(battles.Where(LordBattle).OrderByDescending(d => Lng(d["attacker"], "men") + Lng(d["defender"], "men")).Take(12).Select(d => d.DeepClone()).ToArray())
            };
            o["lords_killed"] = new JsonArray(run.Where(e => Str(e, "w") == "lord killed").Select(e => e["d"].DeepClone()).ToArray());
            o["notes"] = new JsonArray(run.Where(e => Str(e, "w") == "note").Select(e => e["d"].DeepClone()).ToArray());
            // day by day
            o["days"] = new JsonArray(r.Days.Select(d => (JsonNode)new JsonObject
            {
                ["day"] = Int(d, "day"), ["date"] = Str(d, "date"), ["real_seconds"] = Dbl(d, "real_seconds"),
                ["realms"] = new JsonArray(r.Realms.Select(id => Realm(d, id)).Where(x => x != null).Select(x => (JsonNode)new JsonObject
                {
                    ["realm"] = Str(x, "realm"), ["holdings"] = Lng(x, "towns") + Lng(x, "castles"), ["villages"] = Lng(x, "villages"), ["men"] = Lng(x, "men"), ["strength"] = Lng(x, "strength"),
                    ["lords"] = Lng(x, "lords"), ["lords_captive"] = Lng(x, "lords_captive"), ["clans_gold"] = Lng(x, "clans_gold")
                }).ToArray())
            }).ToArray());
            // the agents: calls, times, tokens
            int days = Math.Max(1, last != null ? Int(last, "day") : 1);
            var agents = new JsonArray();
            foreach (IGrouping<string, Call> g in r.Calls.GroupBy(c => c.Agent.Name))
            {
                Usage u = Usage.Of(g), ans = Usage.Of(g.Where(c => c.Kind == "answer" || c.Kind == "chat"));
                long peak = Usage.PeakWindow(g, 5, out DateTime peakFrom);
                double realHours = Math.Max(0.01, r.Windows.Sum(w => (w.To - w.From).TotalHours));
                agents.Add(new JsonObject
                {
                    ["agent"] = g.Key, ["mode"] = g.First().Agent.Mode,
                    ["calls"] = new JsonObject(g.GroupBy(c => c.Kind).Select(k => new KeyValuePair<string, JsonNode>(k.Key, k.Count()))),
                    ["failed"] = u.Failed, ["limit_hits"] = u.Limits,
                    ["answer_seconds"] = new JsonObject { ["median"] = Round(ans.Median), ["p90"] = Round(ans.P90) },
                    ["council_seconds_median"] = Round(Usage.Of(g.Where(c => c.Kind == "council")).Median),
                    ["tokens"] = new JsonObject
                    {
                        ["fresh_in"] = u.In, ["cache_read"] = u.CacheRead, ["cache_write"] = u.CacheWrite, ["out"] = u.Out, ["thinking"] = u.Thinking, ["all"] = u.Read + u.Out,
                        ["per_game_day"] = (u.Read + u.Out) / days, ["per_real_hour"] = (long)((u.Read + u.Out) / realHours), ["peak_5h"] = peak,
                        ["peak_5h_from"] = peakFrom == DateTime.MinValue ? null : peakFrom.ToString("yyyy-MM-dd HH:mm", Inv)
                    },
                    ["list_price_usd"] = Math.Round(u.Cost, 2),
                    ["by_day"] = new JsonArray(g.GroupBy(c => r.DayAt(c.At)).OrderBy(x => x.Key).Select(x => (JsonNode)new JsonObject
                    {
                        ["day"] = x.Key, ["calls"] = x.Count(), ["tokens"] = x.Sum(c => c.Total), ["out"] = x.Sum(c => c.Out), ["usd"] = Math.Round(x.Sum(c => c.Cost), 3)
                    }).ToArray())
                });
            }
            o["agents"] = agents;
            // their orders, the pokes, your decisions, the lieutenant
            List<JsonObject> receipts = r.Events.Where(e => Str(e, "k") == "answer" && Str(e, "w") == "receipt").ToList();
            o["orders"] = new JsonArray(receipts.GroupBy(e => Str(e["d"], "realm")).Select(g => (JsonNode)new JsonObject
            {
                ["realm"] = g.Key, ["given"] = g.Count(),
                ["by_status"] = new JsonObject(g.GroupBy(e => Str(e["d"], "status") ?? "?").Select(s => new KeyValuePair<string, JsonNode>(s.Key, s.Count()))),
                ["by_order"] = new JsonObject(g.GroupBy(e => Str(e["d"], "do") ?? "?").OrderByDescending(s => s.Count()).Select(s => new KeyValuePair<string, JsonNode>(s.Key, s.Count()))),
                ["refusals"] = new JsonArray(g.Where(e => Str(e["d"], "status") == "refused").GroupBy(e => Str(e["d"], "reason")).OrderByDescending(s => s.Count()).Take(5)
                                              .Select(s => (JsonNode)(s.Count() + " x " + s.Key)).ToArray())
            }).ToArray());
            List<JsonObject> pokes = r.Events.Where(e => Str(e, "k") == "poke" && Str(e["d"], "sent") != "false").ToList();
            o["pokes"] = new JsonObject(pokes.GroupBy(e => Str(e["d"], "faction") ?? "?").Select(g => new KeyValuePair<string, JsonNode>(g.Key, new JsonObject
            {
                ["pokes"] = g.Count(), ["report_tokens_median"] = Median(g.Select(e => Dbl(e["d"], "tokens")))
            })));
            o["your_decisions"] = new JsonObject(r.Events.Where(e => Str(e, "k") == "decision").GroupBy(e => Str(e, "w") ?? "?").Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count())));
            o["lieutenant"] = new JsonObject(r.Events.Where(e => Str(e, "k") == "lieutenant").GroupBy(e => Str(e, "w") ?? "?").Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count())));
            // the run's health
            var sight = r.Events.Where(e => Str(e, "k") == "sight_check").Select(e => (Str(e, "w") ?? "").Split(" of ")).Where(p => p.Length == 2).ToList();
            long agree = sight.Sum(p => long.TryParse(p[0], out long x) ? x : 0), total = sight.Sum(p => long.TryParse(p[1], out long x) ? x : 0);
            o["health"] = new JsonObject
            {
                ["alarms"] = new JsonObject(r.Events.Where(e => Str(e, "k") == "alarm").GroupBy(e => Str(e, "w") ?? "?").Select(g => new KeyValuePair<string, JsonNode>(g.Key, g.Count()))),
                ["errors"] = r.Events.Count(e => Str(e, "k") == "error"),
                ["freezes"] = r.Events.Count(e => Str(e, "k") == "freeze" && Str(e, "w") == "began" && !(Str(e["d"], "state") ?? "").Contains("Loading")),
                ["fog_leaks"] = r.Events.Count(e => Str(e, "k") == "fog" && Str(e, "w") == "leak"),
                ["sight_agreement"] = total > 0 ? Math.Round(100.0 * agree / total, 2) : (double?)null
            };
            return o;
        }

        private static string Markdown(RunData r, JsonObject j)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Test run " + r.Id + ": " + r.Arm + (ArmWords(r.Arm) != null ? " (" + ArmWords(r.Arm) + ")" : ""));
            sb.AppendLine();
            sb.AppendLine("- " + Str(j, "status") + ", day " + Str(j, "days_reached") + " of " + Str(j, "days_planned") + "; started " + Str(j, "started") + (Str(j, "finished") != null ? ", ended " + Str(j, "finished") : "")
                          + "; " + Str(j, "real_hours") + " real hours over " + Str(j, "loads") + " load(s).");
            sb.AppendLine("- From the save " + Str(j["save"], "name") + " (fingerprint " + (Str(j["save"], "sha256") ?? "-") + "); module " + Str(j, "module_version") + ".");
            sb.AppendLine("- Who commanded: " + string.Join("; ", (j["who"] as JsonObject ?? new JsonObject()).Select(kv => kv.Key + ": " + WhoWords(kv.Value?.ToString()))) + (Str(j, "lieutenant") == "true" ? "; your lieutenant on" : ""));
            foreach (JsonNode f in j["flags"] as JsonArray ?? new JsonArray()) sb.AppendLine("- **Mark against it** (day " + Str(f, "day") + ", " + Str(f, "kind") + "): " + Str(f, "text"));
            sb.AppendLine();
            sb.AppendLine("## Where each side ended (day 0 to the last day)");
            sb.AppendLine();
            List<JsonObject> realms = (j["realms"] as JsonArray).OfType<JsonObject>().ToList();
            sb.AppendLine("| | " + string.Join(" | ", realms.Select(x => Str(x, "name") + " (" + WhoWords(Str(x, "who")) + ")")) + " |");
            sb.AppendLine("|---|" + string.Concat(realms.Select(_ => "---|")));
            for (int i = 0; i < Keys.Length; i++)
                sb.AppendLine("| " + KeyWords[i] + " | " + string.Join(" | ", realms.Select(x => N(Lng(x[Keys[i]], "end")) + " (" + Signed(Lng(x[Keys[i]], "change")) + ")")) + " |");
            string[] tk = { "battles_won", "battles_lost", "lord_battles_won", "lord_battles_lost", "men_lost", "enemy_fallen", "sieges_begun", "towns_gained", "towns_lost", "castles_gained", "castles_lost",
                            "villages_gained", "villages_lost", "lords_captured", "lords_taken", "lords_killed", "lords_slain", "lords_freed", "villages_looted", "villages_sacked", "armies" };
            string[] tw = { "battles won", "battles lost", "battles between lords won", "battles between lords lost", "men lost", "enemy men fallen", "sieges begun", "towns taken", "towns lost", "castles taken", "castles lost",
                            "villages taken", "villages lost", "own lords taken prisoner", "enemy lords taken", "own lords killed", "enemy lords killed", "own lords freed", "own villages looted", "villages it looted", "armies formed" };
            for (int i = 0; i < tk.Length; i++) sb.AppendLine("| " + tw[i] + " | " + string.Join(" | ", realms.Select(x => N(Lng(x["tally"], tk[i])))) + " |");
            sb.AppendLine();
            sb.AppendLine("## The game's own war tally over the run");
            sb.AppendLine();
            sb.AppendLine("| between | casualties | sieges won | towns won | raids won |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (JsonNode w in j["wars"] as JsonArray)
                sb.AppendLine("| " + Str(w, "a") + " and " + Str(w, "b") + (Str(w, "at_war") == "true" ? " (at war)" : "") + " | " + N(Lng(w, "casualties_a")) + " / " + N(Lng(w, "casualties_b")) + " | "
                              + Lng(w, "sieges_a") + " / " + Lng(w, "sieges_b") + " | " + Lng(w, "town_sieges_a") + " / " + Lng(w, "town_sieges_b") + " | " + Lng(w, "raids_a") + " / " + Lng(w, "raids_b") + " |");
            sb.AppendLine();
            sb.AppendLine("## Holdings that changed hands");
            sb.AppendLine();
            foreach (JsonNode h in j["holdings"] as JsonArray) sb.AppendLine("- day " + Str(h, "day") + ": " + Str(h, "place") + " (" + Str(h, "kind") + "), " + Str(h, "from") + " to " + Str(h, "to") + " (" + Str(h, "how") + ")");
            if ((j["holdings"] as JsonArray).Count == 0) sb.AppendLine("- none");
            sb.AppendLine();
            sb.AppendLine("## Battles");
            sb.AppendLine();
            sb.AppendLine("- " + Str(j["battles"], "all") + " battles involving these realms, " + Str(j["battles"], "between_lords") + " of them with lords on both sides. The biggest:");
            foreach (JsonNode b in j["battles"]["biggest"] as JsonArray) sb.AppendLine("  - day " + Str(b, "day") + ": " + Battle(b));
            foreach (JsonNode k in j["lords_killed"] as JsonArray) sb.AppendLine("- day " + Str(k, "day") + ": " + Str(k, "lord") + " (" + Str(k, "realm") + ") killed" + (Str(k, "killer") != null ? " by " + Str(k, "killer") : ""));
            sb.AppendLine();
            sb.AppendLine("## The agents");
            sb.AppendLine();
            sb.AppendLine("| agent | calls | failed | answer median / 90% | tokens read (from cache) | tokens written (thinking) | per game day | per real hour | most in 5 hours | list price |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (JsonNode a in j["agents"] as JsonArray)
            {
                JsonNode t = a["tokens"];
                sb.AppendLine("| " + Str(a, "agent") + " (" + Str(a, "mode") + ") | " + string.Join(", ", (a["calls"] as JsonObject).Select(kv => kv.Value + " " + kv.Key)) + " | " + Str(a, "failed") + (Lng(a, "limit_hits") > 0 ? " (" + Str(a, "limit_hits") + " limits)" : "")
                              + " | " + Str(a["answer_seconds"], "median") + " / " + Str(a["answer_seconds"], "p90") + " s | " + K(Lng(t, "fresh_in") + Lng(t, "cache_read") + Lng(t, "cache_write")) + " (" + K(Lng(t, "cache_read")) + ") | "
                              + K(Lng(t, "out")) + " (" + K(Lng(t, "thinking")) + ") | " + K(Lng(t, "per_game_day")) + " | " + K(Lng(t, "per_real_hour")) + " | " + K(Lng(t, "peak_5h")) + " | $" + Str(a, "list_price_usd") + " |");
            }
            if ((j["agents"] as JsonArray).Count == 0) sb.AppendLine("| none | | | | | | | | | |");
            sb.AppendLine();
            foreach (JsonNode o in j["orders"] as JsonArray)
                sb.AppendLine("- " + Str(o, "realm") + "'s orders: " + Str(o, "given") + " (" + string.Join(", ", (o["by_status"] as JsonObject).Select(kv => kv.Value + " " + kv.Key)) + "); most given: "
                              + string.Join(", ", (o["by_order"] as JsonObject).Take(6).Select(kv => kv.Key + " " + kv.Value)) + ((o["refusals"] as JsonArray).Count > 0 ? "; refused most for: " + string.Join(" | ", (o["refusals"] as JsonArray).Select(x => x.ToString())) : ""));
            foreach (KeyValuePair<string, JsonNode> p in j["pokes"] as JsonObject) sb.AppendLine("- " + p.Key + ": " + Str(p.Value, "pokes") + " pokes, their reports about " + N(Dbl(p.Value, "report_tokens_median")) + " tokens (median)");
            sb.AppendLine("- your decisions recorded: " + string.Join(", ", (j["your_decisions"] as JsonObject).Select(kv => kv.Value + " " + kv.Key)) + "; the lieutenant: " + string.Join(", ", (j["lieutenant"] as JsonObject).Select(kv => kv.Value + " " + kv.Key)));
            sb.AppendLine();
            sb.AppendLine("## Health");
            sb.AppendLine();
            JsonNode hl = j["health"];
            sb.AppendLine("- errors " + Str(hl, "errors") + ", freezes (outside loading) " + Str(hl, "freezes") + ", fog leaks " + Str(hl, "fog_leaks") + ", sight check agreement " + (Str(hl, "sight_agreement") ?? "-") + "%");
            sb.AppendLine("- alarms: " + string.Join(", ", (hl["alarms"] as JsonObject).Select(kv => kv.Key + " " + kv.Value)));
            foreach (JsonNode n in j["notes"] as JsonArray) sb.AppendLine("- your note, day " + Str(n, "day") + ": " + Str(n, "text"));
            return sb.ToString();
        }

        /// <summary>Every run side by side: compare.md in the runs folder.</summary>
        public static int Compare()
        {
            List<string> ids = All();
            if (ids.Count == 0) { Console.WriteLine("No test run yet."); return 0; }
            var sb = new StringBuilder();
            sb.AppendLine("# The test runs side by side");
            sb.AppendLine();
            sb.AppendLine("| run | arm | days | your kingdom: holdings | villages | men | lords held / taken | lord battles won-lost | men lost / enemy fallen | rivals: holdings | its tokens / list price | rivals' tokens / list price | marks |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (string id in ids)
            {
                RunData r = Load(id);
                if (r.LastDay == null) continue;
                JsonObject j = Build(r);
                JsonObject k = (j["realms"] as JsonArray).OfType<JsonObject>().FirstOrDefault(x => Str(x, "realm") == r.Kingdom);
                List<JsonObject> rivals = (j["realms"] as JsonArray).OfType<JsonObject>().Where(x => Str(x, "realm") != r.Kingdom).ToList();
                long Ch(JsonObject x, string key) => Lng(x?[key], "change");
                long T(JsonObject x, string key) => Lng(x?["tally"], key);
                List<JsonObject> ag = (j["agents"] as JsonArray).OfType<JsonObject>().ToList();
                bool Ours(JsonObject a) => Str(a, "agent") == r.Kingdom || Str(a, "agent") == "lieutenant";
                string Tok(IEnumerable<JsonObject> xs) => K(xs.Sum(a => Lng(a["tokens"], "all"))) + " / $" + xs.Sum(a => Dbl(a, "list_price_usd")).ToString("0.00", Inv);
                sb.AppendLine("| " + id + " | " + r.Arm + " | " + Str(j, "days_reached") + " | " + Signed(Ch(k, "towns") + Ch(k, "castles")) + " | " + Signed(Ch(k, "villages")) + " | " + Signed(Ch(k, "men"))
                              + " | " + T(k, "lords_captured") + " / " + T(k, "lords_taken") + " | " + T(k, "lord_battles_won") + "-" + T(k, "lord_battles_lost") + " | " + N(T(k, "men_lost")) + " / " + N(T(k, "enemy_fallen"))
                              + " | " + Signed(rivals.Sum(x => Ch(x, "towns") + Ch(x, "castles"))) + " | " + Tok(ag.Where(Ours)) + " | " + Tok(ag.Where(a => !Ours(a))) + " | " + ((j["flags"] as JsonArray)?.Count ?? 0) + " |");
            }
            string path = Path.Combine(RunsDir, "compare.md");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine(sb.ToString());
            Console.WriteLine("Written: " + path);
            return 0;
        }

        private static double? Round(double x) => double.IsNaN(x) ? null : Math.Round(x, 1);

        private static double Median(IEnumerable<double> xs)
        {
            List<double> s = xs.Where(x => x > 0).OrderBy(x => x).ToList();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }
    }
}
