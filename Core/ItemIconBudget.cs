namespace SafeDialogueTools.Core;

public readonly record struct ItemIconTicket(long Id, string Key);

/// <summary>Tracks ownership, including obsolete requests whose native callback is still pending.</summary>
public sealed class ItemIconBudget
{
    private readonly int capacity;
    private readonly int perFrame;
    private int started;
    private long next;
    private HashSet<string> visible = new(StringComparer.Ordinal);
    private readonly Dictionary<ItemIconTicket, (bool Complete, bool Stale)> loans = new();
    public ItemIconBudget(int capacity = 64, int perFrame = 2)
    {
        if (capacity < 1 || perFrame < 1) throw new ArgumentOutOfRangeException();
        this.capacity = capacity;
        this.perFrame = perFrame;
    }
    public int Count => loans.Count;
    public void BeginFrame() => started = 0;
    public void SetVisible(IEnumerable<string> keys)
    {
        visible = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var ticket in loans.Keys.ToArray())
            if (!visible.Contains(ticket.Key)) loans[ticket] = (loans[ticket].Complete, true);
    }
    public bool TryStart(string key, double elapsedMilliseconds, out ItemIconTicket ticket)
    {
        ticket = default;
        if (!visible.Contains(key) || loans.Count >= capacity || started >= perFrame ||
            elapsedMilliseconds >= 2 || loans.Any(pair => pair.Key.Key == key && !pair.Value.Stale)) return false;
        ticket = new ItemIconTicket(++next, key);
        loans.Add(ticket, (false, false));
        started++;
        return true;
    }
    public bool CanBind(ItemIconTicket ticket) =>
        loans.TryGetValue(ticket, out var state) && !state.Stale && visible.Contains(ticket.Key);
    public bool Complete(ItemIconTicket ticket)
    {
        if (!loans.TryGetValue(ticket, out var state)) return false;
        loans[ticket] = (true, state.Stale);
        return true;
    }
    public IReadOnlyList<ItemIconTicket> ReleaseCompleted()
    {
        var release = loans.Where(pair => pair.Value.Complete && pair.Value.Stale).Select(pair => pair.Key).ToArray();
        foreach (var ticket in release) loans.Remove(ticket);
        return release;
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
