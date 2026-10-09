using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// The only two places where the logs hook into the game's own code instead of just reading it. Each has its own
    /// switch; if one cannot attach, it says so and everything else carries on.
    /// - The movement watcher (full detail only). Every movement command in the game passes through one common step,
    ///   MobileParty.ResetAllMovementParameters. When anything other than us calls it for a lord holding our order,
    ///   the log names who: the game calling him into an army, its rule that a lord fights an enemy attacking the
    ///   place he guards, Party AI Controls, another mod.
    /// - The AI's reasons. The game's hourly decision step for a party (AiPartyThinkBehavior.PartyHourlyAiTick) gives
    ///   every option a score (besiege here, raid there, defend that) and then picks. For the watched realm's lords,
    ///   the options, their scores and the choice are recorded in lords.log.
    /// Harmony 2.4.2 comes from TAOM.Dependencies. Only InstallCore touches it, so a missing Harmony disables these
    /// two pieces and nothing else.
    /// </summary>
    internal static class GameHooks
    {
        private const string Owner = "bannerlordagents.logs";
        public static string Status = "not attached";
        public static bool WitnessInstalled, ReasonsInstalled;
        private static object _harmony;

        /// <summary>Game's thread: at start-up and whenever the settings change. Attaches what is wanted and not yet attached.</summary>
        public static void Apply()
        {
            bool wantWitness = Settings.Witness && Settings.Full;
            bool wantReasons = Settings.AiReasons;
            if ((wantWitness && !WitnessInstalled) || (wantReasons && !ReasonsInstalled))
            {
                try { InstallCore(wantWitness, wantReasons); }
                catch (Exception e)
                {
                    Status = "could not attach (" + e.GetType().Name + ": " + e.Message + ")";
                    Diag.Swallowed("game hooks", e);
                    Log.Line("game hooks: " + Status);
                }
            }
            Witness.Enabled = wantWitness && WitnessInstalled;
            Witness.Refresh();
            Reasons.Active = wantReasons && ReasonsInstalled;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallCore(bool witness, bool reasons)
        {
            var h = (HarmonyLib.Harmony)(_harmony ?? (_harmony = new HarmonyLib.Harmony(Owner)));
            var notes = new List<string>();
            if (witness && !WitnessInstalled)
            {
                MethodInfo target = typeof(MobileParty).GetMethod("ResetAllMovementParameters", BindingFlags.Instance | BindingFlags.NonPublic);
                if (target == null) notes.Add("movement watcher: the game's common movement step was not found, so it is off");
                else
                {
                    h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(WitnessPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)));
                    WitnessInstalled = Ours(target);
                    notes.Add("movement watcher " + (WitnessInstalled ? "attached" : "did not attach") + Others(target));
                }
            }
            if (reasons && !ReasonsInstalled)
            {
                MethodInfo target = typeof(AiPartyThinkBehavior).GetMethod("PartyHourlyAiTick", BindingFlags.Instance | BindingFlags.NonPublic);
                if (target == null) notes.Add("AI reasons: the game's decision step was not found, so it is off");
                else
                {
                    h.Patch(target,
                            prefix: new HarmonyLib.HarmonyMethod(typeof(ThinkPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyLib.HarmonyMethod(typeof(ThinkPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
                    ReasonsInstalled = Ours(target);
                    notes.Add("AI reasons " + (ReasonsInstalled ? "attached" : "did not attach") + Others(target));
                }
            }
            Status = string.Join("; ", notes);
            Log.Line("game hooks: " + Status);
            Diag.Event("hooks", "attached", new { witness = WitnessInstalled, reasons = ReasonsInstalled, status = Status });
        }

        private static bool Ours(MethodBase m)
        {
            HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
            return info != null && info.Owners.Contains(Owner);
        }

        /// <summary>Other mods patching the same game method, which matters when reading what these hooks report.</summary>
        private static string Others(MethodBase m)
        {
            HarmonyLib.Patches info = HarmonyLib.Harmony.GetPatchInfo(m);
            List<string> others = info?.Owners.Where(o => o != Owner).Distinct().ToList();
            return others != null && others.Count > 0 ? " (also patched by: " + string.Join(", ", others) + ")" : "";
        }
    }

    internal static class WitnessPatch
    {
        private static void Prefix(MobileParty __instance) => Witness.OnMovement(__instance);
    }

    internal static class ThinkPatch
    {
        private static void Prefix(MobileParty mobileParty, out Reasons.Before __state) => __state = Reasons.Before_(mobileParty);
        private static void Postfix(MobileParty mobileParty, Reasons.Before __state) => Reasons.After(mobileParty, __state);
    }

    /// <summary>The movement watcher's records. The hook may run on any thread; the lord watch reads the records on the game's thread.</summary>
    internal static class Witness
    {
        internal sealed class Record
        {
            public string Party;
            public long Wall;
            public bool MainThread;
            public string Culprit;
            public string[] Trail;
        }

        public static volatile bool Enabled;
        private static volatile bool _active;
        private static volatile MobileParty[] _watched = new MobileParty[0];
        /// <summary>Set around our own movement commands, so they are not reported.</summary>
        [ThreadStatic] public static bool Ours;
        public static readonly ConcurrentQueue<Record> Pending = new ConcurrentQueue<Record>();

        /// <summary>Game's thread: the parties of the lords holding our orders.</summary>
        public static void Watch(MobileParty[] parties)
        {
            _watched = parties ?? new MobileParty[0];
            Refresh();
        }

        public static void Refresh() => _active = Enabled && _watched.Length > 0;

        internal static void OnMovement(MobileParty p)
        {
            if (!_active || Ours) return;
            MobileParty[] w = _watched;
            for (int i = 0; i < w.Length; i++)
            {
                if (!ReferenceEquals(w[i], p)) continue;
                try { Capture(p); }
                catch (Exception e) { Diag.Swallowed("movement watcher", e); }
                return;
            }
        }

        private static void Capture(MobileParty p)
        {
            var st = new StackTrace(3, false);
            var trail = new List<string>(16);
            string culprit = null;
            for (int i = 0; i < st.FrameCount && trail.Count < 16; i++)
            {
                string name = Describe(st.GetFrame(i)?.GetMethod());
                if (name == null) continue;
                trail.Add(name);
                if (culprit == null && !Plumbing(name)) culprit = name;
            }
            var r = new Record
            {
                Party = p.StringId, Wall = DateTime.UtcNow.Ticks, Culprit = culprit ?? "unknown", Trail = trail.ToArray(),
                MainThread = Thread.CurrentThread.ManagedThreadId == Diag.MainThread
            };
            Pending.Enqueue(r);
            Diag.Count("witness:records");
            Diag.Event("witness", "plan changed by " + r.Culprit, new { party = r.Party, main_thread = r.MainThread, trail });
        }

        private static string Describe(MethodBase m)
        {
            if (m == null) return null;
            string type = m.DeclaringType?.FullName;
            string name = m.Name;
            if (type != null) return type + "." + name;
            // a method Harmony has rewritten is named like "DMD<Namespace.Type::Method>"
            int a = name.IndexOf('<'), b = name.LastIndexOf('>');
            return a >= 0 && b > a ? name.Substring(a + 1, b - a - 1).Replace("::", ".") : name;
        }

        /// <summary>The movement commands themselves and the hook's own frames: they carry a decision out, they do not make it.</summary>
        private static bool Plumbing(string n) =>
            n.StartsWith("TaleWorlds.CampaignSystem.Party.MobileParty.", StringComparison.Ordinal)
            || n.StartsWith("TaleWorlds.CampaignSystem.Actions.SetPartyAiAction", StringComparison.Ordinal)
            || n.StartsWith("BannerlordAgents.", StringComparison.Ordinal)
            || n.StartsWith("HarmonyLib.", StringComparison.Ordinal)
            || n.StartsWith("MonoMod.", StringComparison.Ordinal);
    }

    /// <summary>The game's AI weighing its options, for the watched realm's lords.</summary>
    internal static class Reasons
    {
        public struct Before
        {
            public bool On;
            public AiBehavior Behavior;
            public Settlement Settlement;
            public MobileParty Party;
            public bool HadArmy;
        }

        internal sealed class Option
        {
            public AiBehavior B;
            public string Target;
            public float X, Y, Score;
            public bool IsPoint, Army;
        }

        internal sealed class Thought
        {
            public double Hours;
            public string Party, Lord, LordName, BeforeTarget, AfterTarget;
            public AiBehavior BeforeB, AfterB;
            public float Objective;
            public bool DoNotChange, WillGather, ArmyCalled, ArmyLeader;
            public Option[] Options;
        }

        public static volatile bool Active;

        internal static Before Before_(MobileParty p)
        {
            if (!Active || p == null) return default(Before);
            try
            {
                Kingdom w = Bridge.Watched;
                if (w == null || p.LeaderHero == null || !p.IsLordParty || p.MapFaction != w) return default(Before);
                return new Before { On = true, Behavior = p.DefaultBehavior, Settlement = p.TargetSettlement, Party = p.TargetParty, HadArmy = p.Army != null };
            }
            catch (Exception e)
            {
                Diag.Swallowed("AI reasons (before)", e);
                return default(Before);
            }
        }

        internal static void After(MobileParty p, Before b)
        {
            if (!b.On) return;
            try
            {
                if (p.Ai.HourCounter != 1) return;   // it weighs its options only every 1 to 6 hours; this hour it did not
                PartyThinkParams tp = p.ThinkParamsCache;
                if (tp == null) return;
                var scores = tp.AIBehaviorScores;
                var opts = new Option[scores.Count];
                for (int i = 0; i < scores.Count; i++)
                {
                    (AIBehaviorData d, float score) = scores[i];
                    var o = new Option { B = d.AiBehavior, Score = score, Army = d.WillGatherArmy };
                    if (d.Party is Settlement s) o.Target = s.StringId;
                    else if (d.Party is MobileParty mp) o.Target = mp.StringId;
                    else { o.IsPoint = true; o.X = d.Position.X; o.Y = d.Position.Y; }
                    opts[i] = o;
                }
                var t = new Thought
                {
                    Hours = CampaignTime.Now.ToHours, Party = p.StringId, Lord = p.LeaderHero?.StringId, LordName = Names.Hero(p.LeaderHero),
                    BeforeB = b.Behavior, BeforeTarget = b.Settlement?.StringId ?? b.Party?.StringId,
                    AfterB = p.DefaultBehavior, AfterTarget = p.TargetSettlement?.StringId ?? p.TargetParty?.StringId,
                    Objective = tp.CurrentObjectiveValue, DoNotChange = tp.DoNotChangeBehavior, WillGather = tp.WillGatherAnArmy,
                    ArmyCalled = !b.HadArmy && p.Army != null && p.Army.LeaderParty == p, ArmyLeader = p.Army != null && p.Army.LeaderParty == p,
                    Options = opts
                };
                Diag.Count("ai:thoughts");
                Background.Post("AI reasons", () => Write(t));
            }
            catch (Exception e) { Diag.Swallowed("AI reasons (after)", e); }
        }

        /// <summary>The helper's half: names, the ranking, and the lines in lords.log.</summary>
        private static void Write(Thought t)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            Snapshot s = Brain.Latest;
            List<Option> sorted = t.Options.OrderByDescending(o => o.Score).ToList();
            Option best = sorted.FirstOrDefault();
            bool changed = t.BeforeB != t.AfterB || t.BeforeTarget != t.AfterTarget;
            if (changed) Diag.Count("ai:changes");
            // The game's own rule (AiPartyThinkBehavior): it moves to its best option only when a random roll falls under
            // that option's score, and only when the score clears a bar: 0.03 for patrols and visits, 0.1 otherwise,
            // doubled when it means gathering an army, a third of that for an army's leader.
            double bar = best == null ? 0
                : ((best.B == AiBehavior.PatrolAroundPoint || best.B == AiBehavior.GoToSettlement) ? 0.03 : 0.1) * (best.Army ? 2 : t.ArmyLeader ? 0.33 : 1);
            string plan = changed ? "changed plan: " + Words(s, t.BeforeB, t.BeforeTarget, null) + " -> " + Words(s, t.AfterB, t.AfterTarget, null)
                                  : "kept the plan: " + Words(s, t.AfterB, t.AfterTarget, null);
            if (!changed && !t.ArmyCalled)
            {
                // the common case, kept short: at high game speed the lords think many times a minute
                Option alt = sorted.FirstOrDefault(o => Words(s, o.B, o.Target, o) != Words(s, t.AfterB, t.AfterTarget, null));
                Diag.Lord(Text.When(t.Hours) + "  " + t.LordName + " [" + t.Lord + "] weighed " + t.Options.Length + " options and " + plan
                          + (alt != null ? " (best other: " + Words(s, alt.B, alt.Target, alt) + " " + alt.Score.ToString("0.000", inv) + ")" : "") + ".");
                Diag.Event("ai", "kept plan", new
                {
                    lord = t.Lord, plan = Words(s, t.AfterB, t.AfterTarget, null), options = t.Options.Length, best = best != null ? Math.Round(best.Score, 4) : (double?)null,
                    best_other = alt != null ? Words(s, alt.B, alt.Target, alt) : null, best_other_score = alt != null ? Math.Round(alt.Score, 4) : (double?)null
                });
                return;
            }
            Diag.Lord(Text.When(t.Hours) + "  " + t.LordName + " [" + t.Lord + "] weighed " + t.Options.Length + " options and " + plan
                      + (t.ArmyCalled ? "; the lord called an army" : "") + ".");
            if (sorted.Count > 0)
                Diag.Lord("    options: " + string.Join(" | ", sorted.Take(8).Select(o => Words(s, o.B, o.Target, o) + " " + o.Score.ToString("0.000", inv)
                                                                                       + (o.Army ? " (gathering an army)" : "")))
                          + (sorted.Count > 8 ? " | and " + (sorted.Count - 8) + " more" : ""));
            if (best != null)
                Diag.Lord("    best " + best.Score.ToString("0.000", inv) + " against a bar of " + bar.ToString("0.000", inv)
                          + (t.DoNotChange ? "; the game also told it not to change course this time" : "")
                          + (t.Objective > 0 ? "; its current plan was worth " + t.Objective.ToString("0.000", inv) : ""));
            Diag.Event("ai", changed ? "changed plan" : "called an army", new
            {
                lord = t.Lord, name = t.LordName, before = Words(s, t.BeforeB, t.BeforeTarget, null), after = Words(s, t.AfterB, t.AfterTarget, null),
                objective = Math.Round(t.Objective, 3), bar = Math.Round(bar, 3), do_not_change = t.DoNotChange, army_called = t.ArmyCalled,
                options = sorted.Take(10).Select(o => new { doing = o.B.ToString(), target = o.Target, score = Math.Round(o.Score, 4), army = o.Army }).ToList()
            });
        }

        private static string Words(Snapshot s, AiBehavior b, string target, Option point)
        {
            string name = Name(s, target);
            if (name == null && point != null && point.IsPoint) name = "a point at " + point.X.ToString("0", CultureInfo.InvariantCulture) + "," + point.Y.ToString("0", CultureInfo.InvariantCulture);
            name = name ?? "somewhere";
            switch (b)
            {
                case AiBehavior.BesiegeSettlement: return "besiege " + name;
                case AiBehavior.AssaultSettlement: return "assault " + name;
                case AiBehavior.RaidSettlement: return "raid " + name;
                case AiBehavior.DefendSettlement: return "defend " + name;
                case AiBehavior.PatrolAroundPoint: return "patrol around " + name;
                case AiBehavior.GoToSettlement: return "go to " + name;
                case AiBehavior.EscortParty: return "follow " + name;
                case AiBehavior.EngageParty: return "attack " + name;
                case AiBehavior.GoAroundParty: return "go around " + name;
                case AiBehavior.GoToPoint: return "go to " + name;
                case AiBehavior.MoveToNearestLandOrPort: return "make for land";
                case AiBehavior.Hold: return "hold";
                case AiBehavior.None: return "nothing";
                default: return b + " " + name;
            }
        }

        private static string Name(Snapshot s, string id)
        {
            if (id == null) return null;
            if (s != null && s.SettlementById.TryGetValue(id, out SettlementRow h)) return h.Name ?? id;
            if (s != null && s.PartyById.TryGetValue(id, out PartyRow r)) return r.Name ?? id;
            return id;
        }
    }
}
