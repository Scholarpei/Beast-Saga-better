using HarmonyLib;
using PixelCrushers.DialogueSystem;
using SafeDialogueTools.Core;

namespace SafeDialogueTools;

internal static class Indicators
{
    internal static string SubtitleHint = "";
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
        var expression = entry.conditionsString;
        if (string.IsNullOrWhiteSpace(expression) || !expression.Contains("IsPlayer", StringComparison.Ordinal))
        {
            if (entry.outgoingLinks == null || entry.outgoingLinks.Count != 2 || !string.IsNullOrWhiteSpace(entry.userScript) ||
                !string.IsNullOrWhiteSpace(entry.Sequence)) return "";
            var database = DialogueManager.masterDatabase;
            var first = database.GetDialogueEntry(entry.outgoingLinks[0]);
            var second = database.GetDialogueEntry(entry.outgoingLinks[1]);
            if (first == null || second == null) return "";
            var firstCheck = CheckPreview.Parse(first.conditionsString);
            var secondCheck = CheckPreview.Parse(second.conditionsString);
            if (firstCheck == null || secondCheck == null) return "";
            var firstFunction = firstCheck.Function.Replace("IsPlayer2Life", "IsPlayerLife");
            var secondFunction = secondCheck.Function.Replace("IsPlayer2Life", "IsPlayerLife");
            if (firstFunction != secondFunction || firstCheck.Attribute != secondCheck.Attribute || firstCheck.Operation != secondCheck.Operation ||
                firstCheck.Threshold != secondCheck.Threshold || firstCheck.Expected == secondCheck.Expected) return "";
            expression = firstCheck.Expected ? first.conditionsString : second.conditionsString;
        }
        if (!expression.Contains("IsPlayer", StringComparison.Ordinal)) return "";
        var spec = CheckPreview.Parse(expression);
        if (spec == null) return "? 复合或未知条件，暂无法判断";
        var successExpression = $"{spec.Function}(\"{spec.Attribute}\",\"{spec.Operation}\",{spec.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}) == true";
        var result = CheckPreview.Evaluate(successExpression, (function, attribute) => ReadValue(spec));
        var suffix = spec.Function != "IsPlayerPro" && result.Passed != null ? "（含队友及说服修正）" : "";
        return result.Text + suffix;
    }

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

[HarmonyPatch(typeof(StandardUIResponseButton), nameof(StandardUIResponseButton.SetFormattedText))]
internal static class ResponseIndicatorPatch
{
    private static void Postfix(StandardUIResponseButton __instance, FormattedText __0)
    {
        try
        {
            if (__instance.response == null) return;
            var hint = Indicators.Hint(__instance.response.destinationEntry);
            if (!string.IsNullOrEmpty(hint) && __0 != null) __instance.text = __0.text + "  [" + hint + "]";
        }
        catch (Exception error) { Plugin.Logger.LogWarning("Indicator skipped: " + error.Message); }
    }
}

[HarmonyPatch(typeof(ConversationView), nameof(ConversationView.StartSubtitle))]
internal static class SubtitleIndicatorPatch
{
    private static void Postfix(Subtitle __0)
    {
        try { Indicators.SubtitleHint = __0 == null ? "" : Indicators.Hint(__0.dialogueEntry); }
        catch (Exception error) { Indicators.SubtitleHint = ""; Plugin.Logger.LogWarning("Subtitle indicator skipped: " + error.Message); }
    }
}

[HarmonyPatch(typeof(ConversationController), nameof(ConversationController.Close))]
internal static class ClearIndicatorPatch
{
    private static void Postfix() => Indicators.SubtitleHint = "";
}
