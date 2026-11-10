using HarmonyLib;
using PixelCrushers.DialogueSystem;
using SafeDialogueTools.Core;

namespace SafeDialogueTools;

internal static class Indicators
{
    internal static string HoverHint = "";
    internal static StandardUIResponseButton? HoveredButton;
    private static readonly Dictionary<string, int> AttributeTypes = new()
    {
        ["力道"] = 1, ["感知"] = 2, ["灵巧"] = 3, ["体魄"] = 4, ["经脉"] = 5, ["速度"] = 6
    };
    private static readonly Dictionary<string, int> LifeTypes = new()
    {
        ["铁匠"] = 0, ["巧艺"] = 1, ["织锦"] = 2, ["厨艺"] = 3, ["医术"] = 4,
        ["钓鱼"] = 5, ["商业"] = 6, ["说服"] = 7, ["开锁"] = 8
    };

    internal static string Hint(DialogueEntry entry)
    {
        if (!Plugin.EnableIndicators.Value || entry == null) return "";
        var lines = new List<string>();
        var expression = entry.conditionsString;
        var hasCheck = !string.IsNullOrWhiteSpace(expression) &&
            (expression.Contains("IsPlayerPro", StringComparison.Ordinal) || expression.Contains("IsPlayerLife", StringComparison.Ordinal) || expression.Contains("IsPlayer2Life", StringComparison.Ordinal));
        if (hasCheck || entry.outgoingLinks?.Count == 2)
        {
            if (TryGetSingleCheckExpression(entry, out var checkExpression))
            {
                var spec = CheckPreview.Parse(checkExpression);
                if (spec == null) lines.Add("? 判定：复合或未知条件，暂无法判断");
                else
                {
                    var successExpression = $"{spec.Function}(\"{spec.Attribute}\",\"{spec.Operation}\",{spec.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}) == true";
                    var result = CheckPreview.Evaluate(successExpression, (function, attribute) => ReadValue(spec));
                    var suffix = spec.Function != "IsPlayerPro" && result.Passed != null ? "（含队友及说服修正）" : "";
                    lines.Add(result.Text + suffix);
                }
            }
            else if (hasCheck) lines.Add("? 判定：复合或未知条件，暂无法判断");
        }
        var future = PreviewFuture(entry);
        if (future == null && lines.Count == 0) return "";
        if (future != null) lines.AddRange(future);
        return "预测未来\n" + string.Join("\n", lines);
    }

    private static bool TryGetSingleCheckExpression(DialogueEntry entry, out string expression)
    {
        expression = entry.conditionsString;
        if (!string.IsNullOrWhiteSpace(entry.userScript) || !string.IsNullOrWhiteSpace(entry.Sequence)) return false;
        var links = entry.outgoingLinks;
        if (links == null || links.Count == 0) return true;
        if (links.Count != 2 || DialogueManager.masterDatabase == null) return false;
        var first = DialogueManager.masterDatabase.GetDialogueEntry(links[0]);
        var second = DialogueManager.masterDatabase.GetDialogueEntry(links[1]);
        if (first == null || second == null) return false;
        var firstCheck = CheckPreview.Parse(first.conditionsString);
        var secondCheck = CheckPreview.Parse(second.conditionsString);
        if (firstCheck == null || secondCheck == null) return false;
        var firstFunction = firstCheck.Function.Replace("IsPlayer2Life", "IsPlayerLife");
        var secondFunction = secondCheck.Function.Replace("IsPlayer2Life", "IsPlayerLife");
        if (firstFunction != secondFunction || firstCheck.Attribute != secondCheck.Attribute || firstCheck.Operation != secondCheck.Operation ||
            firstCheck.Threshold != secondCheck.Threshold || firstCheck.Expected == secondCheck.Expected) return false;
        expression = firstCheck.Expected ? first.conditionsString : second.conditionsString;
        return true;
    }

