namespace SafeDialogueTools.Core;

public static class CommandPanelState
{
    public static bool Open { get; set; }
    public static CommandPanelTab Tab { get; private set; } = CommandPanelTab.Status;
    private static (float X, float Y, float Width, float Height) pointerBounds;

    public sealed record State(
        bool CheckpointsActive,
        bool Dialogue,
        bool Busy,
        bool Loading,
        bool Faulted,
        bool IndicatorsEnabled,
        bool IsTestCopy,
        string? DialogueCheckpoint,
        string? SelectedCheckpoint,
        string? BlockedReason,
        IReadOnlyList<CheckpointSummary>? Archive = null,
        CheckpointSummary? ArchiveSelection = null)
    {
        public string Status => Faulted ? "插件异常" : CheckpointsActive ? "存读档可用" : "存读档受限";

        public string Mode => Faulted ? "本次运行已停用" : IsTestCopy ? "隔离副本" : "正式目录";

        public bool CanSave => CheckpointsActive && !Faulted && !Busy && !Loading && (!Dialogue || DialogueCheckpoint != null);

        public bool CanLoad => CheckpointsActive && !Faulted && !Busy && !Loading;

        public string DialogueCheckpointText => DialogueCheckpoint is null ? "未建立" : "已建立";

        public string SelectedCheckpointText => SelectedCheckpoint is null ? "未选择" : SelectedCheckpoint;

        public string BlockedText => Faulted ? "上次操作异常，本次运行已停用存读档"
            : Busy ? "正在存读档"
            : Loading ? "正在读取检查点"
            : BlockedReason ?? "安全检查通过";

        public string ArchiveCountText => Archive?.Count(item => item.Kind == CheckpointKind.Manual) is { } count and > 0
            ? $"{count} 份手动存档"
            : "暂无手动存档";
    }

    public static void ToggleHome(bool focused, bool inputFieldFocused, bool canShowPanel = true)
    {
        if (!focused || inputFieldFocused) return;
        if (!canShowPanel) Open = false;
        else Open = !Open;
    }

    public sealed record Actions(Action Save, Action Load);

    public static void RunSave(State state, Actions actions)
    {
        if (state.CanSave) actions.Save();
    }

    public static void RunLoad(State state, Actions actions)
    {
        if (state.CanLoad) actions.Load();
    }

    public static void SelectTab(CommandPanelTab tab)
    {
        Tab = tab;
    }

    public static void Reset()
    {
        Open = false;
        Tab = CommandPanelTab.Status;
    }

    public static void SetPointerBounds(float x, float y, float width, float height) =>
        pointerBounds = (x, y, width, height);

    public static void ClearPointerBounds() => pointerBounds = default;

    public static bool IsPointerBlocked(float x, float y) =>
        pointerBounds.Width > 0 && pointerBounds.Height > 0 &&
        x >= pointerBounds.X && x < pointerBounds.X + pointerBounds.Width &&
        y >= pointerBounds.Y && y < pointerBounds.Y + pointerBounds.Height;

    public static bool ShouldBlockMouseWheel(string axis, float x, float y) =>
        axis == "Mouse ScrollWheel" && Open && IsPointerBlocked(x, y);
}

public enum CommandPanelTab { Status, Archive }

public enum CheckpointArchiveSortMode { CapturedTime, GameTime }

public static class CheckpointArchiveSort
{
    public static IReadOnlyList<CheckpointSummary> Apply(
        IEnumerable<CheckpointSummary> archive, CheckpointArchiveSortMode mode) =>
        mode switch
        {
            CheckpointArchiveSortMode.CapturedTime => archive
                .OrderByDescending(item => item.CapturedAt)
                .ThenByDescending(item => item.Generation, StringComparer.Ordinal)
                .ToArray(),
            CheckpointArchiveSortMode.GameTime => archive
                .OrderBy(item => item.Round is null || item.Day is null)
                .ThenByDescending(item => item.Round ?? int.MinValue)
                .ThenByDescending(item => item.CapturedAt)
                .ThenByDescending(item => item.Day ?? int.MinValue)
                .ThenByDescending(item => item.Generation, StringComparer.Ordinal)
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
}

public static class CheckpointSummaryText
{
    public static string DisplayLine(this CheckpointSummary summary) =>
        $"{summary.CapturedAt:yyyy/MM/dd HH:mm:ss} · {summary.MapName ?? "未知地图"} · Lv.{summary.PlayerLevel?.ToString() ?? "?"} · 第{summary.Round?.ToString() ?? "?"}周目 第{summary.Day?.ToString() ?? "?"}天";

    public static string DisplayTopLine(this CheckpointSummary summary) =>
        summary.CapturedAt == default ? "时间未知" : summary.CapturedAt.ToString("yyyy/MM/dd HH:mm:ss");

    public static string DisplayDetailLine(this CheckpointSummary summary) =>
        $"{summary.MapName ?? "未知地图"} · Lv.{summary.PlayerLevel?.ToString() ?? "?"} · 第{summary.Round?.ToString() ?? "?"}周目 第{summary.Day?.ToString() ?? "?"}天";

    public static string NoteText(this CheckpointSummary summary) =>
        string.IsNullOrWhiteSpace(summary.Note) ? "未写备注" : summary.Note!;

    public static string BadgeText(this CheckpointSummary summary) =>
        !string.IsNullOrWhiteSpace(summary.Note) ? "已备注" : summary.Selected ? "读取目标" : summary.HasScreenshot ? "F5 历史" : "无截图";
}

public readonly record struct CommandPanelLayout(float X, float Y, float Width, float Height)
{
    public static CommandPanelLayout Create(int screenWidth, int screenHeight)
    {
        return new(0, 0, Math.Max(0, screenWidth), Math.Max(0, screenHeight));
    }
}

public readonly record struct CommandPanelMetrics(
    float Scale,
    float ArchiveListWidth,
    float DetailPreviewHeight,
    float ArchiveRowHeight,
    float ArchiveRowSpacing,
    float MajorButtonHeight)
{
    public static CommandPanelMetrics Create(CommandPanelLayout layout)
    {
        var scale = Math.Clamp(layout.Height / 720f, 0.72f, 1.35f);
        return new(
            scale,
            Math.Clamp(layout.Width * 0.26f, 280f, Math.Min(760f, layout.Width * 0.38f)),
            Math.Clamp(layout.Height * 0.56f, 260f, Math.Min(900f, layout.Height * 0.64f)),
            Math.Clamp(96f * scale, 84f, 136f),
            Math.Max(6f, 9f * scale),
            Math.Clamp(46f * scale, 44f, 64f));
    }
}
