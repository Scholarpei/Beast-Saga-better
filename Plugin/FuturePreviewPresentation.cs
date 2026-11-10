namespace SafeDialogueTools;

internal enum FuturePreviewKind
{
    Title,
    Check,
    Affection,
    Gain,
    Loss,
    Equipment,
    State,
    Unknown,
    Boundary
}

internal sealed record FuturePreviewLine(string Text, FuturePreviewKind Kind);

internal static class FuturePreviewPresentation
{
    internal static FuturePreviewLine Classify(string line) => line switch
    {
        "预测未来" => new(line, FuturePreviewKind.Title),
        _ when line.StartsWith("✓") || line.StartsWith("✗") || line.StartsWith("? 判定") => new(line, FuturePreviewKind.Check),
        _ when line.StartsWith("好感：") => new(line, FuturePreviewKind.Affection),
        _ when line.StartsWith("获得装备：") || line.StartsWith("失去装备：") => new(line, FuturePreviewKind.Equipment),
        _ when line.StartsWith("获得：") => new(line, FuturePreviewKind.Gain),
        _ when line.StartsWith("失去：") => new(line, FuturePreviewKind.Loss),
        _ when line.StartsWith("变量：") || line.StartsWith("剧情：") || line.StartsWith("任务：") => new(line, FuturePreviewKind.State),
        _ when line.StartsWith("?") => new(line, FuturePreviewKind.Unknown),
        _ when line.StartsWith("（") && line.EndsWith("）") => new(line, FuturePreviewKind.Boundary),
        _ => new(line, FuturePreviewKind.State)
    };
}
