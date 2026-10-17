using System.Collections.Generic;

namespace BannerlordAgents
{
    // Plain copies of game state, taken on the game's thread and handed to the worker. Nothing in here points at a
    // game object, so the worker can read them freely. Field names become the JSON names (snake_case) in the logs;
    // see protocol/README.md.

    /// <summary>One party as copied on the game's thread.</summary>
    internal sealed class PartyRow
    {
        public string Id;
        public string Name;
        public string Faction;
        public string Leader;
        public float X, Y;
        public int Men;
        public int Wounded;
        public float Strength;
        /// <summary>lord, caravan, villager, bandit, garrison, militia or other.</summary>
        public string Kind;
        public string InSettlement;
        /// <summary>The party id of the army's leader, when this party belongs to an army (its own id if it leads).</summary>
        public string ArmyLeader;
        /// <summary>Men in the whole army, when this party leads one.</summary>
        public int ArmyMen;
        public int ArmyParties;
        /// <summary>Marching inside someone else's army.</summary>
        public bool Attached;
        /// <summary>The game's own name for what the party is doing (GoToSettlement, BesiegeSettlement, ...).</summary>
        public string Doing;
        public string TargetSettlement;
        public string TargetParty;
        public string Besieging;
        public bool InBattle;
        /// <summary>The village it is raiding right now (not just marching to).</summary>
        public string Raiding;
        /// <summary>For an army's leader: the whole army's strength, the game's own estimate.</summary>
        public float ArmyStrength;
        /// <summary>Map units per in-game hour, as the game last worked it out (lords only).</summary>
        public float Speed;
        /// <summary>The watched realm's own lords only: the party's size limit and the days its food lasts, and the lord's clan.</summary>
        public int SizeLimit, FoodDays = -1;
        public string Clan;
        /// <summary>The game's own flag: the player's party can see this one.</summary>
        public bool SeenByPlayer;
        /// <summary>Can be spotted at all: outside a settlement, or a lord flying his banner inside one.</summary>
        public bool Spottable;
        /// <summary>
        /// For the dashboard (0.7): where the party is walking to (the game's MoveTargetPoint), its speed with the ground and the
        /// night counted in (lords only), and, for lords and caravans on the march, the corners of the way the game planned
        /// for it, still ahead, as x, y pairs (at most 48); Straight when it walks straight there with no planned way.
        /// </summary>
        public float Tx, Ty, FinalSpeed;
        public float[] Route;
        public bool Straight;
        /// <summary>
        /// The holding whose gate the game counts this party's road distances from (MapDistanceModel.GetClosestEntranceToFace
        /// for the ground it stands on): with the game's table of road distances between holdings (RoadBook), the travel
        /// distance to anywhere, as the game reckons it (Roads, 0.7.1).
        /// </summary>
        public string Entrance;
    }

    /// <summary>
    /// The game's own road distances between holdings (its navigation cache: the real way between gates), copied once on the
    /// game's thread (Capture.RoadsFor): every holding of the realms followed to every other, and each holding to its nearest
    /// neighbours. Never changed once handed to the worker: a new copy replaces it.
    /// </summary>
    internal sealed class RoadBook
    {
        public readonly Dictionary<string, Dictionary<string, float>> Rows = new Dictionary<string, Dictionary<string, float>>();
        public int Pairs;

        public void Set(string a, string b, float d)
        {
            if (!Rows.TryGetValue(a, out Dictionary<string, float> row)) Rows[a] = row = new Dictionary<string, float>();
            if (!row.ContainsKey(b)) Pairs++;
            row[b] = d;
        }

        public bool TryGet(string a, string b, out float d)
        {
            d = float.NaN;
            if (a == null || b == null) return false;
            if (a == b) { d = 0; return true; }
            return (Rows.TryGetValue(a, out Dictionary<string, float> r1) && r1.TryGetValue(b, out d)) || (Rows.TryGetValue(b, out Dictionary<string, float> r2) && r2.TryGetValue(a, out d));
        }

        public bool HasRow(string a) => a != null && Rows.ContainsKey(a);
    }

