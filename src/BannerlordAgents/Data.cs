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
        /// <summary>The watched realm's own lords only: the party's size limit and the days its food lasts.</summary>
        public int SizeLimit, FoodDays = -1;
        /// <summary>The game's own flag: the player's party can see this one.</summary>
        public bool SeenByPlayer;
        /// <summary>Can be spotted at all: outside a settlement, or a lord flying his banner inside one.</summary>
        public bool Spottable;
    }

    internal sealed class SettlementRow
    {
        public string Id, Name, Kind, Faction;
        public float X, Y;
        public string BesiegedBy;
        public string Besieger;
        public int Garrison;
        public int Militia;
        public string VillageState;
        public string Bound;
    }

    internal sealed class FactionRow
    {
        public string Id, Name, Ruler;
        public List<string> AtWar = new List<string>();
        public List<string> Allies = new List<string>();
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
    }

    /// <summary>The world as copied once per in-game hour.</summary>
    internal sealed class Snapshot
    {
        public string Campaign;
        public double Hours;
        public string Date;
        public bool Night;
        public string PlayerFaction, PlayerParty, Watched;
        public List<PartyRow> Parties = new List<PartyRow>(4096);
        public Dictionary<string, PartyRow> PartyById = new Dictionary<string, PartyRow>(4096);
        public List<SettlementRow> Settlements = new List<SettlementRow>(1024);
        public Dictionary<string, SettlementRow> SettlementById = new Dictionary<string, SettlementRow>(1024);
        public Dictionary<string, FactionRow> Factions = new Dictionary<string, FactionRow>();
        public Dictionary<string, string> FactionNames = new Dictionary<string, string>();
        public HashSet<string> HostileToWatched = new HashSet<string>();
        public HashSet<string> HostileToPlayer = new HashSet<string>();
        /// <summary>What the watched realm sees, by party id.</summary>
        public Dictionary<string, Sighting> WatchedSees;
        /// <summary>Settlements close enough to the watched realm's eyes for their garrisons to be counted.</summary>
        public HashSet<string> InspectedByWatched;

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
