using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

// 인게임 Esc 메뉴
[DefaultExecutionOrder(100)]
public class SaveMenuUI : MonoBehaviour
{
    public static SaveMenuUI Instance => Services.Get<SaveMenuUI>();

    [Header("조작 키 (Esc 외 단축키)")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F5;

    [Header("씬")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset fontAsset;

    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private bool          isOpen = false;
    private Coroutine     animCo;

    private TextMeshProUGUI worldLabel, infoLabel;
    private Button          resumeButton, saveButton, exitButton, inviteButton, labButton;
    private TextMeshProUGUI exitLabel, inviteLabel, closeHint;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        ClearBuilt();
        Build();
        panelRT.localScale = Vector3.zero;
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    private void OnEnable()  => NetBootstrap.Changed += RefreshIfOpen;
    private void OnDisable() => NetBootstrap.Changed -= RefreshIfOpen;

    private void RefreshIfOpen()
    {
        if (isOpen) RefreshInfo();
    }

    private void LateUpdate()
    {
        if (TextInputFocus.Active) return;

        bool togglePressed = GameInput.GetKeyDown(toggleKey)
                          || (InputManager.Instance != null && InputManager.Instance.SaveMenuButton);

        if (isOpen)
        {
            bool covered = OptionsUI.IsOpen || CharacterEditorUI.IsOpen;
            panelCG.interactable = !covered;
            if (covered) return;

            if (togglePressed || EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Close();
            else UIStyleKit.KeepNavigable(resumeButton.gameObject, panelRT);
            return;
        }

        if (PlayerTileController.IsAnyUIOpen()) return;
        if (togglePressed || EscapeRouter.TryConsume(EscapeRouter.EscPressed)) Open();
    }

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        RefreshInfo();
        Sfx.Play(SfxId.UiOpen);
        StartAnim(true);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        UIStyleKit.ClearSelectionIn(panelRT);
        Sfx.Play(SfxId.UiClose);
        StartAnim(false);
    }

    public void Toggle()
    {
        if (isOpen) Close(); else Open();
    }

    private void StartAnim(bool opening)
    {
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Anim(opening));
    }

    private IEnumerator Anim(bool opening)
    {
        panelCG.blocksRaycasts = opening;
        panelCG.interactable   = opening;

        if (opening)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG);
        }
        animCo = null;
    }

    private void RefreshInfo()
    {
        int slot = SaveManager.CurrentSlot;
        worldLabel.text = $"월드 {slot + 1}  ·  {WorldSizeConfig.DisplayName(GameLaunchContext.PendingWorldSize)} · {WorldThemeConfig.DisplayName(GameLaunchContext.PendingWorldTheme)} 월드";
        closeHint.text  = Pad.Active ? "B / START: 닫기" : $"Esc / {toggleKey}: 닫기";
        RefreshInvite();
        labButton.gameObject.SetActive(Lab.Active);

        if (NetSession.IsReplica)
        {
            worldLabel.text = $"{NetBootstrap.HostName}님의 {(Lab.Active ? "실험실" : "월드")}  ·  {NetBootstrap.PlayerCount}명";
            infoLabel.text  = Lab.Active ? "친구 실험실에 참가 중 — 실험실은 저장되지 않아요."
                                         : "친구 월드에 참가 중 — 월드와 내 인벤토리는 호스트가 저장해요.";
            saveButton.interactable = false;
            exitLabel.text = "나가기";
            return;
        }

        if (Lab.Active)
        {
            worldLabel.text = $"실험실  ·  {WorldSizeConfig.DisplayName(GameLaunchContext.PendingWorldSize)} · {WorldThemeConfig.DisplayName(GameLaunchContext.PendingWorldTheme)} 월드";
            infoLabel.text  = "새 놀거리를 먼저 해 보는 월드라 저장되지 않아요. 친구 초대도 돼요.";
            saveButton.interactable = false;
            exitLabel.text = "메인메뉴로 나가기";
            return;
        }

        if (!SaveManager.CanSave)
        {
            infoLabel.text = "테스트 실행 중 — 메인메뉴에서 월드를 골라 시작하지 않아 저장되지 않습니다.";
            saveButton.interactable = false;
            exitLabel.text = "메인메뉴로 나가기";
            return;
        }

        var data = SaveManager.ReadSlotStatic(slot);
        infoLabel.text = data != null
            ? $"마지막 저장  {data.timestamp}   ·   플레이 {FormatPlayTime(data.playTimeSeconds)}"
            : "아직 저장하지 않은 새 월드입니다.";
        saveButton.interactable = true;
        exitLabel.text = "저장하고 나가기";
    }

    private void RefreshInvite()
    {
        inviteButton.gameObject.SetActive(!NetSession.IsReplica);
        inviteButton.interactable = !NetBootstrap.Busy;
        if (NetBootstrap.Busy)                                inviteLabel.text = "조인 코드 만드는 중...";
        else if (!string.IsNullOrEmpty(NetBootstrap.JoinCode)) inviteLabel.text = $"조인 코드 {NetBootstrap.JoinCode} 복사  ·  {NetBootstrap.PlayerCount}명";
        else                                                   inviteLabel.text = "친구 초대 (조인 코드)";
    }

    private void Invite()
    {
        if (NetSession.IsHost) NetHud.CopyCode();
        else NetBootstrap.StartHosting();
        RefreshInvite();
    }

    public static string FormatPlayTime(float seconds)
    {
        int totalMin = Mathf.FloorToInt(seconds / 60f);
        return totalMin < 60 ? $"{totalMin}분" : $"{totalMin / 60}시간 {totalMin % 60}분";
    }

    private void SaveCurrentWorld()
    {
        if (SaveManager.Instance == null) return;
        if (SaveManager.Instance.Save(SaveManager.CurrentSlot))
        {
            NotificationUI.Instance?.Show($"월드 {SaveManager.CurrentSlot + 1}에 저장했어요.");
            Sfx.Play(SfxId.Save);
        }
        else Sfx.Play(SfxId.UiError);
        RefreshInfo();
    }

    private void OpenOptions() => OptionsUI.Open();

    private void OpenHelp()
    {
        Close();
        HelpUI.Open();
    }

    private void OpenLab()
    {
        Close();
        LabPanelUI.Open();
    }

    private void OpenCharacter()
    {
        string note = NetSession.IsReplica ? "친구 월드에서 쓰는 내 캐릭터예요" : "이 월드에서 함께하는 내 캐릭터예요";
        CharacterEditorUI.Open(PlayerCharacter.Current, "캐릭터 꾸미기", note, "이 모습으로 바꾸기", look =>
        {
            PlayerCharacter.Change(look);
            NotificationUI.Instance?.Show($"{look.name}의 모습을 바꿨어요!");
            Sfx.Play(SfxId.UiConfirm);
        });
    }

    private void SaveAndExitToMainMenu()
    {
        if (SaveManager.CanSave && (SaveManager.Instance == null || !SaveManager.Instance.Save(SaveManager.CurrentSlot)))
        {
            NotificationUI.Instance?.Show("저장하지 못해 나가지 않았습니다. 다시 시도해 주세요.");
            Sfx.Play(SfxId.UiError);
            return;
        }
        if (SaveManager.CanSave) Sfx.Play(SfxId.Save);
        NetBootstrap.Leave();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private void ClearBuilt()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        { var c = GetComponent<Outline>();             if (c) DestroyImmediate(c); }
        { var c = GetComponent<VerticalLayoutGroup>(); if (c) DestroyImmediate(c); }
        { var c = GetComponent<CanvasGroup>();         if (c) DestroyImmediate(c); }
        { var c = GetComponent<ContentSizeFitter>();   if (c) DestroyImmediate(c); }
    }

    private void Build()
    {
        panelRT = GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = Vector2.one * 0.5f;
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = new Vector2(360f, 0f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(22, 22, 18, 20);
        vlg.spacing                = 10f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CuteUIKit.TitleRibbon(Centered(transform), "메뉴");

        var info = CuteUIKit.Card(transform, CuteUIKit.LatteLight, name: "WorldInfo").gameObject;
        var infoVlg = info.AddComponent<VerticalLayoutGroup>();
        infoVlg.padding                = new RectOffset(12, 12, 8, 9);
        infoVlg.spacing                = 3f;
        infoVlg.childForceExpandWidth  = true;
        infoVlg.childForceExpandHeight = false;
        infoVlg.childControlWidth      = true;
        infoVlg.childControlHeight     = true;

        worldLabel = Text(info.transform, "", 14f, UIStyleKit.TextPrimary, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        infoLabel  = Text(info.transform, "", 11f, UIStyleKit.TextSecondary, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        infoLabel.textWrappingMode = TextWrappingModes.Normal;

        resumeButton = MakeButton("계속하기", CuteUIKit.ButtonLatte, Close);
        labButton    = MakeButton("실험실 창 (L)", LabPanelUI.TabColor, OpenLab);
        saveButton   = MakeButton("저장하기", CuteUIKit.ButtonMint, SaveCurrentWorld);
        MakeButton("캐릭터 꾸미기", CuteUIKit.LavenderTag, OpenCharacter);
        inviteButton = MakeButton("친구 초대 (조인 코드)", CuteUIKit.PinkSoft, Invite);
        inviteLabel  = inviteButton.GetComponentInChildren<TextMeshProUGUI>();
        MakeButton("옵션", CuteUIKit.ButtonSky, OpenOptions);
        MakeButton("도움말", CuteUIKit.MintTag, OpenHelp);
        exitButton   = MakeButton("저장하고 나가기", CuteUIKit.ButtonButter, SaveAndExitToMainMenu);
        exitLabel    = exitButton.GetComponentInChildren<TextMeshProUGUI>();

        closeHint = Text(transform, $"Esc / {toggleKey}: 닫기", 10f, UIStyleKit.TextMuted, FontStyles.Normal, TextAlignmentOptions.Center);
    }

    [ContextMenu("Rebuild UI")]
    public void RebuildUI()
    {
        ClearBuilt();
        Build();
        if (panelRT != null) panelRT.localScale = isOpen ? Vector3.one : Vector3.zero;
        if (panelCG != null) { panelCG.blocksRaycasts = isOpen; panelCG.interactable = isOpen; }
        if (isOpen) RefreshInfo();
    }

    private TextMeshProUGUI Text(Transform parent, string text, float size, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var tmp = UIStyleKit.CreateText(parent, text, size, color, style, align);
        tmp.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        if (fontAsset != null) tmp.font = fontAsset;
        return tmp;
    }

    private Button MakeButton(string label, Color bgColor, UnityAction onClick)
    {
        var btn = UIStyleKit.CreateButton(transform, label, bgColor, onClick, preferredHeight: 36f);
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        tmp.fontSize = 14f;
        if (fontAsset != null) tmp.font = fontAsset;
        return btn;
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
