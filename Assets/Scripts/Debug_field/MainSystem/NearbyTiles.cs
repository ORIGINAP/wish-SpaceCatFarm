using System.Collections.Generic;
using UnityEngine;

// 그리드 바뀔 때만 다시 구하는 캐시
public sealed class NearbyTiles
{
    private readonly TileType       type;
    private readonly List<TileItem> tiles = new();
    private int        version = -1;
    private Vector2Int min, max;
    private int        radius;

    public NearbyTiles(TileType type) => this.type = type;

    public List<TileItem> Get(TileItem owner, int radius)
    {
        var gm = GridManager.Instance;
        if (gm == null) { tiles.Clear(); version = -1; return tiles; }

        if (version != gm.Version || owner.FootprintMin != min || owner.FootprintMax != max || radius != this.radius)
        {
            gm.GetTilesNearArea(owner.FootprintMin, owner.FootprintMax, radius, type, tiles);
            version     = gm.Version;
            min         = owner.FootprintMin;
            max         = owner.FootprintMax;
            this.radius = radius;
        }
        return tiles;
    }
}
