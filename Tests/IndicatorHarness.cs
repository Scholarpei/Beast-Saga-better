// Minimal UI boundary doubles. The render order is taken from the pinned game's
// StandardUIMenuPanel.SetResponseButton: format text, assign response, number text.
// Tests execute the actual Indicators.cs and dispatch its registered postfixes.
using System.Reflection;
using PixelCrushers.DialogueSystem;

namespace SafeDialogueTools.Tests
{
    internal static class IndicatorHarness
    {
        internal static void Render(StandardUIResponseButton button, Response response)
        {
            button.text = response.formattedText.text;
            Postfix(typeof(StandardUIResponseButton), "SetFormattedText", button, response.formattedText);
            button.response = response;
            button.text = "3." + button.text;
        button.isVisible = true;
        Hover(button, response);
        }

    internal static void Rebind(StandardUIResponseButton button, Response response)
    {
        button.response = response;
        Postfix(typeof(StandardUIMenuPanel), "SetResponseButton", new StandardUIMenuPanel(), button, response);
    }

    internal static void Hover(StandardUIResponseButton button, Response response)
    {
        button.response = response;
        Postfix(typeof(StandardUIResponseButton), "OnPointerEnter", button);
    }

    internal static void Leave(StandardUIResponseButton button)
    {
        Postfix(typeof(StandardUIResponseButton), "OnPointerExit", button);
    }

    internal static void Click(StandardUIResponseButton button)
    {
        foreach (var type in typeof(Indicators).Assembly.GetTypes())
        {
            var patch = type.GetCustomAttribute<HarmonyLib.HarmonyPatch>();
            if (patch?.Target != typeof(StandardUIResponseButton) || patch.Method != "OnClick") continue;
            var prefix = type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            prefix?.Invoke(null, null);
        }
    }

        private static void Postfix(Type target, string method, object instance, params object[] args)
        {
            foreach (var type in typeof(Indicators).Assembly.GetTypes())
            {
                var patch = type.GetCustomAttribute<HarmonyLib.HarmonyPatch>();
                if (patch?.Target != target || patch.Method != method) continue;
                var postfix = type.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
                if (postfix == null) continue;
                postfix.Invoke(null, postfix.GetParameters().Select(parameter =>
                    parameter.Name == "__instance" ? instance : args[int.Parse(parameter.Name![2..])]).ToArray());
            }
        }

        internal static Response CraftOption()
        {
            var entry = new DialogueEntry();
            var success = new DialogueEntry { conditionsString = "IsPlayerLife(\"巧艺\", \"大于等于\", 20)  == true" };
            var failure = new DialogueEntry { conditionsString = "IsPlayerLife(\"巧艺\", \"大于等于\", 20)  == false" };
            entry.outgoingLinks.Add(new Link(success));
            entry.outgoingLinks.Add(new Link(failure));
            return new Response { destinationEntry = entry, formattedText = new FormattedText { text = "仿造戒指（判定：巧艺20）" } };
        }

    }

    internal static class DialogueVariableState
    {
        internal static readonly Dictionary<string, string> Variables = new();
        internal static int Reads;

