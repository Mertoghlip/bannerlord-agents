using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.14 (Nov 2-3: a news wake's report shows what bears on the news, by the clock of the development, and the rest is asked for):
        /// the theatre's focus, its clock and horizon, who is in it and who is not; the news report's sections and its fog; the whole report for a
        /// first look, the realm's diplomacy for a vote; the question book with every list and the enemy holdings' look-ups; and a poke in theatre
        /// mode end to end (poke.txt, the book, poke.json's news, the ids the book shows counted as shown), and in shadow mode.
        /// </summary>
        private static void NewsTests(string root)
        {
            string dir = Path.Combine(root, "news_orcs");
            Settings.AgentDir = dir;
            Settings.Agent = "live";
            Brain.Reset("smoketest-news", double.NaN, "orcs", null);

            // Bolg and Gorbag near Orc Castle, Ugluk far off; Aragorn's army marching on Orc Castle; Baranthir far off, idle; Imrahil's army far
            // off but marching on Orc Keep
            PartyRow Lord(string id, string name, float x, float y, int men, string doing = "Hold", string target = null)
                => new PartyRow { Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = men, Strength = men * 1.4f, Speed = 4.5f, FoodDays = 20, X = x, Y = y, Doing = doing, TargetSettlement = target, Spottable = true };
            PartyRow Foe(string id, string name, float x, float y, int men, string doing = "Hold", string target = null, bool army = false)
                => new PartyRow
                {
                    Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = men, ArmyMen = army ? men : 0, ArmyParties = army ? 4 : 0, ArmyLeader = army ? "p_" + id : null,
                    Strength = men * 2f, ArmyStrength = army ? men * 2f : 0, Speed = 4f, FoodDays = 20, X = x, Y = y, Doing = doing, TargetSettlement = target, Spottable = true
                };
            Snapshot World(double h, params PartyRow[] more)
            {
                var parties = new List<PartyRow>
                {
                    Lord("bolg", "Bolg", 104, 100, 300), Lord("gorbag", "Gorbag", 125, 102, 280), Lord("ugluk", "Ugluk", 900, 900, 400),
                    Foe("aragorn", "Aragorn", 175, 100, 3000, "GoToSettlement", "castle_orc", army: true), Foe("baranthir", "Baranthir", 700, 120, 400),
                    Foe("imrahil", "Imrahil", 600, 100, 2500, "GoToSettlement", "castle_two", army: true)
                };
                for (int i = 0; i < 25; i++) parties.Add(Lord("far" + i, "Farlord" + i, 600 + 9 * i, 700 + 7 * i, 200 + i));
                for (int i = 0; i < 12; i++) parties.Add(Foe("foe" + i, "Farfoe" + i, 800 + 11 * i, 300 + 5 * i, 150 + i));
                parties.AddRange(more);
                return ToolWorld(h, parties.ToArray());
            }

            Snapshot s = World(80000);
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, new Brain.Memory());
            BannerlordAgents.Tools.World w = BannerlordAgents.Tools.Runner.WorldFor(s);
            var sighting = new List<Trigger> { new Trigger { Hours = 80000, Kind = "enemy_in_view", Party = "p_aragorn", Text = "Enemy army in view: Aragorn (Kingdom of Arnor and Gondor), 3,000 men, marching on Orc Castle." } };
            Theatre t = Brain.TheatreOf(s, v, sighting, w, AgentChannel.For("orcs"));
            Focus castle = t.Focuses.FirstOrDefault(f => f.Id == "castle_orc");
            ViewParty aragorn = v.Seen.FirstOrDefault(p => p.Id == "p_aragorn");
            Check(t.Recipe == "theatre" && castle != null && !double.IsNaN(castle.DecidedAt) && aragorn != null && !double.IsNaN(aragorn.ArrivesAt)
                  && castle.DecidedAt > aragorn.ArrivesAt && castle.Horizon >= Theatre.Floor && castle.Horizon <= Theatre.Ceiling && t.Focuses.Any(f => f.Id == "p_aragorn"),
                  "an enemy army marching on one of the realm's castles: the castle is the focus, decided after the enemy arrives by how long its walls or food hold, "
                  + "the horizon kept between 6 hours and 3 days (decided in " + (castle != null ? Math.Round(castle.DecidedAt - 80000, 1) + " h, horizon " + Math.Round(castle.Horizon, 1) : "?") + ")");
            Check(t.Lords.Any(r => r.Party.Id == "p_bolg") && t.Lords.Any(r => r.Party.Id == "p_gorbag") && !t.Lords.Any(r => r.Party.Id == "p_ugluk")
                  && (t.Beyond.Any(r => r.Party.Id == "p_ugluk") || t.LordsFurther > 0),
                  "the lords who can be there in time are in the theatre, soonest first; the far one is not, but named among those beyond the clock");
            Check(t.Others.Any(r => r.Party.Id == "p_aragorn") && !t.Others.Any(r => r.Party.Id == "p_baranthir"),
                  "the enemy marching in is in the theatre; an idle enemy far off is not");
            Check(t.Places.Contains("castle_orc") && t.Places.Contains("castle_two") && t.Others.Any(r => r.Party.Id == "p_imrahil") && t.HeadingIn.Contains("p_imrahil"),
                  "an enemy army far off but marching on a holding of the theatre is shown, marked as marching in");
            Reach bolg = t.Lords.First(r => r.Party.Id == "p_bolg");
            Check(t.Mark(bolg) == "in time even if your orders land late" && t.UsualLag == Theatre.DefaultUsualLag && t.LateLag == Theatre.DefaultLateLag,
                  "each lord is marked against the clock with the agent's answer time counted (7 and 20 in-game hours until five answers are in): " + t.Mark(bolg));

            var x = new ReportExtras { Since = "- nothing new.", InForce = "- None. Every lord is led by the game's own AI.", Facts = Facts.ForPoke(w, sighting) };
            string full = Views.Report(v, sighting, x).Replace("\r", "");
            string news = Views.NewsReport(v, sighting, x, t).Replace("\r", "");
            List<string> known = Brain.Known(s, "orcs", s.WatchedSees, Brain.Get("orcs"), w, sighting).ToList();
            List<string> leaks = Audit.Fog(v, news, s.WatchedSees, s.InspectedByWatched, known);
            Check(news.StartsWith("REPORT FOR MISTY MOUNTAIN ORCS: THE NEWS AND WHAT BEARS ON IT") && news.Contains("THE CLOCK\n- Orc Castle [castle_orc]: decided by about ")
                  && news.Contains("YOUR LORDS WHO CAN BE THERE IN TIME (") && news.Contains("Bolg [lord_bolg]") && news.Contains("in time even if your orders land late")
                  && news.Contains("OTHERS WHO CAN BE THERE BEFORE IT IS DECIDED") && news.Contains("(further off, but marching into this theatre")
                  && news.Contains("HOLDINGS HERE") && news.Contains("READY-MADE FACTS") && news.Contains("THE REST OF THE WAR (") && news.Contains("WHAT YOU CAN ASK")
                  && !news.Contains("ENEMY Baranthir [lord_baranthir]") && news.Contains("Baranthir [lord_baranthir] 400 men") && !news.Contains("Ugluk [lord_ugluk]: ") && news.Contains("more further off (list lords)")
                  && !news.Contains("lookups/") && leaks.Count == 0,
                  "the news report: the news, its clock, the lords in time with their marks, the others who could join, the holdings here, the facts, the rest "
                  + "of the war and the questions; the far lord only named beyond the clock, the idle enemy left out, no look-up files, nothing the realm could not know"
                  + (leaks.Count > 0 ? ": " + string.Join("; ", leaks) : ""));
            Check(news.Length < full.Length, "the news report is shorter than the whole one (" + news.Length + " against " + full.Length + " characters)");

            // a first look keeps the whole report (decision 2), with the questions it may ask; a vote gets the realm's diplomacy and purse
            var first = new List<Trigger> { new Trigger { Hours = 80000, Kind = "first_look", Text = "First look since the campaign was loaded." } };
            Theatre whole = Theatre.Build(v, first, Theatre.ByLine(v), Theatre.ByLine(v));
            string wholeText = Views.NewsReport(v, first, x, whole).Replace("\r", "");
            var vote = new List<Trigger> { new Trigger { Hours = 80000, Kind = "vote", Text = "A vote is open in your realm: war on Rohan [v1]. It closes Summer 10." } };
            Theatre realm = Theatre.Build(v, vote, Theatre.ByLine(v), Theatre.ByLine(v));
            string realmText = Views.NewsReport(v, vote, x, realm).Replace("\r", "");
            Check(whole.Whole && wholeText.StartsWith("REPORT FOR MISTY MOUNTAIN ORCS\n") && wholeText.Contains("YOUR LORDS AND ARMIES") && wholeText.Contains("Ugluk [lord_ugluk]: ")
                  && wholeText.TrimEnd().EndsWith(Views.AskMenu.Split('\n').Last()) && realm.Recipe == "realm" && realmText.Contains("is about your realm's diplomacy")
                  && realmText.Contains("THE REST OF THE WAR") && !realmText.Contains("YOUR LORDS WHO CAN BE THERE"),
                  "a first look gets the whole report with the questions; news of a vote alone gets the realm's diplomacy, its purse and sum, and the rest of the war");

            // a letter's theatre is the places it names (decision 4)
            var letter = new List<Trigger> { new Trigger { Hours = 80000, Kind = "letter", Text = "A letter from the ruler of Dol Guldur [dg] (l3): \"Hold Orc Keep [castle_two] until I come.\"" } };
            Theatre lt = Theatre.Build(v, letter, Theatre.ByLine(v), Theatre.ByLine(v));
            Check(lt.IsTheatre && lt.Focuses.Any(f => f.Id == "castle_two"), "a letter's theatre is the places it names: " + string.Join(", ", lt.Focuses.Select(f => f.Id)));

            // the question book: every list, the regions, and the look-ups, the enemy's castle near the realm's lands among them
            Dictionary<string, string> pages = Facts.Lookups(w, Brain.Get("orcs").LastSeen, Brain.Get("orcs").Trails);
            foreach (KeyValuePair<string, string> kv in Facts.EnemyHoldings(w, v)) pages[kv.Key] = kv.Value;
            JObject book = JObject.Parse(Json.Line(Views.Book(v, x, 77, Views.Areas(v), pages)));
            string bookText = Views.BookText(v, x, Views.Areas(v), pages);
            Check(book["lists"]?["lords"]?.Count() == v.Forces.Count && book["lists"]["lords"].Any(i => ((string)i["t"]).Contains("Ugluk [lord_ugluk]"))
                  && book["pages"]?["castle_men"] != null && ((string)book["pages"]["castle_men"]).StartsWith("Fornost [castle_men], a castle of Kingdom of Arnor and Gondor (at war with you)")
                  && book["areas"]?.Count() >= 1 && (int)book["poke"] == 77 && Audit.Fog(v, bookText, s.WatchedSees, s.InspectedByWatched, known).Count == 0,
                  "the question book holds every lord, the regions and the look-ups, an enemy castle near the realm's lands among them, and nothing the realm could not know");

            // end to end: a poke in theatre mode
            Agents.Of("orcs").Config.News = "theatre";
            Brain.OnHour(World(80001));
            Brain.OnHour(World(80002));
            Files.FlushAll();
            string poke = ReadIf(Path.Combine(dir, "poke.txt"));
            JObject meta = JObject.Parse(ReadIf(Path.Combine(dir, "poke.json")));
            int n = (int)meta["number"];
            bool bookThere = File.Exists(Path.Combine(dir, "book", "poke-" + n + ".json"));
            string lookups = Path.Combine(dir, "lookups");
            bool noLookups = !Directory.Exists(lookups) || !Directory.GetFiles(lookups).Any();
            Check(meta["news"]?["mode"]?.ToString() == "theatre" && (poke.Contains("THE NEWS AND WHAT BEARS ON IT") || meta["news"]?["recipe"]?.ToString() == "whole")
                  && bookThere && noLookups,
                  "in theatre mode a poke sends its news report (or the whole one for a first look), writes its question book and no look-up files, and poke.json says so ("
                  + meta["news"]?["recipe"] + ")");
            // the next poke, about Aragorn, is a theatre's
            Brain.OnNotice(new Notice { Id = 9001, Hours = 80002.5, Kind = "besieged", Text = "Orc Castle is besieged by Aragorn (Kingdom of Arnor and Gondor).", Factions = { "orcs" }, Place = "castle_orc", Party = "p_aragorn" });
            Brain.OnHour(World(80003));
            Files.FlushAll();
            poke = ReadIf(Path.Combine(dir, "poke.txt"));
            meta = JObject.Parse(ReadIf(Path.Combine(dir, "poke.json")));
            string pokes = ReadIf(Path.Combine(Brain.Dir, "pokes_orcs.jsonl"));
            string lastRecord = pokes.Split('\n').Where(l => l.Trim().Length > 0).Last();
            Check(meta["news"]?["recipe"]?.ToString() == "theatre" && poke.Contains("THE NEWS AND WHAT BEARS ON IT") && poke.Contains("Orc Castle [castle_orc]")
                  && meta["news"]?["names"]?.Any(nm => nm.ToString() == "Aragorn") == true && lastRecord.Contains("\"sent\":\"news\"") && lastRecord.Contains("\"news\":\"REPORT FOR"),
                  "a news poke in theatre mode: the report about it goes, poke.json carries the theatre's names for the war room, and the record keeps both reports");

            // shadow mode: the whole report goes, the news report is written beside it
            Agents.Of("orcs").Config.News = "shadow";
            Brain.OnNotice(new Notice { Id = 9002, Hours = 80003.5, Kind = "raided", Text = "Orc Village is being raided by Aragorn (Kingdom of Arnor and Gondor).", Factions = { "orcs" }, Place = "village_orc", Party = "p_aragorn" });
            Brain.OnHour(World(80004));
            Files.FlushAll();
            poke = ReadIf(Path.Combine(dir, "poke.txt"));
            lastRecord = ReadIf(Path.Combine(Brain.Dir, "pokes_orcs.jsonl")).Split('\n').Where(l => l.Trim().Length > 0).Last();
            Check(poke.Contains("YOUR LORDS AND ARMIES") && !poke.Contains("THE NEWS AND WHAT BEARS ON IT") && lastRecord.Contains("\"news_mode\":\"shadow\"") && lastRecord.Contains("\"news\":\"REPORT FOR")
                  && File.Exists(Path.Combine(Brain.Dir, "reports", "orcs_latest_news.txt")),
                  "in shadow mode the whole report goes and the news report is written beside it, for comparison");
            Agents.Of("orcs").Config.News = "full";

            // the agent's answer time: the median and the one in ten, once five answers are in
            Check(AgentConfig.NewsMode("Theater") == "theatre" && AgentConfig.NewsMode("shadow") == "shadow" && AgentConfig.NewsMode("anything") == "full",
                  "agent.txt's news setting reads theatre, shadow or full");
        }
    }
}
