using UnityEngine;
using System.Collections.Generic;

public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance => Services.Get<QuestManager>();

    [Header("퀘스트 목록 (순서대로 진행됩니다)")]
    [SerializeField] private List<QuestDefinition> questList = new();

    private int                    currentIndex = -1;
    private QuestDefinition        currentDef;
    private List<QuestObjective>   activeObjectives = new();

    // 로드 후 Start에서 덮어쓰기 방지
    private bool restoredFromSave;

    public QuestDefinition       CurrentQuest      => currentDef;
    public List<QuestObjective>  ActiveObjectives  => activeObjectives;
    public bool                  AllQuestsDone     => currentDef == null && currentIndex >= questList.Count;
    public int                   CurrentQuestIndex => currentIndex;
    public string                CurrentQuestId    => currentDef != null ? currentDef.name : "";

    private QuestChain chain;
    private readonly List<int> chapterOfQuest = new();
    private readonly List<int> chapterStart   = new();

    public int CurrentChapterIndex => currentDef != null && currentIndex >= 0 && currentIndex < chapterOfQuest.Count ? chapterOfQuest[currentIndex] : -1;
    public QuestChain.Chapter CurrentChapter => chain != null && CurrentChapterIndex >= 0 ? chain.chapters[CurrentChapterIndex] : null;
    public int StepInChapter => CurrentChapterIndex >= 0 ? currentIndex - chapterStart[CurrentChapterIndex] + 1 : 0;
    public int StepsInChapter => CurrentChapter?.quests.FindAll(q => q != null).Count ?? 0;
    public int ChapterCount => chain != null ? chain.chapters.Count : 0;

    public int ResolveSavedIndex(string questId, int savedIndex)
    {
        string name = !string.IsNullOrEmpty(questId) ? questId : $"Quest{savedIndex + 1}";
        int i = questList.FindIndex(q => q != null && q.name == name);
        if (i >= 0) return i;
        if (chain != null && chapterStart.Count > 0)
            return chapterStart[Mathf.Clamp(CenterCore.CurrentLevel - 1, 0, chapterStart.Count - 1)];
        return savedIndex;
    }

    public IReadOnlyList<QuestDefinition> AllQuests => questList;

    public event System.Action OnQuestUpdated;

    public event System.Action<QuestDefinition> OnQuestStarted;

    public event System.Action<QuestDefinition> OnQuestCompleted;

    public event System.Action OnAllQuestsDone;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        LoadChain();
    }

    private void LoadChain()
    {
        chain = QuestChain.Load();
        chapterOfQuest.Clear();
        chapterStart.Clear();
        if (chain == null || chain.Flatten().Count == 0) { chain = null; return; }

        questList = new List<QuestDefinition>();
        for (int c = 0; c < chain.chapters.Count; c++)
        {
            chapterStart.Add(questList.Count);
            foreach (var q in chain.chapters[c].quests)
            {
                if (q == null) continue;
                questList.Add(q);
                chapterOfQuest.Add(c);
            }
        }
    }

    private void Start()
    {
        if (Inventory.Instance != null)
            Inventory.Instance.OnItemAdded += HandleItemAdded;

        if (CraftingManager.Instance != null)
            CraftingManager.Instance.OnItemCrafted += HandleItemCrafted;

        if (ShopManager.Instance != null)
            ShopManager.Instance.OnItemSold += HandleItemSold;

        if (TechTreeManager.Instance != null)
            TechTreeManager.Instance.OnNodeUpgraded += HandleTechUpgraded;

        PlantTile.OnAnyPlantHarvested += HandlePlantHarvested;
        PlantTile.OnAnySeedPlanted    += HandleSeedPlanted;
        TileItem.OnAnyTilePlaced      += HandleTilePlaced;
        CenterCore.OnAnyLevelUp       += HandleCoreLevelUp;
        ShuttleService.OnAnyArrival   += HandleShuttleArrived;
        AutoHarvester.OnAnyMachineHarvest += HandleMachineHarvest;

        // 멀티: 호스트만 진행
        if (NetSession.IsReplica) return;

        SyncCoreLevelObjectives();

        if (restoredFromSave)
        {
            if (currentDef != null) NotifyAndCheck();
        }
        else if (questList.Count > 0)
        {
            ActivateQuest(0);
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        if (Inventory.Instance != null)
            Inventory.Instance.OnItemAdded -= HandleItemAdded;

        if (CraftingManager.Instance != null)
            CraftingManager.Instance.OnItemCrafted -= HandleItemCrafted;

        if (ShopManager.Instance != null)
            ShopManager.Instance.OnItemSold -= HandleItemSold;

        if (TechTreeManager.Instance != null)
            TechTreeManager.Instance.OnNodeUpgraded -= HandleTechUpgraded;

        PlantTile.OnAnyPlantHarvested -= HandlePlantHarvested;
        PlantTile.OnAnySeedPlanted    -= HandleSeedPlanted;
        TileItem.OnAnyTilePlaced      -= HandleTilePlaced;
        CenterCore.OnAnyLevelUp       -= HandleCoreLevelUp;
        ShuttleService.OnAnyArrival   -= HandleShuttleArrived;
        AutoHarvester.OnAnyMachineHarvest -= HandleMachineHarvest;

        Services.Unregister(this);
    }

    private void ActivateQuest(int index)
    {
        if (index < 0 || index >= questList.Count) return;

        currentIndex = index;
        currentDef   = questList[index];

        activeObjectives = new List<QuestObjective>();
        foreach (var src in currentDef.objectives)
        {
            var obj = new QuestObjective
            {
                type             = src.type,
                targetItem       = src.targetItem,
                targetTechNodeId = src.targetTechNodeId,
                targetAmount     = src.targetAmount,
                currentAmount    = 0
            };

            // 이미 해금된 테크 soft-lock 방지
            if (obj.type == QuestObjectiveType.UpgradeTech
                && !string.IsNullOrEmpty(obj.targetTechNodeId)
                && TechTreeManager.Instance != null)
            {
                obj.currentAmount = Mathf.Min(TechTreeManager.Instance.GetLevel(obj.targetTechNodeId), obj.targetAmount);
            }
            if (obj.type == QuestObjectiveType.CoreLevel)
                obj.currentAmount = Mathf.Min(CenterCore.CurrentLevel, obj.targetAmount);
            if (obj.type == QuestObjectiveType.CropVarieties)
                obj.currentAmount = Mathf.Min(HarvestLog.Count, obj.targetAmount);
            if (obj.type == QuestObjectiveType.OrdersTotal)
                obj.currentAmount = Mathf.Min(ShuttleService.Trips, obj.targetAmount);
            if (obj.type == QuestObjectiveType.Residents)
                obj.currentAmount = Mathf.Min(ShuttleService.Population, obj.targetAmount);
            // 미리 해 둔 만큼 채움
            obj.currentAmount = Mathf.Max(obj.currentAmount, AlreadyDone(obj));

            activeObjectives.Add(obj);
        }

        OnQuestStarted?.Invoke(currentDef);
        NotifyAndCheck();
    }

    public void ForceCompleteCurrentQuest() => CompleteCurrentQuest();

    private void CompleteCurrentQuest()
    {
        if (currentDef == null) return;

        if (Inventory.Instance != null)
        {
            Vector3 dropPos = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform.position : Vector3.zero;
            foreach (var reward in currentDef.rewards)
            {
                if (reward.item == null) continue;
                Inventory.Instance.AddItemOrDrop(reward.item, reward.amount, dropPos);
                NetHost.GiveAll(reward.item, reward.amount);
            }
        }
        if (currentDef.rewardCoins > 0) CurrencyManager.Instance?.Add(currentDef.rewardCoins);

        var completed = currentDef;
        Sfx.Play(SfxId.QuestComplete);
        OnQuestCompleted?.Invoke(completed);

        int next = currentIndex + 1;
        if (next < questList.Count)
        {
            ActivateQuest(next);
        }
        else
        {
            currentDef = null;
            currentIndex = questList.Count;
            activeObjectives.Clear();
            OnQuestUpdated?.Invoke();
            OnAllQuestsDone?.Invoke();
        }
    }

    public void SyncFromHost(int questIndex, List<int> objectiveAmounts, bool allDone)
    {
        int shown = currentDef == null ? questList.Count : currentIndex;
        int next  = allDone || questIndex < 0 || questIndex >= questList.Count ? questList.Count : questIndex;
        bool sameAmounts = activeObjectives != null && objectiveAmounts != null && activeObjectives.Count == objectiveAmounts.Count;
        for (int i = 0; sameAmounts && i < activeObjectives.Count; i++)
            sameAmounts = activeObjectives[i].currentAmount == objectiveAmounts[i];
        if (next == shown && (sameAmounts || currentDef == null)) return;

        var finished = next > shown ? currentDef : null;
        RestoreFromSave(questIndex, objectiveAmounts, allDone);
        if (finished == null) return;

        Sfx.Play(SfxId.QuestComplete);
        OnQuestCompleted?.Invoke(finished);
        if (currentDef != null) OnQuestStarted?.Invoke(currentDef);
        else OnAllQuestsDone?.Invoke();
    }

    public void AddRemoteProgress(QuestObjectiveType type, FarmingItemData item, int amount)
    {
        if (type == QuestObjectiveType.CollectItem && item != null && item.isGolden) Codex.RecordGolden(item);
        switch (type)
        {
            case QuestObjectiveType.CollectItem: HandleItemAdded(item, amount);   break;
            case QuestObjectiveType.CraftItem:   HandleItemCrafted(item, amount); break;
            case QuestObjectiveType.SellItem:    HandleItemSold(item, amount);    break;
        }
    }

    public void RestoreFromSave(int questIndex, List<int> objectiveAmounts, bool allDone)
    {
        restoredFromSave = true;

        if (allDone || questIndex < 0 || questIndex >= questList.Count)
        {
            currentDef = null;
            currentIndex = questList.Count;
            activeObjectives.Clear();
            OnQuestUpdated?.Invoke();
            return;
        }

        currentIndex = questIndex;
        currentDef   = questList[questIndex];

        activeObjectives = new List<QuestObjective>();
        for (int i = 0; i < currentDef.objectives.Count; i++)
        {
            var src = currentDef.objectives[i];
            var obj = new QuestObjective
            {
                type             = src.type,
                targetItem       = src.targetItem,
                targetTechNodeId = src.targetTechNodeId,
                targetAmount     = src.targetAmount,
                currentAmount    = (i < objectiveAmounts?.Count ? objectiveAmounts[i] : 0)
            };

            if (obj.type == QuestObjectiveType.UpgradeTech
                && !string.IsNullOrEmpty(obj.targetTechNodeId)
                && TechTreeManager.Instance != null)
            {
                obj.currentAmount = Mathf.Max(obj.currentAmount,
                    Mathf.Min(TechTreeManager.Instance.GetLevel(obj.targetTechNodeId), obj.targetAmount));
            }
            if (!NetSession.IsReplica) obj.currentAmount = Mathf.Max(obj.currentAmount, AlreadyDone(obj));
            if (!NetSession.IsReplica && obj.type == QuestObjectiveType.CropVarieties)
                obj.currentAmount = Mathf.Max(obj.currentAmount, Mathf.Min(HarvestLog.Count, obj.targetAmount));
            if (!NetSession.IsReplica && obj.type == QuestObjectiveType.OrdersTotal)
                obj.currentAmount = Mathf.Max(obj.currentAmount, Mathf.Min(ShuttleService.Trips, obj.targetAmount));
            if (!NetSession.IsReplica && obj.type == QuestObjectiveType.Residents)
                obj.currentAmount = Mathf.Max(obj.currentAmount, Mathf.Min(ShuttleService.Population, obj.targetAmount));

            activeObjectives.Add(obj);
        }

        OnQuestUpdated?.Invoke();
    }

    private static int AlreadyDone(QuestObjective obj)
    {
        if (obj == null || obj.targetItem == null) return 0;
        int have = Inventory.Instance != null ? Inventory.Instance.GetCount(obj.targetItem) : 0;
        int n = obj.type switch
        {
            QuestObjectiveType.PlaceTile   => CountPlaced(obj.targetItem),
            QuestObjectiveType.CraftItem   => have + CountPlaced(obj.targetItem),
            QuestObjectiveType.CollectItem => have,
            _                              => 0,
        };
        return Mathf.Min(n, obj.targetAmount);
    }

    private static int CountPlaced(FarmingItemData item)
    {
        var gm = GridManager.Instance;
        if (gm == null || item == null || item.itemType != FarmingItemType.Tile) return 0;
        int n = 0;
        foreach (var t in gm.GetDynamicTiles())
            if (t != null && !t.IsGhost && t.SourceItem == item) n++;
        return n;
    }

    private void HandleItemAdded(FarmingItemData item, int amount)
    {
        // 참가자는 호스트로 전송
        if (NetSession.IsReplica) { if (!NetOps.Applying) NetOps.Quest(QuestObjectiveType.CollectItem, item, amount); return; }
        if (currentDef == null || item == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type != QuestObjectiveType.CollectItem) continue;
            if (obj.targetItem != item) continue;

            obj.currentAmount = Mathf.Min(obj.currentAmount + amount, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandleItemCrafted(FarmingItemData item, int amount)
    {
        if (NetSession.IsReplica) { NetOps.Quest(QuestObjectiveType.CraftItem, item, amount); return; }
        if (currentDef == null || item == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type != QuestObjectiveType.CraftItem) continue;
            if (obj.targetItem != item) continue;

            obj.currentAmount = Mathf.Min(obj.currentAmount + amount, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandleItemSold(FarmingItemData item, int amount)
    {
        if (NetSession.IsReplica) return;
        if (currentDef == null || item == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type != QuestObjectiveType.SellItem) continue;
            if (obj.targetItem != item) continue;

            obj.currentAmount = Mathf.Min(obj.currentAmount + amount, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandleTechUpgraded(TechNodeData node)
    {
        if (NetSession.IsReplica || currentDef == null || node == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type != QuestObjectiveType.UpgradeTech) continue;
            if (!string.IsNullOrEmpty(obj.targetTechNodeId) && obj.targetTechNodeId != node.id) continue;

            obj.currentAmount = Mathf.Min(obj.currentAmount + 1, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandlePlantHarvested(PlantTile plant, HarvestResult[] results)
    {
        if (NetSession.IsReplica || currentDef == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type == QuestObjectiveType.CropVarieties)
            {
                int v = Mathf.Min(HarvestLog.Count, obj.targetAmount);
                if (v > obj.currentAmount) { obj.currentAmount = v; changed = true; }
                continue;
            }
            if (obj.type != QuestObjectiveType.HarvestPlant) continue;
            if (obj.targetItem != null && !Contains(results, obj.targetItem)) continue;

            obj.currentAmount = Mathf.Min(obj.currentAmount + 1, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private static bool Contains(HarvestResult[] results, FarmingItemData item)
    {
        if (results == null) return false;
        foreach (var r in results)
            if (r != null && r.item == item) return true;
        return false;
    }

    private void HandleSeedPlanted(PlantTile plant, SeedData seed)
    {
        if (NetSession.IsReplica || currentDef == null || seed == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete || obj.type != QuestObjectiveType.PlantSeed) continue;
            if (obj.targetItem != null && obj.targetItem.seedData != seed) continue;
            obj.currentAmount = Mathf.Min(obj.currentAmount + 1, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandleShuttleArrived()
    {
        if (NetSession.IsReplica || currentDef == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type == QuestObjectiveType.CompleteOrder)
                obj.currentAmount = Mathf.Min(obj.currentAmount + 1, obj.targetAmount);
            else if (obj.type == QuestObjectiveType.OrdersTotal)
                obj.currentAmount = Mathf.Min(ShuttleService.Trips, obj.targetAmount);
            else if (obj.type == QuestObjectiveType.Residents)
                obj.currentAmount = Mathf.Min(ShuttleService.Population, obj.targetAmount);
            else continue;
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandleMachineHarvest(AutoHarvester harvester, PlantTile plant)
    {
        if (NetSession.IsReplica || currentDef == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete || obj.type != QuestObjectiveType.AutoHarvest) continue;
            obj.currentAmount = Mathf.Min(obj.currentAmount + 1, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void HandleCoreLevelUp(CenterCore core, int level)
    {
        if (NetSession.IsReplica) return;
        if (SyncCoreLevelObjectives()) NotifyAndCheck();
    }

    private bool SyncCoreLevelObjectives()
    {
        if (activeObjectives == null) return false;
        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.type != QuestObjectiveType.CoreLevel) continue;
            int value = Mathf.Min(CenterCore.CurrentLevel, obj.targetAmount);
            if (value <= obj.currentAmount) continue;
            obj.currentAmount = value;
            changed = true;
        }
        return changed;
    }

    private void HandleTilePlaced(TileItem tile)
    {
        if (NetSession.IsReplica || currentDef == null || tile == null) return;

        bool changed = false;
        foreach (var obj in activeObjectives)
        {
            if (obj.IsComplete) continue;
            if (obj.type != QuestObjectiveType.PlaceTile) continue;
            if (obj.targetItem != tile.SourceItem) continue;

            obj.currentAmount = Mathf.Min(obj.currentAmount + 1, obj.targetAmount);
            changed = true;
        }

        if (changed) NotifyAndCheck();
    }

    private void NotifyAndCheck()
    {
        OnQuestUpdated?.Invoke();

        foreach (var obj in activeObjectives)
            if (!obj.IsComplete) return;

        CompleteCurrentQuest();
    }
}
