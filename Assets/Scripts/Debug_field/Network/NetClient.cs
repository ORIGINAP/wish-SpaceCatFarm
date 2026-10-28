using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 참가자: 호스트 상태로 월드 맞춤
public class NetClient
{
    public static NetClient Current => Services.Get<NetClient>();

    public static bool CanSend => Current != null && Current.worldReady;

    private const float PosInterval       = 1f / 12f;
    private const float GuestSaveInterval = 10f;

    private readonly string playerId;
    private WelcomeMsg welcome;
    private bool worldReady;

    private int seq, ack, pendingGlobals;
    private float lastSettledGlobals;
    private readonly Dictionary<Vector2Int, int> pendingTiles = new();
    private readonly Dictionary<Vector2Int, int> pendingRocks = new();
    private readonly Dictionary<Vector2Int, ResourceBlock> rocks = new();

    private readonly Dictionary<ulong, RosterEntry> roster = new();
    private float posTimer, guestTimer;

    public string HostName => roster.TryGetValue(NetworkManager.ServerClientId, out var host) ? host.name : welcome?.hostName ?? "";
    public int    PlayerCount => Mathf.Max(1, roster.Count);
    public bool   WorldReady  => worldReady;

    public NetClient(string playerId)
    {
        this.playerId = playerId;
        Services.Replace(this);
    }

    public void Dispose()
    {
        Services.Unregister(this);
    }

    public void OnConnected()
    {
        var me = PlayerCharacter.Current;
        NetWire.SendJson(NetWire.Hello, NetworkManager.ServerClientId, new HelloMsg
        {
            protocol = NetSession.Protocol, id = playerId, name = me.name, color = me.color, accessory = me.accessory,
        });
    }

    public void SendLook()
    {
        if (welcome == null) return;
        NetWire.SendJson(NetWire.Look, NetworkManager.ServerClientId, PlayerCharacter.Current);
    }

    public void OnWelcome(byte[] data)
    {
        var msg = NetWire.Decode<WelcomeMsg>(data);
        if (msg == null || !msg.ok)
        {
            NetBootstrap.Fail(msg?.reason ?? "호스트가 접속을 받지 않았어요.");
            return;
        }
        welcome = msg;

        // 지형은 시드로 똑같이 생성
        GameLaunchContext.PendingWorldSize  = (WorldSize)msg.worldSize;
        GameLaunchContext.PendingWorldTheme = (WorldTheme)msg.worldTheme;
        GameLaunchContext.PendingWorldSeed  = msg.worldSeed;
        GameLaunchContext.PendingGridWidth  = msg.gridWidth;
        GameLaunchContext.PendingGridHeight = msg.gridHeight;
        GameLaunchContext.IsNewGame         = !msg.knownGuest;
        GameLaunchContext.LaunchedFromMenu  = false;
        NetBootstrap.SetStatus($"{msg.hostName}님의 {(msg.lab ? "실험실" : "월드")}로 들어가는 중...");
        if (msg.lab)
        {
            Lab.LaunchPrepared = true;
            SceneManager.LoadScene(Lab.SceneName);
        }
        else SceneManager.LoadScene(NetBootstrap.InGameScene);
    }

    public void OnSceneReady()
    {
        if (welcome == null) return;
        NetWire.SendJson(NetWire.Ready, NetworkManager.ServerClientId, new NoticeMsg());
    }

    public void OnSnapshot(byte[] data)
    {
        var msg = NetWire.Decode<SnapshotMsg>(data);
        if (msg?.world == null || SaveManager.Instance == null) return;
        ack = msg.ack;

        using (NetOps.ApplyingHost())
        {
            SaveManager.Instance.ApplyRemoteWorld(msg.world);
            TerrainMap.Current?.ApplyCarved(msg.carved);
            ApplyMyData();
        }

        rocks.Clear();
        foreach (var b in ResourceBlock.Active)
            if (b != null) rocks[NetWorld.RockKey(b)] = b;

        worldReady = true;
        NetBootstrap.SetStatus("");
        NotificationUI.Instance?.Show(Lab.Active ? $"{welcome.hostName}님의 실험실에 들어왔어요! [L] 실험실 창"
                                                 : $"{welcome.hostName}님의 월드에 들어왔어요! 함께 개척해요.");
    }

