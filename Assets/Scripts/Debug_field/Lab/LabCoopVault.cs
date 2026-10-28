using System;
using TMPro;
using UnityEngine;

public class LabCoopVault : LabExperiment
{
    [SerializeField] private float holdSeconds   = 1.2f;
    [SerializeField] private float relockSeconds = 60f;
    [SerializeField] private int   plateCells    = 3;

    public override string Id      => "vault";
    public override string Title   => "협동 금고";
    public override string Tags    => "협동 퍼즐";
    public override Color  Accent  => CuteUIKit.Hex(0xC98A5C);
    public override string Summary =>
        "발전소 근처 금고 양옆에 발판이 두 개 있어요. 두 발판을 동시에 1초 누르면 열려서 근처의 모두가 보물을 받아요. " +
        "혼자라면 고양이 조각상(실험용 꾸러미) 같은 설치물을 한쪽 발판에 올려 보세요. 열린 금고는 1분 뒤 다시 잠겨요.";
    public override string Status
    {
        get
        {
            if (!placed) return Lab.IsAuthority ? "금고 놓을 자리를 찾지 못했어요 ([금고 내 근처로])" : "금고를 받는 중";
            if (open) return $"열렸어요! 다시 잠길 때까지 {Mathf.CeilToInt(Mathf.Max(0f, relockAt - Time.time))}초";
            return $"발판 {(pressedA ? 1 : 0) + (pressedB ? 1 : 0)}/2  ·  금고 {Lab.DirectionName(Lab.LocalPos, center)}";
        }
    }

    [Serializable] private class SetupMsg { public float x, y, ax, ay, bx, by, relockIn; public bool open; }
    [Serializable] private class PlateMsg { public bool a, b; public float p; }

    private bool    placed, open, pressedA, pressedB;
    private Vector2 center, posA, posB;
    private Vector2Int cellA, cellB;
    private float   progress, relockAt, lastProgSent;
    private readonly System.Collections.Generic.List<Lab.Member> members = new();

    private Transform root;
    private SpriteRenderer chest, plateA, plateB, glowA, glowB, barBack, barFill;
    private TextMeshPro hint;

    private const string ChestPalette = "##5A4038 w#C98A5C s#A86E46 b#B5774B c#8E5A38 y#FFD66B Y#FFE9A8 K#5A4038 G#FFE36B h#FFFFFF";
    private static readonly string[] ChestRows =
    {
        "..############..", ".#wwwwwwwwwwws#.", "#wwwwwwwwwwwwss#", "#yyyyyyyyyyyyyy#", "#wwwwwwwwwwwwss#", "################",
        "#bbbbbbYYbbbbcc#", "#bbbbbYKKYbbbcc#", "#bbbbbbYYbbbbcc#", "#yyyyyyyyyyyyyy#", "#bbbbbbbbbbbbcc#", "#cccccccccccccc#", ".##############.",
    };
    private static readonly string[] OpenRows =
    {
        "..############..", ".#wwwwwwwwwwws#.", "#yyyyyyyyyyyyyy#", "#wwwwwwwwwwwwss#", "################", "#GGGhGGGGGhGGGG#",
        "#GhGGGGGhGGGhGG#", "#bbbbbbYYbbbbcc#", "#bbbbbbYYbbbbcc#", "#yyyyyyyyyyyyyy#", "#bbbbbbbbbbbbcc#", "#cccccccccccccc#", ".##############.",
    };
    private static readonly string[] PlateRows =
    {
        ".############.", "#ssssssssssssd", "#sllllllllllsd", "#sllwlwwlwllsd", "#sllllllllllsd", "#slllwwwwlllsd",
        "#sllwwwwwwllsd", "#sllwwwwwwllsd", "#slllwwwwlllsd", "#sllllllllllsd", "#ssssssssssssd", ".dddddddddddd.",
    };

    protected override void OnSwitched(bool on)
    {
        if (on && Lab.IsAuthority)
        {
            var core = CenterCore.Current != null ? (Vector2)CenterCore.Current.transform.position : Vector2.zero;
            Place(core, 6f, 16f);
        }
    }

