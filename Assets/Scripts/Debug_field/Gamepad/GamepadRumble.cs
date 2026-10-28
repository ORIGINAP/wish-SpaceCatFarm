using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Haptic
{
    UiMove, UiEdge, UiClick, UiOpen, UiClose, UiTab, UiGrab, UiDrop,
    Confirm, Error,
    Place, Plant, Harvest, MineHit, MineBreak, Pickup, DismantleDone,
    Equip, Store, Craft, Trade, Upgrade, Trash,
    QuestComplete, Save, GameStart, StormWarning, StormHit, Boost,
}

public static class GamepadRumble
{
    private struct Voice
    {
        public float start, duration;
        public float low0, low1, high0, high1;
        public bool  shake;
    }

    private static readonly List<Voice> voices = new();
    private static float holdLow, holdHigh, holdUntil;
    private static float previewUntil;
    private static float lastPickup = -1f;

    private static Gamepad sentDevice;
    private static float   sentLow = -1f, sentHigh = -1f;
    private static float   lastSendTime = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        voices.Clear();
        holdLow = holdHigh = holdUntil = 0f;
        previewUntil = 0f;
        lastPickup = -1f;
        sentDevice = null;
        sentLow = sentHigh = -1f;
        lastSendTime = -1f;
    }

    // 미세 진동 남는 것 방지
    private const float Deadband = 0.02f;
    // 무선 패드 명령 밀림 방지
    private const float MinSendInterval = 1f / 60f;
    // 놓친 멈춤 명령 대비
    private const float RefreshInterval = 0.25f;

    private static bool CanRumble =>
        Pad.Active && Gamepad.current != null && GameSettings.VibrationStrength > 0.001f && Application.isFocused;

    public static void Play(Haptic h, float scale = 1f)
    {
        if (!CanRumble) return;
        float now = Time.unscaledTime;
        switch (h)
        {
            case Haptic.UiMove:        Add(0f,    0.025f, 0f,    0.12f, scale); break;
            case Haptic.UiEdge:        Add(0f,    0.05f,  0.20f, 0.04f, scale); break;
            case Haptic.UiClick:       Add(0f,    0.035f, 0f,    0.16f, scale); break;
            case Haptic.UiOpen:        Add(0f,    0.06f,  0.08f, 0.18f, scale, decay: true); break;
            case Haptic.UiClose:       Add(0f,    0.045f, 0.06f, 0.12f, scale, decay: true); break;
            case Haptic.UiTab:         Add(0f,    0.045f, 0.06f, 0.20f, scale); break;
            case Haptic.UiGrab:        Add(0f,    0.05f,  0.12f, 0.22f, scale); break;
            case Haptic.UiDrop:        Add(0f,    0.06f,  0.18f, 0.16f, scale, decay: true); break;
            case Haptic.Confirm:       Add(0f,    0.07f,  0.08f, 0.32f, scale); break;
            case Haptic.Error:         Add(0f,    0.05f,  0.30f, 0.50f, scale);
                                       Add(0.10f, 0.05f,  0.30f, 0.50f, scale); break;
            case Haptic.Place:         Add(0f,    0.08f,  0.38f, 0.22f, scale, decay: true); break;
            case Haptic.Plant:         Add(0f,    0.05f,  0.05f, 0.26f, scale); break;
            case Haptic.Harvest:       Add(0f,    0.06f,  0.18f, 0.40f, scale);
                                       Add(0.09f, 0.06f,  0.10f, 0.32f, scale); break;
            case Haptic.MineHit:       Add(0f,    0.07f,  0.30f, 0.50f, scale, decay: true); break;
            case Haptic.MineBreak:     Add(0f,    0.24f,  0.85f, 0.60f, scale, decay: true); break;
            case Haptic.Pickup:
                if (now - lastPickup < 0.09f) return;
                lastPickup = now;
                Add(0f, 0.03f, 0f, 0.14f, scale); break;
            case Haptic.DismantleDone: Add(0f,    0.20f,  0.75f, 0.45f, scale, decay: true); break;
            case Haptic.Equip:         Add(0f,    0.08f,  0.22f, 0.36f, scale); break;
            case Haptic.Store:         Add(0f,    0.08f,  0.26f, 0.30f, scale, decay: true); break;
            case Haptic.Craft:         Add(0f,    0.07f,  0.15f, 0.42f, scale);
                                       Add(0.11f, 0.10f,  0.32f, 0.55f, scale, decay: true); break;
            case Haptic.Trade:         Add(0f,    0.06f,  0.10f, 0.36f, scale); break;
            case Haptic.Upgrade:       Add(0f,    0.10f,  0.30f, 0.50f, scale);
                                       Add(0.13f, 0.22f,  0.55f, 0.75f, scale, decay: true); break;
            case Haptic.Trash:         Add(0f,    0.09f,  0.30f, 0.20f, scale, decay: true); break;
            case Haptic.QuestComplete: Add(0f,    0.08f,  0.15f, 0.35f, scale);
                                       Add(0.12f, 0.08f,  0.30f, 0.50f, scale);
                                       Add(0.24f, 0.30f,  0.60f, 0.80f, scale, decay: true); break;
            case Haptic.Save:          Add(0f,    0.06f,  0.05f, 0.30f, scale); break;
            case Haptic.GameStart:     Add(0f,    0.10f,  0.30f, 0.50f, scale);
                                       Add(0.14f, 0.35f,  0.65f, 0.80f, scale, decay: true); break;
            case Haptic.StormWarning:  Add(0f,    0.35f,  0.40f, 0.10f, scale);
                                       Add(0.55f, 0.35f,  0.40f, 0.10f, scale); break;
            case Haptic.StormHit:      Add(0f,    0.60f,  1.00f, 0.70f, scale, decay: true); break;
            case Haptic.Boost:         Add(0f,    0.08f,  0.10f, 0.40f, scale);
                                       Add(0.11f, 0.16f,  0.25f, 0.60f, scale, decay: true); break;
        }
    }

    public static void Hold(float low, float high)
    {
        if (!CanRumble) return;
        float now = Time.unscaledTime;
        if (now >= holdUntil) holdLow = holdHigh = 0f;
        holdLow   = Mathf.Max(holdLow, low);
        holdHigh  = Mathf.Max(holdHigh, high);
        holdUntil = now + 0.1f;
    }

    public static void Shake(float duration, float startMagnitude, float endMagnitude)
    {
        voices.RemoveAll(v => v.shake);
        if (!CanRumble || duration <= 0f) return;
        float k0 = Mathf.Clamp01(startMagnitude / 0.35f);
        float k1 = Mathf.Clamp01(endMagnitude / 0.35f);
        if (Mathf.Approximately(startMagnitude, endMagnitude)) k1 *= 0.35f;
        voices.Add(new Voice
        {
            start = Time.unscaledTime, duration = duration, shake = true,
            low0  = k0,         low1  = k1,
            high0 = k0 * 0.55f, high1 = k1 * 0.55f,
        });
    }

    public static void Preview()
    {
        if (Gamepad.current == null) return;
        previewUntil = Time.unscaledTime + 0.25f;
        Add(0f, 0.12f, 0.35f, 0.55f, 1f, decay: true);
    }

    public static void OnSfx(SfxId id)
    {
        switch (id)
        {
            case SfxId.UiOpen:        Play(Haptic.UiOpen);        break;
            case SfxId.UiClose:       Play(Haptic.UiClose);       break;
            case SfxId.UiConfirm:     Play(Haptic.Confirm);       break;
            case SfxId.UiError:       Play(Haptic.Error);         break;
            case SfxId.Craft:         Play(Haptic.Craft);         break;
            case SfxId.Buy:
            case SfxId.Sell:          Play(Haptic.Trade);         break;
            case SfxId.Upgrade:       Play(Haptic.Upgrade);       break;
            case SfxId.Equip:         Play(Haptic.Equip);         break;
            case SfxId.StoreItem:
            case SfxId.FuelInsert:    Play(Haptic.Store);         break;
            case SfxId.Pickup:        Play(Haptic.Pickup);        break;
            case SfxId.Dismantle:     Play(Haptic.Trash);         break;
            case SfxId.QuestComplete: Play(Haptic.QuestComplete); break;
            case SfxId.Save:          Play(Haptic.Save);          break;
            case SfxId.GameStart:     Play(Haptic.GameStart);     break;
            case SfxId.StormWarning:  Play(Haptic.StormWarning);  break;
            case SfxId.StormHit:      Play(Haptic.StormHit);      break;
            case SfxId.Boost:         Play(Haptic.Boost);         break;
        }
    }

    public static void StopAll()
    {
        voices.Clear();
        holdUntil = previewUntil = 0f;
        Send(0f, 0f, force: true);
    }

    internal static void Tick()
    {
        float now  = Time.unscaledTime;
        float low  = 0f, high = 0f;
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            var v = voices[i];
            if (now >= v.start + v.duration) { voices.RemoveAt(i); continue; }
            if (now < v.start) continue;
            float t = (now - v.start) / v.duration;
            low  = Mathf.Max(low,  Mathf.Lerp(v.low0,  v.low1,  t));
            high = Mathf.Max(high, Mathf.Lerp(v.high0, v.high1, t));
        }
        if (now < holdUntil)
        {
            low  = Mathf.Max(low,  holdLow);
            high = Mathf.Max(high, holdHigh);
        }

        bool  allowed  = CanRumble || (now < previewUntil && Gamepad.current != null);
        float strength = allowed ? GameSettings.VibrationStrength : 0f;
        Send(Mathf.Clamp01(low * strength), Mathf.Clamp01(high * strength));
    }

    private static void Add(float delay, float duration, float low, float high, float scale, bool decay = false)
    {
        low  *= scale;
        high *= scale;
        voices.Add(new Voice
        {
            start = Time.unscaledTime + delay, duration = duration,
            low0  = low,  low1  = decay ? 0f : low,
            high0 = high, high1 = decay ? 0f : high,
        });
    }

    private static void Send(float low, float high, bool force = false)
    {
        if (low  < Deadband) low  = 0f;
        if (high < Deadband) high = 0f;

        var g = Gamepad.current;
        if (g != sentDevice)
        {
            if (sentDevice != null && sentDevice.added) sentDevice.ResetHaptics();
            sentDevice = g;
            sentLow = sentHigh = -1f;
        }
        if (g == null) return;

        float now     = Time.unscaledTime;
        bool  off     = low == 0f && high == 0f;
        bool  wasOff  = sentLow == 0f && sentHigh == 0f;
        bool  changed = off != wasOff || Mathf.Abs(low - sentLow) >= 0.01f || Mathf.Abs(high - sentHigh) >= 0.01f;
        if (!force)
        {
            if (!changed && now - lastSendTime < RefreshInterval) return;
            if (changed && !off && !wasOff && now - lastSendTime < MinSendInterval) return;
        }
        g.SetMotorSpeeds(low, high);
        sentLow      = low;
        sentHigh     = high;
        lastSendTime = now;
    }

    public static Vector2 CurrentMotors => new(Mathf.Max(0f, sentLow), Mathf.Max(0f, sentHigh));
}
