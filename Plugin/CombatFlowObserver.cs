using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace BeastSagaBetter;

// Observe native iterator lifetimes, including yielding resumes. A factory postfix alone
// cannot tell whether native death, action or turn-transition work is still executing.
internal static class CombatFlowObserver
{
    private static readonly Dictionary<IntPtr, string> active = new();
    internal static bool Ready { get; private set; }
    internal static string Status { get; private set; } = "尚未安装";
    internal static long Version { get; private set; }
    internal static bool Resolving => active.Values.Any(name => name.Contains("EnemyHurt") ||
        name.Contains("DieEnemy") || name.Contains("DieAnim") || name.Contains("OneBattleUnitOver") || name.Contains("UISeque"));
    internal static bool Acting => active.Count != 0;
    internal static string[] Active => active.Values.ToArray();
    internal static void Reset() { active.Clear(); Version++; }

    internal static void Initialize(Harmony harmony)
    {
        try
        {
            var required = new[] { "EnemyHurt", "DieEnemy", "OneBattleUnitOver", "UISeque", "EnterBattleAttack",
                "PlayerAttackInScence", "PlayerCallUnitUseKangFu", "PlayerBattleUseKangFu", "PlayerBattleUseItem",
                "ForAIActionAndMove", "ForPlayerAIActionAndMove" };
            var nested = new[] { typeof(BattleManager), typeof(BattleUnit) }
                .SelectMany(type => type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                .Where(type => required.Any(name => type.Name.StartsWith("_" + name + "_", StringComparison.Ordinal)) ||
                    type.Name.StartsWith("_DieAnim_", StringComparison.Ordinal) ||
                    type.Name.StartsWith("_KangFuEnemyHurtLogic_", StringComparison.Ordinal) ||
                    type.Name.StartsWith("_UseItemLogic_", StringComparison.Ordinal) ||
                    type.Name.StartsWith("_AIBattle", StringComparison.Ordinal) ||
                    type.Name.StartsWith("_PlayerAttackAnim", StringComparison.Ordinal) ||
                    type.Name.StartsWith("_AIAttackAnim", StringComparison.Ordinal)).ToArray();
            foreach (var name in required)
                if (!nested.Any(type => type.Name.StartsWith("_" + name + "_", StringComparison.Ordinal)))
                    throw new MissingMethodException("缺少战斗迭代器 " + name);
            foreach (var type in nested)
            {
                var method = AccessTools.Method(type, "MoveNext", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(CombatFlowObserver), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(CombatFlowObserver), nameof(After)),
                    finalizer: new HarmonyMethod(typeof(CombatFlowObserver), nameof(Failed)));
                if (Harmony.GetPatchInfo(method)?.Owners.Contains(harmony.Id) != true)
                    throw new InvalidOperationException("战斗迭代器挂钩未生效：" + type.Name);
            }
            // FairyGUI dispatches clicks independently of Unity Input. Block only user command
            // callbacks; native simulation, animation, timers and settlement keep running.
            var callbacks = typeof(BattleUILogicResident).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.DeclaringType == typeof(BattleUILogicResident) && method.Name.StartsWith("OnClick", StringComparison.Ordinal) &&
                    // In this fingerprint OnClickEscReturn is an empty ret at RVA 0x37b120,
                    // aliased to 1877 unrelated methods. Detouring it corrupts unrelated calls.
                    method.Name != "OnClickEscReturn").ToArray();
            if (callbacks.Length == 0) throw new MissingMethodException("战斗 UI 操作回调");
            foreach (var callback in callbacks)
                harmony.Patch(callback, prefix: new HarmonyMethod(typeof(CombatFlowObserver), nameof(AllowUiCommand)));
            Ready = true; Status = $"已观察 {nested.Length} 个原生迭代器与 {callbacks.Length} 个战斗输入回调";
        }
        catch (Exception error)
        {
            Ready = false; Status = error.Message;
            Plugin.Logger.LogError("战斗修改已停用：流程观察器安装失败：" + error);
        }
    }

    private static bool AllowUiCommand() => !CombatEditor.BlockGameInput;
    private static void Before(Il2CppObjectBase __instance, MethodBase __originalMethod)
    {
        if (!active.ContainsKey(__instance.Pointer))
        { active[__instance.Pointer] = __originalMethod.DeclaringType?.Name ?? "未知战斗流程"; Version++; }
    }
    private static void After(Il2CppObjectBase __instance, bool __result)
    {
        if (!__result)
        {
            CombatEditor.IteratorFinished(__instance.Pointer);
            if (active.Remove(__instance.Pointer)) Version++;
        }
    }
    private static Exception? Failed(Il2CppObjectBase __instance, Exception? __exception)
    {
        if (__exception != null)
        {
            active.Remove(__instance.Pointer); Version++;
            Ready = false; Status = "原生战斗流程异常，本次运行已禁用战斗修改";
            Plugin.Logger.LogError(Status + "：" + __exception);
        }
        return __exception;
    }
}
