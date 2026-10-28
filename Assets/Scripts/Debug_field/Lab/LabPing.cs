using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class LabPing : LabExperiment
{
    [SerializeField] private float lifetime     = 8f;
    [SerializeField] private int   maxPerPlayer = 3;
    [SerializeField] private float cooldown     = 0.35f;

    public override string Id      => "ping";
    public override string Title   => "핑 찍기";
    public override string Tags    => "협동";
    public override Color  Accent  => CuteUIKit.Hex(0x6FB6EE);
    public override string Summary =>
        "[Z] 또는 마우스 가운데 버튼으로 커서 자리에 발바닥 핀을 꽂아요. 함께하는 모두의 화면 · 미니맵에 보이고, " +
        "화면 밖이면 가장자리 화살표가 길을 알려 줘요. 게임패드는 아래 [내 앞에 핑].";
    public override string Status  => pins.Count > 0 ? $"지금 꽂힌 핀 {pins.Count}개" : "꽂힌 핀 없음";

    [Serializable] private class PingMsg { public float x, y; public ulong who; public string name; }

    private sealed class Pin
    {
        public Transform      root, body;
        public SpriteRenderer[] fade;
        public TMPro.TextMeshPro label;
        public ulong          who;
        public float          born, nextRing;
        public Color          color;
    }

    private readonly List<Pin> pins = new();
    private readonly Dictionary<ulong, float> lastPing = new();
    private float localReady;

    private static readonly string[] PinRows =
    {
        "...#####...",
        "..#wwwww#..",
        ".#wwwwwww#.",
        "#wwwwwwwww#",
        "#wwwwwwwww#",
        "#wwwwwwwww#",
        "#wwwwwwwww#",
        ".#wwwwwww#.",
        ".#wwwwwww#.",
        "..#wwwww#..",
        "...#www#...",
        "....#w#....",
        ".....#.....",
    };

    protected override void LocalTick()
    {
        bool mouse = GameInput.GetMouseButtonDown(2) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
        if (!HotkeysBlocked && (GameInput.GetKeyDown(KeyCode.Z) || mouse)) PingAt(CursorWorld());

        for (int i = pins.Count - 1; i >= 0; i--)
        {
            var p = pins[i];
            float age = Time.time - p.born;
            if (p.root == null || age > lifetime) { RemoveAt(i); continue; }

            float pop = age < 0.25f ? CuteUIKit.EaseOutBack(age / 0.25f, 2.6f) : 1f;
            p.body.localScale    = Vector3.one * pop;
            p.body.localPosition = new Vector3(0f, 0.08f + Mathf.Sin(age * 4f) * 0.05f, 0f);
            float alpha = Mathf.Clamp01((lifetime - age) / 1f);
            foreach (var sr in p.fade)
            {
                var c = sr.color;
                c.a = alpha * (sr.name == "Glow" ? 0.4f : 1f);
                sr.color = c;
            }
            p.label.alpha = alpha;
            if (Time.time >= p.nextRing && age < lifetime - 1f)
            {
                p.nextRing = Time.time + 1.1f;
                LabFx.Ring(p.root.position, new Color(p.color.r, p.color.g, p.color.b, 0.8f), 0.9f, 0.8f);
            }
        }
    }

    private static Vector2 CursorWorld()
    {
        var cam = Camera.main;
        Vector3 sp = InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : GameInput.MousePosition;
        if (cam == null) return Lab.LocalPos;
        Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, -cam.transform.position.z));
        return wp;
    }

    private void PingAhead()
    {
        var me = PlayerTileController.Instance;
        if (me == null) return;
        var facing = me.TryGetComponent<Player>(out var pl) ? pl.FacingDirection : Vector2.down;
        PingAt((Vector2)me.transform.position + facing * 2f);
    }

    private void PingAt(Vector2 pos)
    {
        if (!IsOn || Time.unscaledTime < localReady) return;
        localReady = Time.unscaledTime + cooldown;
        if (!Lab.IsAuthority) Show(pos, Lab.LocalId, PlayerCharacter.Name);
        Request("ping", new PingMsg { x = pos.x, y = pos.y });
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        if (kind != "ping") return;
        var msg = Read<PingMsg>(json);
        if (msg == null) return;
        if (lastPing.TryGetValue(from, out float t) && Time.unscaledTime - t < cooldown * 0.7f) return;
        lastPing[from] = Time.unscaledTime;

        var gm  = GridManager.Instance;
        var pos = gm != null ? gm.ClampToWorldBounds(new Vector2(msg.x, msg.y)) : new Vector2(msg.x, msg.y);
        string name = Lab.MemberName(from);
        Show(pos, from, name);
        Broadcast("ping", new PingMsg { x = pos.x, y = pos.y, who = from, name = name }, except: from);
    }

    protected override void OnEvent(string kind, string json)
    {
        if (kind != "ping") return;
        var msg = Read<PingMsg>(json);
        if (msg != null) Show(new Vector2(msg.x, msg.y), msg.who, msg.name);
    }

    private void Show(Vector2 pos, ulong who, string name)
    {
        // 핀 많으면 오래된 것부터 제거
        int mine = 0;
        for (int i = pins.Count - 1; i >= 0; i--) if (pins[i].who == who) mine++;
        for (int i = 0; i < pins.Count && mine >= maxPerPlayer; i++)
            if (pins[i].who == who) { RemoveAt(i); mine--; i--; }

        var color = Lab.MemberColor(who);
        var root  = new GameObject($"Ping_{name}").transform;
        root.position = pos;
        var glow = LabArt.Make("Glow", LabArt.Disc(), pos, LabArt.OrderGround + 1, new Color(color.r, color.g, color.b, 0.4f), root);
        glow.transform.localScale = new Vector3(0.9f, 0.45f, 1f);

        var body = new GameObject("Body").transform;
        body.SetParent(root, false);
        var pin = LabArt.Make("Pin", LabArt.Pixel("pingPin", PinRows, "##4A3A50 w#FFFFFF", new Vector2(0.5f, 0f)),
                              pos, LabArt.OrderAir, color, body);
        pin.transform.localPosition = Vector3.zero;
        var paw = LabArt.Make("Paw", CuteUIKit.Paw(), pos, LabArt.OrderAir + 1, Color.white, body);
        paw.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        paw.transform.localScale    = Vector3.one * 0.42f;
        var label = LabArt.Label(root, name, 2.4f, color, new Vector3(0f, 1.15f, 0f));
        label.richText = false;

        pins.Add(new Pin
        {
            root = root, body = body, who = who, born = Time.time, nextRing = Time.time, color = color,
            fade = new[] { glow, pin, paw }, label = label,
        });
        LabMarkers.Set(root, pos, color, name);
        LabFx.Burst(pos, color, 8, 1.8f, 0.45f);
        Sfx.Play(SfxId.Notify, 0.6f);
    }

    private void RemoveAt(int i)
    {
        var p = pins[i];
        pins.RemoveAt(i);
        if (p.root == null) return;
        LabMarkers.Remove(p.root);
        Destroy(p.root.gameObject);
    }

    protected override void Cleanup()
    {
        for (int i = pins.Count - 1; i >= 0; i--) RemoveAt(i);
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "내 앞에 핑", CuteUIKit.ButtonSky, PingAhead);
    }
}
