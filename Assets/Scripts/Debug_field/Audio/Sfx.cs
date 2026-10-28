using UnityEngine;

public enum SfxId
{
    UiClick, UiHover, UiOpen, UiClose, UiError, UiConfirm,
    Place, Dismantle, Plant, Harvest, Pickup, MineHit, BlockBreak,
    LaserMining, LaserSeeding, LaserHarvest,
    Craft, Buy, Sell, Upgrade, FuelInsert, StoreItem, Equip,
    QuestComplete, Notify, Save, GameStart, StormWarning, StormHit, Boost, CoreLevelUp,
    ShuttleLaunch, ShuttleLand,
}

public static class Sfx
{
    public static void Play(SfxId id, float volume = 1f)
    {
        var audio = GameAudio.Instance;
        if (audio != null) audio.PlayUi(id, volume);
        GamepadRumble.OnSfx(id);
    }

    public static void PlayAt(SfxId id, Vector3 worldPos, float volume = 1f)
    {
        var audio = GameAudio.Instance;
        if (audio != null) audio.PlayWorld(id, worldPos, volume);
    }
}
