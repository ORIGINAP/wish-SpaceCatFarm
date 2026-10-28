// 에셋에 int로 저장, 새 값은 끝에
public enum CropTrait
{
    None,
    StarFriends,
    CatLover,
    NightOwl,
    RainLover,
    WaterLover,
    StormLover,
    Shade,
    Cluster,
    Variety,
    Starlight,
    SunLover,
    Hearty,
}

public static class CropTraits
{
    public static string Name(CropTrait t) => t switch
    {
        CropTrait.StarFriends => "별자리",
        CropTrait.CatLover    => "고양이 사랑",
        CropTrait.NightOwl    => "밤 작물",
        CropTrait.RainLover   => "비 좋아함",
        CropTrait.WaterLover  => "물 좋아함",
        CropTrait.StormLover  => "폭풍 작물",
        CropTrait.Shade       => "그늘 작물",
        CropTrait.Cluster     => "군락",
        CropTrait.Variety     => "섞어심기",
        CropTrait.Starlight   => "별빛",
        CropTrait.SunLover    => "해 바라기",
        CropTrait.Hearty      => "대식가",
        _                     => "",
    };

    public static string Describe(CropTrait t) => t switch
    {
        CropTrait.StarFriends => $"다른 색 별이 곁에 심겨 있으면 색마다 {FarmConditions.StarFriendChance * 100f:0}% 확률로 1개 더 열려요",
        CropTrait.CatLover    => $"고양이가 {FarmConditions.CatRadiusCells}칸 안에 있으면 {FarmConditions.CatLoverMult:0.#}배 빨리 자라요",
        CropTrait.NightOwl    => $"밤(18~6시)에 {FarmConditions.NightOwlMult:0.#}배 빨리 자라요",
        CropTrait.RainLover   => $"별비가 내리면 {FarmConditions.RainLoverMult:0.#}배 빨리 자라요",
        CropTrait.WaterLover  => $"물을 받으면 {FarmConditions.WaterLoverMult:0.#}배 빨리 자라요 (다른 작물은 {FarmConditions.WaterMult:0.##}배)",
        CropTrait.StormLover  => $"폭풍에 날아가지 않고, 폭풍 날씨엔 {FarmConditions.StormLoverMult:0.#}배 빨리 자라요",
        CropTrait.Shade       => "전기 없이도 자라요",
        CropTrait.Cluster     => $"곁(상하좌우 · 대각선)의 같은 작물 하나마다 {FarmConditions.ClusterChance * 100f:0}% 확률로 1개 더 열려요",
        CropTrait.Variety     => $"곁에 심긴 다른 작물 한 종류마다 {FarmConditions.VarietyChance * 100f:0}% 확률로 1개 더 열려요",
        CropTrait.Starlight   => $"밤에 {FarmConditions.StarlightMult:0.#}배 빨리 자라고, 오로라가 뜨면 황금 작물이 훨씬 잘 나와요",
        CropTrait.SunLover    => $"낮(6~18시)에 {FarmConditions.SunLoverMult:0.#}배 빨리 자라요",
        CropTrait.Hearty      => "퇴비를 준 밭에서 2개 더 열려요 (다른 작물은 1개)",
        _                     => "",
    };
}
