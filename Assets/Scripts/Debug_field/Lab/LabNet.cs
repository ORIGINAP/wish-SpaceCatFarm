using System;
using System.Collections.Generic;
using Unity.Netcode;

// 실험실 메시지는 wish.lab 하나로
public static class LabNet
{
    [Serializable]
    private class Envelope
    {
        public string e;
        public string k;
        public string d;
    }

    private static readonly List<NetPeerInfo> peers = new();

    // 호스트면 부탁, 참가자면 이벤트
    public static void Receive(ulong sender, byte[] data)
    {
        if (!Lab.Active) return;
        var msg = NetWire.Decode<Envelope>(data);
        var exp = msg != null ? Lab.Find(msg.e) : null;
        if (exp == null) return;
        if (NetSession.IsHost) exp.HandleRequest(sender, msg.k, msg.d);
        else if (NetSession.IsReplica) exp.HandleEvent(msg.k, msg.d);
    }

    public static void ToHost(string exp, string kind, string json)
    {
        if (!NetClient.CanSend) return;
        NetWire.SendJson(NetWire.Lab, NetworkManager.ServerClientId, new Envelope { e = exp, k = kind, d = json });
    }

    public static void ToPeer(ulong peer, string exp, string kind, string json, bool reliable = true)
    {
        if (NetHost.Current == null) return;
        Send(peer, NetWire.Encode(new Envelope { e = exp, k = kind, d = json }, false), reliable);
    }

    public static void ToAll(string exp, string kind, string json, bool reliable = true, ulong except = ulong.MaxValue)
    {
        if (NetHost.Current == null) return;
        peers.Clear();
        NetHost.Current.CollectReadyPeers(peers);
        if (peers.Count == 0) return;
        var payload = NetWire.Encode(new Envelope { e = exp, k = kind, d = json }, false);
        foreach (var p in peers)
            if (p.id != except) Send(p.id, payload, reliable);
    }

    private static void Send(ulong to, byte[] payload, bool reliable)
    {
        if (reliable) NetWire.SendBytes(NetWire.Lab, to, payload, NetworkDelivery.ReliableFragmentedSequenced);
        else NetWire.SendBytes(NetWire.Lab, to, payload, NetworkDelivery.UnreliableSequenced);
    }
}
