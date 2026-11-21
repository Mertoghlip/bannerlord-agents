using System.Text.Json.Nodes;
using BannerlordAgents.Routes;

namespace BannerlordAgents.Dashboard
{
    /// <summary>
    /// Dashboard.exe selftest: the way-finder, the timing, the crossing atlas, the words, the map picture and the agent's log, on
    /// a made-up map and made-up records in a scratch folder. A river runs north to south with one bridge; a mountain wall
    /// runs east to west with one pass; a wood lies on the plain.
    /// </summary>
    internal static class SelfTest
    {
        private static int _failed, _passed;

        public static int Run()
        {
            string scratch = Path.Combine(Path.GetTempPath(), "BannerlordAgents-dashboard-selftest");
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            Directory.CreateDirectory(scratch);
            WalkMap m = Samples.SmallMap();
            m.Dir = Path.Combine(scratch, "map", "test");
            Directory.CreateDirectory(m.Dir);
            var f = new Finder(m);

            // the way-finder
            RouteResult open = f.Find(5, 5, 20, 40);
            Check(open.Ok && Math.Abs(open.Length - open.Straight) < 0.5f, "across open plain the way is the straight line (" + Words.D1(open.Length) + " against " + Words.D1(open.Straight) + ")");
            RouteResult river = f.Find(20, 29, 80, 29);
            Check(river.Ok && river.Crossings.Count == 1 && river.Crossings[0].Ground == Ground.Bridge, "across the river the way takes the bridge (" + river.Crossings.Count + " crossings)");
            Check(river.Ok && river.Length >= river.Straight - 0.01f && river.Length < river.Straight * 1.1f, "the bridge lies on the straight line, so the way is barely longer (" + Words.D1(river.Length) + ")");
            var blocked = new bool[m.W * m.H];
            for (int y = 30; y <= 31; y++) for (int x = 48; x <= 51; x++) blocked[y * m.W + x] = true;
            RouteResult cut = f.Find(20, 29, 80, 29, null, blocked);
            Check(!cut.Ok && cut.Why.Contains("no way"), "with the bridge closed there is no way across (" + (cut.Why ?? "found one") + ")");
            RouteResult pass = f.Find(20, 30, 20, 54);
            Check(pass.Ok && pass.NarrowWidth <= 5f && pass.Length > pass.Straight + 4, "the way north goes round by the pass, four wide (" + Words.D1(pass.NarrowWidth) + " across, " + Words.D1(pass.Length) + " long)");
            RouteResult wood = f.Find(20, 10, 40, 10);
            float inWood = wood.OnGround.GetValueOrDefault(Ground.Forest);
            Check(wood.Ok && inWood > 8, "the shortest way goes through the wood (" + Words.D1(wood.OnGround.GetValueOrDefault(Ground.Forest)) + " of it)");
            RouteResult dear = f.Find(20, 10, 40, 10, Costs.ForestDear);
            Check(dear.Ok && dear.OnGround.GetValueOrDefault(Ground.Forest) < inWood, "counting forest twice, the way goes round the wood");

            // a bridge the copy missed: rebuilt from its face's centre, or learned from a way the game planned over it
            WalkMap noBridge = Samples.SmallMap();
            for (int y = 30; y <= 31; y++) for (int x = 48; x <= 51; x++) { noBridge.Ground[y * noBridge.W + x] = Ground.River; noBridge.Walk[y * noBridge.W + x] = false; }
            Check(!new Finder(noBridge).Find(20, 29, 80, 29).Ok, "without its bridge the river cannot be crossed");
            noBridge.CrossingFaces.Add(new CrossingFace { X = 49.6f, Y = 30.4f, T = Ground.Bridge });
            noBridge.StampCrossingFaces();
            RouteResult over = new Finder(noBridge).Find(20, 29, 80, 29);
            Check(over.Ok && over.Crossings.Count == 1 && noBridge.Rebuilt >= 3, "a bridge known only by its face's centre is rebuilt bank to bank (" + noBridge.Rebuilt + " cells; " + (over.Why ?? "crossed") + ")");
            WalkMap unlearned = Samples.SmallMap();
            for (int y = 30; y <= 31; y++) for (int x = 48; x <= 51; x++) { unlearned.Ground[y * unlearned.W + x] = Ground.River; unlearned.Walk[y * unlearned.W + x] = false; }
            bool learnt = unlearned.Learn(new List<float[]> { new[] { 40f, 30.5f }, new[] { 60f, 30.5f } });
            Check(learnt && new Finder(unlearned).Find(20, 29, 80, 29).Ok && unlearned.Learned >= 4, "a way the game planned across it opens it (" + unlearned.Learned + " cells learned)");

            // the time, by the game's rules (GamePace): a speed of 4.8 covers 4 map units an hour (speed / 1.2); forest adds -0.3 to the sum
            var speed48 = BannerlordAgents.Common.Pace.FromSpeed(4.8f);
            double hOpen = Marching.Hours(f.Find(5, 30, 45, 30), m, speed48, 6, out _);
            Check(Math.Abs(hOpen - 10) < 0.3, "40 units of open plain at speed 4.8 (4 map units an hour) by day take 10 hours (" + Words.D1(hOpen) + ")");
            double hWood = Marching.Hours(wood, m, speed48, 6, out _);
            Check(hWood > 20 / 4.0 + 1, "through the wood it is slower (" + Words.D1(hWood) + " hours for 20 units)");
            double hNight = Marching.Hours(f.Find(5, 30, 45, 30), m, speed48, 22, out _);
            Check(hNight > hOpen + 0.5, "setting off at night it takes longer (" + Words.D1(hNight) + " hours)");
            var forested = new BannerlordAgents.Common.Pace { Base = 2f, Factors = 0.1f };
            Check(Math.Abs(BannerlordAgents.Common.GamePace.Speed(forested, Ground.Forest, true) - 2f * (1 + 0.1f - 0.3f - 0.25f)) < 1e-4
                  && Math.Abs(BannerlordAgents.Common.GamePace.Speed(forested, Ground.Forest, true, true) - 1f) < 1e-4,
                  "the ground and the night add into the speed's one sum, and the game's floor of 1 holds (" + BannerlordAgents.Common.GamePace.Speed(forested, Ground.Forest, true) + ")");
            var cal = new GameCalendar();
            Check(cal.When(24 * 21 * 4 + 24 * 22 + 15) == "Summer 2, 1, 15:00", "the calendar: " + cal.When(24 * 21 * 4 + 24 * 22 + 15));

            // the atlas
            Atlas a = Atlas.Build(m);
            AtlasCrossing bridge = a.Crossings.FirstOrDefault(c => c.Kind == "bridge");
            AtlasCrossing gap = a.Crossings.FirstOrDefault(c => c.Kind == "pass" && Math.Abs(c.X - 11.5f) < 4 && Math.Abs(c.Y - 46) < 4);
            Check(bridge != null && bridge.CutsOff && bridge.Critical, "the atlas finds the bridge, the only way across (" + (bridge?.Name ?? "not found") + ")");
            Check(gap != null && gap.Critical, "the atlas finds the pass through the wall (" + (gap?.Name ?? "not found: " + string.Join("; ", a.Crossings.Select(c => c.Kind + " " + c.X + "," + c.Y))) + ")");
            Check(bridge != null && bridge.Name.StartsWith("the bridge ", StringComparison.Ordinal) && bridge.Name.Contains(" of "), "crossings are named by the nearest holding: " + bridge?.Name);
            JsonText.Write(Atlas.PathIn(m.Dir), a);
            Atlas back = Atlas.Load(m.Dir);
            Check(back != null && back.Crossings.Count == a.Crossings.Count, "the atlas is kept and read back (" + back?.Crossings.Count + ")");

            // the words
            List<string> lines = Explain.Way(m, a, river, BannerlordAgents.Common.Pace.FromSpeed(4f), "its own pace", 6, cal.WhenShort, out double hours, out _);
            string all = string.Join(" ", lines);
            Check(all.Contains("map units") && all.Contains("bridge") && all.Contains("no other way") && !double.IsNaN(hours), "the answer names the way's length, the bridge and that it is the only way: " + all);
            string rules = Marching.Rules(Marching.PeopleBonuses(new Dictionary<string, float[]> { ["orcs"] = new[] { 0, 0, 0, 0, 0, 0, 0.1f, 0 } }, new Dictionary<string, string> { ["orcs"] = "Misty Mountain Orcs" }));
            Check(rules.Contains("shortest way, not the fastest") && rules.Contains("divided by 1.2") && rules.Contains("Misty Mountain Orcs +10% on snow"),
                  "how marching works is told in plain words, the game's numbers and the peoples' bonuses with it");
            RouteResult given = f.FromPoints(new List<float[]> { new[] { 20f, 29f }, new[] { 50f, 30.5f }, new[] { 80f, 29f } });
            Check(given.Ok && given.Crossings.Count == 1, "a way given by its corners is described too (" + given.Crossings.Count + " crossing)");

            // the picture
            byte[] png = Picture.Render(m);
            Check(png.Length > 100 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71, "the map picture is a PNG (" + png.Length + " bytes)");

            // the log
            LogTests(scratch);
            RecordingTests(scratch);

            Console.WriteLine(_failed == 0 ? "ALL " + _passed + " CHECKS PASSED" : _failed + " OF " + (_passed + _failed) + " CHECKS FAILED");
            return _failed == 0 ? 0 : 1;
        }

