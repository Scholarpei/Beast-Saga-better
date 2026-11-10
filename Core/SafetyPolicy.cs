namespace SafeDialogueTools.Core;

public enum LoadRoute { Original, Isolated, Reject }

public static class SafetyPolicy
{
    public static bool CanOperate(bool verifiedTestCopy, bool enabled, bool productionAuthorized = false) =>
        verifiedTestCopy ? enabled : productionAuthorized && enabled;
    public static LoadRoute Route(bool pending, bool loading, bool faulted, int index) =>
        !pending ? LoadRoute.Original : loading && !faulted && index == 1 ? LoadRoute.Isolated : LoadRoute.Reject;
}
