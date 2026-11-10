using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SafeDialogueTools;

[HarmonyPatch]
internal static class SandboxPrefs
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(PlayerPrefs).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.Name is "SetInt" or "SetString" or "SetFloat" or "DeleteKey" or "DeleteAll" or "Save");

    private static bool Prefix() => !Plugin.TestMode;
}

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.RestartAppIfNecessary))]
internal static class SandboxSteamRestart
{
    private static bool Prefix(ref bool __result)
    {
        if (!Plugin.TestMode) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Steamworks.SteamAPI), nameof(Steamworks.SteamAPI.Init))]
internal static class SandboxSteamInit
{
    private static bool Prefix(ref bool __result)
    {
        if (!Plugin.TestMode) return true;
        __result = false;
        return false;
    }
}
