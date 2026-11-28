using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// Module 0.25.7 (Nov 28, run 20261127-1453-C2): (1) a lord whose order would ride into a storm under way holds instead, and the agent is told once
        /// (day 160: the agent's own strike order sent 17 lords at Goksun's camp after his storm of Shúrdmúl had begun; they joined the defenders, the storm
        /// became a battle outside the walls led by the castle, and all 17 were taken); relieve still rides in, by its design. (2) The siege-lift bar counts
        /// this siege's lords still on their way who can reach the camp by the middle or end of the relief's battle (day 153: Shúrdmúl's first siege was lifted
        /// on Kisha's 1.2k alone while 16 lords of it were still marching in).
        /// </summary>
        private static void Fixes0257Tests()
        {
            Runner.Reset();
            string Notices() { Files.FlushAll(); return ReadIf(Path.Combine(Brain.Dir, "notices.jsonl")); }
            PartyRow Own(string id, string name, float x, float y, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), SizeLimit = 1000, Strength = strength, Speed = 5,
                FoodDays = 20, FoodItems = 200, FoodEats = 10, X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 3000
            };
            PartyRow Foe(string id, string name, float x, float y, float strength) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = (int)(strength / 2), Strength = strength, Speed = 4, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            ToolInstance Inst(int id) => Runner.All().FirstOrDefault(x => x.Id == id);
            string Verb(int id) { ToolInstance x = Inst(id); return x != null && x.HasCurrent ? x.Current.Verb : null; }
            string Aim(int id) { ToolInstance x = Inst(id); return x != null && x.HasCurrent ? x.Current.Party : null; }

            // ---------- (1) no lord rides into a storm under way, but with relieve ----------
            ToolDef hit = ToolDef.FromJson(@"{ ""name"": ""hit_camp"", ""kind"": ""tool"", ""about"": ""attacks the besiegers of a holding"", ""at"": ""any_holding"", ""start"": ""go"", ""states"": {
                ""go"": { ""about"": ""falling on the camp at {at}"", ""do"": ""engage besieger(at)"", ""when"": [ { ""if"": ""not besieged(at)"", ""go"": ""done"" } ] },
                ""done"": { ""about"": ""done"", ""end"": ""free"" } } }", "hit_camp", "orcs");
            Check(hit.Valid, "0.25.7: a test order that attacks a siege camp passes the checker: " + string.Join("; ", hit.Problems));
            var bes = Foe("bes", "Halbarad", 103, 100, 3000);
            bes.Besieging = "castle_orc";
            bes.Doing = "BesiegeSettlement";
            var striker = Own("hc1", "Ufthak", 112, 100, 4000);
            var striker2 = Own("hc2", "Shagrat", 113, 101, 2000);
            double h0 = 93000;
            Snapshot Storm(double hh, bool storming)
            {
                Snapshot s = ToolWorld(hh, bes, striker, striker2);
                SettlementRow c = s.SettlementById["castle_orc"];
                c.BesiegedBy = "player_k";
                c.Besieger = "p_bes";
                s.Sieges.Add(new SiegeRow { Settlement = "castle_orc", Besieger = "p_bes", BesiegerFaction = "player_k", StartHours = hh - 30, Prep = 1f, PrepRate = 0.05f, WallLevel = 1, Advantage = 2f, Defenders = 450, Attackers = 1500, Storming = storming });
                return s;
            }
            Diag.Hours = h0;
            Brain.OnHour(Storm(h0, true));
            int before = System.Text.RegularExpressions.Regex.Matches(Notices(), "holds back from Halbarad").Count;
            Runner.Start(9301, new ToolOrder { Tool = "hit_camp", Def = hit, Lord = striker.Leader, LordName = striker.Name, Party = striker.Id, At = "castle_orc", Realm = "orcs" });
            Runner.Start(9302, new ToolOrder { Tool = "hit_camp", Def = hit, Lord = striker2.Leader, LordName = striker2.Name, Party = striker2.Id, At = "castle_orc", Realm = "orcs" });
            for (int k = 1; k <= 3; k++) { Diag.Hours = h0 + k; Brain.OnHour(Storm(h0 + k, true)); }
            int told = System.Text.RegularExpressions.Regex.Matches(Notices(), "holds back from Halbarad").Count - before;
            Check(Verb(9301) == "hold" && Verb(9302) == "hold" && told == 1
                  && Notices().Contains("it is storming Orc Castle now") && Notices().Contains("give them relieve, which rides into a storm by its design"),
                  "0.25.7: while their storm of our castle is fought, two lords whose order would ride in hold, and the agent is told once (" + Verb(9301) + ", " + Verb(9302) + ", told " + told + ")");
            Diag.Hours = h0 + 4;
            Brain.OnHour(Storm(h0 + 4, false));
            Diag.Hours = h0 + 5;
            Brain.OnHour(Storm(h0 + 5, false));
            Check(Verb(9301) == "engage" && Aim(9301) == "p_bes",
                  "0.25.7: once the storm is over and the siege goes on, the order falls on the camp again (" + Verb(9301) + " " + Aim(9301) + ")");
            Runner.Ended(9301, "test over");
            Runner.Ended(9302, "test over");
            Runner.Reset();

            // ---------- (2) the siege-lift bar counts this siege's lords still on their way ----------
            var kisha = Own("ksh", "Kisha", 168, 100, 1200);
            kisha.Besieging = "castle_men";
            var rest = Own("rst", "Gárjan", 152, 100, 6000);                 // 18 away, beyond 6: on his way to the same siege
            var far = Own("far", "Imák", 400, 100, 9000);                    // far off: cannot be there in time
            var relief = Foe("gks", "Goksun", 198, 100, 3000);
            relief.Doing = "GoToSettlement";
            relief.TargetSettlement = "castle_men";
            double h1 = 94000;
            Snapshot Lift(double hh) => ToolWorld(hh, kisha, rest, far, relief);
            Diag.Hours = h1;
            Brain.OnHour(Lift(h1));
            var lift = new Dictionary<string, double> { ["withdraw_on_relief"] = 0.9, ["wait_hours"] = 0, ["storm"] = 2, ["storm_guard"] = 0 };
            ToolOrder Besiege(PartyRow p) { var o = new ToolOrder { Tool = "besiege", Lord = p.Leader, LordName = p.Name, Party = p.Id, At = "castle_men", Realm = "orcs" }; foreach (var kv in lift) o.Settings[kv.Key] = kv.Value; return o; }
            Runner.Start(9311, Besiege(kisha));
            Runner.Start(9312, Besiege(rest));
            Runner.Start(9313, Besiege(far));
            Diag.Hours = h1 + 1;
            BannerlordAgents.Tools.World w1 = Runner.WorldFor(Lift(h1 + 1));
            double odds = Expr.Number(ScopeFor(9311, w1)?.Call("fight_odds", new object[] { "castle_men" }));
            Check(odds > 0.2 && odds < 0.9,
                  "0.25.7: one lord camped (1,200) and a relief of 3,000 coming: the side counts the siege's lord still on his way who reaches the camp in time (6,000) and not one too far off (odds "
                  + ToolDef.Num(Math.Round(odds, 2)) + "; on the camp alone it was 2.5)");
            for (int k = 2; k <= 4; k++) { Diag.Hours = h1 + k; Brain.OnHour(Lift(h1 + k)); }
            Check(!Notices().Contains("lifts the siege of Fornost") && Inst(9311) != null && !Inst(9311).Finished,
                  "0.25.7: so the siege is not lifted on the first lord alone (" + Inst(9311)?.State + ")");
            foreach (int id in new[] { 9311, 9312, 9313 }) Runner.Ended(id, "test over");
            Runner.Reset();
        }
    }
}