    private void Place(Vector2 around, float minR, float maxR)
    {
        var gm = GridManager.Instance;
        float span = (plateCells + 1) * gm.CellSize * 2f;
        if (!Lab.FindClearArea(around, new Vector2(span, 2.6f), minR, maxR, out var c, Lab.ReservedExcept(this), 200, 0.8f))
        {
            Lab.Notify("협동 금고를 놓을 빈터를 찾지 못했어요. 넓은 곳에서 [금고 내 근처로]를 눌러 보세요.");
            return;
        }
        // 칸 가운데에 맞춤
        var cell = gm.WorldToGrid(c);
        Vector2 v = gm.GridToWorld(cell);
        Vector2 a = gm.GridToWorld(cell + new Vector2Int(-plateCells, 0));
        Vector2 b = gm.GridToWorld(cell + new Vector2Int(plateCells, 0));
        var msg = new SetupMsg { x = v.x, y = v.y, ax = a.x, ay = a.y, bx = b.x, by = b.y };
        Setup(msg);
        Lab.Reserve(this, new Rect(v.x - span * 0.5f, v.y - 1.3f, span, 2.6f));
        Broadcast("setup", msg);
    }

    private void Setup(SetupMsg m)
    {
        var gm = GridManager.Instance;
        center = new Vector2(m.x, m.y);
        posA   = new Vector2(m.ax, m.ay);
        posB   = new Vector2(m.bx, m.by);
        cellA  = gm.WorldToGrid(posA);
        cellB  = gm.WorldToGrid(posB);
        placed = true;
        open   = m.open;
        relockAt = Time.time + m.relockIn;
        progress = 0f;
        pressedA = pressedB = false;
        Build();
        LabMarkers.Set(this, center, Accent, "협동 금고", edge: false);
    }

    protected override void AuthorityTick()
    {
        if (!placed) return;
        if (open)
        {
            if (Time.time >= relockAt) { Relock(); Broadcast("relock"); }
            return;
        }

        Lab.CollectMembers(members);
        bool a = Pressed(cellA, posA), b = Pressed(cellB, posB);
        bool changed = a != pressedA || b != pressedB;
        pressedA = a;
        pressedB = b;
        float before = progress;
        progress = a && b ? progress + Time.deltaTime / holdSeconds : 0f;
        if (progress >= 1f) { Open(); return; }
        if (changed || (progress != before && Time.time - lastProgSent > 0.1f))
        {
            lastProgSent = Time.time;
            Broadcast("plates", new PlateMsg { a = a, b = b, p = progress }, reliable: changed);
        }
    }

    // 고양이나 설치물이 있으면 눌림
    private bool Pressed(Vector2Int cell, Vector2 pos)
    {
        var tile = GridManager.Instance.GetTile(cell);
        if (tile != null && !tile.IsGhost) return true;
        float half = GridManager.Instance.CellSize * 0.5f + 0.12f;
        foreach (var m in members)
        {
            var f = m.Feet;
            if (Mathf.Abs(f.x - pos.x) <= half && Mathf.Abs(f.y - pos.y) <= half) return true;
        }
        return false;
    }

    private void Open()
    {
        open     = true;
        progress = 1f;
        relockAt = Time.time + relockSeconds;
        var loot = LabLoot.RollVault();
        LabLoot.Settle(loot, center, 7f, members);
        ShowOpen(loot);
        Broadcast("open", loot);
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        if (kind != "move") return;
        Lab.CollectMembers(members);
        var around = Lab.LocalPos;
        foreach (var m in members) if (m.id == from) around = m.pos;
        Place(around, 3f, 12f);
    }

    protected override void SendState(ulong peer)
    {
        if (!placed) return;
        SendTo(peer, "setup", new SetupMsg
        {
            x = center.x, y = center.y, ax = posA.x, ay = posA.y, bx = posB.x, by = posB.y,
            open = open, relockIn = Mathf.Max(0f, relockAt - Time.time),
        });
    }

    protected override void OnEvent(string kind, string json)
    {
        switch (kind)
        {
            case "setup":  { var m = Read<SetupMsg>(json); if (m != null) Setup(m); break; }
            case "plates":
            {
                var m = Read<PlateMsg>(json);
                if (m == null || open) return;
                pressedA = m.a;
                pressedB = m.b;
                progress = m.p;
                break;
            }
            case "open":
            {
                var loot = Read<LabLoot.Bundle>(json);
                if (loot == null) return;
                open = true;
                relockAt = Time.time + relockSeconds;
                ShowOpen(loot);
                break;
            }
            case "relock": Relock(); break;
        }
    }

