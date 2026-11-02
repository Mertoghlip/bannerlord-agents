using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerlordAgents
{
    /// <summary>
    /// The lieutenant and Party AI Controls together (0.11.1; Mert, Oct 23: "The liutenant should be able to work in tandem with
    /// party ai controls. Any order given by the liutenant should override any order given by party ai controls, and it should be
    /// able to change backup orders, troop templates, and all options as well. All changes made by the liutenant and all of its
    /// orders should also be reflected in the dashboards of party ai controls as well.")
    /// - An order of the lieutenant's for a lord of the realm is carried out by our order layer, as every agent's is (Orders), and
    ///   shown in Party AI Controls as that lord's order: the nearest of its own kinds (PacRules.MirrorOf), or none where it has no
    ///   such kind (raid, shadow, a watch post, armies). It replaces the order he held there; his queue and backup order stay.
    ///   While it runs, Party AI Controls' hourly look and its settlement-entry check pass him by (GameHooks), so the two never pull
    ///   against each other; when it ends, the next order of his queue starts there, or his backup order once he is free.
    /// - If the ruler gives such a lord another order in Party AI Controls (its panel, or the "I have orders for you" talk) while
    ///   the lieutenant's runs, the ruler's wins: the lieutenant's order ends (Skip, Hourly).
    /// - The lieutenant's party_ai order (Apply) sets his Party AI Controls order itself (carried out by Party AI Controls), his
    ///   queue, his backup order, his troop template (or a new one), his troop mix and every option of the panel.
    /// - A vassal Party AI Controls does not manage is made manageable the way the ruler's own talk makes him: TFAOMFixes' list
    ///   of commanded vassals, reached by name; a new entry gets auto-recruit and troop dismissal off, as that talk's do. The
    ///   panel lists only lords it manages, so this is what puts him there.
    /// - Saves: our mirrors, and the entries and list places made only for them, are taken out before every save and put back
    ///   after (with the orders' own hand-back), so a save holds only what the ruler or the lieutenant set on purpose.
    /// Party AI Controls' members are internal: reached by name, checked against the installed 1.4.5 DLL. Game's thread.
    /// </summary>
    internal static class PacLink
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        /// <summary>A lord one of the lieutenant's orders holds, and what Party AI Controls shows for it.</summary>
        private sealed class Drive
        {
            public Hero Lord;
            public MobileParty Party;
            public int OrderId;
            /// <summary>The order object we put in his settings (null: none shown, his old one cleared).</summary>
            public object Mirror;
            public string Key = "";
        }

        private static bool _resolved, _gaveUp;
        private static object _mgr;
        private static Type _tSettings, _tOrder, _tKind, _tTemplate, _tComposition;
        private static MethodInfo _settingsOf, _manageable, _orderSet, _setTemplate;
        private static FieldInfo _table, _templates, _manageKingdom, _defaultsClan, _defaultsKingdom;
        private static HashSet<string> _commanded;
        private static readonly Dictionary<string, PropertyInfo> Props = new Dictionary<string, PropertyInfo>();

        private static readonly Dictionary<MobileParty, Drive> Driving = new Dictionary<MobileParty, Drive>();
        // Party AI Controls orders of ours now, lord id to "Kind|target id" ("" for a cleared one): the recorder and the runs ask
        private static readonly Dictionary<string, string> Ours = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> JustCleared = new HashSet<string>(StringComparer.Ordinal);
        // lords the lieutenant set Party AI Controls settings for (their entries stay, saves included)
        private static readonly HashSet<string> Standing = new HashSet<string>(StringComparer.Ordinal);
        // entries made and vassals made manageable only for the lieutenant's orders
        private static readonly HashSet<string> Created = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Added = new HashSet<string>(StringComparer.Ordinal);
        // what a save took out, put back after it
        private static readonly List<KeyValuePair<object, object>> StrippedOrders = new List<KeyValuePair<object, object>>();
        private static readonly List<KeyValuePair<Hero, object>> StrippedEntries = new List<KeyValuePair<Hero, object>>();
        private static readonly List<string> StrippedAdded = new List<string>();
        private static string _file;

        public static string Status = "not looked for yet";

        /// <summary>A campaign was loaded: nothing of ours is held (mirrors never reach a save); the lieutenant's own Party AI Controls orders of this save's history are read back for the record.</summary>
        public static void Reset(History history)
        {
            Driving.Clear();
            Ours.Clear();
            JustCleared.Clear();
            Standing.Clear();
            Created.Clear();
            Added.Clear();
            StrippedOrders.Clear();
            StrippedEntries.Clear();
            StrippedAdded.Clear();
            _resolved = _gaveUp = false;
            _mgr = null;
            Status = "not looked for yet";
            _file = Path.Combine(Paths.Data(Bridge.CampaignId), "lieutenant", "party_ai.jsonl");
            if (history == null) return;
            foreach (JObject o in LabelledLines.Read(_file, history))
            {
                string lord = (string)o["lord"];
                if (string.IsNullOrEmpty(lord)) continue;
                if (o["key"] != null) Ours[lord] = (string)o["key"] ?? "";
                if ((bool?)o["standing"] == true) Standing.Add(lord);
                if ((bool?)o["standing"] == false) Standing.Remove(lord);
            }
        }

        // ---------- Party AI Controls by name ----------

        private static bool Resolve()
        {
            if (_resolved) return _mgr != null;
            if (_gaveUp) return false;
            Type sub = HarmonyLib.AccessTools.TypeByName("PartyAIControls.SubModule");
            if (sub == null) { Status = "Party AI Controls is not loaded"; _gaveUp = true; return false; }
            try { _mgr = sub.GetField("PartySettingsManager", Any)?.GetValue(null); }
            catch (Exception e) { Diag.Swallowed("reaching Party AI Controls", e); }
            if (_mgr == null) { Status = "Party AI Controls found, its settings not ready yet"; return false; }
            Type m = _mgr.GetType();
            _settingsOf = m.GetMethod("Settings", Any, null, new[] { typeof(Hero) }, null);
            _manageable = m.GetMethod("IsHeroManageable", Any, null, new[] { typeof(Hero) }, null);
            _table = m.GetField("_partySettings", Any);
            _templates = m.GetField("_partyTemplates", Any);
            _manageKingdom = m.GetField("ManageKingdomParties", Any);
            _defaultsClan = m.GetField("_defaultClanPartySettings", Any);
            _defaultsKingdom = m.GetField("_defaultKingdomPartySettings", Any);
            _tSettings = HarmonyLib.AccessTools.TypeByName("PartyAIControls.PartyAIClanPartySettings");
            _tOrder = HarmonyLib.AccessTools.TypeByName("PartyAIControls.PAICustomOrder");
            _tKind = _tOrder?.GetNestedType("OrderType", Any);
            _tTemplate = HarmonyLib.AccessTools.TypeByName("PartyAIControls.PAICustomTemplate");
            _tComposition = HarmonyLib.AccessTools.TypeByName("PartyAIControls.PartyCompositionObect");
            _orderSet = _tSettings?.GetProperty("Order", Any)?.GetSetMethod(true);
            _setTemplate = _tSettings?.GetMethod("SetPartyTemplate", Any);
            var missing = new List<string>();
            if (_settingsOf == null) missing.Add("Settings(Hero)");
            if (_manageable == null) missing.Add("IsHeroManageable");
            if (_table == null) missing.Add("_partySettings");
            if (_templates == null) missing.Add("_partyTemplates");
            if (_tSettings == null) missing.Add("PartyAIClanPartySettings");
            if (_tOrder == null || _tKind == null) missing.Add("PAICustomOrder.OrderType");
            if (_tTemplate == null) missing.Add("PAICustomTemplate");
            if (_tComposition == null) missing.Add("PartyCompositionObect");
            if (_orderSet == null) missing.Add("settings' Order");
            _resolved = true;
            if (missing.Count > 0)
            {
                _mgr = null;
                Status = "Party AI Controls' " + string.Join(", ", missing) + " not found, so the lieutenant's orders are not shown there";
                Diag.Alarm("pac_link", "Party AI Controls changed: " + string.Join(", ", missing) + " not found, so the lieutenant cannot show its orders there or change its settings.", true);
                return false;
            }
            try
            {
                Type vo = HarmonyLib.AccessTools.TypeByName("TFAOMFixes.VassalOrders");
                _commanded = vo?.GetField("Commanded", Any)?.GetValue(null) as HashSet<string>;
            }
            catch (Exception e) { Diag.Swallowed("finding the commanded vassals' list", e); }
            Status = "linked" + (_commanded != null ? ", vassals made manageable through TFAOMFixes' commanded list" : ", vassals manageable only if Party AI Controls manages the kingdom's parties");
            Log.Line("party ai link: " + Status);
            return true;
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            PropertyInfo p = PropOf(o.GetType(), name);
            try { return p?.GetValue(o, null); }
            catch (Exception e) { Diag.Swallowed("reading Party AI Controls (" + name + ")", e); return null; }
        }

        private static void SetProp(object o, string name, object value)
        {
            PropertyInfo p = PropOf(o.GetType(), name);
            MethodInfo set = p?.GetSetMethod(true);
            if (set == null) throw new MissingMemberException("Party AI Controls has no settable " + name);
            set.Invoke(o, new[] { value });
        }

        private static PropertyInfo PropOf(Type t, string name)
        {
            string key = t.FullName + "." + name;
            if (!Props.TryGetValue(key, out PropertyInfo p)) Props[key] = p = t.GetProperty(name, Any);
            return p;
        }

        /// <summary>A lord's settings if Party AI Controls already has them (never makes an entry).</summary>
        private static object Existing(Hero h)
        {
            if (h == null || !Resolve()) return null;
            try { return _table.GetValue(_mgr) is IDictionary d && d.Contains(h) ? d[h] : null; }
            catch (Exception e) { Diag.Swallowed("reading Party AI Controls' settings", e); return null; }
        }

        private static bool Manageable(Hero h)
        {
            try { return (bool)_manageable.Invoke(_mgr, new object[] { h }); }
            catch (Exception e) { Diag.Swallowed("asking Party AI Controls who it manages", e); return false; }
        }

        private static bool ManagesKingdom => _manageKingdom?.GetValue(_mgr) is bool b && b;

        /// <summary>
        /// A lord's settings, made manageable and given an entry when he has none (as the ruler's own talk does), or null and why.
        /// Only for lords of the ruler's realm, the ruler ruling it.
        /// </summary>
        private static object Ensure(Hero h, out string why)
        {
            why = null;
            if (!Resolve()) { why = Status; return null; }
            Kingdom mine = Clan.PlayerClan?.Kingdom;
            if (h == null || h == Hero.MainHero) { why = "that is the ruler"; return null; }
            if (h.Clan != Clan.PlayerClan && (mine == null || h.Clan?.Kingdom != mine || mine.Leader != Hero.MainHero))
            {
                why = Names.Hero(h) + " is not a lord of the ruler's own realm";
                return null;
            }
            object s = Existing(h);
            bool wasManaged = Manageable(h);
            if (!wasManaged && h.Clan != Clan.PlayerClan)
            {
                if (_commanded == null && !ManagesKingdom)
                {
                    why = "Party AI Controls manages only the ruler's clan here (its kingdom parties switch is off, and TFAOMFixes' talk-to-lords orders are not loaded)";
                    return null;
                }
                if (_commanded != null && _commanded.Add(h.StringId)) Added.Add(h.StringId);
            }
            if (s == null)
            {
                try { s = _settingsOf.Invoke(_mgr, new object[] { h }); }
                catch (Exception e) { Diag.Error("making Party AI Controls' entry for a lord", e); why = "Party AI Controls would not take him (" + e.Message + ")"; return null; }
                if (s == null || !(Prop(s, "Hero") is Hero))
                {
                    if (Added.Remove(h.StringId)) _commanded?.Remove(h.StringId);
                    why = "Party AI Controls gave no settings for " + Names.Hero(h);
                    return null;
                }
                Created.Add(h.StringId);
                if (h.Clan != Clan.PlayerClan)
                {
                    // as the ruler's own talk does: nothing beyond the orders happens to a vassal it starts managing
                    try
                    {
                        SetProp(s, "AutoRecruitment", false);
                        SetProp(s, "DismissUnwantedTroops", false);
                    }
                    catch (Exception e) { Diag.Swallowed("switching off auto-recruit for a new entry", e); }
                }
                Diag.Event("pac", "entry made", new { lord = h.StringId, added_to_commanded = Added.Contains(h.StringId) });
            }
            return s;
        }

        private static object NewOrder(string kind, IMapPoint target)
        {
            object k = Enum.Parse(_tKind, kind);
            return Activator.CreateInstance(_tOrder, new object[] { target, k });
        }

        private static bool Active(object order) => order != null && Prop(order, "Behavior")?.ToString() is string b && b != "None";

        private static string KeyOf(object order) => order == null ? "" : (Prop(order, "Behavior")?.ToString() ?? "") + "|" + PacReader.TargetId(Prop(order, "Target"));

        private static string WordsOf(object order) => order == null ? "no order" : PacReader.Words(KeyOf(order), Prop(order, "Target"));

        private static IList Queue(object s) => Prop(s, "OrderQueue") as IList;

        private static void SetOrder(object s, object order) => _orderSet.Invoke(s, new[] { order });

        // ---------- the lieutenant's orders, shown ----------

        /// <summary>
        /// One of our orders started for a lord (Orders.StartTool). The lieutenant's, for a lord of the ruler's realm: shown in Party
        /// AI Controls as his order, replacing the one he held there. Anyone else's: a mirror of ours left on him goes. Returns a
        /// note for the receipt, or null.
        /// </summary>
        public static string OnOrderStarted(int orderId, Hero lord, MobileParty party, string source, Kingdom realm, string tool, string atId, string targetId)
        {
            if (party == null || lord == null) return null;
            bool lieutenant = source == "agent" && realm != null && Lieutenant.IsRealm(realm.StringId);
            Driving.TryGetValue(party, out Drive old);
            if (!lieutenant)
            {
                if (old != null) { Unshow(old, false); Driving.Remove(party); }
                return null;
            }
            object s = Ensure(lord, out string why);
            if (s == null)
            {
                Driving.Remove(party);
                Diag.Event("pac", "not shown", new { lord = lord.StringId, tool, why });
                return " (not shown in Party AI Controls: " + why + ")";
            }
            object current = Prop(s, "Order");
            string note = null;
            bool mine = old != null && ReferenceEquals(current, old.Mirror);
            if (Active(current) && !mine) note = " (it replaces his Party AI Controls order, " + WordsOf(current) + ")";
            Settlement at = !string.IsNullOrEmpty(atId) ? Settlement.Find(atId) : null;
            MobileParty target = !string.IsNullOrEmpty(targetId) ? Orders.PartyById(targetId) : null;
            string kind = PacRules.MirrorOf(tool, at != null);
            object mirror = null;
            try
            {
                IMapPoint point = PacRules.UsesParty(kind) ? (IMapPoint)target : at;
                if (kind != null && (point != null || kind == "RecruitFromTemplate")) mirror = NewOrder(kind, point);
                SetOrder(s, mirror);
            }
            catch (Exception e)
            {
                Diag.Error("showing the lieutenant's order in Party AI Controls", e);
                return " (not shown in Party AI Controls: " + e.Message + ")";
            }
            var d = new Drive { Lord = lord, Party = party, OrderId = orderId, Mirror = mirror, Key = KeyOf(mirror) };
            Driving[party] = d;
            if (old != null && Ours.TryGetValue(lord.StringId, out string was) && was.Length > 0) JustCleared.Add(lord.StringId + "#" + was);
            Ours[lord.StringId] = d.Key;
            Diag.Event("pac", "shown", new { lord = lord.StringId, order = orderId, tool, shown = mirror != null ? d.Key : null, replaced = note != null ? KeyOf(current) : null });
            return note;
        }

        /// <summary>One of our orders ended (Orders.End): its mirror goes, and his next queued order starts there unless it was replaced by one of the lieutenant's party_ai orders.</summary>
        public static void OnOrderEnded(MobileParty party, int orderId, string key)
        {
            if (party == null || !Driving.TryGetValue(party, out Drive d) || d.OrderId != orderId) return;
            Driving.Remove(party);
            Unshow(d, key != "party ai order");
        }

        /// <summary>The mirror comes off (if it is still ours): the next order of his queue starts, or none; a vassal managed only for it is handed back.</summary>
        private static void Unshow(Drive d, bool next)
        {
            object s = Existing(d.Lord);
            if (s == null) return;
            object current = Prop(s, "Order");
            string id = d.Lord.StringId;
            if (current == null || ReferenceEquals(current, d.Mirror))
            {
                object following = null;
                IList q = Queue(s);
                if (next && q != null && q.Count > 0) { following = q[0]; q.RemoveAt(0); }
                try { SetOrder(s, following); }
                catch (Exception e) { Diag.Error("taking the lieutenant's order off Party AI Controls", e); }
                if (d.Key.Length > 0) JustCleared.Add(id + "#" + d.Key);
                Ours.Remove(id);
                Diag.Event("pac", "unshown", new { lord = id, order = d.OrderId, next = following != null ? KeyOf(following) : null });
            }
            HandBackIfFresh(d.Lord, s);
        }

        /// <summary>A vassal made manageable and given an entry only for the lieutenant's orders goes back as he was, once nothing of Party AI Controls' is his.</summary>
        private static void HandBackIfFresh(Hero h, object s)
        {
            string id = h.StringId;
            if (Standing.Contains(id) || Driving.Values.Any(x => x.Lord == h)) return;
            if (!Fresh(s)) { Created.Remove(id); Added.Remove(id); return; }   // the ruler has set something there: it is his now
            if (Added.Remove(id)) _commanded?.Remove(id);
            if (Created.Remove(id))
            {
                try { (_table.GetValue(_mgr) as IDictionary)?.Remove(h); }
                catch (Exception e) { Diag.Swallowed("removing an entry made for the lieutenant", e); }
            }
        }

        /// <summary>An entry with nothing set on it: no order of anyone's, no queue, no backup order, no template.</summary>
        private static bool Fresh(object s)
        {
            object o = Prop(s, "Order");
            if (Active(o) && !Driving.Values.Any(x => ReferenceEquals(x.Mirror, o))) return false;
            return (Queue(s)?.Count ?? 0) == 0 && Prop(s, "FallbackOrder") == null && Prop(s, "PartyTemplate") == null;
        }

        /// <summary>
        /// Party AI Controls' hourly look and settlement entry (GameHooks), for every party: true when one of the lieutenant's orders
        /// holds it, so Party AI Controls passes it by. A lord given another order there meanwhile (the ruler's) is let go: the
        /// lieutenant's order ends. No allocation on the way through.
        /// </summary>
        public static bool Skip(MobileParty p)
        {
            if (Driving.Count == 0 || p == null || !Driving.TryGetValue(p, out Drive d)) return false;
            return !TakenBack(d);
        }

        /// <summary>Every in-game hour (Orders.Hourly): the ruler's own orders win; a mirror Party AI Controls cleared (an army's call) is put back while ours runs.</summary>
        public static void Hourly()
        {
            if (Driving.Count == 0) return;
            foreach (Drive d in Driving.Values.ToList())
            {
                if (TakenBack(d)) continue;
                if (d.Mirror == null) continue;
                object s = Existing(d.Lord);
                if (s != null && Prop(s, "Order") == null && d.Party.Army == null)
                {
                    try { SetOrder(s, d.Mirror); }
                    catch (Exception e) { Diag.Swallowed("putting the lieutenant's order back in Party AI Controls", e); }
                }
            }
        }

        private static bool TakenBack(Drive d)
        {
            object s = Existing(d.Lord);
            object current = s != null ? Prop(s, "Order") : null;
            if (current == null || ReferenceEquals(current, d.Mirror) || !Active(current)) return false;
            string words = WordsOf(current);
            Driving.Remove(d.Party);
            Ours.Remove(d.Lord.StringId);
            // he is the ruler's now: his entry and his place on the commanded list stay as they are
            Created.Remove(d.Lord.StringId);
            Added.Remove(d.Lord.StringId);
            Diag.Event("pac", "taken back", new { lord = d.Lord.StringId, order = d.OrderId, now = KeyOf(current) });
            Orders.EndFor(d.Party, "Party AI Controls now holds another order for him (" + words + "): yours, or the next of his queue", "taken back");
            return true;
        }

        // ---------- saves ----------

        /// <summary>Just before a save (Orders.HandBackForSave): our mirrors out, and the entries and list places made only for them.</summary>
        public static void StripForSave()
        {
            StrippedOrders.Clear();
            StrippedEntries.Clear();
            StrippedAdded.Clear();
            if (_mgr == null) return;
            try
            {
                foreach (Drive d in Driving.Values)
                {
                    object s = Existing(d.Lord);
                    if (s == null || d.Mirror == null || !ReferenceEquals(Prop(s, "Order"), d.Mirror)) continue;
                    SetOrder(s, null);
                    StrippedOrders.Add(new KeyValuePair<object, object>(s, d.Mirror));
                }
                IDictionary table = _table.GetValue(_mgr) as IDictionary;
                foreach (string id in Created.ToList())
                {
                    Hero h = Hero.Find(id);
                    object s = h != null ? Existing(h) : null;
                    if (s == null || Standing.Contains(id) || !Fresh(s)) continue;
                    table?.Remove(h);
                    StrippedEntries.Add(new KeyValuePair<Hero, object>(h, s));
                }
                foreach (string id in Added.ToList())
                {
                    Hero h = Hero.Find(id);
                    object s = h != null ? Existing(h) : null;
                    if (Standing.Contains(id) || (s != null && !Fresh(s)) || _commanded == null) continue;   // the ruler's now, or set on purpose
                    if (_commanded.Remove(id)) StrippedAdded.Add(id);
                }
            }
            catch (Exception e) { Diag.Error("taking the lieutenant's orders out of Party AI Controls before a save", e); }
            if (StrippedOrders.Count + StrippedEntries.Count + StrippedAdded.Count > 0)
                Diag.Event("pac", "out for the save", new { orders = StrippedOrders.Count, entries = StrippedEntries.Count, commanded = StrippedAdded.Count });
        }

        /// <summary>Right after a save: everything StripForSave took out goes back.</summary>
        public static void RestoreAfterSave()
        {
            try
            {
                IDictionary table = _mgr != null ? _table.GetValue(_mgr) as IDictionary : null;
                foreach (KeyValuePair<Hero, object> kv in StrippedEntries)
                    if (table != null && !table.Contains(kv.Key)) table[kv.Key] = kv.Value;
                foreach (KeyValuePair<object, object> kv in StrippedOrders)
                    if (Prop(kv.Key, "Order") == null) SetOrder(kv.Key, kv.Value);
                foreach (string id in StrippedAdded) _commanded?.Add(id);
            }
            catch (Exception e) { Diag.Error("putting the lieutenant's orders back in Party AI Controls after a save", e); }
            StrippedOrders.Clear();
            StrippedEntries.Clear();
            StrippedAdded.Clear();
        }

        // ---------- the playerless switch (Playerless) ----------

        /// <summary>Has this lord a Party AI Controls order or queue now? (Never makes an entry.)</summary>
        public static bool HasOrders(Hero h)
        {
            object s = Existing(h);
            return s != null && (Active(Prop(s, "Order")) || (Queue(s)?.Count ?? 0) > 0);
        }

        /// <summary>
        /// "Act as you see fit", for the runs without the player: his Party AI Controls order, queue and backup order cleared, off
        /// TFAOMFixes' commanded list, the game's AI on unless an order of ours holds him. True when anything was cleared.
        /// </summary>
        public static bool Release(Hero h)
        {
            if (h == null || !Resolve()) return false;
            bool any = false;
            object s = Existing(h);
            try
            {
                if (s != null)
                {
                    if (Prop(s, "Order") != null) { SetOrder(s, null); any = true; }
                    IList q = Queue(s);
                    if (q != null && q.Count > 0) { q.Clear(); any = true; }
                    if (Prop(s, "FallbackOrder") != null) { SetProp(s, "FallbackOrder", null); any = true; }
                }
                if (_commanded != null && _commanded.Remove(h.StringId)) any = true;
            }
            catch (Exception e) { Diag.Error("releasing a lord from Party AI Controls", e); }
            MobileParty p = h.PartyBelongedTo;
            if (any && p != null && p.LeaderHero == h && !Orders.Holds(p)) p.Ai.SetDoNotMakeNewDecisions(false);
            Ours.Remove(h.StringId);
            Standing.Remove(h.StringId);
            if (any) Diag.Event("pac", "released", new { lord = h.StringId });
            return any;
        }

        /// <summary>
        /// Party AI Controls' own refill switched off for every lord it has settings for, and in its defaults for new ones (0.12.1;
        /// run 20261025-1524-C2: after the battle with Naglur on day 14 it sent Aragorn to fill his ranks and held him there to the
        /// run's end, and the kingdom's agent could not command him. Mert: "Switch it off at setup, and make sure it is off for
        /// everyone else as well."). Only settings that exist are changed. How many lords had it on; defaults: it was on there.
        /// </summary>
        public static int AutoRecruitOff(out bool defaults)
        {
            defaults = false;
            if (!Resolve()) return 0;
            int n = 0;
            try
            {
                if (_table.GetValue(_mgr) is IDictionary d)
                    foreach (DictionaryEntry e in d)
                    {
                        if (e.Value == null || !(Prop(e.Value, "AutoRecruitment") is bool on) || !on) continue;
                        SetProp(e.Value, "AutoRecruitment", false);
                        n++;
                        Diag.Event("pac", "auto-recruit off", new { lord = (e.Key as Hero)?.StringId });
                    }
                foreach (FieldInfo f in new[] { _defaultsClan, _defaultsKingdom })
                {
                    object s = f?.GetValue(_mgr);
                    if (s == null || !(Prop(s, "AutoRecruitment") is bool on) || !on) continue;
                    SetProp(s, "AutoRecruitment", false);
                    defaults = true;
                    Diag.Event("pac", "auto-recruit off", new { defaults = f.Name });
                }
            }
            catch (Exception e) { Diag.Error("switching off Party AI Controls' auto-recruit", e); }
            return n;
        }

        // ---------- who gave it (the recorder, the runs) ----------

        /// <summary>Is this Party AI Controls order (PacReader's "Kind|target id") the lieutenant's?</summary>
        public static bool IsOurs(string heroId, string key) => heroId != null && key != null && Ours.TryGetValue(heroId, out string k) && k == key;

        /// <summary>Was this order, now gone, one of ours that we took off (asked once per change)?</summary>
        public static bool WasOurs(string heroId, string key) => heroId != null && key != null && JustCleared.Remove(heroId + "#" + key);

        // ---------- party_ai: the lieutenant sets Party AI Controls itself ----------

        /// <summary>
        /// The lieutenant's party_ai order for one lord (Orders.Issue): checked whole first, then carried out whole. Returns what
        /// was done (status applied) or why not.
        /// </summary>
        public static string Apply(Hero lord, MobileParty party, JObject raw, Kingdom realm, bool shadow, ref string status)
        {
            string name = Names.Hero(lord);
            if (raw == null) return "party_ai needs its settings";
            if (!Resolve()) return Status;
            var plan = new List<Action<object>>();
            var said = new List<string>();
            string error = null;

            // the order itself, carried out by Party AI Controls
            JToken order = raw["order"];
            bool endOurs = false;
            if (order != null)
            {
                if (IsNone(order))
                {
                    endOurs = true;
                    plan.Add(s => SetOrder(s, null));
                    said.Add("no order (his queue, then his backup order, then the game's own AI)");
                }
                else
                {
                    object o = OrderFrom(order, realm, party, out string w, out error);
                    if (o == null) return "order: " + error;
                    if (party.Army != null && party.Army.LeaderParty != party)
                        return name + " marches in " + Names.Party(party.Army.LeaderParty) + "'s army. Order the army's leader instead, or leave the order out.";
                    endOurs = true;
                    plan.Add(s => SetOrder(s, o));
                    said.Add("order: " + w + ", carried out by Party AI Controls");
                }
            }
            if (raw["queue"] != null)
            {
                if (!(raw["queue"] is JArray qa)) return "queue is a list of orders ([] empties it)";
                var items = new List<object>();
                var words = new List<string>();
                foreach (JToken t in qa)
                {
                    object o = OrderFrom(t, realm, party, out string w, out error);
                    if (o == null) return "queue: " + error;
                    items.Add(o);
                    words.Add(w);
                }
                plan.Add(s =>
                {
                    IList q = Queue(s);
                    if (q == null) { SetProp(s, "OrderQueue", Activator.CreateInstance(typeof(List<>).MakeGenericType(_tOrder))); q = Queue(s); }
                    q.Clear();
                    foreach (object o in items) q.Add(o);
                });
                said.Add(items.Count == 0 ? "queue emptied" : "queue: " + string.Join(", then ", words));
            }
            if (raw["fallback"] != null)
            {
                if (IsNone(raw["fallback"])) { plan.Add(s => SetProp(s, "FallbackOrder", null)); said.Add("no backup order"); }
                else
                {
                    object o = OrderFrom(raw["fallback"], realm, party, out string w, out error);
                    if (o == null) return "fallback: " + error;
                    plan.Add(s => SetProp(s, "FallbackOrder", o));
                    said.Add("backup order: " + w);
                }
            }
            if (raw["new_template"] != null)
            {
                if (!(raw["new_template"] is JObject nt)) return "new_template needs {\"name\": ..., \"troops\": [troop ids]}";
                string tn = ((string)nt["name"] ?? "").Trim();
                if (tn.Length == 0) return "new_template needs a name";
                if (Templates().Any(t => string.Equals((string)Prop(t, "Name"), tn, StringComparison.OrdinalIgnoreCase))) return "a template named " + tn + " already exists: use template";
                if (!(nt["troops"] is JArray ta) || ta.Count == 0) return "new_template needs troops: the troop ids it upgrades towards";
                var troops = new List<CharacterObject>();
                foreach (JToken t in ta)
                {
                    string tid = ((string)t ?? "").Trim().Trim('[', ']');
                    CharacterObject co = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<CharacterObject>(tid);
                    if (co == null || co.IsHero) return "new_template: there is no troop " + tid;
                    if (!troops.Contains(co)) troops.Add(co);
                }
                plan.Add(s =>
                {
                    TroopRoster roster = TroopRoster.CreateDummyTroopRoster();
                    foreach (CharacterObject co in troops) roster.AddToCounts(co, 1);
                    object made = Activator.CreateInstance(_tTemplate, new object[] { tn, roster });   // it joins Party AI Controls' list itself
                    SetTemplate(s, made);
                });
                said.Add("new template " + tn + " (" + string.Join(", ", troops.Select(c => c.Name.ToString())) + "), his from now");
            }
            else if (raw["template"] != null)
            {
                if (IsNone(raw["template"])) { plan.Add(s => SetTemplate(s, null)); said.Add("no template"); }
                else
                {
                    string tn = ((string)raw["template"] ?? "").Trim();
                    object t = Templates().FirstOrDefault(x => string.Equals((string)Prop(x, "Name"), tn, StringComparison.OrdinalIgnoreCase));
                    if (t == null) return "there is no template named " + tn + (Templates().Count > 0 ? "; there are " + string.Join(", ", Templates().Select(x => (string)Prop(x, "Name"))) : "; there are none yet (new_template makes one)");
                    plan.Add(s => SetTemplate(s, t));
                    said.Add("template " + (string)Prop(t, "Name"));
                }
            }
            if (raw["composition"] != null)
            {
                if (!PacRules.Composition(raw["composition"] as JObject, out float[] mix, out error)) return "composition: " + error;
                plan.Add(s => SetProp(s, "Composition", Activator.CreateInstance(_tComposition, new object[] { mix[0], mix[1], mix[2], mix[3] })));
                said.Add("troop mix " + string.Join("/", mix.Select(x => Math.Round(x * 100).ToString("0", Text.Inv))) + " (foot/archers/horse/horse archers)");
            }
            if (raw["options"] != null)
            {
                if (!(raw["options"] is JObject opts)) return "options is an object of settings";
                foreach (JProperty p in opts.Properties())
                {
                    if (!PacRules.Option(p.Name, p.Value, out string prop, out object value, out string words, out error)) return "options: " + error;
                    plan.Add(s => SetProp(s, prop, value));
                    said.Add(words);
                }
            }
            bool handBack = raw["managed"] != null && raw["managed"].Type == JTokenType.Boolean && !(bool)raw["managed"];
            if (handBack)
            {
                if (lord.Clan == Clan.PlayerClan) return name + " is of the ruler's own clan, whom Party AI Controls always manages: clear his order, queue and backup order instead";
                endOurs = true;
                said.Add("handed back to the game's own AI, out of Party AI Controls (order, queue and backup order cleared)");
            }
            if (said.Count == 0) return "party_ai needs at least one of order, queue, fallback, template, new_template, composition, options, managed";
            string text = name + " in Party AI Controls: " + string.Join("; ", said);
            if (shadow) { status = "checked"; return "valid: " + text + " (shadow mode: not carried out)"; }

            object settings = Ensure(lord, out string why);
            if (settings == null) return "Party AI Controls: " + why;
            // set on purpose from here: ending his order of ours below must not hand him back
            if (!handBack) Standing.Add(lord.StringId);
            if (endOurs && Orders.Holds(party))
            {
                Orders.EndFor(party, "replaced by a Party AI Controls order of the lieutenant's", "party ai order");
                text += " (his order from you before ends)";
            }
            try
            {
                if (handBack)
                {
                    SetOrder(settings, null);
                    Queue(settings)?.Clear();
                    SetProp(settings, "FallbackOrder", null);
                    if (_commanded != null && _commanded.Remove(lord.StringId)) Added.Remove(lord.StringId);
                    Standing.Remove(lord.StringId);
                    Ours.Remove(lord.StringId);
                    party.Ai.SetDoNotMakeNewDecisions(false);
                }
                else foreach (Action<object> step in plan) step(settings);
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Diag.Error("setting Party AI Controls for the lieutenant", inner);
                return "Party AI Controls did not take it (" + inner.Message + ")";
            }
            if (!handBack)
            {
                if (order != null) { string k = KeyOf(Prop(settings, "Order")); Ours[lord.StringId] = k; Remember(lord.StringId, k, true); }
                else Remember(lord.StringId, null, true);
            }
            else Remember(lord.StringId, "", false);
            Created.Remove(lord.StringId);   // set on purpose: it stays, saves included
            status = "applied";
            Diag.Event("pac", "set", new { lord = lord.StringId, raw = raw.ToString(Newtonsoft.Json.Formatting.None), hand_back = handBack });
            return text;
        }

        private static void SetTemplate(object s, object template)
        {
            if (_setTemplate != null) _setTemplate.Invoke(s, new[] { template });
            else SetProp(s, "PartyTemplate", template);
        }

        private static List<object> Templates()
        {
            try { return (_templates.GetValue(_mgr) as IEnumerable)?.Cast<object>().Where(t => t != null).ToList() ?? new List<object>(); }
            catch (Exception e) { Diag.Swallowed("reading Party AI Controls' templates", e); return new List<object>(); }
        }

        private static bool IsNone(JToken t) => t == null || t.Type == JTokenType.Null || (t.Type == JTokenType.String && string.Equals(((string)t).Trim(), "none", StringComparison.OrdinalIgnoreCase));

        /// <summary>A Party AI Controls order from the lieutenant's words: {"do": defend|patrol|patrol_lands|stay|besiege|follow|attack|recruit|visit, "at" or "target"}.</summary>
        private static object OrderFrom(JToken t, Kingdom realm, MobileParty self, out string words, out string error)
        {
            words = null;
            error = null;
            if (!(t is JObject o)) { error = "an order is {\"do\": ..., \"at\" or \"target\": ...}"; return null; }
            string verb = ((string)(o["do"] ?? o["order"]) ?? "").Trim().ToLowerInvariant();
            PacRules.KindDef def = PacRules.Kinds.FirstOrDefault(k => k.Word == verb);
            if (def == null) { error = "'" + verb + "' is not a Party AI Controls order; they are " + string.Join(", ", PacRules.Kinds.Select(k => k.Word)); return null; }
            IMapPoint point = null;
            string place = ((string)(o["at"] ?? o["place"]) ?? (string)o["target"])?.Trim().Trim('[', ']');
            if (def.Needs == "holding" || def.Needs == "own_holding" || def.Needs == "friendly_fort" || def.Needs == "enemy_fort")
            {
                if (string.IsNullOrEmpty(place)) { error = verb + " needs \"at\": a holding"; return null; }
                Settlement s = Orders.SettlementFor(place, out error);
                if (s == null) return null;
                bool own = s.MapFaction == realm, hostile = s.MapFaction != null && FactionManager.IsAtWarAgainstFaction(s.MapFaction, realm);
                bool allied = !own && s.MapFaction is Kingdom k && realm.AlliedKingdoms.Contains(k);
                if (def.Needs == "own_holding" && !own) { error = Names.Settlement(s) + " is not one of " + Names.Faction(realm) + "'s holdings"; return null; }
                if (def.Needs == "friendly_fort" && !((own || allied) && s.IsFortification)) { error = Names.Settlement(s) + " is not a castle or town of the realm or an ally"; return null; }
                if (def.Needs == "enemy_fort" && !(hostile && s.IsFortification)) { error = Names.Settlement(s) + " is not an enemy castle or town"; return null; }
                point = s;
                words = verb.Replace('_', ' ') + " " + Names.Settlement(s);
            }
            else if (def.Needs == "own_party" || def.Needs == "enemy_party")
            {
                if (string.IsNullOrEmpty(place)) { error = verb + " needs \"target\": a lord"; return null; }
                MobileParty p = Orders.PartyFor(place, out error);
                if (p == null) return null;
                if (p == self) { error = "a lord cannot " + verb + " himself"; return null; }
                if (def.Needs == "enemy_party" && !FactionManager.IsAtWarAgainstFaction(p.MapFaction, realm)) { error = Names.Party(p) + " is not at war with " + Names.Faction(realm); return null; }
                if (def.Needs == "own_party" && p.MapFaction != realm) { error = Names.Party(p) + " is not of " + Names.Faction(realm); return null; }
                point = p;
                words = verb + " " + Names.Party(p);
            }
            else words = verb == "patrol_lands" ? "patrol his own lands" : "recruit (his template, or the troop mix)";
            try { return NewOrder(def.Kind, point); }
            catch (Exception e) { error = "Party AI Controls would not make it (" + e.Message + ")"; return null; }
        }

        private static void Remember(string lord, string key, bool standing)
        {
            string file = _file;
            if (file == null) return;
            string line = Json.Line(new { Load = Bridge.History?.Label, H = CampaignTime.Now.ToHours, W = DateTime.Now.ToString("o", Text.Inv), Lord = lord, Key = key, Standing = standing });
            Background.Post("party ai record", () => Files.Append(file, line));
        }

        // ---------- what the lieutenant reads (with each of the ruler's messages) ----------

        /// <summary>The realm's lords in Party AI Controls now, its templates and the troops a new one can be made of. Game's thread, on each message.</summary>
        public static string Overview(Kingdom realm)
        {
            if (realm == null) return null;
            if (!Resolve()) return "Party AI Controls: " + Status + ".";
            var sb = new StringBuilder();
            sb.AppendLine("It manages the ruler's own clan's lords, and the vassals who hold an order from the ruler or from you; the rest follow the game's own AI.");
            var managed = new List<string>();
            var free = new List<string>();
            foreach (MobileParty p in MobileParty.AllLordParties.Where(x => x != null && x.IsActive && x.LeaderHero != null && x.MapFaction == realm && x.LeaderHero != Hero.MainHero)
                                                                .OrderBy(x => x.LeaderHero.Clan == Clan.PlayerClan ? 0 : 1).ThenBy(x => Names.Hero(x.LeaderHero)))
            {
                Hero h = p.LeaderHero;
                object s = Existing(h);
                bool managedNow = Manageable(h);
                if (!managedNow) { free.Add(Names.Hero(h) + " [" + h.StringId + "]"); continue; }
                managed.Add(LordLine(h, p, s, (h.Clan == Clan.PlayerClan ? _defaultsClan : _defaultsKingdom)?.GetValue(_mgr)));
            }
            sb.AppendLine("Managed now (" + managed.Count + "):");
            foreach (string l in managed) sb.AppendLine(l);
            if (managed.Count == 0) sb.AppendLine("- none");
            sb.AppendLine("Not managed now (" + free.Count + "; any party_ai or order of yours for one makes him managed): " + (free.Count > 0 ? string.Join(", ", free) : "none") + ".");
            List<object> templates = Templates();
            sb.AppendLine("Templates (" + templates.Count + "): " + (templates.Count == 0 ? "none yet." : ""));
            foreach (object t in templates)
            {
                List<string> names = TemplateTroops(t);
                sb.AppendLine("- " + (string)Prop(t, "Name") + ": " + (names.Count > 0 ? string.Join(", ", names) : "(no troops)"));
            }
            sb.AppendLine("Troops a new template can aim at (the top of each line of your realm's peoples): " + string.Join(", ", TopTroops(realm)) + ".");
            return sb.ToString().TrimEnd();
        }

        private static string LordLine(Hero h, MobileParty p, object s, object defaults)
        {
            var parts = new List<string>();
            object order = s != null ? Prop(s, "Order") : null;
            string id = h.StringId;
            bool ours = Driving.ContainsKey(p) || (Active(order) && IsOurs(id, KeyOf(order)));
            parts.Add("order: " + (Active(order) ? WordsOf(order) + (ours ? " (yours)" : " (the ruler's, or its own)") : Driving.ContainsKey(p) ? "none shown (yours runs: " + (Orders.ToolOf(p) ?? "an order") + ")" : "none"));
            IList q = s != null ? Queue(s) : null;
            if (q != null && q.Count > 0) parts.Add("queue: " + string.Join(", then ", q.Cast<object>().Select(WordsOf)));
            object fb = s != null ? Prop(s, "FallbackOrder") : null;
            if (Active(fb)) parts.Add("backup: " + WordsOf(fb));
            object tpl = s != null ? Prop(s, "PartyTemplate") : null;
            if (tpl != null) parts.Add("template: " + (string)Prop(tpl, "Name"));
            object comp = Prop(s ?? defaults, "Composition");
            if (comp != null)
                parts.Add("troop mix " + string.Join("/", new[] { "Infantry", "Ranged", "Cavalry", "HorseArcher" }.Select(n => Prop(comp, n) is float f ? Math.Round(f * 100).ToString("0", Text.Inv) : "?")));
            var opts = new List<string>();
            if (s != null)
                foreach (PacRules.OptionDef o in PacRules.Options)
                {
                    object v = Prop(s, o.Prop), dv = Prop(defaults, o.Prop);
                    if (v != null && !Equals(v, dv)) opts.Add(o.Word + " " + PacRules.Show(o, v));
                }
            if (opts.Count > 0) parts.Add("options unlike the defaults: " + string.Join(", ", opts));
            if (s == null) parts.Add("no entry yet (the defaults)");
            int men = p.Party.NumberOfHealthyMembers, limit = p.Party.PartySizeLimit;
            return "- " + Names.Hero(h) + " [" + id + "]" + (h.Clan == Clan.PlayerClan ? " (your clan)" : "") + ", " + Text.N(men) + "/" + Text.N(limit) + " men: " + string.Join("; ", parts);
        }

        private static List<string> TemplateTroops(object t)
        {
            var list = new List<string>();
            try
            {
                if (Prop(t, "UpgradeTargets") is TroopRoster r)
                    foreach (TroopRosterElement e in r.GetTroopRoster()) if (e.Character != null) list.Add(e.Character.Name + " [" + e.Character.StringId + "]");
            }
            catch (Exception e) { Diag.Swallowed("reading a template's troops", e); }
            return list;
        }

        private static List<string> TopTroops(Kingdom realm)
        {
            var cultures = new HashSet<CultureObject>(realm.Clans.Where(c => c != null && c.Culture != null).Select(c => c.Culture));
            if (realm.Culture != null) cultures.Add(realm.Culture);
            return CharacterObject.All.Where(c => c != null && !c.IsHero && c.Culture != null && cultures.Contains(c.Culture) && c.Occupation == Occupation.Soldier
                                                 && (c.UpgradeTargets == null || c.UpgradeTargets.Length == 0) && c.Tier >= 3)
                                      .OrderBy(c => c.Culture.StringId).ThenByDescending(c => c.Tier).Take(80)
                                      .Select(c => c.Name + " [" + c.StringId + "] (tier " + c.Tier + ", " + Kind(c) + ")").ToList();
        }

        private static string Kind(CharacterObject c)
        {
            switch (c.DefaultFormationClass)
            {
                case FormationClass.Ranged: return "archer";
                case FormationClass.Cavalry: return "horse";
                case FormationClass.HorseArcher: return "horse archer";
                default: return "foot";
            }
        }
    }

    /// <summary>Party AI Controls' kinds and options in the lieutenant's words (game-free: the smoke test reads them).</summary>
    internal static class PacRules
    {
        internal sealed class KindDef
        {
            public string Word, Kind, Needs, About;
        }

        internal sealed class OptionDef
        {
            public string Word, Prop, Type, About;
        }

        /// <summary>Party AI Controls' own orders, carried out by it (party_ai's order, queue and fallback).</summary>
        public static readonly KindDef[] Kinds =
        {
            new KindDef { Word = "defend", Kind = "DefendSettlement", Needs = "own_holding", About = "defends one of the realm's holdings (at)" },
            new KindDef { Word = "patrol", Kind = "PatrolAroundPoint", Needs = "holding", About = "patrols around a holding (at); the patrol_radius option sets how far" },
            new KindDef { Word = "patrol_lands", Kind = "PatrolClanLands", Needs = null, About = "patrols his own clan's lands" },
            new KindDef { Word = "stay", Kind = "StayInSettlement", Needs = "friendly_fort", About = "goes into a castle or town of the realm or an ally (at) and stays there" },
            new KindDef { Word = "besiege", Kind = "BesiegeSettlement", Needs = "enemy_fort", About = "besieges an enemy castle or town (at)" },
            new KindDef { Word = "follow", Kind = "EscortParty", Needs = "own_party", About = "follows another lord of the realm (target)" },
            new KindDef { Word = "attack", Kind = "AttackParty", Needs = "enemy_party", About = "attacks an enemy lord's party (target)" },
            new KindDef { Word = "recruit", Kind = "RecruitFromTemplate", Needs = null, About = "goes recruiting troops of his template (or his troop mix) until full" },
            new KindDef { Word = "visit", Kind = "VisitSettlement", Needs = "holding", About = "goes to a holding (at)" },
        };

        /// <summary>The per-lord options of Party AI Controls' panel.</summary>
        public static readonly OptionDef[] Options =
        {
            new OptionDef { Word = "join_armies", Prop = "AllowAllowJoinArmies", Type = "bool", About = "may join the kingdom's armies" },
            new OptionDef { Word = "donate_troops", Prop = "AllowDonateTroops", Type = "bool", About = "may give troops to friendly garrisons" },
            new OptionDef { Word = "take_troops_from_holdings", Prop = "AllowTakeTroopsFromSettlement", Type = "bool", About = "may take troops from friendly garrisons" },
            new OptionDef { Word = "besiege", Prop = "AllowSieging", Type = "bool", About = "may besiege towns and castles (off: he also leaves armies that besiege)" },
            new OptionDef { Word = "raid_villages", Prop = "AllowRaidVillages", Type = "bool", About = "may raid enemy villages" },
            new OptionDef { Word = "take_lord_prisoners", Prop = "AllowLordPrisoners", Type = "bool", About = "may take enemy lords prisoner after a battle" },
            new OptionDef { Word = "recruit", Prop = "AllowRecruitment", Type = "bool", About = "may recruit troops at all" },
            new OptionDef { Word = "recruit_from_enemy_holdings", Prop = "RecruitFromEnemySettlements", Type = "bool", About = "may recruit in enemy villages and towns" },
            new OptionDef { Word = "buy_horses", Prop = "BuyHorses", Type = "bool", About = "buys horses for his men on foot, to march faster" },
            new OptionDef { Word = "horse_budget", Prop = "BuyHorsesBudget", Type = "int", About = "denars a day he may spend on horses" },
            new OptionDef { Word = "auto_recruit", Prop = "AutoRecruitment", Type = "bool", About = "goes recruiting by himself when below auto_recruit_below" },
            new OptionDef { Word = "auto_recruit_below", Prop = "AutoRecruitmentPercentage", Type = "percent", About = "% of his party's limit" },
            new OptionDef { Word = "dismiss_unwanted", Prop = "DismissUnwantedTroops", Type = "bool", About = "dismisses troops that fit neither his template nor his troop mix when above dismiss_above" },
            new OptionDef { Word = "dismiss_above", Prop = "DismissUnwantedTroopsPercentage", Type = "percent", About = "% of his party's limit" },
            new OptionDef { Word = "max_troop_tier", Prop = "MaxTroopTier", Type = "int", About = "the highest tier his troops are upgraded to (0: no limit); lowering it downgrades troops above it already in his party" },
            new OptionDef { Word = "patrol_radius", Prop = "PatrolRadius", Type = "percent", About = "how far Party AI Controls' patrol orders range, % of its usual (100)" },
        };

        /// <summary>
        /// What Party AI Controls' panel shows for one of our tools: the nearest of its own kinds (null: it has none, and shows no
        /// order). A watch, rest or garrison at a holding shows; a watch post on open ground does not.
        /// </summary>
        public static string MirrorOf(string tool, bool atIsHolding)
        {
            switch ((tool ?? "").ToLowerInvariant())
            {
                case "besiege": return "BesiegeSettlement";
                case "defend":
                case "relieve": return "DefendSettlement";
                case "garrison":
                case "rest": return atIsHolding ? "StayInSettlement" : null;
                case "patrol":
                case "screen":
                case "watch": return atIsHolding ? "PatrolAroundPoint" : null;
                case "goto":
                case "sell":
                case "supply": return atIsHolding ? "VisitSettlement" : null;
                case "recruit": return "RecruitFromTemplate";
                case "engage":
                case "hunt": return "AttackParty";
                case "follow": return "EscortParty";
                default: return null;   // raid, shadow, hold, leave, gather, join: Party AI Controls has no such order
            }
        }

        public static bool UsesParty(string kind) => kind == "EscortParty" || kind == "AttackParty";

        /// <summary>One option from the lieutenant: its property and value in Party AI Controls' terms, and the words for the receipt.</summary>
        public static bool Option(string word, JToken value, out string prop, out object v, out string words, out string error)
        {
            prop = null;
            v = null;
            words = null;
            error = null;
            OptionDef o = Options.FirstOrDefault(x => string.Equals(x.Word, (word ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (o == null) { error = "'" + word + "' is not an option; they are " + string.Join(", ", Options.Select(x => x.Word)); return false; }
            prop = o.Prop;
            switch (o.Type)
            {
                case "bool":
                    if (value == null || value.Type != JTokenType.Boolean) { error = o.Word + " takes true or false"; return false; }
                    v = (bool)value;
                    break;
                case "int":
                    if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) { error = o.Word + " takes a number"; return false; }
                    double d = (double)value;
                    double max = o.Word == "max_troop_tier" ? 10 : 100000;
                    if (d < 0 || d > max) { error = o.Word + " takes 0 to " + max; return false; }
                    v = (int)Math.Round(d);
                    break;
                default:
                    if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) { error = o.Word + " takes a percentage, e.g. 50"; return false; }
                    double pc = (double)value;
                    double top = o.Word == "patrol_radius" ? 500 : 100;
                    if (pc < (o.Word == "patrol_radius" ? 10 : 0) || pc > top) { error = o.Word + " takes " + (o.Word == "patrol_radius" ? "10" : "0") + " to " + top + " (%)"; return false; }
                    v = (float)(pc / 100.0);
                    break;
            }
            words = o.Word + " " + Show(o, v);
            return true;
        }

        public static string Show(OptionDef o, object v)
        {
            if (v is bool b) return b ? "yes" : "no";
            if (o.Type == "percent" && v is float f) return Math.Round(f * 100).ToString("0", Text.Inv) + "%";
            return Convert.ToString(v, Text.Inv);
        }

        /// <summary>A troop mix in percentages (infantry, ranged, cavalry, horse_archers), as Party AI Controls' fractions summing to 1.</summary>
        public static bool Composition(JObject o, out float[] mix, out string error)
        {
            mix = null;
            error = null;
            if (o == null) { error = "it takes {\"infantry\": %, \"ranged\": %, \"cavalry\": %, \"horse_archers\": %}"; return false; }
            string[] keys = { "infantry", "ranged", "cavalry", "horse_archers" };
            var vals = new double[4];
            foreach (JProperty p in o.Properties())
            {
                int i = Array.FindIndex(keys, k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase) || (k == "horse_archers" && string.Equals(p.Name, "horse_archer", StringComparison.OrdinalIgnoreCase)));
                if (i < 0) { error = "'" + p.Name + "' is not a part of the mix; they are " + string.Join(", ", keys); return false; }
                if (p.Value.Type != JTokenType.Integer && p.Value.Type != JTokenType.Float) { error = p.Name + " takes a percentage"; return false; }
                vals[i] = (double)p.Value;
                if (vals[i] < 0) { error = p.Name + " cannot be below 0"; return false; }
            }
            double sum = vals.Sum();
            if (sum <= 0) { error = "the parts add up to nothing"; return false; }
            mix = vals.Select(x => (float)(x / sum)).ToArray();
            return true;
        }

        /// <summary>The lieutenant's library section (its tools.txt only).</summary>
        public static string LibraryText()
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Party AI Controls (your ruler's own command panel; only you have these):");
            sb.AppendLine("- Every order you give one of the realm's lords shows in Party AI Controls as his order: defend, patrol, stay, besiege, follow, attack, recruit or visit, whichever is nearest (a raid, a shadow, a watch post on open ground and armies have no such order there, so it shows none). It replaces the order he held there; his queue and backup order stay, and when your order ends the next order of his queue starts there, or his backup order once he is free. If the ruler gives him another order there meanwhile, the ruler's wins and yours ends.");
            sb.AppendLine("- **party_ai** (one lord; any of these, all checked before any is set):");
            sb.AppendLine("  - order: a Party AI Controls order that Party AI Controls itself carries out, replacing whatever he does now (yours included), or \"none\";");
            sb.AppendLine("  - queue: a list of such orders, each started when the one before ends ([] empties it);");
            sb.AppendLine("  - fallback: his backup order, started whenever he is free and out of an army, or \"none\";");
            sb.AppendLine("  - template: the name of a troop template, which he recruits and upgrades towards, or \"none\"; new_template {\"name\", \"troops\": [troop ids]} makes one and gives it to him;");
            sb.AppendLine("  - composition: his troop mix in %, {\"infantry\", \"ranged\", \"cavalry\", \"horse_archers\"}, which he recruits towards when he has no template;");
            sb.AppendLine("  - options: {name: value}, any of: " + string.Join("; ", Options.Select(o => o.Word + " (" + o.About + (o.Type == "bool" ? ", true or false" : o.Type == "percent" ? ", %" : ", a number") + ")")) + ";");
            sb.AppendLine("  - managed: false hands a vassal back to the game's own AI, out of Party AI Controls (his order, queue and backup order cleared).");
            sb.AppendLine("  Its orders: " + string.Join("; ", Kinds.Select(k => k.Word + " (" + k.About + ")")) + ".");
            sb.AppendLine("  Example: {\"lord\": \"<id>\", \"do\": \"party_ai\", \"order\": {\"do\": \"patrol_lands\"}, \"fallback\": {\"do\": \"defend\", \"at\": \"<holding id>\"}, \"options\": {\"raid_villages\": false}, \"why\": \"...\"}");
            sb.AppendLine("- PARTY AI CONTROLS NOW, in each message, shows every managed lord's order, queue, backup order, template, troop mix and options unlike the defaults, the templates, and the troops a new template can aim at.");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Files of lines labelled with the game session that wrote them and its in-game hour (0.11.1): the lieutenant's conversation
    /// and its Party AI Controls record. A load keeps the lines of this save's history (History.HoldsLine), so each save has the
    /// conversation it lived through. Read at load (game's thread, a loading screen).
    /// </summary>
    internal static class LabelledLines
    {
        public static List<JObject> Read(string file, History history)
        {
            var list = new List<JObject>();
            try
            {
                if (file == null || !File.Exists(file)) return list;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0) continue;
                        JObject o;
                        try { o = JObject.Parse(line); }
                        catch (Newtonsoft.Json.JsonException) { continue; }   // a line half-written when the game closed
                        double h = (double?)o["h"] ?? double.NaN;
                        DateTime? w = DateTime.TryParse((string)o["w"], Text.Inv, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime wt) ? wt : (DateTime?)null;
                        if (history.HoldsLine((string)o["load"], h, w)) list.Add(o);
                    }
                }
            }
            catch (Exception e) { Diag.Swallowed("reading " + Path.GetFileName(file), e); }
            return list;
        }
    }
}
