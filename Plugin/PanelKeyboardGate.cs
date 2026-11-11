using HarmonyLib;
using SafeDialogueTools.Core;
using UnityEngine;

namespace SafeDialogueTools;

internal static class PanelKeyGate
{
    private static bool readingOwnInput;

    internal static int ConsumedFrame = -1;

    internal static bool IsPanelKey(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        return key == Plugin.PanelKey.Value ||
               (Plugin.ClosePanelKey.Value != KeyCode.None && key == Plugin.ClosePanelKey.Value);
    }

    internal static bool IsPanelKeyName(string keyName) =>
        string.Equals(keyName, Plugin.PanelKey.Value.ToString(), StringComparison.OrdinalIgnoreCase) ||
        (Plugin.ClosePanelKey.Value != KeyCode.None &&
         string.Equals(keyName, Plugin.ClosePanelKey.Value.ToString(), StringComparison.OrdinalIgnoreCase));

    internal static bool ShouldBlock(KeyCode key) =>
        PanelKeySuppression.Blocks(IsPanelKey(key), CommandPanelState.Open, ConsumedFrame, Time.frameCount);

    internal static bool ShouldBlockName(string keyName) =>
        PanelKeySuppression.Blocks(IsPanelKeyName(keyName), CommandPanelState.Open, ConsumedFrame, Time.frameCount);

    internal static bool ShouldBlockCancelButton() =>
        PanelKeySuppression.Blocks(true, CommandPanelState.Open, ConsumedFrame, Time.frameCount);

    internal static void Consume() => ConsumedFrame = Time.frameCount;

    internal static bool ReadKeyDown(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        var previous = readingOwnInput;
        readingOwnInput = true;
        try { return Input.GetKeyDown(key); }
        finally { readingOwnInput = previous; }
    }

    internal static bool ReadingOwnInput => readingOwnInput;
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) })]
internal static class PanelKeyDownGate
{
    private static bool Prefix(KeyCode key, ref bool __result)
    {
        if (PanelKeyGate.ReadingOwnInput || !PanelKeyGate.ShouldBlock(key)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) })]
internal static class PanelKeyHeldGate
{
    private static bool Prefix(KeyCode key, ref bool __result)
    {
        if (PanelKeyGate.ReadingOwnInput || !PanelKeyGate.ShouldBlock(key)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(KeyCode) })]
internal static class PanelKeyUpGate
{
    private static bool Prefix(KeyCode key, ref bool __result)
    {
        if (PanelKeyGate.ReadingOwnInput || !PanelKeyGate.ShouldBlock(key)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(string) })]
internal static class PanelKeyNameDownGate
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (PanelKeyGate.ReadingOwnInput || !PanelKeyGate.ShouldBlockName(__0)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKey), new[] { typeof(string) })]
internal static class PanelKeyNameHeldGate
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (PanelKeyGate.ReadingOwnInput || !PanelKeyGate.ShouldBlockName(__0)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKeyUp), new[] { typeof(string) })]
internal static class PanelKeyNameUpGate
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (PanelKeyGate.ReadingOwnInput || !PanelKeyGate.ShouldBlockName(__0)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetButtonDown))]
internal static class PanelCancelButtonDownGate
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (!IsCancelButton(__0) || !PanelKeyGate.ShouldBlockCancelButton()) return true;
        __result = false;
        return false;
    }

    private static bool IsCancelButton(string buttonName) =>
        string.Equals(buttonName, "Cancel", StringComparison.OrdinalIgnoreCase);
}

[HarmonyPatch(typeof(Input), nameof(Input.GetButton))]
internal static class PanelCancelButtonHeldGate
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (!string.Equals(__0, "Cancel", StringComparison.OrdinalIgnoreCase) ||
            !PanelKeyGate.ShouldBlockCancelButton()) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetButtonUp))]
internal static class PanelCancelButtonUpGate
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (!string.Equals(__0, "Cancel", StringComparison.OrdinalIgnoreCase) ||
            !PanelKeyGate.ShouldBlockCancelButton()) return true;
        __result = false;
        return false;
    }
}
