using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public static class NetWire
{
    public const string Hello    = "wish.hello";
    public const string Welcome  = "wish.welcome";
    public const string Ready    = "wish.ready";
    public const string Snapshot = "wish.snap";
    public const string Delta    = "wish.delta";
    public const string Op       = "wish.op";
    public const string Give     = "wish.give";
    public const string Pos      = "wish.pos";
    public const string Roster   = "wish.roster";
    public const string Guest    = "wish.guest";
    public const string Notice   = "wish.notice";
    public const string Storm    = "wish.storm";
    public const string Chat     = "wish.chat";
    public const string Look     = "wish.look";
    public const string Lab      = "wish.lab";

    public static readonly string[] All = { Hello, Welcome, Ready, Snapshot, Delta, Op, Give, Pos, Roster, Guest, Notice, Storm, Chat, Look, Lab };

    public static void SendJson(string name, ulong to, object dto, bool compress = false)
        => SendBytes(name, to, Encode(dto, compress), NetworkDelivery.ReliableFragmentedSequenced);

    public static void SendBytes(string name, ulong to, byte[] payload, NetworkDelivery delivery)
    {
        var cmm = NetworkManager.Singleton != null ? NetworkManager.Singleton.CustomMessagingManager : null;
        if (cmm == null) return;
        using var w = new FastBufferWriter(payload.Length + sizeof(int), Allocator.Persistent);
        w.WriteValueSafe(payload.Length);
        w.WriteBytesSafe(payload);
        cmm.SendNamedMessage(name, to, w, delivery);
    }

    public static byte[] ReadBytes(FastBufferReader r)
    {
        r.ReadValueSafe(out int length);
        var bytes = new byte[length];
        r.ReadBytesSafe(ref bytes, length);
        return bytes;
    }

    public static byte[] Encode(object dto, bool compress)
    {
        var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(dto));
        using var ms = new MemoryStream();
        // 첫 바이트: 0 원본, 1 gzip
        ms.WriteByte(compress ? (byte)1 : (byte)0);
        if (compress)
        {
            using var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true);
            gz.Write(json, 0, json.Length);
        }
        else ms.Write(json, 0, json.Length);
        return ms.ToArray();
    }

    public static T Decode<T>(byte[] data)
    {
        if (data == null || data.Length < 1) return default;
        using var input = new MemoryStream(data, 1, data.Length - 1);
        if (data[0] == 0) return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(data, 1, data.Length - 1));
        using var gz  = new GZipStream(input, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        gz.CopyTo(raw);
        return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(raw.GetBuffer(), 0, (int)raw.Length));
    }
}

[Serializable]
public class HelloMsg
{
    public int    protocol;
    public string id;
    public string name;
    public int    color, accessory;
}

[Serializable]
public class WelcomeMsg
{
    public bool   ok;
    public string reason;
    public int    worldSize, worldTheme, worldSeed;
    public int    gridWidth, gridHeight;
    public string hostName;
    public bool   knownGuest;
    public GuestSave guest = new();
    public bool   lab;
}

[Serializable]
public class SnapshotMsg
{
    public int       ack;
    public SaveData  world;
    public List<int> carved = new();
}

[Serializable]
public class DeltaMsg
{
    public int ack;
    public List<TileSave>          tiles   = new();
    public List<LinkSave>          removed = new();
    public List<ResourceBlockSave> rocks   = new();
    public bool       hasGlobals;
    public NetGlobals globals = new();
}

[Serializable]
public class NetGlobals
{
    public long   currency;
    public int    day, hour, minute;
    public List<TechNodeSave> tech = new();
    public int    questIndex;
    public bool   questsDone;
    public List<int>    questAmounts = new();
    public List<string> harvested    = new();
    public int    coreLevel;
    public float  coreFuel;
    public bool   boost;
    public int    weather, weatherNext, weatherNext2;
    public List<ShuttleSave>  shuttles  = new();
    public int    population, shuttleTrips;
    public List<ResidentSave> residents = new();
    public List<string> codexGolden = new(), codexDishes = new();
    public int    codexMilestones;
}

[Serializable]
public class GiveMsg
{
    public string item;
    public int    amount;
}

[Serializable]
public class RosterMsg
{
    public List<RosterEntry> players = new();
}

[Serializable]
public class RosterEntry
{
    public ulong  clientId;
    public string name;
    public int    color, accessory;
}

[Serializable]
public class NoticeMsg
{
    public string text;
}

[Serializable]
public class ChatMsg
{
    public ulong  from;
    public string name;
    public string text;
}

[Serializable]
public class StormMsg
{
    public int   theme;
    public float warning;
}
