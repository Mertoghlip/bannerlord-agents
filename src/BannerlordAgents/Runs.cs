using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Library;

namespace BannerlordAgents
{
    /// <summary>
    /// A measured test run (0.11; Mert, Oct 23: "All tests will be for 60 in-game days, starting from the TEST START save";
    /// docs/RUNS.md). Loading the run's save (run_save, TEST START) readies a run; it begins on the first in-game hour the clock
    /// runs (after any setting up), named by who commands each side (RunRules.Arm: A you against the agents, B you against the
    /// game's AI, C1 an agent ruling your kingdom against the agents, C2 against the game's AI). For its 60 days it keeps, in its
    /// own folder (runs\(id)\): run.json (what it is, its loads, its saves, what spoiled it), setup.json (versions, mods, each
    /// agent's mode, settings and the fingerprints of its instructions, the save's), days.jsonl (the scoreboard every 24 in-game
    /// hours from the start, day 0 included) and events.jsonl (battles, holdings, lords taken, killed and freed, sieges, raids,
    /// armies, wars, with the numbers; plus the run's share of the detailed log: orders, pokes, answers, the lieutenant, your
    /// decisions, alarms, freezes, speed; Diag tees them). At day 60 it pauses the game and offers a save. A load of one of the
    /// run's own saves (an autosave after a crash) carries it on; any other save leaves it waiting. Every run of a campaign starts
    /// from the same save, so these folders, not the campaign's, tell runs apart.
    /// Game's thread, except the writing (worker).
    /// </summary>
    internal static class Run
    {
        private static RunState _run;
        private static Snapshot _last;
        private static readonly HashSet<string> Realms = new HashSet<string>(StringComparer.Ordinal);
        // recruits since the last day line, by realm (counted on the game's hot path without allocating)
        private static readonly Dictionary<string, int> Recruits = new Dictionary<string, int>(StringComparer.Ordinal);
        private static bool _trueSightFlagged, _cheatFlagged;
        private static DateTime _lastLineAt = DateTime.Now;

        /// <summary>Where the detailed log's run lines go (Diag's writer reads it); null when no run is going.</summary>
        public static volatile string EventsFile;

        public static bool Active => _run != null && _run.Status == "running";
        public static RunState Current => _run;

        // ---------- the load ----------

        /// <summary>Bridge.Start: the run's save readies a run; one of a running run's own saves carries it on; any other leaves it waiting.</summary>
        public static void OnLoad(string save, double loadedAt, string loadId)
        {
            _last = null;
            EventsFile = null;
            RunTags.Set(null, null);
            Recruits.Clear();
            Battles.Begun.Clear();
            _trueSightFlagged = _cheatFlagged = false;
            RunState open = RunFiles.ReadOpen();
            string wall = Now();
            if (!string.IsNullOrEmpty(save) && string.Equals(save.Trim(), Settings.RunSave, StringComparison.OrdinalIgnoreCase))
            {
                if (open != null && open.Status == "running")
                {
                    open.Flag(wall, open.LastHours, "abandoned", "The run's save (" + save + ") was loaded again on day " + open.LastDay + ", so this run ends unfinished.");
                    open.Status = "abandoned";
                    open.Finished = wall;
                    open.CloseSegment(wall, open.LastHours);
                    RunFiles.Write(open);
                }
                Kingdom k = Bridge.Resolve(Settings.RunKingdom.Length > 0 ? Settings.RunKingdom : "player");
                _run = new RunState
                {
                    Id = DateTime.Now.ToString("yyyyMMdd-HHmm", Text.Inv), Status = "armed", Save = save.Trim(), Campaign = Bridge.CampaignId, H0 = loadedAt, LastHours = loadedAt,
                    Days = Settings.RunDays, Kingdom = k?.StringId, KingdomName = k != null ? Names.Faction(k) : null, Rivals = Settings.RunRivals.ToList(), Armed = wall
                };
                _run.Segments.Add(new RunSegment { Load = loadId, From = loadedAt, WallFrom = wall, How = "the run's save loaded" });
                RunFiles.Write(_run, current: true);
                Say("Test run ready: " + save.Trim() + " is loaded. It begins when the clock first runs, so set up first (agents.run check shows who commands what).");
                Diag.Event("run", "armed", new { run = _run.Id, save, hours = loadedAt, kingdom = _run.Kingdom });
                return;
            }
            // a save made while setting up, before the clock first ran (0.11.1; Mert saved TEST A first): it readies the run again from there
            if (open != null && open.Status == "armed" && !string.IsNullOrEmpty(save) && open.Saves.Contains(save.Trim()))
            {
                _run = open;
                open.CloseSegment(wall, open.LastHours);
                open.Segments.Add(new RunSegment { Load = loadId, From = loadedAt, WallFrom = wall, How = "readied again from " + save + " (saved while setting up)" });
                open.H0 = loadedAt;
                open.LastHours = loadedAt;
                RunFiles.Write(open, current: true);
                Say("Test run ready again from " + save.Trim() + " (saved before its clock first ran). It begins when the clock runs.");
                Diag.Event("run", "readied again", new { run = open.Id, save, hours = loadedAt });
                return;
            }
            if (open != null && open.Status == "running" && !string.IsNullOrEmpty(save) && open.Saves.Contains(save.Trim()))
            {
                _run = open;
                if (loadedAt < open.LastHours - 0.5)
                    open.Flag(wall, loadedAt, "replayed", "Carried on from its save " + save + ": the " + Text.D((float)(open.LastHours - loadedAt)) + " in-game hours after that save are played again.");
                open.CloseSegment(wall, open.LastHours);
                open.Segments.Add(new RunSegment { Load = loadId, From = loadedAt, WallFrom = wall, How = "carried on from " + save });
                open.LastHours = loadedAt;
                open.LastDay = Math.Max(0, RunRules.Day(loadedAt, open.H0) - 1);
                if (Diag.PreviousLaunchNote != null) open.Flag(wall, loadedAt, "crash", Diag.PreviousLaunchNote);
                Realms.Clear();
                Realms.UnionWith(open.AllRealms());
                EventsFile = RunFiles.Events(open.Id);
                RunTags.Set(open.Id, open.Arm);
                RunFiles.Write(open, current: true);
                Say("Test run " + open.Arm + " (" + open.Id + ") carries on from " + save + ", day " + RunRules.Day(loadedAt, open.H0) + " of " + open.Days + ".");
                Diag.Event("run", "carried on", new { run = open.Id, save, hours = loadedAt });
                return;
            }
            _run = null;
            if (open != null && open.Status == "running")
                Say("Test run " + open.Arm + " (" + open.Id + ") waits: this save is not one of its own. Load " + (open.Saves.LastOrDefault() ?? open.Save) + " to carry it on.");
        }

        // ---------- the hours ----------