        internal static void Reset()
        {
            Variables.Clear();
            Reads = 0;
        }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch(Type target, string method) : Attribute
    {
        internal Type Target = target;
        internal string Method = method;
    }
}

namespace PixelCrushers.DialogueSystem
{
    internal sealed class Link
    {
        internal Link(DialogueEntry entry, int id = -1)
        {
            if (id >= 0) entry.id = id;
            Entry = entry;
        }
        internal DialogueEntry Entry = null!;
    }
    internal sealed class DialogueEntry
    {
        internal string conditionsString = "", userScript = "", Sequence = "";
        internal int conversationID = 2, id = 90000;
        internal List<Link> outgoingLinks = new();
    }
    internal sealed class FormattedText { internal string text = ""; }
    internal sealed class Response
    {
        internal DialogueEntry destinationEntry = new();
        internal FormattedText formattedText = new();
    }
    internal sealed class StandardUIResponseButton
    {
        internal Response? response;
        internal string text = "";
        internal bool isVisible = true;
        internal void SetFormattedText(FormattedText text) { }
        internal void OnPointerEnter() { }
        internal void OnPointerExit() { }
        internal void OnClick() { }
    }
    internal sealed class StandardUIMenuPanel
    {
        internal void SetResponseButton(StandardUIResponseButton button, Response response) { }
    }
    internal sealed class Subtitle { internal DialogueEntry dialogueEntry = new(); }
    internal sealed class ConversationView { internal void StartSubtitle(Subtitle subtitle) { } }
    internal sealed class ConversationController { internal void Close() { } }
    internal sealed class Database { internal DialogueEntry GetDialogueEntry(Link link) => link.Entry; }
    internal static class DialogueManager { internal static Database masterDatabase = new(); }
    internal static class DialogueLua
    {
        internal static Lua.Result GetVariable(string name)
        {
            SafeDialogueTools.Tests.DialogueVariableState.Reads++;
            return new Lua.Result(SafeDialogueTools.Tests.DialogueVariableState.Variables.TryGetValue(name, out var value) ? value : null);
        }
    }
    internal sealed class Lua
    {
        internal readonly struct Result(object? value)
        {
            internal bool HasReturnValue => value != null;
            internal bool IsNumber => double.TryParse(value?.ToString(), out _);
            internal bool IsBool => value is bool;
            internal bool IsString => value is string;
            internal float AsFloat => float.Parse(value!.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
            internal bool AsBool => (bool)value!;
            internal string AsString => (string)value!;
        }
    }
}

namespace SafeDialogueTools
{
    internal static class Plugin
    {
        internal sealed class Setting { internal bool Value = true; }
        internal sealed class Log { internal void LogWarning(string text) => throw new Exception(text); }
        internal static Setting EnableIndicators = new();
        internal static Log Logger = new();
    }
    internal static class RuntimeDriver
    {
        internal static void ClearStaleHover(StandardUIResponseButton? button)
        {
            if (button == null || button.isVisible) return;
            Indicators.HoveredButton = null;
            Indicators.HoverHint = "";
        }
    }
}

internal enum PlayerProType { }
internal enum LifeType { }
internal sealed class PlayerDataManager
{
    internal static PlayerDataManager _Instance = new();
    internal object _PlayerData = new();
    internal bool _IsLoadOver = true;
    internal readonly Dictionary<string, int> ItemCounts = new();
    internal int GetWuXing() => 10;
    internal int GetPro(PlayerProType type) => 10;
    internal int GetLifeTypeLV(LifeType type) => 10;
    internal int ItemCount(string name) => ItemCounts.TryGetValue(name, out var count) ? count : 0;
}
internal sealed class FriendData { internal List<string> BattleFriend = new(); }
internal sealed class Friend { internal int GetLifeLvbyType(LifeType type) => 10; }
internal sealed class PlayerFriendDataManager
{
    internal static PlayerFriendDataManager _Instance = new();
    internal FriendData _FriendData = new();
    internal bool _IsLoadOver = true;
    internal Friend? GetFriend(string name) => null;
}
internal sealed class PlayerDataHelper
{
    internal static PlayerDataHelper _Instance = new();
    internal float LifeYiLiFuRen() => 0;
}
internal sealed class ExcelDataHelp
{
    internal sealed class FriendFavorHelper
    {
        internal readonly Dictionary<string, string> Names = new();
        internal string GetShowName(string name) => Names.TryGetValue(name, out var shown) ? shown : "";
    }

    internal static FriendFavorHelper? FriendFavor { get; set; } = new();

    internal sealed class KungFuHelper
    {
        internal string GetRankColor(int rank) => rank switch { 1 => "FFFFFF", 2 => "4DE28A", _ => "6FA8FF" };
    }

    internal static KungFuHelper? KungFu { get; set; } = new();
}

internal sealed class ItemDataSet { internal int _Rank = 1; }
internal sealed class EquipDataSet { internal string _Rank = "2"; }
internal sealed class LoadDataSet
{
    internal static LoadDataSet? _Instance = new();
    private readonly Dictionary<string, ItemDataSet> items = new() { ["1铁矿"] = new() { _Rank = 1 } };
    private readonly Dictionary<string, EquipDataSet> equips = new() { ["2铸铁剑"] = new() };
    internal ItemDataSet? GetItemData(string name) => items.TryGetValue(name, out var data) ? data : null;
    internal EquipDataSet? GetEuipData(string name) => equips.TryGetValue(name, out var data) ? data : null;
}
