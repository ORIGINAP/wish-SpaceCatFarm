using System.Collections.Generic;
using UnityEngine;

public static class GamepadPanels
{
    // ring 순서 바꾸면 LB/RB 순서도 바뀜
    public readonly struct Panel
    {
        public readonly Transform root;
        public readonly int ring;
        public Panel(Transform root, int ring) { this.root = root; this.ring = ring; }
    }

    public const int RingCount = 6;

    private static MainMenuUI mainMenu;
    private static readonly List<Panel> open = new();
    private static int openFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        mainMenu = null;
        open.Clear();
        openFrame = -1;
    }

    public static List<Panel> Open
    {
        get
        {
            if (openFrame != Time.frameCount)
            {
                openFrame = Time.frameCount;
                Collect(open);
            }
            return open;
        }
    }

    private static void Collect(List<Panel> list)
    {
        list.Clear();

        if (PlayerTileController.Instance == null)
        {
            if (mainMenu == null) mainMenu = Object.FindFirstObjectByType<MainMenuUI>();
            if (mainMenu != null) list.Add(new Panel(mainMenu.transform, -1));
        }

        if (InventoryUI.Instance     != null && InventoryUI.Instance.IsOpen)     list.Add(new Panel(InventoryUI.Instance.transform, 0));
        if (CraftingTableUI.Instance != null && CraftingTableUI.Instance.IsOpen) list.Add(new Panel(CraftingTableUI.Instance.transform, 1));
        if (ShopUI.Instance          != null && ShopUI.Instance.IsOpen)          list.Add(new Panel(ShopUI.Instance.transform, 2));
        if (TechTreeUI.Instance      != null && TechTreeUI.Instance.IsOpen)      list.Add(new Panel(TechTreeUI.Instance.transform, 3));
        if (JourneyUI.IsOpen)                                                    list.Add(new Panel(JourneyUI.Root, 4));
        if (ShuttleUI.IsOpen)                                                    list.Add(new Panel(ShuttleUI.Instance.transform, 5));

        if (CenterCoreUI.Instance        != null && CenterCoreUI.Instance.IsOpen)        list.Add(new Panel(CenterCoreUI.Instance.transform, -1));
        if (GeneratorFuelUI.Instance     != null && GeneratorFuelUI.Instance.IsOpen)     list.Add(new Panel(GeneratorFuelUI.Instance.transform, -1));
        if (ItemStorageUI.Instance       != null && ItemStorageUI.Instance.IsOpen)       list.Add(new Panel(ItemStorageUI.Instance.transform, -1));
        if (ResourceExtractorUI.Instance != null && ResourceExtractorUI.Instance.IsOpen) list.Add(new Panel(ResourceExtractorUI.Instance.transform, -1));
        if (SmelterUI.IsOpen)            list.Add(new Panel(SmelterUI.Instance.transform, -1));
        if (CatKitchenUI.IsOpen)         list.Add(new Panel(CatKitchenUI.Instance.transform, -1));
        if (ComposterUI.IsOpen)          list.Add(new Panel(ComposterUI.Instance.transform, -1));
        if (CodexUI.IsOpen)              list.Add(new Panel(CodexUI.Instance.transform, -1));
        if (FacilityPopup.IsOpen)        list.Add(new Panel(FacilityPopup.Instance.transform, -1));

        if (HelpUI.AnyOpen)              list.Add(new Panel(HelpUI.PanelRoot, -1));
        if (DevPanelUI.AnyOpen)          list.Add(new Panel(DevPanelUI.PanelRoot, -1));
        if (LabPanelUI.IsOpen)           list.Add(new Panel(LabPanelUI.Root, -1));
        if (TutorialUI.IsShowing)        list.Add(new Panel(TutorialUI.Root, -1));
        if (LevelUpUI.IsOpen)            list.Add(new Panel(LevelUpUI.Root, -1));
        if (EndingUI.IsShowing)          list.Add(new Panel(EndingUI.Root, -1));

        if (SaveMenuUI.Instance != null && SaveMenuUI.Instance.IsOpen) list.Add(new Panel(SaveMenuUI.Instance.transform, -1));
        if (CharacterEditorUI.IsOpen)    list.Add(new Panel(CharacterEditorUI.Root, -1));
        if (OptionsUI.IsOpen)            list.Add(new Panel(OptionsUI.Root, -1));

        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].root == null) list.RemoveAt(i);
    }

    public static bool IsRingOpen(int ring) => ring switch
    {
        0 => InventoryUI.Instance     != null && InventoryUI.Instance.IsOpen,
        1 => CraftingTableUI.Instance != null && CraftingTableUI.Instance.IsOpen,
        2 => ShopUI.Instance          != null && ShopUI.Instance.IsOpen,
        3 => TechTreeUI.Instance      != null && TechTreeUI.Instance.IsOpen,
        4 => JourneyUI.IsOpen,
        5 => ShuttleUI.IsOpen,
        _ => false,
    };

    private static bool RingExists(int ring) => ring switch
    {
        0 => InventoryUI.Instance     != null,
        1 => CraftingTableUI.Instance != null,
        2 => ShopUI.Instance          != null,
        3 => TechTreeUI.Instance      != null,
        4 => JourneyUI.Root           != null,
        5 => ShuttleUI.Instance       != null,
        _ => false,
    };

    public static void SetRingOpen(int ring, bool open)
    {
        if (!RingExists(ring) || IsRingOpen(ring) == open) return;
        switch (ring)
        {
            case 0: InventoryUI.Instance.Toggle();     break;
            case 1: CraftingTableUI.Instance.Toggle(); break;
            case 2: ShopUI.Instance.Toggle();          break;
            case 3: TechTreeUI.Instance.Toggle();      break;
            case 4: JourneyUI.Toggle();                break;
            case 5: ShuttleUI.Instance.Toggle();       break;
        }
    }

    public static void SwitchRing(int from, int delta)
    {
        if (from < 0) return;
        int to = from;
        for (int i = 0; i < RingCount; i++)
        {
            to = (to + delta + RingCount) % RingCount;
            if (RingExists(to)) break;
        }
        if (to == from) return;
        SetRingOpen(from, false);
        SetRingOpen(to, true);
    }

    // 창 열려 있으면 Esc 메뉴가 안 열림
    public static void CloseAllForMenu()
    {
        for (int i = 0; i < RingCount; i++) SetRingOpen(i, false);
        if (CenterCoreUI.Instance        != null && CenterCoreUI.Instance.IsOpen)        CenterCoreUI.Instance.Close();
        if (GeneratorFuelUI.Instance     != null && GeneratorFuelUI.Instance.IsOpen)     GeneratorFuelUI.Instance.Close();
        if (ItemStorageUI.Instance       != null && ItemStorageUI.Instance.IsOpen)       ItemStorageUI.Instance.Close();
        if (ResourceExtractorUI.Instance != null && ResourceExtractorUI.Instance.IsOpen) ResourceExtractorUI.Instance.Close();
        if (SmelterUI.IsOpen) SmelterUI.Instance.Close();
        if (CatKitchenUI.IsOpen) CatKitchenUI.Instance.Close();
        if (ComposterUI.IsOpen) ComposterUI.Instance.Close();
        if (CodexUI.IsOpen) CodexUI.Instance.Close();
        FacilityPopup.Close();
        HelpUI.CloseIfOpen();
        DevPanelUI.CloseIfOpen();
        LabPanelUI.CloseIfOpen();
    }

    public static bool CycleTab(int ring, int delta)
    {
        switch (ring)
        {
            case 1: CraftingTableUI.Instance.CycleTab(delta); return true;
            case 2: ShopUI.Instance.CycleTab(delta);          return true;
            case 3: TechTreeUI.Instance.CycleCategory(delta); return true;
            default: return false;
        }
    }
}