        /// <summary>Every in-game hour, after the hourly copy: the start, the day lines, the checks, the end.</summary>
        public static void OnHour(Snapshot snap)
        {
            if (_run == null) return;
            double h = CampaignTime.Now.ToHours;
            if (_run.Status == "armed") Begin(snap, h);
            if (_run.Status != "running") return;
            _last = snap;
            _run.LastHours = h;
            Check(h);
            while (h >= _run.H0 + 24.0 * (_run.LastDay + 1) - 1e-6 && _run.LastDay < _run.Days) DayLine(++_run.LastDay, h, snap);
            if (RunRules.Over(h, _run.H0, _run.Days)) Finish(h);
        }

        private static void Begin(Snapshot snap, double h)
        {
            string wall = Now();
            _run.Who = WhoCommands();
            _run.Lieutenant = Bridge.Configs.Any(c => c.IsLieutenant && c.Mode == "live" && c.Realm == _run.Kingdom);
            _run.Arm = RunRules.Arm(_run.Who.TryGetValue(_run.Kingdom ?? "", out string ruler) ? ruler : "none", _run.Rivals.Select(r => _run.Who.TryGetValue(r, out string w) ? w : "none"));
            _run.Status = "running";
            _run.Started = wall;
            string armedId = _run.Id;
            _run.Id = _run.Id.EndsWith("-" + _run.Arm, StringComparison.Ordinal) ? _run.Id : _run.Id + "-" + _run.Arm;
            // the folder it was readied under holds only that run.json: it goes (0.11.1), so only the named run is left
            if (armedId != _run.Id) Background.Post("run folder", () => RunFiles.DropArmed(armedId));
            if (h - _run.H0 > 2) _run.Flag(wall, h, "late start", "The clock ran " + Text.D((float)(h - _run.H0)) + " in-game hours between the load and the first hour of the run (setting up).");
            Realms.Clear();
            Realms.UnionWith(_run.AllRealms());
            _run.WarAtStart = Wars();
            EventsFile = RunFiles.Events(_run.Id);
            RunTags.Set(_run.Id, _run.Arm);
            RunFiles.Write(_run, current: true);
            RunSetup setup = Setup();
            string id = _run.Id;
            Background.Post("run setup", () => RunFiles.WriteSetup(id, setup));
            _lastLineAt = DateTime.Now;
            DayLine(0, h, snap);
            Say("Test run " + _run.Arm + " began (" + _run.Id + "): " + string.Join("; ", _run.Who.Select(kv => RealmName(kv.Key) + " commanded by " + WhoWords(kv.Value)))
                + ". It ends after " + _run.Days + " in-game days.");
            Diag.Event("run", "began", new { run = _run.Id, arm = _run.Arm, who = _run.Who, lieutenant = _run.Lieutenant, hours = h });
        }

        /// <summary>Each hour: true sight or cheat mode switched on, and a change in who commands a side.</summary>
        private static void Check(double h)
        {
            string wall = Now();
            if (Campaign.Current.TrueSight && !_trueSightFlagged)
            {
                _trueSightFlagged = true;
                _run.Flag(wall, h, "true sight", "True sight (a cheat that shows every party to the player) was on on day " + RunRules.Day(h, _run.H0) + ".");
                RunFiles.Write(_run);
            }
            bool cheats = false;
            try { cheats = TaleWorlds.Core.Game.Current?.CheatMode == true; }
            catch (Exception e) { Diag.Swallowed("reading cheat mode", e); }
            if (cheats && !_cheatFlagged)
            {
                _cheatFlagged = true;
                _run.Flag(wall, h, "cheat mode", "The game's cheat mode was on on day " + RunRules.Day(h, _run.H0) + " (setting up a run may need it; the console's cheats are not logged one by one).");
                RunFiles.Write(_run);
            }
            Dictionary<string, string> who = WhoCommands();
            foreach (KeyValuePair<string, string> kv in who)
            {
                if (_run.Who.TryGetValue(kv.Key, out string was) && was == kv.Value) continue;
                _run.Flag(wall, h, "commander changed", RealmName(kv.Key) + " is now commanded by " + WhoWords(kv.Value) + " (it was " + WhoWords(was) + "), on day " + RunRules.Day(h, _run.H0) + ".");
                _run.Who[kv.Key] = kv.Value;
                RunFiles.Write(_run);
            }
        }

        private static void Finish(double h)
        {
            string wall = Now();
            _run.Status = "finished";
            _run.Finished = wall;
            _run.CloseSegment(wall, h);
            RunFiles.Write(_run, current: true);
            Diag.Event("run", "finished", new { run = _run.Id, arm = _run.Arm, hours = h });
            EventsFile = null;
            RunTags.Set(null, null);
            // the run's bundle, for the dataset builder (0.13, C5): packed by Runs.exe beside the game, once the files are flushed
            RunFiles.StartBundle(_run.Id);
            string name = "TEST END " + _run.Arm + " " + _run.Id;
            try
            {
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                InformationManager.ShowInquiry(new InquiryData("Test run " + _run.Arm + " is over",
                    "Day " + _run.Days + " of the run is reached and the game is paused. Its numbers are written (runs\\" + _run.Id + ").\n\nSave the game now as \"" + name + "\"?",
                    true, true, "Save", "Not now",
                    () => { try { Campaign.Current.SaveHandler.SaveAs(name); } catch (Exception e) { Diag.Error("saving the run's end", e); } },
                    null), true);
            }
            catch (Exception e) { Diag.Error("the run's end", e); }
            Say("Test run " + _run.Arm + " is over: day " + _run.Days + " reached. The game is paused.");
        }

        /// <summary>agents.run stop: the run ends here, early.</summary>
        public static string Stop()
        {
            if (_run == null) return "No test run is going.";
            string wall = Now();
            double h = CampaignTime.Now.ToHours;
            _run.Flag(wall, h, "stopped", "Stopped by hand on day " + RunRules.Day(h, _run.H0) + ".");
            _run.Status = "stopped";
            _run.Finished = wall;
            _run.CloseSegment(wall, h);
            RunFiles.Write(_run, current: true);
            EventsFile = null;
            RunTags.Set(null, null);
            Diag.Event("run", "stopped", new { run = _run.Id, hours = h });
            string id = _run.Id;
            _run = null;
            return "Test run " + id + " stopped. Its files stay in runs\\" + id + ".";
        }

        /// <summary>A clan joining or leaving one of the run's realms, with its purse (0.11.3: hired clans leaving took millions out of the orcs' count).</summary>
        public static void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom)
        {
            if (!Active || clan == null) return;
            string from = oldKingdom?.StringId, to = newKingdom?.StringId;
            if (!Realms.Contains(from ?? "") && !Realms.Contains(to ?? "")) return;
            Diag.Event("run", "clan moved", new
            {
                run = _run.Id, day = Day(), clan = Names.Faction(clan), id = clan.StringId, from, to, hired = clan.IsUnderMercenaryService, gold = clan.Gold,
                lords = clan.Heroes.Count(h => h != null && h.IsLord && h.IsAlive)
            });
        }

