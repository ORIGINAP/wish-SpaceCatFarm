using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LabMeteorShower : LabExperiment
{
    [SerializeField] private float firstDelay = 90f;
    [SerializeField] private float interval   = 240f;
    [SerializeField] private float duration   = 14f;
    [SerializeField] private int   meteors    = 6;

    private const float FallTime = 1.1f;

    public override string Id      => "meteor";
    public override string Title   => "유성우";
    public override string Tags    => "싱글 · 멀티";
    public override Color  Accent  => CuteUIKit.Hex(0xF28A3C);
    public override string Summary =>
        "몇 분마다 유성우가 쏟아져요. 몇 개는 근처에 떨어져 운석 광석(구리 ~ 다이아몬드)이 생겨요. " +
        "바닥의 붉은 원이 떨어질 자리예요 — 달려가 캐 보세요!";
    public override string Status => Time.time < showerEnd ? "유성우가 쏟아지는 중!"
                                   : Lab.IsAuthority ? $"다음 유성우까지 {Clock(nextShowerAt - Time.time)}" : "호스트가 불러요";

    [Serializable] private class Fall      { public float x, y, delay; public string ore; }
    [Serializable] private class ShowerMsg { public List<Fall> falls = new(); public float duration; }

    private static readonly (string ore, float weight)[] Ores =
    {
        ("RD_Copper", 28f), ("RD_Iron", 28f), ("RD_Lithium", 14f), ("RD_Aluminum", 14f),
        ("RD_Titanium", 9f), ("RD_Uranium", 4f), ("RD_Diamond", 3f),
    };

    private static readonly string[] RockRows =
    {
        "..####..", ".#oyyo#.", "#oyhyoo#", "#oyyoor#", "#ooorrr#", ".#orrr#.", "..####..",
    };

    private readonly List<Lab.Member> members = new();
    private float nextShowerAt, showerEnd, nextStreak;
    private Image overlay;
    private Transform fxRoot;

    private Transform Fx => fxRoot != null ? fxRoot : (fxRoot = new GameObject("MeteorFx").transform);

    protected override void OnSwitched(bool on)
    {
        if (on && Lab.IsAuthority) nextShowerAt = Time.time + firstDelay;
    }

    protected override void AuthorityTick()
    {
        if (Time.time >= showerEnd && Time.time >= nextShowerAt) StartShower();
    }

    private void StartShower()
    {
        nextShowerAt = Time.time + interval;
        Lab.CollectMembers(members);
        if (members.Count == 0) return;
        var msg = new ShowerMsg { duration = duration };
        int count = meteors + Mathf.Max(0, members.Count - 1) * 2;
        var taken = new List<Rect>();
        for (int i = 0; i < count; i++)
        {
            var who = members[UnityEngine.Random.Range(0, members.Count)];
            if (!Lab.FindClearArea(who.pos, new Vector2(1.2f, 1.2f), 5f, 15f, out var p, taken, 40, 1.1f)) continue;
            taken.Add(new Rect(p.x - 1.2f, p.y - 1.2f, 2.4f, 2.4f));
            msg.falls.Add(new Fall { x = p.x, y = p.y, delay = UnityEngine.Random.Range(1.5f, duration - 2.5f), ore = PickOre() });
        }
        msg.falls.Sort((a, b) => a.delay.CompareTo(b.delay));
        Play(msg);
        Broadcast("shower", msg);
        StartCoroutine(SpawnOres(msg));
    }

    private static string PickOre()
    {
        float total = 0f;
        foreach (var o in Ores) total += o.weight;
        float r = UnityEngine.Random.value * total;
        foreach (var o in Ores)
        {
            r -= o.weight;
            if (r <= 0f) return o.ore;
        }
        return Ores[0].ore;
    }

    // 그새 설치물 생겼으면 건너뜀
    private IEnumerator SpawnOres(ShowerMsg msg)
    {
        float start = Time.time;
        foreach (var f in msg.falls)
        {
            while (Time.time < start + f.delay + FallTime) yield return null;
            var data = NetWorld.Resource(f.ore);
            var pos  = new Vector2(f.x, f.y);
            if (data == null || !Lab.IsSpotClear(pos, 0.45f)) continue;
            var player = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null;
            ResourceBlock.CreateSaved(data, pos, player, data.maxResources);
        }
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        if (kind == "start" && Time.time >= showerEnd) StartShower();
    }

    protected override void OnEvent(string kind, string json)
    {
        if (kind != "shower") return;
        var msg = Read<ShowerMsg>(json);
        if (msg != null) Play(msg);
    }

    private void Play(ShowerMsg msg)
    {
        showerEnd = Time.time + msg.duration;
        Lab.Notify("유성우가 쏟아져요! 떨어진 운석을 캐 보세요");
        Sfx.Play(SfxId.Boost);
        foreach (var f in msg.falls) StartCoroutine(MeteorFall(f));
    }

    protected override void LocalTick()
    {
        bool raining = Time.time < showerEnd;
        if (overlay == null && raining) overlay = BuildOverlay();
        if (overlay != null)
        {
            float target = raining ? 0.32f : 0f;
            var c = overlay.color;
            c.a = Mathf.MoveTowards(c.a, target, Time.deltaTime * 0.5f);
            overlay.color = c;
            overlay.enabled = c.a > 0.001f;
        }
        if (raining && Time.time >= nextStreak)
        {
            nextStreak = Time.time + UnityEngine.Random.Range(0.12f, 0.35f);
            StartCoroutine(Streak());
        }
    }

    private static Image BuildOverlay()
    {
        var go = new GameObject("MeteorSky", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(go, -5).GetComponent<GraphicRaycaster>().enabled = false;
        var img = new GameObject("Tint", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        img.rectTransform.anchorMin = Vector2.zero;
        img.rectTransform.anchorMax = Vector2.one;
        img.rectTransform.offsetMin = img.rectTransform.offsetMax = Vector2.zero;
        img.color = new Color(0.10f, 0.08f, 0.24f, 0f);
        img.raycastTarget = false;
        return img;
    }

    private IEnumerator Streak()
    {
        var cam = Camera.main;
        if (cam == null) yield break;
        float h = cam.orthographicSize, w = h * cam.aspect;
        Vector3 c = cam.transform.position;
        var start = new Vector3(c.x + UnityEngine.Random.Range(-w * 0.6f, w * 1.1f), c.y + h * UnityEngine.Random.Range(0.3f, 1.1f), 0f);
        var dir   = new Vector3(-1f, -0.55f, 0f).normalized;
        float len = UnityEngine.Random.Range(1.4f, 2.8f), life = UnityEngine.Random.Range(0.35f, 0.6f);
        var tail = LabArt.Make("Streak", LabArt.Beam(), start, LabArt.OrderAir, new Color(1f, 0.95f, 0.8f, 0.9f), Fx);
        tail.transform.rotation   = Quaternion.FromToRotation(Vector3.up, -dir);
        tail.transform.localScale = new Vector3(0.12f, len / 4f, 1f);
        for (float t = 0f; t < life && tail != null; t += Time.deltaTime)
        {
            float k = t / life;
            tail.transform.position = start + dir * (k * 6f);
            tail.color = new Color(1f, 0.95f, 0.8f, 0.9f * Mathf.Sin(k * Mathf.PI));
            yield return null;
        }
        if (tail != null) Destroy(tail.gameObject);
    }

    private IEnumerator MeteorFall(Fall f)
    {
        float wait = f.delay - 1f;
        if (wait > 0f) yield return new WaitForSeconds(wait);
        var target = new Vector3(f.x, f.y, 0f);

        var warn = LabArt.Make("MeteorWarn", LabArt.Ring(), target, LabArt.OrderGround + 1, new Color(1f, 0.35f, 0.25f, 0.8f), Fx);
        for (float t = 0f; t < 1f && warn != null; t += Time.deltaTime)
        {
            float s = Mathf.Lerp(1.6f, 1f, t) * (1f + 0.08f * Mathf.Sin(t * 30f));
            warn.transform.localScale = new Vector3(s, s * 0.55f, 1f);
            yield return null;
        }
        if (warn != null) Destroy(warn.gameObject);

        var start = target + new Vector3(3.5f, 9f, 0f);
        var dir   = (target - start).normalized;
        var rock  = LabArt.Make("Meteor", LabArt.Pixel("meteor", RockRows, "##5A3A30 o#F28A3C y#FFD06B h#FFF6D8 r#C2552E", new Vector2(0.5f, 0.5f)),
                                start, LabArt.OrderAir + 2, Color.white, Fx);
        rock.transform.localScale = Vector3.one * 1.3f;
        var glow  = LabArt.Make("Glow", LabArt.Glow(), start, LabArt.OrderAir + 1, new Color(1f, 0.6f, 0.25f, 0.85f), rock.transform);
        glow.transform.localScale = Vector3.one * 1.4f;
        var trail = LabArt.Make("Trail", LabArt.Beam(), start, LabArt.OrderAir, new Color(1f, 0.7f, 0.35f, 0.9f), Fx);
        trail.transform.rotation   = Quaternion.FromToRotation(Vector3.up, -dir);
        trail.transform.localScale = new Vector3(0.45f, 0.9f, 1f);

        for (float t = 0f; t < FallTime; t += Time.deltaTime)
        {
            if (rock == null) yield break;
            float k = t / FallTime;
            var p = Vector3.Lerp(start, target, k * k);
            rock.transform.position  = p;
            rock.transform.rotation  = Quaternion.Euler(0f, 0f, t * 540f);
            trail.transform.position = p;
            yield return null;
        }
        if (rock != null) Destroy(rock.gameObject);
        if (trail != null) Destroy(trail.gameObject);

        LabFx.Run(Flash(target));
        LabFx.Dust(target, 1.5f);
        LabFx.Burst(target + new Vector3(0f, 0.3f, 0f), new Color(1f, 0.6f, 0.25f), 14, 3f, 0.6f);
        LabFx.Ring(target, new Color(1f, 0.75f, 0.4f, 0.9f), 1.8f, 0.6f);
        LabFx.Run(Crater(target));
        Sfx.PlayAt(SfxId.BlockBreak, target);
        if (Vector2.Distance(Lab.LocalPos, target) < 7f) CameraShake.GetOrCreate().Shake(0.25f, 0.5f);
    }

    private static IEnumerator Flash(Vector3 at)
    {
        var sr = LabArt.Make("MeteorFlash", LabArt.Glow(), at, LabArt.OrderAir, new Color(1f, 0.9f, 0.6f, 1f), null);
        for (float t = 0f; t < 0.35f && sr != null; t += Time.deltaTime)
        {
            float k = t / 0.35f;
            sr.transform.localScale = Vector3.one * Mathf.Lerp(1f, 4f, k);
            sr.color = new Color(1f, 0.9f, 0.6f, 1f - k);
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private static IEnumerator Crater(Vector3 at)
    {
        var sr = LabArt.Make("MeteorCrater", LabArt.Disc(), at + new Vector3(0f, -0.05f, 0f), LabArt.OrderGround, new Color(0.25f, 0.14f, 0.1f, 0.4f), null);
        sr.transform.localScale = new Vector3(1.5f, 0.7f, 1f);
        for (float t = 0f; t < 60f && sr != null; t += Time.deltaTime)
        {
            sr.color = new Color(0.25f, 0.14f, 0.1f, 0.4f * (1f - t / 60f));
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    protected override void Cleanup()
    {
        StopAllCoroutines();
        showerEnd = 0f;
        if (fxRoot != null) Destroy(fxRoot.gameObject);
        fxRoot = null;
        if (overlay != null) Destroy(overlay.canvas.gameObject);
        overlay = null;
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "지금 유성우 부르기", CuteUIKit.Hex(0xFFD2A8), () => Request("start"));
    }

    private static string Clock(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
