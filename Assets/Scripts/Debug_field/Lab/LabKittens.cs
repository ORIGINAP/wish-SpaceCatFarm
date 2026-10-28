using System.Collections.Generic;
using UnityEngine;

public class LabKittens : LabExperiment
{
    [SerializeField] private float scale       = 0.6f;
    [SerializeField] private float followGap   = 0.85f;
    [SerializeField] private float maxSpeed    = 7f;
    [SerializeField] private float teleportGap = 7f;

    public override string Id      => "kittens";
    public override string Title   => "아기 고양이";
    public override string Tags    => "싱글 · 멀티";
    public override Color  Accent  => CuteUIKit.Hex(0xF2A7C3);
    public override string Summary =>
        "대원마다 아기 고양이가 졸졸 따라다녀요. 멈춰 있으면 둘레를 어슬렁거리다 가끔 '냥' 하고 울어요. " +
        "털 색은 주인과 겹치지 않게 정해지고, 친구 화면에도 똑같이 보여요.";
    public override string Status  => $"아기 고양이 {kittens.Count}마리";

    private static readonly int[] Accessories = { (int)CatAccessory.Ribbon, (int)CatAccessory.Flower, (int)CatAccessory.Sprout };

    private sealed class Kitten
    {
        public Transform      owner, root;
        public SpriteRenderer sr, shadow;
        public int            color = -1, accessory, frame;
        public float          frameTimer, idle, nextWander, nextHeart, hop = -1f;
        public float          side;
        public Vector2        pos, target, lastOwner, moveDir = Vector2.down;
        public bool           seen;
    }

    private readonly Dictionary<ulong, Kitten> kittens = new();
    private readonly List<(ulong id, Transform root, int color)> owners = new();
    private readonly List<ulong> gone = new();

    protected override void LocalTick()
    {
        owners.Clear();
        var me = PlayerTileController.Instance;
        if (me != null) owners.Add((Lab.LocalId, me.transform, PlayerCharacter.Current.color));
        NetBootstrap.Avatars.CollectRoots(owners);

        foreach (var k in kittens.Values) k.seen = false;
        foreach (var (id, root, color) in owners)
        {
            if (!kittens.TryGetValue(id, out var k) || k.root == null) kittens[id] = k = Create(id, root);
            k.owner = root;
            k.seen  = true;
            int want = KittenColor(id, color);
            if (k.color != want) { k.color = want; k.frame = -1; }
            Step(k, Time.deltaTime);
        }

        gone.Clear();
        foreach (var pair in kittens) if (!pair.Value.seen) gone.Add(pair.Key);
        foreach (var id in gone)
        {
            if (kittens[id].root != null) Destroy(kittens[id].root.gameObject);
            kittens.Remove(id);
        }
    }

    // 모든 PC에서 같은 색
    private static int KittenColor(ulong id, int ownerColor)
    {
        int n = Mathf.Max(2, CatSkin.ColorCount);
        return (CatSkin.ValidColor(ownerColor) + 1 + (int)(id * 3 % (ulong)(n - 1))) % n;
    }

    private Kitten Create(ulong id, Transform owner)
    {
        var root = new GameObject($"Kitten_{id}").transform;
        Vector2 start = (Vector2)owner.position + new Vector2(0.6f, -0.3f);
        root.position = start;
        root.localScale = owner.lossyScale * scale;

        var sr = root.gameObject.AddComponent<SpriteRenderer>();
        if (owner.TryGetComponent<SpriteRenderer>(out var src))
        {
            sr.sharedMaterial = src.sharedMaterial;
            sr.sortingLayerID = src.sortingLayerID;
            sr.sortingOrder   = src.sortingOrder;
        }
        var shadow = LabArt.Make("Shadow", LabArt.Disc(), start, LabArt.OrderGround + 1, new Color(0.2f, 0.12f, 0.1f, 0.18f), null);
        shadow.transform.SetParent(root, true);
        shadow.transform.localScale = new Vector3(1.6f, 0.6f, 1f);
        shadow.transform.localPosition = new Vector3(0f, -1.25f, 0f);

        LabFx.Burst(start, CuteUIKit.Hex(0xF2A7C3), 8, 1.6f, 0.4f);
        return new Kitten
        {
            owner = owner, root = root, sr = sr, shadow = shadow, pos = start, target = start, lastOwner = owner.position,
            accessory = Accessories[(int)(id % (ulong)Accessories.Length)], side = id % 2 == 0 ? 0.35f : -0.35f,
            nextWander = Time.time + 1f, nextHeart = Time.time + Random.Range(12f, 25f),
        };
    }

