namespace BeastSagaBetter.Core;

public static class CommandPanelState
{
    public static bool Open { get; set; }
    public static CommandPanelTab Tab { get; private set; } = CommandPanelTab.Status;
    public static CheatSection CheatSection { get; private set; } = CheatSectionCatalog.Default;
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
        CheckpointSummary? ArchiveSelection = null,
        bool FishingInstantSuccess = false)
    {
        public string Status => Faulted ? "插件异常" : CheckpointsActive ? "存读档可用" : "存读档受限";

        public string Mode => Faulted ? "本次运行已停用" : IsTestCopy ? "隔离副本" : "正式目录";

        public bool CanSave => CheckpointsActive && !Faulted && !Busy && !Loading && (!Dialogue || DialogueCheckpoint != null);

        public bool CanLoad => CheckpointsActive && !Faulted && !Busy && !Loading;

        public string DialogueCheckpointText => DialogueCheckpoint is not null
            ? "已建立"
            : Archive?.Any(item => item.Kind == CheckpointKind.PreDialogue) == true
                ? "本轮未建立（存档库有历史）"
                : "未建立";

        public string SelectedCheckpointText
        {
            get
            {
                if (SelectedCheckpoint is null) return "未选择";
                var selected = Archive?.FirstOrDefault(item => item.Generation == SelectedCheckpoint);
                if (selected is null) return SelectedCheckpoint;
                return selected.Kind == CheckpointKind.PreDialogue
                    ? "对话前 · " + (string.IsNullOrWhiteSpace(selected.Conversation) ? "未命名" : selected.Conversation)
                    : "手动 · " + selected.DisplayFullTime();
            }
        }

        public string BlockedText => Faulted ? "上次操作异常，本次运行已停用存读档"
            : Busy ? "正在存读档"
            : Loading ? "正在读取检查点"
            : BlockedReason ?? "安全检查通过";

        public string ArchiveCountText
        {
            get
            {
                var manual = Archive?.Count(item => item.Kind == CheckpointKind.Manual) ?? 0;
                var dialogue = Archive?.Count(item => item.Kind == CheckpointKind.PreDialogue) ?? 0;
                return manual + dialogue == 0
                    ? "暂无存档"
                    : $"共 {manual + dialogue} 份 · 手动 {manual} · 对话前 {dialogue}";
            }
        }
    }

    public static void Toggle(bool focused, bool inputFieldFocused, bool canShowPanel = true)
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

    public static void SelectCheatSection(CheatSection section)
    {
        if (CheatSectionCatalog.IsAvailable(section)) CheatSection = section;
    }

    public static void Close()
    {
        Open = false;
        ClearPointerBounds();
    }

    public static void Reset()
    {
        Close();
        Tab = CommandPanelTab.Status;
        CheatSection = CheatSectionCatalog.Default;
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

public enum CommandPanelTab { Status, Archive, Cheats }

public enum CheckpointArchiveSortMode { CapturedTime, GameTime }

public enum CheckpointArchiveFilter { All, Manual, PreDialogue }

public static class CheckpointArchiveView
{
    public static IReadOnlyList<CheckpointSummary> Apply(
        IEnumerable<CheckpointSummary> archive, CheckpointArchiveFilter filter, CheckpointArchiveSortMode mode)
    {
        var filtered = archive.Where(item => (item.Kind is CheckpointKind.Manual or CheckpointKind.PreDialogue) &&
            (filter switch
            {
                CheckpointArchiveFilter.All => true,
                CheckpointArchiveFilter.Manual => item.Kind == CheckpointKind.Manual,
                CheckpointArchiveFilter.PreDialogue => item.Kind == CheckpointKind.PreDialogue,
                _ => false
            }));
        return CheckpointArchiveSort.Apply(filtered, mode);
    }
}

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

    public static string DisplayFullTime(this CheckpointSummary summary) =>
        summary.CapturedAt == default ? "时间未知" : summary.CapturedAt.ToString("yyyy/MM/dd HH:mm:ss");

    public static string DisplayTopLine(this CheckpointSummary summary) =>
        summary.CapturedAt == default
            ? "时间未知"
            : summary.Kind == CheckpointKind.PreDialogue && !string.IsNullOrWhiteSpace(summary.Conversation)
                ? $"{summary.CapturedAt:MM/dd HH:mm:ss} · {summary.Conversation}"
                : summary.DisplayFullTime();

    public static string DisplayDetailLine(this CheckpointSummary summary) =>
        $"{summary.MapName ?? "未知地图"} · Lv.{summary.PlayerLevel?.ToString() ?? "?"} · 第{summary.Round?.ToString() ?? "?"}周目 第{summary.Day?.ToString() ?? "?"}天";

    public static string NoteText(this CheckpointSummary summary) =>
        string.IsNullOrWhiteSpace(summary.Note) ? "未写备注" : summary.Note!;

    public static string BadgeText(this CheckpointSummary summary) => summary.Kind == CheckpointKind.PreDialogue
        ? summary.Selected ? "对话前·目标" : !string.IsNullOrWhiteSpace(summary.Note) ? "对话前·备注" : "对话前"
        : !string.IsNullOrWhiteSpace(summary.Note) ? "已备注"
        : summary.Selected ? "读取目标"
        : summary.HasScreenshot ? "F5 历史"
        : "无截图";

    public static string KindText(this CheckpointSummary summary) => summary.Kind switch
    {
        CheckpointKind.Manual => "手动 F5 存档",
        CheckpointKind.PreDialogue => "对话前自动检查点",
        _ => "旧版检查点"
    };
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
    float ContentScale,
    float ArchiveListWidth,
    float DetailPreviewHeight,
    float ArchiveRowHeight,
    float ArchiveRowSpacing,
    float MajorButtonHeight)
{
    public static CommandPanelMetrics Create(CommandPanelLayout layout)
    {
        var scale = Math.Clamp(layout.Height / 720f, 0.72f, 3f);
        // The full-screen chrome follows the display scale, while page content stops
        // growing at 1080p-equivalent sizes so 1440p/4K keep a useful information density.
        var contentScale = Math.Min(scale, 1.5f);
        var listMaximum = Math.Max(0, Math.Min(420f * contentScale, layout.Width * 0.38f));
        var listMinimum = Math.Min(280f * contentScale, listMaximum);
        var previewMaximum = Math.Max(0, Math.Min(480f * contentScale, layout.Height * 0.64f));
        var previewMinimum = Math.Min(260f * contentScale, previewMaximum);
        return new(
            scale,
            contentScale,
            Math.Clamp(layout.Width * 0.26f, listMinimum, listMaximum),
            Math.Clamp(layout.Height * 0.56f, previewMinimum, previewMaximum),
            96f * contentScale,
            Math.Max(6f, 9f * contentScale),
            46f * contentScale);
    }
}
