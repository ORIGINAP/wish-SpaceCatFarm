using UnityEditor;

public static class MobilePreviewMenu
{
    private const string MenuPath = "Tools/WISH/모바일 미리보기";

    [MenuItem(MenuPath)]
    private static void Toggle()
        => EditorPrefs.SetBool(MobileMode.PreviewPrefKey, !EditorPrefs.GetBool(MobileMode.PreviewPrefKey, false));

    [MenuItem(MenuPath, true)]
    private static bool Validate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(MobileMode.PreviewPrefKey, false));
        return true;
    }
}
