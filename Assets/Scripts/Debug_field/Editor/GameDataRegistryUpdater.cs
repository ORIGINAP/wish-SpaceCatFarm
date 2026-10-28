using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 런타임 아이템 조회용 목록 자동 갱신
public class GameDataRegistryUpdater : AssetPostprocessor, IPreprocessBuildWithReport
{
    private const string AssetPath = "Assets/Resources/GameDataRegistry.asset";
    private static bool scheduled;

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Refresh();

    [MenuItem("Tools/WISH/게임 데이터 목록 갱신")]
    public static void Refresh()
    {
        var registry = AssetDatabase.LoadAssetAtPath<GameDataRegistry>(AssetPath);
        if (registry == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            registry = ScriptableObject.CreateInstance<GameDataRegistry>();
            AssetDatabase.CreateAsset(registry, AssetPath);
        }

        bool changed = Fill(registry.items) | Fill(registry.seeds) | Fill(registry.resources) | Fill(registry.recipes);
        if (!changed) return;
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssetIfDirty(registry);
    }

    private static bool Fill<T>(List<T> list) where T : Object
    {
        var found = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { "Assets" })
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .OrderBy(p => p, System.StringComparer.Ordinal)
            .Select(p => AssetDatabase.LoadAssetAtPath<T>(p))
            .Where(a => a != null)
            .ToList();
        if (found.SequenceEqual(list)) return false;

        list.Clear();
        list.AddRange(found);
        return true;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (scheduled) return;
        bool relevant = imported.Concat(deleted).Concat(moved).Any(p => p.EndsWith(".asset") && p != AssetPath);
        if (!relevant) return;

        scheduled = true;
        // 임포트 중엔 에셋 수정 불가라 지연
        EditorApplication.delayCall += () =>
        {
            scheduled = false;
            Refresh();
        };
    }
}