    private void ApplyMyData()
    {
        var g = welcome.guest;
        if (welcome.knownGuest && g != null)
        {
            var inv = Inventory.Instance;
            if (inv != null && g.inventorySlots != null)
            {
                inv.ClearAll();
                for (int i = 0; i < g.inventorySlots.Count && i < inv.SlotCount; i++)
                {
                    var s = g.inventorySlots[i];
                    var item = NetWorld.Item(s.itemName);
                    if (item != null && s.count > 0) inv.SetSlotDirect(i, item, s.count);
                }
            }
            var em = EquipmentManager.GetOrCreate();
            for (int i = 0; i < EquipmentManager.SlotCount; i++) em.Unequip(i);
            for (int i = 0; g.equipmentSlots != null && i < g.equipmentSlots.Length && i < EquipmentManager.SlotCount; i++)
            {
                var item = NetWorld.Item(g.equipmentSlots[i]);
                if (item != null) em.TryEquip(i, item);
            }
        }

        var ptc = PlayerTileController.Instance;
        if (ptc == null) return;
        if (welcome.knownGuest && g != null) ptc.transform.position = new Vector3(g.x, g.y, 0f);
        else if (CenterCore.Current != null)
            ptc.transform.position = CenterCore.Current.transform.position;
        if (CenterCore.Current != null) CenterCore.Current.MoveOutside(ptc.transform);
        Physics2D.SyncTransforms();
    }

    public void OnDelta(byte[] data)
    {
        if (!worldReady) return;
        var msg = NetWire.Decode<DeltaMsg>(data);
        if (msg == null) return;
        if (msg.ack > ack) ack = msg.ack;

        SaveManager.ApplyRemote(() =>
        {
            bool placedAny = false;
            using (NetOps.ApplyingHost())
            {
                // 광석 먼저 처리
                foreach (var r in msg.rocks)
                    if (!Pending(pendingRocks, NetWorld.RockKey(r.posX, r.posY)))
                        NetWorld.ApplyRock(r, rocks);

                foreach (var a in msg.removed)
                {
                    var anchor = new Vector2Int(a.x, a.y);
                    if (!Pending(pendingTiles, anchor)) NetWorld.RemoveTileAt(anchor);
                }

                var applied = new List<(TileItem tile, TileSave save)>();
                foreach (var ts in msg.tiles)
                {
                    if (Pending(pendingTiles, new Vector2Int(ts.anchorX, ts.anchorY))) continue;
                    var tile = NetWorld.ApplyTile(ts, out bool placedNew);
                    placedAny |= placedNew;
                    if (tile != null) applied.Add((tile, ts));
                }
                foreach (var (tile, save) in applied)
                    NetWorld.SetLinks(tile, save.links);

                // 내 조작 다 처리된 값만 반영
                if (msg.hasGlobals)
                {
                    bool settled = ack >= pendingGlobals || Time.unscaledTime - lastSettledGlobals > 2f;
                    if (settled) lastSettledGlobals = Time.unscaledTime;
                    NetWorld.ApplyGlobals(msg.globals, settled);
                }
            }
            return placedAny;
        });
    }

    private bool Pending(Dictionary<Vector2Int, int> map, Vector2Int key)
    {
        if (!map.TryGetValue(key, out int s)) return false;
        if (s > ack) return true;
        map.Remove(key);
        return false;
    }

    public void SendOp(NetOp op, bool tile, bool rock)
    {
        op.seq = ++seq;
        pendingGlobals = seq;
        if (tile)
        {
            pendingTiles[new Vector2Int(op.x, op.y)] = seq;
            if (op.type == NetOpType.Link || op.type == NetOpType.Unlink) pendingTiles[new Vector2Int(op.x2, op.y2)] = seq;
        }
        if (rock) pendingRocks[new Vector2Int(op.x, op.y)] = seq;
        NetWire.SendJson(NetWire.Op, NetworkManager.ServerClientId, op);
    }

