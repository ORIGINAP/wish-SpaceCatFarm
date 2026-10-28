using UnityEngine;

public class DroppedItem : MonoBehaviour
{
    private FarmingItemData itemData;
    private int             amount;
    private Transform       playerTransform;
    private Rigidbody2D     rb;
    private bool            isMagnetic = false;
    private float           spawnTime;
    private float           nextPickupAttempt;

    private float magnetRadius;
    private float magnetSpeed;
    private float magnetDelay;
    private float pickupRadius;
    private float drag;

    private const float FullRetryDelay   = 1.5f;
    private const float StandardIconSize = 128f / 220f;
    private const float ThrowPickupDelay = 3f;
    private bool thrown;
    private bool homing;
    private static float lastFullNoticeTime = -999f;

    private static readonly System.Collections.Generic.List<DroppedItem> active = new();
    private const float MergeRadius = 0.75f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        lastFullNoticeTime = -999f;
    }

    public static DroppedItem Spawn(FarmingItemData item, int amount, Vector3 worldPos, Transform player, bool homing = false, bool merge = true)
    {
        if (item == null) return null;

        // 근처 같은 아이템과 합치기
        if (merge && amount > 0)
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var d = active[i];
                if (d == null) { active.RemoveAt(i); continue; }
                if (d.itemData != item || d.thrown || d.isMagnetic) continue;
                if (((Vector2)(d.transform.position - worldPos)).sqrMagnitude > MergeRadius * MergeRadius) continue;
                d.amount += amount;
                d.homing |= homing;
                return d;
            }

        if (player == null && PlayerTileController.Instance != null)
            player = PlayerTileController.Instance.transform;

        var cfg = DroppedItemManager.GetOrDefault();

        var go = new GameObject($"Drop_{item.itemName}");
        go.transform.position   = worldPos;
        go.transform.localScale = Vector3.one * (cfg.iconScale * FitScale(item.icon));

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = item.icon;
        sr.sortingOrder = cfg.sortingOrder;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale  = 0f;
        rb.linearDamping = cfg.drag;
        rb.angularDamping = 10f;
        rb.constraints   = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius    = 0.25f;

        var dropped = go.AddComponent<DroppedItem>();
        dropped.Initialize(item, amount, player, rb, cfg);
        dropped.homing = homing;

        Vector2 randomDir = Random.insideUnitCircle.normalized;
        rb.linearVelocity = randomDir * cfg.popSpeed;

        return dropped;
    }

    private static float FitScale(Sprite icon)
    {
        if (icon == null) return 1f;
        float side = Mathf.Max(icon.bounds.size.x, icon.bounds.size.y);
        return side > 0f ? StandardIconSize / side : 1f;
    }

    public static DroppedItem Throw(FarmingItemData item, int amount, Vector3 from, Vector2 toward)
    {
        var dropped = Spawn(item, amount, from, null, merge: false);
        if (dropped == null) return null;
        dropped.thrown            = true;
        // 던진 건 바로 다시 안 주움
        dropped.nextPickupAttempt = Time.time + ThrowPickupDelay;

        Vector2 dir = toward - (Vector2)from;
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.down;
        dropped.rb.linearVelocity = dir.normalized * DroppedItemManager.GetOrDefault().popSpeed * 2f;
        return dropped;
    }

    private void Initialize(FarmingItemData item, int amt, Transform player,
                            Rigidbody2D rigidbody, DroppedItemManager cfg)
    {
        itemData        = item;
        amount          = amt;
        playerTransform = player;
        rb              = rigidbody;
        spawnTime       = Time.time;
        active.Add(this);

        float rangeMult  = TechTreeManager.Instance != null ? TechTreeManager.Instance.PickupRangeMult : 1f;
        float equipBonus = EquipmentManager.Instance != null ? EquipmentManager.Instance.GetBonus(TechStatType.PickupRange) : 0f;
        magnetRadius = cfg.magnetRadius * (rangeMult + equipBonus);
        magnetSpeed  = cfg.magnetSpeed;
        magnetDelay  = cfg.magnetDelay;
        pickupRadius = cfg.pickupRadius;
        drag         = cfg.drag;
    }

    private void OnDestroy() => active.Remove(this);

    private void Update()
    {
        if (itemData == null) return;

        if (playerTransform == null)
        {
            if (PlayerTileController.Instance == null) return;
            playerTransform = PlayerTileController.Instance.transform;
        }

        if (Time.time < nextPickupAttempt) return;

        float elapsed = Time.time - spawnTime;
        float dist    = Vector2.Distance(transform.position, playerTransform.position);

        if (dist <= pickupRadius)
        {
            PickUp();
            return;
        }

        if (!isMagnetic && elapsed >= magnetDelay && (dist <= magnetRadius || homing))
        {
            isMagnetic       = true;
            rb.linearDamping = 0f;
        }

        if (isMagnetic)
        {
            Vector2 dir       = ((Vector2)playerTransform.position - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * magnetSpeed;
        }
    }

    private void PickUp()
    {
        if (Inventory.Instance == null) return;

        if (Inventory.Instance.CanAddItem(itemData) && Inventory.Instance.AddItem(itemData, amount, countAsNew: !thrown))
        {
            Sfx.PlayAt(SfxId.Pickup, transform.position);
            GamepadRumble.Play(Haptic.Pickup);
            Destroy(gameObject);
            return;
        }

        // 인벤 가득 참, 잠시 후 재시도
        isMagnetic         = false;
        rb.linearDamping   = drag;
        rb.linearVelocity  = Vector2.zero;
        nextPickupAttempt  = Time.time + FullRetryDelay;

        if (Time.time - lastFullNoticeTime > 3f)
        {
            lastFullNoticeTime = Time.time;
            NotificationUI.Instance?.Show("인벤토리가 가득 찼습니다. 아이템이 바닥에 남아 있습니다.");
        }
    }
}
