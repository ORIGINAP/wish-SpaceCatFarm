using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LabYarnBall : LabExperiment
{
    [SerializeField] private float fieldWidth  = 10f;
    [SerializeField] private float fieldHeight = 5.6f;
    [SerializeField] private float goalHalf    = 0.9f;
    [SerializeField] private float goalDepth   = 0.9f;
    [SerializeField] private float friction    = 1.1f;
    [SerializeField] private float kickBase    = 2.4f;
    [SerializeField] private float kickPower   = 1.6f;
    [SerializeField] private float maxSpeed    = 9f;

    private const float ContactRadius = 0.52f;
    private const float SendInterval  = 1f / 15f;
    private static readonly Vector2 FeetOffset = new(0f, -0.28f);
    private static readonly Vector2 BallHalf   = new(0.2f, 0.14f);
    private static readonly Color   PinkTeam   = CuteUIKit.Strawberry;
    private static readonly Color   SkyTeam    = CuteUIKit.Hex(0x6FB6EE);

    public override string Id      => "yarnball";
    public override string Title   => "털실 공 축구";
    public override string Tags    => "대결 · 연습";
    public override Color  Accent  => CuteUIKit.Hex(0x5CC79A);
    public override string Summary =>
        "발전소 근처에 작은 경기장이 생겨요. 털실 공에 걸어가 부딪히면 뻥! (달려와 부딪힐수록 세게). " +
        "핑크 골대에 넣으면 하늘 팀, 하늘 골대에 넣으면 핑크 팀 득점. 혼자서도 슈팅 연습을 할 수 있어요.";
    public override string Status => hasField
        ? $"핑크 {pink} : {sky} 하늘  ·  경기장 {Lab.DirectionName(Lab.LocalPos, center)}"
        : Lab.IsAuthority ? "경기장 자리를 찾지 못했어요 ([경기장 내 근처로])" : "경기장을 받는 중";

    [Serializable] private class FieldMsg { public float x, y; public int pink, sky; }
    [Serializable] private class BallMsg  { public float x, y, vx, vy; }
    [Serializable] private class GoalMsg  { public int side; public string name; public int pink, sky; }
    [Serializable] private class KickMsg  { public float x, y; }

    private bool    hasField;
    private Vector2 center, ballPos, ballVel;
    private int     pink, sky;
    private string  lastKicker = "";
    private float   frozenUntil, outSince = -1f, nextSend;
    private readonly List<Lab.Member> members = new();
    private readonly Dictionary<ulong, Vector2> lastMemberPos = new(), memberVel = new();
    private readonly Dictionary<ulong, float> lastMoveAt = new(), kickReady = new();
    private readonly List<Rect> frames = new();

    private Vector2 netPos, netVel, shownPos;
    private float   netAt;
    private float   spin;

    private Transform fieldRoot, ballRoot;
    private SpriteRenderer ballSr, ballShadow;
    private RectTransform hud;
    private TextMeshProUGUI hudText;

    private static readonly string[] YarnRows =
    {
        "...####...", "..#pPPp#..", ".#pPppPp#.", "#pPpPPpPp#", "#ppPpPpPp#",
        "#pPpPpPpp#", "#pPPpPPpq#", ".#ppPppq#.", "..#pqqq#..", "...####...",
    };
    private static readonly string[] NetRows = { "w.", ".w" };

    protected override void OnSwitched(bool on)
    {
        if (on && Lab.IsAuthority)
        {
            var core = CenterCore.Current != null ? (Vector2)CenterCore.Current.transform.position : Vector2.zero;
            PlaceField(core, 7f, 18f);
        }
    }

    private Vector2 AreaSize => new(fieldWidth + goalDepth * 2f + 1f, fieldHeight + 1f);

    private void PlaceField(Vector2 around, float minR, float maxR)
    {
        if (!Lab.FindClearArea(around, AreaSize, minR, maxR, out var c, Lab.ReservedExcept(this), 200, 0.6f))
        {
            Lab.Notify("털실 공 경기장을 놓을 빈터를 찾지 못했어요. 넓은 곳에서 [경기장 내 근처로]를 눌러 보세요.");
            return;
        }
        SetField(c);
        Broadcast("field", new FieldMsg { x = c.x, y = c.y, pink = pink, sky = sky });
        SendBall();
    }

    private void SetField(Vector2 c)
    {
        center   = c;
        hasField = true;
        ballPos  = shownPos = netPos = c;
        ballVel  = netVel = Vector2.zero;
        Lab.Reserve(this, new Rect(c - AreaSize * 0.5f, AreaSize));
        BuildField();
        LabMarkers.Set(this, c, Accent, "털실 공 경기장", edge: false);

        frames.Clear();
        float t = 0.14f;
        foreach (int side in new[] { -1, 1 })
        {
            float mouth = c.x + side * fieldWidth * 0.5f;
            float back  = mouth + side * goalDepth;
            float xMin  = Mathf.Min(mouth, back), xMax = Mathf.Max(mouth, back);
            frames.Add(Rect.MinMaxRect(xMin, c.y + goalHalf, xMax, c.y + goalHalf + t));
            frames.Add(Rect.MinMaxRect(xMin, c.y - goalHalf - t, xMax, c.y - goalHalf));
            frames.Add(side < 0 ? Rect.MinMaxRect(back - t, c.y - goalHalf - t, back, c.y + goalHalf + t)
                                : Rect.MinMaxRect(back, c.y - goalHalf - t, back + t, c.y + goalHalf + t));
        }
    }

    protected override void AuthorityTick()
    {
        if (!hasField) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        if (dt <= 0f) return;

        Lab.CollectMembers(members);
        foreach (var m in members)
        {
            // 참가자 위치는 초당 12번뿐
            if (!lastMemberPos.TryGetValue(m.id, out var last))
            {
                lastMemberPos[m.id] = m.pos;
                lastMoveAt[m.id]    = Time.time;
                memberVel[m.id]     = Vector2.zero;
                continue;
            }
            if ((m.pos - last).sqrMagnitude > 1e-6f)
            {
                var v = (m.pos - last) / Mathf.Max(Time.time - lastMoveAt[m.id], 1f / 60f);
                memberVel[m.id]     = v.sqrMagnitude > 400f ? Vector2.zero : v;
                lastMemberPos[m.id] = m.pos;
                lastMoveAt[m.id]    = Time.time;
            }
            else if (Time.time - lastMoveAt[m.id] > 0.25f) memberVel[m.id] = Vector2.zero;
        }

        if (frozenUntil > 0f)
        {
            if (Time.time < frozenUntil) return;
            frozenUntil = 0f;
            ResetBall();
            return;
        }

        int steps = Mathf.CeilToInt(dt / 0.02f);
        float h = dt / steps;
        for (int s = 0; s < steps; s++)
        {
            foreach (var m in members) Contact(m);
            Integrate(h);
            if (CheckGoal()) return;
        }

        // 너무 멀리 나가면 가운데로
        if (Vector2.Distance(ballPos, center) > 16f)
        {
            if (outSince < 0f) outSince = Time.time;
            else if (Time.time - outSince > 4f)
            {
                ResetBall();
                Lab.Notify("털실 공이 경기장으로 돌아왔어요.");
            }
        }
        else outSince = -1f;

        if (Time.time >= nextSend) SendBall();
    }

    private void Contact(Lab.Member m)
    {
        var feet = m.pos + FeetOffset;
        var d    = ballPos - feet;
        float dist = d.magnitude;
        if (dist >= ContactRadius) return;
        var n = dist > 0.001f ? d / dist : UnityEngine.Random.insideUnitCircle.normalized;
        ballPos = feet + n * ContactRadius;
        var mv = memberVel.TryGetValue(m.id, out var v) ? v : Vector2.zero;
        float approach = Vector2.Dot(mv, n);

        if (!kickReady.TryGetValue(m.id, out float ready) || Time.time >= ready)
        {
            float speed = Mathf.Clamp(approach * kickPower + kickBase, kickBase, maxSpeed);
            ballVel = n * speed + mv * 0.2f;
            kickReady[m.id] = Time.time + 0.15f;
            lastKicker = m.name;
            KickFx(ballPos);
            Broadcast("kick", new KickMsg { x = ballPos.x, y = ballPos.y }, reliable: false);
            SendBall();
        }
        else if (Vector2.Dot(ballVel, n) < approach) ballVel += n * (approach - Vector2.Dot(ballVel, n));
    }

    private void Integrate(float h)
    {
        ballVel *= Mathf.Exp(-friction * h);
        if (ballVel.sqrMagnitude < 0.0016f) { ballVel = Vector2.zero; return; }

        var next = ballPos + ballVel * h;
        if (Blocked(new Vector2(next.x, ballPos.y))) { ballVel.x = -ballVel.x * 0.6f; next.x = ballPos.x; }
        if (Blocked(next))                            { ballVel.y = -ballVel.y * 0.6f; next.y = ballPos.y; }

        var gm = GridManager.Instance;
        if (gm != null)
        {
            Vector2 min = gm.WorldMin + BallHalf, max = gm.WorldMax - BallHalf;
            if (next.x < min.x || next.x > max.x) { ballVel.x = -ballVel.x * 0.6f; next.x = Mathf.Clamp(next.x, min.x, max.x); }
            if (next.y < min.y || next.y > max.y) { ballVel.y = -ballVel.y * 0.6f; next.y = Mathf.Clamp(next.y, min.y, max.y); }
        }
        ballPos = next;
    }

    private bool Blocked(Vector2 p)
    {
        if (TileCollision.Overlaps(p, BallHalf)) return true;
        var box = new Rect(p - BallHalf, BallHalf * 2f);
        foreach (var r in frames) if (box.Overlaps(r)) return true;
        return false;
    }

    private bool CheckGoal()
    {
        if (Mathf.Abs(ballPos.y - center.y) > goalHalf) return false;
        float line = fieldWidth * 0.5f + 0.15f;
        int side = ballPos.x < center.x - line ? -1 : ballPos.x > center.x + line ? 1 : 0;
        if (side == 0) return false;
        if (side < 0) sky++; else pink++;
        frozenUntil = Time.time + 1.8f;
        ballVel = Vector2.zero;
        var msg = new GoalMsg { side = side, name = lastKicker, pink = pink, sky = sky };
        ShowGoal(msg);
        Broadcast("goal", msg);
        SendBall();
        return true;
    }

    private void ResetBall()
    {
        ballPos = center;
        ballVel = Vector2.zero;
        outSince = -1f;
        SendBall();
    }

    private void SendBall()
    {
        nextSend = Time.time + SendInterval;
        Broadcast("ball", new BallMsg { x = ballPos.x, y = ballPos.y, vx = ballVel.x, vy = ballVel.y }, reliable: false);
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        switch (kind)
        {
            case "reset":
                if (hasField) ResetBall();
                break;
            case "score0":
                pink = sky = 0;
                if (hasField) Broadcast("field", new FieldMsg { x = center.x, y = center.y, pink = pink, sky = sky });
                break;
            case "move":
            {
                Lab.CollectMembers(members);
                var around = Lab.LocalPos;
                foreach (var m in members) if (m.id == from) around = m.pos;
                PlaceField(around, 3f, 14f);
                break;
            }
        }
    }

    protected override void SendState(ulong peer)
    {
        if (!hasField) return;
        SendTo(peer, "field", new FieldMsg { x = center.x, y = center.y, pink = pink, sky = sky });
        SendTo(peer, "ball", new BallMsg { x = ballPos.x, y = ballPos.y, vx = ballVel.x, vy = ballVel.y });
    }

    protected override void OnEvent(string kind, string json)
    {
        switch (kind)
        {
            case "field":
            {
                var m = Read<FieldMsg>(json);
                if (m == null) return;
                pink = m.pink;
                sky  = m.sky;
                var c = new Vector2(m.x, m.y);
                if (!hasField || Vector2.Distance(c, center) > 0.01f) SetField(c);
                break;
            }
            case "ball":
            {
                var m = Read<BallMsg>(json);
                if (m == null) return;
                netPos = new Vector2(m.x, m.y);
                netVel = new Vector2(m.vx, m.vy);
                netAt  = Time.time;
                break;
            }
            case "kick":
            {
                var m = Read<KickMsg>(json);
                if (m != null) KickFx(new Vector2(m.x, m.y));
                break;
            }
            case "goal":
            {
                var m = Read<GoalMsg>(json);
                if (m == null) return;
                pink = m.pink;
                sky  = m.sky;
                ShowGoal(m);
                break;
            }
        }
    }

    protected override void LocalTick()
    {
        if (!hasField || ballRoot == null) return;
        Vector2 vel;
        if (Lab.IsAuthority)
        {
            shownPos = ballPos;
            vel = ballVel;
        }
        else
        {
            // 받은 속도로 앞질러 보간
            float age = Mathf.Min(Time.time - netAt, 0.3f);
            var predicted = netPos + netVel * ((1f - Mathf.Exp(-friction * age)) / friction);
            shownPos = Vector2.Distance(shownPos, predicted) > 2f ? predicted : Vector2.Lerp(shownPos, predicted, 1f - Mathf.Exp(-15f * Time.deltaTime));
            vel = netVel * Mathf.Exp(-friction * age);
        }
        spin -= vel.x * Time.deltaTime * 220f;
        ballRoot.position = shownPos;
        ballSr.transform.localRotation = Quaternion.Euler(0f, 0f, spin);
        ballShadow.transform.position = new Vector3(shownPos.x, shownPos.y - 0.16f, 0f);

        bool near = Vector2.Distance(Lab.LocalPos, center) < 14f;
        if (near && hud == null) BuildHud();
        if (hud != null)
        {
            hud.gameObject.SetActive(near);
            if (near) hudText.text = $"<size=80%>털실 공 축구</size>\n<color=#F7879F>핑크 {pink}</color>  :  <color=#4F95D8>{sky} 하늘</color>";
        }
    }

    private void KickFx(Vector2 at)
    {
        Sfx.PlayAt(SfxId.Place, at, 0.5f);
        LabFx.Burst(at, new Color(1f, 0.85f, 0.9f), 6, 1.4f, 0.3f);
    }

    private void ShowGoal(GoalMsg m)
    {
        var goalAt   = new Vector3(center.x + m.side * (fieldWidth * 0.5f + goalDepth * 0.5f), center.y, 0f);
        var scorer   = m.side < 0 ? SkyTeam : PinkTeam;
        string team  = m.side < 0 ? "하늘" : "핑크";
        LabFx.Burst(goalAt, scorer, 22, 3.4f, 0.8f);
        LabFx.Ring(goalAt, scorer, 2f, 0.7f);
        LabFx.FloatText(goalAt + new Vector3(0f, 1f, 0f), "골!", scorer, 5f);
        Sfx.Play(SfxId.QuestComplete);
        string who = string.IsNullOrEmpty(m.name) ? "" : $"{m.name}의 슛 — ";
        Lab.Notify($"골! {who}{team} 팀 득점 (핑크 {m.pink} : {m.sky} 하늘)");
    }

    private void BuildField()
    {
        if (fieldRoot != null) Destroy(fieldRoot.gameObject);
        if (ballRoot != null) Destroy(ballRoot.gameObject);
        fieldRoot = new GameObject("YarnBallField").transform;
        fieldRoot.position = center;
        var line = new Color(1f, 1f, 1f, 0.55f);

        var turf = Bar("Turf", center, new Vector2(fieldWidth, fieldHeight), new Color(0.48f, 0.78f, 0.5f, 0.2f), LabArt.OrderGround, 6);
        turf.transform.SetParent(fieldRoot, true);
        float w = fieldWidth * 0.5f, hh = fieldHeight * 0.5f, t = 0.07f;
        Bar("Top",    center + new Vector2(0f, hh),  new Vector2(fieldWidth + t, t), line, LabArt.OrderGround + 1, 2).transform.SetParent(fieldRoot, true);
        Bar("Bottom", center + new Vector2(0f, -hh), new Vector2(fieldWidth + t, t), line, LabArt.OrderGround + 1, 2).transform.SetParent(fieldRoot, true);
        Bar("Left",   center + new Vector2(-w, 0f),  new Vector2(t, fieldHeight + t), line, LabArt.OrderGround + 1, 2).transform.SetParent(fieldRoot, true);
        Bar("Right",  center + new Vector2(w, 0f),   new Vector2(t, fieldHeight + t), line, LabArt.OrderGround + 1, 2).transform.SetParent(fieldRoot, true);
        Bar("Mid",    center,                        new Vector2(t, fieldHeight), line, LabArt.OrderGround + 1, 2).transform.SetParent(fieldRoot, true);
        var circle = LabArt.Make("Circle", LabArt.Ring(), center, LabArt.OrderGround + 1, line, fieldRoot);
        circle.transform.localScale = Vector3.one * 2.2f;

        foreach (int side in new[] { -1, 1 })
        {
            var color = side < 0 ? PinkTeam : SkyTeam;
            float mouth = center.x + side * w;
            var netCenter = new Vector2(mouth + side * goalDepth * 0.5f, center.y);
            var net = LabArt.Make("Net", LabArt.Pixel("goalNet", NetRows, "w#FFFFFF", new Vector2(0.5f, 0.5f)), netCenter,
                                  LabArt.OrderGround + 1, new Color(1f, 1f, 1f, 0.45f), fieldRoot);
            net.drawMode = SpriteDrawMode.Tiled;
            net.size     = new Vector2(goalDepth, goalHalf * 2f);
            Bar("BarTop", new Vector2(netCenter.x, center.y + goalHalf + 0.07f), new Vector2(goalDepth, 0.14f), color, LabArt.OrderGround + 2, 3).transform.SetParent(fieldRoot, true);
            Bar("BarBottom", new Vector2(netCenter.x, center.y - goalHalf - 0.07f), new Vector2(goalDepth, 0.14f), color, LabArt.OrderGround + 2, 3).transform.SetParent(fieldRoot, true);
            Bar("BarBack", new Vector2(mouth + side * (goalDepth + 0.07f), center.y), new Vector2(0.14f, goalHalf * 2f + 0.28f), color, LabArt.OrderGround + 2, 3).transform.SetParent(fieldRoot, true);
            var label = LabArt.Label(fieldRoot, side < 0 ? "핑크 골대" : "하늘 골대", 2f, color, Vector3.zero, 150);
            label.transform.position = new Vector3(netCenter.x, center.y + goalHalf + 0.45f, 0f);
        }

        ballRoot = new GameObject("YarnBall").transform;
        ballRoot.position = center;
        ballSr = LabArt.Make("Yarn", LabArt.Pixel("yarn", YarnRows, "##8A3A52 p#F7879F P#FFC2D1 q#D9667F", new Vector2(0.5f, 0.35f)),
                             center, LabArt.OrderObject, Color.white, ballRoot);
        ballSr.transform.localScale = Vector3.one * 0.85f;
        ballShadow = LabArt.Make("Shadow", LabArt.Disc(), center, LabArt.OrderGround + 1, new Color(0.2f, 0.12f, 0.1f, 0.25f), ballRoot);
        ballShadow.transform.localScale = new Vector3(0.5f, 0.2f, 1f);
    }

    private static SpriteRenderer Bar(string name, Vector2 pos, Vector2 size, Color color, int order, int radius)
    {
        var sr = LabArt.Make(name, CuteUIKit.Rounded(radius), pos, order, color, null);
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size     = size;
        return sr;
    }

    private void BuildHud()
    {
        hud = LabPanelUI.HudCard("YarnBallHud", 210f);
        if (hud == null) return;
        hudText = CuteUIKit.Text(hud, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold);
        hudText.lineSpacing = -8f;
    }

    protected override void Cleanup()
    {
        hasField = false;
        frozenUntil = 0f;
        Lab.Unreserve(this);
        LabMarkers.Remove(this);
        if (fieldRoot != null) Destroy(fieldRoot.gameObject);
        if (ballRoot != null) Destroy(ballRoot.gameObject);
        if (hud != null) Destroy(hud.gameObject);
        fieldRoot = ballRoot = null;
        hud = null;
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "공 가운데로", CuteUIKit.ButtonMint, () => Request("reset"));
        LabPanelUI.Btn(row, "점수 0:0", CuteUIKit.ButtonLatte, () => Request("score0"));
        LabPanelUI.Btn(row, "경기장 내 근처로", CuteUIKit.ButtonSky, () => Request("move"));
    }
}
