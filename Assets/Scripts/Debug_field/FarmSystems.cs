using UnityEngine;

public class FarmSystems : MonoBehaviour
{
    // 씬에 없으면 런타임 생성
    public static void Ensure()
    {
        if (FindFirstObjectByType<FarmSystems>() != null) return;
        var go = new GameObject("FarmSystems");
        go.AddComponent<FarmSystems>();
        go.AddComponent<WeatherManager>();
        go.AddComponent<ShuttleService>();
        go.AddComponent<ResidentCats>();

        var fx = new GameObject("WeatherFx", typeof(RectTransform));
        fx.transform.SetParent(go.transform, false);
        fx.AddComponent<WeatherFx>();

        var info = new GameObject("PlotInfoUI", typeof(RectTransform));
        info.transform.SetParent(go.transform, false);
        info.AddComponent<PlotInfoUI>();
    }

    private void Start()
    {
        foreach (var cam in Camera.allCameras)
        {
            // y축 기준 스프라이트 정렬
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = Vector3.up;
        }

        WeatherHUD.Create();
        ShuttleUI.Ensure();
        HudDock.Create();
        QuestGuide.Create();
        JourneyUI.Create();
        OreTwinkle.Ensure();
    }
}
