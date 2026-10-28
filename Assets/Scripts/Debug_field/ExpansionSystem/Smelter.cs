using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Smelter : TileItem
{
    [Header("제련 (화로)")]
    [Tooltip("주괴 한 번(광석 한 묶음)을 녹이는 데 걸리는 시간")]
    [SerializeField, Min(0.1f)] private float secondsPerBatch = 3f;
    [Tooltip("넣는 칸에 담을 수 있는 광석 수")]
    [SerializeField, Min(1)] private int inputCapacity  = 100;
    [Tooltip("꺼내는 칸에 쌓일 수 있는 주괴 수")]
    [SerializeField, Min(1)] private int outputCapacity = 100;
    [Tooltip("녹이는 동안 쓰는 전기")]
    [SerializeField, Min(0f)] private float powerUse = 1f;

    [Header("연결 창고")]
    [Tooltip("연결된 창고와 광석 · 주괴를 주고받는 주기 (초)")]
    [SerializeField, Min(0.2f)] private float transferInterval = 2f;
    [Tooltip("한 번에 창고에서 가져오는 최대 광석 수")]
    [SerializeField, Min(1)] private int pullPerTransfer = 20;

    [Header("모양")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite workingSprite;
    [SerializeField] private float  glowDuration = 1.5f;

    [Header("실시간 상태")]
    [SerializeField] private FarmingItemData inputItem;
    [SerializeField] private int             inputCount;
    [SerializeField] private FarmingItemData outputItem;
    [SerializeField] private int             outputCount;
    [SerializeField] private float           progress;
    [SerializeField] private int             smeltedCount;

    private static readonly Color MoltenBeamColor = new Color(1f, 0.55f, 0.18f, 1f);

    public override string AbilityText => $"광석을 넣으면 {secondsPerBatch:0.#}초마다 주괴로 녹여요 (전기 필요)\n곁에 있으면 제작창에서 합금을 만들 수 있어요";
    public override float  WorkRate    => 1f / secondsPerBatch;

    private PowerLink power;
    private FlashBeam flashBeam;
    private float     glowUntil;
    private bool      isWorking;
    private bool      glowing;

    private FarmingItemData recipeKey;
    // 광석 바뀔 때만 다시 찾음
    private CraftingRecipe  recipeCache;

    private static readonly List<FarmingItemData> stockBuffer = new();

    public FarmingItemData InputItem      => inputItem;
    public int             InputCount     => inputCount;
    public FarmingItemData OutputItem     => outputItem;
    public int             OutputCount    => outputCount;
    public int             InputCapacity  => inputCapacity;
    public int             OutputCapacity => outputCapacity;
    public float           SecondsPerBatch => secondsPerBatch;
    public float           Progress       => progress;
    public float           Progress01     => Mathf.Clamp01(progress / secondsPerBatch);
    public bool            IsPowered      => power != null && power.IsPowered;
    public bool            IsWorking      => isWorking;

    public event System.Action OnChanged;

    public CraftingRecipe CurrentRecipe
    {
        get
        {
            if (recipeKey != inputItem)
            {
                recipeKey   = inputItem;
                recipeCache = RecipeFor(inputItem);
            }
            return recipeCache;
        }
    }

    public static CraftingRecipe RecipeFor(FarmingItemData ore)
    {
        var cm = CraftingManager.Instance;
        if (cm == null || ore == null) return null;
        foreach (var r in cm.Recipes)
            if (r != null && r.IsFurnaceRecipe && r.ingredients[0].item == ore && cm.IsTierUnlocked(r.tier))
                return r;
        return null;
    }

    private void Start()
    {
        SetGlow(false);
        if (IsGhost) return;

        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Generic);
        power     = PowerLink.Attach(this, PowerLink.Kind.Consumer);
        if (power != null) power.OnPowerChanged += NotifyChanged;
        if (!NetSession.IsReplica) StartCoroutine(TransferLoop());
        LinkRules.HintOnPlace(this);
    }

    private void Update()
    {
        if (IsGhost || NetSession.IsReplica) return;

        bool working = TickSmelting(Time.deltaTime);
        if (working != isWorking)
        {
            isWorking = working;
            OnChanged?.Invoke();
        }
        SetGlow(isWorking || Time.time < glowUntil);
    }

    private void NotifyChanged() => OnChanged?.Invoke();

    private bool TickSmelting(float dt)
    {
        var recipe = CurrentRecipe;
        if (recipe == null || inputCount < recipe.ingredients[0].amount)
        {
            progress = 0f;
            return false;
        }
        if (outputItem != null && outputItem != recipe.outputItem) return false;
        if (outputCount + recipe.outputAmount > outputCapacity) return false;
        if (!IsPowered) return false;

        progress += dt * CivFacility.WorkSpeedFor(this) * TechTreeManager.ProcessSpeed;
        EnergyManager.DrawPower(power.Source, powerUse * dt);
        if (progress < secondsPerBatch) return true;

        progress -= secondsPerBatch;
        inputCount -= recipe.ingredients[0].amount;
        if (inputCount <= 0)
        {
            inputCount = 0;
            inputItem  = null;
            progress   = 0f;
        }
        outputItem   = recipe.outputItem;
        outputCount += recipe.outputAmount;
        smeltedCount++;
        OnChanged?.Invoke();
        return true;
    }

    public int BatchSize => CurrentRecipe != null ? Mathf.Max(1, CurrentRecipe.ingredients[0].amount) : 1;

    public bool HasLeftover => inputItem != null && inputCount > 0 && inputCount < BatchSize;

    public bool CanInsert(FarmingItemData ore)
        => ore != null && (inputItem == null || inputItem == ore) && inputCount < inputCapacity && RecipeFor(ore) != null;

    public int InsertOre(FarmingItemData ore, int amount)
    {
        if (amount <= 0 || !CanInsert(ore)) return 0;
        int n = Mathf.Min(amount, inputCapacity - inputCount);
        if (inputItem != ore) progress = 0f;
        inputItem   = ore;
        inputCount += n;
        OnChanged?.Invoke();
        NetOps.Tile(NetOpType.SmelterInsert, this, ore, n);
        return n;
    }

    public int TakeInput(int amount, out FarmingItemData item)
    {
        item = inputItem;
        int n = Mathf.Min(amount, inputCount);
        if (item == null || n <= 0) return 0;
        inputCount -= n;
        if (inputCount <= 0)
        {
            inputCount = 0;
            inputItem  = null;
            progress   = 0f;
        }
        OnChanged?.Invoke();
        NetOps.Tile(NetOpType.SmelterTakeInput, this, item, n);
        return n;
    }

    public int TakeOutput(int amount, out FarmingItemData item)
    {
        item = outputItem;
        int n = Mathf.Min(amount, outputCount);
        if (item == null || n <= 0) return 0;
        outputCount -= n;
        if (outputCount <= 0)
        {
            outputCount = 0;
            outputItem  = null;
        }
        OnChanged?.Invoke();
        NetOps.Tile(NetOpType.SmelterTakeOutput, this, item, n);
        return n;
    }

    public void RestoreFromSave(FarmingItemData input, int inCount, FarmingItemData output, int outCount, float savedProgress)
    {
        inputItem   = inCount > 0 ? input : null;
        inputCount  = inputItem != null ? Mathf.Clamp(inCount, 0, inputCapacity) : 0;
        outputItem  = outCount > 0 ? output : null;
        outputCount = outputItem != null ? Mathf.Max(0, outCount) : 0;
        progress    = inputItem != null ? Mathf.Clamp(savedProgress, 0f, secondsPerBatch) : 0f;
        OnChanged?.Invoke();
    }

    protected internal override IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops()
    {
        if (inputItem  != null && inputCount  > 0) yield return (inputItem,  inputCount);
        if (outputItem != null && outputCount > 0) yield return (outputItem, outputCount);
    }

    private IEnumerator TransferLoop()
    {
        var wait = new WaitForSeconds(transferInterval);
        while (true)
        {
            yield return wait;
            PushOutput();
            PullInput();
        }
    }

    private void PushOutput()
    {
        for (int i = 0; i < Links.Count && outputCount > 0; i++)
        {
            if (Links[i] is not ItemStorage storage || !storage.Accepts(outputItem)) continue;
            int added = storage.AddItem(outputItem, outputCount);
            if (added <= 0) continue;
            TakeOutput(added, out _);
            flashBeam?.Flash(transform.position, storage.transform.position, MoltenBeamColor, 0.05f, 0.4f);
        }
    }

    private void PullInput()
    {
        ReturnLeftover();
        int budget = Mathf.Min(pullPerTransfer, inputCapacity - inputCount);
        for (int i = 0; i < Links.Count && budget > 0; i++)
        {
            if (Links[i] is not ItemStorage storage) continue;

            stockBuffer.Clear();
            stockBuffer.AddRange(storage.StockedItems);
            foreach (var item in stockBuffer)
            {
                if (budget <= 0) break;
                if (!CanInsert(item)) continue;
                int taken = storage.RemoveItem(item, Mathf.Min(budget, storage.GetCount(item)));
                if (taken <= 0) continue;
                int inserted = InsertOre(item, taken);
                if (inserted < taken) storage.AddItem(item, taken - inserted);
                budget -= inserted;
                if (inserted > 0)
                    flashBeam?.Flash(storage.transform.position, transform.position, MoltenBeamColor, 0.05f, 0.4f);
            }
        }
    }

    private void ReturnLeftover()
    {
        if (!HasLeftover) return;
        for (int i = 0; i < Links.Count; i++)
            if (Links[i] is ItemStorage s && s.GetCount(inputItem) > 0) return;
        for (int i = 0; i < Links.Count; i++)
        {
            if (Links[i] is not ItemStorage storage || !storage.Accepts(inputItem)) continue;
            var ore = inputItem;
            int added = storage.AddItem(ore, inputCount);
            if (added <= 0) continue;
            TakeInput(added, out _);
            flashBeam?.Flash(transform.position, storage.transform.position, MoltenBeamColor, 0.05f, 0.4f);
            return;
        }
    }

    public void Glow() => glowUntil = Time.time + glowDuration;

    private void SetGlow(bool on)
    {
        if (spriteRenderer == null || (on == glowing && spriteRenderer.sprite != null)) return;
        glowing = on;
        var sprite = on ? workingSprite : idleSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }
}
