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
    /// - an enemy army comes into its view, or an enemy lord comes within reach of its holdings (sighting_distance),
    ///   heads for one of them or chases one of its parties: for the first time, or after 12 hours without;
    /// - its ruler gets a notice (a war, a siege or raid on its lands, a holding taken, an army broken, ...), except
    ///   the ones kept quiet, such as fights with bandits, caravans and villagers (Notices);
    /// - a full day passes with nothing new.
    /// Enemy lords further off are still in the report, just not a reason to poke (docs/reports/2026-10-08-pokes.md).
    /// Everything that happens within the same hour is merged into one poke. The watched realm's pokes carry the
    /// full view and the written report an agent would read; the player's own pokes are kept so each recorded
    /// decision can say what the player had just seen. Every report is fog-checked before it is written, and every
    /// notice is followed to the poke that carried it (docs/LOGGING.md).
    /// Worker thread only.
    /// </summary>
    internal static class Brain
    {
        internal sealed class Memory
        {
            public readonly Dictionary<string, ViewParty> LastSeen = new Dictionary<string, ViewParty>();
            public HashSet<string> SeenLastHour = new HashSet<string>();
            /// <summary>The last hour each enemy lord or army was in view and close enough to poke for.</summary>
            public readonly Dictionary<string, double> LastClose = new Dictionary<string, double>();
            public double Started = double.NaN;
            public double LastPoke = double.NaN;
            public readonly List<Notice> Pending = new List<Notice>();
            public readonly List<Notice> News = new List<Notice>();
            public readonly List<Trigger> Recent = new List<Trigger>();
            public readonly Dictionary<string, List<double>> Sightings = new Dictionary<string, List<double>>();
            /// <summary>Each enemy army's trail, by party id (Trails).</summary>
            public readonly Dictionary<string, ViewTrail> Trails = new Dictionary<string, ViewTrail>();
            public int Pokes, PokedHoursRunning;
        }

        /// <summary>An enemy army's trail: a new point after this many map units marched, or this many hours; kept two days, at most 12 points.</summary>
        private const float TrailStep = 10f;
        private const double TrailEvery = 6, TrailKeep = 48;
        private const int TrailMost = 12;

        private const double OutOfSightHours = 12;
        private const double ForgetHours = 72;
        private const int SmallestLordParty = 50;

        private static readonly Dictionary<string, Memory> Mem = new Dictionary<string, Memory>();
        private static readonly List<string> RecentDecisions = new List<string>();
        private static readonly List<string> RecentPokes = new List<string>();
        private static string _dir;

        /// <summary>The latest hour, the whole copy (each agent keeps its own realm's view of it: RealmAgent.Latest).</summary>
        public static Snapshot Latest;
        public static int DecisionsWritten, PokesWatched, PokesPlayer;

        public static string Dir => _dir ?? (_dir = Paths.Data("unknown"));

        /// <summary>
        /// A campaign was loaded, with one agent for one realm in agent_dir (the smoke test). loadedAt is the in-game hour of its
        /// save, so the chronicle can be cut back to it; history, the game's own lines about the realm (GameHistory), to start it.
        /// </summary>
        public static void Reset(string campaign, double loadedAt = double.NaN, string realm = null, List<ChronicleEntry> history = null)
            => Reset(campaign, loadedAt, realm != null ? new List<string> { realm } : new List<string>(),
                     realm != null && history != null ? new Dictionary<string, List<ChronicleEntry>> { [realm] = history } : null);

        /// <summary>The same, for realms whose channels the settings of 0.8 name (agent_dir, agent_dir.&lt;realm&gt;).</summary>
        public static void Reset(string campaign, double loadedAt, List<string> realms, Dictionary<string, List<ChronicleEntry>> histories)
            => Reset(campaign, loadedAt, realms.Select((r, i) => AgentConfig.FromSettings(r, Settings.AgentFolderFor(r, i == 0), Settings.Agent, Settings.AgentLords)).ToList(), histories);

        /// <summary>The history of the game now running (History, since 0.9.1): which records it lived through.</summary>
        public static History History = History.Unmarked("", double.NaN);

        /// <summary>
        /// A campaign was loaded, with its agents (Agents.Configured, each realm resolved), the game's own history of each realm,
        /// and which records are this game's history (History; for none, the unlabelled records up to the save's hour).
        /// </summary>
        public static void Reset(string campaign, double loadedAt, List<AgentConfig> agents, Dictionary<string, List<ChronicleEntry>> histories, History history = null)
        {
            History = history ?? History.Unmarked(Bridge.LoadId, loadedAt);
            Files.FlushAll();
            Mem.Clear();
            RecentDecisions.Clear();
            RecentPokes.Clear();
            Latest = null;
            DecisionsWritten = PokesWatched = PokesPlayer = 0;
            _dir = Paths.Data(campaign);
            MarkLoad(loadedAt, agents);
            Tools.Runner.Reset();
            Agents.Reset(_dir, loadedAt, agents, histories, History);
            AgentChannel.WriteLibrary();
            Audit.Reset();
            LiveMap.Reset();
            Takings.Reset(_dir);
        }

        /// <summary>
        /// Where each record file stood when this load began (loads.jsonl in the campaign's folder), so the dashboard's log can show
        /// one load's pokes, answers and receipts alone: a campaign reloaded from the same save repeats its hours. Each agent's war
        /// room logs where its warroom.txt says (log_dir), so each agent's own files are marked, and the load names its agents.
        /// </summary>
        private static void MarkLoad(double loadedAt, List<AgentConfig> agents)
        {
            try
            {
                string warroom = AgentConfig.DefaultLogDir;
                long Size(string dir, string f) { string p = Path.Combine(dir, f); return File.Exists(p) ? new FileInfo(p).Length : 0; }
                var offsets = new Dictionary<string, long>
                {
                    ["orders.jsonl"] = Size(_dir, "orders.jsonl"), ["notices.jsonl"] = Size(_dir, "notices.jsonl"),
                    ["calls.jsonl"] = Size(warroom, "calls.jsonl"), ["routes.jsonl"] = Size(warroom, "routes.jsonl")
                };
                var named = agents.Where(a => a?.Realm != null).ToList();
                foreach (AgentConfig a in named)
                {
                    offsets["pokes_" + a.Realm + ".jsonl"] = Size(_dir, "pokes_" + a.Realm + ".jsonl");
                    offsets["calls.jsonl@" + a.Realm] = Size(a.LogDir ?? warroom, "calls.jsonl");
                    offsets["routes.jsonl@" + a.Realm] = Size(a.LogDir ?? warroom, "routes.jsonl");
                }
                Files.Append(Path.Combine(_dir, "loads.jsonl"), Json.Line(new
                {
                    LoadId = Bridge.LoadId, LoadedAt = double.IsNaN(loadedAt) ? (double?)null : loadedAt, Realm = named.FirstOrDefault()?.Realm, Realms = named.Select(a => a.Realm).ToList(),
                    Agents = named.Select(a => new { a.Realm, a.Workspace, a.LogDir, a.Mode }).ToList(), History = History.ForAgent(), HistoryText = History.ToString(),
                    Wall = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", Text.Inv), Offsets = offsets
                }));
                Files.FlushAll();
            }
            catch (Exception e) { Diag.Swallowed("marking the load in loads.jsonl", e); }
        }

        /// <summary>The latest hour as this realm's agent has it (the whole copy for none).</summary>
        public static Snapshot LatestFor(string realm) => Agents.Of(realm)?.Latest ?? Latest;

        /// <summary>A tool's call-back (worker): a notice for its realm, so it reaches the agent with this hour's poke.</summary>
        public static void ToolNotice(string text, string party, double hours, string place = null, string realm = null)
        {
            string fid = realm ?? Latest?.Watched;
            if (fid == null) return;
            OnNotice(new Notice { Id = NoticeIds.Next(), Hours = hours, Kind = "tool", Text = text, Factions = { fid }, Party = party, Place = place });
        }

        /// <summary>Every id the agent may legitimately be shown this hour beyond the report's own lists (for the fog check).</summary>
        private static IEnumerable<string> Known(Snapshot s, Dictionary<string, Sighting> sees, Memory m, Tools.World w)
        {
            foreach (string id in sees.Keys)
            {
                yield return id;
                if (s.PartyById.TryGetValue(id, out PartyRow r) && r.Leader != null) yield return r.Leader;
            }
            foreach (KeyValuePair<string, ViewParty> kv in m.LastSeen)
            {
                yield return kv.Key;
                if (kv.Value.Lord != null) yield return kv.Value.Lord;
            }
            if (w != null) foreach (string id in Tools.Runner.Ids(w)) if (id != null) yield return id;
            // the player's own party, in the player's realm's report (the lieutenant's, 0.10)
            if (s.PlayerParty != null && s.PartyById.TryGetValue(s.PlayerParty, out PartyRow me) && me.Faction != null && me.Faction == s.Watched)
            {
                yield return me.Id;
                if (me.Leader != null) yield return me.Leader;
            }
        }

        /// <summary>What a realm remembers: an agent's realm's is its agent's (RealmAgent.Memory); any other's (the player's) is kept here.</summary>
        public static Memory Get(string fid)
        {
            RealmAgent ag = Agents.Of(fid);
            if (ag != null) return ag.Memory;
            fid = fid ?? "none";
            if (!Mem.TryGetValue(fid, out Memory m)) Mem[fid] = m = new Memory();
            return m;
        }

        public static void OnNotice(Notice n)
        {
            Diag.Event("notice", n.Kind, new { id = n.Id, factions = n.Factions, party = n.Party, text = n.Text, quiet = n.Quiet });
            if (n.Factions.Count == 0) Diag.Count("notice:for no realm we follow");
            if (n.Kind == "party_destroyed" && n.Party != null)
            {
                foreach (Memory m in Mem.Values) m.LastSeen.Remove(n.Party);
                foreach (RealmAgent a in Agents.All) a.Memory.LastSeen.Remove(n.Party);
            }
            Files.Append(Path.Combine(Dir, "notices.jsonl"), Json.Line(n));
            Chronicle.FromNotice(n);
            if (n.Quiet != null)
            {
                // kept to the logs: no poke, not in the chronicle, and not in the report's news either
                Diag.Count("quiet");
                Diag.Count("quiet:" + n.Quiet);
                return;
            }
            if (n.RecordOnly) Diag.Count("notice:record only");
            foreach (string f in n.Factions.Distinct())
            {
                Memory m = Get(f);
                if (!n.RecordOnly) m.Pending.Add(n);
                m.News.Add(n);
                m.News.RemoveAll(x => n.Hours - x.Hours > 48);
            }
        }

        public static void OnHour(Snapshot s)
        {
            Latest = s;
            Roads.Known = s.Roads != null;
            double lagMs = s.Wall > 0 ? (DateTime.UtcNow.Ticks - s.Wall) / 10000.0 : -1;
            Diag.Event("hour", Text.When(s.Hours), new { parties = s.Parties.Count, settlements = s.Settlements.Count, worker_lag_ms = Math.Round(lagMs, 1), parts_ms = s.PartsMs?.Select(x => Math.Round(x, 3)).ToArray() });
            Audit.CheckSnapshot(s);
            Forecast.Calibrate(s);
            if (s.Sight != null) Audit.PlayerSight(s.Sight, s.Hours);
            // each agent's own hour, on its realm's view of the copy (since 0.9 every agent the same way)
            var seats = new List<KeyValuePair<RealmAgent, Snapshot>>();
            foreach (Snapshot v in s.SeatViews().Where(x => x.Watched != null))
            {
                RealmAgent ag = Agents.Serving(v.Watched);
                if (ag == null) continue;
                ag.Latest = v;
                seats.Add(new KeyValuePair<RealmAgent, Snapshot>(ag, v));
            }
            foreach (KeyValuePair<RealmAgent, Snapshot> kv in seats)
            {
                RealmAgent ag = kv.Key;
                Snapshot v = kv.Value;
                Audit.Sight(v);
                Chronicle.Ensure(v.Watched, Views.FactionName(v, v.Watched), v.Hours);
                // the tools first: their call-backs of this hour go out with this hour's poke
                if (v.WatchedSees != null) Tools.Runner.OnHour(v, ag);
                if (v.WatchedSees != null)
                    Process(v, v.Watched, v.WatchedSees, v.HostileToWatched, false);
            }
            if (s.PlayerFaction != null && !seats.Any(kv => kv.Value.Watched == s.PlayerFaction))
                Process(s, s.PlayerFaction, PlayerSees(s), s.HostileToPlayer, true);
            DropOrphanNotices(s);
            var views = new List<KeyValuePair<Snapshot, View>>();
            foreach (KeyValuePair<RealmAgent, Snapshot> kv in seats)
            {
                RealmAgent ag = kv.Key;
                Snapshot v = kv.Value;
                if ((!ag.On && !Settings.Dashboard) || v.WatchedSees == null) continue;
                // one view of the hour: the agent's hourly report is written from it, and the dashboard shows the same one
                Memory m = Get(v.Watched);
                View watchedView = Views.Build(v, v.Watched, v.WatchedSees, v.HostileToWatched, m);
                views.Add(new KeyValuePair<Snapshot, View>(v, watchedView));
                if (ag.On)
                {
                    Tools.World w = Tools.Runner.WorldFor(v);
                    // the lieutenant (0.10) serves the player: its report says where the player's own party is, and its look-ups are
                    // kept fresh every hour, since it is never poked and answers whenever the player speaks
                    bool lt = ag.Config.IsLieutenant;
                    string report = Views.Report(watchedView, null, new ReportExtras { InForce = Tools.Runner.InForce(w), Lookups = true, Ruler = lt ? Views.RulerLine(v) : null });
                    Audit.CheckReport(watchedView, report, v.WatchedSees, v.InspectedByWatched, "hourly report", Known(v, v.WatchedSees, m, w));
                    ag.Channel.OnHour(v.Hours, report);
                    if (lt)
                    {
                        try { ag.Channel.WriteLookups(Facts.Lookups(w, m.LastSeen, m.Trails)); }
                        catch (Exception e) { Diag.Error("writing the lieutenant's look-up files", e); }
                    }
                    try { ag.Channel.WriteCouncil(Facts.Council(w)); }
                    catch (Exception e) { Diag.Error("writing the council briefing", e); }
                }
            }
            if (Settings.Dashboard)
            {
                try { DashboardHour(s, views); }
                catch (Exception e) { Diag.Error("writing the dashboard's hourly picture", e); }
            }
            foreach (KeyValuePair<RealmAgent, Snapshot> kv in seats) Recruitment.OnHour(kv.Value);
        }

        /// <summary>The dashboard's frame of this hour (LiveMap): each watched realm's view, the player's, and each agent's view file for the "ask the way" tool.</summary>
        private static void DashboardHour(Snapshot s, List<KeyValuePair<Snapshot, View>> views)
        {
            var realms = new Dictionary<string, RealmFrame>();
            foreach (KeyValuePair<Snapshot, View> kv in views)
            {
                Snapshot v = kv.Key;
                // a realm of the player's with an agent of its own (the lieutenant) is still the player's frame
                realms[v.Watched] = LiveMap.Realm(v, v.Watched, kv.Value, v.WatchedSees, v.HostileToWatched, Get(v.Watched), (Agents.Of(v.Watched) ?? Agents.For(null)).On, v.Watched == s.PlayerFaction);
            }
            if (s.PlayerFaction != null && !realms.ContainsKey(s.PlayerFaction))
            {
                Dictionary<string, Sighting> mine = PlayerSees(s);
                View pv = Views.Build(s, s.PlayerFaction, mine, s.HostileToPlayer, Get(s.PlayerFaction));
                realms[s.PlayerFaction] = LiveMap.Realm(s, s.PlayerFaction, pv, mine, s.HostileToPlayer, Get(s.PlayerFaction), false, true);
            }
            LiveMap.Write(s, realms);
            foreach (KeyValuePair<Snapshot, View> kv in views) LiveMap.WriteView(kv.Key, kv.Value, kv.Key.Watched);
        }

        /// <summary>Notices for realms no longer followed (the watched realm changed): dropped, and said so.</summary>
        private static void DropOrphanNotices(Snapshot s)
        {
            // an agent's realm's notices are its agent's; what is here belongs to the player's realm or to realms no longer followed
            foreach (KeyValuePair<string, Memory> kv in Mem)
            {
                if (kv.Key == s.PlayerFaction || kv.Value.Pending.Count == 0) continue;
                Diag.Event("notice", "dropped", new { faction = kv.Key, ids = kv.Value.Pending.Select(n => n.Id).ToList(), why = "the realm is no longer followed" });
                Diag.Count("notice:dropped", kv.Value.Pending.Count);
                kv.Value.Pending.Clear();
            }
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
            var ownIds = new HashSet<string>(own.Select(h => h.Id));
            bool first = double.IsNaN(m.Started);
            if (first)
            {
                m.Started = s.Hours;
                triggers.Add(new Trigger { Hours = s.Hours, Kind = "first_look", Text = "First look since the campaign was loaded." });
            }

            var close = new HashSet<string>();
            foreach (KeyValuePair<string, Sighting> kv in sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r)) continue;
                if (r.Kind != "lord" || r.Attached || !hostile.Contains(r.Faction ?? "")) continue;
                if (r.ArmyMen == 0 && r.Men < SmallestLordParty) continue;
                ViewParty vp = Views.Party(s, r, kv.Value, "enemy", own);
                if (!Close(s, fid, r, vp, own, ownIds))
                {
                    // logged where the old rule would have poked: newly in view, after 12 hours out of sight
                    bool wasNew = !m.SeenLastHour.Contains(r.Id)
                                  && !(m.LastSeen.TryGetValue(r.Id, out ViewParty lastView) && s.Hours - lastView.SeenHours < OutOfSightHours);
                    if (!first && !isPlayer && wasNew)
                    {
                        Diag.Count("sighting:far off");
                        Diag.Event("sighting", "far off", new { party = r.Id, name = vp.Name, distance = vp.HoldingDistance, from = vp.NearestHolding, doing = vp.Doing });
                    }
                    continue;
                }
                close.Add(r.Id);
                if (first || (m.LastClose.TryGetValue(r.Id, out double last) && s.Hours - last < OutOfSightHours)) continue;
                int men = r.ArmyMen > 0 ? r.ArmyMen : r.Men;
                string where = vp.NearestHolding != null ? Roads.Both(vp.HoldingDistance, vp.HoldingRoad) + " from " + vp.NearestHolding : "near " + (vp.Near ?? "nowhere known");
                var sighting = new Trigger
                {
                    Hours = s.Hours, Kind = "enemy_in_view", Party = r.Id,
                    Text = "Enemy " + (r.ArmyMen > 0 ? "army" : "lord") + " in view: " + vp.Name + " (" + vp.FactionName + "), "
                           + Text.N(men) + " men, strength " + Text.N((int)Math.Round(vp.Strength)) + ", " + where + "; " + vp.Doing + "; seen by " + kv.Value.By + "."
                };
                triggers.Add(sighting);
                if (!isPlayer) Sighted(m, s, r, vp);
                // an enemy army coming into view is history; a lord passing by is not
                if (!isPlayer && r.ArmyMen > 0) Chronicle.For(fid).Line(s.Hours, "sighting", sighting.Text, party: r.Id);
            }
            foreach (string id in close) m.LastClose[id] = s.Hours;
            foreach (string id in m.LastClose.Where(x => s.Hours - x.Value > ForgetHours).Select(x => x.Key).ToList()) m.LastClose.Remove(id);

            m.SeenLastHour = new HashSet<string>(sees.Keys);
            foreach (KeyValuePair<string, Sighting> kv in sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r) || r.Attached || r.Faction == fid) continue;
                if (r.Kind != "lord" && r.Kind != "bandit") continue;
                m.LastSeen[r.Id] = Views.Party(s, r, kv.Value, hostile.Contains(r.Faction ?? "") ? "enemy" : "neutral", null);
            }
            foreach (string id in m.LastSeen.Where(x => s.Hours - x.Value.SeenHours > ForgetHours).Select(x => x.Key).ToList())
                m.LastSeen.Remove(id);
            if (!isPlayer) Trails(m, s, sees, hostile, own);

            foreach (Notice n in m.Pending)
                triggers.Add(new Trigger { Hours = n.Hours, Kind = n.Kind, Text = n.Text, Party = n.Party, Place = n.Place, Notice = n.Id });
            m.Pending.Clear();

            if (triggers.Count == 0)
            {
                double since = double.IsNaN(m.LastPoke) ? m.Started : m.LastPoke;
                if (s.Hours - since >= 24)
                    triggers.Add(new Trigger { Hours = s.Hours, Kind = "quiet_day", Text = "A full day has passed with nothing new." });
            }
            if (triggers.Count == 0)
            {
                m.PokedHoursRunning = 0;
                return;
            }

            m.LastPoke = s.Hours;
            m.Pokes++;
            m.Recent.AddRange(triggers);
            m.Recent.RemoveAll(t => s.Hours - t.Hours > 24);

            // the record of the player's own play keeps its pokes when the player's realm has an agent of its own (the
            // lieutenant), which then takes the place of the player's pass above (Oct 22)
            if (isPlayer || fid == s.PlayerFaction)
            {
                PokesPlayer++;
                Files.Append(Path.Combine(Dir, "record", "pokes_you.jsonl"),
                    Json.Line(new { Type = "poke", Faction = fid, Hours = s.Hours, Date = s.Date, Triggers = triggers, Seat = !isPlayer }));
                if (isPlayer) return;
            }

            PokesWatched++;
            Channel ch = AgentChannel.For(fid);
            int number = ch.NextNumber(Path.Combine(Dir, "pokes_" + fid + ".jsonl"));
            View v = Views.Build(s, fid, sees, hostile, m);
            Tools.World w = Tools.Runner.WorldFor(s);
            var extras = new ReportExtras
            {
                Since = ch.On ? ch.SinceLastAnswer(number) : null, LastAnswered = ch.LastAnswered,
                InForce = Tools.Runner.InForce(w), Facts = Facts.ForPoke(w, triggers), Lookups = ch.On
            };
            string report = Views.Report(v, triggers, extras);
            Audit.CheckReport(v, report, sees, s.InspectedByWatched, "report of poke #" + number, Known(s, sees, m, w));
            if (ch.On)
            {
                // the look-ups go out before the poke, so they are there when the agent wakes
                try { ch.WriteLookups(Facts.Lookups(w, m.LastSeen, m.Trails)); }
                catch (Exception e) { Diag.Error("writing the look-up files", e); }
            }
            Files.Append(Path.Combine(Dir, "pokes_" + fid + ".jsonl"),
                Json.Line(new { Type = "poke", Number = number, Faction = fid, v.FactionName, Hours = s.Hours, Date = s.Date, Triggers = triggers, View = v, Report = report }));
            Files.Overwrite(Path.Combine(Dir, "reports", fid + "_latest.txt"), report);
            // the player's lieutenant is never poked (0.10): it speaks when spoken to; what became of its orders goes into the chat box
            if (ch.IsLieutenant) Lieutenant.Heard(triggers);
            if (ch.On && !ch.IsLieutenant)
                ch.OnPoke(number, s.Hours, triggers, report, new
                {
                    Number = number, Hours = s.Hours, Date = Text.When(s.Hours), Campaign = s.Campaign, Realm = fid, RealmName = v.FactionName,
                    LoadId = s.LoadId, LoadedAt = double.IsNaN(s.LoadedAt) ? (double?)null : s.LoadedAt,
                    LoadedAtDate = double.IsNaN(s.LoadedAt) ? null : Text.When(s.LoadedAt), ChronicleLines = Chronicle.For(fid).Count,
                    History = History.ForAgent()
                });

            Dictionary<string, int> sizes = Audit.Sizes(report, out int tokens);
            Diag.Count("pokes");
            foreach (string kind in triggers.Select(t => t.Kind).Distinct()) Diag.Count("poke:" + kind);
            Diag.Sample("report:tokens", tokens);
            if (v.SeenNotListed > 0) Diag.Count("report:cap hit");
            double lagMs = s.Wall > 0 ? (DateTime.UtcNow.Ticks - s.Wall) / 10000.0 : -1;
            Diag.Event("poke", "#" + number, new
            {
                number, faction = fid, triggers = triggers.Select(t => t.Kind).ToList(), notices = triggers.Where(t => t.Notice.HasValue).Select(t => t.Notice.Value).ToList(),
                tokens, sections = sizes, seen = v.Seen.Count, not_listed = v.SeenNotListed, last_seen = v.LastSeen.Count,
                written_ms_after_the_hour = Math.Round(lagMs, 1), to_agent = ch.On
            });
            m.PokedHoursRunning++;
            if (m.PokedHoursRunning == 12)
                Diag.Alarm("poke_storm", v.FactionName + " has been poked every in-game hour for 12 hours running; the agent never gets a quiet hour.");

            string line = Text.When(s.Hours) + ": " + v.FactionName + " poked (#" + number + "). " + triggers[0].Text
                          + (triggers.Count > 1 ? " (and " + (triggers.Count - 1) + " more)" : "");
            Log.Line("poke: " + line);
            RecentPokes.Add(line);
            if (RecentPokes.Count > 50) RecentPokes.RemoveAt(0);
            if (Settings.Notify && !ch.IsLieutenant)
            {
                string say = v.FactionName + ": " + triggers[0].Text + (triggers.Count > 1 ? " (+" + (triggers.Count - 1) + ")" : "");
                Background.ToMain.Enqueue(() => Ui.Say(say, Colors.Yellow));
            }
        }

        /// <summary>
        /// Each enemy army's trail (version 0.6.4; Mert, Oct 16: an army that turns aside to recruit, gather supplies or strike
        /// a weak enemy has not changed its main aim, and the agent should be able to see that). Each hour an enemy army is
        /// in view, a point is added when its course changed (what it is doing, or where it is going), when it has marched
        /// TrailStep map units since the last point, when TrailEvery hours have passed, or when it came back into sight.
        /// Points older than two days go, at most TrailMost are kept, and a trail ends when the army is seen broken up or
        /// is gone. Only what the realm saw: the fog rule holds.
        /// </summary>
        private static void Trails(Memory m, Snapshot s, Dictionary<string, Sighting> sees, HashSet<string> hostile, List<SettlementRow> own)
        {
            var ended = new List<string>();
            foreach (KeyValuePair<string, Sighting> kv in sees)
            {
                if (!s.PartyById.TryGetValue(kv.Key, out PartyRow r) || r.Attached || r.Kind != "lord" || !hostile.Contains(r.Faction ?? "")) continue;
                if (r.ArmyMen <= 0)
                {
                    // seen without its army: the army broke up
                    if (m.Trails.ContainsKey(r.Id)) ended.Add(r.Id);
                    continue;
                }
                ViewParty vp = Views.Party(s, r, kv.Value, "enemy", own);
                if (!m.Trails.TryGetValue(r.Id, out ViewTrail t)) m.Trails[r.Id] = t = new ViewTrail { Id = r.Id };
                t.Lord = r.Leader;
                t.Name = vp.Name;
                t.FactionName = vp.FactionName;
                t.ArmyMen = r.ArmyMen;
                t.ArmyParties = r.ArmyParties;
                t.Strength = vp.Strength;
                TrailPoint last = t.Points.Count > 0 ? t.Points[t.Points.Count - 1] : null;
                double gap = double.IsNaN(t.LastSeenHours) ? 0 : s.Hours - t.LastSeenHours;
                bool back = last != null && gap >= 1.5;
                if (last == null || back || vp.Doing != last.Doing || Views.Dist(last.X, last.Y, r.X, r.Y) >= TrailStep || s.Hours - last.Hours >= TrailEvery - 1e-6)
                    t.Points.Add(new TrailPoint
                    {
                        Hours = s.Hours, X = r.X, Y = r.Y, Near = vp.Near, NearDistance = vp.NearDistance, NearRoad = vp.NearRoad, NearestHolding = vp.NearestHolding,
                        HoldingDistance = vp.HoldingDistance, HoldingRoad = vp.HoldingRoad, Doing = vp.Doing, AfterGap = back ? gap : 0
                    });
                t.LastSeenHours = s.Hours;
            }
            foreach (KeyValuePair<string, ViewTrail> kv in m.Trails)
            {
                ViewTrail t = kv.Value;
                t.InView = sees.ContainsKey(kv.Key);
                t.Points.RemoveAll(p => s.Hours - p.Hours > TrailKeep);
                if (t.Points.Count > TrailMost) t.Points.RemoveRange(0, t.Points.Count - TrailMost);
                if (t.Points.Count == 0 || !s.PartyById.ContainsKey(kv.Key)) ended.Add(kv.Key);
            }
            foreach (string id in ended.Distinct()) m.Trails.Remove(id);
        }

        private static readonly HashSet<string> HeadingIn = new HashSet<string> { "GoToSettlement", "RaidSettlement", "BesiegeSettlement", "AssaultSettlement", "PatrolAroundPoint" };

        /// <summary>
        /// Close enough to poke for: an army; a lord within sighting_distance of one of the realm's holdings; or one
        /// heading for, besieging or raiding one of them, or chasing one of the realm's parties.
        /// </summary>
        private static bool Close(Snapshot s, string fid, PartyRow r, ViewParty vp, List<SettlementRow> own, HashSet<string> ownIds)
        {
            if (r.ArmyMen > 0 || own.Count == 0 || Views.Reach(vp) < Settings.SightingDistance) return true;   // within reach by road, where the game's table knows the way
            if ((r.Besieging != null && ownIds.Contains(r.Besieging)) || (r.Raiding != null && ownIds.Contains(r.Raiding))) return true;
            if (r.TargetSettlement != null && ownIds.Contains(r.TargetSettlement) && HeadingIn.Contains(r.Doing ?? "")) return true;
            return r.Doing == "EngageParty" && r.TargetParty != null && s.PartyById.TryGetValue(r.TargetParty, out PartyRow chased) && chased.Faction == fid;
        }

        /// <summary>
        /// Two things worth knowing about a new sighting: the same enemy setting off pokes again and again at the edge
        /// of sight, and a lord who lost his party and raised a new one, which looks like a stranger.
        /// </summary>
        private static void Sighted(Memory m, Snapshot s, PartyRow r, ViewParty vp)
        {
            if (!m.Sightings.TryGetValue(r.Id, out List<double> times)) m.Sightings[r.Id] = times = new List<double>();
            times.Add(s.Hours);
            times.RemoveAll(t => s.Hours - t > 24);
            if (times.Count == 3)
                Diag.Alarm("edge_flicker", vp.Name + " has set off 3 sightings in a day for " + Views.FactionName(s, s.Watched)
                                           + ", coming in and out of sight at its edge. Each one is a poke.", false, new { party = r.Id });
            if (r.Leader == null) return;
            ViewParty twin = m.LastSeen.Values.FirstOrDefault(x => x.Lord == r.Leader && x.Id != r.Id);
            if (twin != null)
            {
                Diag.Count("sighting:same lord, new party");
                Diag.Event("sighting", "same lord, new party", new { lord = r.Leader, old_party = twin.Id, new_party = r.Id });
                Diag.Alarm("lord_new_party", vp.Name + " was seen with a new party (" + r.Id + ", before " + twin.Id + "), so the sighting counted as a stranger's.");
            }
        }

        /// <summary>For the console: the watched realm's report as of now, without counting as a poke.</summary>
        public static string ReportNow(Snapshot all, string realm = null)
        {
            Latest = all;
            Snapshot s = realm == null ? all : all.SeatViews().FirstOrDefault(x => x.Watched == realm);
            if (s == null) return "No watched realm has the id '" + realm + "'.";
            if (s.Watched == null || s.WatchedSees == null) return "No realm is under watch.";
            View v = Views.Build(s, s.Watched, s.WatchedSees, s.HostileToWatched, Get(s.Watched));
            string report = Views.Report(v, null);
            Audit.CheckReport(v, report, s.WatchedSees, s.InspectedByWatched, "report shown by agents.view");
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
            Diag.Count("record:decisions");
            string line = Text.When(d.Hours) + ": " + d.Text;
            RecentDecisions.Add(line);
            if (RecentDecisions.Count > 50) RecentDecisions.RemoveAt(0);
            Log.Line("recorded: " + line);
        }

        public static void WriteRecord(string file, object o)
        {
            if (file == "events.jsonl") Diag.Count("record:events");
            Files.Append(Path.Combine(Dir, "record", file), Json.Line(o));
        }

        public static void WriteOrder(object o) => Files.Append(Path.Combine(Dir, "orders.jsonl"), Json.Line(o));

        /// <summary>A record in the campaign's own folder (handbacks.jsonl, ...).</summary>
        public static void WriteData(string file, object o) => Files.Append(Path.Combine(Dir, file), Json.Line(o));

        /// <summary>Every lord we ever handed back at a save in this campaign, from handbacks.jsonl.</summary>
        public static List<string> ReadHandbackIds(string campaign)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string path = Path.Combine(Paths.Data(campaign), "handbacks.jsonl");
                if (!File.Exists(path)) return new List<string>();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0) continue;
                        var lords = Newtonsoft.Json.Linq.JObject.Parse(line)["lords"] as Newtonsoft.Json.Linq.JArray;
                        if (lords == null) continue;
                        foreach (var t in lords) { string id = (string)t; if (!string.IsNullOrEmpty(id)) ids.Add(id); }
                    }
                }
            }
            catch (Exception e) { Diag.Swallowed("reading the hand-back records", e); }
            return ids.ToList();
        }

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
            try { InformationManager.DisplayMessage(new InformationMessage("[Agents] " + text, color)); }
            catch (Exception e) { Diag.Swallowed("on-screen message", e); }
        }
    }
}
