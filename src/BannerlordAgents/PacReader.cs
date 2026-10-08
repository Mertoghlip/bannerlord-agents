using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerlordAgents
{
    /// <summary>
    /// Reads the orders the player gives vassals and clan parties, from Party AI Controls, without changing anything.
    ///
    /// The player's dialogue orders ("I have orders for you", in TFAOMFixes) and Party AI Controls' own screen both
    /// end up as a custom order on Party AI Controls' settings for that lord. Party AI Controls' members are internal,
    /// so they are reached by reflection. Only the list of lords who already hold an order is read: asking for any
    /// other lord's settings would make Party AI Controls create a new entry for him, and it saves those entries with
    /// the game. Game's thread only.
    /// </summary>
    internal static class PacReader
    {
        internal sealed class Change
        {
            public Hero Hero;
            public string Before, After;
            public object Target;
        }

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static bool _gaveUp;
        private static object _manager;
        private static PropertyInfo _withOrders;
        private static FieldInfo _table;
        private static readonly Dictionary<string, PropertyInfo> Props = new Dictionary<string, PropertyInfo>();
        private static Dictionary<string, string> _last;

        public static string Status = "not looked for yet";

        public static void Reset()
        {
            _manager = null;
            _gaveUp = false;
            _last = null;
            Status = "not looked for yet";
        }

        private static bool Resolve()
        {
            if (_manager != null) return true;
            if (_gaveUp) return false;
            Type sub = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { sub = a.GetType("PartyAIControls.SubModule", false); }
                catch (Exception e) { Diag.Swallowed("looking for Party AI Controls", e); }
                if (sub != null) break;
            }
            if (sub == null) { Status = "Party AI Controls is not loaded, so vassal orders are not recorded"; _gaveUp = true; return false; }
            object mgr = null;
            try
            {
                FieldInfo f = sub.GetField("PartySettingsManager", Any);
                mgr = f != null ? f.GetValue(null) : sub.GetProperty("PartySettingsManager", Any)?.GetValue(null, null);
            }
            catch (Exception e) { Diag.Swallowed("reading Party AI Controls", e); }
            if (mgr == null) { Status = "Party AI Controls found, its settings not ready yet"; return false; }
            _withOrders = mgr.GetType().GetProperty("HeroesWithOrders", Any);
            _table = _withOrders == null ? mgr.GetType().GetField("_partySettings", Any) : null;
            if (_withOrders == null && _table == null) { Status = "Party AI Controls' order list was not found, so vassal orders are not recorded"; _gaveUp = true; return false; }
            _manager = mgr;
            Status = "reading Party AI Controls' " + (_withOrders != null ? "list of lords holding orders" : "settings table") + ", read-only";
            return true;
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            Type t = o.GetType();
            string key = t.FullName + "." + name;
            if (!Props.TryGetValue(key, out PropertyInfo p)) Props[key] = p = t.GetProperty(name, Any);
            try { return p?.GetValue(o, null); }
            catch (Exception e) { Diag.Swallowed("reading Party AI Controls (" + name + ")", e); return null; }
        }

        /// <summary>The orders held right now: hero id to "Behavior|target id".</summary>
        private static Dictionary<string, KeyValuePair<string, object>> Read(out Dictionary<string, Hero> heroes)
        {
            var now = new Dictionary<string, KeyValuePair<string, object>>();
            heroes = new Dictionary<string, Hero>();
            IEnumerable items = null;
            try
            {
                items = _withOrders != null ? _withOrders.GetValue(_manager, null) as IEnumerable
                                            : (_table.GetValue(_manager) as IDictionary)?.Values;
            }
            catch (Exception e)
            {
                Diag.Count("pac:read failed");
                Diag.Swallowed("reading Party AI Controls' order list", e);
                Diag.Alarm("pac_read_failed", "Party AI Controls' list of vassal orders could not be read, so your dialogue orders are not being recorded.");
            }
            if (items == null) return now;
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            foreach (object st in items)
            {
                if (!(Prop(st, "Hero") is Hero h)) continue;
                if (h.Clan != Clan.PlayerClan && (mine == null || h.Clan?.Kingdom != mine)) continue;
                object order = Prop(st, "Order");
                string behaviour = Prop(order, "Behavior")?.ToString();
                if (string.IsNullOrEmpty(behaviour) || behaviour == "None") continue;
                object target = Prop(order, "Target");
                now[h.StringId] = new KeyValuePair<string, object>(behaviour + "|" + TargetId(target), target);
                heroes[h.StringId] = h;
            }
            return now;
        }

        /// <summary>What changed since the last look. The first look after loading only sets the baseline.</summary>
        public static List<Change> Poll()
        {
            var changes = new List<Change>();
            if (!Resolve()) return changes;
            Dictionary<string, KeyValuePair<string, object>> now = Read(out Dictionary<string, Hero> heroes);
            if (_last == null)
            {
                _last = new Dictionary<string, string>();
                foreach (var kv in now) _last[kv.Key] = kv.Value.Key;
                Log.Line("vassal orders: " + _last.Count + " lords hold orders at the start (" + Status + ")");
                return changes;
            }
            foreach (var kv in now)
            {
                _last.TryGetValue(kv.Key, out string before);
                if (before == kv.Value.Key) continue;
                changes.Add(new Change { Hero = heroes[kv.Key], Before = before, After = kv.Value.Key, Target = kv.Value.Value });
            }
            foreach (var kv in _last)
            {
                if (now.ContainsKey(kv.Key)) continue;
                changes.Add(new Change { Hero = Hero.FindFirst(h => h.StringId == kv.Key), Before = kv.Value, After = null });
            }
            _last = new Dictionary<string, string>();
            foreach (var kv in now) _last[kv.Key] = kv.Value.Key;
            if (changes.Count > 0)
                Diag.Event("pac", "changes", new { holding = now.Count, changes = changes.Select(c => (c.Hero?.StringId ?? "?") + ": " + (c.Before ?? "none") + " -> " + (c.After ?? "none")).ToList() });
            return changes;
        }

        /// <summary>Whether a lord holds an order in Party AI Controls. Read-only.</summary>
        public static bool HoldsOrder(Hero h) => OrderOf(h) != null;

        /// <summary>The order a lord holds in Party AI Controls, in words, or null. Read-only, like everything here.</summary>
        public static string OrderOf(Hero h)
        {
            if (h == null || !Resolve()) return null;
            Dictionary<string, KeyValuePair<string, object>> now = Read(out _);
            return now.TryGetValue(h.StringId, out KeyValuePair<string, object> kv) ? Words(kv.Key, kv.Value) : null;
        }

        public static string TargetId(object target)
        {
            if (target is Settlement s) return s.StringId;
            if (target is MobileParty p) return p.StringId;
            if (target is PartyBase pb) return pb.Id;
            return "";
        }

        /// <summary>"defend Minas Tirith", "patrol their own lands", ...</summary>
        public static string Words(string behaviourAndTarget, object target)
        {
            string b = behaviourAndTarget?.Split('|')[0];
            string name = target is Settlement s ? Names.Settlement(s) : target is MobileParty p ? Names.Party(p) : null;
            switch (b)
            {
                case "DefendSettlement": return "defend " + (name ?? "a fief");
                case "PatrolAroundPoint": return "patrol around " + (name ?? "a point");
                case "PatrolClanLands": return "patrol their own lands";
                case "StayInSettlement": return "wait in " + (name ?? "a fief");
                case "RecruitFromTemplate": return "go and fill their ranks";
                case "BesiegeSettlement": return "besiege " + (name ?? "a holding");
                case "EscortParty": return "follow " + (name ?? "a party");
                case "AttackParty": return "attack " + (name ?? "a party");
                case "VisitSettlement": return "visit " + (name ?? "a place");
                default: return (b ?? "an order") + (name != null ? " " + name : "");
            }
        }
    }
}
