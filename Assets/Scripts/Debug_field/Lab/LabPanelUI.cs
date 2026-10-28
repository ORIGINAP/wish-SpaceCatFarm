using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class LabPanelUI : MonoBehaviour
{
    private const float PanelWidth   = 390f;
    private const float AnimDuration = 0.22f;
    private const float ShownX       = 14f;
    private const float HiddenX      = -(PanelWidth + 90f);

    public static readonly Color TabColor = CuteUIKit.Hex(0xB59CF0);

    private static LabPanelUI instance;

    public static bool IsOpen => instance != null && instance.isOpen;

    public static Transform Root => instance != null ? instance.panelRT : null;

    public static RectTransform HudRoot => instance != null ? instance.hudRoot : null;

    private RectTransform panelRT, hudRoot, tabRT;
    private CanvasGroup   panelCG;
    private bool          isOpen, dirty;
    private Coroutine     animCo;
    private float         nextRefresh;

    private sealed class Card
    {
        public LabExperiment   exp;
        public Button          toggle;
        public TextMeshProUGUI toggleLabel, status;
    }
    private readonly List<Card> cards = new();

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("LabPanelUI", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(go, 25);
        instance = go.AddComponent<LabPanelUI>();
        instance.Build();
    }

    public static void MarkDirty()
    {
        if (instance != null) instance.dirty = true;
    }

    public static void Toggle()
    {
        if (instance != null) instance.SetOpen(!instance.isOpen);
    }

    public static void Open()
    {
        if (instance != null) instance.SetOpen(true);
    }

    public static void CloseIfOpen()
    {
        if (IsOpen) instance.SetOpen(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Update()
    {
        if (isOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) SetOpen(false);
        else if (!TextInputFocus.Active && !PlayerTileController.IsPauseMenuOpen() && GameInput.GetKeyDown(KeyCode.L)) SetOpen(!isOpen);

        if (isOpen && (dirty || Time.unscaledTime >= nextRefresh))
        {
            dirty = false;
            nextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }
    }

    private void Build()
    {
        var hud = new GameObject("LabHud", typeof(RectTransform));
        hud.transform.SetParent(transform, false);
        hudRoot = (RectTransform)hud.transform;
        hudRoot.anchorMin = hudRoot.anchorMax = hudRoot.pivot = new Vector2(0.5f, 1f);
        hudRoot.anchoredPosition = new Vector2(0f, -76f);
        var hudVlg = hud.AddComponent<VerticalLayoutGroup>();
        hudVlg.spacing                = 6f;
        hudVlg.childAlignment         = TextAnchor.UpperCenter;
        hudVlg.childControlWidth      = true;
        hudVlg.childControlHeight     = true;
        hudVlg.childForceExpandWidth  = false;
        hudVlg.childForceExpandHeight = false;
        var hudFit = hud.AddComponent<ContentSizeFitter>();
        hudFit.horizontalFit = hudFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var tab = new GameObject("LabTab", typeof(RectTransform));
        tab.transform.SetParent(transform, false);
        tabRT = (RectTransform)tab.transform;
        tabRT.anchorMin = tabRT.anchorMax = tabRT.pivot = new Vector2(0f, 0.5f);
        tabRT.anchoredPosition = new Vector2(-6f, 30f);
        tabRT.sizeDelta = new Vector2(40f, 112f);
        tab.AddComponent<Image>().color = Color.clear;
        var lip  = CuteUIKit.Layer(tab.transform, "Lip", CuteUIKit.Rounded(14), CuteUIKit.Darker(TabColor, 0.8f));
        lip.rectTransform.offsetMin = lip.rectTransform.offsetMax = new Vector2(0f, -3f);
        var face = CuteUIKit.Layer(tab.transform, "Face", CuteUIKit.Rounded(14), Color.white);
        var tabBtn = tab.AddComponent<Button>();
        tabBtn.targetGraphic = face;
        CuteUIKit.ApplyButtonColors(tabBtn, TabColor);
        tabBtn.onClick.AddListener(Toggle);
        tab.AddComponent<UiButtonSfx>().hover = true;
        tab.AddComponent<CutePop>();
        var tabText = CuteUIKit.Text(tab.transform, "실\n험\n실", 14f, Color.white, FontStyles.Bold);
        tabText.lineSpacing = -12f;
        var ttr = tabText.rectTransform;
        ttr.anchorMin = Vector2.zero;
        ttr.anchorMax = Vector2.one;
        ttr.offsetMin = new Vector2(6f, 22f);
        ttr.offsetMax = new Vector2(0f, -6f);
        var key = CuteUIKit.Pill(tab.transform, "L", CuteUIKit.Hex(0xFFFFFF, 0.9f), TabColor, 9f, 7, new RectOffset(5, 5, 1, 1), out _, fitSelf: true);
        key.anchorMin = key.anchorMax = key.pivot = new Vector2(0.5f, 0f);
        key.anchoredPosition = new Vector2(3f, 8f);

        var panelGO = new GameObject("LabPanel", typeof(RectTransform));
        panelGO.transform.SetParent(transform, false);
        panelRT = (RectTransform)panelGO.transform;
        panelRT.anchorMin = new Vector2(0f, 0f);
        panelRT.anchorMax = new Vector2(0f, 1f);
        panelRT.pivot     = new Vector2(0f, 0.5f);
        panelRT.sizeDelta = new Vector2(PanelWidth, 0f);
        panelRT.offsetMin = new Vector2(panelRT.offsetMin.x, 24f);
        panelRT.offsetMax = new Vector2(panelRT.offsetMax.x, -64f);
        panelGO.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT, catEars: true, earInset: 92f);
        panelCG = panelGO.AddComponent<CanvasGroup>();
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var ribbon = CuteUIKit.TitleRibbon(panelRT, "실험실", "L");
        ribbon.anchorMin = ribbon.anchorMax = ribbon.pivot = new Vector2(0.5f, 1f);
        ribbon.anchoredPosition = new Vector2(0f, -16f);
        var fit = ribbon.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var close = (RectTransform)CuteUIKit.CloseButton(panelRT, Toggle, 28f).transform;
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-14f, -16f);
        close.sizeDelta = new Vector2(28f, 28f);

        var note = CuteUIKit.Text(panelRT, "새로 만드는 중인 놀거리를 먼저 해 보는 곳이에요. 실험실 월드는 저장되지 않아요.\n" +
                                           "친구와 하려면 Esc → [친구 초대] (판정은 호스트가 해요).",
                                  11f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.TopLeft);
        note.textWrappingMode = TextWrappingModes.Normal;
        var nrt = note.rectTransform;
        nrt.anchorMin = new Vector2(0f, 1f);
        nrt.anchorMax = new Vector2(1f, 1f);
        nrt.pivot     = new Vector2(0.5f, 1f);
        nrt.offsetMin = new Vector2(18f, -94f);
        nrt.offsetMax = new Vector2(-18f, -56f);

        var page = new GameObject("Page", typeof(RectTransform));
        page.transform.SetParent(panelRT, false);
        var prt = (RectTransform)page.transform;
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = Vector2.zero;
        prt.offsetMax = new Vector2(0f, -100f);
        var content = HelpUI.BuildScrollView(prt, new Vector2(14f, 16f), new Vector2(-10f, 0f));
        foreach (var exp in Lab.Experiments) BuildCard(content, exp);

        panelRT.anchoredPosition = new Vector2(HiddenX, panelRT.anchoredPosition.y);
    }

    private void BuildCard(RectTransform content, LabExperiment exp)
    {
        var card = CuteUIKit.Card(content, Color.white, 12, $"Card_{exp.Id}");
        card.raycastTarget = false;
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(12, 10, 9, 10);
        vlg.spacing                = 5f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        var head = Row(card.transform, 6f, expand: false);
        head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
        var dot = new GameObject("Dot", typeof(RectTransform));
        dot.transform.SetParent(head, false);
        CuteUIKit.SetRounded(dot.AddComponent<Image>(), 5, exp.Accent).raycastTarget = false;
        var dle = dot.AddComponent<LayoutElement>();
        dle.preferredWidth = dle.preferredHeight = dle.minWidth = 10f;
        CuteUIKit.Text(head, exp.Title, 14f, CuteUIKit.Cocoa, FontStyles.Bold).gameObject.AddComponent<LayoutElement>();
        CuteUIKit.Pill(head, exp.Tags, CuteUIKit.Lighter(exp.Accent, 0.75f), CuteUIKit.Darker(exp.Accent, 0.6f), 9f, 8, new RectOffset(7, 7, 1, 1), out _);
        var spacer = new GameObject("Space", typeof(RectTransform));
        spacer.transform.SetParent(head, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var c = new Card { exp = exp };
        c.toggle = CuteUIKit.Button(head, "켜짐", CuteUIKit.ButtonMint, () => exp.RequestSetOn(!exp.IsOn), 62f, 24f, 11f);
        c.toggleLabel = c.toggle.GetComponentInChildren<TextMeshProUGUI>();

        var summary = CuteUIKit.Text(card.transform, exp.Summary, 11f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.TopLeft);
        summary.textWrappingMode = TextWrappingModes.Normal;
        summary.lineSpacing = 6f;

        var controls = Row(card.transform, 6f, expand: true);
        exp.BuildControls(controls);
        if (controls.childCount == 0) Destroy(controls.gameObject);

        c.status = CuteUIKit.Text(card.transform, "", 11f, CuteUIKit.AmberText, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        c.status.textWrappingMode = TextWrappingModes.Normal;
        cards.Add(c);
    }

    private void Refresh()
    {
        foreach (var c in cards)
        {
            if (c.exp == null) continue;
            bool on = c.exp.IsOn;
            c.toggleLabel.text = on ? "켜짐" : "꺼짐";
            CuteUIKit.ApplyButtonColors(c.toggle, on ? CuteUIKit.ButtonMint : CuteUIKit.ButtonLatte);
            string s = on ? c.exp.Status : "꺼져 있어요";
            if (c.status.text != s) c.status.text = s;
            c.status.gameObject.SetActive(!string.IsNullOrEmpty(s));
        }
    }

    private void SetOpen(bool open)
    {
        if (isOpen == open) return;
        isOpen = open;
        Sfx.Play(open ? SfxId.UiOpen : SfxId.UiClose);
        panelCG.blocksRaycasts = open;
        panelCG.interactable   = open;
        tabRT.gameObject.SetActive(!open);
        if (open)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            Refresh();
        }
        else UIStyleKit.ClearSelectionIn(panelRT);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Slide(open));
    }

    private IEnumerator Slide(bool open)
    {
        float from = panelRT.anchoredPosition.x;
        float to   = open ? ShownX : HiddenX;
        float y    = panelRT.anchoredPosition.y;
        float duration = open ? AnimDuration * 1.5f : AnimDuration;
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            float t = e / duration;
            float k = open ? CuteUIKit.EaseOutBack(t, 1.4f) : t * t;
            panelRT.anchoredPosition = new Vector2(Mathf.LerpUnclamped(from, to, k), y);
            yield return null;
        }
        panelRT.anchoredPosition = new Vector2(to, y);
        animCo = null;
    }

    public static Button Btn(Transform row, string label, Color fill, UnityAction onClick)
        => CuteUIKit.Button(row, label, fill, onClick, -1f, 26f, 11f);

    private static Transform Row(Transform parent, float spacing, bool expand)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = spacing;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = expand;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    public static RectTransform HudCard(string name, float width)
    {
        if (HudRoot == null) return null;
        var card = CuteUIKit.Card(HudRoot, CuteUIKit.Latte, 14, name);
        card.raycastTarget = false;
        CuteUIKit.Layer(card.transform, "Fill", CuteUIKit.Rounded(12), CuteUIKit.Cream, 2f);
        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(14, 14, 7, 8);
        vlg.spacing                = 2f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        var le = card.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        return card.rectTransform;
    }
}
