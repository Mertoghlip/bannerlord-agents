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
        /// 0.17.3 (Nov 8: battle times work differently when the player is fighting the battle; the agents should know which party is the human player's,
        /// close in and wait to strike together automatically, and study his battles): the report names him and how his battles are fought; the weighing,
        /// the danger rule and the relief rule count only the ring of 3; attacks on him close in and strike together; his record by what the realm saw.
        /// </summary>
        private static void InPersonTests()
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
            PartyRow Aragorn(float x, float y, float strength) => new PartyRow
            {
                Id = "main_party", Leader = "lord_aragorn", Name = "Aragorn", Faction = "player_k", Kind = "lord", Men = (int)(strength / 3), Strength = strength / 2, ArmyMen = (int)(strength / 3),
                ArmyParties = 2, ArmyLeader = "main_party", ArmyStrength = strength, Speed = 5, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            void Hour(double h, params PartyRow[] parties) { Diag.Hours = h; Brain.OnHour(ToolWorld(h, parties)); }
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;
            ToolOrder Attack(PartyRow r, string target)
            {
                var o = new ToolOrder { Tool = "engage", Lord = r.Leader, LordName = r.Name, Party = r.Id, Target = target, Realm = "orcs" };
                o.Settings["caution"] = 0;   // the danger rule would come first: the attack itself is what is checked
                o.Settings["give_up_odds"] = 10;
                return o;
            }

            // the report names him, and says how his battles are fought; his party is marked wherever it is listed
            var ar = Aragorn(120, 100, 3000);
            Snapshot s0 = ToolWorld(67000, Orc("ip0", "Bolg", 400, 100, 100), ar);
            View v0 = Views.Build(s0, "orcs", s0.WatchedSees, s0.HostileToWatched, new Brain.Memory());
            string report = Views.Report(v0, new List<Trigger>());
            Check(report.Contains("YOUR ENEMY, THE HUMAN PLAYER") && report.Contains("Aragorn [main_party], of Kingdom of Arnor and Gondor, is commanded in person by a human player")
                  && report.Contains("only the lords and bands within 3 of it") && report.Contains("[lord_aragorn] (the human player, in person)"),
                  "the report names the human player's party, says how his battles are fought, and marks him where it lists him");
            View vk = Views.Build(s0, "player_k", new Dictionary<string, Sighting>(), s0.HostileToPlayer, new Brain.Memory());
            string own = Views.Report(vk, new List<Trigger>());
            Check(own.Contains("YOUR OWN BATTLES, THE HUMAN PLAYER'S") && own.Contains("You, Aragorn [main_party], are the human player") && own.Contains("only those in your army or within 3"),
                  "his own realm's report (the lieutenant's) tells him which of his lords fight in his battles");

            // the weighing of a fight with him: his side within the ring of him, ours within the ring of this lord; nobody else, however near in hours
            var w0 = Runner.WorldFor(ToolWorld(67001, Orc("ip1", "Gorbag", 600, 110, 100), Orc("ip2", "Shagrat", 600, 111.5f, 100), Orc("ip3", "Lugdush", 900, 104, 100), ar));
            Check(w0.InPerson("main_party") && !w0.InPerson("p_ip1") && Math.Abs(w0.RingSide(w0.Party("p_ip1")) - 1200) < 0.01 && Math.Abs(w0.RingEnemy("main_party") - 3000) < 0.01,
                  "a battle with the human player counts the ring of 3 only: Gorbag with Shagrat 1.5 away (1,200), not Lugdush 6 away; Aragorn's army as it is (3,000)");

            // attacks on him close in, wait about 6 from him, and strike together once those sent are in position
            var a1 = Orc("ca1", "Grishnakh", 500, 140, 100);
            var a2 = Orc("ca2", "Ufthak", 500, 141, 102);
            var a3 = Orc("ca3", "Muzgash", 500, 142, 98);
            var king = Aragorn(110, 100, 1200);
            double h0 = 67100;
            Hour(h0, a1, a2, a3, king);
            Runner.Start(9990, Attack(a1, "main_party"));
            Runner.Start(9991, Attack(a2, "main_party"));
            Runner.Start(9992, Attack(a3, "main_party"));
            Hour(h0 + 1, a1, a2, a3, king);
            bool closing = !moves.Any(m => m.Key >= 9990 && m.Key <= 9992 && m.Value.Verb == "engage") && moves.Any(m => m.Key == 9990 && m.Value.Verb == "shadow" && Math.Abs(m.Value.R - 6) < 0.01);
            a1.X = 117; a2.X = 117; a2.Y = 101; a3.X = 117; a3.Y = 99;
            moves.Clear();
            Hour(h0 + 2, a1, a2, a3, king);
            bool together = moves.Count(m => m.Key >= 9990 && m.Key <= 9992 && m.Value.Verb == "engage" && m.Value.Party == "main_party") == 3;
            Check(closing && together,
                  "lords sent at the human player close in to about 6 from him and wait, then all strike in the same hour once they are in position (" + closing + ", " + together + ")");
            Runner.Reset();
            // one alone in position, the others too far to come within 4 hours: he waits, then strikes once his 4 hours are up
            var b1 = Orc("cb1", "Snaga", 500, 117, 100);
            var b2 = Orc("cb2", "Radbug", 500, 240, 100);
            double h1 = 67200;
            Hour(h1, b1, b2, king);
            Runner.Start(9993, Attack(b1, "main_party"));
            Runner.Start(9994, Attack(b2, "main_party"));
            moves.Clear();
            Hour(h1 + 1, b1, b2, king);
            bool strikesAlone = moves.Any(m => m.Key == 9993 && m.Value.Verb == "engage");
            Runner.Reset();
            var c1 = Orc("cc1", "Lurtz", 500, 117, 100);
            var c2 = Orc("cc2", "Mauhur", 500, 126, 100);
            double h2 = 67300;
            Hour(h2, c1, c2, king);
            Runner.Start(9995, Attack(c1, "main_party"));
            Runner.Start(9996, Attack(c2, "main_party"));
            moves.Clear();
            for (int i = 1; i <= 3; i++) Hour(h2 + i, c1, c2, king);
            bool waits = !moves.Any(m => m.Key == 9995 && m.Value.Verb == "engage");
            Hour(h2 + 5, c1, c2, king);
            bool then = moves.Any(m => m.Key == 9995 && m.Value.Verb == "engage");
            Check(strikesAlone && waits && then,
                  "a lord in position whose partners are too far to come within 4 hours strikes at once; one whose partner could come waits, and strikes once his 4 hours are up ("
                  + strikesAlone + ", " + waits + ", " + then + ")");
            Runner.Reset();

            // the danger rule against him counts only the ring: with a friend 10 away a lord stands against another lord as strong as Aragorn, but not against him
            var d1 = Orc("dg1", "Grizznak", 800, 100, 130);
            var d2 = Orc("dg2", "Boldog", 800, 110, 130);
            var other = new PartyRow { Id = "p_imr", Leader = "lord_imr", Name = "Imrahil", Faction = "player_k", Kind = "lord", Men = 500, Strength = 1300, Speed = 5, X = 100, Y = 124, Doing = "Hold", Spottable = true };
            var him = Aragorn(100, 124, 1300);
            ToolOrder Hold(PartyRow r) => new ToolOrder { Tool = "hold", Lord = r.Leader, LordName = r.Name, Party = r.Id, Realm = "orcs" };
            double h3 = 67400;
            Hour(h3, d1, d2, other);
            Runner.Start(9987, new ToolOrder { Tool = "follow", Lord = d2.Leader, LordName = d2.Name, Party = d2.Id, Target = d1.Id, Realm = "orcs" });
            Runner.Start(9997, Hold(d1));
            Hour(h3 + 1, d1, d2, other);
            bool standsVsLord = Runner.All().FirstOrDefault(x => x.Id == 9997)?.Shelter == null;
            Runner.Reset();
            Hour(h3 + 10, d1, d2, him);
            Runner.Start(9988, new ToolOrder { Tool = "follow", Lord = d2.Leader, LordName = d2.Name, Party = d2.Id, Target = d1.Id, Realm = "orcs" });
            Runner.Start(9998, Hold(d1));
            Hour(h3 + 11, d1, d2, him);
            bool sheltersVsHim = Runner.All().FirstOrDefault(x => x.Id == 9998)?.Shelter != null;
            Check(standsVsLord && sheltersVsHim,
                  "the danger rule: a lord of 800 with a friend 10 away following him stands against a lord of 1,300, but shelters from the human player's 1,300, whose battle would take only the ring of 3 ("
                  + standsVsLord + ", " + sheltersVsHim + ")");
            Runner.Reset();

            // his record, by what the realm saw
            InPerson.Reset(null, null);
            InPerson.AddForTest(new InPersonBattle
            {
                Hours = 67500, Date = "Autumn 9, 1087, 13:00", Kind = "field", Won = true, Known = { "orcs" },
                His = new InPersonSide { Realm = "player_k", Men = 902, Strength = 2500, Lost = 54, Lords = 1, Infantry = 300, Ranged = 300, Cavalry = 302 },
                Other = new InPersonSide { Realm = "orcs", RealmName = "Misty Mountain Orcs", Leader = "Wruk", Men = 1161, Strength = 3100, Lost = 1161, Lords = 11, Infantry = 900, Ranged = 261 }
            });
            InPerson.AddForTest(new InPersonBattle
            {
                Hours = 67600, Date = "Autumn 10, 1087, 02:00", Kind = "storm", Place = "Westoft", Won = true, Known = { "dolguldur" },
                His = new InPersonSide { Men = 4439, Strength = 9000, Lost = 727 }, Other = new InPersonSide { Men = 3664, Strength = 6000, Lost = 3664 }
            });
            string summary = InPerson.Summary(InPerson.For("orcs"), "Aragorn");
            string section = InPerson.Section(s0, "orcs");
            string lookup = InPerson.Lookup(s0, "orcs", new List<string> { "Autumn 9, 1087, 08:00 · Enemy army in view: Aragorn (Kingdom of Arnor and Gondor), 2,586 men" });
            Check(summary.StartsWith("His record as your realm saw it: 1 battle fought in person, 1 won (1 field battle (1 won))") && summary.Contains("the lowest odds he won at: his side's strength 0.81 of the other's")
                  && summary.Contains("men lost: his side 54, the other 1,161 (21.5 for each of his)") && !section.Contains("Westoft")
                  && section.Contains("Aragorn's side 902 men, strength 2,500, 1 lord (foot 33%, archers 33%, horse 33%, horse archers 0%)") && section.Contains("under Wruk")
                  && lookup.Contains("WHAT YOUR REALM SAW HIM DO") && lookup.Contains("Enemy army in view: Aragorn"),
                  "the human player's record holds the battles the realm knew of (not another realm's), with both sides' strength and troops: " + summary);
            InPerson.Reset(null, null);
            Runner.MoveSink = keptMove;
        }
    }
}
