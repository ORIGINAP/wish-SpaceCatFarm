using UnityEngine;
using UnityEngine.UI;

public class TilePowerBar : MonoBehaviour
{
    [SerializeField] [Range(0f, 1f)] private float lowThreshold = 0.3f;

    private const float BarWidth  = 90f;
    private const float BarHeight = 14f;
    private const float FillInset = 3f;

    private Transform target;
    private Vector3   worldOffset;
    private System.Func<float> ratioGetter;
    private Color highColor;
    private Color lowColor;

    private RectTransform fillRT;
    private Image         fillImg;

    public static TilePowerBar Attach(TileItem tile, System.Func<float> ratioGetter, Color highColor, Color lowColor)
    {
        if (tile == null || ratioGetter == null) return null;

        float cellSize   = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        float halfHeight = cellSize * 0.5f;

        if (tile.OccupiedCells != null && tile.OccupiedCells.Length > 0)
        {
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var c in tile.OccupiedCells)
            {
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }
            halfHeight = (maxY - minY + 1) * cellSize * 0.5f;
        }

        var go  = new GameObject($"PowerBar_{tile.name}");
        var bar = go.AddComponent<TilePowerBar>();
        bar.target      = tile.transform;
        bar.worldOffset = Vector3.up * (halfHeight + 0.25f);
        bar.ratioGetter = ratioGetter;
        bar.highColor   = ThemeFill(highColor);
        bar.lowColor    = ThemeFill(lowColor);
        bar.BuildUI();
        bar.Refresh();
        return bar;
    }

    private void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        transform.position = target.position + worldOffset;
        Refresh();
    }

    private void Refresh()
    {
        if (fillRT == null) return;

        float ratio = Mathf.Clamp01(ratioGetter());
        fillRT.anchorMax = new Vector2(ratio, 1f);
        fillImg.color = ratio <= lowThreshold ? lowColor : highColor;
    }

    private static Color ThemeFill(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out _);
        if (s < 0.2f) return CuteUIKit.CocoaSoft;

        float deg = h * 360f;
        if (deg < 20f || deg >= 330f) return CuteUIKit.Hex(0xC9485B);
        if (deg < 70f)                return CuteUIKit.Butter;
        if (deg < 170f)               return CuteUIKit.Lighter(CuteUIKit.MintText, 0.25f);
        if (deg < 260f)               return CuteUIKit.Lighter(CuteUIKit.SkyText, 0.2f);
        if (deg < 300f)               return CuteUIKit.LavenderText;
        return CuteUIKit.Strawberry;
    }

    private void BuildUI()
    {
        transform.localScale = Vector3.one * 0.01f;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 120;

        var rt = gameObject.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(BarWidth, BarHeight);

        int radius = Mathf.RoundToInt(BarHeight * 0.5f);
        CuteUIKit.Layer(transform, "Border", CuteUIKit.Rounded(radius), CuteUIKit.Latte);
        CuteUIKit.Layer(transform, "Track", CuteUIKit.Rounded(radius - 1), CuteUIKit.LatteLight, 1.5f);

        // 비율이 작아도 폭 음수 방지
        var areaGO = new GameObject("FillArea", typeof(RectTransform));
        areaGO.transform.SetParent(transform, false);
        var areaRT = (RectTransform)areaGO.transform;
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(FillInset, FillInset);
        areaRT.offsetMax = new Vector2(-FillInset, -FillInset);

        fillImg = CuteUIKit.Layer(areaRT, "Fill", CuteUIKit.Rounded(Mathf.Max(1, radius - Mathf.RoundToInt(FillInset))), highColor);
        fillRT  = fillImg.rectTransform;
    }
}
