using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.19.3 (Nov 13: Dol Guldur kept three quarters of its field strength at castles where it saw no enemy army, and weighed only its western lords
        /// against my army): YOUR FRONTS groups the realm's castles and towns into fronts, counts its lords at the front nearest them, sets beside each the
        /// enemy lords and armies in view or seen lately near it and the realms bordering it with their whole strength and whether enemy or ally, and says
        /// where each enemy realm's strength has been seen; and the gift order's text says any lord with gold can give.
        /// </summary>
        private static void FrontsTests()
        {
            double hours = 52000;
            Snapshot s = ToolWorld(hours,
                new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 900, Strength = 1500, Speed = 5, FoodDays = 10, X = 110, Y = 100, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_azog", Leader = "lord_azog", Name = "Azog", Faction = "orcs", Kind = "lord", Men = 500, Strength = 900, Speed = 5, FoodDays = 10, X = 410, Y = 110, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_ugluk", Leader = "lord_ugluk", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 300, Strength = 600, Speed = 5, FoodDays = 10, X = 425, Y = 128, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_aragorn", Leader = "lord_aragorn", Name = "Aragorn's army", Faction = "player_k", Kind = "lord", Men = 3000, ArmyMen = 3000, ArmyStrength = 5000, Strength = 2000, Speed = 4, X = 160, Y = 104, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_far_men", Leader = "lord_far_men", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 600, Strength = 1200, Speed = 4, X = 900, Y = 900, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_gob", Leader = "lord_gob", Name = "Great Goblin", Faction = "goblins", Kind = "lord", Men = 700, Strength = 1100, Speed = 4, X = 470, Y = 120, Doing = "Hold", Spottable = true });
            // a second front of the orcs' far to the east, an ally beside it, and the enemy's own castle near it
            s.Factions["goblins"] = new FactionRow { Id = "goblins", Name = "Goblins of Blue Craig", Ruler = "Great Goblin", Lords = 12 };
            s.FactionNames["goblins"] = "Goblins of Blue Craig";
            s.Factions["orcs"].Allies.Add("goblins");
            s.Factions["player_k"].Lords = 203;
            Add(s, new SettlementRow { Id = "castle_far", Name = "Far Hold", Kind = "castle", Faction = "orcs", X = 400, Y = 100, Garrison = 300, GarrisonStrength = 400, Militia = 60, MilitiaStrength = 60, WallLevel = 1 });
            Add(s, new SettlementRow { Id = "town_far", Name = "Gundabad Gate", Kind = "town", Faction = "orcs", X = 420, Y = 130, Garrison = 500, GarrisonStrength = 700, Militia = 100, MilitiaStrength = 100, WallLevel = 2 });
            Add(s, new SettlementRow { Id = "castle_gob", Name = "Blue Craig", Kind = "castle", Faction = "goblins", X = 460, Y = 120, Garrison = 300 });
            // the far enemy lord is out of sight: the test world sees every other realm's party, so take him and the goblin out of its sight
            s.WatchedSees.Remove("p_far_men");
            var mem = new Brain.Memory();
            // an enemy lord seen ten hours ago near the far front, out of sight now; one seen five days ago, too long ago to count
            mem.LastSeen["p_elladan"] = new ViewParty { Id = "p_elladan", Lord = "lord_elladan", Name = "Elladan", Faction = "player_k", FactionName = "Kingdom of Arnor and Gondor", Relation = "enemy", Kind = "lord",
                                                        Men = 400, Strength = 800, X = 380, Y = 100, SeenHours = hours - 10, Doing = "holding" };
            mem.LastSeen["p_old"] = new ViewParty { Id = "p_old", Lord = "lord_old", Name = "Elrohir", Faction = "player_k", FactionName = "Kingdom of Arnor and Gondor", Relation = "enemy", Kind = "lord",
                                                    Men = 400, Strength = 700, X = 120, Y = 100, SeenHours = hours - 120, Doing = "holding" };
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, mem);
            string report = Views.Report(v, null);
            string section = report.Contains("YOUR FRONTS (2)") ? report.Substring(report.IndexOf("YOUR FRONTS (2)", StringComparison.Ordinal)) : "";
            section = section.Contains("\r\n\r\n") ? section.Substring(0, section.IndexOf("\r\n\r\n", StringComparison.Ordinal)) : section;
            string west = v.Fronts.FirstOrDefault(l => l.Contains("[castle_orc]")) ?? "", east = v.Fronts.FirstOrDefault(l => l.Contains("[town_far]")) ?? "";
            bool two = v.Fronts.Count(l => l.Contains("]. Your lords: ")) == 2 && west != east && !west.Contains("[town_far]") && east.Contains("[castle_far]");
            bool named = east.StartsWith("The front of Gundabad Gate: Gundabad Gate [town_far], Far Hold [castle_far].", StringComparison.Ordinal) && west.StartsWith("The front of ", StringComparison.Ordinal);
            bool lords = west.Contains("Your lords: 1 lord, strength 1,500") && east.Contains("Your lords: 2 lords, strength 1,500") && east.Contains("garrisons and militia strength 1,260");
            bool seen = west.Contains("in view 1, strength 5,000 (Kingdom of Arnor and Gondor 5,000)") && east.Contains("seen 10 hours ago and out of sight now 1, strength 800")
                        && !west.Contains("Elrohir") && !report.Contains("700 near");
            bool border = west.Contains("Bordering realms: Kingdom of Arnor and Gondor (enemy; 203 lords; strength 3,200): 1 castle or town within two days' march, the nearest Fornost")
                          && east.Contains("Goblins of Blue Craig (ally; 12 lords; strength 1,100): 1 castle or town within two days' march, the nearest Blue Craig") && !report.Contains("[castle_gob]");
            bool first = v.Fronts.Count > 0 && v.Fronts[0].StartsWith("Your lords in the field by front: the front of Orc", StringComparison.Ordinal) && v.Fronts[0].Contains("(50%)");
            bool gathered = v.Fronts.Any(l => l.StartsWith("Kingdom of Arnor and Gondor (strength 3,200 in all): 5,000 near the front of Orc", StringComparison.Ordinal) && l.Contains(", 800 near the front of Gundabad Gate"));
            // the fog check on the section itself (the test world names its ally by id elsewhere, where the game's ids are known to the realm)
            List<string> fog = Audit.Fog(v, section, s.WatchedSees, s.InspectedByWatched);
            Check(two && named && lords && seen && border && first && gathered && fog.Count == 0 && section.Contains(Views.FrontsNote),
                  "YOUR FRONTS: the castles and towns in fronts two days' march across, the lords counted at the front nearest them, the enemy in view and seen lately near each, the bordering realms "
                  + "with their whole strength and whether enemy or ally, and where each enemy realm's strength was seen, with no id the realm should not know ("
                  + two + ", " + named + ", " + lords + ", " + seen + ", " + border + ", " + first + ", " + gathered + ", fog " + string.Join("; ", fog) + "): " + string.Join(" | ", v.Fronts));

            // a realm with no castle or town has no fronts
            Snapshot bare = ToolWorld(hours);
            bare.Settlements.RemoveAll(h => h.Faction == "orcs");
            View none = Views.Build(bare, "orcs", bare.WatchedSees, bare.HostileToWatched, null);
            Check(none.Fronts.Count == 0 && !Views.Report(none, null).Contains("YOUR FRONTS"), "a realm with no castle or town has no YOUR FRONTS");

            // the gift order: any lord with gold gives, party or not (Dol Guldur read "the giver may lead no party" as "must lead none")
            string library = BannerlordAgents.Tools.Library.Text();
            Check(library.Contains("any lord with gold can give, whether or not he leads a party") && !library.Contains("may lead no party"),
                  "the gift order's text says any lord with gold can give, whether or not he leads a party");
        }
    }
}
