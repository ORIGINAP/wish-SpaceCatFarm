using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterEditorUI : MonoBehaviour
{
    private static CharacterEditorUI instance;

    public static bool IsOpen => instance != null && instance.isOpen;

    public static Transform Root => instance != null ? instance.transform : null;

    public static void Open(CatLook start, string title, string note, string confirmLabel, Action<CatLook> onConfirm)
    {
        if (instance == null)
        {
            instance = new GameObject("CharacterEditorUI").AddComponent<CharacterEditorUI>();
            instance.Build();
        }
        instance.Show(start, title, note, confirmLabel, onConfirm);
    }

    // 옵션 창 위, 가상 커서 아래
    private const int   SortingOrder  = 610;
    private const float PanelWidth    = 500f;
    private const float PreviewHeight = 180f;
    private const float PreviewPivotY = 14f;
    private const int   PreviewScale  = 7;
    private const int   IconScale     = 3;

    private Canvas        canvas;
    private CanvasGroup   rootCG;
    private RectTransform panelRT;
    private bool          isOpen;
    private Coroutine     animCo, bounceCo;

    private TextMeshProUGUI titleText, noteText, nameTagText, colorText, accessoryText, statusText, confirmText;
    private TMP_InputField  nameInput;
    private CatPortrait     preview;
    private readonly List<Button>      colorButtons     = new();
    private readonly List<Button>      accessoryButtons = new();
    private readonly List<CatPortrait> accessoryIcons   = new();

    private CatLook         look = new();
    private Action<CatLook> onConfirm;
    private bool            nameTyped;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Update()
    {
        if (!isOpen) return;
        if (nameInput.isFocused) TextInputFocus.MarkTyping();
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(sound: true); return; }
        UIStyleKit.KeepNavigable(colorButtons[CatSkin.ValidColor(look.color)].gameObject, panelRT);
    }

    private void Show(CatLook start, string title, string note, string confirmLabel, Action<CatLook> confirm)
    {
        look      = start != null ? start.Copy() : new CatLook();
        onConfirm = confirm;
        nameTyped = false;

        titleText.text   = title;
        noteText.text    = note ?? "";
        noteText.gameObject.SetActive(!string.IsNullOrEmpty(note));
        confirmText.text = confirmLabel;
        nameInput.SetTextWithoutNotify(look.name ?? "");
        SetStatus("");
        Refresh();

        if (!isOpen) Sfx.Play(SfxId.UiOpen);
        isOpen = true;
        StartAnim(true);
    }

    private void Close(bool sound)
    {
        if (!isOpen) return;
        isOpen = false;
        if (sound) Sfx.Play(SfxId.UiClose);
        nameInput.DeactivateInputField();
        UIStyleKit.ClearSelectionIn(panelRT);
        StartAnim(false);
    }

    private void StartAnim(bool opening)
    {
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Anim(opening));
    }

    private IEnumerator Anim(bool opening)
    {
        canvas.enabled        = true;
        rootCG.blocksRaycasts = opening;
        rootCG.interactable   = opening;

        if (opening)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, rootCG, 0.28f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, rootCG, 0.14f, hideAtEnd: false);
            canvas.enabled = false;
        }
        animCo = null;
    }

    private void PickColor(int color)
    {
        look.color = color;
        Refresh();
        Bounce();
    }

    private void PickAccessory(int accessory)
    {
        look.accessory = accessory;
        Refresh();
        Bounce();
    }

    private void Randomize()
    {
        var random = CatLook.RandomLook();
        look.color     = random.color;
        look.accessory = random.accessory;
        if (!nameTyped) nameInput.SetTextWithoutNotify(random.name);
        SetStatus("");
        Refresh();
        Bounce();
    }

    private void OnNameChanged(string typed)
    {
        nameTyped = true;
        nameTagText.text = DisplayName(typed);
        SetStatus("");
    }

    private void Confirm()
    {
        if (!isOpen) return;
        look.name = CatLook.CleanName(nameInput.text);
        if (look.name.Length == 0)
        {
            SetStatus("고양이 이름을 지어 주세요!");
            Sfx.Play(SfxId.UiError);
            nameInput.ActivateInputField();
            return;
        }
        var done = onConfirm;
        onConfirm = null;
        Close(sound: false);
        done?.Invoke(look.Copy());
    }

    private void Refresh()
    {
        preview.Set(look);
        nameTagText.text   = DisplayName(nameInput.text);
        colorText.text     = CatSkin.ColorName(look.color);
        accessoryText.text = CatSkin.AccessoryName(look.accessory);

        int color = CatSkin.ValidColor(look.color), accessory = CatSkin.ValidAccessory(look.accessory);
        for (int i = 0; i < colorButtons.Count; i++) SetRing(colorButtons[i], i == color);
        for (int i = 0; i < accessoryButtons.Count; i++)
        {
            SetRing(accessoryButtons[i], i == accessory);
            UIStyleKit.ApplyButtonColors(accessoryButtons[i], i == accessory ? CuteUIKit.PinkSoft : CuteUIKit.LatteLight);
            accessoryIcons[i].Set(new CatLook { color = look.color, accessory = i });
        }
    }

    private static string DisplayName(string typed)
    {
        var name = CatLook.CleanName(typed);
        return name.Length > 0 ? name : "이름 없음";
    }

    private static void SetRing(Button button, bool chosen)
    {
        var ring = button.transform.Find("Ring");
        if (ring != null) ring.gameObject.SetActive(chosen);
    }

    private void SetStatus(string text)
    {
        statusText.text = text;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    private void Bounce()
    {
        if (bounceCo != null) StopCoroutine(bounceCo);
        var t = preview.transform;
        t.localScale = Vector3.one * 0.86f;
        bounceCo = StartCoroutine(CuteUIKit.ScaleTo(t, 1f, 0.28f));
    }

    private void Build()
    {
        canvas = UIStyleKit.CreateOverlayCanvas(gameObject, SortingOrder);
        rootCG = gameObject.AddComponent<CanvasGroup>();
        rootCG.alpha          = 0f;
        rootCG.blocksRaycasts = false;
        rootCG.interactable   = false;

        var dim = new GameObject("Dim", typeof(RectTransform));
        dim.transform.SetParent(transform, false);
        var dimRT = (RectTransform)dim.transform;
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = dimRT.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = CuteUIKit.Overlay;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        var panelCG = UIStyleKit.CreatePanelRoot(panel, PanelWidth, new RectOffset(26, 26, 22, 22), spacing: 10f);
        panelCG.alpha          = 1f;
        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;

        var p = panel.transform;
        titleText = CuteUIKit.TitleRibbon(Centered(p), "캐릭터 만들기", fontSize: 18f).GetComponentInChildren<TextMeshProUGUI>();
        noteText  = FullWidthText(p, "", 12f, UIStyleKit.TextSecondary, FontStyles.Normal);
        noteText.alignment        = TextAlignmentOptions.Center;
        noteText.textWrappingMode = TextWrappingModes.Normal;

        BuildPreview(p);

        var nameRow = Row(p, 10f);
        UIStyleKit.CreateText(nameRow, "이름", 14f, UIStyleKit.TextPrimary, FontStyles.Bold)
                  .GetComponent<LayoutElement>().preferredWidth = 52f;
        nameInput = NameInput(nameRow);
        nameInput.onValueChanged.AddListener(OnNameChanged);
        UIStyleKit.CreateButton(nameRow, "랜덤", CuteUIKit.ButtonButter, Randomize, preferredWidth: 76f, preferredHeight: 34f);

        colorText = Header(p, "털 색", CuteUIKit.MintTag, CuteUIKit.MintText);
        var colors = Row(p, 9f);
        colors.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        for (int i = 0; i < CatSkin.ColorCount; i++) colorButtons.Add(ColorButton(colors, i));

        accessoryText = Header(p, "장신구", CuteUIKit.LavenderTag, CuteUIKit.LavenderText);
        var gridGO = new GameObject("Accessories", typeof(RectTransform));
        gridGO.transform.SetParent(p, false);
        var grid = gridGO.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(104f, 84f);
        grid.spacing         = new Vector2(8f, 8f);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment  = TextAnchor.UpperCenter;
        for (int i = 0; i < CatSkin.AccessoryCount; i++) accessoryButtons.Add(AccessoryButton(gridGO.transform, i));

        statusText = FullWidthText(p, "", 12f, CuteUIKit.CoralText, FontStyles.Bold);
        statusText.alignment = TextAlignmentOptions.Center;

        UIStyleKit.CreateDivider(p);
        var bottom = Row(p, 10f);
        UIStyleKit.CreateButton(bottom, "뒤로", UIStyleKit.ButtonNeutral, () => Close(sound: true), preferredWidth: 120f, preferredHeight: 36f);
        var spacer = new GameObject("Space", typeof(RectTransform));
        spacer.transform.SetParent(bottom, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        confirmText = UIStyleKit.CreateButton(bottom, "확인", CuteUIKit.Strawberry, Confirm, preferredWidth: 200f, preferredHeight: 36f)
                                .GetComponentInChildren<TextMeshProUGUI>();

        panelRT.anchoredPosition = new Vector2(0f, -10f);
        canvas.enabled = false;
    }

    private void BuildPreview(Transform parent)
    {
        var card = CuteUIKit.Card(parent, CuteUIKit.LatteLight, 16, "Preview");
        card.raycastTarget = false;
        card.gameObject.AddComponent<LayoutElement>().preferredHeight = PreviewHeight;
        var c = card.transform;

        var shadow = CuteUIKit.Layer(c, "Shadow", CuteUIKit.Rounded(7), CuteUIKit.Shadow).rectTransform;
        shadow.anchorMin = shadow.anchorMax = new Vector2(0.5f, 0.5f);
        shadow.sizeDelta        = new Vector2(104f, 14f);
        shadow.anchoredPosition = new Vector2(0f, PreviewPivotY - 6.5f * PreviewScale);

        preview = CatPortrait.Create(c, new Vector2(220f, PreviewHeight), PreviewScale, animate: true, new Vector2(0f, PreviewPivotY));

        var tag = CuteUIKit.Pill(c, "", CuteUIKit.SkyText, Color.white, 13f, 12, new RectOffset(12, 12, 3, 3), out nameTagText, fitSelf: true);
        tag.anchoredPosition = new Vector2(0f, -62f);
        nameTagText.richText = false;
    }

    private Button ColorButton(Transform row, int color)
    {
        const float size = 34f;
        var go = new GameObject($"Color_{CatSkin.ColorName(color)}", typeof(RectTransform));
        go.transform.SetParent(row, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = le.preferredHeight = le.minHeight = size;
        go.AddComponent<Image>().color = Color.clear;

        CuteUIKit.Layer(go.transform, "Ring", CuteUIKit.Rounded(20), CuteUIKit.Strawberry, -3f);
        CuteUIKit.Layer(go.transform, "Edge", CuteUIKit.Rounded(17), CatSkin.SwatchOutline(color));
        var face = CuteUIKit.Layer(go.transform, "Face", CuteUIKit.Rounded(14), Color.white, 2.5f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = face;
        UIStyleKit.ApplyButtonColors(btn, CatSkin.Swatch(color));
        btn.onClick.AddListener(() => PickColor(color));
        go.AddComponent<UiButtonSfx>().hover = true;
        go.AddComponent<CutePop>();
        return btn;
    }

    private Button AccessoryButton(Transform grid, int accessory)
    {
        var go = new GameObject($"Accessory_{CatSkin.AccessoryName(accessory)}", typeof(RectTransform));
        go.transform.SetParent(grid, false);
        go.AddComponent<Image>().color = Color.clear;

        CuteUIKit.Layer(go.transform, "Ring", CuteUIKit.Rounded(16), CuteUIKit.Strawberry, -3f);
        var face = CuteUIKit.Layer(go.transform, "Face", CuteUIKit.Rounded(14), Color.white);

        var icon = CatPortrait.Create(go.transform, new Vector2(96f, 58f), IconScale, animate: false, new Vector2(0f, -4f));
        var irt  = (RectTransform)icon.transform;
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 1f);
        irt.anchoredPosition = new Vector2(0f, -3f);
        accessoryIcons.Add(icon);

        var label = CuteUIKit.Text(go.transform, CatSkin.AccessoryName(accessory), 11f, CuteUIKit.Cocoa, FontStyles.Bold);
        var lrt   = label.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot     = new Vector2(0.5f, 0f);
        lrt.sizeDelta = new Vector2(0f, 18f);
        lrt.anchoredPosition = new Vector2(0f, 5f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = face;
        btn.onClick.AddListener(() => PickAccessory(accessory));
        go.AddComponent<UiButtonSfx>().hover = true;
        go.AddComponent<CutePop>();
        return btn;
    }

    private static TMP_InputField NameInput(Transform row)
    {
        var box = new GameObject("NameInput", typeof(RectTransform), typeof(Image));
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

        var ph   = Stretch(CuteUIKit.Text(area.transform, "고양이 이름 (12자까지)", 14f, CuteUIKit.CocoaMuted, FontStyles.Italic, TextAlignmentOptions.MidlineLeft));
        var text = Stretch(CuteUIKit.Text(area.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft));

        var input = box.AddComponent<TMP_InputField>();
        input.textViewport   = art;
        input.textComponent  = text;
        input.placeholder    = ph;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.characterLimit = CatLook.MaxNameLength;
        input.richText       = false;
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;
        return input;
    }

    private static TextMeshProUGUI Header(Transform parent, string label, Color tagColor, Color textColor)
    {
        var row = Row(parent, 8f);
        CuteUIKit.Pill(row, label, tagColor, textColor, 11f, 10, new RectOffset(10, 10, 2, 2), out _);
        return UIStyleKit.CreateText(row, "", 13f, UIStyleKit.TextPrimary, FontStyles.Bold);
    }

    private static TextMeshProUGUI Stretch(TextMeshProUGUI t)
    {
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    private static Transform Row(Transform parent, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = spacing;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private static Transform Centered(Transform parent)
    {
        var go = new GameObject("Center", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private static TextMeshProUGUI FullWidthText(Transform parent, string text, float size, Color color, FontStyles style)
    {
        var t = UIStyleKit.CreateText(parent, text, size, color, style);
        t.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return t;
    }
}