    internal sealed class SettlementRow
    {
        public string Id, Name, Kind, Faction;
        public float X, Y;
        /// <summary>The gate, where parties march to and where the game counts road distances from.</summary>
        public float Gx, Gy;
        public string BesiegedBy;
        public string Besieger;
        public int Garrison;
        /// <summary>
        /// The watched realm's and its allies' castles and towns only: the garrison's and the militia's strength (the
        /// game's own estimate) and the walls' level (1 to 3), which decide whether the place would hold an assault.
        /// </summary>
        public float GarrisonStrength, MilitiaStrength;
        public int WallLevel;
        public int Militia;
        public string VillageState;
        public string Bound;
    }

    /// <summary>A lord of the watched realm held prisoner, from the game's own records (what the encyclopedia shows).</summary>
    internal sealed class CaptiveRow
    {
        public string Hero, Name;
        /// <summary>Where: a holding's id, or the party holding the lord (with that party's lord).</summary>
        public string Holding, Party, PartyLord, Faction;
        public float X, Y;
        /// <summary>The in-game hour the captivity began (NaN when the game does not say).</summary>
        public double Since = double.NaN;
    }

    /// <summary>One clan of the watched realm: its influence (the price of calling lords into an army), and the ruler's own clan's gold.</summary>
    internal sealed class ClanRow
    {
        public string Id, Name, Leader;
        public float Influence;
        /// <summary>Known only for the ruler's own clan (the realm's treasury); -1 for the others.</summary>
        public int Gold = -1;
        public bool Ruler;
    }

    internal sealed class FactionRow
    {
        public string Id, Name, Ruler;
        public List<string> AtWar = new List<string>();
        public List<string> Allies = new List<string>();
        /// <summary>The realm's colours as the game paints them (ARGB), for the dashboard.</summary>
        public uint Color, Color2;
    }

    internal sealed class Sighting
    {
        public string Party;
        public string By;
        public float Distance;
    }

    /// <summary>Notice numbers, shared by the game's thread (events) and the worker (the tools' call-backs). Game-free.</summary>
    internal static class NoticeIds
    {
        private static int _next;
        public static int Next() => System.Threading.Interlocked.Increment(ref _next);
    }

    /// <summary>Something a ruler would be told about: a war, a siege, a raid, a holding lost, an army broken.</summary>
    internal sealed class Notice
    {
        /// <summary>Numbered as it happens, so the logs can follow it to the poke that carried it.</summary>
        public int Id;
        public double Hours;
        public string Kind;
        public string Text;
        public List<string> Factions = new List<string>();
        public string Party;
        /// <summary>The holding it is about, if any (a siege, a raid, a holding taken, a battle at a holding, a tool's place).</summary>
        public string Place;
        /// <summary>
        /// Why it is kept to the logs instead of poking anyone (a fight with bandits, a caravan or villagers; a raid
        /// already announced; a lost party whose lord's capture already says it), or null when it pokes.
        /// </summary>
        public string Quiet;
        /// <summary>Goes into the realm's chronicle, and so its recent history, without poking: a lord set free, an army of its own formed.</summary>
        public bool RecordOnly;
    }

    /// <summary>
    /// One line of a realm's chronicle (Chronicle): something it learned, and when. Game-free; the worker writes it to
    /// the campaign's folder and the agent channel.
    /// </summary>
    internal sealed class ChronicleEntry
    {
        public double Hours;
        public string Date;
        /// <summary>The notice kind (war, besieged, battle, ...), sighting, order, refused, or the game's own entry kind.</summary>
        public string Kind;
        public string Text;
        public string Place;
        public string Party;
        /// <summary>"game" for a line from the game's own history log, read at the first load; empty for our own.</summary>
        public string From;
    }

