using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BannerlordAgents
{
    /// <summary>
    /// How a camp of the agents' or the lieutenant's chooses its siege engines (0.20.0), set by besiege's engines settings and read on the game's
    /// thread by the drill. A camp with no rule here follows the default drill.
    /// </summary>
    internal sealed class DrillRule
    {
        /// <summary>0 the game's own strategy; 1 the siege drill; 2 rams and towers only.</summary>
        public int Mode = 1;
        /// <summary>With the drill: how many trebuchets to mass in reserve before they roll out together (1 to 4).</summary>
        public int Trebuchets = 4;
        /// <summary>With the drill and the human player defending in person: swap the trebuchets one by one for fire onagers once his engines are silenced.</summary>
        public bool Fire = true;

        public static readonly DrillRule Default = new DrillRule();

        public bool Same(DrillRule o) => o != null && o.Mode == Mode && o.Trebuchets == Trebuchets && o.Fire == Fire;
        public bool IsDefault => Same(Default);

        public override string ToString() => Mode == 0 ? "the game's own engines" : Mode == 2 ? "rams and towers only"
            : "the siege drill (" + Trebuchets + (Trebuchets == 1 ? " trebuchet" : " trebuchets") + " massed" + (Fire ? "" : ", no fire onagers against the player") + ")";
    }

    /// <summary>
    /// The siege drill (0.20.0, Nov 16). The best way to take walls: build every siege tower and the ram, then trebuchets, each held in reserve as it
    /// is finished (an engine in reserve can neither shoot nor be shot), then roll them out all together to overwhelm the defenders' engines; build a
    /// new trebuchet for each one that falls; storm once the walls are down. Against the human player defending in person, whose siege battle is
    /// fought in a battle scene where fire onagers' bursts cut down the men massing at the breaches: once the trebuchets have silenced his engines,
    /// one trebuchet at a time goes to reserve and a fire onager is built in its spot, so at least three engines keep firing, until four fire onagers
    /// stand. Defenders against the player's own siege keep fire catapults in all four spots and build a new one for each that falls. Against
    /// computer camps the defenders keep the game's own choice, and every side the player leads is his.
    ///
    /// The game asks each side every frame what to do next (SiegeStrategyActionModel.GetLogicalActionForStrategy, which the game's own strategy
    /// lists answer; GameHooks.DrillPatch answers in their place). The drill decides afresh each time from what the side has, so a save and a load
    /// cannot confuse it; only "rolled out" is remembered (and worked out again after a load). It builds one engine at a time, as the game does, and
    /// never sends a half-built engine to reserve: the game would never finish it (a half-built engine brought back counts as being redeployed,
    /// which stops its building, and only finished engines redeploy) and the side would build nothing more; one caught so is scrapped.
    /// </summary>
    internal static class SiegeDrill
    {
        public const string Trebuchet = "trebuchet", FireOnager = "fire_onager", FireCatapult = "fire_catapult", Tower = "siege_tower_level1", Ram = "ram";
        public const int Towers = 2, FireNeeded = 4;

        // ---------- the rules, by the camp leader's party (written by the tools' worker each hour, read on the game's thread) ----------

        private static readonly ConcurrentDictionary<string, DrillRule> Rules = new ConcurrentDictionary<string, DrillRule>(StringComparer.Ordinal);

        /// <summary>Sets (or with null or the default drill, clears) a party's rule; a null party clears every rule.</summary>
        public static void Set(string party, DrillRule r)
        {
            if (party == null) { Rules.Clear(); return; }
            if (r == null || r.IsDefault) { Rules.TryRemove(party, out _); return; }
            if (Rules.TryGetValue(party, out DrillRule old) && old.Same(r)) return;
            Rules[party] = r;
            Diag.Event("drill", "rule", new { party, rule = r.ToString() });
        }

        public static DrillRule For(string party) => party != null && Rules.TryGetValue(party, out DrillRule r) ? r : DrillRule.Default;

        // ---------- the decision, on plain values (the game's hook and the siege clock both ask it) ----------

        internal struct Slot
        {
            public string Type;   // null: empty
            public bool Built, Redeploying;
            public bool Empty => Type == null;
            public bool Active => Type != null && Built && !Redeploying;
        }

        internal sealed class SideView
        {
            public Slot[] Melee = new Slot[3];
            public Slot[] Ranged = new Slot[4];
            /// <summary>The reserve, in the game's order: each engine's type and whether it is finished.</summary>
            public List<string> Reserve = new List<string>();
            public List<bool> ReserveBuilt = new List<bool>();
            public bool PrepDone = true;

            public void Clear(int melee)
            {
                if (Melee.Length != melee) Melee = new Slot[melee];
                for (int i = 0; i < Melee.Length; i++) Melee[i] = default(Slot);
                for (int i = 0; i < Ranged.Length; i++) Ranged[i] = default(Slot);
                Reserve.Clear();
                ReserveBuilt.Clear();
                PrepDone = true;
            }

            public int InSpots(string type) { int n = 0; foreach (Slot s in Ranged) if (s.Type == type) n++; return n; }
            public int ActiveIn(string type) { int n = 0; foreach (Slot s in Ranged) if (s.Type == type && s.Active) n++; return n; }
            public int ActiveRanged() { int n = 0; foreach (Slot s in Ranged) if (s.Active) n++; return n; }
            public int ReserveOf(string type) { int n = 0; for (int i = 0; i < Reserve.Count; i++) if (Reserve[i] == type && ReserveBuilt[i]) n++; return n; }
        }

        internal enum Kind { Hold, Build, Deploy, ToReserve, Scrap }

        internal struct Act
        {
            public Kind Kind;
            public string Type;
            public bool Ranged;
            public int Slot, Reserve;
            public static readonly Act Hold = new Act { Kind = Kind.Hold, Slot = -1, Reserve = -1 };
            public override string ToString() => Kind == Kind.Hold ? "hold" : Kind + " " + Type + (Kind == Kind.Deploy ? " from reserve " + Reserve : "") + " at " + (Ranged ? "ranged " : "melee ") + Slot;
        }

        private static Act Build(string type, bool ranged, int slot) => new Act { Kind = Kind.Build, Type = type, Ranged = ranged, Slot = slot, Reserve = -1 };
        private static Act Deploy(string type, bool ranged, int reserve, int slot) => new Act { Kind = Kind.Deploy, Type = type, Ranged = ranged, Slot = slot, Reserve = reserve };
        private static Act ToReserve(string type, bool ranged, int slot) => new Act { Kind = Kind.ToReserve, Type = type, Ranged = ranged, Slot = slot, Reserve = -1 };
        private static Act Scrap(string type, bool ranged, int slot) => new Act { Kind = Kind.Scrap, Type = type, Ranged = ranged, Slot = slot, Reserve = -1 };

        private static int FreeSlot(Slot[] slots) { for (int i = 0; i < slots.Length; i++) if (slots[i].Empty) return i; return -1; }
        private static int ReserveIndex(SideView v, string type) { for (int i = 0; i < v.Reserve.Count; i++) if (v.Reserve[i] == type && v.ReserveBuilt[i]) return i; return -1; }

        /// <summary>The checks before any choice: a half-built engine caught as redeploying is scrapped; while one is being built, nothing new starts.</summary>
        private static bool Busy(SideView v, out Act a)
        {
            a = Act.Hold;
            for (int i = 0; i < v.Melee.Length; i++)
                if (!v.Melee[i].Empty && !v.Melee[i].Built && v.Melee[i].Redeploying) { a = Scrap(v.Melee[i].Type, false, i); return true; }
            for (int i = 0; i < v.Ranged.Length; i++)
                if (!v.Ranged[i].Empty && !v.Ranged[i].Built && v.Ranged[i].Redeploying) { a = Scrap(v.Ranged[i].Type, true, i); return true; }
            foreach (Slot s in v.Melee) if (!s.Empty && !s.Built) return true;
            foreach (Slot s in v.Ranged) if (!s.Empty && !s.Built) return true;
            return false;
        }

        /// <summary>The ram's spot among a camp's rams and towers (the towers take the others), as the game's own strategies leave them and the map draws them.</summary>
        public const int RamSpot = 0;

        private static bool IsRam(string type) => type == Ram || type == "improved_ram";

        /// <summary>A ram outside its spot, or a tower in the ram's: a finished one to reserve (it comes back to its own spot), a half-built one scrapped.</summary>
        private static bool WrongSpot(SideView v, out Act a)
        {
            a = Act.Hold;
            for (int j = 0; j < v.Melee.Length; j++)
            {
                Slot s = v.Melee[j];
                if (s.Empty) continue;
                bool wrong = (IsRam(s.Type) && j != RamSpot) || (s.Type == Tower && j == RamSpot);
                if (!wrong) continue;
                a = s.Built ? ToReserve(s.Type, false, j) : Scrap(s.Type, false, j);
                return true;
            }
            return false;
        }

        /// <summary>Has the camp rolled its trebuchets out, as far as what it has shows (after a load, when nothing is remembered)?</summary>
        public static bool LooksRolledOut(SideView v, bool wallsTouched)
        {
            int built = 0;
            foreach (Slot s in v.Ranged) if (s.Type == Trebuchet && s.Built) built++;
            return built >= 2 || v.InSpots(FireOnager) > 0 || wallsTouched;
        }

        /// <summary>
        /// The besiegers' next step by the drill (rule mode 1, or 2 for rams and towers only). againstPlayer: the human player defends the place in
        /// person; enemyActive: the defenders' throwing engines in place and working. rollNow: the trebuchets are massed and roll out from now on.
        /// </summary>
        public static Act Attacker(SideView v, DrillRule r, bool againstPlayer, bool rolledOut, int enemyActive, out bool rollNow)
        {
            rollNow = false;
            r = r ?? DrillRule.Default;
            // each kind in its own spots: the game keeps a camp's ram in its first spot and its towers in the next two, and the map draws them only there
            // (SettlementVisual.AddSiegeIconComponents: a ram in any other spot breaks the drawing, Nov 16). One put elsewhere (by 0.20.0, which filled
            // the spots in building order) goes to reserve, to come back to its own spot; one half built there is scrapped
            if (WrongSpot(v, out Act fix)) return fix;
            if (Busy(v, out Act busy)) return busy;
            if (!v.PrepDone) return Act.Hold;
            // every siege tower, then the ram
            int towers = 0;
            for (int j = 1; j < v.Melee.Length; j++) if (v.Melee[j].Type == Tower) towers++;
            bool ram = v.Melee.Length > RamSpot && IsRam(v.Melee[RamSpot].Type);
            if (towers < Towers)
            {
                int spot = -1;
                for (int j = 1; j < v.Melee.Length; j++) if (v.Melee[j].Empty) { spot = j; break; }
                if (spot >= 0)
                {
                    int ri = ReserveIndex(v, Tower);
                    return ri >= 0 ? Deploy(Tower, false, ri, spot) : Build(Tower, false, spot);
                }
            }
            if (!ram && v.Melee.Length > RamSpot && v.Melee[RamSpot].Empty)
            {
                int ri = ReserveIndex(v, Ram);
                if (ri < 0) ri = ReserveIndex(v, "improved_ram");
                return ri >= 0 ? Deploy(v.Reserve[ri], false, ri, RamSpot) : Build(Ram, false, RamSpot);
            }
            if (r.Mode == 2) return Act.Hold;
            int n = Math.Max(1, Math.Min(4, r.Trebuchets));
            int free = FreeSlot(v.Ranged);
            if (!rolledOut)
            {
                // massing: a trebuchet finished in its spot goes straight to reserve, where nothing can reach it
                for (int i = 0; i < v.Ranged.Length; i++)
                    if (v.Ranged[i].Type == Trebuchet && v.Ranged[i].Active) return ToReserve(Trebuchet, true, i);
                if (v.ReserveOf(Trebuchet) >= n) { rollNow = true; rolledOut = true; }
                else
                {
                    if (free >= 0) return Build(Trebuchet, true, free);
                    // every spot held by some other engine (built before the drill took over): one makes room, to reserve
                    for (int i = 0; i < v.Ranged.Length; i++)
                        if (v.Ranged[i].Active) return ToReserve(v.Ranged[i].Type, true, i);
                    return Act.Hold;
                }
            }
            // rolled out. Against the player in person, once his engines are silenced (and from then on), the trebuchets give way to fire onagers
            bool swap = againstPlayer && r.Fire && (enemyActive == 0 || v.InSpots(FireOnager) > 0);
            if (swap)
            {
                int rf = ReserveIndex(v, FireOnager);
                if (rf >= 0 && free >= 0) return Deploy(FireOnager, true, rf, free);
                if (v.InSpots(FireOnager) >= FireNeeded) return Act.Hold;
                if (free >= 0) return Build(FireOnager, true, free);
                // one engine at a time to reserve (the next look builds a fire onager in its spot), so at least three keep firing
                for (int i = 0; i < v.Ranged.Length; i++)
                    if (v.Ranged[i].Type != FireOnager && v.Ranged[i].Active) return ToReserve(v.Ranged[i].Type, true, i);
                return Act.Hold;
            }
            int rt = ReserveIndex(v, Trebuchet);
            if (rt >= 0 && free >= 0) return Deploy(Trebuchet, true, rt, free);
            if (free >= 0 && v.InSpots(Trebuchet) < n) return Build(Trebuchet, true, free);
            // a spot left over (fewer trebuchets wanted than spots): any finished engine in reserve fills it
            if (free >= 0)
                for (int i = 0; i < v.Reserve.Count; i++)
                    if (v.ReserveBuilt[i] && IsRanged(v.Reserve[i]) && v.Reserve[i] != FireOnager) return Deploy(v.Reserve[i], true, i, free);
            return Act.Hold;
        }

        /// <summary>The defenders' next step against the player's own siege: fire catapults in every spot, a new one for each that falls.</summary>
        public static Act Defender(SideView v)
        {
            if (Busy(v, out Act busy)) return busy;
            int free = FreeSlot(v.Ranged);
            if (free < 0) return Act.Hold;
            int rf = ReserveIndex(v, FireCatapult);
            return rf >= 0 ? Deploy(FireCatapult, true, rf, free) : Build(FireCatapult, true, free);
        }

        public static bool IsRanged(string type) => type != null && type != Ram && type != "improved_ram" && type != Tower && type != "siege_tower_level2" && type != "ladder";
    }

    /// <summary>
    /// The siege drill's side in the game (0.20.0): the hook's answer every frame, read from the siege's engines, and what it remembers. Kept apart from
    /// SiegeDrill, whose choices the siege clock and the tests run without the game.
    /// </summary>
    internal static class SiegeDrillGame
    {

        /// <summary>Rolled out, by siege (place and start); the game's thread only.</summary>
        private static readonly Dictionary<string, bool> RolledOut = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly SiegeDrill.SideView Scratch = new SiegeDrill.SideView();
        private static bool _failed;

        public static string Key(SiegeEvent ev) => ev?.BesiegedSettlement?.StringId + "@" + (ev?.SiegeStartTime.ToHours ?? 0).ToString("0.###", Text.Inv);

        /// <summary>Remembered roll-out for a siege (false when not known), for the hourly copy.</summary>
        public static bool IsRolledOut(SiegeEvent ev) { string k = Key(ev); return k != null && RolledOut.TryGetValue(k, out bool r) && r; }

        public static void Fill(SiegeDrill.SideView v, SiegeEvent.SiegeEnginesContainer c, bool prepDone, int melee)
        {
            v.Clear(melee);
            v.PrepDone = prepDone;
            if (c == null) return;
            for (int i = 0; i < v.Melee.Length && i < c.DeployedMeleeSiegeEngines.Length; i++) v.Melee[i] = SlotOf(c.DeployedMeleeSiegeEngines[i]);
            for (int i = 0; i < v.Ranged.Length && i < c.DeployedRangedSiegeEngines.Length; i++) v.Ranged[i] = SlotOf(c.DeployedRangedSiegeEngines[i]);
            foreach (SiegeEvent.SiegeEngineConstructionProgress e in c.ReservedSiegeEngines)
            {
                v.Reserve.Add(e?.SiegeEngine?.StringId);
                v.ReserveBuilt.Add(e != null && e.IsConstructed);
            }
        }

        private static SiegeDrill.Slot SlotOf(SiegeEvent.SiegeEngineConstructionProgress e) => e?.SiegeEngine == null ? default(SiegeDrill.Slot)
            : new SiegeDrill.Slot { Type = e.SiegeEngine.StringId, Built = e.IsConstructed, Redeploying = e.IsBeingRedeployed };

        /// <summary>The working throwing engines in place of a side.</summary>
        public static int ActiveRanged(SiegeEvent.SiegeEnginesContainer c)
        {
            int n = 0;
            if (c?.DeployedRangedSiegeEngines == null) return 0;
            foreach (SiegeEvent.SiegeEngineConstructionProgress e in c.DeployedRangedSiegeEngines)
                if (e != null && e.IsActive && e.Hitpoints > 0f) n++;
            return n;
        }

        /// <summary>The player is in the siege's camp (leading it or in an army that does): the defenders face his own storm.</summary>
        public static bool PlayerBesieges(SiegeEvent ev)
        {
            MobileParty me = MobileParty.MainParty;
            if (me == null || ev?.BesiegerCamp == null) return false;
            if (me.BesiegerCamp == ev.BesiegerCamp) return true;
            return me.Army?.LeaderParty != null && me.Army.LeaderParty.BesiegerCamp == ev.BesiegerCamp && me.AttachedTo != null;
        }

        /// <summary>The human player is inside the besieged place: he fights its storm in person.</summary>
        public static bool PlayerDefends(SiegeEvent ev) => ev?.BesiegedSettlement != null && MobileParty.MainParty?.CurrentSettlement == ev.BesiegedSettlement;

        public static bool WallsTouched(Settlement s)
        {
            var walls = s?.SettlementWallSectionHitPointsRatioList;
            if (walls == null) return false;
            foreach (float w in walls) if (w < 0.999f) return true;
            return false;
        }

        public static bool WallsDown(Settlement s) => s != null && s.SettlementTotalWallHitPoints < 1e-5f;

        /// <summary>
        /// The game's thread, every frame for every side of every siege (GameHooks.DrillPatch): true leaves the step to the game's own strategy;
        /// false answers it with the drill's.
        /// </summary>
        public static bool Decide(ISiegeEventSide side, ref SiegeStrategyActionModel.SiegeAction action, ref SiegeEngineType type, ref int slot, ref int reserve)
        {
            if (!Settings.SiegeDrill || _failed) return true;
            try
            {
                SiegeEvent ev = side?.SiegeEvent;
                Settlement place = ev?.BesiegedSettlement;
                if (place?.Town == null || side.SiegeEngines == null) return true;
                // a side the player leads is his own (the game holds it for his choices)
                if (side.SiegeStrategy == DefaultSiegeStrategies.Custom) return true;
                SiegeDrill.Act a;
                string who;
                if (side.BattleSide == BattleSideEnum.Attacker)
                {
                    var camp = side as BesiegerCamp;
                    MobileParty lead = camp?.LeaderParty;
                    if (lead == null || lead == MobileParty.MainParty) return true;
                    DrillRule r = SiegeDrill.For(lead.StringId);
                    if (r.Mode == 0) return true;
                    Fill(Scratch, side.SiegeEngines, camp.IsPreparationComplete, 3);
                    string key = Key(ev);
                    bool rolled = RolledOut.TryGetValue(key, out bool ro) ? ro : SiegeDrill.LooksRolledOut(Scratch, WallsTouched(place));
                    a = SiegeDrill.Attacker(Scratch, r, PlayerDefends(ev), rolled, ActiveRanged(place.SiegeEngines), out bool rollNow);
                    if (!RolledOut.ContainsKey(key) || (rollNow && !ro)) RolledOut[key] = rolled || rollNow;
                    if (rollNow && !rolled)
                    {
                        Diag.Event("drill", "roll out", new { place = place.StringId, camp = lead.StringId, trebuchets = Scratch.ReserveOf(SiegeDrill.Trebuchet) });
                        Log.Line("siege drill: " + Names.Party(lead) + "'s camp at " + Names.Settlement(place) + " rolls out " + Scratch.ReserveOf(SiegeDrill.Trebuchet) + " trebuchets together");
                    }
                    who = lead.StringId;
                }
                else
                {
                    // the defenders: against the player's own siege only (the game's own choice against computer camps)
                    if (!PlayerBesieges(ev)) return true;
                    Fill(Scratch, side.SiegeEngines, true, 0);
                    a = SiegeDrill.Defender(Scratch);
                    who = place.StringId;
                }
                if (!Apply(a, side, ref action, ref type, ref slot, ref reserve)) return true;
                if (a.Kind != SiegeDrill.Kind.Hold) Diag.Event("drill", a.Kind.ToString().ToLowerInvariant(), new { place = place.StringId, side = side.BattleSide.ToString().ToLowerInvariant(), who, engine = a.Type, slot = a.Slot });
                return false;
            }
            catch (Exception e)
            {
                // never again this session: the game's own strategy takes over
                _failed = true;
                Diag.Swallowed("the siege drill", e);
                Log.Line("siege drill: switched off for this session after an error (" + e.GetType().Name + ": " + e.Message + "); the game's own siege strategies decide");
                return true;
            }
        }

        private static bool Apply(SiegeDrill.Act a, ISiegeEventSide side, ref SiegeStrategyActionModel.SiegeAction action, ref SiegeEngineType type, ref int slot, ref int reserve)
        {
            action = SiegeStrategyActionModel.SiegeAction.Hold;
            type = null;
            slot = -1;
            reserve = -1;
            if (a.Kind == SiegeDrill.Kind.Hold) return true;
            SiegeEngineType t = EngineType(a.Type);
            if (t == null) return false;
            switch (a.Kind)
            {
                case SiegeDrill.Kind.Build: action = SiegeStrategyActionModel.SiegeAction.ConstructNewSiegeEngine; break;
                case SiegeDrill.Kind.Deploy: action = SiegeStrategyActionModel.SiegeAction.DeploySiegeEngineFromReserve; break;
                case SiegeDrill.Kind.ToReserve: action = SiegeStrategyActionModel.SiegeAction.MoveSiegeEngineToReserve; break;
                case SiegeDrill.Kind.Scrap: action = SiegeStrategyActionModel.SiegeAction.RemoveDeployedSiegeEngine; break;
            }
            type = t;
            slot = a.Slot;
            reserve = a.Reserve;
            if (a.Kind == SiegeDrill.Kind.Deploy && (reserve < 0 || reserve >= side.SiegeEngines.ReservedSiegeEngines.Count)) return false;
            return true;
        }

        private static readonly Dictionary<string, SiegeEngineType> Types = new Dictionary<string, SiegeEngineType>(StringComparer.Ordinal);

        public static SiegeEngineType EngineType(string id)
        {
            if (id == null) return null;
            if (Types.TryGetValue(id, out SiegeEngineType t) && t != null) return t;
            switch (id)
            {
                case SiegeDrill.Trebuchet: t = DefaultSiegeEngineTypes.Trebuchet; break;
                case SiegeDrill.FireOnager: t = DefaultSiegeEngineTypes.FireOnager; break;
                case SiegeDrill.FireCatapult: t = DefaultSiegeEngineTypes.FireCatapult; break;
                case SiegeDrill.Tower: t = DefaultSiegeEngineTypes.SiegeTower; break;
                case SiegeDrill.Ram: t = DefaultSiegeEngineTypes.Ram; break;
                case "onager": t = DefaultSiegeEngineTypes.Onager; break;
                case "ballista": t = DefaultSiegeEngineTypes.Ballista; break;
                case "fire_ballista": t = DefaultSiegeEngineTypes.FireBallista; break;
                case "catapult": t = DefaultSiegeEngineTypes.Catapult; break;
                default: t = MBObjectManager.Instance?.GetObject<SiegeEngineType>(id); break;
            }
            Types[id] = t;
            return t;
        }

        /// <summary>A new campaign or load: what was remembered belongs to the old one.</summary>
        public static void Reset()
        {
            RolledOut.Clear();
            Types.Clear();
            _failed = false;
        }
    }
}
