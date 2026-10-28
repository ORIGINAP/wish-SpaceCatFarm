public static class NetSession
{
    public enum Role { Offline, Host, Client }

    public static Role Mode { get; internal set; } = Role.Offline;

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Mode = Role.Offline;

    public static bool IsHost => Mode == Role.Host;

    public static bool IsReplica => Mode == Role.Client;

    // 메시지 형식 바꾸면 올릴 것
    public const int Protocol = 9;
}
