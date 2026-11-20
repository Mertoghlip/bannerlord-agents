using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// The realm's files (war room 0.20, docs/FILES.md), with no calls and no game: a made-up question book built into pages twice, the changes between the two flagged
        /// on the shelf, on the folder's changes page and at the top of the page; reads (by the Read tool's path or open) clearing "new to you"; the agent's pages written,
        /// refused, replaced (the old one kept) and taken back with a load; a files wake that says only what woke it.
        /// </summary>
        private static void FilesTests(string root)
        {
            string channel = Path.Combine(root, "files-channel");
            try { Directory.Delete(channel, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(channel);
            JsonObject Book(int garrison, double food, string order, bool captive) => (JsonObject)JsonNode.Parse(@"{
              ""poke"": 7, ""hours"": " + (captive ? "2210" : "2200") + @", ""date"": """ + (captive ? "Autumn 2, 1087, 10:00" : "Autumn 2, 1087, 00:00") + @""", ""realm"": ""orcs"", ""realm_name"": ""Misty Mountain Orcs"",
              ""areas"": [{""name"": ""Moria"", ""anchor"": ""town_MM1"", ""holdings"": [""town_MM1""]}],
              ""lists"": {
                ""lords"": [
                  {""t"": ""- Naglur [lord_MM8_1]: 410 men, strength 576; 4.6 (4.6 by road) from Zog-rod; on the move (your order: defend)."", ""area"": ""Moria"", ""ids"": [""lord_MM8_1""]},
                  {""t"": ""- Ufgrish [lord_MM9_6]: army of 179 men in 8 parties, strength 358; at Starkmoore; holding (your order: gather).\n  - Bolg [lord_MM6_5]: 122 men, strength 199"", ""area"": ""Moria"", ""ids"": [""lord_MM9_6"", ""lord_MM6_5""]}" + (captive ? "" : @",
                  {""t"": ""- Rashid [CharacterObject_5554]: 76 men, strength 107; near Yonu; on the move."", ""area"": ""Moria"", ""ids"": [""CharacterObject_5554""]}") + @"
                ],
                ""holdings"": [
                  {""t"": ""- Eastern Moria [town_MM1] (town of Throgmaw): garrison " + garrison + @" (strength 600), militia 150; food 300 of 650, about 5 days under siege; walls level 3."", ""area"": ""Moria"", ""ids"": [""town_MM1""]},
                  {""t"": ""- Zog-rod [village_MM1_2] (village of Throgmaw): hearths 300."", ""area"": ""Moria"", ""ids"": [""village_MM1_2""]}
                ],
                ""orders"": [{""t"": ""- Naglur [lord_MM8_1]: " + order + @" Eastern Moria [town_MM1]. Now on his way; since Autumn 1."", ""ids"": [""lord_MM8_1""]}],
                ""captives"": [" + (captive ? @"{""t"": ""- In Yonu [castle_A4] (castle of Gondor): 1 lord: Rashid [CharacterObject_5554] since Autumn 2, 1087, 08:00."", ""ids"": [""castle_A4"", ""CharacterObject_5554""]}" : "") + @"],
                ""enemies"": [{""t"": ""- ENEMY Aragorn [player_party] (Kingdom of Arnor and Gondor): army of 4,000 men, strength 9,000; 20 from your Eastern Moria."", ""ids"": [""player_party""]}],
                ""realm"": [{""t"": ""- In the field: 2 lords leading parties."", ""ids"": []}], ""purse"": [], ""allies"": [], ""diplomacy"": [], ""votes"": []
              },
              ""pages"": {
                ""lord_MM8_1"": ""Naglur [lord_MM8_1]: 410 men, strength 576, ranks 80%, wounded 0%, food for " + food.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" days, speed 3.0; your order: defend Eastern Moria.\nPurse: gold 5,000; wages 900 a day."",
                ""town_MM1"": ""Eastern Moria [town_MM1], your town; garrison " + garrison + @", militia 150.\nYour lords, soonest first:\n- Naglur [lord_MM8_1] (strength 576, about 3 h, your order: defend Eastern Moria)\n- Ufgrish [lord_MM9_6] (strength 358, about 30 h, your order: gather Starkmoore)""
              }
            }");
            var shelf = new Shelf(channel);
            var pages = new Dictionary<string, PageRec>(StringComparer.Ordinal) { ["plan/the-plan"] = new PageRec { Text = "Hold Eastern Moria with Naglur; Rashid raids south.", By = "you" } };
            ShelfInput Input(JsonObject b, int poke, string why) => new ShelfInput
            {
                Book = b, Poke = poke, Campaign = "camp", LoadId = "load1", RealmName = "Misty Mountain Orcs", Date = (string)b["date"], Hours = (double)b["hours"], AgentPages = pages, Why = why,
                PokeText = "POKE #" + poke + "\n\nYOUR WATCHES (1 of 12; read every in-game hour)\n- moria_watch (moria = Eastern Moria [town_MM1]): wakes you if besieged(moria) [not now]; ends Autumn 9.\n\nTHE CLOCK\n- Your answers' orders take effect about 7 in-game hours after the report they answer.\n",
                ToolsText = "ORDERS YOU CAN GIVE\n\nMoves (one step):\n- **goto** (at: any holding): Goes to a holding.\n- **hold**: Stops where the lord is.\n\nEvery move and tool also takes caution (default 1.5).\n"
            };
            int first = shelf.Build(Input(Book(300, 20, "defend", false), 7, ""));
            string dir = Path.Combine(channel, Shelf.DirName);
            Check(first == 0 && File.Exists(Path.Combine(dir, "shelf.md")) && File.Exists(Path.Combine(dir, "lords", "naglur.md")) && File.Exists(Path.Combine(dir, "lords", "army-ufgrish.md"))
                  && File.Exists(Path.Combine(dir, "settlements", "eastern-moria.md")) && File.Exists(Path.Combine(dir, "orders", "goto.md")) && File.Exists(Path.Combine(dir, "plan", "the-plan.md")),
                  "the files' first build writes the shelf and a page per lord, army, holding and order, and the agent's plan, and flags nothing (" + first + ")");
            Check(!File.Exists(Path.Combine(dir, "lords", "bolg.md")) && File.ReadAllText(Path.Combine(dir, "lords", "army-ufgrish.md")).Contains("Bolg"),
                  "a lord marching in an army has no page of his own: he is named on his army's page");
            string holding = File.ReadAllText(Path.Combine(dir, "settlements", "eastern-moria.md"));
            Check(holding.Contains("Zog-rod") && holding.Contains("Lords whose orders are tied to it (1)") && holding.Contains("within 3 days' march") && holding.Contains("within a day's march"),
                  "a holding's page has its villages, the lords tied to it, and the enemy strength around it within 3 days' and a day's march");
            Check(File.ReadAllText(Path.Combine(dir, "orders", "index.md")).Contains("- hold (hold): Stops where the lord is.") && File.ReadAllText(Path.Combine(dir, "orders", "common-settings.md")).Contains("caution"),
                  "the Orders file lists each order in a line (past its name and what it takes) and the settings every order takes");
            // the second hour: the garrison down by half, Naglur's food low and his order changed, Rashid taken
            int second = shelf.Build(Input(Book(150, 2, "patrol", true), 8, "- Your watch \"moria_watch\": Eastern Moria is besieged."));
            string sh = File.ReadAllText(Path.Combine(dir, "shelf.md"));
            Check(second >= 5 && sh.Contains("Rashid") && sh.Contains("garrison 300 → 150") && sh.Contains("food for 2 days") && sh.Contains("order defend Eastern Moria → patrol Eastern Moria") && sh.Contains("fired: Your watch"),
                  "the second build flags what changed on the shelf: a lord taken, a garrison halved, food low, an order changed, the watch that fired (" + second + " changes)");
            string lordsChanges = File.ReadAllText(Path.Combine(dir, "lords", "changes.md"));
            Check(lordsChanges.Contains("NEW ") && File.ReadAllText(Path.Combine(dir, "lords", "naglur.md")).Contains("▲") && File.ReadAllText(Path.Combine(dir, "lords", "contents.md")).Contains("▲"),
                  "each change is on its folder's changes page (NEW), at the top of its page and marked in the folder's contents");
            Check(File.ReadAllText(Path.Combine(dir, "plan", "plan-check.md")).Contains("names Rashid, who is held by the enemy"), "the plan's check names a lord the plan counts on who is now held");
            // reading clears "new to you": a Read of the page's whole path in the call's stream, and open
            string raw = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{\"file_path\":\"" + Path.Combine(dir, "lords", "naglur.md").Replace("\\", "\\\\") + "\"}}]}}\n";
            var reads = shelf.TrackReads(raw);
            shelf.Open(new[] { "settlements/eastern-moria" });
            shelf.Build(Input(Book(150, 2, "patrol", true), 9, ""));
            string sh2 = File.ReadAllText(Path.Combine(dir, "shelf.md"));
            Check(reads.reads == 1 && reads.steps == 1 && !sh2.Contains("garrison 300 → 150") && !sh2.Contains("order defend Eastern Moria → patrol") && sh2.Contains("Rashid"),
                  "a page read by its path or opened is no longer new to the agent; what it has not read still is");
            Check(shelf.Find("Eastern Moria garrison").Contains("settlements/eastern-moria") && shelf.Open(new[] { "town_MM1" }).Contains("== files/settlements/eastern-moria.md"),
                  "find names the pages holding some words, and open finds a page by the id it is about");
            Check(Room.FilesWake(12, "WHY YOU ARE ASKED NOW\n- Hiltbolt has one day of food.") == "WAKE #12 (answer with one JSON object that says \"poke\": 12)\n- Hiltbolt has one day of food.\n",
                  "a files wake says what woke the agent, and nothing else");

            // the agent's own pages: written, refused, replaced with the old one kept, removed, and taken back with a load
            var c = new Config { AgentDir = Path.Combine(root, "files-agent"), Channel = "warroom" };
            Directory.CreateDirectory(c.AgentDir);
            Memory m = Memory.Open(c, "camp", _ => { });
            m.LoadId = "load1";
            List<string> notes = m.ApplyPages((JsonObject)JsonNode.Parse("{\"pages\": [{\"page\": \"plan/the-plan\", \"text\": \"Hold Moria.\", \"why\": \"first\"}, {\"page\": \"plan/watches\", \"text\": \"x\"}, {\"page\": \"lords/naglur\", \"text\": \"x\"}, {\"page\": \"intelligence/Aragorn the King\", \"text\": \"Comes from the west.\"}]}"), 100, "Summer 5");
            Check(notes.Count == 4 && notes[0] == "plan/the-plan: written" && notes[1].Contains("refused") && notes[2].Contains("refused") && m.Pages.ContainsKey("intelligence/aragorn-the-king"),
                  "the agent writes its plan, notes and intelligence pages (names made plain), never the game's pages or another file's (" + string.Join("; ", notes) + ")");
            m.Snapshot(100, "Summer 5", 1, "answer");
            m.ApplyPages((JsonObject)JsonNode.Parse("{\"pages\": [{\"page\": \"plan/the-plan\", \"text\": \"Strike Lasgalor.\", \"why\": \"Arnor left\"}], \"lessons\": [\"Worth copying: the storm guard held.\"]}"), 120, "Summer 6");
            string hist = Path.Combine(m.HistoryDir, "plan", "the-plan");
            Check(m.Pages["plan/the-plan"].Text == "Strike Lasgalor." && Directory.Exists(hist) && Directory.GetFiles(hist).Length == 1 && File.ReadAllText(Directory.GetFiles(hist)[0]).Contains("Hold Moria.")
                  && m.Pages["notes/lessons"].Text.Contains("storm guard"), "a page rewritten keeps its old version in the history folder; lessons go to notes/lessons");
            Check(Shelf.History(m.Dir, "plan/the-plan", 0).Contains("1. ") && Shelf.History(m.Dir, "plan/the-plan", 1).Contains("Hold Moria."), "history lists a page's old versions and reads one whole");
            m.Snapshot(120, "Summer 6", 2, "answer");
            m.ApplyPages((JsonObject)JsonNode.Parse("{\"pages\": [{\"page\": \"intelligence/aragorn-the-king\", \"text\": \"\"}]}"), 130, "Summer 6");
            Check(!m.Pages.ContainsKey("intelligence/aragorn-the-king"), "an empty text takes a page away");
            m.Restore(new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("load1", 110) }, 110);
            Check(m.Pages.TryGetValue("plan/the-plan", out PageRec back) && back.Text == "Hold Moria." && m.Pages.ContainsKey("intelligence/aragorn-the-king"),
                  "a load puts back the pages of its save's history, as it does the notes");
        }
    }
}
