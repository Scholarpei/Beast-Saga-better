using System.Globalization;
using System.Text.RegularExpressions;

namespace SafeDialogueTools.Core;

public sealed record CheckResult(bool? Passed, string Text);
public sealed record CheckSpec(string Function, string Attribute, string Operation, double Threshold, bool Expected);

public static class CheckPreview
{
    private static readonly Regex SingleCheck = new(
        "\\A\\s*(IsPlayerPro|IsPlayerLife|IsPlayer2Life)\\s*\\(\\s*(['\"])([^'\"\\r\\n]+)\\2\\s*,\\s*(['\"])([^'\"\\r\\n]+)\\4\\s*,\\s*(-?\\d+(?:\\.\\d+)?)\\s*\\)\\s*(?:==\\s*(true|false))?\\s*\\z",
        RegexOptions.CultureInvariant);

    public static CheckSpec? Parse(string expression)
    {
        var match = SingleCheck.Match(expression ?? "");
        if (!match.Success || !double.TryParse(match.Groups[6].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) || !double.IsFinite(threshold)) return null;
        return new(match.Groups[1].Value, match.Groups[3].Value, match.Groups[5].Value, threshold, match.Groups[7].Value != "false");
    }

    public static CheckResult Evaluate(string expression, Func<string, string, double?> read)
    {
        var match = SingleCheck.Match(expression ?? "");
        if (!match.Success) return new(null, "? 暂无法判断");
        var function = match.Groups[1].Value;
        var attribute = match.Groups[3].Value;
        var operation = match.Groups[5].Value;
        if (operation is not ("大于等于" or "小于等于" or "等于") ||
            (function != "IsPlayerPro" && operation != "大于等于")) return new(null, "? 暂无法判断");
        if (!double.TryParse(match.Groups[6].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) || !double.IsFinite(threshold))
            return new(null, "? 暂无法判断");
        var current = read(function, attribute);
        if (current is null || !double.IsFinite(current.Value)) return new(null, $"? {attribute}：暂无法判断");
        var passed = operation switch
        {
            "大于等于" => current >= threshold,
            "小于等于" => current <= threshold,
            _ => current == threshold
        };
        var comparison = operation switch { "大于等于" => "≥", "小于等于" => "≤", _ => "=" };
        var branch = match.Groups[7].Value != "false" ? passed : !passed;
        var suffix = match.Groups[7].Value == "false" ? "（失败分支条件）" : "";
        return new(branch, $"{(branch ? "✓" : "✗")} {attribute} {current.Value.ToString("0.##", CultureInfo.InvariantCulture)} {comparison} {threshold.ToString("0.##", CultureInfo.InvariantCulture)}{suffix}");
    }
}
