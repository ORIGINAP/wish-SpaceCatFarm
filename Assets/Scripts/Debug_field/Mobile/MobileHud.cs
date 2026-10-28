using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MobileHud : MonoBehaviour
{
    private const float ButtonSize = 64f;
    private const float Gap        = 10f;
    private const float Margin     = 16f;
    private const int   HudOrder   = -1;

    private static MobileHud instance;

    private Canvas        canvas;
    private RectTransform stickBase, stickFill, stickKnob;
    private Image         stickBaseImg, stickFillImg, stickKnobImg;
    private Button        dismantleButton, cocoButton, chatButton;
    private readonly List<Button> topRow = new();
    private float nextSlowCheck, stickRadius;
    private bool  cocoAvailable, chatAvailable, dismantleShown;

    public static void Create(Transform parent)
    {
        if (instance != null) return;
        var go = new GameObject("MobileHud", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        instance = go.AddComponent<MobileHud>();
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
        var ptc = PlayerTileController.Instance;
        bool world = ptc != null && !PlayerTileController.IsPauseMenuOpen() && !TextInputFocus.Active;
        if (canvas.enabled != world) canvas.enabled = world;

        bool windowOpen = world && GamepadPanels.Open.Count > 0;
        TouchInput.StickEnabled = world && !windowOpen;
        if (!world) return;

        if (Time.unscaledTime >= nextSlowCheck)
        {
            nextSlowCheck = Time.unscaledTime + 0.5f;
            cocoAvailable = GameSettings.AiNpcEnabled && AINpc.Instance != null;
            chatAvailable = NetSession.Mode != NetSession.Role.Offline;
            LayerHudBelowWindows();
        }

        UpdateStick(!windowOpen);
        UpdateTopRow();
    }

    private void UpdateStick(bool show)
    {
        stickBase.gameObject.SetActive(show);
        stickKnob.gameObject.SetActive(show);
        if (!show) return;

        float scale  = canvas.scaleFactor;
        float radius = TouchInput.StickRadiusPx / scale;
        if (!Mathf.Approximately(radius, stickRadius))
        {
            stickRadius = radius;
            float d = radius * 2f + 24f;
            SetCircle(stickBase, stickBaseImg, d);
            SetCircle(stickFill, stickFillImg, d - 10f);
            SetCircle(stickKnob, stickKnobImg, radius * 0.95f);
        }

        bool active = TouchInput.StickActive;
        Vector2 home = new(Margin + radius + 44f, Margin + radius + 56f);
        Place(stickBase, active ? TouchInput.StickOrigin / scale : home);
        Place(stickKnob, active ? TouchInput.StickKnob / scale : home);
        stickBaseImg.color = CuteUIKit.Hex(0xE9CBA7, active ? 0.75f : 0.45f);
        stickFillImg.color = CuteUIKit.Hex(0xFFF7EA, active ? 0.40f : 0.22f);
        stickKnobImg.color = active ? CuteUIKit.Hex(0xF7879F, 0.92f) : CuteUIKit.Hex(0xFFF7EA, 0.6f);
    }

    private static void SetCircle(RectTransform rt, Image img, float d)
    {
        rt.sizeDelta = new Vector2(d, d);
        img.pixelsPerUnitMultiplier = 132f / d;
    }

    // 캔버스 리빌드 줄이기
    private static void Place(RectTransform rt, Vector2 pos)
    {
        if (rt.anchoredPosition != pos) rt.anchoredPosition = pos;
    }

    private void UpdateTopRow()
    {
        cocoButton.gameObject.SetActive(cocoAvailable);
        chatButton.gameObject.SetActive(chatAvailable);

        bool on = TouchInput.DismantleMode;
        if (on != dismantleShown)
        {
            dismantleShown = on;
            CuteUIKit.ApplyButtonColors(dismantleButton, on ? CuteUIKit.ButtonCoral : CuteUIKit.ButtonLatte);
        }

        int slot = 0;
        foreach (var b in topRow)
        {
            if (!b.gameObject.activeSelf) continue;
            Place((RectTransform)b.transform, Slot(slot++, row: 1));
        }
    }

    private static void OnMenu()
    {
        if (PlayerTileController.IsAnyUIOpen()) GameInput.TapKey(KeyCode.Escape);
        else SaveMenuUI.Instance?.Open();
    }

    private static void OnDismantle()
    {
        TouchInput.DismantleMode = !TouchInput.DismantleMode;
        NotificationUI.Instance?.Show(TouchInput.DismantleMode
            ? "해체 모드: 설치물을 꾹 누르면 해체해요 ([해체]를 다시 누르면 끝)"
            : "해체 모드를 껐어요.");
        Sfx.Play(TouchInput.DismantleMode ? SfxId.UiOpen : SfxId.UiClose);
    }

    private static Vector2 Slot(int i, int row)
        => new(-(Margin + ButtonSize * 0.5f + i * (ButtonSize + Gap)), Margin + ButtonSize * 0.5f + row * (ButtonSize + Gap));

    // 폰에서 큰 창과 겹쳐서
    private static void LayerHudBelowWindows()
    {
        Lower(QuestUI.Instance);
        Lower(CurrencyHUD.Instance);
        Lower(GameClockUI.Instance);
        Lower(WeatherHUD.Instance);
        Lower(ShuttleUI.Instance?.HudPill);
        Lower(HelpUI.ToggleButton);
        Lower(DevPanelUI.ToggleButton);

        var hotbar = TilePlacementUI.Instance;
        var hotbarCanvas = hotbar != null ? hotbar.GetComponentInParent<Canvas>() : null;
        if (hotbarCanvas != null && hotbarCanvas.rootCanvas.sortingOrder > HudOrder) hotbarCanvas.rootCanvas.sortingOrder = HudOrder;
    }

    private static void Lower(Component c)
    {
        if (c == null || c.GetComponent<Canvas>() != null) return;
        var cv = c.gameObject.AddComponent<Canvas>();
        cv.overrideSorting = true;
        cv.sortingOrder    = HudOrder;
        c.gameObject.AddComponent<GraphicRaycaster>();
    }

    private void Build()
    {
        canvas = UIStyleKit.CreateOverlayCanvas(gameObject, HudOrder);

        stickBaseImg = Circle("StickBase", transform);
        stickFillImg = Circle("Fill", stickBaseImg.transform);
        stickKnobImg = Circle("StickKnob", transform);
        stickBase = stickBaseImg.rectTransform;
        stickFill = stickFillImg.rectTransform;
        stickKnob = stickKnobImg.rectTransform;
        stickFill.anchorMin = stickFill.anchorMax = new Vector2(0.5f, 0.5f);

        AddButton("메뉴", CuteUIKit.ButtonCoral,  OnMenu,                                      0, 0);
        AddButton("연구", CuteUIKit.ButtonSky,    () => TechTreeUI.Instance?.Toggle(),        1, 0);
        AddButton("상점", CuteUIKit.ButtonButter, () => ShopUI.Instance?.Toggle(),            2, 0);
        AddButton("제작", CuteUIKit.ButtonMint,   () => CraftingTableUI.Instance?.Toggle(),   3, 0);
        AddButton("가방", CuteUIKit.ButtonLatte,  () => InventoryUI.Instance?.Toggle(),       4, 0);

        dismantleButton = AddButton("해체", CuteUIKit.ButtonLatte,  OnDismantle,                               0, 1);
        cocoButton      = AddButton("코코", CuteUIKit.LavenderTag,  () => GameInput.TapKey(NpcChatUI.OpenKey), 1, 1);
        chatButton      = AddButton("채팅", CuteUIKit.ButtonMint,   () => GameInput.TapKey(KeyCode.Return),    2, 1);
        topRow.AddRange(new[] { dismantleButton, cocoButton, chatButton });

        canvas.enabled = false;
    }

    private static Image Circle(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        var img = CuteUIKit.SetRounded(go.AddComponent<Image>(), 64, Color.white);
        img.raycastTarget = false;
        return img;
    }

    private Button AddButton(string label, Color fill, UnityEngine.Events.UnityAction onClick, int slot, int row)
    {
        var b  = CuteUIKit.Button(transform, label, fill, onClick, ButtonSize, ButtonSize, 17f);
        var rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        rt.anchoredPosition = Slot(slot, row);
        return b;
    }
}
