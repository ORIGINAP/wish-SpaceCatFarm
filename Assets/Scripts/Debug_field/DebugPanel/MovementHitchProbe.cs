using System.IO;
using System.Text;
using UnityEngine;

// 임시 진단용, 카메라보다 늦게 기록
[DefaultExecutionOrder(32000)]
public class MovementHitchProbe : MonoBehaviour
{
    private const string CsvHeader = "frame,time,dt,fixedSteps,inX,inY,px,py,cx,cy,sx,sy,gc0";

    // 두 덩어리 번갈아 써서 용량 제한
    private const int ChunkMaxChars = 1 << 20;
    private StringBuilder sb     = new(1 << 20);
    private StringBuilder prevSb = new(1 << 20);
    private Transform player;
    private Camera    cam;
    private int       frames;

#if UNITY_EDITOR
    private const string PrefKey  = "WISH_MovementHitchProbe";
    private const string MenuPath = "Tools/WISH/진단/이동 끊김 기록 (켜기 · 끄기)";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!UnityEditor.EditorPrefs.GetBool(PrefKey, false)) return;
        var go = new GameObject("MovementHitchProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<MovementHitchProbe>();
    }

    [UnityEditor.MenuItem(MenuPath)]
    private static void ToggleProbe()
    {
        bool on = !UnityEditor.EditorPrefs.GetBool(PrefKey, false);
        UnityEditor.EditorPrefs.SetBool(PrefKey, on);
        Debug.Log(on ? "[MovementHitchProbe] 켬 — 다음 플레이부터 Temp/hitch_probe.csv에 기록합니다." : "[MovementHitchProbe] 끔");
    }

    [UnityEditor.MenuItem(MenuPath, true)]
    private static bool ToggleProbeValidate()
    {
        UnityEditor.Menu.SetChecked(MenuPath, UnityEditor.EditorPrefs.GetBool(PrefKey, false));
        return true;
    }
#endif

    private int fixedSteps;
    private void FixedUpdate() => fixedSteps++;

    private void LateUpdate()
    {
        if (sb.Length >= ChunkMaxChars)
        {
            (sb, prevSb) = (prevSb, sb);
            sb.Clear();
        }

        if (player == null)
        {
            var p = FindFirstObjectByType<Player>();
            if (p != null) player = p.transform;
        }
        if (cam == null)
        {
            cam = Camera.main;
            var brain = cam != null ? cam.GetComponent<Unity.Cinemachine.CinemachineBrain>() : null;
            var comp  = FindFirstObjectByType<Unity.Cinemachine.CinemachinePositionComposer>();
            sb.AppendLine($"# brainUpdate={brain?.UpdateMethod} damping={comp?.Damping} distance={comp?.CameraDistance} " +
                          $"hardLimits={comp?.Composition.HardLimits.Enabled}/{comp?.Composition.HardLimits.Size} " +
                          $"fixedDt={Time.fixedDeltaTime} vSync={QualitySettings.vSyncCount} targetFps={Application.targetFrameRate}");
        }
        if (player == null || cam == null) { fixedSteps = 0; return; }

        Vector3 pp = player.position;
        Vector3 cp = cam.transform.position;
        Vector3 sp = cam.WorldToScreenPoint(pp);

        sb.Append(Time.frameCount).Append(',')
          .Append(Time.unscaledTime.ToString("F4")).Append(',')
          .Append((Time.unscaledDeltaTime * 1000f).ToString("F2")).Append(',')
          .Append(fixedSteps).Append(',')
          .Append(GameInput.GetAxisRaw("Horizontal")).Append(',')
          .Append(GameInput.GetAxisRaw("Vertical")).Append(',')
          .Append(pp.x.ToString("F4")).Append(',').Append(pp.y.ToString("F4")).Append(',')
          .Append(cp.x.ToString("F4")).Append(',').Append(cp.y.ToString("F4")).Append(',')
          .Append(sp.x.ToString("F1")).Append(',').Append(sp.y.ToString("F1")).Append(',')
          .Append(System.GC.CollectionCount(0)).AppendLine();

        fixedSteps = 0;
        if (++frames % 600 == 0) Flush();
    }

    private void OnApplicationQuit() => Flush();
    private void OnDestroy()         => Flush();

    private void Flush()
    {
        try
        {
            string path = Path.Combine(Application.dataPath, "..", "Temp", "hitch_probe.csv");
            File.WriteAllText(path, CsvHeader + System.Environment.NewLine + prevSb.ToString() + sb.ToString());
        }
        catch {  }
    }
}