    private void Step(Kitten k, float dt)
    {
        if (dt <= 0f) return;
        Vector2 owner = k.owner.position;
        Vector2 ownerVel = (owner - k.lastOwner) / dt;
        k.lastOwner = owner;

        if (ownerVel.sqrMagnitude > 0.09f)
        {
            k.idle = 0f;
            k.moveDir = ownerVel.normalized;
            var perp = new Vector2(-k.moveDir.y, k.moveDir.x);
            k.target = owner - k.moveDir * followGap + perp * k.side - (Vector2)Lab.FeetOffset * 0.2f;
        }
        else
        {
            k.idle += dt;
            if (k.idle > 1.2f && Time.time >= k.nextWander)
            {
                k.nextWander = Time.time + Random.Range(2.5f, 4.5f);
                k.target = owner + Random.insideUnitCircle.normalized * Random.Range(0.6f, 1.4f);
            }
            if (k.idle > 3f && Time.time >= k.nextHeart)
            {
                k.nextHeart = Time.time + Random.Range(15f, 30f);
                LabFx.FloatText(k.root.position + new Vector3(0f, 0.35f, 0f), "냥", CuteUIKit.Hex(0xF2A7C3), 2.2f);
            }
        }

        float dist = Vector2.Distance(k.pos, k.target);
        if (Vector2.Distance(k.pos, owner) > teleportGap)
        {
            k.pos = owner - k.moveDir * followGap;
            LabFx.Burst(k.pos, CuteUIKit.Hex(0xF2A7C3), 8, 1.6f, 0.4f);
            dist = 0f;
        }
        float speed = Mathf.Min(dist * 3.5f, maxSpeed);
        k.pos = Vector2.MoveTowards(k.pos, k.target, speed * dt);
        bool moving = speed > 0.3f;

        float lift = 0f;
        if (k.hop >= 0f)
        {
            k.hop += dt;
            lift = Mathf.Sin(Mathf.Clamp01(k.hop / 0.4f) * Mathf.PI) * 0.3f;
            if (k.hop >= 0.4f) k.hop = -1f;
        }
        k.root.position = new Vector3(k.pos.x, k.pos.y + lift, k.root.position.z);
        k.shadow.transform.position = new Vector3(k.pos.x, k.pos.y - 0.12f, 0f);

        var frames = CatFrames.Sprites;
        if (frames.Count == 0) return;
        int frame = k.frame;
        if (moving)
        {
            k.frameTimer += dt;
            if (k.frameTimer >= CatFrames.FrameSeconds * 0.6f) { k.frameTimer = 0f; frame = (Mathf.Max(0, frame) + 1) % frames.Count; }
        }
        else frame = 0;
        if (frame != k.frame)
        {
            k.frame = frame;
            k.sr.sprite = CatSkin.Get(frames[frame], k.color, k.accessory);
        }
    }

    public bool HighFive(ulong owner, out Vector3 at)
    {
        at = Vector3.zero;
        if (!kittens.TryGetValue(owner, out var k) || k.root == null || k.owner == null) return false;
        if (Vector2.Distance(k.pos, k.owner.position) > 3f) return false;
        k.hop = 0f;
        at = k.root.position;
        return true;
    }

    protected override void Cleanup()
    {
        foreach (var k in kittens.Values) if (k.root != null) Destroy(k.root.gameObject);
        kittens.Clear();
    }
}
