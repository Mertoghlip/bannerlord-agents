using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13.22 (Mert, Nov 1: Dol Guldur's agent gave up Gwígar for "no food" in an answer that took effect about 20 in-game hours after the
        /// report it answered, 13 hours after the food had come): when an answer's orders take effect 6 in-game hours or more after its report,
        /// the next poke says so, and what changed since that report at the places and lords its orders named; once, only for what changed,
        /// never for a lord's own moves.
        /// </summary>
        private static void LateAnswerTests()
        {
            var dur = new PartyRow { Id = "p_dur", Leader = "lord_dur", Name = "Durnia", Faction = "orcs", Kind = "lord", Men = 300, Strength = 500, Speed = 5, FoodDays = 30, X = 120, Y = 100, Doing = "Hold", Spottable = true };
            var other = new PartyRow { Id = "p_oth", Leader = "lord_oth", Name = "Gorbag", Faction = "orcs", Kind = "lord", Men = 300, Strength = 500, Speed = 5, FoodDays = 30, X = 130, Y = 90, Doing = "Hold", Spottable = true };
            var foe = new PartyRow { Id = "p_cel", Leader = "lord_cel", Name = "Baranthir", Faction = "player_k", Kind = "lord", Men = 900, Strength = 1800, Speed = 4, FoodDays = 10, X = 110, Y = 104, Doing = "Hold", Spottable = true };
            Snapshot s1 = ToolWorld(43900, dur, other, foe);
            SettlementRow c1 = s1.SettlementById["castle_orc"];
            c1.Food = 0; c1.FoodCap = 450; c1.FoodPerDay = 24;
            View v1 = Views.Build(s1, "orcs", s1.WatchedSees, s1.HostileToWatched, null);
            // twenty hours on: the castle has its food, Durnia lost a third of his men, Baranthir marches on Orc Keep; Gorbag, not named, lost men too
            dur.Men = 200; other.Men = 100;
            foe.Doing = "BesiegeSettlement"; foe.TargetSettlement = "castle_two"; foe.X = 136; foe.Y = 100;
            Snapshot s2 = ToolWorld(43920, dur, other, foe);
            SettlementRow c2 = s2.SettlementById["castle_orc"];
            c2.Food = 219; c2.FoodCap = 450; c2.FoodPerDay = 24;
            View v2 = Views.Build(s2, "orcs", s2.WatchedSees, s2.HostileToWatched, null);

            var orders = new List<AgentChannel.AgentOrder>
            {
                new AgentChannel.AgentOrder { Lord = "lord_dur", Do = "goto", At = "castle_two", Poke = 500 },
                new AgentChannel.AgentOrder { Lord = "Durnia", Do = "defend", At = "[castle_orc]", Poke = 500, Then = Newtonsoft.Json.Linq.JObject.Parse("{\"do\": \"engage\", \"target\": \"lord_cel\"}") }
            };
            var book = new LateAnswer.Book();
            book.Poked(500, 43900, v1);
            book.Answered(500, orders);
            book.Applied(500, 43918);
            string told = book.Tell(v2, 501, "orcs");
            string again = book.Tell(v2, 502, "orcs");
            bool says = told != null && told.StartsWith("Your answer to poke #500 took effect late") && told.Contains("18 in-game hours later")
                        && told.Contains("- Orc Castle [castle_orc]: food 0 of 450 (about 0 days of siege) then, 219 of 450 (about 9 days of siege) now")
                        && told.Contains("- Durnia [p_dur]: 300 men then, 200 now") && told.Contains("- Baranthir [p_cel]: ") && !told.Contains("Gorbag");
            // on time, nothing told; late but with nothing changed where its orders act, nothing told either
            var onTime = new LateAnswer.Book();
            onTime.Poked(600, 43900, v1);
            onTime.Answered(600, orders);
            onTime.Applied(600, 43903);
            var quiet = new LateAnswer.Book();
            quiet.Poked(700, 43900, v1);
            quiet.Answered(700, new[] { new AgentChannel.AgentOrder { Lord = "lord_oth", Do = "goto", At = "castle_men", Poke = 700 } });
            quiet.Applied(700, 43915);
            Check(says && again == null && onTime.Tell(v2, 601, "orcs") == null && quiet.Tell(v1, 701, "orcs") == null,
                  "an answer that took effect 18 in-game hours after its report is told in the next poke, once, with what changed since that report at the places and lords "
                  + "its orders (and their next orders) named, and not for a lord not named; an answer on time, or one where nothing it named changed, is not: " + told?.Replace("\n", " | "));
        }

        /// <summary>
        /// 0.13.22 (Mert, Nov 1: the lieutenant had never been told the words its own tools are written in): the agents' and the lieutenant's
        /// instructions each list every move, name and function of the tool language, so neither can fall behind the library again.
        /// </summary>
        private static void LanguageWordsTests()
        {
            foreach ((string file, string heading) in new[] { ("warroom-system.template.md", "## Writing your own tools"), ("lieutenant-system.template.md", "## The tool language") })
            {
                string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "agent", file));
                if (!File.Exists(path)) { Check(false, "the instructions' template is where the test looks for it (" + path + ")"); continue; }
                string text = File.ReadAllText(path);
                int a = text.IndexOf(heading, StringComparison.Ordinal), b = a < 0 ? -1 : text.IndexOf("\n## ", a + 3, StringComparison.Ordinal);
                string section = a < 0 ? "" : b < 0 ? text.Substring(a) : text.Substring(a, b - a);
                var missing = ToolDef.Verbs.Keys.Where(v => !section.Contains("`" + v)).Select(v => "move " + v)
                    .Concat(ToolDef.Functions.Keys.Where(f => !section.Contains("`" + f + "(")).Select(f => "function " + f + "()"))
                    .Concat(ToolDef.BuiltIns.Where(n => !section.Contains("`" + n + "`")).Select(n => "name " + n)).ToList();
                Check(section.Length > 0 && missing.Count == 0, file + " lists every move, name and function of the tool language under " + heading
                      + (section.Length == 0 ? " (no such section)" : missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : ""));
            }
        }
    }
}
