using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LabStarRush : LabExperiment
{
    [SerializeField] private float countdown    = 3f;
    [SerializeField] private float roundSeconds = 40f;
    [SerializeField] private float radius       = 8f;
    [SerializeField] private int   maxShards    = 12;
    [SerializeField] private int   coinsPerStar = 15;

    private const string BestKey = "Lab.StarRush.Best";
    private static readonly Color Gold = CuteUIKit.Hex(0xF2B731);

    public override string Id      => "starrush";
    public override string Title   => "별조각 사냥";
    public override string Tags    => "대결 · 기록";
    public override Color  Accent  => Gold;
    public override string Summary =>
        "[시작]을 누르면 3초 뒤 내 둘레에 별조각이 쏟아져요. 40초 동안 밟아서 많이 모으기! 금별은 3점. " +
        "친구와 하면 순위 대결, 혼자면 최고 기록에 도전해요. 모은 별만큼 코인도 받아요.";
    public override string Status
    {
        get
        {
            if (phase == 1) return "곧 시작해요!";
            if (phase == 2) return $"진행 중 {Mathf.CeilToInt(Mathf.Max(0f, phaseEnd - Time.time))}초 남음  ·  내 점수 {MyScore}";
            int best = PlayerPrefs.GetInt(BestKey, 0);
            return (string.IsNullOrEmpty(lastResult) ? "" : lastResult + "  ·  ") + (best > 0 ? $"내 최고 기록 {best}개" : "아직 기록 없음");
        }
    }

    [Serializable] private class BeginMsg { public float x, y, r, wait, seconds; }
    [Serializable] private class ShardMsg { public int id; public float x, y; public int v; }
    [Serializable] private class GotMsg   { public int id; public ulong who; public string name; public int score, v; }
    [Serializable] private class EndMsg   { public List<string> names = new(); public List<int> scores = new(); public List<ulong> ids = new(); public long coins; }
    [Serializable] private class StateMsg
    {
        public int phase; public float x, y, r, left;
        public List<ShardMsg> shards = new();
        public List<ulong> ids = new(); public List<string> names = new(); public List<int> scores = new();
    }

    private sealed class Shard
    {
        public int id, value;
        public Vector2 pos;
        public Transform root;
        public SpriteRenderer sprite;
        public float born;
    }

    private int     phase;
    private float   phaseEnd, nextSpawn;
    private Vector2 arena;
    private int     serial;
    private string  lastResult = "";
    private readonly Dictionary<int, Shard> shards = new();
    private readonly Dictionary<ulong, (string name, int score)> scores = new();
    private readonly List<Lab.Member> members = new();
    private readonly List<int> picked = new();

    private Transform arenaRoot;
    private RectTransform hud;
    private TextMeshProUGUI hudTitle, hudBig, hudBoard;
    private float hudHideAt;

    private static readonly string[] StarRows =
    {
        "....#....", "...#y#...", "...#y#...", "####yh###", "#yyyyhhy#", ".#yyyyy#.", "..#yyy#..", ".#yy#yy#.", ".##.#.##.",
    };
    private static readonly string[] GoldRows =
    {
        ".....#.....", "....#y#....", "....#y#....", "...#yhy#...", "#####yh####", "#yyyyyhhyy#",
        ".#yyyyhyy#.", "..#yyyyy#..", "..#yy#yy#..", ".#yy#.#yy#.", ".###...###.",
    };

    private int MyScore => scores.TryGetValue(Lab.LocalId, out var s) ? s.score : 0;

    protected override void OnRequest(ulong from, string kind, string json)
    {
        if (kind == "start" && phase == 0)
        {
            Lab.CollectMembers(members);
            var at = Lab.LocalPos;
            foreach (var m in members) if (m.id == from) at = m.pos;
            var msg = new BeginMsg { x = at.x, y = at.y, r = radius, wait = countdown, seconds = roundSeconds };
            Begin(msg);
            Broadcast("begin", msg);
        }
        else if (kind == "stop" && phase != 0) End();
    }

    protected override void AuthorityTick()
    {
        if (phase == 1 && Time.time >= phaseEnd)
        {
            phase = 2;
            phaseEnd = Time.time + roundSeconds;
            for (int i = 0; i < 8; i++) Spawn();
            nextSpawn = Time.time + 0.55f;
        }
        if (phase != 2) return;

        if (Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + 0.55f;
            if (shards.Count < maxShards) Spawn();
        }

        Lab.CollectMembers(members);
        picked.Clear();
        foreach (var s in shards.Values)
            foreach (var m in members)
            {
                if (Vector2.Distance(m.Feet, s.pos) > 0.6f) continue;
                int total = (scores.TryGetValue(m.id, out var old) ? old.score : 0) + s.value;
                scores[m.id] = (m.name, total);
                var got = new GotMsg { id = s.id, who = m.id, name = m.name, score = total, v = s.value };
                picked.Add(s.id);
                ShowGot(got);
                Broadcast("got", got);
                break;
            }
        foreach (int id in picked) RemoveShard(id);

        if (Time.time >= phaseEnd) End();
    }

    private void Spawn()
    {
        for (int t = 0; t < 12; t++)
        {
            var p = arena + UnityEngine.Random.insideUnitCircle * radius * 0.95f;
            if (!Lab.IsSpotClear(p, 0.3f)) continue;
            var msg = new ShardMsg { id = ++serial, x = p.x, y = p.y, v = UnityEngine.Random.value < 0.12f ? 3 : 1 };
            AddShard(msg);
            Broadcast("shard", msg);
            return;
        }
    }

    private void End()
    {
        var msg = new EndMsg();
        var order = new List<KeyValuePair<ulong, (string name, int score)>>(scores);
        order.Sort((a, b) => b.Value.score.CompareTo(a.Value.score));
        int total = 0;
        foreach (var pair in order)
        {
            msg.ids.Add(pair.Key);
            msg.names.Add(pair.Value.name);
            msg.scores.Add(pair.Value.score);
            total += pair.Value.score;
        }
        msg.coins = (long)total * coinsPerStar;
        if (msg.coins > 0) CurrencyManager.Instance?.Add(msg.coins);
        ShowEnd(msg);
        Broadcast("end", msg);
    }

    protected override void SendState(ulong peer)
    {
        if (phase == 0) return;
        var s = new StateMsg { phase = phase, x = arena.x, y = arena.y, r = radius, left = phaseEnd - Time.time };
        foreach (var sh in shards.Values) s.shards.Add(new ShardMsg { id = sh.id, x = sh.pos.x, y = sh.pos.y, v = sh.value });
        foreach (var pair in scores) { s.ids.Add(pair.Key); s.names.Add(pair.Value.name); s.scores.Add(pair.Value.score); }
        SendTo(peer, "state", s);
    }

    protected override void OnEvent(string kind, string json)
    {
        switch (kind)
        {
            case "begin": { var m = Read<BeginMsg>(json); if (m != null) Begin(m); break; }
            case "shard":
            {
                var m = Read<ShardMsg>(json);
                if (m == null) return;
                if (phase == 1) { phase = 2; phaseEnd = Time.time + roundSeconds; }
                AddShard(m);
                break;
            }
            case "got":
            {
                var m = Read<GotMsg>(json);
                if (m == null) return;
                scores[m.who] = (m.name, m.score);
                ShowGot(m);
                RemoveShard(m.id);
                break;
            }
            case "end": { var m = Read<EndMsg>(json); if (m != null) ShowEnd(m); break; }
            case "state":
            {
                var m = Read<StateMsg>(json);
                if (m == null) return;
                Begin(new BeginMsg { x = m.x, y = m.y, r = m.r, wait = 0f, seconds = m.left });
                phase = m.phase;
                phaseEnd = Time.time + m.left;
                foreach (var sh in m.shards) AddShard(sh);
                for (int i = 0; i < m.ids.Count; i++) scores[m.ids[i]] = (m.names[i], m.scores[i]);
                break;
            }
        }
    }

    private void Begin(BeginMsg m)
    {
        ClearRound();
        phase    = m.wait > 0f ? 1 : 2;
        arena    = new Vector2(m.x, m.y);
        radius   = m.r;
        phaseEnd = Time.time + (m.wait > 0f ? m.wait : m.seconds);
        roundSeconds = m.seconds;

        arenaRoot = new GameObject("StarRushArena").transform;
        arenaRoot.position = arena;
        var fill = LabArt.Make("Fill", LabArt.Disc(), arena, LabArt.OrderGround, new Color(1f, 0.85f, 0.35f, 0.07f), arenaRoot);
        fill.transform.localScale = Vector3.one * radius * 2f;
        var ring = LabArt.Make("Ring", LabArt.ThinRing(), arena, LabArt.OrderGround + 1, new Color(1f, 0.8f, 0.3f, 0.75f), arenaRoot);
        ring.transform.localScale = Vector3.one * radius * 2f;

        EnsureHud();
        hudHideAt = 0f;
        if (m.wait > 0f)
        {
            Lab.Notify($"별조각 사냥이 곧 시작해요! ({Lab.DirectionName(Lab.LocalPos, arena)})");
            Sfx.Play(SfxId.UiOpen);
        }
    }

    private void AddShard(ShardMsg m)
    {
        if (shards.ContainsKey(m.id)) return;
        var s = new Shard { id = m.id, value = m.v, pos = new Vector2(m.x, m.y), born = Time.time };
        s.root = new GameObject($"StarShard_{m.id}").transform;
        s.root.position = s.pos;
        var glow = LabArt.Make("Glow", LabArt.Glow(), s.pos, LabArt.OrderGround + 1, new Color(1f, 0.85f, 0.4f, 0.55f), s.root);
        glow.transform.localScale = Vector3.one * (m.v > 1 ? 1.3f : 0.9f);
        s.sprite = LabArt.Make("Star", m.v > 1 ? LabArt.Pixel("starGold", GoldRows, "##A0520A y#FFB03B h#FFF0B0", new Vector2(0.5f, 0.2f))
                                               : LabArt.Pixel("star", StarRows, "##B07A10 y#FFD66B h#FFF6C8", new Vector2(0.5f, 0.2f)),
                               s.pos, LabArt.OrderObject, Color.white, s.root);
        shards[m.id] = s;
    }

    private void RemoveShard(int id)
    {
        if (!shards.TryGetValue(id, out var s)) return;
        shards.Remove(id);
        if (s.root != null) Destroy(s.root.gameObject);
    }

    private void ShowGot(GotMsg m)
    {
        if (!shards.TryGetValue(m.id, out var s)) return;
        var at = (Vector3)s.pos + new Vector3(0f, 0.3f, 0f);
        LabFx.Burst(at, m.v > 1 ? new Color(1f, 0.65f, 0.2f) : Gold, m.v > 1 ? 12 : 7, 2f, 0.4f);
        LabFx.FloatText(at, $"+{m.v}", m.who == Lab.LocalId ? Gold : CuteUIKit.CocoaSoft, m.v > 1 ? 3.2f : 2.4f);
        Sfx.PlayAt(SfxId.Pickup, s.pos, m.who == Lab.LocalId ? 1f : 0.5f);
    }

    private void ShowEnd(EndMsg m)
    {
        phase = 0;
        foreach (var id in new List<int>(shards.Keys)) RemoveShard(id);
        if (arenaRoot != null) Destroy(arenaRoot.gameObject);
        arenaRoot = null;

        int mine = 0;
        for (int i = 0; i < m.ids.Count; i++) if (m.ids[i] == Lab.LocalId) mine = m.scores[i];
        int best = PlayerPrefs.GetInt(BestKey, 0);
        bool record = mine > best;
        if (record) { PlayerPrefs.SetInt(BestKey, mine); PlayerPrefs.Save(); }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < m.names.Count && i < 4; i++)
            sb.Append(i == 0 ? "1등 " : $"\n{i + 1}등 ").Append(m.names[i]).Append(' ').Append(m.scores[i]).Append('개');
        if (m.names.Count == 0) sb.Append("아무도 못 주웠어요");

        bool multi = m.names.Count > 1;
        lastResult = m.names.Count == 0 ? "지난 판: 0개" : multi ? $"지난 판 1등: {m.names[0]} {m.scores[0]}개" : $"지난 판: {mine}개";
        string headline = record ? $"새 기록! {mine}개" : multi ? $"{m.names[0]} 우승!" : $"{mine}개 모았어요";
        EnsureHud();
        hudTitle.text = "별조각 사냥 끝!";
        hudBig.text   = headline;
        hudBoard.text = sb.ToString() + (m.coins > 0 ? $"\n<color=#A8741A>코인 +{CurrencyManager.Format(m.coins)}</color>" : "");
        hudHideAt = Time.time + 6f;
        Lab.Notify($"별조각 사냥 끝! {headline}" + (m.coins > 0 ? $"  ·  코인 +{CurrencyManager.Format(m.coins)}" : ""));
        Sfx.Play(record || (multi && m.ids.Count > 0 && m.ids[0] == Lab.LocalId) ? SfxId.CoreLevelUp : SfxId.QuestComplete);
        if (PlayerTileController.Instance != null)
            LabFx.Burst(PlayerTileController.Instance.transform.position + new Vector3(0f, 0.8f, 0f), Gold, 16, 2.6f, 0.7f);
    }

    protected override void LocalTick()
    {
        foreach (var s in shards.Values)
        {
            if (s.sprite == null) continue;
            float age = Time.time - s.born;
            float pop = age < 0.25f ? CuteUIKit.EaseOutBack(age / 0.25f, 2.6f) : 1f;
            float tw  = 1f + 0.08f * Mathf.Sin(Time.time * 6f + s.id);
            s.sprite.transform.localScale    = Vector3.one * pop * tw * (s.value > 1 ? 1.15f : 1f);
            s.sprite.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2f + s.id) * 10f);
        }
        UpdateHud();
    }

    private void UpdateHud()
    {
        if (hud == null) return;
        if (phase == 0)
        {
            if (hudHideAt > 0f && Time.time > hudHideAt) { hud.gameObject.SetActive(false); hudHideAt = 0f; }
            return;
        }
        hud.gameObject.SetActive(true);
        if (phase == 1)
        {
            hudTitle.text = "별조각 사냥";
            hudBig.text   = $"준비… {Mathf.CeilToInt(Mathf.Max(0f, phaseEnd - Time.time))}";
            hudBoard.text = "별을 밟아서 모아요! 금별은 3점";
            return;
        }
        hudTitle.text = "별조각 사냥";
        hudBig.text   = $"{Mathf.CeilToInt(Mathf.Max(0f, phaseEnd - Time.time))}초";
        var order = new List<KeyValuePair<ulong, (string name, int score)>>(scores);
        order.Sort((a, b) => b.Value.score.CompareTo(a.Value.score));
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < order.Count && i < 4; i++)
        {
            if (i > 0) sb.Append('\n');
            bool me = order[i].Key == Lab.LocalId;
            sb.Append(me ? "<color=#F7879F>" : "").Append($"{i + 1}. {order[i].Value.name}  {order[i].Value.score}").Append(me ? "</color>" : "");
        }
        hudBoard.text = sb.Length > 0 ? sb.ToString() : $"내 점수 {MyScore}";
    }

    private void EnsureHud()
    {
        if (hud != null) return;
        hud = LabPanelUI.HudCard("StarRushHud", 230f);
        if (hud == null) return;
        hudTitle = CuteUIKit.Text(hud, "별조각 사냥", 12f, CuteUIKit.AmberText, FontStyles.Bold);
        hudBig   = CuteUIKit.Text(hud, "", 22f, CuteUIKit.Cocoa, FontStyles.Bold);
        hudBoard = CuteUIKit.Text(hud, "", 12f, CuteUIKit.Cocoa);
        hudBoard.lineSpacing = 4f;
        hudBoard.richText = true;
    }

    private void ClearRound()
    {
        foreach (var id in new List<int>(shards.Keys)) RemoveShard(id);
        scores.Clear();
        if (arenaRoot != null) Destroy(arenaRoot.gameObject);
        arenaRoot = null;
    }

    protected override void Cleanup()
    {
        ClearRound();
        phase = 0;
        if (hud != null) Destroy(hud.gameObject);
        hud = null;
    }

    public override void BuildControls(Transform row)
    {
        LabPanelUI.Btn(row, "시작", CuteUIKit.ButtonButter, () => Request("start"));
        LabPanelUI.Btn(row, "그만", CuteUIKit.ButtonLatte, () => Request("stop"));
    }
}
