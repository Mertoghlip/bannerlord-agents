using System;
using System.Collections.Generic;

namespace BannerlordAgents.Common
{
    /// <summary>
    /// How fast a party marches, exactly as the rules in force work it out (read in the 1.4.8 code and TAOM 2.0.26 for 0.7.3,
    /// Mert, Oct 17: "We should be adjusting our prediction of the marching speed of a force based on the game's exact
    /// equations"). One file, built into the game module (net472) and the way-finder (net8), so the report, the facts and the
    /// "ask the way" tool all reckon a march the same way.
    ///
    /// The game's own sum (DefaultPartySpeedCalculatingModel; ExplainedNumber: result = base x (1 + sum of factors), every
    /// factor added into the one sum, limited to at least 1 when read):
    /// - the base: 4 x (200 / (200 + men))^0.4, every man of an army's parties pooled on its leader, wounded included, and an
    ///   overburdened party's penalty added to it (CalculateLandBaseSpeed); its factors: horses, cargo, herds, wounded,
    ///   prisoners, morale, perks. The hourly copy reads both from the game's own cache (the very numbers it moves the party
    ///   with), so whatever the base rules of the pack, they are exact;
    /// - the ground and the hour, added to the same sum (CalculateFinalSpeed): forest -0.3 (-0.15 with a Forest Kin scout and
    ///   three quarters on foot), water, rivers, under bridges, bridges and fords -0.3, desert and dunes -0.1, night (22:00 to
    ///   02:00) -0.25, snowfall -0.1; the scout's Pathfinder (+0.02 on plain and steppe), Desert Born (+0.05), Night Runner
    ///   (+0.05 by night), Day Traveler (+0.02 by day);
    /// - TAOM's peoples (TaomPartySpeedModel.CalculateFinalSpeed): each people's bonus for the ground the party stands on, or
    ///   for the night, added to the sum (read from the game's own feats at load: CultureSpeeds);
    /// - whatever else the game adds that these rules cannot name (a lord's career, a perk tied to chasing or to morale) is
    ///   measured from the party's speed now and carried as Other.
    /// A party covers its speed / 1.2 map units an in-game hour (Campaign.Tick: 4,320 seconds of map time per unit of dt,
    /// the party moving Speed x dt): 20 x speed a day.
    /// </summary>
    public static class GamePace
    {
        /// <summary>Map units covered in an in-game hour at a speed of 1: 3,600 / 4,320.</summary>
        public const float UnitsPerSpeedHour = 1f / 1.2f;
        public const float MinimumSpeed = 1f;
        public const int SunRise = 2, SunSet = 22, HoursInDay = 24;
        public const float NightFactor = -0.25f, SnowfallFactor = -0.1f;

        // the game's TerrainType numbers (TaleWorlds.Core.TerrainType, 1.4.8); the face group of the walking map is the same number
        public const int Plain = 1, Desert = 2, Snow = 3, Forest = 4, Steppe = 5, Fording = 6, Water = 10, River = 11, Swamp = 15, Dune = 16, Bridge = 17, UnderBridge = 25;

        // TAOM's ground kinds (TaomPartySpeedModel.MapTerrain) and the night, as indices into a people's bonuses
        public const int KindNone = 0, KindPlain = 1, KindForest = 2, KindSwamp = 3, KindSteppe = 4, KindDesert = 5, KindSnow = 6, KindNight = 7, Kinds = 8;

        /// <summary>The scout's perks that change with the ground or the hour, as flags.</summary>
        public const int PerkForestKin = 1, PerkPathfinder = 2, PerkDesertBorn = 4, PerkNightRunner = 8, PerkDayTraveler = 16;

        public static bool IsNight(double hours)
        {
            int h = (int)Math.Floor(((hours % HoursInDay) + HoursInDay) % HoursInDay);
            return !(h >= SunRise && h < SunSet);
        }

        /// <summary>TAOM's grouping of the ground for its peoples' bonuses: plain, desert (and dunes), snow, forest, steppe, swamp.</summary>
        public static int Kind(int terrain)
        {
            switch (terrain)
            {
                case Plain: return KindPlain;
                case Desert: case Dune: return KindDesert;
                case Snow: return KindSnow;
                case Forest: return KindForest;
                case Steppe: return KindSteppe;
                case Swamp: return KindSwamp;
                default: return KindNone;
            }
        }

        /// <summary>The game's own slowing for the ground (vanilla CalculateFinalSpeed), with the scout's ground perks.</summary>
        public static float GroundFactor(int terrain, int perks, float footShare, PerkValues pv)
        {
            pv = pv ?? PerkValues.Default;
            switch (terrain)
            {
                // the game: -0.3 x (0 - ForestKin.PrimaryBonus) with the perk and three quarters on foot (PrimaryBonus -0.5: -0.15)
                case Forest: return (perks & PerkForestKin) != 0 && footShare >= 0.75f ? 0.3f * pv.ForestKin : -0.3f;
                case Water: case River: case UnderBridge: case Bridge: case Fording: return -0.3f;
                case Desert: case Dune: return -0.1f + ((perks & PerkDesertBorn) != 0 ? pv.DesertBorn : 0f);
                case Plain: case Steppe: return (perks & PerkPathfinder) != 0 ? pv.Pathfinder : 0f;
                default: return 0f;
            }
        }

