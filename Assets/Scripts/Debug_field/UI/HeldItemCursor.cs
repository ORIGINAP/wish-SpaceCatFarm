using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class HeldItemCursor : MonoBehaviour
{
    private sealed class ToolCursor
    {
        public Texture2D texture;
        public Vector2   hotspot;
        public Vector2   pivot;
    }

    private readonly Dictionary<FarmingItemData, ToolCursor> cache = new();
    private FarmingItemData applied;
    private bool            appliedController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // 첫 씬에서 만들고 계속 유지
        var go = new GameObject("HeldItemCursor");
        DontDestroyOnLoad(go);
        go.AddComponent<HeldItemCursor>();
    }

    private void LateUpdate()
    {
        var  tool       = Hidden() ? null : HeldTool();
        bool controller = InputManager.Instance != null && InputManager.Instance.UsingController;
        if (tool == applied && controller == appliedController) return;
        applied           = tool;
        appliedController = controller;
        Apply(tool, controller);
    }

    private void OnDestroy() => Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

    private void Apply(FarmingItemData tool, bool controller)
    {
        var im = InputManager.Instance;
        var tc = tool != null ? CursorFor(tool) : null;

        if (tc != null && !controller) Cursor.SetCursor(tc.texture, tc.hotspot, CursorMode.Auto);
        else                           Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        if (im != null) im.SetCursorOverride(tc != null ? tool.icon : null, tc != null ? tc.pivot : Vector2.zero);
    }

    private static FarmingItemData HeldTool()
    {
        var ptc  = PlayerTileController.Instance;
        var item = ptc != null ? ptc.SelectedItem : null;
        return item != null && item.itemType == FarmingItemType.Tool && item.icon != null ? item : null;
    }

    private static bool Hidden()
        => (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
           || PlayerTileController.IsPauseMenuOpen()
           || LinkModeController.IsActive;

    private ToolCursor CursorFor(FarmingItemData tool)
    {
        if (cache.TryGetValue(tool, out var tc)) return tc;

        int size = CursorSize();
        var tex  = CopySprite(tool.icon, size);
        var tip  = TopRightTip(tex);
        tc = new ToolCursor
        {
            texture = tex,
            hotspot = new Vector2(tip.x, size - 1 - tip.y),
            pivot   = new Vector2((tip.x + 0.5f) / size, (tip.y + 0.5f) / size),
        };
        cache[tool] = tc;
        return tc;
    }

    private static int CursorSize()
    {
        float dpi = Screen.dpi > 0f ? Screen.dpi : 96f;
        return dpi >= 180f ? 64 : dpi >= 132f ? 48 : 32;
    }

    // 읽기 금지 텍스처라 GPU로 복사
    private static Texture2D CopySprite(Sprite sprite, int size)
    {
        var src = sprite.texture;
        Rect r  = sprite.textureRect;
        var rt  = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;

        Graphics.Blit(src, rt, new Vector2(r.width / src.width, r.height / src.height),
                               new Vector2(r.x / src.width, r.y / src.height));
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
#if UNITY_EDITOR
        tex.alphaIsTransparency = true;
#endif
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply(false);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    private static Vector2Int TopRightTip(Texture2D tex)
    {
        var px   = tex.GetPixels32();
        int size = tex.width;
        var best = new Vector2Int(0, size - 1);
        int bestScore = int.MinValue;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            if (px[y * size + x].a < 128) continue;
            int score = x + y;
            if (score > bestScore) { bestScore = score; best = new Vector2Int(x, y); }
        }
        return best;
    }
}
