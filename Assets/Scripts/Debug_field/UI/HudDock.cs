using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudDock : MonoBehaviour
{
    public enum Slot { Bag, Craft, Shop, Shuttle, Research, Journey }

    private const float ButtonW = 58f, ButtonH = 56f, Gap = 6f, Margin = 14f;

    private static HudDock instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private Canvas        canvas;
    private RectTransform row;
    private readonly Dictionary<Slot, RectTransform> buttons = new();
    private Image           orderDot;
    private TextMeshProUGUI orderDotText;
    private float           nextSlowCheck;

    public static bool Visible => instance != null && instance.canvas != null && instance.canvas.enabled;

    public static RectTransform ButtonRect(Slot slot)
        => Visible && instance.buttons.TryGetValue(slot, out var rt) ? rt : null;

    public static Sprite LetterIcon => Icons.Letter;
    public static Sprite FlaskIcon  => Icons.Flask;
    public static Sprite RocketIcon => Icons.Rocket;
    public static Sprite CatIcon    => Icons.Cat;

    public static string KeyOf(Slot slot) => slot switch
    {
        Slot.Bag      => "Tab",
        Slot.Craft    => "C",
        Slot.Shop     => "B",
        Slot.Shuttle  => "J",
        Slot.Journey  => "L",
        _             => "T",
    };

    public static void Create()
    {
        if (instance != null || MobileMode.Active) return;
        var go = new GameObject("HudDock", typeof(RectTransform));
        // 창보다 아래 층
        UIStyleKit.CreateOverlayCanvas(go, -1);
        instance = go.AddComponent<HudDock>();
        instance.canvas = go.GetComponent<Canvas>();
        instance.Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        var ptc  = PlayerTileController.Instance;
        bool show = ptc != null && !Pad.Active && !PlayerTileController.IsPauseMenuOpen();
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show || Time.unscaledTime < nextSlowCheck) return;
        nextSlowCheck = Time.unscaledTime + 0.5f;

        int ready = ShuttleService.Instance != null ? ShuttleService.Instance.CompletableCount() : 0;
        bool dot = ready > 0;
        if (orderDot.gameObject.activeSelf != dot) orderDot.gameObject.SetActive(dot);
        if (dot) orderDotText.text = ready.ToString();
    }

    private static void Press(Slot slot)
    {
        switch (slot)
        {
            case Slot.Bag:      InventoryUI.Instance?.Toggle(); break;
            case Slot.Craft:    CraftingTableUI.Instance?.Toggle(); break;
            case Slot.Shop:     ShopUI.Instance?.Toggle(); break;
            case Slot.Shuttle:  ShuttleUI.Instance?.Toggle(); break;
            case Slot.Research: TechTreeUI.Instance?.Toggle(); break;
            case Slot.Journey:  JourneyUI.Toggle(); break;
        }
    }

    private void Build()
    {
        var rowGO = new GameObject("Row", typeof(RectTransform));
        rowGO.transform.SetParent(transform, false);
        row = (RectTransform)rowGO.transform;
        row.anchorMin = row.anchorMax = row.pivot = new Vector2(1f, 0f);
        row.anchoredPosition = new Vector2(-Margin, Margin);
        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = Gap;
        hlg.childAlignment         = TextAnchor.LowerRight;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        var fit = rowGO.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        MakeButton(Slot.Bag,      "가방", Icons.Bag);
        MakeButton(Slot.Craft,    "제작", Icons.Hammer);
        MakeButton(Slot.Shop,     "상점", Icons.Shop);
        var orders = MakeButton(Slot.Shuttle, "셔틀", Icons.Rocket);
        MakeButton(Slot.Research, "연구", Icons.Flask);
        MakeButton(Slot.Journey,  "여정", Icons.Flag);

        orderDot = CuteUIKit.Card(orders, CuteUIKit.Strawberry, 9, "ReadyDot");
        orderDot.raycastTarget = false;
        var drt = orderDot.rectTransform;
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1f, 1f);
        drt.anchoredPosition = new Vector2(5f, 5f);
        drt.sizeDelta = new Vector2(18f, 18f);
        var dle = orderDot.gameObject.AddComponent<LayoutElement>();
        dle.ignoreLayout = true;
        orderDotText = CuteUIKit.Text(drt, "0", 11f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        var trt = orderDotText.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        orderDot.gameObject.SetActive(false);
    }

    private RectTransform MakeButton(Slot slot, string label, Sprite icon)
    {
        var card = CuteUIKit.Card(row, CuteUIKit.Latte, 14, "Btn_" + slot);
        var rt   = card.rectTransform;
        var le   = card.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = ButtonW;
        le.preferredHeight = ButtonH;
        CuteUIKit.Layer(rt, "Fill", CuteUIKit.Rounded(12), CuteUIKit.Cream, 2f).raycastTarget = false;

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(rt, false);
        var img = iconGO.AddComponent<Image>();
        img.sprite = icon;
        img.raycastTarget = false;
        var irt = img.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
        irt.pivot     = new Vector2(0.5f, 1f);
        irt.anchoredPosition = new Vector2(0f, -7f);
        irt.sizeDelta = new Vector2(30f, 30f);

        var text = CuteUIKit.Text(rt, label, 11f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.Center);
        var lrt  = text.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot     = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 4f);
        lrt.sizeDelta = new Vector2(0f, 16f);
        text.raycastTarget = false;

        var key = CuteUIKit.Pill(rt, KeyOf(slot), CuteUIKit.Cocoa, Color.white, 9f, 7, new RectOffset(4, 4, 0, 0), out _, fitSelf: true);
        key.anchorMin = key.anchorMax = key.pivot = new Vector2(0f, 1f);
        key.anchoredPosition = new Vector2(-4f, 5f);
        key.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        foreach (var g in key.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;

        card.raycastTarget = true;
        var button = card.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Press(slot));
        card.gameObject.AddComponent<UiButtonSfx>();
        card.gameObject.AddComponent<CutePop>();

        buttons[slot] = rt;
        return rt;
    }

    private static class Icons
    {
        private const string Palette =
            "##7A5A4F o#F2B880 O#E39A5C y#FFD66B s#C9D3DD S#9AA8B5 w#FFF7EA W#E9CBA7 r#F7879F m#9FE0BE M#5FBF8F b#8B5E3C";

        private static Sprite Make(string key, string[] rows) => LabArt.Pixel("dock_" + key, rows, Palette, new Vector2(0.5f, 0.5f));

        public static Sprite Bag => Make("bag", new[]
        {
            "................",
            "......####......",
            ".....#....#.....",
            ".....#....#.....",
            "...##########...",
            "..#oooooooooo#..",
            ".#oooooooooooo#.",
            ".#oooooooooooo#.",
            ".#ooo######ooo#.",
            ".#ooo#yyyy#ooo#.",
            ".#ooo######ooo#.",
            ".#OOOOOOOOOOOO#.",
            ".#OOOOOOOOOOOO#.",
            "..############..",
            "................",
            "................",
        });

        public static Sprite Hammer => Make("hammer", new[]
        {
            "................",
            "..#########.....",
            ".#sssssssss#....",
            ".#sssssssss##...",
            ".#SSSSSSSSS#....",
            "..####bb###.....",
            ".....#bb#.......",
            ".....#bb#.......",
            ".....#bb#.......",
            ".....#bb#.......",
            ".....#bb#.......",
            ".....#bb#.......",
            ".....#bb#.......",
            ".....####.......",
            "................",
            "................",
        });

        public static Sprite Shop => Make("shop", new[]
        {
            "................",
            ".##############.",
            "#rrwwrrwwrrwwrr#",
            "#rrwwrrwwrrwwrr#",
            ".#rwwrrwwrrwwr#.",
            "..############..",
            "..#WWWWWWWWWW#..",
            "..#W###WW###W#..",
            "..#W#y#WW#y#W#..",
            "..#W###WW###W#..",
            "..#WWWWWWWWWW#..",
            "..#WWWW##WWWW#..",
            "..#WWWW##WWWW#..",
            "..############..",
            "................",
            "................",
        });

        public static Sprite Letter => Make("letter", new[]
        {
            "................",
            "......####......",
            "..####yyyy####..",
            "..#wwwwwwwwww#..",
            "..#w######www#..",
            "..#wwwwwwwwww#..",
            "..#w########w#..",
            "..#wwwwwwwwww#..",
            "..#w#######ww#..",
            "..#wwwwwwwwww#..",
            "..#w#####wwww#..",
            "..#wwwwwwwrrw#..",
            "..#wwwwwwrrrr#..",
            "..############..",
            "................",
            "................",
        });

        public static Sprite Rocket => Make("rocket", new[]
        {
            "................",
            ".....#....#.....",
            "....#r#..#r#....",
            "....#rr##rr#....",
            "....#rrrrrr#....",
            "....#w####W#....",
            "....#w#ss#W#....",
            "....#w#sS#W#....",
            "...##w####W##...",
            "..#r#rrrrrr#r#..",
            "..#r#wwwwwW#r#..",
            "..#r#wwwwwW#r#..",
            "..############..",
            ".....oyyyyo.....",
            "......oyyo......",
            ".......yy.......",
        });

        public static Sprite Cat => Make("cat", new[]
        {
            "................",
            "..##........##..",
            "..#r#......#r#..",
            "..#rr######rr#..",
            "..#wwwwwwwwww#..",
            ".#wwwwwwwwwwww#.",
            ".#www#wwww#www#.",
            ".#www#wwww#www#.",
            ".#rrwwwrrwwwrr#.",
            ".#wwww#ww#wwww#.",
            "..#wwww##wwww#..",
            "...##wwwwww##...",
            ".....######.....",
            "................",
            "................",
            "................",
        });

        public static Sprite Flag => Make("flag", new[]
        {
            "................",
            "..##............",
            "..#y#...........",
            "..#b############",
            "..#b#rrrrrrrrrr#",
            "..#b#rrrryrrrr#.",
            "..#b#rryyyyyr#..",
            "..#b#rrryyyrrr#.",
            "..#b#rryrrryrrr#",
            "..#b############",
            "..#b#...........",
            "..#b#...........",
            "..#b#...........",
            ".#####..........",
            ".#mmm#..........",
            ".#####..........",
        });

        public static Sprite Flask => Make("flask", new[]
        {
            "................",
            "......####......",
            "......#ww#......",
            "......#ww#......",
            "......#ww#......",
            ".....##ww##.....",
            "....#wwwwww#....",
            "...#wwwwwwww#...",
            "..#mmmmwmmmmm#..",
            "..#mmmmmmmwmm#..",
            "..#mmwmmmmmmm#..",
            "..#MMMMMMMMMM#..",
            "...##########...",
            "................",
            "................",
            "................",
        });
    }
}