        /// <summary>Everything the ground and the hour add to the sum for this party: the game's slowing, its people's bonus, night, the scout's perks.</summary>
        public static float PlaceFactor(Pace p, int terrain, bool night, bool snowing = false)
        {
            float f = GroundFactor(terrain, p.Perks, p.FootShare, p.PerkValues);
            float[] feats = p.Feats;
            if (feats != null)
            {
                int k = Kind(terrain);
                if (k > 0 && k < feats.Length) f += feats[k];
                if (night && KindNight < feats.Length) f += feats[KindNight];
            }
            if (night) f += NightFactor + ((p.Perks & PerkNightRunner) != 0 ? (p.PerkValues ?? PerkValues.Default).NightRunner : 0f);
            else if ((p.Perks & PerkDayTraveler) != 0) f += (p.PerkValues ?? PerkValues.Default).DayTraveler;
            if (snowing) f += SnowfallFactor;
            return f;
        }

        /// <summary>The game's speed for this party on this ground at this hour: base x (1 + all factors), at least 1.</summary>
        public static float Speed(Pace p, int terrain, bool night, bool snowing = false)
        {
            if (p == null || p.Base <= 0) return 0f;
            return Math.Max(MinimumSpeed, p.Base * (1f + p.Factors + p.Other + PlaceFactor(p, terrain, night, snowing)));
        }

        /// <summary>Map units an in-game hour at that speed.</summary>
        public static float PerHour(float speed) => speed * UnitsPerSpeedHour;

        /// <summary>
        /// What the named rules leave unexplained in the party's speed now (careers, perks tied to chasing or morale): its
        /// speed now over its base, less one, its base factors and the place's factors. NaN when the speed now is the game's
        /// floor of 1 (nothing can be read from it) or unknown.
        /// </summary>
        public static float Residual(Pace p, float speedNow, int terrainNow, bool nightNow, bool snowingNow)
        {
            if (p == null || p.Base <= 0 || speedNow <= MinimumSpeed + 0.001f) return float.NaN;
            return speedNow / p.Base - 1f - p.Factors - PlaceFactor(p, terrainNow, nightNow, snowingNow);
        }

        /// <summary>
        /// Average map units an hour over a whole day on one kind of ground (night's 4 hours slower): for long ways where only
        /// the length is known (the game's road table), on open ground unless told otherwise.
        /// </summary>
        public static float DayAverage(Pace p, int terrain = Plain)
        {
            if (p == null || p.Base <= 0) return 0f;
            if (p.Flat > 0) return p.Flat;
            float day = Speed(p, terrain, false), night = Speed(p, terrain, true);
            int nightHours = HoursInDay - (SunSet - SunRise);
            return PerHour((day * (HoursInDay - nightHours) + night * nightHours) / HoursInDay);
        }

        /// <summary>
        /// Marches along a way (x, y points, the first where the party stands), the ground sampled every step, the hour and the
        /// night as it goes. Returns the hours it takes (NaN if no pace); arrive is the in-game hour of arrival. Stops at
        /// maxHours (then returns maxHours and reached = false). at receives (hours from start, x, y) every whole hour, for
        /// where it will be when.
        /// </summary>
        public static double March(IList<float[]> way, Func<float, float, int> terrainAt, Pace p, double startHours, out double arrive, out bool reached,
                                   double maxHours = 24 * 30, Action<double, float, float> at = null, float step = 0.25f)
        {
            arrive = startHours;
            reached = false;
            if (p == null || p.Base <= 0 || way == null || way.Count == 0) return double.NaN;
            double t = startHours;
            double nextMark = Math.Floor(startHours) + 1;
            for (int i = 1; i < way.Count; i++)
            {
                float[] a = way[i - 1], b = way[i];
                float dx = b[0] - a[0], dy = b[1] - a[1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) continue;
                for (float d = 0; d < len; d += step)
                {
                    float piece = Math.Min(step, len - d);
                    float x = a[0] + dx / len * (d + piece * 0.5f), y = a[1] + dy / len * (d + piece * 0.5f);
                    int g = terrainAt != null ? terrainAt(x, y) : Plain;
                    float units = p.Flat > 0 ? p.Flat : PerHour(Speed(p, g, IsNight(t)));
                    t += piece / Math.Max(0.05f, units);
                    while (at != null && t >= nextMark) { at(nextMark - startHours, x, y); nextMark += 1; }
                    if (t - startHours >= maxHours) { arrive = t; return maxHours; }
                }
            }
            arrive = t;
            reached = true;
            return t - startHours;
        }