        private static void LogTests(string scratch)
        {
            var store = new Store(scratch);
            string data = store.DataDir("camp"), war = store.WarRoomLogs;
            Directory.CreateDirectory(data);
            Directory.CreateDirectory(war);
            string pokes = Path.Combine(data, "pokes_orcs.jsonl"), orders = Path.Combine(data, "orders.jsonl"), calls = Path.Combine(war, "calls.jsonl");
            // an earlier load, then this one
            System.IO.File.WriteAllText(pokes, "{\"type\":\"poke\",\"number\":1,\"hours\":100,\"triggers\":[{\"text\":\"Old poke.\"}]}\n");
            long p0 = new FileInfo(pokes).Length;
            System.IO.File.WriteAllText(orders, "");
            System.IO.File.WriteAllText(calls, "");
            System.IO.File.AppendAllText(Path.Combine(data, "loads.jsonl"), "{\"load_id\":\"L1\",\"realm\":\"orcs\",\"wall\":\"2026-10-16 10:00:00.000\",\"offsets\":{\"pokes_orcs.jsonl\":0}}\n"
                + "{\"load_id\":\"L2\",\"realm\":\"orcs\",\"wall\":\"2026-10-16 11:00:00.000\",\"offsets\":{\"pokes_orcs.jsonl\":" + p0 + ",\"orders.jsonl\":0,\"calls.jsonl\":0}}\n");
            System.IO.File.AppendAllText(pokes, "{\"type\":\"poke\",\"number\":2,\"hours\":90,\"triggers\":[{\"text\":\"Enemy army in view: Aragorn.\",\"party\":\"p_ar\"}],\"view\":{\"big\":\"" + new string('x', 5000) + "\"}}\n");
            string folder = Path.Combine(war, "calls", "c1");
            Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(Path.Combine(folder, "answer.json"), "{\"orders\":[{\"lord\":\"lord_bolg\",\"do\":\"defend\",\"at\":\"castle_v7\",\"why\":\"keep out of his way\"}],\"plan\":\"Send no relief.\"}");
            System.IO.File.AppendAllText(calls, "{\"kind\":\"answer\",\"at\":\"2026-10-16 11:05:00.000\",\"poke\":2,\"ok\":true,\"campaign\":\"camp\",\"seconds\":21.5,\"folder\":" + JsonValue.Create(folder).ToJsonString() + ",\"written\":true}\n");
            System.IO.File.AppendAllText(orders, "{\"type\":\"agent_receipt\",\"poke\":2,\"lord\":\"lord_bolg\",\"do\":\"defend\",\"status\":\"applied\",\"reason\":\"Bolg defend Gramburg\",\"hours\":91}\n"
                + "{\"type\":\"agent_receipt\",\"poke\":2,\"lord\":\"lord_x\",\"do\":\"gather\",\"status\":\"refused\",\"reason\":\"already in an army\",\"hours\":91}\n"
                + "{\"type\":\"agent_receipt\",\"poke\":2,\"lord\":\"lord_y\",\"do\":\"rest\",\"status\":\"appl");
            var reader = new LogReader(store);
            LoadInfo l2 = store.Load("camp", "L2");
            List<LogEntry> got = reader.Read("camp", l2, "orcs");
            Check(got.Count == 4 && got[0].Kind == "woke" && got[1].Kind == "answer" && got[2].Kind == "carried" && got[3].Kind == "refused",
                  "the log of a load: woken, answered, carried out, refused, in order, without the earlier load's poke or a half-written line (" + string.Join(", ", got.Select(e => e.Kind)) + ")");
            Check(got.Count > 1 && got[1].Orders?.Count == 1 && got[1].Plan == "Send no relief." && Math.Abs(got[1].Hours - 90.01) < 1e-6, "an answer carries its orders, its plan and its poke's hour");
            System.IO.File.AppendAllText(orders, "ied\",\"reason\":\"Yazneg rest\",\"hours\":92}\n");
            got = reader.Read("camp", l2, "orcs");
            Check(got.Count == 5 && got[4].Kind == "carried", "a line finished later is read on the next look (" + got.Count + ")");
            LoadInfo l1 = store.Load("camp", "L1");
            Check(l1.Ends.TryGetValue("pokes_orcs.jsonl", out long e1) && e1 == p0, "a load's lines end where the next load's begin");
            // a files agent's council (war room 0.20): no orders and no plan line, its plan a page it wrote
            var filesAnswer = JsonNode.Parse("{\"orders\":[],\"pages\":[{\"page\":\"notes/lessons\",\"text\":\"# lessons\\n\\nStorm early.\"},{\"page\":\"plan/the-plan\",\"text\":\"# the plan (Autumn 11)\\n\\nHold Glad Thaw.\",\"why\":\"x\"}]}") as JsonObject;
            List<string> writes = LogReader.PageWrites(filesAnswer, out string planPage);
            Check(writes.Count == 2 && writes[1] == "plan/the-plan" && planPage == "Hold Glad Thaw.", "a files council shows the plan page it wrote and its page writes (" + string.Join(", ", writes) + "; " + planPage + ")");
        }

