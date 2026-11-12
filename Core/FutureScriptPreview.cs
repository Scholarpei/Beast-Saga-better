using System.Globalization;
using System.Text.RegularExpressions;

namespace BeastSagaBetter.Core;

public sealed record FutureScriptResult(IReadOnlyList<FutureScriptEffect> Effects, string? StopReason);

public sealed record FutureScriptEffect(string Kind, string Target, double Amount, string? Detail = null);

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
    private static readonly Regex TrailingSemicolon = new(@";\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex QuestStatePattern2 = new(@"\ASetQuestState\s*\(\s*(?<quote>['""])(?<quest>[^'""]+)\k<quote>\s*,\s*(?<quote2>['""])(?<state>unassigned|active|success|failure)\k<quote2>\s*\)\s*\z", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
    private static readonly Regex QuestEntryStatePattern = new(@"\ASetQuestEntryState\s*\(\s*(?<quote>['""])(?<quest>[^'""]+)\k<quote>\s*,\s*(?<entry>\d+)\s*,\s*(?<quote2>['""])(?<state>unassigned|active|success|failure)\k<quote2>\s*\)\s*\z", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
    private static readonly Regex AwardNumericPattern = new(@"\AAwardExp\s*\(\s*(?<amount>[-+]?\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex AwardNamedAmountPattern = new(@"\AAwardLifeExp\s*\(\s*(['""])(?<name>[^'""]+)\1\s*,\s*(?<amount>[-+]?\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex AwardAttributePattern = new(@"\AAwardPro\s*\(\s*(['""])(?<name>[^'""]+)\1\s*,\s*(?<amount>[-+]?\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex AwardWuXueExpPattern = new(@"\AAwardWuXueExp\s*\(\s*(['""])(?<name>[^'""]+)\1\s*,\s*(?<from>\d+(?:\.\d+)?)\s*,\s*(?<to>\d+(?:\.\d+)?)\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex PartyMemberPattern = new(@"\A(?:FriendAdd|FriendLeave)\s*\(\s*(['""])(?<name>[^'""]+)\1\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex KongFuPattern = new(@"\AAwardKongFu\s*\(\s*(['""])(?<name>[^'""]+)\1\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex InKongFuPattern = new(@"\AAwardInKongFu\s*\(\s*(['""])(?<name>[^'""]+)\1\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex KongFuSkillPattern = new(@"\AAwardKongFuSkill\s*\(\s*(['""])(?<name>[^'""]+)\1\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex FriendKongFuPattern = new(@"\AAwardFriendKongFu\s*\(\s*(['""])(?<friend>[^'""]+)\1\s*,\s*(['""])(?<name>[^'""]+)\2\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex FriendKongFuSkillPattern = new(@"\AAwardFriendKongFuSkill\s*\(\s*(['""])(?<friend>[^'""]+)\1\s*,\s*(['""])(?<name>[^'""]+)\2\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex MapPattern = new(@"\AGoOtherMap\s*\(\s*(['""])(?<name>[^'""]+)\1\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex UnknownMapPattern = new(@"\AGo(?:Can|Not|Back)OtherMap\s*\(\s*\)\s*\z", RegexOptions.CultureInvariant);
    private static readonly Regex OpenShopPattern = new(@"\AOpenShop\s*\(\s*(['""])(?<name>[^'""]+)\1\s*\)\s*\z", RegexOptions.CultureInvariant);

    public static FutureScriptResult Parse(string? script)
    {
        var effects = new List<FutureScriptEffect>();
        var normalized = TrailingSemicolon.Replace(script ?? "", "");
        foreach (var rawStatement in StatementSeparator.Split(normalized))
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
            if (constant.Success)
            {
                if (constant.Groups["name"].Value.Contains("好感", StringComparison.Ordinal))
                {
                    continue;
                }
                return new(effects, "剧情变量赋值后，后续状态未知，未继续预测");
            }
            var questState = QuestStatePattern2.Match(statement);
            if (questState.Success)
            {
                effects.Add(new("quest", questState.Groups["quest"].Value, 0, "→ " + questState.Groups["state"].Value));
                continue;
            }
            var questEntry = QuestEntryStatePattern.Match(statement);
            if (questEntry.Success)
            {
                effects.Add(new("quest", questEntry.Groups["quest"].Value, questEntry.Groups["entry"].Value.Length,
                    "第" + questEntry.Groups["entry"].Value + "步 → " + questEntry.Groups["state"].Value));
                continue;
            }
            if (SimpleNumeric(statement, AwardNumericPattern, "exp", out var exp)) { effects.Add(exp); continue; }
            if (SimpleNamedAmount(statement, AwardNamedAmountPattern, "life-exp", out var lifeExp)) { effects.Add(lifeExp); continue; }
            if (SimpleNamedAmount(statement, AwardAttributePattern, "attribute", out var attribute)) { effects.Add(attribute); continue; }
            var martial = AwardWuXueExpPattern.Match(statement);
            if (martial.Success && Number(martial.Groups["from"].Value, out var from) && Number(martial.Groups["to"].Value, out var to))
            {
                effects.Add(new("martial-flow", martial.Groups["name"].Value, to, Format(from) + " → " + Format(to)));
                continue;
            }
            var party = PartyMemberPattern.Match(statement);
            if (party.Success)
            {
                effects.Add(new(statement.StartsWith("FriendAdd", StringComparison.Ordinal) ? "friend-add" : "friend-leave", party.Groups["name"].Value, 1));
                continue;
            }
            if (SimpleNamed(statement, KongFuPattern, "kungfu", out var kungfu)) { effects.Add(kungfu); continue; }
            if (SimpleNamed(statement, InKongFuPattern, "in-kungfu", out var inKongfu)) { effects.Add(inKongfu); continue; }
            if (SimpleNamed(statement, KongFuSkillPattern, "kungfu-skill", out var kungfuSkill)) { effects.Add(kungfuSkill); continue; }
            var friendKungfu = FriendKongFuPattern.Match(statement);
            if (friendKungfu.Success)
            {
                effects.Add(new("friend-kungfu", friendKungfu.Groups["friend"].Value, 1, friendKungfu.Groups["name"].Value));
                continue;
            }
            var friendKungfuSkill = FriendKongFuSkillPattern.Match(statement);
            if (friendKungfuSkill.Success)
            {
                effects.Add(new("friend-kungfu-skill", friendKungfuSkill.Groups["friend"].Value, 1, friendKungfuSkill.Groups["name"].Value));
                continue;
            }
            if (SimpleNamed(statement, MapPattern, "map", out var map)) { effects.Add(map); continue; }
            if (UnknownMapPattern.IsMatch(statement))
            {
                effects.Add(new("map-unknown", "", 1));
                continue;
            }
            if (SimpleNamed(statement, OpenShopPattern, "shop", out var shop)) { effects.Add(shop); continue; }
            if (statement is "SaveData()" or "SaveData();")
            {
                effects.Add(new("save", "", 1));
                continue;
            }
            return new(effects, "后续脚本未知，未继续预测");
        }
        return new(effects, null);
    }

    private static bool SimpleNumeric(string statement, Regex regex, string kind, out FutureScriptEffect effect)
    {
        effect = null!;
        var match = regex.Match(statement);
        if (!match.Success || !Amount(match.Groups["amount"].Value, out var amount)) return false;
        effect = new(kind, "", amount);
        return true;
    }

    private static bool SimpleNamedAmount(string statement, Regex regex, string kind, out FutureScriptEffect effect)
    {
        effect = null!;
        var match = regex.Match(statement);
        if (!match.Success || !Amount(match.Groups["amount"].Value, out var amount)) return false;
        effect = new(kind, match.Groups["name"].Value, amount);
        return true;
    }

    private static bool SimpleNamed(string statement, Regex regex, string kind, out FutureScriptEffect effect)
    {
        effect = null!;
        var match = regex.Match(statement);
        if (!match.Success) return false;
        effect = new(kind, match.Groups["name"].Value, 1);
        return true;
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

    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