    /// <summary>The world as copied once per in-game hour.</summary>
    internal sealed class Snapshot
    {
        public string Campaign;
        /// <summary>This load of the campaign, and the in-game hour of the save it came from (Bridge.LoadId, Bridge.LoadedAt).</summary>
        public string LoadId;
        public double LoadedAt = double.NaN;
        public double Hours;
        public string Date;
        public bool Night;
        public string PlayerFaction, PlayerParty, Watched;
        /// <summary>The walking map's checksum (MapExport), so the dashboard knows which map the world stands on.</summary>
        public string MapCrc;
        /// <summary>The game's road distances between holdings (Capture.RoadsFor); null when not copied.</summary>
        public RoadBook Roads;
        public List<PartyRow> Parties = new List<PartyRow>(4096);
        public Dictionary<string, PartyRow> PartyById = new Dictionary<string, PartyRow>(4096);
        public List<SettlementRow> Settlements = new List<SettlementRow>(1024);
        public Dictionary<string, SettlementRow> SettlementById = new Dictionary<string, SettlementRow>(1024);
        public Dictionary<string, FactionRow> Factions = new Dictionary<string, FactionRow>();
        public Dictionary<string, string> FactionNames = new Dictionary<string, string>();
        /// <summary>Every faction's colour (realms, clans, bandits), ARGB, for the dashboard's map.</summary>
        public Dictionary<string, uint> FactionColors = new Dictionary<string, uint>();
        public HashSet<string> HostileToWatched = new HashSet<string>();
        public HashSet<string> HostileToPlayer = new HashSet<string>();
        /// <summary>What the watched realm sees, by party id.</summary>
        public Dictionary<string, Sighting> WatchedSees;
        /// <summary>Settlements close enough to the watched realm's eyes for their garrisons to be counted.</summary>
        public HashSet<string> InspectedByWatched;
        /// <summary>The watched realm's lords held prisoner, and its clans.</summary>
        public List<CaptiveRow> Captives = new List<CaptiveRow>();
        public List<ClanRow> Clans = new List<ClanRow>();
        /// <summary>How far a lord may be from an army's leader and still be called into the army (the game's rule, in map units along the way).</summary>
        public float CallRange;

        // for the logs (docs/LOGGING.md)
        /// <summary>When the copy began (UTC ticks), so the logs can say how long the worker took after it.</summary>
        public long Wall;
        /// <summary>The copy's parts on the game's thread, in ms: realms, holdings, parties, hostility, sight, names.</summary>
        public double[] PartsMs;
        public VisionStats Vision;
        /// <summary>Every few in-game hours: our sight rule against the game's own, for the player's party.</summary>
        public SightAudit Sight;
    }

    internal sealed class VisionStats
    {
        public int Eyes, Inspected, Seen, RatioAsked, RatioCached, RatioFailed, RangeFailed;
    }

    internal sealed class SightAudit
    {
        public float Range;
        public bool Night;
        public int Total, Agree;
        public List<SightCase> Disagree = new List<SightCase>();
    }

    internal sealed class SightCase
    {
        public string Id, Name, Kind;
        public float Distance, Ratio;
        public int Men;
        public bool Ours, Game, InSettlement, ArmyMember, Besieging, Raiding;
    }

    /// <summary>A party the player could see when a decision was made.</summary>
    internal sealed class SeenParty
    {
        public string Id, Name, Faction, Kind, Doing;
        public int Men, ArmyMen;
        public float Strength, X, Y, Distance;
        public bool Hostile;
    }

    internal sealed class TargetInfo
    {
        public string Kind, Id, Name, Faction;
        public int Men;
        public float X, Y;
    }

    /// <summary>One decision of the player's, as recorded. The worker adds the realm and the recent pokes.</summary>
    internal sealed class DecisionRecord
    {
        public string Type = "decision";
        public string Kind;
        public string Text;
        public double Hours;
        public string Date;
        public string Wall;
        public string Faction;
        /// <summary>For orders the player gives a lord: the lord's hero id and the verb (the agents' own verbs).</summary>
        public string Lord, Verb;
        public SeenParty You;
        public TargetInfo Target;
        public List<SeenParty> SeenByYou;
        public object Realm;
        public List<Trigger> RecentPokes;
        /// <summary>For movements: in-game hours from the first click of this course to the decision, and quick corrections merged into it.</summary>
        public double? SettleHours;
        public int? Corrections;
    }

    internal sealed class Trigger
    {
        public double Hours;
        public string Kind;
        public string Text;
        public string Party;
        /// <summary>The holding it is about, if any.</summary>
        public string Place;
        /// <summary>The notice it came from, if any.</summary>
        public int? Notice;
    }
}
