using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class DevPanelUI : MonoBehaviour
{
    // 출시 때 isDebugBuild로 되돌리기
    public static bool Enabled => true;

    public const float PanelTop = -150f;

    private const float PanelWidth   = 400f;
    private const float AnimDuration = 0.22f;
    private const float ShownX       = -20f;
    private const float HiddenX      = PanelWidth + 80f;

    private static readonly Color   ToggleColor = CuteUIKit.Hex(0xA58BE8);
    private static readonly float[] Speeds      = { 1f, 2f, 5f, 10f };
    private static readonly int[]   GiveAmounts = { 1, 10, 100 };
    private static readonly (string label, FarmingItemType? type)[] Categories =
    {
        ("전체", null), ("설치물", FarmingItemType.Tile), ("씨앗", FarmingItemType.Seed),
        ("자원", FarmingItemType.Resource), ("도구", FarmingItemType.Tool), ("장비", FarmingItemType.Equipment),
    };

    private static DevPanelUI current;

    public static bool AnyOpen => current != null && current.isOpen;

    public static Transform PanelRoot => current != null ? current.panelRT : null;

    public static Transform ToggleButton => current != null ? current.toggleButton : null;

    private TMP_FontAsset   font;
    private Transform       toggleButton;
    private RectTransform   panelRT;
    private CanvasGroup     panelCG;
    private bool            isOpen;
    private Coroutine       animCo;
    private GameObject[]    pages;
    private Button[]        tabButtons;
    private TextMeshProUGUI status;
    private TextMeshProUGUI stormNote;
    private float           nextStatusAt;
    private readonly List<Button> speedButtons = new();

    private sealed class Confirm { public TextMeshProUGUI label; public string text; public float until; }
    private readonly List<Confirm> confirms = new();

    private RectTransform    itemContent;
    private TMP_InputField   search;
    private Button[]         categoryButtons;
    private FarmingItemType? category;
    private readonly List<(FarmingItemData item, GameObject row)> itemRows = new();

    public static void Create(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject("DevPanelUI", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var ui = go.AddComponent<DevPanelUI>();
        ui.font = font;
        ui.Build();
    }

    private void Awake() => current = this;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    private void OnDestroy()
    {
        if (current != this) return;
        current = null;
        // 배속 바꾼 채 나가도 복구
        Time.timeScale = 1f;
    }

    private void Update()
    {
        if (isOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Toggle();
        if (search != null && search.isFocused) TextInputFocus.MarkTyping();

        foreach (var c in confirms)
            if (c.until > 0f && Time.unscaledTime > c.until) Disarm(c);

        if (isOpen && Time.unscaledTime >= nextStatusAt)
        {
            nextStatusAt = Time.unscaledTime + 0.25f;
            RefreshStatus();
        }
    }

    private void Build()
    {
        toggleButton = HelpUI.CornerButton(transform, "DevToggleButton", "개발", new Vector2(-30.5f, -93f), 44f, ToggleColor, 14f,
                                           font, Toggle).transform;

        var panelGO = new GameObject("DevPanel", typeof(RectTransform));
        panelGO.transform.SetParent(transform, false);
        panelRT = (RectTransform)panelGO.transform;
        panelRT.anchorMin = new Vector2(1f, 0f);
        panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot     = new Vector2(1f, 0.5f);
        panelRT.sizeDelta = new Vector2(PanelWidth, 0f);
        panelRT.offsetMin = new Vector2(panelRT.offsetMin.x, 24f);
        panelRT.offsetMax = new Vector2(panelRT.offsetMax.x, PanelTop);
        panelGO.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT, catEars: true, earInset: 92f);
        panelCG = panelGO.AddComponent<CanvasGroup>();
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var ribbon = CuteUIKit.TitleRibbon(panelRT, "개발자 옵션");
        ribbon.anchorMin = ribbon.anchorMax = ribbon.pivot = new Vector2(0.5f, 1f);
        ribbon.anchoredPosition = new Vector2(0f, -16f);
        var fit = ribbon.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var close = (RectTransform)CuteUIKit.CloseButton(panelRT, Toggle, 28f).transform;
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-14f, -16f);
        close.sizeDelta = new Vector2(28f, 28f);

        var tabs = Strip(panelRT, -58f, 28f, 18f);
        tabButtons = new[]
        {
            CuteUIKit.Button(tabs, "기능", CuteUIKit.ButtonLatte, () => ShowPage(0), -1f, 28f, 13f),
            CuteUIKit.Button(tabs, "아이템 넣기", CuteUIKit.ButtonLatte, () => ShowPage(1), -1f, 28f, 13f),
        };

        pages = new[] { Page("ToolsPage"), Page("ItemsPage") };
        BuildTools(HelpUI.BuildScrollView((RectTransform)pages[0].transform, new Vector2(14f, 16f), new Vector2(-10f, 0f)));
        BuildItemsHeader((RectTransform)pages[1].transform);

        ApplyFont(transform);
        ShowPage(0);
        panelRT.anchoredPosition = new Vector2(HiddenX, panelRT.anchoredPosition.y);
    }

    private GameObject Page(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(panelRT, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = new Vector2(0f, -94f);
        return go;
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].SetActive(i == index);
            CuteUIKit.ApplyButtonColors(tabButtons[i], i == index ? CuteUIKit.Strawberry : CuteUIKit.ButtonLatte);
        }
        if (index == 1) EnsureItemList();
    }

    private void BuildTools(RectTransform content)
    {
        var card = CuteUIKit.Card(content, CuteUIKit.ButterSoft, 12, "Status");
        card.raycastTarget = false;
        Pad(card.gameObject, new RectOffset(12, 12, 8, 8));
        status = CuteUIKit.Text(card.transform, "", 12f, CuteUIKit.Cocoa, align: TextAlignmentOptions.TopLeft);
        status.lineSpacing = 8f;

        var section = Section(content, "폭풍 · 이벤트", CuteUIKit.SkyTag, CuteUIKit.SkyText);
        stormNote = Note(section, "3초 경고 뒤 몰아쳐요.");
        var row = Row(section);
        Btn(row, "모래폭풍 발동", CuteUIKit.ButtonButter, () => Storm(WorldTheme.Wasteland));
        Btn(row, "눈보라 발동", CuteUIKit.ButtonSky, () => Storm(WorldTheme.Tundra));
        row = Row(section);
        Btn(row, "모래폭풍 효과만", CuteUIKit.ButtonLatte, () => StormWeatherFx.Play(WorldTheme.Wasteland, 1f, 4f, 3f));
        Btn(row, "눈보라 효과만", CuteUIKit.ButtonLatte, () => StormWeatherFx.Play(WorldTheme.Tundra, 1f, 4f, 3f));
        Btn(Row(section), "에너지 풍족기간 시작", CuteUIKit.ButtonMint, StartBoost);
        row = Row(section);
        Btn(row, "맑음", CuteUIKit.ButtonLatte, () => SetWeather(WeatherType.Clear));
        Btn(row, "별비", CuteUIKit.ButtonSky, () => SetWeather(WeatherType.StarRain));
        Btn(row, "오로라", CuteUIKit.ButtonMint, () => SetWeather(WeatherType.Aurora));
        row = Row(section);
        Btn(row, "유성우", CuteUIKit.ButtonButter, () => SetWeather(WeatherType.MeteorShower));
        Btn(row, "폭풍 날씨", CuteUIKit.ButtonCoral, () => SetWeather(WeatherType.Storm));
        Btn(row, "셔틀 짐 새로", CuteUIKit.ButtonButter, RefreshShuttles);
        Btn(row, "셔틀 바로 도착", CuteUIKit.ButtonSky, ArriveShuttles);

        section = Section(content, "코인", CuteUIKit.ButterSoft, CuteUIKit.AmberText);
        row = Row(section);
        foreach (var (label, amount) in new[] { ("+1천", 1_000L), ("+1만", 10_000L), ("+10만", 100_000L), ("+100만", 1_000_000L) })
            Btn(row, label, CuteUIKit.ButtonButter, () => AddCoins(amount));
        ConfirmBtn(Row(section), "코인 0으로", () => SetCoins(0));

        section = Section(content, "연구 · 퀘스트", CuteUIKit.LavenderTag, CuteUIKit.LavenderText);
        row = Row(section);
        ConfirmBtn(row, "연구 초기화", ResetTech);
        Btn(row, "연구 전부 완료", CuteUIKit.ButtonMint, CompleteTech);
        Btn(Row(section), "지금 퀘스트 완료 (보상 받기)", CuteUIKit.ButtonMint, CompleteQuest);

        section = Section(content, "중앙 발전소", CuteUIKit.MintTag, CuteUIKit.MintText);
        row = Row(section);
        Btn(row, "연료 가득", CuteUIKit.ButtonMint, () => SetPlantFuel(1f));
        Btn(row, "연료 비우기", CuteUIKit.ButtonCoral, () => SetPlantFuel(0f));
        Btn(row, "문명 +1", CuteUIKit.ButtonSky, PlantLevelUp);

        section = Section(content, "시간 · 밭", CuteUIKit.SkyTag, CuteUIKit.SkyText);
        row = Row(section);
        Btn(row, "낮 12:00", CuteUIKit.ButtonButter, () => SetHour(12));
        Btn(row, "밤 22:00", CuteUIKit.ButtonSky, () => SetHour(22));
        Btn(row, "작물 다 키우기", CuteUIKit.ButtonMint, GrowAllCrops);

        section = Section(content, "게임 속도", CuteUIKit.PinkSoft, CuteUIKit.CoralText);
        Note(section, "게임 속 시간이 모두 빨라져요 (씬을 나가면 ×1로 돌아와요).");
        row = Row(section);
        foreach (float speed in Speeds)
            speedButtons.Add(Btn(row, $"×{speed:0}", CuteUIKit.ButtonLatte, () => SetSpeed(speed)));
        RefreshSpeedButtons();
    }

    private static void Notify(string message) => NotificationUI.Instance?.Show($"[개발] {message}", CuteUIKit.LavenderText);

    private static void Storm(WorldTheme theme)
    {
        if (GameEventManager.Instance == null) { Notify("이벤트 관리자가 없어요."); return; }
        GameEventManager.Instance.TriggerStormNow(theme, 3f);
    }

    private static void StartBoost()
    {
        if (GameEventManager.Instance == null) { Notify("이벤트 관리자가 없어요."); return; }
        GameEventManager.Instance.StartBoostNow();
    }

    private static void SetWeather(WeatherType w)
    {
        if (WeatherManager.Instance == null) { Notify("날씨 관리자가 없어요."); return; }
        WeatherManager.Instance.ForceWeather(w);
    }

    private static void RefreshShuttles()
    {
        if (ShuttleService.Instance == null) { Notify("이주 셔틀이 없어요."); return; }
        ShuttleService.Instance.DebugRerollAll();
        Notify("내려와 있는 셔틀의 짐을 모두 새로 받았어요.");
    }

    private static void ArriveShuttles()
    {
        if (ShuttleService.Instance == null) { Notify("이주 셔틀이 없어요."); return; }
        ShuttleService.Instance.DebugArriveAll();
        Notify("날아가는 셔틀이 곧 내려앉아요.");
    }

    private static void AddCoins(long amount)
    {
        if (CurrencyManager.Instance == null) return;
        CurrencyManager.Instance.Add(amount);
        Notify($"코인 +{CurrencyManager.Format(amount)}");
    }

    private static void SetCoins(long amount)
    {
        if (CurrencyManager.Instance == null) return;
        CurrencyManager.Instance.SetCurrency(amount);
        Notify($"코인을 {CurrencyManager.Format(amount)}으로 맞췄어요.");
    }

    private static void ResetTech()
    {
        if (TechTreeManager.Instance == null) return;
        TechTreeManager.Instance.ResetAllNodes();
        Notify("연구를 모두 초기화했어요 (쓴 코인은 돌아오지 않아요).");
    }

    private static void CompleteTech()
    {
        if (TechTreeManager.Instance == null) return;
        TechTreeManager.Instance.CompleteAllNodes();
        Notify("연구를 모두 끝까지 올렸어요.");
    }

    private static void CompleteQuest()
    {
        var quests = QuestManager.Instance;
        if (quests == null) return;
        if (quests.CurrentQuest == null) { Notify("남은 퀘스트가 없어요."); return; }
        string questName = quests.CurrentQuest.questName;
        quests.ForceCompleteCurrentQuest();
        Notify($"퀘스트 \"{questName}\" 완료!");
    }

    private static void SetPlantFuel(float ratio)
    {
        var core = CenterCore.Current;
        if (core == null) { Notify("중앙 발전소가 없어요."); return; }
        core.RestoreFromSave(core.MaxFuel * ratio);
        Notify(ratio > 0f ? "발전소 연료를 가득 채웠어요." : "발전소 연료를 비웠어요.");
    }

    private static void PlantLevelUp()
    {
        var core = CenterCore.Current;
        if (core == null) { Notify("중앙 발전소가 없어요."); return; }
        if (core.IsMaxLevel) { Notify("이미 마지막 문명 단계예요."); return; }
        core.ForceAdvance();
    }

    private static void SetHour(int hour)
    {
        var clock = GameTimeManager.Instance;
        if (clock == null) return;
        clock.RestoreTime(clock.CurrentDay, hour, 0);
        EnergyManager.Instance?.RefreshEnergyNetwork();
        Notify($"시각을 {hour:00}:00으로 바꿨어요.");
    }

    private static void GrowAllCrops()
    {
        int grown = 0;
        foreach (var plant in FindObjectsByType<PlantTile>(FindObjectsSortMode.None))
        {
            if (plant.IsGhost || plant.CurrentSeed == null) continue;
            if (plant.CurrentState != PlantTile.PlantState.Seeded && plant.CurrentState != PlantTile.PlantState.Growing) continue;
            var crop = plant.CurrentSeed.cropData;
            int lastStage = crop != null && crop.StageCount > 1 ? crop.StageCount - 1 : plant.GrowthStage;
            plant.RestoreFromSave(PlantTile.PlantState.ReadyToHarvest, plant.CurrentSeed, lastStage, 0f);
            grown++;
        }
        Notify(grown > 0 ? $"작물 {grown}개를 다 키웠어요." : "자라는 중인 작물이 없어요.");
    }

    private void SetSpeed(float speed)
    {
        Time.timeScale = speed;
        RefreshSpeedButtons();
        Notify($"게임 속도 ×{speed:0}");
    }

    private void RefreshSpeedButtons()
    {
        for (int i = 0; i < speedButtons.Count; i++)
            CuteUIKit.ApplyButtonColors(speedButtons[i],
                Mathf.Approximately(Time.timeScale, Speeds[i]) ? CuteUIKit.Strawberry : CuteUIKit.ButtonLatte);
    }

    private void RefreshStatus()
    {
        var sb = new System.Text.StringBuilder();
        if (GameTimeManager.Instance != null) sb.Append(GameTimeManager.Instance.GetDateTimeString());
        if (CurrencyManager.Instance != null) sb.Append("   코인 ").Append(CurrencyManager.Format(CurrencyManager.Instance.Currency));

        var events = GameEventManager.Instance;
        if (events != null)
        {
            sb.Append("\n날씨 ").Append(WeatherManager.Name(WeatherManager.Current));
            if (events.NextStormIn >= 0f)
                sb.Append(" (").Append(WorldThemeConfig.StormName(GameLaunchContext.PendingWorldTheme))
                  .Append(' ').Append(Clock(events.NextStormIn)).Append(" 뒤)");
            sb.Append("   ").Append(events.BoostActive ? "풍족기간 중!" : "다음 풍족기간 " + Clock(events.NextBoostIn));
            stormNote.text = $"3초 경고 뒤 몰아쳐요. 침입방어막 · 온실이 지키지 않는 작물이 {events.DestructionChance:0%} 확률로 날아가요 " +
                             "(밭은 그대로 · 폭풍은 우주 날씨가 폭풍일 때 와요).";
        }

        var core = CenterCore.Current;
        if (core != null)
            sb.Append($"\n문명 Lv.{core.Level} {core.CurrentStage?.title}   연료 {core.CurrentFuel:0} / {core.MaxFuel:0}");

        status.text = sb.ToString();
    }

    private static string Clock(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    private void BuildItemsHeader(RectTransform page)
    {
        search = SearchField(page);

        var chips = Strip(page, -40f, 24f, 16f);
        categoryButtons = new Button[Categories.Length];
        for (int i = 0; i < Categories.Length; i++)
        {
            var type = Categories[i].type;
            categoryButtons[i] = CuteUIKit.Button(chips, Categories[i].label, CuteUIKit.ButtonLatte, () => SetCategory(type), -1f, 24f, 11f);
        }

        itemContent = HelpUI.BuildScrollView(page, new Vector2(14f, 16f), new Vector2(-10f, -74f));
        SetCategory(null);
    }

    private TMP_InputField SearchField(RectTransform page)
    {
        var box = new GameObject("Search", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(page, false);
        var brt = (RectTransform)box.transform;
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot     = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(16f, -32f);
        brt.offsetMax = new Vector2(-16f, 0f);

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(box.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(12f, 2f);
        art.offsetMax = new Vector2(-12f, -2f);

        var placeholder = Stretch(CuteUIKit.Text(area.transform, "이름으로 찾기 (예: 주괴, 드릴)", 13f, CuteUIKit.CocoaMuted,
                                                 align: TextAlignmentOptions.MidlineLeft));
        placeholder.fontStyle = FontStyles.Italic;
        var text = Stretch(CuteUIKit.Text(area.transform, "", 13f, CuteUIKit.Cocoa, align: TextAlignmentOptions.MidlineLeft));

        var input = box.AddComponent<TMP_InputField>();
        input.textViewport   = art;
        input.textComponent  = text;
        input.placeholder    = placeholder;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 30;
        input.richText       = false;
        input.restoreOriginalTextOnEscape = false;
        input.onValueChanged.AddListener(_ => ApplyItemFilter());
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;
        return input;
    }

    private void SetCategory(FarmingItemType? type)
    {
        category = type;
        for (int i = 0; i < Categories.Length; i++)
            CuteUIKit.ApplyButtonColors(categoryButtons[i], Categories[i].type == type ? CuteUIKit.Strawberry : CuteUIKit.ButtonLatte);
        ApplyItemFilter();
    }

    private void EnsureItemList()
    {
        if (itemRows.Count > 0) return;
        var items = GameDataRegistry.FindAll<FarmingItemData>();
        items.RemoveAll(item => item == null || string.IsNullOrEmpty(item.itemName));
        items.Sort((a, b) => a.itemType != b.itemType ? a.itemType.CompareTo(b.itemType)
                                                      : string.CompareOrdinal(a.itemName, b.itemName));
        foreach (var item in items)
            itemRows.Add((item, ItemRow(item)));
        ApplyFont(itemContent);
        ApplyItemFilter();
    }

    private GameObject ItemRow(FarmingItemData item)
    {
        var row = CuteUIKit.Card(itemContent, CuteUIKit.LatteLight, 10, $"Item_{item.itemName}");
        row.raycastTarget = false;
        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(8, 6, 4, 4);
        hlg.spacing                = 5f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(row.transform, false);
        var icon = iconGO.AddComponent<Image>();
        icon.sprite         = item.icon;
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        if (item.icon == null) icon.color = Color.clear;
        var iconLE = iconGO.AddComponent<LayoutElement>();
        iconLE.preferredWidth = iconLE.preferredHeight = 26f;

        var label = CuteUIKit.Text(row.transform, item.itemName, 13f, CuteUIKit.Cocoa, align: TextAlignmentOptions.MidlineLeft);
        label.overflowMode = TextOverflowModes.Ellipsis;
        var labelLE = label.gameObject.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 10f;
        labelLE.flexibleWidth  = 1f;

        foreach (int amount in GiveAmounts)
            CuteUIKit.Button(row.transform, $"+{amount}", CuteUIKit.ButtonMint, () => Give(item, amount), 42f, 24f, 11f);
        return row.gameObject;
    }

    private static void Give(FarmingItemData item, int amount)
    {
        if (Inventory.Instance == null) return;
        var player = PlayerTileController.Instance;
        Inventory.Instance.AddItemOrDrop(item, amount, player != null ? player.transform.position : Vector3.zero);
    }

    private void ApplyItemFilter()
    {
        string query = search != null ? search.text.Trim() : "";
        foreach (var (item, row) in itemRows)
        {
            bool typeOk = category == null || item.itemType == category;
            bool nameOk = query.Length == 0 || item.itemName.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
            row.SetActive(typeOk && nameOk);
        }
    }

    public void Toggle()
    {
        isOpen = !isOpen;
        if (isOpen) HelpUI.CloseIfOpen();
        else if (search != null) search.DeactivateInputField();
        Sfx.Play(isOpen ? SfxId.UiOpen : SfxId.UiClose);
        panelCG.blocksRaycasts = isOpen;
        panelCG.interactable   = isOpen;
        if (isOpen)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            RefreshStatus();
            RefreshSpeedButtons();
        }
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Slide(isOpen));
    }

    public static void CloseIfOpen()
    {
        if (AnyOpen) current.Toggle();
    }

    private IEnumerator Slide(bool open)
    {
        float from = panelRT.anchoredPosition.x;
        float to   = open ? ShownX : HiddenX;
        float y    = panelRT.anchoredPosition.y;
        float duration = open ? AnimDuration * 1.5f : AnimDuration;

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = elapsed / duration;
            float k = open ? CuteUIKit.EaseOutBack(t, 1.4f) : t * t;
            panelRT.anchoredPosition = new Vector2(Mathf.LerpUnclamped(from, to, k), y);
            yield return null;
        }
        panelRT.anchoredPosition = new Vector2(to, y);
        animCo = null;
    }

    private static Transform Section(RectTransform content, string title, Color tag, Color text)
    {
        var card = CuteUIKit.Card(content, Color.white, 12, $"Section_{title}");
        card.raycastTarget = false;
        Pad(card.gameObject, new RectOffset(10, 10, 8, 10)).spacing = 7f;
        CuteUIKit.SectionHeader(card.transform, title, tag, text);
        return card.transform;
    }

    private static Transform Row(Transform section)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(section, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 6f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private static Transform Strip(RectTransform parent, float top, float height, float inset)
    {
        var go = new GameObject("Strip", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(inset, top - height);
        rt.offsetMax = new Vector2(-inset, top);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 6f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private static TextMeshProUGUI Note(Transform section, string text)
    {
        var t = CuteUIKit.Text(section, text, 11f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    private static Button Btn(Transform row, string label, Color fill, UnityAction onClick)
        => CuteUIKit.Button(row, label, fill, onClick, -1f, 28f, 12f);

    private void ConfirmBtn(Transform row, string label, System.Action action)
    {
        var c = new Confirm { text = label };
        var btn = Btn(row, label, CuteUIKit.ButtonCoral, () =>
        {
            if (c.until > 0f) { Disarm(c); action(); return; }
            c.until = Time.unscaledTime + 3f;
            c.label.text = "한 번 더 누르면 실행";
        });
        c.label = btn.GetComponentInChildren<TextMeshProUGUI>();
        confirms.Add(c);
    }

    private static void Disarm(Confirm c)
    {
        c.until = 0f;
        c.label.text = c.text;
    }

    private static VerticalLayoutGroup Pad(GameObject go, RectOffset padding)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = padding;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        return vlg;
    }

    private static TextMeshProUGUI Stretch(TextMeshProUGUI t)
    {
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    private void ApplyFont(Transform root)
    {
        if (font == null) return;
        foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) t.font = font;
    }
}
