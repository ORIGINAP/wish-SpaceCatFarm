using UnityEngine;

public enum WorldTheme { Wasteland, Tundra }

public static class WorldThemeConfig
{
    public static string DisplayName(WorldTheme theme) => theme switch
    {
        WorldTheme.Tundra => "혹한지",
        _                 => "황무지",
    };

    public static string StormName(WorldTheme theme) => theme switch
    {
        WorldTheme.Tundra => "눈보라",
        _                 => "모래폭풍",
    };

    public static string StormSubject(WorldTheme theme) => theme switch
    {
        WorldTheme.Tundra => "눈보라가",
        _                 => "모래폭풍이",
    };

    public static string TerrainFolder(WorldTheme theme) => $"Terrain/{theme}";

    public static Color StormTint(WorldTheme theme) => theme switch
    {
        WorldTheme.Tundra => new Color(0.56f, 0.66f, 0.82f, 0.42f),
        _                 => new Color(0.7f, 0.43f, 0.25f, 0.42f),
    };

    public static Color StormParticle(WorldTheme theme) => theme switch
    {
        WorldTheme.Tundra => new Color(1f, 1f, 1f, 0.95f),
        _                 => new Color(1f, 0.88f, 0.66f, 0.9f),
    };
}
