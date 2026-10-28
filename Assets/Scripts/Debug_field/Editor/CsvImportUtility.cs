using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public static class CsvImportUtility
{
    public static bool TryReadCsvLines(string path, out string[] lines, out string error)
    {
        try
        {
            // 엑셀이 열고 있어도 읽기
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var list = new List<string>();
            string line;
            while ((line = reader.ReadLine()) != null)
                list.Add(line);

            lines = list.ToArray();
            error = null;
            return true;
        }
        catch (IOException)
        {
            lines = null;
            error = "파일이 다른 프로그램(엑셀 등)에서 열려 있어 읽을 수 없습니다.\n해당 프로그램에서 파일을 닫고 다시 시도해주세요.";
            return false;
        }
    }

    public static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    public static Dictionary<string, int> ParseHeader(string headerLine)
    {
        var header = ParseCsvLine(headerLine);
        var colIndex = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Length; i++)
            colIndex[header[i].Trim()] = i;
        return colIndex;
    }

    public static string GetCell(string[] row, Dictionary<string, int> colIndex, string colName)
    {
        if (!colIndex.TryGetValue(colName, out int idx) || idx >= row.Length) return "";
        return row[idx].Trim();
    }

    public static int ParseIntOr(string raw, int fallback)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public static float ParseFloatOr(string raw, float fallback)
        => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

    public static bool ParseBool(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        return raw == "1"
            || raw.Equals("true", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("y", System.StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", System.StringComparison.OrdinalIgnoreCase)
            || raw == "예" || raw == "참";
    }

    public static CraftingTier ParseTier(string raw)
    {
        switch (raw?.Trim())
        {
            case "중급": case "Intermediate": case "intermediate": return CraftingTier.Intermediate;
            case "고급": case "Advanced":     case "advanced":     return CraftingTier.Advanced;
            default: return CraftingTier.Beginner;
        }
    }

    public static T FindAssetByName<T>(string name) where T : Object
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), name, System.StringComparison.OrdinalIgnoreCase))
                continue;

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
        }
        return null;
    }

    public static string MakeSafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public static void CreateFolderRecursive(string path)
    {
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    public static void WriteSampleCsv(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(true));
        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(path);
    }
}
