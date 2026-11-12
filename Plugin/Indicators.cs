using System.Text.RegularExpressions;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using BeastSagaBetter.Core;

namespace BeastSagaBetter;

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
    private static readonly Regex BattleSequencePattern = new(@"SendMessage\s*\(\s*EnterBattle[A-Za-z0-9_]*\s*,\s*(?<target>[^+,]+)", RegexOptions.CultureInvariant);
    private static readonly Dictionary<string, string> PartyNames = new()
    {
        ["Ashou"] = "阿寿", ["Debutou"] = "德捕头", ["Huerjing"] = "狐二靖", ["buzhou"] = "不周",
        ["fu"] = "腹", ["houqianchong"] = "猴千冲", ["langjiusiD"] = "狼九思", ["langjiusiK"] = "狼九思",
        ["langying"] = "狼萤", ["langying2"] = "狼萤", ["langyuan"] = "狼渊", ["lu"] = "鹿",
        ["luxi"] = "鹿柒", ["luxi2"] = "鹿柒", ["maochunye"] = "猫春也", ["maohualai"] = "猫花来",
        ["shexing"] = "蛇形", ["shixiao"] = "狮逍", ["shou"] = "兽", ["shusanniang"] = "鼠三娘",
        ["tuqianqian"] = "兔千千", ["wangyou"] = "忘忧", ["wuchang"] = "无常",
        ["xianglingshuang"] = "向凌霜", ["xianglingshuang2"] = "向凌霜", ["zanglinglong"] = "藏玲珑"
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
                    if (result.Passed == true && spec.Function != "IsPlayerPro")
                    {
                        var nativeGain = Math.Truncate(spec.Threshold * 0.01);
                        if (nativeGain > 0) result = result with { Text = result.Text + "；达标奖励：生活经验 " + spec.Attribute + " +" + FormatCount(nativeGain) };
                    }
                    var suffix = spec.Function != "IsPlayerPro" && result.Passed != null ? "（含队友及说服修正）" : "";
                    lines.Add(result.Text + suffix);
                }
            }
            else if (hasCheck) lines.Add("? 判定：复合或未知条件，暂无法判断");
            else if (TryGetPairedItemCountExpression(entry, out var itemCountExpression))
            {
                var itemCountResult = CheckPreview.EvaluateItemCount(itemCountExpression, ReadItemCount);
                lines.Add(itemCountResult.Text);
                if (itemCountResult.Passed == false)
                {
                    var quest = FindQuestSuccessAfterItemCountSuccess(entry);
                    if (quest != null && !lines.Any(line => line == "✗ 本次不会完成任务：" + quest.Target)) lines.Add("✗ 本次不会完成任务：" + quest.Target);
                }
            }
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

    private static bool TryGetPairedItemCountExpression(DialogueEntry entry, out string expression)
    {
        expression = "";
        var links = entry.outgoingLinks;
        if (links == null || links.Count != 2 || DialogueManager.masterDatabase == null) return false;
        var first = DialogueManager.masterDatabase.GetDialogueEntry(links[0]);
        var second = DialogueManager.masterDatabase.GetDialogueEntry(links[1]);
        if (first == null || second == null) return false;
        var firstCheck = CheckPreview.ParseItemCount(first.conditionsString);
        var secondCheck = CheckPreview.ParseItemCount(second.conditionsString);
        if (firstCheck == null || secondCheck == null || !SameItemCount(firstCheck, secondCheck) || firstCheck.Expected == secondCheck.Expected) return false;
        expression = firstCheck.Expected ? first.conditionsString : second.conditionsString;
        return true;
    }

    private static bool SameItemCount(ItemCountSpec first, ItemCountSpec second) =>
        first.Item == second.Item && first.Operation == second.Operation && first.Threshold == second.Threshold;

    private static FutureScriptEffect? FindQuestSuccessAfterItemCountSuccess(DialogueEntry entry)
    {
        DialogueEntry? success = null;
        var links = entry.outgoingLinks;
        if (links != null)
        {
            for (var index = 0; index < links.Count; index++)
            {
                var next = DialogueManager.masterDatabase?.GetDialogueEntry(links[index]);
                if (next != null && CheckPreview.ParseItemCount(next.conditionsString ?? "") is { Expected: true }) success = next;
            }
        }
        return success == null ? null : ReachableQuestSuccess(success);
    }

    private static List<string>? PreviewFuture(DialogueEntry entry)
    {
        var paths = new List<(IReadOnlyList<FutureScriptEffect> Effects, IReadOnlyList<string> Stops, bool Certain)>();
        {
            var queue = new List<(DialogueEntry Node, List<FutureScriptEffect> Path, List<string> PathStops, HashSet<string> Visited, Dictionary<string, double> ItemCounts)> { (entry, new(), new(), new(), ReadInitialItemCounts()) };
            var explored = 0;
            var duplicateNodes = new HashSet<string>();
            while (queue.Count > 0 && paths.Count < 8 && explored < 48)
            {
                var current = queue[^1];
                queue.RemoveAt(queue.Count - 1);
                explored++;
                if (current.Path.Count + current.PathStops.Count >= 64)
                {
                    current.PathStops.Add("预测内容达到安全上限");
                    paths.Add((current.Path, current.PathStops, false));
                    continue;
                }
                var key = current.Node.conversationID + ":" + current.Node.id;
                if (duplicateNodes.Contains(key) && key != entry.conversationID + ":" + entry.id && current.Path.Count > 0)
                {
                    current.PathStops.Add("对话会回到已预测内容");
                    paths.Add((current.Path, current.PathStops, false));
                    continue;
                }
                if (!current.Visited.Add(key))
                {
                    current.PathStops.Add(key == entry.conversationID + ":" + entry.id ? "可回溯" : "对话会回到已预测内容");
                    paths.Add((current.Path, current.PathStops, false));
                    continue;
                }
                var script = FutureScriptPreview.Parse(current.Node.userScript);
                current.Path.AddRange(script.Effects);
                if (script.StopReason != null)
                {
                    current.PathStops.Add(script.StopReason);
                    paths.Add((current.Path, current.PathStops, true));
                    continue;
                }
                if (script.Effects.Any(effect => effect.Kind == "shop"))
                {
                    paths.Add((current.Path, current.PathStops, true));
                    continue;
                }
                var battle = BattleSequencePattern.Match(current.Node.Sequence ?? "");
                if (battle.Success)
                {
                    current.PathStops.Add("进入战斗：" + battle.Groups["target"].Value.Trim() + "，后续不预测");
                    paths.Add((current.Path, current.PathStops, true));
                    continue;
                }
                var boundary = BoundaryStop(script.Effects);
                if (boundary != null)
                {
                    current.PathStops.Add(boundary);
                    paths.Add((current.Path, current.PathStops, true));
                    continue;
                }
                var links = current.Node.outgoingLinks;
                if (links == null || links.Count == 0)
                {
                    paths.Add((current.Path, current.PathStops, true));
                    continue;
                }
                var successors = new List<DialogueEntry>();
                var childItemCounts = new Dictionary<string, double>(current.ItemCounts);
                ApplyItemEffects(childItemCounts, script.Effects);
                var nativeCheckSpec = CheckPreview.Parse(current.Node.conditionsString ?? "");
                if (nativeCheckSpec is { Function: "IsPlayerLife" or "IsPlayer2Life", Operation: "大于等于" } &&
                    CheckPreview.Evaluate(current.Node.conditionsString!, (function, attribute) => ReadValue(nativeCheckSpec)).Passed == true)
                {
                    var nativeLifeGain = Math.Truncate(nativeCheckSpec.Threshold * 0.01);
                    if (nativeLifeGain > 0) current.Path.Add(new FutureScriptEffect("life-exp", nativeCheckSpec.Attribute, nativeLifeGain));
                }
                foreach (var link in links)
                {
                    var next = DialogueManager.masterDatabase?.GetDialogueEntry(link);
                    if (next == null || successors.Contains(next)) continue;
                    if (!string.IsNullOrWhiteSpace(next.conditionsString))
                    {
                        var checkSpec = CheckPreview.Parse(next.conditionsString);
                        var itemSpec = CheckPreview.ParseItemCount(next.conditionsString);
                        var visible = checkSpec != null
                            ? CheckPreview.Evaluate(next.conditionsString, (function, attribute) => ReadValue(checkSpec)).Passed == true
                            : itemSpec != null
                                ? EvaluateExpected(itemSpec, childItemCounts)
                                : VariableConditionIsTrue(next.conditionsString);
                        if (visible != true) continue;
                    }
                    successors.Add(next);
                }
                if (successors.Count == 0)
                {
                    var chosen = ChoosePairedItemCountSuccessor(EnumerateLinks(links), childItemCounts);
                    if (chosen == null)
                    {
                        current.PathStops.Add(links.Count > 1 ? "后续有分支条件未知，未继续预测" : "后续条件未知，未继续预测");
                        paths.Add((current.Path, current.PathStops, false));
                        continue;
                    }
                    queue.Add((chosen, new List<FutureScriptEffect>(current.Path), new List<string>(current.PathStops), new HashSet<string>(current.Visited), new Dictionary<string, double>(childItemCounts)));
                    duplicateNodes.Add(chosen.conversationID + ":" + chosen.id);
                    continue;
                }
                var ancestorSuccessors = successors
                    .Where(next => current.Visited.Contains(next.conversationID + ":" + next.id))
                    .ToList();
                if (ancestorSuccessors.Count > 0) successors = ancestorSuccessors;
                foreach (var next in successors)
                {
                    queue.Add((next, new List<FutureScriptEffect>(current.Path), new List<string>(current.PathStops), new HashSet<string>(current.Visited), new Dictionary<string, double>(childItemCounts)));
                    if (current.Visited.Contains(next.conversationID + ":" + next.id)) duplicateNodes.Add(next.conversationID + ":" + next.id);
                }
                continue;
            }
            if (queue.Count > 0) paths.Add((new List<FutureScriptEffect>(), new List<string> { "预测路径达到安全上限" }, false));
        }
        var lines = new List<string>();
        if (paths.Count == 1)
        {
            var path = paths[0];
            lines.AddRange(FormatEffects(path.Effects.ToList()));
            var quest = path.Effects.FirstOrDefault(effect => effect.Kind == "quest" && effect.Detail == "→ success");
            if (quest != null && path.Certain)
            {
                if (!path.Stops.Any(stop => stop.Contains("未知", StringComparison.Ordinal)))
                    lines.Add("✓ 会完成任务：" + quest.Target);
            }
            lines.AddRange(path.Stops.Select(stop => stop.StartsWith('?') || stop.StartsWith("✓ ") || stop.StartsWith("✗ ") ? stop : "（" + stop + "）"));
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
            var backtrackOnly = pathEffects
                .Select((path, index) => (Index: index, path.Lines, path.Stops))
                .Where(path => path.Stops.All(stop => stop == "可回溯" || stop == "对话会回到已预测内容"))
                .ToList();
            var meaningful = pathEffects
                .Select((path, index) => (Index: index, path.Lines, path.Stops))
                .Where(path => path.Stops.Count == 0 || path.Stops.Any(stop => stop != "可回溯" && stop != "对话会回到已预测内容"))
                .ToList();
            var backtracks = backtrackOnly.Count;
            if (meaningful.Count == 0)
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
                    .Concat(path.Stops.Where(stop => stop != "可回溯").Select(stop => stop.StartsWith('?') || stop.StartsWith("✓ ") || stop.StartsWith("✗ ") ? stop : "（" + stop + "）"))
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

    private static DialogueEntry? ChoosePairedItemCountSuccessor(System.Collections.IEnumerable links, IReadOnlyDictionary<string, double> itemCounts)
    {
        if (DialogueManager.masterDatabase == null) return null;
        var candidates = new List<DialogueEntry?>();
        foreach (var linkValue in links) candidates.Add(DialogueManager.masterDatabase.GetDialogueEntry((Link)linkValue));
        if (candidates.Count != 2) return null;
        if (candidates.Any(next => next == null)) return null;
        var specs = candidates.Select(next => CheckPreview.ParseItemCount(next!.conditionsString ?? "")).ToList();
        if (specs.Any(spec => spec == null) || !SameItemCount(specs[0]!, specs[1]!) || specs[0]!.Expected == specs[1]!.Expected) return null;
        DialogueEntry? chosen = null;
        foreach (var next in candidates)
        {
            var expected = EvaluateExpected(CheckPreview.ParseItemCount(next!.conditionsString ?? "")!, itemCounts);
            if (expected == null) return null;
            if (expected.Value) chosen = next;
        }
        return chosen;
    }

    private static void ApplyItemEffects(Dictionary<string, double> counts, IReadOnlyList<FutureScriptEffect> effects)
    {
        foreach (var effect in effects)
        {
            if (effect.Kind != "item") continue;
            counts.TryGetValue(effect.Target, out var count);
            counts[effect.Target] = count + effect.Amount;
        }
    }

    private static Dictionary<string, double> ReadInitialItemCounts()
    {
        var counts = new Dictionary<string, double>(StringComparer.Ordinal);
        return counts;
    }

    private static bool VariableConditionIsTrue(string expression)
    {
        var spec = CheckPreview.ParseVariableEquality(expression);
        if (spec != null) return VariableEquals(spec) == true;

        var alternatives = SplitTopLevel(expression, "or");
        if (alternatives.Count < 2) return false;
        var values = new Dictionary<string, VariableValue>(StringComparer.Ordinal);
        foreach (var alternative in alternatives)
        {
            var equality = CheckPreview.ParseVariableEquality(StripOuterParentheses(alternative.Trim()));
            if (equality == null) continue;
            if (VariableEquals(equality, values) == true) return true;
        }
        return false;
    }

    private static bool? VariableEquals(VariableEqualitySpec spec, Dictionary<string, VariableValue>? values = null)
    {
        if (values == null || !values.TryGetValue(spec.Variable, out var value))
        {
            value = ReadVariable(spec.Variable);
            if (values != null) values[spec.Variable] = value;
        }
        if (!value.HasValue) return null;
        return spec.Expected switch
        {
            bool expected => value.IsBool ? value.BoolValue == expected : false,
            double expected => value.IsNumber && double.IsFinite(value.NumberValue) && Math.Abs(value.NumberValue - expected) < 0.000001,
            string expected => value.IsString && value.StringValue == expected,
            _ => null
        };
    }

    private static VariableValue ReadVariable(string name)
    {
        var result = DialogueLua.GetVariable(name);
        if (!result.HasReturnValue) return default;
        var isBool = result.IsBool;
        var isNumber = result.IsNumber;
        var isString = result.IsString;
        return new(true, isBool, isBool && result.AsBool, isNumber, isNumber ? result.AsFloat : 0, isString, isString ? result.AsString : "");
    }

    private readonly record struct VariableValue(bool HasValue, bool IsBool, bool BoolValue, bool IsNumber, double NumberValue, bool IsString, string StringValue);

    private static List<string> SplitTopLevel(string expression, string keyword)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == '\\')
                {
                    index++;
                    continue;
                }
                if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (character is '(' or '[')
            {
                depth++;
                continue;
            }
            if (character is ')' or ']')
            {
                depth--;
                continue;
            }
            if (depth != 0 || index + keyword.Length > expression.Length) continue;
            if (!expression.Substring(index, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase)) continue;
            if (index > 0 && IsIdentifierCharacter(expression[index - 1])) continue;
            var end = index + keyword.Length;
            if (end < expression.Length && IsIdentifierCharacter(expression[end])) continue;
            parts.Add(expression[start..index]);
            start = end;
            index = end - 1;
        }
        if (parts.Count == 0) return new List<string> { expression };
        parts.Add(expression[start..]);
        return parts;
    }

    private static bool IsIdentifierCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static string StripOuterParentheses(string expression)
    {
        while (expression.Length > 1 && expression[0] == '(' && expression[^1] == ')')
        {
            var depth = 0;
            var quote = '\0';
            var wrapsWholeExpression = true;
            for (var index = 0; index < expression.Length; index++)
            {
                var character = expression[index];
                if (quote != '\0')
                {
                    if (character == '\\')
                    {
                        index++;
                        continue;
                    }
                    if (character == quote) quote = '\0';
                    continue;
                }
                if (character is '\'' or '"')
                {
                    quote = character;
                    continue;
                }
                if (character == '(') depth++;
                else if (character == ')' && --depth == 0 && index != expression.Length - 1)
                {
                    wrapsWholeExpression = false;
                    break;
                }
            }
            if (!wrapsWholeExpression || depth != 0) break;
            expression = expression[1..^1].Trim();
        }
        return expression;
    }

    private static System.Collections.Generic.IEnumerable<Link> EnumerateLinks(
#if SAFE_DIALOGUE_TOOLS_TESTS
        System.Collections.Generic.List<Link> links
#else
        Il2CppSystem.Collections.Generic.List<Link> links
#endif
    )
    {
#if SAFE_DIALOGUE_TOOLS_TESTS
        return links;
#else
        for (var index = 0; index < links.Count; index++) yield return links[index];
#endif
    }

    private static void AddQuestUnavailableMarker(List<string> stops, DialogueEntry chosen)
    {
        var quest = ReachableQuestSuccess(chosen);
        if (quest != null) stops.Add("✗ 本次不会完成任务：" + quest.Target);
    }

    private static FutureScriptEffect? ReachableQuestSuccess(DialogueEntry entry)
    {
        var visited = new HashSet<string>();
        var stack = new Stack<DialogueEntry>();
        stack.Push(entry);
        while (stack.Count > 0 && visited.Count < 32)
        {
            var node = stack.Pop();
            if (!visited.Add(node.conversationID + ":" + node.id)) continue;
            var script = FutureScriptPreview.Parse(node.userScript);
            var quest = script.Effects.FirstOrDefault(effect => effect.Kind == "quest" && effect.Detail == "→ success");
            if (quest != null || script.StopReason != null) return quest;
            var outgoingLinks = node.outgoingLinks;
            if (outgoingLinks == null) return null;
            foreach (var linkValue in outgoingLinks)
            {
                var next = DialogueManager.masterDatabase?.GetDialogueEntry((Link)linkValue);
                if (next != null) stack.Push(next);
            }
        }
        return null;
    }

    private static bool? EvaluateExpected(ItemCountSpec spec)
    {
        return EvaluateExpected(spec, null);
    }

    private static bool? EvaluateExpected(ItemCountSpec spec, IReadOnlyDictionary<string, double>? pathCounts)
    {
        double? current = pathCounts != null && pathCounts.TryGetValue(spec.Item, out var pathCount) ? pathCount : ReadItemCount(spec.Item);
        if (current is null || !double.IsFinite(current.Value)) return null;
        bool? passed = spec.Operation switch
        {
            "大于等于" => current >= spec.Threshold,
            "小于等于" => current <= spec.Threshold,
            "等于" => current == spec.Threshold,
            _ => null
        };
        return passed == null ? null : spec.Expected == passed;
    }

    private static double? ReadItemCount(string item)
    {
        var player = PlayerDataManager._Instance;
        if (player == null || player._PlayerData == null || !player._IsLoadOver) return null;
        var dialogue = player.GetComponent<PlayerDialogue>();
        if (dialogue == null) return null;
        var bag = player.ItemCount(item);
        var dressed = player.ItemDressCount(item);
        var stored = dialogue.ChuCangIsHaveItemNum(item);
        if (bag < 0 || dressed < 0 || stored < 0) return null;
        var total = (long)bag + dressed + stored;
        return total <= int.MaxValue ? total : null;
    }

    private static List<string> FormatEffects(List<FutureScriptEffect> effects)
    {
        var lines = new List<string>();
        var totals = new Dictionary<string, FutureScriptEffect>();
        foreach (var effect in effects)
        {
            var key = effect.Kind + "\0" + effect.Target + "\0" + effect.Detail;
            if (!totals.TryGetValue(key, out var total)) totals[key] = effect;
            else totals[key] = total with { Amount = total.Amount + effect.Amount };
        }
        foreach (var effect in totals.Values.OrderBy(effect => effect.Kind).ThenBy(effect => effect.Target, StringComparer.CurrentCulture))
        {
            var amount = effect.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (effect.Kind == "save")
            {
                lines.Add("（此分支会立即存档）");
                continue;
            }
            if (effect.Kind is "map" or "map-unknown") continue;
            if (effect.Kind == "love") lines.Add("好感：" + DisplayName(effect.Target) + " " + Signed(amount));
            else if (effect.Kind == "variable" && effect.Amount != 0 && effect.Target.Contains("好感", StringComparison.Ordinal))
                lines.Add("好感：" + effect.Target + " " + Signed(amount));
            else if (effect.Kind == "exp") lines.Add("经验 " + Signed(amount));
            else if (effect.Kind == "life-exp") lines.Add("生活经验：" + effect.Target + " " + Signed(amount));
            else if (effect.Kind == "attribute") lines.Add(effect.Target + " " + Signed(amount));
            else if (effect.Kind == "martial-flow") lines.Add("武学升段流程：" + effect.Target + " " + effect.Detail);
            else if (effect.Kind == "friend-add") lines.Add("入队：" + PartyName(effect.Target));
            else if (effect.Kind == "friend-leave") lines.Add("离队：" + PartyName(effect.Target));
            else if (effect.Kind == "kungfu") lines.Add("习得武功：" + effect.Target);
            else if (effect.Kind == "in-kungfu") lines.Add("习得内功：" + effect.Target);
            else if (effect.Kind == "kungfu-skill") lines.Add("习得技能：" + effect.Target);
            else if (effect.Kind == "friend-kungfu") lines.Add(PartyName(effect.Target) + "习得武功：" + effect.Detail);
            else if (effect.Kind == "friend-kungfu-skill") lines.Add(PartyName(effect.Target) + "习得技能：" + effect.Detail);
            else if (effect.Kind == "item") lines.Add((effect.Amount >= 0 ? "获得：" : "失去：") + ColoredName(effect.Target, false) + " ×" + FormatCount(effect.Amount));
            else if (effect.Kind == "equip") lines.Add((effect.Amount >= 0 ? "获得装备：" : "失去装备：") + ColoredName(effect.Target, true) + " ×" + FormatCount(effect.Amount));
            else if (effect.Kind == "shop") lines.Add("打开商店：" + effect.Target);
            else if (effect.Kind == "variable") lines.Add("剧情：" + effect.Target);
            else if (effect.Kind == "quest") lines.Add("任务：" + effect.Target + " " + effect.Detail);
        }
        return lines;
    }

    private static string? BoundaryStop(IReadOnlyList<FutureScriptEffect> effects)
    {
        foreach (var effect in effects)
        {
            if (effect.Kind == "map") return "切换地图：" + effect.Target + "，后续不预测";
            if (effect.Kind == "map-unknown") return "切换地图（目标未知），后续不预测";
        }
        return null;
    }

    private static string DisplayName(string name)
    {
        var shown = ExcelDataHelp.FriendFavor?.GetShowName(name);
        return string.IsNullOrWhiteSpace(shown) ? name : shown;
    }

    private static string PartyName(string name) => PartyNames.TryGetValue(name, out var shown) ? shown : name;

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
            if (spec.Attribute == "悟性")
            {
                var wisdom = player.GetWuXing();
                if (wisdom >= spec.Threshold) return wisdom;
                var friendManager = PlayerFriendDataManager._Instance;
                if (friendManager == null || friendManager._FriendData == null || !friendManager._IsLoadOver) return null;
                var friendNames = friendManager._FriendData.BattleFriend;
                if (friendNames == null) return null;
                foreach (var friendName in friendNames)
                {
                    if (string.IsNullOrEmpty(friendName)) return null;
                    var friend = friendManager.GetFriend(friendName);
                    if (friend == null) return null;
                    if (friend._WuXing >= spec.Threshold) return friend._WuXing;
                }
                return wisdom;
            }
            if (!AttributeTypes.TryGetValue(spec.Attribute, out var attributeType)) return null;
            var value = player.GetPro((PlayerProType)attributeType);
            return value >= spec.Threshold ? value : Math.Max(value, FriendPro((PlayerProType)attributeType));
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

    private static int FriendPro(PlayerProType type)
    {
        var friends = PlayerFriendDataManager._Instance;
        if (friends == null || friends._FriendData == null || !friends._IsLoadOver) return -1;
        var battleFriends = friends._FriendData.BattleFriend;
        if (battleFriends == null) return -1;
        var maximum = -1;
        foreach (var friendName in battleFriends)
        {
            if (string.IsNullOrEmpty(friendName)) return -1;
            var friend = friends.GetFriend(friendName);
            if (friend == null) return -1;
            var value = friend.GetPro(type);
            if (value < 0) return -1;
            maximum = Math.Max(maximum, value);
        }
        return maximum;
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
