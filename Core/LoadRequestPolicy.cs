namespace SafeDialogueTools.Core;

public static class LoadRequestPolicy
{
    public const float DefaultAbandonAfter = 10f;

    public static bool AllowContinue(
        bool testMode,
        bool requested,
        bool nativeLoadStarted,
        float pendingSeconds,
        float abandonAfter = DefaultAbandonAfter)
    {
        if (!testMode) return true;
        if (nativeLoadStarted) return false;
        if (!requested) return true;
        return abandonAfter > 0 && pendingSeconds > abandonAfter;
    }

    public static bool MenuReady(
        bool indexReady,
        bool transitionPlaying,
        bool buttonEnabled,
        bool buttonVisible,
        bool buttonTouchable) =>
        indexReady && !transitionPlaying && buttonEnabled && buttonVisible && buttonTouchable;
}
