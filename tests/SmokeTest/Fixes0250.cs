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
        /// Module 0.25.0 (Nov 26, the tooling audit of run 20261125-2341-C2): each fix's own check on the made-up world, as the fault list names it. The items whose code
        /// runs only in the game (the refusal of a lord called to an army, partners left out of an order, the vote reminder, the run-finished mark, the day's timing) are
        /// built but not tried here.
        /// </summary>
        private static void Fixes0250Tests(string root)
        {
            string dir = Path.Combine(root, "fix250_orcs");
            var cfg = AgentConfig.FromSettings("orcs", dir, "live", "all");
            Brain.Reset("smoketest-250", double.NaN, new List<AgentConfig> { cfg }, null);
            var moves = new List<KeyValuePair<int, Move>>();
            Action<int, Move> keptMove = Runner.MoveSink;
            Action<int, string> keptRelease = Runner.ReleaseSink;
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => { };
            Runner.Reset();
            string Notices() { Files.FlushAll(); return ReadIf(Path.Combine(Brain.Dir, "notices.jsonl")); }
            PartyRow Own(string id, string name, float x, float y, float strength, int food = 20) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), SizeLimit = 1000, Strength = strength, Speed = 5,
                FoodDays = food, FoodItems = food * 10, FoodEats = 10, X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 3000
            };
            PartyRow Foe(string id, string name, float x, float y, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = (int)(strength / 2), Strength = strength, Speed = 4, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            ToolInstance Inst(int id) => Runner.All().FirstOrDefault(x => x.Id == id);

            // ---------- item 1: food trips go to a market with food for sale, buy at its gate, and give up only when no closer ----------
            Snapshot FoodWorld(double h, params PartyRow[] ps)
            {
                Snapshot s = ToolWorld(h, ps);
                Add(s, new SettlementRow { Id = "village_dry", Name = "Dry Village", Kind = "village", Faction = "orcs", X = 104, Y = 140, Bound = "castle_orc", VillageState = "Normal", MarketFood = 0 });
                s.SettlementById["village_orc"].MarketFood = 120;
                return s;
            }
            var hungry = Own("hgr", "Gorbag", 100, 140, 600, food: 0);
            double h = 80000;
            Diag.Hours = h;
            Snapshot fw = FoodWorld(h, hungry);
            Brain.OnHour(fw);
            World w0 = Runner.WorldFor(fw);
            SettlementRow market = w0.FoodMarket(hungry, null, out double marketHours);
            Check(market?.Id == "village_orc" && !w0.IsMarket(fw.SettlementById["village_dry"]) && !double.IsNaN(marketHours),
                  "item 1: of a village with no food for sale (4 away) and one that sells food (36 away), the food rule's market is the one that sells (" + market?.Id + ")");
            Runner.Start(25001, new ToolOrder { Tool = "goto", Lord = hungry.Leader, LordName = hungry.Name, Party = hungry.Id, At = "castle_two", Realm = "orcs" });
            Brain.OnHour(FoodWorld(++h, hungry));
            bool buys = moves.Any(m => m.Key == 25001 && m.Value.Verb == "buy" && m.Value.Place == "village_orc");
            bool neverDry = !moves.Any(m => m.Key == 25001 && m.Value.Place == "village_dry");
            Check(buys && neverDry && Inst(25001)?.SupplyAt == "village_orc", "item 1: a lord with no food rides to buy at the village that sells food, never at the one that has none");
            // the game's side bought nothing there: the trip ends and that market is left out, so with no other market he makes no trip to the dry village
            Runner.FoodTrip(25001, 0, "could buy no food");
            moves.Clear();
            for (int k = 0; k < 3; k++) Brain.OnHour(FoodWorld(++h, hungry));
            Check(Inst(25001)?.SupplyAt == null && !moves.Any(m => m.Key == 25001 && m.Value.Verb == "buy"), "item 1: a market where he could buy nothing is left out for three days, and the dry village is never picked");
            Runner.Ended(25001, "test over");
            // a trip that buys ends once he carries food
            var hungry2 = Own("hg2", "Lugdush", 100, 140, 600, food: 0);
            Brain.OnHour(FoodWorld(++h, hungry2));
            Runner.Start(25002, new ToolOrder { Tool = "goto", Lord = hungry2.Leader, LordName = hungry2.Name, Party = hungry2.Id, At = "castle_two", Realm = "orcs" });
            Brain.OnHour(FoodWorld(++h, hungry2));
            Runner.FoodTrip(25002, 60, "bought 60 food");
            hungry2.FoodDays = 6;
            moves.Clear();
            Brain.OnHour(FoodWorld(++h, hungry2));
            Check(Inst(25002)?.SupplyAt == null && moves.Any(m => m.Key == 25002 && m.Value.Verb != "buy"), "item 1: the trip ends once the food is bought at the gate, and the order takes up its own move");
            Runner.Ended(25002, "test over");
            // a trip that gets no closer for 6 hours gives up (it was a fixed 24, even while closing in)
            var hungry3 = Own("hg3", "Shagrat", 100, 140, 600, food: 0);
            Brain.OnHour(FoodWorld(++h, hungry3));
            Runner.Start(25003, new ToolOrder { Tool = "goto", Lord = hungry3.Leader, LordName = hungry3.Name, Party = hungry3.Id, At = "castle_two", Realm = "orcs" });
            Brain.OnHour(FoodWorld(++h, hungry3));
            bool started3 = Inst(25003)?.SupplyAt == "village_orc";
            for (int k = 0; k < 4; k++) Brain.OnHour(FoodWorld(++h, hungry3));
            bool still5 = Inst(25003)?.SupplyAt == "village_orc";
            Brain.OnHour(FoodWorld(++h, hungry3));
            Check(started3 && still5 && Inst(25003)?.SupplyAt == null, "item 1: standing still 5 hours keeps the trip; at 6 hours with no step closer it gives up (" + started3 + ", " + still5 + ")");
            Runner.Ended(25003, "test over");
            // a lord with no food losing men is told of once, and the line wakes the agent
            var starving = Own("stv", "Ugluk", 100, 140, 600, food: 0);
            Brain.OnHour(FoodWorld(++h, starving));
            Runner.Start(25004, new ToolOrder { Tool = "hold", Lord = starving.Leader, LordName = starving.Name, Party = starving.Id, Realm = "orcs" });
            Brain.OnHour(FoodWorld(++h, starving));
            starving.Men -= 47;
            Brain.OnHour(FoodWorld(++h, starving));
            starving.Men -= 20;
            Brain.OnHour(FoodWorld(++h, starving));
            int told = System.Text.RegularExpressions.Regex.Matches(Notices(), "Ugluk has food for 0 days and lost").Count;
            var starveLine = new Trigger { Hours = h, Kind = "tool", Text = "Ugluk has food for 0 days and lost 47 men in an hour; the nearest market with food is Orc Village, 9 hours by road." };
            WakeRules.Sort(starveLine);
            WakeRules.SortSelf(starveLine, new HashSet<string>(), "orcs");
            Check(told == 1 && Notices().Contains("Ugluk has food for 0 days and lost 47 men") && Notices().Contains("the nearest market with food is Orc Village") && starveLine.Lane == WakeRules.Now,
                  "item 1: a lord with no food who starts losing men is told of once, with the nearest market with food, and it wakes the agent, a self-waking one too (" + told + ", " + starveLine.Lane + ")");
            Runner.Ended(25004, "test over");

            // ---------- item 2: standing still is counted by where he stands across the whole order ----------
            ToolDef stepper = ToolDef.FromJson(@"{ ""name"": ""stepper"", ""kind"": ""tool"", ""about"": ""goes to a holding, changing its step every 6 hours"", ""at"": ""any_holding"", ""start"": ""a"", ""states"": {
                ""a"": { ""about"": ""going to {at}"", ""do"": ""goto at"", ""when"": [ { ""if"": ""hours_in_state >= 6"", ""go"": ""b"" } ] },
                ""b"": { ""about"": ""going close to {at}"", ""do"": ""near at"", ""when"": [ { ""if"": ""hours_in_state >= 6"", ""go"": ""a"" } ] } } }", "stepper", "orcs");
            Check(stepper.Valid, "item 2: a test order that changes its step every 6 hours passes the checker: " + string.Join("; ", stepper.Problems));
            var still = Own("stl", "Grishnakh", 100, 60, 500);
            still.Doing = "GoToSettlement";
            still.TargetSettlement = "castle_men";
            double h2 = 81000;
            Brain.OnHour(ToolWorld(h2, still));
            Runner.Start(25010, new ToolOrder { Tool = "stepper", Def = stepper, Lord = still.Leader, LordName = still.Name, Party = still.Id, At = "castle_men", Realm = "orcs" });
            string Stalls() => System.Text.RegularExpressions.Regex.Matches(Notices(), "Grishnakh has not moved for").Count.ToString();
            for (int k = 1; k <= 12; k++) Brain.OnHour(ToolWorld(h2 + k, still));
            bool notAt12 = Stalls() == "0";
            Brain.OnHour(ToolWorld(h2 + 13, still));
            var stallLine = new Trigger { Hours = h2 + 13, Kind = "tool", Text = "Grishnakh has not moved for 12 hours, 85 from Fornost; he may be stuck. His order is kept." };
            WakeRules.Sort(stallLine);
            WakeRules.SortSelf(stallLine, new HashSet<string>(), "orcs");
            Check(notAt12 && Stalls() == "1" && Notices().Contains("Grishnakh has not moved for 12 hours") && Notices().Contains("he may be stuck. His order is kept.") && stallLine.Lane == WakeRules.Now,
                  "item 2: a lord whose position never changes while his order changes step every 6 hours is told of at hour 12, in one line that wakes the agent (" + Stalls() + ")");
            Snapshot sv = ToolWorld(h2 + 14, still);
            Brain.OnHour(sv);
            View stillView = Views.Build(sv, "orcs", sv.WatchedSees, sv.HostileToWatched, Brain.Get("orcs"));
            ViewParty sp = stillView.Forces.Concat(stillView.Forces.Where(f => f.Members != null).SelectMany(f => f.Members)).FirstOrDefault(x => x.Id == still.Id);
            Check(sp != null && sp.Arrives != null && sp.Arrives.StartsWith("has not moved for 1", StringComparison.Ordinal) && !sp.Arrives.Contains("there in about") && double.IsNaN(sp.ArrivesAt),
                  "item 2: his page says how long he has not moved, with no arrival time: " + sp?.Arrives);
            Runner.Ended(25010, "test over");

            // ---------- item 3: the siege-lift bar weighs this siege's lords within 6, and waits for the camp ----------
            var val = Own("val", "Val", 168, 100, 191);
            val.Besieging = "castle_men";
            var bulk = Own("blk", "Durbag", 165, 101, 8431);
            var relief = Foe("rlf", "Elladan", 195, 100, 1000);
            relief.Doing = "GoToSettlement";
            relief.TargetSettlement = "castle_men";
            double h3 = 82000;
            Snapshot S3(double hh) { Snapshot s = ToolWorld(hh, val, bulk, relief); return s; }
            Brain.OnHour(S3(h3));
            var lift = new Dictionary<string, double> { ["withdraw_on_relief"] = 0.9, ["wait_hours"] = 0, ["storm"] = 2, ["storm_guard"] = 0 };
            var o1 = new ToolOrder { Tool = "besiege", Lord = val.Leader, LordName = val.Name, Party = val.Id, At = "castle_men", Realm = "orcs" };
            var o2 = new ToolOrder { Tool = "besiege", Lord = bulk.Leader, LordName = bulk.Name, Party = bulk.Id, At = "castle_men", Realm = "orcs" };
            foreach (KeyValuePair<string, double> kv in lift) { o1.Settings[kv.Key] = kv.Value; o2.Settings[kv.Key] = kv.Value; }
            Runner.Start(25020, o1);
            Runner.Start(25021, o2);
            for (int k = 1; k <= 5; k++) Brain.OnHour(S3(h3 + k));
            Check(!Notices().Contains("lifts the siege of Fornost") && Inst(25020) != null && !Inst(25020).Finished,
                  "item 3: one lord camped (191), another of the siege within 6 (8,431) and a small relief (1,000): no lift, in the first hours or after (" + Inst(25020)?.State + ")");
            Check(Library.Get("besiege").Json.Contains("now >= camp_at + 2 or camp_strength(at) * 2 >= max(camp_strength(at), mates_strength(at, 6))"),
                  "item 3: the lift bar is weighed only once the camp has stood 2 hours or holds half the siege's strength");
            Runner.Ended(25020, "test over");
            Runner.Ended(25021, "test over");

            // ---------- item 7: a lord in shelter keeps the danger range he went in with; an enemy marches on a place only once it keeps to it ----------
            var guard = Own("grd", "Muzgash", 100, 124, 400);
            var raider = Foe("rdr", "Halbarad", 100, 135, 1100);
            double h7 = 83000;
            Brain.OnHour(ToolWorld(h7, guard, raider));
            var go = new ToolOrder { Tool = "goto", Lord = guard.Leader, LordName = guard.Name, Party = guard.Id, At = "castle_two", Realm = "orcs" };
            go.Settings["danger_hours"] = 10;
            Runner.Start(25030, go);
            Brain.OnHour(ToolWorld(h7 + 1, guard, raider));
            ToolInstance gi = Inst(25030);
            bool wentIn = gi != null && gi.Shelter != null;
            double kept = gi?.ShelterReach ?? double.NaN;
            guard.X = 100; guard.Y = 100; guard.InSettlement = gi?.Shelter == "castle_orc" ? "castle_orc" : null;
            for (int k = 2; k <= 10; k++) Brain.OnHour(ToolWorld(h7 + k, guard, raider));
            Check(wentIn && Inst(25030)?.Shelter != null && kept > 3,
                  "item 7: a lord in shelter, with the enemy standing about 35 away (beyond the 3 hours his range inside would shrink to), stays in, his kept range " + Math.Round(kept, 1) + " hours (" + wentIn + ", " + Inst(25030)?.Shelter + ")");
            Runner.Ended(25030, "test over");
            var marcher = Foe("mrc", "Elrohir", 130, 100, 2000);
            marcher.ArmyLeader = marcher.Id; marcher.ArmyMen = 1500; marcher.ArmyParties = 3; marcher.ArmyStrength = 2000;
            marcher.Doing = "GoToSettlement"; marcher.TargetSettlement = "castle_orc";
            double h7b = 83100;
            string March(double hh) { Snapshot s = ToolWorld(hh, marcher); return Runner.MarchedOn(Runner.WorldFor(s), "castle_orc", 60); }
            string m1 = March(h7b), m2 = March(h7b + 1), m3 = March(h7b + 2);
            marcher.TargetSettlement = "castle_two";
            string m4 = March(h7b + 3), m9 = March(h7b + 8), m10 = March(h7b + 9);
            Check(m1 == null && m2 == null && m3 == marcher.Id && m4 == marcher.Id && m9 == marcher.Id && m10 == null,
                  "item 7: an enemy marches on a holding only once it has headed there 2 hours running, and counts until 6 hours after it stopped (" + m1 + ", " + m2 + ", " + m3 + ", " + m4 + ", " + m9 + ", " + m10 + ")");
            Check(!Library.Get("defend").Json.Contains("\"shelter\", \"set\": { \"hs\""), "item 7: the guard order no longer works out its danger range again while it shelters");

            // ---------- item 8: a strike waiting for its enemy does not fall back from it; the group count counts each army once and no sheltering lord ----------
            ToolDef waiter = ToolDef.FromJson(@"{ ""name"": ""waiting_strike"", ""kind"": ""tool"", ""about"": ""waits near the strongest enemy, then strikes it"", ""at"": ""any_holding"",
                ""init"": { ""prey"": ""strongest_enemy_near(me, 40)"" }, ""start"": ""wait"", ""states"": {
                ""wait"": { ""about"": ""waiting for {prey}"", ""do"": ""hold"", ""when"": [ { ""if"": ""prey != none and distance(me, prey) < 1"", ""go"": ""hit"" } ] },
                ""hit"": { ""about"": ""striking {prey}"", ""do"": ""engage prey"", ""when"": [ { ""if"": ""gone(prey)"", ""go"": ""done"" } ] },
                ""done"": { ""about"": ""done"", ""end"": ""free"" } } }", "waiting_strike", "orcs");
            var striker = Own("stk", "Thranduk", 120, 100, 400);
            var prey = Foe("pry", "Lorgath", 126, 100, 3000);
            double h8 = 84000;
            Brain.OnHour(ToolWorld(h8, striker, prey));
            Runner.Start(25040, new ToolOrder { Tool = "waiting_strike", Def = waiter, Lord = striker.Leader, LordName = striker.Name, Party = striker.Id, At = "castle_men", Realm = "orcs" });
            for (int k = 1; k <= 3; k++) Brain.OnHour(ToolWorld(h8 + k, striker, prey));
            Check(waiter.Valid && Inst(25040)?.Shelter == null && !Notices().Contains("Thranduk is falling back") && !Notices().Contains("Thranduk is sheltering"),
                  "item 8: a strike lord waiting near his target (3,000 against his 400, caution on) does not fall back from it: " + string.Join("; ", waiter.Problems));
            Runner.Ended(25040, "test over");
            ToolDef counter = ToolDef.FromJson(@"{ ""name"": ""group_count"", ""kind"": ""tool"", ""about"": ""says the group's strength"", ""at"": ""any_holding"", ""lords"": true, ""start"": ""a"", ""states"": {
                ""a"": { ""about"": ""counting"", ""do"": ""hold"", ""when"": [ { ""if"": ""hours_running >= 1"", ""go"": ""b"", ""poke"": ""Group strength {group_strength(at, 60)}, by road within 30 hours {group_within(at, 30)}."" } ] },
                ""b"": { ""about"": ""done"", ""do"": ""hold"" } } }", "group_count", "orcs");
            var head = Own("ghd", "Ufthak", 130, 100, 500);
            var hidden = Own("ghi", "Snaga", 132, 100, 300);
            var leader = Own("gld", "Radbug", 128, 102, 300);
            leader.ArmyLeader = leader.Id; leader.ArmyMen = 450; leader.ArmyParties = 2; leader.ArmyStrength = 900;
            var member = Own("gmb", "Lagduf", 128, 102, 300);
            member.ArmyLeader = leader.Id; member.Attached = true;
            double h8b = 84100;
            Brain.OnHour(ToolWorld(h8b, head, hidden, leader, member));
            Runner.Start(25041, new ToolOrder { Tool = "hold", Lord = hidden.Leader, LordName = hidden.Name, Party = hidden.Id, Realm = "orcs" });
            Inst(25041).Shelter = "castle_two";
            Inst(25041).AsideSince = h8b + 100;   // kept in shelter for the test
            var gco = new ToolOrder { Tool = "group_count", Def = counter, Lord = head.Leader, LordName = head.Name, Party = head.Id, At = "castle_two", Realm = "orcs" };
            gco.Lords.AddRange(new[] { hidden.Id, leader.Id, member.Id });
            Runner.Start(25042, gco);
            Brain.OnHour(ToolWorld(h8b + 1, head, hidden, leader, member));
            Brain.OnHour(ToolWorld(h8b + 2, head, hidden, leader, member));
            Check(counter.Valid && Notices().Contains("Group strength 1,400") && Notices().Contains("by road within 30 hours 1,400"),
                  "item 8: the group count with one lord sheltering and an army whose leader and member share the order counts the army once and skips the sheltering lord (500 + 900): "
                  + string.Join("; ", counter.Problems));
            Runner.Ended(25041, "test over");
            Runner.Ended(25042, "test over");

            // ---------- item 11: wake answers and review answers each have their own delay ----------
            Channel ch = AgentChannel.For("orcs");
            double h11 = 85000;
            for (int k = 0; k < 6; k++)
            {
                int n = 9000 + k;
                ch.OnPoke(n, h11 + 30 * k, new List<Trigger> { new Trigger { Hours = h11 + 30 * k, Kind = "quiet_day", Text = "A full day." } }, "report");
                ch.Receipt(new AgentChannel.AgentOrder { Poke = n, File = "poke-" + n + ".json", Lord = "lord_x", Do = "hold", Received = DateTime.Now }, "applied", "held", h11 + 30 * k + 8);
                ch.BriefingWritten(h11 + 30 * k + 2);
                ch.ReviewAnswerRead("poke-" + n + "-council-1.json", n);
                ch.Receipt(new AgentChannel.AgentOrder { Poke = n, File = "poke-" + n + "-council-1.json", Lord = "lord_x", Do = "hold", Received = DateTime.Now }, "applied", "held", h11 + 30 * k + 18);
            }
            ch.Lag(out double wu, out double wl);
            ch.ReviewLag(out double ru, out double rl);
            string lagLine = Views.LagLine(wu, wl, ru, rl);
            Check(Math.Abs(wu - 8) < 1e-6 && Math.Abs(ru - 16) < 1e-6 && lagLine.Contains("Wake answers: about 8 hours (8 for one in ten).") && lagLine.Contains("Review answers: about 16 hours (16 for one in ten).")
                  && Math.Abs(ch.UsualLagNow - 8) < 1e-6,
                  "item 11: wake answers (8 hours from their wake) and review answers (16 from their briefing) are measured apart and both said: " + lagLine);
            // item 41: a next order's receipt says what it is, and is no answer delay
            ch.Receipt(new AgentChannel.AgentOrder { Poke = 9000, File = "then", Lord = "lord_x", Do = "goto", Received = DateTime.Now }, "applied", "goes on", h11 + 299);
            Files.FlushAll();
            string receipts = ReadIf(Path.Combine(dir, "receipts.log"));
            ch.Lag(out double wu2, out _);
            Check(receipts.Contains("A next order (then), started when the order before it ended") && Math.Abs(wu2 - 8) < 1e-6,
                  "item 41: a follow-on order's receipt has its own words and stays out of the delay figures");

            // ---------- item 12: the fresh book in steps; a withdrawn request skipped; a briefing writes the hour's book ----------
            var b1 = Own("bk1", "Bolg", 104, 100, 500);
            double h12 = 86000;
            Brain.OnHour(ToolWorld(h12, b1));
            string orders = Path.Combine(dir, "orders");
            Directory.CreateDirectory(orders);
            int finished0 = Writes.Finished;
            File.WriteAllText(Path.Combine(orders, "book-request-w1.json"), "{}");
            AgentChannel.Scan();
            File.WriteAllText(Path.Combine(orders, "book-request-w1.withdrawn"), "");
            int pending = Writes.Pending;
            Writes.Drain();
            string stamp = ReadIf(Path.Combine(dir, "book", "now.txt"));
            Check(pending == 1 && !stamp.StartsWith("book-request-w1", StringComparison.Ordinal) && !File.Exists(Path.Combine(orders, "book-request-w1.withdrawn")),
                  "item 12: a request the war room withdrew before its book was written is skipped, and its mark taken away (contract (c))");
            File.WriteAllText(Path.Combine(orders, Channel.BriefingRequest + "-r1.json"), "{}");
            AgentChannel.Scan();
            var stepsTaken = 0;
            for (Action a = Writes.Next(); a != null; a = Writes.Next()) { a(); stepsTaken++; }
            string nowBook = Path.Combine(dir, "book", "now.json");
            bool bookByBriefing = File.Exists(nowBook) && ReadIf(nowBook).Contains("\"date\":\"" + Text.When(h12) + "\"");
            DateTime builtAt = File.Exists(nowBook) ? File.GetLastWriteTimeUtc(nowBook) : DateTime.MinValue;
            System.Threading.Thread.Sleep(30);
            File.WriteAllText(Path.Combine(orders, "book-request-r2.json"), "{}");
            AgentChannel.Scan();
            Writes.Drain();
            stamp = ReadIf(Path.Combine(dir, "book", "now.txt"));
            Check(stepsTaken >= 10 && bookByBriefing && stamp.StartsWith("book-request-r2\n" + h12.ToString("R", Text.Inv), StringComparison.Ordinal) && File.GetLastWriteTimeUtc(nowBook) == builtAt
                  && File.Exists(Path.Combine(dir, "council.txt")),
                  "item 12: a briefing is written in " + stepsTaken + " steps between other jobs and writes that hour's book with it, so the review's own request right after is answered with its stamp alone");

            // ---------- item 13: big quiet news rides the next poke as since_last; a big battle wakes the agent at once ----------
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h12 + 1, Kind = "captured", Text = "Lek (Misty Mountain Orcs) was taken prisoner by Skulgar (Dol Guldur).", Factions = { "orcs" },
                                        Since = new Dictionary<string, string> { ["orcs"] = "Your lord Lek was taken prisoner by Skulgar (Dol Guldur)." } });
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h12 + 1, Kind = "battle_big", Text = "Imnagath battle lost: 3 of your lords taken (Lek, Rad and Muz), 1,320 men lost; they are held by Skulgar, at Imnagath.", Factions = { "orcs" } });
            Brain.OnHour(ToolWorld(h12 + 1, b1));
            Files.FlushAll();
            string pokeJson = ReadIf(Path.Combine(dir, "poke.json"));
            var bigLine = new Trigger { Hours = h12 + 1, Kind = "battle_big", Text = "Imnagath battle lost: 3 of your lords taken." };
            WakeRules.Sort(bigLine);
            WakeRules.SortSelf(bigLine, new HashSet<string>(), "orcs");
            Check(pokeJson.Contains("\"since_last\":[\"Your lord Lek was taken prisoner by Skulgar (Dol Guldur).\"]") && bigLine.Lane == WakeRules.Now && ReadIf(Path.Combine(dir, "poke.txt")).Contains("Imnagath battle lost"),
                  "item 13: a capture of one of the realm's lords rides the next poke as since_last, and a battle with 3 lords taken wakes the agent at once: " + pokeJson.Substring(0, Math.Min(160, pokeJson.Length)));
            Answer250(dir, ch.LastPoke);
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h12 + 30, Kind = "holding_changed", Text = "Orc Keep passed from Misty Mountain Orcs to Kingdom of Arnor and Gondor.", Factions = { "orcs" } });
            Brain.OnHour(ToolWorld(h12 + 30, b1));
            Files.FlushAll();
            Check(ReadIf(Path.Combine(dir, "poke.json")).Contains("\"since_last\":[]"), "item 13: the next poke's since_last is empty when nothing big happened since");

            // ---------- item 14: an answer written after the save, to a wake from before it, is taken at the load ----------
            string loadDir = Path.Combine(root, "fix250_load");
            var lcfg = AgentConfig.FromSettings("orcs", loadDir, "live", "all");
            DateTime savedUtc = DateTime.UtcNow.AddMinutes(-10);
            CarryOn.SetSaveMark(new JObject { ["wall_utc"] = savedUtc.ToString("o"), ["pokes"] = new JObject { ["orcs"] = 13293 }, ["answered"] = new JObject { ["orcs"] = 13290 } });
            Directory.CreateDirectory(Path.Combine(loadDir, "orders"));
            string after = Path.Combine(loadDir, "orders", "poke-13293-council-051436.json");
            File.WriteAllText(after, "{\"poke\": 13293, \"kind\": \"council\", \"orders\": []}");
            File.SetLastWriteTimeUtc(after, savedUtc.AddSeconds(16));
            string later = Path.Combine(loadDir, "orders", "poke-13300.json");
            File.WriteAllText(later, "{\"poke\": 13300, \"orders\": []}");
            File.SetLastWriteTimeUtc(later, savedUtc.AddMinutes(5));
            Brain.Reset("smoketest-250-load", double.NaN, new List<AgentConfig> { lcfg }, null);
            Channel lch = AgentChannel.For("orcs");
            lch.CheckLeftovers();
            string note = lch.LoadAnswerNote();
            Check(File.Exists(after) && !File.Exists(later) && note.Contains("Your answer to #13293 was written after the save") && note.Contains("reached the game")
                  && note.Contains("Your answer to #13300") && note.Contains("did not reach the game"),
                  "item 14: at the load an answer written after the save to a wake at or before it is taken; one to a later wake is set aside; the first look says which: " + note);
            CarryOn.SetSaveMark(null);
            Check(!CarryOn.TakesAtLoad("orcs", DateTime.UtcNow, 1), "item 14: with no save mark (a save of before 0.25.0) nothing from before the session is taken");
            Brain.Reset("smoketest-250", double.NaN, new List<AgentConfig> { cfg }, null);

            // ---------- item 16: a lord called to an army says so on his page ----------
            var called = Own("cld", "Lashonek", 120, 100, 400);
            var gather = Own("gjn", "Garjan", 150, 100, 900);
            gather.ArmyLeader = gather.Id; gather.ArmyMen = 450; gather.ArmyParties = 1; gather.ArmyStrength = 900;
            called.ArmyLeader = gather.Id;
            called.Doing = "EscortParty"; called.TargetParty = gather.Id;
            Snapshot s16 = ToolWorld(87000, called, gather);
            Check(Views.Doing(s16, called) == "called to Garjan's army, marching to join it", "item 16: a lord called to an army and marching to join it says so: " + Views.Doing(s16, called));

            // ---------- item 20: the feed rule chooses the market by the food it can sell for each extra hour ----------
            var feeder = Own("fdr", "Asta", 100, 100, 400);
            feeder.FoodItems = 0;
            Snapshot s20 = ToolWorld(87100, feeder);
            SettlementRow keep = s20.SettlementById["castle_orc"];
            keep.Food = 6; keep.FoodCap = 800; keep.FoodPerDay = 10;
            s20.SettlementById["village_orc"].MarketFood = 5;
            Add(s20, new SettlementRow { Id = "town_far", Name = "Far Market", Kind = "town", Faction = "orcs", X = 100, Y = 125, MarketFood = 300 });
            World w20 = Runner.WorldFor(s20);
            SettlementRow best = w20.FeedMarket(feeder, keep, 10, 3, out double gain);
            s20.SettlementById["town_far"].MarketFood = 0;
            World w20b = Runner.WorldFor(s20);
            SettlementRow none = w20b.FeedMarket(feeder, keep, 10, 3, out double gainNone);
            Check(best?.Id == "town_far" && gain > 5 && none == null && gainNone < 0.5,
                  "item 20: of a market near with 5 food and one farther with 300, the guard rides to the second; with only the near one, the trip would add under half a day (" + best?.Id + ", " + Math.Round(gain, 1) + ", " + Math.Round(gainNone, 2) + ")");
            Check(Library.Get("defend").Json.Contains("feed_gain(at) < 0.5") && Library.Get("defend").Json.Contains("now + 48"), "item 20: such a trip is not made: the agent is told once and it is tried again 2 days later");

            // ---------- item 21: a followed lord's routine line wakes once a day ----------
            var follows = new HashSet<string> { "p_fol" };
            Trigger Line(double hh, string text) { var t = new Trigger { Hours = hh, Kind = "tool", Text = text, Party = "p_fol" }; WakeRules.Sort(t); WakeRules.SortSelf(t, follows, "orcs"); return t; }
            string siegeLine = "Orc Keep is besieged by strength {0}, too strong to break in time; Snaga shelters in Orc Castle.";
            List<string> lanes = new List<string>();
            for (int k = 0; k < 6; k++) lanes.Add(Line(88000 + k, string.Format(siegeLine, 4000 + 100 * k)).Lane);
            string lane25 = Line(88025, string.Format(siegeLine, 5000)).Lane;
            Check(lanes[0] == WakeRules.Now && lanes.Skip(1).All(l => l == WakeRules.Digest) && lane25 == WakeRules.Now,
                  "item 21: the same siege line six hours running, numbers aside, gives one wake; a day later it wakes again (" + string.Join(",", lanes) + ", " + lane25 + ")");
            Check(Library.Get("besiege").Json.Contains("now >= held_at + 2") && Library.Get("besiege").Json.Contains("now >= clear_at + 2 or storm_relief(at) >= clear_relief * 1.25 + 50")
                  && Library.Get("defend").Json.Contains("of your {sside}"),
                  "item 21: the storm guard keeps a hold or a clear 2 hours unless a new force comes within reach, and the guard's siege line says why it stays out");

            // ---------- item 24: the relief rule caps free riders and sends none to a fight against bandits it wins anyway ----------
            Relief250Tests(moves);

            // ---------- item 25: a guard who never gets into the castle says so ----------
            Brace250Tests(moves);

            // ---------- item 26: no hour count with no road behind it ----------
            ToolDef noRoad = ToolDef.FromJson(@"{ ""name"": ""no_road"", ""kind"": ""tool"", ""about"": ""says an hour count"", ""at"": ""any_holding"", ""init"": { ""t0"": ""none"" }, ""start"": ""a"", ""states"": {
                ""a"": { ""about"": ""saying"", ""do"": ""hold"", ""when"": [ { ""if"": ""true"", ""go"": ""b"", ""poke"": ""{me} goes at Looters: about {t0} hours to reach it."" } ] },
                ""b"": { ""about"": ""done"", ""do"": ""hold"" } } }", "no_road", "orcs");
            var chaser = Own("chs", "Alvar", 104, 100, 400);
            Brain.OnHour(ToolWorld(89000, chaser));
            Runner.Start(25060, new ToolOrder { Tool = "no_road", Def = noRoad, Lord = chaser.Leader, LordName = chaser.Name, Party = chaser.Id, At = "castle_orc", Realm = "orcs" });
            Brain.OnHour(ToolWorld(89001, chaser));
            Check(!Notices().Contains("none hours") && Notices().Contains("Alvar goes at Looters: no road known to reach it")
                  && Library.Get("defend").Json.Contains("\"if\": \"hours_to(foe) == none\", \"go\": \"round\", \"set\": { \"spurned[foe]\": \"now + 6\"")
                  && Library.Get("harass").Json.Contains("\"if\": \"hours_to(foe) == none\""),
                  "item 26: a guard skips a target he has no road to and sets it aside 6 hours, and no message ever says \"none hours\"");
            Runner.Ended(25060, "test over");

            // ---------- item 29: a gone partner by its last known name, and whether its lord is held ----------
            var partner = Own("mlt", "Malthorn", 110, 100, 400);
            var lead29 = Own("l29", "Azgar", 112, 100, 400);
            Brain.OnHour(ToolWorld(89100, partner, lead29));
            var o29 = new ToolOrder { Tool = "besiege", Lord = lead29.Leader, LordName = lead29.Name, Party = lead29.Id, At = "castle_men", Realm = "orcs" };
            o29.Lords.Add(partner.Id);
            Runner.Start(25070, o29);
            Brain.OnHour(ToolWorld(89101, partner, lead29));
            Snapshot s29 = ToolWorld(89102, lead29);
            s29.Captives.Add(new CaptiveRow { Hero = partner.Leader, Name = "Malthorn", Holding = "castle_men" });
            Brain.OnHour(s29);
            string inForce = Runner.InForce(Runner.WorldFor(s29));
            Snapshot s29b = ToolWorld(89103, lead29);
            Brain.OnHour(s29b);
            string inForce2 = Runner.InForce(Runner.WorldFor(s29b));
            Check(inForce.Contains("with Malthorn (prisoner)") && !inForce.Contains(partner.Id) && inForce2.Contains("with Malthorn (no party now)"),
                  "item 29: a partner taken prisoner reads \"Malthorn (prisoner)\", one gone otherwise \"(no party now)\", never the party's code: " + inForce.Replace("\n", " | "));
            Runner.Ended(25070, "test over");

            // ---------- item 37: a realm whose agent is off gets no wake report (see the two-realm test); the summary counts pokes by realm ----------
            Check(Diag.CountOf("pokes of:orcs") > 0, "item 37: pokes are counted by realm for the session's summary");

            Runner.MoveSink = keptMove;
            Runner.ReleaseSink = keptRelease;
            Runner.Reset();
        }

        private static void Answer250(string dir, int n)
        {
            string orders = Path.Combine(dir, "orders");
            Directory.CreateDirectory(orders);
            File.WriteAllText(Path.Combine(orders, "poke-" + n + ".json"), "{\"poke\": " + n + ", \"orders\": []}");
            System.Threading.Thread.Sleep(350);
            AgentChannel.Scan();
        }

        /// <summary>Item 24: 25 free lords near a fight against 422 bandits their side wins alone: none ride; against an army of 2,000, riders stop once our side there is twice its strength.</summary>
        private static void Relief250Tests(List<KeyValuePair<int, Move>> moves)
        {
            var free = new List<PartyRow>();
            for (int k = 0; k < 25; k++)
                free.Add(new PartyRow { Id = "p_fr" + k, Leader = "lord_fr" + k, Name = "Rider " + k, Faction = "orcs", Kind = "lord", Men = 150, SizeLimit = 300, Strength = 300, Speed = 5, FoodDays = 20,
                                        X = 140 + k % 5, Y = 96 + k / 5, Doing = "Hold", Spottable = true });
            var friend = new PartyRow { Id = "p_frd", Leader = "lord_frd", Name = "Osven", Faction = "orcs", Kind = "lord", Men = 1500, Strength = 3000, Speed = 4, FoodDays = 20, X = 150, Y = 100, InBattle = true, Spottable = true };
            var bandits = new PartyRow { Id = "p_cor", Leader = null, Name = "Corsairs", Faction = "corsairs", Kind = "bandit", Men = 422, Strength = 422, Speed = 4, X = 150, Y = 100, InBattle = true, Spottable = true };
            BattleRow Fight(double h, PartyRow foe, string faction, int foeMen) => new BattleRow
            {
                Key = "p_frd@" + h.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "field", X = 150, Y = 100, StartHours = h - 1,
                Attackers = new BattleSideRow { Leader = foe.Id, Faction = faction, Healthy = foeMen, StartHealthy = foeMen, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { foe.Id } },
                Defenders = new BattleSideRow { Leader = "p_frd", Faction = "orcs", Healthy = 1500, StartHealthy = 1500, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_frd" } }
            };
            double h = 90000;
            Snapshot World1(double hh)
            {
                Snapshot s = ToolWorld(hh, free.Concat(new[] { friend, bandits }).ToArray());
                s.HostileToWatched.Add("corsairs");
                s.FactionNames["corsairs"] = "Corsairs";
                s.Battles.Add(Fight(hh, bandits, "corsairs", 422));
                return s;
            }
            Brain.OnHour(World1(h));
            for (int k = 0; k < 25; k++) Runner.Start(25100 + k, new ToolOrder { Tool = "hold", Lord = free[k].Leader, LordName = free[k].Name, Party = free[k].Id, Realm = "orcs" });
            moves.Clear();
            Brain.OnHour(World1(h + 1));
            int rodeBandits = moves.Count(m => m.Key >= 25100 && m.Key < 25125 && m.Value.Verb == "engage");
            // the same lords against an enemy army of 2,000 that our lord (500 now) would not beat alone
            friend.Strength = 500;
            friend.Men = 250;
            var host = new PartyRow { Id = "p_hst", Leader = "lord_hst", Name = "Imrahil", Faction = "player_k", Kind = "lord", Men = 1000, Strength = 2000, Speed = 4, X = 150, Y = 100, InBattle = true, Spottable = true };
            Snapshot World2(double hh)
            {
                Snapshot s = ToolWorld(hh, free.Concat(new[] { friend, host }).ToArray());
                s.Battles.Add(Fight(hh, host, "player_k", 1000));
                return s;
            }
            moves.Clear();
            Brain.OnHour(World2(h + 2));
            List<int> riders = moves.Where(m => m.Key >= 25100 && m.Key < 25125 && m.Value.Verb == "engage" && m.Value.Party == "p_hst").Select(m => m.Key).Distinct().ToList();
            double ours = 500 + riders.Count * 300;
            Check(rodeBandits == 0 && riders.Count > 0 && riders.Count < 25 && ours >= 2 * 2000 && ours - 300 < 2 * 2000,
                  "item 24: 25 free lords near a fight against 422 bandits their lord wins anyway: none ride (" + rodeBandits + "); against an army of 2,000 they ride, nearest first, only until your side there is twice its strength (" + riders.Count + " riders, " + ours + ")");
            for (int k = 0; k < 25; k++) Runner.Ended(25100 + k, "test over");
        }

        /// <summary>Item 25: a guard who never reaches the castle he rides into for a siege says he could not get in, never that he braces inside, and shelters elsewhere.</summary>
        private static void Brace250Tests(List<KeyValuePair<int, Move>> moves)
        {
            string Notices() { Files.FlushAll(); return ReadIf(Path.Combine(Brain.Dir, "notices.jsonl")); }
            var guard = new PartyRow { Id = "p_drn", Leader = "lord_drn", Name = "Durin", Faction = "orcs", Kind = "lord", Men = 500, SizeLimit = 800, Strength = 1000, Speed = 5, FoodDays = 20, X = 101, Y = 103, Doing = "Hold", Spottable = true };
            var host = new PartyRow
            {
                Id = "p_hs9", Leader = "lord_hs9", Name = "Theoden", Faction = "player_k", Kind = "lord", Men = 650, ArmyMen = 650, ArmyParties = 2, ArmyLeader = "p_hs9", ArmyStrength = 1300,
                Strength = 650, Speed = 4, X = 150, Y = 140, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true
            };
            double h = 91000;
            Brain.OnHour(ToolWorld(h, guard, host));
            var o = new ToolOrder { Tool = "defend", Lord = guard.Leader, LordName = guard.Name, Party = guard.Id, At = "castle_orc", Realm = "orcs" };
            o.Places.AddRange(new[] { "castle_orc", "village_orc" });
            o.Settings["caution"] = 0;
            Runner.Start(25200, o);
            string state = null;
            int k = 1;
            for (; k <= 6 && state != "brace"; k++) { Brain.OnHour(ToolWorld(h + k, guard, host)); state = Runner.All().FirstOrDefault(x => x.Id == 25200)?.State; }
            bool braced = state == "brace";
            // he never gets in: he stays where he is for a day
            for (int j = 0; j < 26; j++) Brain.OnHour(ToolWorld(h + k + j, guard, host));
            string notices = Notices();
            Check(braced && notices.Contains("Durin could not get into Orc Castle") && !notices.Contains("Durin braces for the siege inside"),
                  "item 25: a lord who never reaches the castle gets the \"could not get in\" notice, never \"braces inside\" (" + braced + ", " + Runner.All().FirstOrDefault(x => x.Id == 25200)?.State + ")");
            Runner.Ended(25200, "test over");
        }
    }
}
