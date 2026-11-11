namespace SafeDialogueTools.Core;

public readonly record struct CheatItemEntry(
    string Key, string Name, string Description, int Category, int ItemType, int Rank, bool IsEquipment);

public static class CheatItemCatalog
{
    // Filtering retains the catalog's stable ordering and never merges entries by display name.
    public static IReadOnlyList<CheatItemEntry> Filter(
        IEnumerable<CheatItemEntry> entries,
        string? search = "",
        int? category = null,
        bool onlyOwned = false,
        IReadOnlyDictionary<string, int>? ownedCounts = null)
    {
        var query = search?.Trim() ?? "";
        // Dictionary callers may use a case-insensitive comparer; identities remain opaque and ordinal.
        var ownedKeys = onlyOwned && ownedCounts is not null
            ? new HashSet<string>(ownedCounts.Where(pair => pair.Value > 0).Select(pair => pair.Key), StringComparer.Ordinal)
            : null;
        return entries.Where(entry =>
            (!category.HasValue || entry.Category == category.Value) &&
            (!onlyOwned || ownedKeys?.Contains(entry.Key) == true) &&
            (query.Length == 0 ||
             (entry.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (entry.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))).ToArray();
    }
}

public enum ItemEditOperation
{
    Add,
    Remove
}

// Key identifies the concrete native instance in the current player/load generation only.
public readonly record struct CheatEquipmentInstance(string Key, string ItemKey, bool? IsWorn, bool? IsLocked);

public readonly record struct ItemEditRequest(
    CheatItemEntry Entry,
    ItemEditOperation Operation,
    long Quantity,
    long? OwnedCount,
    bool TemplateValid = false,
    bool? CapacityAvailable = null,
    IReadOnlyList<string>? SelectedInstanceKeys = null,
    IReadOnlyList<CheatEquipmentInstance>? EquipmentInstances = null);

public static class ItemEditPolicy
{
    // Null means allowed. The adapter must re-read and verify these facts immediately before mutation.
    public static string? BlockReason(ItemEditRequest request)
    {
        var entry = request.Entry;
        if (!request.TemplateValid || string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Name) ||
            entry.Description is null || entry.Category is < 1 or > 8 || entry.ItemType is < 0 or > 9 || entry.Rank < 0)
            return "物品数据不完整或类型未知";
        if (entry.Category == 4 || entry.ItemType == 5) return "任务物品与信件只允许浏览";
        if (request.Operation is not (ItemEditOperation.Add or ItemEditOperation.Remove)) return "未知物品操作";
        if (request.Quantity is <= 0 or > int.MaxValue) return "数量必须为有效正整数";
        if (request.OwnedCount is null or < 0 or > int.MaxValue) return "背包拥有数量未知或无效";

        if (request.Operation == ItemEditOperation.Add)
        {
            if (request.Quantity > (entry.IsEquipment ? 10 : 999)) return "超过单次添加数量上限";
            if (request.CapacityAvailable != true) return "背包容量或堆叠条件未确认";
            if (request.Quantity > int.MaxValue - request.OwnedCount.Value) return "添加后数量溢出";
            return null;
        }

        if (!entry.IsEquipment && request.Quantity > 999) return "超过单次移除数量上限";
        if (request.Quantity > request.OwnedCount.Value) return "移除数量超过当前拥有数量";
        if (!entry.IsEquipment) return null;
        if (request.SelectedInstanceKeys is null || request.EquipmentInstances is null ||
            request.SelectedInstanceKeys.Count != request.Quantity) return "请重新选择要移除的具体装备实例";

        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in request.SelectedInstanceKeys)
        {
            if (string.IsNullOrWhiteSpace(key) || !selected.Add(key)) return "装备实例选择重复或无效";
            var matches = request.EquipmentInstances.Where(instance => string.Equals(instance.Key, key, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !string.Equals(matches[0].ItemKey, entry.Key, StringComparison.Ordinal))
                return "装备实例已失效或目标不匹配";
            if (matches[0].IsWorn != false || matches[0].IsLocked != false)
                return "穿戴中、锁定或状态未知的装备不能移除";
        }
        return null;
    }
}
