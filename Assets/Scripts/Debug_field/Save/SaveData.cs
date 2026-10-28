using System;
using System.Collections.Generic;

[Serializable]
public class SaveData
{
    // 세이브 포맷 버전
    public const int CurrentVersion = 3;

    public const int FirstCentralPlantVersion = 2;

    public const int FirstCivilizationVersion = 3;

    public int    version;

    public int    slotIndex;
    public string timestamp;
    public float  playTimeSeconds;
    // 키 없는 구버전은 Middle 유지
    public int worldSize = (int)WorldSize.Middle;

    public int worldTheme;

    public int worldSeed;

    public int gridWidth, gridHeight;

    public float playerX, playerY;

    public CatLook character = new();

    public long currency;

    public List<ItemSlotSave> inventorySlots = new();

    public string[] equipmentSlots = new string[5];

    public List<TechNodeSave> techNodes = new();

    public List<TileSave> placedTiles = new();

    public CentralPlantSave centralPlant = new();

    public List<ResourceBlockSave> resourceBlocks = new();

    public int       currentQuestIndex    = 0;
    public string    currentQuestId;
    public bool      allQuestsDone        = false;
    public List<int> questObjectiveAmounts = new();

    public List<string> harvestedCrops = new();

    public int gameDay    = -1;
    public int gameHour   = -1;
    public int gameMinute = -1;

    public string exploredCells;

    public int weather, weatherNext, weatherNext2;
    public int weatherPeriod     = -1;
    public int stormStruckPeriod = -1;

    public int ordersCompleted;

    public List<ShuttleSave>  shuttles = new();
    public int                shuttleSerial;
    public int                shuttleTrips;
    public int                population;
    public List<ResidentSave> residents = new();
    public bool               shuttlePadGranted;

    public List<string> codexGolden = new();
    public List<string> codexDishes = new();
    public int          codexMilestones;

    public List<GuestSave> guests = new();
}

[Serializable]
public class GuestSave
{
    public string id;
    public string name;
    public float  x, y;
    public List<ItemSlotSave> inventorySlots = new();
    public string[] equipmentSlots = new string[5];
}

[Serializable]
public class ItemSlotSave
{
    public string itemName;
    public int    count;
}

[Serializable]
public class TechNodeSave
{
    public string id;
    public int    level;
}

[Serializable]
public class TileSave
{
    public string itemName;
    public int    anchorX, anchorY;
    public int    rotation;
    public string subType;

    public int    plantState;
    public string seedDataName;
    public int    growthStage;
    public float  stageTimer;
    public int    plotFertility;

    public string machineRecipe;

    public int    compostPoints;

    public float  generatorFuel;

    public string seederSeedItem;

    public List<ItemSlotSave> storageItems = new();

    public string extractorDrill;

    public int    coreLevel;
    public float  coreGrowth;

    public ItemSlotSave smelterInput  = new();
    public ItemSlotSave smelterOutput = new();
    public float        smelterProgress;

    public List<LinkSave> links = new();
}

[Serializable]
public class LinkSave
{
    public int x, y;
}

[Serializable]
public class CentralPlantSave
{
    public bool  exists;
    public int   anchorX, anchorY;
    public float fuel;
    public int   level;
    public float growth;
}

[Serializable]
public class ResourceBlockSave
{
    public float  posX, posY;
    public string resourceDataName;
    public int    remaining;
}
