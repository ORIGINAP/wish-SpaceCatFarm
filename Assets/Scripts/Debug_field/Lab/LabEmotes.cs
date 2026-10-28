using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LabEmotes : LabExperiment
{
    [SerializeField] private float bubbleSeconds = 2.4f;
    [SerializeField] private float fiveRange     = 2.2f;
    [SerializeField] private float fiveWindow    = 2.5f;
    [SerializeField] private int   fiveCoins     = 100;
    [SerializeField] private int   soloFiveCoins = 20;

    public override string Id      => "emote";
    public override string Title   => "감정표현 · 하이파이브";
    public override string Tags    => "싱글 · 멀티";
    public override Color  Accent  => CuteUIKit.Strawberry;
    public override string Summary =>
        "[V]로 감정표현 바퀴를 열어 골라요 (좋아 · 신나 · ! · ? · ㅋㅋ · 하이파이브). 친구와 가까이서 동시에 하이파이브하면 \"짝!\" + 코인. " +
        "혼자라면 코코나 아기 고양이 옆에서 해 보세요.";
    public override string Status  => fives > 0 ? $"이번 실험실에서 하이파이브 {fives}번" : "아직 하이파이브 전";

    private struct Emote { public string glyph, name; public Color color; }

    private static readonly Emote[] Emotes =
    {
        new() { glyph = "^^",  name = "좋아",       color = CuteUIKit.Strawberry },
        new() { glyph = "~~",  name = "신나",       color = CuteUIKit.Hex(0x5AA6E6) },
        new() { glyph = "!",  name = "앗",         color = CuteUIKit.Hex(0xF0A12E) },
        new() { glyph = "?",  name = "갸웃",       color = CuteUIKit.Hex(0x9C82E0) },
        new() { glyph = "ㅋㅋ", name = "웃음",      color = CuteUIKit.Hex(0x45B886) },
        new() { glyph = "",   name = "하이파이브", color = CuteUIKit.Hex(0xF2B731) },
    };
    private const int HighFive = 5;

    [Serializable] private class EmoteMsg { public int i; public ulong who; }
    [Serializable] private class FiveMsg  { public ulong a, b; }

    private sealed class Bubble
    {
        public Transform target, root;
        public TextMeshPro glyph;
        public SpriteRenderer paw;
        public float born, headroom;
    }
    private readonly Dictionary<ulong, Bubble> bubbles = new();

    private readonly Dictionary<ulong, float> fiveAt = new();
    private readonly Dictionary<string, float> pairRewardAt = new();
    private readonly Dictionary<ulong, float> soloRewardAt = new();
    private readonly List<Lab.Member> members = new();
    private readonly List<(ulong id, Transform root, int color)> avatars = new();
    private int fives;

    private RectTransform wheelRoot;
    private Canvas wheelCanvas;
    private bool wheelOpen;

    protected override void LocalTick()
    {
        if (wheelOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) SetWheel(false);
        else if (!HotkeysBlocked && GameInput.GetKeyDown(KeyCode.V)) SetWheel(!wheelOpen);
        AnimateBubbles();
    }

    private void SendEmote(int i)
    {
        SetWheel(false);
        if (!IsOn) return;
        Show(Lab.LocalId, i);
        Request("emote", new EmoteMsg { i = i });
        if (i == HighFive) TrySoloFive();
    }

    protected override void OnRequest(ulong from, string kind, string json)
    {
        if (kind == "emote")
        {
            var msg = Read<EmoteMsg>(json);
            if (msg == null || msg.i < 0 || msg.i >= Emotes.Length) return;
            if (from != Lab.LocalId) Show(from, msg.i);
            Broadcast("emote", new EmoteMsg { i = msg.i, who = from }, except: from);
            if (msg.i == HighFive) CheckFive(from);
        }
        else if (kind == "solo" && soloFiveCoins > 0)
        {
            if (soloRewardAt.TryGetValue(from, out float t) && Time.time - t < 20f) return;
            soloRewardAt[from] = Time.time;
            CurrencyManager.Instance?.Add(soloFiveCoins);
        }
    }

    protected override void OnEvent(string kind, string json)
    {
        if (kind == "emote")
        {
            var msg = Read<EmoteMsg>(json);
            if (msg != null && msg.i >= 0 && msg.i < Emotes.Length) Show(msg.who, msg.i);
        }
        else if (kind == "five")
        {
            var msg = Read<FiveMsg>(json);
            if (msg != null) ShowFive(msg.a, msg.b);
        }
    }

    // 하이파이브 판정은 호스트에서
    private void CheckFive(ulong from)
    {
        Lab.CollectMembers(members);
        Vector2 me = Vector2.zero;
        bool found = false;
        foreach (var m in members) if (m.id == from) { me = m.pos; found = true; }
        if (!found) return;

        foreach (var m in members)
        {
            if (m.id == from || !fiveAt.TryGetValue(m.id, out float t) || Time.time - t > fiveWindow) continue;
            if (Vector2.Distance(me, m.pos) > fiveRange) continue;
            fiveAt.Remove(m.id);
            fiveAt.Remove(from);
            ShowFive(from, m.id);
            Broadcast("five", new FiveMsg { a = from, b = m.id });
            string pair = from < m.id ? $"{from}-{m.id}" : $"{m.id}-{from}";
            if (fiveCoins > 0 && (!pairRewardAt.TryGetValue(pair, out float last) || Time.time - last > 15f))
            {
                pairRewardAt[pair] = Time.time;
                CurrencyManager.Instance?.Add(fiveCoins);
            }
            return;
        }
        fiveAt[from] = Time.time;
    }

    private void TrySoloFive()
    {
        var me = PlayerTileController.Instance;
        if (me == null) return;
        avatars.Clear();
        NetBootstrap.Avatars.CollectRoots(avatars);
        foreach (var o in avatars)
            if (Vector2.Distance(o.root.position, me.transform.position) <= fiveRange) return;
        Vector3 at;
        string partner;
        var coco = AINpc.Instance;
        if (coco != null && Vector2.Distance(coco.transform.position, me.transform.position) <= fiveRange)
        {
            at = coco.transform.position;
            partner = "코코";
            coco.Say("짝! 하이파이브!", 2.5f);
        }
        else if (Lab.Find<LabKittens>() is { IsOn: true } kittens && kittens.HighFive(Lab.LocalId, out at))
            partner = "아기 고양이";
        else return;

        Vector3 mid = (me.transform.position + at) * 0.5f + new Vector3(0f, 0.6f, 0f);
        LabFx.Burst(mid, Emotes[HighFive].color, 14, 2.4f, 0.6f);
        LabFx.FloatText(mid, $"짝! ({partner})", Emotes[HighFive].color);
        Sfx.Play(SfxId.UiConfirm);
        fives++;
        Request("solo");
    }

    private void ShowFive(ulong a, ulong b)
    {
        var ta = Lab.MemberTransform(a);
        var tb = Lab.MemberTransform(b);
        if (ta == null || tb == null) return;
        Vector3 mid = (ta.position + tb.position) * 0.5f + new Vector3(0f, 0.6f, 0f);
        LabFx.Burst(mid, Emotes[HighFive].color, 18, 3f, 0.7f);
        LabFx.Ring(mid - new Vector3(0f, 0.6f, 0f), Emotes[HighFive].color, 1.6f, 0.6f);
        LabFx.FloatText(mid, "짝!", Emotes[HighFive].color, 4f);
        if (fiveCoins > 0) LabFx.FloatText(mid + new Vector3(0f, -0.5f, 0f), $"+{fiveCoins} 코인", CuteUIKit.AmberText, 2.4f);
        Sfx.Play(SfxId.QuestComplete, 0.7f);
        fives++;
    }

    private void Show(ulong who, int i)
    {
        var target = Lab.MemberTransform(who);
        if (target == null) return;
        if (!bubbles.TryGetValue(who, out var b) || b.root == null) bubbles[who] = b = BuildBubble();
        b.target   = target;
        b.born     = Time.time;
        b.headroom = ChatBubble.SpriteTop(target) + 0.12f;
        var e = Emotes[i];
        b.glyph.gameObject.SetActive(i != HighFive);
        b.paw.gameObject.SetActive(i == HighFive);
        b.glyph.text  = e.glyph;
        b.glyph.color = e.color;
        b.glyph.fontSize = e.glyph.Length > 1 ? 3.4f : 5f;
        b.paw.color   = e.color;
        b.root.gameObject.SetActive(true);
        if (who == Lab.LocalId) Sfx.Play(SfxId.UiClick, 0.7f);
    }

    private void AnimateBubbles()
    {
        foreach (var b in bubbles.Values)
        {
            if (b.root == null || !b.root.gameObject.activeSelf) continue;
            float age = Time.time - b.born;
            if (b.target == null || age > bubbleSeconds) { b.root.gameObject.SetActive(false); continue; }
            float s = age < 0.22f ? CuteUIKit.EaseOutBack(age / 0.22f, 2.8f)
                    : age > bubbleSeconds - 0.18f ? (bubbleSeconds - age) / 0.18f : 1f;
            b.root.localScale = Vector3.one * Mathf.Max(0.01f, s);
            b.root.position   = b.target.position + new Vector3(0f, b.headroom + 0.32f + Mathf.Sin(age * 5f) * 0.03f, 0f);
        }
    }

    private static Bubble BuildBubble()
    {
        var layers = SortingLayer.layers;
        int top    = layers.Length > 0 ? layers[layers.Length - 1].id : 0;
        var root   = new GameObject("EmoteBubble").transform;

        var border = Part(root, "Border", CuteUIKit.Rounded(12), CuteUIKit.Latte, 196, top, new Vector2(0.66f, 0.6f));
        Part(root, "Fill", CuteUIKit.Rounded(10), Color.white, 197, top, new Vector2(0.58f, 0.52f));
        var tail = Part(root, "Tail", CuteUIKit.Rounded(3), CuteUIKit.Latte, 196, top, new Vector2(0.17f, 0.17f));
        tail.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        tail.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var tailFill = Part(tail.transform, "Fill", CuteUIKit.Rounded(2), Color.white, 197, top, new Vector2(0.1f, 0.1f));
        tailFill.transform.localRotation = Quaternion.identity;
        border.transform.localPosition = Vector3.zero;

        var glyph = new GameObject("Glyph").AddComponent<TextMeshPro>();
        glyph.transform.SetParent(root, false);
        glyph.alignment        = TextAlignmentOptions.Center;
        glyph.fontStyle        = FontStyles.Bold;
        glyph.textWrappingMode = TextWrappingModes.NoWrap;
        glyph.rectTransform.sizeDelta = new Vector2(1f, 0.6f);
        glyph.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        glyph.sortingLayerID = top;
        glyph.sortingOrder   = 199;

        var paw = Part(root, "Paw", CuteUIKit.Paw(), Color.white, 199, top, Vector2.zero);
        paw.drawMode = SpriteDrawMode.Simple;
        paw.transform.localScale = Vector3.one * 0.62f;

        root.gameObject.SetActive(false);
        return new Bubble { root = root, glyph = glyph, paw = paw };
    }

    private static SpriteRenderer Part(Transform parent, string name, Sprite sprite, Color color, int order, int layer, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = sprite;
        sr.sharedMaterial = LabArt.SpriteMaterial;
        sr.color          = color;
        sr.sortingLayerID = layer;
        sr.sortingOrder   = order;
        if (size != Vector2.zero)
        {
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size     = size;
        }
        return sr;
    }

    private void SetWheel(bool open)
    {
        if (open == wheelOpen) return;
        if (open && wheelRoot == null) BuildWheel();
        wheelOpen = open;
        if (wheelCanvas != null) wheelCanvas.gameObject.SetActive(open);
        if (!open) return;

        var canvasRT = (RectTransform)wheelCanvas.transform;
        float scale  = canvasRT.lossyScale.x > 0f ? canvasRT.lossyScale.x : 1f;
        Vector2 half = canvasRT.rect.size * 0.5f;
        Vector2 p    = ((Vector2)GameInput.MousePosition - new Vector2(Screen.width, Screen.height) * 0.5f) / scale;
        if (Pad.Active) p = Vector2.zero;
        p.x = Mathf.Clamp(p.x, -half.x + 110f, half.x - 110f);
        p.y = Mathf.Clamp(p.y, -half.y + 110f, half.y - 110f);
        wheelRoot.anchoredPosition = p;
        StartCoroutine(CuteUIKit.PopIn(wheelRoot, null, 0.22f));
        Sfx.Play(SfxId.UiOpen, 0.6f);
    }

    private void BuildWheel()
    {
        var go = new GameObject("EmoteWheel", typeof(RectTransform));
        wheelCanvas = UIStyleKit.CreateOverlayCanvas(go, 30);

        // 바퀴 밖 클릭 막기
        var blocker = new GameObject("Blocker", typeof(RectTransform));
        blocker.transform.SetParent(go.transform, false);
        var brt = (RectTransform)blocker.transform;
        brt.anchorMin = Vector2.zero;
        brt.anchorMax = Vector2.one;
        brt.offsetMin = brt.offsetMax = Vector2.zero;
        blocker.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        blocker.AddComponent<Button>().onClick.AddListener(() => SetWheel(false));

        var rootGO = new GameObject("Wheel", typeof(RectTransform));
        rootGO.transform.SetParent(go.transform, false);
        wheelRoot = (RectTransform)rootGO.transform;
        wheelRoot.sizeDelta = new Vector2(10f, 10f);

        var hub = CuteUIKit.Pill(wheelRoot, "감정표현  V", CuteUIKit.Hex(0xFFF7EA, 0.95f), CuteUIKit.CocoaSoft, 11f, 12,
                                 new RectOffset(10, 10, 4, 4), out _, fitSelf: true);
        hub.anchoredPosition = Vector2.zero;

        for (int i = 0; i < Emotes.Length; i++)
        {
            int index = i;
            float a   = Mathf.PI * 0.5f - i * Mathf.PI * 2f / Emotes.Length;
            var e     = Emotes[i];
            var btn   = CuteUIKit.Button(wheelRoot, e.glyph, Color.white, () => SendEmote(index), 54f, 54f, e.glyph.Length > 1 ? 17f : 24f);
            var brt2  = (RectTransform)btn.transform;
            brt2.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 82f;
            brt2.sizeDelta = new Vector2(54f, 54f);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            label.color = e.color;
            if (i == HighFive)
            {
                var paw = CuteUIKit.Layer(btn.transform, "Paw", CuteUIKit.Paw(), e.color, 13f);
                paw.preserveAspect = true;
            }
            var name = CuteUIKit.Text(btn.transform, e.name, 10f, CuteUIKit.Cocoa, FontStyles.Bold);
            name.outlineWidth = 0.3f;
            name.outlineColor = new Color32(255, 247, 234, 255);
            var nrt = name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0f);
            nrt.pivot     = new Vector2(0.5f, 1f);
            nrt.anchoredPosition = new Vector2(0f, -4f);
            nrt.sizeDelta = new Vector2(80f, 14f);
        }
        go.SetActive(false);
    }

    protected override void Cleanup()
    {
        SetWheel(false);
        foreach (var b in bubbles.Values) if (b.root != null) Destroy(b.root.gameObject);
        bubbles.Clear();
        fiveAt.Clear();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (wheelCanvas != null) Destroy(wheelCanvas.gameObject);
    }

    public override void BuildControls(Transform row)
    {
        for (int i = 0; i < Emotes.Length; i++)
        {
            int index = i;
            var e = Emotes[i];
            var btn = LabPanelUI.Btn(row, i == HighFive ? "짝" : e.glyph, Color.white, () => SendEmote(index));
            btn.GetComponentInChildren<TextMeshProUGUI>().color = e.color;
        }
    }
}
