using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LabSupplyDrop : LabExperiment
{
    [SerializeField] private float firstDelay  = 40f;
    [SerializeField] private float interval    = 150f;
    [SerializeField] private int   maxActive   = 2;
    [SerializeField] private float openSeconds = 2.5f;
    [SerializeField] private float lifetime    = 240f;

    public override string Id      => "supply";
    public override string Title   => "보급 캡슐";
    public override string Tags    => "싱글 · 협동";
    public override Color  Accent  => CuteUIKit.Butter;
    public override string Summary =>
        "몇 분마다 하늘에서 낙하산 캡슐이 떨어져요. 미니맵 · 화면 가장자리 화살표를 따라가 곁에 서 있으면 열려요 " +
        "(함께 서 있으면 더 빨리). 열릴 때 곁에 있던 모두가 광석 · 주괴 · 씨앗 · 코인을 받아요.";
    public override string Status
    {
        get
        {
            int waiting = 0;
            foreach (var c in capsules.Values) if (!c.opened) waiting++;
            string next = Lab.IsAuthority ? $"다음 보급까지 {Clock(nextDropAt - Time.time)}" : "호스트가 떨어뜨려요";
            return waiting > 0 ? $"{next}  ·  기다리는 캡슐 {waiting}개" : next;
        }
    }

    [Serializable] private class DropMsg  { public int id; public float x, y; public float progress; public bool landed; }
    [Serializable] private class ProgMsg  { public int id; public float p; }
    [Serializable] private class IdMsg    { public int id; }
    [Serializable] private class StateMsg { public List<DropMsg> list = new(); }

    private sealed class Capsule
    {
        public int     id;
        public Vector2 pos;
        public float   progress, spawned, lastSent = -1f;
        public bool    opened, landed;
        public Transform root, body;
        public SpriteRenderer sprite, beam, barBack, barFill, chute;
        public TMPro.TextMeshPro label;
    }

    private readonly Dictionary<int, Capsule> capsules = new();
    private readonly List<Lab.Member> members = new();
    private readonly List<int> remove = new();
    private float nextDropAt;
    private int   serial;

    private const string CapsulePalette = "##5A4038 w#FFF4E0 s#E8D2B8 h#FFFFFF p#F7879F q#D9667F y#FFD66B Y#FFF3B0 g#8FD0F0 G#D8F1FF";

    private static readonly string[] CapsuleRows =
    {
        "....######....", "...#hwwwww#...", "..#hwwwwwws#..", ".#hwwwggwwws#.", ".#wwwgGGgwws#.", ".#wwwgGGgwws#.",
        ".#wwwwggwwws#.", "#pppppppppppq#", "#pqqqqqqqqqqq#", "#wwwwwwwwwwss#", "#wwwyywwwwwss#", "#wwwyywwwwwss#",
        ".#wwwwwwwwss#.", ".#ssssssssss#.", "..#########...", ".#yy#....#yy#.", ".####....####.",
    };

    private static readonly string[] OpenRows =
    {
        "..............", ".....####.....", "....#hwww#....", "...#hwwwws#...", "....######....", "..............",
        "..............", "#pppppppppppq#", "#qYyYyYyYyYyq#", "#wwwwwwwwwwss#", "#wwwyywwwwwss#", "#wwwyywwwwwss#",
        ".#wwwwwwwwss#.", ".#ssssssssss#.", "..#########...", ".#yy#....#yy#.", ".####....####.",
    };

    private static readonly string[] ChuteRows =
    {
        "......########......", "...###pwwppwwp###...", "..#pppwwwppwwwppp#..", ".#ppppwwwppwwwpppp#.", "#pppppwwwppwwwppppp#",
        "#####.#########.####", ".#.....#.....#....#.", "..#.....#...#....#..", "...#.....#.#....#...", "....#.....#....#....",
        ".....#....#...#.....", "......#...#..#......", ".......#..#.#.......", "........#.##........",
    };

    private static Sprite CapsuleSprite => LabArt.Pixel("capsule", CapsuleRows, CapsulePalette, new Vector2(0.5f, 0.06f));
    private static Sprite OpenSprite    => LabArt.Pixel("capsuleOpen", OpenRows, CapsulePalette, new Vector2(0.5f, 0.06f));
    private static Sprite ChuteSprite   => LabArt.Pixel("chute", ChuteRows, "##8A6A5E p#F7879F w#FFF4E0", new Vector2(0.5f, 0f));

    protected override void OnSwitched(bool on)
    {
        if (on && Lab.IsAuthority) nextDropAt = Time.time + firstDelay;
    }

    protected override void AuthorityTick()
    {
        if (Time.time >= nextDropAt)
        {
            nextDropAt = Time.time + interval;
            if (CountWaiting() < maxActive) Drop(null);
        }

        Lab.CollectMembers(members);
        remove.Clear();
        foreach (var c in capsules.Values)
        {
            if (c.opened)
            {
                if (Time.time - c.spawned > 1f && Time.time - c.lastSent > 8f) remove.Add(c.id);
                continue;
            }
            if (Time.time - c.spawned > lifetime) { remove.Add(c.id); continue; }
            if (!c.landed) continue;

            int near = 0;
            foreach (var m in members) if (Vector2.Distance(m.Feet, c.pos) <= 1.4f) near++;
            float before = c.progress;
            c.progress = near > 0
                ? c.progress + Time.deltaTime * (1f + 0.6f * (near - 1)) / openSeconds
                : Mathf.Max(0f, c.progress - Time.deltaTime * 0.5f / openSeconds);
            if (c.progress >= 1f) { Open(c); continue; }
            if (!Mathf.Approximately(before, c.progress) && Time.time - c.lastSent > 0.2f)
            {
                c.lastSent = Time.time;
                Broadcast("prog", new ProgMsg { id = c.id, p = c.progress }, reliable: false);
            }
        }
        foreach (int id in remove)
        {
            Remove(id);
            Broadcast("gone", new IdMsg { id = id });
        }
    }

    private int CountWaiting()
    {
        int n = 0;
        foreach (var c in capsules.Values) if (!c.opened) n++;
        return n;
    }

    // 대원 근처 9~20m 빈자리
    private void Drop(Lab.Member? near)
    {
        Lab.CollectMembers(members);
        if (members.Count == 0) return;
        var who = near ?? members[UnityEngine.Random.Range(0, members.Count)];
        if (!Lab.FindClearArea(who.pos, new Vector2(1.6f, 1.6f), 9f, 20f, out var pos) &&
            !Lab.FindClearArea(who.pos, new Vector2(1.6f, 1.6f), 4f, 30f, out pos)) return;

        var c = new Capsule { id = ++serial, pos = pos, spawned = Time.time };
        capsules[c.id] = c;
        BuildVisual(c, falling: true);
        Broadcast("drop", new DropMsg { id = c.id, x = pos.x, y = pos.y });
        Announce(c.pos);
    }

    private void Open(Capsule c)
    {
        c.opened   = true;
        c.progress = 1f;
        c.lastSent = Time.time;
        var loot = LabLoot.RollSupply();
        loot.id = c.id;
        LabLoot.Settle(loot, c.pos, 4f, members);
        ShowOpen(c, loot);
        Broadcast("open", loot);
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        if (kind != "drop") return;
        if (CountWaiting() >= maxActive + 2) return;
        Lab.CollectMembers(members);
        foreach (var m in members) if (m.id == from) { Drop(m); return; }
        Drop(null);
    }

    protected override void SendState(ulong peer)
    {
        var s = new StateMsg();
        foreach (var c in capsules.Values)
            if (!c.opened) s.list.Add(new DropMsg { id = c.id, x = c.pos.x, y = c.pos.y, progress = c.progress, landed = c.landed });
        SendTo(peer, "state", s);
    }

    protected override void OnEvent(string kind, string json)
    {
        switch (kind)
        {
            case "drop":
            {
                var m = Read<DropMsg>(json);
                if (m == null || capsules.ContainsKey(m.id)) return;
                var c = new Capsule { id = m.id, pos = new Vector2(m.x, m.y), spawned = Time.time };
                capsules[c.id] = c;
                BuildVisual(c, falling: true);
                Announce(c.pos);
                break;
            }
            case "state":
            {
                var s = Read<StateMsg>(json);
                if (s == null) return;
                foreach (var m in s.list)
                {
                    if (capsules.ContainsKey(m.id)) continue;
                    var c = new Capsule { id = m.id, pos = new Vector2(m.x, m.y), spawned = Time.time, progress = m.progress };
                    capsules[c.id] = c;
                    BuildVisual(c, falling: !m.landed);
                }
                break;
            }
            case "prog":
            {
                var m = Read<ProgMsg>(json);
                if (m != null && capsules.TryGetValue(m.id, out var c) && !c.opened) c.progress = m.p;
                break;
            }
            case "open":
            {
                var loot = Read<LabLoot.Bundle>(json);
                if (loot != null && capsules.TryGetValue(loot.id, out var c) && !c.opened)
                {
                    c.opened = true;
                    c.progress = 1f;
                    ShowOpen(c, loot);
                }
                break;
            }
            case "gone":
            {
                var m = Read<IdMsg>(json);
                if (m != null) Remove(m.id);
                break;
            }
        }
    }

    private void Announce(Vector2 pos)
    {
        Lab.Notify($"보급 캡슐이 떨어지고 있어요! ({Lab.DirectionName(Lab.LocalPos, pos)})");
        Sfx.Play(SfxId.Notify);
    }

    private void BuildVisual(Capsule c, bool falling)
    {
        c.root = new GameObject($"SupplyCapsule_{c.id}").transform;
        c.root.position = c.pos;
        var shadow = LabArt.Make("Shadow", LabArt.Disc(), c.pos, LabArt.OrderGround + 1, new Color(0.2f, 0.12f, 0.1f, 0.25f), c.root);
        shadow.transform.localScale = new Vector3(0.95f, 0.38f, 1f);

        c.beam = LabArt.Make("Beam", LabArt.Beam(), c.pos, LabArt.OrderObject, new Color(1f, 0.85f, 0.42f, 0f), c.root);
        c.beam.transform.localScale = new Vector3(0.9f, 1.4f, 1f);

        c.body = new GameObject("Body").transform;
        c.body.SetParent(c.root, false);
        c.sprite = LabArt.Make("Capsule", CapsuleSprite, c.pos, LabArt.OrderObject, Color.white, c.body);
        c.sprite.transform.localPosition = Vector3.zero;
        c.chute = LabArt.Make("Chute", ChuteSprite, c.pos, LabArt.OrderObject, Color.white, c.body);
        c.chute.transform.localPosition = new Vector3(0f, 0.95f, 0f);

        c.barBack = LabArt.Make("BarBack", CuteUIKit.Rounded(4), c.pos, LabArt.OrderAir, new Color(0.35f, 0.25f, 0.22f, 0.75f), c.root);
        c.barBack.drawMode = SpriteDrawMode.Sliced;
        c.barBack.size = new Vector2(1f, 0.16f);
        c.barBack.transform.localPosition = new Vector3(0f, 1.45f, 0f);
        c.barFill = LabArt.Make("BarFill", CuteUIKit.Rounded(3), c.pos, LabArt.OrderAir + 1, CuteUIKit.Butter, c.root);
        c.barFill.drawMode = SpriteDrawMode.Sliced;
        c.barFill.transform.localPosition = new Vector3(-0.46f, 1.45f, 0f);
        c.barBack.gameObject.SetActive(false);
        c.barFill.gameObject.SetActive(false);

        c.label = LabArt.Label(c.root, "보급!", 2.4f, CuteUIKit.Butter, new Vector3(0f, 1.75f, 0f));
        c.label.gameObject.SetActive(false);

        LabMarkers.Set(c, c.pos, CuteUIKit.Butter, "보급 캡슐");
        if (falling) StartCoroutine(Fall(c, shadow));
        else Land(c, quiet: true);
    }

    private IEnumerator Fall(Capsule c, SpriteRenderer shadow)
    {
        const float height = 9f, duration = 3.2f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (c.root == null) yield break;
            float k = t / duration;
            float y = height * (1f - k);
            c.body.localPosition = new Vector3(Mathf.Sin(t * 2.4f) * 0.25f * (1f - k), y, 0f);
            c.body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 2.4f + 0.5f) * 8f * (1f - k));
            shadow.transform.localScale = new Vector3(0.95f, 0.38f, 1f) * Mathf.Lerp(0.3f, 1f, k);
            shadow.color = new Color(0.2f, 0.12f, 0.1f, Mathf.Lerp(0.05f, 0.25f, k));
            yield return null;
        }
        if (c.root == null) yield break;
        c.body.localPosition = Vector3.zero;
        c.body.localRotation = Quaternion.identity;
        Land(c, quiet: false);
    }

    private void Land(Capsule c, bool quiet)
    {
        c.landed = true;
        if (c.chute != null)
        {
            var chute = c.chute.transform;
            chute.SetParent(null, true);
            LabFx.Run(FloatAway(chute));
            c.chute = null;
        }
        c.label.gameObject.SetActive(true);
        if (quiet) return;
        LabFx.Dust(c.pos, 1.2f);
        Sfx.PlayAt(SfxId.Place, c.pos);
        if (Vector2.Distance(Lab.LocalPos, c.pos) < 7f) CameraShake.GetOrCreate().Shake(0.2f, 0.35f);
    }

    private static IEnumerator FloatAway(Transform chute)
    {
        var sr = chute.GetComponent<SpriteRenderer>();
        Vector3 start = chute.position;
        for (float t = 0f; t < 1.6f && chute != null; t += Time.deltaTime)
        {
            float k = t / 1.6f;
            chute.position = start + new Vector3(k * 1.6f, k * 0.8f, 0f);
            chute.rotation = Quaternion.Euler(0f, 0f, -k * 25f);
            sr.color = new Color(1f, 1f, 1f, 1f - k);
            yield return null;
        }
        if (chute != null) Destroy(chute.gameObject);
    }

    protected override void LocalTick()
    {
        foreach (var c in capsules.Values)
        {
            if (c.root == null || !c.landed) continue;
            float t = Time.time;
            if (!c.opened)
            {
                c.beam.color = new Color(1f, 0.85f, 0.42f, 0.32f + 0.12f * Mathf.Sin(t * 3f));
                bool opening = c.progress > 0.01f;
                c.barBack.gameObject.SetActive(opening);
                c.barFill.gameObject.SetActive(opening);
                if (opening)
                {
                    c.barFill.size = new Vector2(Mathf.Max(0.08f, 0.92f * c.progress), 0.1f);
                    c.barFill.transform.localPosition = new Vector3(-0.46f + c.barFill.size.x * 0.5f, 1.45f, 0f);
                    c.body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 30f) * 4f * c.progress);
                }
                else c.body.localRotation = Quaternion.identity;
                c.label.transform.localPosition = new Vector3(0f, 1.75f + Mathf.Sin(t * 3f) * 0.05f, 0f);
            }
            else
            {
                var col = c.beam.color;
                col.a = Mathf.Max(0f, col.a - Time.deltaTime * 0.5f);
                c.beam.color = col;
            }
        }
    }

    private void ShowOpen(Capsule c, LabLoot.Bundle loot)
    {
        if (c.root != null)
        {
            c.sprite.sprite = OpenSprite;
            c.body.localRotation = Quaternion.identity;
            c.barBack.gameObject.SetActive(false);
            c.barFill.gameObject.SetActive(false);
            c.label.text  = "열렸다!";
            c.label.color = CuteUIKit.Strawberry;
        }
        LabMarkers.Remove(c);
        LabLoot.Deliver(loot, CuteUIKit.Butter);
        Sfx.PlayAt(SfxId.Harvest, c.pos);
    }

    private void Remove(int id)
    {
        if (!capsules.TryGetValue(id, out var c)) return;
        capsules.Remove(id);
        LabMarkers.Remove(c);
        if (c.root != null) Destroy(c.root.gameObject);
    }

    protected override void Cleanup()
    {
        foreach (var id in new List<int>(capsules.Keys)) Remove(id);
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "지금 떨어뜨리기", CuteUIKit.ButtonButter, () => Request("drop"));
    }

    private static string Clock(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
