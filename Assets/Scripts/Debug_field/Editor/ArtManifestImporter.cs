using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class ArtManifestImporter
{
    // integrate_manifest.py 출력
    private const string ManifestPath = "Tools/Art/stage/manifest.txt";

    [MenuItem("Tools/WISH/그림 반영 (Tools/Art)")]
    public static void Run()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string manifest = Path.Combine(root, ManifestPath);
        if (!File.Exists(manifest))
        {
            EditorUtility.DisplayDialog("그림 반영", $"{ManifestPath} 이 없습니다.\npython Tools/Art/integrate_manifest.py 를 먼저 실행하세요.", "확인");
            return;
        }

        var entries = new List<Entry>();
        foreach (var raw in File.ReadAllLines(manifest))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var p = line.Split('|');
            entries.Add(new Entry
            {
                kind   = p[0],
                src    = Path.Combine(root, p[1]),
                dst    = p[2],
                frames = int.Parse(p[3]),
                ppu    = p[4],
                filter = p[5],
                pivotY = p.Length > 6 ? float.Parse(p[6], CultureInfo.InvariantCulture) : 0.5f,
            });
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var e in entries)
            {
                if (e.kind == "inplace") e.oldWorldWidth = LeftmostSpriteWorldWidth(e.dst);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, e.dst)));
                File.Copy(e.src, Path.Combine(root, e.dst), true);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        int done = 0;
        foreach (var e in entries)
        {
            if (Apply(e, factory)) done++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[그림 반영] {done}/{entries.Count}개 반영했습니다.");
    }

    private class Entry
    {
        public string kind, src, dst, ppu, filter;
        public int    frames;
        public float  oldWorldWidth;
        public float  pivotY = 0.5f;
    }

    private static bool Apply(Entry e, SpriteDataProviderFactories factory)
    {
        var imp = AssetImporter.GetAtPath(e.dst) as TextureImporter;
        if (imp == null) { Debug.LogWarning($"[그림 반영] 가져오기 설정을 찾지 못함: {e.dst}"); return false; }

        var size = ImageSize(e.src);
        int frameW = size.x / Mathf.Max(1, e.frames);
        // auto면 기존 스프라이트 월드 크기 유지
        float ppu = e.ppu == "auto"
            ? (e.oldWorldWidth > 0f ? frameW / e.oldWorldWidth : 100f)
            : float.Parse(e.ppu, CultureInfo.InvariantCulture);
        bool mip = e.filter == "mip";

        imp.textureType = TextureImporterType.Sprite;
        if (e.kind == "new") imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = ppu;
        imp.filterMode          = mip ? FilterMode.Bilinear : FilterMode.Point;
        imp.mipmapEnabled       = mip;
        imp.textureCompression  = TextureImporterCompression.Uncompressed;
        imp.alphaIsTransparency = true;
        imp.wrapMode            = TextureWrapMode.Clamp;
        int need = Mathf.NextPowerOfTwo(Mathf.Max(size.x, size.y));
        if (imp.maxTextureSize < need) imp.maxTextureSize = need;
        foreach (var platform in new[] { "Standalone", "WebGL", "Android", "iPhone" })
        {
            var ps = imp.GetPlatformTextureSettings(platform);
            if (!ps.overridden) continue;
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            ps.format = TextureImporterFormat.Automatic;
            if (ps.maxTextureSize < need) ps.maxTextureSize = need;
            imp.SetPlatformTextureSettings(ps);
        }

        if (e.kind == "new")
        {
            var ts = new TextureImporterSettings();
            imp.ReadTextureSettings(ts);
            bool center = Mathf.Approximately(e.pivotY, 0.5f);
            ts.spriteAlignment = (int)(center ? SpriteAlignment.Center : SpriteAlignment.Custom);
            ts.spritePivot     = new Vector2(0.5f, e.pivotY);
            ts.spriteMeshType  = SpriteMeshType.FullRect;
            imp.SetTextureSettings(ts);
        }

        if (imp.spriteImportMode == SpriteImportMode.Multiple)
        {
            var dp = factory.GetSpriteEditorDataProviderFromObject(imp);
            dp.InitSpriteEditorDataProvider();
            var rects = dp.GetSpriteRects();
            System.Array.Sort(rects, (a, b) => a.rect.x.CompareTo(b.rect.x));
            for (int i = 0; i < rects.Length; i++)
            {
                int f = Mathf.Min(i, e.frames - 1);
                rects[i].rect      = new Rect(f * frameW, 0, frameW, size.y);
                rects[i].alignment = SpriteAlignment.Center;
                rects[i].pivot     = new Vector2(0.5f, 0.5f);
                rects[i].border    = Vector4.zero;
            }
            dp.SetSpriteRects(rects);
            dp.Apply();
        }

        imp.SaveAndReimport();
        return true;
    }

    private static float LeftmostSpriteWorldWidth(string assetPath)
    {
        float minX = float.MaxValue, width = 0f;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (o is not Sprite s) continue;
            if (s.rect.x < minX) { minX = s.rect.x; width = s.rect.width / s.pixelsPerUnit; }
        }
        return width;
    }

    private static Vector2Int ImageSize(string pngPath)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(pngPath));
        var size = new Vector2Int(tex.width, tex.height);
        Object.DestroyImmediate(tex);
        return size;
    }
}
