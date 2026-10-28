using UnityEngine;
using System.Collections.Generic;

public class StormShield : TileItem
{
    // FindObjectsOfType 대신 레지스트리
    private static readonly List<StormShield> _all = new();
    public static IReadOnlyList<StormShield> All => _all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _all.Clear();

    public override string AbilityText => $"반경 {shieldRadius:0.#} 안의 밭을 모래폭풍 · 눈보라로부터 지켜요";

    [Header("방어막 설정")]
    [Tooltip("보호 범위 반경 (월드 유닛)")]
    [SerializeField] private float shieldRadius = 10f;

    [Header("실시간 상태")]
    [Tooltip("false면 방어막이 비활성화되어 작물을 보호하지 않습니다")]
    [SerializeField] private bool isActive = true;

    [Header("게임 내 범위 표시")]
    [SerializeField] private Color outlineColor = Color.white;
    [Tooltip("외곽선 두께 (월드 유닛)")]
    [SerializeField] [Range(0.01f, 0.3f)] private float outlineWidth = 0.04f;
    [SerializeField] [Range(8, 128)] private int outlineSegments = 64;

    private void OnEnable()  => _all.Add(this);
    private void OnDisable() => _all.Remove(this);

    private void Start()
    {
        if (IsGhost) return;
        BuildRangeOutline();
    }

    private void BuildRangeOutline()
        => WorldCircleOutline.Create(transform, shieldRadius, outlineColor, outlineWidth, outlineSegments);

    public bool Protects(Vector3 worldPos) =>
        isActive && !IsGhost && Vector2.Distance(transform.position, worldPos) <= shieldRadius;

    public void SetActive(bool active) => isActive = active;

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var color = isActive
            ? new Color(0.3f, 0.6f, 1f, 0.2f)
            : new Color(0.5f, 0.5f, 0.5f, 0.15f);

        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawSolidDisc(transform.position, Vector3.forward, shieldRadius);

        color.a = isActive ? 0.8f : 0.4f;
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, shieldRadius);
    }
#endif
}
