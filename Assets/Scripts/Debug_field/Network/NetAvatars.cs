using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 다른 플레이어 캐릭터, 충돌 없음
public class NetAvatars
{
    private const float FollowSharpness = 12f;
    private const float SnapDistance    = 4f;

    private sealed class Avatar
    {
        public Transform   root;
        public Vector3     target;
        public TextMeshPro label;
        public SpriteRenderer pill;
        public CatSkinRenderer skin;
        public int         color = -1, accessory = -1;
        public float       headroom;
    }

    private readonly Dictionary<ulong, Avatar> avatars = new();

    public void SetTarget(ulong id, Vector2 pos, string name, int color, int accessory)
    {
        if (!avatars.TryGetValue(id, out var a) || a.root == null)
        {
            a = Create(pos);
            if (a == null) return;
            avatars[id] = a;
        }
        a.target = new Vector3(pos.x, pos.y, a.root.position.z);
        if (a.color != color || a.accessory != accessory) SetLook(a, color, accessory);
        if (!string.IsNullOrEmpty(name) && a.label.text != name) SetName(a, name);
    }

    public void Say(ulong id, string text)
    {
        if (avatars.TryGetValue(id, out var a) && a.root != null) ChatBubble.Show(a.root, text, a.headroom);
    }

    public Transform RootOf(ulong id) => avatars.TryGetValue(id, out var a) && a.root != null ? a.root : null;

    public void CollectRoots(List<(ulong id, Transform root, int color)> list)
    {
        foreach (var pair in avatars)
            if (pair.Value.root != null) list.Add((pair.Key, pair.Value.root, pair.Value.color));
    }

    public void Remove(ulong id)
    {
        if (!avatars.TryGetValue(id, out var a)) return;
        if (a.root != null) Object.Destroy(a.root.gameObject);
        avatars.Remove(id);
    }

    public void Prune(IEnumerable<ulong> keep)
    {
        var alive = new HashSet<ulong>(keep);
        foreach (var id in new List<ulong>(avatars.Keys))
            if (!alive.Contains(id)) Remove(id);
    }

    public void Clear()
    {
        foreach (var id in new List<ulong>(avatars.Keys)) Remove(id);
    }

    public void Update(float dt)
    {
        float k = 1f - Mathf.Exp(-FollowSharpness * dt);
        foreach (var a in avatars.Values)
        {
            if (a.root == null) continue;
            var p = a.root.position;
            a.root.position = (a.target - p).sqrMagnitude > SnapDistance * SnapDistance ? a.target : Vector3.Lerp(p, a.target, k);
        }
    }

    private static Avatar Create(Vector2 pos)
    {
        var local = Player.Local;
        if (local == null) return null;

        var go = new GameObject("NetPlayer");
        go.transform.position   = new Vector3(pos.x, pos.y, local.transform.position.z);
        go.transform.localScale = local.transform.lossyScale;

        var src = local.GetComponent<SpriteRenderer>();
        var sr  = go.AddComponent<SpriteRenderer>();
        if (src != null)
        {
            sr.sprite         = CatSkin.Original(src.sprite);
            sr.sharedMaterial = src.sharedMaterial;
            sr.sortingLayerID = src.sortingLayerID;
            sr.sortingOrder   = src.sortingOrder;
            sr.drawMode       = src.drawMode;
            sr.size           = src.size;
        }
        var anim = local.GetComponent<Animator>();
        if (anim != null && anim.runtimeAnimatorController != null)
            go.AddComponent<Animator>().runtimeAnimatorController = anim.runtimeAnimatorController;
        var skin = go.AddComponent<CatSkinRenderer>();

        var layers   = SortingLayer.layers;
        int topLayer = layers.Length > 0 ? layers[layers.Length - 1].id : 0;

        var tagGO = new GameObject("Name");
        tagGO.transform.SetParent(go.transform, false);
        tagGO.transform.localScale    = new Vector3(1f / Mathf.Max(0.01f, go.transform.localScale.x), 1f / Mathf.Max(0.01f, go.transform.localScale.y), 1f);
        var label = tagGO.AddComponent<TextMeshPro>();
        label.fontSize         = 2.2f;
        label.alignment        = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(4f, 0.5f);
        label.color          = Color.white;
        label.fontStyle      = FontStyles.Bold;
        label.sortingLayerID = topLayer;
        label.sortingOrder   = 200;

        var pillGO = new GameObject("Pill");
        pillGO.transform.SetParent(tagGO.transform, false);
        var pill = pillGO.AddComponent<SpriteRenderer>();
        pill.sprite         = CuteUIKit.Rounded(13);
        pill.drawMode       = SpriteDrawMode.Sliced;
        pill.sharedMaterial = WorldConnectionLine.SharedMaterial;
        pill.color          = CuteUIKit.SkyText;
        pill.sortingLayerID = topLayer;
        pill.sortingOrder   = 199;

        CatPresence.Register(go.transform);
        var avatar = new Avatar { root = go.transform, target = go.transform.position, label = label, pill = pill, skin = skin };
        PlaceTag(avatar, sr.sprite);
        return avatar;
    }

    private static void SetLook(Avatar a, int color, int accessory)
    {
        a.color     = color;
        a.accessory = accessory;
        a.skin.SetLook(color, accessory);
        var standing = CatSkin.Get(CatFrames.First, color, accessory);
        if (standing != null) PlaceTag(a, standing);
    }

    private static void PlaceTag(Avatar a, Sprite sprite)
    {
        float scaleY = Mathf.Max(0.01f, a.root.localScale.y);
        float top    = sprite != null ? sprite.bounds.max.y * scaleY : 0.5f;
        a.label.transform.localPosition = new Vector3(0f, (top + 0.15f) / scaleY, 0f);
        a.headroom = top + 0.38f;
    }

    private static void SetName(Avatar a, string name)
    {
        a.label.text = name;
        a.root.name  = $"NetPlayer_{name}";
        a.label.ForceMeshUpdate();
        var b = a.label.textBounds;
        a.pill.size = new Vector2(b.size.x + 0.26f, Mathf.Max(0.3f, b.size.y + 0.1f));
        a.pill.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
    }
}
