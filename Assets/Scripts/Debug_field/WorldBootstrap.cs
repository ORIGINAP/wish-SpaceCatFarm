using UnityEngine;

[DefaultExecutionOrder(-150)]
public class WorldBootstrap : MonoBehaviour
{
    private WorldSize resolvedWorldSize;

    private void Awake()
    {
        // 이어하기면 세이브에서 맵 정보 복원
        bool continuing = !GameLaunchContext.IsNewGame && !NetSession.IsReplica;
        var  saved      = continuing ? SaveManager.ReadSlotStatic(GameLaunchContext.SelectedSlot) : null;

        resolvedWorldSize = ResolveWorldSize(saved, continuing);
        GameLaunchContext.PendingWorldSize = resolvedWorldSize;
        ResolveThemeAndSeed(saved, continuing);
        if (continuing) GameLaunchContext.PendingCharacter = saved?.character;
        HarvestLog.Clear();
        Codex.Clear();
        NetGuests.Clear();

        var cfg = ResolveGrid(saved, continuing);

        GridManager.Instance?.Initialize(cfg.gridWidth, cfg.gridHeight, cfg.cellSize);
        if (GridManager.Instance != null)
            WorldBackground.Create(GridManager.Instance, GameLaunchContext.PendingWorldTheme, GameLaunchContext.PendingWorldSeed);
        ExploredMap.Clear(cfg.gridWidth, cfg.gridHeight);
        MinimapUI.Ensure();
        FarmSystems.Ensure();

        var spawner = FindFirstObjectByType<ResourceBlockSpawner>();
        if (spawner != null)
        {
            float worldW = cfg.gridWidth  * cfg.cellSize;
            float worldH = cfg.gridHeight * cfg.cellSize;
            spawner.SetSpawnArea(Vector2.zero, new Vector2(worldW, worldH));
            float plantHalf = CenterCore.Size * cfg.cellSize * 0.5f;
            // 시작 기지 주변은 광물 스폰 금지
            spawner.SetReservedArea(Vector2.zero, plantHalf * 1.5f + 2.5f);
        }
    }

    private void Start()
    {
        if (NetSession.IsReplica) return;

        if (!GameLaunchContext.IsNewGame)
            SaveManager.Instance?.Load(GameLaunchContext.SelectedSlot);

        if (CenterCore.Current == null && CenterCore.Spawn(CenterCore.CenterAnchor) is CenterCore plant)
            plant.MoveOutside(PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null);
    }

    private static void ResolveThemeAndSeed(SaveData data, bool continuing)
    {
        if (!continuing) return;
        GameLaunchContext.PendingWorldTheme = data != null ? (WorldTheme)data.worldTheme : WorldTheme.Wasteland;
        GameLaunchContext.PendingWorldSeed  = data != null ? data.worldSeed : 0;
    }

    private static WorldSize ResolveWorldSize(SaveData data, bool continuing)
    {
        if (!continuing) return GameLaunchContext.PendingWorldSize;
        return data != null ? (WorldSize)data.worldSize : WorldSize.Middle;
    }

    private WorldSizeConfig.Entry ResolveGrid(SaveData data, bool continuing)
    {
        var cfg = WorldSizeConfig.Get(resolvedWorldSize);
        // 멀티 게스트는 호스트 맵 크기 사용
        if (NetSession.IsReplica && GameLaunchContext.PendingGridWidth > 0)
        {
            cfg.gridWidth  = GameLaunchContext.PendingGridWidth;
            cfg.gridHeight = GameLaunchContext.PendingGridHeight;
        }
        else if (continuing && data != null)
        {
            if (data.gridWidth > 0 && data.gridHeight > 0)
            {
                cfg.gridWidth  = data.gridWidth;
                cfg.gridHeight = data.gridHeight;
            }
            else cfg = WorldSizeConfig.Legacy(resolvedWorldSize);
        }
        return cfg;
    }
}
