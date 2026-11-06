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
        /// 0.14.2 (Nov 6, the news wake study: 0 of 92 next orders of supply and recruit orders started in run A). Those two end on the game's side,
        /// where only a tool's own end used to start a next order. The game's side itself needs the game; here, the runner's half: what it starts, and when.
        /// </summary>
        private static void NextOrderTests()
        {
            var next = new List<KeyValuePair<ToolOrder, JObject>>();
            Action<ToolOrder, JObject> kept = Runner.NextSink;
            Runner.NextSink = (o, then) => next.Add(new KeyValuePair<ToolOrder, JObject>(o, then));
            var lord = new PartyRow { Id = "p_thor", Leader = "lord_thor", Name = "Thorzag", Faction = "orcs", Kind = "lord", Men = 80, SizeLimit = 300, Strength = 120, Speed = 5, FoodDays = 20, X = 120, Y = 100, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true };
            Diag.Hours = 44000;
            Brain.OnHour(ToolWorld(44000, lord));
            ToolOrder Order(string tool, bool withNext) => new ToolOrder
            {
                Tool = tool, Lord = "lord_thor", LordName = "Thorzag", Party = "p_thor", At = "castle_orc", Realm = "orcs", Poke = 512,
                Then = withNext ? JObject.Parse("{\"do\":\"defend\",\"at\":\"castle_two\"}") : null
            };

            Runner.Start(9890, Order("supply", true));
            Runner.MoveEnded(9890, "handed over 379 food at Orc Castle", true);
            Check(next.Count == 1 && next[0].Key.Lord == "lord_thor" && next[0].Key.Realm == "orcs" && next[0].Key.Source == "agent" && next[0].Key.Poke == 512 && (string)next[0].Value["do"] == "defend",
                  "a supply order that handed over its food starts its next order, for the same lord, realm and poke (" + next.Count + ")");
            Runner.MoveEnded(9890, "handed over 379 food at Orc Castle", true);
            Check(next.Count == 1, "and only once: the tool run is over");
            Files.FlushAll();
            Check(Lines(ReadIf(Path.Combine(Brain.Dir, "toolbook.jsonl"))).Any(x => (string)x["type"] == "run_end" && (int?)x["tool_run"] == 9890 && (string)x["how"] == "done, next order"),
                  "the record says it was done and went on to its next order, as for any tool");

            next.Clear();
            Runner.Start(9891, Order("recruit", true));
            Runner.MoveEnded(9891, "volunteers are within reach, but he has too little gold: 2700, ranks at 29%", true);
            Check(next.Count == 1 && (string)next[0].Value["do"] == "defend", "a recruit order out of money goes on to its next order too");

            next.Clear();
            Runner.Start(9892, Order("supply", true));
            Runner.MoveEnded(9892, "Orc Castle is under siege: he cannot bring food in past the besiegers", false);
            Check(next.Count == 0, "a supply order that did not get there does not start its next order, which assumed it did");

            Runner.Start(9893, Order("supply", false));
            Runner.MoveEnded(9893, "handed over 379 food at Orc Castle", true);
            Check(next.Count == 0, "and an order with no next order starts nothing");

            // a save and a load in between: the next order comes back with the tool
            Runner.Start(9894, Order("recruit", true));
            JObject saved = Runner.Export(9894);
            Runner.Ended(9894, "the game was loaded");
            string why = Runner.Resume(JObject.Parse(saved.ToString()), null);
            Runner.MoveEnded(9894, "the ranks are full (300 men), all of the clan's own troop lines", true);
            Check(why == null && next.Count == 1 && next[0].Key.Poke == 512 && (string)next[0].Value["do"] == "defend",
                  "an order carried across a save still starts its next order (" + (why ?? next.Count.ToString()) + ")");

            // a lord captured, released or replaced goes the old way: no next order
            next.Clear();
            Runner.Start(9895, Order("supply", true));
            Runner.Ended(9895, "the lord was taken prisoner");
            Check(next.Count == 0, "a lord taken prisoner starts no next order");

            Check(Supply.Reached("supplied") && Supply.Reached("stocked") && !Supply.Reached("besieged") && !Supply.Reached("place fell") && !Supply.Reached("nothing to bring") && !Supply.Reached("no holding"),
                  "supply goes on when the food is in or the place needs none, and not when the place was lost or besieged first, or he had nothing to bring");
            string library = Library.Text();
            Check(library.Contains("Its next order (then) starts once the food is in or the place needs none") && library.Contains("Whichever it is, its next order (then) starts"),
                  "the agent's library says when these two go on to their next order");
            Runner.NextSink = kept;
        }
    }
}
