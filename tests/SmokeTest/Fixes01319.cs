using System;
using System.Linq;
using BannerlordAgents;
using BannerlordAgents.Tools;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.13.19 (Nov 1): the supply order (castles at my fronts with zero food due to raids on their villages), the garrison floor on
        /// every order (take troops from those garrisons as they recruit up until the strength of the garrison falls to a floor set by the
        /// agent), and what the report shows for them: each own castle's and town's garrison strength and food, never an enemy's.
        /// </summary>
        private static void SupplyAndGarrisonTests()
        {
            Snapshot s = ToolWorld(43700);
            SettlementRow own = s.SettlementById["castle_orc"], foe = s.SettlementById["castle_men"];
            own.Food = 64; own.FoodCap = 450; own.FoodPerDay = 31.4f;
            foe.Food = 200; foe.FoodCap = 450; foe.FoodPerDay = 40;
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, null);
            string report = Views.Report(v, null);
            bool food = report.Contains("Orc Castle [castle_orc] (castle): garrison 450 (strength 600), militia 120; food 64 of 450, about 2 days under siege (it eats 32 a day)");
            bool notFoe = v.Frontier.All(h => h.Food < 0 && h.GarrisonStrength < 0) && !report.Contains("food 200 of 450");
            Check(food && notFoe, "the report shows each of the realm's castles and towns' garrison strength and food (how long it lasts a siege), and no enemy's (" + food + ", " + notFoe + ")");

            ToolDef supply = Library.All.TryGetValue("supply", out ToolDef sd) ? sd : null;
            string library = Library.Text();
            bool everyTool = Library.All.Values.All(d => d.Settings.ContainsKey("garrison_floor") && d.Settings["garrison_floor"].Default == 0);
            bool once = library.Contains("And garrison_floor (default 0)") && !library.Contains("garrison_floor 0 (0 to");
            bool tool = supply != null && supply.Kind == "move" && supply.Settings.ContainsKey("days") && supply.Settings.ContainsKey("keep_days") && supply.Problems.Count == 0
                        && library.Contains("- **supply**") && ToolDef.Verbs.ContainsKey("supply");
            Check(everyTool && once && tool, "every order carries garrison_floor (0: takes none), described once in the library; the supply order is in it, with its days and keep_days ("
                  + everyTool + ", " + once + ", " + tool + ")");
        }
    }
}
