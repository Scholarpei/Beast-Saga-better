namespace BeastSagaBetter.Core;

public readonly record struct ItemIconTicket(long Id, string Key);

/// <summary>Tracks ownership, including obsolete requests whose native callback is still pending.</summary>
public sealed class ItemIconBudget
{
    private readonly int capacity;
    private readonly int perFrame;
    private int started;
    private long next;
    private HashSet<string> visible = new(StringComparer.Ordinal);
    private sealed class Ownership
    {
        internal bool Complete, Stale, Returning, Quarantined;
    }
    private readonly Dictionary<ItemIconTicket, Ownership> loans = new();
    public ItemIconBudget(int capacity = 64, int perFrame = 2)
    {
        if (capacity < 1 || perFrame < 1) throw new ArgumentOutOfRangeException();
        this.capacity = capacity;
        this.perFrame = perFrame;
    }
    public int Count => loans.Count;
    public int QuarantinedCount => loans.Count(pair => pair.Value.Quarantined);
    public void BeginFrame() => started = 0;
    public void SetVisible(IEnumerable<string> keys)
    {
        visible = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var ticket in loans.Keys.ToArray())
            if (!visible.Contains(ticket.Key)) loans[ticket].Stale = true;
    }
    public bool TryStart(string key, double elapsedMilliseconds, out ItemIconTicket ticket)
    {
        ticket = default;
        if (!visible.Contains(key) || loans.Count >= capacity || started >= perFrame ||
            elapsedMilliseconds >= 2 || loans.Any(pair => pair.Key.Key == key && !pair.Value.Stale)) return false;
        ticket = new ItemIconTicket(++next, key);
        loans.Add(ticket, new Ownership());
        started++;
        return true;
    }
    public bool CanBind(ItemIconTicket ticket) =>
        loans.TryGetValue(ticket, out var state) && !state.Stale && !state.Quarantined && !state.Returning && visible.Contains(ticket.Key);
    public bool Complete(ItemIconTicket ticket)
    {
        if (!loans.TryGetValue(ticket, out var state) || state.Complete) return false;
        state.Complete = true;
        return true;
    }
    public IReadOnlyList<ItemIconTicket> ReleaseCompleted()
    {
        return loans.Where(pair => pair.Value.Complete && pair.Value.Stale && !pair.Value.Returning && !pair.Value.Quarantined).Select(pair => pair.Key).ToArray();
    }
    // Capacity retires only after the native return is known to have succeeded. An uncertain
    // partially executed release is quarantined permanently rather than retried or forgotten.
    public bool TryReturn(ItemIconTicket ticket, Action release)
    {
        if (!loans.TryGetValue(ticket, out var state) || !state.Complete || !state.Stale || state.Returning || state.Quarantined) return false;
        state.Returning = true;
        try { release(); loans.Remove(ticket); return true; }
        catch { state.Quarantined = true; throw; }
        finally { state.Returning = false; }
    }
}

/// <summary>Completion observation is separated from the surviving native main-thread heartbeat.</summary>
public sealed class ItemIconCompletionPump
{
    private readonly ItemIconBudget budget;
    private readonly int mainThreadId;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(ItemIconTicket Ticket, Action? Observed)> completed = new();
    public ItemIconCompletionPump(ItemIconBudget budget, int mainThreadId)
    {
        this.budget = budget;
        this.mainThreadId = mainThreadId;
    }
    public void Observe(ItemIconTicket ticket, Action? observed = null) => completed.Enqueue((ticket, observed));
    public void Drain(int currentThreadId, Action<ItemIconTicket> release, Action<ItemIconTicket, Exception> failed)
    {
        if (currentThreadId != mainThreadId) return;
        while (completed.TryDequeue(out var completion))
            if (budget.Complete(completion.Ticket)) completion.Observed?.Invoke();
        foreach (var ticket in budget.ReleaseCompleted())
        {
            try { budget.TryReturn(ticket, () => release(ticket)); }
            catch (Exception error) { failed(ticket, error); }
        }
    }
}

public static class ItemSubmission
{
    public static bool TryExecute(ItemEditSession session, Func<string?> create, Func<string, bool> validate,
        Func<bool> revalidate, Action commit)
    {
        if (!session.TryBeginSubmission(out var token)) return false;
        try
        {
            if (!session.EnsureProtection(token, create, validate) || !revalidate() || !session.CanCommit(token)) return false;
            commit();
            return true;
        }
        finally { session.EndSubmission(token); }
    }
}
