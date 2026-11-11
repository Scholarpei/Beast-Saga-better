using System.Globalization;
using BeastSagaBetter.Core;
using UnityEngine;

namespace BeastSagaBetter;

public sealed partial class RuntimeDriver : MonoBehaviour
{
    private const float CheatHeadingHeight = 30f;
    private const float CheatPageHeadHeight = 50f;
    private const float CheatSectionGap = 14f;
    private const float CheatSidebarWidth = 190f;
    private const float CheatBodyGap = 16f;
    private const float CheatBaseHeight = 94f;
    private const float CheatStatsHeight = 166f;
    private const float CheatLevelHeight = 158f;
    private const float CheatFavorHeight = 98f;
    private const float CheatKnownHeight = 112f;
    private const float CheatPointCardHeight = 172f;

    private Vector2 cheatScroll;
    private Vector2 cheatCharacterMenuScroll;
    private string selectedCheatCharacter = CheatCharacterList.PlayerKey;
    private bool cheatCharacterMenuOpen;
    private string cheatMessage = "";
    private float cheatMessageUntil;
    private readonly Dictionary<string, string> cheatDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> cheatDraftSources = new(StringComparer.Ordinal);
    private readonly List<Texture2D> cheatTextures = new();
    private readonly PanelStyleGeneration cheatStyleGeneration = new();

    private GUIStyle? cheatH2Style;
    private GUIStyle? cheatPageTitleStyle;
    private GUIStyle? cheatSectionTitleStyle;
    private GUIStyle? cheatHintStyle;
    private GUIStyle? cheatSmallStyle;
    private GUIStyle? cheatTinyStyle;
    private GUIStyle? cheatLabelStyle;
    private GUIStyle? cheatValueStyle;
    private GUIStyle? cheatFlowStyle;
    private GUIStyle? cheatFlowStrongStyle;
    private GUIStyle? cheatBigStyle;
    private GUIStyle? cheatPinkStyle;
    private GUIStyle? cheatInputStyle;
    private GUIStyle? cheatCenterInputStyle;
    private GUIStyle? cheatButtonStyle;
    private GUIStyle? cheatPrimaryButtonStyle;
    private GUIStyle? cheatSectionActionStyle;
    private GUIStyle? cheatPickerStyle;
    private GUIStyle? cheatPickerItemStyle;
    private GUIStyle? cheatPickerItemActiveStyle;
    private GUIStyle? cheatSidebarStyle;
    private GUIStyle? cheatSidebarButtonStyle;
    private GUIStyle? cheatSidebarActiveStyle;
    private GUIStyle? cheatSidebarDisabledStyle;
    private GUIStyle? cheatCardStyle;
    private GUIStyle? cheatMenuStyle;
    private GUIStyle? cheatCellStyle;
    private GUIStyle? cheatDisabledCellStyle;
    private GUIStyle? cheatPointCardStyle;
    private GUIStyle? cheatNoticeBgStyle;
    private GUIStyle? cheatNoticeTextStyle;
    private GUIStyle? cheatPillStyle;
    private GUIStyle? cheatPillWarnStyle;
    private GUIStyle? cheatPillOkStyle;
    private GUIStyle? cheatBadgeStyle;
    private GUIStyle? cheatToastStyle;
    private GUIStyle? cheatSwitchOffStyle;
    private GUIStyle? cheatSwitchOnStyle;
    private GUIStyle? cheatKnobStyle;
    private GUIStyle? cheatKnobOnStyle;
    private GUIStyle? cheatKnobOffStyle;
    private GUIStyle? cheatEmptyStyle;
    private GUIStyle? cheatScrollbarStyle;
    private GUIStyle? cheatScrollbarThumbStyle;
    private GUIStyle? cheatScrollbarEmptyStyle;

