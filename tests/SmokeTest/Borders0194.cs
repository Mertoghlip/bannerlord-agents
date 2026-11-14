using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAgents;

namespace SmokeTest
{
    internal static partial class Program
    {
        /// <summary>
        /// 0.19.4 (Nov 13; it replaces 0.19.3's fronts): YOUR BORDERS names each enemy realm, strongest first, with its whole strength, whether it borders the
        /// realm and which of the realm's castles and towns face it, and what of it was seen near the realm's lands now and lately; then the allies and realms
        /// at peace that border the realm, and the realm's castles and towns that face no enemy (its heartland and its borders with friends). And the gift
        /// order's text says any lord with gold can give (0.19.3).
        /// </summary>
        private static void BordersTests()
        {
            double hours = 52000;
            Snapshot s = ToolWorld(hours,
                new PartyRow { Id = "p_bolg", Leader = "lord_bolg", Name = "Bolg", Faction = "orcs", Kind = "lord", Men = 900, Strength = 1500, Speed = 5, FoodDays = 10, X = 110, Y = 100, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_azog", Leader = "lord_azog", Name = "Azog", Faction = "orcs", Kind = "lord", Men = 500, Strength = 900, Speed = 5, FoodDays = 10, X = 410, Y = 110, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_ugluk", Leader = "lord_ugluk", Name = "Ugluk", Faction = "orcs", Kind = "lord", Men = 300, Strength = 600, Speed = 5, FoodDays = 10, X = 425, Y = 128, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_aragorn", Leader = "lord_aragorn", Name = "Aragorn's army", Faction = "player_k", Kind = "lord", Men = 3000, ArmyMen = 3000, ArmyStrength = 5000, Strength = 2000, Speed = 4, X = 160, Y = 104, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_far_men", Leader = "lord_far_men", Name = "Halbarad", Faction = "player_k", Kind = "lord", Men = 600, Strength = 1200, Speed = 4, X = 900, Y = 900, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_eomer", Leader = "lord_eomer", Name = "Eomer", Faction = "rohan", Kind = "lord", Men = 300, Strength = 600, Speed = 4, X = 950, Y = 950, Doing = "Hold", Spottable = true },
                new PartyRow { Id = "p_gob", Leader = "lord_gob", Name = "Great Goblin", Faction = "goblins", Kind = "lord", Men = 700, Strength = 1100, Speed = 4, X = 470, Y = 120, Doing = "Hold", Spottable = true });
            // a second region of the orcs' far to the east with an ally beside it; a second enemy far off that borders no one of theirs
            s.Factions["goblins"] = new FactionRow { Id = "goblins", Name = "Goblins of Blue Craig", Ruler = "Great Goblin", Lords = 12 };
            s.FactionNames["goblins"] = "Goblins of Blue Craig";
            s.Factions["rohan"] = new FactionRow { Id = "rohan", Name = "Rohan", Ruler = "Theoden", AtWar = { "orcs" } };
            s.FactionNames["rohan"] = "Rohan";
            s.Factions["orcs"].Allies.Add("goblins");
            s.Factions["orcs"].AtWar.Add("rohan");
            s.HostileToWatched.Add("rohan");
            s.Factions["player_k"].Lords = 203;
            Add(s, new SettlementRow { Id = "castle_far", Name = "Far Hold", Kind = "castle", Faction = "orcs", X = 400, Y = 100, Garrison = 300, GarrisonStrength = 400, Militia = 60, MilitiaStrength = 60, WallLevel = 1 });
            Add(s, new SettlementRow { Id = "town_far", Name = "Gundabad Gate", Kind = "town", Faction = "orcs", X = 420, Y = 130, Garrison = 500, GarrisonStrength = 700, Militia = 100, MilitiaStrength = 100, WallLevel = 2 });
            Add(s, new SettlementRow { Id = "castle_gob", Name = "Blue Craig", Kind = "castle", Faction = "goblins", X = 460, Y = 120, Garrison = 300 });
            Add(s, new SettlementRow { Id = "town_edoras", Name = "Edoras", Kind = "town", Faction = "rohan", X = 900, Y = 900, Garrison = 300 });
            // the far lords are out of sight: the test world sees every other realm's party
            s.WatchedSees.Remove("p_far_men");
            s.WatchedSees.Remove("p_eomer");
            var mem = new Brain.Memory();
            // an enemy lord seen ten hours ago near the eastern castles, out of sight now; one seen five days ago, too long ago to count
            mem.LastSeen["p_elladan"] = new ViewParty { Id = "p_elladan", Lord = "lord_elladan", Name = "Elladan", Faction = "player_k", FactionName = "Kingdom of Arnor and Gondor", Relation = "enemy", Kind = "lord",
                                                        Men = 400, Strength = 800, X = 380, Y = 100, SeenHours = hours - 10, Doing = "holding" };
            mem.LastSeen["p_old"] = new ViewParty { Id = "p_old", Lord = "lord_old", Name = "Elrohir", Faction = "player_k", FactionName = "Kingdom of Arnor and Gondor", Relation = "enemy", Kind = "lord",
                                                    Men = 400, Strength = 700, X = 120, Y = 100, SeenHours = hours - 120, Doing = "holding" };
            View v = Views.Build(s, "orcs", s.WatchedSees, s.HostileToWatched, mem);
            string report = Views.Report(v, null);
            int at = report.IndexOf("YOUR BORDERS", StringComparison.Ordinal);
            string section = at >= 0 ? report.Substring(at) : "";
            section = section.Contains("\r\n\r\n") ? section.Substring(0, section.IndexOf("\r\n\r\n", StringComparison.Ordinal)) : section;
            string arnor = v.Borders.FirstOrDefault(l => l.StartsWith("Kingdom of Arnor and Gondor (enemy", StringComparison.Ordinal)) ?? "";
            string rohan = v.Borders.FirstOrDefault(l => l.StartsWith("Rohan (enemy", StringComparison.Ordinal)) ?? "";
            bool first = v.Borders.Count > 0 && v.Borders[0] == "Your lords in the field: strength 3,000. Your enemies' in all: Kingdom of Arnor and Gondor 3,200, Rohan 600.";
            bool borders = arnor.StartsWith("Kingdom of Arnor and Gondor (enemy; 203 lords, strength 3,200): it borders you: 1 castle or town of its lies within two days' march of yours, the nearest Fornost, about 0.6 days from your Orc Keep. "
                                            + "Your castles and towns facing it: Orc Castle [castle_orc], Orc Keep [castle_two].", StringComparison.Ordinal);
            bool seen = arnor.EndsWith("Of its strength, seen near your lands: 5,000 in view now, 800 more seen in the last three days.", StringComparison.Ordinal);
            bool far = rohan.StartsWith("Rohan (enemy; strength 600): it does not border you: its nearest castle or town, Edoras, is about", StringComparison.Ordinal)
                       && rohan.EndsWith("from your Gundabad Gate. None of its strength seen near your lands in the last three days.", StringComparison.Ordinal);
            bool order = v.Borders.IndexOf(arnor) == 1 && v.Borders.IndexOf(rohan) == 2;
            bool friends = v.Borders.Contains("Your allies that border you: Goblins of Blue Craig (strength 1,100).") && !v.Borders.Any(l => l.StartsWith("Realms at peace", StringComparison.Ordinal));
            bool safe = v.Borders.Contains("Your castles and towns with no enemy castle or town within two days' march (your heartland, and your borders with allies and realms at peace): Far Hold [castle_far], Gundabad Gate [town_far].");
            bool placed = at > report.IndexOf("ENEMY REALMS, AS THEIR ENCYCLOPEDIA PAGES SHOW ANY RULER", StringComparison.Ordinal) && section.Contains(Views.BordersNote) && !report.Contains("YOUR FRONTS");
            // the fog check on the section itself (the test world names its ally by id elsewhere, where the game's ids are known to the realm)
            List<string> fog = Audit.Fog(v, section, s.WatchedSees, s.InspectedByWatched);
            Check(first && borders && seen && far && order && friends && safe && placed && fog.Count == 0 && !section.Contains("[castle_gob]") && !section.Contains("[town_edoras]") && !section.Contains("Elrohir"),
                  "YOUR BORDERS: the enemy realms strongest first with their whole strength, whether each borders the realm and which castles and towns face it, what of each was seen near the realm's "
                  + "lands now and lately, the allies that border it, and the castles and towns facing no enemy, with no id the realm should not know ("
                  + first + ", " + borders + ", " + seen + ", " + far + ", " + order + ", " + friends + ", " + safe + ", " + placed + ", fog " + string.Join("; ", fog) + "): " + string.Join(" | ", v.Borders));

            // a realm with no castle or town has no borders
            Snapshot bare = ToolWorld(hours);
            bare.Settlements.RemoveAll(h => h.Faction == "orcs");
            View none = Views.Build(bare, "orcs", bare.WatchedSees, bare.HostileToWatched, null);
            Check(none.Borders.Count == 0 && !Views.Report(none, null).Contains("YOUR BORDERS"), "a realm with no castle or town has no YOUR BORDERS");

            // the gift order: any lord with gold gives, party or not (Dol Guldur read "the giver may lead no party" as "must lead none")
            string library = BannerlordAgents.Tools.Library.Text();
            Check(library.Contains("any lord with gold can give, whether or not he leads a party") && !library.Contains("may lead no party"),
                  "the gift order's text says any lord with gold can give, whether or not he leads a party");
        }
    }
}
