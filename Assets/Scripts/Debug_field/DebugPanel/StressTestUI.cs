using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

public class StressTestUI : MonoBehaviour
{
    public static StressTestUI Instance => Services.Get<StressTestUI>();

    [SerializeField] private KeyCode toggleKey = KeyCode.F9;

    private RectTransform  panelRT;
    private CanvasGroup    panelCG;
    private bool           isOpen = false;
    private Coroutine      animCo;

    private TextMeshProUGUI lblFPS;
    private TextMeshProUGUI lblMemory;
    private TextMeshProUGUI lblCounts;

    private TextMeshProUGUI lblEnergyResult;

    private float  fpsTimer;
    private int    fpsFrameCount;
    private float  fpsDisplay;
    private const float FPS_UPDATE_INTERVAL = 0.5f;

    private int spawnCount = 20;

    private int benchPlants    = 50;
    private int benchRelays = 20;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
    }

    private void OnDestroy() => Services.Unregister(this);

    private void Start()
    {
        Build();
        panelRT.localScale = Vector3.zero;
    }

    private void Update()
    {
        if (GameInput.GetKeyDown(toggleKey)) Toggle();
        if (isOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Toggle();
        if (isOpen) UpdateStats();
    }

    public void Toggle()
    {
        isOpen = !isOpen;
        if (isOpen) transform.SetAsLastSibling();
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Anim(isOpen));
        if (isOpen) panelCG.blocksRaycasts = panelCG.interactable = true;
        else        panelCG.blocksRaycasts = panelCG.interactable = false;
    }

    private IEnumerator Anim(bool opening)
    {
        float dur = 0.12f, elapsed = 0f;
        float from = opening ? 0f : 1f, to = opening ? 1f : 0f;
        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            panelRT.localScale = Vector3.one * Mathf.Lerp(from, to, t * t * (3f - 2f * t));
            yield return null;
        }
        panelRT.localScale = Vector3.one * to;
        if (!opening) panelCG.blocksRaycasts = panelCG.interactable = false;
    }

    private void UpdateStats()
    {
        fpsFrameCount++;
        fpsTimer += Time.unscaledDeltaTime;
        bool refreshCounts = string.IsNullOrEmpty(lblCounts.text);
        if (fpsTimer >= FPS_UPDATE_INTERVAL)
        {
            fpsDisplay    = fpsFrameCount / fpsTimer;
            fpsTimer      = 0f;
            fpsFrameCount = 0;
            refreshCounts = true;
        }

        float ms    = Time.unscaledDeltaTime * 1000f;
        float memMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);

        lblFPS.text    = $"FPS: <color=#3F8A66>{fpsDisplay:F0}</color>   프레임: <color=#6E57B5>{ms:F1}ms</color>";
        lblMemory.text = $"메모리: <color=#3F78AE>{memMB:F1} MB</color>";

        // 씬 검색이라 0.5초마다만
        if (!refreshCounts) return;
        int invUsed    = CountInventoryUsed();
        int blockCount = ResourceBlock.Active.Count;
        int plantCount = UnityEngine.Object.FindObjectsByType<PlantTile>(FindObjectsSortMode.None).Length;
        int relayCount = UnityEngine.Object.FindObjectsByType<PowerRelay>(FindObjectsSortMode.None).Length;
        int genCount   = EnergyManager.Instance != null ? EnergyManager.Instance.Generators.Count : 0;

        lblCounts.text = $"인벤 사용: {invUsed}슬롯  자원블록: {blockCount}  " +
                         $"식물: {plantCount}  중계탑: {relayCount}  발전기: {genCount}";
    }

    private int CountInventoryUsed()
    {
        var inv = Inventory.Instance;
        if (inv == null) return 0;
        int n = 0;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var s = inv.GetSlot(i);
            if (s != null && !s.IsEmpty) n++;
        }
        return n;
    }

    private void GiveAllItems()
    {
        var inv = Inventory.Instance;
        if (inv == null) { Log("Inventory.Instance 없음"); return; }

        var items = Resources.FindObjectsOfTypeAll<FarmingItemData>();
        int added = 0;
        foreach (var item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.itemName)) continue;
            if (inv.AddItem(item, 1)) added++;
        }
        Log($"아이템 {added}종 지급 완료 (전체 {items.Length}종)");
    }

    private void ClearInventory()
    {
        Inventory.Instance?.ClearAll();
        Log("인벤토리 초기화 완료");
    }

    private void SpamSingleItem()
    {
        var inv = Inventory.Instance;
        if (inv == null) return;

        var items = Resources.FindObjectsOfTypeAll<FarmingItemData>();
        if (items.Length == 0) { Log("아이템 에셋 없음"); return; }

        var target = items[0];
        int added = 0;
        while (inv.AddItem(target, 1)) added++;
        Log($"'{target.itemName}' {added}개 스팸 완료 (인벤 가득)");
    }

    private void SpawnResourceBlocks()
    {
        var resList = Resources.FindObjectsOfTypeAll<ResourceData>();
        if (resList.Length == 0) { Log("ResourceData 에셋 없음"); return; }

        Transform player = PlayerTileController.Instance?.transform;
        Vector2 center   = player != null ? (Vector2)player.position : Vector2.zero;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < spawnCount; i++)
        {
            var res = resList[i % resList.Length];
            var pos = center + UnityEngine.Random.insideUnitCircle * 12f;
            ResourceBlock.CreateSaved(res, pos, player, res.maxResources);
        }
        sw.Stop();
        Log($"자원 블록 {spawnCount}개 생성  ({sw.ElapsedMilliseconds}ms)");
    }

    private void DestroyAllResourceBlocks()
    {
        var blocks = UnityEngine.Object.FindObjectsByType<ResourceBlock>(FindObjectsSortMode.None);
        foreach (var b in blocks) UnityEngine.Object.Destroy(b.gameObject);
        Log($"자원 블록 {blocks.Length}개 제거");
    }

    private void RunEnergyStressTest()
    {
        var em = EnergyManager.Instance;
        if (em == null) { Log("EnergyManager.Instance 없음"); return; }

        float ms = em.BenchmarkRefresh(benchPlants, benchRelays);

        int realPlants    = em.PlantCount;
        int realRelays    = em.RelayCount;

        lblEnergyResult.text =
            $"더미  식물 <color=#3F8A66>{benchPlants}</color>  중계탑 <color=#3F78AE>{benchRelays}</color>\n" +
            $"연산: <color=#6E57B5>{ms:F3}ms</color>   " +
            $"실제 등록: 식물 {realPlants} / 중계탑 {realRelays}";

        Log($"EnergyBenchmark P={benchPlants} R={benchRelays} → {ms:F3}ms");
    }

    private void ResetPlayerPrefs()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();

        GameSettings.ReapplyAll();

        Log("PlayerPrefs 초기화 완료 (옵션 · 튜토리얼 등)");
    }

    private static void Log(string msg)
    {
        NotificationUI.Instance?.Show(msg);
        UnityEngine.Debug.Log($"[StressTest] {msg}");
    }

    private void Build()
    {
        var canvas = GetComponentInParent<Canvas>();

        panelRT = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
        if (canvas != null) panelRT.SetParent(canvas.transform, false);
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(1f, 1f);
        panelRT.anchoredPosition = new Vector2(-12f, -92f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT, catEars: false);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(14, 14, 10, 14);
        vlg.spacing = 6f;
        vlg.childAlignment     = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;

        var csf = gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var titleRow = new GameObject("Title", typeof(RectTransform));
        titleRow.transform.SetParent(transform, false);
        var titleHlg = titleRow.AddComponent<HorizontalLayoutGroup>();
        titleHlg.childAlignment        = TextAnchor.MiddleCenter;
        titleHlg.childControlWidth     = titleHlg.childControlHeight     = true;
        titleHlg.childForceExpandWidth = titleHlg.childForceExpandHeight = false;
        CuteUIKit.TitleRibbon(titleRow.transform, "부하 테스트", toggleKey.ToString(), 14f);
        MakeSeparator();

        MakeSection("성능 모니터", CuteUIKit.SkyTag, CuteUIKit.SkyText);
        lblFPS     = MakeLabel("FPS: -", 10f, CuteUIKit.Cocoa);
        lblMemory  = MakeLabel("메모리: -", 10f, CuteUIKit.Cocoa);
        lblCounts  = MakeLabel("오브젝트: -", 10f, CuteUIKit.Cocoa);
        MakeSeparator();

        MakeSection("아이템", CuteUIKit.MintTag, CuteUIKit.MintText);
        MakeButtonRow(new[]
        {
            ("전체 아이템 +1", CuteUIKit.ButtonMint,  (System.Action)GiveAllItems),
            ("단일 스팸",      CuteUIKit.ButtonSky,   (System.Action)SpamSingleItem),
            ("인벤 초기화",    CuteUIKit.ButtonCoral, (System.Action)ClearInventory),
        });
        MakeSeparator();

        MakeSection("자원 블록", CuteUIKit.LavenderTag, CuteUIKit.LavenderText);

        var countRow = MakeHRow();
        MakeLabelIn(countRow, "생성 수:", 10f, CuteUIKit.Cocoa);
        MakeIntAdjuster(countRow, () => spawnCount, v => spawnCount = v, 5, 5, 200);

        MakeButtonRow(new[]
        {
            ("블록 생성",   CuteUIKit.ButtonSky,   (System.Action)SpawnResourceBlocks),
            ("전체 제거",   CuteUIKit.ButtonCoral, (System.Action)DestroyAllResourceBlocks),
        });
        MakeSeparator();

        MakeSection("에너지 네트워크", CuteUIKit.ButterSoft, CuteUIKit.TextOn(CuteUIKit.ButterSoft), "더미 시뮬레이션");

        var pRow = MakeHRow();
        MakeLabelIn(pRow, "식물 수:", 10f, CuteUIKit.Cocoa);
        MakeIntAdjuster(pRow, () => benchPlants, v => benchPlants = v, 10, 10, 2000);

        var bRow = MakeHRow();
        MakeLabelIn(bRow, "중계탑 수:", 10f, CuteUIKit.Cocoa);
        MakeIntAdjuster(bRow, () => benchRelays, v => benchRelays = v, 5, 5, 500);

        MakeButtonRow(new[]
        {
            ("측정 실행", CuteUIKit.ButtonButter, (System.Action)RunEnergyStressTest),
        });

        lblEnergyResult = MakeLabel("결과: -", 10f, CuteUIKit.Cocoa);
        MakeSeparator();

        MakeSection("세이브 데이터", CuteUIKit.ButtonCoral, CuteUIKit.Hex(0xC9485B));
        MakeButtonRow(new[]
        {
            ("PlayerPrefs 초기화", CuteUIKit.ButtonCoral, (System.Action)ResetPlayerPrefs),
        });
        MakeSeparator();

        var closeBtn = MakeButton("닫기", CuteUIKit.ButtonLatte);
        closeBtn.onClick.AddListener(Toggle);
    }

    private TextMeshProUGUI MakeLabel(string text, float size, Color color)
    {
        var go  = new GameObject("Lbl", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.raycastTarget = false;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        go.AddComponent<LayoutElement>().minWidth = 300f;
        return tmp;
    }

    private void MakeSeparator() => CuteUIKit.DottedDivider(transform);

    private void MakeSection(string label, Color tagColor, Color textColor, string note = null)
        => CuteUIKit.SectionHeader(transform, label, tagColor, textColor, note);

    private GameObject MakeHRow()
    {
        var go = new GameObject("HRow", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f; le.preferredHeight = 24f;
        return go;
    }

    private void MakeLabelIn(GameObject parent, string text, float size, Color color)
    {
        var go  = new GameObject("Lbl", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.raycastTarget = false;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void MakeIntAdjuster(GameObject parent,
        System.Func<int> getter, System.Action<int> setter,
        int step, int min, int max)
    {
        var btnMinus = MakeSmallButtonIn(parent, "-", CuteUIKit.ButtonCoral, 24f);
        var valGO  = new GameObject("Val", typeof(RectTransform));
        valGO.transform.SetParent(parent.transform, false);
        var valTmp = valGO.AddComponent<TextMeshProUGUI>();
        valTmp.text      = getter().ToString();
        valTmp.fontSize  = 11f;
        valTmp.fontStyle = FontStyles.Bold;
        valTmp.alignment = TextAlignmentOptions.Center;
        valTmp.color     = CuteUIKit.Cocoa;
        valTmp.raycastTarget = false;
        var valLE = valGO.AddComponent<LayoutElement>();
        valLE.preferredWidth = 44f;
        var btnPlus = MakeSmallButtonIn(parent, "+", CuteUIKit.ButtonMint, 24f);

        btnMinus.onClick.AddListener(() =>
        {
            setter(Mathf.Max(min, getter() - step));
            valTmp.text = getter().ToString();
        });
        btnPlus.onClick.AddListener(() =>
        {
            setter(Mathf.Min(max, getter() + step));
            valTmp.text = getter().ToString();
        });
    }

    private void MakeButtonRow((string label, Color color, System.Action action)[] buttons)
    {
        var row = MakeHRow();
        foreach (var (label, color, action) in buttons)
        {
            var btn = MakeSmallButtonIn(row, label, color);
            btn.onClick.AddListener(() => action());
            var le = btn.gameObject.GetComponent<LayoutElement>() ?? btn.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }
    }

    private Button MakeSmallButtonIn(GameObject parent, string label, Color bgColor, float width = 68f)
        => CuteUIKit.Button(parent.transform, label, bgColor, null, width, 24f, 10f);

    private Button MakeButton(string label, Color bgColor)
        => CuteUIKit.Button(transform, label, bgColor, null, -1f, 28f, 11f);
}
