namespace SafeDialogueTools.Core;

public static class CheatPolicy
{
    public static bool ShouldFinishFishing(bool enabled, bool fishingActive, double progressMax) =>
        enabled && fishingActive && progressMax > 0;
}
