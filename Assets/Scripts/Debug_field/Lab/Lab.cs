using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 실험실 공용 도우미
public static class Lab
{
    public const string SceneName = "Lab";

    public static bool Active { get; internal set; }

    public static bool LaunchPrepared;

    public static readonly List<LabExperiment> Experiments = new();

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = false;
        LaunchPrepared = false;
        Experiments.Clear();
        reserved.Clear();
    }

    public static bool WorldReady =>
        Active && GridManager.Instance != null && PlayerTileController.Instance != null &&
        (NetSession.IsReplica ? NetClient.CanSend : CenterCore.Current != null);

    // 혼자거나 호스트면 판정
    public static bool IsAuthority => !NetSession.IsReplica;

    public static ulong LocalId => NetSession.IsReplica && NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : NetworkManager.ServerClientId;

    public static T Find<T>() where T : LabExperiment
    {
        foreach (var e in Experiments) if (e is T t) return t;
        return null;
    }

    public static LabExperiment Find(string id)
    {
        foreach (var e in Experiments) if (e != null && e.Id == id) return e;
        return null;
    }

    internal static void OnPeerReady(ulong peer)
    {
        foreach (var e in Experiments)
            if (e != null) e.SendFullState(peer);
    }

    public struct Member
    {
        public ulong   id;
        public string  name;
        public int     color;
        public Vector2 pos;
        public Vector2 Feet => pos + FeetOffset;
    }

    // 판정은 발밑 기준
    public static readonly Vector2 FeetOffset = new(0f, -0.33f);

    private static readonly List<NetPeerInfo> peers = new();

    public static void CollectMembers(List<Member> list)
    {
        list.Clear();
        var me = PlayerTileController.Instance;
        if (me != null)
            list.Add(new Member { id = LocalId, name = PlayerCharacter.Name, color = PlayerCharacter.Current.color, pos = me.transform.position });
        if (NetHost.Current == null) return;
        peers.Clear();
        NetHost.Current.CollectReadyPeers(peers);
        foreach (var p in peers)
            if (p.hasPos) list.Add(new Member { id = p.id, name = p.name, color = p.color, pos = p.pos });
    }

    public static string MemberName(ulong id)
    {
        if (id == LocalId) return PlayerCharacter.Name;
        if (NetHost.Current != null)
        {
            peers.Clear();
            NetHost.Current.CollectReadyPeers(peers);
            foreach (var p in peers)
                if (p.id == id) return p.name;
        }
        return "대원";
    }

    public static Transform MemberTransform(ulong id)
    {
        if (id == LocalId) return PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null;
        return NetBootstrap.Avatars.RootOf(id);
    }

    public static Vector2 LocalPos => PlayerTileController.Instance != null ? (Vector2)PlayerTileController.Instance.transform.position : Vector2.zero;

    public static Color MemberColor(ulong id)
    {
        switch ((int)(id % 4))
        {
            case 0:  return CuteUIKit.Strawberry;
            case 1:  return CuteUIKit.Hex(0x6FB6EE);
            case 2:  return CuteUIKit.Hex(0x5CC79A);
            default: return CuteUIKit.Hex(0xF2B731);
        }
    }

    private static readonly List<ResourceBlock> nearBlocks = new();
    private static readonly Dictionary<object, Rect> reserved = new();

    public static void Reserve(object key, Rect area) => reserved[key] = area;

    public static void Unreserve(object key) => reserved.Remove(key);

    public static List<Rect> ReservedExcept(object key)
    {
        var list = new List<Rect>();
        foreach (var pair in reserved)
            if (!Equals(pair.Key, key)) list.Add(pair.Value);
        return list;
    }

    internal static void ClearReserved() => reserved.Clear();

    public static bool IsAreaClear(Rect area, float rockMargin = 0.9f, List<Rect> avoid = null)
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;
        Vector2 min = gm.WorldMin, max = gm.WorldMax;
        if (area.xMin < min.x + 1f || area.yMin < min.y + 1f || area.xMax > max.x - 1f || area.yMax > max.y - 1f) return false;

        // 시작 자리도 가리지 않게 여유
        float coreHalf = CenterCore.Size * gm.CellSize * 0.5f + 1.6f;
        var core = CenterCore.Current != null ? (Vector2)CenterCore.Current.transform.position : Vector2.zero;
        if (area.Overlaps(new Rect(core.x - coreHalf, core.y - coreHalf - 1.2f, coreHalf * 2f, coreHalf * 2f + 1.2f))) return false;

        if (avoid != null)
            foreach (var r in avoid)
                if (area.Overlaps(r)) return false;

        var terrain = TerrainMap.Current;
        Vector2Int c0 = gm.WorldToGrid(new Vector3(area.xMin, area.yMin)), c1 = gm.WorldToGrid(new Vector3(area.xMax, area.yMax));
        for (int x = c0.x; x <= c1.x; x++)
        for (int y = c0.y; y <= c1.y; y++)
        {
            var cell = new Vector2Int(x, y);
            if (!gm.IsInBounds(cell) || !gm.IsEmpty(cell)) return false;
            if (terrain != null && terrain.IsCellBlocked(cell)) return false;
        }

        var grown = new Rect(area.xMin - rockMargin, area.yMin - rockMargin, area.width + rockMargin * 2f, area.height + rockMargin * 2f);
        ResourceBlock.Near(grown, nearBlocks);
        foreach (var b in nearBlocks)
            if (b != null && grown.Contains((Vector2)b.transform.position)) return false;
        return true;
    }

    public static bool FindClearArea(Vector2 center, Vector2 size, float minR, float maxR, out Vector2 found,
                                     List<Rect> avoid = null, int tries = 160, float rockMargin = 0.9f)
    {
        for (int i = 0; i < tries; i++)
        {
            float r = Mathf.Lerp(minR, maxR, Mathf.Sqrt(Random.value));
            var p = center + Random.insideUnitCircle.normalized * r;
            if (IsAreaClear(new Rect(p - size * 0.5f, size), rockMargin, avoid))
            {
                found = p;
                return true;
            }
        }
        found = center;
        return false;
    }

    public static bool IsSpotClear(Vector2 p, float radius = 0.5f, List<Rect> avoid = null)
        => IsAreaClear(new Rect(p.x - radius, p.y - radius, radius * 2f, radius * 2f), 0.7f, avoid);

    public static readonly Color NoticeColor = CuteUIKit.Hex(0xB59CF0);

    public static void Notify(string message) => NotificationUI.Instance?.Show(message, NoticeColor);

    public static string DirectionName(Vector2 from, Vector2 to)
    {
        var d = to - from;
        if (d.sqrMagnitude < 4f) return "바로 근처";
        float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        string[] names = { "동쪽", "북동쪽", "북쪽", "북서쪽", "서쪽", "남서쪽", "남쪽", "남동쪽" };
        int i = Mathf.RoundToInt(Mathf.Repeat(a, 360f) / 45f) % 8;
        return $"{names[i]} {d.magnitude:0}m";
    }
}
