using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class TutorialUI : MonoBehaviour
{
    private const string PREFS_KEY = "IntroCompleted_v1";

    [Header("디버그")]
    [Tooltip("체크하면 새 월드가 아니어도 항상 인트로를 표시합니다")]
    [SerializeField] private bool forceShow = false;

    private const string Title = "행성 이주 프로토콜 발동";
    private const string CocoLine = "대원님, 고향 고양고양이 행성의 자원이 바닥나 행성 이주 프로토콜이 시작됐어요! " +
                                    "우리가 선발대로 이 불모지에 먼저 내려왔어요. 이 땅을 개척해서 고양이들이 살 새 보금자리를 만들어요.";

    private static TutorialUI current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    public static bool IsShowing => current != null && current.isActiveAndEnabled && current.panelCG != null && current.panelCG.alpha > 0.05f;

    public static bool IsActive => current != null && current.isActiveAndEnabled;

    public static Transform Root => current != null ? current.panelRT : null;

    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private RectTransform controls;
    private bool          closing;

    private void Awake()
    {
        current = this;
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        BuildDialog();
        transform.SetAsLastSibling();

        // 새 월드만, 참가자는 제외
        bool newWorld = GameLaunchContext.IsNewGame && !NetSession.IsReplica && !Lab.Active;
        if (!forceShow && !newWorld)
        {
            gameObject.SetActive(false);
            return;
        }

        panelCG.alpha       = 0f;
        panelRT.localScale  = new Vector3(0.85f, 0.85f, 1f);
    }

    private void Start()
    {
        if (!gameObject.activeSelf) return;
        StartCoroutine(OpenDialog());
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
    }

    private IEnumerator OpenDialog()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        FillControls();
        CuteUIKit.WiggleEars(this, panelRT);
        yield return FadePanel(true, 0.3f);
    }

    private void Close()
    {
        if (closing) return;
        closing = true;
        PlayerPrefs.SetInt(PREFS_KEY, 1);
        PlayerPrefs.Save();
        StopAllCoroutines();
        StartCoroutine(FadeAndClose());
    }

    private IEnumerator FadeAndClose()
    {
        yield return FadePanel(false, 0.16f);
        gameObject.SetActive(false);
    }

    private IEnumerator FadePanel(bool show, float duration = 0.18f)
    {
        float from = show ? 0f : 1f;
        float to   = show ? 1f : 0f;
        float scaleFrom = show ? 0.88f : 1f;
        float scaleTo   = show ? 1f    : 0.88f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p    = Mathf.Clamp01(t / duration);
            float ease = show ? CuteUIKit.EaseOutBack(p, 2.2f) : p * p;
            panelCG.alpha      = Mathf.Lerp(from, to, ease);
            panelRT.localScale = Vector3.one * Mathf.LerpUnclamped(scaleFrom, scaleTo, ease);
            yield return null;
        }
        panelCG.alpha      = to;
        panelRT.localScale = Vector3.one * scaleTo;
    }

    private void BuildDialog()
    {
        var dialogGO = new GameObject("DialogPanel", typeof(RectTransform));
        dialogGO.transform.SetParent(transform, false);

        panelRT = (RectTransform)dialogGO.transform;
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 40f);
        panelRT.sizeDelta        = new Vector2(680f, 0f);

        dialogGO.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = dialogGO.AddComponent<CanvasGroup>();

        var vlg = dialogGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(22, 22, 18, 20);
        vlg.spacing               = 12f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;

        var csf = dialogGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var titleRowGO = NewRow(dialogGO.transform, "TitleRow");
        CuteUIKit.TitleRibbon(titleRowGO, Title, null, 18f);
        AddSpacer(titleRowGO);
        CuteUIKit.Button(titleRowGO, "건너뛰기", CuteUIKit.ButtonLatte, Close, 86f, 28f, 12f).name = "SkipBtn";

        CocoUI.Row(dialogGO.transform, CocoLine, 72f, 17f);

        var flow = NewRow(dialogGO.transform, "Flow");
        flow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        var stage7 = CoreStageTable.Load()?.Stage(7);
        FlowCard(flow, "1", "농사", "씨앗을 심고\n다 자라면 거둬요", NetWorld.Item("BlueStar")?.icon, CuteUIKit.MintTag, CuteUIKit.MintText);
        Arrow(flow);
        FlowCard(flow, "2", "셔틀", "작물을 실어 보내면\n고양이들이 건너와요", ShuttleUI.ShipSprite ?? HudDock.RocketIcon, CuteUIKit.ButterSoft, CuteUIKit.AmberText);
        Arrow(flow);
        FlowCard(flow, "3", "문명", "주민이 늘면\n기지가 도시로 커져요", stage7 != null ? stage7.spriteOn : null, CuteUIKit.PinkSoft, CuteUIKit.CoralText);

        var guide = CuteUIKit.Text(dialogGO.transform, "<b>왼쪽 위 퀘스트</b>와 <b>분홍 화살표</b>가 할 일을 하나씩 알려 줄게요.", 15f, CuteUIKit.Cocoa);
        guide.richText = true;

        CuteUIKit.DottedDivider(dialogGO.transform);

        var controlsGO = new GameObject("Controls", typeof(RectTransform));
        controlsGO.transform.SetParent(dialogGO.transform, false);
        var cvl = controlsGO.AddComponent<VerticalLayoutGroup>();
        cvl.spacing = 6f;
        cvl.childAlignment = TextAnchor.UpperCenter;
        cvl.childControlWidth = cvl.childControlHeight = true;
        cvl.childForceExpandWidth = false;
        cvl.childForceExpandHeight = false;
        controls = (RectTransform)controlsGO.transform;

        var bottomRowGO = NewRow(dialogGO.transform, "BottomRow");
        bottomRowGO.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
        var startBtn = CuteUIKit.Button(bottomRowGO, "시작!", CuteUIKit.ButtonMint, Close, 200f, 44f, 18f);
        startBtn.name = "NextBtn";
        startBtn.gameObject.AddComponent<GamepadDefault>();
    }

    private static void FlowCard(Transform parent, string number, string title, string body, Sprite icon, Color tag, Color tagText)
    {
        var card = CuteUIKit.Card(parent, CuteUIKit.LatteLight, 16, "Step_" + title);
        card.raycastTarget = false;
        var vl = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(10, 10, 12, 12);
        vl.spacing = 7f;
        vl.childAlignment = TextAnchor.UpperCenter;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = false;
        vl.childForceExpandHeight = false;
        var le = card.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 176f;

        var holder = new GameObject("Icon", typeof(RectTransform));
        holder.transform.SetParent(card.transform, false);
        var hle = holder.AddComponent<LayoutElement>();
        hle.preferredWidth = hle.minWidth = 64f;
        hle.preferredHeight = hle.minHeight = 64f;
        var img = new GameObject("Image", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(holder.transform, false);
        img.sprite = icon;
        img.enabled = icon != null;
        img.preserveAspect = true;
        img.raycastTarget = false;
        var irt = img.rectTransform;
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = irt.offsetMax = Vector2.zero;

        CuteUIKit.Pill(card.transform, $"{number}. {title}", tag, tagText, 15f, 10, new RectOffset(10, 10, 2, 3), out _);
        var t = CuteUIKit.Text(card.transform, body, 14f, CuteUIKit.Cocoa);
        t.lineSpacing = 4f;
    }

    private static void Arrow(Transform parent)
    {
        var t = CuteUIKit.Text(parent, ">", 18f, CuteUIKit.Strawberry, FontStyles.Bold);
        t.gameObject.AddComponent<LayoutElement>().preferredWidth = 22f;
    }

    private void FillControls()
    {
        for (int i = controls.childCount - 1; i >= 0; i--) Destroy(controls.GetChild(i).gameObject);
        bool pad = Pad.Active;
        var row1 = NewRow(controls, "Keys1");
        var row2 = NewRow(controls, "Keys2");
        if (pad)
        {
            Key(row1, "왼쪽 스틱", "이동");
            Key(row1, "RT", "심기 · 캐기 · 수확");
            Key(row1, "LT 꾹", "해체");
            Key(row2, "Y", "가방");
            Key(row2, "십자키 ↑", "제작");
            Key(row2, "LB / RB", "핫바 · 창 넘기기");
        }
        else
        {
            Key(row1, "WASD", "이동");
            Key(row1, "좌클릭", "심기 · 캐기 · 수확");
            Key(row1, "우클릭 꾹", "해체");
            Key(row2, "Tab", "가방");
            Key(row2, "C", "제작");
            Key(row2, "L", "여정");
            Key(row2, "1~7", "핫바");
        }
    }

    private static void Key(Transform row, string key, string what)
    {
        CuteUIKit.Pill(row, key, CuteUIKit.Cocoa, Color.white, 13f, 8, new RectOffset(8, 8, 2, 3), out _);
        var t = CuteUIKit.Text(row, what, 13f, CuteUIKit.CocoaSoft);
        t.gameObject.AddComponent<LayoutElement>().minWidth = 10f;
        var gap = new GameObject("Gap", typeof(RectTransform));
        gap.transform.SetParent(row, false);
        gap.AddComponent<LayoutElement>().preferredWidth = 8f;
    }

    private static RectTransform NewRow(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 8f;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        return (RectTransform)go.transform;
    }

    private static void AddSpacer(Transform parent)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }
}
