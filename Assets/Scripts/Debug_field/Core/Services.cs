using System;
using System.Collections.Generic;
using UnityEngine;

// 싱글톤 등록부
public static class Services
{
    private static readonly List<Action> clears = new();
    private static readonly List<Func<object>> reads = new();

    private static class Slot<T> where T : class
    {
        public static T Value;

        static Slot()
        {
            clears.Add(() => Value = null);
            reads.Add(() => Value);
        }
    }

    public static T Get<T>() where T : class => Slot<T>.Value;

    public static bool Register<T>(T service) where T : class
    {
        var current = Slot<T>.Value;
        if (current != null && !ReferenceEquals(current, service) && Alive(current)) return false;
        Slot<T>.Value = service;
        return true;
    }

    public static void Replace<T>(T service) where T : class => Slot<T>.Value = service;

    public static void Unregister<T>(T service) where T : class
    {
        if (service != null && ReferenceEquals(Slot<T>.Value, service)) Slot<T>.Value = null;
    }

    public static int Count
    {
        get
        {
            int n = 0;
            foreach (var read in reads)
                if (read() is { } s && Alive(s)) n++;
            return n;
        }
    }

    private static bool Alive(object service) => service is not UnityEngine.Object unityObject || unityObject != null;

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetStatics()
    {
        foreach (var clear in clears) clear();
    }
}
