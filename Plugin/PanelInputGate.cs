using HarmonyLib;
using SafeDialogueTools.Core;
using UnityEngine;

namespace SafeDialogueTools;

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButton))]
internal static class PanelMouseButtonGate
{
    private static bool Prefix(ref bool __result)
    {
        if (!CommandPanelState.Open || !CommandPanelState.IsPointerBlocked(Input.mousePosition.x, Input.mousePosition.y)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonDown))]
internal static class PanelMouseButtonDownGate
{
    private static bool Prefix(ref bool __result)
    {
        if (!CommandPanelState.Open || !CommandPanelState.IsPointerBlocked(Input.mousePosition.x, Input.mousePosition.y)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetMouseButtonUp))]
internal static class PanelMouseButtonUpGate
{
    private static bool Prefix(ref bool __result)
    {
        if (!CommandPanelState.Open || !CommandPanelState.IsPointerBlocked(Input.mousePosition.x, Input.mousePosition.y)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetAxis))]
internal static class PanelMouseWheelAxisGate
{
    private static void Postfix(string axisName, ref float __result)
    {
        if (__result == 0 || !CommandPanelState.ShouldBlockMouseWheel(axisName, Input.mousePosition.x, Input.mousePosition.y)) return;
        __result = 0;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw))]
internal static class PanelMouseWheelRawAxisGate
{
    private static void Postfix(string axisName, ref float __result)
    {
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
