using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.19 (Nov 13): the harass order (lords ahead of an attack on an enemy castle or town: roaming its ground in a group within reach of each other, striking its
        /// isolated lords, raiding its villages only when nothing could come before the raid is done, sheltering from strong forces, and once a camp of the realm's
        /// besieges it, a post round the camp, strikes on the relief and the camp joined for the storm); the reserve order (a lord standing off another, who rides in
        /// the hour that lord fights, whatever the odds, if he can get there in time); and the battle facts of the language.
        /// </summary>
        private static void HarassTests()
        {
            var moves = new List<KeyValuePair<int, Move>>();
            var released = new List<int>();
            Action<int, Move> keptMove = Runner.MoveSink;
            Action<int, string> keptRelease = Runner.ReleaseSink;
            Runner.MoveSink = (id, m) => moves.Add(new KeyValuePair<int, Move>(id, m));
            Runner.ReleaseSink = (id, why) => released.Add(id);
            Runner.Reset();
            PartyRow Orc(string id, string name, float strength, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = (int)(strength / 2), SizeLimit = 300, Strength = strength, Speed = 5, FoodDays = 10,
                X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 5000
            };
            PartyRow Man(string id, string name, float strength, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "player_k", Kind = "lord", Men = (int)(strength / 2), Strength = strength, Speed = 4, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            Snapshot W(double h, Action<Snapshot> edit, params PartyRow[] parties) { Snapshot s = ToolWorld(h, parties); edit?.Invoke(s); return s; }
            void Hour(double h, Action<Snapshot> edit, params PartyRow[] parties) { Diag.Hours = h; Brain.OnHour(W(h, edit, parties)); }
            ToolOrder Harass(PartyRow r, double caution = -1, double radius = -1)
            {
                var o = new ToolOrder { Tool = "harass", Lord = r.Leader, LordName = r.Name, Party = r.Id, At = "castle_men", Realm = "orcs" };
                if (caution >= 0) o.Settings["caution"] = caution;
                if (radius >= 0) o.Settings["radius"] = radius;
                return o;
            }
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;
            object Mem(int id, string key) => Runner.All().FirstOrDefault(t => t.Id == id) is ToolInstance ti && ti.Memory.TryGetValue(key, out object v) ? v : null;
            bool Moved(int id, string verb, string place = null, string party = null)
                => moves.Any(m => m.Key == id && m.Value.Verb == verb && (place == null || m.Value.Place == place) && (party == null || m.Value.Party == party));

            ToolDef harass = Library.Get("harass"), reserve = Library.Get("reserve");
            Check(harass != null && harass.Valid && harass.At == "enemy_fort" && harass.Lords && harass.Settings.ContainsKey("standoff") && harass.Settings.ContainsKey("storm_margin")
                  && reserve != null && reserve.Valid && reserve.Target == "own_lord" && reserve.Settings.ContainsKey("keep") && reserve.Settings.ContainsKey("away")
                  && Library.Text().Contains("- **harass** (at: an enemy castle or town") && Library.Text().Contains("- **reserve** (target: one of your lords")
                  && Library.Text().Contains("A lord on reserve rides to no other fight"),
                  "harass (at an enemy castle or town, with a group) and reserve (on a lord of yours) pass the checker and are in the library the agents read: "
                  + string.Join("; ", (harass?.Problems ?? new List<string>()).Concat(reserve?.Problems ?? new List<string>())));

            // harass roams the place's ground: a patrol within its radius and no nearer than its stand-off; in position once inside its area
            var ufthak = Orc("hr1", "Ufthak", 300, 150, 100);
            double h0 = 70000;
            Hour(h0, null, ufthak);
            Runner.Start(9990, Harass(ufthak));
            bool roams = moves.Any(m => m.Key == 9990 && m.Value.Verb == "patrol" && m.Value.Place == "castle_men" && Math.Abs(m.Value.R - 12) < 0.01 && Math.Abs(m.Value.R0 - 4) < 0.01);
            bool notYet = Expr.Number(Mem(9990, "told")) == 0;
            ufthak.X = 162;
            Hour(h0 + 1, null, ufthak);
            Check(roams && notYet && Expr.Number(Mem(9990, "told")) == 1 && StateOf(9990) == "round",
                  "harass roams the enemy place's ground (a patrol within its radius, 12, no nearer its walls than its standoff, 4), and calls back once he is in position (" + roams + ", " + notYet + ", " + StateOf(9990) + "; " + string.Join(" / ", moves.Where(m => m.Key == 9990).Select(m => m.Value.ToString())) + ")");
            Runner.Reset();

            // it strikes an isolated enemy lord in its area, in the hour it weighs him
            var grishnakh = Orc("hr2", "Grishnakh", 300, 165, 100);
            var halbarad = Man("hf1", "Halbarad", 80, 166, 108);
            double h1 = 70100;
            Hour(h1, null, grishnakh, halbarad);
            Runner.Start(9991, Harass(grishnakh));
            Hour(h1 + 1, null, grishnakh, halbarad);
            double odds1 = Expr.Number(Mem(9991, "odds"));
            Check(Moved(9991, "engage", party: "p_hf1") && StateOf(9991) == "strike" && odds1 > 0.2 && odds1 < 0.35,
                  "harass strikes an isolated enemy lord in its area by the shared weighing (80 against 300: " + ToolDef.Num(Math.Round(odds1, 2)) + " of our side); state " + StateOf(9991));
            Runner.Reset();

            // raids: refused while an enemy that could reach the village before the raid is done outweighs the bar; taken when none could; begun; ended
            var lagduf = Orc("hr3", "Lagduf", 300, 158, 106);
            var elendil = Man("hg1", "Elendil", 2000, 200, 140);
            double h2 = 70200;
            Hour(h2, null, lagduf, elendil);
            Runner.Start(9992, Harass(lagduf, radius: 16));
            Hour(h2 + 1, null, lagduf, elendil);
            bool refused = !Moved(9992, "raid") && StateOf(9992) != "toraid";
            Hour(h2 + 2, null, lagduf);
            bool rides = Moved(9992, "raid", "village_men") && StateOf(9992) == "toraid";
            lagduf.X = 160; lagduf.Y = 110; lagduf.InBattle = true; lagduf.Raiding = "village_men";
            Hour(h2 + 3, s => s.SettlementById["village_men"].VillageState = "BeingRaided", lagduf);
            bool raiding = StateOf(9992) == "raiding";
            lagduf.InBattle = false; lagduf.Raiding = null;
            Hour(h2 + 4, s => s.SettlementById["village_men"].VillageState = "Looted", lagduf);
            bool done = StateOf(9992) == "roam" || StateOf(9992) == "round" || StateOf(9992) == "back";
            bool triedKept = Mem(9992, "tried") is Dictionary<string, object> tried && tried.ContainsKey("village_men");
            Check(refused && rides && raiding && done && triedKept,
                  "harass refuses a raid while an enemy lord (2,000, some 15 hours off) could reach the village before the raid is done (about 53 hours for 150 men), "
                  + "rides to raid it once none could, raids it, and goes back to harassing once it is looted (" + refused + ", " + rides + ", " + raiding + ", " + done + ", " + triedKept + ")");
            Runner.Reset();

            // the group: a lord farther than keep_hours of march from the nearest of his group in the area closes up to him first; mates and their strength
            var mauhur = Orc("hc1", "Mauhur", 300, 165, 92);
            var ugluk = Orc("hc2", "Ugluk", 400, 182, 108);
            double h3 = 70300;
            Hour(h3, null, mauhur, ugluk);
            Runner.Start(9993, Harass(mauhur));
            Runner.Start(9994, Harass(ugluk));
            Hour(h3 + 1, null, mauhur, ugluk);
            bool closes = StateOf(9993) == "close" && Moved(9993, "toward", party: "p_hc2");
            World w3 = Runner.WorldFor(W(h3 + 1, null, mauhur, ugluk));
            IScope sc3 = ScopeFor(9993, w3);
            object near3 = sc3?.Call("nearest_mate", new object[] { "castle_men", 30.0 }), group3 = sc3?.Call("mates_strength", new object[] { "castle_men", 30.0 });
            List<object> list3 = sc3?.Call("mates", new object[0]) as List<object>;
            Check(closes && (near3 as string) == "p_hc2" && Expr.Number(group3) == 700 && list3 != null && list3.Count == 1,
                  "harass keeps its group within reach: a lord 23 apart from the nearest of his group (some 6 hours, more than keep_hours 4) closes up to him; mates(), nearest_mate and mates_strength read the group ("
                  + closes + ", " + near3 + ", " + group3 + ")");
            Runner.Reset();

            // shelter: from a force that could reach him, into a castle that would hold with him inside; falling back toward one, outside, when none would
            var shagrat = Orc("hs1", "Shagrat", 300, 165, 100);
            var army1 = Man("ha1", "Imrahil", 1000, 172, 102);
            double h4 = 70400;
            Hour(h4, null, shagrat, army1);
            Runner.Start(9995, Harass(shagrat, caution: 0));
            Hour(h4 + 1, null, shagrat, army1);
            bool shelters = StateOf(9995) == "shelter" && Moved(9995, "enter", "castle_two");
            Runner.Reset();
            var gorbag = Orc("hs2", "Gorbag", 300, 165, 100);
            var army2 = Man("ha2", "Imrahil", 3000, 172, 102);
            Hour(h4 + 10, null, gorbag, army2);
            Runner.Start(9996, Harass(gorbag, caution: 0));
            Hour(h4 + 11, null, gorbag, army2);
            bool fallsBack = StateOf(9996) == "fallback" && Moved(9996, "near", "castle_two");
            Check(shelters && fallsBack,
                  "harass shelters from a force that outweighs it, in the castle of yours it reaches first that would hold with it inside (1,000 against Orc Keep's 250 and his 300 behind its walls), "
                  + "and falls back toward it, staying outside, when it would not hold (3,000) (" + shelters + ", " + StateOf(9995) + ", " + fallsBack + ", " + StateOf(9996) + ")");
            Runner.Reset();

            // back to his post (0.19, Nov 13: harassers go back to their defensive posts once enemy strength gathers against them): strength near the place that matches
            // his group's but is no sudden danger to him sends him home to the post he was sent from, where his harassing ends (his next order, else he holds there)
            var muzgash = Orc("hw1", "Radbug", 300, 160, 94);
            var gather1 = Man("hw2", "Beregond", 340, 178, 104);
            double h4b = 70450;
            Hour(h4b, null, muzgash, gather1);
            ToolOrder home = Harass(muzgash, caution: 0);
            home.Places.Add("castle_two");
            Runner.Start(9980, home);
            for (int k = 1; k <= 6; k++) Hour(h4b + k, null, muzgash, gather1);
            bool notYetHome = StateOf(9980) != "withdraw" && Expr.Number(Mem(9980, "gath")) > 0;
            for (int k = 7; k <= 14; k++) Hour(h4b + k, null, muzgash, gather1);
            bool goesHome = StateOf(9980) == "withdraw" && Moved(9980, "goto", "castle_two") && (Mem(9980, "home") as string) == "castle_two";
            muzgash.X = 140; muzgash.Y = 100; muzgash.InSettlement = "castle_two";
            Hour(h4b + 15, null, muzgash, gather1);
            bool posted = StateOf(9980) == "posted" && Moved(9980, "hold");
            Runner.Reset();
            var golfimbul = Orc("hw3", "Snaga", 300, 160, 94);
            var gather2 = Man("hw4", "Beregond", 200, 178, 104);
            Hour(h4b + 20, null, golfimbul, gather2);
            Runner.Start(9981, Harass(golfimbul, caution: 0));
            for (int k = 21; k <= 36; k++) Hour(h4b + k, null, golfimbul, gather2);
            bool stays = StateOf(9981) != "withdraw" && !Moved(9981, "goto", "castle_two");
            // the two settings are the agent's: a shorter wait sends him home sooner; a higher bar keeps him out against the same even force
            Runner.Reset();
            var lugbur = Orc("hw5", "Lugbur", 300, 160, 94);
            var gather3 = Man("hw6", "Beregond", 340, 178, 104);
            Hour(h4b + 40, null, lugbur, gather3);
            ToolOrder quick = Harass(lugbur, caution: 0);
            quick.Settings["withdraw_hours"] = 3;
            Runner.Start(9982, quick);
            for (int k = 41; k <= 45; k++) Hour(h4b + k, null, lugbur, gather3);
            bool sooner = StateOf(9982) == "withdraw";
            Runner.Reset();
            var bolg2 = Orc("hw7", "Bolg", 300, 160, 94);
            Hour(h4b + 50, null, bolg2, gather3);
            ToolOrder hold = Harass(bolg2, caution: 0);
            hold.Settings["withdraw_at"] = 1.5;
            Runner.Start(9983, hold);
            for (int k = 51; k <= 66; k++) Hour(h4b + k, null, bolg2, gather3);
            bool holdsOut = StateOf(9983) != "withdraw" && !Moved(9983, "goto", "castle_two");
            Check(notYetHome && goesHome && posted && stays && sooner && holdsOut,
                  "harass goes back to its post (castle_two, the first of places) once the enemy that could reach the place within reach_hours has matched its group for half a day (340 against 300, "
                  + "no sudden danger to him), not within six hours, and its harassing ends there; a weaker gathering (200) does not send it home; withdraw_hours 3 sends it home within the hours, "
                  + "and withdraw_at 1.5 keeps it out against the same even force (" + notYetHome + ", " + goesHome + ", " + posted + ", " + stays + ", " + sooner + ", " + holdsOut + ")");
            Runner.Reset();

            // a siege of your realm's at the place: a post about keep from the camp; a relief struck by the weighing, the camp left out of a fight it is not in; the camp
            // joined for the storm when it is short of the game's assault test, not when it is far past it; the order ends when the place falls
            var camp = Orc("gb1", "Bolg", 1000, 170, 103);
            camp.Besieging = "castle_men";
            camp.Doing = "BesiegeSettlement";
            camp.TargetSettlement = "castle_men";
            Action<Snapshot> Sieged(float prep, float attackers) => s =>
            {
                SettlementRow fornost = s.SettlementById["castle_men"];
                fornost.BesiegedBy = "orcs";
                fornost.Besieger = "p_gb1";
                fornost.GarrisonStrength = 600;
                s.Sieges.Add(new SiegeRow { Settlement = "castle_men", Besieger = "p_gb1", BesiegerFaction = "orcs", StartHours = s.Hours - 10, Prep = prep, PrepRate = 0.02f,
                                            WallLevel = 1, Advantage = 2f, Defenders = 600, Attackers = attackers });
            };
            var radbug = Orc("hp1", "Radbug", 300, 160, 95);
            double h5 = 70500;
            Hour(h5, Sieged(0.5f, 1000), radbug, camp);
            Runner.Start(9997, Harass(radbug));
            Hour(h5 + 1, Sieged(0.5f, 1000), radbug, camp);
            bool posts = StateOf(9997) == "post" && Moved(9997, "shadow", party: "p_gb1") && Expr.Number(Mem(9997, "sieged")) == 1;
            var beregond = Man("rl1", "Beregond", 150, 185, 100);
            beregond.Doing = "DefendSettlement";
            beregond.TargetSettlement = "castle_men";
            Hour(h5 + 2, Sieged(0.5f, 1000), radbug, camp, beregond);
            double odds5 = Expr.Number(Mem(9997, "odds"));
            bool strikesRelief = StateOf(9997) == "strike" && Moved(9997, "engage", party: "p_rl1") && odds5 > 0.4 && odds5 < 0.6;
            Check(posts && strikesRelief,
                  "harass takes a post about keep from your siege camp at the place, and strikes a lord coming to its relief by the shared weighing, without counting the camp, which joins no fight "
                  + "in the field (150 against his 300: " + ToolDef.Num(Math.Round(odds5, 2)) + "; " + posts + ", " + strikesRelief + ", " + StateOf(9997) + ")");
            Runner.Reset();

            var lurtz = Orc("hp2", "Lurtz", 300, 160, 95);
            Hour(h5 + 10, Sieged(1f, 1000), lurtz, camp);
            Runner.Start(9998, Harass(lurtz));
            Hour(h5 + 11, Sieged(1f, 1000), lurtz, camp);
            bool joins = StateOf(9998) == "camp" && Moved(9998, "besiege", "castle_men");
            World w5 = Runner.WorldFor(W(h5 + 11, Sieged(1f, 1000), lurtz, camp));
            IScope sc5 = ScopeFor(9998, w5);
            double need = Expr.Number(sc5?.Call("storm_need", new object[] { "castle_men" })), sodds = Expr.Number(sc5?.Call("storm_odds", new object[] { "castle_men" }));
            bool ourSiege = Expr.Truthy(sc5?.Call("our_siege", new object[] { "castle_men" }));
            Runner.Reset();
            var snaga = Orc("hp3", "Snaga", 300, 160, 95);
            Hour(h5 + 20, Sieged(1f, 5000), snaga, camp);
            Runner.Start(9999, Harass(snaga));
            Hour(h5 + 21, Sieged(1f, 5000), snaga, camp);
            bool staysOut = StateOf(9999) == "post" && !Moved(9999, "besiege");
            Check(joins && staysOut && ourSiege && Math.Abs(need - 600 * Math.Pow(2, 1.25)) < 1.5 && Math.Abs(sodds - 1000 / (600 * Math.Pow(2, 1.25))) < 0.01,
                  "harass joins your camp for the storm when the camp is short of the game's assault test (1,000 against the " + ToolDef.Num(Math.Round(need)) + " it asks with no ram or tower built: "
                  + ToolDef.Num(Math.Round(sodds, 2)) + "), and keeps its post when the camp is far past it (5,000) (" + joins + ", " + staysOut + ", " + ourSiege + ")");
            Hour(h5 + 22, s => { s.SettlementById["castle_men"].Faction = "orcs"; }, snaga);
            Check(released.Contains(9999) && StateOf(9999) == "done",
                  "harass ends, its lord let go (or his next order started), once the place falls to his realm (" + released.Contains(9999) + ")");
            Runner.Reset();

            ReserveTests(moves, released);
            BattleFactsTests();
            StormGuardTests();
            FrontsTests();
            Runner.MoveSink = keptMove;
            Runner.ReleaseSink = keptRelease;
        }

        /// <summary>A tool run's scope on a world, as its rules read it (the smoke test's look at its functions).</summary>
        private static IScope ScopeFor(int id, World w) => Runner.ScopeOf(id, w);

        /// <summary>The reserve order: station off its lord, behind him from the nearest enemy; riding in when he fights, outweighed or not; not when the fight ends before it could arrive.</summary>
        private static void ReserveTests(List<KeyValuePair<int, Move>> moves, List<int> released)
        {
            PartyRow Orc(string id, string name, float strength, int men, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = name, Faction = "orcs", Kind = "lord", Men = men, SizeLimit = 300, Strength = strength, Speed = 5, FoodDays = 10,
                X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 5000
            };
            void Hour(double h, BattleRow b, params PartyRow[] parties) { Diag.Hours = h; Snapshot s = ToolWorld(h, parties); if (b != null) s.Battles.Add(b); Brain.OnHour(s); }
            string StateOf(int id) => Runner.All().FirstOrDefault(t => t.Id == id)?.State;
            Move? Last(int id) => moves.Where(m => m.Key == id).Select(m => (Move?)m.Value).LastOrDefault();
            ToolOrder Reserve(PartyRow r, PartyRow lord) => new ToolOrder { Tool = "reserve", Lord = r.Leader, LordName = r.Name, Party = r.Id, Target = lord.Id, Realm = "orcs" };
            BattleRow Fight(double h, PartyRow us, PartyRow them) => new BattleRow
            {
                Key = them.Id + "@" + (h - 1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), Kind = "field", X = us.X, Y = us.Y, StartHours = h - 1,
                Attackers = new BattleSideRow { Leader = them.Id, Faction = "player_k", Healthy = them.Men, StartHealthy = them.Men, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { them.Id } },
                Defenders = new BattleSideRow { Leader = us.Id, Faction = "orcs", Healthy = us.Men, StartHealthy = us.Men, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { us.Id } }
            };

            // station: about keep from the lord, on his side away from the nearest enemy (the enemy south of him, the station north)
            var lugdush = Orc("rv1", "Lugdush", 400, 300, 100, 130);
            var reserveLord = Orc("rv2", "Muzgash", 400, 300, 120, 130);
            var foe = new PartyRow { Id = "p_rve", Leader = "lord_rve", Name = "Eomer", Faction = "player_k", Kind = "lord", Men = 700, Strength = 1400, Speed = 4, X = 100, Y = 105, Doing = "Hold", Spottable = true };
            double h0 = 71000;
            Hour(h0, null, lugdush, reserveLord, foe);
            Runner.Start(9900, Reserve(reserveLord, lugdush));
            Move? st = Last(9900);
            bool station = StateOf(9900) == "station" && st?.Verb == "shadow" && st?.Party == "p_rv1" && Math.Abs(st.Value.X - 100) < 0.6 && Math.Abs(st.Value.Y - 140) < 0.6;

            // his lord is attacked by a far stronger enemy (1,400 against the two of them, 800): he rides in all the same, and the fight's parties are no danger he turns from
            reserveLord.X = 100; reserveLord.Y = 140;
            lugdush.InBattle = foe.InBattle = true;
            foe.X = 100; foe.Y = 130.4f;
            Hour(h0 + 1, Fight(h0 + 1, lugdush, foe), lugdush, reserveLord, foe);
            bool rides = StateOf(9900) == "ride" && Last(9900)?.Verb == "engage" && Last(9900)?.Party == "p_rve";
            // the fight over: back to his station
            lugdush.InBattle = foe.InBattle = false;
            foe.X = 100; foe.Y = 100;
            Hour(h0 + 2, null, lugdush, reserveLord, foe);
            bool back = StateOf(9900) == "station";
            Runner.Reset();

            // a fight that ends before he could get there: he keeps his station and says so once
            var far = Orc("rv3", "Lurtz", 400, 300, 100, 230);
            lugdush.InBattle = foe.InBattle = true;
            foe.X = 100; foe.Y = 130.4f;
            Hour(h0 + 10, Fight(h0 + 10, lugdush, foe), lugdush, far, foe);
            Runner.Start(9901, Reserve(far, lugdush));
            Hour(h0 + 11, Fight(h0 + 11, lugdush, foe), lugdush, far, foe);
            ToolInstance t1 = Runner.All().FirstOrDefault(t => t.Id == 9901);
            bool keeps = StateOf(9901) == "station" && t1 != null && Expr.Number(t1.Memory["told"]) == 1 && !moves.Any(m => m.Key == 9901 && m.Value.Verb == "engage");
            // the lord he backs gone: it ends
            lugdush.InBattle = foe.InBattle = false;
            Hour(h0 + 12, null, far, foe);
            bool ends = released.Contains(9901);
            Check(station && rides && back && keeps && ends,
                  "reserve keeps station about keep (10) from its lord, behind him from the nearest enemy; rides in when its lord fights, though his side is outweighed (800 against 1,400), "
                  + "and goes back after; keeps its station when the fight would be over before it got there; ends when its lord is gone ("
                  + station + ", " + rides + ", " + back + ", " + keeps + ", " + ends + ")");
            Runner.Reset();
        }

        /// <summary>The battle facts: in a fight, its hours left and the foe; pace; marching to join an army; the slowdown and the retreat; the game's quick reaction mirrored; raids; could_reach; fall_back.</summary>
        /// <summary>
        /// The storm guard (0.19.1, Nov 13: storm only when no force that could reach the camp before the storm ends would turn it): a storm run ahead alone and with the relief
        /// that could come in its hours, each force at its own pace and never slower than speed 2.8; the besiege order's storm rule held while it would turn the storm.
        /// </summary>
        private static void StormGuardTests()
        {
            PartyRow Orc(string id, float strength, int men, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = id, Faction = "orcs", Kind = "lord", Men = men, SizeLimit = 1200, Strength = strength, Speed = 3, FoodDays = 10, X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 5000
            };
            PartyRow Man(string id, float strength, int men, float x, float y, float speed) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = id, Faction = "player_k", Kind = "lord", Men = men, Strength = strength, Speed = speed, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            var camp = Orc("sg1", 1500, 1000, 170, 103);
            camp.Besieging = "castle_men";
            camp.Doing = "BesiegeSettlement";
            camp.TargetSettlement = "castle_men";
            Snapshot Sieged(double h, params PartyRow[] more)
            {
                Snapshot s = ToolWorld(h, new[] { camp }.Concat(more).ToArray());
                SettlementRow fornost = s.SettlementById["castle_men"];
                fornost.BesiegedBy = "orcs";
                fornost.Besieger = "p_sg1";
                s.Sieges.Add(new SiegeRow { Settlement = "castle_men", Besieger = "p_sg1", BesiegerFaction = "orcs", StartHours = h - 30, Prep = 1f, PrepRate = 0.02f, WallLevel = 1, Advantage = 2f,
                                            Defenders = 300, DefenderMen = 200, Attackers = 1500, AttackerMen = 1000, AttackerMorale = 60f });
                return s;
            }
            World W(Snapshot s) => new World(s, "orcs", s.WatchedSees, new Dictionary<string, ViewParty>());
            object F(World w, string fn, params object[] a) => Runner.WatchScope(w, "p_sg1", new KeyValuePair<string, string>[0]).Call(fn, a);
            double h = 72000;

            World alone = W(Sieged(h));
            double hours = Expr.Number(F(alone, "storm_hours", "castle_men"));
            bool aloneClear = Expr.Truthy(F(alone, "storm_clear", "castle_men", 0.25)) && Expr.Number(F(alone, "storm_relief", "castle_men")) == 0;
            // a strong army close by: it would come in the storm's hours and turn it
            World near = W(Sieged(h, Man("sr1", 3000, 1500, 178, 103, 4)));
            bool nearHeld = F(near, "storm_clear", "castle_men", 0.25) is bool c1 && !c1 && Expr.Number(F(near, "storm_relief", "castle_men")) == 3000;
            // the same army too far to come before the storm would end
            double far = hours * BannerlordAgents.Common.GamePace.DayAverageOfSpeed(4f) * 1.5 + 20;
            World away = W(Sieged(h, Man("sr2", 3000, 1500, (float)(170 + far), 103, 4)));
            bool farClear = Expr.Truthy(F(away, "storm_clear", "castle_men", 0.25)) && Expr.Number(F(away, "storm_relief", "castle_men")) == 0;
            // a slow army (speed 1) that at its own pace would come too late, but is never counted slower than speed 2.8: counted
            double between = hours * (BannerlordAgents.Common.GamePace.DayAverageOfSpeed(BannerlordAgents.Tools.World.ReliefFloorSpeed) + 1f * BannerlordAgents.Common.GamePace.UnitsPerSpeedHour) / 2;
            var slow = Man("sr3", 3000, 1500, (float)(170 + between), 103, 1);
            World floor = W(Sieged(h, slow));
            bool slowOwnLate = between / floor.PerHour(slow) > hours;
            bool slowCounted = Expr.Number(F(floor, "storm_relief", "castle_men")) == 3000 && F(floor, "storm_clear", "castle_men", 0.25) is bool c2 && !c2;
            Check(hours > 1 && aloneClear && nearHeld && farClear && slowOwnLate && slowCounted,
                  "the storm guard: a storm of Fornost run ahead lasts about " + ToolDef.Num(Math.Round(hours, 1)) + " hours; with no relief it is clear; a 3,000 army 8 away would come in time and turn it; "
                  + "the same army out of reach leaves it clear; a slow army (speed 1) that would come too late at its own pace is counted at speed 2.8 and holds it ("
                  + aloneClear + ", " + nearHeld + ", " + farClear + ", " + slowOwnLate + ", " + slowCounted + ")");

            // the besiege order's storm rule: held while that relief could turn the storm, free once it cannot; storm_guard 0 never holds
            var rules = new List<KeyValuePair<string, StormRule>>();
            Action<string, StormRule> keptSink = Runner.StormSink;
            Runner.StormSink = (party, r) => rules.Add(new KeyValuePair<string, StormRule>(party, r));
            Runner.Reset();
            try
            {
                StormRule Last() => rules.LastOrDefault(kv => kv.Key == "p_sg1").Value;
                void Hour(double at, params PartyRow[] more) { Diag.Hours = at; Brain.OnHour(Sieged(at, more)); }
                var o = new ToolOrder { Tool = "besiege", Lord = camp.Leader, LordName = camp.Name, Party = camp.Id, At = "castle_men", Realm = "orcs" };
                o.Settings["storm"] = 1;
                o.Settings["storm_engines"] = 0;
                Hour(h + 100);
                Runner.Start(9970, o);
                Hour(h + 101, Man("sr4", 3000, 1500, 178, 103, 4));
                Hour(h + 102, Man("sr4", 3000, 1500, 178, 103, 4));
                StormRule held = Last();
                Hour(h + 103);
                StormRule free = Last();
                Runner.Reset();
                rules.Clear();
                var o2 = new ToolOrder { Tool = "besiege", Lord = camp.Leader, LordName = camp.Name, Party = camp.Id, At = "castle_men", Realm = "orcs" };
                o2.Settings["storm"] = 1;
                o2.Settings["storm_guard"] = 0;
                Hour(h + 200);
                Runner.Start(9971, o2);
                Hour(h + 201, Man("sr5", 3000, 1500, 178, 103, 4));
                Hour(h + 202, Man("sr5", 3000, 1500, 178, 103, 4));
                StormRule off = Last();
                Check(held != null && held.Hold && (held.HoldWhy ?? "").Contains("could reach the camp") && free != null && !free.Hold && off != null && !off.Hold,
                      "the besiege order holds its storm while a relief could turn it, frees it once none could, and never holds with storm_guard 0 ("
                      + (held?.ToString() ?? "none") + " / " + (held?.HoldWhy ?? "") + "; " + (free?.Hold.ToString() ?? "none") + "; " + (off?.Hold.ToString() ?? "none") + ")");
            }
            finally { Runner.StormSink = keptSink; Runner.Reset(); }
        }

        private static void BattleFactsTests()
        {
            PartyRow Orc(string id, float strength, int men, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = id, Faction = "orcs", Kind = "lord", Men = men, SizeLimit = 400, Strength = strength, Speed = 5, FoodDays = 10, X = x, Y = y, Doing = "Hold", Spottable = true, Gold = 5000
            };
            PartyRow Man(string id, float strength, int men, float x, float y) => new PartyRow
            {
                Id = "p_" + id, Leader = "lord_" + id, Name = id, Faction = "player_k", Kind = "lord", Men = men, Strength = strength, Speed = 4, X = x, Y = y, Doing = "Hold", Spottable = true
            };
            var mine = Orc("bf1", 400, 300, 100, 130);
            var theirs = Man("bf2", 1400, 700, 100, 130.4f);
            mine.InBattle = theirs.InBattle = true;
            mine.DisorganizedUntil = 71104;
            mine.LastBattleEnd = 71090;
            mine.LastRetreated = true;
            var idle = Orc("bf3", 300, 150, 120, 120);
            var unseen = Man("bf4", 500, 250, 300, 300);
            var joiner = Man("bf5", 300, 150, 140, 140);
            var host = Man("bf6", 3000, 1500, 150, 150);
            host.ArmyLeader = host.Id; host.ArmyMen = 1500; host.ArmyParties = 2; host.ArmyStrength = 3000;
            joiner.ArmyLeader = host.Id;
            var village = new BattleRow
            {
                Key = "p_bf7@71099.00", Kind = "raid", Settlement = "village_men", X = 160, Y = 110, StartHours = 71099,
                Attackers = new BattleSideRow { Leader = "p_bf7", Faction = "orcs", Healthy = 100, StartHealthy = 100, Parties = { "p_bf7" } },
                Defenders = new BattleSideRow { Leader = "village_men", Faction = "player_k", Healthy = 0, StartHealthy = 30, SettlementLed = true }
            };
            var raider = Orc("bf7", 200, 100, 160, 110);
            raider.InBattle = true;
            raider.Raiding = "village_men";
            double h = 71100;
            Snapshot s = ToolWorld(h, mine, theirs, idle, unseen, joiner, host, raider);
            s.WatchedSees.Remove("p_bf4");
            s.SettlementById["village_men"].VillageState = "BeingRaided";
            s.SettlementById["village_men"].HitPoints = 0.5f;
            s.Battles.Add(new BattleRow
            {
                Key = "p_bf2@71099.00", Kind = "field", X = 100, Y = 130, StartHours = 71099,
                Attackers = new BattleSideRow { Leader = "p_bf2", Faction = "player_k", Healthy = 700, StartHealthy = 700, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_bf2" } },
                Defenders = new BattleSideRow { Leader = "p_bf1", Faction = "orcs", Healthy = 300, StartHealthy = 300, Power = 1f, Advantage = 1f, Morale = 60f, Parties = { "p_bf1" } }
            });
            s.Battles.Add(village);
            var w = new World(s, "orcs", s.WatchedSees, new Dictionary<string, ViewParty>());
            IScope sc = Runner.WatchScope(w, "p_bf3", new KeyValuePair<string, string>[0]);
            object F(string fn, params object[] a) => sc.Call(fn, a);

            double left = Expr.Number(F("battle_hours_left", "p_bf1")), loot = Expr.Number(F("battle_hours_left", "p_bf7"));
            Check(Expr.Truthy(F("in_battle_of", "p_bf1")) && Expr.Truthy(F("in_battle_of", "p_bf2")) && F("in_battle_of", "p_bf3") is bool b3 && !b3 && F("in_battle_of", "p_bf4") == null
                  && left > 1 && left < 120 && (F("battle_foe_of", "p_bf1") as string) == "p_bf2" && (F("battle_foe_of", "p_bf2") as string) == "p_bf1" && F("battle_foe_of", "p_bf3") == null
                  && Math.Abs(loot - 0.5 / ((Math.Sqrt(100) + 5) / 900.0)) < 1,
                  "in_battle_of, battle_hours_left and battle_foe_of read a fight as the realm sees it (an enemy out of view: none; 700 against 300: about " + ToolDef.Num(Math.Round(left, 1))
                  + " hours left; a raid whose defenders are down: the hours until the village is looted, " + ToolDef.Num(Math.Round(loot, 1)) + ")");

            Snapshot sp = ToolWorld(h, mine, theirs);
            sp.PlayerParty = "p_bf2";
            sp.Battles.Add(s.Battles[0]);
            var wp = new World(sp, "orcs", sp.WatchedSees, new Dictionary<string, ViewParty>());
            double inPerson = Expr.Number(Runner.WatchScope(wp, "p_bf1", new KeyValuePair<string, string>[0]).Call("battle_hours_left", new object[] { "p_bf1" }));
            Check(inPerson == 0, "battle_hours_left is 0 for a fight of the human player's, fought on the spot with the clock standing still (" + inPerson + ")");

            Check(Math.Abs(Expr.Number(F("pace_of", "p_bf3")) - Math.Round(w.PerHour(idle), 2)) < 0.01 && Math.Abs(Expr.Number(F("pace_of", "p_bf2")) - Math.Round(w.PerHour(theirs), 2)) < 0.01
                  && F("pace_of", "p_bf4") == null && (F("joins_army", "p_bf5") as string) == "p_bf6" && F("joins_army", "p_bf3") == null && F("joins_army", "p_bf6") == null,
                  "pace_of gives any party's pace by the game's marching rules (none for one never seen), and joins_army the army a lord is marching to join (" + F("pace_of", "p_bf2") + ", " + F("joins_army", "p_bf5") + ")");

            Check(Math.Abs(Expr.Number(F("disorganized_of", "p_bf1")) - 4) < 0.01 && Expr.Number(F("disorganized_of", "p_bf3")) == 0 && F("disorganized_of", "p_bf4") == null
                  && F("retreated", "p_bf1") is bool r1 && r1 && F("retreated", "p_bf3") == null,
                  "disorganized_of gives the hours left of the slowdown after a battle (0 when none), and retreated whether the last battle ended in a retreat (none when no battle is known)");

            // the game's quick reaction mirrored: a lone lord takes a bait it outweighs where it stands, not one with friends beside it; it flees one far stronger next to it;
            // an army on the march passes by a lone lord it outweighs more than two to one, and a lord marching to join an army anything weaker than that army
            var bait = Orc("qr1", 200, 100, 200, 200);
            var hunter = Man("qr2", 500, 250, 202, 200);
            var friend = Orc("qr3", 600, 300, 200, 201.5f);
            var prey = Man("qr4", 200, 100, 202, 205);
            var big = Orc("qr5", 600, 300, 202, 206);
            var army = Man("qr6", 3000, 1500, 205, 200);
            army.ArmyLeader = army.Id; army.ArmyStrength = 3000; army.ArmyMen = 1500; army.ArmyParties = 1;
            var lone = Orc("qr7", 300, 150, 200, 200);
            var marcher = Man("qr8", 500, 250, 201.5f, 200);
            var head = Man("qr9", 4000, 2000, 260, 260);
            head.ArmyLeader = head.Id; head.ArmyStrength = 4000; head.ArmyMen = 2000; head.ArmyParties = 2;
            marcher.ArmyLeader = head.Id;
            bool Q(string fn, params PartyRow[] parties)
            {
                Snapshot sq = ToolWorld(h, parties);
                var wq = new World(sq, "orcs", sq.WatchedSees, new Dictionary<string, ViewParty>());
                return Expr.Truthy(Runner.WatchScope(wq, null, new KeyValuePair<string, string>[0]).Call(fn, new object[] { parties[0].Id, parties[1].Id }));
            }
            bool takes = Q("would_attack", hunter, bait), guarded = !Q("would_attack", hunter, bait, friend), flees = Q("would_flee", prey, big) && !Q("would_attack", prey, big);
            bool armySkips = !Q("would_attack", army, lone), loneTakes = Q("would_attack", Man("qr6", 3000, 1500, 205, 200), lone), marchSkips = !Q("would_attack", marcher, lone, head);
            Check(takes && guarded && flees && armySkips && loneTakes && marchSkips,
                  "would_attack and would_flee mirror the game's quick reaction: a lone lord takes a bait he outweighs where it stands, not one with a strong friend beside it; one far weaker flees; "
                  + "an army passes by a lone lord it outweighs more than two to one, and a lord marching to join an army anything weaker than that army ("
                  + takes + ", " + guarded + ", " + flees + ", " + armySkips + ", " + loneTakes + ", " + marchSkips + ")");

            // raid_hours and could_reach
            var orc = Orc("rh1", 300, 150, 150, 110);
            var nearMan = Man("rh2", 900, 450, 170, 110);
            var farMan = Man("rh3", 2000, 1000, 260, 110);
            Snapshot sr = ToolWorld(h, orc, nearMan, farMan);
            var wr = new World(sr, "orcs", sr.WatchedSees, new Dictionary<string, ViewParty>());
            IScope scr = Runner.WatchScope(wr, "p_rh1", new KeyValuePair<string, string>[0]);
            double rh = Expr.Number(scr.Call("raid_hours", new object[] { "village_men" }));
            double pace = wr.PerHour(nearMan);
            double soon = Expr.Number(scr.Call("could_reach", new object[] { "village_men", 10 / pace + 0.5 })), later = Expr.Number(scr.Call("could_reach", new object[] { "village_men", 100 / pace + 1 }));
            Check(Math.Abs(rh - Math.Round(1 / ((Math.Sqrt(150) + 5) / 900.0) + 1, 1)) < 0.05 && soon == 900 && later == 2900 && scr.Call("raid_hours", new object[] { "castle_men" }) == null,
                  "raid_hours gives the game's hours to loot a village for this lord's men (150: " + ToolDef.Num(rh) + "), and could_reach the enemy strength that could march to a place in so many hours ("
                  + soon + " within about 3 hours, " + later + " within about 31)");

            // fall_back: the castle of yours that would hold, else none of those, the one farther from the danger
            var hold = Orc("fb1", 300, 150, 165, 100);
            var weak = Man("fb2", 1000, 500, 172, 102);
            Snapshot sf = ToolWorld(h, hold, weak);
            var wf = new World(sf, "orcs", sf.WatchedSees, new Dictionary<string, ViewParty>());
            object fb = Runner.WatchScope(wf, "p_fb1", new KeyValuePair<string, string>[0]).Call("fall_back", new object[] { 20.0 });
            Check((fb as string) == "castle_two", "fall_back names where the safety rule would take a lord from a force near him (Orc Keep): " + fb);
        }
    }
}
