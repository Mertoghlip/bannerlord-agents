using System.Text.Json.Nodes;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// Continuous agents (0.18), with no calls: the continuous instructions laid over the agent's own (renamed and new sections in place), a wake's message less what
        /// the conversation holds, a review's report less the briefing's sections, the conversation's state kept and given back, a reload at the same hour carried on and an
        /// earlier one begun again, when a review is due, the handover read from an answer, the agent's own tools told when they change, and the settings.
        /// </summary>
        private static void ContinuousTests(string root)
        {
            // the instructions: a section replaced, one renamed in place, new ones right after the one before them, one removed
            string mine = "# War council\n\n## Who you are\n\nA captain.\n\n## The council of war\n\nCouncils.\n\n## How you are asked\n\nPokes.\n\n## Your memory\n\nThe story.\n\n## Checking a tool\n\nCheck.\n\n## Rules\n\nOnly your lords.\n";
            string over = "<!-- note -->\n\n## The council of war => Strategy reviews\n\nReviews.\n\n## Watches\n\nWatch.\n\n## How you are asked\n\nWakes.\n\n## Checking a tool\n\n(removed)\n\n## Your handover\n\nHandover.\n";
            string laid = Room.Overlay(mine, over);
            int Pos(string h) => laid.IndexOf("\n" + h + "\n", StringComparison.Ordinal);
            Check(!laid.Contains("## The council of war") && Pos("## Strategy reviews") > Pos("## Who you are") && Pos("## Watches") > Pos("## Strategy reviews") && Pos("## Watches") < Pos("## How you are asked"),
                  "a renamed section takes the old one's place, and a new one follows the one before it");
            Check(laid.Contains("## How you are asked\n\nWakes.") && !laid.Contains("Pokes.") && !laid.Contains("## Checking a tool") && laid.TrimEnd().EndsWith("## Your handover\n\nHandover.", StringComparison.Ordinal) == false
                  && Pos("## Your handover") > Pos("## How you are asked") && laid.Contains("## Rules\n\nOnly your lords."), "a section is replaced, one removed, and one placed after its forerunner: " + laid.Replace("\n", "|"));

            // the template installed for a continuous agent reads: every section of it lands
            string tpl = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "agent", "warroom-continuous.template.md");
            string sys = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "agent", "warroom-system.template.md");
            if (File.Exists(tpl) && File.Exists(sys))
            {
                string full = Room.Overlay(File.ReadAllText(sys).Replace("\r", ""), File.ReadAllText(tpl));
                string[] want = { "## Strategy reviews", "## Watches", "## How you are asked", "## Where your attention goes", "## Your memory", "## Your handover", "## How you answer",
                                  "## Following your own orders", "## Learning from what happens", "## Look-ups", "## Allies", "## The human player", "## Time", "## Rules" };
                var missing = want.Where(h => full.IndexOf("\n" + h + "\n", StringComparison.Ordinal) < 0).ToList();
                Check(missing.Count == 0 && !full.Contains("## The council of war") && !full.Contains("Each poke is a fresh conversation") && full.Contains("Orders you just gave, and the sections of the map that are critical for your wars that you are watching"),
                      "the continuous instructions lay over the realm's own: every section lands, the council and the fresh-call memory are gone" + (missing.Count > 0 ? ": missing " + string.Join(", ", missing) : ""));
                string shape = full.Substring(full.IndexOf("## Your handover", StringComparison.Ordinal));
                shape = shape.Substring(0, shape.IndexOf("\n## ", 5, StringComparison.Ordinal));
                string[] parts = { "1. HOW WE GOT HERE", "2. WHERE THE WAR STANDS", "3. MY PLAN AND WHY", "4. ORDERS IN FORCE", "5. WATCHES IN FORCE", "6. THE HUMAN PLAYER", "7. LESSONS AND WHAT IS WORTH COPYING", "8. ALLIES AND LETTERS", "9. WHAT I WAS ABOUT TO DO" };
                Check(parts.All(shape.Contains) && parts.Select(x => shape.IndexOf(x, StringComparison.Ordinal)).SequenceEqual(parts.Select(x => shape.IndexOf(x, StringComparison.Ordinal)).OrderBy(i => i))
                      && shape.Contains("ON TRIAL") && !shape.Contains("STILL TESTING") && shape.Contains("game's own list"), "the handover's nine parts in order, the orders in force among them with the trials marked");
                Check(full.IndexOf("## Strategy reviews", StringComparison.Ordinal) < full.IndexOf("## How you are asked", StringComparison.Ordinal)
                      && full.IndexOf("## Your handover", StringComparison.Ordinal) > full.IndexOf("## Your memory", StringComparison.Ordinal)
                      && full.IndexOf("## Your handover", StringComparison.Ordinal) < full.IndexOf("## How you answer", StringComparison.Ordinal), "and each new section sits beside the one it belongs with");
            }

            // a wake's message less what the conversation holds
            string dir = Path.Combine(root, "continuous");
            Directory.CreateDirectory(Path.Combine(dir, "warroom"));
            var c = new Config { AgentDir = dir, Channel = "warroom", MemoryMode = "continuous" };
            var room = new Room(c, Path.Combine(dir, "logs"));
            Memory mem = Memory.Open(c, "cc", _ => { });
            Talk talk = Talk.Load(mem.Dir);
            room.UseForTest(mem, talk);
            string rule = "A castle or town holds when its defenders' strength (garrison, militia and the lords inside) times what its walls are worth is at least its besiegers' strength: the game's own test for storming.";
            string Poke(string why, string holding, string purse, string regions = "Around Eaworth: 7 lords.") =>
                "POKE #10, Autumn 3, 1087, 10:00\r\nAnswer with one orders file in orders\\ that says \"poke\": 10.\r\n\r\n"
                + "REPORT FOR MISTY MOUNTAIN ORCS: THE NEWS AND WHAT BEARS ON IT\r\nRuler: Burzum.\r\n\r\n"
                + "WHY YOU ARE ASKED NOW\r\n- " + why + "\r\n\r\n"
                + "YOUR WATCHES (1 of 12; read every in-game hour; a condition wakes you when it turns true)\r\n- keep_road: wakes you if distance(thalion, keep) < 15 [true now].\r\n\r\n"
                + "THE CLOCK\r\n- Orc Keep [castle_two]: a day's march.\r\n\r\n"
                + "WHAT CHANGED SINCE YOUR LAST ANSWER (to poke #9)\r\n- No other pokes came.\r\n\r\n"
                + "HOLDINGS HERE (1 of your 3)\r\n" + rule + "\r\n- " + holding + "\r\n\r\n"
                + "YOUR PURSE\r\n- " + purse + "\r\n\r\n"
                + "RECENT HISTORY HERE, LAST TWO DAYS (from your chronicle; 2 of 2 lines)\r\n- Autumn 3 · Shazga goes at Looters.\r\n\r\n"
                + "THE REST OF THE WAR (2 regions)\r\n- " + regions + "\r\n\r\n"
                + "WHAT YOU CAN ASK (each question costs a few seconds while the game runs)\r\n- look_up: one id in full.\r\n";
            string w1 = room.Slim(Poke("Your watch \"keep_road\": Thalion is within 15 of Orc Keep.", "Orc Keep [castle_two]: garrison 200.", "Clans' gold 100,000."), 1000, false);
            Check(w1.StartsWith("POKE #10", StringComparison.Ordinal) && w1.Contains("WHY YOU ARE ASKED NOW\n- Your watch") && w1.Contains("YOUR WATCHES (1 of 12") && w1.Contains(rule) && w1.Contains("Clans' gold 100,000.")
                  && w1.Contains("Around Eaworth") && w1.Contains("look_up: one id in full.") && !w1.Contains("Shazga goes at Looters"),
                  "a conversation's first wake shows every section whole but the chronicle's last two days, which it holds already");
            string w2 = room.Slim(Poke("Check-in on your watch \"keep_road\".", "Orc Keep [castle_two]: garrison 200.", "Clans' gold 99,000."), 1002, false);
            Check(w2.Contains("WHY YOU ARE ASKED NOW\n- Check-in") && w2.Contains("YOUR WATCHES (1 of 12") && w2.Contains("THE CLOCK\n- Orc Keep") && w2.Contains("HOLDINGS HERE (1 of your 3): as in your last message that showed it.")
                  && w2.Contains("WHAT YOU CAN ASK (each question costs a few seconds while the game runs): as in your last message that showed it.") && w2.Contains("YOUR PURSE: not repeated (shown 2 hours ago")
                  && w2.Contains("THE REST OF THE WAR (2 regions): not repeated") && !w2.Contains("99,000"),
                  "the next wake: why, the watches and the clock whole; a section as it was, in a line; the purse and the regions not before their hours: " + w2.Replace("\n", "|"));
            string w3 = room.Slim(Poke("A siege.", "Orc Keep [castle_two]: garrison 150.", "Clans' gold 98,000.", "Around Eaworth: 6 lords."), 1013, false);
            Check(w3.Contains("- Orc Keep [castle_two]: garrison 150.") && !w3.Contains(rule) && w3.Contains("Around Eaworth: 6 lords.") && w3.Contains("YOUR PURSE: not repeated"),
                  "a section that changed is shown again without the rule paragraph it opened with; the regions come again after 12 hours, the purse after 24: " + w3.Replace("\n", "|"));
            string w4 = room.Slim(Poke("A siege.", "Orc Keep [castle_two]: garrison 150.", "Clans' gold 97,000."), 1013, true);
            Check(w4.Contains("Shazga goes at Looters") && w4.Contains("97,000"), "a new conversation's first message shows everything");

            // a review's report: less the briefing's sections and the chronicle
            string report = "REPORT FOR MISTY MOUNTAIN ORCS\r\nRuler: Burzum.\r\n\r\nYOUR PURSE\r\n- gold\r\n\r\nYOUR LORDS AND ARMIES (51, 9,619 men)\r\n- Naglur: army.\r\n\r\nYOUR ENEMY, THE HUMAN PLAYER\r\n- Aragorn.\r\n\r\n"
                            + "RECENT HISTORY, LAST TWO DAYS (from your chronicle)\r\n- old.\r\n\r\nDIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT\r\n- war with Gondor.\r\n";
            string r1 = room.Trim(report, 1020, review: true, opening: false);
            Check(r1.Contains("YOUR LORDS AND ARMIES (51, 9,619 men)\n- Naglur") && r1.Contains("DIPLOMACY") && !r1.Contains("YOUR PURSE") && !r1.Contains("Aragorn") && !r1.Contains("- old."),
                  "a review's situation report leaves out the purse, the human player and the chronicle, which the briefing and the conversation hold");
            Check(room.Trim(report, 1040, review: true, opening: false).Contains("DIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT: as in your last message that showed it."), "and says so in a line for a section as it was");

            // headings
            Check(Room.IsHeading("YOUR LORDS WHO CAN BE THERE IN TIME (14 of your 51, 2,775 men; soonest first)") && Room.IsHeading("DIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT") && !Room.IsHeading("- ENEMY Marton [castle_V1]")
                  && !Room.IsHeading("Quieter news since then, which did not call you:") && !Room.IsHeading("POKE #10, Autumn 3"), "a section heading is its words in capitals before any bracket");
            Check(Room.Key("YOUR ORDERS IN FORCE HERE (18 of 50)") == Room.Key("YOUR ORDERS IN FORCE HERE (17 of 50)") && Room.Key("REPORT FOR MISTY MOUNTAIN ORCS: THE NEWS") == "REPORT FOR MISTY MOUNTAIN ORCS: THE NEWS",
                  "a section is the same one whatever its counts");

            // the conversation's state, kept and given back
            talk.SessionId = "s1";
            talk.Started = true;
            talk.Tokens = 123456;
            talk.WakesByDay["41"] = 3;
            talk.Save();
            Talk back = Talk.Load(mem.Dir);
            Check(back.SessionId == "s1" && back.Started && back.Tokens == 123456 && back.WakesByDay["41"] == 3 && back.Sent.Count > 0 && back.Static.Count > 0 && double.IsNaN(back.NextReviewHours),
                  "the conversation's state is written and read back");
            object[] mark = talk.Mark();
            talk.Sent["NEW"] = "x";
            talk.Restore(mark);
            Check(!talk.Sent.ContainsKey("NEW"), "what a message marked as shown is given back when it never reached the conversation");

            // when a review is due (0.18.2): only at the hour the agent set, however soon after the last
            mem.SessionId = "s1";
            talk.LastReviewHours = 1000;
            talk.NextReviewHours = 1001;
            Check(room.ReviewDue(1000.5) == null && room.ReviewDue(1001) == "the hour you set for it has come", "a review sits at the hour the agent set, even an hour after the last");
            talk.NextReviewHours = double.NaN;
            Check(room.ReviewDue(1024) == null && room.ReviewDue(1500) == null, "with no hour set, none sits: no daily review of its own");
            Check(room.Review_In("{\"review_in\": 0.2}", 2000, true) == 2001 && room.Review_In("{\"review_in\": 500}", 2000, true) == 2240 && double.IsNaN(room.Review_In("{\"orders\": []}", 2000, true))
                  && room.Review_In("{\"council_in\": 6}", 2000, false) == 2006, "review_in is kept between 1 and 240 hours; a review that sets none leaves none set; a wake's answer may set it too");
            talk.LastReviewHours = 2000;
            room.WantReview("Orc Keep is besieged by Thalion");
            Check(room.ReviewDue(2000.5) == "Orc Keep is besieged by Thalion", "a big event calls a review at once, even right after the last");
            room.Review_In("{\"review_in\": 12}", 2001, true);
            Check(room.ReviewDue(2002) == null, "and the review that sits clears it");

            // a question answered from the newest hour (0.18.1): the call asks the game before each question, and answers from what it wrote
            string oldBook = Path.Combine(dir, "warroom", "book", "poke-5.json"), newBook = Path.Combine(dir, "warroom", "book", "now.json");
            Directory.CreateDirectory(Path.GetDirectoryName(oldBook));
            string Book(double hours, string date, int men) => new JsonObject
            {
                ["poke"] = 5, ["hours"] = hours, ["date"] = date, ["realm"] = "orcs", ["lists"] = new JsonObject(),
                ["pages"] = new JsonObject { ["lord_bolg"] = "Bolg [lord_bolg]: " + men + " men, strength 420. As of " + date + "." }
            }.ToJsonString();
            File.WriteAllText(oldBook, Book(1000, "Summer 3, 1085, 10:00", 300));
            File.WriteAllText(newBook, Book(1007, "Summer 3, 1085, 17:00", 250));
            string freshPath = newBook;
            var asking = new RouteTool(root, Path.Combine(dir, "logs"), "cc", "orcs", 5, "wake") { BookOnly = true, BookPath = oldBook, ChronicleDir = Path.Combine(dir, "warroom"), Notes = () => "", Refresh = () => freshPath };
            string AskBook(string id) => (string)asking.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"look_up\",\"arguments\":{\"id\":\"" + id + "\"}}}"))?["result"]?["content"]?[0]?["text"];
            Check(AskBook("lord_bolg").Contains("250 men") && AskBook("lord_bolg").Contains("Summer 3, 1085, 17:00"), "a question is answered from the book the game wrote afresh, not the one the call began with");
            freshPath = null;
            File.WriteAllText(newBook, Book(1009, "Summer 3, 1085, 19:00", 200));
            Check(AskBook("lord_bolg").Contains("250 men"), "when the game does not answer, the last book read answers");
            string listed = string.Join("\n", (asking.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"))?["result"]?["tools"] as JsonArray ?? new JsonArray())
                                                  .Select(t => (string)t?["description"] ?? ""));
            Check(listed.Contains("as of your realm's newest hour when you ask") && !listed.Contains("as of this report"), "and the questions say their answers are of the newest hour");

            // a reload: at the hour the conversation reached it carries on; an earlier save begins again
            mem.LoadId = "L1";
            mem.LastHours = 1030;
            mem.Save();
            talk.Save();
            string note = room.EnsureContinuous(new PokeMeta { Campaign = "cc", LoadId = "L2", LoadedAt = 1030.3, LoadedAtDate = "Autumn 3, 1087, 22:00" });
            Check(note != null && note.Contains("THE GAME WAS LOADED AGAIN") && room.TalkNow.Started && room.TalkNow.SessionId == "s1", "a load at the hour the conversation reached carries it on, and says so");
            mem.Snapshot(1000, "Autumn 2", 9, "handover");
            string none = room.EnsureContinuous(new PokeMeta { Campaign = "cc", LoadId = "L3", LoadedAt = 1005, LoadedAtDate = "Autumn 2, 1087, 05:00", Before = new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>("L2", 1005) } });
            Check(none == null && !room.TalkNow.Started && room.TalkNow.SessionId == null && (room.TalkNow.NextWhy ?? "").Contains("earlier than your last conversation reached"),
                  "an earlier save ends the conversation, and the next begins from what the agent knew then: " + room.TalkNow.NextWhy);

            // the handover read from an answer
            string h = "HANDOVER, Misty Mountain Orcs, Autumn 12 (the game was closed)\n\n1. HOW WE GOT HERE\n" + string.Join(" ", Enumerable.Repeat("We held the passes.", 40));
            Check(Room.HandoverText("Here it is:\n```\n" + h + "\n```") == h && Room.HandoverText("HANDOVER: too short.") == null && Room.HandoverText("{\"orders\": []}") == null,
                  "a handover is read from the word HANDOVER on, fences dropped; too short or missing, it is not one");

            // the game's own list for the handover's parts 4 and 5 (0.18.1): the newer of the last wake's report and the last situation report
            string ch = Path.Combine(dir, "warroom");
            File.WriteAllText(Path.Combine(ch, "report.txt"), "REPORT FOR MISTY MOUNTAIN ORCS\r\nRuler: Burzum.\r\n\r\nYOUR ORDERS IN FORCE\r\n- Naglur [lord_MM8_1]: army_guard Gor-mir.\r\n- Ulga [lord_MM2_8]: garrison Sirannon.\r\n\r\n"
                                                              + "YOUR PURSE\r\n- gold\r\n\r\nLORDS WITH NO ORDER (1 of your 3; the game's own AI leads each until you order him)\r\n- Hrokmaw [lord_MM11_4]: 309 men.\r\n");
            File.WriteAllText(Path.Combine(ch, "council.txt"), "COUNCIL BRIEFING FOR MISTY MOUNTAIN ORCS, Autumn 12, 1087, 14:00\r\nThe whole realm.\r\n\r\nEVERY LORD (3)\r\n- Naglur.\r\n\r\n"
                                                               + "YOUR WATCHES (1 of 12; read every in-game hour)\r\n- north_push: wakes you if distance(elves, pass) < 25.\r\n");
            File.SetLastWriteTimeUtc(Path.Combine(ch, "report.txt"), DateTime.UtcNow);
            File.WriteAllText(Path.Combine(ch, "poke.txt"), "POKE #900, Autumn 11, 1087, 09:00\r\nAnswer.\r\n\r\nYOUR ORDERS IN FORCE HERE (1 of 2)\r\n- Naglur [lord_MM8_1]: army_guard Gor-mir.\r\n- Elsewhere (1; list orders for them in full): Ulga [lord_MM2_8]: garrison Sirannon [castle_MM7].\r\n");
            File.SetLastWriteTimeUtc(Path.Combine(ch, "poke.txt"), DateTime.UtcNow.AddMinutes(-30));
            string facts = room.HandoverFacts(false);
            Check(facts != null && facts.StartsWith("THE GAME'S OWN LIST FOR YOUR HANDOVER (as of Autumn 12, 1087, 14:00, the last the game wrote before it closed)", StringComparison.Ordinal)
                  && facts.Contains("YOUR ORDERS IN FORCE\n- Naglur") && facts.Contains("- Ulga [lord_MM2_8]: garrison Sirannon.") && facts.Contains("LORDS WITH NO ORDER (1 of your 3") && facts.Contains("- Hrokmaw")
                  && facts.Contains("YOUR WATCHES (1 of 12") && !facts.Contains("YOUR PURSE") && !facts.Contains("EVERY LORD"),
                  "the handover's list: every order, the lords with none and the watches, from the situation report and its briefing when they are the newest, dated by the briefing: " + (facts ?? "none").Replace("\n", "|"));
            File.SetLastWriteTimeUtc(Path.Combine(ch, "poke.txt"), DateTime.UtcNow.AddMinutes(5));
            string fromPoke = room.HandoverFacts(false);
            Check(fromPoke != null && fromPoke.Contains("(as of Autumn 11, 1087, 09:00,") && fromPoke.Contains("YOUR ORDERS IN FORCE HERE (1 of 2)\n- Naglur") && fromPoke.Contains("Elsewhere (1; list orders for them in full): Ulga"),
                  "or from the last wake's report when it is newer, whose orders section names every other order in a line: " + (fromPoke ?? "none").Replace("\n", "|"));
            File.Delete(Path.Combine(ch, "report.txt"));
            File.WriteAllText(Path.Combine(ch, "poke.txt"), "POKE #901, Autumn 11, 1087, 10:00\r\nAnswer.\r\n\r\nWHY YOU ARE ASKED NOW\r\n- x\r\n");
            Check(room.HandoverFacts(false) == null, "with no orders section anywhere, the handover is asked for without a list");

            // its own tools, as the library lists them
            Check(Room.OwnToolNames("ORDERS\n- **defend** (ours)\nYOUR OWN TOOLS (your realm's library)\n- **ally_join** (yours, v1) (at: ...)\n- **circuit** (yours, v3): recruits\n").SequenceEqual(new[] { "ally_join v1", "circuit v3" }),
                  "the agent's own tools are read from the library with their versions");

            // the settings
            var cs = new Config { AgentDir = Path.Combine(root, "continuous-settings"), MemoryMode = "continuous", CompactAtTokens = 400_000, WakeBudget = 10 };
            cs.Save();
            Config cr = Config.Load(cs.AgentDir);
            Check(cr.Continuous && cr.CompactAtTokens == 400_000 && cr.CompactAtCloseTokens == 500_000 && cr.CompactHardTokens == 850_000 && cr.WakeBudget == 10 && cr.ContinuousModel == "claude-opus-5-5[1m]"
                  && cr.ContinuousThinkingCap == 6000, "a continuous agent's settings are written and read back");
            Check(Claude.RunawayLimit("HANDOVER, Misty Mountain Orcs") == Claude.RunawayCharsWithTools && Claude.RunawayLimit("{\"poke\": 3") == Claude.RunawayChars, "a handover may run longer than an answer");
            Check(Room.ContinuousEnv["CLAUDE_CODE_PROMPT_CACHE_TTL"] == "1h" && Room.ContinuousEnv["DISABLE_AUTO_COMPACT"] == "1", "every call of a conversation asks for the one-hour cache and no compaction of Claude Code's own");
        }
    }
}
