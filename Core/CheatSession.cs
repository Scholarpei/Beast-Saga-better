namespace BeastSagaBetter.Core;

public static class CheatSessionState
{
    private static readonly object Gate = new();
    private static long serial;
    private static long? activeReservation;
    private static string? protectedGeneration;

    public static string? ProtectedGeneration
    {
        get { lock (Gate) return protectedGeneration; }
    }

    public static bool TryBeginPreEditCheckpoint(bool checkpointsActive, bool canOperate, out long token)
    {
        lock (Gate)
        {
            token = 0;
            if (protectedGeneration != null || activeReservation.HasValue || !checkpointsActive || !canOperate)
                return false;
            token = ++serial;
            activeReservation = token;
            return true;
        }
    }

    public static void CompletePreEditCheckpoint(long token, string generation)
    {
        lock (Gate)
        {
            if (activeReservation != token || string.IsNullOrWhiteSpace(generation)) return;
            protectedGeneration = generation;
            activeReservation = null;
        }
    }

    public static void ReleaseFailedPreEditCheckpoint(long token)
    {
        lock (Gate)
        {
            if (activeReservation != token) return;
            activeReservation = null;
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            activeReservation = null;
            protectedGeneration = null;
            serial++;
        }
    }
}
