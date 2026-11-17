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
        /// 0.20.0 (Nov 16: build every siege tower and the ram, then trebuchets held in reserve until all are ready, roll them out together, replace each
        /// that falls, storm once the walls are down; against the player in person swap the trebuchets one by one for fire onagers once his engines are
        /// silenced; defenders against the player keep fire catapults up): the drill's choices, the siege clock with engines and walls run ahead, the
        /// storm rule "once the walls are down", the report's words, and besiege's new settings.
        /// </summary>
        private static void SiegeDrillTests()
        {
            // ---------- the choices ----------
            SiegeDrill.Slot Built(string t) => new SiegeDrill.Slot { Type = t, Built = true };
            SiegeDrill.Slot Half(string t) => new SiegeDrill.Slot { Type = t, Built = false };
            SiegeDrill.SideView Camp(bool prep = true)
            {
                var v = new SiegeDrill.SideView();
                v.Clear(3);
                v.PrepDone = prep;
                return v;
            }
            var rule = DrillRule.Default;
            bool roll;
            SiegeDrill.SideView v0 = Camp(false);
            bool prepHolds = SiegeDrill.Attacker(v0, rule, false, false, 0, out roll).Kind == SiegeDrill.Kind.Hold;
            SiegeDrill.SideView v1 = Camp();
            SiegeDrill.Act a1 = SiegeDrill.Attacker(v1, rule, false, false, 0, out roll);
            v1.Melee[0] = Built(SiegeDrill.Tower);
            SiegeDrill.Act a2 = SiegeDrill.Attacker(v1, rule, false, false, 0, out roll);
            v1.Melee[1] = Built(SiegeDrill.Tower);
            SiegeDrill.Act a3 = SiegeDrill.Attacker(v1, rule, false, false, 0, out roll);
            v1.Melee[2] = Built(SiegeDrill.Ram);
            SiegeDrill.Act a4 = SiegeDrill.Attacker(v1, rule, false, false, 0, out roll);
            v1.Ranged[0] = Half(SiegeDrill.Trebuchet);
            SiegeDrill.Act a5 = SiegeDrill.Attacker(v1, rule, false, false, 0, out roll);
            v1.Ranged[0] = Built(SiegeDrill.Trebuchet);
            SiegeDrill.Act a6 = SiegeDrill.Attacker(v1, rule, false, false, 0, out roll);
            bool order = a1.Kind == SiegeDrill.Kind.Build && a1.Type == SiegeDrill.Tower && !a1.Ranged && a1.Slot == 0
                         && a2.Type == SiegeDrill.Tower && a2.Slot == 1 && a3.Type == SiegeDrill.Ram && a3.Slot == 2
                         && a4.Kind == SiegeDrill.Kind.Build && a4.Type == SiegeDrill.Trebuchet && a4.Ranged && a4.Slot == 0
                         && a5.Kind == SiegeDrill.Kind.Hold && a6.Kind == SiegeDrill.Kind.ToReserve && a6.Slot == 0;
            Check(prepHolds && order, "the siege drill builds both towers, then the ram, then a trebuchet, holds while one is half built, and sends a finished trebuchet to reserve while massing ("
                                      + prepHolds + "; " + a1 + "; " + a2 + "; " + a3 + "; " + a4 + "; " + a5 + "; " + a6 + ")");

            // massing to four, then rolling out together; a fallen trebuchet rebuilt in its spot
            SiegeDrill.SideView m = Camp();
            m.Melee[0] = Built(SiegeDrill.Tower); m.Melee[1] = Built(SiegeDrill.Tower); m.Melee[2] = Built(SiegeDrill.Ram);
            for (int i = 0; i < 3; i++) { m.Reserve.Add(SiegeDrill.Trebuchet); m.ReserveBuilt.Add(true); }
            SiegeDrill.Act three = SiegeDrill.Attacker(m, rule, false, false, 0, out bool roll3);
            m.Reserve.Add(SiegeDrill.Trebuchet); m.ReserveBuilt.Add(true);
            SiegeDrill.Act four = SiegeDrill.Attacker(m, rule, false, false, 0, out bool roll4);
            SiegeDrill.SideView o = Camp();
            o.Melee[0] = Built(SiegeDrill.Tower); o.Melee[1] = Built(SiegeDrill.Tower); o.Melee[2] = Built(SiegeDrill.Ram);
            o.Ranged[0] = Built(SiegeDrill.Trebuchet); o.Ranged[1] = Built(SiegeDrill.Trebuchet); o.Ranged[3] = Built(SiegeDrill.Trebuchet);
            SiegeDrill.Act rebuild = SiegeDrill.Attacker(o, rule, false, true, 2, out roll);
            o.Ranged[2] = Built(SiegeDrill.Trebuchet);
            SiegeDrill.Act full = SiegeDrill.Attacker(o, rule, false, true, 2, out roll);
            bool mass = three.Kind == SiegeDrill.Kind.Build && three.Type == SiegeDrill.Trebuchet && !roll3
                        && roll4 && four.Kind == SiegeDrill.Kind.Deploy && four.Type == SiegeDrill.Trebuchet && four.Reserve == 0 && four.Slot == 0
                        && rebuild.Kind == SiegeDrill.Kind.Build && rebuild.Slot == 2 && rebuild.Type == SiegeDrill.Trebuchet && full.Kind == SiegeDrill.Kind.Hold;
            // after a load nothing is remembered: two finished trebuchets in place, or a wall touched, means rolled out; one fresh one with whole walls means massing
            SiegeDrill.SideView one = Camp(); one.Ranged[0] = Built(SiegeDrill.Trebuchet);
            bool looks = SiegeDrill.LooksRolledOut(o, false) && !SiegeDrill.LooksRolledOut(one, false) && SiegeDrill.LooksRolledOut(one, true);
            Check(mass && looks, "the siege drill builds until four trebuchets wait in reserve, then rolls them out together; out, it rebuilds a fallen one in its spot; after a load it tells massing from rolled out ("
                                 + three + "; " + four + " " + roll4 + "; " + rebuild + "; " + full + "; " + looks + ")");

            // rams and towers only; the trap of a half-built engine back from reserve; fewer trebuchets wanted
            SiegeDrill.SideView r = Camp();
            r.Melee[0] = Built(SiegeDrill.Tower); r.Melee[1] = Built(SiegeDrill.Tower); r.Melee[2] = Built(SiegeDrill.Ram);
            bool rams = SiegeDrill.Attacker(r, new DrillRule { Mode = 2 }, false, false, 0, out roll).Kind == SiegeDrill.Kind.Hold;
            SiegeDrill.SideView trap = Camp();
            trap.Ranged[1] = new SiegeDrill.Slot { Type = SiegeDrill.Trebuchet, Built = false, Redeploying = true };
            SiegeDrill.Act scrap = SiegeDrill.Attacker(trap, rule, false, false, 0, out roll);
            SiegeDrill.SideView two = Camp();
            two.Melee[0] = Built(SiegeDrill.Tower); two.Melee[1] = Built(SiegeDrill.Tower); two.Melee[2] = Built(SiegeDrill.Ram);
            two.Reserve.Add(SiegeDrill.Trebuchet); two.ReserveBuilt.Add(true); two.Reserve.Add(SiegeDrill.Trebuchet); two.ReserveBuilt.Add(true);
            bool fewer = SiegeDrill.Attacker(two, new DrillRule { Trebuchets = 2 }, false, false, 0, out bool rollTwo).Kind == SiegeDrill.Kind.Deploy && rollTwo;
            Check(rams && scrap.Kind == SiegeDrill.Kind.Scrap && scrap.Slot == 1 && scrap.Ranged && fewer,
                  "rams and towers only builds no throwing engine; a half-built engine caught as redeploying is scrapped (the game would never finish it); two trebuchets roll out when two are wanted ("
                  + rams + "; " + scrap + "; " + fewer + ")");

            // against the player in person: no swap while his engines shoot; then one trebuchet at a time to reserve, a fire onager in its spot, until four
            SiegeDrill.SideView f = Camp();
            f.Melee[0] = Built(SiegeDrill.Tower); f.Melee[1] = Built(SiegeDrill.Tower); f.Melee[2] = Built(SiegeDrill.Ram);
            for (int i = 0; i < 4; i++) f.Ranged[i] = Built(SiegeDrill.Trebuchet);
            SiegeDrill.Act shooting = SiegeDrill.Attacker(f, rule, true, true, 2, out roll);
            SiegeDrill.Act giveUp = SiegeDrill.Attacker(f, rule, true, true, 0, out roll);
            f.Ranged[0] = default(SiegeDrill.Slot);
            SiegeDrill.Act fire1 = SiegeDrill.Attacker(f, rule, true, true, 0, out roll);
            f.Ranged[0] = Half(SiegeDrill.FireOnager);
            SiegeDrill.Act building = SiegeDrill.Attacker(f, rule, true, true, 0, out roll);
            f.Ranged[0] = Built(SiegeDrill.FireOnager);
            SiegeDrill.Act next = SiegeDrill.Attacker(f, rule, true, true, 1, out roll);   // his new engine does not stop a swap begun
            for (int i = 0; i < 4; i++) f.Ranged[i] = Built(SiegeDrill.FireOnager);
            SiegeDrill.Act done = SiegeDrill.Attacker(f, rule, true, true, 0, out roll);
            SiegeDrill.Act noFire = SiegeDrill.Attacker(o, new DrillRule { Fire = false }, true, true, 0, out roll);
            bool swap = shooting.Kind == SiegeDrill.Kind.Hold && giveUp.Kind == SiegeDrill.Kind.ToReserve && giveUp.Type == SiegeDrill.Trebuchet
                        && fire1.Kind == SiegeDrill.Kind.Build && fire1.Type == SiegeDrill.FireOnager && fire1.Slot == 0 && building.Kind == SiegeDrill.Kind.Hold
                        && next.Kind == SiegeDrill.Kind.ToReserve && next.Slot == 1 && done.Kind == SiegeDrill.Kind.Hold && noFire.Kind == SiegeDrill.Kind.Hold;
            // defenders against the player's own siege: fire catapults in every free spot, from the reserve first
            var d = new SiegeDrill.SideView(); d.Clear(0);
            SiegeDrill.Act d1 = SiegeDrill.Defender(d);
            d.Ranged[0] = Half(SiegeDrill.FireCatapult);
            SiegeDrill.Act d2 = SiegeDrill.Defender(d);
            d.Ranged[0] = Built(SiegeDrill.FireCatapult); d.Reserve.Add(SiegeDrill.FireCatapult); d.ReserveBuilt.Add(true);
            SiegeDrill.Act d3 = SiegeDrill.Defender(d);
            bool defend = d1.Kind == SiegeDrill.Kind.Build && d1.Type == SiegeDrill.FireCatapult && d1.Slot == 0 && d2.Kind == SiegeDrill.Kind.Hold
                          && d3.Kind == SiegeDrill.Kind.Deploy && d3.Slot == 1 && d3.Reserve == 0;
            Check(swap && defend, "against the player in person the trebuchets give way one at a time to fire onagers once his engines are silenced (three always firing), until four stand; "
                                  + "defenders against his siege build fire catapults in every spot (" + shooting + "; " + giveUp + "; " + fire1 + "; " + next + "; " + done + "; " + d1 + "; " + d3 + ")");

            // ---------- the clock: Torech Emel, walls level 1, a camp of 538 (the copy of Nov 16), defenders with the game's prepare-against-assault list ----------
            SiegeRow Torech(string drill, bool playerInside = false)
            {
                var g = new SiegeRow
                {
                    Settlement = "castle_M3", Besieger = "p_naz", BesiegerFaction = "dolguldur", Strategy = "siege_strategy_prepare_assault", StartHours = 1000 - 5,
                    Prep = 0.17f, PrepRate = 0.0348f, WallLevel = 1, Advantage = 6.0f, Defenders = 592, Attackers = 1023, AttackerMen = 538, DefenderMen = 419,
                    Food = 378.6f, FoodChange = -34.5f, GarrisonRegulars = 250, GarrisonPower = 500, DefStrategy = "siege_strategy_prepare_against_assault",
                    Drill = drill, DrillTrebuchets = 4, DrillFire = true, PlayerInside = playerInside, WallSectionHp = 15000
                };
                g.WallSections.Add(1f); g.WallSections.Add(1f);
                g.Rates["ram"] = 0.13933f; g.Rates["siege_tower_level1"] = 0.1045f; g.Rates["ballista"] = 0.209f; g.Rates["onager"] = 0.0836f; g.Rates["trebuchet"] = 0.06967f; g.Rates["fire_onager"] = 0.0836f;
                g.DefRates["catapult"] = 0.0533f; g.DefRates["fire_catapult"] = 0.0533f; g.DefRates["ballista"] = 0.1066f; g.DefRates["fire_ballista"] = 0.1066f;
                return g;
            }
            Forecast.SiegeClock c = Forecast.Siege(Torech("drill"), 1000);
            Forecast.SiegeClock walls = Forecast.Siege(Torech("drill"), 1000, new StormRule { Mode = 3 });
            Forecast.SiegeClock game = Forecast.Siege(Torech("game"), 1000);
            Forecast.SiegeClock ramsOnly = Forecast.Siege(Torech("rams"), 1000);
            double expectRoll = (1 - 0.17) / 0.0348 + 2 / 0.1045 + 1 / 0.13933 + 4 / 0.06967;   // about 107.6 hours
            double advDown = 2.0 / (1 + 0.25 + 0.16 + 0.24 + 4 * 0.08) * 1.2;
            bool clock = Math.Abs((c.RolledOutAt - 1000) - expectRoll) < 1.5 && c.SilencedAt > c.RolledOutAt && c.WallsDownAt > c.SilencedAt
                         && c.WallsDownAt - c.RolledOutAt > 15 && c.WallsDownAt - c.RolledOutAt < 50 && Math.Abs(c.AdvDown - advDown) < 0.02
                         && Math.Abs(walls.RuleStormAt - walls.WallsDownAt) <= 0.25 && double.IsNaN(ramsOnly.WallsDownAt) && (double.IsNaN(game.WallsDownAt) || game.WallsDownAt > c.WallsDownAt);
            Check(clock, "the siege clock runs the drill at Torech Emel: the trebuchets roll out after the preparations, both towers, the ram and four trebuchets one at a time (about "
                         + Math.Round(expectRoll) + " hours), silence the defenders' engines, then bring the walls down (worth " + Math.Round(advDown, 2) + " then); storm 3 storms then; rams only never breaks them "
                         + "(rolled out +" + Math.Round(c.RolledOutAt - 1000, 1) + " h, silenced +" + Math.Round(c.SilencedAt - 1000, 1) + ", walls down +" + Math.Round(c.WallsDownAt - 1000, 1)
                         + ", worth " + Math.Round(c.AdvDown, 2) + ", rule storm +" + Math.Round(walls.RuleStormAt - 1000, 1) + "; the game's own strategy: walls down +" + Math.Round(game.WallsDownAt - 1000, 1) + ")");

            // against the player in person: four fire onagers after his engines are silenced; storm 3 waits for them too
            Forecast.SiegeClock fc = Forecast.Siege(Torech("drill", true), 1000, new StormRule { Mode = 3 });
            bool fireClock = fc.FireCase && !double.IsNaN(fc.SwapDoneAt) && fc.SwapDoneAt > fc.SilencedAt && fc.RuleStormAt >= Math.Max(fc.WallsDownAt, fc.SwapDoneAt) - 0.25;
            Check(fireClock, "against the player in person the clock gives the fire onagers their days, and storm 3 waits for both walls and four fire onagers (silenced +" + Math.Round(fc.SilencedAt - 1000, 1)
                             + ", four fire onagers +" + Math.Round(fc.SwapDoneAt - 1000, 1) + ", walls down +" + Math.Round(fc.WallsDownAt - 1000, 1) + ", storm +" + Math.Round(fc.RuleStormAt - 1000, 1) + ")");

            // the words
            SiegeRow massing = Torech("drill");
            massing.Prep = 1;
            massing.Engines.Add(new EngineRow { Type = SiegeDrill.Tower, Progress = 1, Hitpoints = 5000, Slot = 0 });
            massing.Engines.Add(new EngineRow { Type = SiegeDrill.Tower, Progress = 1, Hitpoints = 5000, Slot = 1 });
            massing.Engines.Add(new EngineRow { Type = SiegeDrill.Ram, Progress = 1, Hitpoints = 5000, Slot = 2 });
            massing.Engines.Add(new EngineRow { Type = SiegeDrill.Trebuchet, Progress = 1, Hitpoints = 3200, Reserve = true, Ranged = true, Redeploy = 0 });
            massing.Engines.Add(new EngineRow { Type = SiegeDrill.Trebuchet, Progress = 0.5f, Hitpoints = 3200, Slot = 0, Ranged = true });
            massing.DefEngines.Add(new EngineRow { Type = "fire_catapult", Progress = 1, Hitpoints = 1200, Slot = 1, Ranged = true });
            massing.DefEngines.Add(new EngineRow { Type = "catapult", Progress = 1, Hitpoints = 1200, Slot = 2, Ranged = true });
            string words = Forecast.WallsText(massing, Forecast.Siege(massing, 1000));
            SiegeRow broken = Torech("game"); broken.WallSections[0] = 0.4f; broken.WallSections[1] = 0f;
            string brokenWords = Forecast.WallsText(broken, Forecast.Siege(broken, 1000));
            bool said = words.Contains("The walls (walls level 1): both stretches whole") && words.Contains("1 trebuchet being built; 1 trebuchet in reserve")
                        && words.Contains("the defenders': 1 fire catapult, 1 catapult in place") && words.Contains("trebuchets held in reserve until 4 are ready (1 now); they roll out together about")
                        && words.Contains("the walls down about") && brokenWords.Contains("one stretch 40% standing, the other down (both must be down before they count less in a storm)")
                        && brokenWords.Contains("The camp builds by the game's own strategy");
            Check(said, "the report's siege line gives the walls by stretch, both sides' throwing engines, and the drill's roll-out and walls-down hours (" + words + " | " + brokenWords + ")");

            // the storm rule and besiege's settings
            string ruleWords = new StormRule { Mode = 3 }.ToString();
            ToolDef besiege = Library.Get("besiege");
            bool settings = besiege.Settings["storm"].Default == 3 && besiege.Settings["storm"].Max == 3 && besiege.Settings["engines"].Default == 1 && besiege.Settings["engines"].Max == 2
                            && besiege.Settings["trebuchets"].Default == 4 && besiege.Settings["fire"].Default == 1 && ruleWords.StartsWith("storm once the walls are down");
            Check(settings, "besiege storms once the walls are down by default (storm 3) and builds by the siege drill (engines 1, trebuchets 4, fire 1) (" + ruleWords + ")");
        }
    }
}
