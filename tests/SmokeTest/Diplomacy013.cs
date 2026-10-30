using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BannerlordAgents;
using Newtonsoft.Json.Linq;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>A made-up realm's diplomacy as its ruler's screens would show it: the orcs at war with the kingdom, at peace with Dol Guldur, Rohan an ally.</summary>
        private static DiploView OrcDiplomacy()
        {
            var v = new DiploView { Realm = "orcs", RealmName = "Misty Mountain Orcs", RulerClan = "clan_azog", RulerClanName = "Azog's clan", Influence = 260, RingWar = true };
            foreach (KeyValuePair<string, int> kv in new Dictionary<string, int>
            {
                ["propose_war"] = 200, ["propose_peace"] = 100, ["propose_alliance"] = 150, ["call_to_war"] = 100, ["propose_trade"] = 50, ["propose_policy"] = 100, ["propose_expel"] = 300
            }) v.Costs[kv.Key] = kv.Value;
            v.Stances.Add(new DiploStance { Id = "player_k", Name = "Kingdom of Arnor and Gondor", Stance = "war", Since = 900, OurLosses = 1200, TheirLosses = 900, OurSieges = 1, TheirSieges = 2, OurRaids = 5, TheirRaids = 3, NoPeace = true, Lords = 24, Strength = 45000 });
            v.Stances.Add(new DiploStance { Id = "dg", Name = "Dol Guldur", Stance = "peace", Since = 500, Lords = 12, Strength = 20000, NoWar = true });
            v.Stances.Add(new DiploStance { Id = "rohan", Name = "Rohan", Stance = "alliance", Since = 700, Lords = 9, Strength = 15000 });
            v.Stances.Add(new DiploStance { Id = "dale", Name = "Dale", Stance = "war", Since = 950, Lords = 6, Strength = 6000, Tribute = -50, TributeDays = 0 });
            v.Clans.Add(new DiploClan { Id = "clan_azog", Name = "Azog's clan", Ruling = true, Lords = 4, Fiefs = 3, Strength = 3000 });
            v.Clans.Add(new DiploClan { Id = "clan_bolg", Name = "Bolg's clan", Lords = 3, Fiefs = 1, Strength = 1800 });
            v.Clans.Add(new DiploClan { Id = "clan_wolf", Name = "Wolf Riders", Mercenary = true, Strength = 900, ContractUntil = 1100, Award = 30 });
            v.ForHire.Add(new DiploClan { Id = "clan_hill", Name = "Hill Trolls", Strength = 1200 });
            v.Policies.Add(new DiploPolicy { Id = "policy_war_tax", Name = "War Tax", Active = true });
            v.Policies.Add(new DiploPolicy { Id = "policy_levy", Name = "Levy", Active = false });
            var vote = new DiploVote { Id = "v3", Title = "Declare war on Dale", Kind = "war", Proposer = "Bolg's clan", Closes = 1010 };
            vote.Options.AddRange(new[] { "Yes", "No" });
            vote.Support.AddRange(new[] { 0.62f, 0.38f });
            vote.Override.AddRange(new[] { 0, 85 });
            v.Votes.Add(vote);
            v.Offers.Add(new DiploOffer { Id = "o2", Kind = "peace", From = "dale", FromName = "Dale", Tribute = 40, Days = 100, Closes = 1012 });
            return v;
        }

        /// <summary>
        /// 0.13 step 4, diplomacy (docs/PLAN-0.13.md Part B), the parts that need no game: an act read as the agent writes it, the checks
        /// against the realm's screens (the stances each act needs, the War of the Ring, influence, the vote or offer open, clans and policies),
        /// the report's DIPLOMACY and OPEN VOTES AND OFFERS, and an answer's acts receipted and recorded (in shadow mode checked only; live, handed
        /// to the game's thread).
        /// </summary>
        private static void DiplomacyTests(string root, string agentDir)
        {
            DiploAct A(string json) => DiploAct.From(JObject.Parse(json), 7, "poke-7.json", 1);
            DiploAct peace = A("{\"act\": \"Propose Peace\", \"with\": \"[dale]\", \"tribute\": -40, \"days\": 30, \"why\": \"end the second front\"}");
            Check(peace.Act == "propose_peace" && peace.With == "dale" && peace.Tribute == -40 && peace.Days == 30 && peace.Why == "end the second front" && A("{\"act\": \"abolish_policy\", \"policy\": \"War Tax\"}").Abolish,
                  "an act is read as the agent writes it: its words in any case, ids with or without brackets, its terms, and a policy's two ways");
            DiploView v = OrcDiplomacy();
            string Why(string json) => DiploRules.Check(A(json), v, out _);
            Check(Why("{\"act\": \"propose_peace\", \"with\": \"dale\"}") == null && Why("{\"act\": \"propose_peace\", \"with\": \"player_k\"}").Contains("War of the Ring forbids peace")
                  && Why("{\"act\": \"propose_peace\", \"with\": \"dg\"}").Contains("not at war"),
                  "peace only with a realm at war with yours, and never one the War of the Ring forbids");
            Check(Why("{\"act\": \"propose_war\", \"with\": \"dg\"}").Contains("forbids war") && Why("{\"act\": \"propose_war\", \"with\": \"rohan\"}").Contains("your ally")
                  && Why("{\"act\": \"propose_war\", \"with\": \"dale\"}").Contains("already at war") && Why("{\"act\": \"propose_war\", \"with\": \"nowhere\"}").Contains("no realm called 'nowhere'"),
                  "war never on an ally, a realm at war already, or one the War of the Ring forbids; an unknown realm is said so");
            v.Stances.First(s => s.Id == "dg").NoWar = false;
            Check(Why("{\"act\": \"propose_war\", \"with\": \"Dol Guldur\"}") == null, "a realm can be named by its name as well as its id");
            Check(Why("{\"act\": \"propose_expel\", \"clan\": \"clan_bolg\"}").Contains("costs your clan 300 influence and it has 260") && Why("{\"act\": \"propose_expel\", \"clan\": \"clan_azog\"}").Contains("your own clan")
                  && Why("{\"act\": \"propose_expel\", \"clan\": \"Wolf Riders\"}").Contains("release_mercenary"),
                  "an act the ruler's clan cannot pay for is refused with the cost; your own clan cannot be expelled, and a mercenary is let go instead");
            Check(Why("{\"act\": \"call_to_war\", \"with\": \"rohan\", \"against\": \"player_k\"}") == null && Why("{\"act\": \"call_to_war\", \"with\": \"dg\", \"against\": \"player_k\"}").Contains("not your ally")
                  && Why("{\"act\": \"call_to_war\", \"with\": \"rohan\", \"against\": \"dg\"}").Contains("not at war with Dol Guldur"),
                  "a call to war goes to an ally, against a realm you are at war with");
            Check(Why("{\"act\": \"propose_policy\", \"policy\": \"Levy\"}") == null && Why("{\"act\": \"propose_policy\", \"policy\": \"War Tax\"}").Contains("in force already")
                  && Why("{\"act\": \"abolish_policy\", \"policy\": \"policy_war_tax\"}") == null && Why("{\"act\": \"abolish_policy\", \"policy\": \"Levy\"}").Contains("not in force"),
                  "a policy is proposed when not in force and abolished when it is");
            Check(Why("{\"act\": \"hire_mercenary\", \"clan\": \"clan_hill\"}") == null && Why("{\"act\": \"hire_mercenary\", \"clan\": \"clan_wolf\"}").Contains("serves your realm already")
                  && Why("{\"act\": \"release_mercenary\", \"clan\": \"clan_wolf\"}") == null && Why("{\"act\": \"release_mercenary\", \"clan\": \"clan_bolg\"}").Contains("no clan serving your realm for pay"),
                  "mercenaries: one for hire is hired, one in your pay let go");
            Check(Why("{\"act\": \"vote\", \"vote\": \"v3\", \"option\": \"no\", \"weight\": \"strong\"}") == null && Why("{\"act\": \"vote\", \"vote\": \"v3\", \"option\": \"2\"}") == null
                  && Why("{\"act\": \"vote\", \"vote\": \"v9\", \"option\": \"yes\"}").Contains("no vote v9 is open") && Why("{\"act\": \"vote\", \"vote\": \"v3\", \"option\": \"maybe\"}").Contains("its options: 1 Yes, 2 No")
                  && Why("{\"act\": \"decide\", \"vote\": \"v3\", \"option\": \"no\"}") == null,
                  "a vote names an open vote and one of its options, by its words or its number; deciding against the clans costs what the game asks (85 here)");
            v.Influence = 50;
            Check(Why("{\"act\": \"vote\", \"vote\": \"v3\", \"option\": \"yes\"}").Contains("costs 150 influence") && Why("{\"act\": \"vote\", \"vote\": \"v3\", \"option\": \"yes\", \"weight\": \"slight\"}") == null
                  && Why("{\"act\": \"decide\", \"vote\": \"v3\", \"option\": \"no\"}").Contains("costs 85 influence"),
                  "with little influence, only a light vote can be paid for, and deciding against the clans not at all");
            v.Influence = 260;
            Check(Why("{\"act\": \"accept_peace\", \"with\": \"dale\"}") == null && Why("{\"act\": \"decline_peace\", \"offer\": \"o2\"}") == null
                  && Why("{\"act\": \"accept_alliance\", \"with\": \"dale\"}").Contains("no alliance offer from dale is open") && Why("{\"act\": \"accept_alliance\", \"offer\": \"o2\"}").Contains("is a peace, not an alliance")
                  && Why("{\"act\": \"accept_peace\", \"with\": \"rohan\"}").Contains("is open to your realm"),
                  "an offer is answered by its id or by who made it, and only as the kind of offer it is");
            Check(Why("{\"act\": \"make_friends\", \"with\": \"dale\"}").StartsWith("'make_friends' is not a diplomatic act") && DiploRules.Check(A("{\"act\": \"propose_peace\", \"with\": \"dale\"}"), null, out _).Contains("not known yet"),
                  "an unknown act lists the acts; with no copy of the realm's screens yet, nothing is decided");

            // the report's sections
            List<string> dip = DiploRules.Diplomacy(v, 1000, h => "hour " + h);
            List<string> open = DiploRules.Open(v, h => "hour " + h);
            Check(dip[0].StartsWith("Your clan's influence: 260. A proposal costs it, in influence: war 200, peace 100") && dip.Any(l => l.StartsWith("The War of the Ring rages"))
                  && dip.Any(l => l.StartsWith("Kingdom of Arnor and Gondor [player_k]: at war since hour 900 (4 days); men lost yours 1,200, theirs 900") && l.Contains("no peace (War of the Ring)"))
                  && dip.Any(l => l.StartsWith("Rohan [rohan]: your ally")) && dip.Any(l => l.Contains("Mercenary clans in your pay") && l.Contains("Wolf Riders [clan_wolf]") && l.Contains("bound until hour 1100"))
                  && dip.Any(l => l.StartsWith("Mercenary clans for hire") && l.Contains("Hill Trolls [clan_hill]")) && dip.Any(l => l.StartsWith("Policies in force: War Tax [policy_war_tax]") && l.Contains("Levy [policy_levy]")),
                  "DIPLOMACY: influence and costs, the War of the Ring, each realm's stance and the war's tally, the clans, mercenaries, those for hire and the policies:\n  " + string.Join("\n  ", dip));
            Check(open.Count == 2 && open[0].StartsWith("[v3] Declare war on Dale, proposed by Bolg's clan; closes hour 1010. Options and the clans' support so far: 1 Yes 62%; 2 No 38% (to decide it against the clans: 85 influence)")
                  && open[1].StartsWith("[o2] A peace offered by Dale [dale]: they pay you 40 a day for 100 days; answer by hour 1012. accept_peace or decline_peace"),
                  "OPEN VOTES AND OFFERS: each vote with its options, their support and what deciding costs; each offer with its terms and when it lapses:\n  " + string.Join("\n  ", open));
            var bolg = new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 300, Strength = 400, Speed = 5, FoodDays = 10, X = 104, Y = 100, Doing = "Hold", Spottable = true };
            Snapshot world = ToolWorld(60000, bolg);
            world.Diplomacy["orcs"] = v;
            View view = Views.Build(world, "orcs", world.WatchedSees, world.HostileToWatched, new Brain.Memory());
            string report = Views.Report(view, new List<Trigger>());
            Check(report.Contains("OPEN VOTES AND OFFERS (2; answer them in \"diplomacy\"") && report.Contains("DIPLOMACY, AS YOUR RULER'S SCREENS SHOW IT") && report.Contains("- [o2] A peace offered by Dale"),
                  "the realm's report carries both sections");
            // 0.13.6: the ids of the ruler's own screens pass the fog check (run 20261029-1903-A flagged every report on the realms' ids);
            // without what the realm knows they would not, so the check still bites
            List<string> fog = Audit.Fog(view, report, world.WatchedSees, world.InspectedByWatched, Brain.Known(world, "orcs", world.WatchedSees, new Brain.Memory(), null));
            List<string> bare = Audit.Fog(view, report, world.WatchedSees, world.InspectedByWatched);
            Check(fog.Count == 0 && bare.Any(p => p.Contains("[player_k]")) && bare.Any(p => p.Contains("[policy_levy]")),
                  "the realms, clans, policies, votes and offers of the ruler's own screens pass the fog check (" + string.Join("; ", fog) + ")");

            // an answer's acts: refused with their reason here, checked only in shadow mode, handed to the game's thread when live
            string dir = Path.Combine(root, "diplomacy-agent");
            Settings.AgentDir = dir;
            Brain.Reset("smoketest-diplomacy", double.NaN, "orcs", null);
            Snapshot hour = ToolWorld(60001, bolg);
            hour.Diplomacy["orcs"] = OrcDiplomacy();
            Brain.OnHour(hour);
            Files.FlushAll();
            int poke = (int?)JObject.Parse(ReadIf(Path.Combine(dir, "poke.json")))["number"] ?? -1;
            string receipts = Path.Combine(dir, "receipts.log");
            File.WriteAllText(Path.Combine(dir, "orders", "poke-" + poke + ".json"), new JObject
            {
                ["poke"] = poke,
                ["diplomacy"] = JArray.Parse("[{\"act\": \"propose_peace\", \"with\": \"dale\", \"why\": \"end the second front\"}, {\"act\": \"propose_peace\", \"with\": \"player_k\"}, {\"act\": \"vote\", \"vote\": \"v3\", \"option\": \"no\"}]")
            }.ToString());
            Thread.Sleep(400);
            int before = ReadIf(receipts).Length;
            AgentChannel.Scan();
            Files.FlushAll();
            string said = ReadIf(receipts).Substring(before);
            Check(said.Contains("diplomacy 1 (propose_peace dale): CHECKED. valid: propose_peace dale (shadow mode: not carried out)") && said.Contains("diplomacy 2 (propose_peace player_k): REFUSED. the War of the Ring forbids peace")
                  && said.Contains("diplomacy 3 (vote v3 no): CHECKED."),
                  "in shadow mode an answer's acts are checked and receipted one by one, and a forbidden one refused with its reason: " + said);
            List<JObject> book = Lines(ReadIf(Path.Combine(Brain.Dir, "diplomacy.jsonl")));
            Check(book.Count(l => (string)l["type"] == "act") == 3 && book.Any(l => (string)l["status"] == "refused" && ((string)l["reason"]).Contains("War of the Ring"))
                  && Lines(ReadIf(Path.Combine(Brain.Dir, "episodes.jsonl"))).Count(l => (string)l["type"] == "diplomacy") == 3
                  && Lines(ReadIf(Path.Combine(Brain.Dir, "episodes.jsonl"))).Any(l => (string)l["type"] == "answer" && (int)l["diplomacy"] == 3),
                  "every act goes into the diplomacy book and the episode index, with what became of it");
            Settings.Agent = "live";
            Brain.Reset("smoketest-diplomacy-2", double.NaN, "orcs", null);
            hour = ToolWorld(60002, bolg);
            hour.Diplomacy["orcs"] = OrcDiplomacy();
            Brain.OnHour(hour);
            Files.FlushAll();
            poke = (int?)JObject.Parse(ReadIf(Path.Combine(dir, "poke.json")))["number"] ?? -1;
            while (Background.ToMain.TryDequeue(out _)) { }
            File.WriteAllText(Path.Combine(dir, "orders", "poke-" + poke + ".json"), "{\"poke\": " + poke + ", \"diplomacy\": [{\"act\": \"hire_mercenary\", \"clan\": \"clan_hill\"}, {\"act\": \"hire_mercenary\", \"clan\": \"clan_nobody\"}]}");
            Thread.Sleep(400);
            before = ReadIf(receipts).Length;
            AgentChannel.Scan();
            Files.FlushAll();
            said = ReadIf(receipts).Substring(before);
            int queued = 0;
            while (Background.ToMain.TryDequeue(out _)) queued++;
            Check(queued == 1 && said.Contains("(hire_mercenary clan_nobody): REFUSED. no clan for hire is called 'clan_nobody'") && !said.Contains("(hire_mercenary clan_hill)"),
                  "live, an act that passes goes to the game's thread to be carried out by the game's own rules (" + queued + " queued); one that fails is refused at once");
            Settings.Agent = "shadow";
            Settings.AgentDir = agentDir;
        }
    }
}
