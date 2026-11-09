using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.17.4 (Nov 8, test A1's second start): the standard defend's food steps (feed, brace) are steps, so a supply move that ends on the game's side
        /// hands back to its rules (Runner.StepDone) instead of ending the order, as it still does for the supply order itself; and a holding is fed only up to
        /// what its stores hold when full (food_full_days_at), so a castle full at 3 days is not fed again every 12 hours.
        /// </summary>
        private static void DefendStepTests()
        {
            var moves = new List<KeyValuePair<int, Move>>();
            var released = new List<int>();
            Action<int, Move> keptMove = Runner.MoveSink;
            Action<int, string> keptRelease = Runner.ReleaseSink;
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => released.Add(id);
            Runner.Reset();
            PartyRow Lord(string id, string name) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 300, Strength = 400, Speed = 5, FoodDays = 10,
                X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true, Gold = 5000
            };
            void Hour(double h, float food, float cap, float perDay, params PartyRow[] parties)
            {
                Diag.Hours = h;
                Snapshot s = ToolWorld(h, parties);
                SettlementRow c = s.SettlementById["castle_orc"];
                c.Food = food; c.FoodCap = cap; c.FoodPerDay = perDay;
                Brain.OnHour(s);
            }
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;

            // a castle low on food (50 of 450, 30 a day): the guard rides to feed it, his supply move marked as a step of the defend
            var lugdush = Lord("fd1", "Lugdush");
            double h0 = 70000;
            Hour(h0, 50, 450, 30, lugdush);
            Runner.Start(9970, new ToolOrder { Tool = "defend", Lord = lugdush.Leader, LordName = lugdush.Name, Party = lugdush.Id, At = "castle_orc", Realm = "orcs", Places = { "castle_orc", "village_orc" } });
            double h = h0;
            for (int i = 0; i < 8 && StateOf(9970) != "feed"; i++) Hour(++h, 50, 450, 30, lugdush);
            KeyValuePair<int, Move> feedMove = moves.LastOrDefault(m => m.Key == 9970 && m.Value.Verb == "supply");
            bool feeds = StateOf(9970) == "feed" && feedMove.Key == 9970 && feedMove.Value.Place == "castle_orc" && feedMove.Value.Step;
            // carried on from a save in the middle of the food run, its move is given again marked as a step (0.17.5: on 0.17.4 it was not,
            // and Lothuial's brace at Lasgalor ended once more after the load); and an intervention's upgrade gives an older version the library's
            JObject saved = Runner.Export(9970);
            JObject older = JObject.Parse(saved.ToString());
            older["def"]["json"] = ((string)older["def"]["json"]).Replace("min(days, food_full_days_at(at))", "days");
            Runner.Ended(9970, "the game was loaded");
            moves.Clear();
            Runner.UpgradeAtLoad.Add("defend");
            string why = Runner.Resume(older, null);
            bool upgraded = why == null && Runner.LastResumeUpgraded && Runner.All().FirstOrDefault(x => x.Id == 9970)?.Def.Json == Library.Get("defend").Json && StateOf(9970) == "feed";
            Runner.UpgradeAtLoad.Clear();
            bool resumedStep = moves.Any(m => m.Key == 9970 && m.Value.Verb == "supply" && m.Value.Step);
            Check(upgraded && resumedStep,
                  "a defend carried on from a save takes the library's version when an intervention names it, in the state it stood (" + (why ?? "carried") + ", " + upgraded
                  + "), and its food run is given again as a step (" + resumedStep + ")");
            // the game's side says the run is over (here with nothing handed over, so only "arrived" can move it on): the defend goes on, back to its loop
            Runner.StepDone(9970, "he reached Orc Castle with no food to spare");
            ToolInstance t = Runner.All().FirstOrDefault(x => x.Id == 9970);
            bool done = t != null && t.MoveDone;
            moves.Clear();
            Hour(++h, 50, 450, 30, lugdush);
            Hour(++h, 50, 450, 30, lugdush);
            t = Runner.All().FirstOrDefault(x => x.Id == 9970);
            bool goesOn = t != null && StateOf(9970) != "feed" && !released.Contains(9970) && !t.MoveDone && moves.Any(m => m.Key == 9970 && m.Value.Verb != "supply");
            Check(feeds && done && goesOn,
                  "defend's food run is a step: its supply move is marked so, and when the game's side ends it the order goes on by its rules instead of ending ("
                  + feeds + ", " + done + ", " + goesOn + ": state " + StateOf(9970) + ")");

            // the supply order's own move is its whole job: not a step, so its end still ends the order (and starts its next one)
            var gothmog = Lord("fd2", "Gothmog");
            double h1 = 71000;
            Hour(h1, 50, 450, 30, gothmog);
            moves.Clear();
            Runner.Start(9971, new ToolOrder { Tool = "supply", Lord = gothmog.Leader, LordName = gothmog.Name, Party = gothmog.Id, At = "castle_orc", Realm = "orcs" });
            Hour(h1 + 1, 50, 450, 30, gothmog);
            KeyValuePair<int, Move> supplyMove = moves.FirstOrDefault(m => m.Key == 9971 && m.Value.Verb == "supply");
            Check(supplyMove.Key == 9971 && !supplyMove.Value.Step, "the supply order's move is its whole job, not a step, so the game's side still ends the order when it is done");

            // full stores that last only 3 days (800 of 800, 270 a day): below half of days 10, but nothing more can go in, so the guard is not sent to feed it
            var shagrat = Lord("fd3", "Shagrat");
            double h2 = 72000;
            Hour(h2, 800, 800, 270, shagrat);
            Runner.Start(9972, new ToolOrder { Tool = "defend", Lord = shagrat.Leader, LordName = shagrat.Name, Party = shagrat.Id, At = "castle_orc", Realm = "orcs", Places = { "castle_orc", "village_orc" } });
            var seen = new List<string>();
            for (int i = 1; i <= 30; i++) { Hour(h2 + i, 800, 800, 270, shagrat); seen.Add(StateOf(9972)); }
            // and one that is low within its full stores (100 of 800, 270 a day: under half of the 3 days they hold) is fed
            var gorbag = Lord("fd4", "Gorbag");
            double h3 = 73000;
            Hour(h3, 100, 800, 270, gorbag);
            Runner.Start(9973, new ToolOrder { Tool = "defend", Lord = gorbag.Leader, LordName = gorbag.Name, Party = gorbag.Id, At = "castle_orc", Realm = "orcs", Places = { "castle_orc", "village_orc" } });
            bool lowFed = false;
            for (int i = 1; i <= 8 && !lowFed; i++) { Hour(h3 + i, 100, 800, 270, gorbag); lowFed = StateOf(9973) == "feed"; }
            Check(!seen.Contains("feed") && lowFed && Library.Get("defend").Json.Contains("food_full_days_at"),
                  "a holding is fed only up to what its full stores hold: a castle full at 3 days is not fed again (states " + string.Join(",", seen.Distinct()) + "), one low within them is (" + lowFed + ")");

            // 0.17.5: our siege order given again for the same place keeps the run where it stands and takes the new numbers, partners and next order
            var naltur = Lord("bs1", "Naltûr");
            double h4 = 74000;
            Hour(h4, 800, 800, 270, naltur);
            Runner.Start(9974, new ToolOrder { Tool = "besiege", Lord = naltur.Leader, LordName = naltur.Name, Party = naltur.Id, At = "castle_men", Realm = "orcs", Settings = { ["wait_hours"] = 30 } });
            Hour(h4 + 1, 800, 800, 270, naltur);
            Hour(h4 + 2, 800, 800, 270, naltur);
            string before = StateOf(9974);
            int movesBefore = moves.Count(m => m.Key == 9974);
            var again = new ToolOrder { Tool = "besiege", Lord = naltur.Leader, LordName = naltur.Name, Party = naltur.Id, At = "castle_men", Realm = "orcs", Settings = { ["wait_hours"] = 6 }, Lords = { "p_bs2" },
                                        Then = JObject.Parse("{\"do\": \"defend\", \"at\": \"castle_orc\"}") };
            Runner.GivenAgain(9974, again);
            ToolInstance b = Runner.All().FirstOrDefault(x => x.Id == 9974);
            Check(before != null && b != null && b.State == before && Math.Abs(b.Settings["wait_hours"] - 6) < 1e-9 && b.Order.Lords.Contains("p_bs2") && b.Order.Then != null
                  && moves.Count(m => m.Key == 9974) == movesBefore,
                  "a siege order given again for the same place goes on where it stands (state " + before + " -> " + b?.State + ") with the new numbers, partners and next order, no new move");
            Runner.Ended(9974, "test over");

            Runner.Reset();
            Runner.MoveSink = keptMove;
            Runner.ReleaseSink = keptRelease;
        }
    }
}
