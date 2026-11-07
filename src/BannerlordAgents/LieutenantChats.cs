using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The lieutenant's talks with the ruler in his other saves (0.16, Nov 7: I want it to have access to chats from other saves, so I can reference
    /// them). The conversation of the save being played is conversation.txt (0.11.1); since 0.16 every other save, of this campaign or another, gets
    /// a file of its own in the channel's other_chats folder, each the whole conversation that save had lived through when it was last saved (the
    /// lines its history holds, History.HoldsLine, from the campaign's lieutenant\chat.jsonl and the save's note in saves.jsonl), oldest first and
    /// dated like conversation.txt, and index.txt lists them, newest save first, with their dates and sizes. Written at each load, on the helper's
    /// thread; the folder is ours alone, so files of saves no longer there are taken away. Game-free.
    /// </summary>
    internal static class LieutenantChats
    {
        public const string Folder = "other_chats";

        /// <summary>One save's talk: its name, campaign, dates, and the lines for the lieutenant.</summary>
        internal sealed class Talk
        {
            public string Save, Campaign, Date, Wall, File, First, Last;
            public int Lines, Words;
            public string Text;
        }

        /// <summary>
        /// Writes other_chats for a lieutenant's channel: every save of every campaign under dataRoot with something said in it, but the save being played
        /// (save, of campaign). Returns the talks written.
        /// </summary>
        public static List<Talk> Write(string channelDir, string dataRoot, string campaign, string save)
        {
            var talks = new List<Talk>();
            if (string.IsNullOrEmpty(channelDir) || string.IsNullOrEmpty(dataRoot) || !Directory.Exists(dataRoot)) return talks;
            foreach (string dir in Directory.GetDirectories(dataRoot))
            {
                string id = Path.GetFileName(dir), chat = Path.Combine(dir, "lieutenant", "chat.jsonl");
                if (!System.IO.File.Exists(chat)) continue;
                foreach (KeyValuePair<string, JObject> kv in History.LatestNotes(dir))
                {
                    if (id == campaign && kv.Key == save) continue;
                    List<JObject> lines = LabelledLines.Read(chat, History.FromNote(kv.Value));
                    List<string> shown = lines.Select(l => Lieutenant.ForLieutenant(l, true)).Where(x => x != null).ToList();
                    int words = lines.Count(l => (string)l["k"] == "you");
                    if (words == 0) continue;
                    double first = lines.Select(l => (double?)l["h"] ?? double.NaN).Where(h => !double.IsNaN(h)).DefaultIfEmpty(double.NaN).Min();
                    double last = lines.Select(l => (double?)l["h"] ?? double.NaN).Where(h => !double.IsNaN(h)).DefaultIfEmpty(double.NaN).Max();
                    talks.Add(new Talk
                    {
                        Save = kv.Key, Campaign = id, Date = (string)kv.Value["date"], Wall = (string)kv.Value["wall"], Lines = shown.Count, Words = words,
                        First = double.IsNaN(first) ? null : Text.When(first), Last = double.IsNaN(last) ? null : Text.When(last),
                        Text = string.Join("\n", shown)
                    });
                }
            }
            talks = talks.OrderByDescending(t => t.Wall ?? "", StringComparer.Ordinal).ToList();
            bool many = talks.Select(t => t.Campaign).Distinct().Count() > 1 || talks.Any(t => t.Campaign != campaign);
            string folder = Path.Combine(channelDir, Folder);
            Directory.CreateDirectory(folder);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "index.txt" };
            foreach (Talk t in talks)
            {
                string name = FileName(t.Save + (many && t.Campaign != campaign ? " (" + t.Campaign + ")" : ""), written);
                t.File = Folder + "/" + name;
                Files.Overwrite(Path.Combine(folder, name), "THE CONVERSATION WITH YOUR RULER IN THE SAVE '" + t.Save + "'" + (t.Campaign != campaign ? " OF ANOTHER CAMPAIGN" : "")
                                                         + ", AS IT WAS WHEN LAST SAVED (" + (t.Date ?? "?") + "; oldest first)\n" + t.Text + "\n");
            }
            var sb = new StringBuilder();
            sb.AppendLine("YOUR TALKS WITH YOUR RULER IN HIS OTHER SAVES (written at this load; the save he plays now is conversation.txt)");
            sb.AppendLine("Each file is the whole conversation a save had lived through when it was last saved, oldest first and dated as conversation.txt is. A save played on from another shares its beginning. "
                          + "When the ruler refers to another save, an earlier test or something said there, read the file of that save, from where it bears on his words (an offset and a limit), not all of it.");
            if (talks.Count == 0) sb.AppendLine("(none: nothing was said to you in any other save)");
            foreach (Talk t in talks)
                sb.AppendLine("- '" + t.Save + "'" + (t.Campaign != campaign ? " (another campaign, " + t.Campaign + ")" : "") + ": saved at " + (t.Date ?? "?") + " (" + Clock(t.Wall) + "); "
                              + t.Words + (t.Words == 1 ? " message" : " messages") + " from him, " + t.Lines + " lines, " + (t.First ?? "?") + " to " + (t.Last ?? "?") + "; " + t.File);
            Files.Overwrite(Path.Combine(folder, "index.txt"), sb.ToString());
            foreach (string f in Directory.GetFiles(folder, "*.txt"))
                if (!written.Contains(Path.GetFileName(f)))
                {
                    try { System.IO.File.Delete(f); }
                    catch (IOException e) { Diag.Swallowed("an old file of the lieutenant's other talks", e); }
                }
            return talks;
        }

        /// <summary>A save's name as a file's: what Windows takes, unique in the folder.</summary>
        private static string FileName(string save, HashSet<string> taken)
        {
            var sb = new StringBuilder();
            foreach (char c in save.Trim()) sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            string stem = sb.Length > 0 ? sb.ToString().TrimEnd('.', ' ') : "save", name = stem + ".txt";
            for (int n = 2; !taken.Add(name); n++) name = stem + " " + n + ".txt";
            return name;
        }

        private static string Clock(string wall)
            => DateTime.TryParse(wall, Text.Inv, System.Globalization.DateTimeStyles.None, out DateTime w) ? w.ToString("MMM d, yyyy HH:mm", Text.Inv) + " by the clock" : "when, not known";
    }
}
