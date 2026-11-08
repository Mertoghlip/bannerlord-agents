using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.16, the lieutenant's orders' ways made general rules (Nov 7: I really like all of the ideas you propose for the other orders, implement them as
        /// general rules): the danger counted by the hours to shelter; food bought by the trip to market; one weighing of a fight (fight_hours, fight_odds) in
        /// hunt, engage, relieve and the siege's relief bars; just enough men (needed); the group shortcuts; reach in hours for raid and screen; screen telling
        /// each army once a day; the enemies' points for the recruit tour; and the bigger limits on a tool of one's own.
        /// </summary>
        private static void GeneralRulesTests()
        {
            var moves = new List<KeyValuePair<int, Move>>();
            Action<int, Move> keptMove = Runner.MoveSink;
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.Reset();
            PartyRow Orc(string id, string name, float strength, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), SizeLimit = 300, Strength = strength, Speed = 5, FoodDays = 10,
                X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 5000
            };
            PartyRow Foe(string id, string name, float strength, float x, float y, string kind = "lord") => new PartyRow
            {
                Id = "p_" + id, Leader = kind == "lord" ? "lord_" + id : null, Name = name, Faction = kind == "lord" ? "player_k" : "looters", Kind = kind, Men = (int)(strength / 2), Strength = strength,
                Speed = 4, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            void Hour(double h, params PartyRow[] parties) { Diag.Hours = h; Brain.OnHour(ToolWorld(h, parties)); }
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;
            ToolOrder Order(string tool, PartyRow r, string at = "castle_orc", string target = null, ToolDef def = null)
                => new ToolOrder { Tool = tool, Def = def, Lord = r.Leader, LordName = r.Name, Party = r.Id, At = at, Target = target, Realm = "orcs" };

            // the danger counted by the hours to shelter: far from any fort it reaches farther than danger_radius, at most danger_hours of his march; 0 keeps danger_radius alone
            var far = Orc("dgf", "Ufthak", 300, 100, 160);
            var nearFort = Orc("dgn", "Lagduf", 300, 101, 100);
            Snapshot s0 = ToolWorld(66000, far, nearFort);
            World w0 = Runner.WorldFor(s0);
            ToolInstance Inst(double hours) { var ti = new ToolInstance(); ti.Settings["danger_radius"] = 8; ti.Settings["danger_hours"] = hours; return ti; }
            double pace = w0.PerHour(far), r3 = Runner.DangerReach(Inst(3), far, w0), r0 = Runner.DangerReach(Inst(0), far, w0), rn = Runner.DangerReach(Inst(3), nearFort, w0);
            Check(Math.Abs(r3 - Math.Max(8, 3 * pace)) < 0.01 && r0 == 8 && rn >= 8 && rn <= Math.Max(8, 2.5 * pace) + 0.01,
                  "the danger rule counts what could reach a lord before he is behind walls: far from forts " + ToolDef.Num(Math.Round(r3, 1)) + " (3 hours of his march), next to one "
                  + ToolDef.Num(Math.Round(rn, 1)) + ", and danger_radius alone (8) with danger_hours 0");
            // food bought by the trip to market, not at a fixed 2 days
            SettlementRow market = s0.SettlementById["castle_orc"];
            double needFar = Runner.FoodNeed(far, market, w0), needNear = Runner.FoodNeed(nearFort, market, w0);
            Check(needNear < 1.6 && needFar > needNear + 0.4,
                  "a lord buys food when it would last less than the trip to market and a day and a half: next to one " + ToolDef.Num(Math.Round(needNear, 2)) + " days, 60 away " + ToolDef.Num(Math.Round(needFar, 2)));

            // one weighing of a fight, and just enough men: two guards of Orc Castle, a weak raider; the nearer goes, the farther keeps to his loop; against a raider worth
            // more than half of the nearer, both go
            var nearer = Orc("jn1", "Grishnakh", 400, 100, 100);
            var farther = Orc("jn2", "Shagrat", 400, 104, 100);
            nearer.InSettlement = "castle_orc";
            double h1 = 66100;
            Hour(h1, nearer, farther);
            ToolOrder Guard(PartyRow r) { ToolOrder o = Order("defend", r); o.Places.AddRange(new[] { "castle_orc", "village_orc" }); return o; }
            Runner.Start(9970, Guard(nearer));
            Runner.Start(9971, Guard(farther));
            Hour(h1 + 1, nearer, farther);
            var raider = Foe("rd1", "Halbarad", 80, 96, 104);
            moves.Clear();
            Hour(h1 + 2, nearer, farther, raider);
            bool nearGoes = moves.Any(m => m.Key == 9970 && m.Value.Verb == "engage"), farStays = !moves.Any(m => m.Key == 9971 && m.Value.Verb == "engage");
            ToolInstance g1 = Runner.All().FirstOrDefault(t => t.Id == 9970);
            double odds = g1 != null && g1.Memory.TryGetValue("odds", out object ov) ? Expr.Number(ov) : double.NaN;
            Runner.Reset();
            Hour(h1 + 10, nearer, farther);
            Runner.Start(9972, Guard(nearer));
            Runner.Start(9973, Guard(farther));
            Hour(h1 + 11, nearer, farther);
            var strong = Foe("rd2", "Beregond", 300, 96, 104);
            moves.Clear();
            Hour(h1 + 12, nearer, farther, strong);
            bool bothGo = moves.Any(m => m.Key == 9972 && m.Value.Verb == "engage") && moves.Any(m => m.Key == 9973 && m.Value.Verb == "engage");
            Check(nearGoes && farStays && bothGo && odds > 0 && odds < 0.2,
                  "just enough men: against a raider of 80 the nearer guard goes and the farther keeps to his loop; against one of 300, more than half of the nearer, both go ("
                  + nearGoes + ", " + farStays + ", " + bothGo + "; the fight weighed at " + ToolDef.Num(Math.Round(odds, 2)) + " of our side)");
            Runner.Reset();

            // hunt and engage weigh by the same rule: a hunt stops, and an attack is broken off, once the enemy who could be in the fight is too strong
            var hunter = Orc("hn1", "Lugdush", 300, 100, 120);
            var prey = Foe("pr1", "Damrod", 200, 108, 120);
            var host = Foe("pr2", "Faramir", 1500, 116, 124);
            double h2 = 66200;
            Hour(h2, hunter, prey);
            ToolOrder hunt = Order("hunt", hunter, null, "p_pr1");
            hunt.Settings["caution"] = 0;   // the danger rule, which comes first, would shelter them: their own weighing is what is checked
            Runner.Start(9974, hunt);
            ToolOrder attack = Order("engage", Orc("hn2", "Mauhur", 300, 100, 118), null, "p_pr1");
            attack.Settings["caution"] = 0;
            Runner.Start(9975, attack);
            Hour(h2 + 1, hunter, prey, Orc("hn2", "Mauhur", 300, 100, 118));
            bool chasing = StateOf(9974) == "chase" && StateOf(9975) == "chase";
            host.Doing = "GoToParty";
            host.TargetParty = "p_pr1";
            Hour(h2 + 2, hunter, prey, host, Orc("hn2", "Mauhur", 300, 100, 118));
            Check(chasing && (StateOf(9974) ?? "over") == "over" && (StateOf(9975) ?? "over") == "over",
                  "hunt and engage weigh a fight by the same rule: they chase a target their side can take, and stop once the enemies who could be in the fight outweigh their bars ("
                  + chasing + ", " + (StateOf(9974) ?? "ended") + ", " + (StateOf(9975) ?? "ended") + ")");
            Runner.Reset();

            // 0.17.1 (Nov 7, test A1: 19 attacks broke off in the hour they were given, Krithak's on Stein "584% of your side's 376", Krithak alone, with 12
            // other lords sent at Stein in the same answer, and Dol Guldur's 16 at Aragorn): lords sent at one target in the same answer count together, so
            // the weighing waits for the second hour, when every one of them holds his order
            var j1 = Orc("ja1", "Krithak", 300, 100, 140);
            var j2 = Orc("ja2", "Muzgash", 300, 101, 141);
            var j3 = Orc("ja3", "Ufza", 300, 99, 139);
            var stein = Foe("st1", "Stein", 700, 106, 140);
            double h6 = 66250;
            Hour(h6, j1, j2, j3, stein);
            foreach (var kv in new[] { new KeyValuePair<int, PartyRow>(9980, j1), new KeyValuePair<int, PartyRow>(9981, j2), new KeyValuePair<int, PartyRow>(9982, j3) })
            {
                ToolOrder at = Order("engage", kv.Value, null, "p_st1");
                at.Settings["caution"] = 0;
                Runner.Start(kv.Key, at);
            }
            bool keptAtOnce = StateOf(9980) == "chase" && StateOf(9981) == "chase" && StateOf(9982) == "chase";
            Hour(h6 + 1, j1, j2, j3, stein);
            bool keptTogether = StateOf(9980) == "chase" && StateOf(9981) == "chase" && StateOf(9982) == "chase";
            var alone = Orc("ja4", "Lugbúrz", 300, 100, 150);
            var big = Foe("st2", "Imrahil", 1500, 106, 150);
            Hour(h6 + 2, alone, big);
            ToolOrder lone = Order("engage", alone, null, "p_st2");
            lone.Settings["caution"] = 0;
            Runner.Start(9983, lone);
            bool loneFirstHour = StateOf(9983) == "chase";
            Hour(h6 + 3, alone, big);
            Check(keptAtOnce && keptTogether && loneFirstHour && (StateOf(9983) ?? "over") == "over",
                  "lords sent at one target in the same answer count together: three of 300 attacking one of 700 keep at it (each alone would be under half his strength), "
                  + "and a lone 300 against 1,500 breaks off in his second hour, not in the hour he is sent (" + keptAtOnce + ", " + keptTogether + ", " + loneFirstHour + ", " + (StateOf(9983) ?? "ended") + ")");
            Runner.Reset();

            // the group shortcuts and marching_on, in a tool of one's own past the old limits
            ToolDef shortcuts = ToolDef.FromJson(@"{ ""name"": ""group_look"", ""at"": ""own_fort"", ""lords"": true, ""start"": ""a"", ""states"": {
                ""a"": { ""do"": ""hold"", ""when"": [ { ""if"": ""lords_near(at, 6) == 1 and group_strength(at, 6) == 700 and group_offers(at) == 5 and marching_on(at, 60) != none and strength_of(marching_on(at, 60)) == 900"", ""go"": ""b"" } ] },
                ""b"": { ""do"": ""hold"" } } }", "group_look", "orcs");
            var lookA = Orc("gl1", "Gorbag", 300, 100, 100);
            var lookB = Orc("gl2", "Radbug", 400, 103, 100);
            var lookC = Orc("gl3", "Muzgash", 200, 140, 100);
            lookA.Offers = new Dictionary<string, int> { ["castle_orc"] = 2 };
            lookB.Offers = new Dictionary<string, int> { ["castle_orc"] = 3 };
            var marcher = Foe("mo1", "Imrahil", 900, 130, 130);
            marcher.Doing = "GoToSettlement";
            marcher.TargetSettlement = "castle_orc";
            double h3 = 66300;
            Hour(h3, lookA, lookB, lookC, marcher);
            ToolOrder lo = Order("group_look", lookA, def: shortcuts);
            lo.Lords.AddRange(new[] { "p_gl2", "p_gl3" });
            Runner.Start(9976, lo);
            Hour(h3 + 1, lookA, lookB, lookC, marcher);
            Check(shortcuts.Valid && StateOf(9976) == "b",
                  "a tool reads its group (lords_near, group_strength, group_offers) and who marches on a holding (marching_on): " + (shortcuts.Valid ? StateOf(9976) : string.Join("; ", shortcuts.Problems)));
            Runner.Reset();

            // screen and raid reach in hours of the lord's march; screen tells each army once a day
            ToolDef screen = Library.Get("screen"), raid = Library.Get("raid");
            var watcher = Orc("sc1", "Snaga", 300, 102, 100);
            var army = Foe("sc2", "Elfhelm", 1200, 115, 106);
            army.ArmyMen = 1200;
            army.ArmyParties = 3;
            army.ArmyLeader = army.Id;
            army.ArmyStrength = 1500;
            double h4 = 66400;
            Hour(h4, watcher);
            Runner.Start(9977, Order("screen", watcher));
            bool keptWithin = moves.Any(m => m.Key == 9977 && m.Value.Verb == "patrol" && m.Value.R > 6 && Math.Abs(m.Value.R - 6 * Runner.WorldFor(ToolWorld(h4, watcher)).PerHour(watcher)) < 0.2);
            Hour(h4 + 1, watcher, army);
            Hour(h4 + 2, watcher, army);
            Hour(h4 + 3, watcher);
            Hour(h4 + 4, watcher, army);
            ToolInstance sc = Runner.All().FirstOrDefault(t => t.Id == 9977);
            object told = null;
            bool once = sc != null && sc.Memory.TryGetValue("told", out told) && told is Dictionary<string, object> tt && tt.ContainsKey("p_sc2") && tt.Count == 1;
            Check(screen.Settings.ContainsKey("reach_hours") && !screen.Settings.ContainsKey("radius") && raid.Settings.ContainsKey("reach_hours") && raid.Settings.ContainsKey("avoid_hours") && keptWithin && once,
                  "screen and raid reach in hours of the lord's own march (screen patrols within 6 of them), and screen remembers each army it told of, for a day (" + keptWithin + ", " + once + ")");
            Runner.Reset();

            // the recruit tour's enemies: what the realm sees, as points with their strength
            var bandit = Foe("bd1", "Looters", 120, 110, 104, "bandit");
            var unseen = Foe("bd2", "Sea raiders", 500, 300, 300, "bandit");
            Snapshot s5 = ToolWorld(66500, Orc("tr1", "Lurtz", 300, 100, 100), bandit, unseen);
            s5.WatchedSees.Remove("p_bd2");
            Threats.Publish(Runner.WorldFor(s5));
            Check(Math.Abs(Threats.Around("orcs", 108, 100) - 120) < 0.01 && Threats.Around("orcs", 300, 300) == 0 && Threats.Around("player_k", 108, 100) == 0,
                  "the recruit tour knows of the enemies its realm sees near a stop, and of no others: " + Threats.Around("orcs", 108, 100) + ", " + Threats.Around("orcs", 300, 300));
            Threats.Reset();

            // the bigger limits: 20 states, 80 rules, 1,200 characters, 12 settings
            Check(ToolDef.MaxStates == 20 && ToolDef.MaxRules == 80 && ToolDef.MaxExpression == 1200 && ToolDef.MaxSettings == 12,
                  "a tool of one's own may have 20 states, 80 rules, 12 settings and 1,200 characters an expression (it was 12, 40, 8 and 600)");
            Runner.MoveSink = keptMove;
        }
    }
}