    private static List<string>? PreviewFuture(DialogueEntry entry)
    {
        var paths = new List<(IReadOnlyList<FutureScriptEffect> Effects, IReadOnlyList<string> Stops)>();
        {
            var queue = new List<(DialogueEntry Node, List<FutureScriptEffect> Path, List<string> PathStops, HashSet<string> Visited)> { (entry, new(), new(), new()) };
            var explored = 0;
            while (queue.Count > 0 && paths.Count < 8 && explored < 48)
            {
                var current = queue[^1];
                queue.RemoveAt(queue.Count - 1);
                explored++;
                if (current.Path.Count + current.PathStops.Count >= 64)
                {
                    current.PathStops.Add("预测内容达到安全上限");
                    paths.Add((current.Path, current.PathStops));
                    continue;
                }
                var key = current.Node.conversationID + ":" + current.Node.id;
                if (!current.Visited.Add(key))
                {
                    current.PathStops.Add(key == entry.conversationID + ":" + entry.id ? "可回溯" : "对话会回到已预测内容");
                    paths.Add((current.Path, current.PathStops));
                    continue;
                }
                var script = FutureScriptPreview.Parse(current.Node.userScript);
                current.Path.AddRange(script.Effects);
                if (script.StopReason != null)
                {
                    current.PathStops.Add(script.StopReason);
                    paths.Add((current.Path, current.PathStops));
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(current.Node.Sequence)) { }
                var links = current.Node.outgoingLinks;
                if (links == null || links.Count == 0)
                {
                    paths.Add((current.Path, current.PathStops));
                    continue;
                }
                var successors = new List<DialogueEntry>();
                foreach (var link in links)
                {
                    var next = DialogueManager.masterDatabase?.GetDialogueEntry(link);
                    if (next == null || !string.IsNullOrWhiteSpace(next.conditionsString) || successors.Contains(next)) continue;
                    successors.Add(next);
                }
                if (successors.Count == 0)
                {
                    current.PathStops.Add(links.Count > 1 ? "后续有分支条件未知，未继续预测" : "后续条件未知，未继续预测");
                    paths.Add((current.Path, current.PathStops));
                    continue;
                }
                foreach (var next in successors)
                    queue.Add((next, new List<FutureScriptEffect>(current.Path), new List<string>(current.PathStops), new HashSet<string>(current.Visited)));
                continue;
            }
            if (queue.Count > 0) paths.Add((new List<FutureScriptEffect>(), new List<string> { "预测路径达到安全上限" }));
        }
        var lines = new List<string>();
        if (paths.Count == 1)
        {
            var path = paths[0];
            lines.AddRange(FormatEffects(path.Effects.ToList()));
            lines.AddRange(path.Stops.Select(stop => stop.StartsWith('?') ? stop : "（" + stop + "）"));
        }
        else
        {
            var pathEffects = paths
                .Select(path => (Lines: path.Effects.SelectMany(effect => FormatEffects(new List<FutureScriptEffect> { effect })).GroupBy(line => line).Select(group => group.First()).ToList(), Stops: path.Stops))
                .ToList();
            var common = new HashSet<string>(pathEffects[0].Lines);
            foreach (var path in pathEffects.Skip(1)) common.IntersectWith(path.Lines);
            if (common.Count > 0)
            {
                lines.Add("共同结果：");
                lines.AddRange(common);
            }
            var meaningful = pathEffects
                .Select((path, index) => (Index: index, path.Lines, path.Stops))
                .Where(path => path.Stops.Count == 0 || path.Stops.Any(stop => stop != "可回溯"))
                .ToList();
            var backtracks = pathEffects.Count - meaningful.Count;
            if (backtracks == pathEffects.Count || backtracks > 0)
            {
                lines.Add($"（另有 {backtracks} 条路径可回溯）");
                return lines;
            }
            lines.Add("可能分支：");
            for (var index = 0; index < meaningful.Count; index++)
            {
                var path = meaningful[index];
                var connector = index == meaningful.Count - 1 ? "└─" : "├─";
                var branchLines = path.Lines.Where(line => !common.Contains(line))
                    .Concat(path.Stops.Where(stop => stop != "可回溯").Select(stop => stop.StartsWith('?') ? stop : "（" + stop + "）"))
                    .ToList();
                lines.Add($"{connector} 分支{index + 1}");
                var indent = index == meaningful.Count - 1 ? "   " : "│  ";
                if (branchLines.Count == 0) lines.Add(indent + "无额外变化");
                else lines.AddRange(branchLines.Select(line => indent + line));
            }
            if (backtracks > 0) lines.Add($"另有 {backtracks} 条路径可回溯");
        }
        return lines.Count == 0 ? null : lines;
    }

    private static List<string> FormatEffects(List<FutureScriptEffect> effects)
    {
        var lines = new List<string>();
        var totals = new Dictionary<string, FutureScriptEffect>();
        foreach (var effect in effects)
        {
            var key = effect.Kind + "\0" + effect.Target;
            if (!totals.TryGetValue(key, out var total)) totals[key] = effect;
            else totals[key] = total with { Amount = total.Amount + effect.Amount };
        }
        foreach (var effect in totals.Values.OrderBy(effect => effect.Kind).ThenBy(effect => effect.Target, StringComparer.CurrentCulture))
        {
            var amount = effect.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (effect.Kind == "love") lines.Add("好感：" + DisplayName(effect.Target) + " " + Signed(amount));
            else if (effect.Kind == "variable" && effect.Amount != 0 && effect.Target.Contains("好感", StringComparison.Ordinal))
                lines.Add("好感：" + effect.Target + " " + Signed(amount));
            else if (effect.Kind == "item") lines.Add((effect.Amount >= 0 ? "获得：" : "失去：") + ColoredName(effect.Target, false) + " ×" + FormatCount(effect.Amount));
            else if (effect.Kind == "equip") lines.Add((effect.Amount >= 0 ? "获得装备：" : "失去装备：") + ColoredName(effect.Target, true) + " ×" + FormatCount(effect.Amount));
            else if (effect.Kind == "variable") lines.Add("剧情：" + effect.Target);
            else if (effect.Kind == "quest") lines.Add("任务：" + effect.Target + " 状态会变化");
        }
        return lines;
    }

