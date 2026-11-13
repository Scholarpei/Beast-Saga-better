using BeastSagaBetter;
using PixelCrushers.DialogueSystem;

namespace BeastSagaBetter.Tests;

internal static class PreviewRegressionTests
{
    internal static (int Checks, int Failures) Run()
    {
        var checks = 0;
        var failures = 0;
        void Test(string name, Action action)
        {
            checks++;
            IndicatorHarness.ResetIndicatorState();
            try { action(); Console.WriteLine("PASS preview regression " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL preview regression " + name + ": " + error.Message); }
        }
        void Require(bool value, string hint) { if (!value) throw new Exception(hint); }
        DialogueEntry Node(int id, string script = "", string condition = "", int actor = 0) =>
            new() { id = id, userScript = script, conditionsString = condition, ActorID = actor };
        void LinkTo(DialogueEntry parent, params DialogueEntry[] children)
        {
            foreach (var child in children) parent.outgoingLinks.Add(new Link(child) { priority = ConditionPriority.Normal });
        }
        foreach (var (delta, threshold) in new[] { (-1, 2), (1, 4) })
        {
            Test("existing three items plus " + delta + " uses the original inventory", () =>
            {
                PlayerDataManager._Instance.ItemCounts["矿"] = 1;
                PlayerDataManager._Instance.DressedItemCounts["矿"] = 1;
                PlayerDataManager._Instance.Dialogue!.StoredItemCounts["矿"] = 1;
                var start = Node(1, $"AwardItem('矿',{delta})");
                LinkTo(start, Node(2, "AwardItem('正确奖励',1)", $"IsPlayerItemCount('矿','大于等于',{threshold}) == true"),
                    Node(3, "AwardItem('错误奖励',1)", $"IsPlayerItemCount('矿','大于等于',{threshold}) == false"));
                var hint = Indicators.Hint(start);
                Require(hint.Contains("正确奖励") && !hint.Contains("错误奖励"), hint);
                Require(!hint.Contains("✗ 矿"), "pre-script inventory must not label a later check as failed: " + hint);
                Require(PlayerDataManager._Instance.ItemCount("矿") == 1, "preview changed actual inventory");
            });
        }
        Test("unreadable original inventory stays unknown after an item delta", () =>
        {
            PlayerDataManager._Instance.Dialogue = null;
            var start = Node(1, "AwardItem('矿',1)");
            LinkTo(start, Node(2, "AwardItem('错误奖励',1)", "IsPlayerItemCount('矿','大于等于',1) == true"));
            var hint = Indicators.Hint(start);
            Require(hint.Contains("未知") && !hint.Contains("错误奖励"), hint);
        });
        Test("variable delta never selects the old variable branch", () =>
        {
            IndicatorHarness.DialogueVariableState.Variables["进度"] = "0";
            var start = Node(1, "Variable['进度']=Variable['进度']+1");
            LinkTo(start, Node(2, "AwardItem('新状态奖励',1)", "Variable['进度'] == 1"),
                Node(3, "AwardItem('旧状态奖励',1)", "Variable['进度'] == 0"));
            var hint = Indicators.Hint(start);
            Require(!hint.Contains("旧状态奖励") && (hint.Contains("新状态奖励") || hint.Contains("未知")), hint);
        });
        Test("attribute delta never selects the old attribute branch", () =>
        {
            var start = Node(1, "AwardPro('力道',1)");
            LinkTo(start, Node(2, "AwardItem('新状态奖励',1)", "IsPlayerPro('力道','大于等于',11) == true"),
                Node(3, "AwardItem('旧状态奖励',1)", "IsPlayerPro('力道','大于等于',11) == false"));
            var hint = Indicators.Hint(start);
            Require(!hint.Contains("旧状态奖励") && (hint.Contains("新状态奖励") || hint.Contains("未知")), hint);
        });
        Test("known and unknown PC alternatives cannot promise task success", () =>
        {
            var start = Node(1);
            LinkTo(start, Node(2, "SetQuestState('条件任务','success')", actor: 1),
                Node(3, "AwardItem('未知奖励',1)", "1 == 1", actor: 1));
            var hint = Indicators.Hint(start);
            Require(hint.Contains("未知") && hint.Contains("可能分支") && !hint.Contains("✓ 会完成任务"), hint);
            Require(!hint.Contains("未知奖励"), hint);
        });
        Test("same-priority PC backedge retains a different reward option", () =>
        {
            var start = Node(1);
            var previousChoice = Node(2, actor: 1);
            var menu = Node(3);
            LinkTo(start, previousChoice);
            LinkTo(previousChoice, menu);
            LinkTo(menu, previousChoice, Node(4, "AwardItem('可选奖励',1)", actor: 1));
            var hint = Indicators.Hint(start);
            Require(hint.Contains("可选奖励") && hint.Contains("回溯"), hint);
        });
        Test("high-priority PC reward is not hidden by a low-priority selected return", () =>
        {
            var selected = Node(1, actor: 1);
            var menu = Node(2);
            var reward = Node(3, "AwardItem('高优先级合法奖励',1)", actor: 1);
            LinkTo(selected, menu);
            menu.outgoingLinks.Add(new Link(selected) { priority = ConditionPriority.Low });
            menu.outgoingLinks.Add(new Link(reward) { priority = ConditionPriority.High });
            var hint = Indicators.Hint(selected);
            Require(hint.Contains("高优先级合法奖励") && !hint.Contains("回溯"), hint);
        });
        Test("successful life condition awards before the same node's explicit life experience", () =>
        {
            PlayerDataManager._Instance.LifeValues.Add(100);
            var start = Node(1);
            var pass = Node(2, "AwardLifeExp('厨艺',10)", "IsPlayerLife('厨艺','大于等于',100) == true");
            LinkTo(start, pass);
            LinkTo(pass, Node(3, "AwardItem('成功奖励',1)"));
            var hint = Indicators.Hint(start);
            Require(hint.Contains("生活经验：厨艺 +11") && hint.Contains("成功奖励"), hint);
            Require(PlayerDataManager._Instance.LifeValues[0] == 100, "preview changed actual life state");
        });
        Test("two sequential hundreds and one two-hundred reward agree", () =>
        {
            var start = Node(1);
            var first = Node(2, "AwardItem('铜钱',100)", actor: 1);
            LinkTo(first, Node(3, "AwardItem('铜钱',100)"));
            LinkTo(start, first, Node(4, "AwardItem('铜钱',200)", actor: 1));
            var hint = Indicators.Hint(start);
            Require(hint.Contains("共同结果：\n获得：铜钱 ×200") && !hint.Contains("×100"), hint);
        });
        Test("acyclic alternatives have no backtrack label", () =>
        {
            var start = Node(1);
            LinkTo(start, Node(2, "AwardItem('甲',1)", actor: 1), Node(3, "AwardItem('乙',1)", actor: 1));
            var hint = Indicators.Hint(start);
            Require(hint.Contains("甲") && hint.Contains("乙") && !hint.Contains("回溯"), hint);
        });
        return (checks, failures);
    }
}
