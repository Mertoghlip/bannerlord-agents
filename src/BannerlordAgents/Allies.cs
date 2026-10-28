using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BannerlordAgents
{
    /// <summary>
    /// The allied channel (0.13, docs/PLAN-0.13.md B5; Mert, Oct 28: "agents communicating with their allies to coordinate attacks, and the
    /// player being able to communicate with their allies (who are agents) through their liutenant"):
    /// - letters: an agent writes to an allied realm's agent (an alliance), a few lines delivered in that agent's next poke, saying who wrote
    ///   and when; Mert's realm is written to through his lieutenant, into its window. A letter binds no one and carries no orders; it tells
    ///   only what its writer chose to tell (each realm's fog holds). A letter to a realm no agent reads, or to one not allied, is refused;
    /// - joint plans: a letter may carry a plan in a fixed form (what, where, when, each side's part), so both sides see the same thing;
    ///   its state (proposed, agreed, declined, under way, done, dropped) changes by later letters, and agreed plans show in both realms'
    ///   reports under ALLIES AND JOINT PLANS.
    /// Kept per campaign in allies.jsonl, each line labelled with its session, so a load keeps the letters and plans of its save's history
    /// (the same rule as the chronicle's), and in the diplomacy book. Nothing of it is in the save. Worker thread.
    /// </summary>
    internal static class Allies
    {
        internal sealed class Letter
        {
            public string Id, From, FromName, To, ToName, Text, Plan, State, Source;
            public double Hours;
        }

        internal sealed class Plan
        {
            public string Id, From, To, What, Where, When, State = "proposed";
            public readonly Dictionary<string, string> Parts = new Dictionary<string, string>(StringComparer.Ordinal);
            public double Opened, Changed;
        }

        public static readonly string[] States = { "proposed", "agreed", "declined", "under way", "done", "dropped" };

        private static readonly List<Letter> Letters = new List<Letter>();
        private static readonly Dictionary<string, Plan> Plans = new Dictionary<string, Plan>(StringComparer.OrdinalIgnoreCase);
        private static int _letters, _plans;
        private static string _file;

        /// <summary>Who reads letters for the player's realm: the lieutenant's window (set by the bridge; null in the smoke test).</summary>
        public static Action<Letter, Plan> ToLieutenant;

        /// <summary>A load (worker): the letters and plans of this save's history.</summary>
        public static void Reset(string dataDir, History history)
        {
            Letters.Clear();
            Plans.Clear();
            _letters = _plans = 0;
            _file = dataDir != null ? Path.Combine(dataDir, "allies.jsonl") : null;
            if (_file == null || history == null) return;
            foreach (JObject l in LabelledLines.Read(_file, history))
            {
                string kind = (string)l["k"];
                if (kind == "letter")
                    Letters.Add(new Letter
                    {
                        Id = (string)l["id"], From = (string)l["from"], FromName = (string)l["from_name"], To = (string)l["to"], ToName = (string)l["to_name"], Text = (string)l["text"],
                        Plan = l["plan"]?.Type == JTokenType.String ? (string)l["plan"] : null, State = (string)l["state"], Source = (string)l["source"], Hours = (double?)l["hours"] ?? double.NaN
                    });
                else if (kind == "plan" && l["plan"] is JObject p)
                {
                    Plan plan = PlanFrom(p);
                    if (plan.Id != null) Plans[plan.Id] = plan;
                }
                Count((string)l["id"]);
                if (l["plan"] is JObject pp) Count((string)pp["id"]);
            }
        }

        /// <summary>Numbers carry on from the lines read back, so an id never means two letters or plans.</summary>
        private static void Count(string id)
        {
            if (id == null) return;
            if (id.StartsWith("jp", StringComparison.Ordinal) && int.TryParse(id.Substring(2), out int p)) _plans = Math.Max(_plans, p);
            else if (id.StartsWith("l", StringComparison.Ordinal) && int.TryParse(id.Substring(1), out int n)) _letters = Math.Max(_letters, n);
        }

        private static Plan PlanFrom(JObject p)
        {
            var plan = new Plan
            {
                Id = (string)p["id"], From = (string)p["from"], To = (string)p["to"], What = (string)p["what"], Where = (string)p["where"], When = (string)p["when"],
                State = (string)p["state"] ?? "proposed", Opened = (double?)p["opened"] ?? double.NaN, Changed = (double?)p["changed"] ?? double.NaN
            };
            if (p["parts"] is JObject parts) foreach (JProperty x in parts.Properties()) plan.Parts[x.Name] = (string)x.Value;
            return plan;
        }

        private static JObject PlanJson(Plan p)
        {
            var parts = new JObject();
            foreach (KeyValuePair<string, string> kv in p.Parts) parts[kv.Key] = kv.Value;
            return new JObject { ["id"] = p.Id, ["from"] = p.From, ["to"] = p.To, ["what"] = p.What, ["where"] = p.Where, ["when"] = p.When, ["parts"] = parts, ["state"] = p.State, ["opened"] = p.Opened, ["changed"] = p.Changed };
        }

        // ---------- who can be written to ----------

        /// <summary>Does an agent read letters for this realm: its own agent (switched on), or, for the player's realm, the lieutenant?</summary>
        public static bool Reads(string realm, out string how)
        {
            how = null;
            RealmAgent a = realm != null ? Agents.Of(realm) : null;
            if (a == null || !a.On) return false;
            how = a.Config.IsLieutenant ? "lieutenant" : "agent";
            return true;
        }

        /// <summary>Why a letter from one realm to another cannot go (null: it can): the realm known, an ally, someone there to read it.</summary>
        public static string CanWrite(Snapshot s, string from, string to)
        {
            if (to == null) return "a letter needs \"to\": an ally's id in [brackets]";
            if (s == null || !s.Factions.TryGetValue(from ?? "", out FactionRow mine)) return "your realm is not known yet this session";
            if (!s.Factions.ContainsKey(to)) return "no realm called '" + to + "' is known";
            if (to == from) return "a letter goes to another realm";
            if (!mine.Allies.Contains(to)) return Name(s, to) + " is not your ally: letters go to allies only";
            if (!Reads(to, out _)) return "no one reads letters for " + Name(s, to) + ": its ruler is the game's own";
            return null;
        }

        private static string Name(Snapshot s, string id) => s != null && s.FactionNames.TryGetValue(id ?? "", out string n) ? n : id;

        /// <summary>A realm named by id or name, from the hour's realms.</summary>
        private static string RealmId(Snapshot s, string q)
        {
            if (string.IsNullOrWhiteSpace(q) || s == null) return q;
            string id = q.Trim().Trim('[', ']');
            if (s.Factions.ContainsKey(id)) return id;
            string p = Text.Plain(id);
            return s.Factions.Values.FirstOrDefault(f => Text.Plain(f.Name) == p)?.Id ?? id;
        }

        // ---------- a letter ----------

        /// <summary>
        /// One letter of an answer: {"to": ally, "text": "...", "plan": {"what", "where", "when", "parts": {realm: words}}} for a new joint plan,
        /// or "plan_id" with "state" to answer or update one. Checked, delivered and recorded; returns what became of it (status: sent, refused).
        /// </summary>
        public static string Send(Snapshot s, string from, JObject l, string source, int poke, out string status)
        {
            status = "refused";
            if (l == null) return "write each letter as an object, for example {\"to\": \"...\", \"text\": \"...\"}";
            string to = RealmId(s, ((string)l["to"])?.Trim());
            string why = CanWrite(s, from, to);
            if (why != null) return why;
            string text = ((string)l["text"] ?? "").Trim();
            if (text.Length == 0) return "a letter needs \"text\"";
            if (text.Length > 2000) return "a letter is a few lines (at most 2,000 characters; this one has " + text.Length + ")";
            double hours = s.Hours;
            Plan plan = null;
            string planNote = null;
            if (l["plan"] is JObject pj)
            {
                plan = new Plan { Id = "jp" + (++_plans), From = from, To = to, What = ((string)pj["what"] ?? "").Trim(), Where = ((string)pj["where"] ?? "").Trim(), When = ((string)pj["when"] ?? "").Trim(), Opened = hours, Changed = hours };
                if (plan.What.Length == 0) { _plans--; return "a joint plan needs \"what\" (a siege, a field attack, holding a place, raiding)"; }
                if (pj["parts"] is JObject parts)
                    foreach (JProperty x in parts.Properties()) plan.Parts[RealmId(s, x.Name)] = ((string)x.Value ?? "").Trim();
                Plans[plan.Id] = plan;
                planNote = "a joint plan, " + plan.Id + ", proposed";
            }
            else if ((string)l["plan_id"] is string pid)
            {
                if (!Plans.TryGetValue(pid.Trim(), out plan) || (plan.From != from && plan.To != from)) return "no joint plan " + pid + " is between your realm and " + Name(s, to);
                string state = (((string)l["state"]) ?? "").Trim().ToLowerInvariant().Replace('_', ' ');
                if (!States.Contains(state)) return "a plan's state is one of: " + string.Join(", ", States);
                if ((state == "agreed" || state == "declined") && (plan.State != "proposed" || plan.To != from)) return "only the realm a plan was proposed to agrees to it or declines it, while it is proposed";
                if (plan.State == "declined" || plan.State == "done" || plan.State == "dropped") return "plan " + plan.Id + " is " + plan.State + " already";
                plan.State = state;
                plan.Changed = hours;
                planNote = "plan " + plan.Id + " is now " + state;
            }
            var letter = new Letter
            {
                Id = "l" + (++_letters), From = from, FromName = Name(s, from), To = to, ToName = Name(s, to), Text = text, Plan = plan?.Id, State = plan?.State, Source = source, Hours = hours
            };
            Letters.Add(letter);
            Keep(letter, plan, poke);
            Deliver(s, letter, plan);
            status = "sent";
            return "your letter " + letter.Id + " goes to " + letter.ToName + "'s ruler" + (planNote != null ? ", with " + planNote : "") + "; it reads it with its next news";
        }

        private static void Keep(Letter letter, Plan plan, int poke)
        {
            var line = new JObject
            {
                ["load"] = Bridge.History?.Label ?? Brain.History?.Label, ["h"] = letter.Hours, ["w"] = DateTime.Now.ToString("o", Text.Inv), ["k"] = "letter", ["id"] = letter.Id,
                ["from"] = letter.From, ["from_name"] = letter.FromName, ["to"] = letter.To, ["to_name"] = letter.ToName, ["text"] = letter.Text, ["plan"] = letter.Plan, ["state"] = letter.State,
                ["source"] = letter.Source, ["hours"] = letter.Hours, ["poke"] = poke, ["run"] = RunTags.Id, ["arm"] = RunTags.Arm
            };
            if (_file != null) Files.Append(_file, line.ToString(Newtonsoft.Json.Formatting.None));
            if (plan != null && _file != null)
                Files.Append(_file, new JObject { ["load"] = line["load"], ["h"] = letter.Hours, ["w"] = line["w"], ["k"] = "plan", ["plan"] = PlanJson(plan), ["by"] = letter.From, ["letter"] = letter.Id }.ToString(Newtonsoft.Json.Formatting.None));
            DiploBook.Write(new { Type = "letter", Realm = letter.From, Run = RunTags.Id, Arm = RunTags.Arm, Hours = letter.Hours, Id = letter.Id, letter.To, letter.Text, letter.Plan, letter.State, letter.Source, Poke = poke });
            if (plan != null) DiploBook.Write(new { Type = "plan", Realm = letter.From, Run = RunTags.Id, Arm = RunTags.Arm, Hours = letter.Hours, plan.Id, plan.From, plan.To, plan.What, plan.Where, plan.When, plan.Parts, plan.State, Letter = letter.Id });
            Diag.Count("allies:letters");
        }

        /// <summary>The letter to its reader: a realm's agent hears it in its next poke; the player's realm, in the lieutenant's window.</summary>
        private static void Deliver(Snapshot s, Letter letter, Plan plan)
        {
            Reads(letter.To, out string how);
            string text = "A letter from the ruler of " + letter.FromName + " [" + letter.From + "] (" + letter.Id + "): \"" + letter.Text + "\"" + (plan != null ? " " + PlanText(s, plan) : "");
            if (how == "lieutenant")
            {
                ToLieutenant?.Invoke(letter, plan);
                Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = letter.Hours, Kind = "letter", Text = text, Factions = { letter.To }, RecordOnly = true });
                return;
            }
            Brain.OnNotice(new Notice { Id = NoticeIds.Next(), Hours = letter.Hours, Kind = "letter", Text = text, Factions = { letter.To } });
        }

        /// <summary>A plan in words: what, where, when, each side's part, its state.</summary>
        public static string PlanText(Snapshot s, Plan p)
        {
            string parts = p.Parts.Count > 0 ? "; parts: " + string.Join("; ", p.Parts.Select(kv => Name(s, kv.Key) + ": " + kv.Value)) : "";
            return "[Joint plan " + p.Id + ": " + p.What + (p.Where.Length > 0 ? " at " + p.Where : "") + (p.When.Length > 0 ? ", " + p.When : "") + parts + "; " + p.State + ", proposed by " + Name(s, p.From) + "]";
        }

        // ---------- what the report shows ----------

        /// <summary>ALLIES AND JOINT PLANS: each ally, whether its ruler reads letters, its wars, its last letters to you; the plans between you still open.</summary>
        public static List<string> Section(Snapshot s, string realm)
        {
            var lines = new List<string>();
            if (s == null || realm == null || !s.Factions.TryGetValue(realm, out FactionRow mine)) return lines;
            foreach (string ally in mine.Allies)
            {
                s.Factions.TryGetValue(ally, out FactionRow fr);
                List<Letter> from = Letters.Where(l => l.From == ally && l.To == realm).ToList();
                var sb = new StringBuilder(Name(s, ally) + " [" + ally + "]: your ally");
                sb.Append(Reads(ally, out _) ? "; its ruler reads letters (write in \"letters\")" : "; its ruler is the game's own and reads no letters");
                if (fr != null) sb.Append("; at war with " + (fr.AtWar.Count > 0 ? string.Join(", ", fr.AtWar.Select(x => Name(s, x))) : "no one"));
                foreach (Letter l in from.Skip(Math.Max(0, from.Count - 2))) sb.Append("; its letter " + l.Id + " of " + Text.When(l.Hours) + ": \"" + Cut(l.Text, 300) + "\"");
                lines.Add(sb.Append('.').ToString());
            }
            foreach (Plan p in Plans.Values.Where(x => (x.From == realm || x.To == realm) && (x.State == "proposed" || x.State == "agreed" || x.State == "under way" || s.Hours - x.Changed < 72)).OrderBy(x => x.Opened))
                lines.Add(PlanText(s, p).Trim('[', ']') + " (since " + Text.When(p.Changed) + ").");
            return lines;
        }

        private static string Cut(string t, int n) => t.Length <= n ? t : t.Substring(0, n) + "...";

        /// <summary>The plans, for the smoke test and the dashboard.</summary>
        public static List<Plan> AllPlans() => Plans.Values.ToList();
        public static List<Letter> AllLetters() => Letters.ToList();
    }
}
