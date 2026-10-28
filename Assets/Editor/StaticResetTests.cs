using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;

// 도메인 리로드 끈 상태 대비 static 초기화 검사
public class StaticResetTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                       .Select(f => (OpCode)f.GetValue(null))
                       .ToDictionary(op => op.Value);

    private static Assembly Game => typeof(Services).Assembly;

    [Test]
    public void EveryMutableStatic_IsResetWhenPlayStarts()
    {
        var touched = new HashSet<string>();
        foreach (var reset in ResetMethods())
            Walk(reset, touched, new HashSet<MethodBase>(), depth: 3, top: true, subscriptions: null);

        var missing = new List<string>();
        foreach (var type in GameTypes())
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!field.IsLiteral && !field.IsInitOnly && !touched.Contains(Key(field)))
                    missing.Add(Key(field));

        Assert.IsEmpty(missing, "Play를 시작할 때 비우지 않는 static 필드 (같은 클래스에 " +
                                "[RuntimeInitializeOnLoadMethod(SubsystemRegistration)] 초기화를 두세요):\n" + string.Join("\n", missing));
    }

    [Test]
    public void ResetMethods_DoNotSubscribeToStaticEvents()
    {
        var subscriptions = new List<string>();
        foreach (var reset in ResetMethods())
            Walk(reset, new HashSet<string>(), new HashSet<MethodBase>(), depth: 0, top: true, subscriptions);

        Assert.IsEmpty(subscriptions, "SubsystemRegistration 초기화 안의 static 이벤트 구독 (BeforeSceneLoad 이후로 옮기세요):\n" +
                                      string.Join("\n", subscriptions));
    }

    private static IEnumerable<Type> GameTypes()
        => Game.GetTypes().Where(t => !t.ContainsGenericParameters && !IsCompilerGenerated(t));

    private static bool IsCompilerGenerated(Type t)
    {
        for (; t != null; t = t.DeclaringType)
            if (t.IsDefined(typeof(CompilerGeneratedAttribute), false)) return true;
        return false;
    }

    private static IEnumerable<MethodInfo> ResetMethods()
    {
        foreach (var type in Game.GetTypes())
            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var attr = m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attr != null && attr.loadType == RuntimeInitializeLoadType.SubsystemRegistration) yield return m;
            }
    }

    private static string Key(FieldInfo f) => f.DeclaringType?.FullName + "." + f.Name;

    // IL 훑어서 리셋되는 필드 수집
    private static void Walk(MethodBase m, HashSet<string> touched, HashSet<MethodBase> seen, int depth, bool top, List<string> subscriptions)
    {
        if (m == null || !seen.Add(m)) return;
        var il = m.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return;

        var typeArgs   = m.DeclaringType != null && m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
        var methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
        for (int i = 0; i < il.Length;)
        {
            short value = il[i++];
            if (value == 0xFE) value = (short)(0xFE00 | il[i++]);
            if (!OpCodesByValue.TryGetValue(value, out var op)) return;

            int operand = i;
            i += op.OperandType switch
            {
                OperandType.InlineNone                                                                     => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar                                                                      => 2,
                OperandType.InlineI8 or OperandType.InlineR                                                => 8,
                OperandType.InlineSwitch                                                                   => 4 + 4 * BitConverter.ToInt32(il, i),
                _                                                                                          => 4,
            };

            if (op == OpCodes.Stsfld || op == OpCodes.Ldsflda || (top && op == OpCodes.Ldsfld))
            {
                var field = Resolve(() => m.Module.ResolveField(BitConverter.ToInt32(il, operand), typeArgs, methodArgs));
                if (field != null) touched.Add(Key(field));
            }
            else if (op == OpCodes.Call || op == OpCodes.Callvirt)
            {
                var callee = Resolve(() => m.Module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArgs, methodArgs));
                if (callee == null) continue;
                if (subscriptions != null && callee.IsStatic && callee.IsSpecialName && callee.Name.StartsWith("add_"))
                    subscriptions.Add($"{m.DeclaringType?.FullName}.{m.Name} → {callee.DeclaringType?.Name}.{callee.Name.Substring(4)}");
                if (depth > 0 && callee.Module == m.Module) Walk(callee, touched, seen, depth - 1, top: false, subscriptions);
            }
        }
    }

    private static T Resolve<T>(Func<T> resolve) where T : class
    {
        try { return resolve(); }
        catch (ArgumentException) { return null; }
    }
}
