using System.Diagnostics;
using SafeDialogueTools.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SafeDialogueTools;

internal sealed record OwnedEquipment(CheatEquipmentInstance Identity, string Detail);
internal sealed record ItemOperationResult(long Id, bool Ok, string Message, string Key, long Before, long After, string? Checkpoint);
internal static class ItemEditor
{
    private sealed record Template(CheatItemEntry Entry, ItemDataSet? Item, EquipDataSet? Equipment);
    private sealed record Pending(long Id, long Epoch, string Key, ItemEditOperation Operation, long Quantity, string[] Instances, IntPtr Stack);
    private static readonly Dictionary<string, Template> templates = new(StringComparer.Ordinal);
    private static readonly List<CheatItemEntry> catalog = new();
    private static readonly ItemEditSession session = new();
    private static Pending? pending;
    private static IntPtr playerPointer;
    private static IntPtr dataPointer;
    private static int scene;
    private static bool wasOpen;
    private static bool wasUnsafe;
    private static long epoch;
    private static long requestId;
    private static int itemIndex, equipIndex;
    private static float lastSnapshot;
    private static bool catalogReady;
    internal static bool CatalogReady => catalogReady;
    private static Dictionary<string, CheatItemEntry> resultLookup = new(StringComparer.Ordinal);
    internal static CheatItemEntry Selected(string key) => resultLookup.GetValueOrDefault(key);
    private static string search = "", appliedSearch = "";
    private static int filter, appliedFilter = -2;
    private static bool ownedOnly, appliedOwned;
    private static float queryAt;
    private static bool filterDirty = true;
    internal static IReadOnlyList<CheatItemEntry> Catalog => catalog;
    internal static IReadOnlyList<CheatItemEntry> Results { get; private set; } = Array.Empty<CheatItemEntry>();
    internal static Dictionary<string, int> Counts { get; private set; } = new(StringComparer.Ordinal);
    internal static Dictionary<string, OwnedEquipment[]> Equipment { get; private set; } = new(StringComparer.Ordinal);
    private static Dictionary<string, IntPtr> stacks = new(StringComparer.Ordinal);
    internal static string? Block { get; private set; } = "等待游戏就绪";
    internal static string Status { get; private set; } = "目录尚未加载";
    internal static ItemOperationResult? LastResult { get; private set; }
    internal static long Epoch => epoch;
    internal static long RequestId => requestId;
    internal static string? ProtectedGeneration => session.ProtectedGeneration;
    internal static string? RetainedGeneration => session.ProtectedGeneration ?? (LastResult?.Ok == false ? LastResult.Checkpoint : null);
    internal static string ProtectionDisplay { get; private set; } = "尚未建立（提交时建立并校验）";
    internal static bool IsBusy => pending != null || session.IsBusy;
    internal static bool PageActive => CommandPanelState.Open && CommandPanelState.Tab == CommandPanelTab.Cheats && CommandPanelState.CheatSection == CheatSection.Items;
    internal static void Query(string text, int category, bool onlyOwned)
    {
        if (search == text && filter == category && ownedOnly == onlyOwned) return;
        search = text; filter = category; ownedOnly = onlyOwned; queryAt = Time.realtimeSinceStartup;
    }
    internal static void Reset(string reason)
    {
        if (pending != null) LastResult = new ItemOperationResult(pending.Id, false, reason + "；请求已取消，未写入", pending.Key, -1, -1, session.ProtectedGeneration);
        epoch++; session.Reset(); pending = null; Counts.Clear(); Equipment.Clear(); stacks.Clear();
        ProtectionDisplay = "会话失效；下次提交重新建立并校验";
        filterDirty = true; ItemIcons.Clear(); Status = reason;
    }
    internal static bool Request(string key, ItemEditOperation operation, long quantity, IEnumerable<string>? instances = null)
    {
        if (IsBusy) return false;
        pending = new Pending(++requestId, epoch, key, operation, quantity, instances?.ToArray() ?? Array.Empty<string>(), stacks.GetValueOrDefault(key));
        Status = "请求已排队";
        return true;
    }
    internal static void Tick()
    {
        var active = PageActive;
        var watch = Stopwatch.StartNew();
        try { TickCore(); }
        finally { if (Plugin.TestMode && active) ItemTimings.Add("editor-active-update", watch.Elapsed.TotalMilliseconds); }
    }
    private static void TickCore()
    {
        var player = PlayerDataManager._Instance?._PlayerData;
        var data = LoadDataSet._Instance;
        var nextPlayer = player?.Pointer ?? IntPtr.Zero;
        var nextData = data?.Pointer ?? IntPtr.Zero;
        var nextScene = SceneManager.GetActiveScene().handle;
        var open = CommandPanelState.Open;
        var battle = BattleManager._Instance;
        var map = MapManager._Instance;
        var save = SaveManager._Instance;
        var unsafeNow = Plugin.Loading || Plugin.Faulted || save == null || !save.IsLogin || !save._IsLoadOver ||
            battle == null || battle._IsBattle || battle._IsStartBattle || battle._IsEnterBattle || battle._IsGameOver ||
            map == null || !map._IsInitOver || map._ChuanSongIng || map._ChuangSongMap;
        if (nextPlayer != playerPointer || nextData != dataPointer || nextScene != scene || wasOpen && !open || unsafeNow && !wasUnsafe)
        {
            Reset("会话已失效；下次修改重新建立检查点");
            if (nextData != dataPointer || nextPlayer != playerPointer) InvalidateCatalog();
        }
        playerPointer = nextPlayer; dataPointer = nextData; scene = nextScene; wasOpen = open; wasUnsafe = unsafeNow;
        Block = CheatEditor.EditBlockReason() ?? (!Plugin.CheckpointsActive ? "需先启用并授权检查点" : null);
        if (PageActive && player != null && data != null && !Plugin.Loading)
        {
            BuildCatalog(data);
            if (Time.realtimeSinceStartup - lastSnapshot >= 0.25f) RefreshSnapshot();
            if (filterDirty || Time.realtimeSinceStartup - queryAt >= 0.1f && (search != appliedSearch || filter != appliedFilter || ownedOnly != appliedOwned))
            {
                var entries = filter == -1 ? catalog.Where(entry => entry.IsEquipment) : filter > 0 ? catalog.Where(entry => !entry.IsEquipment && entry.Category == filter) : catalog;
                Results = CheatItemCatalog.Filter(entries, search, onlyOwned: ownedOnly, ownedCounts: Counts);
                resultLookup = Results.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
                appliedSearch = search; appliedFilter = filter; appliedOwned = ownedOnly; filterDirty = false;
            }
        }
        if (pending != null) Execute();
        ItemIcons.Tick(PageActive);
    }
    private static void BuildCatalog(LoadDataSet data)
    {
        if (catalogReady) return;
        if (data._ItemDataManager == null || data._EquipsDataManager == null) { Status = "原生目录尚未就绪"; return; }
        var watch = Stopwatch.StartNew();
        var processed = 0;
        while (processed++ < 32 && watch.Elapsed.TotalMilliseconds < 2)
        {
            if (data._ItemDataManager != null && itemIndex < data._ItemDataManager.Count)
            {
                var item = data._ItemDataManager[itemIndex++];
                if (item == null || string.IsNullOrWhiteSpace(item.name)) continue;
                var entry = new CheatItemEntry("item:" + item.name, item._Name ?? "", item._ItemDes ?? "", (int)item._EquipType, (int)item._ItemType, item._Rank, false);
                Add(new Template(entry, item, null));
            }
            else if (data._EquipsDataManager != null && equipIndex < data._EquipsDataManager.Count)
            {
                var equip = data._EquipsDataManager[equipIndex++];
                if (equip == null || string.IsNullOrWhiteSpace(equip.name)) continue;
                var rank = int.TryParse(equip._Rank, out var parsed) ? parsed : -1;
                var entry = new CheatItemEntry("equip:" + equip.name, equip._Name ?? "", equip._Des ?? "", 1, 0, rank, true);
                Add(new Template(entry, null, equip));
            }
            else { catalogReady = true; Status = $"目录 {catalog.Count} 项"; break; }
        }
    }
    internal static void InvalidateCatalog()
    {
        templates.Clear(); catalog.Clear(); itemIndex = equipIndex = 0; catalogReady = false;
        Results = Array.Empty<CheatItemEntry>(); resultLookup.Clear(); filterDirty = true;
    }
    private static void Add(Template value)
    {
        if (templates.ContainsKey(value.Entry.Key)) return;
        templates.Add(value.Entry.Key, value); catalog.Add(value.Entry); filterDirty = true;
    }
    internal static void RefreshSnapshot()
    {
        lastSnapshot = Time.realtimeSinceStartup;
        var player = PlayerDataManager._Instance?._PlayerData ?? throw new InvalidOperationException("玩家数据不可用");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var owned = new Dictionary<string, OwnedEquipment[]>(StringComparer.Ordinal);
        var stackPointers = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
        var worn = new HashSet<IntPtr>();
        var slotsKnown = player.PlayerDressEquip != null;
        if (player.PlayerDressEquip != null) foreach (var pair in player.PlayerDressEquip) if (pair.Value != null) worn.Add(pair.Value.Pointer);
        var friendData = PlayerFriendDataManager._Instance?._FriendData;
        slotsKnown &= friendData?.AddFriends != null && friendData.LeaveFriends != null;
        var allFriends = new List<FriendData>();
        if (friendData?.AddFriends != null) foreach (var pair in friendData.AddFriends) { if (pair.Value != null) allFriends.Add(pair.Value); else slotsKnown = false; }
        if (friendData?.LeaveFriends != null) foreach (var pair in friendData.LeaveFriends) { if (pair.Value != null) allFriends.Add(pair.Value); else slotsKnown = false; }
        foreach (var friend in allFriends)
        {
            if (friend._PlayerDressEquip != null) { foreach (var slot in friend._PlayerDressEquip) if (slot.Value != null) worn.Add(slot.Value.Pointer); }
            else slotsKnown = false;
            if (friend._LeavePlayerDressEquip != null) { foreach (var slot in friend._LeavePlayerDressEquip) if (slot.Value != null) worn.Add(slot.Value.Pointer); }
            else slotsKnown = false;
        }
        if (player.ItemHad == null || player.EquipHad == null) throw new InvalidOperationException("背包数据不完整");
        foreach (var pair in player.ItemHad)
        {
            var key = "item:" + pair.Key;
            if (pair.Value == null || pair.Value._ItemData == null || pair.Value._ItemData.name != pair.Key) { counts[key] = -1; continue; }
            counts[key] = pair.Value._Count; stackPointers[key] = pair.Value.Pointer;
        }
        foreach (var pair in player.EquipHad)
        {
            var key = "equip:" + pair.Key;
            if (pair.Value == null) { counts[key] = -1; continue; }
            var list = new List<OwnedEquipment>();
            foreach (var equip in pair.Value)
            {
                if (equip == null || equip._EquipData == null || equip._EquipData.name != pair.Key) { slotsKnown = false; continue; }
                bool? isWorn = (int)equip._DressType == 2 || worn.Contains(equip.Pointer) ? true : (int)equip._DressType == 1 && slotsKnown ? false : null;
                var identity = new CheatEquipmentInstance(InstanceKey(equip.Pointer), key, isWorn, equip._LockDissolve);
                var skills = new List<string>();
                if (equip._SkillLv != null) foreach (var skill in equip._SkillLv) skills.Add(skill.Key + ":" + (skill.Value == null ? "?" : string.Join(",", skill.Value.ToArray())));
                if (equip._ItemSkillLv != null) foreach (var skill in equip._ItemSkillLv) skills.Add(skill.Key + ":" + (skill.Value == null ? "?" : string.Join(",", skill.Value.ToArray())));
                if (equip._QiItemNames != null) foreach (var name in equip._QiItemNames) skills.Add(name);
                list.Add(new OwnedEquipment(identity, $"Lv{equip._Lv} 耐久{equip._Durable} {string.Join(" / ", skills)}"));
            }
            counts[key] = list.Count == pair.Value.Count ? list.Count : -1; owned[key] = list.ToArray();
        }
        if (ownedOnly && (Counts.Count != counts.Count || counts.Any(pair => Counts.GetValueOrDefault(pair.Key, -1) != pair.Value))) filterDirty = true;
        Counts = counts; Equipment = owned; stacks = stackPointers;
    }
    private static string InstanceKey(IntPtr pointer) => epoch + ":" + pointer.ToInt64().ToString("X");
    private static ItemEditRequest Policy(Template template, Pending request) => new(template.Entry, request.Operation, request.Quantity,
        Counts.GetValueOrDefault(request.Key), Valid(template), Counts.GetValueOrDefault(request.Key) >= 0 && (long)Counts.GetValueOrDefault(request.Key) + request.Quantity <= int.MaxValue,
        request.Instances, Equipment.GetValueOrDefault(request.Key)?.Select(equip => equip.Identity).ToArray());
    private static bool Valid(Template value) => value.Entry.IsEquipment
        ? value.Equipment != null && value.Equipment.name == value.Entry.Key[6..] && (int)value.Equipment._EquipPosition is >= 1 and <= 5 && value.Equipment._Des != null
        : value.Item != null && value.Item.name == value.Entry.Key[5..] && value.Item._ItemDes != null &&
            (int)value.Item._EquipType == value.Entry.Category && (int)value.Item._ItemType == value.Entry.ItemType && value.Item._Rank == value.Entry.Rank;
    private static void Execute()
    {
        var request = pending!; pending = null;
        long before = -1, after = -1;
        var ok = false;
        var message = "请求被拒绝";
        try
        {
            if (!CommandPanelState.Open || request.Epoch != epoch || Block != null) throw new InvalidOperationException(Block ?? "会话已失效");
            if (!templates.TryGetValue(request.Key, out var template)) throw new InvalidOperationException("未知目录键");
            RefreshSnapshot(); before = Counts.GetValueOrDefault(request.Key);
            var reason = ItemEditPolicy.BlockReason(Policy(template, request));
            if (reason != null) throw new InvalidOperationException(reason);
            ok = ItemSubmission.TryExecute(session, () =>
            {
                var generation = Plugin.Capture(CheckpointKind.Manual);
                Plugin.Store.Select(generation); Plugin.RefreshPanelSelectedCheckpoint(); Plugin.RefreshPanelArchive();
                var summary = Plugin.PanelArchive?.FirstOrDefault(item => item.Generation == generation);
                ProtectionDisplay = summary == null ? generation : summary.DisplayFullTime() + " · " + summary.MapName;
                return generation;
            }, generation => { NativeSave.Validate(Plugin.Store.Validate(generation)); return true; }, () =>
            {
                if (!RuntimeValid(request)) return false;
                RefreshSnapshot();
                if (request.Operation == ItemEditOperation.Remove && !template.Entry.IsEquipment && stacks.GetValueOrDefault(request.Key) != request.Stack) return false;
                return ItemEditPolicy.BlockReason(Policy(template, request)) == null;
            }, () =>
            {
                var manager = PlayerDataManager._Instance;
                var start = Counts.GetValueOrDefault(request.Key);
                before = start;
                if (template.Entry.IsEquipment)
                {
                    var pointersBefore = Equipment.GetValueOrDefault(request.Key)?.Select(item => item.Identity.Key).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
                    for (var index = 0; index < request.Quantity; index++)
                    {
                        if (!RuntimeValid(request)) throw new InvalidOperationException("原生调用期间状态变化；停止后续写入");
                        if (request.Operation == ItemEditOperation.Add) manager.AwardEquip(template.Equipment!, 1, true);
                        else
                        {
                            RefreshSnapshot();
                            var selected = request.Instances[index];
                            var current = Equipment.GetValueOrDefault(request.Key)?.SingleOrDefault(item => item.Identity.Key == selected);
                            if (current == null || current.Identity.IsWorn != false || current.Identity.IsLocked != false) throw new InvalidOperationException("具体实例已失效或受保护");
                            var native = manager._PlayerData.EquipHad[template.Equipment!.name].ToArray().Single(item => InstanceKey(item.Pointer) == selected);
                            if (!manager.RemovEquip(native)) throw new InvalidOperationException("原生移除返回失败");
                            LogicHelper._Instance.ConditionObserverCheck();
                        }
                        RefreshSnapshot();
                        var expected = start + (request.Operation == ItemEditOperation.Add ? index + 1 : -index - 1);
                        if (Counts.GetValueOrDefault(request.Key) != expected) throw new InvalidOperationException("原生数量变化不符；停止后续写入");
                        var currentKeys = Equipment.GetValueOrDefault(request.Key)?.Select(item => item.Identity.Key).ToArray() ?? Array.Empty<string>();
                        if (currentKeys.Distinct().Count() != currentKeys.Length || request.Operation == ItemEditOperation.Add && currentKeys.Count(key => !pointersBefore.Contains(key)) != index + 1 || request.Operation == ItemEditOperation.Remove && currentKeys.Contains(request.Instances[index])) throw new InvalidOperationException("装备实例核对失败；停止后续写入");
                    }
                }
                else if (request.Operation == ItemEditOperation.Add) manager.AwardItem(template.Item!, (int)request.Quantity, false, false);
                else
                {
                    if (!manager.RemoveBagItem(template.Item!.name, (int)request.Quantity)) throw new InvalidOperationException("原生移除返回失败");
                    LogicHelper._Instance.ConditionObserverCheck();
                }
                RefreshSnapshot(); after = Counts.GetValueOrDefault(request.Key);
                if (after != start + (request.Operation == ItemEditOperation.Add ? request.Quantity : -request.Quantity)) throw new InvalidOperationException("原生结果数量不符");
            });
            message = ok ? $"已完成：{before} → {after}；检查点 {session.ProtectedGeneration}" : "检查点建立/校验失败或提交状态已变化，未执行写入";
            if (ok && !ProtectionDisplay.EndsWith(" · 已完整验证", StringComparison.Ordinal)) ProtectionDisplay += " · 已完整验证";
            else if (!session.IsProtected) ProtectionDisplay = "检查点建立或校验失败；本次未写入";
        }
        catch (Exception error)
        {
            try { RefreshSnapshot(); after = Counts.GetValueOrDefault(request.Key); } catch { }
            message = error.Message + $"；实际 {before} → {after}；如有部分完成请读取完整检查点";
            Plugin.Logger.LogWarning("物品操作：" + error);
            var protection = session.ProtectedGeneration;
            Reset("操作停止；会话已重置");
            LastResult = new ItemOperationResult(request.Id, false, message, request.Key, before, after, protection);
        }
        LastResult ??= new ItemOperationResult(request.Id, ok, message, request.Key, before, after, session.ProtectedGeneration);
        if (LastResult.Id != request.Id) LastResult = new ItemOperationResult(request.Id, ok, message, request.Key, before, after, session.ProtectedGeneration);
        Status = message;
        Plugin.Logger.LogInfo("物品结果：" + System.Text.Json.JsonSerializer.Serialize(LastResult));
    }
    private static bool RuntimeValid(Pending request) => request.Epoch == epoch &&
        PlayerDataManager._Instance?._PlayerData?.Pointer == playerPointer && LoadDataSet._Instance?.Pointer == dataPointer &&
        SceneManager.GetActiveScene().handle == scene && CheatEditor.EditBlockReason() == null && CommandPanelState.Open && Plugin.CheckpointsActive;
    internal static string IconUrl(string key)
    {
        if (!templates.TryGetValue(key, out var template)) return "";
        if (template.Item != null) { var item = new PlayerItemData(); item.SetItemData(template.Item, 1); return FormatRenderer.FormatItem(item).ui_icon ?? ""; }
        if (template.Equipment != null) { var equip = new PlayerEquipData(); equip.SetData(template.Equipment); return FormatRenderer.FormatWeapon(equip).ui_icon ?? ""; }
        return "";
    }
}

internal static class ItemTimings
{
    private static readonly Dictionary<string, Queue<double>> samples = new(StringComparer.Ordinal);
    internal static void Add(string scope, double value)
    {
        if (!samples.TryGetValue(scope, out var values)) samples[scope] = values = new Queue<double>();
        values.Enqueue(value); while (values.Count > 512) values.Dequeue();
    }
    internal static object Snapshot() => samples.ToDictionary(pair => pair.Key, pair => new { samples = pair.Value.Count,
        p95Ms = pair.Value.OrderBy(value => value).ElementAt((int)((pair.Value.Count - 1) * .95)), maxMs = pair.Value.Max() });
}

[HarmonyLib.HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadGameSave))]
internal static class ItemLoadSessionPatch
{
    private static void Prefix() { ItemEditor.Reset("原生读档开始"); ItemEditor.InvalidateCatalog(); }
}