    private void DrawCheatsTab(Rect bounds, CommandPanelState.State state)
    {
        EnsureCheatStyles();
        var metrics = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height));
        var scale = metrics.Scale;
        var origin = new Vector3(bounds.x + 14 * scale,
            bounds.y + 12 * scale + 78 * scale + 12 * scale + 38 * scale + 16 * scale, 0);
        var designWidth = (bounds.width - 28 * scale) / scale;
        var designBottom = (bounds.y + bounds.height - 10 * scale - origin.y) / scale;
        var oldMatrix = GUI.matrix;
        var skin = GUI.skin;
        var oldScrollbar = skin.verticalScrollbar;
        var oldScrollbarThumb = skin.verticalScrollbarThumb;
        var oldScrollbarUp = skin.verticalScrollbarUpButton;
        var oldScrollbarDown = skin.verticalScrollbarDownButton;
        try
        {
            skin.verticalScrollbar = cheatScrollbarStyle!;
            skin.verticalScrollbarThumb = cheatScrollbarThumbStyle!;
            skin.verticalScrollbarUpButton = cheatScrollbarEmptyStyle!;
            skin.verticalScrollbarDownButton = cheatScrollbarEmptyStyle!;
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1));
            DrawCheatHeading(designWidth);
            var footerY = designBottom - 15f;
            var bodyBottom = footerY - 10f;
            var body = new Rect(0, CheatHeadingHeight, designWidth,
                Math.Max(0, bodyBottom - CheatHeadingHeight));
            if (body.width > CheatSidebarWidth + 120 && body.height > 80)
            {
                DrawCheatSidebar(new Rect(0, body.y, CheatSidebarWidth, body.height));
                DrawCheatMain(new Rect(CheatSidebarWidth + CheatBodyGap, body.y,
                    body.width - CheatSidebarWidth - CheatBodyGap, body.height), state);
            }
            GUI.Label(new Rect(0, footerY, designWidth, 14),
                $"{Plugin.PanelCloseHint} · 修改作弊", cheatSmallStyle);
            DrawCheatMessage(designWidth, designBottom);
        }
        finally
        {
            GUI.matrix = oldMatrix;
            skin.verticalScrollbar = oldScrollbar;
            skin.verticalScrollbarThumb = oldScrollbarThumb;
            skin.verticalScrollbarUpButton = oldScrollbarUp;
            skin.verticalScrollbarDownButton = oldScrollbarDown;
        }
    }

    private void DrawCheatHeading(float width)
    {
        var titleSize = cheatH2Style!.CalcSize(new GUIContent("修改作弊"));
        GUI.Label(new Rect(0, 0, titleSize.x + 8, titleSize.y), "修改作弊", cheatH2Style);
        GUI.Label(new Rect(titleSize.x + 12, 2, width - titleSize.x - 12, 18),
            "左侧选择分类，右侧显示该分类的修改项", cheatHintStyle);
    }

    private void DrawCheatMain(Rect bounds, CommandPanelState.State state)
    {
        var section = CheatSectionCatalog.Get(CommandPanelState.CheatSection);
        if (!section.Available)
        {
            DrawCheatDisabled(bounds, section);
            return;
        }
        if (section.Section == CheatSection.Items)
        {
            DrawItemsPage(bounds);
            return;
        }
        var reason = CheatEditor.EditBlockReason();
        var title = section.Label;
        var description = section.Section switch
        {
            CheatSection.Character => "不同角色的属性和武学不同；先选角色，再修改该角色的数据。",
            CheatSection.Points => "主角的点数池单独放在这里，不和角色六维混在一起。",
            _ => "不属于角色属性的开关和运行时修改。"
        };
        var pill = reason is null
            ? section.Section == CheatSection.Character ? "安全修改"
                : section.Section == CheatSection.Points ? "主角" : null
            : "当前不可修改";
        var pillStyle = reason is null
            ? section.Section == CheatSection.Character ? cheatPillOkStyle : cheatPillStyle
            : cheatPillWarnStyle;
        DrawCheatPageHead(new Rect(bounds.x, bounds.y, bounds.width, CheatPageHeadHeight),
            title, description, pill, pillStyle);
        var content = new Rect(bounds.x, bounds.y + CheatPageHeadHeight,
            bounds.width, bounds.height - CheatPageHeadHeight);
        var previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && reason is null;
        GUI.BeginGroup(content);
        try
        {
            switch (section.Section)
            {
                case CheatSection.Character:
                    DrawCharacterPage(new Rect(0, 0, content.width, content.height));
                    break;
                case CheatSection.Points:
                    DrawPointsPage(new Rect(0, 0, content.width, content.height));
                    break;
                case CheatSection.Other:
                    DrawOtherPage(new Rect(0, 0, content.width, content.height), state);
                    break;
                default:
                    DrawCheatDisabled(new Rect(0, 0, content.width, content.height), section);
                    break;
            }
        }
        finally
        {
            GUI.EndGroup();
            GUI.enabled = previousEnabled;
        }
    }

    private void DrawCheatPageHead(Rect rect, string title, string description, string? pill, GUIStyle? pillStyle)
    {
        GUI.Label(new Rect(rect.x, rect.y, rect.width - 120, 20), title, cheatPageTitleStyle);
        GUI.Label(new Rect(rect.x, rect.y + 22, rect.width - 120, 18), description, cheatHintStyle);
        if (pill is null || pillStyle is null) return;
        var width = PillWidth(pill, pillStyle);
        DrawPill(new Rect(rect.xMax - width, rect.y, width, 20), pill, pillStyle);
    }

    private void DrawCheatSidebar(Rect bounds)
    {
        GUI.Box(bounds, GUIContent.none, cheatSidebarStyle);
        const float padding = 10f;
        const float gap = 8f;
        var inner = new Rect(bounds.x + padding, bounds.y + padding,
            bounds.width - padding * 2, bounds.height - padding * 2);
        var items = CheatSectionCatalog.All;
        var rowHeight = (inner.height - gap * (items.Count - 1)) / items.Count;
        if (rowHeight < 34f) rowHeight = 34f;
        var y = inner.y;
        foreach (var item in items)
        {
            var rect = new Rect(inner.x, y, inner.width, rowHeight);
            var selected = CommandPanelState.CheatSection == item.Section;
            var style = selected ? cheatSidebarActiveStyle! : item.Available
                ? cheatSidebarButtonStyle! : cheatSidebarDisabledStyle!;
            if (GUI.Button(rect, item.Label, style) && item.Available)
            {
                CommandPanelState.SelectCheatSection(item.Section);
                cheatScroll = Vector2.zero;
                cheatCharacterMenuOpen = false;
                ClearCheatDrafts();
            }
            if (selected)
                GUI.Box(new Rect(rect.x, rect.y + 6, 3, rect.height - 12), GUIContent.none, panelLineStyle!);
            if (!item.Available && item.Badge is not null)
            {
                var badgeWidth = PillWidth(item.Badge, cheatBadgeStyle!);
                DrawPill(new Rect(rect.xMax - 12 - badgeWidth, rect.y + (rect.height - 18) / 2,
                    badgeWidth, 18), item.Badge, cheatBadgeStyle!);
            }
            y += rowHeight + gap;
        }
    }

    private void DrawCheatDisabled(Rect bounds, CheatSectionInfo section)
    {
        GUI.Box(bounds, GUIContent.none, cheatEmptyStyle);
        DrawDashedBorder(bounds);
        GUI.Label(new Rect(bounds.x + 24, bounds.y + bounds.height / 2 - 28, bounds.width - 48, 24),
            section.Label + " · 第二批接入", cheatPageTitleStyle);
        GUI.Label(new Rect(bounds.x + 24, bounds.y + bounds.height / 2 + 2, bounds.width - 48, 20),
            section.Section switch
            {
                CheatSection.Items => "按类别/搜索添加或移除物品、装备、材料、秘籍等。",
                CheatSection.Combat => "恢复全队状态、击杀敌人、直接获取战斗奖励。",
                _ => "设置时间、一键开锁、生成/传送等。"
            }, cheatHintStyle);
    }

    private void DrawCharacterPage(Rect bounds)
    {
        var options = CheatEditor.Characters();
        selectedCheatCharacter = CheatSelection.Resolve(options.Select(item => item.Key), selectedCheatCharacter,
            CheatCharacterList.PlayerKey);
        var option = options.FirstOrDefault(item => item.Key == selectedCheatCharacter) ?? options[0];
        var snapshot = CheatEditor.Snapshot(option);

        GUI.Label(new Rect(0, 1, 64, 34), "当前角色", cheatLabelStyle);
        var pickerWidth = Math.Min(330f, Math.Max(160f, bounds.width - 250f));
        var pickerRect = new Rect(72, 0, pickerWidth, 36);
        if (GUI.Button(pickerRect, GUIContent.none, cheatPickerStyle))
            cheatCharacterMenuOpen = !cheatCharacterMenuOpen;
        GUI.Label(new Rect(pickerRect.x + 12, pickerRect.y, pickerRect.width - 90, pickerRect.height),
            option.Name, cheatValueStyle);
        GUI.Label(new Rect(pickerRect.xMax - 82, pickerRect.y, 68, pickerRect.height),
            KindLabel(option.Kind), cheatHintStyle);
        var badge = option.CanEditStats ? "可修改全部属性" : "仅好感度";
        var badgeStyle = option.CanEditStats ? cheatPillOkStyle! : cheatPillStyle!;
        var badgeWidth = PillWidth(badge, badgeStyle);
        DrawPill(new Rect(pickerRect.xMax + 10, 8, badgeWidth, 20), badge, badgeStyle);

        var notice = option.CanEditStats
            ? "直接修改六维属于高级操作，可能和已分配点数不一致；优先使用“点数”页。"
            : "该角色尚未入队，只开放好感度；六维、武学和生活技能需要先入队。";
        var noticeHeight = DrawNotice(0, 46, bounds.width, notice);
        var scrollTop = 46 + noticeHeight + 10;
        var scrollRect = new Rect(0, scrollTop, bounds.width, bounds.yMax - scrollTop);
        if (scrollRect.height <= 30) return;
        var contentWidth = Math.Max(220f, scrollRect.width - 7f);
        var contentHeight = CharacterContentHeight(option);
        cheatScroll = GUI.BeginScrollView(scrollRect, cheatScroll,
            new Rect(0, 0, contentWidth, contentHeight));
        try
        {
        var y = 0f;
        if (option.CanEditStats)
        {
            DrawBaseSection(new Rect(0, y, contentWidth, CheatBaseHeight), option, snapshot);
            y += CheatBaseHeight + CheatSectionGap;
            DrawStatsSection(new Rect(0, y, contentWidth, CheatStatsHeight), option, snapshot);
            y += CheatStatsHeight + CheatSectionGap;
            DrawLevelSection(new Rect(0, y, contentWidth, CheatLevelHeight), option, snapshot, true);
            y += CheatLevelHeight + CheatSectionGap;
            DrawLevelSection(new Rect(0, y, contentWidth, CheatLevelHeight), option, snapshot, false);
            y += CheatLevelHeight + CheatSectionGap;
        }
        if (option.Kind != CheatCharacterKind.Player)
        {
            DrawFavorSection(new Rect(0, y, contentWidth, CheatFavorHeight), option, snapshot);
            y += CheatFavorHeight + CheatSectionGap;
        }
        if (option.Kind == CheatCharacterKind.Known)
            DrawKnownSection(new Rect(0, y, contentWidth, CheatKnownHeight));
        }
        finally { GUI.EndScrollView(); }
        DrawCheatScrollbar(scrollRect, cheatScroll, contentHeight);
        if (cheatCharacterMenuOpen) DrawCharacterMenu(bounds, pickerRect, options);
    }

    private static float CharacterContentHeight(CheatCharacterOption option)
    {
        var height = 0f;
        if (option.CanEditStats)
            height += CheatBaseHeight + CheatSectionGap + CheatStatsHeight + CheatSectionGap +
                CheatLevelHeight + CheatSectionGap + CheatLevelHeight + CheatSectionGap;
        if (option.Kind != CheatCharacterKind.Player) height += CheatFavorHeight + CheatSectionGap;
        if (option.Kind == CheatCharacterKind.Known) height += CheatKnownHeight + CheatSectionGap;
        return Math.Max(height, 180f);
    }

    private void DrawBaseSection(Rect rect, CheatCharacterOption option, CheatCharacterSnapshot snapshot)
    {
        DrawSectionCard(rect);
        DrawSectionHeader(rect, "基础", "等级只读；经验通过“添加经验”写入", null, null, null);
        var rowY = rect.y + 48;
        GUI.Label(new Rect(rect.x + 14, rowY, 40, 32), "等级", cheatLabelStyle);
        GUI.Label(new Rect(rect.x + 54, rowY, 80, 32), "Lv. " + snapshot.Level, cheatValueStyle);
        DrawPill(new Rect(rect.x + 132, rowY + 6, 36, 20), "只读", cheatPillStyle!);
        if (!option.IsPlayer)
        {
            GUI.Label(new Rect(rect.x + 180, rowY, rect.width - 194, 32),
                "队友不显示经验，只显示等级", cheatHintStyle);
            return;
        }
        const float buttonWidth = 84f;
        const float inputWidth = 76f;
        const float valueWidth = 70f;
        const float labelWidth = 30f;
        var buttonX = rect.xMax - 14 - buttonWidth;
        var inputX = buttonX - 8 - inputWidth;
        var valueX = inputX - 8 - valueWidth;
        var labelX = valueX - 8 - labelWidth;
        GUI.Label(new Rect(labelX, rowY, labelWidth, 32), "经验", cheatLabelStyle);
        GUI.Label(new Rect(valueX, rowY, valueWidth, 32), FormatNumber(snapshot.Exp), cheatValueStyle);
        DrawCheatInput("char:" + option.Key + ":exp", new Rect(inputX, rowY, inputWidth, 32), "1000");
        if (GUI.Button(new Rect(buttonX, rowY, buttonWidth, 32), "添加经验", cheatPrimaryButtonStyle!))
            ApplyExperience(option);
    }

    private void DrawStatsSection(Rect rect, CheatCharacterOption option, CheatCharacterSnapshot snapshot)
    {
        DrawSectionCard(rect);
        DrawSectionHeader(rect, "六维", "左边填写基础值，箭头后是含装备/技能的最终值",
            "应用六维", () => ApplyStats(option, snapshot), "高级");
        DrawStatGrid(new Rect(rect.x + 14, rect.y + 48, rect.width - 28, 104), option, snapshot);
    }

    private void DrawStatGrid(Rect grid, CheatCharacterOption option, CheatCharacterSnapshot snapshot)
    {
        const float gap = 8f;
        const float cellHeight = 48f;
        var cellWidth = (grid.width - gap * 3) / 4f;
        for (var index = 0; index < 8; index++)
        {
            var column = index % 4;
            var row = index / 4;
            var cell = new Rect(grid.x + column * (cellWidth + gap), grid.y + row * (cellHeight + gap),
                cellWidth, cellHeight);
            if (index >= CheatEditor.AttributeTypes.Length)
            {
                GUI.Box(cell, GUIContent.none, cheatDisabledCellStyle);
                GUI.Label(new Rect(cell.x + 9, cell.y + 6, 50, 18), "脚程", cheatLabelStyle);
                GUI.Label(new Rect(cell.x + 9, cell.y + 24, 90, 16), "未验证", cheatTinyStyle);
                var wasEnabled = GUI.enabled;
                GUI.enabled = false;
                DrawCheatInput("char:" + option.Key + ":stat:7",
                    new Rect(cell.xMax - 9 - 62, cell.y + 9, 62, 30), "—", cheatCenterInputStyle);
                GUI.enabled = wasEnabled;
                continue;
            }
            var type = CheatEditor.AttributeTypes[index];
            var baseValue = snapshot.BaseStats[index];
            var finalValue = snapshot.FinalStats[index];
            GUI.Box(cell, GUIContent.none, cheatCellStyle);
            GUI.Label(new Rect(cell.x + 9, cell.y + 6, 60, 18), type.Label, cheatValueStyle);
            var prefix = FormatNumber(baseValue) + " → ";
            GUI.Label(new Rect(cell.x + 9, cell.y + 24, 80, 16), prefix, cheatFlowStyle);
            var prefixWidth = cheatFlowStyle!.CalcSize(new GUIContent(prefix)).x;
            var verified = CheatStatPolicy.HasVerifiedFinal(type.Value);
            GUI.Label(new Rect(cell.x + 9 + prefixWidth, cell.y + 23, 70, 16),
                verified ? FormatNumber(finalValue) : "未验证", verified ? cheatFlowStrongStyle : cheatTinyStyle);
            DrawCheatInput(StatDraftId(option, type.Value),
                new Rect(cell.xMax - 9 - 62, cell.y + 9, 62, 30), FormatNumber(baseValue), cheatCenterInputStyle);
        }
    }

    private void DrawLevelSection(Rect rect, CheatCharacterOption option, CheatCharacterSnapshot snapshot, bool martial)
    {
        DrawSectionCard(rect);
        DrawSectionHeader(rect, martial ? "武学等级" : "生活技能",
            "填写目标等级，改完一次应用；0 表示未学会、不会写入",
            martial ? "应用武学" : "应用生活",
            () => ApplyLevels(option, snapshot, martial), null);
        var types = martial ? CheatEditor.MartialTypes : CheatEditor.LifeTypes;
        var levels = martial ? snapshot.MartialLevels : snapshot.LifeLevels;
        DrawLevelGrid(new Rect(rect.x + 14, rect.y + 48, rect.width - 28, 95),
            option, levels, !martial, martial ? 7 : 5);
        _ = types;
    }

    private void DrawLevelGrid(Rect grid, CheatCharacterOption option, int[] levels, bool life, int columns)
    {
        var types = life ? CheatEditor.LifeTypes : CheatEditor.MartialTypes;
        const float gap = 7f;
        const float cellHeight = 44f;
        var cellWidth = (grid.width - gap * (columns - 1)) / columns;
        for (var index = 0; index < types.Length; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var cell = new Rect(grid.x + column * (cellWidth + gap), grid.y + row * (cellHeight + gap),
                cellWidth, cellHeight);
            var type = types[index];
            GUI.Box(cell, GUIContent.none, cheatCellStyle);
            GUI.Label(new Rect(cell.x + 7, cell.y + 5, 46, 17), type.Label, cheatValueStyle);
            GUI.Label(new Rect(cell.x + 7, cell.y + 23, 62, 14), "当前 " + levels[index], cheatTinyStyle);
            DrawCheatInput(LevelDraftId(option, life, type.Value),
                new Rect(cell.xMax - 7 - 38, cell.y + 8, 38, 28), FormatNumber(levels[index]),
                cheatCenterInputStyle);
        }
    }

    private void DrawFavorSection(Rect rect, CheatCharacterOption option, CheatCharacterSnapshot snapshot)
    {
        DrawSectionCard(rect);
        DrawSectionHeader(rect, "好感度", "按输入值加减；最低为 0", null, null, null);
        var rowY = rect.y + 48;
        GUI.Label(new Rect(rect.x + 14, rowY, 66, 36),
            snapshot.HasFavor ? FormatNumber(snapshot.Favor) : "未记录", cheatPinkStyle);
        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && snapshot.HasFavor;
        var inputRect = new Rect(rect.x + 84, rowY + 2, 80, 32);
        DrawCheatInput("char:" + option.Key + ":favor", inputRect, "5");
        if (GUI.Button(new Rect(inputRect.xMax + 8, rowY + 2, 70, 32), "+ 添加", cheatPrimaryButtonStyle!))
            ApplyFavor(option, 1f);
        if (GUI.Button(new Rect(inputRect.xMax + 86, rowY + 2, 70, 32), "- 减少", cheatButtonStyle!))
            ApplyFavor(option, -1f);
        GUI.enabled = wasEnabled;
        var status = snapshot.HasFavor ? "当前存档已记录" : "当前存档未记录";
        var style = snapshot.HasFavor ? cheatPillOkStyle! : cheatPillStyle!;
        var width = PillWidth(status, style);
        DrawPill(new Rect(rect.xMax - 14 - width, rowY + 7, width, 20), status, style);
    }

    private void DrawKnownSection(Rect rect)
    {
        DrawSectionCard(rect);
        DrawSectionHeader(rect, "已认识角色", "未入队，只开放好感度", null, null, null);
        DrawNotice(rect.x + 14, rect.y + 48, rect.width - 28,
            "该角色只能修改好感度；六维、武学和生活技能需要先入队。");
    }

    private void DrawPointsPage(Rect bounds)
    {
        var points = CheatEditor.Points();
        var noticeHeight = DrawNotice(0, 0, bounds.width,
            "可用 = 总数 - 已用；修改后请在游戏内重新打开角色界面确认。");
        var scrollRect = new Rect(0, noticeHeight + 10, bounds.width, bounds.yMax - noticeHeight - 10);
        if (scrollRect.height <= 30) return;
        var contentWidth = Math.Max(220f, scrollRect.width - 7f);
        const float gap = 12f;
        var cardWidth = (contentWidth - gap * 2) / 3f;
        cheatScroll = GUI.BeginScrollView(scrollRect, cheatScroll,
            new Rect(0, 0, contentWidth, CheatPointCardHeight + 14 + 116));
        try
        {
        DrawPointCard(new Rect(0, 0, cardWidth, CheatPointCardHeight), "属性点", CheatPointPool.Attribute,
            points.AttributeAvailable, points.AttributeUsed, points.AttributeTotal, points.AttributeAvailable);
        DrawPointCard(new Rect(cardWidth + gap, 0, cardWidth, CheatPointCardHeight), "生活点", CheatPointPool.Life,
            points.LifeAvailable, points.LifeUsed, points.LifeTotal, points.LifeDerivedAvailable);
        DrawPointCard(new Rect((cardWidth + gap) * 2, 0, cardWidth, CheatPointCardHeight), "特性点",
            CheatPointPool.Talent, points.TalentAvailable, points.TalentUsed, points.TalentTotal,
            points.TalentAvailable);
        var note = new Rect(0, CheatPointCardHeight + 14, contentWidth, 116);
        DrawSectionCard(note);
        DrawSectionHeader(note, "说明", "", null, null, null);
        DrawNotice(note.x + 14, note.y + 48, note.width - 28,
            "生活点不是独立点数池，而是由生活技能等级换算；要增加生活点，请在“角色属性”页提高生活技能等级。属性点和特性点可以直接写入。");
        }
        finally { GUI.EndScrollView(); }
        DrawCheatScrollbar(scrollRect, cheatScroll, CheatPointCardHeight + 14 + 116);
    }

    private void DrawPointCard(Rect rect, string title, CheatPointPool pool, int available, int used,
        int total, int derived)
    {
        GUI.Box(rect, GUIContent.none, cheatPointCardStyle);
        GUI.Label(new Rect(rect.x + 14, rect.y + 14, rect.width - 28, 20), title, cheatSectionTitleStyle);
        var big = pool == CheatPointPool.Life ? derived : available;
        var bigText = FormatNumber(big);
        GUI.Label(new Rect(rect.x + 14, rect.y + 38, rect.width - 28, 38), bigText, cheatBigStyle);
        var bigWidth = cheatBigStyle!.CalcSize(new GUIContent(bigText)).x;
        GUI.Label(new Rect(rect.x + 18 + bigWidth, rect.y + 55, rect.width - 32 - bigWidth, 18),
            pool == CheatPointPool.Life ? "面板可用" : "可用", cheatSmallStyle);
        GUI.Label(new Rect(rect.x + 14, rect.y + 82, rect.width - 28, 18),
            pool == CheatPointPool.Life
                ? "由生活技能等级换算 · 只读"
                : $"已用 {used} · 总数 {total}", cheatHintStyle);
        GUI.Label(new Rect(rect.x + 14, rect.y + 100, rect.width - 28, 18),
            CheatPointPolicy.Description(pool), cheatHintStyle);

        var wasEnabled = GUI.enabled;
        var writable = CheatPointPolicy.CanWrite(pool);
        GUI.enabled = wasEnabled && writable;
        var input = new Rect(rect.x + 14, rect.y + 130, 62, 30);
        DrawCheatInput("points:" + pool + ":amount", input, "5", cheatCenterInputStyle);
        if (GUI.Button(new Rect(input.xMax + 6, input.y, 34, 30), "+", cheatButtonStyle!))
            ApplyPoints(pool, 1);
        if (GUI.Button(new Rect(input.xMax + 46, input.y, 34, 30), "-", cheatButtonStyle!))
            ApplyPoints(pool, -1);
        var setWidth = Math.Max(68f, rect.width - 14 - (input.xMax + 86 - rect.x));
        if (GUI.Button(new Rect(input.xMax + 86, input.y, setWidth, 30), "设为可用", cheatButtonStyle!))
            ApplyPointAvailable(pool);
        GUI.enabled = wasEnabled;
    }

    private void DrawOtherPage(Rect bounds, CommandPanelState.State state)
    {
        var fishingRect = new Rect(0, 0, bounds.width, 70);
        GUI.Box(fishingRect, GUIContent.none, cheatCardStyle);
        GUI.Label(new Rect(fishingRect.x + 14, fishingRect.y + 14, fishingRect.width - 90, 18),
            "钓鱼直接成功", cheatValueStyle);
        GUI.Label(new Rect(fishingRect.x + 14, fishingRect.y + 36, fishingRect.width - 90, 18),
            "钓鱼开始后进度条自动拉满，并立即结算成功。", cheatHintStyle);
        DrawSwitch(new Rect(fishingRect.xMax - 14 - 46, fishingRect.y + 23, 46, 24),
            state.FishingInstantSuccess, enabled =>
            {
                Plugin.EnableFishingInstantSuccess.Value = enabled;
                Plugin.Tip(enabled ? "钓鱼直接成功已开启" : "钓鱼直接成功已关闭");
            });

        var speed = new Rect(0, 82, bounds.width, 94);
        DrawSectionCard(speed);
        DrawSectionHeader(speed, "移动速度", "仅当前场景，不写存档", null, null, null);
        var rowY = speed.y + 48;
        GUI.Label(new Rect(speed.x + 14, rowY, 100, 32), "当前倍率", cheatLabelStyle);
        GUI.Label(new Rect(speed.x + 126, rowY, 90, 32),
            FormatNumber(CheatEditor.MovementSpeed()) + "x", cheatValueStyle);
        DrawCheatInput("other:speed", new Rect(speed.x + 226, rowY, 76, 32), "2");
        if (GUI.Button(new Rect(speed.x + 312, rowY, 84, 32), "设置", cheatPrimaryButtonStyle!))
            ApplyMovementSpeed();
    }

    private void DrawSwitch(Rect rect, bool on, Action<bool> changed)
    {
        var click = Event.current;
        if (click != null && click.type == EventType.MouseDown && click.button == 0 &&
            rect.Contains(click.mousePosition))
            changed(!on);
        GUI.Box(rect, GUIContent.none, on ? cheatPrimaryButtonStyle! : cheatCellStyle!);
        var knob = new Rect(on ? rect.xMax - 22 : rect.x + 4, rect.y + 3, 18, 18);
        GUI.Box(knob, GUIContent.none, on ? cheatPillOkStyle! : cheatPillStyle!);
    }

    private void DrawCharacterMenu(Rect bounds, Rect pickerRect, IReadOnlyList<CheatCharacterOption> options)
    {
        var width = pickerRect.width;
        var height = Math.Min(250f, options.Count * 34f + 12f);
        var x = pickerRect.x;
        var y = pickerRect.yMax + 4;
        if (y + height > bounds.yMax) y = Math.Max(bounds.y, bounds.yMax - height);
        var menu = new Rect(x, y, width, height);
        GUI.Box(menu, GUIContent.none, cheatMenuStyle);
        var listRect = new Rect(menu.x + 6, menu.y + 6, menu.width - 12, menu.height - 12);
        cheatCharacterMenuScroll = GUI.BeginScrollView(listRect, cheatCharacterMenuScroll,
            new Rect(0, 0, listRect.width - 7, Math.Max(listRect.height, options.Count * 34f)));
        try
        {
        var rowY = 0f;
        foreach (var item in options)
        {
            var row = new Rect(0, rowY, listRect.width - 7, 32);
            var selected = item.Key == selectedCheatCharacter;
            if (GUI.Button(row, GUIContent.none, selected ? cheatPickerItemActiveStyle! : cheatPickerItemStyle!))
            {
                selectedCheatCharacter = item.Key;
                cheatCharacterMenuOpen = false;
                cheatScroll = Vector2.zero;
                ClearCheatDrafts();
            }
            GUI.Label(new Rect(row.x + 10, row.y, row.width - 80, row.height), item.Name, cheatValueStyle);
            GUI.Label(new Rect(row.xMax - 70, row.y, 60, row.height), KindLabel(item.Kind), cheatHintStyle);
            rowY += 34f;
        }
        }
        finally { GUI.EndScrollView(); }
        if (Event.current.type == EventType.MouseDown &&
            !menu.Contains(Event.current.mousePosition) && !pickerRect.Contains(Event.current.mousePosition))
            cheatCharacterMenuOpen = false;
    }

    private void DrawSectionCard(Rect rect) => GUI.Box(rect, GUIContent.none, cheatCardStyle);

    private void DrawSectionHeader(Rect rect, string title, string hint, string? action, Action? onClick,
        string? pill)
    {
        var x = rect.x + 14;
        var y = rect.y + 12;
        GUI.Label(new Rect(x, y, rect.width - 28, 20), title, cheatSectionTitleStyle);
        var titleWidth = cheatSectionTitleStyle!.CalcSize(new GUIContent(title)).x;
        GUI.Label(new Rect(x + titleWidth + 10, y + 2, rect.width - 28 - titleWidth - 10, 18), hint, cheatHintStyle);
        var right = rect.xMax - 14;
        if (action is not null && onClick is not null)
        {
            const float actionWidth = 84f;
            if (GUI.Button(new Rect(right - actionWidth, y, actionWidth, 28), action, cheatSectionActionStyle!))
                onClick();
            right -= actionWidth + 8;
        }
        if (pill is not null)
        {
            var style = cheatPillWarnStyle!;
            var width = PillWidth(pill, style);
            DrawPill(new Rect(right - width, y + 4, width, 20), pill, style);
        }
    }

    private float DrawNotice(float x, float y, float width, string text)
    {
        var textWidth = Math.Max(40f, width - 20f);
        var height = Math.Max(34f, cheatNoticeTextStyle!.CalcHeight(new GUIContent(text), textWidth) + 16f);
        var rect = new Rect(x, y, width, height);
        GUI.Box(rect, GUIContent.none, cheatNoticeBgStyle);
        GUI.Box(new Rect(rect.x, rect.y, 2, rect.height), GUIContent.none, panelLineStyle!);
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, textWidth, rect.height - 16), text, cheatNoticeTextStyle);
        return height;
    }

    private void DrawPill(Rect rect, string text, GUIStyle style) => GUI.Box(rect, text, style);

    private float PillWidth(string text, GUIStyle style) =>
        style.CalcSize(new GUIContent(text)).x + 16f;

    private void DrawDashedBorder(Rect rect)
    {
        const float dash = 6f;
        const float gap = 4f;
        for (var x = rect.x; x < rect.xMax; x += dash + gap)
        {
            var width = Math.Min(dash, rect.xMax - x);
            GUI.Box(new Rect(x, rect.y, width, 1), GUIContent.none, panelLineStyle!);
            GUI.Box(new Rect(x, rect.yMax - 1, width, 1), GUIContent.none, panelLineStyle!);
        }
        for (var y = rect.y; y < rect.yMax; y += dash + gap)
        {
            var height = Math.Min(dash, rect.yMax - y);
            GUI.Box(new Rect(rect.x, y, 1, height), GUIContent.none, panelLineStyle!);
            GUI.Box(new Rect(rect.xMax - 1, y, 1, height), GUIContent.none, panelLineStyle!);
        }
    }

    private void DrawCheatScrollbar(Rect view, Vector2 scroll, float contentHeight)
    {
        if (!PanelScrollbarGeometry.TryCreate(new PanelRect(view.x, view.y, view.width, view.height),
                contentHeight, scroll.y, out var geometry)) return;
        var previous = GUI.color;
        try
        {
            GUI.color = new Color32(13, 20, 30, 133);
            GUI.DrawTexture(UnityRect(geometry.Track), Texture2D.whiteTexture);
            GUI.color = new Color32(93, 182, 197, 160);
            GUI.DrawTexture(UnityRect(geometry.Thumb), Texture2D.whiteTexture);
        }
        finally { GUI.color = previous; }
    }

    private void DrawCheatMessage(float width, float height)
    {
        if (string.IsNullOrEmpty(cheatMessage) || Time.realtimeSinceStartup > cheatMessageUntil) return;
        var content = new GUIContent(cheatMessage);
        var size = cheatToastStyle!.CalcSize(content);
        var rect = new Rect((width - Math.Min(width - 40, size.x + 32)) / 2,
            height - Math.Max(30, size.y + 18) - 34, Math.Min(width - 40, size.x + 32),
            Math.Max(30, size.y + 18));
        GUI.Box(rect, cheatMessage, cheatToastStyle);
    }

    private void SetCheatMessage(string message)
    {
        cheatMessage = message;
        cheatMessageUntil = Time.realtimeSinceStartup + 4f;
    }

    private string DrawCheatInput(string id, Rect rect, string current, GUIStyle? style = null)
    {
        GUI.SetNextControlName(id);
        var draft = CheatDraft(id, current);
        var edited = GUI.TextField(rect, draft, style ?? cheatInputStyle!);
        if (edited != draft) cheatDrafts[id] = edited;
        return edited;
    }

    private string CheatDraft(string id, string current)
    {
        if (!cheatDrafts.TryGetValue(id, out var draft) ||
            !cheatDraftSources.TryGetValue(id, out var source) || source != current)
        {
            if (GUI.GetNameOfFocusedControl() != id)
            {
                draft = current;
                cheatDrafts[id] = draft;
                cheatDraftSources[id] = current;
            }
            else
            {
                draft ??= current;
            }
        }
        return draft;
    }

    private void ClearCheatDrafts()
    {
        cheatDrafts.Clear();
        cheatDraftSources.Clear();
    }

    private static string FormatNumber(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    private static string FormatNumber(int value) => value.ToString(CultureInfo.InvariantCulture);

    private void ApplyExperience(CheatCharacterOption option)
    {
        var id = "char:" + option.Key + ":exp";
        var text = cheatDrafts.GetValueOrDefault(id, "1000");
        if (!CheatInput.TryParseInt(text, out var amount) || amount <= 0)
        {
            SetCheatMessage("经验必须是大于 0 的整数");
            return;
        }
        var warning = CheatEditor.EnsurePreEditCheckpoint();
        var ok = CheatEditor.AddPlayerExp(amount, out var message);
        SetCheatMessage(CombineMessages(warning, message));
        if (ok)
        {
            cheatDrafts.Remove(id);
            cheatDraftSources.Remove(id);
        }
    }

    private void ApplyStats(CheatCharacterOption option, CheatCharacterSnapshot snapshot)
    {
        var drafts = new List<(string Key, int Draft, int Current)>();
        for (var index = 0; index < CheatEditor.AttributeTypes.Length; index++)
        {
            var type = CheatEditor.AttributeTypes[index];
            var current = (int)Math.Round(snapshot.BaseStats[index]);
            var id = StatDraftId(option, type.Value);
            var text = cheatDrafts.GetValueOrDefault(id, FormatNumber(current));
            if (!CheatInput.TryParseInt(text, out var value))
            {
                SetCheatMessage(type.Label + "输入无效");
                return;
            }
            drafts.Add((type.Value.ToString(CultureInfo.InvariantCulture), CheatMath.ClampStat(value), current));
        }
        var changed = CheatBatch.Changed(drafts);
        if (changed.Count == 0)
        {
            SetCheatMessage("六维没有变化");
            return;
        }
        var warning = CheatEditor.EnsurePreEditCheckpoint();
        var failures = new List<string>();
        foreach (var item in changed)
        {
            var type = CheatEditor.AttributeTypes.First(type => type.Value.ToString(CultureInfo.InvariantCulture) == item.Key);
            if (CheatEditor.SetStat(option, (PlayerProType)type.Value, item.Value, out var message))
            {
                var id = StatDraftId(option, type.Value);
                cheatDrafts.Remove(id);
                cheatDraftSources.Remove(id);
            }
            else
            {
                failures.Add(message);
            }
        }
        SetCheatMessage(CombineMessages(warning,
            failures.Count == 0 ? $"已应用 {changed.Count} 项六维修改" : string.Join("；", failures)));
    }

    private void ApplyLevels(CheatCharacterOption option, CheatCharacterSnapshot snapshot, bool martial)
    {
        var types = martial ? CheatEditor.MartialTypes : CheatEditor.LifeTypes;
        var levels = martial ? snapshot.MartialLevels : snapshot.LifeLevels;
        var drafts = new List<(string Key, int Draft, int Current)>();
        for (var index = 0; index < types.Length; index++)
        {
            var type = types[index];
            var current = levels[index];
            var id = LevelDraftId(option, !martial, type.Value);
            var text = cheatDrafts.GetValueOrDefault(id, FormatNumber(current));
            if (!CheatInput.TryParseInt(text, out var value))
            {
                SetCheatMessage(type.Label + "等级输入无效");
                return;
            }
            var action = CheatLevelEditPolicy.Resolve(value, current, out var normalized);
            if (action == CheatLevelEditAction.Reject)
            {
                SetCheatMessage(value < 0 ? type.Label + "等级不能为负数" : type.Label + "等级最低为 1");
                return;
            }
            if (action == CheatLevelEditAction.Skip) continue;
            drafts.Add((type.Value.ToString(CultureInfo.InvariantCulture), normalized, current));
        }
        var changed = CheatBatch.Changed(drafts);
        if (changed.Count == 0)
        {
            SetCheatMessage((martial ? "武学" : "生活技能") + "没有变化");
            return;
        }
        var warning = CheatEditor.EnsurePreEditCheckpoint();
        var failures = new List<string>();
        foreach (var item in changed)
        {
            var type = types.First(type => type.Value.ToString(CultureInfo.InvariantCulture) == item.Key);
            var ok = martial
                ? CheatEditor.SetMartial(option, (KFType)type.Value, item.Value, out var message)
                : CheatEditor.SetLife(option, (LifeType)type.Value, item.Value, out message);
            if (ok)
            {
                var id = LevelDraftId(option, !martial, type.Value);
                cheatDrafts.Remove(id);
                cheatDraftSources.Remove(id);
            }
            else
            {
                failures.Add(message);
            }
        }
        SetCheatMessage(CombineMessages(warning,
            failures.Count == 0 ? $"已应用 {changed.Count} 项{(martial ? "武学" : "生活技能")}修改" : string.Join("；", failures)));
    }

    private void ApplyFavor(CheatCharacterOption option, float sign)
    {
        var id = "char:" + option.Key + ":favor";
        var text = cheatDrafts.GetValueOrDefault(id, "5");
        if (!CheatInput.TryParseFloat(text, out var amount) || amount <= 0)
        {
            SetCheatMessage("好感变化必须是大于 0 的数字");
            return;
        }
        var warning = CheatEditor.EnsurePreEditCheckpoint();
        var ok = CheatEditor.AddFavor(option, sign * amount, out var message);
        SetCheatMessage(CombineMessages(warning, message));
        if (ok)
        {
            cheatDrafts.Remove(id);
            cheatDraftSources.Remove(id);
        }
    }

    private void ApplyPoints(CheatPointPool pool, int sign)
    {
        var id = "points:" + pool + ":amount";
        var text = cheatDrafts.GetValueOrDefault(id, "5");
        if (!CheatInput.TryParseInt(text, out var amount) || amount <= 0)
        {
            SetCheatMessage("点数必须是大于 0 的整数");
            return;
        }
        var warning = CheatEditor.EnsurePreEditCheckpoint();
        var ok = CheatEditor.AddPoints(pool, sign * amount, out var message);
        SetCheatMessage(CombineMessages(warning, message));
        if (ok)
        {
            cheatDrafts.Remove(id);
            cheatDraftSources.Remove(id);
        }
    }

    private void ApplyPointAvailable(CheatPointPool pool)
    {
        var id = "points:" + pool + ":amount";
        var text = cheatDrafts.GetValueOrDefault(id, "5");
        if (!CheatInput.TryParseInt(text, out var amount) || amount < 0)
        {
            SetCheatMessage("可用点数不能小于 0");
            return;
        }
        var warning = CheatEditor.EnsurePreEditCheckpoint();
        var ok = CheatEditor.SetAvailablePoints(pool, amount, out var message);
        SetCheatMessage(CombineMessages(warning, message));
        if (ok)
        {
            cheatDrafts.Remove(id);
            cheatDraftSources.Remove(id);
        }
    }

    private void ApplyMovementSpeed()
    {
        var text = cheatDrafts.GetValueOrDefault("other:speed", "2");
        if (!CheatInput.TryParseFloat(text, out var multiplier) || multiplier <= 0)
        {
            SetCheatMessage("移动速度必须是大于 0 的数字");
            return;
        }
        var ok = CheatEditor.SetMovementSpeed(multiplier, out var message);
        SetCheatMessage(message);
        if (ok)
        {
            cheatDrafts.Remove("other:speed");
            cheatDraftSources.Remove("other:speed");
        }
    }

    private static string CombineMessages(string? warning, string message) =>
        warning is null ? message : warning + "；" + message;

    private static string StatDraftId(CheatCharacterOption option, int type) =>
        "char:" + option.Key + ":stat:" + type;

    private static string LevelDraftId(CheatCharacterOption option, bool life, int type) =>
        "char:" + option.Key + (life ? ":life:" : ":martial:") + type;

    private static string KindLabel(CheatCharacterKind kind) => kind switch
    {
        CheatCharacterKind.Player => "主角",
        CheatCharacterKind.Recruited => "队友",
        _ => "已认识"
    };

    private void EnsureCheatStyles()
    {
        var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height)).Scale;
        if (cheatStyleGeneration.Matches(Screen.width, Screen.height, scale, CheatStylesAlive() && CheatTexturesAlive())) return;
        DestroyCheatTextures();
        var section = new Color32(134, 217, 229, 255);
        var text = new Color32(212, 226, 241, 255);
        var muted = new Color32(145, 164, 190, 255);
        var accent = new Color32(154, 235, 244, 255);
        var warn = new Color32(255, 214, 110, 255);
        var ok = new Color32(118, 224, 165, 255);

        cheatH2Style = LabelStyle(17, section, FontStyle.Bold);
        cheatPageTitleStyle = LabelStyle(16, section, FontStyle.Bold);
        cheatSectionTitleStyle = LabelStyle(14, section, FontStyle.Bold);
        cheatHintStyle = LabelStyle(12, muted);
        cheatSmallStyle = LabelStyle(11, muted);
        cheatTinyStyle = LabelStyle(10, muted);
        cheatLabelStyle = LabelStyle(13, muted);
        cheatValueStyle = LabelStyle(13, text);
        cheatFlowStyle = LabelStyle(11, muted);
        cheatFlowStrongStyle = LabelStyle(11, new Color32(185, 234, 242, 255), FontStyle.Bold);
        cheatBigStyle = LabelStyle(32, new Color32(235, 243, 255, 255), FontStyle.Bold);
        cheatPinkStyle = LabelStyle(20, new Color32(255, 176, 205, 255), FontStyle.Bold);
        cheatNoticeTextStyle = LabelStyle(12, new Color32(228, 207, 157, 255), wrap: true);

        cheatInputStyle = InputStyle(false);
        cheatCenterInputStyle = InputStyle(true);
        cheatButtonStyle = ButtonStyle(new Color32(23, 34, 48, 235), new Color32(37, 56, 75, 245),
            new Color32(30, 46, 62, 245), text, 12);
        cheatPrimaryButtonStyle = ButtonStyle(new Color32(35, 82, 92, 184), new Color32(45, 104, 116, 220),
            new Color32(35, 82, 92, 220), accent, 12);
        cheatSectionActionStyle = ButtonStyle(new Color32(35, 82, 92, 140), new Color32(45, 104, 116, 190),
            new Color32(35, 82, 92, 190), accent, 12);
        cheatPickerStyle = ButtonStyle(new Color32(16, 25, 37, 245), new Color32(23, 36, 51, 245),
            new Color32(20, 30, 42, 245), text, 14, TextAnchor.MiddleLeft);
        cheatPickerItemStyle = ButtonStyle(new Color32(0, 0, 0, 0), new Color32(27, 43, 60, 220),
            new Color32(27, 43, 60, 220), text, 13, TextAnchor.MiddleLeft);
        cheatPickerItemActiveStyle = ButtonStyle(new Color32(35, 64, 76, 235), new Color32(45, 81, 94, 245),
            new Color32(45, 81, 94, 245), accent, 13, TextAnchor.MiddleLeft);

        cheatSidebarStyle = BoxStyle(new Color32(20, 30, 43, 170), new Color32(93, 182, 197, 36), 9);
        cheatSidebarButtonStyle = ButtonStyle(new Color32(20, 29, 42, 210), new Color32(28, 42, 58, 235),
            new Color32(20, 29, 42, 235), new Color32(181, 195, 212, 255), 16, TextAnchor.MiddleCenter);
        cheatSidebarActiveStyle = ButtonStyle(new Color32(35, 64, 76, 235), new Color32(45, 81, 94, 245),
            new Color32(35, 64, 76, 245), accent, 16, TextAnchor.MiddleCenter);
        cheatSidebarDisabledStyle = ButtonStyle(new Color32(23, 34, 48, 140), new Color32(23, 34, 48, 160),
            new Color32(23, 34, 48, 160), new Color32(110, 122, 138, 255), 16, TextAnchor.MiddleCenter);
        cheatCardStyle = BoxStyle(new Color32(26, 36, 51, 168), new Color32(93, 182, 197, 31), 8);
        cheatMenuStyle = BoxStyle(new Color32(14, 22, 33, 250), new Color32(93, 182, 197, 89), 8);
        cheatCellStyle = BoxStyle(new Color32(13, 20, 30, 133), new Color32(120, 170, 190, 26), 6);
        cheatDisabledCellStyle = BoxStyle(new Color32(13, 20, 30, 80), new Color32(120, 170, 190, 20), 6);
        cheatPointCardStyle = BoxStyle(new Color32(26, 36, 51, 168), new Color32(93, 182, 197, 36), 8);
        cheatNoticeBgStyle = BoxStyle(new Color32(255, 214, 110, 15), new Color32(0, 0, 0, 0), 6);
        cheatPillStyle = PillStyle(new Color32(120, 170, 190, 18), new Color32(120, 170, 190, 51), muted);
        cheatPillWarnStyle = PillStyle(new Color32(255, 214, 110, 15), new Color32(255, 214, 110, 71), warn);
        cheatPillOkStyle = PillStyle(new Color32(118, 224, 165, 15), new Color32(118, 224, 165, 71), ok);
        cheatBadgeStyle = PillStyle(new Color32(255, 214, 110, 20), new Color32(255, 214, 110, 64), warn, 10);
        cheatToastStyle = BoxStyle(new Color32(12, 20, 30, 240), new Color32(120, 220, 235, 77), 16,
            accent, 13, TextAnchor.MiddleCenter);
        cheatSwitchOffStyle = BoxStyle(new Color32(38, 52, 68, 255), new Color32(0, 0, 0, 0), 12);
        cheatSwitchOnStyle = BoxStyle(new Color32(45, 104, 116, 230), new Color32(0, 0, 0, 0), 12);
        cheatSwitchOffStyle.hover.background = cheatSwitchOffStyle.normal.background;
        cheatSwitchOffStyle.active.background = cheatSwitchOffStyle.normal.background;
        cheatSwitchOnStyle.hover.background = cheatSwitchOnStyle.normal.background;
        cheatSwitchOnStyle.active.background = cheatSwitchOnStyle.normal.background;
        cheatKnobStyle = BoxStyle(new Color32(159, 176, 194, 255), new Color32(0, 0, 0, 0), 12);
        cheatKnobOnStyle = LabelStyle(17, accent, FontStyle.Bold, TextAnchor.MiddleCenter);
        cheatKnobOffStyle = LabelStyle(17, new Color32(159, 176, 194, 255), FontStyle.Bold, TextAnchor.MiddleCenter);
        cheatEmptyStyle = BoxStyle(new Color32(26, 36, 51, 77), new Color32(93, 182, 197, 56), 10);
        cheatScrollbarStyle = BoxStyle(new Color32(10, 15, 24, 80), new Color32(0, 0, 0, 0), 4);
        cheatScrollbarStyle.fixedWidth = 7f;
        cheatScrollbarThumbStyle = BoxStyle(new Color32(93, 182, 197, 160), new Color32(0, 0, 0, 0), 3);
        cheatScrollbarThumbStyle.fixedWidth = 7f;
        cheatScrollbarEmptyStyle = new GUIStyle { fixedWidth = 7f, fixedHeight = 0f };
        if (!CheatStylesAlive() || !CheatTexturesAlive()) throw new InvalidOperationException("修改面板样式未完整初始化");
        cheatStyleGeneration.Complete(Screen.width, Screen.height, scale);
    }

    private bool CheatTexturesAlive() => cheatTextures.Count > 0 && cheatTextures.All(texture => texture != null);

    private bool CheatStylesAlive() => cheatH2Style is not null && cheatPageTitleStyle is not null &&
        cheatSectionTitleStyle is not null && cheatHintStyle is not null && cheatSmallStyle is not null &&
        cheatTinyStyle is not null && cheatLabelStyle is not null && cheatValueStyle is not null &&
        cheatFlowStyle is not null && cheatFlowStrongStyle is not null && cheatBigStyle is not null &&
        cheatPinkStyle is not null && cheatInputStyle is not null && cheatCenterInputStyle is not null &&
        cheatButtonStyle is not null && cheatPrimaryButtonStyle is not null && cheatSectionActionStyle is not null &&
        cheatPickerStyle is not null && cheatPickerItemStyle is not null && cheatPickerItemActiveStyle is not null &&
        cheatSidebarStyle is not null && cheatSidebarButtonStyle is not null && cheatSidebarActiveStyle is not null &&
        cheatSidebarDisabledStyle is not null && cheatCardStyle is not null && cheatMenuStyle is not null &&
        cheatCellStyle is not null && cheatDisabledCellStyle is not null && cheatPointCardStyle is not null &&
        cheatNoticeBgStyle is not null && cheatNoticeTextStyle is not null && cheatPillStyle is not null &&
        cheatPillWarnStyle is not null && cheatPillOkStyle is not null && cheatBadgeStyle is not null &&
        cheatToastStyle is not null && cheatSwitchOffStyle is not null && cheatSwitchOnStyle is not null &&
        cheatKnobStyle is not null && cheatKnobOnStyle is not null && cheatKnobOffStyle is not null &&
        cheatEmptyStyle is not null && cheatScrollbarStyle is not null && cheatScrollbarThumbStyle is not null &&
        cheatScrollbarEmptyStyle is not null;

    private GUIStyle LabelStyle(int size, Color32 color, FontStyle fontStyle = FontStyle.Normal,
        TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false) =>
        new(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = fontStyle,
            alignment = anchor,
            wordWrap = wrap,
            padding = new RectOffset(0, 0, 0, 0),
            normal = { textColor = color }
        };

    private GUIStyle InputStyle(bool centered)
    {
        var style = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 13,
            alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft,
            padding = centered ? new RectOffset(2, 2, 0, 0) : new RectOffset(9, 9, 0, 0),
            border = new RectOffset(10, 10, 10, 10)
        };
        style.normal.background = TrackTexture(RoundedTexture(new Color32(13, 20, 30, 255),
            new Color32(42, 58, 76, 255), 5));
        style.hover.background = style.normal.background;
        style.focused.background = style.normal.background;
        style.normal.textColor = new Color32(212, 226, 241, 255);
        style.focused.textColor = style.normal.textColor;
        return style;
    }

    private GUIStyle ButtonStyle(Color32 normalFill, Color32 hoverFill, Color32 activeFill,
        Color32 textColor, int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
    {
        var style = new GUIStyle
        {
            fontSize = fontSize,
            alignment = alignment,
            border = new RectOffset(8, 8, 8, 8),
            padding = new RectOffset(10, 10, 0, 0),
            clipping = TextClipping.Clip
        };
        style.normal.background = TrackTexture(RoundedTexture(normalFill, new Color32(120, 170, 190, 51), 5));
        style.hover.background = TrackTexture(RoundedTexture(hoverFill, new Color32(120, 220, 235, 65), 5));
        style.active.background = TrackTexture(RoundedTexture(activeFill, new Color32(120, 220, 235, 65), 5));
        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.active.textColor = textColor;
        return style;
    }

    private GUIStyle BoxStyle(Color32 fill, Color32 border, int radius, Color32? textColor = null,
        int fontSize = 12, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var style = new GUIStyle
        {
            fontSize = fontSize,
            alignment = alignment,
            border = new RectOffset(radius + 2, radius + 2, radius + 2, radius + 2),
            padding = new RectOffset(radius, radius, 0, 0)
        };
        style.normal.background = TrackTexture(RoundedTexture(fill, border, radius));
        if (textColor.HasValue) style.normal.textColor = textColor.Value;
        return style;
    }

    private GUIStyle PillStyle(Color32 fill, Color32 border, Color32 textColor, int fontSize = 11)
    {
        var style = new GUIStyle
        {
            fontSize = fontSize,
            alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(12, 12, 12, 12),
            padding = new RectOffset(8, 8, 0, 0)
        };
        style.normal.background = TrackTexture(RoundedTexture(fill, border, 12));
        style.normal.textColor = textColor;
        return style;
    }

    private Texture2D TrackTexture(Texture2D texture)
    {
        cheatTextures.Add(texture);
        return texture;
    }

    private static Texture2D RoundedTexture(Color32 fill, Color32 border, int radius)
    {
        const int size = 32;
        radius = PanelTextureGeometry.ClampCornerRadius(radius, size);
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x - Math.Clamp(x, radius, size - 1 - radius);
            var dy = y - Math.Clamp(y, radius, size - 1 - radius);
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var inside = distance <= radius + 0.35;
            var inner = distance <= radius - 1.35;
            pixels[y * size + x] = inside ? (inner ? fill : border) : new Color32(0, 0, 0, 0);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private void DestroyCheatTextures()
    {
        cheatStyleGeneration.Invalidate();
        foreach (var texture in cheatTextures)
            if (texture != null) UnityEngine.Object.Destroy(texture);
        cheatTextures.Clear();
        cheatH2Style = null;
        cheatPageTitleStyle = null;
        cheatSectionTitleStyle = null;
        cheatHintStyle = null;
        cheatSmallStyle = null;
        cheatTinyStyle = null;
        cheatLabelStyle = null;
        cheatValueStyle = null;
        cheatFlowStyle = null;
        cheatFlowStrongStyle = null;
        cheatBigStyle = null;
        cheatPinkStyle = null;
        cheatInputStyle = null;
        cheatCenterInputStyle = null;
        cheatButtonStyle = null;
        cheatPrimaryButtonStyle = null;
        cheatSectionActionStyle = null;
        cheatPickerStyle = null;
        cheatPickerItemStyle = null;
        cheatPickerItemActiveStyle = null;
        cheatSidebarStyle = null;
        cheatSidebarButtonStyle = null;
        cheatSidebarActiveStyle = null;
        cheatSidebarDisabledStyle = null;
        cheatCardStyle = null;
        cheatMenuStyle = null;
        cheatCellStyle = null;
        cheatDisabledCellStyle = null;
        cheatPointCardStyle = null;
        cheatNoticeBgStyle = null;
        cheatNoticeTextStyle = null;
        cheatPillStyle = null;
        cheatPillWarnStyle = null;
        cheatPillOkStyle = null;
        cheatBadgeStyle = null;
        cheatToastStyle = null;
        cheatSwitchOffStyle = null;
        cheatSwitchOnStyle = null;
        cheatKnobStyle = null;
        cheatKnobOnStyle = null;
        cheatKnobOffStyle = null;
        cheatEmptyStyle = null;
        cheatScrollbarStyle = null;
        cheatScrollbarThumbStyle = null;
        cheatScrollbarEmptyStyle = null;
    }
}
