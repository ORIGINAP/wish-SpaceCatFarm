using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class EndingUI : MonoBehaviour
{
    private RectTransform   panelRT;
    private CanvasGroup     rootCG;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI messageText;
    private readonly Transform[] stars = new Transform[3];
    private bool            isShowing;

    private static EndingUI current;

    public static bool IsShowing => current != null && current.isShowing;

    public static Transform Root => current != null ? current.transform : null;

    private const string Title =
        "행성 이주 완료!";

    private static string Message =>
        "선발대가 처음 내려앉은 불모지가 고양이들의 새 고향이 되었어요.\n" +
        $"셔틀로 먼저 건너온 주민 {ShuttleService.Population}마리에 이어, 큰 이주선이\n" +
        "고향별에 남은 고양이들을 모두 태우고 착륙장에 내려앉았어요.\n" +
        "행성 이주 프로토콜 완료 — 고양이 대원, 임무를 완수했어요!";

    private void Awake()
    {
        current = this;
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        BuildDialog();
        transform.SetAsLastSibling();

        rootCG.alpha = 0f;
        panelRT.localScale = new Vector3(0.85f, 0.85f, 1f);
        SetInteractable(false);
    }

    private void Start()
    {
        CenterCore.OnAnyLevelUp += HandleCoreLevelUp;
    }

    private void OnDestroy()
    {
        // static 이벤트라 꼭 해제
        CenterCore.OnAnyLevelUp -= HandleCoreLevelUp;
        if (current == this) current = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => current = null;

    private void HandleCoreLevelUp(CenterCore core, int level)
    {
        if (core != null && core.IsMaxLevel) StartCoroutine(ShowAfterLanding());
    }

    private IEnumerator ShowAfterLanding()
    {
        yield return null;
        for (float waited = 0f; MigrationShipDock.Landing && waited < 15f; waited += Time.unscaledDeltaTime)
            yield return null;
        ShowEnding();
    }

    private void Update()
    {
        if (isShowing && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Close();
    }

    private void ShowEnding()
    {
        StopAllCoroutines();
        isShowing = true;
        SetInteractable(true);
        if (messageText != null) messageText.text = Message;
        CuteUIKit.WiggleEars(this, panelRT);
        StartCoroutine(CuteUIKit.PopIn(panelRT, rootCG, 0.4f));
        StartCoroutine(PopStars());
    }

    private void Close()
    {
        StopAllCoroutines();
        isShowing = false;
        StartCoroutine(CloseRoutine());
    }

    private IEnumerator CloseRoutine()
    {
        yield return CuteUIKit.PopOut(panelRT, rootCG, 0.2f, hideAtEnd: false);
        SetInteractable(false);
    }

    private void SetInteractable(bool on)
    {
        rootCG.blocksRaycasts = on;
        rootCG.interactable   = on;
    }

    private IEnumerator PopStars()
    {
        foreach (var s in stars) s.localScale = Vector3.zero;
        yield return new WaitForSecondsRealtime(0.25f);
        foreach (var s in stars)
        {
            StartCoroutine(CuteUIKit.ScaleTo(s, 1f, 0.3f, 3f));
            yield return new WaitForSecondsRealtime(0.12f);
        }
    }

    private void BuildDialog()
    {
        var selfRT = GetComponent<RectTransform>();
        selfRT.anchorMin = Vector2.zero;
        selfRT.anchorMax = Vector2.one;
        selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;

        rootCG = gameObject.AddComponent<CanvasGroup>();

        var dimGO = new GameObject("Dim");
        dimGO.transform.SetParent(transform, false);
        var dimRT = dimGO.AddComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = dimRT.offsetMax = Vector2.zero;
        var dimImg = dimGO.AddComponent<Image>();
        dimImg.color = CuteUIKit.Overlay;
        dimImg.raycastTarget = false;

        var dialogGO = new GameObject("DialogPanel");
        dialogGO.transform.SetParent(transform, false);

        panelRT = dialogGO.AddComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta        = new Vector2(520f, 0f);

        dialogGO.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        var vlg = dialogGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(26, 26, 20, 26);
        vlg.spacing               = 12f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;

        var csf = dialogGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var ribbon = CuteUIKit.TitleRibbon(Centered(dialogGO.transform), Title, fontSize: 18f);
        titleText = ribbon.GetComponentInChildren<TextMeshProUGUI>();
        BuildStars(dialogGO.transform);

        CuteUIKit.DottedDivider(dialogGO.transform);

        var card = CuteUIKit.Card(dialogGO.transform, CuteUIKit.LatteLight, name: "MessageCard");
        card.raycastTarget = false;
        var cardVlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cardVlg.padding                = new RectOffset(18, 18, 14, 14);
        cardVlg.childForceExpandWidth  = true;
        cardVlg.childForceExpandHeight = false;
        cardVlg.childControlWidth      = true;
        cardVlg.childControlHeight     = true;

        var msgGO = new GameObject("Message");
        msgGO.transform.SetParent(card.transform, false);
        messageText = msgGO.AddComponent<TextMeshProUGUI>();
        messageText.text      = Message;
        messageText.fontSize  = 14f;
        messageText.color     = CuteUIKit.Cocoa;
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.textWrappingMode = TextWrappingModes.Normal;
        messageText.raycastTarget = false;
        messageText.lineSpacing = 8f;

        CuteUIKit.Button(Centered(dialogGO.transform), "확인", CuteUIKit.ButtonMint, Close, 200f, 40f, 15f);
    }

    private void BuildStars(Transform parent)
    {
        var badge = CuteUIKit.Card(Centered(parent), CuteUIKit.Cocoa, 18, "Stars");
        badge.raycastTarget = false;
        var hlg = badge.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(16, 16, 2, 3);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        for (int i = 0; i < stars.Length; i++)
        {
            var star = CuteUIKit.Text(badge.transform, "*", i == 1 ? 26f : 20f, CuteUIKit.Butter).transform;
            star.localRotation = Quaternion.Euler(0f, 0f, (1 - i) * 12f);
            stars[i] = star;
        }
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
}
