using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class OptionsUI : MonoBehaviour
{
    private static OptionsUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static bool IsOpen => instance != null && instance.isOpen;

    public static Transform Root => instance != null ? instance.transform : null;

    public static void Open()
    {
        if (instance == null)
        {
            instance = new GameObject("OptionsUI").AddComponent<OptionsUI>();
            instance.Build();
        }
        instance.Show();
    }

    // 채팅창 위, 가상 커서 아래
    private const int   SortingOrder = 600;
    private const float PanelWidth   = 560f;
    private const float LabelWidth   = 130f;

    private Canvas        canvas;
    private CanvasGroup   rootCG;
    private RectTransform panelRT;
    private bool          isOpen;
    private Coroutine     animCo;

    private Slider          volumeSlider, musicSlider, sfxSlider, vibrationSlider;
    private TextMeshProUGUI volumeValue, musicValue, sfxValue, vibrationValue, resolutionValue, screenModeValue, uiScaleValue, applyNote, aiLabel, keyNote;
    private Button          applyButton, aiButton;
    private TMP_InputField  apiKeyInput;

    private const string KeyNoteDefault = "API 키는 이 PC에만 저장되고 *로 가려 보입니다. 비워 두면 env 파일의 키를 씁니다.";

    private List<Vector2Int> resolutions = new();
    private int resIndex, modeIndex;
    private float nextSfxPreview, nextRumblePreview;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (!isOpen) return;
        // 키 입력 중 단축키 막기
        if (apiKeyInput.isFocused) TextInputFocus.MarkTyping();
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Hide(); return; }
        UIStyleKit.KeepNavigable(volumeSlider.gameObject, panelRT);
    }

    private void Show()
    {
        LoadCurrentValues();
        if (!isOpen) Sfx.Play(SfxId.UiOpen);
        isOpen = true;
        StartAnim(true);
    }

    private void Hide()
    {
        if (!isOpen) return;
        isOpen = false;
        Sfx.Play(SfxId.UiClose);
        GameSettings.Flush();
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

    private void LoadCurrentValues()
    {
        SetSlider(volumeSlider, volumeValue, GameSettings.MasterVolume);
        SetSlider(musicSlider,  musicValue,  GameSettings.MusicVolume);
        SetSlider(sfxSlider,    sfxValue,    GameSettings.SfxVolume);
        SetSlider(vibrationSlider, vibrationValue, GameSettings.VibrationStrength);
        if (GameSettings.VibrationStrength <= 0.001f) vibrationValue.text = "끔";

        resolutions = GameSettings.GetResolutionOptions();
        resIndex    = Mathf.Max(0, resolutions.IndexOf(GameSettings.SavedResolution));
        modeIndex   = Mathf.Max(0, System.Array.IndexOf(GameSettings.ScreenModes, GameSettings.SavedScreenMode));

        applyNote.text = "";
        apiKeyInput.SetTextWithoutNotify(GameSettings.AiApiKey);
        keyNote.text = KeyNoteDefault;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var res = resolutions[resIndex];
        resolutionValue.text = GameSettings.ResolutionName(res);
        screenModeValue.text = GameSettings.ScreenModeName(GameSettings.ScreenModes[modeIndex]);
        uiScaleValue.text    = Percent(UiScale.UserScale);
        applyButton.interactable = HasPendingDisplayChange;

        bool ai = GameSettings.AiNpcEnabled;
        aiLabel.text = ai ? "사용" : "사용 안 함";
        UIStyleKit.ApplyButtonColors(aiButton, ai ? UIStyleKit.ButtonConfirm : UIStyleKit.ButtonNeutral);
    }

    private bool HasPendingDisplayChange =>
        resolutions[resIndex] != GameSettings.SavedResolution ||
        GameSettings.ScreenModes[modeIndex] != GameSettings.SavedScreenMode;

    private static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

    private static void SetSlider(Slider slider, TextMeshProUGUI valueText, float v)
    {
        slider.SetValueWithoutNotify(v);
        valueText.text = Percent(v);
    }

    private void OnVolumeChanged(float v)
    {
        GameSettings.MasterVolume = v;
        volumeValue.text = Percent(v);
    }

    private void OnMusicChanged(float v)
    {
        GameSettings.MusicVolume = v;
        musicValue.text = Percent(v);
    }

    private void OnSfxChanged(float v)
    {
        GameSettings.SfxVolume = v;
        sfxValue.text = Percent(v);

        if (Time.unscaledTime < nextSfxPreview) return;
        nextSfxPreview = Time.unscaledTime + 0.15f;
        Sfx.Play(SfxId.UiConfirm);
    }

    private void OnVibrationChanged(float v)
    {
        GameSettings.VibrationStrength = v;
        vibrationValue.text = v <= 0.001f ? "끔" : Percent(v);

        if (Time.unscaledTime < nextRumblePreview) return;
        nextRumblePreview = Time.unscaledTime + 0.2f;
        GamepadRumble.Preview();
    }

    private void StepResolution(int dir)
    {
        resIndex = (resIndex + dir + resolutions.Count) % resolutions.Count;
        applyNote.text = "";
        RefreshDisplay();
    }

    private void StepScreenMode(int dir)
    {
        int n = GameSettings.ScreenModes.Length;
        modeIndex = (modeIndex + dir + n) % n;
        applyNote.text = "";
        RefreshDisplay();
    }

    private void ApplyDisplay()
    {
        GameSettings.ApplyResolution(resolutions[resIndex], GameSettings.ScreenModes[modeIndex]);
#if UNITY_EDITOR
        applyNote.text = "저장했습니다. 에디터에서는 창 크기가 바뀌지 않고 빌드에서 적용됩니다.";
#else
        applyNote.text = "적용했습니다.";
#endif
        RefreshDisplay();
    }

    private void StepUiScale(int dir)
    {
        UiScale.SetUserScale(UiScale.UserScale + dir * 0.05f);
        RefreshDisplay();
    }

    private void ToggleAi()
    {
        GameSettings.AiNpcEnabled = !GameSettings.AiNpcEnabled;
        RefreshDisplay();
    }

    private void SaveApiKey(string key)
    {
        string before = GameSettings.AiApiKey;
        GameSettings.AiApiKey = key;
        string after = GameSettings.AiApiKey;
        if (after == before) return;

        keyNote.text = after.Length > 0
            ? "키를 저장했어요. 이제 코코가 이 키로 대화합니다."
            : "입력한 키를 지웠어요. env 파일에 키가 있으면 그 키를 씁니다.";
        Sfx.Play(SfxId.UiConfirm);
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
        var panelCG = UIStyleKit.CreatePanelRoot(panel, PanelWidth, new RectOffset(28, 28, 22, 24), spacing: 12f);
        panelCG.alpha          = 1f;
        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;

        var p = panel.transform;
        CuteUIKit.TitleRibbon(Centered(p), "옵션", fontSize: 18f);

        Section(p, "사운드", CuteUIKit.MintTag, CuteUIKit.MintText);
        volumeSlider = VolumeRow(p, "마스터 볼륨", OnVolumeChanged, out volumeValue);
        musicSlider  = VolumeRow(p, "배경음",      OnMusicChanged,  out musicValue);
        sfxSlider    = VolumeRow(p, "효과음",      OnSfxChanged,    out sfxValue);
        UIStyleKit.CreateDivider(p);

        Section(p, "화면", CuteUIKit.SkyTag, CuteUIKit.SkyText);
        resolutionValue = Selector(LabeledRow(p, "해상도"),   () => StepResolution(-1), () => StepResolution(+1));
        var modeRow     = LabeledRow(p, "화면 모드");
        screenModeValue = Selector(modeRow, () => StepScreenMode(-1), () => StepScreenMode(+1));
        modeRow.gameObject.SetActive(!MobileMode.Active);

        var applyRow = Row(p);
        applyNote = ValueText(applyRow, TextAlignmentOptions.MidlineLeft);
        applyNote.fontSize  = 11f;
        applyNote.fontStyle = FontStyles.Normal;
        applyNote.color     = UIStyleKit.TextSecondary;
        applyNote.textWrappingMode = TextWrappingModes.Normal;
        applyNote.GetComponent<LayoutElement>().flexibleWidth = 1f;
        applyButton = UIStyleKit.CreateButton(applyRow, "적용", UIStyleKit.ButtonConfirm, ApplyDisplay, preferredWidth: 110f, preferredHeight: 30f);

        var uiRow    = LabeledRow(p, "UI 크기");
        uiScaleValue = Selector(uiRow, () => StepUiScale(-1), () => StepUiScale(+1));
        uiRow.gameObject.SetActive(!MobileMode.Active);
        UIStyleKit.CreateDivider(p);

        Section(p, "게임패드", CuteUIKit.ButterSoft, CuteUIKit.AmberText);
        vibrationSlider = VolumeRow(p, "진동 세기", OnVibrationChanged, out vibrationValue);
        UIStyleKit.CreateDivider(p);

        Section(p, "AI 조수", CuteUIKit.LavenderTag, CuteUIKit.LavenderText);
        var aiRow = LabeledRow(p, "AI 코코");
        aiButton = UIStyleKit.CreateButton(aiRow, "사용", UIStyleKit.ButtonConfirm, ToggleAi, preferredHeight: 30f);
        aiLabel  = aiButton.GetComponentInChildren<TextMeshProUGUI>();
        apiKeyInput = KeyInput(LabeledRow(p, "API 키"));
        apiKeyInput.onEndEdit.AddListener(SaveApiKey);
        Note(p, "코코와의 대화는 외부 AI 서버를 거칩니다. 끄면 코코와 채팅창이 사라지고 서버에 요청을 보내지 않습니다.");
        keyNote = Note(p, KeyNoteDefault);

        UIStyleKit.CreateDivider(p);
        UIStyleKit.CreateButton(p, "닫기", UIStyleKit.ButtonNeutral, Hide, preferredHeight: 36f);

        panelRT.anchoredPosition = Vector2.zero;
        canvas.enabled = false;
    }

    private static void Section(Transform parent, string title, Color tagColor, Color textColor)
    {
        CuteUIKit.SectionHeader(parent, title, tagColor, textColor);
    }

    private static TextMeshProUGUI Note(Transform parent, string text)
    {
        var t = FullWidthText(parent, text, 11f, UIStyleKit.TextSecondary, FontStyles.Normal);
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
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

    private static Transform Row(Transform parent)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 10f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        go.AddComponent<LayoutElement>().minHeight = 32f;
        return go.transform;
    }

    private static Transform LabeledRow(Transform parent, string label)
    {
        var row = Row(parent);
        row.name = $"Row_{label}";
        var t = UIStyleKit.CreateText(row, label, 14f, UIStyleKit.TextPrimary);
        t.GetComponent<LayoutElement>().preferredWidth = LabelWidth;
        return row;
    }

    private static TextMeshProUGUI ValueText(Transform parent, TextAlignmentOptions align)
    {
        var t = UIStyleKit.CreateText(parent, "", 14f, UIStyleKit.TextPrimary, FontStyles.Bold, align);
        DestroyImmediate(t.GetComponent<ContentSizeFitter>());
        return t;
    }

    private static Slider VolumeRow(Transform parent, string label, UnityAction<float> onChanged, out TextMeshProUGUI valueText)
    {
        var row    = LabeledRow(parent, label);
        var slider = CreateSlider(row, $"Slider_{label}");
        slider.onValueChanged.AddListener(onChanged);
        valueText = ValueText(row, TextAlignmentOptions.MidlineRight);
        valueText.GetComponent<LayoutElement>().preferredWidth = 52f;
        return slider;
    }

    private static TextMeshProUGUI Selector(Transform row, UnityAction prev, UnityAction next)
    {
        const float size = 30f;
        CuteUIKit.Button(row, "<", CuteUIKit.ButtonSky, prev, size, size, 12f);

        var box = CuteUIKit.Card(row, CuteUIKit.LatteLight, Mathf.RoundToInt(size * 0.5f), "ValueBox");
        box.raycastTarget = false;
        var boxLE = box.gameObject.AddComponent<LayoutElement>();
        boxLE.flexibleWidth   = 1f;
        boxLE.preferredHeight = size;

        var value = ValueText(box.transform, TextAlignmentOptions.Center);
        var vrt = value.rectTransform;
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = vrt.offsetMax = Vector2.zero;

        CuteUIKit.Button(row, ">", CuteUIKit.ButtonSky, next, size, size, 12f);
        return value;
    }

    private static TMP_InputField KeyInput(Transform row)
    {
        var box = new GameObject("KeyInput", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(row, false);
        var le = box.AddComponent<LayoutElement>();
        le.flexibleWidth   = 1f;
        le.preferredHeight = 30f;

        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(box.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero;
        art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(12f, 2f);
        art.offsetMax = new Vector2(-12f, -2f);

        var ph   = Stretch(CuteUIKit.Text(area.transform, "키를 붙여 넣으세요 (Ctrl+V)", 12f, CuteUIKit.CocoaMuted, FontStyles.Italic, TextAlignmentOptions.MidlineLeft));
        var text = Stretch(CuteUIKit.Text(area.transform, "", 14f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft));

        var input = box.AddComponent<TMP_InputField>();
        input.textViewport    = art;
        input.textComponent   = text;
        input.placeholder     = ph;
        input.contentType     = TMP_InputField.ContentType.Password;
        input.asteriskChar    = '*';
        input.richText        = false;
        input.onValidateInput = (_, _, c) => c > ' ' && c < 127 ? c : '\0';
        CuteUIKit.StyleInputField(input);
        var colors = input.colors;
        colors.highlightedColor = colors.selectedColor = Color.white;
        input.colors = colors;
        return input;
    }

    private static TextMeshProUGUI Stretch(TextMeshProUGUI t)
    {
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    private static Slider CreateSlider(Transform parent, string name)
    {
        const float height = 24f, track = 12f, handle = 22f;

        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.name = name;
        go.transform.SetParent(parent, false);

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth   = 1f;
        le.minWidth        = 160f;
        le.preferredHeight = height;

        var bg = (RectTransform)go.transform.Find("Background");
        CuteUIKit.SetRounded(bg.GetComponent<Image>(), Mathf.RoundToInt(track * 0.5f), CuteUIKit.LatteLight);
        bg.anchorMin = new Vector2(0f, 0.5f);
        bg.anchorMax = new Vector2(1f, 0.5f);
        bg.offsetMin = new Vector2(0f, -track * 0.5f);
        bg.offsetMax = new Vector2(0f,  track * 0.5f);

        var fillArea = (RectTransform)go.transform.Find("Fill Area");
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.offsetMin = new Vector2(7f,  -(track * 0.5f - 2f));
        fillArea.offsetMax = new Vector2(-15f,  track * 0.5f - 2f);
        CuteUIKit.SetRounded(fillArea.Find("Fill").GetComponent<Image>(), Mathf.RoundToInt(track * 0.5f - 2f), CuteUIKit.Strawberry);

        var knob = (RectTransform)go.transform.Find("Handle Slide Area/Handle");
        knob.sizeDelta = new Vector2(handle, handle - height);
        CuteUIKit.SetRounded(knob.GetComponent<Image>(), Mathf.RoundToInt(handle * 0.5f), Color.white);
        CuteUIKit.Layer(knob, "Inner", CuteUIKit.Rounded(Mathf.RoundToInt(handle * 0.5f) - 3), Color.white, 3f);

        var slider = go.GetComponent<Slider>();
        slider.minValue     = 0f;
        slider.maxValue     = 1f;
        slider.wholeNumbers = false;

        UIStyleKit.ApplyButtonColors(slider, CuteUIKit.Latte);
        var colors = slider.colors;
        colors.highlightedColor = colors.selectedColor = CuteUIKit.Strawberry;
        colors.pressedColor     = CuteUIKit.Darker(CuteUIKit.Strawberry, 0.9f);
        slider.colors = colors;
        return slider;
    }
}