        /// <summary>What was done to set the run up before its clock ran (the playerless switch, 0.11.2), kept in run.json.</summary>
        public static void SetUp(string text)
        {
            if (_run == null) return;
            _run.SetUp.Add(Now() + " " + text);
            RunFiles.Write(_run);
        }

        /// <summary>agents.run note (text): a line of your own in the run, with its day.</summary>
        public static string Note(string text)
        {
            if (!Active) return "No test run is going, so the note was not kept.";
            double h = CampaignTime.Now.ToHours;
            Diag.Event("run", "note", new { run = _run.Id, day = RunRules.Day(h, _run.H0), text });
            return "Noted in run " + _run.Id + ", day " + RunRules.Day(h, _run.H0) + ".";
        }

        /// <summary>
        /// One of the run's own saves (made by the game or by you), so a load of it carries the run on; since 0.11.1 one made while
        /// setting up, before the clock first ran, too, whose load readies the run again from there.
        /// </summary>
        public static void OnSaved(string name)
        {
            if (_run == null || (_run.Status != "running" && _run.Status != "armed") || string.IsNullOrEmpty(name) || _run.Saves.Contains(name)) return;
            _run.Saves.Add(name);
            RunFiles.Write(_run);
        }

        // ---------- what happens ----------

        public static void OnRecruited(Hero recruiter, int amount)
        {
            if (!Active || recruiter == null) return;
            string f = recruiter.MapFaction?.StringId;
            if (f == null || !Realms.Contains(f)) return;
            Recruits.TryGetValue(f, out int n);
            Recruits[f] = n + amount;
        }

        /// <summary>
        /// A battle's sides as it began (0.11.3): by the time the game says a battle has ended, the losing lords are already taken
        /// from their parties (MapEvent.CaptureDefeatedPartyMembers runs before FinalizeEvent raises MapEventEnded), so run C1's
        /// record counted the loser's lords as 0 in 1,131 of 1,132 decided battles, and named him by his party. Kept per battle
        /// until its end; a party that joins later is counted at the end.
        /// </summary>
        private sealed class BattleStart
        {
            public int AMen, DMen, ALords, DLords;
            public string ALeader, DLeader, ABy, DBy;
        }

        // kept apart from Run's own fields: a field of a game type would make the smoke test, which runs without the game, load the game (Diag reads Run.EventsFile)
        private static class Battles
        {
            public static readonly Dictionary<MapEvent, BattleStart> Begun = new Dictionary<MapEvent, BattleStart>();
        }

        public static void OnBattleStarted(MapEvent me)
        {
            if (!Active || me == null || me.AttackerSide == null || me.DefenderSide == null) return;
            string a = me.AttackerSide.MapFaction?.StringId, d = me.DefenderSide.MapFaction?.StringId;
            if (!Realms.Contains(a ?? "") && !Realms.Contains(d ?? "")) return;
            if (Battles.Begun.Count > 500) Battles.Begun.Clear();   // battles whose end never came (a load in between)
            Battles.Begun[me] = new BattleStart
            {
                AMen = Men(me.AttackerSide), DMen = Men(me.DefenderSide), ALords = Lords(me.AttackerSide), DLords = Lords(me.DefenderSide),
                ALeader = Leader(me.AttackerSide), DLeader = Leader(me.DefenderSide), ABy = CommandedBy(me.AttackerSide), DBy = CommandedBy(me.DefenderSide)
            };
        }

        private static int Men(MapEventSide side)
        {
            int n = 0;
            foreach (MapEventParty mp in side.Parties) n += mp.Party?.NumberOfHealthyMembers ?? 0;
            return n;
        }

        public static void OnBattleEnded(MapEvent me)
        {
            if (!Active || me == null) return;
            string a = me.AttackerSide?.MapFaction?.StringId, d = me.DefenderSide?.MapFaction?.StringId;
            Battles.Begun.TryGetValue(me, out BattleStart b0);
            Battles.Begun.Remove(me);
            if (!Realms.Contains(a ?? "") && !Realms.Contains(d ?? "")) return;
            int aMen = Math.Max(me.AttackerSide.HealthyTroopCountAtMapEventStart, b0?.AMen ?? 0), dMen = Math.Max(me.DefenderSide.HealthyTroopCountAtMapEventStart, b0?.DMen ?? 0);
            int aLost = me.AttackerSide.TroopCasualties, dLost = me.DefenderSide.TroopCasualties;
            int aLords = Math.Max(Lords(me.AttackerSide), b0?.ALords ?? 0), dLords = Math.Max(Lords(me.DefenderSide), b0?.DLords ?? 0);
            string winner = !me.HasWinner ? "none" : me.WinningSide == TaleWorlds.Core.BattleSideEnum.Attacker ? "attacker" : "defender";
            string kind = me.IsSiegeAssault ? "storm" : me.IsSallyOut ? "sally" : me.IsRaid ? "raid" : me.IsSiegeOutside ? "siege outside" : "field";
            if (winner != "none")
            {
                string w = winner == "attacker" ? a : d, l = winner == "attacker" ? d : a;
                Tally(w).BattlesWon++;
                Tally(l).BattlesLost++;
                if (aLords > 0 && dLords > 0) { Tally(w).LordBattlesWon++; Tally(l).LordBattlesLost++; }
            }
            Tally(a).MenLost += aLost;
            Tally(d).MenLost += dLost;
            Tally(a).EnemyFallen += dLost;
            Tally(d).EnemyFallen += aLost;
            Diag.Event("run", "battle", new
            {
                run = _run.Id, day = RunRules.Day(CampaignTime.Now.ToHours, _run.H0), kind, place = me.MapEventSettlement != null ? Names.Settlement(me.MapEventSettlement) : null, winner,
                attacker = new { realm = a, leader = b0?.ALeader ?? Leader(me.AttackerSide), by = b0?.ABy ?? CommandedBy(me.AttackerSide), men = aMen, lost = aLost, lords = aLords },
                defender = new { realm = d, leader = b0?.DLeader ?? Leader(me.DefenderSide), by = b0?.DBy ?? CommandedBy(me.DefenderSide), men = dMen, lost = dLost, lords = dLords },
                you_fought = MobileParty.MainParty != null && MobileParty.MainParty.MapEvent == me
            });
        }

