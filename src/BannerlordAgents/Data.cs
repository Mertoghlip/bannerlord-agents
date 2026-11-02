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
        /// <summary>
        /// The game's speed on its base (open ground by day, no night), as it last worked it out (lords only). Not map units an
        /// hour: a party covers its speed / 1.2 each hour (GamePace).
        /// </summary>
        public float Speed;
        /// <summary>
        /// The parts of its speed sum (GamePace, 0.7.3), for lords not marching in someone else's army: the base number and the
        /// sum of the base factors, from the game's own cache (the very numbers it moves the party with); the ground under it
        /// now (the game's TerrainType); its people for TAOM's march bonuses (the lord's culture, else its realm's); its scout's
        /// ground and hour perks (GamePace.Perk flags); the share of its men on foot; and snowfall where it stands.
        /// </summary>
        public float SpeedBase, SpeedFactors;
        public int Terrain;
        public string Culture;
        public int ScoutPerks;
        public float FootShare = 1f;
        public bool Snowing;
        /// <summary>Lords: the party's morale and its leader's Tactics skill (a battle's advantage is 1 + 0.001 a point), for battles run ahead (0.7.4); only the realm's own reach the agent.</summary>
        public float Morale = -1f;
        public int Tactics;
        /// <summary>The watched realm's own lords only: the party's size limit and the days its food lasts, and the lord's clan.</summary>
        public int SizeLimit, FoodDays = -1;
        public string Clan;
        /// <summary>
        /// The watched realms' own lords too (0.9.4, Purse): his gold; his wages a day and their cap (-1: no cap; CapOurs: the
        /// agent set it); the loot he carries (its worth at base value, and pieces); his prisoners (soldiers, lords, and their
        /// ransom by the game's reckoning); and his last sale (the hour, and in words).
        /// </summary>
        public int Gold = -1, Wages, WageCap = -1, Loot, LootPieces, Prisoners, PrisonerLords, Ransom;
        public bool CapOurs;
        public double SaleHours = double.NaN;
        public string SaleText;
        /// <summary>
        /// 0.13 (the language's sold_last and recruited_last): what his last sale brought in gold (loot and ransom), and the men he took at
        /// his last recruiting stop, when, and where.
        /// </summary>
        public int SaleGold, RecruitedLast;
        public double RecruitedLastHours = double.NaN;
        public string RecruitedLastAt;
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
        /// <summary>A village's health, 0 to 1 (0.7.4): a raid takes it down hour by hour and loots the village at 0.</summary>
        public float HitPoints = 1f;
        public string Bound;
        /// <summary>
        /// A castle's or town's food (0.13.19, the supply order): its store now (Town.FoodStocks, -1 for a village), the most it holds
        /// (FoodStocksUpperLimit) and what it eats a day under siege, when nothing comes in (the game's food model: its people's
        /// prosperity / 40 and its garrison's men / 20). Only the realm's own are shown (Views).
        /// </summary>
        public float Food = -1, FoodCap, FoodPerDay;
    }

    /// <summary>
    /// One siege as the game runs it, copied hourly for its clock (0.7.3, Forecast.Siege; read in the 1.4.8 code and the pack's
    /// mods): the camp's preparations and engines with the rate each is built at by the model in force, the numbers of the
    /// game's storm test, and the besieged place's food.
    /// </summary>
    internal sealed class SiegeRow
    {
        public string Settlement, Besieger, BesiegerFaction, Strategy;
        public double StartHours;
        /// <summary>The camp's preparations: progress 0 to 1, and how much of it a hour (GetConstructionProgressPerHour).</summary>
        public float Prep, PrepRate;
        /// <summary>The camp's engines, deployed and in reserve.</summary>
        public List<EngineRow> Engines = new List<EngineRow>();
        /// <summary>Progress a hour for each engine type the camp could build next, by the model in force.</summary>
        public Dictionary<string, float> Rates = new Dictionary<string, float>();
        public int WallLevel;
        /// <summary>Every wall section down (the walls then count a quarter), and the walls' mean standing share.</summary>
        public bool WallsDown;
        public float WallShare = 1f;
        /// <summary>The storm test's numbers now: the place's advantage, engines counted as built, the most-advanced unfinished one's progress.</summary>
        public float Advantage = 1f, MaxProgress;
        public int Built;
        /// <summary>The storm test's strengths: the defenders it counts (halved with the player inside) and the besiegers; the garrison's part of the defenders.</summary>
        public float Defenders, Attackers, GarrisonPower;
        /// <summary>The two sides' healthy men (the storm test's parties), and the besiegers' morale weighted by men: for a storm's clock run ahead (0.7.4).</summary>
        public int AttackerMen, DefenderMen;
        public float AttackerMorale = 50f;
        public bool PlayerInside;
        /// <summary>The place's food, its change a day, its store's limit; the garrison's regulars and wounded; starving now.</summary>
        public float Food, FoodChange;
        public int FoodCap, GarrisonRegulars, GarrisonWounded;
        public bool Starving;
        /// <summary>The besieging army's cohesion and its change a day (-1 when the camp's leader leads no army).</summary>
        public float Cohesion = -1f, CohesionPerDay;
        /// <summary>A storm is being fought now.</summary>
        public bool Storming;
    }

    internal sealed class EngineRow
    {
        public string Type;
        public float Progress, Hitpoints;
        public bool Reserve;
    }

    /// <summary>
    /// One battle on the map, copied hourly for its clock (0.7.3, Forecast.Battle; MapEvent in 1.4.8 with TAOM's combat models):
    /// its kind, where, since when, and each side's healthy men, losses, mean troop power by the model in force, advantage
    /// and morale.
    /// </summary>
    internal sealed class BattleRow
    {
        public string Key, Kind, Settlement;
        /// <summary>At a town (a storm's strike rule changes below 100 defenders there, 30 at a castle).</summary>
        public bool Town;
        public float X, Y;
        public double StartHours;
        public float SettlementAdvantage = 1f;
        public string Retreating;
        public BattleSideRow Attackers = new BattleSideRow(), Defenders = new BattleSideRow();
    }

    internal sealed class BattleSideRow
    {
        public string Leader, Faction;
        /// <summary>Led by a holding (siege defenders): its men never rout, their morale counts at least 30.</summary>
        public bool SettlementLed;
        public int Healthy, Wounded, Casualties, StartHealthy;
        /// <summary>Mean troop power of the healthy men (MilitaryPowerModel.GetTroopPower), the side's battle advantage, and morale weighted by men.</summary>
        public float Power = 1f, Advantage = 1f, Morale = 50f;
        public List<string> Parties = new List<string>();
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

    /// <summary>One clan of the watched realm: its influence (the price of calling lords into an army), and its gold.</summary>
    internal sealed class ClanRow
    {
        public string Id, Name, Leader;
        public float Influence;
        /// <summary>Its leader's gold, which pays the clan's lords their top-ups and sets the game's wage caps (every clan of the realm since 0.9.4; before, the ruler's only); -1 unknown.</summary>
        public int Gold = -1;
        /// <summary>Its gold's change a day by the game's own reckoning (the clan screen's number; 0.9.4); int.MinValue when not worked out.</summary>
        public int Daily = int.MinValue;
        public bool Ruler;
    }

    /// <summary>
    /// What one won battle brought one watched realm (0.9.4, TakingsPatch): its winning lords and their men, the enemy's fallen
    /// (the beaten side's casualties, shared out among the winners by men), and the loot, prisoners and ransom its lords gained.
    /// </summary>
    internal sealed class TakingsRow
    {
        public string Type = "takings";
        public double Hours;
        public string Date, Realm, Kind, Place, Load;
        public List<string> Lords = new List<string>();
        public int Men, EnemyFallen, Loot, Pieces, Prisoners, PrisonerLords, Ransom;
    }

    internal sealed class FactionRow
    {
        public string Id, Name, Ruler;
        /// <summary>Its living lords, as the encyclopedia lists them (0.7.4).</summary>
        public int Lords;
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
        /// <summary>The test run it happened in (RunTags, 0.13); null outside a run.</summary>
        public string Run = RunTags.Id;
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
        /// <summary>The game session (load id) that wrote it; null for lines written before labels began (0.9.1). Lines are never cut: a load keeps those of its save's history (History).</summary>
        public string Load;
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
        /// <summary>The test run this hour belongs to (RunTags, 0.13): its id and arm; null outside a run.</summary>
        public string Run, Arm;
        /// <summary>
        /// The player rules the player's realm himself this hour (Recorder.YouRule, 0.13), so his moves are his to record; and, in a
        /// test run of his (A, B), his realm's report is kept every hour too (RecordHourly), for the hours he let things run.
        /// </summary>
        public bool YouRule, RecordHourly;
        /// <summary>The walking map's checksum (MapExport), so the dashboard knows which map the world stands on.</summary>
        public string MapCrc;
        /// <summary>The game's road distances between holdings (Capture.RoadsFor); null when not copied.</summary>
        public RoadBook Roads;
        public List<PartyRow> Parties = new List<PartyRow>(4096);
        public Dictionary<string, PartyRow> PartyById = new Dictionary<string, PartyRow>(4096);
        public List<SettlementRow> Settlements = new List<SettlementRow>(1024);
        public Dictionary<string, SettlementRow> SettlementById = new Dictionary<string, SettlementRow>(1024);
        public Dictionary<string, FactionRow> Factions = new Dictionary<string, FactionRow>();
        /// <summary>Each realm an agent serves: its diplomacy as its ruler's screens show it (0.13, DiploView).</summary>
        public Dictionary<string, DiploView> Diplomacy = new Dictionary<string, DiploView>();
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
        /// <summary>
        /// The marching rules in force (0.7.3, GamePace): the speed model's name, each people's march bonuses by culture id (TAOM's
        /// feats, read from the game at load; null when the model in force is not TAOM's), and the scout perks' values.
        /// </summary>
        public string SpeedModel;
        public Dictionary<string, float[]> CultureSpeeds;
        public Dictionary<string, string> CultureNames;
        public Common.PerkValues PerkValues;
        /// <summary>Every siege now, and every battle with a lord or a holding in it (Capture.Sieges, Capture.Battles; 0.7.3).</summary>
        public List<SiegeRow> Sieges = new List<SiegeRow>();
        public List<BattleRow> Battles = new List<BattleRow>();

        // for the logs (docs/LOGGING.md)
        /// <summary>When the copy began (UTC ticks), so the logs can say how long the worker took after it.</summary>
        public long Wall;
        /// <summary>The copy's parts on the game's thread, in ms: realms, holdings, parties, hostility, sight, names.</summary>
        public double[] PartsMs;
        public VisionStats Vision;
        /// <summary>Every few in-game hours: our sight rule against the game's own, for the player's party.</summary>
        public SightAudit Sight;

        /// <summary>
        /// Since 0.8, every watched realm's own part of the hour (its sight, its enemies, its captives and clans), the first
        /// realm's also in the fields above. Empty in a copy made by hand (the smoke test): then the fields above are the one realm.
        /// </summary>
        public List<SeatSight> Seats = new List<SeatSight>();

        /// <summary>The hour as one watched realm has it: the same world, with that realm's own part in the fields above.</summary>
        public Snapshot For(SeatSight seat)
        {
            var v = (Snapshot)MemberwiseClone();
            v.Watched = seat.Realm;
            v.WatchedSees = seat.Sees;
            v.InspectedByWatched = seat.Inspected;
            v.HostileToWatched = seat.Hostile;
            v.Captives = seat.Captives;
            v.Clans = seat.Clans;
            v.Vision = seat.Vision;
            return v;
        }

        /// <summary>Each watched realm's hour, the first realm's first (just this copy when it was made by hand).</summary>
        public List<Snapshot> SeatViews()
        {
            if (Seats == null || Seats.Count == 0) return new List<Snapshot> { this };
            var list = new List<Snapshot>(Seats.Count);
            for (int i = 0; i < Seats.Count; i++) list.Add(i == 0 && Seats[i].Realm == Watched ? this : For(Seats[i]));
            return list;
        }
    }

    /// <summary>One watched realm's own part of an hourly copy (Snapshot.Seats).</summary>
    internal sealed class SeatSight
    {
        public string Realm;
        public Dictionary<string, Sighting> Sees;
        public HashSet<string> Inspected;
        public HashSet<string> Hostile = new HashSet<string>();
        public List<CaptiveRow> Captives = new List<CaptiveRow>();
        public List<ClanRow> Clans = new List<ClanRow>();
        public VisionStats Vision;
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
        /// <summary>
        /// Since 0.13 (docs/PLAN-0.13.md C2): the test run and its arm (null outside a run), this load, the realm's report as of that
        /// hour (record\reports\(hour).txt.gz, the same text an agent of the realm reads, and its fingerprint), and what the move
        /// was in detail (the troops recruited, the loot sold, the vote cast, ...).
        /// </summary>
        public string Run, Arm, Load, Report, ReportHash;
        public object Detail;
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
