namespace SafeDialogueTools.Core;

// Independent from the legacy cheat session: attempting a save never constitutes protection.
public sealed class ItemEditSession
{
    private readonly object gate = new();
    private long serial;
    private long? activeToken;
    private string? protectedGeneration;
    private bool checking;
    private bool protectionFailed;

    public bool IsBusy { get { lock (gate) return activeToken.HasValue; } }
    public bool IsProtected { get { lock (gate) return protectedGeneration is not null; } }
    public string? ProtectedGeneration { get { lock (gate) return protectedGeneration; } }
    public bool CanCommit(long token)
    {
        lock (gate) return activeToken == token && protectedGeneration != null && !checking && !protectionFailed;
    }

    public bool TryBeginSubmission(out long token)
    {
        lock (gate)
        {
            token = 0;
            if (activeToken.HasValue) return false;
            token = ++serial;
            activeToken = token;
            protectionFailed = false;
            return true;
        }
    }

    // Call validate with BOTH store and native-save validation. On reuse, a failure rejects this
    // submission; a later user submission may create a replacement, never a silent fallback here.
    public bool EnsureProtection(long token, Func<string?> create, Func<string, bool> validate)
    {
        string? generation;
        lock (gate)
        {
            if (activeToken != token || checking || protectionFailed) return false;
            checking = true;
            generation = protectedGeneration;
        }

        var valid = false;
        try
        {
            generation ??= create();
            lock (gate)
                if (activeToken != token) return false;
            valid = !string.IsNullOrWhiteSpace(generation) && validate(generation);
        }
        catch
        {
            // Disk errors, corrupt saves or adapter failures cannot authorize a write.
            valid = false;
        }
        finally
        {
            lock (gate)
            {
                // A callback may reset and even start a newer session. Never mutate its state.
                if (activeToken == token)
                {
                    protectedGeneration = valid ? generation : null;
                    protectionFailed = !valid;
                    checking = false;
                }
            }
        }
        lock (gate) return activeToken == token && valid;
    }

    public void EndSubmission(long token)
    {
        lock (gate)
        {
            if (activeToken != token) return;
            activeToken = null;
            checking = false;
        }
    }

    // Called for panel close, load/scene/player changes and operation exceptions by the adapter.
    public void Reset()
    {
        lock (gate)
        {
            activeToken = null;
            protectedGeneration = null;
            checking = false;
            protectionFailed = false;
            serial++;
        }
    }
}
