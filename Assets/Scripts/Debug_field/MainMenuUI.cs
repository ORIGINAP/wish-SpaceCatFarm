using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private string inGameSceneName = "InGame";

    private static readonly Color Warning = CuteUIKit.Hex(0xC9485B);

    private static readonly Vector2 TitleHome  = new(-90f, 80f);
    private static readonly Vector2 WorldsHome = new(-80f, 0f);
    private const float WorldsWidth       = 500f;
    private const float TitleButtonHeight = 56f;

    private enum Page { Title, Worlds, Join, Lab }

    private RectTransform titleRoot, worldsRoot, joinRoot, labRoot, slotList;
    private CanvasGroup   titleCG, worldsCG, joinCG, labCG;
    private TextMeshProUGUI loadingLabel, joinStatus, guestName, labNote, labLoading;
    private Button[]      labThemeButtons;
    private GameObject    labDefault;
    private WorldTheme    labTheme = WorldTheme.Wasteland;
    private TMP_InputField  codeInput;
    private Button          joinButton, guestEditButton;
    private CatPortrait     guestPortrait;
    private GameObject    titleDefault, worldsDefault;

    private Page page = Page.Title;
    private int  confirmDeleteSlot = -1;
    private WorldTheme newWorldTheme = WorldTheme.Wasteland;
    private bool rebuildPending;
    private bool loading;
    private Coroutine fadeCo;

    private void Awake()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        var rt = (RectTransform)transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        BuildTitle();
        BuildWorlds();
        BuildJoin();
        BuildLab();
        ShowPage(Page.Title, instant: true);
        if (MobileMode.Active) FitBackdropForMobile();
    }

    // 모바일 화면비에 맞춰 배경 스케일
    private void FitBackdropForMobile()
    {
        Vector2 canvas = new Vector2(Screen.width, Screen.height) / UiScale.MobileFactor;
        float pcFit = Mathf.Min(canvas.x / UiScale.PcReference.x, canvas.y / UiScale.PcReference.y);
        foreach (Transform t in transform.parent)
        {
            if (t == transform || !t.gameObject.activeSelf || t.GetComponent<Image>() == null || t.GetComponent<Selectable>() != null) continue;
            var rt   = (RectTransform)t;
            var size = rt.rect.size;
            bool backdrop = size.x >= UiScale.PcReference.x && size.y >= UiScale.PcReference.y;
            float k = backdrop ? Mathf.Max(canvas.x / size.x, canvas.y / size.y) : pcFit;
            rt.localScale       *= k;
            rt.anchoredPosition *= k;
        }
    }

    private void OnEnable()  => NetBootstrap.Changed += RefreshJoin;
    private void OnDisable() => NetBootstrap.Changed -= RefreshJoin;

    private void LateUpdate()
    {
        // 콜백 중 파괴 방지용 지연 재생성
        if (rebuildPending)
        {
            rebuildPending = false;
            RebuildSlots();
        }
        if (loading) return;

        bool covered = OptionsUI.IsOpen || CharacterEditorUI.IsOpen;
        titleCG.interactable  = page == Page.Title  && !covered;
        worldsCG.interactable = page == Page.Worlds && !covered;
        joinCG.interactable   = page == Page.Join   && !covered;
        labCG.interactable    = page == Page.Lab    && !covered;
        if (covered) return;

        if (page == Page.Join)
        {
            if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) CancelJoin();
            else if (GameInput.GetKeyDown(KeyCode.Return) || GameInput.GetKeyDown(KeyCode.KeypadEnter)) JoinClicked();
            return;
        }

        if (page == Page.Lab)
        {
            if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) ShowPage(Page.Title);
            else UIStyleKit.KeepNavigable(labDefault, labRoot);
            return;
        }

        if (page == Page.Worlds && EscapeRouter.TryConsume(EscapeRouter.CancelPressed))
        {
            if (confirmDeleteSlot >= 0) { confirmDeleteSlot = -1; rebuildPending = true; }
            else ShowPage(Page.Title);
            return;
        }

        if (page == Page.Title) UIStyleKit.KeepNavigable(titleDefault, titleRoot);
        else                    UIStyleKit.KeepNavigable(worldsDefault, worldsRoot);
    }

    private void ShowPage(Page next, bool instant = false)
    {
        page = next;
        confirmDeleteSlot = -1;
        if (next == Page.Worlds) RebuildSlots();

        UIStyleKit.ClearSelectionIn(transform);
        if (fadeCo != null) StopCoroutine(fadeCo);
        titleRoot.anchoredPosition  = TitleHome;
        worldsRoot.anchoredPosition = WorldsHome;
        joinRoot.anchoredPosition   = WorldsHome;
        labRoot.anchoredPosition    = WorldsHome;

        var show = next switch { Page.Title => titleCG, Page.Worlds => worldsCG, Page.Join => joinCG, _ => labCG };
        foreach (var cg in new[] { titleCG, worldsCG, joinCG, labCG })
            if (cg != show) SetVisible(cg, false);
        if (instant) SetVisible(show, true);
        else
        {
            Sfx.Play(next == Page.Title ? SfxId.UiClose : SfxId.UiOpen);
            fadeCo = StartCoroutine(FadeIn(show, next == Page.Title ? TitleHome : WorldsHome));
        }
        if (next == Page.Join)
        {
            RefreshJoin();
            codeInput.Select();
        }
        if (next == Page.Lab) RefreshLab();
    }

    private IEnumerator FadeIn(CanvasGroup show, Vector2 home)
    {
        var rt = (RectTransform)show.transform;
        show.blocksRaycasts = true;
        if (show != titleCG) CuteUIKit.WiggleEars(this, rt);

        const float dur = 0.26f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float p = t / dur;
            show.alpha = Mathf.Clamp01(p * 3f);
            float k = CuteUIKit.EaseOutBack(p, 2.2f);
            rt.anchoredPosition = home + new Vector2((1f - k) * 36f, 0f);
            yield return null;
        }
        rt.anchoredPosition = home;
        SetVisible(show, true);
        fadeCo = null;
    }

    private static void SetVisible(CanvasGroup cg, bool visible)
    {
        cg.alpha          = visible ? 1f : 0f;
        cg.blocksRaycasts = visible;
        cg.interactable   = visible;
    }

    private void BuildTitle()
    {
        titleRoot = NewRect("TitleMenu", transform);
        titleRoot.anchorMin = titleRoot.anchorMax = titleRoot.pivot = new Vector2(1f, 0f);
        titleRoot.anchoredPosition = TitleHome;
        titleRoot.sizeDelta = new Vector2(300f, 0f);
        titleCG = titleRoot.gameObject.AddComponent<CanvasGroup>();

        var vlg = titleRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 12f;
        vlg.childAlignment         = TextAnchor.LowerRight;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        titleRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        titleDefault = MenuButton(titleRoot, "시작", true, () => ShowPage(Page.Worlds)).gameObject;
        MenuButton(titleRoot, "참가하기", false, () => ShowPage(Page.Join));
        MenuButton(titleRoot, "실험실", false, () => ShowPage(Page.Lab));
        MenuButton(titleRoot, "옵션", false, OptionsUI.Open);
        MenuButton(titleRoot, "종료", false, QuitGame);
    }

    private static Button MenuButton(Transform parent, string label, bool primary, UnityAction onClick)
    {
        var btn = CuteUIKit.Button(parent, label, primary ? CuteUIKit.Strawberry : CuteUIKit.Cream, onClick,
                                   height: TitleButtonHeight, fontSize: 21f);
        var t = btn.transform;

        var pill = CuteUIKit.Rounded(Mathf.RoundToInt(TitleButtonHeight * 0.5f));
        t.Find("Face").GetComponent<Image>().sprite = pill;
        var lip = t.Find("Lip").GetComponent<Image>();
        lip.sprite = pill;
        lip.rectTransform.offsetMin = new Vector2(0f, -5f);
        lip.rectTransform.offsetMax = new Vector2(0f, -5f);
        if (!primary) lip.color = CuteUIKit.Latte;

        var colors = btn.colors;
        if (!primary) colors.highlightedColor = colors.selectedColor = CuteUIKit.PinkSoft;
        colors.disabledColor = colors.normalColor;
        btn.colors = colors;

        var txt = t.GetComponentInChildren<TextMeshProUGUI>();
        txt.color = primary ? Color.white : CuteUIKit.Cocoa;
        txt.characterSpacing = 12f;

        if (primary)
        {
            const float pawSize = 20f, gap = 8f;
            float textWidth = txt.GetPreferredValues(label).x;
            txt.rectTransform.anchoredPosition = new Vector2((pawSize + gap) * 0.5f, 0f);
            var paw = CuteUIKit.Layer(t, "Paw", CuteUIKit.Paw(), Color.white).rectTransform;
            paw.anchorMin = paw.anchorMax = new Vector2(0.5f, 0.5f);
            paw.sizeDelta        = new Vector2(pawSize, pawSize);
            paw.anchoredPosition = new Vector2(-(textWidth + gap) * 0.5f, 0f);
        }
        return btn;
    }

    private void BuildWorlds()
    {
        worldsRoot = NewRect("WorldSelect", transform);
        worldsRoot.anchorMin = worldsRoot.anchorMax = worldsRoot.pivot = new Vector2(1f, 0.5f);
        worldsRoot.anchoredPosition = WorldsHome;
        worldsCG = UIStyleKit.CreatePanelRoot(worldsRoot.gameObject, WorldsWidth, new RectOffset(24, 24, 20, 22), spacing: 12f);

        var t = worldsRoot.transform;
        CuteUIKit.TitleRibbon(Centered(t), "월드 선택", fontSize: 18f);
        UIStyleKit.CreateText(t, "플레이할 월드를 고르세요", 12f, UIStyleKit.TextSecondary, align: TextAlignmentOptions.Center);
        UIStyleKit.CreateDivider(t);

        slotList = NewRect("Slots", t);
        var vlg = slotList.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 10f;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        UIStyleKit.CreateDivider(t);

        var bottom = Row(t, 10f);
        UIStyleKit.CreateButton(bottom, "뒤로", UIStyleKit.ButtonNeutral, () => ShowPage(Page.Title), preferredWidth: 120f, preferredHeight: 34f);
        loadingLabel = FullWidthText(bottom, "", 13f, UIStyleKit.Accent, FontStyles.Bold);
        loadingLabel.alignment = TextAlignmentOptions.MidlineRight;
        loadingLabel.GetComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private void BuildJoin()
    {
        joinRoot = NewRect("JoinWorld", transform);
        joinRoot.anchorMin = joinRoot.anchorMax = joinRoot.pivot = new Vector2(1f, 0.5f);
        joinRoot.anchoredPosition = WorldsHome;
        joinCG = UIStyleKit.CreatePanelRoot(joinRoot.gameObject, 420f, new RectOffset(24, 24, 20, 22), spacing: 12f);

        var t = joinRoot.transform;
        CuteUIKit.TitleRibbon(Centered(t), "친구 월드 참가", fontSize: 18f);
        UIStyleKit.CreateText(t, "친구에게 받은 조인 코드를 넣고 [참가]를 누르세요", 12f, UIStyleKit.TextSecondary,
                              align: TextAlignmentOptions.Center);
        UIStyleKit.CreateDivider(t);

        codeInput = LabeledInput(t, "조인 코드", "예: 7K2P9Q", 12);
        codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
        codeInput.onValueChanged.AddListener(v =>
        {
            var up = v.ToUpperInvariant();
            if (up != v) codeInput.SetTextWithoutNotify(up);
        });
        BuildGuestRow(t);

        joinStatus = FullWidthText(t, "", 12f, UIStyleKit.Accent, FontStyles.Bold);
        joinStatus.alignment        = TextAlignmentOptions.Center;
        joinStatus.textWrappingMode = TextWrappingModes.Normal;

        UIStyleKit.CreateDivider(t);
        var bottom = Row(t, 10f);
        UIStyleKit.CreateButton(bottom, "뒤로", UIStyleKit.ButtonNeutral, CancelJoin, preferredWidth: 120f, preferredHeight: 34f);
        var spacer = NewRect("Space", bottom);
        spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        joinButton = UIStyleKit.CreateButton(bottom, "참가", UIStyleKit.ButtonConfirm, JoinClicked, preferredWidth: 140f, preferredHeight: 34f);
    }

    private static TMP_InputField LabeledInput(Transform parent, string label, string placeholder, int limit)
    {
        var row = Row(parent, 10f);
        var name = UIStyleKit.CreateText(row, label, 13f, UIStyleKit.TextPrimary, FontStyles.Bold);
        name.GetComponent<LayoutElement>().preferredWidth = 72f;

        var box = new GameObject("Input", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(row, false);
        var le = box.AddComponent<LayoutElement>();
        le.flexibleWidth   = 1f;
        le.preferredHeight = 34f;

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(box.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(12f, 2f);
        art.offsetMax = new Vector2(-12f, -2f);

        var ph = Stretch(CuteUIKit.Text(area.transform, placeholder, 14f, CuteUIKit.CocoaMuted, align: TextAlignmentOptions.MidlineLeft));
        ph.fontStyle = FontStyles.Italic;
        var text = Stretch(CuteUIKit.Text(area.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft));

        var input = box.AddComponent<TMP_InputField>();
        input.textViewport   = art;
        input.textComponent  = text;
        input.placeholder    = ph;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.characterLimit = limit;
        input.richText       = false;
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;
        return input;
    }

    private void BuildGuestRow(Transform parent)
    {
        var row = Row(parent, 10f);
        UIStyleKit.CreateText(row, "내 캐릭터", 13f, UIStyleKit.TextPrimary, FontStyles.Bold)
                  .GetComponent<LayoutElement>().preferredWidth = 72f;
        guestPortrait = CatPortrait.Create(row, new Vector2(40f, 40f), 2, animate: true, new Vector2(0f, -2f));
        guestName = FullWidthText(row, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold);
        guestName.GetComponent<LayoutElement>().flexibleWidth = 1f;
        guestName.richText = false;
        guestEditButton = UIStyleKit.CreateButton(row, "꾸미기", CuteUIKit.LavenderTag, EditGuestLook, preferredWidth: 96f, preferredHeight: 34f);
        RefreshGuest();
    }

    private void RefreshGuest()
    {
        var look = NetBootstrap.GuestLook;
        guestPortrait.Set(look);
        guestName.text = look.name;
    }

    private void EditGuestLook()
    {
        CharacterEditorUI.Open(NetBootstrap.GuestLook, "내 캐릭터", "친구 월드에 들어갈 때의 내 모습이에요 (이 PC에 저장)", "확인", look =>
        {
            NetBootstrap.GuestLook = look;
            RefreshGuest();
            Sfx.Play(SfxId.UiConfirm);
        });
    }

    private static TextMeshProUGUI Stretch(TextMeshProUGUI t)
    {
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var fitter = t.GetComponent<ContentSizeFitter>();
        if (fitter != null) Destroy(fitter);
        return t;
    }

    private void JoinClicked()
    {
        if (page != Page.Join || NetBootstrap.Busy || NetSession.Mode != NetSession.Role.Offline) return;
        string code = codeInput.text.Trim();
        if (code.Length < 4)
        {
            NetBootstrap.SetStatus("조인 코드를 넣어 주세요.");
            Sfx.Play(SfxId.UiError);
            return;
        }
        Sfx.Play(SfxId.UiConfirm);
        NetBootstrap.Join(code);
    }

    private void CancelJoin()
    {
        if (NetSession.Mode != NetSession.Role.Offline) NetBootstrap.Leave();
        NetBootstrap.SetStatus("");
        ShowPage(Page.Title);
    }

    private void RefreshJoin()
    {
        if (joinStatus == null) return;
        joinStatus.text = NetBootstrap.Status;
        bool idle = !NetBootstrap.Busy && NetSession.Mode == NetSession.Role.Offline;
        joinButton.interactable      = idle;
        codeInput.interactable       = idle;
        guestEditButton.interactable = idle;
    }

    private void BuildLab()
    {
        labRoot = NewRect("Lab", transform);
        labRoot.anchorMin = labRoot.anchorMax = labRoot.pivot = new Vector2(1f, 0.5f);
        labRoot.anchoredPosition = WorldsHome;
        labCG = UIStyleKit.CreatePanelRoot(labRoot.gameObject, 440f, new RectOffset(24, 24, 20, 22), spacing: 12f);

        var t = labRoot.transform;
        CuteUIKit.TitleRibbon(Centered(t), "실험실", fontSize: 18f);
        var intro = FullWidthText(t, "새로 만드는 중인 놀거리를 먼저 해 보는 곳이에요.\n" +
                                     "보급 캡슐 · 유성우 · 털실 공 축구 · 별조각 사냥 · 협동 금고\n" +
                                     "아기 고양이 · 감정표현 · 핑 · 잠자기 투표 · 사진 모드",
                                  12f, UIStyleKit.TextSecondary, FontStyles.Normal);
        intro.alignment        = TextAlignmentOptions.Center;
        intro.textWrappingMode = TextWrappingModes.Normal;
        UIStyleKit.CreateDivider(t);

        var themeRow = Row(t, 8f);
        labThemeButtons = new[] { LabThemeButton(themeRow, WorldTheme.Wasteland), LabThemeButton(themeRow, WorldTheme.Tundra) };
        var sizeRow = Row(t, 8f);
        labDefault = LabSizeButton(sizeRow, WorldSize.Small).gameObject;
        LabSizeButton(sizeRow, WorldSize.Middle);
        LabSizeButton(sizeRow, WorldSize.Large);

        labNote = FullWidthText(t, "", 11f, UIStyleKit.TextSecondary, FontStyles.Normal);
        labNote.alignment        = TextAlignmentOptions.Center;
        labNote.textWrappingMode = TextWrappingModes.Normal;
        labNote.richText         = false;

        UIStyleKit.CreateDivider(t);
        var bottom = Row(t, 10f);
        UIStyleKit.CreateButton(bottom, "뒤로", UIStyleKit.ButtonNeutral, () => ShowPage(Page.Title), preferredWidth: 120f, preferredHeight: 34f);
        labLoading = FullWidthText(bottom, "", 13f, UIStyleKit.Accent, FontStyles.Bold);
        labLoading.alignment = TextAlignmentOptions.MidlineRight;
        labLoading.GetComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private Button LabThemeButton(Transform row, WorldTheme theme)
    {
        var btn = UIStyleKit.CreateButton(row, WorldThemeConfig.DisplayName(theme), CuteUIKit.Cream, () =>
        {
            labTheme = theme;
            RefreshLab();
        }, preferredHeight: 36f);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        label.text = $"<b>{WorldThemeConfig.DisplayName(theme)}</b>\n<size=75%>{WorldThemeConfig.StormName(theme)}</size>";
        label.lineSpacing = -10f;
        return btn;
    }

    private Button LabSizeButton(Transform row, WorldSize size)
    {
        var e = WorldSizeConfig.Get(size);
        var btn = UIStyleKit.CreateButton(row, WorldSizeConfig.DisplayName(size), LabPanelUI.TabColor, () => StartLab(size), preferredHeight: 40f);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        label.text = $"<b>{WorldSizeConfig.DisplayName(size)}</b>\n<size=75%>{e.gridWidth}×{e.gridHeight}</size>";
        label.lineSpacing = -10f;
        return btn;
    }

    private void RefreshLab()
    {
        for (int i = 0; i < labThemeButtons.Length; i++)
            CuteUIKit.ApplyButtonColors(labThemeButtons[i], (WorldTheme)i == labTheme ? CuteUIKit.Strawberry : CuteUIKit.Cream);
        labNote.text = $"테마를 고르고 크기를 누르면 바로 들어가요 (저장되지 않아요).\n" +
                       $"내 캐릭터: {NetBootstrap.GuestLook.name} — [참가하기] → [꾸미기]에서 바꿔요.\n" +
                       "친구와 하려면 들어가서 Esc → [친구 초대]";
    }

    private void StartLab(WorldSize size)
    {
        if (loading) return;
        GameLaunchContext.IsNewGame         = true;
        GameLaunchContext.LaunchedFromMenu  = false;
        GameLaunchContext.PendingWorldSize  = size;
        GameLaunchContext.PendingWorldTheme = labTheme;
        GameLaunchContext.PendingWorldSeed  = Random.Range(1, int.MaxValue);
        GameLaunchContext.PendingGridWidth  = GameLaunchContext.PendingGridHeight = 0;
        GameLaunchContext.PendingCharacter  = NetBootstrap.GuestLook;
        Lab.LaunchPrepared = true;
        StartCoroutine(LoadLab());
    }

    private IEnumerator LoadLab()
    {
        loading = true;
        labCG.interactable = false;
        labLoading.text = "실험실 여는 중...";
        Sfx.Play(SfxId.GameStart);
        yield return null;
        SceneManager.LoadScene(Lab.SceneName);
    }

    private void RebuildSlots()
    {
        for (int i = slotList.childCount - 1; i >= 0; i--)
            DestroyImmediate(slotList.GetChild(i).gameObject);

        worldsDefault = null;
        for (int slot = 0; slot < SaveManager.SlotCount; slot++)
            BuildSlotCard(slot);
    }

    private void BuildSlotCard(int slot)
    {
        var card = NewRect($"World{slot + 1}", slotList);
        CuteUIKit.SetRounded(card.gameObject.AddComponent<Image>(), 12, CuteUIKit.LatteLight);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(16, 16, 12, 12);
        vlg.spacing                = 6f;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var state = SaveManager.GetSlotState(slot);
        var data  = state == SaveManager.SlotState.Ok ? SaveManager.ReadSlotStatic(slot) : null;

        switch (state)
        {
            case SaveManager.SlotState.Ok:
            {
                string size  = WorldSizeConfig.DisplayName((WorldSize)data.worldSize);
                string theme = WorldThemeConfig.DisplayName((WorldTheme)data.worldTheme);
                CardCharacter(CardTitle(card, slot, $"{size} · {theme} 월드", CuteUIKit.SkyTag, CuteUIKit.SkyText), data.character);
                FullWidthText(card, $"{data.timestamp} 저장  ·  플레이 {SaveMenuUI.FormatPlayTime(data.playTimeSeconds)}", 11f, UIStyleKit.TextSecondary, FontStyles.Normal);

                var row = Row(card, 8f);
                if (confirmDeleteSlot == slot) BuildDeleteConfirm(row, slot);
                else
                {
                    Remember(UIStyleKit.CreateButton(row, "이어하기", UIStyleKit.ButtonConfirm, () => ContinueSlot(slot), preferredHeight: 34f));
                    UIStyleKit.CreateButton(row, "삭제", UIStyleKit.ButtonDanger, () => AskDelete(slot), preferredWidth: 84f, preferredHeight: 34f);
                }
                break;
            }

            case SaveManager.SlotState.Corrupt:
            {
                CardTitle(card, slot, "손상된 세이브", CuteUIKit.ButtonCoral, Warning);
                FullWidthText(card, "세이브 파일을 읽을 수 없습니다. 삭제하면 이 칸에 새 월드를 만들 수 있습니다.", 11f, Warning, FontStyles.Normal)
                    .textWrappingMode = TextWrappingModes.Normal;

                var row = Row(card, 8f);
                if (confirmDeleteSlot == slot) BuildDeleteConfirm(row, slot);
                else Remember(UIStyleKit.CreateButton(row, "삭제", UIStyleKit.ButtonDanger, () => AskDelete(slot), preferredHeight: 34f));
                break;
            }

            default:
            {
                CardTitle(card, slot, "빈 슬롯", CuteUIKit.Cream, CuteUIKit.CocoaSoft);
                FullWidthText(card, "새 월드 — 테마를 고르고 크기를 누르면 캐릭터를 만들고 시작합니다", 11f, UIStyleKit.TextSecondary, FontStyles.Normal);

                var themeRow = Row(card, 8f);
                ThemeButton(themeRow, WorldTheme.Wasteland);
                ThemeButton(themeRow, WorldTheme.Tundra);

                var row = Row(card, 8f);
                Remember(SizeButton(row, slot, WorldSize.Small));
                SizeButton(row, slot, WorldSize.Middle);
                SizeButton(row, slot, WorldSize.Large);
                break;
            }
        }
    }

    private void BuildDeleteConfirm(Transform row, int slot)
    {
        var q = FullWidthText(row, "정말 삭제할까요? 되돌릴 수 없습니다.", 12f, Warning, FontStyles.Bold);
        q.GetComponent<LayoutElement>().flexibleWidth = 1f;
        Remember(UIStyleKit.CreateButton(row, "취소", CuteUIKit.Cream, CancelDelete, preferredWidth: 84f, preferredHeight: 34f));
        UIStyleKit.CreateButton(row, "삭제", UIStyleKit.ButtonDanger, () => DeleteSlot(slot), preferredWidth: 84f, preferredHeight: 34f);
    }

    private Button SizeButton(Transform row, int slot, WorldSize size)
    {
        var e = WorldSizeConfig.Get(size);
        var btn = UIStyleKit.CreateButton(row, WorldSizeConfig.DisplayName(size), UIStyleKit.ButtonConfirm,
                                          () => CreateCharacter(slot, size), preferredHeight: 40f);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        label.text = $"<b>{WorldSizeConfig.DisplayName(size)}</b>\n<size=75%><color=#3F8A66>{e.gridWidth}×{e.gridHeight}</color></size>";
        label.lineSpacing = -10f;
        return btn;
    }

    private void ThemeButton(Transform row, WorldTheme theme)
    {
        bool selected = newWorldTheme == theme;
        var btn = UIStyleKit.CreateButton(row, WorldThemeConfig.DisplayName(theme), selected ? CuteUIKit.Strawberry : CuteUIKit.Cream,
                                          () => { newWorldTheme = theme; rebuildPending = true; }, preferredHeight: 36f);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        label.text = $"<b>{WorldThemeConfig.DisplayName(theme)}</b>\n<size=75%>{WorldThemeConfig.StormName(theme)}</size>";
        label.lineSpacing = -10f;
    }

    private void Remember(Button b)
    {
        if (worldsDefault == null) worldsDefault = b.gameObject;
    }

    private void CreateCharacter(int slot, WorldSize size)
    {
        if (loading) return;
        string world = $"월드 {slot + 1} · {WorldSizeConfig.DisplayName(size)} · {WorldThemeConfig.DisplayName(newWorldTheme)} 월드";
        CharacterEditorUI.Open(new CatLook { name = CatLook.SuggestName() }, "캐릭터 만들기",
                               $"{world}에서 함께할 고양이 대원을 꾸며 주세요", "이 캐릭터로 시작",
                               look => NewGame(slot, size, look));
    }

    private void NewGame(int slot, WorldSize size, CatLook character)
    {
        GameLaunchContext.SelectedSlot      = slot;
        GameLaunchContext.IsNewGame         = true;
        GameLaunchContext.PendingWorldSize  = size;
        GameLaunchContext.PendingWorldTheme = newWorldTheme;
        GameLaunchContext.PendingWorldSeed  = Random.Range(1, int.MaxValue);
        GameLaunchContext.PendingCharacter  = character;
        StartCoroutine(LoadInGame($"월드 {slot + 1} 만드는 중..."));
    }

    private void ContinueSlot(int slot)
    {
        GameLaunchContext.SelectedSlot = slot;
        GameLaunchContext.IsNewGame    = false;
        StartCoroutine(LoadInGame($"월드 {slot + 1} 불러오는 중..."));
    }

    private IEnumerator LoadInGame(string message)
    {
        if (loading) yield break;
        loading = true;
        GameLaunchContext.LaunchedFromMenu = true;
        worldsCG.interactable = false;
        loadingLabel.text = message;
        Sfx.Play(SfxId.GameStart);
        yield return null;
        SceneManager.LoadScene(inGameSceneName);
    }

    private void AskDelete(int slot)
    {
        confirmDeleteSlot = slot;
        rebuildPending = true;
    }

    private void CancelDelete()
    {
        confirmDeleteSlot = -1;
        rebuildPending = true;
    }

    private void DeleteSlot(int slot)
    {
        SaveManager.DeleteSlotStatic(slot);
        Sfx.Play(SfxId.Dismantle);
        confirmDeleteSlot = -1;
        rebuildPending = true;
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Transform Row(Transform parent, float spacing)
    {
        var rt = NewRect("Row", parent);
        var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = spacing;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return rt;
    }

    private static TextMeshProUGUI FullWidthText(Transform parent, string text, float size, Color color, FontStyles style)
    {
        var t = UIStyleKit.CreateText(parent, text, size, color, style);
        t.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return t;
    }

    private static Transform Centered(Transform parent)
    {
        var rt = NewRect("Center", parent);
        var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return rt;
    }

    private static Transform CardTitle(Transform card, int slot, string tag, Color tagFill, Color tagText)
    {
        var row = Row(card, 8f);
        UIStyleKit.CreateText(row, $"월드 {slot + 1}", 16f, UIStyleKit.TextPrimary, FontStyles.Bold);
        CuteUIKit.Pill(row, tag, tagFill, tagText, 11f, 10, new RectOffset(9, 9, 2, 2), out _);
        return row;
    }

    private static void CardCharacter(Transform titleRow, CatLook character)
    {
        // 구버전 세이브는 캐릭터 정보 없음
        var look = character != null && !string.IsNullOrEmpty(character.name) ? character : new CatLook { name = NetBootstrap.PlayerName };
        NewRect("Space", titleRow).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CatPortrait.Create(titleRow, new Vector2(34f, 34f), 2, animate: true, new Vector2(0f, -2f)).Set(look);
        UIStyleKit.CreateText(titleRow, look.name, 13f, UIStyleKit.TextPrimary, FontStyles.Bold).richText = false;
    }
}
