using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 다른 입력창 타이핑 중 단축키 막기
public static class TextInputFocus
{
    private static bool chatActive;
    private static int  typingFrame = -10;

    public static bool Active
    {
        get => chatActive || Time.frameCount - typingFrame <= 1;
        internal set => chatActive = value;
    }

    public static void MarkTyping() => typingFrame = Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        chatActive  = false;
        typingFrame = -10;
    }
}

public class NpcChatUI : MonoBehaviour
{
    public const KeyCode OpenKey = KeyCode.K;

    private static NpcChatUI instance;

    private AINpc          npc;
    private GameObject     panel;
    private TMP_InputField input;
    private TextMeshProUGUI hint;
    private int            closedFrame = -1;
    private Coroutine      popCo;

    private readonly Image[]           personaButtons = new Image[2];
    private readonly TextMeshProUGUI[] personaLabels  = new TextMeshProUGUI[2];
    private static readonly Color PersonaOn      = CuteUIKit.Strawberry;
    private static readonly Color PersonaOff     = CuteUIKit.ButtonLatte;
    private static readonly Color PersonaOnText  = Color.white;
    private static readonly Color PersonaOffText = CuteUIKit.Cocoa;

    public static void Ensure(AINpc npc)
    {
        if (instance == null)
        {
            var go = new GameObject("NpcChatUI");
            instance = go.AddComponent<NpcChatUI>();
            instance.Build();
        }
        instance.npc = npc;
    }

    public static void DestroyInstance()
    {
        if (instance != null) Destroy(instance.gameObject);
        instance = null;
        TextInputFocus.Active = false;
    }

    private void OnDestroy()
    {
        if (instance == this) { instance = null; TextInputFocus.Active = false; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Update()
    {
        if (npc == null)
        {
            if (panel.activeSelf) Close();
            TextInputFocus.Active = false;
            return;
        }
        bool open = panel.activeSelf;

        if (!open)
        {
            if (Time.frameCount != closedFrame && !TextInputFocus.Active && !PlayerTileController.IsPauseMenuOpen() &&
                GameInput.GetKeyDown(OpenKey))
                Open();
        }
        else
        {
            if (GameInput.GetKeyDown(KeyCode.Escape)) Close();
            else if (!input.isFocused && !MobileMode.Active) input.ActivateInputField();
            hint.text = npc.IsThinking ? $"{AINpc.NpcName}가 생각 중..."
                      : MobileMode.Active ? "키보드 완료: 보내기   뒤로 가기: 닫기" : "Enter: 보내기   Esc: 닫기";
            for (int i = 0; i < personaButtons.Length; i++)
            {
                bool on = (int)npc.Persona == i + 1;
                personaButtons[i].color = on ? PersonaOn : PersonaOff;
                personaLabels[i].color  = on ? PersonaOnText : PersonaOffText;
            }
        }

        TextInputFocus.Active = panel.activeSelf || closedFrame == Time.frameCount;
    }

    private void Open()
    {
        panel.SetActive(true);
        input.text = "";
        input.ActivateInputField();
        TextInputFocus.Active = true;
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
        if (!string.IsNullOrWhiteSpace(text)) npc.HandlePlayerMessage(text);
    }

    private void Build()
    {
        UIStyleKit.CreateOverlayCanvas(gameObject, 500);

        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 130f);
        prt.sizeDelta        = new Vector2(580f, 86f);
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

        var placeholder = MakeText(areaGO.transform, $"{AINpc.NpcName}에게 부탁하기... (예: 철광석 5개 캐줘)", 17f);
        placeholder.fontStyle = FontStyles.Italic;
        var text = MakeText(areaGO.transform, "", 17f);

        hint = MakeText(panel.transform, "", 12f);
        hint.color = CuteUIKit.CocoaSoft;
        var hrt = hint.rectTransform;
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = new Vector2(1f, 0f);
        hrt.offsetMin = new Vector2(20f, 9f); hrt.offsetMax = new Vector2(-250f, 31f);

        BuildPersonaButtons();

        input = boxGO.AddComponent<TMP_InputField>();
        input.textViewport   = art;
        input.textComponent  = text;
        input.placeholder    = placeholder;
        input.lineType       = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 120;
        input.richText       = false;
        input.onSubmit.AddListener(OnSubmit);
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;

        panel.SetActive(false);
    }

    private void BuildPersonaButtons()
    {
        const float height = 22f;
        var row = new GameObject("Persona", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);
        var rrt = (RectTransform)row.transform;
        rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(1f, 0f);
        rrt.anchoredPosition = new Vector2(-14f, 9f);
        rrt.sizeDelta        = new Vector2(236f, height);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 4f;
        hlg.childAlignment         = TextAnchor.MiddleRight;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        var label = MakeText(row.transform, "성격", 11f);
        label.color = CuteUIKit.CocoaSoft;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;

        for (int i = 0; i < personaButtons.Length; i++)
        {
            var type = (NpcPersonaType)(i + 1);
            var go = new GameObject($"Btn_{type}", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            go.AddComponent<LayoutElement>().preferredWidth = 56f;
            personaButtons[i] = CuteUIKit.SetRounded(go.AddComponent<Image>(), Mathf.RoundToInt(height * 0.5f), PersonaOff);
            go.AddComponent<Button>().onClick.AddListener(() => { if (npc != null && npc.Persona != type) npc.SetPersona(type); });
            go.AddComponent<CutePop>();

            var t = MakeText(go.transform, NpcPersona.DisplayName(type), 11f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.color     = PersonaOffText;
            personaLabels[i] = t;
        }
    }

    private static TextMeshProUGUI MakeText(Transform parent, string content, float size)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text      = content;
        t.fontSize  = size;
        t.color     = Color.white;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }
}