    private static string DisplayName(string name)
    {
        var shown = ExcelDataHelp.FriendFavor?.GetShowName(name);
        return string.IsNullOrWhiteSpace(shown) ? name : shown;
    }

    private static string ColoredName(string name, bool equip)
    {
        var data = LoadDataSet._Instance;
        var rankText = equip ? data?.GetEuipData(name)?._Rank : data?.GetItemData(name)?._Rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!int.TryParse(rankText, out var rank)) return name;
        var color = ExcelDataHelp.KungFu?.GetRankColor(rank);
        return string.IsNullOrWhiteSpace(color) ? name : $"<color=#{color.TrimStart('#')}>{name}</color>";
    }

    private static string Signed(string amount) => amount.StartsWith('-') ? amount : "+" + amount;

    private static string FormatCount(double amount) => Math.Abs(amount).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static double? ReadValue(CheckSpec spec)
    {
        var player = PlayerDataManager._Instance;
        if (player == null || player._PlayerData == null || !player._IsLoadOver) return null;
        if (spec.Function == "IsPlayerPro")
        {
            if (spec.Attribute == "悟性") return player.GetWuXing();
            if (AttributeTypes.TryGetValue(spec.Attribute, out var attributeType)) return player.GetPro((PlayerProType)attributeType);
            return null;
        }
        if (!LifeTypes.TryGetValue(spec.Attribute, out var lifeType) || spec.Threshold != Math.Truncate(spec.Threshold)) return null;
        var friends = PlayerFriendDataManager._Instance;
        if (friends == null || friends._FriendData == null || !friends._IsLoadOver) return null;
        var current = player.GetLifeTypeLV((LifeType)lifeType);
        var battleFriends = friends._FriendData.BattleFriend;
        if (battleFriends == null) return null;
        foreach (var friendName in battleFriends)
        {
            if (string.IsNullOrEmpty(friendName)) return null;
            var friend = friends.GetFriend(friendName);
            if (friend == null) return null;
            current = Math.Max(current, friend.GetLifeLvbyType((LifeType)lifeType));
        }
        var bonus = 0f;
        if (spec.Attribute == "说服")
        {
            if (PlayerDataHelper._Instance == null) return null;
            bonus = PlayerDataHelper._Instance.LifeYiLiFuRen();
            if (!float.IsFinite(bonus) || bonus != MathF.Truncate(bonus)) return null;
            bonus = Math.Max(0, bonus);
        }
        return current + bonus;
    }
}

[HarmonyPatch(typeof(StandardUIResponseButton), nameof(StandardUIResponseButton.OnPointerEnter))]
internal static class ResponseHoverEnterPatch
{
    private static void Postfix(StandardUIResponseButton __instance)
    {
        try
        {
            if (__instance == null) return;
            Indicators.HoveredButton = null;
            Indicators.HoverHint = "";
            if (__instance.response == null) return;
            var hint = Indicators.Hint(__instance.response.destinationEntry);
            if (string.IsNullOrEmpty(hint)) return;
            Indicators.HoveredButton = __instance;
            Indicators.HoverHint = hint;
        }
        catch (Exception error) { Plugin.Logger.LogWarning("Indicator skipped: " + error.Message); }
    }
}

[HarmonyPatch(typeof(StandardUIResponseButton), nameof(StandardUIResponseButton.OnPointerExit))]
internal static class ResponseHoverExitPatch
{
    private static void Postfix(StandardUIResponseButton __instance)
    {
        if (__instance != null && !ReferenceEquals(__instance, Indicators.HoveredButton)) return;
        Indicators.HoveredButton = null;
        Indicators.HoverHint = "";
    }
}

[HarmonyPatch(typeof(StandardUIResponseButton), nameof(StandardUIResponseButton.OnClick))]
internal static class ResponseClickIndicatorPatch
{
    private static void Prefix()
    {
        Indicators.HoveredButton = null;
        Indicators.HoverHint = "";
    }
}

[HarmonyPatch(typeof(StandardUIMenuPanel), nameof(StandardUIMenuPanel.SetResponseButton))]
internal static class ResponseRebindIndicatorPatch
{
    private static void Postfix(StandardUIResponseButton __0, Response __1)
    {
        try
        {
            if (__0 == null || !ReferenceEquals(__0, Indicators.HoveredButton)) return;
            Indicators.HoverHint = __1 == null ? "" : Indicators.Hint(__1.destinationEntry);
            if (string.IsNullOrEmpty(Indicators.HoverHint)) Indicators.HoveredButton = null;
        }
        catch (Exception error)
        {
            Indicators.HoveredButton = null;
            Indicators.HoverHint = "";
            Plugin.Logger.LogWarning("Indicator skipped: " + error.Message);
        }
    }
}

[HarmonyPatch(typeof(ConversationController), nameof(ConversationController.Close))]
internal static class ClearIndicatorPatch
{
    private static void Postfix()
    {
        Indicators.HoveredButton = null;
        Indicators.HoverHint = "";
    }
}
