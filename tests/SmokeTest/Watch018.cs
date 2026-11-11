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
        /// The agent's own watches and self-wakes (0.18, continuous agents): a watch read and checked, refused with its problems, a condition that wakes when it turns true
        /// (and only then), a check-in, an end at its hour or its end condition, a lord's orders followed, every other line riding the next wake, YOUR WATCHES in the poke,
        /// and the watches carried across a save.
        /// </summary>
        private static void WatchTests(string root)
        {
            PartyRow Own(string id, string name, float x, float y, int men, float strength) => new PartyRow { Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = men, Strength = strength, Speed = 4.5f, FoodDays = 20, X = x, Y = y, Doing = "Hold", Spottable = true };
            PartyRow Foe(string id, string name, float x, float y, int men, float strength) => new PartyRow { Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = men, Strength = strength, Speed = 4f, FoodDays = 20, X = x, Y = y, Doing = "Hold", Spottable = true };
            PartyRow naglur = Own("naglur", "Naglur", 120, 100, 2000, 3400);
            PartyRow thalion = Foe("thalion", "Thalion", 160, 100, 2500, 4400);

            // read and checked on its own
            var list = new List<Watch>();
            Snapshot s0 = ToolWorld(50000, naglur, thalion);
            JObject good = JObject.Parse(@"{""name"": ""north_push"", ""why"": ""Naglur strikes west while Thalion is held"", ""lord"": ""lord_naglur"",
                ""names"": {""thalion"": ""lord_thalion"", ""keep"": ""castle_two""},
                ""wake_if"": [{""if"": ""distance(thalion, keep) < 15"", ""say"": ""Thalion is within {round(distance(thalion, keep))} of Orc Keep""},
                              {""if"": ""strength < 2400"", ""say"": ""Naglur's army fell to {strength}""}],
                ""follow"": [""lord_naglur""], ""check_every"": 6, ""until"": 48}");
            Watches.Taken t = Watches.Take(list, good, 7, s0, null, "orcs");
            Check(t.Status == "kept" && list.Count == 1 && list[0].Party == "p_naglur" && list[0].Names.Any(kv => kv.Key == "thalion" && kv.Value == "p_thalion") && list[0].FollowParties.Contains("p_naglur"),
                  "a watch is read: its lord's party is its \"me\", its names read the parties and holdings they name, and its followed lord's party is known: " + t.Reason);
            Check(Math.Abs(list[0].Until - 50048) < 1e-6 && list[0].CheckEvery == 6, "its end is its hours from now, and its check-in its own");
            Check(t.Reason.Contains("wakes you if distance(thalion, keep) < 15") && t.Reason.Contains("checks in every 6 hours") && t.Reason.Contains("follows the orders of lord_naglur"), "its receipt says what wakes it: " + t.Reason);

            // refused, with each problem
            Watches.Taken bad = Watches.Take(list, JObject.Parse(@"{""name"": ""Bad Name""}"), 7, s0, null, "orcs");
            Check(bad.Status == "refused" && bad.Reason.Contains("small letters"), "a watch's name is checked: " + bad.Reason);
            bad = Watches.Take(list, JObject.Parse(@"{""name"": ""typo"", ""names"": {""ghost"": ""lord_nobody""}, ""wake_if"": [{""if"": ""distanse(ghost, castle_orc) < 3""}, {""if"": ""strength < 5""}]}"), 7, s0, null, "orcs");
            Check(bad.Status == "refused" && bad.Reason.Contains("lord_nobody is no lord") && bad.Reason.Contains("there is no function distanse") && bad.Reason.Contains("name the lord"),
                  "unknown ids, functions and a lord's own values with no lord are refused, each said: " + bad.Reason);
            bad = Watches.Take(list, JObject.Parse(@"{""name"": ""idle"", ""until"": 12}"), 7, s0, null, "orcs");
            Check(bad.Status == "refused" && bad.Reason.Contains("never wake you"), "a watch that could never wake the agent is refused");
            bad = Watches.Take(list, JObject.Parse(@"{""name"": ""theirs"", ""lord"": ""lord_thalion"", ""check_every"": 6}"), 7, s0, null, "orcs");
            Check(bad.Status == "refused" && bad.Reason.Contains("not a lord of yours"), "a watch's lord is a lord of the realm's own");

            // a condition already true waits until it has turned false
            Watches.Taken already = Watches.Take(list, JObject.Parse(@"{""name"": ""strong"", ""lord"": ""lord_naglur"", ""wake_if"": [{""if"": ""strength > 3000"", ""say"": ""strong""}]}"), 7, s0, null, "orcs");
            Check(already.Status == "kept" && already.Reason.Contains("Already true now"), "a condition already true when given is said, and does not wake at once: " + already.Reason);
            Check(Watches.Hour(list, ToolWorld(50001, naglur, thalion)).All(x => !x.Text.Contains("\"strong\"")), "it does not wake while it stays true");
            Check(Watches.Take(list, JObject.Parse(@"{""name"": ""strong"", ""end"": true}"), 8, s0, null, "orcs").Status == "ended" && list.Count == 1, "a watch is ended by name");

            // it wakes the hour a condition turns true, once
            thalion.X = 150;
            List<Trigger> h1 = Watches.Hour(list, ToolWorld(50002, naglur, thalion));
            Check(h1.Count == 1 && h1[0].Kind == "watch" && h1[0].Text.Contains("Thalion is within 10 of Orc Keep") && h1[0].Place == "castle_two" && h1[0].Party == "p_naglur",
                  "a condition that turns true wakes the agent with its words, filled in, about its lord and its holding: " + string.Join(" | ", h1.Select(x => x.Text)));
            Check(Watches.Hour(list, ToolWorld(50003, naglur, thalion)).Count == 0, "and not again while it stays true");
            thalion.X = 170;
            Watches.Hour(list, ToolWorld(50004, naglur, thalion));
            thalion.X = 150;
            Check(Watches.Hour(list, ToolWorld(50005, naglur, thalion)).Count(x => x.Kind == "watch") == 1, "once it has been false, it can wake again");

            // a check-in, and a wake starts its clock again
            thalion.X = 170;
            Watches.Woke(list, 50005);
            Check(Watches.Hour(list, ToolWorld(50010, naglur, thalion)).Count == 0, "no check-in before its hours have passed since the last wake");
            List<Trigger> ci = Watches.Hour(list, ToolWorld(50011, naglur, thalion));
            Check(ci.Count == 1 && ci[0].Kind == "check_in" && ci[0].Text.Contains("every 6 hours"), "a check-in comes due its hours after the last wake: " + string.Join(" | ", ci.Select(x => x.Text)));

            // its end: its hour, or its end condition
            List<Trigger> end = Watches.Hour(list, ToolWorld(50048, naglur, thalion));
            Check(end.Count == 1 && end[0].Kind == "watch_ended" && list.Count == 0, "a watch ends at its hour, and the next wake says so");
            Watches.Taken plain = Watches.Take(list, JObject.Parse(@"{""name"": ""raw_id"", ""check_every"": 24, ""end_if"": ""distance(lord_thalion, castle_orc) > 100""}"), 9, ToolWorld(50050, naglur, thalion), null, "orcs");
            Check(plain.Status == "refused" && plain.Reason.Contains("'castle_orc' is not one of its names"), "an id written into a condition is refused: ids go in its names: " + plain.Reason);
            Watches.Taken named = Watches.Take(list, JObject.Parse(@"{""name"": ""gone"", ""names"": {""thalion"": ""lord_thalion"", ""home"": ""castle_orc""}, ""check_every"": 24, ""end_if"": ""distance(thalion, home) > 100""}"), 9, ToolWorld(50050, naglur, thalion), null, "orcs");
            Check(named.Status == "kept", "a watch with no conditions, only a check-in and an end, is kept: " + named.Reason);
            thalion.X = 300;
            List<Trigger> gone = Watches.Hour(list, ToolWorld(50051, naglur, thalion));
            Check(gone.Count == 1 && gone[0].Kind == "watch_ended" && gone[0].Text.Contains("end condition") && list.Count == 0, "or once its end condition holds");

            // self-wakes: what wakes, and what rides the next wake
            string Self(string kind, string text, string party = null, ISet<string> followed = null, bool urgent = false)
            {
                var tr = new Trigger { Kind = kind, Text = text, Party = party, Facts = urgent ? new LineFacts { Urgent = true } : null };
                WakeRules.Sort(tr);
                WakeRules.SortSelf(tr, followed);
                return tr.Lane;
            }
            var follows = new HashSet<string> { "p_naglur" };
            Check(Self("besieged", "Orc Keep is besieged by Thalion.") == WakeRules.Now && Self("captured", "Bolg was taken prisoner.") == WakeRules.Now && Self("vote_closing", "The vote closes.") == WakeRules.Now
                  && Self("letter", "A letter.", urgent: true) == WakeRules.Now && Self("first_look", "First look.") == WakeRules.Now, "the locked wakes still wake a self-waking agent");
            Check(Self("war", "Gondor declared war on Misty Mountain Orcs.") == WakeRules.Now && Self("offer", "Gondor offers peace.") == WakeRules.Now && Self("watch", "Your watch.") == WakeRules.Now && Self("check_in", "Check-in.") == WakeRules.Now,
                  "and so do war, peace, offers, its watches and their check-ins");
            Check(Self("raided", "Orc Village is being raided.") == WakeRules.Digest && Self("enemy_in_view", "Enemy army in view: X.") == WakeRules.Digest && Self("letter", "A letter.") == WakeRules.Digest
                  && Self("tool", "Naglur gave up the chase of Thalion.", "p_other", follows) == WakeRules.Digest, "every other line rides the next wake, an enemy army in view and a raid too");
            Check(Self("tool", "Naglur gave up the chase of Thalion.", "p_naglur", follows) == WakeRules.Now && Self("order_ended", "The order \"Naglur engage\" ended.", "p_naglur", follows) == WakeRules.Now,
                  "the order news of a lord a watch follows wakes it");

            // in play: a realm that sets its own wakes
            string dir = Path.Combine(root, "self_orcs");
            var cfg = AgentConfig.FromSettings("orcs", dir, "live", "all");
            cfg.Wake = "self";
            cfg.News = "theatre";
            Brain.Reset("smoketest-self", double.NaN, new List<AgentConfig> { cfg }, null);
            Check(cfg.WakeRule && cfg.WakeSelf && AgentConfig.WakeMode(" Self ") == "self" && AgentConfig.WakeMode("all") == "all" && AgentConfig.WakeMode("x") == "rule", "agent.txt's wake = self is read");
            PartyRow bolg = Own("bolg", "Bolg", 104, 100, 300, 500);
            thalion.X = 170;
            Snapshot W(double h, params PartyRow[] more) => ToolWorld(h, new[] { bolg, naglur }.Concat(more).ToArray());
            string Poke() { Files.FlushAll(); return ReadIf(Path.Combine(dir, "poke.txt")); }
            Channel ch = AgentChannel.For("orcs");
            void Answer(int n, string extra = "")
            {
                string orders = Path.Combine(dir, "orders");
                Directory.CreateDirectory(orders);
                File.WriteAllText(Path.Combine(orders, "poke-" + n + ".json"), "{\"poke\": " + n + ", \"orders\": []" + extra + "}");
                System.Threading.Thread.Sleep(350);
                AgentChannel.Scan();
            }
            double h0 = 91000;
            int before = Brain.PokesWatched;
            Brain.OnHour(W(h0, thalion));
            Check(Brain.PokesWatched == before + 1 && Poke().Contains("YOUR WATCHES (0 of 12") && Poke().Contains("None. Give one with \"watch\""), "a self-waking agent's first look says it has no watches, and how to give one");
            Answer(ch.LastPoke, ", \"watch\": [{\"name\": \"keep_road\", \"why\": \"Thalion may march on Orc Keep\", \"names\": {\"thalion\": \"lord_thalion\", \"keep\": \"castle_two\"}, \"wake_if\": [{\"if\": \"distance(thalion, keep) < 15\", \"say\": \"Thalion is within 15 of Orc Keep\"}], \"follow\": [\"lord_naglur\"], \"until\": 40}]");
            Files.FlushAll();
            Check(Agents.Of("orcs").Watches.Count == 1 && ReadIf(Path.Combine(dir, "receipts.log")).Contains("watch 1 (keep_road): KEPT"), "an answer's watch is kept, and its receipt written");
            before = Brain.PokesWatched;
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h0 + 1, Kind = "raided", Text = "Orc Village is being raided by Elendara (Kingdom of Arnor and Gondor).", Place = "village_orc", Factions = { "orcs" } });
            Brain.OnHour(W(h0 + 1, thalion));
            Check(Brain.PokesWatched == before, "a raid alone does not wake a self-waking agent");
            thalion.X = 150;
            Brain.OnHour(W(h0 + 2, thalion));
            string p = Poke();
            string why = p.Substring(p.IndexOf("WHY YOU ARE ASKED NOW", StringComparison.Ordinal));
            Check(Brain.PokesWatched == before + 1 && why.Contains("Your watch \"keep_road\": Thalion is within 15 of Orc Keep") && p.Contains("Quieter news since then") && p.Contains("Orc Village is being raided"),
                  "its watch wakes it, and the raid rides that wake as quieter news");
            int w1 = p.IndexOf("YOUR WATCHES (1 of 12", StringComparison.Ordinal), w2 = p.IndexOf("WHAT CHANGED SINCE", StringComparison.Ordinal);
            Check(w1 > p.IndexOf("WHY YOU ARE ASKED NOW", StringComparison.Ordinal) && (w2 < 0 || w1 < w2) && p.Contains("keep_road (thalion = Thalion [lord_thalion], keep = Orc Keep [castle_two])"),
                  "YOUR WATCHES follows why it is woken, with each watch's names");
            Answer(ch.LastPoke);
            before = Brain.PokesWatched;
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = h0 + 3, Kind = "tool", Text = "Naglur gave up the chase of Thalion.", Party = "p_naglur", Factions = { "orcs" } });
            Brain.OnHour(W(h0 + 3, thalion));
            Check(Brain.PokesWatched == before + 1 && Poke().Contains("Naglur gave up the chase"), "a followed lord's order news wakes it");
            Answer(ch.LastPoke);

            // a review's briefing carries the watches
            AskBriefing(ch, dir);
            Check(ReadIf(Path.Combine(dir, "council.txt")).Contains("YOUR WATCHES (1 of 12"), "a review's briefing lists the watches");

            // a fresh book on the war room's asking (0.18.1): from the newest hour, whenever a question comes; once an hour at most
            string Ask(string id)
            {
                string orders = Path.Combine(dir, "orders");
                Directory.CreateDirectory(orders);
                File.WriteAllText(Path.Combine(orders, id + ".json"), "{}");
                AgentChannel.Scan();
                return ReadIf(Path.Combine(dir, "book", "now.txt"));
            }
            string stamp1 = Ask("book-request-1");
            string nowBook = Path.Combine(dir, "book", "now.json");
            Check(stamp1.StartsWith("book-request-1\n" + (h0 + 3).ToString("R", Text.Inv) + "\n", StringComparison.Ordinal) && File.Exists(nowBook) && ReadIf(nowBook).Contains("\"date\":\"" + Text.When(h0 + 3) + "\"")
                  && ReadIf(nowBook).Contains("lord_naglur") && !Directory.GetFiles(Path.Combine(dir, "orders"), "book-request*").Any(),
                  "a request for the newest hour writes the question book from it and a stamp naming the request, and the request is taken away: " + stamp1.Replace("\n", "|"));
            Check(File.Exists(Path.Combine(Paths.Root, "live", "smoketest", "view_orcs.json")), "and the view file the way-finding questions read");
            DateTime built = File.GetLastWriteTimeUtc(nowBook);
            System.Threading.Thread.Sleep(30);
            string stamp2 = Ask("book-request-2");
            Check(stamp2.StartsWith("book-request-2\n", StringComparison.Ordinal) && File.GetLastWriteTimeUtc(nowBook) == built, "asked again within the same hour, only the stamp is written");
            Brain.OnHour(W(h0 + 5, thalion));
            string stamp3 = Ask("book-request-3");
            Check(stamp3.Contains("\n" + (h0 + 5).ToString("R", Text.Inv) + "\n") && ReadIf(nowBook).Contains("\"date\":\"" + Text.When(h0 + 5) + "\""), "a new hour, a new book: " + stamp3.Replace("\n", "|"));

            // carried across a save, checked again, clocks as they stood
            JArray saved = Watches.Export(Agents.Of("orcs").Watches);
            var copy = new List<Watch>();
            List<string> no = Watches.Import(copy, saved, W(h0 + 4, thalion), null, "orcs");
            Check(no.Count == 0 && copy.Count == 1 && copy[0].Name == "keep_road" && Math.Abs(copy[0].Until - (h0 + 40)) < 1e-6 && copy[0].When[0].WasTrue,
                  "a watch written at the save is taken up at its load, its end and its conditions as they stood: " + string.Join("; ", no));
            JArray broken = (JArray)saved.DeepClone();
            broken[0]["names"]["thalion"] = "lord_gone";
            Check(Watches.Import(copy, broken, W(h0 + 4, thalion), null, "orcs").Count == 1 && copy.Count == 0, "one whose names the realm no longer knows is not carried on, and said");
            Watches.SetPending(new Dictionary<string, JArray> { ["orcs"] = saved });
            Check(Watches.TakePending("orcs", out JArray got) && got.Count == 1 && !Watches.TakePending("orcs", out _), "the watches of a save wait for the realm's first hour, once");
            Check(Views.AfterSection("A\r\nWHY YOU ARE ASKED NOW\r\n- x\r\n\r\nNEXT\r\n", "WHY YOU ARE ASKED NOW", "W") == "A\r\nWHY YOU ARE ASKED NOW\r\n- x\r\n\r\nW\r\n\r\nNEXT\r\n"
                  && Views.AfterSection("no heading", "WHY", "W").EndsWith("\r\n\r\nW\r\n"), "a section goes right after the one named, or at the end");
        }
    }
}
