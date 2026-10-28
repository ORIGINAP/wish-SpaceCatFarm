using UnityEngine;

public class PowerRelay : TileItem
{
    public const int DefaultSupplyRadius = 30;

    [Header("전력 중계탑")]
    [Tooltip("전기를 주는 범위")]
    [SerializeField, Min(1)] private int supplyRadius = DefaultSupplyRadius;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;

    public int SupplyRadius => supplyRadius;

    public override string AbilityText => $"주변 {supplyRadius}칸에 전기를 보내요 (중계탑 범위 안에 또 세우면 더 멀리)";

    public bool IsEnergized { get; private set; }

    public TileItem Upstream { get; private set; }

    public Generator Root { get; private set; }

    public void ClearNetwork()
    {
        IsEnergized = false;
        Upstream    = null;
        Root        = null;
    }

    public void SetNetwork(bool energized, TileItem upstream, Generator root)
    {
        IsEnergized = energized;
        Upstream    = upstream;
        Root        = root;
        ApplyLook();
    }

    private LineRenderer previewOutline;

    private void Start()
    {
        ApplyLook();
        if (IsGhost)
        {
            int w = DropItemData != null ? DropItemData.tileWidth  : 3;
            int h = DropItemData != null ? DropItemData.tileHeight : 3;
            previewOutline = PowerRangeOverlay.CreateOutline(null, w, h, supplyRadius, PowerRangeOverlay.PreviewColor);
            return;
        }
        PowerLink.Attach(this, PowerLink.Kind.Relay);
    }

    private void LateUpdate()
    {
        if (previewOutline != null) previewOutline.transform.position = transform.position;
    }

    private void OnDestroy()
    {
        if (previewOutline != null) Destroy(previewOutline.gameObject);
    }

    private void ApplyLook()
    {
        if (spriteRenderer == null) return;
        var sprite = IsGhost || IsEnergized ? onSprite : offSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }
}
