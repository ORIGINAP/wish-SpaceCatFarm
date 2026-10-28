using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// InGame 씬을 얹어서 월드 생성
[DefaultExecutionOrder(-500)]
public class LabBootstrap : MonoBehaviour
{
    [Header("실험용 꾸러미 (새로 시작할 때)")]
    [SerializeField] private string[] kitItems  = { "Item_CatStatue" };
    [SerializeField] private int[]    kitCounts = { 2 };

    private void Awake()
    {
        Lab.Active = true;
        Lab.Experiments.Clear();
        GetComponentsInChildren(false, Lab.Experiments);

        if (!Lab.LaunchPrepared) PrepareDefaultLaunch();
        Lab.LaunchPrepared = false;

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(NetBootstrap.InGameScene, LoadSceneMode.Additive);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Lab.Active = false;
        Lab.Experiments.Clear();
        Lab.ClearReserved();
    }

    private static void PrepareDefaultLaunch()
    {
        GameLaunchContext.IsNewGame         = true;
        GameLaunchContext.LaunchedFromMenu  = false;
        GameLaunchContext.PendingWorldSize  = WorldSize.Small;
        GameLaunchContext.PendingWorldTheme = WorldTheme.Wasteland;
        GameLaunchContext.PendingWorldSeed  = Random.Range(1, int.MaxValue);
        GameLaunchContext.PendingGridWidth  = GameLaunchContext.PendingGridHeight = 0;
        GameLaunchContext.PendingCharacter  = NetBootstrap.GuestLook;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != NetBootstrap.InGameScene) return;
        SceneManager.SetActiveScene(scene);
        LabPanelUI.Ensure();
        StartCoroutine(GiveKitWhenReady());
    }

    private IEnumerator GiveKitWhenReady()
    {
        if (!GameLaunchContext.IsNewGame) yield break;
        while (!Lab.WorldReady) yield return null;
        yield return null;
        var inv = Inventory.Instance;
        if (inv == null) yield break;
        var given = new System.Text.StringBuilder();
        for (int i = 0; i < kitItems.Length; i++)
        {
            var item  = NetWorld.Item(kitItems[i]);
            int count = i < kitCounts.Length ? kitCounts[i] : 1;
            if (item == null || count <= 0) continue;
            inv.AddItemOrDrop(item, count, PlayerTileController.Instance.transform.position);
            if (given.Length > 0) given.Append(", ");
            given.Append($"{item.itemName} {count}개");
        }
        Lab.Notify(given.Length > 0
            ? $"실험실에 온 걸 환영해요! 실험용 꾸러미: {given}  ·  [L] 실험실 창"
            : "실험실에 온 걸 환영해요!  ·  [L] 실험실 창");
    }
}
