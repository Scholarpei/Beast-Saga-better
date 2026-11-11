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
