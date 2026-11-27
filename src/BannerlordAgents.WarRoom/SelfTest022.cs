using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.WarRoom
{
    internal static partial class SelfTest
    {
        /// <summary>
        /// War room 0.22.0 (Nov 26): the checks of the tooling audit of run 20261125-2341-C2, the war room's share, each on the made-up map or a scratch channel, with no real
        /// call (a stand-in for Claude Code answers where the order of calls matters). Numbered as the audit numbers its issues.
        /// </summary>
        private static void Fixes022Tests(string root)
        {
            string data = Path.Combine(root, "fix022-data");
            string oldRootEnv = Environment.GetEnvironmentVariable("BANNERLORD_AGENTS_ROOT");
            string oldProjects = PastTalk.ProjectsRoot;
            TimeSpan oldBriefing = Room.BriefingWait, oldFresh = Room.FreshWait;
            int oldReviewWait = Room.ReviewWaitMs;
            Environment.SetEnvironmentVariable("BANNERLORD_AGENTS_ROOT", data);
            Room.RootOverride = data;
            PastTalk.ProjectsRoot = Path.Combine(root, "fix022-projects");
            Room.BriefingWait = TimeSpan.FromMilliseconds(200);
            Room.FreshWait = TimeSpan.FromMilliseconds(200);
            Room.ReviewWaitMs = 100;
            Check(oldReviewWait == 3000, "item 22: a review waits at most 3 s for the last answer's receipts, not 15 (" + oldReviewWait + " ms)");
            try
            {
                BattleFixTests(root, data);
                MessageFixTests(root);
                ShelfFixTests(root);
                CallOrderTests(root, data);
            }
            finally
            {
                Environment.SetEnvironmentVariable("BANNERLORD_AGENTS_ROOT", oldRootEnv);
                Room.RootOverride = null;
                PastTalk.ProjectsRoot = oldProjects;
                Room.BriefingWait = oldBriefing;
                Room.FreshWait = oldFresh;
                Room.ReviewWaitMs = oldReviewWait;
                Room.GameRunningForTest = null;
                Claude.Fake = null;
            }
        }

        /// <summary>The made-up map (a river with one bridge) under a data folder, and a tool on a realm's view written there.</summary>
        private static RouteTool MapTool(string data, string campaign, string view, string book = null)
        {
            Routes.WalkMap m = Routes.Samples.SmallMap();
            string mapDir = Path.Combine(data, "map", "test");
            if (!File.Exists(Path.Combine(mapDir, "ground.bin")))
            {
                Directory.CreateDirectory(mapDir);
                File.WriteAllBytes(Path.Combine(mapDir, "ground.bin"), m.Ground);
                Routes.JsonText.Write(Path.Combine(mapDir, "places.json"), m.Places);
                Routes.JsonText.Write(Path.Combine(mapDir, "meta.json"), m.Meta);
                m.Dir = mapDir;
                Routes.JsonText.Write(Routes.Atlas.PathIn(mapDir), Routes.Atlas.Build(m));
            }
            Directory.CreateDirectory(Path.Combine(data, "live", campaign));
            File.WriteAllText(Path.Combine(data, "live", "current.json"), "{\"calendar\":{\"hours_in_day\":24,\"days_in_season\":21,\"seasons_in_year\":4,\"season_names\":[\"Spring\",\"Summer\",\"Autumn\",\"Winter\"]}}");
            File.WriteAllText(Path.Combine(data, "live", campaign, "view_orcs.json"), view);
            return new RouteTool(data, Path.Combine(data, "logs"), campaign, "orcs", 40, "wake") { BookPath = book };
        }

        private static string AskTool(RouteTool tool, string name, string args) =>
            (string)tool.Handle((JsonObject)JsonNode.Parse("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + args + "}}"))?["result"]?["content"]?[0]?["text"];

        // ---------- battle numbers: items 4, 5, 9, 18, 23, 28, 30 ----------

        private static void BattleFixTests(string root, string data)
        {
            string Own(string id, string lord, string name, double x, double y, int men, int str, string extra = "") =>
                "{\"id\":\"" + id + "\",\"lord\":\"" + lord + "\",\"name\":\"" + name + "\",\"x\":" + x + ",\"y\":" + y + ",\"men\":" + men + ",\"strength\":" + str + ",\"morale\":80,\"tactics\":100,\"pace\":4" + extra + "}";

            // item 5: a lord behind the river is timed by road (over the bridge, far to the south), so he falls in a later band than his straight line would put him
            string riverView = "{\"hours\":1000,\"map\":\"test\",\"own\":[" + Own("p_near", "lord_near", "Nearlord", 40, 3, 300, 400) + "," + Own("p_far", "lord_far", "Riverlord", 57, 3, 500, 700).Replace("\"pace\":4", "\"pace\":2") + "],"
                               + "\"seen\":[],\"holdings\":[{\"id\":\"castle_r\",\"name\":\"Rivermark\",\"kind\":\"castle\",\"garrison\":100,\"militia\":50,\"defence\":200,\"wall_level\":1,\"x\":44,\"y\":3}]}";
            RouteTool rt = MapTool(data, "c22", riverView);
            string mu = AskTool(rt, "muster", "{\"at\":\"castle_r\"}");
            Match far = Regex.Match(mu ?? "", @"Riverlord \[lord_far\]: 500 men, strength 700, about (\d+) hours");
            Check(mu != null && mu.Contains("by the way the game would march") && far.Success && int.Parse(far.Groups[1].Value) > 12 && mu.Contains("Gathered within 12 hours: 1 lords and armies, 300 men")
                  && !mu.Contains("in a straight line"),
                  "item 5: the muster times each lord by road at his pace: a lord across the river from the place, 13 away in a straight line (8 hours at his pace), is in a later band than 12 hours (" + (far.Success ? far.Groups[1].Value : "?") + " hours by road): " + mu?.Replace("\n", " | "));
            // war room 0.22.3 (Nov 27, run 20261127-1453-C2): a holding is reached at its gate, never its centre (which for 19 of the front's holdings lies on the
            // holding's own faces, where the copy of the game's way-finder finds no way: the muster at Goblin Town had 69 of 70 lords with "no way over land")
            float gx = 20, gy = 30, ux = 44, uy = 3;
            RouteTool.GateOf(data, "test", "town_a", ref gx, ref gy);
            RouteTool.GateOf(data, "test", "castle_r", ref ux, ref uy);
            Check(gx == 20 && gy == 29 && ux == 44 && uy == 3,
                  "0.22.3: a question about a holding aims at its gate (Aldburg's centre 20,30, gate " + gx + "," + gy + "); a place the map does not know keeps its point (" + ux + "," + uy + ")");
            string aq = Room.ArmyQuestion((JsonObject)JsonNode.Parse(riverView.Replace("\"seen\":[]", "\"seen\":[{\"id\":\"p_en\",\"lord\":\"lord_en\",\"name\":\"Gothmog\",\"relation\":\"enemy\",\"faction_name\":\"Mordor\",\"army_parties\":4,\"army_men\":2000,\"strength\":3000,\"x\":44,\"y\":3,\"doing\":\"besieging Rivermark\"}]")), data);
            Check(aq.Contains("You could gather where it stands (by road, at each one's pace): within 24 hours strength 400 (1 lords and armies)") && aq.Contains("within 72 hours 1,100 (2)"),
                  "item 5: the review's question times the gathering by road too: " + aq.Replace("\n", " | "));

            // item 4: a battle under way at Orc Keep: an enemy marching in is counted; a lord of yours in another fight is left out and named; a free lord joins only after the order delay
            string battle(string key, string kind, string place, string placeName, string ours, string usParties, string themParties, int usMen, int themMen, string measured = "") =>
                "{\"key\":\"" + key + "\",\"kind\":\"" + kind + "\",\"place\":" + (place == null ? "null" : "\"" + place + "\"") + ",\"place_name\":" + (placeName == null ? "null" : "\"" + placeName + "\"") + ",\"town\":false,\"x\":20,\"y\":29,"
                + "\"started\":990,\"hours_in\":10,\"ours\":\"" + ours + "\",\"walls_worth\":1,"
                + "\"us\":{\"healthy\":" + usMen + ",\"casualties\":100,\"power\":2,\"advantage\":1,\"morale\":80,\"led_by\":\"Orc Keep\",\"settlement_led\":true,\"parties\":[" + usParties + "]},"
                + "\"them\":{\"healthy\":" + themMen + ",\"casualties\":100,\"power\":2,\"advantage\":1.1,\"led_by\":\"Lorgath\",\"settlement_led\":false,\"parties\":[" + themParties + "]}" + measured + "}";
            string underView = "{\"hours\":1000,\"map\":\"test\",\"own\":[" + Own("p_bolg", "lord_bolg", "Bolg", 20, 29, 300, 600) + "," + Own("p_gor", "lord_gor", "Gorbag", 30, 20, 800, 1600) + ","
                               + Own("p_thal", "lord_thal", "Thalion", 40, 29, 900, 1800) + "],"
                               + "\"seen\":[{\"id\":\"p_lg\",\"lord\":\"lord_lg\",\"name\":\"Lorgath\",\"relation\":\"enemy\",\"faction_name\":\"Dol Guldur\",\"men\":1500,\"strength\":3000,\"x\":20.5,\"y\":29,\"doing\":\"fighting\",\"seen_hours\":1000},"
                               + "{\"id\":\"p_ul\",\"lord\":\"lord_ul\",\"name\":\"Ulfang\",\"relation\":\"enemy\",\"faction_name\":\"Dol Guldur\",\"men\":2500,\"strength\":5000,\"x\":15,\"y\":20,\"doing\":\"going to Orc Keep\",\"toward\":\"castle_k\",\"arrives_at\":1006,\"seen_hours\":1000}],"
                               + "\"holdings\":[{\"id\":\"castle_k\",\"name\":\"Orc Keep\",\"kind\":\"castle\",\"garrison\":100,\"militia\":50,\"defence\":200,\"wall_level\":2,\"x\":20,\"y\":29}],"
                               + "\"battles\":[" + battle("b1", "siege_outside", "castle_k", "Orc Keep", "defenders", "\"p_bolg\",\"castle_k\"", "\"p_lg\"", 450, 1500) + ","
                               + battle("b2", "field", null, null, "attackers", "\"p_gor\"", "\"p_looters\"", 800, 40).Replace("\"led_by\":\"Orc Keep\",\"settlement_led\":true", "\"led_by\":\"Gorbag\",\"settlement_led\":false") + "]}";
            RouteTool ut = MapTool(data, "c22", underView);
            ut.LagHours = () => 8;
            string uw = AskTool(ut, "battle_odds", "{\"ours\":[\"lord_bolg\",\"lord_gor\",\"lord_thal\"],\"theirs\":[\"lord_lg\"],\"at\":\"castle_k\"}");
            Check(uw != null && uw.StartsWith("BATTLE ODDS: a battle already under way at Orc Keep [castle_k]") && uw.Contains("- Without joiners: your side")
                  && uw.Contains("Joining their side as they arrive: Ulfang [lord_ul] (seen marching to it) in about 6 hours") && uw.Contains("Left out, since they cannot come: Gorbag is fighting another battle")
                  && uw.Contains("Thalion [lord_thal] (if ordered now: about 8 hours for your order to land, then the march)") && !uw.Contains("in a straight line"),
                  "item 4: a battle under way counts the enemy marching in, leaves out (and names) a lord of yours in another fight, and adds the order delay for a lord not yet ordered there: " + uw?.Replace("\n", " | "));
            Check(uw != null && uw.Contains("would turn them back on the way") && uw.Contains("Thalion"),
                  "item 4: it says when the enemy with its joiners would still outweigh a lord of yours as he arrives, so an order's default caution would turn him back: " + uw?.Replace("\n", " | "));

            // item 18: a storm question about an enemy castle, with a named lord in a skirmish elsewhere, answers the storm and names the other fight in a line
            string stormView = underView.Replace("\"holdings\":[", "\"frontier\":[{\"id\":\"castle_e\",\"name\":\"Edoras\",\"kind\":\"castle\",\"faction_name\":\"Dol Guldur\",\"garrison\":100,\"militia\":50,\"defence\":260,\"wall_level\":1,\"x\":40,\"y\":25}],\"holdings\":[");
            RouteTool st = MapTool(data, "c22", stormView);
            string so = AskTool(st, "battle_odds", "{\"ours\":[\"lord_gor\",\"lord_thal\"],\"at\":\"castle_e\"}");
            Check(so != null && so.StartsWith("BATTLE ODDS: Gorbag [lord_gor], Thalion [lord_thal] storming Edoras [castle_e]") && so.Contains("Gorbag is also fighting in a battle against Lorgath; ask about it by naming the enemy in theirs") && !so.Contains("already under way"),
                  "item 18: a storm question about a castle with nothing under way there answers that storm, the place in its first line, and names a lord's other fight: " + so?.Split('\n')[0] + " | " + so?.Replace("\n", " | "));

            // war room 0.22.2 (Nov 27, run 20261126-1838-C2, Dale): storming an enemy castle, an enemy lord standing near it (not marching there) joins its defenders as he
            // arrives, as the storm guard counts him; so does he in a battle under way at an enemy holding
            string nearLord = "{\"id\":\"p_gh\",\"lord\":\"lord_gh\",\"name\":\"Ghash\",\"relation\":\"enemy\",\"kind\":\"lord\",\"faction_name\":\"Dol Guldur\",\"men\":4000,\"strength\":8000,\"x\":43,\"y\":25,"
                              + "\"doing\":\"holding position\",\"seen_hours\":1000,\"march\":{\"base\":3.0}}";
            string inLord = "{\"id\":\"p_in\",\"lord\":\"lord_in\",\"name\":\"Inner\",\"relation\":\"enemy\",\"kind\":\"lord\",\"faction_name\":\"Dol Guldur\",\"men\":3000,\"strength\":6000,\"x\":40,\"y\":25,"
                            + "\"doing\":\"inside Edoras\",\"entrance\":\"castle_e\",\"seen_hours\":1000,\"march\":{\"base\":3.0}}";
            string noUlfang = Regex.Replace(stormView, ",\\{\"id\":\"p_ul\"[^}]*\\}", "");   // Ulfang (12 hours off) would come too: left out here so the near lord alone turns it
            string dView = noUlfang.Replace("\"seen\":[", "\"seen\":[" + nearLord + "," + inLord + ",");
            string ds = AskTool(MapTool(data, "c22", dView), "battle_odds", "{\"ours\":[\"lord_thal\"],\"at\":\"castle_e\"}");
            string dsAlone = AskTool(MapTool(data, "c22", noUlfang), "battle_odds", "{\"ours\":[\"lord_thal\"],\"at\":\"castle_e\"}");
            Check(ds != null && ds.Contains("joining the defenders as each arrives") && ds.Contains("Ghash [lord_gh] (4,000 men, near it") && !ds.Contains("Inner [lord_in]")
                  && ds.Contains("the defenders win") && dsAlone != null && dsAlone.Contains("the besiegers win"),
                  "0.22.2: storming an enemy castle, an enemy lord standing near it joins its defenders and turns the forecast (one inside is its defenders already): " + ds?.Replace("\n", " | ") + " || alone: " + dsAlone?.Replace("\n", " | "));
            string atEdoras = "{\"key\":\"b9\",\"kind\":\"siege_outside\",\"place\":\"castle_e\",\"place_name\":\"Edoras\",\"town\":false,\"x\":40,\"y\":25,\"started\":990,\"hours_in\":10,\"ours\":\"attackers\",\"walls_worth\":1,"
                              + "\"us\":{\"healthy\":900,\"casualties\":100,\"power\":2,\"advantage\":1,\"morale\":80,\"led_by\":\"Thalion\",\"settlement_led\":false,\"parties\":[\"p_thal\"]},"
                              + "\"them\":{\"healthy\":300,\"casualties\":100,\"power\":2,\"advantage\":1,\"led_by\":\"Edoras\",\"settlement_led\":true,\"parties\":[\"castle_e\"]}}";
            string du = AskTool(MapTool(data, "c22", dView.Replace("\"battles\":[", "\"battles\":[" + atEdoras + ",")), "battle_odds", "{\"ours\":[\"lord_thal\"],\"at\":\"castle_e\"}");
            Check(du != null && du.StartsWith("BATTLE ODDS: a battle already under way at Edoras [castle_e]") && du.Contains("Joining their side as they arrive: Ghash [lord_gh] (near it"),
                  "0.22.2: a battle under way at an enemy holding counts an enemy lord standing near it among its joiners: " + du?.Replace("\n", " | "));

            // item 23: a relief due after the storm ends is named apart and is not among the fallen
            string lateView = "{\"hours\":1000,\"map\":\"test\",\"own\":[" + Own("p_far", "lord_far", "Riverlord", 57, 3, 800, 1600) + "],"
                              + "\"seen\":[{\"id\":\"p_lg\",\"lord\":\"lord_lg\",\"name\":\"Lorgath\",\"relation\":\"enemy\",\"faction_name\":\"Dol Guldur\",\"army_men\":6000,\"men\":6000,\"strength\":12000,\"x\":21,\"y\":29,\"doing\":\"besieging Orc Keep\",\"seen_hours\":1000}],"
                              + "\"holdings\":[{\"id\":\"castle_k\",\"name\":\"Orc Keep\",\"kind\":\"castle\",\"garrison\":20,\"militia\":0,\"defence\":30,\"wall_level\":1,\"x\":20,\"y\":29}]}";
            RouteTool lt = MapTool(data, "c22", lateView);
            string lo = AskTool(lt, "battle_odds", "{\"ours\":[\"lord_far\"],\"theirs\":[\"lord_lg\"],\"at\":\"castle_k\"}");
            Check(lo != null && lo.Contains("Arriving after it ends, so not in these numbers: Riverlord [lord_far]") && lo.Contains("the besiegers win") && Regex.IsMatch(lo, @"all 20 of the defenders' men are down") && !lo.Contains("820"),
                  "item 23: a relief due after the storm ends is named apart, and the defenders' fallen count only those in the fight: " + lo?.Replace("\n", " | "));

            // item 28: a siege under way is reckoned from its own clock, as the place's page shows it
            string bookFile = Path.Combine(data, "book-c22.json");
            string sieges = "- Orc Keep [castle_k] (castle, Misty Mountain Orcs), besieged by Lorgath [lord_lg] (Dol Guldur) since Summer 9, 1087, 10:00 (72 hours): preparations done; built: 1 ram. "
                            + "Walls: 2 of 2 stretches standing. A storm could come from about Summer 12, 1087, 19:00; chance it comes within a day 40%, two days 70%, three 90% (besiegers 12,000 against defenders 30 times the walls' worth 2.0). Food lasts about 9 days.";
            File.WriteAllText(bookFile, new JsonObject
            {
                ["poke"] = 40, ["hours"] = 1000, ["date"] = "Summer 12, 1087, 16:00", ["realm"] = "orcs",
                ["lists"] = new JsonObject
                {
                    ["sieges"] = new JsonArray(new JsonObject { ["t"] = sieges, ["ids"] = new JsonArray("castle_k", "lord_lg") }),
                    ["lords"] = new JsonArray(new JsonObject { ["t"] = "- Alenon [lord_al]: army of 900 men in 3 parties, strength 1,800; at Dale; holding.\n  - Nár [lord_nar]: 200 men, strength 300\n  - Górli [lord_gorli]: 150 men", ["ids"] = new JsonArray("lord_al", "lord_nar", "lord_gorli") }),
                    ["captives"] = new JsonArray(new JsonObject { ["t"] = "- In Yonu [castle_A4] (castle of Gondor): 1 lord: Rashid [lord_rashid] since Summer 8, 1087, 08:00.", ["ids"] = new JsonArray("castle_A4", "lord_rashid") })
                },
                ["pages"] = new JsonObject { ["lord_dagnir"] = "Dagnir [lord_dagnir]: no party now; at Lasgalor." }
            }.ToJsonString());
            RouteTool ct = MapTool(data, "c22", lateView, bookFile);
            string co = AskTool(ct, "battle_odds", "{\"ours\":[\"lord_far\"],\"theirs\":[\"lord_lg\"],\"at\":\"castle_k\"}");
            Check(co != null && co.Contains("The siege's own clock, as your place's page shows it: preparations done; built: 1 ram. A storm could come from about Summer 12, 1087, 19:00") && !co.Contains("no storm can come before about"),
                  "item 28: a siege three days in gives the earliest storm its place page gives, not a camp just arrived: " + co?.Replace("\n", " | "));

            // item 30: a lord riding in an army (counted with it), a prisoner, a lord with no party
            string armyView = lateView.Replace("\"own\":[", "\"own\":[" + Own("p_al", "lord_al", "Alenon", 30, 29, 900, 1800, ",\"army_men\":900,\"army_strength\":1800") + ",");
            RouteTool at30 = MapTool(data, "c22", armyView, bookFile);
            string ao = AskTool(at30, "battle_odds", "{\"ours\":[\"lord_nar\",\"lord_rashid\",\"lord_dagnir\"],\"theirs\":[\"lord_lg\"]}");
            Check(ao != null && ao.Contains("(Nár [lord_nar] rides in Alenon's army: counted with it.)") && !ao.Contains("(- Nár") && ao.Contains("Rashid [lord_rashid] is a prisoner (In Yonu [castle_A4] (castle of Gondor)): left out")
                  && ao.Contains("Dagnir [lord_dagnir] has no party now: left out") && ao.StartsWith("BATTLE ODDS: Alenon [lord_al] against Lorgath [lord_lg]") && !ao.Contains("no lord or army of yours by that id"),
                  "item 30: a lord riding in an army is counted with it, a prisoner and a lord with no party are said so, never \"no lord of yours by that id\": " + ao?.Replace("\n", " | "));

            // item 9: the enemy realm's measured edge, after 5 battles of the realm's own against it: both figures in a forecast made before a battle
            string edgeFile = Path.Combine(data, "enemy-edge.json");
            if (File.Exists(edgeFile)) File.Delete(edgeFile);
            string EdgeView(int i) => "{\"hours\":" + (900 + i) + ",\"seen\":[{\"id\":\"p_lg\",\"lord\":\"lord_lg\",\"name\":\"Lorgath\",\"relation\":\"enemy\",\"faction_name\":\"Dol Guldur\",\"men\":1000,\"strength\":2000,\"x\":20,\"y\":29}],"
                                     + "\"battles\":[{\"key\":\"k" + i + "\",\"started\":" + (890 + i) + ",\"them\":{\"parties\":[\"p_lg\"]},\"measured\":{\"hours\":6,\"hit_us\":1,\"hit_them\":1.4}}]}";
            for (int i = 0; i < 4; i++) RouteTool.RecordEdge(edgeFile, (JsonObject)JsonNode.Parse(EdgeView(i)));
            string fieldView = "{\"hours\":1000,\"map\":\"test\",\"own\":[" + Own("p_bolg", "lord_bolg", "Bolg", 20, 29, 3000, 6000) + "],"
                               + "\"seen\":[{\"id\":\"p_lg\",\"lord\":\"lord_lg\",\"name\":\"Lorgath\",\"relation\":\"enemy\",\"faction_name\":\"Dol Guldur\",\"men\":1500,\"strength\":3000,\"x\":22,\"y\":29,\"seen_hours\":1000}]}";
            RouteTool et = MapTool(data, "c22", fieldView);
            et.EdgeFile = edgeFile;
            string e4 = AskTool(et, "battle_odds", "{\"ours\":[\"lord_bolg\"],\"theirs\":[\"lord_lg\"]}");
            RouteTool.RecordEdge(edgeFile, (JsonObject)JsonNode.Parse(EdgeView(4)));
            string e5 = AskTool(et, "battle_odds", "{\"ours\":[\"lord_bolg\"],\"theirs\":[\"lord_lg\"]}");
            Match em = Regex.Match(e5 ?? "", @"At the game's rules you lose about (\d+)% of your men; at what Dol Guldur's armies have shown in 5 of your battles \(their blows landing at about 140% [^)]*\), about (\d+)%");
            Check(e4 != null && !e4.Contains("have shown in") && em.Success && int.Parse(em.Groups[2].Value) > int.Parse(em.Groups[1].Value),
                  "item 9: after 5 battles against a realm, a forecast before a battle gives the losses at the rules and at that realm's measured edge (more); with 4, the rules alone: " + (em.Success ? em.Value : e5?.Replace("\n", " | ")));
        }

        // ---------- what a message opens with, and the lords: items 10, 13, 15, 17, 22, 31, 32, 36, 40, 41 ----------

        private static void MessageFixTests(string root)
        {
            string agent = Path.Combine(root, "fix022-agent"), channel = Path.Combine(agent, "warroom");
            try { Directory.Delete(agent, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(channel);
            var c = new Config { AgentDir = agent, Channel = "warroom", MemoryMode = "continuous", Context = "files" };
            var room = new Room(c, Path.Combine(root, "fix022-logs"));
            Memory mem = Memory.Open(c, "c22", _ => { });
            var talk = new Talk { Started = true, SessionId = "s22", LastReviewHours = 1000 };
            talk.Path = Path.Combine(mem.Dir, "conversation.json");
            room.UseForTest(mem, talk);

            // item 13: the big quiet news of every poke since the last message, oldest first: one seen only as it came (during a call), and the one answered
            string Meta(int n, params string[] since) => new JsonObject { ["number"] = n, ["hours"] = 1000 + n, ["date"] = "Summer 3", ["campaign"] = "c22", ["realm"] = "orcs", ["since_last"] = new JsonArray(since.Select(s => (JsonNode)s).ToArray()) }.ToJsonString();
            File.WriteAllText(Path.Combine(channel, "poke.json"), Meta(5, "Lasgalor was lost to Dol Guldur."));
            room.NotePoke(room.ReadMetaForTest(-1));
            var taken = Enumerable.Range(1, 18).Select(i => "Lord" + i + " was taken prisoner at the battle of Imnagath.").ToArray();
            File.WriteAllText(Path.Combine(channel, "poke.json"), Meta(6, taken.Prepend("Imnagath battle lost: 18 of your lords taken, 4,323 men lost; they are held in Imnagath.").ToArray()));
            string news = room.NewsOpening(room.ReadMetaForTest(-1), null);
            Check(news.StartsWith("Since your last answer:\n- Lasgalor was lost to Dol Guldur.\n- Imnagath battle lost: 18 of your lords taken") && taken.All(news.Contains) && room.NewsOpening(room.ReadMetaForTest(-1), null) == "",
                  "item 13: a message opens with the big quiet news of every poke since the last one, oldest first, the one that came during a call too, all 18 lords taken named; told once: " + news.Replace("\n", " | "));

            // item 15: the refusals in the receipts since the last message open the next one, with their full reasons; a change to a watch that ended says when
            string receipts = Path.Combine(channel, "receipts.log");
            File.WriteAllText(receipts, "20:10:00  poke #4, poke-4.json order 1 (lord_a garrison): REFUSED. old refusal.\n");
            var room2 = new Room(c, Path.Combine(root, "fix022-logs"));
            room2.UseForTest(mem, talk);
            room2.TakeRefusals();   // the war room's start: what came before is not told again
            room2.NoteWatchEnds("- (#7) Your watch \"las_trap\" ended: its end condition holds (fallen(las)).", "Summer 3, 1087, 14:00");
            File.AppendAllText(receipts, "20:16:18  poke #8, poke-8.json order 2 (lord_lek defend castle_s): REFUSED. Lek is called to Gárjan's army and will join it: release him from it or order Gárjan. Answered 40 s after poke #8 (6.0 in-game hours).\n"
                                         + "20:16:18  poke #8, poke-8.json order 3 (lord_b defend castle_s): APPLIED. Ok.\n"
                                         + "20:16:19  poke #8, poke-8.json watch 1 (las_trap): REFUSED. you have no watch of that name.\n");
            var answer = (JsonObject)JsonNode.Parse("{\"orders\":[],\"watch\":[{\"name\":\"baracharn\",\"names\":{\"b\":\"castle_b\"},\"wake_if\":[{\"if\":\"besieged(b)\",\"say\":\"x\"}]},{\"name\":\"good_one\",\"names\":{\"keep\":\"castle_b\"}}]}");
            List<string> bad = room2.CheckWatchNames(answer);
            string refusals = room2.NewsOpening(new PokeMeta { Number = 9 }, null);
            Check(refusals.StartsWith("Not carried out from your last answer:") && refusals.Contains("order (lord_lek defend castle_s): Lek is called to Gárjan's army and will join it: release him from it or order Gárjan.")
                  && refusals.Contains("watch (las_trap): you have no watch of that name (it ended Summer 3, 1087, 14:00: its end condition holds (fallen(las)))") && !refusals.Contains("old refusal") && !refusals.Contains("APPLIED")
                  && !refusals.Contains("Answered 40 s") && refusals.Contains("watch (baracharn): names: 'b' must be 2 to 32 small letters") && bad.Count == 1 && ((JsonArray)answer["watch"]).Count == 1,
                  "item 15: the next message opens with each refusal and its whole reason, a change to an ended watch says when it ended, and a watch with a one-letter name is caught before it reaches the game: " + refusals.Replace("\n", " | "));

            // item 17: three wakes during one call: the next message carries the reasons of the two that got no call (the third is the wake itself), oldest first
            string poke = "POKE #13, Summer 4\n\nWHY YOU ARE ASKED NOW\n- Imnagath is besieged.\n\nWHAT CHANGED SINCE YOUR LAST ANSWER (to poke #10)\n- (#11) Your watch \"las_trap\": Lasgalor fell.\n- (#12) Caras Laerolin: a relief of 2,000 is near.\nQuieter news since then, which did not call you:\n- A raid.\n";
            string skipped = room2.NewsOpening(new PokeMeta { Number = 13 }, poke);
            Check(skipped.Contains("Also since your last answer (wakes that came while you were answering another message):\n- (#11) Your watch \"las_trap\": Lasgalor fell.\n- (#12) Caras Laerolin: a relief of 2,000 is near.") && !skipped.Contains("A raid"),
                  "item 17: wakes that came during a call: the next message carries every one's reasons, oldest first: " + skipped.Replace("\n", " | "));

            // item 22: a review more than 12 hours overdue sits before a routine wake, never before a siege or a battle; the receipts wait is 3 s at most
            talk.NextReviewHours = double.NaN;
            talk.LastReviewHours = 1000;
            string routine = "POKE #20\n\nWHY YOU ARE ASKED NOW\n- Check-in on your watch \"east_road\".\n";
            string siege = "POKE #21\n\nWHY YOU ARE ASKED NOW\n- Imnagath is besieged by Skulgar.\n";
            Check(room2.ReviewBeforeWake(routine, 1037) && !room2.ReviewBeforeWake(routine, 1035) && !room2.ReviewBeforeWake(siege, 1040) && Room.RoutineWake("- A whole in-game day with no wake.") && !Room.RoutineWake("- The battle of Imnagath was lost.")
                  && new Config().ReviewEveryHours == 24,
                  "item 22: with five wakes queued and the review 13 hours overdue, the review sits first, before a routine wake only (not a siege's); 11 hours overdue it waits");

            // item 10: a review right after an answer that ordered 23 lords lists none of them; its header gives the files' hour and age
            string lordsDir = Path.Combine(channel, Shelf.DirName, "lords");
            Directory.CreateDirectory(lordsDir);
            var ids = Enumerable.Range(1, 23).Select(i => "lord_x" + i).ToList();
            File.WriteAllText(Path.Combine(lordsDir, "no-order.md"), "# Lords with no order\n\n" + string.Join("\n", ids.Select((id, i) => "- Lord " + i + " [" + id + "] (lord-" + i + "): 100 men, strength 150; at Dale")) + "\n- Gárjan [lord_garjan] (garjan): army of 900 men in 4 parties, strength 1,500; at Sirannon\n");
            File.SetLastWriteTimeUtc(Path.Combine(lordsDir, "no-order.md"), DateTime.UtcNow.AddMinutes(-1));
            room2.NoteOrderedForTest(new JsonObject { ["orders"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject { ["lord"] = id, ["do"] = "defend" }).ToArray()) }, 1040);
            string digest = room2.ReviewDigest(1041);
            Check(ids.All(id => !digest.Contains("[" + id + "]: ")) && digest.Contains("- Gárjan [lord_garjan]: 900 men") && digest.Contains("23 more you ordered after these files were built, or in the last 12 in-game hours, are left out")
                  && digest.StartsWith("REVIEW DIGEST, from your files as built from the game's hour of"),
                  "item 10: a review right after an answer that ordered 23 lords lists none of them (only the one still unordered), and says why: " + digest.Replace("\n", " | "));

            // item 31: a lord left to the game's AI until an hour is not asked about before it; one leading an army, until it breaks up; the review lists them on one line
            room2.TakeLeaves((JsonObject)JsonNode.Parse("{\"orders\":[],\"leave_to_ai\":[{\"lord\":\"lord_garjan\",\"until\":\"army ends\"},{\"lord\":\"lord_frar\",\"until\":48}]}"), 1041, "Summer 4, 1087, 17:00");
            var garjan = new Room.Unordered { Id = "lord_garjan", Name = "Gárjan", Line = "army of 900 men in 4 parties, strength 1,500; at Sirannon" };
            var frar = new Room.Unordered { Id = "lord_frar", Name = "Frar", Line = "271 men, strength 556" };
            string digest2 = room2.ReviewDigest(1042);
            Check(room2.IsLeft(garjan, 1100) && !room2.IsLeft(new Room.Unordered { Id = "lord_garjan", Name = "Gárjan", Line = "300 men; at Sirannon" }, 1100) && room2.IsLeft(frar, 1088) && !room2.IsLeft(frar, 1090)
                  && !digest2.Contains("- Gárjan [lord_garjan]: 900 men") && digest2.Contains("Left to the game's AI by your choice, not asked about until then: Gárjan [lord_garjan] until his army breaks up"),
                  "item 31: a lord left to the game's AI is not asked about until his hour (or, leading an army, until it breaks up), and the review names such lords on one line: " + digest2.Replace("\n", " | "));
            room2.TakeLeaves((JsonObject)JsonNode.Parse("{\"orders\":[{\"lord\":\"lord_frar\",\"do\":\"defend\"}]}"), 1043, "");
            Check(!room2.IsLeft(frar, 1050) && Room.LordsQuestion(41, new List<Room.Unordered> { frar }, "x").Contains("\"leave_to_ai\""), "item 31: an order for a lord ends his mark, and the lords question says how to leave one");

            // item 32: the realm's count of lords without an order adds up to the list
            string sum = Shelf.InSumCounts("- In the field: 30 lords leading parties, 9,000 men, strength 20,000; 2 armies (10 parties). Holding your orders: 21; the other 9 are led by the game's own AI.", 8, 1);
            Check(sum.Contains("Holding your orders: 21. Lords with no order of yours: 9. 8 march in your armies or to join one; 1 is led by the game's AI (lords/no-order lists them).") && !sum.Contains("the other 9"),
                  "item 32: the count of lords with no order says how many march in the realm's armies and how many the game's AI leads, adding up to the list: " + sum);

            // item 36: while the game's run-finished mark is there, no call of any kind
            int calls = 0;
            Claude.Fake = (p, s) => { calls++; return new CallResult { Ok = true, Text = "{\"poke\": 1, \"orders\": []}", Raw = "{\"type\":\"assistant\"}" }; };
            File.WriteAllText(Path.Combine(channel, Room.RunFinishedFile), "{\"run\":\"r1\",\"at\":\"2026-11-26T09:08:00\"}");
            bool reviewed = room2.Review("a test");
            Check(room2.RunFinished() && !reviewed && calls == 0 && !room2.Handover("a test", null), "item 36: while the run-finished mark is there the war room makes no call: no review, no handover");
            File.Delete(Path.Combine(channel, Room.RunFinishedFile));
            Check(!room2.RunFinished(), "item 36: once the mark is gone (a run armed or begun) calls may be made again");

            // item 40: a call whose poke carries no run is tagged with the run the game has open
            string runs = Path.Combine(Room.RootOverride, "runs", "20261125-2341-C2");
            Directory.CreateDirectory(runs);
            File.WriteAllText(Path.Combine(Room.RootOverride, "runs", "current.txt"), "20261125-2341-C2");
            File.WriteAllText(Path.Combine(runs, "run.json"), "{\"id\":\"20261125-2341-C2\",\"arm\":\"C2\",\"status\":\"running\",\"campaign\":\"c22\"}");
            mem.Run = null;
            string tag = room2.RunTag(new PokeMeta { Campaign = "c22" }, out string arm);
            string other = room2.RunTag(new PokeMeta { Campaign = "another" }, out _);
            Check(tag == "20261125-2341-C2" && arm == "C2" && other == null && room2.RunTag(new PokeMeta { Run = "R9", Arm = "A" }, out string a2) == "R9" && a2 == "A",
                  "item 40: a call whose poke carries no run (the run's first look, the chronicler) is tagged with the run the game has open for this campaign");

            // item 41 and item 14: the lords question's log line names only the lords left without an order; the busy mark is there while the call runs, gone after
            bool busySeen = false;
            string busyKind = null;
            Claude.Fake = (p, s) =>
            {
                string f = Directory.Exists(Room.BusyDir) ? Directory.GetFiles(Room.BusyDir, "*.json").FirstOrDefault() : null;
                if (f != null) { busySeen = true; busyKind = (string)JsonNode.Parse(File.ReadAllText(f))?["kind"]; }
                return new CallResult { Ok = true, Text = "{\"poke\": 30, \"orders\": [{\"lord\": \"lord_x1\", \"do\": \"defend\", \"at\": \"castle_s\"}]}", Raw = "{\"type\":\"assistant\"}" };
            };
            File.WriteAllText(Path.Combine(channel, "poke.json"), Meta(30));
            room2.AskAboutLordsForTest(30, new List<Room.Unordered> { new Room.Unordered { Id = "lord_x1", Name = "Lord 1" }, new Room.Unordered { Id = "lord_x2", Name = "Lord 2" } }, room2.ReadMetaForTest(-1));
            string log = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "fix022-logs"), "warroom-*.log").First());
            string lordsLine = log.Split('\n').Last(l => l.Contains("Lords with no order put to the agent"));
            Check(lordsLine.Contains("it ordered 1, the rest stay with the game's own AI: Lord 2 [lord_x2].") && !lordsLine.Contains("Lord 1 [lord_x1]"),
                  "item 41: the lords question's log line lists only the lords left without an order: " + lordsLine.Trim());
            Check(busySeen && busyKind == "lords" && Directory.GetFiles(Room.BusyDir, "*.json").Length == 0,
                  "item 14: the busy mark (pid, since, kind) is there while a call runs and gone when it ends (" + busyKind + ")");
            Claude.Fake = null;
        }

        // ---------- the files: items 12, 19, 29, 34, 35, 39 ----------

        private static void ShelfFixTests(string root)
        {
            string channel = Path.Combine(root, "fix022-shelf");
            try { Directory.Delete(channel, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(channel);
            JsonObject book = (JsonObject)JsonNode.Parse(@"{
              ""poke"": 7, ""hours"": 2200, ""date"": ""Autumn 2, 1087, 00:00"", ""realm"": ""orcs"", ""realm_name"": ""Misty Mountain Orcs"",
              ""lists"": {
                ""lords"": [
                  {""t"": ""- Calathiel [lord_cal]: 300 men, strength 450; at Dale; holding (your order: defend)."", ""ids"": [""lord_cal""]},
                  {""t"": ""- Náth [lord_nath]: army of 900 men in 3 parties, strength 1,800; at Dale; holding (your order: hold).\n  - Bolg [lord_bolg]: 300 men, strength 500\n  - Ufthak [lord_uf]: 300 men"", ""ids"": [""lord_nath"", ""lord_bolg"", ""lord_uf""]},
                  {""t"": ""- House of the Dragon-slayer's Party [p_house_party_1]: 40 men, strength 60; at Dale; holding."", ""ids"": [""p_house_party_1""]},
                  {""t"": ""- Frar [lord_frar]: 271 men, strength 556; 7.9 from Angloss; going to Angloss to sell its loot, then on to its patrol of the passes, which it has held since the start of the season, a long line."", ""ids"": [""lord_frar""]}
                ],
                ""orders"": [{""t"": ""- Calathiel [lord_cal]: defend Dale [town_dale] with lord_tali_party_1 and lord_gone_party_1. Now holding."", ""ids"": [""lord_cal""]}, {""t"": ""- Náth [lord_nath]: hold. Now holding."", ""ids"": [""lord_nath""]}],
                ""captives"": [{""t"": ""- In Yonu [castle_A4] (castle of Gondor): 1 lord: Táli [lord_tali] since Autumn 1."", ""ids"": [""castle_A4"", ""lord_tali""]}],
                ""realm"": [{""t"": ""- In the field: 4 lords leading parties, 1,500 men, strength 2,800; 1 army (3 parties). Holding your orders: 2; the other 3 are led by the game's own AI."", ""ids"": []}],
                ""enemies"": [], ""holdings"": [], ""purse"": [], ""allies"": [], ""diplomacy"": [], ""votes"": []
              },
              ""pages"": {}
            }");
            var view = (JsonObject)JsonNode.Parse("{\"load_id\":\"load1\",\"hours\":2200,\"own\":[{\"id\":\"p_house_party_1\",\"lord\":null,\"name\":\"House of the Dragon-slayer's Party\"},{\"id\":\"lord_cal_party_1\",\"lord\":\"lord_cal\",\"name\":\"Calathiel\"}],\"holdings\":[]}");
            var shelf = new Shelf(channel);
            // the names the realm knew of two partners from earlier builds: Táli (now taken), and one whose party is gone
            var earlier = (JsonObject)book.DeepClone();
            ((JsonArray)earlier["lists"]["lords"]).Add(JsonNode.Parse("{\"t\": \"- Táli [lord_tali]: 100 men; at Dale.\", \"ids\": [\"lord_tali\"]}"));
            ((JsonArray)earlier["lists"]["lords"]).Add(JsonNode.Parse("{\"t\": \"- Malthorn [lord_gone]: 80 men; at Dale.\", \"ids\": [\"lord_gone\"]}"));
            ((JsonArray)earlier["lists"]["captives"]).Clear();
            ShelfInput In(JsonObject b, string tools = null, List<string> receipts = null) => new ShelfInput
            {
                Book = b, View = view, Poke = 7, Campaign = "c", LoadId = "load1", RealmName = "Misty Mountain Orcs", Date = (string)b["date"], Hours = (double)b["hours"],
                ToolsText = tools ?? "ORDERS YOU CAN GIVE\n\nTools:\n- **camp_strike** (at: a castle): strikes a siege camp.\n- **goto** (at: any holding): goes.\n", Receipts = receipts ?? new List<string>()
            };
            shelf.Build(In(earlier));
            shelf.Build(In(book));
            string dir = Path.Combine(channel, Shelf.DirName);
            string cal = File.ReadAllText(Path.Combine(dir, "lords", "calathiel.md"));
            string noOrder = File.ReadAllText(Path.Combine(dir, "lords", "no-order.md"));
            Check(cal.Contains("with Táli (prisoner) and Malthorn (no party now)") && !cal.Contains("lord_tali_party_1") && !noOrder.Contains("Dragon-slayer") && noOrder.Contains("- Frar [lord_frar]")
                  && noOrder.Contains("which it has held since the start of the season, a long line.") && File.ReadAllText(Path.Combine(dir, "lords", "index.md")).Contains("no lord at its head: it cannot be ordered"),
                  "item 29 (and 31's whole reason line): a partner taken is named \"Táli (prisoner)\", one gone \"(no party now)\", never the party's code; a party with no lord is no lord to order: " + cal.Replace("\n", " | "));
            string inSum = File.ReadAllText(Path.Combine(dir, "realm", "in-sum.md"));
            Check(shelf.InArmies == 2 && shelf.NoOrderCount == 1 && inSum.Contains("Lords with no order of yours: 3. 2 march in your armies or to join one; 1 is led by the game's AI"),
                  "item 32: the realm's sum counts the lords in its armies apart from those the game's AI leads, adding up to the no-order list (" + shelf.InArmies + ", " + shelf.NoOrderCount + ")");

            // item 35: an order's page is named as the order is, found under either spelling; the list never shown half-built
            shelf.Build(In(book, tools: ""));
            Check(File.Exists(Path.Combine(dir, "orders", "camp_strike.md")) && shelf.Open(new[] { "orders/camp-strike" }).Contains("== files/orders/camp_strike.md") && shelf.Open(new[] { "camp_strike" }).Contains("== files/orders/camp_strike.md")
                  && File.ReadAllText(Path.Combine(dir, "orders", "index.md")).Contains("2 orders"),
                  "item 35: an order's page bears the order's own name and is found under either spelling, and a library read half-written leaves the last whole list");

            // item 19: the order-results page: receipts on it as soon as the game takes the answer, each answer's quieter news cut, the newest ten on it, the older on the earlier page
            shelf.Build(In(book, receipts: new List<string> { "poke #8, poke-8.json order 1 (lord_cal defend town_dale): APPLIED. Calathiel defends Dale." }));
            string results = File.ReadAllText(Path.Combine(dir, "lords", "order-results.md"));
            string entry = Shelf.ResultEntry("- (#9) Your watch fired.\nWhat became of your orders since then:\n- poke #8, poke-8.json order 1 (lord_cal defend town_dale): APPLIED. Calathiel defends Dale.\n- poke #8, poke-8.json order 2 (lord_x hold): REFUSED. gone.\nQuieter news since then, which did not call you:\n"
                                             + string.Concat(Enumerable.Range(1, 200).Select(i => "- A raider band " + i + " was seen near the passes, far from anything of yours.\n")),
                                             new List<string> { "poke #8, poke-8.json order 1 (lord_cal defend town_dale): APPLIED. Calathiel defends Dale." });
            var many = Enumerable.Range(1, 25).Select(i => "As of day " + i + ": " + new string('x', 3000)).ToList();
            (List<string> shown, List<string> rest) = Shelf.SplitResults(many);
            Check(results.Contains("the game's receipts of your answers\n- poke #8, poke-8.json order 1 (lord_cal defend town_dale): APPLIED.") && entry.Contains("REFUSED. gone.") && !entry.Contains("APPLIED. Calathiel")
                  && entry.Contains("(more quieter news: chronicle/record has it all)") && entry.Length < 2500 && shown.Count == 10 && rest.Count > 0 && shown.Sum(s => s.Length) < Shelf.PageMostChars,
                  "item 19: the receipts reach the order-results page as soon as the game takes the answer, an answer's entry keeps its receipts whole (no repeats) and at most 1,500 characters of quieter news, and the page keeps the newest 10 (" + entry.Length + " characters)");
            shelf.Pages["lords/huge"] = "# Huge\n" + string.Concat(Enumerable.Repeat("- a line of a very big page, far too big for one answer.\n", 3700));
            string opened = shelf.Open(new[] { "lords/huge", "lords/index", "realm/in-sum" });
            Check(Regex.Matches(opened, "== files/").Count == 3 && opened.Contains("(cut here: this page holds") && opened.Contains("== files/realm/in-sum.md") && opened.Length < Shelf.OpenMostChars + 20000 && shelf.Pages["lords/huge"].Length > 200000,
                  "item 19: a 200,000-character page opened with two others returns all three, the big one cut with a note (" + opened.Length + " characters)");

            // item 39: the pages an open names are pages read, listed with the call
            string raw = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"mcp__routes__open\",\"input\":{\"pages\":[\"lords/index\",\"orders/camp-strike\"]}},{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{\"file_path\":\""
                         + Path.Combine(dir, "realm", "in-sum.md").Replace("\\", "\\\\") + "\"}}]}}\n";
            var reads = shelf.TrackReads(raw);
            var r1 = new CallResult { Reads = reads };
            r1.Add(new CallResult { Reads = (1, 1, 0, new List<string> { "plan/the-plan" }) });
            Check(reads.reads == 3 && reads.opens == 1 && reads.steps == 1 && reads.pages.Contains("orders/camp_strike") && reads.pages.Contains("lords/index") && r1.Reads.reads == 4 && r1.Reads.pages.Count == 4,
                  "item 39: every page an open names counts as read and is listed with its call, and a retry's reads join its call's: " + string.Join(", ", reads.pages));

            // item 34: a look-up of a lord who only marches on another lord's line does not return that lord's page
            string bookFile = Path.Combine(channel, "book.json");
            File.WriteAllText(bookFile, new JsonObject
            {
                ["poke"] = 7, ["hours"] = 2200, ["date"] = "Autumn 2",
                ["lists"] = new JsonObject { ["lords"] = new JsonArray(new JsonObject { ["t"] = "- Dáin II Ironfoot [lord_dain]: army of 675 men in 2 parties; at Erebor.\n  - Aragorn [player_party]: 128 men", ["ids"] = new JsonArray("lord_dain", "player_party") }) },
                ["pages"] = new JsonObject { ["lord_dain"] = "Dáin II Ironfoot [lord_dain]: 675 men." }
            }.ToJsonString());
            var lu = new RouteTool(root, Path.Combine(root, "fix022-logs"), "c", "orcs", 7, "wake") { BookOnly = true, BookPath = bookFile };
            string look = AskTool(lu, "look_up", "{\"id\":\"player_party\"}"), lookDain = AskTool(lu, "look_up", "{\"id\":\"lord_dain\"}");
            Check(look != null && !look.StartsWith("Dáin II Ironfoot [lord_dain]: 675 men.") && look.Contains("No page of its own for \"player_party\"") && lookDain.StartsWith("Dáin II Ironfoot [lord_dain]: 675 men."),
                  "item 34: a look-up of a lord named on another lord's line (two lords on one line) shows the lines that name him, not the other lord's page: " + look?.Replace("\n", " | "));

            // item 35: the order tester takes an order's name, ours or kept of the realm's own, under either spelling
            Directory.CreateDirectory(Path.Combine(channel, "tools"));
            File.WriteAllText(Path.Combine(channel, "tools", "camp_strike.json"), "{\"name\": \"camp_strike\", \"about\": \"patrols around its castle\", \"takes\": {\"at\": \"own_fort: the castle\"}, \"settings\": {\"radius\": [12, 3, 30, \"how far\"]},"
                                                                          + " \"start\": \"a\", \"states\": {\"a\": {\"do\": \"patrol at\", \"when\": [{\"if\": \"hours_in_state >= 24\", \"go\": \"b\"}]}, \"b\": {\"end\": \"free\"}}}");
            var ck = new RouteTool(root, Path.Combine(root, "fix022-logs"), "c", "orcs", 7, "wake") { ChannelDir = channel };
            string ours = ck.CheckTool((JsonObject)JsonNode.Parse("{\"name\": \"goto\"}")), mine = ck.CheckTool((JsonObject)JsonNode.Parse("{\"name\": \"camp-strike\"}")),
                   none = ck.CheckTool((JsonObject)JsonNode.Parse("{\"name\": \"no_such\"}")), byText = ck.CheckTool((JsonObject)JsonNode.Parse("{\"definition\": \"camp_strike\"}")),
                   noStates = ck.CheckTool((JsonObject)JsonNode.Parse("{\"definition\": {\"name\": \"camp_strike\", \"start\": \"a\"}}"));
            Check(ours.StartsWith("THE CHECKER: goto passes") && mine.StartsWith("THE CHECKER: camp_strike passes") && byText.StartsWith("THE CHECKER: camp_strike passes") && none.StartsWith("You have no order called \"no_such\"")
                  && noStates.Contains("give its name in name"),
                  "item 35: the order tester checks an order of ours or one kept of the realm's own by its name (either spelling), and a definition with no states says what was expected: " + ours.Split('\n')[0] + " | " + mine.Split('\n')[0] + " | " + noStates.Replace("\n", " | "));

            // item 12: a snapshot request given up on is withdrawn: taken back while the game has not picked it up, else marked for the game to skip
            string orders = Path.Combine(channel, "orders");
            Directory.CreateDirectory(orders);
            string req = Path.Combine(orders, "book-request-1.json");
            File.WriteAllText(req, "{}");
            bool marked1 = Room.Withdraw(req, "book-request-1");
            bool marked2 = Room.Withdraw(Path.Combine(orders, "book-request-2.json"), "book-request-2");
            Check(!marked1 && !File.Exists(req) && !File.Exists(Path.Combine(orders, "book-request-1.withdrawn")) && marked2 && File.Exists(Path.Combine(orders, "book-request-2.withdrawn")),
                  "item 12: a snapshot request given up on is taken back while the game has not picked it up, and marked withdrawn (book-request-<id>.withdrawn) once it has");
        }

        // ---------- the order of calls: items 6, 12 (same hour), 13 at a wake ----------

        private static void CallOrderTests(string root, string data)
        {
            string agent = Path.Combine(root, "fix022-calls"), channel = Path.Combine(agent, "warroom");
            try { Directory.Delete(agent, true); } catch (DirectoryNotFoundException) { }
            Directory.CreateDirectory(Path.Combine(channel, "book"));
            File.WriteAllText(Path.Combine(agent, "warroom-system.md"), "# War council\n\n## How you are asked\n\nWakes.\n\n## Rules\n\nOnly your lords.\n");
            var c = new Config { AgentDir = agent, Channel = "warroom", MemoryMode = "continuous", Context = "files", EveryLordOrdered = false, CompactHardTokens = 100_000, CompactAtTokens = 80_000 };
            var room = new Room(c, Path.Combine(root, "fix022-calls-logs"));
            Memory mem = Memory.Open(c, "c6", _ => { });
            mem.LoadId = "L1";
            mem.SessionId = "s-old";
            mem.SessionStarted = true;
            mem.Save();
            var talk = Talk.Load(mem.Dir);
            talk.SessionId = "s-old";
            talk.Started = true;
            talk.Tokens = 120_000;
            talk.Context = "files";
            talk.LastReviewHours = 1000;
            talk.Save();
            room.UseForTest(mem, talk);
            string projects = Path.Combine(PastTalk.ProjectsRoot, "p");
            Directory.CreateDirectory(projects);
            File.WriteAllText(Path.Combine(projects, "s-old.jsonl"), "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"x\"},\"uuid\":\"u1\"}\n");
            File.WriteAllText(Path.Combine(channel, "poke.json"), "{\"number\":7,\"hours\":1001,\"date\":\"Summer 1, 1087, 17:00\",\"campaign\":\"c6\",\"realm\":\"orcs\",\"load_id\":\"L1\",\"loaded_at\":990,\"since_last\":[\"Táli was taken prisoner near Dale.\"]}");
            string pokeText = "POKE #7, Summer 1, 1087, 17:00\nAnswer.\n\nWHY YOU ARE ASKED NOW\n- Imnagath is besieged by Skulgar.\n";
            File.WriteAllText(Path.Combine(channel, "poke.txt"), pokeText);
            File.WriteAllText(Path.Combine(channel, "pokes.log"), "#7 | Summer 1, 1087, 17:00 | Imnagath is besieged by Skulgar.\n");
            File.WriteAllText(Path.Combine(channel, "book", "poke-7.json"), "{\"poke\":7,\"hours\":1001,\"date\":\"Summer 1, 1087, 17:00\",\"realm\":\"orcs\",\"lists\":{},\"pages\":{}}");
            File.WriteAllText(Path.Combine(channel, "council.txt"), "COUNCIL BRIEFING FOR ORCS, Summer 1, 1087, 17:00\n");
            File.WriteAllText(Path.Combine(channel, "report.txt"), "SITUATION\n");
            var prompts = new List<string>();
            Claude.Fake = (p, s) =>
            {
                prompts.Add(p);
                if (p.Contains("WRITE YOUR HANDOVER NOW"))
                    return new CallResult { Ok = true, Raw = "{\"type\":\"assistant\"}", LastContext = 130_000, Text = "{\"pages\": [], \"handover\": \"HANDOVER, Orcs, Summer 1 (" + new string('w', 200) + ")\"}" };
                return new CallResult { Ok = true, Raw = "{\"type\":\"assistant\"}", LastContext = prompts.Count == 1 ? 125_000 : 20_000, Text = "{\"poke\": 7, \"orders\": []}" };
            };
            room.LastAnsweredForTest = 6;
            room.Wake(7, "Summer 1, 1087, 17:00", pokeText);
            Check(prompts.Count == 3 && prompts[0].Contains("WAKE #7") && !prompts[0].Contains("A NEW CONVERSATION") && prompts[0].StartsWith("Since your last answer:\n- Táli was taken prisoner near Dale.")
                  && prompts[1].Contains("WRITE YOUR HANDOVER NOW") && prompts[2].StartsWith("A NEW CONVERSATION BEGINS") && prompts[2].Contains("STRATEGY REVIEW")
                  && room.TalkNow.Started && room.TalkNow.SessionId != "s-old",
                  "item 6: a wake past the ceiling is answered first, in the old conversation (its big news first, item 13), then the handover, then the new conversation begins at once with a review, not at the next wake: "
                  + string.Join(" || ", prompts.Select(p => p.Split('\n')[0])));
            // a quiet moment past compact_at_tokens, no wake waiting and the game running: the handover starts there
            prompts.Clear();
            room.TalkNow.Tokens = 90_000;
            room.TalkNow.Save();
            Room.GameRunningForTest = true;
            room.HandoverIfDueForTest();
            Room.GameRunningForTest = null;
            Check(prompts.Count >= 1 && prompts[0].Contains("WRITE YOUR HANDOVER NOW") && prompts[0].Contains("quiet moment"),
                  "item 6: once past compact_at_tokens, a quiet moment with no wake waiting starts the handover: " + string.Join(" || ", prompts.Select(p => p.Split('\n')[0])));
            Claude.Fake = null;
            // item 12: a snapshot of the game's current in-game hour serves a question without asking again
            File.WriteAllText(Path.Combine(channel, "clock.txt"), "1001.5\nSummer 1, 1087, 17:30\n");
            File.WriteAllText(Path.Combine(channel, "book", "now.json"), "{\"poke\":-1,\"hours\":1001,\"date\":\"Summer 1, 1087, 17:00\"}");
            string same = room.SnapshotOfThisHour(Path.Combine(channel, "book"));
            File.WriteAllText(Path.Combine(channel, "book", "now.json"), "{\"poke\":-1,\"hours\":999,\"date\":\"Summer 1, 1087, 15:00\"}");
            string older = room.SnapshotOfThisHour(Path.Combine(channel, "book"));
            Check(same != null && older == null, "item 12: a page opened within the same in-game hour as the last snapshot uses it without asking the game again; an older one does not serve");
        }
    }
}
