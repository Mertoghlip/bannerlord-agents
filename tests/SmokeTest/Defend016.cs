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
        /// 0.16 (Nov 7): the standard defend is the guard loop the lieutenant wrote in test A1 (a loop of the holding and its villages, a group taking turns at the
        /// volunteers, each fight weighed by the game's marching and battle rules, food carried in, the guards riding in when an enemy marches on the holding),
        /// with the rule that they ride in only where the defenders would hold; the stop check (offers, offers_of, can_recruit); recruiting orders refused up front
        /// (ToolDef.IsRecruiting); wage caps beside the lords; wage orders first in an answer; and the lieutenant's talks in other saves.
        /// </summary>
        private static void DefendTests(string root, string agentDir)
        {
            // the checks before this one ran on the defend of 0.15 (Fixtures): from here on, the one that ships
            Library.SwapForTest("defend", null);
            Library.DangerHours.Default = 3;
            ToolDef def = Library.Get("defend");
            Check(def != null && def.Valid && def.States.ContainsKey("weigh") && def.States.ContainsKey("brace") && def.PlacesDefault == "at_and_villages" && def.Lords
                  && def.Settings.ContainsKey("reach_hours") && def.Settings.ContainsKey("stay_inside") && !def.Settings.ContainsKey("radius"),
                  "the standard defend is the guard loop: it takes a loop of places (left out: the holding and its villages) and a group of lords, and passes the checker: "
                  + string.Join("; ", def?.Problems ?? new List<string>()));
            Check(Library.Problems().Count == 0 && def.Describe().Contains("may be left out: then the holding at, and its villages, nearest first"),
                  "the library passes its checks with it, and says the loop may be left out");
            Check(Library.Get("recruit").IsRecruiting && !def.IsRecruiting
                  && ToolDef.FromJson("{\"name\": \"round_up\", \"takes\": {\"places\": \"friendly_holding: the round\"}, \"start\": \"go\", \"states\": {\"go\": {\"do\": \"recruit at places[0]\"}}}", "round_up", "orcs").IsRecruiting,
                  "a recruiting order is ours or one whose first move recruits; defend, which guards and recruits on the way, is not");

            var moves = new List<KeyValuePair<int, Move>>();
            var released = new List<int>();
            Action<int, Move> keptMove = Runner.MoveSink;
            Action<int, string> keptRelease = Runner.ReleaseSink;
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => released.Add(id);
            Runner.Reset();
            PartyRow Lord(string id, string name, float strength, Dictionary<string, int> offers = null) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), SizeLimit = 300, Strength = strength, Speed = 5, FoodDays = 10,
                X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true, Gold = 5000, Offers = offers
            };
            ToolOrder Defend(PartyRow r, params string[] places)
            {
                var o = new ToolOrder { Tool = "defend", Lord = r.Leader, LordName = r.Name, Party = r.Id, At = "castle_orc", Realm = "orcs" };
                o.Places.AddRange(places.Length > 0 ? places : new[] { "castle_orc", "village_orc" });
                return o;
            }
            void Hour(double h, params PartyRow[] parties) { Diag.Hours = h; Brain.OnHour(ToolWorld(h, parties)); }
            bool Moved(int id, string verb, string place = null, string party = null)
                => moves.Any(m => m.Key == id && m.Value.Verb == verb && (place == null || m.Value.Place == place) && (party == null || m.Value.Party == party));
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;

            // the loop: to the first stop, standing, then taking the volunteers; a stop with nothing left for him (offers 0) is left after an hour, not stay_hours
            var lagduf = Lord("lpa", "Lagduf", 150, new Dictionary<string, int> { ["castle_orc"] = 0, ["village_orc"] = 5 });
            var mauhur = Lord("lpc", "Mauhur", 150);
            double h0 = 60000;
            Hour(h0, lagduf, mauhur);
            Runner.Start(9960, Defend(lagduf));
            Runner.Start(9961, Defend(mauhur));
            bool rides = Moved(9960, "goto", "castle_orc");
            Hour(h0 + 1, lagduf, mauhur);
            bool stands = Moved(9960, "hold");
            Hour(h0 + 2, lagduf, mauhur);
            bool takes = Moved(9960, "recruit", "castle_orc");
            Hour(h0 + 3, lagduf, mauhur);
            bool onEarly = Moved(9960, "goto", "village_orc"), controlStays = !Moved(9961, "goto", "village_orc");
            Hour(h0 + 4, lagduf, mauhur);
            Hour(h0 + 5, lagduf, mauhur);
            bool controlOn = Moved(9961, "goto", "village_orc");
            Check(rides && stands && takes && onEarly && controlStays && controlOn,
                  "defend rides its loop: to the stop, standing, taking the volunteers; with nothing left for him there (offers 0) it rides on after an hour, "
                  + "and with no count known after its stay_hours (" + rides + ", " + stands + ", " + takes + ", " + onEarly + ", " + controlStays + ", " + controlOn + ")");

            // a fight weighed and begun in the hour the enemy is seen (the lieutenant's version took three hours to weigh it)
            var grishnakh = Lord("lwg", "Grishnakh", 400);
            double h1 = 61000;
            Hour(h1, grishnakh);
            Runner.Start(9962, Defend(grishnakh));
            Hour(h1 + 1, grishnakh);
            var scout = new PartyRow { Id = "p_dsc", Leader = "lord_dsc", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 60, Strength = 80, Speed = 6, X = 108, Y = 96, Doing = "Hold", Spottable = true };
            moves.Clear();
            Hour(h1 + 2, grishnakh, scout);
            ToolInstance g = Runner.All().FirstOrDefault(t => t.Id == 9962);
            Check(Moved(9962, "engage", party: "p_dsc") && StateOf(9962) == "strike" && g != null && g.Memory.ContainsKey("b") && Expr.Number(g.Memory["b"]) > 1 && Expr.Number(g.Memory["odds"]) > 0.15 && Expr.Number(g.Memory["odds"]) < 0.25,
                  "defend weighs a weak enemy lord by the fight's own hours and strikes in the hour it is seen: state " + StateOf(9962) + ", fight hours "
                  + (g?.Memory.TryGetValue("b", out object b) == true ? b : "?"));

            // an enemy army marching on the holding: the guards ride in with their food when its defenders, with them, would hold; elsewhere when not; in all the same with stay_inside 1
            PartyRow Host(string id, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = "Theoden", Faction = "player_k", Kind = "lord", Men = 1500, ArmyMen = 1500, ArmyParties = 3, ArmyLeader = "p_" + id, ArmyStrength = strength,
                Strength = strength / 2, Speed = 4, X = 130, Y = 125, Doing = "GoToSettlement", TargetSettlement = "castle_orc", Spottable = true
            };
            var ufthak = Lord("lbr", "Ufthak", 400);
            double h2 = 62000;
            Hour(h2, ufthak);
            Runner.Start(9963, Defend(ufthak));
            Hour(h2 + 1, ufthak);
            moves.Clear();
            Hour(h2 + 2, ufthak, Host("hs1", 1000));
            bool bracesIn = Moved(9963, "supply", "castle_orc") && StateOf(9963) == "brace";
            var shagrat = Lord("lrn", "Shagrat", 400);
            double h3 = 63000;
            Hour(h3, shagrat);
            Runner.Start(9964, Defend(shagrat));
            Hour(h3 + 1, shagrat);
            moves.Clear();
            Hour(h3 + 2, shagrat, Host("hs2", 5000));
            bool runs = !Moved(9964, "supply") && moves.Any(m => m.Key == 9964 && m.Value.Verb == "enter" && m.Value.Place != "castle_orc") && StateOf(9964) == "shelter";
            var gorbag = Lord("lsi", "Gorbag", 400);
            double h4 = 64000;
            Hour(h4, gorbag);
            ToolOrder stay = Defend(gorbag);
            stay.Settings["stay_inside"] = 1;
            Runner.Start(9965, stay);
            Hour(h4 + 1, gorbag);
            moves.Clear();
            Hour(h4 + 2, gorbag, Host("hs3", 5000));
            bool staysIn = Moved(9965, "supply", "castle_orc");
            Check(bracesIn && runs && staysIn,
                  "an army marching on the holding: the guards ride in with their food when its defenders, with them, would hold (1,000 against 1,000 behind level-1 walls); "
                  + "against 5,000 they shelter elsewhere rather than be taken with it; with stay_inside 1 they go in all the same (" + bracesIn + ", " + runs + ", " + staysIn + ")");

            // the stop check in the language: offers, offers_of, can_recruit
            ToolDef check = ToolDef.FromJson(@"{ ""name"": ""stopcheck"", ""at"": ""own_holding"", ""lords"": true, ""start"": ""a"", ""states"": {
                ""a"": { ""do"": ""hold"", ""when"": [ { ""if"": ""offers(at) == 0 and can_recruit and offers_of(item(lords, 0), at) == 3"", ""go"": ""empty"" }, { ""if"": ""not can_recruit"", ""go"": ""unpaid"" } ] },
                ""empty"": { ""do"": ""hold"" }, ""unpaid"": { ""do"": ""hold"" } } }", "stopcheck", "orcs");
            var x = Lord("lsx", "Snaga", 100, new Dictionary<string, int> { ["castle_orc"] = 0 });
            var y = Lord("lsy", "Radbug", 100, new Dictionary<string, int> { ["castle_orc"] = 3 });
            y.RecruitBar = "wage cap";
            var z = Lord("lsz", "Muzgash", 100);
            double h5 = 65000;
            Hour(h5, x, y, z);
            foreach (var kv in new[] { new KeyValuePair<int, PartyRow>(9966, x), new KeyValuePair<int, PartyRow>(9967, y), new KeyValuePair<int, PartyRow>(9968, z) })
            {
                var o = new ToolOrder { Tool = "stopcheck", Def = check, Lord = kv.Value.Leader, LordName = kv.Value.Name, Party = kv.Value.Id, At = "castle_orc", Realm = "orcs" };
                o.Lords.Add("p_lsy");
                Runner.Start(kv.Key, o);
            }
            Hour(h5 + 1, x, y, z);
            Check(check.Valid && StateOf(9966) == "empty" && StateOf(9967) == "unpaid" && StateOf(9968) == "a",
                  "the language reads what a stop has for a lord (offers, and offers_of another lord's) and whether he can pay (can_recruit); none when not counted: "
                  + StateOf(9966) + ", " + StateOf(9967) + ", " + StateOf(9968) + (check.Valid ? "" : " " + string.Join("; ", check.Problems)));
            Runner.Reset();
            Runner.MoveSink = keptMove;
            Runner.ReleaseSink = keptRelease;

            // wage caps beside the lords in the report
            var capped = new PartyRow { Id = "p_cap", Name = "Lugbúrz", Faction = "orcs", Kind = "lord", Gold = 4000, Wages = 797, WageCap = 800 };
            var near = new PartyRow { Id = "p_near", Name = "Uglúk", Faction = "orcs", Kind = "lord", Gold = 4000, Wages = 700, WageCap = 800, CapOurs = true };
            var room = new PartyRow { Id = "p_room", Name = "Lurtz", Faction = "orcs", Kind = "lord", Gold = 4000, Wages = 200, WageCap = 800 };
            var poorCapped = new PartyRow { Id = "p_poor", Name = "Mauhúr", Faction = "orcs", Kind = "lord", Gold = 900, RecruitFloor = 1000, Wages = 798, WageCap = 800 };
            string c1 = PurseView.RecruitNote(capped), c2 = PurseView.RecruitNote(near), c3 = PurseView.RecruitNote(room), c4 = PurseView.RecruitNote(poorCapped);
            Check(c1 == "wages 797 a day of his 800 cap (the game's): at his cap, so he cannot recruit" && c2 == "wages 700 a day of his 800 cap (yours): room for 95 a day of new men's wages"
                  && c3 == null && c4 != null && c4.Contains("too little to recruit") && c4.Contains("at his cap"),
                  "a lord's wages show beside him at or near his cap, with his gold when it is low too: " + c1 + " | " + c2 + " | " + (c3 ?? "none") + " | " + c4);

            // an answer's wage caps go first, the rest in the order given
            var answer = new List<AgentChannel.AgentOrder>
            {
                new AgentChannel.AgentOrder { Lord = "lord_a", Do = "recruit" }, new AgentChannel.AgentOrder { Lord = "lord_b", Do = "defend" },
                new AgentChannel.AgentOrder { Lord = "lord_a", Do = "Wages " }, new AgentChannel.AgentOrder { Lord = "lord_c", Do = "goto" }
            };
            List<AgentChannel.AgentOrder> sorted = AgentChannel.WagesFirst(answer);
            Check(string.Join(",", sorted.Select(o => o.Do.Trim().ToLowerInvariant())) == "wages,recruit,defend,goto",
                  "an answer's wage orders are carried out first, so a recruit order is checked against the cap the same answer sets: " + string.Join(",", sorted.Select(o => o.Do)));

            GeneralRulesTests();
            LieutenantChatsTests(root);
        }

        /// <summary>The lieutenant's talks in the ruler's other saves (0.16): one file a save, the save played now left out, an index, old files taken away.</summary>
        private static void LieutenantChatsTests(string root)
        {
            string data = Path.Combine(root, "chats-data"), camp = Path.Combine(data, "camp1"), channel = Path.Combine(root, "chats-channel");
            Directory.CreateDirectory(Path.Combine(camp, "lieutenant"));
            Directory.CreateDirectory(Path.Combine(channel, LieutenantChats.Folder));
            File.WriteAllText(Path.Combine(channel, LieutenantChats.Folder, "OLD SAVE.txt"), "left from an earlier load");
            File.WriteAllLines(Path.Combine(camp, "saves.jsonl"), new[]
            {
                "{\"type\":\"save\",\"save\":\"TEST ONE\",\"load\":\"L1\",\"hours\":100,\"date\":\"Summer 1, 1087, 04:00\",\"before\":[],\"wall\":\"2026-11-01 10:00:00\"}",
                "{\"type\":\"save\",\"save\":\"TEST TWO\",\"load\":\"L2\",\"hours\":200,\"date\":\"Summer 5, 1087, 08:00\",\"before\":[{\"load\":\"L1\",\"until\":100,\"wall\":\"2026-11-01 10:00:00\"}],\"wall\":\"2026-11-02 10:00:00\"}",
                "{\"type\":\"save\",\"save\":\"QUIET\",\"load\":\"L3\",\"hours\":50,\"date\":\"Spring 1, 1087, 02:00\",\"before\":[],\"wall\":\"2026-10-30 10:00:00\"}"
            });
            File.WriteAllLines(Path.Combine(camp, "lieutenant", "chat.jsonl"), new[]
            {
                "{\"load\":\"L1\",\"h\":90,\"w\":\"2026-11-01 09:00:00\",\"k\":\"you\",\"t\":\"Guard Lasgalor in pairs.\",\"n\":1}",
                "{\"load\":\"L1\",\"h\":90,\"w\":\"2026-11-01 09:01:00\",\"k\":\"lt\",\"t\":\"Done: four pairs.\"}",
                "{\"load\":\"L1\",\"h\":120,\"w\":\"2026-11-01 11:00:00\",\"k\":\"you\",\"t\":\"Said after TEST ONE was saved.\",\"n\":2}",
                "{\"load\":\"L2\",\"h\":150,\"w\":\"2026-11-02 09:00:00\",\"k\":\"you\",\"t\":\"Now to Torech Emel.\",\"n\":3}",
                "{\"load\":\"L3\",\"h\":40,\"w\":\"2026-10-30 09:00:00\",\"k\":\"aside\",\"t\":\"(nothing said)\"}"
            });
            List<LieutenantChats.Talk> talks = LieutenantChats.Write(channel, data, "camp1", "TEST TWO");
            string folder = Path.Combine(channel, LieutenantChats.Folder);
            string one = File.Exists(Path.Combine(folder, "TEST ONE.txt")) ? File.ReadAllText(Path.Combine(folder, "TEST ONE.txt")) : "";
            string index = File.Exists(Path.Combine(folder, "index.txt")) ? File.ReadAllText(Path.Combine(folder, "index.txt")) : "";
            Check(talks.Count == 1 && one.Contains("Ruler: Guard Lasgalor in pairs.") && one.Contains("You: Done: four pairs.") && !one.Contains("Said after TEST ONE")
                  && !File.Exists(Path.Combine(folder, "TEST TWO.txt")) && !File.Exists(Path.Combine(folder, "QUIET.txt")) && !File.Exists(Path.Combine(folder, "OLD SAVE.txt"))
                  && index.Contains("- 'TEST ONE': saved at Summer 1, 1087, 04:00") && index.Contains("1 message from him") && index.Contains(LieutenantChats.Folder + "/TEST ONE.txt"),
                  "the lieutenant gets each other save's whole talk as it was saved (not the save played now, none where nothing was said, old files gone), and an index: " + index.Replace("\n", " | "));
        }
    }
}
