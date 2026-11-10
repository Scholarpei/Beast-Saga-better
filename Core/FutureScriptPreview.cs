using System.Globalization;
using System.Text.RegularExpressions;

namespace SafeDialogueTools.Core;

public sealed record FutureScriptResult(IReadOnlyList<FutureScriptEffect> Effects, string? StopReason);

public sealed record FutureScriptEffect(string Kind, string Target, double Amount);

public static class FutureScriptPreview
{
    private static readonly Regex StatementSeparator = new(@"\s*;\s*|\r?\n+", RegexOptions.CultureInvariant);

    private static readonly Regex AwardItemPattern = new(@"\AAwardItem\s*\(\s*(['""])(?<item>[^'""]+)\1\s*,\s*(?<amount>[-+]?\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex AwardEquipPattern = new(@"\AAwardEquip\s*\(\s*(['""])(?<item>[^'""]+)\1\s*,\s*(?<amount>[-+]?\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex AddFriendLoveNumPattern = new(@"\AAddFriendLoveNum\s*\(\s*(['""])(?<name>[^'""]+)\1\s*,\s*(?<amount>[-+]?\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex VariableDeltaPattern = new(@"\AVariable\s*\[\s*(['""])(?<name>[^'""]+)\1\s*\]\s*=\s*Variable\s*\[\s*(['""])(?<same>[^'""]+)\2\s*\]\s*(?<operation>[+-])\s*(?<amount>\d+(?:\.\d+)?)\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex VariableConstantPattern = new(@"\AVariable\s*\[\s*(['""])(?<name>[^'""]+)\1\s*\]\s*=\s*(?<value>true|false|-?\d+(?:\.\d+)?)\s*\z", RegexOptions.CultureInvariant);

    private static readonly Regex QuestStatePattern = new(@"\ASetQuest(?:Entry)?State\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex QuestNamePattern = new(@"\ASetQuest(?:Entry)?State\s*\(\s*(['""])(?<quest>[^'""]+)\1\s*,", RegexOptions.CultureInvariant);

    public static FutureScriptResult Parse(string? script)
    {
        var effects = new List<FutureScriptEffect>();
        foreach (var rawStatement in StatementSeparator.Split(script ?? ""))
        {
            var statement = rawStatement.Trim();
            if (statement.Length == 0) continue;
            if (Award(statement, AwardItemPattern, "item", out var itemEffect)) { effects.Add(itemEffect); continue; }
            if (Award(statement, AwardEquipPattern, "equip", out var equipEffect)) { effects.Add(equipEffect); continue; }
            var love = AddFriendLoveNumPattern.Match(statement);
            if (love.Success && Amount(love.Groups["amount"].Value, out var loveAmount))
            {
                effects.Add(new("love", love.Groups["name"].Value, loveAmount));
                continue;
            }
            var delta = VariableDeltaPattern.Match(statement);
            if (delta.Success && delta.Groups["name"].Value == delta.Groups["same"].Value &&
                Number(delta.Groups["amount"].Value, out var magnitude))
            {
                var amount = delta.Groups["operation"].Value == "-" ? -magnitude : magnitude;
                effects.Add(new("variable", delta.Groups["name"].Value, amount));
                continue;
            }
            var constant = VariableConstantPattern.Match(statement);
            if (constant.Success) continue;
            var quest = QuestNamePattern.Match(statement);
            if (quest.Success)
            {
                effects.Add(new("quest", quest.Groups["quest"].Value, 0));
                continue;
            }
            return new(effects, "后续脚本未知，未继续预测");
        }
        return new(effects, null);
    }

    private static bool Award(string statement, Regex regex, string kind, out FutureScriptEffect effect)
    {
        effect = null!;
        var match = regex.Match(statement);
        if (!match.Success || !Amount(match.Groups["amount"].Value, out var amount)) return false;
        effect = new(kind, match.Groups["item"].Value, amount);
        return true;
    }

    private static bool Amount(string value, out double amount) =>
        Number(value, out amount) && Math.Abs(amount) >= 1 && Math.Truncate(amount) == amount;

    private static bool Number(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
