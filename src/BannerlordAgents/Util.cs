using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BannerlordAgents
{
    internal static class Text
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Lower case with the accents removed, so "Nazgûl" and "nazgul" match.</summary>
        public static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
        }

        public static string N(int n) => n.ToString("N0", Inv);
        public static string D(float d) => d.ToString("0.0", Inv);
        public static float R1(float v) => (float)Math.Round(v, 1);

        /// <summary>"Summer 3, 1085, 14:00" from in-game hours, in the game's own calendar.</summary>
        public static string When(double hours) => Calendar.When(hours);
    }

    /// <summary>
    /// The game's calendar, read once when a campaign loads (the time model sets it, and packs can change it), so
    /// the worker can turn in-game hours into the date the player sees on screen without touching the game.
    /// </summary>
    internal static class Calendar
    {
        public static int HoursInDay = 24, DaysInSeason = 21, SeasonsInYear = 4;
        public static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

        /// <summary>Game's thread, at session start.</summary>
        public static void Capture()
        {
            try
            {
                HoursInDay = Math.Max(1, (int)Math.Round(TaleWorlds.CampaignSystem.CampaignTime.Days(1f).ToHours));
                DaysInSeason = Math.Max(1, TaleWorlds.CampaignSystem.CampaignTime.DaysInSeason);
                SeasonsInYear = Math.Max(1, TaleWorlds.CampaignSystem.CampaignTime.DaysInYear / DaysInSeason);
            }
            catch { }
            for (int i = 0; i < SeasonNames.Length; i++)
            {
                try
                {
                    string name = TaleWorlds.Core.GameTexts.FindText("str_season_" + (TaleWorlds.CampaignSystem.CampaignTime.Seasons)i)?.ToString();
                    if (!string.IsNullOrEmpty(name)) SeasonNames[i] = name;
                }
                catch { }
            }
        }

        public static string When(double hours)
        {
            long h = (long)Math.Floor(hours);
            long day = h / HoursInDay;
            int hour = (int)(h % HoursInDay);
            int daysInYear = DaysInSeason * SeasonsInYear;
            long year = day / daysInYear;
            int dayOfYear = (int)(day % daysInYear);
            int season = dayOfYear / DaysInSeason;
            string seasonName = season < SeasonNames.Length ? SeasonNames[season] : "Season " + (season + 1);
            return seasonName + " " + (dayOfYear % DaysInSeason + 1) + ", " + year + ", " + hour.ToString("00", Text.Inv) + ":00";
        }
    }

    /// <summary>One JSON object per line, snake_case names, empty fields left out.</summary>
    internal static class Json
    {
        private static readonly JsonSerializerSettings Options = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Line(object o) => JsonConvert.SerializeObject(o, Options);
    }

    /// <summary>
    /// The worker's open files. Lines are buffered and flushed whenever the worker has nothing else queued. Files
    /// are shared for reading, so they can be opened while the game writes to them.
    /// </summary>
    internal static class Files
    {
        private static readonly Dictionary<string, StreamWriter> Open = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static void Append(string path, string line)
        {
            try
            {
                if (!Open.TryGetValue(path, out StreamWriter w))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Utf8);
                    Open[path] = w;
                }
                w.WriteLine(line);
            }
            catch (Exception e) { Log.Line("could not write " + path + ": " + e.Message); }
        }

        public static bool IsNew(string path)
        {
            try { return !Open.ContainsKey(path) && (!File.Exists(path) || new FileInfo(path).Length == 0); }
            catch { return false; }
        }

        public static void Overwrite(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, Utf8);
            }
            catch (Exception e) { Log.Line("could not write " + path + ": " + e.Message); }
        }

        public static void FlushAll()
        {
            foreach (StreamWriter w in Open.Values)
            {
                try { w.Flush(); } catch { }
            }
        }

        public static void CloseAll()
        {
            foreach (StreamWriter w in Open.Values)
            {
                try { w.Dispose(); } catch { }
            }
            Open.Clear();
        }
    }
}
