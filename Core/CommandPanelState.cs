namespace SafeDialogueTools.Core;

public static class CommandPanelState
{
    public static bool Open { get; set; }

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
        string? BlockedReason)
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
    }

    public static void ToggleHome(bool focused, bool inputFieldFocused)
    {
        if (focused && !inputFieldFocused) Open = !Open;
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
}

public readonly record struct CommandPanelLayout(float X, float Y, float Width, float Height)
{
    public static CommandPanelLayout Create(int screenWidth, int screenHeight)
    {
        var width = Math.Clamp(Math.Max(screenWidth * 0.3f, 320), 0, Math.Min(430, Math.Max(0, screenWidth - 32)));
        var height = Math.Min(506, screenHeight - 32);
        return new(Math.Max(16, screenWidth * 0.06f), (screenHeight - height) / 2f, width, height);
    }
}
