using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ResidentCat : MonoBehaviour
{
    private const float Speed       = 1.1f;
    private const float WanderRange = 3.2f;
    private const float TalkRange   = 8f;
    private const float HopSeconds  = 0.45f;
    private const float TalkGap     = 5f;

    private static float nextAnyTalk;

    public static readonly List<ResidentCat> All = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTalk()
    {
        nextAnyTalk = 0f;
        All.Clear();
    }

    private TileItem       home;
    private string         catName;

    public string CatName => catName;

    public ResidentSave Resident { get; set; }

    private Animator       anim;
    private Vector2        target;
    private float          idleUntil;
    private float          nextTalk;
    private bool           goingHome;
    private float          goneAt;
    private TextMeshPro    bubble;
    private SpriteRenderer bubbleBg, bubbleBorder;
    private float          bubbleUntil;
    private int            bubbleKind;
    private Vector2        feetOffset = new(0f, -0.33f);
    private Vector2        feetHalf   = new(0.25f, 0.1f);
    private Vector2        hopFrom, hopTo;
    private float          hopStart = -1f;
    private bool           hopSpeaks;
    private SpriteRenderer body;

    public static ResidentCat Spawn(Vector2 at, TileItem home, string name, int color, int accessory, bool hop,
                                    float hopDelay = 0f, int hopIndex = 0, bool speak = true)
    {
        var player = Player.Local;
        if (player == null) return null;

        var go = new GameObject("ResidentCat");
        go.transform.position   = new Vector3(at.x, at.y, player.transform.position.z);
        go.transform.localScale = player.transform.lossyScale;

        var src = player.GetComponent<SpriteRenderer>();
        var sr  = go.AddComponent<SpriteRenderer>();
        if (src != null)
        {
            sr.sprite         = CatSkin.Original(src.sprite);
            sr.sharedMaterial = src.sharedMaterial;
            sr.sortingLayerID = src.sortingLayerID;
            sr.sortingOrder   = src.sortingOrder;
        }
        var srcAnim = player.GetComponent<Animator>();
        Animator anim = null;
        if (srcAnim != null && srcAnim.runtimeAnimatorController != null)
        {
            anim = go.AddComponent<Animator>();
            anim.runtimeAnimatorController = srcAnim.runtimeAnimatorController;
        }
        go.AddComponent<CatSkinRenderer>().SetLook(color, accessory);

        var cat = go.AddComponent<ResidentCat>();
        cat.body      = sr;
        cat.home      = home;
        cat.anim      = anim;
        cat.catName   = string.IsNullOrEmpty(name) ? "냥이" : name;
        cat.target    = at;
        cat.idleUntil = Time.time + Random.Range(0.5f, 3f);
        cat.nextTalk  = Time.time + Random.Range(6f, 20f);
        cat.BuildBubble();
        if (hop)
        {
            float side = hopIndex == 0 ? 0f : ((hopIndex + 1) / 2) * 0.55f * (hopIndex % 2 == 1 ? -1f : 1f);
            cat.hopFrom   = at;
            cat.hopTo     = at + new Vector2(Mathf.Clamp(side, -1.4f, 1.4f) + Random.Range(-0.12f, 0.12f), Random.Range(-1.15f, -0.8f));
            if (GridManager.Instance != null) cat.hopTo = GridManager.Instance.ClampToWorldBounds(cat.hopTo);
            cat.hopStart  = Time.time + hopDelay;
            cat.hopSpeaks = speak;
            cat.target    = cat.hopTo;
            cat.idleUntil = cat.hopStart + HopSeconds + Random.Range(0.8f, 1.6f);
            cat.nextTalk  = Time.time + Random.Range(25f, 40f);
            if (hopDelay > 0f) sr.enabled = false;
        }
        CatPresence.Register(go.transform);
        All.Add(cat);
        return cat;
    }

    public void SetHome(TileItem newHome)
    {
        if (newHome == null || newHome == home) return;
        home = newHome;
        idleUntil = Mathf.Min(idleUntil, Time.time + 1f);
        target = HomeSpot();
    }

    public TileItem Home => home;

    public void GoHome()
    {
        goingHome = true;
        goneAt    = Time.time + 3f;
        if (home == null) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        CatPresence.Unregister(transform);
        All.Remove(this);
    }

    public bool Interactable => !goingHome && hopStart < 0f && body != null && body.enabled && Resident != null;

    public static ResidentCat At(Vector2 world)
    {
        ResidentCat best = null;
        foreach (var c in All)
        {
            if (c == null || !c.Interactable) continue;
            var b = c.body.bounds;
            // 투명 가장자리 제외
            if (world.x < b.min.x + b.size.x * 0.15f || world.x > b.max.x - b.size.x * 0.15f) continue;
            if (world.y < b.min.y || world.y > b.max.y - b.size.y * 0.1f) continue;
            if (best == null || c.transform.position.y < best.transform.position.y) best = c;
        }
        return best;
    }

    public static ResidentCat Showing(ResidentSave r)
    {
        if (r == null) return null;
        foreach (var c in All)
            if (c != null && c.Resident != null && (c.Resident == r || (r.id > 0 && c.Resident.id == r.id))) return c;
        return null;
    }

    public Vector3 PromptAnchor => new(transform.position.x, transform.position.y + 1.6f, 0f);

    public void FaceTowards(Vector3 p)
    {
        if (hopStart >= 0f || goingHome) return;
        Vector2 d = (Vector2)(p - transform.position);
        idleUntil = Time.time + 4f;
        target    = transform.position;
        SetWalk(Vector2.zero);
        if (anim != null && d.sqrMagnitude > 0.0001f)
        {
            anim.SetFloat("last.x", d.x);
            anim.SetFloat("last.y", d.y);
        }
    }

    public void Reply(string line, int hearts)
    {
        Show($"<b>{catName}</b>  {ResidentBond.HeartText(Resident)}\n{line}", 5f, 3);
        nextAnyTalk = Time.time + TalkGap;
        nextTalk    = Time.time + Random.Range(25f, 60f);
        for (int i = 0; i < hearts; i++) Heart(i * 0.18f, aboveBubble: true);
    }

    public void ShowHover(string verb)
    {
        if (bubble == null || !Interactable) return;
        if (bubbleKind == 3 && bubble.gameObject.activeSelf) return;
        string hint = string.IsNullOrEmpty(verb) ? "" : $"\n<size=85%><color=#{ColorUtility.ToHtmlStringRGB(CuteUIKit.CocoaSoft)}>{verb}</color></size>";
        string text = $"<b>{catName}</b>  {ResidentBond.HeartText(Resident)}{hint}";
        if (bubbleKind == 2 && bubble.gameObject.activeSelf && bubble.text == text) bubbleUntil = Time.time + 0.25f;
        else Show(text, 0.25f, 2);
    }

    private void Update()
    {
        if (hopStart >= 0f)
        {
            if (Time.time < hopStart) return;
            if (body != null && !body.enabled) body.enabled = true;
            float u = (Time.time - hopStart) / HopSeconds;
            if (u >= 1f)
            {
                hopStart = -1f;
                transform.position = new Vector3(hopTo.x, hopTo.y, transform.position.z);
                if (hopSpeaks) Say(ArrivalLine());
                Heart();
            }
            else
            {
                Vector2 p = Vector2.Lerp(hopFrom, hopTo, u) + Vector2.up * Mathf.Sin(u * Mathf.PI) * 0.55f;
                transform.position = new Vector3(p.x, p.y, transform.position.z);
                SetWalk(hopTo - hopFrom);
                return;
            }
        }

        Vector2 pos = transform.position;
        if (goingHome)
        {
            if (home == null || Time.time > goneAt) { Destroy(gameObject); return; }
            target = home.transform.position;
            if ((pos - target).sqrMagnitude < 1.5f) { Destroy(gameObject); return; }
        }
        else if (home == null) { Destroy(gameObject); return; }

        if (!goingHome && Time.time < idleUntil) { SetWalk(Vector2.zero); TickBubble(); return; }

        Vector2 to = target - pos;
        if (to.sqrMagnitude < 0.02f)
        {
            idleUntil = Time.time + Random.Range(2f, 6f);
            target = HomeSpot();
            SetWalk(Vector2.zero);
        }
        else
        {
            Vector2 step = to.normalized * Speed * Time.deltaTime;
            if (step.sqrMagnitude > to.sqrMagnitude) step = to;
            Vector2 next = TileCollision.Move(pos, pos + step, feetOffset, feetHalf);
            if ((next - pos).sqrMagnitude < step.sqrMagnitude * 0.05f)
            {
                idleUntil = Time.time + 1f;
                target    = next;
            }
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            SetWalk(step);
        }
        TickBubble();
    }

    private Vector2 HomeSpot()
    {
        if (home == null) return transform.position;
        Vector2 t = (Vector2)home.transform.position + Random.insideUnitCircle * WanderRange + Vector2.down * 0.6f;
        return GridManager.Instance != null ? GridManager.Instance.ClampToWorldBounds(t) : t;
    }

    private void SetWalk(Vector2 move)
    {
        if (anim == null) return;
        bool moving = move.sqrMagnitude > 0.000001f;
        bool vertical = Mathf.Abs(move.y) >= Mathf.Abs(move.x);
        anim.SetBool("isWalkUp",    moving && vertical && move.y > 0f);
        anim.SetBool("isWalkDown",  moving && vertical && move.y <= 0f);
        anim.SetBool("isWalkRight", moving && !vertical && move.x > 0f);
        anim.SetBool("isWalkLeft",  moving && !vertical && move.x <= 0f);
        if (moving)
        {
            anim.SetFloat("last.x", move.x);
            anim.SetFloat("last.y", move.y);
        }
    }

    private void TickBubble()
    {
        if (bubble == null) return;
        if (bubble.gameObject.activeSelf && Time.time > bubbleUntil)
        {
            bubble.gameObject.SetActive(false);
            bubbleKind = 0;
        }
        if (goingHome || Time.time < nextTalk) return;
        if (Time.time < nextAnyTalk) { nextTalk = Time.time + Random.Range(3f, 10f); return; }
        nextTalk = Time.time + Random.Range(25f, 60f);

        var player = PlayerTileController.Instance;
        if (player == null || ((Vector2)(player.transform.position - transform.position)).sqrMagnitude > TalkRange * TalkRange) return;
        Say(Line());
    }

    private void Say(string text)
    {
        if (bubble == null) return;
        if (bubbleKind >= 2 && bubble.gameObject.activeSelf) return;
        nextAnyTalk = Time.time + TalkGap;
        Show($"<b>{catName}</b>  {text}", 4.5f, 1);
    }

    private void Show(string text, float seconds, int kind)
    {
        bubble.text = text;
        bubble.gameObject.SetActive(true);
        bubble.ForceMeshUpdate();
        var b = bubble.textBounds;
        bubbleBg.size = new Vector2(b.size.x + 0.3f, b.size.y + 0.16f);
        bubbleBg.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
        bubbleBorder.size = bubbleBg.size + new Vector2(0.07f, 0.07f);
        bubbleBorder.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.02f);
        bubbleUntil = Time.time + seconds;
        bubbleKind  = kind;
    }

    private static string ArrivalLine()
    {
        string[] pool = { "도착했다냥!", "여기가 새 행성이냥?", "와, 땅이 따뜻하다냥~", "대원님 고맙다냥!", "공기가 맛있다냥!" };
        return pool[Random.Range(0, pool.Length)];
    }

    private string Line()
    {
        string[] pool;
        switch (WeatherManager.Current)
        {
            case WeatherType.StarRain:     pool = new[] { "별비다! 목화가 신났다냥", "빗소리 좋다냥…", "밭이 촉촉해서 좋다냥" }; break;
            case WeatherType.Aurora:       pool = new[] { "오로라 예쁘다냥…", "이런 밤엔 황금 작물이 열린다던데냥", "크리스탈 베리가 반짝반짝하다냥" }; break;
            case WeatherType.MeteorShower: pool = new[] { "별똥별에 소원 빌었다냥!", "운석 떨어진 데 가 보라냥, 반짝이는 게 있다냥", "하늘이 불꽃놀이 같다냥" }; break;
            case WeatherType.Storm:        pool = new[] { "바람이 무섭다냥…", "온실 곁이 제일 안전하다냥", "혜성 고추는 폭풍을 좋아한다냥" }; break;
            default:
                pool = FarmConditions.IsNight
                    ? new[] { "밤엔 달빛 무가 쑥쑥 자란다냥", "졸리다냥…", "저 별 어딘가에 고향이 있다냥", "다음 셔틀엔 누가 올까냥?" }
                    : new[] { "새 행성 공기가 좋다냥~", "츄르 먹고 싶다냥…", "털실 뭉치 어디 없냥?", "고향 친구들도 곧 셔틀 타고 온다냥",
                              "낮잠 자기 딱 좋은 날씨다냥", "해바라기가 해를 보고 웃는다냥", "캣닢 냄새 좋다냥~", "여기선 배고플 일이 없다냥",
                              "대원님 덕분에 살았다냥" };
                break;
        }
        return pool[Random.Range(0, pool.Length)];
    }

    private void Heart(float delay = 0f, bool aboveBubble = false)
    {
        var go = new GameObject("Heart");
        go.transform.SetParent(transform, false);
        float inv = 1f / Mathf.Max(0.01f, transform.localScale.x);
        go.transform.localScale = new Vector3(inv, inv, 1f);
        var t = go.AddComponent<TextMeshPro>();
        t.text = "+1";
        t.fontSize = 3.2f;
        t.alignment = TextAlignmentOptions.Center;
        t.color = CuteUIKit.Strawberry;
        t.sortingLayerID = WorldFx.TopLayer;
        t.sortingOrder = 201;
        t.rectTransform.sizeDelta = new Vector2(1f, 1f);
        float x = aboveBubble ? Random.Range(-0.45f, 0.45f) : 0.25f;
        float y = aboveBubble ? 1.4f : 0.75f;
        go.AddComponent<RiseAndFade>().Begin(new Vector3(x * inv, y * inv, 0f), (aboveBubble ? 0.45f : 0.6f) * inv, 1.1f, delay);
    }

    private void BuildBubble()
    {
        var go = new GameObject("Bubble");
        go.transform.SetParent(transform, false);
        float inv = 1f / Mathf.Max(0.01f, transform.localScale.x);
        go.transform.localScale    = new Vector3(inv, inv, 1f);
        go.transform.localPosition = new Vector3(0f, 0.95f * inv, 0f);
        bubble = go.AddComponent<TextMeshPro>();
        bubble.fontSize  = 2.2f;
        bubble.alignment = TextAlignmentOptions.Center;
        bubble.textWrappingMode = TextWrappingModes.NoWrap;
        bubble.rectTransform.sizeDelta = new Vector2(6f, 0.5f);
        bubble.color          = CuteUIKit.Cocoa;
        bubble.sortingLayerID = WorldFx.TopLayer;
        bubble.sortingOrder   = 200;

        bubbleBg = WorldFx.Sprite(go.transform, "Background", CuteUIKit.Rounded(12), CuteUIKit.Cream, 199, 1f);
        bubbleBg.transform.localScale = Vector3.one;
        bubbleBg.drawMode = SpriteDrawMode.Sliced;
        bubbleBorder = WorldFx.Sprite(go.transform, "Border", CuteUIKit.Rounded(12), CuteUIKit.Latte, 198, 1f);
        bubbleBorder.transform.localScale = Vector3.one;
        bubbleBorder.drawMode = SpriteDrawMode.Sliced;
        go.SetActive(false);
    }

    private class RiseAndFade : MonoBehaviour
    {
        private Vector3 from;
        private float   rise, duration, start;
        private TextMeshPro text;

        public void Begin(Vector3 localFrom, float riseBy, float seconds, float delay = 0f)
        {
            from = localFrom;
            rise = riseBy;
            duration = seconds;
            start = Time.time + delay;
            text = GetComponent<TextMeshPro>();
            transform.localPosition = from;
            if (delay > 0f && text != null) { var c = text.color; c.a = 0f; text.color = c; }
        }

        private void Update()
        {
            float u = (Time.time - start) / duration;
            if (u < 0f) return;
            if (u >= 1f) { Destroy(gameObject); return; }
            transform.localPosition = from + Vector3.up * rise * (1f - (1f - u) * (1f - u));
            if (text != null) { var c = text.color; c.a = u < 0.5f ? 1f : 1f - (u - 0.5f) * 2f; text.color = c; }
        }
    }
}
