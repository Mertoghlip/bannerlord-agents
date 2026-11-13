using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BannerlordAgents;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13 step 3, the agents' own tools (docs/PLAN-0.13.md A1, A4 to A8): an answer's tools kept, used once, refused; a change making a
        /// second version while the first runs on; the record of a kept tool; retiring; the limits and names; shadow mode; the library carried
        /// over a new load; the call-back and time guards; the try, asked directly and through the channel as the war room asks it.
        /// </summary>
        private static void OwnToolTests(string root, string agentDir)
        {
            string dir = Path.Combine(root, "owntools-agent");
            Settings.AgentDir = dir;
            Settings.Agent = "live";
            Brain.Reset("smoketest-owntools", double.NaN, "orcs", null);
            var moves = new List<KeyValuePair<int, Move>>();
            var released = new List<KeyValuePair<int, string>>();
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => released.Add(new KeyValuePair<int, string>(id, why));
            Runner.ThenSink = (id, then) => { };
            var bolg = new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, SizeLimit = 320, Strength = 400, Speed = 5, FoodDays = 10, X = 104, Y = 100, Doing = "Hold", Spottable = true };
            var azog = new PartyRow { Id = "p_azog", Leader = "lord_azog", Name = "Azog", Faction = "orcs", Kind = "lord", Men = 200, SizeLimit = 250, Strength = 260, Speed = 5, FoodDays = 10, X = 130, Y = 100, Doing = "Hold", Spottable = true };
            Diag.Hours = 50000;
            Brain.OnHour(ToolWorld(50000, bolg, azog));
            Files.FlushAll();
            int poke = (int?)JObject.Parse(ReadIf(Path.Combine(dir, "poke.json")))["number"] ?? -1;
            Check(poke > 0, "the made-up realm has a poke to answer (#" + poke + ")");
            string receipts = Path.Combine(dir, "receipts.log");
            string Answer(string name, JObject file)
            {
                file["poke"] = poke;
                File.WriteAllText(Path.Combine(dir, "orders", name), file.ToString());
                Thread.Sleep(400);
                int before = ReadIf(receipts).Length;
                AgentChannel.Scan();
                Files.FlushAll();
                while (Background.ToMain.TryDequeue(out _)) { }
                return ReadIf(receipts).Substring(before);
            }
            JObject Tool(string name, bool keep, string definition) => new JObject { ["name"] = name, ["keep"] = keep, ["definition"] = JObject.Parse(definition) };
            const string Sweep = "{\"about\": \"patrols around its holding\", \"takes\": {\"at\": \"own_fort: the holding\"}, \"start\": \"a\", \"states\": {\"a\": {\"do\": \"patrol at\"}}}";

            // an answer keeps the circuit, uses a sweep once, and writes a broken tool; an order naming the broken one is refused with it
            string said = Answer("poke-" + poke + ".json", new JObject
            {
                ["tools"] = new JArray(Tool("circuit", true, CircuitJson), Tool("sweep", false, Sweep), Tool("broken", true, "{\"start\": \"nowhere\", \"states\": {\"a\": {\"do\": \"fly at\"}}}")),
                ["orders"] = new JArray(JObject.Parse("{\"lord\": \"lord_bolg\", \"do\": \"broken\", \"at\": \"castle_orc\"}"))
            });
            Check(said.Contains("tool 1 (circuit): KEPT. circuit v1 is kept in your library") && said.Contains("tool 2 (sweep): ONE-OFF. used once") && said.Contains("tool 3 (broken): REFUSED. the checker found")
                  && said.Contains("(lord_bolg broken): REFUSED. its tool broken was refused (the checker found"),
                  "an answer's tools are answered one by one: the circuit kept, the sweep used once, the broken one refused with its problems, and its order with it: " + said);
            string lib = Path.Combine(dir, "library");
            string tools = ReadIf(Path.Combine(dir, "tools.txt"));
            Check(File.Exists(Path.Combine(lib, "index.json")) && File.Exists(Path.Combine(lib, "circuit.v1.json")) && !File.Exists(Path.Combine(lib, "sweep.v1.json")),
                  "the kept tool is in the realm's library folder, its version as written beside the index; the one-off is not");
            Check(tools.Contains("YOUR OWN TOOLS") && tools.Contains("- **circuit** (yours, v1)") && tools.Contains("Its record: not used yet. Kept at poke #" + poke) && !tools.Contains("**sweep**"),
                  "tools.txt lists the kept tool under YOUR OWN TOOLS, after ours, with its record; not the one-off");
            Check(File.Exists(Path.Combine(dir, "tools", "circuit.json")) && File.Exists(Path.Combine(dir, "tools", "defend.json")) && File.ReadAllText(Path.Combine(dir, "tools", "defend.json")).Contains("\"states\""),
                  "every tool's whole definition can be read: tools\\defend.json for ours, tools\\circuit.json for the realm's own");
            string data = Brain.Dir;
            List<JObject> book = Lines(ReadIf(Path.Combine(data, "toolbook.jsonl")));
            Check(book.Count(l => (string)l["type"] == "tool_written") == 3 && book.Count(l => (string)l["type"] == "tool_check") == 3 && book.Any(l => (string)l["type"] == "tool_kept" && (string)l["name"] == "circuit")
                  && book.Any(l => (string)l["type"] == "tool_one_off") && book.Any(l => (string)l["type"] == "tool_refused" && (string)l["name"] == "broken")
                  && book.Any(l => (string)l["type"] == "tool_check" && (string)l["name"] == "broken" && ((JArray)l["problems"]).Count > 0),
                  "the toolbook has every tool written (its whole definition), its check with its problems, and what became of it");
            Check(Lines(ReadIf(Path.Combine(data, "episodes.jsonl"))).Count(l => (string)l["type"] == "tool") == 3, "the episode index has a line for each tool of the answer");
            ToolDef sweep = OwnLibrary.Find("orcs", "sweep");
            var then = AgentChannel.OrderFrom(JObject.Parse("{\"lord\": \"lord_bolg\", \"do\": \"sweep\", \"tool_hash\": \"" + sweep?.Hash + "\"}"), null, poke, "then");
            Check(sweep != null && sweep.OneOff && then.Tool == sweep, "a next order (then) naming a tool its answer wrote starts that very tool, by its fingerprint");

            // 0.13.13 (Oct 30, the lieutenant's chat-18: recruit_pair written and ordered in one answer, the orders refused as unknown and the
            // tool kept after them): the lieutenant's orders wait for its answer's tools, then carry their definitions or are refused with them
            var held = new List<AgentChannel.AgentOrder>
            {
                AgentChannel.OrderFrom(JObject.Parse("{\"lord\": \"lord_bolg\", \"do\": \"sweep\", \"at\": \"castle_orc\"}"), null, -1, "chat-18.json"),
                AgentChannel.OrderFrom(JObject.Parse("{\"lord\": \"lord_azog\", \"do\": \"broken\", \"at\": \"castle_orc\"}"), null, -1, "chat-18.json")
            };
            for (int k = 0; k < held.Count; k++) { held[k].Index = k + 1; held[k].Realm = "orcs"; }
            var givenNow = new List<AgentChannel.AgentOrder>();
            var saidNow = new List<string>();
            int before18 = ReadIf(receipts).Length;
            int gave = Lieutenant.GiveAfterTools(held, new Dictionary<string, ToolDef>(StringComparer.OrdinalIgnoreCase) { ["sweep"] = sweep },
                                                 new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["broken"] = "the checker found a problem" },
                                                 Agents.Of("orcs")?.Channel, givenNow.Add, saidNow.Add);
            Files.FlushAll();
            string said18 = ReadIf(receipts).Substring(before18);
            Check(gave == 1 && givenNow.Count == 1 && givenNow[0].Tool == sweep && saidNow.Count == 1 && saidNow[0].Contains("broken")
                  && said18.Contains("(lord_azog broken): REFUSED. its tool broken was refused (the checker found a problem)"),
                  "the lieutenant's orders follow its answer's tools: one naming the tool it just wrote carries that tool, one naming a refused tool is refused with it: " + said18);

            // 0.15.2 (Nov 6, test A1, the clock stopped: chat-78, with no tools, gave its orders ahead of chat-76 and 77, which had tools, and NOLDOR came back on): answers land in the order they came
            while (Background.ToMain.TryDequeue(out _)) { }
            var landed = new List<string>();
            Lieutenant.GivenForTest = o => landed.Add(o.Lord);
            Lieutenant.Carry(new Lieutenant.Proposal { Chat = 901, Tools = new JArray(Tool("first_sweep", false, Sweep)), Orders = new JArray(JObject.Parse("{\"lord\": \"lord_bolg\", \"do\": \"first_sweep\", \"at\": \"castle_orc\"}")) }, 901, "orcs");
            Lieutenant.Carry(new Lieutenant.Proposal { Chat = 902, Orders = new JArray(JObject.Parse("{\"lord\": \"lord_azog\", \"do\": \"defend\", \"at\": \"castle_orc\"}")) }, 902, "orcs");
            for (int w = 0; w < 500 && landed.Count < 2; w++) { Background.DrainToMain(); Thread.Sleep(10); }
            Lieutenant.GivenForTest = null;
            while (Background.ToUi.TryDequeue(out _)) { }
            Files.FlushAll();
            Check(landed.SequenceEqual(new[] { "lord_bolg", "lord_azog" }), "an answer with no tools lands after an earlier answer with tools, not ahead of it: " + string.Join(", ", landed));
            // and how many orders wait for the clock: each until it is given, however the clock moved meanwhile (0.19.2; until then a tick forgot them all)
            Lieutenant.ForgetWaiting();
            long ticks = Watchdog.CampaignTicks;
            Lieutenant.NoteCarried(801, 73);
            Lieutenant.NoteCarried(802, 79);
            bool counted = Lieutenant.WaitingOrders == 152 && Lieutenant.WaitingAnswers == 2;
            Watchdog.CampaignTicks = ticks + 1;
            bool stillCounted = Lieutenant.WaitingOrders == 152 && Lieutenant.WaitingAnswers == 2;
            Lieutenant.Started(801, 7);
            bool some = Lieutenant.WaitingOrders == 145 && Lieutenant.WaitingAnswers == 2;
            Lieutenant.Started(801, 66);
            Lieutenant.Started(999, 5);   // an answer not counted changes nothing
            bool oneLeft = Lieutenant.WaitingOrders == 79 && Lieutenant.WaitingAnswers == 1;
            Lieutenant.ForgetWaiting();
            Check(counted && stillCounted && some && oneLeft && Lieutenant.WaitingOrders == 0 && Lieutenant.WaitingAnswers == 0,
                  "the lieutenant's orders agreed to wait until each is given, whether or not the clock moved meanwhile, and the count starts again at a load");
            // an answer carried out: its orders stop waiting as each is given, and one refused with its tool stops waiting too
            while (Background.ToMain.TryDequeue(out _)) { }
            var landed2 = new List<string>();
            Lieutenant.GivenForTest = o => landed2.Add(o.Lord);
            Lieutenant.NoteCarried(903, 3);
            Lieutenant.Carry(new Lieutenant.Proposal { Chat = 903, Tools = new JArray(Tool("broken_sweep", false, "{\"start\": \"nowhere\", \"states\": {\"a\": {\"do\": \"fly at\"}}}")),
                                                       Orders = new JArray(JObject.Parse("{\"lord\": \"lord_azog\", \"do\": \"defend\", \"at\": \"castle_orc\"}"),
                                                                           JObject.Parse("{\"lord\": \"lord_bolg\", \"do\": \"defend\", \"at\": \"castle_orc\"}"),
                                                                           JObject.Parse("{\"lord\": \"lord_bolg\", \"do\": \"broken_sweep\", \"at\": \"castle_orc\"}")) }, 903, "orcs");
            bool waitedWhileQueued = Lieutenant.WaitingOrders == 3;
            for (int w = 0; w < 500 && Lieutenant.WaitingOrders > 0; w++) { Background.DrainToMain(1); Thread.Sleep(10); }
            Lieutenant.GivenForTest = null;
            while (Background.ToUi.TryDequeue(out _)) { }
            Files.FlushAll();
            Check(waitedWhileQueued && landed2.Count == 2 && Lieutenant.WaitingOrders == 0 && Lieutenant.WaitingAnswers == 0,
                  "an answer's orders stop waiting one by one as the game gives them, and one refused with its tool stops waiting too: given " + landed2.Count + ", still waiting " + Lieutenant.WaitingOrders);
            Lieutenant.ForgetWaiting();

            // a change: v2 kept while an order on v1 runs on with v1; the same definition again changes nothing
            ToolDef v1 = OwnLibrary.Find("orcs", "circuit");
            var onV1 = new ToolOrder { Tool = "circuit", Def = v1, Lord = "lord_bolg", LordName = "Bolg", Party = "p_bolg", At = "castle_orc", Realm = "orcs" };
            onV1.Places.AddRange(new[] { "village_orc", "castle_two" });
            Runner.Start(9600, onV1);
            string changed = CircuitJson.Replace("\"sell_above\": [3000,", "\"sell_above\": [2000,");
            said = Answer("poke-" + poke + "-b.json", new JObject { ["tools"] = new JArray(Tool("circuit", true, changed)) });
            ToolDef v2 = OwnLibrary.Find("orcs", "circuit");
            var onV2 = new ToolOrder { Tool = "circuit", Def = v2, Lord = "lord_azog", LordName = "Azog", Party = "p_azog", At = "castle_orc", Realm = "orcs" };
            onV2.Places.AddRange(new[] { "village_orc" });
            Runner.Start(9601, onV2);
            Check(said.Contains("CHANGED. circuit v2 replaces v1") && v2 != null && v2.Version == 2 && v2.Settings["sell_above"].Default == 2000
                  && Runner.All().First(t => t.Id == 9600).Def.Version == 1 && Runner.All().First(t => t.Id == 9601).Def.Version == 2,
                  "writing a kept tool again makes v2: new orders get it, the order already running keeps v1, the two side by side");
            said = Answer("poke-" + poke + "-c.json", new JObject { ["tools"] = new JArray(Tool("circuit", true, changed)) });
            Check(said.Contains("UNCHANGED. circuit v2 is already in your library as written"), "the very same definition again changes nothing");

            // the record: a kept tool's run ends, and its record says so (tools.txt takes it at the day's refresh)
            Brain.OnHour(ToolWorld(50001, bolg, azog));
            Runner.Ended(9600, "replaced by a new order");
            KeptTool kept = OwnLibrary.For("orcs").Kept.First(k => k.Name == "circuit");
            Check(kept.Record.Runs == 1 && kept.Record.Ended.ContainsKey("replaced") && kept.Record.ByVersion.ContainsKey("v1") && kept.Version == 2 && kept.Before.Count == 1,
                  "a kept tool's record gains each run that ends, by how it ended and by version; its earlier version is kept in its history");
            Check(OwnLibrary.RecordText(kept).StartsWith("Its record: used 1 time"), "and the library's listing words it: " + OwnLibrary.RecordText(kept));
            Brain.OnHour(ToolWorld(50024, bolg, azog));
            Check(ReadIf(Path.Combine(dir, "tools.txt")).Contains("used 1 time"), "tools.txt shows the record from the next day's refresh");

            // retiring: gone from the library and tools.txt; its running order finishes as it is; retiring what is not there is refused
            said = Answer("poke-" + poke + "-d.json", new JObject { ["tools"] = new JArray(JObject.Parse("{\"name\": \"circuit\", \"retire\": true}"), JObject.Parse("{\"name\": \"nothing_here\", \"retire\": true}")) });
            Check(said.Contains("RETIRED. circuit v2 has left your library") && said.Contains("REFUSED. no tool of yours is called nothing_here") && OwnLibrary.Find("orcs", "circuit") == null
                  && !File.Exists(Path.Combine(dir, "tools", "circuit.json")) && ReadIf(Path.Combine(dir, "tools.txt")).Contains("- None yet.") && Runner.All().Any(t => t.Id == 9601),
                  "a retired tool leaves the library, tools.txt and tools\\, while the order running it goes on");
            Runner.Ended(9601, "test over");

            // names and limits: never one of ours; at most 30 kept
            said = Answer("poke-" + poke + "-e.json", new JObject { ["tools"] = new JArray(Tool("defend", true, Sweep)) });
            Check(said.Contains("REFUSED") && said.Contains("'defend' is the name of one of our orders"), "a tool of the agent's may not take the name of one of ours");
            var many = new JArray();
            for (int i = 0; i < 31; i++) many.Add(Tool("keep_" + i, true, Sweep));
            said = Answer("poke-" + poke + "-f.json", new JObject { ["tools"] = many });
            Check(OwnLibrary.For("orcs").Kept.Count == OwnLibrary.MaxKept && said.Contains("tool 31 (keep_30): REFUSED. your library already holds 30 tools"),
                  "a realm keeps at most " + OwnLibrary.MaxKept + " tools; the next is refused until one is retired");

            // a new load: the library is read again from the folder and carries over
            Brain.Reset("smoketest-owntools-2", double.NaN, "orcs", null);
            Check(OwnLibrary.Find("orcs", "keep_7") != null && OwnLibrary.Find("orcs", "sweep") == null && OwnLibrary.Find("orcs", "circuit") == null,
                  "after a new load the kept tools are there again (the one-off and the retired one are not): every run starts with them");

            // shadow mode: checked, never kept
            Settings.Agent = "shadow";
            Brain.Reset("smoketest-owntools-3", double.NaN, "orcs", null);
            Brain.OnHour(ToolWorld(50100, bolg, azog));
            Files.FlushAll();
            poke = (int?)JObject.Parse(ReadIf(Path.Combine(dir, "poke.json")))["number"] ?? -1;
            said = Answer("poke-" + poke + "-g.json", new JObject { ["tools"] = new JArray(Tool("keep_7", true, Sweep.Replace("patrols around", "watches"))) });
            Check(said.Contains("CHECKED. valid; it would be kept as keep_7 v2 (shadow mode") && OwnLibrary.Find("orcs", "keep_7").Version == 1, "in shadow mode a tool is checked, and the library is left as it was: " + said);
            Settings.Agent = "live";
            Brain.Reset("smoketest-owntools-4", double.NaN, "orcs", null);

            // the call-back guard: six a day, the rest held and summed when the day is out
            var chat = new PartyRow { Id = "p_chat", Leader = "lord_chat", Name = "Mauhur", Faction = "orcs", Kind = "lord", Men = 100, Strength = 150, Speed = 5, FoodDays = 10, X = 100, Y = 100, InSettlement = "castle_orc", Doing = "Hold", Spottable = true };
            ToolDef chatter = ToolDef.FromJson("{\"name\": \"chatter\", \"start\": \"a\", \"states\": {\"a\": {\"do\": \"hold\", \"when\": [{\"if\": \"true\", \"go\": \"a\", \"poke\": \"still here at {now}\"}]}}}", "chatter", "orcs");
            Diag.Hours = 52800;
            Brain.OnHour(ToolWorld(52800, chat));
            Runner.Start(9700, new ToolOrder { Tool = "chatter", Def = chatter, Lord = "lord_chat", LordName = "Mauhur", Party = "p_chat", Realm = "orcs" });
            for (int h = 52801; h <= 52824; h++) Brain.OnHour(ToolWorld(h, chat));
            Files.FlushAll();
            List<string> told = ReadIf(Path.Combine(Brain.Dir, "notices.jsonl")).Split('\n').Where(l => l.Contains("\"party\":\"p_chat\"")).Select(l => (string)JObject.Parse(l)["text"]).ToList();
            Check(told.Count(t => t.StartsWith("still here")) == 7 && told.Any(t => t.Contains("would have called you back 18 more times the day before")),
                  "a tool of the agent's calls back at most 6 times an in-game day; the rest are held and summed in one when the day is out (" + told.Count + " notices)");
            Runner.Ended(9700, "test over");

            // the time guard: over the limit three hours running, it is stopped and the lord let go
            double keptMs = Runner.SlowMs;
            Runner.SlowMs = -1;
            Runner.Start(9701, new ToolOrder { Tool = "chatter", Def = chatter, Lord = "lord_chat", LordName = "Mauhur", Party = "p_chat", Realm = "orcs" });
            for (int h = 52825; h <= 52828; h++) Brain.OnHour(ToolWorld(h, chat));
            Runner.SlowMs = keptMs;
            Check(released.Any(r => r.Key == 9701 && r.Value.Contains("was stopped: it took too long each hour")), "a tool of the agent's that takes too long each hour, three hours running, is stopped and its lord let go");
            Runner.Ended(9701, "test over");

            // the try: on the realm's newest hour, for a lord, moving no one; and through the channel, as the war room asks for it
            Brain.OnHour(ToolWorld(52829, bolg));
            string request = new JObject { ["id"] = "t1", ["name"] = "circuit", ["definition"] = CircuitJson, ["lord"] = "lord_bolg", ["at"] = "castle_orc", ["places"] = new JArray("village_orc", "castle_two") }.ToString();
            int before = moves.Count;
            string tried = ToolTry.Answer(Agents.Of("orcs"), request);
            Check(tried.StartsWith("THE TRY: circuit for Bolg") && tried.Contains("rule 3 (\"next_due(places, visited, 24) != none\"): true. It fires.") && tried.Contains("recruit Orc Village [village_orc]")
                  && tried.Contains("No rule fires now") && moves.Count == before,
                  "the try says the first move, which rule fires and where it leads, and moves no one:\n" + tried);
            string asked = ToolTry.Answer(Agents.Of("orcs"), new JObject { ["definition"] = CircuitJson, ["lord"] = "lord_nobody" }.ToString());
            Check(asked.Contains("has no lord leading a party called 'lord_nobody'"), "a try for a lord the realm does not have says so");
            File.WriteAllText(Path.Combine(dir, "orders", "toolcheck-request-t2.json"), request);
            AgentChannel.Scan();
            Check(ReadIf(Path.Combine(dir, "toolcheck-t2.txt")).StartsWith("THE TRY: circuit for Bolg") && !File.Exists(Path.Combine(dir, "orders", "toolcheck-request-t2.json")),
                  "the war room's request for a try is answered in the channel folder at once, beside the other files");
            Files.FlushAll();
            Check(Lines(ReadIf(Path.Combine(Brain.Dir, "toolbook.jsonl"))).Any(l => (string)l["type"] == "tool_try" && (string)l["name"] == "circuit"), "each try goes into the toolbook");

            Settings.Agent = "shadow";
            Settings.AgentDir = agentDir;
        }
    }
}
