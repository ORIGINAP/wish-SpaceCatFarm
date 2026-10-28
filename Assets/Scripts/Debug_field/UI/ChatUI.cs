using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ChatUI : MonoBehaviour
{
    private static ChatUI instance;

    private GameObject      panel;
    private TMP_InputField  input;
    private TextMeshProUGUI hint;
    // 닫은 Enter로 다시 열리지 않게
    private int             closedFrame = -1;
    private Coroutine       popCo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        TryCreate();
        // 재시작 시 중복 구독 방지
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TryCreate();

    private static void TryCreate()
    {
        if (instance != null || GridManager.Instance == null) return;
        var go = new GameObject("ChatUI");
        instance = go.AddComponent<ChatUI>();
        instance.Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Update()
    {
        if (!panel.activeSelf)
        {
            if (Time.frameCount != closedFrame && !TextInputFocus.Active && !PlayerTileController.IsPauseMenuOpen() &&
                (GameInput.GetKeyDown(KeyCode.Return) || GameInput.GetKeyDown(KeyCode.KeypadEnter)))
                Open();
        }
        else
        {
            if (GameInput.GetKeyDown(KeyCode.Escape)) Close();
            else if (!input.isFocused && !MobileMode.Active) input.ActivateInputField();
        }

        if (panel.activeSelf || closedFrame == Time.frameCount) TextInputFocus.MarkTyping();
    }

    private void Open()
    {
        panel.SetActive(true);
        input.text = "";
        input.ActivateInputField();
        string keys = MobileMode.Active ? "키보드 완료: 보내기   뒤로 가기: 닫기" : "Enter: 보내기   Esc: 닫기";
        hint.text = NetSession.Mode == NetSession.Role.Offline ? keys : keys + "   ·   함께하는 모두에게 보여요";
        TextInputFocus.MarkTyping();
        Sfx.Play(SfxId.UiOpen);

        var prt = (RectTransform)panel.transform;
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.PopIn(prt, null, 0.24f));
        CuteUIKit.WiggleEars(this, prt);
    }

    private void Close()
    {
        input.DeactivateInputField();
        panel.SetActive(false);
        closedFrame = Time.frameCount;
        Sfx.Play(SfxId.UiClose);
    }

    private void OnSubmit(string text)
    {
        if (!panel.activeSelf) return;
        Close();
        if (!string.IsNullOrWhiteSpace(text)) NetBootstrap.SendChat(text);
    }

    private void Build()
    {
        UIStyleKit.CreateOverlayCanvas(gameObject, 500);

        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 130f);
        prt.sizeDelta        = new Vector2(580f, 80f);
        panel.AddComponent<Image>();
        CuteUIKit.DecorateWindow(prt);

        var boxGO = new GameObject("InputBox", typeof(RectTransform), typeof(Image));
        boxGO.transform.SetParent(panel.transform, false);
        var brt = (RectTransform)boxGO.transform;
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot     = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -12f);
        brt.sizeDelta        = new Vector2(-28f, 38f);

        var areaGO = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        areaGO.transform.SetParent(boxGO.transform, false);
        var art = (RectTransform)areaGO.transform;
        art.anchorMin = Vector2.zero;         art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(14f, 2f); art.offsetMax = new Vector2(-14f, -2f);

        var placeholder = Stretch(CuteUIKit.Text(areaGO.transform, "채팅 입력...", 17f, CuteUIKit.CocoaMuted, align: TextAlignmentOptions.MidlineLeft));
        placeholder.fontStyle = FontStyles.Italic;
        var text = Stretch(CuteUIKit.Text(areaGO.transform, "", 17f, CuteUIKit.Cocoa, align: TextAlignmentOptions.MidlineLeft));

        hint = Stretch(CuteUIKit.Text(panel.transform, "", 12f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.MidlineLeft));
        var hrt = hint.rectTransform;
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = new Vector2(1f, 0f);
        hrt.offsetMin = new Vector2(20f, 8f); hrt.offsetMax = new Vector2(-20f, 28f);

        input = boxGO.AddComponent<TMP_InputField>();
        input.textViewport   = art;
        input.textComponent  = text;
        input.placeholder    = placeholder;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.characterLimit = NetBootstrap.MaxChatLength;
        input.richText       = false;
        input.onSubmit.AddListener(OnSubmit);
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;

        panel.SetActive(false);
    }

    private static TextMeshProUGUI Stretch(TextMeshProUGUI t)
    {
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }
}
