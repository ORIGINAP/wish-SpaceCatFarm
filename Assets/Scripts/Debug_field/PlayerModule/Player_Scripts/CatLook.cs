using System;
using UnityEngine;

// 세이브/멀티에 숫자로 저장, 끝에만 추가
public enum CatColor { White, Cream, Cheese, Strawberry, Mint, Sky, Lavender, Gray, Black }

public enum CatAccessory { None, Ribbon, Flower, Sprout, Crown, PartyHat, Glasses, Halo }

[Serializable]
public class CatLook
{
    public const int MaxNameLength = 12;

    public string name;
    public int    color;
    public int    accessory;

    public CatLook Copy() => (CatLook)MemberwiseClone();

    public static string CleanName(string s)
    {
        s = (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length > MaxNameLength ? s.Substring(0, MaxNameLength).TrimEnd() : s;
    }

    private static readonly string[] SuggestedNames =
        { "나비", "치즈", "모모", "콩이", "보리", "두부", "호두", "밤톨", "구름", "별이", "망고", "쿠키", "토리", "라떼", "솜이", "까미" };

    public static string SuggestName() => SuggestedNames[UnityEngine.Random.Range(0, SuggestedNames.Length)];

    public static CatLook RandomLook() => new()
    {
        name      = SuggestName(),
        color     = UnityEngine.Random.Range(0, CatSkin.ColorCount),
        accessory = UnityEngine.Random.Range(0, CatSkin.AccessoryCount),
    };
}

public static class PlayerCharacter
{
    public static CatLook Current
    {
        get
        {
            var look = GameLaunchContext.PendingCharacter;
            if (look == null) GameLaunchContext.PendingCharacter = look = new CatLook();
            if (string.IsNullOrEmpty(look.name)) look.name = NetBootstrap.PlayerName;
            return look;
        }
    }

    public static string Name => Current.name;

    public static void Change(CatLook look)
    {
        GameLaunchContext.PendingCharacter = look.Copy();
        if (NetSession.IsReplica) NetBootstrap.GuestLook = look;

        var me = PlayerTileController.Instance;
        if (me != null && me.TryGetComponent(out CatSkinRenderer skin)) skin.SetLook(look);
        NetBootstrap.SendMyLook();
    }
}
