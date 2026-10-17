using System.Text.Json.Nodes;

namespace BannerlordAgents.Routes
{
    /// <summary>The game's calendar as the bridge read it (live\current.json, "calendar"), to turn in-game hours into the dates the player sees.</summary>
    public sealed class GameCalendar
    {
        public int HoursInDay = 24, DaysInSeason = 21, SeasonsInYear = 4;
        public string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        public static GameCalendar From(JsonNode n)
        {
            var c = new GameCalendar();
            if (n is not JsonObject o) return c;
            if (o["hours_in_day"] is JsonValue a && a.TryGetValue(out int hd) && hd > 0) c.HoursInDay = hd;
            if (o["days_in_season"] is JsonValue b && b.TryGetValue(out int ds) && ds > 0) c.DaysInSeason = ds;
            if (o["seasons_in_year"] is JsonValue d && d.TryGetValue(out int sy) && sy > 0) c.SeasonsInYear = sy;
            if (o["season_names"] is JsonArray names && names.Count > 0) c.SeasonNames = names.Select(x => (string)x ?? "?").ToArray();
            return c;
        }

        public void Parts(double hours, out int season, out int day, out long year, out int hour)
        {
            long h = (long)Math.Floor(hours);
            long days = h / HoursInDay;
            hour = (int)(h % HoursInDay);
            int daysInYear = DaysInSeason * SeasonsInYear;
            year = days / daysInYear;
            int dayOfYear = (int)(days % daysInYear);
            season = dayOfYear / DaysInSeason;
            day = dayOfYear % DaysInSeason;
        }

        /// <summary>"Summer 13, 1087, 15:00".</summary>
        public string When(double hours)
        {
            if (double.IsNaN(hours)) return "?";
            Parts(hours, out int season, out int day, out long year, out int hour);
            return Season(season) + " " + (day + 1) + ", " + year + ", " + hour.ToString("00") + ":00";
        }

        /// <summary>"Summer 13, 15:00".</summary>
        public string WhenShort(double hours)
        {
            if (double.IsNaN(hours)) return "?";
            Parts(hours, out int season, out int day, out _, out int hour);
            return Season(season) + " " + (day + 1) + ", " + hour.ToString("00") + ":00";
        }

        private string Season(int s) => s < SeasonNames.Length ? SeasonNames[s] : "Season " + (s + 1);
    }
}
