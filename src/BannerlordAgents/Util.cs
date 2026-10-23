using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
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
            catch (Exception e) { Diag.Swallowed("reading the calendar", e); }
            for (int i = 0; i < SeasonNames.Length; i++)
            {
                try
                {
                    string name = TaleWorlds.Core.GameTexts.FindText("str_season_" + (TaleWorlds.CampaignSystem.CampaignTime.Seasons)i)?.ToString();
                    if (!string.IsNullOrEmpty(name)) SeasonNames[i] = name;
                }
                catch (Exception e) { Diag.Swallowed("reading the season names", e); }
            }
        }

        public static string When(double hours)
        {
            Parts(hours, out int season, out int day, out long year, out int hour);
            string seasonName = season < SeasonNames.Length ? SeasonNames[season] : "Season " + (season + 1);
            return seasonName + " " + (day + 1) + ", " + year + ", " + hour.ToString("00", Text.Inv) + ":00";
        }

        /// <summary>"Summer 3, 14:00": the same without the year, for lists of times close together.</summary>
        public static string WhenShort(double hours)
        {
            Parts(hours, out int season, out int day, out _, out int hour);
            string seasonName = season < SeasonNames.Length ? SeasonNames[season] : "Season " + (season + 1);
            return seasonName + " " + (day + 1) + ", " + hour.ToString("00", Text.Inv) + ":00";
        }

        /// <summary>Season (0-based), day of the season (0-based), year and hour, as the game counts them; the self-check compares these with the game's own.</summary>
        public static void Parts(double hours, out int season, out int day, out long year, out int hour)
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

        /// <summary>Reads back what Line wrote (the test run's run.json).</summary>
        public static T Read<T>(string text) => JsonConvert.DeserializeObject<T>(text, Options);
    }

    /// <summary>
    /// The worker's open files. Lines are buffered and flushed whenever the worker has nothing else queued. Files
    /// are shared for reading, so they can be opened while the game writes to them. A file that is rewritten whole
    /// (the agent's poke.txt and report.txt, the reports) is written beside the old one and swapped in, so a reader
    /// never sees half a file. Failures are counted and reported, never dropped silently.
    /// </summary>
    internal static class Files
    {
        private static readonly Dictionary<string, StreamWriter> Open = new Dictionary<string, StreamWriter>(StringComparer.OrdinalIgnoreCase);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static int OpenCount => Open.Count;

        public static void Append(string path, string line)
        {
            try
            {
                if (!Open.TryGetValue(path, out StreamWriter w))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8);
                    Open[path] = w;
                }
                w.WriteLine(line);
            }
            catch (Exception e) { Diag.Error("writing " + Path.GetFileName(path), e); }
        }

        public static bool IsNew(string path)
        {
            try { return !Open.ContainsKey(path) && (!File.Exists(path) || new FileInfo(path).Length == 0); }
            catch (Exception e) { Diag.Swallowed("checking " + Path.GetFileName(path), e); return false; }
        }

        /// <summary>Replaces a whole file at once: written as (name).new, then swapped in (retried if a reader holds it).</summary>
        public static void Overwrite(string path, string text)
        {
            string tmp = path + ".new";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) { File.WriteAllText(path, text, Utf8); return; }
                File.WriteAllText(tmp, text, Utf8);
                for (int i = 0; ; i++)
                {
                    try { File.Replace(tmp, path, null, true); return; }
                    catch (IOException) when (i < 4) { Thread.Sleep(15); }
                    catch (UnauthorizedAccessException) when (i < 4) { Thread.Sleep(15); }
                }
            }
            catch (Exception e)
            {
                Diag.Swallowed("swapping in " + Path.GetFileName(path), e);
                try
                {
                    File.WriteAllText(path, text, Utf8);
                    Diag.Count("files:rewritten in place");
                }
                catch (Exception e2) { Diag.Error("writing " + Path.GetFileName(path), e2); }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception e3) { Diag.Swallowed("removing " + Path.GetFileName(tmp), e3); }
            }
        }

        public static void FlushAll()
        {
            foreach (KeyValuePair<string, StreamWriter> kv in Open)
            {
                try { kv.Value.Flush(); } catch (Exception e) { Diag.Swallowed("flushing " + Path.GetFileName(kv.Key), e); }
            }
        }

        /// <summary>Closes one file kept open for appending, so it can be rewritten whole.</summary>
        public static void Close(string path)
        {
            if (!Open.TryGetValue(path, out StreamWriter w)) return;
            try { w.Dispose(); } catch (Exception e) { Diag.Swallowed("closing " + Path.GetFileName(path), e); }
            Open.Remove(path);
        }

        public static void CloseAll()
        {
            foreach (KeyValuePair<string, StreamWriter> kv in Open)
            {
                try { kv.Value.Dispose(); } catch (Exception e) { Diag.Swallowed("closing " + Path.GetFileName(kv.Key), e); }
            }
            Open.Clear();
        }
    }
}
