using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BannerlordAgents.Tools
{
    /// <summary>
    /// A spot on the map that is no holding, named by where it is: "p673_994" (map units, east then north). The "ask the way"
    /// tool's answers name spots on a way (its crossings, and points every so often along it), and a watch can be posted on
    /// one (0.7.1). A spot is plain geography, so naming one tells the agent nothing its realm could not know. Game-free.
    /// </summary>
    internal static class Spots
    {
        private static readonly Regex Pattern = new Regex(@"^\[?p(\d{1,5})_(\d{1,5})\]?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryParse(string id, out float x, out float y)
        {
            x = y = 0;
            if (string.IsNullOrWhiteSpace(id)) return false;
            Match m = Pattern.Match(id.Trim());
            if (!m.Success) return false;
            x = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            y = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return true;
        }

        public static bool Is(string id) => TryParse(id, out _, out _);

        public static string Id(float x, float y) => "p" + ((int)Math.Round(x)).ToString(CultureInfo.InvariantCulture) + "_" + ((int)Math.Round(y)).ToString(CultureInfo.InvariantCulture);

        /// <summary>"the spot 4 west of Western Moria".</summary>
        public static string Describe(Snapshot s, float x, float y)
        {
            float d = 0;
            SettlementRow near = s == null ? null : Views.NearestSettlement(s, x, y, out d);
            if (near == null) return "the spot " + Id(x, y);
            if (d < 1.5f) return "the spot at " + near.Name;
            return "the spot " + Math.Round(d).ToString("0", CultureInfo.InvariantCulture) + " " + Compass(near.X, near.Y, x, y) + " of " + near.Name;
        }

        /// <summary>Which way a point lies from another: map y grows to the north, x to the east.</summary>
        public static string Compass(float fromX, float fromY, float x, float y)
        {
            double a = Math.Atan2(y - fromY, x - fromX) * 180 / Math.PI;
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            return names[(int)Math.Round((((a % 360) + 360) % 360) / 45.0) % 8];
        }
    }
}
