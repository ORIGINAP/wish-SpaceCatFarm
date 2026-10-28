using System.Collections.Generic;
using UnityEngine;

public interface ICoverageArea
{
    int CoverageRadiusCells { get; }
    Color CoverageColor { get; }
}

public class CoverageHoverPreview : MonoBehaviour
{
    private const int SortingOrder = 30;

    private readonly List<SpriteRenderer> cells = new();
    private ICoverageArea shown;
    private int           shownVersion = -1;
    private Camera        cam;
    private static Sprite cellSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cellSprite = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("CoverageHoverPreview");
        DontDestroyOnLoad(go);
        go.AddComponent<CoverageHoverPreview>();
    }

    private void LateUpdate()
    {
        var gm = GridManager.Instance;
        var target = gm != null ? FindHovered(gm) : null;

        // 같은 대상이면 다시 안 그림
        if (target == shown && (target == null || shownVersion == gm.Version)) return;
        shown        = target;
        shownVersion = gm != null ? gm.Version : -1;

        int used = target != null ? Draw(gm, target) : 0;
        for (int i = used; i < cells.Count; i++) cells[i].enabled = false;
    }

    private ICoverageArea FindHovered(GridManager gm)
    {
        if (GameInput.PointerOverUI) return null;
        if (cam == null) cam = Camera.main;
        if (cam == null) return null;

        Vector3 mp = InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : (Vector3)GameInput.MousePosition;
        mp.z = -cam.transform.position.z;
        Vector3 world = cam.ScreenToWorldPoint(mp);

        var tile = gm.GetTile(gm.WorldToGrid(world));
        return tile != null && !tile.IsGhost ? tile as ICoverageArea : null;
    }

    private int Draw(GridManager gm, ICoverageArea target)
    {
        var tile   = (TileItem)target;
        var min    = tile.FootprintMin;
        var max    = tile.FootprintMax;
        int radius = target.CoverageRadiusCells;
        int used   = 0;

        for (int x = min.x - radius; x <= max.x + radius; x++)
        for (int y = min.y - radius; y <= max.y + radius; y++)
        {
            if (GridManager.AreaDistance(min, max, x, y) > radius) continue;
            var cell = new Vector2Int(x, y);
            if (!gm.IsInBounds(cell)) continue;

            var sr = GetCell(used++);
            sr.transform.position   = gm.GridToWorld(cell);
            sr.transform.localScale = Vector3.one * gm.CellSize * 0.94f;
            sr.color   = target.CoverageColor;
            sr.enabled = true;
        }
        return used;
    }

    private SpriteRenderer GetCell(int index)
    {
        if (index < cells.Count) return cells[index];

        var go = new GameObject("CoverageCell");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = CellSprite();
        sr.sortingOrder = SortingOrder;
        // Lit 머티리얼은 어둡게 나옴
        sr.sharedMaterial = WorldConnectionLine.SharedMaterial;
        cells.Add(sr);
        return sr;
    }

    private static Sprite CellSprite()
    {
        if (cellSprite != null) return cellSprite;
        var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        cellSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return cellSprite;
    }
}
