using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The watched realm's chronicle (docs/MEMORY.md): everything it learned, dated, oldest first, so its memory of the
    /// war does not depend on any one conversation. It holds:
    /// - every notice that reached the realm, except the ones kept quiet: wars and peace, sieges begun and lifted, raids,
    ///   holdings won and lost, battles and who won, lords captured, killed or set free, armies formed and broken up,
    ///   and its orders' call-backs and endings;
    /// - every enemy army it saw come into view;
    /// - every order the agent gave, carried out or refused, with the reason;
    /// - at the first load, what the game's own history log still holds about the realm (GameHistory), for the time
    ///   before our own lines begin. The game keeps most events for only a week, so from then on these lines are the
    ///   only record.
    /// Kept per campaign and realm in the campaign's folder (chronicle_&lt;realm&gt;.jsonl) and mirrored into the agent
    /// channel: chronicle.txt to read, chronicle.jsonl for the war room. When an earlier save is loaded, the lines dated
    /// after it are cut, so the realm never remembers what then did not happen. Worker thread; the console reads it
    /// under the lock.
    /// </summary>
    internal static class Chronicle
    {
        /// <summary>A realm's chronicle: its agent's (the first agent's for null or a realm without one; Agents, since 0.9).</summary>
        public static ChronicleBook For(string realm) => Agents.For(realm).Chronicle;

        /// <summary>The first agent's chronicle.</summary>
        public static ChronicleBook Primary => For(null);

        /// <summary>Does this realm keep a chronicle that has been read (the report's RECENT HISTORY comes from it)?</summary>
        public static bool Holds(string realm) => realm != null && Agents.All.Any(a => a.Chronicle.Open && a.Chronicle.Realm == realm);

        /// <summary>The realm is known (each hourly copy): its agent's chronicle is read the first time.</summary>
        public static void Ensure(string realm, string realmName, double now)
        {
            if (string.IsNullOrEmpty(realm)) return;
            // an agent configured without a realm (the stand-in: the smoke test's first copy) takes the realm its first copy names
            RealmAgent a = Agents.Of(realm) ?? Agents.All.FirstOrDefault(x => x.Realm == null && !x.Chronicle.Open);
            a?.Chronicle.Ensure(realm, realmName, now);
        }

        /// <summary>A notice (worker): into the chronicle of every agent's realm it reached, unless it is kept quiet.</summary>
        public static void FromNotice(Notice n) { foreach (RealmAgent a in Agents.All) a.Chronicle.FromNotice(n); }

        // the first agent's, for the console and the smoke test
        public static string Realm => Primary.Realm;
        public static int Count => Primary.Count;
        public static void Line(double hours, string kind, string text, string place = null, string party = null) => Primary.Line(hours, kind, text, place, party);
        public static List<string> Recent(double now, double withinHours, int max) => Primary.Recent(now, withinHours, max);
        public static string Last(int n) => Primary.Last(n);
    }

    /// <summary>One watched realm's chronicle (Chronicle).</summary>
    internal sealed class ChronicleBook
    {
        private readonly object Gate = new object();
        private readonly List<ChronicleEntry> Entries = new List<ChronicleEntry>();
        /// <summary>Notices and lines that came before the realm was known (its first hourly copy).</summary>
        private readonly List<Notice> WaitingNotices = new List<Notice>();
        private readonly List<ChronicleEntry> WaitingLines = new List<ChronicleEntry>();
        private string _dir, _realm, _realmName, _file, _seedRealm;
        private double _loadedAt = double.NaN;
        private List<ChronicleEntry> _fromGame;
        private bool _open;

        public ChronicleBook(string realm) { _realm = realm; }

        /// <summary>The realm whose chronicle this is (null until a planless one is read).</summary>
        public string Realm => _realm;
        /// <summary>Read from its file (the realm's first hourly copy came).</summary>
        public bool Open => _open;
        public int Count { get { lock (Gate) return Entries.Count; } }

        /// <summary>Is this realm's agent's channel on (each agent has its own mode)?</summary>
        private bool ChannelOn => AgentChannel.For(_realm).On;
        private string ChannelText => Path.Combine(AgentChannel.For(_realm).Dir, "chronicle.txt");
        private string ChannelData => Path.Combine(AgentChannel.For(_realm).Dir, "chronicle.jsonl");

        /// <summary>Replaced by a new campaign's: its file is let go.</summary>
        public void Close() { lock (Gate) if (_file != null) Files.Close(_file); }

        /// <summary>
        /// A campaign was loaded (worker): the realm's lines are read at its first hourly copy (Ensure). loadedAt is the
        /// in-game hour of the save; fromGame, the game's own history lines about seedRealm (GameHistory, game's thread).
        /// </summary>
        public void Reset(string dataDir, double loadedAt, string seedRealm, List<ChronicleEntry> fromGame)
        {
            lock (Gate)
            {
                if (_file != null) Files.Close(_file);
                Entries.Clear();
                WaitingNotices.Clear();
                WaitingLines.Clear();
                _dir = dataDir;
                if (seedRealm != null) _realm = seedRealm;
                _realmName = _file = null;
                _open = false;
                _loadedAt = loadedAt;
                _seedRealm = seedRealm;
                _fromGame = fromGame;
            }
        }

        /// <summary>
        /// The realm is known (each hourly copy; only a new realm does anything): its lines are read, cut back to the
        /// save's date, completed from the game's own log for the time before them, and mirrored into the channel.
        /// </summary>
        public void Ensure(string realm, string realmName, double now)
        {
            if (string.IsNullOrEmpty(realm) || (_open && realm == _realm)) return;
            int cut, seeded;
            double cutoff;
            lock (Gate)
            {
                if (_file != null) Files.Close(_file);
                Entries.Clear();
                _realm = realm;
                _open = true;
                _realmName = realmName;
                _file = Path.Combine(_dir ?? Paths.Data("unknown"), "chronicle_" + realm + ".jsonl");
                Entries.AddRange(Read(_file));
                cutoff = double.IsNaN(_loadedAt) ? now : _loadedAt;
                cut = Entries.RemoveAll(e => e.Hours > cutoff + 0.01);
                seeded = 0;
                if (_fromGame != null && realm == _seedRealm)
                {
                    // the game's own lines only for the time before ours begin, so nothing is told twice
                    double first = Entries.Count > 0 ? Entries.Min(e => e.Hours) : double.MaxValue;
                    List<ChronicleEntry> older = _fromGame.Where(e => e.Hours < first - 0.01 && e.Hours <= cutoff + 0.01).ToList();
                    seeded = older.Count;
                    Entries.AddRange(older);
                }
                _fromGame = null;
                List<ChronicleEntry> sorted = Entries.OrderBy(e => e.Hours).ToList();
                Entries.Clear();
                Entries.AddRange(sorted);
                foreach (ChronicleEntry e in Entries) if (string.IsNullOrEmpty(e.Date)) e.Date = Text.When(e.Hours);
                if (cut > 0 || seeded > 0)
                {
                    Files.Close(_file);
                    Files.Overwrite(_file, string.Concat(Entries.Select(e => Json.Line(e) + "\n")));
                }
                WriteChannel();
            }
            Diag.Event("chronicle", "loaded", new { realm, lines = Entries.Count, cut, seeded_from_game = seeded, cutoff = Math.Round(cutoff, 2) });
            if (cut > 0)
                Log.Line("chronicle: " + cut + " lines dated after " + Text.When(cutoff) + " were cut, because an earlier save was loaded; " + Entries.Count + " lines kept.");
            if (seeded > 0) Log.Line("chronicle: " + seeded + " lines from the game's own history log, for the time before our own records.");
            List<Notice> notices = WaitingNotices.ToList();
            List<ChronicleEntry> lines = WaitingLines.ToList();
            WaitingNotices.Clear();
            WaitingLines.Clear();
            foreach (Notice n in notices) FromNotice(n);
            foreach (ChronicleEntry e in lines) Add(e);
        }

        /// <summary>A notice (worker): into the chronicle of the realm it reached, unless it is kept quiet.</summary>
        public void FromNotice(Notice n)
        {
            if (n == null || n.Quiet != null || string.IsNullOrEmpty(n.Text)) return;
            if (!_open) { WaitingNotices.Add(n); return; }
            if (!n.Factions.Contains(_realm)) return;
            Add(new ChronicleEntry { Hours = n.Hours, Kind = n.Kind, Text = n.Text, Place = n.Place, Party = n.Party });
        }

        /// <summary>A line of our own (worker): an enemy army sighted, an order given or refused.</summary>
        public void Line(double hours, string kind, string text, string place = null, string party = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var e = new ChronicleEntry { Hours = hours, Kind = kind, Text = text, Place = place, Party = party };
            if (!_open) { WaitingLines.Add(e); return; }
            Add(e);
        }

        private void Add(ChronicleEntry e)
        {
            // a line whose hour is not known yet (the game's clock not read) takes the latest hour copied
            if (double.IsNaN(e.Hours) || e.Hours < 0) e.Hours = Brain.Latest?.Hours ?? 0;
            lock (Gate)
            {
                e.Date = Text.When(e.Hours);
                Entries.Add(e);
                Files.Append(_file, Json.Line(e));
                if (ChannelOn)
                {
                    Files.Append(ChannelData, Json.Line(new { e.Hours, e.Date, e.Kind, e.Text }));
                    Files.Append(ChannelText, Show(e));
                }
            }
            Diag.Count("chronicle:lines");
        }

        /// <summary>The lines of the last hours (the report's RECENT HISTORY), newest last, at most max.</summary>
        public List<string> Recent(double now, double withinHours, int max)
        {
            lock (Gate)
            {
                List<string> lines = Entries.Where(e => e.Hours >= now - withinHours && e.Hours <= now + 1).Select(Show).ToList();
                return lines.Count > max ? lines.Skip(lines.Count - max).ToList() : lines;
            }
        }

        /// <summary>For the console: the last lines, and where the whole chronicle is.</summary>
        public string Last(int n)
        {
            lock (Gate)
            {
                if (!_open) return "No chronicle yet: it starts at the first in-game hour after a campaign is loaded.";
                if (Entries.Count == 0) return "The chronicle of " + _realmName + " is empty so far. It is kept in " + _file;
                IEnumerable<string> lines = Entries.Skip(Math.Max(0, Entries.Count - n)).Select(Show);
                return string.Join("\n", lines) + "\n(" + Entries.Count + " lines in all, " + Entries.Count(e => e.From == "game") + " of them from the game's own history log; kept in " + _file
                       + (ChannelOn ? " and the agent's chronicle.txt" : "") + ")";
            }
        }

        private static string Show(ChronicleEntry e) => (e.Date ?? Text.When(e.Hours)) + " · " + e.Text + (e.From == "game" ? " (from the game's records)" : "");

        /// <summary>The whole chronicle into the agent channel: chronicle.txt to read, chronicle.jsonl for the war room.</summary>
        private void WriteChannel()
        {
            if (!ChannelOn) return;
            var text = new StringBuilder(64 * 1024);
            text.Append("THE CHRONICLE OF ").Append((_realmName ?? _realm ?? "YOUR REALM").ToUpperInvariant()).Append('\n');
            text.Append("Everything your realm has learned, oldest first: wars and peace, sieges, raids, holdings won and lost, battles and who won, "
                        + "lords captured, killed or set free, armies formed and broken up, enemy armies sighted, and your orders with what became of them. "
                        + "Lines marked (from the game's records) come from the game's own history, from before this chronicle was kept.\n\n");
            var data = new StringBuilder(64 * 1024);
            foreach (ChronicleEntry e in Entries)
            {
                text.Append(Show(e)).Append('\n');
                data.Append(Json.Line(new { e.Hours, e.Date, e.Kind, e.Text })).Append('\n');
            }
            Files.Close(ChannelText);
            Files.Close(ChannelData);
            Files.Overwrite(ChannelText, text.ToString());
            Files.Overwrite(ChannelData, data.ToString());
        }

        private static List<ChronicleEntry> Read(string path)
        {
            var list = new List<ChronicleEntry>();
            try
            {
                if (!File.Exists(path)) return list;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    int bad = 0;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0) continue;
                        try
                        {
                            JObject o = JObject.Parse(line);
                            list.Add(new ChronicleEntry
                            {
                                Hours = (double?)o["hours"] ?? 0, Date = (string)o["date"], Kind = (string)o["kind"], Text = (string)o["text"],
                                Place = (string)o["place"], Party = (string)o["party"], From = (string)o["from"]
                            });
                        }
                        catch (Exception e)
                        {
                            // a line half-written when the game closed: skipped, and said so once
                            if (++bad == 1) Diag.Swallowed("reading a line of the chronicle", e);
                        }
                    }
                }
            }
            catch (Exception e) { Diag.Error("reading the chronicle", e); }
            return list;
        }
    }
}
