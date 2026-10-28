using System.Collections.Generic;
using UnityEngine;

public class LinkLinesView : MonoBehaviour
{
    private static readonly Color ItemLinkColor = new(1f, 0.85f, 0.15f, 0.85f);
    private static readonly Color FuelLinkColor = new(1f, 0.55f, 0.25f, 0.9f);
    private const float LineWidth    = 0.04f;
    private const int   SortingOrder = WorldConnectionLine.UnderSpritesOrder;

    private static LinkLinesView instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private readonly List<LineRenderer> pool = new();
    private readonly HashSet<TileItem> visited = new();
    private bool dirty = true;

    public static void Refresh()
    {
        if (instance == null)
            instance = new GameObject("LinkLinesView").AddComponent<LinkLinesView>();
        instance.dirty = true;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        Redraw();
    }

    private void Redraw()
    {
        int used = 0;
        visited.Clear();
        var gm = GridManager.Instance;
        if (gm != null)
        {
            // 발전소 연결은 파이프 쪽에서 그림
            foreach (var tile in gm.GetDynamicTiles())
            {
                if (tile == null) continue;
                visited.Add(tile);
                var links = tile.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    var other = links[i];
                    if (other == null || visited.Contains(other)) continue;
                    bool fuel = tile is AutoFuelPipe || other is AutoFuelPipe || tile is Generator || other is Generator;
                    Draw(used++, WorldConnectionLine.UnderSprites(tile.transform.position),
                                 WorldConnectionLine.UnderSprites(other.transform.position),
                                 fuel ? FuelLinkColor : ItemLinkColor);
                }
            }
        }
        for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
    }

    private void Draw(int index, Vector3 from, Vector3 to, Color color)
    {
        LineRenderer line;
        if (index < pool.Count)
        {
            line = pool[index];
            line.enabled = true;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = line.endColor = color;
        }
        else
        {
            line = WorldConnectionLine.Create(from, to, color, LineWidth, SortingOrder, transform);
            pool.Add(line);
        }
    }
}
