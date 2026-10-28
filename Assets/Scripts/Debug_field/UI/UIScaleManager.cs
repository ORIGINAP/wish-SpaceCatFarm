using UnityEngine;
using UnityEngine.UI;

public class UIScaleManager : MonoBehaviour
{
    public static UIScaleManager Instance => Services.Get<UIScaleManager>();

    private const string PrefsKey = "UIScale";

    [Header("범위")]
    [Tooltip("저장된 값이 없을 때 사용할 기본 배율")]
    [SerializeField] private float defaultScale = 1.0f;
    [SerializeField] private float minScale     = 0.5f;
    [SerializeField] private float maxScale     = 2.0f;
    [Tooltip("단축키 한 번에 움직이는 폭")]
    [SerializeField] private float step         = 0.1f;

    [Header("단축키 — 설정 메뉴가 생기면 제거해도 됩니다")]
    [SerializeField] private KeyCode increaseKey = KeyCode.Equals;
    [SerializeField] private KeyCode decreaseKey = KeyCode.Minus;
    [Tooltip("true면 Ctrl을 함께 눌러야 동작합니다")]
    [SerializeField] private bool    requireCtrl = true;

    public float Scale { get; private set; } = 1f;

    public event System.Action<float> OnScaleChanged;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        Scale = Mathf.Clamp(PlayerPrefs.GetFloat(PrefsKey, defaultScale), minScale, maxScale);
    }

    // 다른 UI 빌드 뒤에 적용
    private void Start() => Apply();

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    private void Update()
    {
        if (requireCtrl && !(GameInput.GetKey(KeyCode.LeftControl) || GameInput.GetKey(KeyCode.RightControl)))
            return;

        if      (GameInput.GetKeyDown(increaseKey)) SetScale(Scale + step);
        else if (GameInput.GetKeyDown(decreaseKey)) SetScale(Scale - step);
    }

    public void SetScale(float value)
    {
        // 0.05 단위 반올림
        float clamped = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, minScale, maxScale);
        if (Mathf.Approximately(clamped, Scale)) return;

        Scale = clamped;
        PlayerPrefs.SetFloat(PrefsKey, Scale);
        PlayerPrefs.Save();

        Apply();
        OnScaleChanged?.Invoke(Scale);
        NotificationUI.Instance?.Show($"UI 크기 {Scale * 100f:F0}%");
    }

    public void Apply()
    {
        var scalers = FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var s in scalers)
        {
            if (s.uiScaleMode != CanvasScaler.ScaleMode.ConstantPixelSize) continue;
            s.scaleFactor = Scale;
        }
    }

    public void ResetScale() => SetScale(defaultScale);
}