        public static void OnOwnerChanged(Settlement s, Hero newOwner, Hero oldOwner, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail how)
        {
            if (!Active || s == null) return;
            string to = newOwner?.MapFaction?.StringId, from = oldOwner?.MapFaction?.StringId;
            if (!Realms.Contains(to ?? "") && !Realms.Contains(from ?? "")) return;
            string kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : "other";
            // the owning clans too (0.11.3): a holding taken goes to the ruler's clan until a vote gives it out, and that vote is a
            // holding given within the realm, which run C1's record could not show
            string toClan = newOwner?.Clan != null ? Names.Faction(newOwner.Clan) : null, fromClan = oldOwner?.Clan != null ? Names.Faction(oldOwner.Clan) : null;
            if (to == from)
            {
                if (s.IsVillage) return;   // a castle's or town's villages follow it
                Diag.Event("run", "holding given", new { run = _run.Id, day = Day(), place = Names.Settlement(s), id = s.StringId, kind, realm = to, from_clan = fromClan, to_clan = toClan, how = how.ToString() });
                return;
            }
            Tally(to).Gained(kind);
            Tally(from).Lost(kind);
            Diag.Event("run", "holding", new { run = _run.Id, day = Day(), place = Names.Settlement(s), id = s.StringId, kind, from, to, from_clan = fromClan, to_clan = toClan, how = how.ToString() });
        }

        public static void OnSiege(SiegeEvent se, bool began)
        {
            if (!Active || se?.BesiegedSettlement == null) return;
            string by = se.BesiegerCamp?.LeaderParty?.MapFaction?.StringId, of = se.BesiegedSettlement.MapFaction?.StringId;
            if (!Realms.Contains(by ?? "") && !Realms.Contains(of ?? "")) return;
            if (began) Tally(by).SiegesBegun++;
            Diag.Event("run", began ? "siege began" : "siege ended", new
            {
                run = _run.Id, day = Day(), place = Names.Settlement(se.BesiegedSettlement), kind = se.BesiegedSettlement.IsTown ? "town" : "castle", by, of,
                leader = se.BesiegerCamp?.LeaderParty?.LeaderHero != null ? Names.Hero(se.BesiegerCamp.LeaderParty.LeaderHero) : null
            });
        }

