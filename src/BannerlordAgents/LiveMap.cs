using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BannerlordAgents
{
    /// <summary>What one realm saw this hour, for the dashboard: the very view its report was written from, and what the report left out.</summary>
    internal sealed class RealmFrame
    {
        public string Name;
        /// <summary>An agent commands this realm; Player: it is the player's.</summary>
        public bool Agent, Player;
        public View View;
        /// <summary>Every party the realm sees this hour.</summary>
        public List<string> InSight = new List<string>();
        /// <summary>Enemies and lords in sight that the report's list left out (its cap).</summary>
        public List<string> Cut = new List<string>();
        /// <summary>Caravans, villagers and other small parties in sight, which the report only counts.</summary>
        public List<string> CountedOnly = new List<string>();
        /// <summary>Parties the realm remembers seeing that the report's SEEN EARLIER list left out.</summary>
        public List<ViewParty> Remembered = new List<ViewParty>();
        /// <summary>The agent's orders in force, by party, as its report words them.</summary>
        public Dictionary<string, string> Orders;
    }

    /// <summary>
    /// The dashboard's hourly pictures (0.7, docs/DASHBOARD.md). Every in-game hour the helper writes one frame: every party and
    /// holding as the hourly copy has it (with each lord's planned way), and for each realm followed (the agent's and the
    /// player's) the very view its report was built from, plus what the report left out. Frames go to
    /// live\(campaign)\(load)\frames\h(hour).json.gz, so a load can be rewound; live\current.json says where the newest is. The
    /// agent's realm also gets live\(campaign)\view_(realm).json: its view only, for the "ask the way" tool, which must never see
    /// more than the realm does. Worker thread only.
    /// </summary>
    internal static class LiveMap
    {
        private static string _loadDir;
        public static int Frames;
        public static long LastBytes;
        public static double LastMs;

        private static readonly JsonSerializerSettings Compact = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static string Root => Path.Combine(Paths.Root, "live");

        public static void Reset() => _loadDir = null;

        /// <summary>A realm's part of the frame: its view, what it sees, and what its report left out.</summary>
        public static RealmFrame Realm(Snapshot s, string fid, View v, Dictionary<string, Sighting> sees, HashSet<string> hostile, Brain.Memory m, bool agent, bool player)
        {
            var rf = new RealmFrame { Name = v.FactionName, Agent = agent, Player = player, View = v };
            var listed = new HashSet<string>(v.Seen.Select(x => x.Id));
            foreach (string id in sees.Keys)
            {
                rf.InSight.Add(id);
                if (listed.Contains(id) || !s.PartyById.TryGetValue(id, out PartyRow r) || r.Attached || r.Faction == fid) continue;
                bool enemy = hostile.Contains(r.Faction ?? "") || r.Kind == "bandit";
                if (!enemy && r.Kind != "lord") rf.CountedOnly.Add(id);
                else rf.Cut.Add(id);
            }
            if (m != null)
            {
                var shown = new HashSet<string>(v.LastSeen.Select(x => x.Id));
                foreach (ViewParty last in m.LastSeen.Values)
                    if (!shown.Contains(last.Id) && !sees.ContainsKey(last.Id)) rf.Remembered.Add(last);
            }
            if (agent)
            {
                try { rf.Orders = Tools.Runner.ByParty(Tools.Runner.WorldFor(s)); }
                catch (Exception e) { Diag.Swallowed("the orders in force for the dashboard", e); }
            }
            return rf;
        }

        public static void Write(Snapshot s, Dictionary<string, RealmFrame> realms)
        {
            long t = Stopwatch.GetTimestamp();
            string campaignDir = Path.Combine(Root, Safe(s.Campaign));
            string dir = Path.Combine(campaignDir, Safe(string.IsNullOrEmpty(s.LoadId) ? "load" : s.LoadId));
            if (dir != _loadDir)
            {
                _loadDir = dir;
                Directory.CreateDirectory(Path.Combine(dir, "frames"));
                Prune(campaignDir, dir);
            }
            var factions = s.FactionNames.Select(kv =>
            {
                s.Factions.TryGetValue(kv.Key, out FactionRow fr);
                uint c = s.FactionColors.TryGetValue(kv.Key, out uint col) ? col : 0;
                return new
                {
                    Id = kv.Key, Name = kv.Value, Color = "#" + (c & 0xFFFFFF).ToString("x6"), Kingdom = fr != null,
                    AtWar = fr?.AtWar, Allies = fr?.Allies, Ruler = fr?.Ruler
                };
            }).ToList();
            var frame = new
            {
                V = 1, s.Campaign, s.LoadId, s.Hours, s.Date, s.Night, Player = s.PlayerFaction, s.PlayerParty, s.Watched, Map = s.MapCrc,
                Factions = factions, Settlements = s.Settlements, Parties = s.Parties, Realms = realms,
                // the marching rules in force, and every siege and battle with its numbers (0.7.3)
                s.SpeedModel, s.CultureSpeeds, s.CultureNames, s.Sieges, s.Battles
            };
            string json = JsonConvert.SerializeObject(frame, Compact);
            string name = "h" + ((long)Math.Floor(s.Hours + 1e-6)).ToString(Text.Inv) + ".json.gz";
            string path = Path.Combine(dir, "frames", name), tmp = path + ".part";
            byte[] raw = new UTF8Encoding(false).GetBytes(json);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
                gz.Write(raw, 0, raw.Length);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            LastBytes = new FileInfo(path).Length;
            Files.Overwrite(Path.Combine(Root, "current.json"), Json.Line(new
            {
                s.Campaign, LoadId = Path.GetFileName(dir), s.Hours, s.Date, Dir = dir, Frame = name, Map = s.MapCrc, Realms = realms.Keys.ToList(), Agent = s.Watched,
                Agents = s.Seats != null && s.Seats.Count > 0 ? s.Seats.Select(x => x.Realm).ToList() : new List<string> { s.Watched },
                AgentOn = AgentChannel.On, Written = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Text.Inv),
                Calendar = new { Calendar.HoursInDay, Calendar.DaysInSeason, Calendar.SeasonsInYear, Calendar.SeasonNames }
            }));
            Frames++;
            LastMs = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
            Diag.Sample("dashboard:frame ms", LastMs);
            Diag.Sample("dashboard:frame kb", LastBytes / 1024.0);
        }

        /// <summary>The agent's realm's own view, alone, for the "ask the way" tool: its lords (with their pace and planned ways), and only what it saw of the rest.</summary>
        public static void WriteView(Snapshot s, View v, string fid)
        {
            var own = s.Parties.Where(r => r.Faction == fid && r.Kind == "lord" && !r.Attached).Select(r => new
            {
                r.Id, Lord = r.Leader, Name = r.Name ?? r.Id, r.X, r.Y, r.Men, r.ArmyMen, Strength = r.ArmyStrength > 0 ? r.ArmyStrength : r.Strength,
                Pace = r.Speed, r.FinalSpeed, March = MarchOf(s, r), Doing = Views.Doing(s, r), r.InSettlement, r.Route, r.Tx, r.Ty,
                // for the "battle odds" question (0.7.4): its morale and its leader's Tactics, and an army's whole strength
                r.Morale, r.Tactics, r.ArmyStrength, r.Wounded
            }).ToList();
            Files.Overwrite(Path.Combine(Root, Safe(s.Campaign), "view_" + Safe(fid) + ".json"), Json.Line(new
            {
                s.Campaign, s.LoadId, Realm = fid, RealmName = v.FactionName, s.Hours, s.Date, Map = s.MapCrc, Own = own,
                v.Seen, v.LastSeen, v.Trails, v.Holdings, v.Frontier,
                // the castles and towns of each realm at war with it, as the map shows any player (0.7.4, for the "roads in" question)
                EnemyHoldings = EnemyHoldings(s, fid),
                // the marching rules in force, for the "ask the way" tool (GamePace, 0.7.3): the speed model, each people's bonuses, the scout perks' values
                s.SpeedModel, s.CultureSpeeds, s.CultureNames, s.PerkValues
            }));
        }

        /// <summary>The castles and towns of each realm at war with this one, by realm name: who owns which place the map shows anyone.</summary>
        private static Dictionary<string, List<string>> EnemyHoldings(Snapshot s, string fid)
        {
            var map = new Dictionary<string, List<string>>();
            if (!s.Factions.TryGetValue(fid ?? "", out FactionRow me)) return map;
            foreach (string e in me.AtWar)
                map[Views.FactionName(s, e)] = s.Settlements.Where(h => h.Faction == e && (h.Kind == "castle" || h.Kind == "town")).Select(h => h.Id).ToList();
            return map;
        }

        /// <summary>A lord's march for the view file: the parts of its speed sum (its perk values go once, at the top).</summary>
        private static Common.Pace MarchOf(Snapshot s, PartyRow r)
        {
            Common.Pace p = Forecast.PaceOf(s, r);
            if (p != null) p.PerkValues = null;
            return p;
        }

        /// <summary>Only the newest keep_loads loads of a campaign keep their frames.</summary>
        private static void Prune(string campaignDir, string keep)
        {
            try
            {
                List<string> loads = Directory.GetDirectories(campaignDir).OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).ToList();
                foreach (string d in loads.Skip(Math.Max(1, Settings.KeepLoads)))
                {
                    if (string.Equals(d, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.Delete(d, true);
                    Diag.Count("dashboard:old loads removed");
                }
            }
            catch (Exception e) { Diag.Swallowed("removing old dashboard frames", e); }
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
