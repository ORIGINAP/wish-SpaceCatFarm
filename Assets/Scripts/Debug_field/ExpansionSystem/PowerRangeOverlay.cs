using System.Collections.Generic;
using UnityEngine;

public class PowerRangeOverlay : MonoBehaviour
{
    public static readonly Color OnColor      = new(0.45f, 0.9f, 1f, 0.85f);
    public static readonly Color OffColor     = new(0.72f, 0.72f, 0.78f, 0.55f);
    public static readonly Color PreviewColor = new(1f, 1f, 1f, 0.9f);
    private const float Width        = 0.06f;
    private const int   SortingOrder = 45;

    private static PowerRangeOverlay instance;

    public static bool Showing => instance != null && instance.visible;

    public static event System.Action OnShowingChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        OnShowingChanged = null;
        circleBuffer = new Vector3[0];
    }

    private readonly List<LineRenderer> lines = new();
    private readonly Dictionary<FarmingItemData, bool> powerItemCache = new();
    private bool visible;
    private bool dirty = true;
    private bool subscribed;

    public static void Ensure()
    {
        if (instance == null) instance = new GameObject("PowerRangeOverlay").AddComponent<PowerRangeOverlay>();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (subscribed && EnergyManager.Instance != null) EnergyManager.Instance.OnNetworkRefreshed -= MarkDirty;
    }

    private void MarkDirty() => dirty = true;

    private void Update()
    {
        if (!subscribed && EnergyManager.Instance != null)
        {
            EnergyManager.Instance.OnNetworkRefreshed += MarkDirty;
            subscribed = true;
        }

        var item = PlayerTileController.Instance != null ? PlayerTileController.Instance.SelectedItem : null;
        bool show = item != null && IsPowerItem(item);
        if (show != visible)
        {
            visible = show;
            dirty   = true;
            OnShowingChanged?.Invoke();
        }
        if (!dirty) return;
        dirty = false;
        Rebuild();
    }

    private bool IsPowerItem(FarmingItemData item)
    {
        if (powerItemCache.TryGetValue(item, out bool yes)) return yes;
        var p = item.tilePrefab;
        yes = p != null && (p.GetComponent<PowerRelay>() != null || p.GetComponent<PlantTile>() != null ||
                            p.GetComponent<Smelter>() != null || p.GetComponent<GrowthBooster>() != null ||
                            p.GetComponent<CivFacility>() != null || p.GetComponent<SolarPanel>() != null);
        powerItemCache[item] = yes;
        return yes;
    }

    private void Rebuild()
    {
        int used = 0;
        var em = EnergyManager.Instance;
        if (visible && em != null)
        {
            foreach (var gen in em.Generators)
                if (gen != null && gen.DirectSupplyRadius > 0)
                    Draw(ref used, gen, gen.DirectSupplyRadius, gen.IsActive);
            foreach (var relay in em.Relays)
                if (relay != null)
                    Draw(ref used, relay, relay.SupplyRadius, relay.IsEnergized);
        }
        for (int i = used; i < lines.Count; i++) lines[i].enabled = false;
    }

    private void Draw(ref int used, TileItem node, int radius, bool on)
    {
        if (used >= lines.Count) lines.Add(CreateOutline(transform, 0, 0, 0f, on ? OnColor : OffColor));
        var line = lines[used++];
        var gm = GridManager.Instance;
        if (gm == null) { line.enabled = false; return; }

        int w = node.FootprintMax.x - node.FootprintMin.x + 1;
        int h = node.FootprintMax.y - node.FootprintMin.y + 1;
        Vector3 center = gm.GridToWorldCenter(node.FootprintMin, w, h);
        SetCircle(line, center, PowerRange.Reach(node.FootprintMin, node.FootprintMax, radius) * gm.CellSize);
        line.startColor = line.endColor = on ? OnColor : OffColor;
        line.enabled = true;
    }

    public static LineRenderer CreateOutline(Transform parent, int w, int h, float radiusCells, Color color)
    {
        var go = new GameObject("PowerRange", typeof(LineRenderer));
        if (parent != null) go.transform.SetParent(parent, false);
        var line = go.GetComponent<LineRenderer>();
        line.useWorldSpace  = instance != null && parent == instance.transform;
        line.loop           = true;
        line.positionCount  = 0;
        line.startWidth     = line.endWidth = Width;
        line.sharedMaterial = WorldConnectionLine.SharedMaterial;
        line.startColor     = line.endColor = color;
        line.numCornerVertices = 2;
        line.sortingOrder   = SortingOrder;

        var gm = GridManager.Instance;
        if (gm != null && w > 0 && h > 0)
            SetCircle(line, Vector3.zero, PowerRange.Reach(w, h, radiusCells) * gm.CellSize);
        return line;
    }

    private static Vector3[] circleBuffer = new Vector3[0];

    private static void SetCircle(LineRenderer line, Vector3 center, float radius)
    {
        int n = Mathf.Clamp(Mathf.CeilToInt(radius * 6f), 48, 160);
        if (circleBuffer.Length < n) circleBuffer = new Vector3[n];
        float step = Mathf.PI * 2f / n;
        for (int i = 0; i < n; i++)
            circleBuffer[i] = center + new Vector3(Mathf.Cos(i * step) * radius, Mathf.Sin(i * step) * radius);
        line.positionCount = n;
        line.SetPositions(circleBuffer);
    }
}
