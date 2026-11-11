namespace BeastSagaBetter.Core;

public static class CheatPolicy
{
    public static bool ShouldFinishFishing(bool enabled, bool fishingActive, double progressMax) =>
        enabled && fishingActive && progressMax > 0;

    public static bool ShouldSkipFishingCountdown(bool enabled, bool countdownPlaying) =>
        enabled && countdownPlaying;
}
