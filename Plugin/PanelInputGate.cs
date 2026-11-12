using HarmonyLib;
using BeastSagaBetter.Core;
using UnityEngine;

namespace BeastSagaBetter;

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton))]
internal static class PanelMouseButtonGate
{
    private static bool Prefix(ref bool __result)
    {
        if (!MapTeleportController.ReadingOwnInput && MapTeleportMode.Active ||
            CommandPanelState.Open && CommandPanelState.IsPointerBlocked(Input.mousePosition.x, Input.mousePosition.y))
        {
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown))]
internal static class PanelMouseButtonDownGate
{
    private static bool Prefix(ref bool __result)
    {
        if (!MapTeleportController.ReadingOwnInput && MapTeleportMode.Active ||
            CommandPanelState.Open && CommandPanelState.IsPointerBlocked(Input.mousePosition.x, Input.mousePosition.y))
        {
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp))]
internal static class PanelMouseButtonUpGate
{
    private static bool Prefix(ref bool __result)
    {
        if (!MapTeleportController.ReadingOwnInput && MapTeleportMode.Active ||
            CommandPanelState.Open && CommandPanelState.IsPointerBlocked(Input.mousePosition.x, Input.mousePosition.y))
        {
            __result = false;
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetAxis))]
internal static class PanelMouseWheelAxisGate
{
    private static void Postfix(string axisName, ref float __result)
    {
        if (__result != 0 && MapTeleportMode.Active && MapTeleportAxisPolicy.IsMovementAxis(axisName)) __result = 0;
        if (__result == 0 || !CommandPanelState.ShouldBlockMouseWheel(axisName, Input.mousePosition.x, Input.mousePosition.y)) return;
        __result = 0;
    }
}

internal static class MapTeleportAxisPolicy
{
    public static bool IsMovementAxis(string axisName) =>
        axisName.Contains("Horizontal", StringComparison.OrdinalIgnoreCase) ||
        axisName.Contains("Vertical", StringComparison.OrdinalIgnoreCase);
}

[HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw))]
internal static class PanelMouseWheelRawAxisGate
{
    private static void Postfix(string axisName, ref float __result)
    {
        if (__result != 0 && MapTeleportMode.Active && MapTeleportAxisPolicy.IsMovementAxis(axisName)) __result = 0;
        if (__result == 0 || !CommandPanelState.ShouldBlockMouseWheel(axisName, Input.mousePosition.x, Input.mousePosition.y)) return;
        __result = 0;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.mouseScrollDelta), MethodType.Getter)]
internal static class PanelMouseScrollDeltaGate
{
    private static void Postfix(ref Vector2 __result)
    {
        if (__result.y == 0 || !CommandPanelState.ShouldBlockMouseWheel("Mouse ScrollWheel", Input.mousePosition.x, Input.mousePosition.y)) return;
        __result.y = 0;
    }
}
