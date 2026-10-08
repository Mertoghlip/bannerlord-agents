using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// The worker's side of the bridge: what each realm remembers, when it gets poked, and every file written.
    ///
    /// A realm is poked, at most once per in-game hour, when:
    /// - an enemy lord or army comes into its view, for the first time or after 12 hours out of sight;
    /// - its ruler gets a notice (a war, a siege or raid on its lands, a holding taken, an army broken, ...);
    /// - a full day passes with nothing new.
    /// Everything that happens within the same hour is merged into one poke. The watched realm's pokes carry the
    /// full view and the written report an agent would read; the player's own pokes are kept so each recorded
    /// decision can say what the player had just seen.
    /// Worker thread only.
    /// </summary>
    internal static class Brain
    {
        internal sealed class Memory
        {
            public readonly Dictionary<string, ViewParty> LastSeen = new Dictionary<string, ViewParty>();
            public HashSet<string> SeenLastHour = new HashSet<string>();
            public double Started = double.NaN;
            public double LastPoke = double.NaN;
            public readonly List<Notice> Pending = new List<Notice>();
            public readonly List<Notice> News = new List<Notice>();
            public readonly List<Trigger> Recent = new List<Trigger>();
            public int Pokes;
        }

        private const double OutOfSightHours = 12;
        private const double ForgetHours = 72;
        private const int SmallestLordParty = 50;

        private static readonly Dictionary<string, Memory> Mem = new Dictionary<string, Memory>();
        private static readonly List<string> RecentDecisions = new List<string>();
        private static readonly List<string> RecentPokes = new List<string>();
        private static string _dir;

        public static Snapshot Latest;
        public static int DecisionsWritten, PokesWatched, PokesPlayer;

        public static string Dir => _dir ?? (_dir = Paths.Data("unknown"));

        public static void Reset(string campaign)
        {
            Files.FlushAll();
            Mem.Clear();
            RecentDecisions.Clear();
            RecentPokes.Clear();
            Latest = null;
            DecisionsWritten = PokesWatched = PokesPlayer = 0;
            _dir = Paths.Data(campaign);
            AgentChannel.Reset();
        }

        public static Memory Get(string fid)
        {
            fid = fid ?? "none";
            if (!Mem.TryGetValue(fid, out Memory m)) Mem[fid] = m = new Memory();
            return m;
        }

        public static void OnNotice(Notice n)
        {
            foreach (string f in n.Factions.Distinct())
            {
                Memory m = Get(f);
                m.Pending.Add(n);
                m.News.Add(n);
                m.News.RemoveAll(x => n.Hours - x.Hours > 48);
            }
            if (n.Kind == "party_destroyed" && n.Party != null)
                foreach (Memory m in Mem.Values) m.LastSeen.Remove(n.Party);
            Files.Append(Path.Combine(Dir, "notices.jsonl"), Json.Line(n));
        }

        public static void OnHour(Snapshot s)
        {
            Latest = s;
            if (s.Watched != null && s.WatchedSees != null)
                Process(s, s.Watched, s.WatchedSees, s.HostileToWatched, false);
            if (s.PlayerFaction != null)
                Process(s, s.PlayerFaction, PlayerSees(s), s.HostileToPlayer, true);
            if (AgentChannel.On && s.Watched != null && s.WatchedSees != null)
                AgentChannel.OnHour(s.Hours, Views.Report(Views.Build(s, s.Watched, s.WatchedSees, s.HostileToWatched, Get(s.Watched)), null));
        }

        /// <summary>The game's own flags for what the player's party sees, as sightings.</summary>
        private static Dictionary<string, Sighting> PlayerSees(Snapshot s)
        {
            var sees = new Dictionary<string, Sighting>();
            s.PartyById.TryGetValue(s.PlayerParty ?? "", out PartyRow me);
            foreach (PartyRow r in s.Parties)
            {
                if (!r.SeenByPlayer) continue;
                float d = me != null ? Views.Dist(me.X, me.Y, r.X, r.Y) : 0f;
                sees[r.Id] = new Sighting { Party = r.Id, By = "you", Distance = Text.R1(d) };
            }
            return sees;
        }

        private static void Process(Snapshot s, string fid, Dictionary<string, Sighting> sees, HashSet<string> hostile, bool isPlayer)
        {
            Memory m = Get(fid);
            var triggers = new List<Trigger>();
            List<SettlementRow> own = s.Settlements.Where(h => h.Faction == fid).ToList();
            bool first = double.IsNaN(m.Started);
            if (first)
            {
                m.Started = s.Hours;
                triggers.Add(new Trigger { Hours = s.Hours, Kind = "first_look", Text = "First look since the campaign was loaded." });
            }
            else
            {
                foreach (KeyValuePair<string, Sighting> kv in sees)
                {
                    if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r)) continue;
                    if (r.Kind != "lord" || r.Attached || !hostile.Contains(r.Faction ?? "")) continue;
                    if (r.ArmyMen == 0 && r.Men < SmallestLordParty) continue;
                    if (m.SeenLastHour.Contains(r.Id)) continue;
                    if (m.LastSeen.TryGetValue(r.Id, out ViewParty last) && s.Hours - last.SeenHours < OutOfSightHours) continue;
                    ViewParty vp = Views.Party(s, r, kv.Value, "enemy", own);
                    int men = r.ArmyMen > 0 ? r.ArmyMen : r.Men;
                    string where = vp.NearestHolding != null ? Text.D(vp.HoldingDistance) + " from " + vp.NearestHolding : "near " + (vp.Near ?? "nowhere known");
                    triggers.Add(new Trigger
                    {
                        Hours = s.Hours, Kind = "enemy_in_view", Party = r.Id,
                        Text = "Enemy " + (r.ArmyMen > 0 ? "army" : "lord") + " in view: " + vp.Name + " (" + vp.FactionName + "), "
                               + Text.N(men) + " men, " + where + "; " + vp.Doing + "; seen by " + kv.Value.By + "."
                    });
                }
            }

            m.SeenLastHour = new HashSet<string>(sees.Keys);
            foreach (KeyValuePair<string, Sighting> kv in sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r) || r.Attached || r.Faction == fid) continue;
                if (r.Kind != "lord" && r.Kind != "bandit") continue;
                m.LastSeen[r.Id] = Views.Party(s, r, kv.Value, hostile.Contains(r.Faction ?? "") ? "enemy" : "neutral", null);
            }
            foreach (string id in m.LastSeen.Where(x => s.Hours - x.Value.SeenHours > ForgetHours).Select(x => x.Key).ToList())
                m.LastSeen.Remove(id);

            foreach (Notice n in m.Pending)
                triggers.Add(new Trigger { Hours = n.Hours, Kind = n.Kind, Text = n.Text, Party = n.Party });
            m.Pending.Clear();

            if (triggers.Count == 0)
            {
                double since = double.IsNaN(m.LastPoke) ? m.Started : m.LastPoke;
                if (s.Hours - since >= 24)
                    triggers.Add(new Trigger { Hours = s.Hours, Kind = "quiet_day", Text = "A full day has passed with nothing new." });
            }
            if (triggers.Count == 0) return;

            m.LastPoke = s.Hours;
            m.Pokes++;
            m.Recent.AddRange(triggers);
            m.Recent.RemoveAll(t => s.Hours - t.Hours > 24);

            if (isPlayer)
            {
                PokesPlayer++;
                Files.Append(Path.Combine(Dir, "record", "pokes_you.jsonl"),
                    Json.Line(new { Type = "poke", Faction = fid, Hours = s.Hours, Date = s.Date, Triggers = triggers }));
                return;
            }

            PokesWatched++;
            int number = PokesWatched;
            View v = Views.Build(s, fid, sees, hostile, m);
            string report = Views.Report(v, triggers);
            Files.Append(Path.Combine(Dir, "pokes_" + fid + ".jsonl"),
                Json.Line(new { Type = "poke", Number = number, Faction = fid, v.FactionName, Hours = s.Hours, Date = s.Date, Triggers = triggers, View = v, Report = report }));
            Files.Overwrite(Path.Combine(Dir, "reports", fid + "_latest.txt"), report);
            if (AgentChannel.On) AgentChannel.OnPoke(number, s.Hours, triggers, report);

            string line = Text.When(s.Hours) + ": " + v.FactionName + " poked. " + triggers[0].Text
                          + (triggers.Count > 1 ? " (and " + (triggers.Count - 1) + " more)" : "");
            Log.Line("poke: " + line);
            RecentPokes.Add(line);
            if (RecentPokes.Count > 50) RecentPokes.RemoveAt(0);
            if (Settings.Notify)
            {
                string say = v.FactionName + ": " + triggers[0].Text + (triggers.Count > 1 ? " (+" + (triggers.Count - 1) + ")" : "");
                Background.ToMain.Enqueue(() => Ui.Say(say, Colors.Yellow));
            }
        }

        /// <summary>For the console: the watched realm's report as of now, without counting as a poke.</summary>
        public static string ReportNow(Snapshot s)
        {
            Latest = s;
            if (s.Watched == null || s.WatchedSees == null) return "No realm is under watch.";
            View v = Views.Build(s, s.Watched, s.WatchedSees, s.HostileToWatched, Get(s.Watched));
            string report = Views.Report(v, null);
            string path = Path.Combine(Dir, "reports", s.Watched + "_now.txt");
            Files.Overwrite(path, report);
            string[] lines = report.Replace("\r", "").Split('\n');
            return string.Join("\n", lines.Take(24)) + "\n...\nThe full report (" + lines.Length + " lines, about "
                   + Text.N(report.Length / 4) + " tokens) is in " + path;
        }

        public static void WriteDecision(DecisionRecord d)
        {
            Memory m = Get(d.Faction);
            d.RecentPokes = m.Recent.Where(t => d.Hours - t.Hours <= 24).ToList();
            if (Latest != null && d.Faction != null) d.Realm = Views.Realm(Latest, d.Faction);
            Files.Append(Path.Combine(Dir, "record", "decisions.jsonl"), Json.Line(d));
            DecisionsWritten++;
            string line = Text.When(d.Hours) + ": " + d.Text;
            RecentDecisions.Add(line);
            if (RecentDecisions.Count > 50) RecentDecisions.RemoveAt(0);
            Log.Line("recorded: " + line);
        }

        public static void WriteRecord(string file, object o) => Files.Append(Path.Combine(Dir, "record", file), Json.Line(o));
        public static void WriteOrder(object o) => Files.Append(Path.Combine(Dir, "orders.jsonl"), Json.Line(o));

        public static string ShowDecisions(int n)
        {
            if (RecentDecisions.Count == 0) return "No decisions recorded yet in this session.";
            return string.Join("\n", RecentDecisions.Skip(Math.Max(0, RecentDecisions.Count - n)));
        }

        public static string ShowPokes(int n)
        {
            if (RecentPokes.Count == 0) return "No pokes for the watched realm yet in this session.";
            return string.Join("\n", RecentPokes.Skip(Math.Max(0, RecentPokes.Count - n)));
        }
    }

    /// <summary>On-screen messages. Game's thread only.</summary>
    internal static class Ui
    {
        public static void Say(string text, Color color)
        {
            try { InformationManager.DisplayMessage(new InformationMessage("[Agents] " + text, color)); } catch { }
        }
    }
}
