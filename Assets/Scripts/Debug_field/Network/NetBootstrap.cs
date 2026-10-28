using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

// 멀티 세션 관리
public class NetBootstrap : MonoBehaviour
{
    public const string InGameScene   = "InGame";
    public const string MainMenuScene = "MainMenu";
    public const ushort DefaultPort   = 7777;
    private const int   MaxPlayers    = 4;

    public static NetBootstrap Instance => Services.Get<NetBootstrap>();

    public static NetAvatars Avatars { get; } = new();

    public static string JoinCode { get; private set; } = "";

    public static string Status { get; private set; } = "";

    public static bool Busy { get; private set; }

    public static event Action Changed;

    public static int PlayerCount => NetHost.Current?.PlayerCount ?? NetClient.Current?.PlayerCount ?? 1;

    public const int MaxChatLength = 100;

    private static readonly Color ChatColor = new(1f, 0.84f, 0.89f);

    public static string HostName => NetHost.Current != null ? PlayerCharacter.Name : NetClient.Current?.HostName ?? "";

    private NetworkManager nm;
    private UnityTransport transport;
    private NetHost   host;
    private NetClient client;
    private ISession  session;
    private bool      hostOnLoad;
    private ushort    hostPort;
    private static string sessionName;

    public static string PlayerName
    {
        get
        {
            if (!string.IsNullOrEmpty(sessionName)) return sessionName;
            var name = PlayerPrefs.GetString("net.name", "");
            if (string.IsNullOrEmpty(name))
            {
                name = $"대원{UnityEngine.Random.Range(1000, 10000)}";
                PlayerPrefs.SetString("net.name", name);
            }
            return name;
        }
        set
        {
            var v = (value ?? "").Trim();
            if (v.Length == 0) return;
            if (v.Length > 12) v = v.Substring(0, 12);
            sessionName = null;
            PlayerPrefs.SetString("net.name", v);
        }
    }

    public static CatLook GuestLook
    {
        get => new()
        {
            name      = PlayerName,
            color     = CatSkin.ValidColor(PlayerPrefs.GetInt("net.color", 0)),
            accessory = CatSkin.ValidAccessory(PlayerPrefs.GetInt("net.accessory", 0)),
        };
        set
        {
            PlayerName = value.name;
            PlayerPrefs.SetInt("net.color", value.color);
            PlayerPrefs.SetInt("net.accessory", value.accessory);
            PlayerPrefs.Save();
        }
    }