        public static void OnLordKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail how)
        {
            if (!Active || victim == null || !victim.IsLord) return;
            string f = victim.MapFaction?.StringId ?? victim.Clan?.Kingdom?.StringId, by = killer?.MapFaction?.StringId;
            if (!Realms.Contains(f ?? "") && !Realms.Contains(by ?? "")) return;
            Tally(f).LordsKilled++;
            Tally(by).LordsSlain++;
            Diag.Event("run", "lord killed", new { run = _run.Id, day = Day(), lord = Names.Hero(victim), realm = f, killer = killer != null ? Names.Hero(killer) : null, by, how = how.ToString() });
        }

        public static void OnLordTaken(PartyBase capturer, Hero h)
        {
            if (!Active || h == null || !h.IsLord) return;
            string f = h.MapFaction?.StringId, by = capturer?.MapFaction?.StringId;
            if (!Realms.Contains(f ?? "") && !Realms.Contains(by ?? "")) return;
            Tally(f).LordsCaptured++;
            Tally(by).LordsTaken++;
            Diag.Event("run", "lord taken", new { run = _run.Id, day = Day(), lord = Names.Hero(h), realm = f, by, holder = capturer != null ? (capturer.IsSettlement ? Names.Settlement(capturer.Settlement) : Names.Party(capturer.MobileParty)) : null });
        }

        public static void OnLordFreed(Hero h, EndCaptivityDetail how)
        {
            if (!Active || h == null || !h.IsLord) return;
            string f = h.MapFaction?.StringId;
            if (!Realms.Contains(f ?? "")) return;
            Tally(f).LordsFreed++;
            Diag.Event("run", "lord freed", new { run = _run.Id, day = Day(), lord = Names.Hero(h), realm = f, how = how.ToString() });
        }

        public static void OnVillageLooted(Village v)
        {
            if (!Active || v?.Settlement == null) return;
            string of = v.Settlement.MapFaction?.StringId, by = v.Settlement.LastAttackerParty?.MapFaction?.StringId;
            if (!Realms.Contains(of ?? "") && !Realms.Contains(by ?? "")) return;
            Tally(of).VillagesLooted++;
            Tally(by).VillagesSacked++;
            Diag.Event("run", "village looted", new { run = _run.Id, day = Day(), place = Names.Settlement(v.Settlement), of, by });
        }

        public static void OnArmy(Army a, bool formed, string why = null)
        {
            if (!Active || a?.LeaderParty == null) return;
            string f = a.LeaderParty.MapFaction?.StringId;
            if (!Realms.Contains(f ?? "")) return;
            if (formed) Tally(f).Armies++;
            Diag.Event("run", formed ? "army formed" : "army dispersed", new
            {
                run = _run.Id, day = Day(), realm = f, leader = a.LeaderParty.LeaderHero != null ? Names.Hero(a.LeaderParty.LeaderHero) : Names.Party(a.LeaderParty),
                by = formed && Orders.FormingBy != null ? BySource(Orders.FormingBy, a.LeaderParty) : CommandedBy(a.LeaderParty), parties = a.Parties.Count, men = a.TotalManCount, why
            });
        }

        public static void OnStance(IFaction x, IFaction y, bool war)
        {
            if (!Active) return;
            string a = x?.StringId, b = y?.StringId;
            if (!Realms.Contains(a ?? "") && !Realms.Contains(b ?? "")) return;
            Diag.Event("run", war ? "war" : "peace", new { run = _run.Id, day = Day(), a, b });
        }

        // ---------- the day line ----------

        private static void DayLine(int day, double h, Snapshot snap)
        {
            var extras = new List<RealmExtras>();
            foreach (string id in _run.AllRealms())
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == id);
                if (k != null) extras.Add(Extras(k));
            }
            List<WarRow> wars = Wars();
            var recruits = new Dictionary<string, int>(Recruits);
            Recruits.Clear();
            DateTime now = DateTime.Now;
            double seconds = (now - _lastLineAt).TotalSeconds;
            _lastLineAt = now;
            var header = new RunDayHeader
            {
                Run = _run.Id, Arm = _run.Arm, Day = day, Hours = h, Date = Text.When(h), Wall = Now(), RealSeconds = day == 0 ? 0 : Math.Round(seconds, 1), Speed = Watchdog.Speed,
                Who = new Dictionary<string, string>(_run.Who), Helper = Background.TakeDay()
            };
            var tally = _run.Tally.ToDictionary(kv => kv.Key, kv => kv.Value.Copy());
            string kingdom = _run.Kingdom;
            List<string> realms = _run.AllRealms();
            RunFiles.Write(_run);
            Background.Post("run day line", () => RunFiles.AppendDay(header.Run, RunDay.Build(header, realms, kingdom, snap, extras, wars, tally, recruits)));
        }

        private static RealmExtras Extras(Kingdom k)
        {
            var x = new RealmExtras { Realm = k.StringId };
            try
            {
                foreach (Clan c in k.Clans)
                {
                    if (c == null || c.IsEliminated) continue;
                    x.Clans++;
                    x.ClansGold += c.Gold;
                    // hired clans come and go with their purses (0.11.3: five left the orcs in run C1, 8.9 of the 10.6 million their gold fell)
                    if (c.IsUnderMercenaryService) { x.HiredClans++; x.HiredGold += c.Gold; }
                    try { x.ClansDaily += (int)Math.Round(Campaign.Current.Models.ClanFinanceModel.CalculateClanGoldChange(c, false, false, false).ResultNumber); }
                    catch (Exception e) { Diag.Swallowed("a clan's day for the run", e); }
                }
                foreach (Hero h in Hero.AllAliveHeroes)
                {
                    if (h == null) continue;
                    if (h.IsLord && h.MapFaction == k)
                    {
                        x.LordsAlive++;
                        if (h.IsPrisoner) x.LordsCaptive++;
                    }
                    if (h.IsPrisoner && h.IsLord && h.PartyBelongedToAsPrisoner?.MapFaction == k) x.EnemyLordsHeld++;
                }
                x.Influence = (int)Math.Round(k.Clans.Where(c => c != null && !c.IsEliminated).Sum(c => c.Influence));
            }
            catch (Exception e) { Diag.Swallowed("the run's numbers for " + k.StringId, e); }
            return x;
        }

        /// <summary>The game's own war tally between the kingdom and each rival (and between the rivals): kept from the war's start, so the run takes the change from its own day 0.</summary>
        private static List<WarRow> Wars()
        {
            var rows = new List<WarRow>();
            List<string> all = _run.AllRealms();
            for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
            {
                Kingdom a = Kingdom.All.FirstOrDefault(x => x.StringId == all[i]), b = Kingdom.All.FirstOrDefault(x => x.StringId == all[j]);
                if (a == null || b == null) continue;
                try
                {
                    StanceLink st = a.GetStanceWith(b);
                    if (st == null) continue;
                    rows.Add(new WarRow
                    {
                        A = a.StringId, B = b.StringId, AtWar = FactionManager.IsAtWarAgainstFaction(a, b),
                        CasualtiesA = st.GetCasualties(a), CasualtiesB = st.GetCasualties(b), SiegesA = st.GetSuccessfulSieges(a), SiegesB = st.GetSuccessfulSieges(b),
                        TownSiegesA = st.GetSuccessfulTownSieges(a), TownSiegesB = st.GetSuccessfulTownSieges(b), RaidsA = st.GetSuccessfulRaids(a), RaidsB = st.GetSuccessfulRaids(b),
                        TributeA = st.GetTotalTributePaid(a), TributeB = st.GetTotalTributePaid(b)
                    });
                }
                catch (Exception e) { Diag.Swallowed("the game's war tally", e); }
            }
            return rows;
        }

        // ---------- who commands, and the setup ----------

        /// <summary>For the kingdom and each rival: you (its ruler is your character), agent (an agent commands it live), watching (an agent answers but gives no order) or game (the game's AI).</summary>
        private static Dictionary<string, string> WhoCommands()
        {
            var who = new Dictionary<string, string>();
            foreach (string id in _run.AllRealms())
            {
                Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == id);
                if (k == null) { who[id] = "gone"; continue; }
                if (k.Leader != null && k.Leader == Hero.MainHero) { who[id] = "you"; continue; }
                AgentConfig c = Bridge.Configs.FirstOrDefault(x => x.Realm == id && !x.IsLieutenant && x.On);
                who[id] = c == null ? "game" : c.Mode == "live" ? "agent" : "watching";
            }
            return who;
        }

        private static RunSetup Setup()
        {
            var s = new RunSetup
            {
                ModuleVersion = SubModule.Version, Modules = SubModule.Modules.ToList(), Speed = Watchdog.Speed, Detail = Settings.Detail, LieutenantPause = Settings.LieutenantPause,
                TrueSight = Campaign.Current.TrueSight, Save = new RunSaveInfo { Name = _run.Save, Path = RunFiles.SavePath(_run.Save) }
            };
            foreach (AgentConfig c in Bridge.Configs)
                s.Agents.Add(new RunAgentInfo { Realm = c.Realm, Kind = c.Kind, Mode = c.Mode, Lords = c.Lords, Workspace = c.Workspace, LogDir = c.LogDir });
            s.Unusable.AddRange(Bridge.Unusable);
            s.SetAside.AddRange(Bridge.SetAside);
            try { s.CheatMode = TaleWorlds.Core.Game.Current?.CheatMode == true; }
            catch (Exception e) { Diag.Swallowed("reading cheat mode", e); }
            try { s.PartyAiOrders = PacReader.Status; }
            catch (Exception e) { Diag.Swallowed("Party AI Controls' orders", e); }
            return s;
        }

        // ---------- the console ----------

        /// <summary>agents.run: the run going, or the one waiting, in words.</summary>
        public static string Status()
        {
            RunState r = _run ?? RunFiles.ReadOpen();
            if (r == null) return "No test run. Loading " + Settings.RunSave + " readies one (runs: " + Paths.Runs + ").";
            double h = Campaign.Current != null ? CampaignTime.Now.ToHours : r.LastHours;
            var lines = new List<string>
            {
                "Test run " + (r.Arm ?? "(not begun)") + " (" + r.Id + "): " + r.Status + (r.Status == "running" ? ", day " + RunRules.Day(h, r.H0) + " of " + r.Days + ", " + Text.D((float)(r.H0 + r.Days * 24 - h)) + " in-game hours left" : "") + "."
            };
            foreach (KeyValuePair<string, string> kv in r.Who) lines.Add("  " + RealmName(kv.Key) + ": " + WhoWords(kv.Value));
            if (r.Lieutenant) lines.Add("  your lieutenant: on");
            lines.Add("  loads: " + r.Segments.Count + "; its saves: " + (r.Saves.Count > 0 ? string.Join(", ", r.Saves) : "none yet") + "; marks against it: " + r.Flags.Count);
            foreach (RunFlag f in r.Flags.Skip(Math.Max(0, r.Flags.Count - 5))) lines.Add("  ! day " + f.Day + ", " + f.Kind + ": " + f.Text);
            lines.Add("  files: " + Path.Combine(Paths.Runs, r.Id));
            return string.Join("\n", lines);
        }

        /// <summary>agents.run check: who commands each side right now, the arm that makes, and what would spoil it.</summary>
        public static string CheckText()
        {
            if (Campaign.Current == null || !Bridge.Running) return "Load a campaign first.";
            if (_run == null) return "No run is ready: load " + Settings.RunSave + " first.";
            Dictionary<string, string> who = WhoCommands();
            string ruler = who.TryGetValue(_run.Kingdom ?? "", out string w) ? w : "none";
            string arm = RunRules.Arm(ruler, _run.Rivals.Select(r => who.TryGetValue(r, out string x) ? x : "none"));
            var lines = new List<string> { "As things stand the run would be " + arm + (RunRules.Names.TryGetValue(arm, out string n) ? " (" + n + ")" : "") + ":" };
            foreach (KeyValuePair<string, string> kv in who) lines.Add("  " + RealmName(kv.Key) + ": " + WhoWords(kv.Value));
            AgentConfig lt = Bridge.Configs.FirstOrDefault(c => c.IsLieutenant && c.Realm == _run.Kingdom);
            lines.Add("  your lieutenant: " + (lt != null && lt.Mode == "live" ? "on" : "off"));
            Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == _run.Kingdom);
            AgentConfig ka = Bridge.Configs.FirstOrDefault(c => c.Realm == _run.Kingdom && !c.IsLieutenant);
            if (ruler == "you" && ka != null && ka.Mode == "live")
                lines.Add("  ! An agent is live for your kingdom and would command your lords: set its mode to off for A or B.");
            if (ruler != "you")
            {
                if ((ka == null || ka.Mode != "live") && arm != "C0") lines.Add("  ! You do not rule " + (k != null ? Names.Faction(k) : "the kingdom") + " and no agent commands it live: for C1 or C2 set the kingdom's agent to live.");
                List<string> held = k == null ? new List<string>() : k.Clans.Where(c => c != null).SelectMany(c => c.Heroes).Where(h => h != null && h.IsLord && h.IsAlive && PacReader.HoldsOrder(h)).Select(Names.Hero).ToList();
                if (held.Count > 0) lines.Add("  ! " + held.Count + " of the kingdom's lords hold Party AI Controls orders, which the agent may not override: release them first (" + string.Join(", ", held.Take(6)) + ").");
            }
            if (Campaign.Current.TrueSight) lines.Add("  ! True sight is on.");
            return string.Join("\n", lines);
        }

        // ---------- helpers ----------

        private static RunTally Tally(string realm)
        {
            if (realm == null || !Realms.Contains(realm)) return RunTally.Nowhere;
            if (!_run.Tally.TryGetValue(realm, out RunTally t)) _run.Tally[realm] = t = new RunTally();
            return t;
        }

        private static int Day() => RunRules.Day(CampaignTime.Now.ToHours, _run.H0);

        /// <summary>The side's lord parties that had a lord in the battle: its leader, or a lord among its men, its wounded or its dead (the game empties the leader's place of a lord taken).</summary>
        private static int Lords(MapEventSide side)
        {
            int n = 0;
            foreach (MapEventParty mp in side.Parties)
                if (mp.Party?.MobileParty != null && mp.Party.MobileParty.IsLordParty && HadLord(mp)) n++;
            return n;
        }

        private static bool HadLord(MapEventParty mp)
        {
            if (mp.Party.LeaderHero != null && mp.Party.LeaderHero.IsLord) return true;
            foreach (FlattenedTroopRosterElement t in mp.Troops)
                if (t.Troop != null && t.Troop.IsHero && t.Troop.HeroObject != null && t.Troop.HeroObject.IsLord) return true;
            return HasLord(mp.WoundedInBattle) || HasLord(mp.DiedInBattle);
        }

        private static bool HasLord(TroopRoster r)
        {
            if (r == null) return false;
            for (int i = 0; i < r.Count; i++)
            {
                CharacterObject c = r.GetCharacterAtIndex(i);
                if (c != null && c.IsHero && c.HeroObject != null && c.HeroObject.IsLord) return true;
            }
            return false;
        }

        private static string Leader(MapEventSide side)
        {
            PartyBase p = side?.LeaderParty;
            if (p == null) return null;
            Hero h = p.LeaderHero ?? (p.IsMobile && p.MobileParty.IsLordParty ? p.Owner : null);
            if (h != null) return Names.Hero(h);
            return p.IsSettlement ? Names.Settlement(p.Settlement) : Names.Party(p.MobileParty);
        }

        private static string CommandedBy(MapEventSide side) => CommandedBy(side?.LeaderParty?.MobileParty);

        /// <summary>Who had the say over a party: you (your own party, or an order of yours), agent (an agent's order), game (the game's AI).</summary>
        private static string BySource(string src, MobileParty m)
        {
            // with you ruling, the only agent giving your lords orders is your lieutenant, and only with your yes
            if (src == "agent") return _run.Lieutenant && m?.MapFaction?.StringId == _run.Kingdom ? "lieutenant" : "agent";
            return src == "you" ? "you" : "hand";
        }

        private static string CommandedBy(MobileParty m)
        {
            if (m == null) return "holding";
            if (m == MobileParty.MainParty) return "you";
            string src = Orders.SourceOf(m);
            if (src != null) return BySource(src, m);
            Hero lord = m.LeaderHero ?? (m.IsLordParty ? m.Owner : null);
            if (lord != null && PacReader.KeyOf(lord) is string key)
                return PacLink.IsOurs(lord.StringId, key) ? "lieutenant (Party AI Controls)" : "you (Party AI Controls)";
            return "game";
        }

        private static string RealmName(string id)
        {
            Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == id);
            return k != null ? Names.Faction(k) : id;
        }

        private static string WhoWords(string who)
        {
            switch (who)
            {
                case "you": return "you";
                case "agent": return "its agent";
                case "watching": return "the game's AI, its agent watching without orders";
                case "lieutenant": return "your lieutenant, with your yes";
                case "game": return "the game's AI";
                default: return who ?? "nobody";
            }
        }

        private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Text.Inv);

        private static void Say(string text)
        {
            Log.Line("run: " + text);
            try { InformationManager.DisplayMessage(new InformationMessage("[Agents] " + text, Color.FromUint(0xFFE6C36A))); }
            catch (Exception e) { Diag.Swallowed("a run message", e); }
        }
    }

    /// <summary>The run's rules that need no game (the smoke test reads them).</summary>
    internal static class RunRules
    {
        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["A"] = "you against the agents", ["B"] = "you against the game's AI", ["C1"] = "an agent ruling your kingdom against the agents", ["C2"] = "an agent ruling your kingdom against the game's AI",
            // the control (0.12.4; Mert, Oct 25: "the control test: all realms playing with the game's ai")
            ["C0"] = "the control: the game's AI ruling your kingdom against the game's AI"
        };

        /// <summary>The arm from who commands the kingdom (you, agent, watching, game) and each rival (agent, watching, game).</summary>
        public static string Arm(string ruler, IEnumerable<string> rivals)
        {
            List<string> r = rivals.ToList();
            bool agents = r.Count > 0 && r.All(x => x == "agent");
            bool ai = r.Count > 0 && r.All(x => x == "game" || x == "watching");
            if (ruler == "you") return agents ? "A" : ai ? "B" : "mixed";
            if (ruler == "agent") return agents ? "C1" : ai ? "C2" : "mixed";
            if (ruler == "game" || ruler == "watching") return ai ? "C0" : "mixed";
            return "other";
        }

        /// <summary>The run's day of an hour: day 1 is the first 24 in-game hours from the save's time.</summary>
        public static int Day(double hours, double h0) => (int)Math.Floor((hours - h0) / 24.0 + 1e-9) + 1;

        public static bool Over(double hours, double h0, int days) => hours - h0 >= days * 24.0 - 1e-6;
    }

    /// <summary>The day line (worker, from the hour's copy and the game's numbers taken on its thread).</summary>
    internal static class RunDay
    {
        public static object Build(RunDayHeader head, List<string> realms, string kingdom, Snapshot s, List<RealmExtras> extras, List<WarRow> wars,
                                   Dictionary<string, RunTally> tally, Dictionary<string, int> recruits)
        {
            var rows = new List<object>();
            foreach (string id in realms)
            {
                List<SettlementRow> own = s?.Settlements.Where(x => x.Faction == id).ToList() ?? new List<SettlementRow>();
                List<SettlementRow> forts = own.Where(x => x.Kind == "town" || x.Kind == "castle").ToList();
                List<PartyRow> lords = s?.Parties.Where(r => r.Faction == id && r.Kind == "lord").ToList() ?? new List<PartyRow>();
                List<PartyRow> armies = lords.Where(r => r.ArmyLeader == r.Id && r.ArmyParties > 0).ToList();
                RealmExtras x = extras.FirstOrDefault(e => e.Realm == id) ?? new RealmExtras { Realm = id };
                tally.TryGetValue(id, out RunTally t);
                recruits.TryGetValue(id, out int rec);
                rows.Add(new
                {
                    Realm = id, Name = s != null && s.FactionNames.TryGetValue(id, out string nm) ? nm : id, Who = head.Who.TryGetValue(id, out string w) ? w : null, Kingdom = id == kingdom,
                    Towns = own.Count(r => r.Kind == "town"), Castles = own.Count(r => r.Kind == "castle"), Villages = own.Count(r => r.Kind == "village"),
                    Garrisons = forts.Sum(r => Math.Max(0, r.Garrison)), Militia = forts.Sum(r => Math.Max(0, r.Militia)),
                    Lords = lords.Count, Men = lords.Sum(r => r.Men), Wounded = lords.Sum(r => r.Wounded), Strength = (int)Math.Round(lords.Sum(r => r.Strength)),
                    Armies = armies.Count, ArmyMen = armies.Sum(r => r.ArmyMen),
                    Loot = lords.Sum(r => r.Loot), Prisoners = lords.Sum(r => r.Prisoners), Ransom = lords.Sum(r => r.Ransom),
                    x.LordsAlive, x.LordsCaptive, x.EnemyLordsHeld, x.Clans, x.ClansGold, x.ClansDaily, x.HiredClans, x.HiredGold, SwornGold = x.ClansGold - x.HiredGold, x.Influence,
                    RecruitedToday = rec, Tally = t
                });
            }
            PartyRow me = s != null && s.PlayerParty != null && s.PartyById.TryGetValue(s.PlayerParty, out PartyRow p) ? p : null;
            return new
            {
                Type = "day", head.Run, head.Arm, head.Day, Hours = Math.Round(head.Hours, 2), head.Date, head.Wall, head.RealSeconds, head.Speed, head.Helper, Realms = rows, Wars = wars,
                You = me == null ? null : new { Party = me.Name, me.Faction, me.Men, Strength = (int)Math.Round(me.Strength), me.ArmyMen, Doing = me.Doing }
            };
        }
    }

    /// <summary>The run's files (worker writes; the game's thread reads only run.json at a load).</summary>
    internal static class RunFiles
    {
        public static string Dir(string id) => Paths.Ensure(Path.Combine(Paths.Runs, id));
        public static string Events(string id) => Path.Combine(Dir(id), "events.jsonl");
        private static string CurrentFile => Path.Combine(Paths.Runs, "current.txt");

        /// <summary>The run that is open (armed or running), from current.txt and its run.json; null when none.</summary>
        public static RunState ReadOpen()
        {
            try
            {
                if (!File.Exists(CurrentFile)) return null;
                string id = File.ReadAllText(CurrentFile).Trim();
                string path = Path.Combine(Paths.Runs, id, "run.json");
                if (id.Length == 0 || !File.Exists(path)) return null;
                RunState r = Json.Read<RunState>(File.ReadAllText(path));
                return r != null && (r.Status == "running" || r.Status == "armed") ? r : null;
            }
            catch (Exception e) { Diag.Swallowed("reading the open run", e); return null; }
        }

        /// <summary>Game's thread: the state as it stands goes to the worker to be written (and current.txt with it when asked).</summary>
        public static void Write(RunState r, bool current = false)
        {
            string json = Json.Line(r), id = r.Id;
            Background.Post("run state", () =>
            {
                Files.Overwrite(Path.Combine(Dir(id), "run.json"), json);
                if (current) Files.Overwrite(CurrentFile, id);
            });
        }

        public static void WriteSetup(string id, RunSetup s)
        {
            foreach (RunAgentInfo a in s.Agents)
            {
                if (a.Workspace == null) continue;
                foreach (string f in new[] { "agent.txt", "warroom.txt", "warroom-system.md", "chronicler-system.md" })
                {
                    string path = Path.Combine(a.Workspace, f);
                    if (File.Exists(path)) a.Files[f] = Sha(path);
                }
                a.WarRoom = ReadSettings(Path.Combine(a.Workspace, "warroom.txt"));
            }
            if (s.Save.Path != null && File.Exists(s.Save.Path))
            {
                s.Save.Sha256 = Sha(s.Save.Path);
                var fi = new FileInfo(s.Save.Path);
                s.Save.Bytes = fi.Length;
                s.Save.Written = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", Text.Inv);
            }
            // each realm's kept tools as the run begins (0.13, A6): names, versions and fingerprints, so the comparisons know what each run had
            s.Libraries = Tools.OwnLibrary.Setup(s.Agents.Select(a => a.Realm));
            Files.Overwrite(Path.Combine(Dir(id), "setup.json"), Json.Line(s));
        }

        /// <summary>The folder a run was readied under before it was named by its arm: gone once only its run.json is in it.</summary>
        public static void DropArmed(string id)
        {
            try
            {
                string dir = Path.Combine(Paths.Runs, id);
                if (!Directory.Exists(dir)) return;
                if (Directory.GetFiles(dir).Any(f => !string.Equals(Path.GetFileName(f), "run.json", StringComparison.OrdinalIgnoreCase)) || Directory.GetDirectories(dir).Length > 0) return;
                Directory.Delete(dir, true);
            }
            catch (Exception e) { Diag.Swallowed("removing the readied run's folder", e); }
        }

        public static void AppendDay(string id, object line)
        {
            Files.Append(Path.Combine(Dir(id), "days.jsonl"), Json.Line(line));
            Files.FlushAll();
        }

        /// <summary>Runs.exe, staged in the module's runs folder beside bin\ (the game loads nothing from there).</summary>
        public static string RunsExe
        {
            get
            {
                string dll = typeof(RunFiles).Assembly.Location;
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dll) ?? ".", "..", "..", "runs", "Runs.exe"));
            }
        }

        /// <summary>
        /// The run's bundle (0.13, docs/PLAN-0.13.md C5), packed at its end: the worker flushes every file, then starts Runs.exe bundle
        /// for it at low priority, its words going to bundle.log in the run's folder. Runs.exe bundle (run) does the same by hand.
        /// </summary>
        public static void StartBundle(string id)
        {
            Background.Post("run bundle", () =>
            {
                Files.FlushAll();
                try
                {
                    string exe = RunsExe;
                    if (!File.Exists(exe)) { Log.Line("run: the bundle was not packed: " + exe + " is missing (Runs.exe bundle " + id + " packs it by hand)"); return; }
                    var psi = new System.Diagnostics.ProcessStartInfo(exe, "bundle " + id)
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                    psi.EnvironmentVariables["BANNERLORD_AGENTS_ROOT"] = Paths.Root;
                    var p = System.Diagnostics.Process.Start(psi);
                    if (p == null) return;
                    try { p.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal; } catch (Exception e) { Diag.Swallowed("the bundle's priority", e); }
                    string log = Path.Combine(Dir(id), "bundle.log");
                    void Keep(string line)
                    {
                        if (line == null) return;
                        try { File.AppendAllText(log, line + Environment.NewLine); }
                        catch (Exception x) { Diag.Swallowed("the bundle's log", x); }
                    }
                    p.OutputDataReceived += (s, e) => Keep(e.Data);
                    p.ErrorDataReceived += (s, e) => Keep(e.Data);
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    Diag.Event("run", "bundle started", new { run = id, exe });
                }
                catch (Exception e) { Diag.Error("packing the run's bundle", e); }
            });
        }

        /// <summary>The save's file: the game's saves folder in your Documents (OneDrive's when it moved them).</summary>
        public static string SavePath(string save)
        {
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return string.IsNullOrEmpty(save) ? null : Path.Combine(docs, "Mount and Blade II Bannerlord", "Game Saves", save + ".sav");
            }
            catch (Exception e) { Diag.Swallowed("the save's path", e); return null; }
        }

        private static string Sha(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").Substring(0, 16);
            }
            catch (Exception e) { Diag.Swallowed("a fingerprint for " + Path.GetFileName(path), e); return null; }
        }

        private static Dictionary<string, string> ReadSettings(string path)
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(path)) return d;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.Length == 0 || line.StartsWith("#") || eq <= 0) continue;
                    d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception e) { Diag.Swallowed("reading " + Path.GetFileName(path), e); }
            return d;
        }
    }

    internal sealed class RunState
    {
        public string Id, Arm, Status = "armed", Save, Campaign, Kingdom, KingdomName, Armed, Started, Finished;
        public List<string> Rivals = new List<string>();
        public Dictionary<string, string> Who = new Dictionary<string, string>();
        public bool Lieutenant;
        public double H0, LastHours;
        public int Days = 60, LastDay;
        public List<RunSegment> Segments = new List<RunSegment>();
        public List<string> Saves = new List<string>();
        /// <summary>What was done to set it up before its clock ran (the playerless switch).</summary>
        public List<string> SetUp = new List<string>();
        public List<RunFlag> Flags = new List<RunFlag>();
        public Dictionary<string, RunTally> Tally = new Dictionary<string, RunTally>();
        public List<WarRow> WarAtStart = new List<WarRow>();

        public List<string> AllRealms() => (Kingdom != null ? new[] { Kingdom } : new string[0]).Concat(Rivals).Distinct().ToList();

        public void Flag(string wall, double hours, string kind, string text) =>
            Flags.Add(new RunFlag { Wall = wall, Hours = Math.Round(hours, 2), Day = RunRules.Day(hours, H0), Kind = kind, Text = text });

        public void CloseSegment(string wall, double hours)
        {
            RunSegment s = Segments.LastOrDefault();
            if (s == null || s.WallTo != null) return;
            s.WallTo = wall;
            s.To = Math.Round(hours, 2);
        }
    }

    internal sealed class RunSegment
    {
        public string Load, WallFrom, WallTo, How;
        public double From, To;
    }

    internal sealed class RunFlag
    {
        public string Wall, Kind, Text;
        public double Hours;
        public int Day;
    }

    /// <summary>A realm's counts over the run (kept in run.json, so a run carried on after a crash keeps them).</summary>
    internal sealed class RunTally
    {
        public static readonly RunTally Nowhere = new RunTally();
        public int BattlesWon, BattlesLost, LordBattlesWon, LordBattlesLost, MenLost, EnemyFallen, SiegesBegun;
        public int TownsGained, TownsLost, CastlesGained, CastlesLost, VillagesGained, VillagesLost;
        public int LordsCaptured, LordsTaken, LordsKilled, LordsSlain, LordsFreed, VillagesLooted, VillagesSacked, Armies;

        public void Gained(string kind) { if (kind == "town") TownsGained++; else if (kind == "castle") CastlesGained++; else if (kind == "village") VillagesGained++; }
        public void Lost(string kind) { if (kind == "town") TownsLost++; else if (kind == "castle") CastlesLost++; else if (kind == "village") VillagesLost++; }
        public RunTally Copy() => (RunTally)MemberwiseClone();
    }

    internal sealed class RealmExtras
    {
        public string Realm;
        public int Clans, ClansGold, ClansDaily, HiredClans, HiredGold, LordsAlive, LordsCaptive, EnemyLordsHeld, Influence;
    }

    internal sealed class WarRow
    {
        public string A, B;
        public bool AtWar;
        public int CasualtiesA, CasualtiesB, SiegesA, SiegesB, TownSiegesA, TownSiegesB, RaidsA, RaidsB, TributeA, TributeB;
    }

    internal sealed class RunDayHeader
    {
        public string Run, Arm, Date, Wall, Speed;
        public int Day;
        public double Hours, RealSeconds;
        public Dictionary<string, string> Who = new Dictionary<string, string>();
        /// <summary>The background helper's day (0.12.0): the most the lords' orders and the agents' reports were behind the game.</summary>
        public HelperDay Helper;
    }

    internal sealed class RunSetup
    {
        public string ModuleVersion, Speed, Detail, PartyAiOrders;
        public bool LieutenantPause, TrueSight, CheatMode;
        public List<string> Modules = new List<string>();
        public List<RunAgentInfo> Agents = new List<RunAgentInfo>();
        public List<string> Unusable = new List<string>();
        /// <summary>Agents switched off and set aside for the one switched on for the same realm (0.11.1).</summary>
        public List<string> SetAside = new List<string>();
        public RunSaveInfo Save = new RunSaveInfo();
        /// <summary>Each realm's own kept tools at the run's start (0.13): name, version, fingerprint, usable, runs so far.</summary>
        public Dictionary<string, List<object>> Libraries;
    }

    internal sealed class RunAgentInfo
    {
        public string Realm, Kind, Mode, Lords, Workspace, LogDir;
        public Dictionary<string, string> Files = new Dictionary<string, string>();
        public Dictionary<string, string> WarRoom = new Dictionary<string, string>();
    }

    internal sealed class RunSaveInfo
    {
        public string Name, Path, Sha256, Written;
        public long Bytes;
    }
}
