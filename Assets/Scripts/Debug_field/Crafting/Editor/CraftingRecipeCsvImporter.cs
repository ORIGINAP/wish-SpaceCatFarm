using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// CSV로 레시피 에셋 일괄 생성
public static class CraftingRecipeCsvImporter
{
    private const string DefaultOutputFolder = "Assets/Scripts/Debug_field/Crafting/Recipes";

    [MenuItem("Tools/Crafting/CSV에서 레시피 가져오기")]
    public static void ImportFromCsv()
    {
        string path = EditorUtility.OpenFilePanel("레시피 CSV 선택", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(path)) return;

        if (!CsvImportUtility.TryReadCsvLines(path, out string[] lines, out string readError))
        {
            EditorUtility.DisplayDialog("가져오기 실패", readError, "확인");
            return;
        }
        if (lines.Length < 2)
        {
            EditorUtility.DisplayDialog("가져오기 실패", "CSV에 데이터 행이 없습니다 (헤더만 있거나 비어 있음).", "확인");
            return;
        }

        var colIndex = CsvImportUtility.ParseHeader(lines[0]);

        if (!colIndex.ContainsKey("recipeName") || !colIndex.ContainsKey("outputItem"))
        {
            EditorUtility.DisplayDialog("가져오기 실패",
                "CSV 헤더에 recipeName, outputItem 열이 반드시 있어야 합니다.", "확인");
            return;
        }

        var ingredientCols = new List<(int itemCol, int amountCol)>();
        var ingredientRegex = new Regex(@"^ingredient(\d+)$", RegexOptions.IgnoreCase);
        foreach (var kv in colIndex)
        {
            var m = ingredientRegex.Match(kv.Key);
            if (!m.Success) continue;
            if (colIndex.TryGetValue($"amount{m.Groups[1].Value}", out int amountCol))
                ingredientCols.Add((kv.Value, amountCol));
        }

        var itemLookup = BuildItemLookup();
        if (!AssetDatabase.IsValidFolder(DefaultOutputFolder))
            CsvImportUtility.CreateFolderRecursive(DefaultOutputFolder);

        int created = 0, updated = 0;
        var errors = new List<string>();
        var touchedRecipes = new List<CraftingRecipe>();

        for (int lineIdx = 1; lineIdx < lines.Length; lineIdx++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIdx])) continue;
            var row = CsvImportUtility.ParseCsvLine(lines[lineIdx]);
            int rowNum = lineIdx + 1;

            string recipeName = CsvImportUtility.GetCell(row, colIndex, "recipeName");
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                errors.Add($"{rowNum}행: recipeName이 비어 있어 건너뜀");
                continue;
            }

            string outputName = CsvImportUtility.GetCell(row, colIndex, "outputItem");
            if (!itemLookup.TryGetValue(outputName, out var outputItem))
            {
                errors.Add($"{rowNum}행 '{recipeName}': 결과 아이템 '{outputName}'을(를) 찾을 수 없어 건너뜀");
                continue;
            }

            var ingredients = new List<CraftingRecipe.Ingredient>();
            bool rowHasError = false;
            foreach (var (itemCol, amountCol) in ingredientCols)
            {
                string itemName = itemCol < row.Length ? row[itemCol].Trim() : "";
                if (string.IsNullOrEmpty(itemName)) continue;

                if (!itemLookup.TryGetValue(itemName, out var ingItem))
                {
                    errors.Add($"{rowNum}행 '{recipeName}': 재료 아이템 '{itemName}'을(를) 찾을 수 없어 건너뜀");
                    rowHasError = true;
                    break;
                }

                string amountStr = amountCol < row.Length ? row[amountCol].Trim() : "1";
                int amount = CsvImportUtility.ParseIntOr(amountStr, 1);
                ingredients.Add(new CraftingRecipe.Ingredient { item = ingItem, amount = Mathf.Max(1, amount) });
            }
            if (rowHasError) continue;

            string description = CsvImportUtility.GetCell(row, colIndex, "description");
            CraftingTier tier   = CsvImportUtility.ParseTier(CsvImportUtility.GetCell(row, colIndex, "tier"));

            int outputAmount = Mathf.Max(1, CsvImportUtility.ParseIntOr(CsvImportUtility.GetCell(row, colIndex, "outputAmount"), 1));

            var recipe = FindExistingRecipe(recipeName);
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<CraftingRecipe>();
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{DefaultOutputFolder}/{CsvImportUtility.MakeSafeFileName(recipeName)}.asset");
                AssetDatabase.CreateAsset(recipe, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            recipe.recipeName   = recipeName;
            recipe.description  = description;
            recipe.tier         = tier;
            recipe.ingredients  = ingredients;
            recipe.outputItem   = outputItem;
            recipe.outputAmount = outputAmount;

            EditorUtility.SetDirty(recipe);
            touchedRecipes.Add(recipe);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int registered = RegisterIntoCraftingManager(touchedRecipes);

        var summary = new StringBuilder();
        summary.AppendLine($"생성: {created}개 / 갱신: {updated}개");
        summary.AppendLine(registered >= 0
            ? $"CraftingManager에 새로 등록: {registered}개"
            : "열린 씬에서 CraftingManager를 찾지 못해 자동 등록은 건너뛰었습니다. 수동으로 Recipes 목록에 추가해주세요.");

        if (errors.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"오류/건너뜀 {errors.Count}건:");
            summary.AppendLine(string.Join("\n", errors.Take(15)));
            if (errors.Count > 15) summary.AppendLine($"...외 {errors.Count - 15}건 (콘솔 로그 참고)");
        }

        Debug.Log($"[CraftingRecipeCsvImporter] 가져오기 완료\n{summary}");
        EditorUtility.DisplayDialog("레시피 가져오기 완료", summary.ToString(), "확인");
    }

    [MenuItem("Tools/Crafting/샘플 CSV 템플릿 생성")]
    public static void CreateSampleCsv()
    {
        string path = EditorUtility.SaveFilePanel("샘플 CSV 저장 위치", Application.dataPath, "recipes_sample", "csv");
        if (string.IsNullOrEmpty(path)) return;

        string content =
            "recipeName,description,tier,outputItem,outputAmount,ingredient1,amount1,ingredient2,amount2\n" +
            "석유 정제,철광석과 돌을 정제해 석유를 만듭니다,초급,석유,1,철광석,3,돌,5\n";

        CsvImportUtility.WriteSampleCsv(path, content);
    }

    private static Dictionary<string, FarmingItemData> BuildItemLookup()
    {
        var dict = new Dictionary<string, FarmingItemData>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var guid in AssetDatabase.FindAssets("t:FarmingItemData"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<FarmingItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null) continue;

            if (!dict.ContainsKey(asset.name)) dict[asset.name] = asset;
            if (!string.IsNullOrEmpty(asset.itemName) && !dict.ContainsKey(asset.itemName))
                dict[asset.itemName] = asset;
        }
        return dict;
    }

    private static CraftingRecipe FindExistingRecipe(string recipeName)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:CraftingRecipe"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<CraftingRecipe>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && asset.recipeName == recipeName) return asset;
        }
        return null;
    }

    private static int RegisterIntoCraftingManager(List<CraftingRecipe> recipes)
    {
        if (recipes.Count == 0) return 0;

        var manager = Object.FindFirstObjectByType<CraftingManager>();
        if (manager == null) return -1;

        var so = new SerializedObject(manager);
        var recipesProp = so.FindProperty("recipes");

        int added = 0;
        foreach (var recipe in recipes)
        {
            bool exists = false;
            for (int i = 0; i < recipesProp.arraySize; i++)
            {
                if (recipesProp.GetArrayElementAtIndex(i).objectReferenceValue == recipe) { exists = true; break; }
            }
            if (exists) continue;

            int idx = recipesProp.arraySize;
            recipesProp.InsertArrayElementAtIndex(idx);
            recipesProp.GetArrayElementAtIndex(idx).objectReferenceValue = recipe;
            added++;
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
        return added;
    }
}
