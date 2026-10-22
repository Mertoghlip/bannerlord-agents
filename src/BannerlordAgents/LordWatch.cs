using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// What the game does with lords, seen from the game's thread:
    /// - every lord of the watched realm, once a second: a line in lords.log whenever his plan changes, including the
    ///   lords nobody orders (in shadow mode this sits beside the agent's orders, and it is the record of the game's
    ///   own AI that the study compares against);
    /// - lords holding our orders (any realm), four times a second in full detail: the moment anything changes, and
    ///   whoever the movement watcher saw doing it.
    /// It reads a handful of fields per lord and writes only when something changed.
    /// </summary>
    internal static class LordWatch
    {
        private sealed class Watched
        {
            public MobileParty Party;
            public string Lord, Name;
            public bool Ordered, Known, Gone;
            public AiBehavior B, Short;
            public Settlement Target, Inside, Besieging;
            public MobileParty TargetParty, ShortParty, ArmyLeader;
            public bool AiOff, Battle, Attached;
        }

        private static readonly List<Watched> Lords = new List<Watched>();
        private static long _nextFast, _nextSlow;
        private static readonly long Quarter = Stopwatch.Frequency / 4;

        public static int Count => Lords.Count;

        public static void Reset() => Lords.Clear();

        /// <summary>Hourly, and whenever orders change: who to watch.</summary>
        public static void Refresh()
        {
            Dictionary<MobileParty, Watched> old = Lords.Where(x => x.Party != null).GroupBy(x => x.Party).ToDictionary(g => g.Key, g => g.First());
            Lords.Clear();
            HashSet<MobileParty> ordered = Orders.HeldParties();
            foreach (MobileParty p in MobileParty.AllLordParties)
            {
                if (p == null || !p.IsActive || p.LeaderHero == null) continue;
                bool isOrdered = ordered.Contains(p);
                if (!Bridge.IsSeat(p.MapFaction) && !isOrdered) continue;
                if (!old.TryGetValue(p, out Watched x)) x = new Watched { Party = p, Lord = p.LeaderHero.StringId, Name = Names.Hero(p.LeaderHero) };
                x.Ordered = isOrdered;
                Lords.Add(x);
            }
        }

        /// <summary>Each campaign tick; does nothing unless it is time to look.</summary>
        public static void Tick()
        {
            long now = Stopwatch.GetTimestamp();
            bool slow = now >= _nextSlow;
            bool fast = Settings.Full && now >= _nextFast;
            if (!slow && !fast) return;
            if (slow) _nextSlow = now + Stopwatch.Frequency;
            if (fast) _nextFast = now + Quarter;
            for (int i = 0; i < Lords.Count; i++)
            {
                Watched x = Lords[i];
                if (slow || x.Ordered) Look(x);
            }
            while (Witness.Pending.TryDequeue(out Witness.Record r)) Witnessed(r);
        }

        private static void Look(Watched x)
        {
            MobileParty p = x.Party;
            if (p == null || !p.IsActive || p.LeaderHero == null)
            {
                if (!x.Gone)
                {
                    x.Gone = true;
                    Line(x, "the party is gone");
                }
                return;
            }
            AiBehavior b = p.DefaultBehavior, sb = p.ShortTermBehavior;
            Settlement ts = p.TargetSettlement, inside = p.CurrentSettlement, besieging = p.BesiegedSettlement;
            MobileParty tp = p.TargetParty, sp = p.ShortTermTargetParty;
            bool off = p.Ai.DoNotMakeNewDecisions, battle = p.MapEvent != null, attached = p.AttachedTo != null;
            MobileParty army = p.Army?.LeaderParty;
            if (!x.Known)
            {
                x.Known = true;
                Store(x, b, sb, ts, tp, sp, inside, besieging, off, battle, attached, army);
                return;
            }
            List<string> changes = null;
            if (b != x.B || ts != x.Target || tp != x.TargetParty)
                Add(ref changes, "plan " + Plan(x.B, x.Target, x.TargetParty) + " -> " + Plan(b, ts, tp));
            if (sb != x.Short || sp != x.ShortParty)
            {
                if (Reacting(sb, b)) Add(ref changes, "reacting: " + ShortText(sb, sp));
                else if (Reacting(x.Short, x.B)) Add(ref changes, "stopped " + ShortText(x.Short, x.ShortParty));
            }
            if (inside != x.Inside) Add(ref changes, inside != null ? "entered " + Names.Settlement(inside) : "left " + Names.Settlement(x.Inside));
            if (besieging != x.Besieging) Add(ref changes, besieging != null ? "began besieging " + Names.Settlement(besieging) : "stopped besieging " + Names.Settlement(x.Besieging));
            if (battle != x.Battle) Add(ref changes, battle ? "in a battle" : "out of the battle");
            if (army != x.ArmyLeader || attached != x.Attached)
                Add(ref changes, army == null ? "no longer in an army" : army == p ? "leads an army" : attached ? "marches in " + Names.Party(army) + "'s army" : "called to " + Names.Party(army) + "'s army");
            if (off != x.AiOff)
            {
                bool ours = Orders.TookSwitch(p);
                Add(ref changes, (off ? "the game's AI was switched off for this lord" : "the game's AI was switched back on for this lord") + (ours ? " (by us)" : " (not by us)"));
                if (!ours && x.Ordered && !off)
                    Diag.Alarm("switch_by_other", "Something other than us switched the game's AI back on for " + x.Name + ", who holds our order. The order is re-applied each hour.", false,
                               new { lord = x.Lord });
            }
            Store(x, b, sb, ts, tp, sp, inside, besieging, off, battle, attached, army);
            if (changes == null) return;
            Diag.Count("lord:changes");
            string text = string.Join("; ", changes);
            Line(x, text);
            Diag.Event("lord", "changed", new
            {
                lord = x.Lord, name = x.Name, ordered = x.Ordered, changes = text, doing = b.ToString(), short_term = sb.ToString(),
                target = ts?.StringId ?? tp?.StringId, inside = inside?.StringId, ai_off = off
            });
        }

        private static void Store(Watched x, AiBehavior b, AiBehavior sb, Settlement ts, MobileParty tp, MobileParty sp, Settlement inside, Settlement besieging,
                                  bool off, bool battle, bool attached, MobileParty army)
        {
            x.B = b;
            x.Short = sb;
            x.Target = ts;
            x.TargetParty = tp;
            x.ShortParty = sp;
            x.Inside = inside;
            x.Besieging = besieging;
            x.AiOff = off;
            x.Battle = battle;
            x.Attached = attached;
            x.ArmyLeader = army;
        }

        private static void Witnessed(Witness.Record r)
        {
            Watched x = Lords.FirstOrDefault(l => l.Party != null && l.Party.StringId == r.Party);
            string who = x != null ? x.Name + " [" + x.Lord + "]" : r.Party;
            Diag.Lord(Text.When(Diag.Hours) + "  " + who + ": the plan was changed by " + Friendly(r.Culprit) + (r.MainThread ? "" : ", from another thread") + ".");
            Orders.Witnessed(r.Party, Friendly(r.Culprit));
        }

        /// <summary>Plain words for the part of the game or the mod that changed a plan, with its own name kept for searching.</summary>
        public static string Friendly(string culprit)
        {
            if (string.IsNullOrEmpty(culprit) || culprit == "unknown") return "something the watcher could not name";
            string who;
            if (culprit.Contains("AiPartyThinkBehavior")) who = "the game's own hourly decision";
            else if (culprit.Contains("PartyAIControls")) who = "Party AI Controls";
            else if (culprit.Contains("TFAOMFixes")) who = "TFAOMFixes";
            else if (culprit.Contains(".Army.") || culprit.Contains("ArmyManagement") || culprit.Contains("AiArmy")) who = "the game's army logic";
            else if (culprit.Contains("MobilePartyAi")) who = "the game's quick reactions";
            else if (culprit.Contains("Siege")) who = "the game's siege logic";
            else if (culprit.Contains("MapEvent") || culprit.Contains("Encounter")) who = "the game's battle logic";
            else if (culprit.StartsWith("TaleWorlds.", StringComparison.Ordinal) || culprit.StartsWith("SandBox", StringComparison.Ordinal)) who = "the game";
            else who = "another mod";
            return who + " (" + culprit + ")";
        }

        private static bool Reacting(AiBehavior shortTerm, AiBehavior plan) =>
            shortTerm == AiBehavior.FleeToPoint || shortTerm == AiBehavior.FleeToGate || shortTerm == AiBehavior.FleeToParty
            || (shortTerm == AiBehavior.EngageParty && plan != AiBehavior.EngageParty)
            || (shortTerm == AiBehavior.GoAroundParty && plan != AiBehavior.GoAroundParty);

        private static string ShortText(AiBehavior b, MobileParty p)
        {
            string who = p != null ? Names.Party(p) : "a party";
            switch (b)
            {
                case AiBehavior.FleeToPoint:
                case AiBehavior.FleeToGate:
                case AiBehavior.FleeToParty: return "fleeing" + (p != null ? " from or toward " + who : "");
                case AiBehavior.EngageParty: return "chasing " + who;
                case AiBehavior.GoAroundParty: return "going around " + who;
                default: return b.ToString();
            }
        }

        private static string Plan(AiBehavior b, Settlement s, MobileParty p)
        {
            string target = s != null ? Names.Settlement(s) : p != null ? Names.Party(p) : null;
            switch (b)
            {
                case AiBehavior.GoToSettlement: return "go to " + (target ?? "a place");
                case AiBehavior.BesiegeSettlement: return "besiege " + (target ?? "a holding");
                case AiBehavior.AssaultSettlement: return "assault " + (target ?? "a holding");
                case AiBehavior.RaidSettlement: return "raid " + (target ?? "a village");
                case AiBehavior.DefendSettlement: return "defend " + (target ?? "a holding");
                case AiBehavior.PatrolAroundPoint: return "patrol" + (target != null ? " around " + target : "");
                case AiBehavior.EngageParty: return "attack " + (target ?? "a party");
                case AiBehavior.EscortParty: return "follow " + (target ?? "a party");
                case AiBehavior.GoAroundParty: return "go around " + (target ?? "a party");
                case AiBehavior.GoToPoint: return "march to a point";
                case AiBehavior.Hold: return "hold";
                case AiBehavior.None: return "no plan";
                default: return b + (target != null ? " " + target : "");
            }
        }

        private static void Line(Watched x, string text) =>
            Diag.Lord(Text.When(Diag.Hours) + "  " + x.Name + " [" + x.Lord + "]" + (x.Ordered ? " (holding our order)" : "") + ": " + text + ".");

        private static void Add(ref List<string> list, string s)
        {
            if (list == null) list = new List<string>(2);
            list.Add(s);
        }
    }
}
