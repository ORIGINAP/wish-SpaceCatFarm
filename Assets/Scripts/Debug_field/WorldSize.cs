public enum WorldSize { Small, Middle, Large }

public static class WorldSizeConfig
{
    public struct Entry
    {
        public int   gridWidth;
        public int   gridHeight;
        public float cellSize;
    }

    public static Entry Get(WorldSize size) => size switch
    {
        WorldSize.Small  => new Entry { gridWidth = 140, gridHeight = 140, cellSize = 0.75f },
        WorldSize.Middle => new Entry { gridWidth = 200, gridHeight = 200, cellSize = 0.75f },
        WorldSize.Large  => new Entry { gridWidth = 280, gridHeight = 280, cellSize = 0.75f },
        _                => new Entry { gridWidth = 200, gridHeight = 200, cellSize = 0.75f }
    };

    // 구버전 세이브 맵 크기
    public static Entry Legacy(WorldSize size) => size switch
    {
        WorldSize.Small  => new Entry { gridWidth = 70,  gridHeight = 70,  cellSize = 0.75f },
        WorldSize.Large  => new Entry { gridWidth = 140, gridHeight = 140, cellSize = 0.75f },
        _                => new Entry { gridWidth = 100, gridHeight = 100, cellSize = 0.75f }
    };

    public static string DisplayName(WorldSize size) => size switch
    {
        WorldSize.Small  => "스몰",
        WorldSize.Middle => "미들",
        WorldSize.Large  => "라지",
        _                => "?"
    };
}
