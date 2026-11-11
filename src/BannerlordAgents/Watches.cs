using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BannerlordAgents.Tools;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>One condition of a watch: an expression of the behavior language, and the words it wakes the agent with.</summary>
    internal sealed class WatchCondition
    {
        public string IfText, Say;
        public Expr If;
        /// <summary>It wakes only when it turns true: false once since the watch was given (armed), and not true the hour before.</summary>
        public bool Armed, WasTrue;
        public int Fired;
    }

    /// <summary>
    /// One of the agent's own watches (0.18, continuous agents, docs/WARROOM.md#continuous-agents-since-018): a part of the map or a lord it follows,
    /// with the conditions that wake it, how often it checks in, and until when. The conditions are read every in-game hour on the realm's own view,
    /// as a tool's rules are, at no call to the agent.
    /// </summary>
    internal sealed class Watch
    {
        public string Name, Why, Lord, Party, LordName;
        /// <summary>The names its conditions read, each with the id as written and the id it reads (a lord's party).</summary>
        public readonly List<KeyValuePair<string, string>> Given = new List<KeyValuePair<string, string>>(), Names = new List<KeyValuePair<string, string>>();
        public readonly List<WatchCondition> When = new List<WatchCondition>();
        /// <summary>The lords whose orders' news wakes the agent (hero ids as written), and their parties.</summary>
        public readonly List<string> Follow = new List<string>(), FollowParties = new List<string>();
        public string EndIfText;
        public Expr EndIf;
        public double CheckEvery, Since = double.NaN, Until = double.NaN, LastCheck = double.NaN;
        public int Poke = -1, Version = 1;
    }

    /// <summary>
    /// The agent's watches (0.18): an answer's "watch" list keeps, changes or ends them; every in-game hour each one's conditions are read on the realm's
    /// newest hour, and a condition that turns true, a check-in come due, or a followed lord's order news wakes the agent (WakeRules, self-wakes). A watch
    /// ends at its hour or when its end condition holds, and the next wake says so. They are the realm agent's own, kept beside the save like the orders in
    /// force (CarryOn). Worker thread.
    /// </summary>
    internal static class Watches
    {
        public const int MaxWatches = 12, MaxConditions = 8, MaxExpression = 600, MaxNames = 12;
        public const double MaxHours = 240, DefaultHours = 72, MinCheckEvery = 2;
        private static readonly Regex NameRule = new Regex("^[a-z][a-z0-9_]{1,31}$", RegexOptions.Compiled);

        /// <summary>The names a watch's conditions can read, beside the lord's own (ToolDef.BuiltIns) and its names.</summary>
        private static readonly HashSet<string> Extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "me", "now", "hour_of_day", "day" };

        // ---------- taking a watch from an answer ----------

        /// <summary>What became of one watch of an answer: its name, kept, changed, ended or refused, and why.</summary>
        internal sealed class Taken
        {
            public string Name, Status, Reason;
        }

        /// <summary>
        /// One watch from an answer: checked against the realm's newest hour (its ids known to the realm, its conditions in the language), then kept (or a watch of the
        /// same name changed), or ended with {"name": ..., "end": true}. s: the realm's newest hour; seen: what the realm remembers of parties out of sight.
        /// </summary>
        public static Taken Take(List<Watch> list, JObject o, int poke, Snapshot s, Dictionary<string, ViewParty> seen, string realm)
        {
            var r = new Taken { Name = ((string)o?["name"])?.Trim().ToLowerInvariant() };
            if (o == null) return Refuse(r, "write each watch as an object, for example {\"name\": \"north_road\", \"wake_if\": [...]}");
            if (string.IsNullOrEmpty(r.Name) || !NameRule.IsMatch(r.Name)) return Refuse(r, "its name must be 2 to 32 small letters, digits and underscores, starting with a letter");
            Watch old = list.FirstOrDefault(w => w.Name == r.Name);
            if ((bool?)o["end"] == true || (bool?)o["retire"] == true)
            {
                if (old == null) return Refuse(r, "you have no watch of that name");
                list.Remove(old);
                r.Status = "ended";
                r.Reason = "ended, as you asked";
                return r;
            }
            if (old == null && list.Count >= MaxWatches) return Refuse(r, "you keep " + MaxWatches + " watches at most; end one first (\"end\": true)");
            if (s == null) return Refuse(r, "no hour of your realm has been copied yet, so its names cannot be checked");
            var w = new Watch { Name = r.Name, Why = ((string)o["why"])?.Trim(), Poke = poke, Since = s.Hours, Version = old != null ? old.Version + 1 : 1 };
            var problems = new List<string>();
            var world = Runner.WorldFor(s);

            // the lord its conditions read as "me"
            string lord = Str(o["lord"]);
            if (lord != null)
            {
                PartyRow p = OwnParty(s, realm, lord);
                if (p == null) problems.Add("lord: " + lord + " is not a lord of yours leading a party");
                else { w.Lord = lord; w.Party = p.Id; w.LordName = world.Name(p.Id); }
            }

            // its names: each an id the realm knows, as the language reads it (a lord's party, a holding, a spot)
            if (o["names"] is JObject names)
            {
                if (names.Count > MaxNames) problems.Add("names: at most " + MaxNames);
                foreach (JProperty p in names.Properties())
                {
                    string n = p.Name.Trim().ToLowerInvariant(), id = Str(p.Value);
                    if (!NameRule.IsMatch(n)) { problems.Add("names: '" + p.Name + "' must be 2 to 32 small letters, digits and underscores"); continue; }
                    if (ToolDef.BuiltIns.Contains(n) || ToolDef.Functions.ContainsKey(n) || Extra.Contains(n) || n == "lords" || n == "places") { problems.Add("names: '" + n + "' is a built-in name or a function; call it something else"); continue; }
                    string reads = Resolve(s, seen, id);
                    if (reads == null) { problems.Add("names: " + n + " = " + (id ?? "nothing") + " is no lord, army, holding or spot your realm knows"); continue; }
                    w.Given.Add(new KeyValuePair<string, string>(n, id));
                    w.Names.Add(new KeyValuePair<string, string>(n, reads));
                }
            }
            else if (o["names"] != null) problems.Add("names: write them as an object, {\"name\": \"id\"}");

            // the lords it follows
            foreach (string f in List(o["follow"]))
            {
                PartyRow p = OwnParty(s, realm, f);
                if (p == null) { problems.Add("follow: " + f + " is not a lord of yours leading a party"); continue; }
                w.Follow.Add(f);
                w.FollowParties.Add(p.Id);
            }

            // the conditions
            JArray when = o["wake_if"] as JArray ?? o["when"] as JArray;
            if (when != null)
            {
                if (when.Count > MaxConditions) problems.Add("wake_if: at most " + MaxConditions + " conditions");
                int i = 0;
                foreach (JToken t in when.Take(MaxConditions))
                {
                    i++;
                    string text = t is JObject c ? (string)c["if"] : t.Type == JTokenType.String ? (string)t : null, say = t is JObject c2 ? ((string)c2["say"])?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(text)) { problems.Add("wake_if " + i + ": needs \"if\""); continue; }
                    if (text.Length > MaxExpression) { problems.Add("wake_if " + i + ": at most " + MaxExpression + " characters"); continue; }
                    Expr e = Check(text, w, "wake_if " + i, problems);
                    if (e != null) w.When.Add(new WatchCondition { IfText = text.Trim(), If = e, Say = string.IsNullOrEmpty(say) ? text.Trim() : say });
                }
            }
            else if (o["wake_if"] != null) problems.Add("wake_if: write it as a list of {\"if\": ..., \"say\": ...}");
            string endIf = (string)o["end_if"];
            if (!string.IsNullOrWhiteSpace(endIf))
            {
                if (endIf.Length > MaxExpression) problems.Add("end_if: at most " + MaxExpression + " characters");
                else { w.EndIf = Check(endIf, w, "end_if", problems); w.EndIfText = endIf.Trim(); }
            }

            // its clock
            double every = Num(o["check_every"]), hours = Num(o["until"]);
            if (!double.IsNaN(every) && every > 0) w.CheckEvery = Math.Max(MinCheckEvery, Math.Min(MaxHours, every));
            w.Until = s.Hours + (double.IsNaN(hours) || hours <= 0 ? DefaultHours : Math.Min(MaxHours, hours));
            if (w.When.Count == 0 && w.Follow.Count == 0 && w.CheckEvery <= 0 && problems.Count == 0) problems.Add("it would never wake you: give it wake_if, follow or check_every");
            if (problems.Count > 0) return Refuse(r, string.Join("; ", problems));

            // each condition read once now: one already true waits until it has been false, so it does not wake you the hour you gave it
            IScope scope = Runner.WatchScope(world, w.Party, w.Names);
            var already = new List<string>();
            foreach (WatchCondition c in w.When)
            {
                bool now = Holds(c.If, scope);
                c.WasTrue = now;
                c.Armed = !now;
                if (now) already.Add("\"" + c.Say + "\"");
            }
            if (old != null) list.Remove(old);
            list.Add(w);
            w.LastCheck = s.Hours;
            r.Status = old != null ? "changed" : "kept";
            r.Reason = Describe(w, world, false) + (already.Count > 0 ? " Already true now, so each wakes you only once it has turned false and true again: " + string.Join(", ", already) + "." : "");
            return r;
        }

        private static Taken Refuse(Taken r, string why) { r.Status = "refused"; r.Reason = why; return r; }

        /// <summary>An expression checked as a tool's rule is: its names the lord's built-ins, the watch's names or none, its functions with their counts of values.</summary>
        private static Expr Check(string text, Watch w, string where, List<string> problems)
        {
            Expr e;
            try { e = Expr.Parse(text); }
            catch (FormatException x) { problems.Add(where + ": " + x.Message); return null; }
            int before = problems.Count;
            e.Walk(n =>
            {
                bool named = w.Names.Any(kv => string.Equals(kv.Key, n, StringComparison.OrdinalIgnoreCase));
                if (named || Extra.Contains(n)) return;
                if (!ToolDef.BuiltIns.Contains(n) || n == "at" || n == "target" || n == "caution" || n == "danger_radius")
                    problems.Add(where + ": '" + n + "' is not one of its names or a built-in name");
                else if (w.Party == null) problems.Add(where + ": '" + n + "' reads a lord's own value, so name the lord in \"lord\"");
            }, (f, count) =>
            {
                if (!ToolDef.Functions.TryGetValue(f, out int want)) problems.Add(where + ": there is no function " + f);
                else if (want != count) problems.Add(where + ": " + f + " takes " + want + " value" + (want == 1 ? "" : "s") + ", given " + count);
            });
            return problems.Count == before ? e : null;
        }

        /// <summary>A lord of the realm (hero id, or a party id) leading a party now.</summary>
        private static PartyRow OwnParty(Snapshot s, string realm, string id)
        {
            if (id == null) return null;
            if (s.PartyById.TryGetValue(id, out PartyRow p) && p.Kind == "lord" && p.Faction == realm) return p;
            return s.Parties.FirstOrDefault(x => x.Kind == "lord" && x.Leader == id && x.Faction == realm);
        }

        /// <summary>The id an expression reads for an id as written: a holding or spot as it is, a lord's party (in view or remembered), else null.</summary>
        private static string Resolve(Snapshot s, Dictionary<string, ViewParty> seen, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (Spots.TryParse(id, out _, out _) || s.SettlementById.ContainsKey(id)) return id;
            if (s.PartyById.ContainsKey(id)) return id;
            PartyRow p = s.Parties.FirstOrDefault(x => x.Leader == id && !x.Attached) ?? s.Parties.FirstOrDefault(x => x.Leader == id);
            if (p != null) return p.Id;
            if (seen != null)
            {
                if (seen.ContainsKey(id)) return id;
                foreach (KeyValuePair<string, ViewParty> kv in seen) if (kv.Value?.Lord == id) return kv.Key;
            }
            return null;
        }

        // ---------- every hour ----------

        /// <summary>
        /// The realm's watches read on its newest hour (v): each condition that turns true, each check-in come due, each watch ended (its hour, its end condition)
        /// as news lines. A check-in or a wake for any reason resets every watch's check-in clock (Woke).
        /// </summary>
        public static List<Trigger> Hour(List<Watch> list, Snapshot v)
        {
            var found = new List<Trigger>();
            if (list.Count == 0 || v == null) return found;
            World world = Runner.WorldFor(v);
            foreach (Watch w in list.ToList())
            {
                IScope scope;
                try { scope = Runner.WatchScope(world, w.Party, w.Names); }
                catch (Exception e) { Diag.Error("reading a watch", e); continue; }
                string place = w.Names.Select(kv => kv.Value).FirstOrDefault(id => v.SettlementById.ContainsKey(id));
                string party = w.Party ?? w.Names.Select(kv => kv.Value).FirstOrDefault(id => v.PartyById.ContainsKey(id));
                if (v.Hours >= w.Until - 1e-6)
                {
                    list.Remove(w);
                    found.Add(new Trigger { Hours = v.Hours, Kind = "watch_ended", Text = "Your watch \"" + w.Name + "\" ended: its time ran out (" + Text.When(w.Until) + ").", Party = party, Place = place });
                    continue;
                }
                if (w.EndIf != null && Holds(w.EndIf, scope))
                {
                    list.Remove(w);
                    found.Add(new Trigger { Hours = v.Hours, Kind = "watch_ended", Text = "Your watch \"" + w.Name + "\" ended: its end condition holds (" + w.EndIfText + ").", Party = party, Place = place });
                    continue;
                }
                var fired = new List<string>();
                foreach (WatchCondition c in w.When)
                {
                    bool now = Holds(c.If, scope);
                    if (now && c.Armed && !c.WasTrue)
                    {
                        c.Fired++;
                        string said;
                        try { said = Runner.WatchSay(c.Say, scope, world); }
                        catch (Exception e) { said = c.Say; Diag.Swallowed("filling a watch's words", e); }
                        fired.Add(said);
                    }
                    if (!now) c.Armed = true;
                    c.WasTrue = now;
                }
                if (fired.Count > 0)
                {
                    found.Add(new Trigger { Hours = v.Hours, Kind = "watch", Text = "Your watch \"" + w.Name + "\": " + string.Join("; ", fired.Select(f => f.TrimEnd('.'))) + ".", Party = party, Place = place });
                    Diag.Count("watch:fired");
                    continue;
                }
                if (w.CheckEvery > 0 && !double.IsNaN(w.LastCheck) && v.Hours - w.LastCheck >= w.CheckEvery - 1e-6)
                {
                    found.Add(new Trigger
                    {
                        Hours = v.Hours, Kind = "check_in", Party = party, Place = place,
                        Text = "Check-in on your watch \"" + w.Name + "\" (every " + ToolDef.Num(w.CheckEvery) + " hours" + (w.Why != null ? "; " + w.Why.TrimEnd('.') : "") + "): "
                               + (w.When.Count == 0 ? "it has no conditions; this is the look you asked for." : "none of its conditions has turned true.")
                    });
                    w.LastCheck = v.Hours;
                    Diag.Count("watch:check-in");
                }
            }
            return found;
        }

        /// <summary>The agent was woken (any wake): every watch's check-in clock starts again, since it has just seen the realm.</summary>
        public static void Woke(List<Watch> list, double hours)
        {
            foreach (Watch w in list) w.LastCheck = hours;
        }

        /// <summary>The parties whose orders' news wakes the agent (each watch's follow list).</summary>
        public static HashSet<string> Followed(List<Watch> list) => new HashSet<string>(list.SelectMany(w => w.FollowParties), StringComparer.Ordinal);

        private static bool Holds(Expr e, IScope scope)
        {
            try { return Expr.Truthy(e.Eval(scope)); }
            catch (Exception) { return false; }
        }

        // ---------- in the report ----------

        /// <summary>YOUR WATCHES: every watch, what wakes it and whether each condition holds now, its check-in and its end; and how to give one when there is none.</summary>
        public static string Section(List<Watch> list, Snapshot v)
        {
            var sb = new StringBuilder();
            sb.Append("YOUR WATCHES (" + list.Count + " of " + MaxWatches + "; read every in-game hour; a condition wakes you when it turns true)");
            if (list.Count == 0) { sb.Append("\r\n- None. Give one with \"watch\" in your answer when a part of the map or an order needs following."); return sb.ToString(); }
            World world = v != null ? Runner.WorldFor(v) : null;
            foreach (Watch w in list.OrderBy(x => x.Since)) sb.Append("\r\n- ").Append(Describe(w, world, true));
            return sb.ToString();
        }

        /// <summary>One watch in words (now: with whether each condition holds now).</summary>
        internal static string Describe(Watch w, World world, bool now)
        {
            var sb = new StringBuilder();
            sb.Append(w.Name);
            var parts = new List<string>();
            if (w.Lord != null) parts.Add("lord " + (w.LordName ?? w.Lord) + " [" + w.Lord + "]");
            if (w.Given.Count > 0) parts.Add(string.Join(", ", w.Given.Select(kv => kv.Key + " = " + (world != null ? world.Name(w.Names.First(n => n.Key == kv.Key).Value) + " " : "") + "[" + kv.Value + "]")));
            if (parts.Count > 0) sb.Append(" (").Append(string.Join("; ", parts)).Append(")");
            sb.Append(":");
            IScope scope = now && world != null ? SafeScope(world, w) : null;
            if (w.When.Count > 0)
                sb.Append(" wakes you if ").Append(string.Join(" | ", w.When.Select(c => c.IfText + (scope != null ? (Holds(c.If, scope) ? " [true now" + (c.Armed ? "" : ", waits to turn false first") + "]" : " [not now]") : ""))));
            if (w.Follow.Count > 0) sb.Append(w.When.Count > 0 ? ";" : "").Append(" follows the orders of ").Append(string.Join(", ", w.Follow));
            if (w.CheckEvery > 0) sb.Append("; checks in every ").Append(ToolDef.Num(w.CheckEvery)).Append(" hours").Append(!double.IsNaN(w.LastCheck) ? " (next by " + Text.When(w.LastCheck + w.CheckEvery) + ")" : "");
            sb.Append("; ends ").Append(Text.When(w.Until)).Append(w.EndIfText != null ? " or once " + w.EndIfText : "").Append(".");
            if (!string.IsNullOrEmpty(w.Why)) sb.Append(" Why: ").Append(w.Why.TrimEnd('.')).Append(".");
            return sb.ToString();
        }

        private static IScope SafeScope(World world, Watch w)
        {
            try { return Runner.WatchScope(world, w.Party, w.Names); }
            catch (Exception) { return null; }
        }

        // ---------- kept beside the save (CarryOn) ----------

        // the watches written at the save just loaded, by realm, until each realm's first hour takes them up (worker)
        private static readonly Dictionary<string, JArray> Pending = new Dictionary<string, JArray>(StringComparer.Ordinal);

        /// <summary>At a load (worker): the watches written at that save, by realm (none: the load carries none on).</summary>
        public static void SetPending(Dictionary<string, JArray> byRealm)
        {
            Pending.Clear();
            if (byRealm != null) foreach (KeyValuePair<string, JArray> kv in byRealm) Pending[kv.Key] = kv.Value;
        }

        /// <summary>A realm's watches from the save, once (its first hour after the load).</summary>
        public static bool TakePending(string realm, out JArray saved)
        {
            saved = null;
            if (realm == null || !Pending.TryGetValue(realm, out saved)) return false;
            Pending.Remove(realm);
            return true;
        }

        /// <summary>Every self-waking agent's watches, for the save's file (worker).</summary>
        public static JObject ExportAll()
        {
            var o = new JObject();
            foreach (RealmAgent a in Agents.All) if (a.Realm != null && a.Watches.Count > 0) o[a.Realm] = Export(a.Watches);
            return o;
        }

        public static JArray Export(List<Watch> list)
        {
            var a = new JArray();
            foreach (Watch w in list)
                a.Add(new JObject
                {
                    ["name"] = w.Name, ["why"] = w.Why, ["lord"] = w.Lord, ["poke"] = w.Poke, ["version"] = w.Version,
                    ["names"] = new JObject(w.Given.Select(kv => new JProperty(kv.Key, kv.Value))),
                    ["wake_if"] = new JArray(w.When.Select(c => new JObject { ["if"] = c.IfText, ["say"] = c.Say, ["armed"] = c.Armed, ["was_true"] = c.WasTrue, ["fired"] = c.Fired })),
                    ["follow"] = new JArray(w.Follow), ["end_if"] = w.EndIfText, ["check_every"] = w.CheckEvery,
                    ["since"] = w.Since, ["until"] = w.Until, ["last_check"] = double.IsNaN(w.LastCheck) ? (double?)null : w.LastCheck
                });
            return a;
        }

        /// <summary>
        /// The watches written at a save, taken up at its load on the realm's first hour: each checked again as it was given (its lord still leading his party, its names
        /// known), with its clocks and conditions as they stood. Returns the names not carried on, with why.
        /// </summary>
        public static List<string> Import(List<Watch> list, JArray saved, Snapshot s, Dictionary<string, ViewParty> seen, string realm)
        {
            var no = new List<string>();
            list.Clear();
            foreach (JObject o in (saved ?? new JArray()).OfType<JObject>())
            {
                var copy = (JObject)o.DeepClone();
                double since = Num(o["since"]), until = Num(o["until"]);
                copy["until"] = double.IsNaN(until) || s == null ? (double?)null : Math.Max(1, until - s.Hours);
                Taken t = Take(list, copy, (int?)o["poke"] ?? -1, s, seen, realm);
                if (t.Status == "refused") { no.Add(t.Name + " (" + t.Reason + ")"); continue; }
                Watch w = list.First(x => x.Name == t.Name);
                w.Since = double.IsNaN(since) ? w.Since : since;
                w.Version = (int?)o["version"] ?? 1;
                double last = Num(o["last_check"]);
                if (!double.IsNaN(last)) w.LastCheck = last;
                if (o["wake_if"] is JArray conds)
                    for (int i = 0; i < Math.Min(conds.Count, w.When.Count); i++)
                    {
                        w.When[i].Armed = (bool?)conds[i]["armed"] ?? w.When[i].Armed;
                        w.When[i].WasTrue = (bool?)conds[i]["was_true"] ?? w.When[i].WasTrue;
                        w.When[i].Fired = (int?)conds[i]["fired"] ?? 0;
                    }
            }
            return no;
        }

        // ---------- helpers ----------

        private static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t is JArray a) return a.Count > 0 ? Str(a[0]) : null;
            string s = ((string)t)?.Trim().Trim('[', ']').Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static IEnumerable<string> List(JToken t)
        {
            if (t is JArray a) return a.Select(Str).Where(x => x != null);
            string one = Str(t);
            return one == null ? Enumerable.Empty<string>() : one.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);
        }

        private static double Num(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : double.NaN;
    }
}
