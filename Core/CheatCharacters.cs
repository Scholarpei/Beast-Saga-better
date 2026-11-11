namespace BeastSagaBetter.Core;

public enum CheatCharacterKind
{
    Player,
    Recruited,
    Known
}

public readonly record struct CheatCharacterEntry(string Key, string Name, CheatCharacterKind Kind);

public static class CheatCharacterPolicy
{
    public static bool CanEditStats(CheatCharacterKind kind) =>
        kind is CheatCharacterKind.Player or CheatCharacterKind.Recruited;

    public static bool CanEditFavor(CheatCharacterKind kind) =>
        kind is CheatCharacterKind.Recruited or CheatCharacterKind.Known;
}

public static class CheatCharacterList
{
    public const string PlayerKey = "@player";

    public static IReadOnlyList<CheatCharacterEntry> Build(
        string playerName,
        IEnumerable<(string Key, string Name)> recruited,
        IEnumerable<string> known)
    {
        var result = new List<CheatCharacterEntry>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "主角" : playerName;
        result.Add(new CheatCharacterEntry(PlayerKey, resolvedPlayerName, CheatCharacterKind.Player));
        seenNames.Add(resolvedPlayerName);
        seenKeys.Add(PlayerKey);

        foreach (var (key, name) in recruited)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) continue;
            var resolvedKey = "f:" + key;
            if (!seenKeys.Add(resolvedKey)) continue;
            seenNames.Add(name);
            result.Add(new CheatCharacterEntry(resolvedKey, name, CheatCharacterKind.Recruited));
        }

        foreach (var raw in known)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var name = raw.Trim();
            if (!seenNames.Add(name)) continue;
            var resolvedKey = "k:" + name;
            if (!seenKeys.Add(resolvedKey)) continue;
            result.Add(new CheatCharacterEntry(resolvedKey, name, CheatCharacterKind.Known));
        }

        return result;
    }
}

public static class CheatSelection
{
    public static string Resolve(IEnumerable<string> keys, string? selected, string fallback)
    {
        var list = keys as IReadOnlyList<string> ?? keys.ToArray();
        if (list.Count == 0) return fallback;
        return selected is not null && list.Contains(selected, StringComparer.Ordinal) ? selected : list[0];
    }
}
