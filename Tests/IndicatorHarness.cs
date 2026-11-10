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
            Postfix(typeof(StandardUIMenuPanel), "SetResponseButton", new StandardUIMenuPanel(), button, response);
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
    internal sealed class Link(DialogueEntry entry) { internal DialogueEntry Entry = entry; }
    internal sealed class DialogueEntry
    {
        internal string conditionsString = "", userScript = "", Sequence = "";
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
        internal void SetFormattedText(FormattedText text) { }
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
}

internal enum PlayerProType { }
internal enum LifeType { }
internal sealed class PlayerDataManager
{
    internal static PlayerDataManager _Instance = new();
    internal object _PlayerData = new();
    internal bool _IsLoadOver = true;
    internal int GetWuXing() => 10;
    internal int GetPro(PlayerProType type) => 10;
    internal int GetLifeTypeLV(LifeType type) => 10;
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
