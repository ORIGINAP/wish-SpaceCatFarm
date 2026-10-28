using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CatKitchen : TileItem
{
    [Header("요리")]
    [Tooltip("기본 요리 시간(초)")]
    [SerializeField, Min(0.1f)] private float defaultSeconds = 10f;
    [Tooltip("설비 빠르기 배율 (고급 주방은 더 크게)")]
    [SerializeField, Min(0.1f)] private float speed = 1f;
    [Tooltip("재료를 몇 번 요리할 만큼까지 담을 수 있는지")]
    [SerializeField, Min(1)] private int batchCapacity = 10;
    [Tooltip("꺼내는 칸에 쌓일 수 있는 요리 수")]
    [SerializeField, Min(1)] private int outputCapacity = 60;
    [Tooltip("요리하는 동안 쓰는 전기")]
    [SerializeField, Min(0f)] private float powerUse = 0.6f;

    [Header("연결 창고")]
    [SerializeField, Min(0.2f)] private float transferInterval = 2f;

    [Header("모양")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite workingSprite;

    private static readonly Color CookBeamColor = new Color(1f, 0.72f, 0.45f, 1f);

    private CraftingRecipe recipe;
    private readonly Dictionary<FarmingItemData, int> inputs = new();
    private FarmingItemData outputItem;
    private int   outputCount;
    private float progress;
    private float pullPausedUntil;
    private bool  isWorking;
    private bool  shownWorking;

    private PowerLink power;
    private FlashBeam flashBeam;
    private static readonly List<FarmingItemData> stockBuffer = new();
    private static List<CraftingRecipe> menuCache;

    public override string AbilityText =>
        $"메뉴를 고르고 재료를 넣으면 시간이 지나 요리가 돼요 (전기 필요)\n창고와 이으면 재료를 가져오고 요리를 돌려놔요";
    public override float WorkRate => speed;

    public event System.Action OnChanged;

    public CraftingRecipe   Recipe        => recipe;
    public FarmingItemData  OutputItem    => outputItem;
    public int              OutputCount   => outputCount;
    public int              OutputCapacity => outputCapacity;
    public int              BatchCapacity => batchCapacity;
    public bool             IsPowered     => power != null && power.IsPowered;
    public bool             IsWorking     => isWorking;
    public float            Progress01    => recipe != null ? Mathf.Clamp01(progress / SecondsPerDish) : 0f;
    public float            Progress      => progress;
    public float SecondsPerDish => (recipe != null && recipe.craftSeconds > 0f ? recipe.craftSeconds : defaultSeconds)
                                   / (speed * CivFacility.WorkSpeedFor(this) * TechTreeManager.ProcessSpeed);

    public int  GetInput(FarmingItemData item) => item != null && inputs.TryGetValue(item, out int n) ? n : 0;
    public int  TotalInputs { get { int t = 0; foreach (var v in inputs.Values) t += v; return t; } }
    public IEnumerable<FarmingItemData> InputItems => inputs.Keys;

    public bool CanChangeRecipe => TotalInputs == 0;

    public static List<CraftingRecipe> Menu()
    {
        if (menuCache != null) return menuCache;
        var cm = CraftingManager.Instance;
        if (cm == null) return new List<CraftingRecipe>();
        menuCache = new List<CraftingRecipe>();
        foreach (var r in cm.Recipes)
            if (r != null && r.IsKitchenRecipe && !menuCache.Contains(r)) menuCache.Add(r);
        menuCache.Sort((a, b) => a.requiredCoreLevel != b.requiredCoreLevel
            ? a.requiredCoreLevel.CompareTo(b.requiredCoreLevel)
            : a.outputItem.basePrice.CompareTo(b.outputItem.basePrice));
        return menuCache;
    }

    public static bool IsUnlocked(CraftingRecipe r)
        => r != null && CraftingManager.Instance != null && CraftingManager.Instance.IsRecipeUnlocked(r);

    public static CraftingRecipe FindRecipe(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var r in Menu())
            if (r.name == name) return r;
        return null;
    }

    private void Start()
    {
        ApplyLook(false);
        if (IsGhost) return;
        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Generic);
        power     = PowerLink.Attach(this, PowerLink.Kind.Consumer);
        if (power != null) power.OnPowerChanged += Notify;
        if (!NetSession.IsReplica) StartCoroutine(TransferLoop());
        LinkRules.HintOnPlace(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => menuCache = null;

    private void Update()
    {
        if (IsGhost) return;
        // 참가자는 표시용으로만 판정
        bool working = NetSession.IsReplica ? CanCookNow() : Tick(Time.deltaTime);
        if (working != isWorking) { isWorking = working; Notify(); }
        ApplyLook(isWorking);
    }

    private bool CanCookNow()
        => recipe != null && HasBatch()
           && (outputItem == null || outputItem == recipe.outputItem)
           && outputCount + recipe.outputAmount <= outputCapacity
           && IsPowered;

    private void Notify() => OnChanged?.Invoke();

    private bool Tick(float dt)
    {
        if (!CanCookNow()) return false;

        progress += dt;
        EnergyManager.DrawPower(power.Source, powerUse * dt);
        if (progress < SecondsPerDish) return true;

        progress = 0f;
        foreach (var ing in recipe.ingredients)
            if (ing.item != null) RemoveInput(ing.item, ing.amount);
        outputItem   = recipe.outputItem;
        outputCount += recipe.outputAmount;
        CraftingManager.Instance?.NotifyMachineCrafted(recipe.outputItem, recipe.outputAmount);
        Codex.RecordDish(recipe.outputItem);
        Sfx.PlayAt(SfxId.Craft, transform.position, 0.5f);
        Notify();
        return true;
    }

    public bool HasBatch()
    {
        if (recipe == null || recipe.ingredients == null) return false;
        foreach (var ing in recipe.ingredients)
            if (ing.item == null || GetInput(ing.item) < ing.amount) return false;
        return true;
    }

    public bool SetRecipe(CraftingRecipe r)
    {
        if (r == recipe) return true;
        if (!CanChangeRecipe) return false;
        if (r != null && !r.IsKitchenRecipe) return false;
        recipe   = r;
        progress = 0f;
        Notify();
        NetOps.TileNamed(NetOpType.KitchenSetRecipe, this, r != null ? r.name : "", 0);
        return true;
    }

    public int RoomFor(FarmingItemData item)
    {
        if (recipe == null || item == null) return 0;
        foreach (var ing in recipe.ingredients)
            if (ing.item == item) return Mathf.Max(0, ing.amount * batchCapacity - GetInput(item));
        return 0;
    }

    public int InsertIngredient(FarmingItemData item, int amount)
    {
        int n = Mathf.Min(amount, RoomFor(item));
        if (n <= 0) return 0;
        inputs[item] = GetInput(item) + n;
        Notify();
        NetOps.Tile(NetOpType.KitchenInsert, this, item, n);
        return n;
    }

    public int TakeInput(FarmingItemData item, int amount)
    {
        int n = Mathf.Min(amount, GetInput(item));
        if (n <= 0) return 0;
        RemoveInput(item, n);
        if (TotalInputs == 0) progress = 0f;
        pullPausedUntil = Time.time + 8f;
        Notify();
        NetOps.Tile(NetOpType.KitchenTakeInput, this, item, n);
        return n;
    }

    public int TakeOutput(int amount, out FarmingItemData item)
    {
        item = outputItem;
        int n = Mathf.Min(amount, outputCount);
        if (item == null || n <= 0) return 0;
        outputCount -= n;
        if (outputCount <= 0) { outputCount = 0; outputItem = null; }
        Notify();
        NetOps.Tile(NetOpType.KitchenTakeOutput, this, item, n);
        return n;
    }

    private void RemoveInput(FarmingItemData item, int n)
    {
        int left = GetInput(item) - n;
        if (left > 0) inputs[item] = left;
        else inputs.Remove(item);
    }

    public void RestoreFromSave(CraftingRecipe savedRecipe, List<ItemSlotSave> savedInputs, FarmingItemData output, int outCount, float savedProgress)
    {
        recipe = savedRecipe;
        inputs.Clear();
        if (savedInputs != null)
            foreach (var s in savedInputs)
            {
                var item = NetWorld.Item(s.itemName);
                if (item != null && s.count > 0) inputs[item] = GetInput(item) + s.count;
            }
        outputItem  = outCount > 0 ? output : null;
        outputCount = outputItem != null ? outCount : 0;
        progress    = recipe != null ? Mathf.Max(0f, savedProgress) : 0f;
        Notify();
    }

    protected internal override IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops()
    {
        foreach (var pair in new List<KeyValuePair<FarmingItemData, int>>(inputs))
            if (pair.Value > 0) yield return (pair.Key, pair.Value);
        if (outputItem != null && outputCount > 0) yield return (outputItem, outputCount);
    }

    private IEnumerator TransferLoop()
    {
        var wait = new WaitForSeconds(transferInterval);
        while (true)
        {
            yield return wait;
            PushOutput();
            PullInputs();
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
            flashBeam?.Flash(transform.position, storage.transform.position, CookBeamColor, 0.05f, 0.4f);
        }
    }

    private void PullInputs()
    {
        if (recipe == null || Time.time < pullPausedUntil) return;
        foreach (var ing in recipe.ingredients)
        {
            if (ing.item == null) continue;
            for (int i = 0; i < Links.Count; i++)
            {
                int room = RoomFor(ing.item);
                if (room <= 0) break;
                if (Links[i] is not ItemStorage storage) continue;
                int have = storage.GetCount(ing.item);
                if (have <= 0) continue;
                int taken = storage.RemoveItem(ing.item, Mathf.Min(room, have));
                if (taken <= 0) continue;
                int put = InsertIngredient(ing.item, taken);
                if (put < taken) storage.AddItem(ing.item, taken - put);
                if (put > 0) flashBeam?.Flash(storage.transform.position, transform.position, CookBeamColor, 0.05f, 0.4f);
            }
        }
    }

    private void ApplyLook(bool working)
    {
        if (spriteRenderer == null || (working == shownWorking && spriteRenderer.sprite != null)) return;
        shownWorking = working;
        var sprite = working ? workingSprite : idleSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }
}
