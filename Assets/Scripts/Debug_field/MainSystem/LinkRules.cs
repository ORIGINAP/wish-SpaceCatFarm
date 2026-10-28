using System.Collections.Generic;
using UnityEngine;

public static class LinkRules
{
    public const float MaxDistanceCells = 15f;

    public static bool IsLinkable(TileItem t) => t is ItemStorage || UsesStorage(t) || IsFuelSink(t);

    public static bool IsFuelSink(TileItem t) => t is Generator g && g.UsesFuel;

    public static bool UsesStorage(TileItem t)
        => t is ResourceExtractor || t is AutoHarvester || t is AutoSeeder || t is AutoSeller || t is Smelter || t is AutoFuelPipe
           || t is CatKitchen || t is Composter || t is ShuttlePad;

    public static bool CanLink(TileItem a, TileItem b, out string reason, bool checkDistance = true)
    {
        reason = null;
        if (a == null || b == null || a.IsGhost || b.IsGhost) { reason = "연결할 수 없어요"; return false; }
        if (a == b) { reason = "다른 설비를 클릭해 주세요"; return false; }
        if (!Compatible(a, b) && !Compatible(b, a)) { reason = PartnerHint(a); return false; }
        if (checkDistance && DistanceCells(a, b) > MaxDistanceCells)
        {
            reason = $"너무 멀어요 (최대 {MaxDistanceCells:0}칸)";
            return false;
        }
        return true;
    }

    private static bool Compatible(TileItem a, TileItem b)
    {
        if (a is ItemStorage)  return UsesStorage(b) || IsFuelSink(b);
        if (a is AutoFuelPipe) return b is AutoFuelPipe || IsFuelSink(b);
        return false;
    }

    public static string PartnerHint(TileItem a) => a switch
    {
        ItemStorage  => "창고는 자동화 설비(제련소 · 주방 · 수확기 · 착륙장 등)와 이을 수 있어요",
        AutoFuelPipe => "파이프는 창고 · 파이프 · 중앙 발전소와 이을 수 있어요",
        Generator    => "발전소는 창고 · 연료전송파이프와 이을 수 있어요",
        _            => "창고와 이어 주세요",
    };

    public static float DistanceCells(TileItem a, TileItem b)
    {
        int dx = Mathf.Max(0, Mathf.Max(a.FootprintMin.x - b.FootprintMax.x, b.FootprintMin.x - a.FootprintMax.x));
        int dy = Mathf.Max(0, Mathf.Max(a.FootprintMin.y - b.FootprintMax.y, b.FootprintMin.y - a.FootprintMax.y));
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    public static string NameOf(TileItem t)
    {
        if (t == null) return "";
        if (t is CenterCore) return "중앙 발전소";
        return t.SourceItem != null ? t.SourceItem.itemName : t.name.Replace("(Clone)", "");
    }

    public static string RoleOf(TileItem t) => t switch
    {
        ItemStorage       => "자동화 설비와 이으면 자동으로 넣고 꺼내 가요",
        ResourceExtractor => "캔 자원을 이어 준 창고에 넣어요",
        AutoHarvester     => "수확물을 이어 준 창고에 넣어요",
        AutoSeeder        => "씨앗통이 비면 이어 준 창고의 씨앗을 꺼내 심고, 퇴비가 있으면 함께 뿌려요",
        CatKitchen        => "창고의 재료를 가져와 요리하고, 요리를 돌려놔요",
        Composter         => "창고의 넘치는 씨앗을 퇴비로 만들고, 퇴비를 돌려놔요",
        AutoSeller        => "이어 준 창고의 작물을 팔아요",
        Smelter           => "창고의 광석을 가져와 녹이고, 주괴를 돌려놔요",
        AutoFuelPipe      => "발전소 연료가 떨어지면 이어 준 창고의 연료를 보내요",
        Generator         => "연료가 떨어지면 이어 준 창고의 연료로 채워요",
        ShuttlePad        => "이어 준 창고에서 셔틀 짐을 알아서 실어요",
        _                 => "",
    };

    private static bool hintShown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => hintShown = false;

    private static readonly List<TileItem> storageBuffer = new();

    private static bool AnyStorage()
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;
        gm.CollectTiles(TileType.Storage, storageBuffer);
        return storageBuffer.Count > 0;
    }

    public static void HintOnPlace(TileItem t)
    {
        if (hintShown || t == null || t.IsGhost || SaveManager.RecentlyLoaded || t.Links.Count > 0) return;
        if (t is not AutoSeeder && !AnyStorage()) return;
        hintShown = true;
        NotificationUI.Instance?.Show(t is AutoSeeder
            ? $"{NameOf(t)}: 씨앗을 든 채 클릭하면 씨앗통에 들어가요 (창 아래 [연결하기]로 창고와 이으면 모자랄 때 꺼내 써요)"
            : t is AutoHarvester || t is AutoSeller || t is AutoFuelPipe
                ? $"{NameOf(t)}: 클릭 → [연결하기]로 창고와 이어 주세요"
                : $"{NameOf(t)}: 클릭하면 열리는 창 맨 아래 [연결하기]로 창고와 이어 주세요");
    }
}