        /// <summary>A way's length, point to point.</summary>
        public static double Length(IList<float[]> way)
        {
            double sum = 0;
            for (int i = 1; way != null && i < way.Count; i++)
            {
                float dx = way[i][0] - way[i - 1][0], dy = way[i][1] - way[i - 1][1];
                sum += Math.Sqrt(dx * dx + dy * dy);
            }
            return sum;
        }

        /// <summary>The plain-words account of how marching works, for the agent (the first "ask the way" answer, marching.txt).</summary>
        public static string Rules(IDictionary<string, string> peopleBonuses = null)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("HOW MARCHING WORKS (the game's own rules, as this game runs them)\n");
            sb.Append("- A party plans its whole way when it sets off and takes the shortest way over walkable ground (open land, forest, steppe, desert, snow, swamp, dunes, beach). "
                    + "Mountains, cliffs, canyons, lakes, rivers and the sea cannot be crossed; a river only at a bridge or a ford. It plans again whenever its destination moves "
                    + "(chasing a party) or it gets a new one. It takes the shortest way, not the fastest.\n");
            sb.Append("- Its speed is a base, set by how many men it has (an army counts all its parties' men together: 4 x (200 / (200 + men))^0.4, so about 3.4 for 100 men, "
                    + "2.3 for 600, 1.6 for 2,000), times 1 plus the sum of everything that speeds or slows it. Each of these is added into that one sum, not multiplied: "
                    + "horses speed it, wounded, prisoners, herds and heavy loads slow it, high morale speeds it a little; then the ground under it and the hour: forest -30%, "
                    + "fords, bridges and rivers -30%, desert and dunes -10%, night (22:00 to 02:00) -25%, snowfall -10%. Some scouts' skills lessen these.\n");
            if (peopleBonuses != null && peopleBonuses.Count > 0)
            {
                sb.Append("- Some peoples march better on their own ground, added into the same sum:");
                foreach (KeyValuePair<string, string> kv in peopleBonuses) sb.Append(" " + kv.Key + " " + kv.Value + ";");
                sb.Length--;
                sb.Append(".\n");
            }
            sb.Append("- The speed is not map units an hour: a party covers its speed divided by 1.2 each hour (20 times its speed a day). A speed of 2.4 covers 2 map units an hour.\n");
            sb.Append("- So a big army is slow, a short way through forest can take longer than a longer way round, and a march that runs through the night loses a quarter of those hours.\n");
            sb.Append("- Where only one bridge, ford or pass joins two lands, every way between them goes through it.\n");
            return sb.ToString();
        }
    }

    /// <summary>The scout perks' values in this game (DefaultPerks.Scouting, read at load; 1.4.8's own numbers until then).</summary>
    public sealed class PerkValues
    {
        public float ForestKin = -0.5f, Pathfinder = 0.02f, DesertBorn = 0.05f, NightRunner = 0.05f, DayTraveler = 0.02f;
        public static readonly PerkValues Default = new PerkValues();
    }

    /// <summary>One party's (or army's) march: the parts of the game's speed sum that belong to it.</summary>
    public sealed class Pace
    {
        /// <summary>The base number of its speed (4 x (200 / (200 + men))^0.4, plus an overload penalty).</summary>
        public float Base;
        /// <summary>The sum of its base factors (horses, cargo, herds, wounded, prisoners, morale, perks).</summary>
        public float Factors;
        /// <summary>What the named rules leave unexplained in its speed now, carried forward (0 when not measured).</summary>
        public float Other;
        /// <summary>Its people's bonuses by ground kind and night (GamePace.Kind...), or null.</summary>
        public float[] Feats;
        /// <summary>Its scout's ground and hour perks (GamePace.Perk... flags), and the share of its men on foot.</summary>
        public int Perks;
        public float FootShare = 1f;
        public PerkValues PerkValues;
        /// <summary>
        /// A pace known only as map units an hour, ground and night already in it (an enemy's trail seen over hours): marched
        /// as it is, with no ground or hour added. 0 for a pace known by its parts.
        /// </summary>
        public float Flat;

        /// <summary>A pace known only by the game's speed number on open ground by day (no factors known).</summary>
        public static Pace FromSpeed(float speed) => new Pace { Base = speed };

        /// <summary>A pace known only as map units an hour, all in.</summary>
        public static Pace FromUnitsPerHour(float units) => new Pace { Base = units / GamePace.UnitsPerSpeedHour, Flat = units };

        /// <summary>Its speed on open ground by day: the number the game's tooltip shows there.</summary>
        public float OpenSpeed => GamePace.Speed(this, GamePace.Plain, false);

        public override string ToString() => "base " + Base.ToString("0.000") + " factors " + Factors.ToString("0.000") + " other " + Other.ToString("0.000");
    }
}
