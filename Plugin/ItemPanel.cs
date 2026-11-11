using System.Globalization;
using BeastSagaBetter.Core;
using UnityEngine;
namespace BeastSagaBetter;

public sealed partial class RuntimeDriver
{
    private Vector2 itemScroll, instanceScroll, itemDescriptionScroll;
    private string itemSearch = "", itemQuantity = "1", selectedItem = "";
    private int itemCategory;
    private bool itemOwned;
    private long itemEpoch = -1;
    private static readonly string[] itemCategories = { "全部", "装备", "道具", "材料", "秘籍", "任务", "战斗", "图纸", "灵化道具", "山海录" };
    internal void SetItemFixture(string search, int category, bool owned, string key, float scrollY, string quantity)
    {
        if (!Plugin.TestMode) throw new InvalidOperationException("Only TestMode supports UI fixtures");
        itemEpoch = ItemEditor.Epoch; itemSearch = search; itemCategory = category == -1 ? 1 : category == 0 ? 0 : category + 1;
        itemOwned = owned; selectedItem = key; itemScroll = new Vector2(0, scrollY); itemQuantity = quantity;
        ItemEditor.DisplayPolicy.SetDraft(key, quantity); ItemEditor.DisplayPolicy.ClearSelection();
        ItemEditor.Query(search, category, owned);
    }
    internal void DrawItemsPage(Rect bounds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var kind = Event.current.type;
        try { DrawItemsPageCore(bounds); }
        finally { if (Plugin.TestMode) ItemTimings.Add("draw-" + kind, watch.Elapsed.TotalMilliseconds); }
    }
    private void DrawItemsPageCore(Rect bounds)
    {
        GUI.enabled = true;
        if (itemEpoch != ItemEditor.Epoch) { itemEpoch = ItemEditor.Epoch; ItemEditor.DisplayPolicy.ClearSelection(); selectedItem = ""; }
        GUI.Label(new Rect(bounds.x, bounds.y, 64, 26), "物品", cheatPageTitleStyle);
        var text = GUI.TextField(new Rect(bounds.x + 65, bounds.y, Math.Max(100, bounds.width - 280), 26), itemSearch, cheatInputStyle);
        if (text != itemSearch) { itemSearch = text; itemScroll = Vector2.zero; }
        if (GUI.Button(new Rect(bounds.xMax - 205, bounds.y, 110, 26), itemCategories[itemCategory] + " ▾", cheatButtonStyle)) { itemCategory = (itemCategory + 1) % itemCategories.Length; itemScroll = Vector2.zero; }
        itemOwned = GUI.Toggle(new Rect(bounds.xMax - 88, bounds.y + 3, 88, 23), itemOwned, "仅背包");
        ItemEditor.Query(itemSearch, itemCategory == 1 ? -1 : itemCategory == 0 ? 0 : itemCategory - 1, itemOwned);
        GUI.Label(new Rect(bounds.x, bounds.y + 32, bounds.width, 20), ItemEditor.Block ?? "原生增删可能触发剧情条件；首次修改前建立完整检查点。", cheatSmallStyle);
        var body = new Rect(bounds.x, bounds.y + 58, bounds.width, Math.Max(60, bounds.height - 118));
        var leftWidth = Math.Max(170, body.width * .45f);
        var list = new Rect(body.x, body.y, leftWidth, body.height);
        var rows = ItemEditor.Results;
        const float rowHeight = 55;
        itemScroll = GUI.BeginScrollView(list, itemScroll, new Rect(0, 0, list.width - 14, Math.Max(list.height, rows.Count * rowHeight)));
        var start = Math.Max(0, (int)(itemScroll.y / rowHeight) - 1);
        var end = Math.Min(rows.Count, start + (int)(list.height / rowHeight) + 3);
        var visible = new List<string>();
        for (var index = start; index < end; index++)
        {
            var entry = rows[index]; var y = index * rowHeight; visible.Add(entry.Key);
            if (GUI.Button(new Rect(0, y, list.width - 16, rowHeight - 3), "", selectedItem == entry.Key ? cheatPickerItemActiveStyle : cheatPickerItemStyle))
            { selectedItem = entry.Key; ItemEditor.DisplayPolicy.SetDraft(selectedItem, itemQuantity); ItemEditor.DisplayPolicy.ClearSelection(); itemDescriptionScroll = Vector2.zero; instanceScroll = Vector2.zero; }
            var iconNote = ItemIcons.Draw(new Rect(4, y + 4, 42, 42), entry.Key);
            if (iconNote.Length > 0) GUI.Label(new Rect(4, y + 14, 42, 25), "◻", cheatHintStyle);
            GUI.Label(new Rect(52, y + 5, list.width - 70, 22), entry.Name + "  品质" + entry.Rank, cheatValueStyle);
            GUI.Label(new Rect(52, y + 28, list.width - 70, 18), "背包 " + ItemEditor.Counts.GetValueOrDefault(entry.Key), cheatSmallStyle);
        }
        GUI.EndScrollView();
        var detail = new Rect(body.x + leftWidth + 12, body.y, body.width - leftWidth - 12, body.height);
        GUI.BeginGroup(detail);
        var selected = ItemEditor.Selected(selectedItem);
        if (!string.IsNullOrEmpty(selected.Key))
        {
            visible.Add(selected.Key);
            var note = ItemIcons.Draw(new Rect(0, 0, 50, 50), selected.Key);
            GUI.Label(new Rect(60, 0, detail.width - 60, 24), selected.Name + "  品质" + selected.Rank, cheatSectionTitleStyle);
            GUI.Label(new Rect(60, 25, detail.width - 60, 24), "背包数量 " + ItemEditor.Counts.GetValueOrDefault(selected.Key), cheatValueStyle);
            if (note.Length > 0) GUI.Label(new Rect(0, 52, detail.width, 20), note, cheatTinyStyle);
            var descriptionHeight = Math.Max(70, cheatNoticeTextStyle!.CalcHeight(new GUIContent(selected.Description), Math.Max(20, detail.width - 18)));
            itemDescriptionScroll = GUI.BeginScrollView(new Rect(0, 75, detail.width, 74), itemDescriptionScroll, new Rect(0, 0, detail.width - 18, descriptionHeight));
            GUI.Label(new Rect(0, 0, detail.width - 18, descriptionHeight), selected.Description, cheatNoticeTextStyle);
            GUI.EndScrollView();
            GUI.Label(new Rect(0, 149, detail.width, 19), selected.Key, cheatTinyStyle);
            itemQuantity = GUI.TextField(new Rect(0, 176, 60, 27), itemQuantity, cheatCenterInputStyle);
            long.TryParse(itemQuantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity);
            var instances = ItemEditor.Equipment.GetValueOrDefault(selected.Key) ?? Array.Empty<OwnedEquipment>();
            var policy = ItemEditor.DisplayPolicy;
            policy.SetDraft(selected.Key, itemQuantity);
            var reason = ItemEditor.Block ?? policy.AddReason;
            GUI.enabled = reason == null && !ItemEditor.IsBusy;
            if (GUI.Button(new Rect(68, 176, 70, 27), "添加", cheatPrimaryButtonStyle)) ItemEditor.Request(selected.Key, ItemEditOperation.Add, quantity);
            var removeReason = ItemEditor.Block ?? policy.RemoveReason;
            GUI.enabled = removeReason == null && !ItemEditor.IsBusy;
            if (GUI.Button(new Rect(146, 176, 70, 27), "移除", cheatButtonStyle)) ItemEditor.Request(selected.Key, ItemEditOperation.Remove, quantity, policy.SelectedKeys);
            GUI.enabled = true;
            GUI.Label(new Rect(0, 207, detail.width, 36), removeReason ?? reason ?? "可提交；实际状态将在 Update 再次验证", cheatNoticeTextStyle);
            if (selected.IsEquipment && detail.height > 270)
            {
                var view = new Rect(0, 248, detail.width, detail.height - 248);
                instanceScroll = GUI.BeginScrollView(view, instanceScroll, new Rect(0, 0, detail.width - 18, instances.Length * 64));
                var instanceStart = Math.Max(0, (int)(instanceScroll.y / 64) - 1);
                var instanceEnd = Math.Min(instances.Length, instanceStart + (int)(view.height / 64) + 3);
                for (var index = instanceStart; index < instanceEnd; index++)
                {
                    var instance = instances[index]; var identity = instance.Identity;
                    GUI.enabled = identity.IsWorn == false && identity.IsLocked == false;
                    var wasSelected = policy.IsSelected(identity.Key);
                    var chosen = GUI.Toggle(new Rect(0, index * 64, detail.width - 20, 22), wasSelected, identity.Key + (identity.IsWorn == true ? " 已穿戴" : identity.IsWorn == null ? " 穿戴状态未知" : " 背包") + (identity.IsLocked == true ? " 锁定熔解" : ""));
                    policy.SetSelected(identity.Key, chosen);
                    GUI.enabled = true;
                    GUI.Label(new Rect(18, index * 64 + 23, detail.width - 35, 37), instance.Detail, cheatNoticeTextStyle);
                }
                GUI.EndScrollView();
            }
        }
        else GUI.Label(new Rect(0, 15, detail.width, 24), "选择物品查看详情与具体实例", cheatHintStyle);
        GUI.EndGroup();
        ItemIcons.RequestVisible(visible);
        GUI.Label(new Rect(bounds.x, bounds.yMax - 55, bounds.width, 23), "修改前检查点：" + ItemEditor.ProtectionDisplay, cheatSmallStyle);
        GUI.Label(new Rect(bounds.x, bounds.yMax - 30, bounds.width, 28), ItemEditor.Status, cheatNoticeTextStyle);
    }
}
