using System;
using UnityEngine;

public static class GameStateManager
{
    public static bool IsUIMode { get; private set; }

    public static event Action<bool> OnUIModeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsUIMode = false;
        OnUIModeChanged = null;
    }

    public static void SetUIMode(bool isUIMode)
    {
        if (IsUIMode == isUIMode)
        {
            return;
        }

        IsUIMode = isUIMode;
        OnUIModeChanged?.Invoke(IsUIMode);
    }
}