        /// <summary>Whole runs played back (0.10): the line a run kept across its loads, an hour's light copy, the run's news.</summary>
        private static void RecordingTests(string scratch)
        {
            // load A from the run's save at hour 100 played to 120; B loaded a save of hour 110 and played to 130 (A's 110 to 120 were never
            // saved); X loaded at 125 but its frames are gone; C carried on from a save of hour 131 to 140
            var frames = new Dictionary<string, List<long>>
            {
                ["A"] = Enumerable.Range(100, 21).Select(h => (long)h).ToList(),
                ["X"] = new List<long>(),
                ["B"] = Enumerable.Range(110, 21).Select(h => (long)h).ToList(),
                ["C"] = Enumerable.Range(131, 10).Select(h => (long)h).ToList(),
            };
            var run = new RunInfo { Id = "test", Campaign = "camp" };
            run.Segments.Add(new RunSeg { Load = "A", From = 100.3, To = 120.5 });
            run.Segments.Add(new RunSeg { Load = "B", From = 110.2, To = 130.9 });
            run.Segments.Add(new RunSeg { Load = "X", From = 125.0, To = 125.5 });
            run.Segments.Add(new RunSeg { Load = "C", From = 131.4, To = 140.2 });
            Recording.KeepLine(run, l => frames[l]);
            List<long> line = run.Segments.SelectMany(s => s.Hours).ToList();
            bool kept = run.Segments[0].Hours.First() == 100 && run.Segments[0].Hours.Last() == 109 && run.Segments[1].Hours.First() == 110 && run.Segments[1].Hours.Last() == 130
                        && run.Segments[2].Hours.Count == 0 && run.Segments[3].Hours.First() == 131 && line.Count == 41 && line.Distinct().Count() == 41;
            Check(kept, "a run's kept line: each hour once, a load of an earlier save dropping what the load before played past it, a later save carried on, a load "
                        + "whose frames are gone cutting nothing (" + string.Join(" | ", run.Segments.Select(s => s.Load + " " + (s.Hours.Count > 0 ? s.Hours.First() + "-" + s.Hours.Last() : "none"))) + ")");

            // an hour's light copy
            string frame = "{\"campaign\":\"camp\",\"load_id\":\"A\",\"hours\":105.25,\"date\":\"Summer 1\",\"factions\":[{\"id\":\"orcs\",\"name\":\"Orcs\",\"color\":\"#222222\",\"kingdom\":true,\"ruler\":\"x\"}],"
                + "\"settlements\":[{\"id\":\"castle_a\",\"name\":\"Keep\",\"kind\":\"castle\",\"faction\":\"orcs\",\"x\":10.04,\"y\":20.06,\"garrison\":300}],"
                + "\"parties\":["
                + "{\"id\":\"p_lord\",\"name\":\"Bolg\",\"kind\":\"lord\",\"faction\":\"orcs\",\"x\":1.234,\"y\":5.678,\"men\":100,\"strength\":250.4,\"gold\":9999,\"route\":[1,2,3,4]},"
                + "{\"id\":\"p_mate\",\"name\":\"Azog\",\"kind\":\"lord\",\"faction\":\"orcs\",\"x\":1,\"y\":5,\"attached\":true,\"army_leader\":\"p_lord\"},"
                + "{\"id\":\"p_gar\",\"kind\":\"garrison\",\"faction\":\"orcs\",\"x\":10,\"y\":20},"
                + "{\"id\":\"p_car\",\"kind\":\"caravan\",\"faction\":\"orcs\",\"x\":3,\"y\":3,\"in_settlement\":\"castle_a\"},"
                + "{\"id\":\"p_band\",\"kind\":\"bandit\",\"x\":7,\"y\":7}],"
                + "\"realms\":{\"orcs\":{\"name\":\"Orcs\",\"agent\":true,\"cut\":[\"p_band\"],\"orders\":{\"p_lord\":\"Bolg: defend Keep\"},\"view\":{\"seen\":[{\"id\":\"p_band\",\"name\":\"Bandits\",\"relation\":\"enemy\",\"kind\":\"bandit\",\"x\":7,\"y\":7,\"near\":\"Keep\"}],"
                + "\"last_seen\":[],\"holdings\":[{\"id\":\"castle_a\",\"garrison\":300}],\"news\":\"long\"}}},"
                + "\"sieges\":[{\"settlement\":\"castle_a\",\"besieger\":\"p_x\",\"besieger_faction\":\"men\",\"start_hours\":100,\"engines\":[]}],"
                + "\"battles\":[{\"key\":\"k\",\"kind\":\"field\",\"x\":4,\"y\":4,\"start_hours\":104,\"attackers\":{\"leader\":\"p_lord\",\"faction\":\"orcs\",\"healthy\":90,\"start_healthy\":100},\"defenders\":{\"leader\":\"p_band\",\"healthy\":5,\"start_healthy\":30}}]}";
            JsonNode slim;
            using (var doc = System.Text.Json.JsonDocument.Parse(frame)) slim = JsonNode.Parse(Recording.SlimJson(doc.RootElement));
            JsonArray parties = slim?["parties"] as JsonArray;
            var ids = parties?.Select(p => (string)p[0]).ToList() ?? new List<string>();
            JsonArray lord = parties?.FirstOrDefault(p => (string)p[0] == "p_lord") as JsonArray, mate = parties?.FirstOrDefault(p => (string)p[0] == "p_mate") as JsonArray;
            JsonNode realm = slim?["realms"]?["orcs"];
            bool light = ids.SequenceEqual(new[] { "p_lord", "p_mate", "p_band" }) && lord != null && (double)lord[4] == 1.2 && (double)lord[7] == 250 && lord.Count == 19
                         && mate != null && (int)mate[18] == 1 && (slim["settlements"] as JsonArray)?.Count == 1 && ((JsonArray)slim["settlements"][0]).Count == 8
                         && (realm?["view"]?["seen"] as JsonArray)?.Count == 1 && ((JsonArray)realm["view"]["seen"][0]).Count == 13 && realm["view"]["news"] == null
                         && (string)realm["orders"]?["p_lord"] == "Bolg: defend Keep" && ((JsonArray)slim["battles"][0]).Count == 14 && slim["factions"]?[0]?["ruler"] == null;
            Check(light, "an hour's light copy keeps what the map draws (lords, those riding in an army marked, bands; no garrison or caravan inside), positions to a tenth, "
                         + "each realm's sightings, cuts and orders, the sieges and battles, and nothing else (" + slim?.ToJsonString().Length + " characters)");

            // the run's news: a battle between realms, one with a caravan and one with a band, a holding taken, a tool's report; and an hour another load took over
            var store = new Store(scratch);
            string data = store.DataDir("newscamp");
            Directory.CreateDirectory(data);
            string notices = Path.Combine(data, "notices.jsonl");
            System.IO.File.WriteAllText(notices,
                "{\"hours\":101,\"kind\":\"battle\",\"text\":\"Battle: Bolg (Orcs) against Aragorn (Gondor); Aragorn won.\",\"factions\":[\"orcs\"]}\n"
                + "{\"hours\":102,\"kind\":\"battle\",\"text\":\"Battle: Bolg (Orcs) against Armed Caravan of Tom (Gondor); Bolg won.\",\"factions\":[\"orcs\"]}\n"
                + "{\"hours\":103,\"kind\":\"battle\",\"text\":\"Battle: Bolg (Orcs) against Looters (Looters); Bolg won.\",\"factions\":[\"orcs\"]}\n"
                + "{\"hours\":104,\"kind\":\"holding_changed\",\"text\":\"Keep passed from Gondor to Orcs.\",\"factions\":[\"orcs\"],\"place\":\"castle_a\"}\n"
                + "{\"hours\":104,\"kind\":\"holding_changed\",\"text\":\"Keep passed from Gondor to Orcs.\",\"factions\":[\"orcs\"],\"place\":\"castle_a\"}\n"
                + "{\"hours\":105,\"kind\":\"tool\",\"text\":\"Bolg is sheltering.\",\"factions\":[\"orcs\"]}\n"
                + "{\"hours\":106,\"kind\":\"holding_changed\",\"text\":\"Hold passed from Rohan to Dunland.\",\"factions\":[\"rohan\"]}\n"
                + "{\"hours\":115,\"kind\":\"captured\",\"text\":\"Played past the save, never kept.\",\"factions\":[\"orcs\"]}\n");
            System.IO.File.WriteAllText(Path.Combine(data, "loads.jsonl"), "{\"load_id\":\"A\",\"offsets\":{\"notices.jsonl\":0}}\n");
            var newsRun = new RunInfo { Id = "news", Campaign = "newscamp" };
            newsRun.Who["orcs"] = "agent";
            newsRun.Segments.Add(new RunSeg { Load = "A", From = 100, Hours = Enumerable.Range(100, 10).Select(h => (long)h).ToList(), Until = 110 });
            List<object> news = Recording.News(store, newsRun, new HashSet<string> { "Orcs", "Gondor" });
            string kinds = string.Join(",", news.Select(n => (string)n.GetType().GetProperty("kind").GetValue(n)));
            Check(kinds == "battle,holding_changed", "the run's news: the battle between realms and the holding taken, once each; no caravan, band, tool report, other "
                                                     + "realms' news or hour played past a save (" + kinds + ")");
        }

        private static void Check(bool ok, string what)
        {
            if (ok) _passed++; else _failed++;
            Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        }
    }
}
