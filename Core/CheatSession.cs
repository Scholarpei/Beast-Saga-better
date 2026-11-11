namespace BeastSagaBetter.Core;

public static class CheatSessionState
{
    public static bool PreEditCheckpointTaken { get; private set; }

    public static bool TryBeginPreEditCheckpoint(bool checkpointsActive, bool canOperate)
    {
        if (PreEditCheckpointTaken || !checkpointsActive || !canOperate) return false;
        PreEditCheckpointTaken = true;
        return true;
    }

    public static void Reset() => PreEditCheckpointTaken = false;
}
