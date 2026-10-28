using System.Collections.Generic;
using UnityEngine;

public class GameDataRegistry : ScriptableObject
{
    public const string ResourcePath = "GameDataRegistry";

    public List<FarmingItemData> items     = new();
    public List<SeedData>        seeds     = new();
    public List<ResourceData>    resources = new();
    public List<CraftingRecipe>  recipes   = new();

    private static GameDataRegistry loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => loaded = null;

    // 목록 갱신 전에 만든 에셋도 포함
    public static List<T> FindAll<T>() where T : ScriptableObject
    {
        if (loaded == null) loaded = Resources.Load<GameDataRegistry>(ResourcePath);

        var set = new HashSet<T>();
        if (loaded != null)
            foreach (var entry in loaded.ListOf(typeof(T)))
                if (entry is T t && t != null) set.Add(t);
        foreach (var t in Resources.FindObjectsOfTypeAll<T>())
            set.Add(t);
        return new List<T>(set);
    }

    private IEnumerable<ScriptableObject> ListOf(System.Type type)
    {
        if (type == typeof(FarmingItemData)) return items;
        if (type == typeof(SeedData))        return seeds;
        if (type == typeof(ResourceData))    return resources;
        if (type == typeof(CraftingRecipe))  return recipes;
        return System.Array.Empty<ScriptableObject>();
    }
}