    public void OnGive(byte[] data)
    {
        var msg  = NetWire.Decode<GiveMsg>(data);
        var item = NetWorld.Item(msg?.item);
        var inv  = Inventory.Instance;
        if (item == null || inv == null || msg.amount == 0) return;

        using (NetOps.ApplyingHost())
        {
            if (msg.amount > 0)
            {
                var ptc = PlayerTileController.Instance;
                inv.AddItemOrDrop(item, msg.amount, ptc != null ? ptc.transform.position : Vector3.zero);
            }
            else
            {
                int take = Mathf.Min(-msg.amount, inv.GetCount(item));
                if (take > 0) inv.ConsumeItem(item, take);
            }
        }
    }

    public void OnRoster(byte[] data)
    {
        var msg = NetWire.Decode<RosterMsg>(data);
        if (msg == null) return;
        roster.Clear();
        foreach (var p in msg.players) roster[p.clientId] = p;
        NetBootstrap.Avatars.Prune(roster.Keys);
        NetBootstrap.RaiseChanged();
    }

    public void OnPositions(FastBufferReader r)
    {
        if (!worldReady) return;
        r.ReadValueSafe(out int count);
        ulong me = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
        for (int i = 0; i < count; i++)
        {
            r.ReadValueSafe(out ulong id);
            r.ReadValueSafe(out float x);
            r.ReadValueSafe(out float y);
            if (id == me) continue;
            roster.TryGetValue(id, out var who);
            NetBootstrap.Avatars.SetTarget(id, new Vector2(x, y), who?.name, who?.color ?? 0, who?.accessory ?? 0);
        }
    }

    public void OnChat(byte[] data)
    {
        var msg = NetWire.Decode<ChatMsg>(data);
        if (msg == null) return;
        var text = NetBootstrap.CleanChat(msg.text);
        NetBootstrap.ShowChat(msg.name, text);
        NetBootstrap.Avatars.Say(msg.from, text);
    }

    public void OnNotice(byte[] data)
    {
        var msg = NetWire.Decode<NoticeMsg>(data);
        if (!string.IsNullOrEmpty(msg?.text)) NotificationUI.Instance?.Show(msg.text);
    }

    public void OnStorm(byte[] data)
    {
        var msg = NetWire.Decode<StormMsg>(data);
        if (msg != null && worldReady) GameEventManager.Instance?.PlayRemoteStorm((WorldTheme)msg.theme, msg.warning);
    }

    public void Tick(float dt)
    {
        if (!worldReady) return;
        var ptc = PlayerTileController.Instance;

        posTimer += dt;
        if (posTimer >= PosInterval && ptc != null)
        {
            posTimer = 0f;
            Vector2 p = ptc.transform.position;
            using var w = new FastBufferWriter(8, Unity.Collections.Allocator.Temp);
            w.WriteValueSafe(p.x);
            w.WriteValueSafe(p.y);
            NetworkManager.Singleton?.CustomMessagingManager?.SendNamedMessage(NetWire.Pos, NetworkManager.ServerClientId, w,
                                                                               NetworkDelivery.UnreliableSequenced);
        }

        guestTimer += dt;
        if (guestTimer >= GuestSaveInterval)
        {
            guestTimer = 0f;
            SendMyData();
        }
    }

    public void SendMyData()
    {
        if (!worldReady) return;
        var g = new GuestSave { id = playerId, name = PlayerCharacter.Name };
        var ptc = PlayerTileController.Instance;
        if (ptc != null)
        {
            g.x = ptc.transform.position.x;
            g.y = ptc.transform.position.y;
        }
        var inv = Inventory.Instance;
        if (inv != null)
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var s = inv.GetSlot(i);
                bool empty = s == null || s.IsEmpty;
                g.inventorySlots.Add(new ItemSlotSave { itemName = empty ? "" : s.Item.name, count = empty ? 0 : s.Count });
            }
        var em = EquipmentManager.Instance;
        g.equipmentSlots = new string[EquipmentManager.SlotCount];
        for (int i = 0; i < EquipmentManager.SlotCount; i++)
            g.equipmentSlots[i] = em != null && em.GetSlot(i) != null ? em.GetSlot(i).name : "";
        NetWire.SendJson(NetWire.Guest, NetworkManager.ServerClientId, g);
    }
}
