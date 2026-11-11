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
        private static readonly System.Text.RegularExpressions.Regex SightingLine = new System.Text.RegularExpressions.Regex(
            @"^Enemy (lord|army) in view: (.+?) \((.+?)\), ([\d,]+) men, strength ([\d,]+), (.*?); (.*?); seen by (.+)\.$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// "SmokeTest --wake-check pokes_(realm).jsonl --run (id) --out lines.jsonl" (0.15): every recorded poke of a run through the wake rule and the two short lists, from its recorded
        /// view alone: each line's lane and rule are written out, to compare against the study's own sorting, and the lists' lengths are told, with the lists of any wake named by --show.
        /// </summary>
        private static int WakeCheck(string[] args)
        {
            string run = Arg(args, "--run"), outFile = Arg(args, "--out"), show = Arg(args, "--show");
            var shown = new HashSet<int>((show ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse));
            var chances = new List<int>();
            var free = new List<int>();
            var chars = new List<int>();
            var lanes = new Dictionary<string, int>();
            using (var w = outFile != null ? new StreamWriter(outFile, false, new System.Text.UTF8Encoding(false)) : null)
                foreach (string line in File.ReadLines(args[1]))
                {
                    if (line.Length < 50 || (run != null && line.IndexOf("\"run\":\"" + run + "\"", 0, Math.Min(line.Length, 2000), StringComparison.Ordinal) < 0)) continue;
                    Recorded p = ReadRecorded(line);
                    if (p == null) continue;
                    ExtrasOf(p);
                    Func<ViewParty, ViewParty, double> hours = (a, b) => Views.Dist(a.X, a.Y, b.X, b.Y) * Theatre.RoadFactor / Math.Max(0.3, Theatre.PerHour(a));
                    foreach (Trigger t in p.Triggers)
                    {
                        System.Text.RegularExpressions.Match m = SightingLine.Match(t.Text ?? "");
                        if (t.Kind == "enemy_in_view" && m.Success)
                        {
                            ViewParty e = p.View.Seen.FirstOrDefault(x => x.Id == t.Party || x.Name == m.Groups[2].Value);
                            t.Facts = new LineFacts
                            {
                                Army = m.Groups[1].Value == "army", Men = double.Parse(m.Groups[4].Value.Replace(",", "")), Strength = double.Parse(m.Groups[5].Value.Replace(",", "")), Doing = WakeRules.DoingOf(m.Groups[7].Value),
                                OwnNear = e == null ? 0 : p.View.Forces.Where(f => Views.Dist(f.X, f.Y, e.X, e.Y) <= WakeRules.Reach).Sum(f => (double)f.Strength)
                            };
                        }
                        WakeRules.Sort(t);
                        lanes.TryGetValue(t.Lane, out int n);
                        lanes[t.Lane] = n + 1;
                        w?.WriteLine(Json.Line(new { p.Number, t.Kind, t.Sub, t.Lane, t.Rule, t.Text }));
                    }
                    List<string> c = Views.ChanceLines(p.View, hours), f2 = Views.NoOrderLines(p.View) ?? new List<string>();
                    chances.Add(c.Count);
                    free.Add(f2.Count);
                    chars.Add(c.Sum(l => l.Length) + f2.Sum(l => l.Length));
                    if (shown.Contains(p.Number))
                        Console.WriteLine("#" + p.Number + " LORDS WITH NO ORDER\n" + string.Join("\n", f2) + "\n#" + p.Number + " CHANCES TO STRIKE\n" + string.Join("\n", c) + "\n");
                }
            int Med(List<int> l) => l.OrderBy(x => x).ElementAt(l.Count / 2);
            int P90(List<int> l) => l.OrderBy(x => x).ElementAt(l.Count * 9 / 10);
            Console.WriteLine("Pokes " + chances.Count + "; lines by lane: " + string.Join(", ", lanes.Select(kv => kv.Key + " " + kv.Value)));
            Console.WriteLine("Chances to strike a wake: mean " + chances.Average().ToString("0.0") + ", median " + Med(chances) + ", one in ten " + P90(chances) + ", most " + chances.Max() + ", none on " + (100 * chances.Count(x => x == 0) / chances.Count) + "%");
            Console.WriteLine("Lords with no order a wake: mean " + free.Average().ToString("0.0") + ", median " + Med(free) + ", one in ten " + P90(free) + ", most " + free.Max());
            Console.WriteLine("The two lists' size a wake, characters: mean " + chars.Average().ToString("0") + ", one in ten " + P90(chars) + ", most " + chars.Max());
            return 0;
        }

        /// <summary>
        /// The wake rule, the two short lists, letter marks and the too-poor line (0.15, docs/reports/2026-11-06-news-wake-study.md): real lines of run A sorted as the study sorted them,
        /// and the rule in play in a made-up world: what wakes, what waits, what rides along, and what a council takes up.
        /// </summary>
        private static void WakeTests(string root)
        {
            Check(WakeRules.Problems.Count == 0, "the wake rule's list reads and its facts exist" + (WakeRules.Problems.Count > 0 ? ": " + string.Join("; ", WakeRules.Problems) : ""));
            string[] locked = { "first_look", "letter_marked", "lord_taken", "holding_attacked", "vote_closing" };
            Check(locked.All(n => WakeRules.Rules.Any(r => r.Name == n && r.Locked && r.Lane == WakeRules.Now)) && WakeRules.Rules.Count(r => r.Locked) == locked.Length,
                  "the locked rules all wake at once, and only those are locked");

            string Lane(string kind, string text, LineFacts f = null)
            {
                var t = new Trigger { Kind = kind, Text = text, Facts = f };
                WakeRules.Sort(t);
                return t.Lane;
            }
            // real lines of run A, by the lanes the study walked
            var table = new List<Tuple<string, string, string>>
            {
                Tuple.Create("tool", "Grishûl is sheltering in Erebor from Imák (strength 220 against 123).", WakeRules.Digest),
                Tuple.Create("tool", "Throgma is falling back, away from Gundabad Orc Raiders.", WakeRules.Digest),
                Tuple.Create("tool", "Gûthrak rides with all haste to relieve Orbug.", WakeRules.Digest),
                Tuple.Create("tool", "Skûzla took the volunteers at Ashúrz: refilled to 80%.", WakeRules.Digest),
                Tuple.Create("tool", "Lorgath cashed in: handed over 153 prisoners for 17,482 in ransom.", WakeRules.Digest),
                Tuple.Create("tool", "Zulgora found few recruits at Liutburg.", WakeRules.Digest),
                Tuple.Create("tool", "Dûrnia, shadowing Alenon: marching to besiege Stranding.", WakeRules.Digest),
                Tuple.Create("tool", "Floraidh is beaten or gone.", WakeRules.Wait),
                Tuple.Create("tool", "Vugra gave up the chase of Lek.", WakeRules.Wait),
                Tuple.Create("tool", "Mhargza has not moved for 7 hours while sheltering in Caras Laerolin.", WakeRules.Now),
                Tuple.Create("tool", "A relief is coming to Imnagath: 5 enemy lords and armies.", WakeRules.Now),
                Tuple.Create("tool", "A line from a tool the agent wrote itself.", WakeRules.Now),
                Tuple.Create("order_ended", "The order \"Thorzag supply Dannenglor\" ended: handed over 379 food at Dannenglor; its next order starts.", WakeRules.Wait),
                Tuple.Create("order_ended", "The order \"Radbug recruits\" ended: volunteers are within reach, but he has too little gold.", WakeRules.Wait),
                Tuple.Create("raided", "Grish-morn is being raided by Elendara (Kingdom of Arnor and Gondor).", WakeRules.Now),
                Tuple.Create("battle", "Fight over a raid at Eithel Glin: Karim against Eithel Glin; Karim won.", WakeRules.Now),
                Tuple.Create("captured", "Malthorn (Kingdom of Arnor and Gondor) was taken prisoner by Dramthak.", WakeRules.Now),
                Tuple.Create("besieged", "Western Moria is besieged by Aragorn, 5,532 men.", WakeRules.Now),
                Tuple.Create("holding_changed", "Western Moria passed from Misty Mountain Orcs to Kingdom of Arnor and Gondor.", WakeRules.Now),
                Tuple.Create("army_dispersed", "The army of Naglur broke up (CohesionDepleted).", WakeRules.Now),
                Tuple.Create("vote_closing", "The vote [v1] closes Winter 10 and you have not voted.", WakeRules.Now),
                Tuple.Create("looted", "Tawar Beleg was looted.", WakeRules.Digest),
                Tuple.Create("proposal", "Bûrzghâsh proposed: Call Goblins to War Against Gondor.", WakeRules.Digest),
                Tuple.Create("alliance", "Goblins and Misty Mountain Orcs formed an alliance.", WakeRules.Digest),
                Tuple.Create("letter", "A letter from the ruler of Misty Mountain Orcs (l95): \"Thank you for l94.\"", WakeRules.Wait),
                Tuple.Create("enemy_in_view", "Enemy army in view: Aragorn, 8,999 men, strength 22,895, 75.4 from Dush-salth.", WakeRules.Now)
            };
            List<string> wrong = table.Where(x => Lane(x.Item1, x.Item2) != x.Item3).Select(x => x.Item2 + " went " + Lane(x.Item1, x.Item2)).ToList();
            Check(wrong.Count == 0, "real lines of run A go in the lanes the study walked" + (wrong.Count > 0 ? ": " + string.Join("; ", wrong) : ""));
            Check(Lane("letter", "A letter (l103): \"A warning for Dramthak's road.\"", new LineFacts { Urgent = true }) == WakeRules.Now, "a marked letter wakes at once; an unmarked one waits");
            string Lord(string doing, double strength, double near) => Lane("enemy_in_view", "Enemy lord in view: X.", new LineFacts { Doing = doing, Strength = strength, OwnNear = near, Men = 100 });
            Check(Lord("going", 240, 5000) == WakeRules.Digest && Lord("moving", 699, 0) == WakeRules.Digest && Lord("patrolling", 300, 0) == WakeRules.Digest, "a weak enemy lord passing never wakes the agent");
            Check(Lord("going", 700, 0) == WakeRules.Now && Lord("chasing", 100, 0) == WakeRules.Now && Lord("raiding", 100, 0) == WakeRules.Now && Lord("besieging", 100, 0) == WakeRules.Now && Lord("inside", 100, 0) == WakeRules.Now,
                  "a lord of 700 or more wakes it, and so does any lord chasing, raiding, besieging or inside a holding");
            Check(Lord("on_way_to_raid", 333, 3119) == WakeRules.Now && Lord("on_way_to_raid", 333, 499) == WakeRules.Wait && Lord("on_way_to_raid", 333, 500) == WakeRules.Now,
                  "a weak raider on its way wakes it with 1.5 times its strength of ours near, and waits otherwise");
            Check(WakeRules.DoingOf("on the way to raid Mazûg-salth") == "on_way_to_raid" && WakeRules.DoingOf("raiding Gor-mîr") == "raiding" && WakeRules.DoingOf("going to Merham") == "going"
                  && WakeRules.DoingOf("besieging Lasgalor") == "besieging" && WakeRules.DoingOf("chasing Bolg") == "chasing" && WakeRules.DoingOf("inside Grafton") == "inside" && WakeRules.DoingOf("fighting") == "fighting",
                  "what an enemy is doing reads as the study read it");

            // in play: one realm under the rule, on the news report
            string dir = Path.Combine(root, "wake_orcs");
            var cfg = AgentConfig.FromSettings("orcs", dir, "live", "all");
            cfg.Wake = "rule";
            cfg.News = "theatre";
            Brain.Reset("smoketest-wake", double.NaN, new List<AgentConfig> { cfg }, null);
            PartyRow Own(string id, string name, float x, float y, int men, float strength) => new PartyRow { Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = men, Strength = strength, Speed = 4.5f, FoodDays = 20, X = x, Y = y, Doing = "Hold", Spottable = true };
            PartyRow Foe(string id, string name, float x, float y, int men, float strength, string doing = "Hold", string target = null) => new PartyRow { Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = men, Strength = strength, Speed = 4f, FoodDays = 20, X = x, Y = y, Doing = doing, TargetSettlement = target, Spottable = true };
            PartyRow bolg = Own("bolg", "Bolg", 104, 100, 300, 500);
            Snapshot World(double h, params PartyRow[] more) => ToolWorld(h, new[] { bolg }.Concat(more).ToArray());
            int Pokes() => Brain.PokesWatched;
            string Poke() { Files.FlushAll(); return ReadIf(Path.Combine(dir, "poke.txt")); }
            void Answer(int n)
            {
                string orders = Path.Combine(dir, "orders");
                Directory.CreateDirectory(orders);
                string file = Path.Combine(orders, "poke-" + n + ".json");
                File.WriteAllText(file, "{\"poke\": " + n + ", \"orders\": []}");
                System.Threading.Thread.Sleep(350);   // a file younger than 300 ms counts as still being written
                AgentChannel.Scan();
            }
            Channel ch = AgentChannel.For("orcs");
            double h0 = 90000;
            int before = Pokes();
            Brain.OnHour(World(h0));
            Check(Pokes() == before + 1 && Poke().Contains("First look"), "a first look wakes the agent and reads the whole report");
            Check(ch.AwaitingAnswer, "and the channel knows that poke waits for its answer");
            Answer(ch.LastPoke);
            Check(!ch.AwaitingAnswer, "an answer ends the wait");
            before = Pokes();

            // a weak lord passing: no wake, for a day
            PartyRow passing = Foe("pass", "Unthery", 118, 104, 163, 240);
            Brain.OnHour(World(h0 + 1, passing));
            Files.FlushAll();
            Check(Pokes() == before, "a weak enemy lord passing wakes no one");
            string book = ReadIf(Path.Combine(Brain.Dir, "wake_orcs.jsonl"));
            Check(book.Contains("\"lane\":\"digest\"") && book.Contains("\"rule\":\"weak_lord_passing\"") && book.Contains("Unthery"), "each line's lane and rule are written down");
            Check(ReadIf(Path.Combine(dir, "clock.txt")).StartsWith((h0 + 1).ToString("R", Text.Inv), StringComparison.Ordinal), "the game's clock is written for the war room, poke or no poke");

            // a raid wakes at once; the digest rides along, in WHAT CHANGED
            var raid = new Notice { Id = NoticeIds.Next(), Hours = h0 + 2, Kind = "raided", Text = "Orc Village is being raided by Elendara (Kingdom of Arnor and Gondor).", Place = "village_orc", Factions = { "orcs" } };
            Brain.OnNotice(raid);
            Brain.OnHour(World(h0 + 2, passing));
            string p1 = Poke();
            string why = p1.Substring(p1.IndexOf("WHY YOU ARE ASKED NOW", StringComparison.Ordinal));
            why = why.Substring(0, why.IndexOf("WHAT CHANGED", StringComparison.Ordinal));
            Check(Pokes() == before + 1 && why.Contains("Orc Village is being raided") && !why.Contains("Unthery"), "a raid wakes the agent at once, and the digest is not its news");
            Check(p1.Contains("Quieter news since then, which did not call you:") && p1.Contains("Enemy lord in view: Unthery"), "the digest rides that call, in WHAT CHANGED");
            Files.FlushAll();
            JObject rec = Lines(ReadIf(Path.Combine(Brain.Dir, "pokes_orcs.jsonl"))).Last();
            Check((string)rec["triggers"]?[0]?["lane"] == "now" && (string)rec["digest"]?[0]?["sub"] == "enemy_lord", "the poke's record keeps each line's lane, and the digest apart");

            // while that poke waits for its answer, news holds, so the next report is fresh
            before = Pokes();
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h0 + 3, Kind = "captured", Text = "Bolg's friend was taken prisoner.", Factions = { "orcs" } });
            Brain.OnHour(World(h0 + 3, passing));
            Check(Pokes() == before, "news that wakes at once waits while the last poke has no answer");
            Answer(ch.LastPoke);
            Brain.OnHour(World(h0 + 4, passing));
            Check(Pokes() == before + 1 && Poke().Contains("Bolg's friend"), "and goes as soon as the answer is in");
            Answer(ch.LastPoke);
            before = Pokes();

            // a letter unmarked waits its 6 hours, and goes on the whole report
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h0 + 5, Kind = "letter", Text = "A letter from the ruler of Dol Guldur [dg] (l1): \"Thanks for your word.\"", Factions = { "orcs" } });
            for (int i = 5; i <= 10; i++) Brain.OnHour(World(h0 + i, passing));
            Check(Pokes() == before, "an unmarked letter waits");
            Brain.OnHour(World(h0 + 11, passing));
            string p2 = Poke();
            Check(Pokes() == before + 1 && p2.Contains("Thanks for your word") && p2.Contains("YOUR LORDS AND ARMIES"), "after 6 in-game hours it wakes the agent, on the whole report");
            Answer(ch.LastPoke);
            before = Pokes();

            // a marked letter wakes at once
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h0 + 12, Kind = "letter", Text = "A letter from the ruler of Dol Guldur [dg] (l2): \"Turn back now.\" (Marked urgent by its writer.)", Factions = { "orcs" }, Urgent = true });
            Brain.OnHour(World(h0 + 12, passing));
            Check(Pokes() == before + 1 && Poke().Contains("Turn back now"), "a marked letter wakes the agent at once");
            Answer(ch.LastPoke);
            before = Pokes();

            // a council takes up what is held: no wake for it later
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h0 + 13, Kind = "order_ended", Text = "The order \"Bolg goto Orc Keep\" ended: arrived.", Factions = { "orcs" } });
            Brain.OnHour(World(h0 + 13, passing));
            AskBriefing(ch, dir);
            string situation = ReadIf(Path.Combine(dir, "report.txt"));
            Check(situation.Contains("NEWS THAT DID NOT CALL YOU") && situation.Contains("Bolg goto Orc Keep"), "a council reads the news held back");
            for (int i = 14; i <= 21; i++) Brain.OnHour(World(h0 + i, passing));
            Check(Pokes() == before, "and that news no longer wakes the agent");

            // a whole day with nothing: a quiet day
            for (int i = 22; i <= 37; i++) Brain.OnHour(World(h0 + i, passing));
            Check(Pokes() == before + 1 && Poke().Contains("A full day has passed"), "a full day with no wake brings a quiet day's report");
            Answer(ch.LastPoke);

            // the two lists, on Celeborn's wake (Misty Orcs #6301): Morznak the one lord with no order, Celeborn off the news
            PartyRow morznak = Own("morznak", "Morznak", 726, 1053, 227, 1177), shagash = Own("shagash", "Shagash", 730, 1040, 150, 331);
            PartyRow celeborn = Foe("celeborn", "Celeborn", 728.6f, 1050.7f, 131, 333, "GoToSettlement", "village_orc");
            celeborn.Doing = "RaidSettlement";
            PartyRow big = Foe("big", "Theoden", 729, 1052, 400, 900, "RaidSettlement", "village_orc");
            PartyRow far = Foe("far", "Lonely", 300, 300, 80, 150, "RaidSettlement", "village_orc");
            Snapshot s = ToolWorld(h0 + 40, morznak, shagash, celeborn, big, far);
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, new Brain.Memory());
            foreach (ViewParty e in v.Seen) e.Doing = e.Name == "Celeborn" ? "on the way to raid Mazûg-salth" : "raiding Mazûg-salth";
            Func<ViewParty, ViewParty, double> hours = (a, b) => Views.Dist(a.X, a.Y, b.X, b.Y) / 3.0;
            List<string> chances = Views.ChanceLines(v, hours);
            Check(chances.Count == 1 && chances[0].Contains("Celeborn") && chances[0].Contains("on the way to raid") && chances[0].Contains("Morznak") && chances[0].Contains("(no order)"),
                  "a lone raider with strength of ours near is a chance, with who is near and their orders; a strong lord, or one with none of ours near, is not: " + string.Join(" | ", chances));
            string text = Views.Report(v, null, new ReportExtras { Chances = chances });
            Check(text.Contains("LORDS WITH NO ORDER (2 of your 2") && text.Contains("- Morznak [lord_morznak]: 227 men, strength 1,177") && text.Contains("CHANCES TO STRIKE (lone enemy lords under strength 700"),
                  "the report lists the lords with no order and the chances to strike");
            Check(!Views.Report(v, null, new ReportExtras()).Contains("LORDS WITH NO ORDER"), "a report that does not ask for them has neither (the lieutenant's)");

            // run A's #6248: a crowd fighting near one holding is one line, measured together
            var crowd = new List<PartyRow>();
            for (int i = 0; i < 15; i++) crowd.Add(Foe("rohan" + i, "Rider" + i, 730, 1041, 100, 100 + i));
            Snapshot s2 = ToolWorld(h0 + 41, new[] { morznak, shagash }.Concat(crowd).ToArray());
            View v2 = Views.Build(s2, "orcs", s2.WatchedSees, s2.HostileToWatched, new Brain.Memory());
            foreach (ViewParty e in v2.Seen) e.Doing = "fighting";
            Check(Views.ChanceLines(v2, hours).Count == 0, "fifteen lords fighting together are measured together: 1,605 strength is no chance for 1,508 of ours");
            foreach (ViewParty e in v2.Seen.Skip(5).ToList()) v2.Seen.Remove(e);
            List<string> group = Views.ChanceLines(v2, hours);
            Check(group.Count == 1 && group[0].StartsWith("- 5 of Kingdom of Arnor and Gondor fighting near", StringComparison.Ordinal) && group[0].Contains("Rider4"), "a crowd of five we can beat is one line: " + string.Join(" | ", group));
            var many = new List<PartyRow>();
            for (int i = 0; i < 8; i++) many.Add(Foe("raid" + i, "Raider" + i, 726 + i, 1053, 40, 50));
            Snapshot s3 = ToolWorld(h0 + 42, new[] { morznak }.Concat(many).ToArray());
            View v3 = Views.Build(s3, "orcs", s3.WatchedSees, s3.HostileToWatched, new Brain.Memory());
            foreach (ViewParty e in v3.Seen) e.Doing = "raiding Mazûg-salth";
            List<string> capped = Views.ChanceLines(v3, hours);
            Check(capped.Count == Views.MostChances + 1 && capped.Last().Contains("And 3 more"), "the list stops at five chances and says how many more");

            // too poor to recruit
            Check(PurseView.PoorText(new PartyRow { Gold = 2700, RecruitFloor = 3050 }).Contains("too little to recruit") && PurseView.PoorText(new PartyRow { Gold = 3200, RecruitFloor = 3050 }).Contains("close to the 3,050")
                  && PurseView.PoorText(new PartyRow { Gold = 50000, RecruitFloor = 3050 }) == null && PurseView.PoorText(new PartyRow { Gold = 100, RecruitFloor = 0 }) == null && PurseView.PoorText(new PartyRow()) == null,
                  "a lord's gold shows beside him only when it is near the floor for recruiting");
            morznak.Gold = 2700;
            morznak.RecruitFloor = 3050;
            Snapshot s4 = ToolWorld(h0 + 43, morznak);
            View v4 = Views.Build(s4, "orcs", s4.WatchedSees, s4.HostileToWatched, new Brain.Memory());
            string line = Views.LordLine(v4.Forces.First(p => p.Name == "Morznak"));
            Check(line.Contains("gold 2,700, too little to recruit") && Views.NoOrderLines(v4)[0].Contains("too little to recruit"), "and the report's lines say so: " + line);

            // the letter's mark comes from the answer
            string dgDir = Path.Combine(root, "wake_dg"), orcDir = Path.Combine(root, "wake_orcs2");
            var orc = AgentConfig.FromSettings("orcs", orcDir, "live", "all");
            var dg = AgentConfig.FromSettings("dg", dgDir, "live", "all");
            Brain.Reset("smoketest-wake2", double.NaN, new List<AgentConfig> { orc, dg }, null);
            Snapshot W5 = ToolWorld(h0 + 50, Own("bolg", "Bolg", 104, 100, 300, 500));
            W5.Factions["dg"] = new FactionRow { Id = "dg", Name = "Dol Guldur", Ruler = "Khamul", AtWar = { "player_k" }, Allies = { "orcs" } };
            W5.Factions["orcs"].Allies.Add("dg");
            W5.FactionNames["dg"] = "Dol Guldur";
            W5.Seats = new List<SeatSight>
            {
                new SeatSight { Realm = "orcs", Sees = W5.WatchedSees, Inspected = W5.InspectedByWatched, Hostile = W5.HostileToWatched },
                new SeatSight { Realm = "dg", Sees = new Dictionary<string, Sighting>(), Inspected = new HashSet<string>(), Hostile = new HashSet<string> { "player_k" } }
            };
            Brain.OnHour(W5);
            string st = Allies.Send(Agents.Of("orcs").Latest, "orcs", JObject.Parse("{\"to\": \"dg\", \"text\": \"Turn back now.\", \"urgent\": true}"), "agent", 1, out string status);
            Files.FlushAll();
            Check(status == "sent" && Allies.AllLetters().Last().Urgent && Brain.Get("dg").Pending.Last().Urgent && Brain.Get("dg").Pending.Last().Text.Contains("(Marked urgent by its writer.)"),
                  "a letter marked urgent carries the mark to its reader's news: " + st);
            BattleWordTests(root);
        }

        /// <summary>0.15.1: as the player's own battle begins, a realm agent that is on gets the word in its channel; one that is off does not. Since 0.18.2 the lieutenant does too (its handover).</summary>
        private static void BattleWordTests(string root)
        {
            string Word(string kind, string mode)
            {
                string dir = Path.Combine(root, "spare-" + kind + "-" + mode);
                Directory.CreateDirectory(dir);
                new RealmAgent(new AgentConfig { Realm = "r_" + kind + mode, ChannelDir = dir, Kind = kind, Mode = mode }).Channel.PlayerBattle(2192838.5);
                Files.FlushAll();
                string f = Path.Combine(dir, "spare.txt");
                return File.Exists(f) ? File.ReadAllText(f) : null;
            }
            Check(Word("realm", "live") == "2192838.5" && Word("realm", "shadow") == "2192838.5", "a realm agent on live or shadow is told the player's battle began, with the hour");
            Check(Word("realm", "off") == null, "an agent that is off is not told");
            Check(Word("lieutenant", "live") == "2192838.5", "the lieutenant is told the player's battle began (0.18.2: a continuous lieutenant writes its handover then)");
        }
    }
}
