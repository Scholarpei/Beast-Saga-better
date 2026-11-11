namespace SafeDialogueTools.Core;

public static class CheatBatch
{
    public static IReadOnlyList<(string Key, T Value)> Changed<T>(
        IEnumerable<(string Key, T Draft, T Current)> items)
        where T : IEquatable<T>
    {
        var changed = new List<(string Key, T Value)>();
        foreach (var item in items)
        {
            if (EqualityComparer<T>.Default.Equals(item.Draft, item.Current)) continue;
            changed.Add((item.Key, item.Draft));
        }
        return changed;
    }
}
