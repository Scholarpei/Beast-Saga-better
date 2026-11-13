using BeastSagaBetter.Core;
using UnityEngine;

namespace BeastSagaBetter;

public sealed partial class RuntimeDriver
{
    private Vector2 combatScroll;
    private string selectedCombatUnit = "";

    private void DrawCombatPage(Rect bounds)
    {
        GUI.Label(new Rect(bounds.x, bounds.y, bounds.width, 24), "战斗", cheatPageTitleStyle);
        var content = new Rect(bounds.x, bounds.y + 32, bounds.width, Math.Max(1, bounds.height - 32));
        var width = Math.Max(1, content.width - 20);
        var reason = CombatEditor.Block ?? "当前为我方手动回合，可以提交操作";
        var hintHeight = Math.Max(24, cheatNoticeTextStyle!.CalcHeight(new GUIContent(reason), width));
        var checkpoint = "战前检查点：" + CombatEditor.CheckpointText;
        var checkpointHeight = Math.Max(42, cheatNoticeTextStyle.CalcHeight(new GUIContent(checkpoint), width));
        var result = CombatEditor.Result;
        var resultHeight = Math.Max(65, cheatNoticeTextStyle.CalcHeight(new GUIContent(result), width));
        var team = CombatEditor.Team;
        if (!team.Any(unit => unit.Key == selectedCombatUnit)) selectedCombatUnit = team.FirstOrDefault()?.Key ?? "";
        var confirmationText = $"确认处理当前 {CombatEditor.Enemies.Count} 名敌人？胜负、死亡和奖励由游戏判定。\n回退点：{CombatEditor.CheckpointText}\n检查点仅能在游戏允许的安全状态读取。";
        var confirmationHeight = CombatEditor.Confirmation == null ? 0 : Math.Max(70, cheatNoticeTextStyle.CalcHeight(new GUIContent(confirmationText), width - 16));
        var layout = CombatPanelGeometry.Create(content.width, content.height, team.Count, hintHeight, checkpointHeight, resultHeight, confirmationHeight);
        combatScroll = GUI.BeginScrollView(content, combatScroll, new Rect(0, 0, width, layout.ContentHeight));
        var oldEnabled = GUI.enabled;
        try
        {
            GUI.Label(UnityRect(layout.Hint), reason, cheatNoticeTextStyle);
            GUI.Label(UnityRect(layout.Checkpoint), checkpoint, cheatNoticeTextStyle);
            GUI.Label(UnityRect(layout.TeamHeading), "我方参战单位（恢复不复活、不清异常）", cheatSectionTitleStyle);
            for (var index = 0; index < team.Count; index++)
            {
                var unit = team[index]; var row = layout.Units[index];
                if (GUI.Button(UnityRect(row), "", unit.Key == selectedCombatUnit ? cheatPickerItemActiveStyle : cheatPickerItemStyle)) selectedCombatUnit = unit.Key;
                GUI.Label(new Rect(10, row.Y + 4, width - 20, 22), unit.Name + (unit.Vitals.Hp <= 0 ? " · 已死亡" : unit.PlayerControlled ? " · 可手动控制" : " · 我方"), cheatValueStyle);
                GUI.Label(new Rect(10, row.Y + 28, width - 20, 20), $"气血 {unit.Vitals.Hp:0.#}/{unit.Vitals.MaxHp:0.#} · 护甲 {unit.Vitals.Armor:0.#}/{unit.Vitals.MaxArmor:0.#} · 真气 {unit.Vitals.Gas:0.#}/{unit.Vitals.MaxGas:0.#}", cheatSmallStyle);
            }
            if (team.Count == 0) GUI.Label(new Rect(0, layout.TeamHeading.Bottom + 6, width, 30), "当前没有有效我方参战单位", cheatHintStyle);
            GUI.enabled = oldEnabled && CombatEditor.Block == null && !CombatEditor.Busy && CombatEditor.Confirmation == null;
            var buttonWidth = Math.Max(1, (width - 12) / 2);
            var selected = team.FirstOrDefault(unit => unit.Key == selectedCombatUnit);
            var canRestore = GUI.enabled;
            GUI.enabled = canRestore && selected?.Vitals.CanRestore == true;
            if (GUI.Button(UnityRect(layout.RestoreSelected), "恢复选中单位", cheatPrimaryButtonStyle)) CombatEditor.RequestRestore(selectedCombatUnit);
            GUI.enabled = canRestore;
            if (GUI.Button(UnityRect(layout.RestoreAll), "恢复我方全队", cheatButtonStyle)) CombatEditor.RequestRestore(null);
            GUI.enabled = canRestore && !CombatEditor.DefeatSubmitted && CombatEditor.Enemies.Count > 0;
            if (GUI.Button(UnityRect(layout.Defeat), $"击败当前全部敌人（{CombatEditor.Enemies.Count}）", cheatButtonStyle)) CombatEditor.PrepareDefeat();
            GUI.enabled = oldEnabled;
            if (layout.Confirmation is { } confirmation)
            {
                var y = confirmation.Y; var height = confirmation.Height - 52;
                GUI.Box(UnityRect(confirmation), GUIContent.none, cheatCardStyle);
                GUI.Label(new Rect(8, y + 6, width - 16, height), confirmationText, cheatNoticeTextStyle);
                GUI.enabled = CombatEditor.Block == null && !CombatEditor.Busy;
                if (GUI.Button(new Rect(8, y + height + 12, buttonWidth - 8, 30), "确认击败", cheatPrimaryButtonStyle)) CombatEditor.ConfirmDefeat();
                GUI.enabled = oldEnabled;
                if (GUI.Button(new Rect(buttonWidth + 12, y + height + 12, buttonWidth - 8, 30), "取消", cheatButtonStyle)) CombatEditor.CancelConfirmation();
            }
            GUI.Label(UnityRect(layout.Result), result, cheatNoticeTextStyle);
        }
        finally { GUI.enabled = oldEnabled; GUI.EndScrollView(); }
    }
}
