using System.Globalization;

namespace SafeDialogueTools.Core;

/// <summary>Built with the Update snapshot; duplicate native identities remain ambiguous.</summary>
public sealed class ItemEquipmentIndex
{
    private readonly Dictionary<string, CheatEquipmentInstance?> instances = new(StringComparer.Ordinal);
    public ItemEquipmentIndex(IEnumerable<CheatEquipmentInstance> source)
    {
        foreach (var item in source)
            if (!instances.TryAdd(item.Key, item)) instances[item.Key] = null;
    }
    public IReadOnlyList<CheatEquipmentInstance> Resolve(IEnumerable<string> keys)
    {
        var selected = new List<CheatEquipmentInstance>();
        foreach (var key in keys)
            if (instances.TryGetValue(key, out var identity) && identity.HasValue) selected.Add(identity.Value);
        return selected;
    }
}

/// <summary>OnGUI changes only drafts and reads this cache; Update owns policy evaluation.</summary>
public sealed class ItemDisplayPolicyCache
{
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private long revision, evaluatedRevision = -1, evaluatedSnapshot = -1;
    private ItemEditRequest? evaluatedRequest;
    private string? addReason, removeReason;
    public string TargetKey { get; private set; } = "";
    public string QuantityText { get; private set; } = "1";
    public IEnumerable<string> SelectedKeys => selected;
    public bool Ready => revision == evaluatedRevision;
    public string? AddReason => Ready ? addReason : "等待状态更新";
    public string? RemoveReason => Ready ? removeReason : "等待状态更新";
    public bool IsSelected(string key) => selected.Contains(key);
    public void SetDraft(string targetKey, string quantity)
    {
        if (TargetKey == targetKey && QuantityText == quantity) return;
        if (TargetKey != targetKey) selected.Clear();
        TargetKey = targetKey; QuantityText = quantity; revision++;
    }
    public void SetSelected(string key, bool value)
    {
        if (value ? selected.Add(key) : selected.Remove(key)) revision++;
    }
    public void ClearSelection()
    {
        if (selected.Count == 0) return;
        selected.Clear(); revision++;
    }
    public void Update(ItemEditRequest request, ItemEquipmentIndex? index, long snapshotVersion)
    {
        if (Ready && snapshotVersion == evaluatedSnapshot && evaluatedRequest == request) return;
        var parsed = long.TryParse(QuantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity);
        var add = request with { Operation = ItemEditOperation.Add, Quantity = parsed ? quantity : 0 };
        addReason = ItemEditPolicy.BlockReason(add);
        var keys = selected.ToArray();
        var remove = add with { Operation = ItemEditOperation.Remove, SelectedInstanceKeys = keys,
            EquipmentInstances = index?.Resolve(keys) ?? Array.Empty<CheatEquipmentInstance>() };
        removeReason = ItemEditPolicy.BlockReason(remove);
        evaluatedRevision = revision; evaluatedSnapshot = snapshotVersion; evaluatedRequest = request;
    }
}
