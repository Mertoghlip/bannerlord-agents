using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BannerlordAgents.Common
{
    /// <summary>
    /// A Claude Code transcript read as the agent lived it (war room 0.18.9; one file shared by the war room, for the agent's own earlier conversations, and the runs'
    /// archive): each message sent to the conversation with all that followed it up to the next (the thinking as summarized, every question with its answer, the answer),
    /// what the message was and its in-game date. Claude Code's own notes (marked meta) and side chains are left out.
    /// </summary>
    internal static class Transcript
    {
        public sealed class Exchange
        {
            public int N;
            public string Date = "", Kind = "", Text = "";
            public double Hours = double.NaN;
        }

        private static readonly string[] Seasons = { "Spring", "Summer", "Autumn", "Winter" };
        private static readonly Regex DateRx = new Regex(@"(Spring|Summer|Autumn|Winter) (\d+), (\d+), (\d\d):(\d\d)", RegexOptions.Compiled);
        private static readonly Regex PokeRx = new Regex(@"POKE #(\d+), ((Spring|Summer|Autumn|Winter) \d+, \d+, \d\d:\d\d)", RegexOptions.Compiled);
        private static readonly Regex ReviewRx = new Regex(@"STRATEGY REVIEW, ((Spring|Summer|Autumn|Winter) \d+, \d+, \d\d:\d\d)", RegexOptions.Compiled);
        private static readonly Regex RulerRx = new Regex(@"YOUR RULER SAYS \(message #(\d+), ((Spring|Summer|Autumn|Winter) \d+, \d+, \d\d:\d\d)\)", RegexOptions.Compiled);

        /// <summary>An in-game date's hour count, as the game keeps it (84 days a year, 21 a season).</summary>
        public static double HoursOf(string date)
        {
            Match m = DateRx.Match(date ?? "");
            if (!m.Success) return double.NaN;
            int season = Array.IndexOf(Seasons, m.Groups[1].Value);
            int day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), year = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture), minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            return (year * 84.0 + season * 21 + day - 1) * 24 + hour + minute / 60.0;
        }

        /// <summary>An hour count as an in-game date ("Autumn 6, 1087, 18:00"); empty for none.</summary>
        public static string DateOf(double hours)
        {
            if (double.IsNaN(hours) || hours <= 0) return "";
            long day = (long)Math.Floor(hours / 24);
            int hour = (int)Math.Floor(hours - day * 24.0);
            long year = day / 84;
            int inYear = (int)(day % 84);
            return Seasons[inYear / 21] + " " + (inYear % 21 + 1) + ", " + year + ", " + hour.ToString("00", CultureInfo.InvariantCulture) + ":00";
        }

        /// <summary>What a message sent to the conversation was, and its in-game date (empty when it says none).</summary>
        public static string KindOf(string text, out string date)
        {
            date = "";
            string t = text ?? "";
            if (t.StartsWith("WRITE YOUR HANDOVER", StringComparison.Ordinal)) return "the request for your handover";
            if (t.StartsWith("That is not a handover", StringComparison.Ordinal)) return "the request for your handover, again";
            if (t.StartsWith("Your answer could not be read", StringComparison.Ordinal) || t.StartsWith("Your review's answer could not be read", StringComparison.Ordinal)) return "asked to answer again";
            string kind = null;
            Match p = PokeRx.Match(t), rv = ReviewRx.Match(t), ru = RulerRx.Match(t);
            Match first = new[] { p, rv, ru }.Where(m => m.Success).OrderBy(m => m.Index).FirstOrDefault();
            if (first == p && p.Success) { kind = "wake (poke #" + p.Groups[1].Value + ")"; date = p.Groups[2].Value; }
            else if (first == rv && rv.Success) { kind = "strategy review"; date = rv.Groups[1].Value; }
            else if (first == ru && ru.Success) { kind = "your ruler's message #" + ru.Groups[1].Value; date = ru.Groups[2].Value; }
            if (t.StartsWith("A NEW CONVERSATION", StringComparison.Ordinal)) kind = "the opening of the conversation" + (kind != null ? ", then " + kind : "");
            else if (t.StartsWith("THE GAME WAS LOADED", StringComparison.Ordinal)) kind = "the game loaded again" + (kind != null ? ", then " + kind : "");
            if (date.Length == 0)
            {
                Match d = DateRx.Match(t.Length > 400 ? t.Substring(0, 400) : t);
                if (d.Success) date = d.Value;
            }
            return kind ?? "a message";
        }

        /// <summary>The exchanges of a transcript, oldest first: each message sent to the conversation with all that followed it up to the next.</summary>
        public static List<Exchange> Read(string jsonl)
        {
            var list = new List<Exchange>();
            StringBuilder sb = null;
            Exchange cur = null;
            string lastDate = "";
            double lastHours = double.NaN;
            void Close()
            {
                if (cur == null) return;
                cur.Text = sb.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
                list.Add(cur);
            }
            foreach (string raw in Lines(jsonl))
            {
                if (raw.Length == 0 || raw[0] != '{') continue;
                JsonObject d;
                try { d = JsonNode.Parse(raw) as JsonObject; }
                catch (JsonException) { continue; }
                if (d == null) continue;
                string type = Str(d["type"]);
                if (type != "user" && type != "assistant") continue;
                if (Bool(d["isMeta"]) || Bool(d["isSidechain"]) || Bool(d["isCompactSummary"])) continue;
                JsonNode content = d["message"]?["content"];
                if (type == "user")
                {
                    string said = TextOf(content);
                    if (said.Length > 0)
                    {
                        Close();
                        string kind = KindOf(said, out string date);
                        if (date.Length > 0) { lastDate = date; lastHours = HoursOf(date); }
                        cur = new Exchange { N = list.Count + 1, Date = lastDate, Hours = lastHours, Kind = kind };
                        sb = new StringBuilder();
                        sb.Append("=== EXCHANGE ").Append(cur.N).Append(" · ").Append(cur.Date.Length > 0 ? cur.Date : "no date").Append(" · ").Append(kind).Append(" ===\n\n");
                        sb.Append("--- YOU WERE SENT ---\n").Append(said.TrimEnd()).Append("\n\n");
                        continue;
                    }
                    if (cur == null || content is not JsonArray results) continue;
                    foreach (JsonNode b in results)
                    {
                        if (b is not JsonObject r || Str(r["type"]) != "tool_result") continue;
                        sb.Append(Bool(r["is_error"]) ? "--- ITS ANSWER (an error) ---\n" : "--- ITS ANSWER ---\n").Append(TextOf(r["content"]).TrimEnd()).Append("\n\n");
                    }
                    continue;
                }
                if (cur == null || content is not JsonArray blocks) continue;
                foreach (JsonNode b in blocks)
                {
                    if (b is not JsonObject o) continue;
                    switch (Str(o["type"]))
                    {
                        case "thinking":
                            string th = Str(o["thinking"]) ?? "";
                            if (th.Trim().Length > 0) sb.Append("--- YOUR THINKING (as summarized) ---\n").Append(th.TrimEnd()).Append("\n\n");
                            break;
                        case "text":
                            string tx = Str(o["text"]) ?? "";
                            if (tx.Trim().Length > 0) sb.Append("--- YOUR ANSWER ---\n").Append(tx.TrimEnd()).Append("\n\n");
                            break;
                        case "tool_use":
                            string name = Str(o["name"]) ?? "?";
                            int cut = name.LastIndexOf("__", StringComparison.Ordinal);
                            if (name.StartsWith("mcp__", StringComparison.Ordinal) && cut > 0) name = name.Substring(cut + 2);
                            sb.Append("--- YOU ASKED ").Append(name).Append(": ").Append(o["input"]?.ToJsonString() ?? "{}").Append(" ---\n\n");
                            break;
                    }
                }
            }
            Close();
            return list;
        }

        /// <summary>The first and last moments a transcript's lines carry (local time); false when it has none.</summary>
        public static bool Span(string jsonl, out DateTime first, out DateTime last)
        {
            first = last = DateTime.MinValue;
            foreach (string raw in Lines(jsonl))
            {
                int at = raw.IndexOf("\"timestamp\":\"", StringComparison.Ordinal);
                if (at < 0) continue;
                int end = raw.IndexOf('"', at + 13);
                if (end < 0 || !DateTimeOffset.TryParse(raw.Substring(at + 13, end - at - 13), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset t)) continue;
                DateTime local = t.LocalDateTime;
                if (first == DateTime.MinValue || local < first) first = local;
                if (local > last) last = local;
            }
            return first != DateTime.MinValue;
        }

        /// <summary>A file's lines, read while another program may still be writing it.</summary>
        private static IEnumerable<string> Lines(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string line;
            while ((line = sr.ReadLine()) != null) yield return line;
        }

        /// <summary>A message's words: a plain string, or its text blocks (a tool's answer, a list of text blocks) joined.</summary>
        private static string TextOf(JsonNode content)
        {
            if (content is JsonValue v && v.TryGetValue(out string s)) return s;
            if (content is not JsonArray a) return "";
            var parts = new List<string>();
            foreach (JsonNode b in a)
                if (b is JsonObject o && Str(o["type"]) == "text" && Str(o["text"]) is string t && !t.StartsWith("<system-reminder>", StringComparison.Ordinal)) parts.Add(t);
            return string.Join("\n", parts);
        }

        private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
        private static bool Bool(JsonNode n) => n is JsonValue v && v.TryGetValue(out bool b) && b;
    }
}
