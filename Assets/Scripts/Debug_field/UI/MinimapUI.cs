using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MinimapUI : MonoBehaviour
{
    private const int   RevealRadius   = 12;
    // 192px 기준 화소당 2px
    private const int   ViewCells      = 48;
    private const float SmallSize      = 192f;
    private const float BigSize        = 420f;
    private const float Pad            = 6f;
    private const float CaptionHeight  = 16f;
    private const float ObjectRefresh  = 1f;
    private const float UploadInterval = 0.1f;
    private const int   StagingSize    = 128;

    private static MinimapUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        markers.Clear();
    }

    private RectTransform   frameRT;
    private RectTransform   mapRT;
    private RawImage        mapImage;
    private RectTransform   playerDot;
    private TextMeshProUGUI caption;
    private bool            big;
    private bool            hidden;

    private Texture2D  texture;
    private Color32[]  pixels;
    private Color32[]  terrain;
    private Color32[]  objects;
    private Color32[]  objectsScratch;
    private int        w, h, minX, minY;
    private int        generation = -1, gridVersion = -1, blockCount = -1;
    private float      nextObjects, nextUpload;
    private bool       uploadPending, fullUpload;
    private RectInt    dirtyPx;
    private Texture2D  staging;
    private Color32[]  stagingPixels;
    private readonly Dictionary<ResourceData, Color32> oreColors = new();
    private Vector2Int lastCell = new(int.MinValue, int.MinValue);
    private TerrainMap terrainMap;
    private Palette    palette;

    private static readonly Dictionary<object, (Vector2 pos, Color color)> markers = new();
    private readonly Dictionary<object, Image> markerViews = new();
    private readonly List<object> staleMarkers = new();

    public static void SetMarker(object key, Vector2 worldPos, Color color) => markers[key] = (worldPos, color);

    public static void RemoveMarker(object key) => markers.Remove(key);

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("MinimapUI", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(go, -1);
        instance = go.AddComponent<MinimapUI>();
        instance.Build();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        markers.Clear();
        if (terrainMap != null) terrainMap.OnCellsChanged -= HandleTerrainChanged;
        if (texture != null) Destroy(texture);
        if (staging != null) Destroy(staging);
    }

    private void Update()
    {
        var gm  = GridManager.Instance;
        var ptc = PlayerTileController.Instance;
        bool ready = gm != null && ptc != null && ExploredMap.Width > 0;

        if (ready && InputManager.Instance != null && InputManager.Instance.MinimapButton && !PlayerTileController.IsPauseMenuOpen())
            SetHidden(!hidden);
        if (ready && !TextInputFocus.Active && GameInput.GetKeyDown(KeyCode.M))
        {
            if (hidden) SetHidden(false);
            else Toggle();
        }

        bool show = ready && !hidden;
        if (frameRT.gameObject.activeSelf != show) frameRT.gameObject.SetActive(show);
        if (!ready) return;

        EnsureTexture(gm);

        var cell = gm.WorldToGrid(ptc.transform.position);
        if (cell != lastCell)
        {
            lastCell = cell;
            ExploredMap.Reveal(cell, RevealRadius);
        }

        if (Time.unscaledTime >= nextObjects)
        {
            nextObjects = Time.unscaledTime + ObjectRefresh;
            if (gm.Version != gridVersion || ResourceBlock.Active.Count != blockCount)
                RefreshObjects(gm);
        }

        if (ExploredMap.TakeDirty(out var revealed)) DrawCells(revealed);

        if (uploadPending && Time.unscaledTime >= nextUpload)
        {
            Upload();
            nextUpload = Time.unscaledTime + UploadInterval;
        }

        UpdateView(gm, ptc.transform.position);
    }

    private void EnsureTexture(GridManager gm)
    {
        if (texture != null && generation == ExploredMap.Generation && terrainMap == TerrainMap.Current) return;
        generation = ExploredMap.Generation;
        w    = ExploredMap.Width;
        h    = ExploredMap.Height;
        minX = ExploredMap.MinX;
        minY = ExploredMap.MinY;

        if (texture == null || texture.width != w * 2 || texture.height != h * 2)
        {
            if (texture != null) Destroy(texture);
            texture = new Texture2D(w * 2, h * 2, TextureFormat.RGBA32, false)
            {
                name       = "Minimap",
                filterMode = big ? FilterMode.Bilinear : FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp,
            };
            mapImage.texture = texture;
            pixels  = new Color32[w * h * 4];
            terrain = new Color32[w * h * 4];
            objects = new Color32[w * h];
            objectsScratch = new Color32[w * h];
        }

        if (terrainMap != TerrainMap.Current)
        {
            if (terrainMap != null) terrainMap.OnCellsChanged -= HandleTerrainChanged;
            terrainMap = TerrainMap.Current;
            if (terrainMap != null) terrainMap.OnCellsChanged += HandleTerrainChanged;
        }

        palette = Palette.For(GameLaunchContext.PendingWorldTheme);
        oreColors.Clear();
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                TerrainCell(i, j);
        BuildObjects(gm, objects);
        ExploredMap.TakeDirty(out _);
        lastCell = new Vector2Int(int.MinValue, int.MinValue);
        DrawCells(new RectInt(minX, minY, w, h));
        fullUpload = true;
    }

    private void HandleTerrainChanged(List<Vector2Int> cells)
    {
        if (terrain == null) return;
        foreach (var c in cells)
        {
            int i = c.x - minX, j = c.y - minY;
            if (i < 0 || j < 0 || i >= w || j >= h) continue;
            TerrainCell(i, j);
            DrawCells(new RectInt(c.x, c.y, 1, 1));
        }
    }

    private void TerrainCell(int i, int j)
    {
        int cx = minX + i, cy = minY + j;
        for (int b = 0; b < 2; b++)
            for (int a = 0; a < 2; a++)
                terrain[(j * 2 + b) * w * 2 + i * 2 + a] = TerrainColor(cx + a, cy + b);
    }

    private Color32 TerrainColor(int vx, int vy)
    {
        var map = terrainMap;
        if (map != null)
        {
            if (map.Has(TerrainMap.Layer.Chasm, vx, vy)) return palette.chasm;
            if (map.Has(TerrainMap.Layer.Mesa,  vx, vy)) return palette.mesa;
            if (map.Has(TerrainMap.Layer.Path,  vx, vy)) return palette.path;
            if (map.Has(TerrainMap.Layer.Patch, vx, vy)) return palette.patch;
        }
        return palette.ground;
    }

    private void BuildObjects(GridManager gm, Color32[] into)
    {
        gridVersion = gm.Version;
        blockCount  = ResourceBlock.Active.Count;
        System.Array.Clear(into, 0, into.Length);

        float half = gm.CellSize * 0.5f;
        foreach (var block in ResourceBlock.Active)
        {
            if (block == null) continue;
            var color = OreColor(block.Data);
            var a = gm.WorldToGrid(block.SolidRect.center - new Vector2(half, half));
            for (int y = a.y; y <= a.y + 1; y++)
                for (int x = a.x; x <= a.x + 1; x++)
                    SetObject(into, x, y, color);
        }
        foreach (var tile in gm.GetDynamicTiles())
        {
            if (tile == null || tile.IsGhost || tile.OccupiedCells == null) continue;
            var color = tile is PlantTile ? palette.plant : palette.facility;
            foreach (var c in tile.OccupiedCells) SetObject(into, c.x, c.y, color);
        }
        var core = CenterCore.Current;
        if (core != null && core.OccupiedCells != null)
            foreach (var c in core.OccupiedCells) SetObject(into, c.x, c.y, palette.core);
    }

    private void SetObject(Color32[] into, int x, int y, Color32 color)
    {
        int i = x - minX, j = y - minY;
        if (i >= 0 && j >= 0 && i < w && j < h) into[j * w + i] = color;
    }

    // 이름 문자열 할당 피하려고 캐시
    private Color32 OreColor(ResourceData data)
    {
        if (data == null) return palette.Ore(null);
        if (!oreColors.TryGetValue(data, out var color)) oreColors[data] = color = palette.Ore(data);
        return color;
    }

    // 바뀐 칸만 다시 칠함
    private void RefreshObjects(GridManager gm)
    {
        BuildObjects(gm, objectsScratch);
        (objects, objectsScratch) = (objectsScratch, objects);
        for (int k = 0; k < objects.Length; k++)
        {
            Color32 now = objects[k], was = objectsScratch[k];
            if (now.r == was.r && now.g == was.g && now.b == was.b && now.a == was.a) continue;
            DrawCells(new RectInt(minX + k % w, minY + k / w, 1, 1));
        }
    }

    private void DrawCells(RectInt r)
    {
        int x0 = Mathf.Max(r.xMin, minX), x1 = Mathf.Min(r.xMax, minX + w);
        int y0 = Mathf.Max(r.yMin, minY), y1 = Mathf.Min(r.yMax, minY + h);
        if (x0 >= x1 || y0 >= y1) return;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int  i = x - minX, j = y - minY;
                bool seen = ExploredMap.IsExplored(x, y);
                var  obj  = objects[j * w + i];
                for (int b = 0; b < 2; b++)
                {
                    int p = (j * 2 + b) * w * 2 + i * 2;
                    pixels[p]     = !seen ? palette.fog : obj.a != 0 ? obj : terrain[p];
                    pixels[p + 1] = !seen ? palette.fog : obj.a != 0 ? obj : terrain[p + 1];
                }
            }

        var px = new RectInt((x0 - minX) * 2, (y0 - minY) * 2, (x1 - x0) * 2, (y1 - y0) * 2);
        if (!uploadPending) dirtyPx = px;
        else
        {
            int ax = Mathf.Min(dirtyPx.xMin, px.xMin), ay = Mathf.Min(dirtyPx.yMin, px.yMin);
            int bx = Mathf.Max(dirtyPx.xMax, px.xMax), by = Mathf.Max(dirtyPx.yMax, px.yMax);
            dirtyPx = new RectInt(ax, ay, bx - ax, by - ay);
        }
        uploadPending = true;
    }

    // 바뀐 영역만 GPU에 올림
    private void Upload()
    {
        var r = dirtyPx;
        bool partial = !fullUpload && r.width <= StagingSize && r.height <= StagingSize
                       && SystemInfo.copyTextureSupport != UnityEngine.Rendering.CopyTextureSupport.None;
        if (partial)
        {
            if (staging == null)
            {
                staging = new Texture2D(StagingSize, StagingSize, TextureFormat.RGBA32, false)
                {
                    name = "MinimapStaging", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                };
                stagingPixels = new Color32[StagingSize * StagingSize];
            }
            int texW = w * 2;
            for (int y = 0; y < r.height; y++)
                System.Array.Copy(pixels, (r.y + y) * texW + r.x, stagingPixels, y * StagingSize, r.width);
            staging.SetPixels32(stagingPixels);
            staging.Apply(false);
            Graphics.CopyTexture(staging, 0, 0, 0, 0, r.width, r.height, texture, 0, 0, r.x, r.y);
        }
        else
        {
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }
        uploadPending = false;
        fullUpload    = false;
    }

    private void UpdateView(GridManager gm, Vector3 playerPos)
    {
        Vector2 p  = ((Vector2)playerPos - gm.WorldMin) / gm.CellSize;
        float   vw = big ? w : Mathf.Min(ViewCells, w);
        float   vh = big ? h : Mathf.Min(ViewCells, h);
        float   ox = Mathf.Clamp(p.x - vw * 0.5f, 0f, w - vw);
        float   oy = Mathf.Clamp(p.y - vh * 0.5f, 0f, h - vh);
        if (!big)
        {
            // 반 칸 단위로 맞춰 번짐 방지
            ox = Mathf.Round(ox * 2f) * 0.5f;
            oy = Mathf.Round(oy * 2f) * 0.5f;
        }
        mapImage.uvRect = new Rect(ox / w, oy / h, vw / w, vh / h);
        var size = mapRT.rect.size;
        playerDot.anchoredPosition = new Vector2((p.x - ox) / vw * size.x, (p.y - oy) / vh * size.y);
        UpdateMarkers(gm, ox, oy, vw, vh, size);
    }

    private void UpdateMarkers(GridManager gm, float ox, float oy, float vw, float vh, Vector2 size)
    {
        staleMarkers.Clear();
        foreach (var key in markerViews.Keys)
            if (!markers.ContainsKey(key)) staleMarkers.Add(key);
        foreach (var key in staleMarkers)
        {
            if (markerViews[key] != null) Destroy(markerViews[key].gameObject);
            markerViews.Remove(key);
        }

        float pulse = 1f + 0.18f * Mathf.Sin(Time.unscaledTime * 6f);
        foreach (var pair in markers)
        {
            if (!markerViews.TryGetValue(pair.Key, out var view) || view == null)
                markerViews[pair.Key] = view = BuildMarker();
            Vector2 c = (pair.Value.pos - gm.WorldMin) / gm.CellSize;
            var local   = new Vector2((c.x - ox) / vw * size.x, (c.y - oy) / vh * size.y);
            var clamped = new Vector2(Mathf.Clamp(local.x, 4f, size.x - 4f), Mathf.Clamp(local.y, 4f, size.y - 4f));
            var rt = view.rectTransform;
            rt.anchoredPosition = clamped;
            rt.localScale = Vector3.one * pulse;
            var col = pair.Value.color;
            col.a = clamped == local ? 1f : 0.55f;
            view.transform.GetChild(0).GetComponent<Image>().color = col;
        }
    }

    private Image BuildMarker()
    {
        var go = new GameObject("Marker", typeof(RectTransform));
        go.transform.SetParent(mapRT, false);
        go.transform.SetSiblingIndex(playerDot.GetSiblingIndex());
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.sizeDelta = new Vector2(9f, 9f);
        var ring = go.AddComponent<Image>();
        ring.sprite = CuteUIKit.Rounded(4);
        ring.color  = Color.white;
        ring.raycastTarget = false;
        CuteUIKit.Layer(go.transform, "Dot", CuteUIKit.Rounded(3), Color.white, 1.5f).raycastTarget = false;
        return ring;
    }

    private void SetHidden(bool value)
    {
        hidden = value;
        Sfx.Play(hidden ? SfxId.UiClose : SfxId.UiOpen);
        if (hidden) NotificationUI.Instance?.Show("미니맵을 껐어요 (BACK으로 다시 켜요)");
    }

    private void Toggle()
    {
        big = !big;
        if (texture != null) texture.filterMode = big ? FilterMode.Bilinear : FilterMode.Point;
        float size = big ? BigSize : SmallSize;
        frameRT.sizeDelta = new Vector2(size + Pad * 2f, size + Pad * 2f + CaptionHeight);
        caption.text = Caption(big);
    }

    private static string Caption(bool big)
        => MobileMode.Active ? (big ? "눌러서 작게 보기" : "눌러서 크게 보기")
                             : (big ? "[M] 작게 보기" : "[M] 크게 보기");

    private void Build()
    {
        var card = CuteUIKit.Card(transform, CuteUIKit.Latte, 14, "Minimap");
        frameRT = card.rectTransform;
        frameRT.anchorMin = frameRT.anchorMax = frameRT.pivot = new Vector2(1f, 1f);
        frameRT.anchoredPosition = new Vector2(-16f, -150f);
        frameRT.sizeDelta = new Vector2(SmallSize + Pad * 2f, SmallSize + Pad * 2f + CaptionHeight);
        CuteUIKit.Layer(card.transform, "Fill", CuteUIKit.Rounded(12), CuteUIKit.Cream, 2f);

        var mapGO = new GameObject("Map", typeof(RectTransform));
        mapGO.transform.SetParent(card.transform, false);
        mapRT = (RectTransform)mapGO.transform;
        mapRT.anchorMin = Vector2.zero;
        mapRT.anchorMax = Vector2.one;
        mapRT.offsetMin = new Vector2(Pad, Pad + CaptionHeight);
        mapRT.offsetMax = new Vector2(-Pad, -Pad);
        mapImage = mapGO.AddComponent<RawImage>();
        mapImage.raycastTarget = false;

        var dotGO = new GameObject("Player", typeof(RectTransform));
        dotGO.transform.SetParent(mapRT, false);
        playerDot = (RectTransform)dotGO.transform;
        playerDot.anchorMin = playerDot.anchorMax = Vector2.zero;
        playerDot.sizeDelta = new Vector2(10f, 10f);
        var ring = dotGO.AddComponent<Image>();
        ring.sprite = CuteUIKit.Rounded(5);
        ring.color  = Color.white;
        ring.raycastTarget = false;
        var dot = CuteUIKit.Layer(dotGO.transform, "Dot", CuteUIKit.Rounded(4), CuteUIKit.Strawberry, 1.5f);
        dot.raycastTarget = false;

        caption = CuteUIKit.Text(card.transform, Caption(false), 11f, CuteUIKit.CocoaSoft, FontStyles.Bold);
        var crt = caption.rectTransform;
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(1f, 0f);
        crt.pivot     = new Vector2(0.5f, 0f);
        crt.anchoredPosition = new Vector2(0f, 3f);
        crt.sizeDelta = new Vector2(0f, CaptionHeight);

        card.raycastTarget = true;
        var button = card.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(Toggle);
        card.gameObject.AddComponent<UiButtonSfx>();
        card.gameObject.AddComponent<CutePop>();

        frameRT.gameObject.SetActive(false);
    }

    private struct Palette
    {
        public Color32 ground, patch, path, chasm, mesa, fog, plant, facility, core;
        private bool   snowy;

        public static Palette For(WorldTheme theme) => theme == WorldTheme.Tundra
            ? new Palette
            {
                ground = Hex(0xD7E2EE), patch = Hex(0xECF2F8), path = Hex(0xBBD5EA), chasm = Hex(0x1C3649), mesa = Hex(0x8FA8BE),
                fog = Hex(0x2B2233), plant = Hex(0x3E9E4A), facility = Hex(0x5A4A6A), core = Hex(0xFFC93C), snowy = true,
            }
            : new Palette
            {
                ground = Hex(0xC37A51), patch = Hex(0xD0915E), path = Hex(0xAB6743), chasm = Hex(0x241210), mesa = Hex(0x7E4A33),
                fog = Hex(0x2B2233), plant = Hex(0x3E9E4A), facility = Hex(0xFFF6E6), core = Hex(0xFFD24A),
            };

        public Color32 Ore(ResourceData data)
        {
            switch (data != null ? data.name : "")
            {
                case "RD_Copper":   return Hex(0xFFB02E);
                case "RD_Iron":     return Hex(0xA23B4B);
                case "RD_Lithium":  return Hex(0xD08FE4);
                case "RD_Titanium": return Hex(0x4F86E8);
                case "RD_Uranium":  return Hex(0xC6F03C);
                case "RD_Aluminum": return snowy ? Hex(0x7A838F) : Hex(0xAAB6C8);
                case "RD_Diamond":  return snowy ? Hex(0x44A1B0) : Hex(0x5FE0F5);
                default:            return Hex(0xA8A8B8);
            }
        }

        private static Color32 Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
