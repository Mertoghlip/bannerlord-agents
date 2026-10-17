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
        }

        private static void Check(bool ok, string what)
        {
            if (ok) _passed++; else _failed++;
            Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        }
    }
}
