using System;
using UnityEngine;

// 실험 베이스 클래스
public abstract class LabExperiment : MonoBehaviour
{
    [Header("실험실")]
    [Tooltip("실험실을 열 때 켜 둘지")]
    [SerializeField] private bool onAtStart = true;

    // 바꾸면 멀티 버전 어긋남
    public abstract string Id { get; }

    public abstract string Title { get; }

    public abstract string Summary { get; }

    public virtual string Tags => "싱글 · 멀티";

    public virtual Color Accent => CuteUIKit.Strawberry;

    public virtual string Status => "";

    public bool IsOn { get; private set; }

    private bool started;

    [Serializable] private class OnMsg { public bool on; }

    protected virtual void Update()
    {
        if (!Lab.WorldReady) return;
        EnsureStarted();
        if (!IsOn) return;
        if (Lab.IsAuthority) AuthorityTick();
        LocalTick();
    }

    protected virtual void OnDestroy() => Cleanup();

    private void EnsureStarted()
    {
        if (started || !Lab.WorldReady) return;
        started = true;
        OnWorldReady();
        if (Lab.IsAuthority) SetOn(onAtStart);
    }

    public void RequestSetOn(bool on)
    {
        if (Lab.IsAuthority) SetOn(on);
        else LabNet.ToHost(Id, "setOn", JsonUtility.ToJson(new OnMsg { on = on }));
    }

    private void SetOn(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        Switched(on);
        Broadcast("on", new OnMsg { on = on });
    }

    private void Switched(bool on)
    {
        if (!on) Cleanup();
        OnSwitched(on);
        LabPanelUI.MarkDirty();
    }

    internal void HandleRequest(ulong from, string kind, string json)
    {
        EnsureStarted();
        if (!started) return;
        if (kind == "setOn") { SetOn(Read<OnMsg>(json)?.on ?? IsOn); return; }
        if (IsOn) OnRequest(from, kind, json);
    }

    internal void HandleEvent(string kind, string json)
    {
        EnsureStarted();
        if (!started) return;
        if (kind == "on")
        {
            bool on = Read<OnMsg>(json)?.on ?? false;
            if (on == IsOn) return;
            IsOn = on;
            Switched(on);
            return;
        }
        if (IsOn) OnEvent(kind, json);
    }

    internal void SendFullState(ulong peer)
    {
        if (!started) return;
        SendTo(peer, "on", new OnMsg { on = IsOn });
        if (IsOn) SendState(peer);
    }

    protected virtual void OnWorldReady() { }

    protected virtual void OnSwitched(bool on) { }

    protected virtual void AuthorityTick() { }

    protected virtual void LocalTick() { }

    protected virtual void OnRequest(ulong from, string kind, string json) { }

    protected virtual void OnEvent(string kind, string json) { }

    protected virtual void SendState(ulong peer) { }

    protected virtual void Cleanup() { }

    public virtual void BuildControls(Transform row) { }

    protected void Broadcast(string kind, object payload = null, bool reliable = true, ulong except = ulong.MaxValue)
    {
        if (NetSession.IsHost) LabNet.ToAll(Id, kind, payload != null ? JsonUtility.ToJson(payload) : "", reliable, except);
    }

    protected void SendTo(ulong peer, string kind, object payload = null)
    {
        if (NetSession.IsHost) LabNet.ToPeer(peer, Id, kind, payload != null ? JsonUtility.ToJson(payload) : "");
    }

    // 참가자는 호스트로 부탁
    protected void Request(string kind, object payload = null)
    {
        string json = payload != null ? JsonUtility.ToJson(payload) : "";
        if (Lab.IsAuthority) HandleRequest(Lab.LocalId, kind, json);
        else LabNet.ToHost(Id, kind, json);
    }

    protected static T Read<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<T>(json); }
        catch (Exception) { return null; }
    }

    protected static bool HotkeysBlocked => TextInputFocus.Active || PlayerTileController.IsPauseMenuOpen();
}