    private void Build()
    {
        if (root != null) Destroy(root.gameObject);
        root = new GameObject("CoopVault").transform;
        root.position = center;

        chest = LabArt.Make("Chest", LabArt.Pixel(open ? "vaultOpen" : "vault", open ? OpenRows : ChestRows, ChestPalette, new Vector2(0.5f, 0.1f)),
                            center, LabArt.OrderObject, Color.white, root);
        chest.transform.localScale = Vector3.one * 1.15f;
        var shadow = LabArt.Make("Shadow", LabArt.Disc(), center, LabArt.OrderGround + 1, new Color(0.2f, 0.12f, 0.1f, 0.25f), root);
        shadow.transform.localScale = new Vector3(1.3f, 0.4f, 1f);

        glowA  = Glow(posA);
        glowB  = Glow(posB);
        plateA = LabArt.Make("PlateA", LabArt.Pixel("plate", PlateRows, "##7A6A64 s#C9BDB4 l#E2D8CF w#F7879F d#8F817A", new Vector2(0.5f, 0.5f)),
                             posA, LabArt.OrderGround + 1, Color.white, root);
        plateB = LabArt.Make("PlateB", plateA.sprite, posB, LabArt.OrderGround + 1, Color.white, root);
        plateA.transform.localScale = plateB.transform.localScale = Vector3.one * 0.85f;

        hint = LabArt.Label(root, "두 발판을 동시에!", 2.1f, new Color(1f, 0.96f, 0.88f), new Vector3(0f, 1.55f, 0f), 150);
        barBack = LabArt.Make("BarBack", CuteUIKit.Rounded(4), center, LabArt.OrderAir, new Color(0.35f, 0.25f, 0.22f, 0.75f), root);
        barBack.drawMode = SpriteDrawMode.Sliced;
        barBack.size = new Vector2(1.2f, 0.16f);
        barBack.transform.localPosition = new Vector3(0f, 1.25f, 0f);
        barFill = LabArt.Make("BarFill", CuteUIKit.Rounded(3), center, LabArt.OrderAir + 1, CuteUIKit.ButtonMint, root);
        barFill.drawMode = SpriteDrawMode.Sliced;
    }

    private SpriteRenderer Glow(Vector2 at)
    {
        var g = LabArt.Make("PlateGlow", LabArt.Glow(), at, LabArt.OrderGround, new Color(0.45f, 0.9f, 0.65f, 0f), root);
        g.transform.localScale = Vector3.one * 1.6f;
        return g;
    }

    protected override void LocalTick()
    {
        if (!placed || root == null) return;
        SetPlate(plateA, glowA, pressedA);
        SetPlate(plateB, glowB, pressedB);

        bool filling = !open && progress > 0.01f;
        barBack.gameObject.SetActive(filling);
        barFill.gameObject.SetActive(filling);
        if (filling)
        {
            barFill.size = new Vector2(Mathf.Max(0.08f, 1.12f * Mathf.Clamp01(progress)), 0.1f);
            barFill.transform.localPosition = new Vector3(-0.56f + barFill.size.x * 0.5f, 1.25f, 0f);
            chest.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 35f) * 3f * progress);
        }
        else chest.transform.localRotation = Quaternion.identity;

        if (open) hint.text = $"다시 잠김 {Mathf.CeilToInt(Mathf.Max(0f, relockAt - Time.time))}초";
        else hint.text = pressedA || pressedB ? "하나 더!" : "두 발판을 동시에!";
        hint.transform.localPosition = new Vector3(0f, 1.55f + Mathf.Sin(Time.time * 3f) * 0.04f, 0f);
    }

    private static void SetPlate(SpriteRenderer plate, SpriteRenderer glow, bool on)
    {
        plate.color = on ? new Color(0.78f, 1f, 0.86f) : Color.white;
        plate.transform.localScale = Vector3.one * (on ? 0.8f : 0.85f);
        var c = glow.color;
        c.a = Mathf.MoveTowards(c.a, on ? 0.55f : 0f, Time.deltaTime * 3f);
        glow.color = c;
    }

    private void ShowOpen(LabLoot.Bundle loot)
    {
        if (chest != null)
            chest.sprite = LabArt.Pixel("vaultOpen", OpenRows, ChestPalette, new Vector2(0.5f, 0.1f));
        LabLoot.Deliver(loot, CuteUIKit.Hex(0xFFD66B));
        Sfx.Play(SfxId.CoreLevelUp, 0.8f);
        Lab.Notify("협동 금고가 열렸어요! 근처에 있던 대원 모두에게 보물이 쏟아져요.");
    }

    private void Relock()
    {
        open = false;
        progress = 0f;
        pressedA = pressedB = false;
        if (chest != null) chest.sprite = LabArt.Pixel("vault", ChestRows, ChestPalette, new Vector2(0.5f, 0.1f));
        if (root != null) LabFx.Burst(center + new Vector2(0f, 0.6f), Accent, 8, 1.6f, 0.4f);
    }

    protected override void Cleanup()
    {
        placed = open = false;
        Lab.Unreserve(this);
        LabMarkers.Remove(this);
        if (root != null) Destroy(root.gameObject);
        root = null;
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "금고 내 근처로", CuteUIKit.ButtonSky, () => Request("move"));
    }
}