    public static string PlayerId
    {
        get
        {
            var id = PlayerPrefs.GetString("net.id", "");
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("net.id", id);
                PlayerPrefs.Save();
            }
            return Profile.Length == 0 ? id : $"{id}-{Profile}";
        }
    }

    private static System.Threading.Mutex profileMutex;
    private static string profile;

    // 한 PC에서 여러 개 띄울 때 구분
    private static string Profile
    {
        get
        {
            if (profile != null) return profile;
            profile = Arg("-mpprofile") ?? "";
            for (int i = 0; profile.Length == 0 && i < 8; i++)
            {
                var m = new System.Threading.Mutex(true, $"WISH.NetProfile.{i}", out bool created);
                if (!created) { m.Dispose(); continue; }
                profileMutex = m;
                if (i > 0) profile = $"p{i}";
                // 구독 중복 방지
                Application.quitting -= ReleaseProfile;
                Application.quitting += ReleaseProfile;
                break;
            }
            return profile;
        }
    }

    private static void ReleaseProfile()
    {
        profileMutex?.Dispose();
        profileMutex = null;
        profile = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        NetSession.Mode = NetSession.Role.Offline;
        JoinCode = Status = "";
        Busy = false;

        var go = new GameObject("NetBootstrap");
        DontDestroyOnLoad(go);
        var bootstrap = go.AddComponent<NetBootstrap>();
        Services.Replace(bootstrap);
        bootstrap.ReadCommandLine();
    }

    private void OnDestroy() => Services.Unregister(this);

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Avatars.Clear();
        JoinCode = Status = "";
        Busy = false;
        Changed = null;
        sessionName = null;
        ReleaseProfile();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded   += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded   -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    private void OnApplicationQuit() => Shutdown();

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        host?.Tick(dt);
        client?.Tick(dt);
        if (GridManager.Instance != null) Avatars.Update(Time.deltaTime);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != InGameScene) return;
        if (client != null) StartCoroutine(TellSceneReady());
        if (hostOnLoad)
        {
            hostOnLoad = false;
            StartCoroutine(HostNextFrame());
        }
        if (NetSession.Mode != NetSession.Role.Offline) NetHud.Ensure();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.name != InGameScene) return;
        Avatars.Clear();
        if (NetSession.Mode != NetSession.Role.Offline) Shutdown();
    }

    private IEnumerator TellSceneReady()
    {
        yield return null;
        client?.OnSceneReady();
    }

    private IEnumerator HostNextFrame()
    {
        yield return null;
        HostAsync(hostPort);
    }

    public static void StartHosting() => Instance?.HostAsync(0);

    public static void Join(string code) => Instance?.JoinAsync(code, null, 0);

    public static void Leave() => Instance?.Shutdown();

    public static void SendChat(string text)
    {
        text = CleanChat(text);
        if (text.Length == 0) return;
        ShowChat(PlayerCharacter.Name, text);
        var me = PlayerTileController.Instance;
        if (me != null) ChatBubble.Show(me.transform, text, ChatBubble.SpriteTop(me.transform) + 0.08f);
        if (NetHost.Current != null) NetHost.Current.RelayChat(NetworkManager.ServerClientId, PlayerCharacter.Name, text);
        else if (NetClient.CanSend) NetWire.SendJson(NetWire.Chat, NetworkManager.ServerClientId, new ChatMsg { text = text });
    }

    public static void ShowChat(string name, string text)
        => NotificationUI.Instance?.Show($"<b>{NoParse(name)}</b>: {NoParse(text)}", ChatColor);

    public static string CleanChat(string text)
    {
        text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return text.Length > MaxChatLength ? text.Substring(0, MaxChatLength) : text;
    }

    private static string NoParse(string s) => $"<noparse>{(s ?? "").Replace("</noparse>", "")}</noparse>";

    public static void SetStatus(string text)
    {
        Status = text ?? "";
        Changed?.Invoke();
    }

    public static void RaiseChanged() => Changed?.Invoke();

    public static void SendMyLook()
    {
        if (NetHost.Current != null) NetHost.Current.OnHostLookChanged();
        else NetClient.Current?.SendLook();
    }

    public static void Fail(string reason)
    {
        if (Instance == null) return;
        bool inWorld = GridManager.Instance != null && NetSession.IsReplica;
        Instance.Shutdown();
        SetStatus(reason);
        if (!inWorld) return;
        NotificationUI.Instance?.Show($"{reason} 잠시 뒤 메인메뉴로 돌아갑니다.");
        Instance.StartCoroutine(Instance.BackToMenu());
    }

    private IEnumerator BackToMenu()
    {
        yield return new WaitForSecondsRealtime(3f);
        if (GridManager.Instance != null && NetSession.Mode == NetSession.Role.Offline) SceneManager.LoadScene(MainMenuScene);
    }

    private async void HostAsync(ushort directPort)
    {
        if (NetSession.Mode != NetSession.Role.Offline || Busy || GridManager.Instance == null) return;
        Busy = true;
        SetStatus("조인 코드 만드는 중...");
        try
        {
            await PrepareNetworkManager();
            host = new NetHost();
            NetSession.Mode = NetSession.Role.Host;
            if (directPort > 0)
            {
                transport.SetConnectionData("0.0.0.0", directPort, "0.0.0.0");
                if (!nm.StartHost()) throw new Exception($"포트 {directPort}를 열지 못했어요");
                JoinCode = $"{LocalIPv4()}:{directPort}";
            }
            else
            {
                await SignInAsync();
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true, Name = $"{PlayerCharacter.Name}의 월드" }
                    .WithRelayNetwork();
                var created = await MultiplayerService.Instance.CreateSessionAsync(options);
                if (NetSession.Mode != NetSession.Role.Host)
                {
                    _ = LeaveSession(created, isHost: true);
                    return;
                }
                session  = created;
                JoinCode = created.Code;
            }
            Busy = false;
            SetStatus("");
            Debug.Log($"[Net] 친구 초대 시작 — 조인 코드: {JoinCode}");
            NotificationUI.Instance?.Show($"친구 초대 준비 완료! 조인 코드: {JoinCode} (Esc 메뉴에서 복사)");
            NetHud.Ensure();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Net] 친구 초대를 시작하지 못했습니다: {e}");
            Shutdown();
            SetStatus($"조인 코드를 만들지 못했어요 — {Explain(e)}");
            NotificationUI.Instance?.Show(Status);
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private async void JoinAsync(string code, string ip, ushort port)
    {
        if (NetSession.Mode != NetSession.Role.Offline || Busy) return;
        Busy = true;
        SetStatus("접속하는 중...");
        try
        {
            await PrepareNetworkManager();
            GameLaunchContext.PendingCharacter = GuestLook;
            client = new NetClient(PlayerId);
            NetSession.Mode = NetSession.Role.Client;
            if (ip != null)
            {
                transport.SetConnectionData(ip, port);
                if (!nm.StartClient()) throw new Exception("연결을 시작하지 못했어요");
            }
            else
            {
                await SignInAsync();
                var joined = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                if (NetSession.Mode != NetSession.Role.Client)
                {
                    _ = LeaveSession(joined, isHost: false);
                    return;
                }
                session = joined;
            }
            if (NetSession.Mode == NetSession.Role.Client && Status == "접속하는 중...") SetStatus("호스트와 연결하는 중...");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Net] 참가하지 못했습니다: {e}");
            Shutdown();
            SetStatus($"참가하지 못했어요 — {Explain(e)}");
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private async Task PrepareNetworkManager()
    {
        if (nm == null)
        {
            var go = new GameObject("NetworkManager");
            DontDestroyOnLoad(go);
            transport = go.AddComponent<UnityTransport>();
            nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport      = transport,
                EnableSceneManagement = false,
                ConnectionApproval    = false,
            };
            nm.OnServerStarted            += RegisterHandlers;
            nm.OnClientStarted            += RegisterHandlers;
            nm.OnClientConnectedCallback  += OnConnected;
            nm.OnClientDisconnectCallback += OnDisconnected;
        }
        while (nm.ShutdownInProgress) await Task.Yield();
    }

    private static async Task SignInAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            var options = new InitializationOptions();
            if (Profile.Length > 0) options.SetProfile(Profile);
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void RegisterHandlers()
    {
        var cmm = nm.CustomMessagingManager;
        if (cmm == null) return;
        foreach (var name in NetWire.All) cmm.UnregisterNamedMessageHandler(name);

        Handle(cmm, NetWire.Hello, (id, r) => host?.OnHello(id, NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Ready, (id, _) => host?.OnReady(id));
        Handle(cmm, NetWire.Op,    (id, r) => host?.OnOp(id, NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Guest, (id, r) => host?.OnGuestData(id, NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Look,  (id, r) => host?.OnLook(id, NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Pos,   (id, r) =>
        {
            if (host != null) host.OnPosition(id, r);
            else client?.OnPositions(r);
        });
        Handle(cmm, NetWire.Chat,  (id, r) =>
        {
            if (host != null) host.OnChat(id, NetWire.ReadBytes(r));
            else client?.OnChat(NetWire.ReadBytes(r));
        });
        Handle(cmm, NetWire.Lab,   (id, r) => LabNet.Receive(id, NetWire.ReadBytes(r)));

        Handle(cmm, NetWire.Welcome,  (_, r) => client?.OnWelcome(NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Snapshot, (_, r) => client?.OnSnapshot(NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Delta,    (_, r) => client?.OnDelta(NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Give,     (_, r) => client?.OnGive(NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Roster,   (_, r) => client?.OnRoster(NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Notice,   (_, r) => client?.OnNotice(NetWire.ReadBytes(r)));
        Handle(cmm, NetWire.Storm,    (_, r) => client?.OnStorm(NetWire.ReadBytes(r)));
    }

    // 오류 나도 연결은 유지
    private static void Handle(CustomMessagingManager cmm, string name, Action<ulong, FastBufferReader> handler)
    {
        cmm.RegisterNamedMessageHandler(name, (sender, reader) =>
        {
            try { handler(sender, reader); }
            catch (Exception e) { Debug.LogError($"[Net] '{name}' 처리 중 오류: {e}"); }
        });
    }

    private void OnConnected(ulong id)
    {
        if (host != null) host.OnClientConnected(id);
        else if (client != null && id == nm.LocalClientId) client.OnConnected();
    }

    private void OnDisconnected(ulong id)
    {
        if (host != null)
        {
            if (id != NetworkManager.ServerClientId) host.OnClientDisconnected(id);
            return;
        }
        if (client != null && (id == nm.LocalClientId || id == NetworkManager.ServerClientId))
            Fail(client.WorldReady ? "호스트와 연결이 끊겼어요." : "호스트에 연결하지 못했어요.");
    }

    private void Shutdown()
    {
        client?.SendMyData();

        var s = session;
        session = null;
        if (s != null) _ = LeaveSession(s, host != null);
        if (nm != null && (nm.IsListening || nm.IsClient || nm.IsServer)) nm.Shutdown();

        host?.Dispose();
        client?.Dispose();
        host   = null;
        client = null;
        bool wasOnline = NetSession.Mode != NetSession.Role.Offline;
        NetSession.Mode = NetSession.Role.Offline;
        JoinCode = "";
        Avatars.Clear();
        NetHud.Hide();
        if (wasOnline) Changed?.Invoke();
    }

    private static async Task LeaveSession(ISession s, bool isHost)
    {
        try
        {
            if (isHost && s is IHostSession hs) await hs.DeleteAsync();
            else await s.LeaveAsync();
        }
        catch (Exception e) { Debug.Log($"[Net] 세션 정리: {e.Message}"); }
    }

    private void ReadCommandLine()
    {
        sessionName = Arg("-mpname");
        var join = Arg("-mpjoin");
        var ip   = Arg("-mpip");
        if (!string.IsNullOrEmpty(join)) JoinAsync(join, null, 0);
        else if (!string.IsNullOrEmpty(ip))
        {
            var parts = ip.Split(':');
            ushort port = parts.Length > 1 && ushort.TryParse(parts[1], out var p) ? p : DefaultPort;
            JoinAsync(null, parts[0], port);
        }
        else if (HasArg("-mphost"))
        {
            // 테스트용, 저장 안 하는 새 월드
            ushort.TryParse(Arg("-mphost"), out hostPort);
            GameLaunchContext.IsNewGame         = true;
            GameLaunchContext.LaunchedFromMenu  = false;
            GameLaunchContext.PendingWorldSize  = WorldSize.Small;
            GameLaunchContext.PendingWorldTheme = WorldTheme.Wasteland;
            GameLaunchContext.PendingWorldSeed  = UnityEngine.Random.Range(1, int.MaxValue);
            hostOnLoad = true;
            if (HasArg("-mplab"))
            {
                GameLaunchContext.PendingCharacter = GuestLook;
                Lab.LaunchPrepared = true;
                SceneManager.LoadScene(Lab.SceneName);
            }
            else SceneManager.LoadScene(InGameScene);
        }
    }

    private static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
    }

    private static string LocalIPv4()
    {
        try
        {
            foreach (var a in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return a.ToString();
        }
        catch (Exception) {  }
        return "127.0.0.1";
    }

    private static string Explain(Exception e)
    {
        string m = e.Message ?? "";
        if (m.Contains("not found") || m.Contains("NotFound") || m.Contains("invalid join code") || m.Contains("InvalidJoinCode"))
            return "조인 코드를 다시 확인해 주세요.";
        if (m.Contains("full") || m.Contains("Full")) return "방이 가득 찼어요.";
        if (m.Contains("project") || m.Contains("Project") || m.Contains("Forbidden") || m.Contains("403"))
            return "Unity 클라우드 프로젝트 연결 · Relay/Lobby 서비스 설정을 확인해 주세요.";
        if (m.Contains("network") || m.Contains("Network") || m.Contains("resolve")) return "인터넷 연결을 확인해 주세요.";
        return m.Length > 80 ? m.Substring(0, 80) + "…" : m;
    }
}
