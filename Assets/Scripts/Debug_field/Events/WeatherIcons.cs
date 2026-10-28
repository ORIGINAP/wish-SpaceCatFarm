using System.Collections.Generic;
using UnityEngine;

public static class WeatherIcons
{
    private static readonly Dictionary<WeatherType, Sprite> cache = new();

    public static Sprite Get(WeatherType w)
    {
        if (cache.TryGetValue(w, out var s) && s != null) return s;
        string file = w switch
        {
            WeatherType.StarRain     => "rain",
            WeatherType.Aurora       => "aurora",
            WeatherType.MeteorShower => "meteor",
            WeatherType.Storm        => "storm",
            _                        => "clear",
        };
        s = Resources.Load<Sprite>("Weather/" + file);
        cache[w] = s;
        return s;
    }
}
