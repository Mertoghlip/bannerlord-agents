using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// My circuit order (docs/PLAN-0.13.md A3), written in the language: once a day it recruits at the realm's nearest town, at each
        /// of its places and at its home castle (Oct 29: towns and castles both give recruits, towns the most), goes to the town at once
        /// when its loot is worth selling (sold as it rides in), and between rounds patrols at home, striking small bands. The places each
        /// take their kind first.
        /// </summary>
        internal const string CircuitJson = @"{ ""name"": ""circuit"",
  ""about"": ""recruits once a day at the realm's nearest town, at each of its places and at its home castle, sells its loot in the town, and between rounds rests at home or patrols there against small bands"",
  ""takes"": { ""places"": ""friendly_holding: other towns, castles and villages to recruit at, each once a day"", ""at"": ""own_fort: home, the castle where it recruits each round and rests or patrols between rounds"" },
  ""settings"": { ""sell_above"": [3000, 0, 100000, ""go to the town at once when its loot is worth this much""],
                ""patrol"": [1, 0, 1, ""1: patrol around home between rounds, striking small bands; 0: rest inside""],
                ""strike_below"": [0.5, 0, 2, ""strike a band at most this strong against the lord""],
                ""radius"": [15, 3, 40, ""the patrol's reach around home""] },
  ""init"": { ""visited"": ""{}"", ""stop"": ""none"" },
  ""start"": ""next"",
  ""states"": {
    ""next"": { ""about"": ""choosing the next stop"",
      ""when"": [ { ""if"": ""loot_value >= sell_above and nearest_town_of_realm() != none and not inside(nearest_town_of_realm())"", ""go"": ""town"", ""set"": { ""stop"": ""nearest_town_of_realm()"" } },
                { ""if"": ""next_due([nearest_town_of_realm()], visited, 24) != none"", ""go"": ""town"", ""set"": { ""stop"": ""nearest_town_of_realm()"" } },
                { ""if"": ""next_due(places, visited, 24) != none"", ""go"": ""recruit"", ""set"": { ""stop"": ""next_due(places, visited, 24)"" } },
                { ""if"": ""next_due([at], visited, 24) != none"", ""go"": ""home"" },
                { ""if"": ""true"", ""go"": ""between"" } ] },
    ""town"": { ""about"": ""recruiting at {stop}, the realm's nearest town, where its loot is sold as it rides in"", ""do"": ""recruit at stop"",
      ""when"": [ { ""if"": ""arrived and hours_in_state >= 1"", ""go"": ""next"", ""set"": { ""visited[stop]"": ""now"" } },
                { ""if"": ""not ours(stop) or besieged(stop)"", ""go"": ""next"" } ] },
    ""recruit"": { ""about"": ""recruiting at {stop}"", ""do"": ""recruit at stop"",
      ""when"": [ { ""if"": ""arrived and hours_in_state >= 1"", ""go"": ""next"", ""set"": { ""visited[stop]"": ""now"" } },
                { ""if"": ""not friendly(stop)"", ""go"": ""next"", ""set"": { ""visited[stop]"": ""now"" }, ""poke"": ""{stop} is no longer ours or an ally's; skipped."" } ] },
    ""home"": { ""about"": ""recruiting at home in {at}"", ""do"": ""recruit at at"",
      ""when"": [ { ""if"": ""arrived and hours_in_state >= 1"", ""go"": ""between"", ""set"": { ""visited[at]"": ""now"" } },
                { ""if"": ""besieged(at)"", ""go"": ""between"", ""set"": { ""visited[at]"": ""now"" } } ] },
    ""between"": { ""about"": ""between rounds at {at}"", ""do"": ""enter at"",
      ""when"": [ { ""if"": ""patrol == 1"", ""go"": ""watch"" },
                { ""if"": ""next_due([nearest_town_of_realm(), at], visited, 24) != none or next_due(places, visited, 24) != none"", ""go"": ""next"" } ] },
    ""watch"": { ""about"": ""patrolling around {at}"", ""do"": ""patrol at"",
      ""when"": [ { ""if"": ""next_due([nearest_town_of_realm(), at], visited, 24) != none or next_due(places, visited, 24) != none"", ""go"": ""next"" },
                { ""if"": ""weakest_bandit_near(at, radius) != none and strength_of(weakest_bandit_near(at, radius)) <= strength * strike_below"",
                  ""go"": ""strike"", ""set"": { ""target"": ""weakest_bandit_near(at, radius)"" } } ] },
    ""strike"": { ""about"": ""striking {target}"", ""do"": ""engage target"",
      ""when"": [ { ""if"": ""gone(target) or hours_in_state >= 6 or distance(target, at) > radius"", ""go"": ""watch"" } ] } } }";

        /// <summary>
        /// 0.13 step 2, the language grows (docs/PLAN-0.13.md A2): lists and tables, the new functions, facts and moves, the checker's new
        /// checks with their places named, the limits on an agent's own tools, and my circuit order end to end on a made-up world.
        /// </summary>
        private static void LanguageTests(string root, string agentDir)
        {
            // the language itself: lists written out and indexed, an empty table, a table read by key
            var scope = new NameScope();
            scope.Values["visited"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["castle_b"] = 10.0 };
            scope.Values["here"] = "castle_b";
            scope.Values["elsewhere"] = "town_c";
            object E(string text) => Expr.Parse(text).Eval(scope);
            Check(Expr.Number(E("[1, 2, 3][1]")) == 2 && E("[1, 2, 3][3]") == null && E("[1, 2][0.5]") == null && (string)E("[here, elsewhere][1]") == "town_c",
                  "a list written out, its items counted from 0, none past its end or between whole positions");
            Check(Expr.Number(E("visited[here]")) == 10 && E("visited[elsewhere]") == null && E("visited[none]") == null && E("{}[here]") == null,
                  "a table read by key: none for a key never set, and for none");
            Check(!Expr.Truthy(E("[]")) && Expr.Truthy(E("[none]")) && !Expr.Truthy(E("{}")) && Expr.Truthy(E("visited")),
                  "an empty list and an empty table count as false, a list with something in it as true");
            Check(Expr.Show(E("[here, 2, none]")) == "castle_b, 2, none" && Expr.Show(E("[]")) == "nothing" && Expr.Show(E("visited")) == "castle_b 10",
                  "lists and tables are shown item by item");
            Check(!ReferenceEquals(Expr.Parse("{}").Eval(scope), Expr.Parse("{}").Eval(scope)), "each {} is a new table, so two tools never share one");
            bool Refused(string text) { try { Expr.Parse(text); return false; } catch (FormatException) { return true; } }
            Check(Refused("visited[]") && Refused("{here}") && Refused("[1, 2") && Refused("here]"), "an empty index, a table with something in it, a list left open, a stray ] are refused");

            // the checker: kinds first in takes, places, a table set by key, its problems named by state and rule, every state reachable
            ToolDef circuit = ToolDef.FromJson(CircuitJson, "circuit", "orcs");
            Check(circuit.Valid && circuit.Places == "friendly_holding" && circuit.At == "own_fort" && circuit.Own && circuit.TakesAbout["places"].StartsWith("other towns"),
                  "the circuit order passes the checker, its places of the friendly kind and its home one of the realm's forts" + (circuit.Valid ? "" : ": " + string.Join("; ", circuit.Problems)));
            Check(circuit.States["recruit"].Do.Verb == "recruit" && circuit.States["recruit"].Do.ArgText == "stop" && circuit.States["town"].Do.Verb == "recruit" && circuit.States["town"].Do.ArgText == "stop"
                  && circuit.States["home"].Do.Verb == "recruit" && circuit.States["home"].Do.ArgText == "at" && circuit.States["between"].Do.ArgText == "at"
                  && circuit.States["recruit"].When[0].SetAt[0] != null && circuit.States["recruit"].When[0].Set[0].Key == "visited",
                  "\"recruit at stop\" and \"recruit at at\" read as the move and its place (\"enter at\" stays the order's own place); \"visited[stop]\" sets the table's entry");
            Check(circuit.Settings.ContainsKey("caution") && circuit.Settings.ContainsKey("danger_radius") && circuit.Settings.ContainsKey("join_hours"),
                  "an agent's own tool carries the safety rule's numbers, as ours do");
            Check(circuit.Describe().StartsWith("- **circuit** (yours, v1) (at: one of your castles or towns, home, the castle where it recruits") && circuit.Describe().Contains("places: a list, each your or an ally's holding, other towns"),
                  "the library lists an agent's tool as theirs, with what it takes in their own words: " + circuit.Describe());
            ToolDef bad = ToolDef.FromJson(@"{ ""name"": ""bad"", ""takes"": { ""places"": ""the towns"" }, ""start"": ""a"", ""states"": {
                ""a"": { ""do"": ""hold"", ""when"": [ { ""if"": ""hours_in_state > 2"", ""go"": ""a"" }, { ""if"": ""next_due(places, 24) != none"", ""go"": ""a"" } ] },
                ""lost"": { ""do"": ""wait"" },
                ""stuck"": { ""about"": ""nothing to do"" } } }", "bad", "orcs");
            Check(bad.Problems.Any(p => p.StartsWith("takes places: say what kind it is first")) && bad.Problems.Any(p => p.Contains("state a, rule 2") && p.Contains("next_due takes 3 values, given 2"))
                  && bad.Problems.Any(p => p.Contains("state lost can never be reached")) && bad.Problems.Any(p => p.Contains("wait needs a number of hours"))
                  && bad.Problems.Any(p => p.Contains("state stuck: it has no move (do), no end and no rules")),
                  "the checker names each problem's place: a kind missing in takes, \"state a, rule 2: next_due takes 3 values, given 2\", a state never reached, a move missing its hours, a dead end: " + string.Join(" | ", bad.Problems));
            var big = new JObject { ["name"] = "big", ["start"] = "s0", ["safety"] = false, ["states"] = new JObject() };
            for (int i = 0; i < 13; i++) ((JObject)big["states"])["s" + i] = JObject.Parse("{\"do\": \"hold\", \"when\": [{\"if\": \"true\", \"go\": \"s" + ((i + 1) % 13) + "\"}]}");
            ToolDef tooBig = ToolDef.FromJson(big.ToString(), "big", "orcs");
            Check(tooBig.Problems.Any(p => p.Contains("13 states, more than the 12")) && tooBig.Problems.Any(p => p.Contains("always carries the safety")) && ToolDef.FromJson(big.ToString(), "big").Problems.Count == 0,
                  "an agent's own tool is held to the limits (13 states of 12) and keeps the safety rule; ours are not held to them");
            Check(ToolDef.FromJson("{\"name\": \"Bad Name\", \"start\": \"a\", \"states\": {\"a\": {\"do\": \"hold\"}}}", "x", "orcs").Problems.Any(p => p.Contains("small letters, digits and underscores"))
                  && ToolDef.FromJson("{\"name\": \"raider\", \"kind\": \"army\", \"start\": \"a\", \"states\": {\"a\": {\"do\": \"hold\"}}}", "x", "orcs").Problems.Any(p => p.Contains("an army's tools are ours only")),
                  "an agent's tool's name is small letters, digits and underscores, and it is not an army's tool");
            ToolDef masks = ToolDef.FromJson("{\"name\": \"masks\", \"settings\": {\"distance\": [1, 0, 2], \"gold\": [1, 0, 2]}, \"init\": {\"men\": \"0\"}, \"start\": \"a\", \"states\": {\"a\": {\"do\": \"hold\"}}}", "x", "orcs");
            Check(masks.Problems.Any(p => p.Contains("setting distance has the name of a function")) && masks.Problems.Any(p => p.Contains("setting gold hides a built-in name"))
                  && masks.Problems.Any(p => p.Contains("'men' is a built-in name")),
                  "a setting or a remembered value may not take the name of a function or a built-in one");
            List<string> library = Library.Problems();
            Check(library.Count == 0, "our 20 tools still pass the stricter checker" + (library.Count > 0 ? ": " + string.Join("; ", library) : ""));

            var moves = new List<KeyValuePair<int, Move>>();
            var released = new List<KeyValuePair<int, string>>();
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => released.Add(new KeyValuePair<int, string>(id, why));
            Runner.ThenSink = (id, then) => { };
            Settings.AgentDir = Path.Combine(root, "language-agent");
            Brain.Reset("smoketest-language", double.NaN, "orcs", null);
            Move? Last(int id) => moves.Where(m => m.Key == id).Select(m => (Move?)m.Value).LastOrDefault();

            // the list functions, the day and the hour as the runner has them: a tool that goes on only if they all read right
            ToolDef lists = ToolDef.FromJson(@"{ ""name"": ""lists"", ""takes"": { ""places"": ""own_holding: two holdings"", ""at"": ""own_holding: the first of them"" }, ""start"": ""a"", ""states"": {
                ""a"": { ""when"": [ { ""if"": ""count(places) == 2 and first(places) == at and last(places) == item(places, 1) and places[1] == last(places) and item(places, 2) == none and contains(places, at) and not contains(places, none) and next(places, last(places)) == at and next(places, none) == at and nearest_of(places) == at and day == 1625 and hour_of_day == 16 and hours_since(now) == 0 and count(lords) == none"", ""go"": ""b"" } ] },
                ""b"": { ""do"": ""goto next(places, at)"" } } }", "lists", "orcs");
            Check(lists.Problems.Any(p => p.Contains("'lords' is not a setting") && p.Contains("this tool takes no lords")), "a tool that takes no lords may not read them");
            lists = ToolDef.FromJson(lists.Json.Replace(" and count(lords) == none", ""), "lists", "orcs");
            var ug = new PartyRow { Id = "p_ugl", Leader = "lord_ugl", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 300, Strength = 300, Speed = 5, FoodDays = 20, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            Diag.Hours = 39016;
            Brain.OnHour(ToolWorld(39016, ug));
            var lo = new ToolOrder { Tool = "lists", Def = lists, Lord = "lord_ugl", LordName = "Ugluk", Party = "p_ugl", At = "castle_orc", Realm = "orcs" };
            lo.Places.AddRange(new[] { "castle_orc", "castle_two" });
            Runner.Start(9400, lo);
            Check(lists.Valid && Last(9400)?.Verb == "goto" && Last(9400)?.Place == "castle_two",
                  "count, first, last, item, [n], contains, next (round to the first), nearest_of, the day, the hour and hours_since read right in a running tool (" + Last(9400) + ")" + (lists.Valid ? "" : ": " + string.Join("; ", lists.Problems)));
            Runner.Ended(9400, "test over");

            // recruit_here, wait and arrived: takes the volunteers where he stands, waits six hours, and is done
            ToolDef rest = ToolDef.FromJson("{\"name\": \"rest_here\", \"start\": \"a\", \"states\": {\"a\": {\"do\": \"recruit_here\", \"when\": [{\"if\": \"recruited_last == 0 and hours_in_state >= 2\", \"go\": \"b\"}]}, \"b\": {\"do\": \"wait 6\", \"when\": [{\"if\": \"arrived\", \"go\": \"c\"}]}, \"c\": {\"end\": \"free\"}}}", "rest_here", "orcs");
            Check(rest.Valid, "the new moves (recruit_here, wait 6) and facts (recruited_last, arrived) pass the checker" + (rest.Valid ? "" : ": " + string.Join("; ", rest.Problems)));
            Runner.Start(9401, new ToolOrder { Tool = "rest_here", Def = rest, Lord = "lord_ugl", LordName = "Ugluk", Party = "p_ugl", Realm = "orcs" });
            Check(Last(9401)?.Verb == "recruit" && Last(9401)?.Place == "castle_orc", "recruit_here recruits in the holding he is in (" + Last(9401) + ")");
            for (int h = 39017; h <= 39018; h++) Brain.OnHour(ToolWorld(h, ug));
            Check(Last(9401)?.Verb == "wait" && Math.Abs(Last(9401).Value.R - 6) < 0.01, "with no recruits in two hours it waits six (" + Last(9401) + ")");
            for (int h = 39019; h <= 39023; h++) Brain.OnHour(ToolWorld(h, ug));
            Check(!released.Any(r => r.Key == 9401), "and has not arrived five hours into its wait");
            Brain.OnHour(ToolWorld(39024, ug));
            Check(released.Any(r => r.Key == 9401 && r.Value == "the rest_here is done"), "six hours on, the wait has arrived and the tool is done");

            CircuitTests(circuit, moves, released);
            CarryOnTests(moves, released);
            Settings.AgentDir = agentDir;
            InstructionExampleTests();
        }

        /// <summary>The example tools in the agents' instructions (agent/warroom-system.template.md, Part D) each pass the game's checker, as written there.</summary>
        private static void InstructionExampleTests()
        {
            string template = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "agent", "warroom-system.template.md"));
            if (!File.Exists(template)) { Check(false, "the instructions' template is where the test looks for it (" + template + ")"); return; }
            string text = File.ReadAllText(template);
            int a = text.IndexOf("## Writing your own tools", StringComparison.Ordinal), b = text.IndexOf("\n## ", a + 3, StringComparison.Ordinal);
            string section = a < 0 ? "" : b < 0 ? text.Substring(a) : text.Substring(a, b - a);
            var tools = new List<ToolDef>();
            foreach (string block in section.Split(new[] { "```json" }, StringSplitOptions.None).Skip(1).Select(x => x.Substring(0, x.IndexOf("```", StringComparison.Ordinal))))
            {
                JObject o;
                try { o = JObject.Parse(block); }
                catch (Newtonsoft.Json.JsonException) { continue; }   // a fragment, such as the answer's "tools" line
                if (o["states"] == null || (string)o["name"] == "my_tool") continue;
                tools.Add(ToolDef.FromJson(block, (string)o["name"], "orcs"));
            }
            Check(tools.Count == 3 && tools.All(t => t.Valid) && tools.Select(t => t.Name).SequenceEqual(new[] { "circuit", "gate_watch", "sortie" }),
                  "the instructions' example tools (" + string.Join(", ", tools.Select(t => t.Name)) + ") each pass the game's checker as written"
                  + string.Concat(tools.Where(t => !t.Valid).Select(t => "; " + t.Name + ": " + string.Join("; ", t.Problems))));
            ToolDef example = tools.FirstOrDefault(t => t.Name == "circuit"), tested = ToolDef.FromJson(CircuitJson, "circuit", "orcs");
            Check(example != null && example.States.Count == tested.States.Count && example.States.Keys.All(k => tested.States.ContainsKey(k) && tested.States[k].When.Count == example.States[k].When.Count
                  && tested.States[k].When.Select(r => r.IfText).SequenceEqual(example.States[k].When.Select(r => r.IfText))),
                  "the circuit in the instructions is the one the tests run, rule for rule");
        }

        /// <summary>The circuit on a made-up world: the town first, each place and home once a day, back to the town when the loot is worth it, a patrol striking only small bands, shelter from an army, a call-back only when a place is lost.</summary>
        private static void CircuitTests(ToolDef circuit, List<KeyValuePair<int, Move>> moves, List<KeyValuePair<int, string>> released)
        {
            var lord = new PartyRow { Id = "p_circ", Leader = "lord_circ", Name = "Grishnakh", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 300, Strength = 400, Speed = 5, FoodDays = 20, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true, Loot = 100, Gold = 2000 };
            Snapshot World(double h, params PartyRow[] more)
            {
                Snapshot s = ToolWorld(h, new[] { lord }.Concat(more).ToArray());
                Add(s, new SettlementRow { Id = "town_orc", Name = "Goblin Town", Kind = "town", Faction = "orcs", X = 120, Y = 130, Garrison = 300, GarrisonStrength = 400, WallLevel = 2 });
                return s;
            }
            Move? Last() => moves.Where(m => m.Key == 9500).Select(m => (Move?)m.Value).LastOrDefault();
            List<Move> Since(int n) => moves.Skip(n).Where(m => m.Key == 9500).Select(m => m.Value).ToList();
            var order = new ToolOrder { Tool = "circuit", Def = circuit, Lord = "lord_circ", LordName = "Grishnakh", Party = "p_circ", At = "castle_orc", Realm = "orcs" };
            order.Places.AddRange(new[] { "village_orc", "castle_two" });
            double h0 = 40000;
            Diag.Hours = h0;
            Brain.OnHour(World(h0));
            Runner.Start(9500, order);
            Check(Last()?.Verb == "recruit" && Last()?.Place == "town_orc", "the circuit starts at the realm's nearest town, where recruits are most plentiful: recruit at Goblin Town (" + Last() + ")");
            lord.InSettlement = "town_orc"; lord.X = 120; lord.Y = 130;
            Brain.OnHour(World(h0 + 1));
            Check(Last()?.Verb == "recruit" && Last()?.Place == "village_orc", "an hour in the town, it rides on to the nearest of its places: recruit at Orc Village (" + Last() + ")");
            lord.InSettlement = null; lord.X = 110; lord.Y = 104;
            Brain.OnHour(World(h0 + 2));
            Check(Last()?.Verb == "recruit" && Last()?.Place == "castle_two", "an hour at the village, it rides on to the next place due: recruit at Orc Keep (" + Last() + ")");
            lord.InSettlement = "castle_two"; lord.X = 140; lord.Y = 100;
            Brain.OnHour(World(h0 + 3));
            Check(Last()?.Verb == "recruit" && Last()?.Place == "castle_orc", "with its places done, it recruits at home, its castle (" + Last() + ")");
            lord.InSettlement = "castle_orc"; lord.X = 100; lord.Y = 100;
            Brain.OnHour(World(h0 + 4));
            Check(Last()?.Verb == "patrol" && Last()?.Place == "castle_orc" && Math.Abs(Last().Value.R - 15) < 0.01, "with the town, every place and home visited today, it patrols at home within its radius (" + Last() + ")");
            lord.InSettlement = null; lord.X = 104; lord.Y = 100;
            int before = moves.Count;
            for (int h = 5; h <= 20; h++) Brain.OnHour(World(h0 + h));
            Check(Since(before).Count == 0, "within the day it does not go back to a place it has visited, and patrols on");
            var big = new PartyRow { Id = "p_horde", Name = "Hill-troll band", Faction = "bandits", Kind = "bandit", Men = 120, Strength = 380, X = 108, Y = 103, Doing = "PatrolAroundPoint", Spottable = true };
            Brain.OnHour(World(h0 + 21, big));
            Check(Last()?.Verb == "patrol", "a band nearly as strong as the lord is left alone (its bar is half his strength)");
            var band = new PartyRow { Id = "p_band", Name = "Hill bandits", Faction = "bandits", Kind = "bandit", Men = 20, Strength = 60, X = 108, Y = 103, Doing = "PatrolAroundPoint", Spottable = true };
            Brain.OnHour(World(h0 + 22, band));
            Check(Last()?.Verb == "engage" && Last()?.Party == "p_band", "a small band near home is struck (" + Last() + ")");
            var army = new PartyRow { Id = "p_host", Leader = "lord_host", Name = "Theoden", Faction = "player_k", Kind = "lord", Men = 3000, ArmyMen = 4000, ArmyParties = 5, ArmyStrength = 6000, Strength = 2000, Speed = 4, X = 107, Y = 101, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            before = moves.Count;
            Brain.OnHour(World(h0 + 23, army));
            Check(Since(before).Any(m => m.Verb == "enter" || m.Verb == "near" || m.Verb == "toward"), "an army far too strong comes: the safety rule takes the lord out of its way, as for every order (" + Last() + ")");
            before = moves.Count;
            for (int h = 24; h <= 28; h++) Brain.OnHour(World(h0 + h));
            Move? firstStop = Since(before).Where(m => m.Verb == "sell" || m.Verb == "recruit").Select(m => (Move?)m).FirstOrDefault();
            Check(firstStop?.Verb == "recruit" && firstStop?.Place == "town_orc", "once the danger has passed and a day is out, it goes round again, the town first (" + firstStop + ")");
            lord.X = 120; lord.Y = 130; lord.InSettlement = "town_orc";
            Brain.OnHour(World(h0 + 29));
            Check(Last()?.Verb == "recruit" && Last()?.Place == "village_orc", "then its places again, the nearest due first (" + Last() + ")");
            lord.InSettlement = null; lord.X = 110; lord.Y = 104; lord.Loot = 3600;
            Brain.OnHour(World(h0 + 30));
            Check(Last()?.Verb == "recruit" && Last()?.Place == "town_orc", "with loot worth more than its bar, it goes back to the town at once to sell it there, though it was there today (" + Last() + ")");
            lord.X = 120; lord.Y = 130; lord.InSettlement = "town_orc"; lord.Loot = 0;
            Brain.OnHour(World(h0 + 31));
            Check(Last()?.Verb == "recruit" && Last()?.Place == "castle_two", "the loot sold as it rode in, it goes on to the next of its places (" + Last() + ")");
            Snapshot lost = World(h0 + 32);
            lost.SettlementById["castle_two"].Faction = "player_k";
            lord.InSettlement = null; lord.X = 125; lord.Y = 100;
            Brain.OnHour(lost);
            Check(Last()?.Verb == "recruit" && Last()?.Place == "castle_orc", "a place lost on the way is skipped: with nothing else due it goes home to recruit at its castle (" + Last() + ")");
            Files.FlushAll();
            List<JObject> mine = ReadIf(Path.Combine(Brain.Dir, "notices.jsonl")).Split('\n').Where(l => l.Contains("\"party\":\"p_circ\"") && l.Contains("\"kind\":\"tool\"")).Select(JObject.Parse).ToList();
            Check(mine.Any(n => ((string)n["text"]).StartsWith("Orc Keep is no longer ours or an ally's; skipped.")), "and the agent is told so, in the tool's own words");
            Check(mine.Count == 2 && mine.Any(n => ((string)n["text"]).Contains("falling back") || ((string)n["text"]).Contains("sheltering")),
                  "its only call-backs are the place lost and the safety rule's: " + string.Join(" | ", mine.Select(n => (string)n["text"])));
            Check(!released.Any(r => r.Key == 9500), "the circuit never ends by itself: it goes round until the agent gives the lord another order");
            Runner.Ended(9500, "test over");
        }

        /// <summary>A scope for the language's own tests: names from a table, no functions.</summary>
        private sealed class NameScope : IScope
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object Get(string name) => Values.TryGetValue(name, out object v) ? v : null;
            public object Call(string fn, object[] a) => throw new InvalidOperationException("no function " + fn);
        }
    }
}
