using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public static class FarmingItemCsvImporter
{
    private const string DefaultOutputFolder = "Assets/Scripts/Debug_field/Farming/Items";

    [MenuItem("Tools/Items/CSV에서 아이템 가져오기")]
    public static void ImportFromCsv()
    {
        string path = EditorUtility.OpenFilePanel("아이템 CSV 선택", Application.dataPath, "csv");
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
        if (!colIndex.ContainsKey("itemName") || !colIndex.ContainsKey("itemType"))
        {
            EditorUtility.DisplayDialog("가져오기 실패",
                "CSV 헤더에 itemName, itemType 열이 반드시 있어야 합니다.", "확인");
            return;
        }

        if (!AssetDatabase.IsValidFolder(DefaultOutputFolder))
            CsvImportUtility.CreateFolderRecursive(DefaultOutputFolder);

        int created = 0, updated = 0;
        var errors = new List<string>();

        for (int lineIdx = 1; lineIdx < lines.Length; lineIdx++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIdx])) continue;
            var row = CsvImportUtility.ParseCsvLine(lines[lineIdx]);
            int rowNum = lineIdx + 1;

            string itemName = CsvImportUtility.GetCell(row, colIndex, "itemName");
            if (string.IsNullOrWhiteSpace(itemName))
            {
                errors.Add($"{rowNum}행: itemName이 비어 있어 건너뜀");
                continue;
            }

            string typeStr = CsvImportUtility.GetCell(row, colIndex, "itemType");
            if (!TryParseItemType(typeStr, out var itemType))
            {
                errors.Add($"{rowNum}행 '{itemName}': itemType '{typeStr}'을(를) 인식할 수 없어 건너뜀 " +
                           "(Tile/Seed/Resource/Tool/Equipment)");
                continue;
            }

            string assetNameRaw = CsvImportUtility.GetCell(row, colIndex, "assetName");
            string assetName = string.IsNullOrEmpty(assetNameRaw) ? itemName : assetNameRaw;

            var item = FindExistingItem(assetName, itemName);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<FarmingItemData>();
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{DefaultOutputFolder}/{CsvImportUtility.MakeSafeFileName(assetName)}.asset");
                AssetDatabase.CreateAsset(item, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            item.itemName    = itemName;
            item.description = CsvImportUtility.GetCell(row, colIndex, "description");
            item.itemType    = itemType;
            item.basePrice   = CsvImportUtility.ParseIntOr(CsvImportUtility.GetCell(row, colIndex, "basePrice"), 10);

            string iconName = CsvImportUtility.GetCell(row, colIndex, "icon");
            if (!string.IsNullOrEmpty(iconName))
            {
                var sprite = CsvImportUtility.FindAssetByName<Sprite>(iconName);
                if (sprite != null) item.icon = sprite;
                else errors.Add($"{rowNum}행 '{itemName}': 아이콘 스프라이트 '{iconName}'을(를) 찾을 수 없어 비워둠");
            }

            ApplyTypeSpecificFields(item, itemType, row, colIndex, rowNum, itemName, errors);

            EditorUtility.SetDirty(item);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var summary = new StringBuilder();
        summary.AppendLine($"생성: {created}개 / 갱신: {updated}개");

        if (errors.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"오류/알림 {errors.Count}건:");
            summary.AppendLine(string.Join("\n", errors.Take(15)));
            if (errors.Count > 15) summary.AppendLine($"...외 {errors.Count - 15}건 (콘솔 로그 참고)");
        }

        Debug.Log($"[FarmingItemCsvImporter] 가져오기 완료\n{summary}");
        EditorUtility.DisplayDialog("아이템 가져오기 완료", summary.ToString(), "확인");
    }

    [MenuItem("Tools/Items/샘플 CSV 템플릿 생성")]
    public static void CreateSampleCsv()
    {
        string path = EditorUtility.SaveFilePanel("샘플 CSV 저장 위치", Application.dataPath, "items_sample", "csv");
        if (string.IsNullOrEmpty(path)) return;

        string content =
            "itemName,assetName,itemType,description,basePrice,icon,minToolGrade,canUseFuel,fuelValue,isCropProduct,toolGrade,miningPower,miningSpeed\n" +
            "티타늄,Item_Titanium,Resource,고급 채굴로 얻는 희귀 자원,120,,2,TRUE,30,FALSE,,,\n" +
            "강화 곡괭이,Item_ReinforcedPickaxe,Tool,채굴량이 늘어나는 상위 도구,300,,,,,,3,1.5,\n";

        CsvImportUtility.WriteSampleCsv(path, content);
    }

    private static void ApplyTypeSpecificFields(FarmingItemData item, FarmingItemType itemType,
        string[] row, Dictionary<string, int> colIndex, int rowNum, string itemName, List<string> errors)
    {
        switch (itemType)
        {
            case FarmingItemType.Tile:
                string prefabName = CsvImportUtility.GetCell(row, colIndex, "tilePrefab");
                if (!string.IsNullOrEmpty(prefabName))
                {
                    var prefab = CsvImportUtility.FindAssetByName<GameObject>(prefabName);
                    if (prefab != null) item.tilePrefab = prefab;
                    else errors.Add($"{rowNum}행 '{itemName}': 프리팹 '{prefabName}'을(를) 찾을 수 없어 비워둠");
                }
                item.tileWidth  = CsvImportUtility.ParseIntOr(CsvImportUtility.GetCell(row, colIndex, "tileWidth"), 1);
                item.tileHeight = CsvImportUtility.ParseIntOr(CsvImportUtility.GetCell(row, colIndex, "tileHeight"), 1);
                break;

            case FarmingItemType.Seed:
                string seedName = CsvImportUtility.GetCell(row, colIndex, "seedData");
                if (!string.IsNullOrEmpty(seedName))
                {
                    var seed = CsvImportUtility.FindAssetByName<SeedData>(seedName);
                    if (seed != null) item.seedData = seed;
                    else errors.Add($"{rowNum}행 '{itemName}': SeedData '{seedName}'을(를) 찾을 수 없어 비워둠");
                }
                break;

            case FarmingItemType.Resource:
                item.minToolGrade  = CsvImportUtility.ParseIntOr(CsvImportUtility.GetCell(row, colIndex, "minToolGrade"), 1);
                item.canUseFuel    = CsvImportUtility.ParseBool(CsvImportUtility.GetCell(row, colIndex, "canUseFuel"));
                item.fuelValue     = CsvImportUtility.ParseFloatOr(CsvImportUtility.GetCell(row, colIndex, "fuelValue"), 20f);
                item.isCropProduct = CsvImportUtility.ParseBool(CsvImportUtility.GetCell(row, colIndex, "isCropProduct"));
                break;

            case FarmingItemType.Tool:
                item.toolGrade   = CsvImportUtility.ParseIntOr(CsvImportUtility.GetCell(row, colIndex, "toolGrade"), 1);
                item.miningPower = CsvImportUtility.ParseFloatOr(CsvImportUtility.GetCell(row, colIndex, "miningPower"), 1f);
                item.miningSpeed = CsvImportUtility.ParseFloatOr(CsvImportUtility.GetCell(row, colIndex, "miningSpeed"), 1f);
                break;

            case FarmingItemType.Equipment:
                break;
        }
    }

    private static bool TryParseItemType(string raw, out FarmingItemType type)
    {
        switch (raw?.Trim())
        {
            case "Tile": case "tile": case "타일":
                type = FarmingItemType.Tile; return true;
            case "Seed": case "seed": case "씨앗":
                type = FarmingItemType.Seed; return true;
            case "Resource": case "resource": case "자원":
                type = FarmingItemType.Resource; return true;
            case "Tool": case "tool": case "도구":
                type = FarmingItemType.Tool; return true;
            case "Equipment": case "equipment": case "장비":
                type = FarmingItemType.Equipment; return true;
            default:
                type = default;
                return false;
        }
    }

    private static FarmingItemData FindExistingItem(string assetName, string itemName)
    {
        FarmingItemData byDisplayName = null;

        foreach (var guid in AssetDatabase.FindAssets("t:FarmingItemData"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<FarmingItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset == null) continue;

            if (asset.name == assetName) return asset;
            if (byDisplayName == null && asset.itemName == itemName) byDisplayName = asset;
        }
        return byDisplayName;
    }
}
