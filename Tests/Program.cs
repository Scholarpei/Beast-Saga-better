using SafeDialogueTools.Core;
using SafeDialogueTools;
using SafeDialogueTools.Tests;
using PixelCrushers.DialogueSystem;

var failures = 0;
var checks = 0;
void Test(string name, Action action)
{
    checks++;
    try { action(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
}
void Require(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new Exception(message);
}
void Reject(Action action)
{
    try { action(); } catch (IOException) { return; } catch (InvalidDataException) { return; }
    throw new Exception("Unsafe operation was accepted");
}
var root = Path.Combine(Path.GetTempPath(), "SafeDialogueTools-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    Test("iron plate option predicts the item beyond its inventory visibility condition", () =>
    {
        var option = new DialogueEntry { conversationID = 2, id = 2581, conditionsString = "(IsPlayerItemCount('3铁布衫图纸','大于等于',1)==false) and (IsHaveMake('3铁布衫配方')==false)" };
        var node = option;
        for (var index = 0; index < 5; index++)
        {
            var next = new DialogueEntry { id = 2582 + index };
            node.outgoingLinks.Add(new Link(next));
            node = next;
        }
        node.userScript = "AwardItem(\"3铁布衫图纸\", 1)";
        var hint = Indicators.Hint(option);
        Require(hint.Contains("预测未来") && hint.Contains("获得：3铁布衫图纸 ×1") && !hint.Contains("复合或未知条件"), hint);
    });
    Test("axe option predicts consumption and stops before choosing a weapon", () =>
    {
        var option = new DialogueEntry { id = 891, conditionsString = "IsPlayerItemCount('全村最好的斧子','大于等于',1)==true" };
        var consume = new DialogueEntry { id = 892, userScript = "AwardItem('全村最好的斧子', -1)" };
        option.outgoingLinks.Add(new Link(consume));
        var choose = new DialogueEntry { id = 899 };
        consume.outgoingLinks.Add(new Link(choose));
        choose.outgoingLinks.Add(new Link(new DialogueEntry { id = 924, userScript = "AwardEquip('2青铜靴',1)" }));
        choose.outgoingLinks.Add(new Link(new DialogueEntry { id = 918, userScript = "AwardEquip('2银丝手套',1)" }));
        choose.outgoingLinks.Add(new Link(new DialogueEntry { id = 912, userScript = "AwardEquip('2盘花棍',1)" }));
        choose.outgoingLinks.Add(new Link(new DialogueEntry { id = 906, userScript = "AwardEquip('2苗刀',1)" }));
        choose.outgoingLinks.Add(new Link(new DialogueEntry { id = 900, userScript = "AwardEquip('2铸铁剑',1)" }));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("共同结果："), hint);
        Require(hint.Contains("失去：全村最好的斧子 ×1"), hint);
        Require(hint.Contains("可能分支："), hint);
        Require(hint.Contains("├─ 分支1\n│  获得装备：<color=#4DE28A>2铸铁剑</color> ×1"), hint);
        Require(hint.Contains("├─ 分支2\n│  获得装备：2苗刀 ×1"), hint);
        Require(hint.Contains("└─ 分支5\n   获得装备：2青铜靴 ×1"), hint);
        Require(hint.IndexOf("全村最好的斧子") == hint.LastIndexOf("全村最好的斧子"), hint);
        Require(!hint.Contains("含演出") && !hint.Contains("回到已预测内容"), hint);
        Require(hint.IndexOf("分支1") < hint.IndexOf("分支2") && hint.IndexOf("分支2") < hint.IndexOf("分支3"), hint);
        Require(!hint.Contains("到后续分支为止"), hint);
    });
    Test("visual-only sequencer still exposes later branch rewards", () =>
    {
        var entry = new DialogueEntry { id = 903 };
        entry.outgoingLinks.Add(new Link(new DialogueEntry
        {
            id = 904,
            Sequence = "SendMessage(ShowCGBack,CGblack+F,Dialogue);\nSendMessage(AwaitTime,2+G,Dialogue)@Message(F);",
            userScript = "AwardEquip('2铸铁剑',1)"
        }));
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("获得装备：<color=#4DE28A>2铸铁剑</color>") && !hint.Contains("含演出"), hint);
    });
    Test("a return option is marked backtrack without opening sibling branches", () =>
    {
        var reconsider = new DialogueEntry { id = 924 };
        var choose = new DialogueEntry { id = 899 };
        reconsider.outgoingLinks.Add(new Link(choose));
        choose.outgoingLinks.Add(new Link(reconsider));
        choose.outgoingLinks.Add(new Link(new DialogueEntry { id = 900, userScript = "AwardEquip('2铸铁剑',1)" }));
        var hint = Indicators.Hint(reconsider);
        Require(hint.Contains("可回溯"), hint);
        Require(!hint.Contains("铸铁剑"), hint);
    });
    Test("item names use native rank colors", () =>
    {
        var hint = Indicators.Hint(new DialogueEntry { userScript = "AwardItem('1铁矿',3); AwardEquip('2铸铁剑',1)" });
        Require(hint.Contains("<color=#") && hint.Contains(">1铁矿</color>") && hint.Contains(">2铸铁剑</color>"), hint);
    });
    Test("affection variables and sequential item effects are previewed without treating flags as love", () =>
    {
        var entry = new DialogueEntry { userScript = "Variable['阿离的好感度'] = Variable['阿离的好感度'] - 2;\nVariable['猫花来好感度30事件已完成'] = true;\nAwardItem('铜钱', 500)" };
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("好感：阿离的好感度 -2") && hint.Contains("获得：铜钱 ×500") && !hint.Contains("事件已完成"), hint);
    });
    Test("unknown control flow never leaks a conditional reward", () =>
    {
        var entry = new DialogueEntry { userScript = "if Mystery() then AwardItem('宝物',1) end" };
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("后续脚本未知") && !hint.Contains("宝物"), hint);
    });
    Test("unknown call preserves earlier effects and hides later rewards", () =>
    {
        var entry = new DialogueEntry { userScript = "AwardItem('铜钱', -5); Mystery(); AwardItem('宝物',1)" };
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("失去：铜钱 ×5") && hint.Contains("后续脚本未知") && !hint.Contains("宝物"), hint);
    });
    Test("merchant shop scripts are predicted instead of reported unknown", () =>
    {
        var askTeacher = new DialogueEntry { id = 1274 };
        var reply = new DialogueEntry { id = 1275 };
        var shop = new DialogueEntry { id = 2610, userScript = "OpenShop(\"狸子的书坊\")" };
        askTeacher.outgoingLinks.Add(new Link(reply));
        reply.outgoingLinks.Add(new Link(shop));
        var hint = Indicators.Hint(askTeacher);
        Require(hint.Contains("打开商店：狸子的书坊"), hint);
        Require(!hint.Contains("后续脚本未知"), hint);
    });
    Test("shop opening stops prediction at the shop boundary", () =>
    {
        var shop = new DialogueEntry { id = 2610, userScript = "OpenShop(\"狸子的书坊\")" };
        shop.outgoingLinks.Add(new Link(new DialogueEntry { id = 2611, userScript = "AwardItem('宝物', 1)" }));
        var hint = Indicators.Hint(shop);
        Require(hint.Contains("打开商店：狸子的书坊") && !hint.Contains("宝物") && !hint.Contains("后续脚本未知"), hint);
    });
    Test("future conditional node does not claim its reward", () =>
    {
        var entry = new DialogueEntry();
        entry.outgoingLinks.Add(new Link(new DialogueEntry { conditionsString = "Mystery()", userScript = "AwardItem('宝物',1)" }));
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("后续条件未知") && !hint.Contains("宝物"), hint);
    });
    Test("single variable-gated option predicts its real future", () =>
    {
        DialogueVariableState.Reset();
        DialogueVariableState.Variables["花铺老板的孩子"] = "0";
        var option = new DialogueEntry { id = 619 };
        var answer = new DialogueEntry { id = 622 };
        var confirmation = new DialogueEntry
        {
            id = 623,
            conditionsString = "Variable[\"花铺老板的孩子\"] == 0"
        };
        var questStart = new DialogueEntry
        {
            id = 3309,
            userScript = "SetQuestState(\"支线命值几钱\", \"active\");\nSetQuestEntryState(\"支线命值几钱\", 1, \"active\")"
        };
        option.outgoingLinks.Add(new Link(answer));
        answer.outgoingLinks.Add(new Link(confirmation));
        confirmation.outgoingLinks.Add(new Link(questStart));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("任务：支线命值几钱 → active"), hint);
        Require(hint.Contains("任务：支线命值几钱 第1步 → active"), hint);
        Require(!hint.Contains("后续条件未知"), hint);
        Require(DialogueVariableState.Reads == 1, DialogueVariableState.Reads.ToString());
    });
    Test("unknown variable value still stops before leaking rewards", () =>
    {
        DialogueVariableState.Reset();
        var option = new DialogueEntry { id = 619 };
        var confirmation = new DialogueEntry
        {
            id = 623,
            conditionsString = "Variable[\"花铺老板的孩子\"] == 0"
        };
        var questStart = new DialogueEntry { id = 3309, userScript = "SetQuestState(\"支线命值几钱\", \"active\")" };
        option.outgoingLinks.Add(new Link(confirmation));
        confirmation.outgoingLinks.Add(new Link(questStart));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("后续条件未知") && !hint.Contains("active"), hint);
    });
    Test("mutually exclusive variable states continue through the only visible option", () =>
    {
        DialogueVariableState.Reset();
        DialogueVariableState.Variables["密室的状态"] = "1";
        var option = new DialogueEntry { id = 2748 };
        var question = new DialogueEntry { id = 2749 };
        var deadA = new DialogueEntry
        {
            id = 2750,
            conditionsString = "(Variable[\"密室的状态\"] == 2) or (Variable[\"密室的状态\"] == 0)"
        };
        var deadB = new DialogueEntry
        {
            id = 2751,
            conditionsString = "Variable[\"密室的状态\"] == 3"
        };
        var alive = new DialogueEntry
        {
            id = 2752,
            conditionsString = "Variable[\"密室的状态\"] == 1 or (Variable[\"密室的状态\"] == 4)"
        };
        var reply = new DialogueEntry { id = 2754 };
        var reward = new DialogueEntry { id = 2755, userScript = "AwardItem(\"铜钱\", 500)" };
        option.outgoingLinks.Add(new Link(question));
        question.outgoingLinks.Add(new Link(deadA));
        question.outgoingLinks.Add(new Link(deadB));
        question.outgoingLinks.Add(new Link(alive));
        alive.outgoingLinks.Add(new Link(reply));
        reply.outgoingLinks.Add(new Link(reward));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("获得：铜钱 ×500"), hint);
        Require(!hint.Contains("后续有分支条件未知"), hint);
        Require(!hint.Contains("他活了下来") && !hint.Contains("他为保护村庄"), hint);
    });
    Test("unknown disjunction still stops before conditional rewards", () =>
    {
        DialogueVariableState.Reset();
        var option = new DialogueEntry { id = 2748 };
        var gate = new DialogueEntry
        {
            id = 2752,
            conditionsString = "(Variable[\"密室的状态\"] == 1) or (Variable[\"密室的状态\"] == 4)"
        };
        var reward = new DialogueEntry { id = 2755, userScript = "AwardItem('宝物',1)" };
        option.outgoingLinks.Add(new Link(gate));
        gate.outgoingLinks.Add(new Link(reward));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("后续条件未知") && !hint.Contains("宝物"), hint);
    });
    Test("cycles terminate and never multiply the same reward", () =>
    {
        var entry = new DialogueEntry { userScript = "AwardItem('铜钱',1)" };
        entry.outgoingLinks.Add(new Link(entry));
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("可回溯") && hint.Split("获得").Length == 2, hint);
    });
    Test("first rendered craft option shows its check before selection", () =>
    {
        var button = new StandardUIResponseButton();
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        Require(button.text == "3.仿造戒指（判定：巧艺20）", button.text);
        Require(Indicators.HoveredButton == button, "hover button was not cached");
        Require(Indicators.HoverHint.Contains("预测未来") && Indicators.HoverHint.Contains("✗ 巧艺 10 ≥ 20"), Indicators.HoverHint);
    });
    Test("reused response button cannot preview the previous option", () =>
    {
        var button = new StandardUIResponseButton
        {
            response = new Response { destinationEntry = new DialogueEntry { conditionsString = "IsPlayerPro('感知','大于等于',5)" } }
        };
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        Require(Indicators.HoverHint.Contains("✗ 巧艺 10 ≥ 20") && !Indicators.HoverHint.Contains("感知"), Indicators.HoverHint);
    });
    Test("ordinary option stays unmarked when a checked button is reused", () =>
    {
        var button = new StandardUIResponseButton { response = IndicatorHarness.CraftOption() };
        IndicatorHarness.Render(button, new Response { formattedText = new FormattedText { text = "离开" } });
        Require(button.text == "3.离开" && Indicators.HoverHint == "", button.text + "|" + Indicators.HoverHint);
    });
    Test("leaving or hiding a response clears its hover preview", () =>
    {
        var button = new StandardUIResponseButton();
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        IndicatorHarness.Leave(button);
        Require(Indicators.HoveredButton == null && Indicators.HoverHint == "");
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        button.isVisible = false;
        RuntimeDriver.ClearStaleHover(button);
        Require(Indicators.HoveredButton == null && Indicators.HoverHint == "");
    });
    Test("hover preview uses the current button response only", () =>
    {
        var button = new StandardUIResponseButton();
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        IndicatorHarness.Hover(button, new Response { formattedText = new FormattedText { text = "离开" } });
        Require(Indicators.HoverHint == "", Indicators.HoverHint);
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        IndicatorHarness.Rebind(button, new Response { formattedText = new FormattedText { text = "离开" } });
        Require(Indicators.HoverHint == "" && Indicators.HoveredButton == null, Indicators.HoverHint);
    });
    Test("clicking a response immediately closes its hover preview", () =>
    {
        var button = new StandardUIResponseButton();
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        IndicatorHarness.Click(button);
        Require(Indicators.HoveredButton == null && Indicators.HoverHint == "");
    });
    Test("future preview presentation separates categories", () =>
    {
        var lines = new[]
        {
            "预测未来",
            "✗ 巧艺 10 ≥ 20",
            "好感：阿离 -2",
            "获得：铜钱 ×500",
            "失去：全村最好的斧子 ×1",
            "获得装备：2铸铁剑 ×1",
            "入队：鹿柒",
            "离队：狮逍",
            "经验 +600",
            "武学升段流程：掌 39 → 60",
            "剧情：猫花来好感度30事件已完成",
            "任务：打铁教程 状态会变化",
            "? 后续条件未知，未继续预测",
            "（到后续分支为止）"
        }.Select(FuturePreviewPresentation.Classify).ToArray();
        Require(lines[0].Kind == FuturePreviewKind.Title);
        Require(lines[1].Kind == FuturePreviewKind.Check);
        Require(lines[2].Kind == FuturePreviewKind.Affection);
        Require(lines[3].Kind == FuturePreviewKind.Gain);
        Require(lines[4].Kind == FuturePreviewKind.Loss);
        Require(lines[5].Kind == FuturePreviewKind.Equipment);
        Require(lines[6].Kind == FuturePreviewKind.Party);
        Require(lines[7].Kind == FuturePreviewKind.Party);
        Require(lines[8].Kind == FuturePreviewKind.Progression);
        Require(lines[9].Kind == FuturePreviewKind.Progression);
        Require(lines[10].Kind == FuturePreviewKind.State);
        Require(lines[11].Kind == FuturePreviewKind.State);
        Require(lines[12].Kind == FuturePreviewKind.Unknown);
        Require(lines[13].Kind == FuturePreviewKind.Boundary);
    });
    Test("quest state previews name the quest once", () =>
    {
        var entry = new DialogueEntry
        {
            userScript = "SetQuestState(\"打铁教程\", \"active\");\nSetQuestEntryState(\"打铁教程\", 1, \"active\");\nAwardItem('1粗铁锭图纸', 1); AwardItem('1铁矿', 3)"
        };
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("任务：打铁教程 → active") && hint.Contains("任务：打铁教程 第1步 → active"), hint);
        Require(!hint.Contains("状态会变化"), hint);
        Require(hint.Contains("获得：1粗铁锭图纸 ×1") && hint.Contains("获得：<color=#FFFFFF>1铁矿</color> ×3"), hint);
    });
    Test("priority-one boundaries stop future traversal", () =>
    {
        var save = new DialogueEntry { id = 1, userScript = "SaveData()" };
        var battle = new DialogueEntry
        {
            id = 2,
            userScript = "AwardItem('铜钱',100)",
            Sequence = "SendMessage(EnterBattleFullCreate,Battle_songhuo_xiaotou1BBone+1+禾兴城+181+null+conversTitleFail+entryIdFail,Dialogue);"
        };
        var unreachable = new DialogueEntry { id = 3, userScript = "AwardItem('宝物',1)" };
        save.outgoingLinks.Add(new Link(battle));
        battle.outgoingLinks.Add(new Link(unreachable));
        var hint = Indicators.Hint(save);
        Require(hint.Contains("（此分支会立即存档）"), hint);
        Require(hint.Contains("（进入战斗：Battle_songhuo_xiaotou1BBone，后续不预测）"), hint);
        Require(!hint.Contains("宝物"), hint);
    });
    Test("map transition is reported and stops traversal", () =>
    {
        var entry = new DialogueEntry { id = 1, userScript = "AwardItem('铜钱',100); GoOtherMap('黑市')" };
        entry.outgoingLinks.Add(new Link(new DialogueEntry { id = 2, userScript = "AwardItem('宝物',1)" }));
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("获得：铜钱 ×100") && hint.Contains("（切换地图：黑市，后续不预测）"), hint);
        Require(!hint.Contains("宝物"), hint);
    });
    Test("parameterless map transitions still show later script effects", () =>
    {
        var entry = new DialogueEntry { id = 1, userScript = "GoCanOtherMap(); AwardPro('悟性',1)" };
        entry.outgoingLinks.Add(new Link(new DialogueEntry { id = 2, userScript = "AwardItem('宝物',1)" }));
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("悟性 +1") && hint.Contains("（切换地图（目标未知），后续不预测）"), hint);
        Require(!hint.Contains("宝物"), hint);
    });
    Test("party and progression effects are previewed", () =>
    {
        var script = string.Join("\n", new[]
        {
            "AwardItem('铜钱',100)",
            "AwardExp(600)",
            "AwardLifeExp('说服',60)",
            "AwardPro('悟性',1)",
            "AwardWuXueExp('掌',39,60)",
            "FriendAdd('Debutou')",
            "FriendLeave('shixiao')",
            "AwardKongFu('5牧龙歌')",
            "AwardInKongFu('5太极心法')",
            "AwardKongFuSkill('5雪山天风刀势')",
            "AwardFriendKongFu('鹿柒','5牧龙歌')",
            "AwardFriendKongFuSkill('鹿柒','5雪山天风刀势')"
        });
        var hint = Indicators.Hint(new DialogueEntry { userScript = script });
        Require(hint.Contains("经验 +600"), hint);
        Require(hint.Contains("生活经验：说服 +60"), hint);
        Require(hint.Contains("悟性 +1"), hint);
        Require(hint.Contains("武学升段流程：掌 39 → 60"), hint);
        Require(hint.Contains("入队：德捕头"), hint);
        Require(hint.Contains("离队：狮逍"), hint);
        Require(hint.Contains("习得武功：5牧龙歌"), hint);
        Require(hint.Contains("习得内功：5太极心法"), hint);
        Require(hint.Contains("习得技能：5雪山天风刀势"), hint);
        Require(hint.Contains("鹿柒习得武功：5牧龙歌"), hint);
        Require(hint.Contains("鹿柒习得技能：5雪山天风刀势"), hint);
    });
    Test("quest stage changes show exact new states", () =>
    {
        var script = "SetQuestState(\"打铁教程\", \"success\");\nSetQuestEntryState(\"打铁教程\", 2, \"active\");\nSetQuestEntryState(\"打铁教程\", 3, \"success\")";
        var hint = Indicators.Hint(new DialogueEntry { userScript = script });
        Require(hint.Contains("任务：打铁教程 → success"), hint);
        Require(hint.Contains("任务：打铁教程 第2步 → active"), hint);
        Require(hint.Contains("任务：打铁教程 第3步 → success"), hint);
    });
    Test("item submission marks whether the quest completes", () =>
    {
        PlayerDataManager._Instance.ItemCounts["2纯铁矿"] = 3;
        var option = new DialogueEntry
        {
            id = 885,
            conditionsString = "CurrentQuestState(\"采集纯铁矿\") == \"active\""
        };
        var lacking = new DialogueEntry
        {
            id = 886,
            conditionsString = "IsPlayerItemCount(\"2纯铁矿\", \"大于等于\", 3)  == false"
        };
        var submit = new DialogueEntry
        {
            id = 887,
            conditionsString = "IsPlayerItemCount(\"2纯铁矿\", \"大于等于\", 3)  == true",
            userScript = "AwardItem(\"2纯铁矿\", -3)"
        };
        var rewardIntro = new DialogueEntry { id = 931 };
        var reward = new DialogueEntry
        {
            id = 2622,
            userScript = "AwardExp(200);\nAwardItem(\"5光棍图纸\", 1);\nSetQuestState(\"采集纯铁矿\", \"success\")"
        };
        var unavailableIntro = new DialogueEntry { id = 888 };
        var unavailable = new DialogueEntry { id = 889, userScript = "SetQuestState(\"采集纯铁矿\", \"success\")" };
        option.outgoingLinks.Add(new Link(lacking));
        option.outgoingLinks.Add(new Link(submit));
        lacking.outgoingLinks.Add(new Link(unavailableIntro));
        unavailableIntro.outgoingLinks.Add(new Link(unavailable));
        submit.outgoingLinks.Add(new Link(rewardIntro));
        rewardIntro.outgoingLinks.Add(new Link(reward));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("✓ 2纯铁矿 3 ≥ 3"), hint);
        Require(hint.Contains("✓ 会完成任务：采集纯铁矿"), hint);
        Require(hint.Contains("失去：2纯铁矿 ×3") && hint.Contains("获得：5光棍图纸 ×1"), hint);
        Require(hint.Contains("任务：采集纯铁矿 → success"), hint);
        Require(!hint.Contains("后续有分支条件未知") && !hint.Contains("宝物"), hint);
    });
    Test("insufficient items show completion as unavailable", () =>
    {
        PlayerDataManager._Instance.ItemCounts["2纯铁矿"] = 2;
        var option = new DialogueEntry
        {
            id = 885,
            conditionsString = "CurrentQuestState(\"采集纯铁矿\") == \"active\""
        };
        var lacking = new DialogueEntry { id = 886, conditionsString = "IsPlayerItemCount(\"2纯铁矿\", \"大于等于\", 3)  == false" };
        var submit = new DialogueEntry
        {
            id = 887,
            conditionsString = "IsPlayerItemCount(\"2纯铁矿\", \"大于等于\", 3)  == true",
            userScript = "AwardItem(\"2纯铁矿\", -3)"
        };
        var reward = new DialogueEntry { id = 889, userScript = "SetQuestState(\"采集纯铁矿\", \"success\")" };
        submit.outgoingLinks.Add(new Link(reward));
        option.outgoingLinks.Add(new Link(lacking));
        option.outgoingLinks.Add(new Link(submit));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("✗ 2纯铁矿 2 ≥ 3"), hint);
        Require(hint.Contains("✗ 本次不会完成任务：采集纯铁矿"), hint);
        Require(!hint.Contains("失去：2纯铁矿") && !hint.Contains("5光棍图纸"), hint);
    });
    Test("equivalent crafting options retain their reward beside one backtrack", () =>
    {
        var selector = new DialogueEntry { id = 899 };
        var reconsider = new DialogueEntry { id = 924 };
        reconsider.outgoingLinks.Add(new Link(selector));
        var choices = new[]
        {
            (id: 900, confirm: 901, reward: 905, name: "2铸铁剑"),
            (id: 906, confirm: 907, reward: 911, name: "2苗刀"),
            (id: 912, confirm: 913, reward: 917, name: "2盘花棍"),
            (id: 918, confirm: 919, reward: 923, name: "2青铜靴"),
            (id: 925, confirm: 926, reward: 930, name: "2银丝手套")
        };
        foreach (var choice in choices)
        {
            var option = new DialogueEntry { id = choice.id };
            var confirm = new DialogueEntry { id = choice.confirm };
            var reward = new DialogueEntry
            {
                id = choice.reward,
                userScript = "SetQuestEntryState(\"全村最好的斧子\", 4, \"success\");\nSetQuestEntryState(\"全村最好的斧子\", 5, \"success\");\nSetQuestState(\"全村最好的斧子\", \"success\");\nAwardEquip('" + choice.name + "',1)"
            };
            option.outgoingLinks.Add(new Link(confirm));
            confirm.outgoingLinks.Add(new Link(reward));
            confirm.outgoingLinks.Add(new Link(reconsider));
            selector.outgoingLinks.Add(new Link(option));
        }
        foreach (var choice in choices)
        {
            var option = selector.outgoingLinks.Select(link => link.Entry).Single(node => node.id == choice.id);
            var hint = Indicators.Hint(option);
            var rewardText = choice.name;
            Require(hint.Contains(rewardText), choice.id + "|" + hint);
            Require(hint.Contains("任务：全村最好的斧子 → success"), choice.id + "|" + hint);
            Require(hint.Contains("回溯"), choice.id + "|" + hint);
            var otherRewards = choices.Where(other => other.id != choice.id).Select(other => other.name);
            Require(otherRewards.All(text => !hint.Contains(text)), choice.id + "|" + hint);
        }
    });
    Test("real chapter transition combines quest stages progression and map boundary", () =>
    {
        var result = FutureScriptPreview.Parse("SetQuestEntryState(\"第一章\", 24, \"success\");\nSetQuestEntryState(\"第一章\", 25, \"active\");\nGoCanOtherMap();\nAwardExp(3000);\nAwardPro(\"悟性\", 1)");
        Require(result.StopReason is null, result.StopReason ?? "");
        Require(result.Effects.Count == 5, result.Effects.Count.ToString());
        Require(result.Effects[0] is { Kind: "quest", Target: "第一章", Detail: "第24步 → success" });
        Require(result.Effects[1] is { Kind: "quest", Target: "第一章", Detail: "第25步 → active" });
        Require(result.Effects[2].Kind == "map-unknown");
        Require(result.Effects[3] is { Kind: "exp", Amount: 3000 });
        Require(result.Effects[4] is { Kind: "attribute", Target: "悟性", Amount: 1 });
        var hint = Indicators.Hint(new DialogueEntry { userScript = "SetQuestEntryState(\"第一章\", 24, \"success\");\nSetQuestEntryState(\"第一章\", 25, \"active\");\nGoCanOtherMap();\nAwardExp(3000);\nAwardPro(\"悟性\", 1)" });
        Require(hint.Contains("任务：第一章 第24步 → success") && hint.Contains("任务：第一章 第25步 → active"), hint);
        Require(hint.Contains("经验 +3000") && hint.Contains("悟性 +1"), hint);
        Require(hint.Contains("（切换地图（目标未知），后续不预测）"), hint);
    });
    Test("real party kungfu transfer combines affection item and friend reward", () =>
    {
        var script = "AddFriendLoveNum(\"猴千冲\", 1);\nAwardItem(\"4大力金刚掌\", -1);\nAwardFriendKongFu(\"猴千冲\", \"4大力金刚掌\")";
        var result = FutureScriptPreview.Parse(script);
        Require(result.StopReason is null && result.Effects.Count == 3, result.StopReason ?? result.Effects.Count.ToString());
        Require(result.Effects[0] is { Kind: "love", Target: "猴千冲", Amount: 1 });
        Require(result.Effects[1] is { Kind: "item", Target: "4大力金刚掌", Amount: -1 });
        Require(result.Effects[2] is { Kind: "friend-kungfu", Target: "猴千冲", Detail: "4大力金刚掌" });
        var hint = Indicators.Hint(new DialogueEntry { userScript = script });
        Require(hint.Contains("好感：猴千冲 +1") && hint.Contains("失去：4大力金刚掌 ×1") && hint.Contains("猴千冲习得武功：4大力金刚掌"), hint);
    });
    Test("real martial level flow is not reported as direct experience", () =>
    {
        var result = FutureScriptPreview.Parse("AwardWuXueExp(\"掌\", 39, 60)");
        Require(result.StopReason is null && result.Effects.Count == 1, result.StopReason ?? result.Effects.Count.ToString());
        Require(result.Effects[0] is { Kind: "martial-flow", Target: "掌", Detail: "39 → 60" });
        var hint = Indicators.Hint(new DialogueEntry { userScript = "AwardWuXueExp(\"掌\", 39, 60)" });
        Require(hint.Contains("武学升段流程：掌 39 → 60") && !hint.Contains("经验 +"), hint);
    });
    Test("real persuasion reward still previews after its check", () =>
    {
        var option = new DialogueEntry { id = 1 };
        var pass = new DialogueEntry { id = 2, conditionsString = "IsPlayerLife(\"说服\", \"大于等于\", 20)  == true", userScript = "AwardLifeExp(\"说服\", 200)" };
        var fail = new DialogueEntry { id = 3, conditionsString = "IsPlayerLife(\"说服\", \"大于等于\", 20)  == false" };
        option.outgoingLinks.Add(new Link(pass));
        option.outgoingLinks.Add(new Link(fail));
        var hint = Indicators.Hint(option);
        Require(hint.Contains("✗ 说服 10 ≥ 20（含队友及说服修正）"), hint);
        Require(!hint.Contains("生活经验：说服 +200"), hint);
    });
    Test("real battle sequence stops without leaking later rewards", () =>
    {
        var entry = new DialogueEntry { id = 1, userScript = "AwardItem(\"铜钱\",100)" };
        var battle = new DialogueEntry
        {
            id = 2,
            Sequence = "SendMessage(EnterBattleFullCreate,Battle_songhuo_xiaotou1BBone+1+禾兴城+181+null+conversTitleFail+entryIdFail,Dialogue);"
        };
        var later = new DialogueEntry { id = 3, userScript = "AwardItem(\"宝物\",1)" };
        entry.outgoingLinks.Add(new Link(battle));
        battle.outgoingLinks.Add(new Link(later));
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("（进入战斗：Battle_songhuo_xiaotou1BBone，后续不预测）"), hint);
        Require(hint.Contains("获得：铜钱 ×100") && !hint.Contains("宝物"), hint);
    });
    Test("new store has no implicit read target", () => Require(new CheckpointStore(root, "v1").Selected() is null));
    var store = new CheckpointStore(root, "v1");
    string Create() => store.Create(path => File.WriteAllText(Path.Combine(path, "save.es3"), "complete-save"), path =>
        Require(File.ReadAllText(Path.Combine(path, "save.es3")) == "complete-save"));
    Test("generation is validated before it can be selected", () =>
    {
        var generation = Create();
        Require(store.Selected() is null);
        store.Select(generation);
        Require(store.Selected() == generation);
        Require(File.Exists(Path.Combine(store.Validate(generation), "save.es3")));
    });
    Test("interrupted writer preserves the previous read target", () =>
    {
        var selected = store.Selected();
        Reject(() => store.Create(path => { File.WriteAllText(Path.Combine(path, "save.es3"), "partial"); throw new IOException("disk full"); }, _ => { }));
        Require(store.Selected() == selected);
        Require(Directory.GetDirectories(root).All(path => !path.EndsWith(".pending")));
    });
    Test("failed validation never publishes a generation", () =>
    {
        var before = Directory.GetDirectories(root).Length;
        Reject(() => store.Create(path => File.WriteAllText(Path.Combine(path, "save.es3"), "invalid"), _ => throw new InvalidDataException("missing player")));
        Require(before == Directory.GetDirectories(root).Length);
    });
    Test("modified payload cannot become the read target", () =>
    {
        var selected = store.Selected();
        var generation = Create();
        File.AppendAllText(Path.Combine(root, generation, "save.es3"), "tampered");
        Reject(() => store.Select(generation));
        Require(store.Selected() == selected);
    });
    Test("missing payload blocks reading without falling back", () =>
    {
        var generation = Create(); store.Select(generation);
        File.Delete(Path.Combine(root, generation, "save.es3"));
        Reject(() => store.Selected());
    });
    Test("other game versions cannot read a checkpoint", () =>
    {
        var generation = Create();
        Reject(() => new CheckpointStore(root, "v2").Select(generation));
    });
    Test("generation path traversal is rejected", () => Reject(() => store.Validate("../outside")));
    Test("pure attribute comparison reports pass", () =>
    {
        var result = CheckPreview.Evaluate("IsPlayerPro(\"感知\", \"大于等于\", 15) == true", (_, _) => 18);
        Require(result.Passed == true && result.Text.Contains("18") && result.Text.Contains("15"));
    });
    Test("false branch is interpreted correctly", () =>
        Require(CheckPreview.Evaluate("IsPlayerPro('灵巧', '大于等于', 15) == false", (_, _) => 12).Passed == true));
    Test("life check preview never executes a predicate", () =>
        Require(CheckPreview.Evaluate("IsPlayerLife('说服', '大于等于', 60) == true", (_, _) => 70).Passed == true));
    Test("unknown compound expression does not read or execute anything", () =>
    {
        var reads = 0;
        var result = CheckPreview.Evaluate("IsPlayerPro('感知','大于等于',15) and AwardExp(100)", (_, _) => { reads++; return 18; });
        Require(result.Passed is null && reads == 0);
    });
    Test("unknown function is not called", () =>
        Require(CheckPreview.Evaluate("Random(100) > 10", (_, _) => throw new Exception("must not read")).Passed is null));
    Test("unavailable character state yields unknown", () =>
        Require(CheckPreview.Evaluate("IsPlayerLife('说服','大于等于',60)", (_, _) => null).Passed is null));
    Test("unsupported native operator yields unknown", () =>
        Require(CheckPreview.Evaluate("IsPlayerLife('说服','大于',60)", (_, _) => 70).Passed is null));
    Test("NaN cannot be claimed as a definite result", () =>
        Require(CheckPreview.Evaluate("IsPlayerPro('感知','大于等于',15)", (_, _) => double.NaN).Passed is null));
    Test("enabling config cannot unlock unverified production saves", () =>
        Require(!SafetyPolicy.CanOperate(false, true)));
    Test("production operation requires config and authorization marker", () =>
    {
        Require(!SafetyPolicy.CanOperate(false, true, false));
        Require(!SafetyPolicy.CanOperate(false, false, true));
        Require(SafetyPolicy.CanOperate(false, true, true));
    });
    Test("disabled checkpoints leave even sandbox writes off", () =>
        Require(!SafetyPolicy.CanOperate(true, false)));
    Test("verified sandbox can enable writes", () =>
        Require(SafetyPolicy.CanOperate(true, true)));
    Test("late native callback after timeout cannot load original quick1", () =>
        Require(SafetyPolicy.Route(true, false, true, 1) == LoadRoute.Reject));
    Test("cancelled pending callback never falls through", () =>
        Require(SafetyPolicy.Route(true, false, false, 1) == LoadRoute.Reject));
    Test("wrong native index cannot read an unrelated save", () =>
        Require(SafetyPolicy.Route(true, true, false, 2) == LoadRoute.Reject));
    Test("expected live request goes to isolated clone", () =>
        Require(SafetyPolicy.Route(true, true, false, 1) == LoadRoute.Isolated));
    Test("ordinary native loads remain unchanged", () =>
        Require(SafetyPolicy.Route(false, false, false, 2) == LoadRoute.Original));
    Test("panel toggles only while the game is focused and no field accepts text", () =>
    {
        CommandPanelState.Open = false;
        CommandPanelState.Toggle(true, false);
        Require(CommandPanelState.Open);
        CommandPanelState.Toggle(true, false);
        Require(!CommandPanelState.Open);
        CommandPanelState.Toggle(true, true);
        Require(!CommandPanelState.Open);
        CommandPanelState.Toggle(false, false);
        Require(!CommandPanelState.Open);
    });
    Test("panel stays closed outside gameplay and the menu reports guidance", () =>
    {
        CommandPanelState.Open = false;
        CommandPanelState.Toggle(true, false, canShowPanel: false);
        Require(!CommandPanelState.Open);
        CommandPanelState.Open = true;
        CommandPanelState.Toggle(true, false, canShowPanel: false);
        Require(!CommandPanelState.Open);
    });
    Test("panel exit keys stay blocked on the closing frame", () =>
    {
        Require(PanelKeySuppression.Blocks(true, true, -1, 12));
        Require(PanelKeySuppression.Blocks(true, false, 12, 12));
        Require(!PanelKeySuppression.Blocks(true, false, 11, 12));
        Require(!PanelKeySuppression.Blocks(false, true, 12, 12));
    });
    Test("starting a load closes the panel and releases pointer capture", () =>
    {
        CommandPanelState.Open = true;
        CommandPanelState.SelectTab(CommandPanelTab.Archive);
        CommandPanelState.SetPointerBounds(0, 0, 1920, 1080);
        Require(CommandPanelState.IsPointerBlocked(10, 10));
        CommandPanelState.Close();
        Require(!CommandPanelState.Open);
        Require(!CommandPanelState.IsPointerBlocked(10, 10));
        Require(CommandPanelState.Tab == CommandPanelTab.Archive);
        CommandPanelState.Reset();
        Require(CommandPanelState.Tab == CommandPanelTab.Status);
    });
    Test("panel state distinguishes restricted, available and faulted tools", () =>
    {
        var restricted = new CommandPanelState.State(false, false, false, false, false, true, false, null, null, null);
        Require(restricted.Status == "存读档受限" && !restricted.CanSave && !restricted.CanLoad);
        var ready = new CommandPanelState.State(true, false, false, false, false, true, true, "checkpoint", "selected", null);
        Require(ready.Status == "存读档可用" && ready.Mode == "隔离副本" && ready.CanSave && ready.CanLoad);
        Require(ready.DialogueCheckpointText == "已建立" && ready.SelectedCheckpointText == "selected" && ready.BlockedText == "安全检查通过");
        var busy = ready with { Busy = true };
        Require(busy.BlockedText == "正在存读档" && !busy.CanSave && !busy.CanLoad);
        var faulted = ready with { Faulted = true };
        Require(faulted.Status == "插件异常" && !faulted.CanSave && !faulted.CanLoad && faulted.BlockedText.Contains("停用"));
        var dialogueReady = ready with { Dialogue = true, DialogueCheckpoint = "before-conversation" };
        Require(dialogueReady.CanSave && dialogueReady.CanLoad);
        var dialogueWithoutCheckpoint = dialogueReady with { DialogueCheckpoint = null };
        Require(!dialogueWithoutCheckpoint.CanSave && dialogueWithoutCheckpoint.DialogueCheckpointText == "未建立");
    });
    Test("panel actions reuse safe save and load commands", () =>
    {
        var calls = new List<string>();
        var actions = new CommandPanelState.Actions(() => calls.Add("save"), () => calls.Add("load"));
        var ready = new CommandPanelState.State(true, false, false, false, false, true, true, "checkpoint", "selected", null);
        CommandPanelState.RunSave(ready, actions);
        CommandPanelState.RunLoad(ready, actions);
        Require(calls.SequenceEqual(new[] { "save", "load" }), string.Join(",", calls));
        calls.Clear();
        var busy = ready with { Busy = true };
        CommandPanelState.RunSave(busy, actions);
        CommandPanelState.RunLoad(busy, actions);
        var dialogue = ready with { Dialogue = true, DialogueCheckpoint = null };
        CommandPanelState.RunSave(dialogue, actions);
        Require(calls.Count == 0, string.Join(",", calls));
    });
    Test("cheat categories match the agreed object-oriented layout", () =>
    {
        var sections = CheatSectionCatalog.All;
        Require(sections.Select(item => item.Section).SequenceEqual(new[]
        {
            CheatSection.Character, CheatSection.Points, CheatSection.Items,
            CheatSection.Combat, CheatSection.World, CheatSection.Other
        }));
        Require(sections.Select(item => item.Label).SequenceEqual(new[]
        {
            "角色属性", "点数", "物品", "战斗", "世界", "其他"
        }));
        Require(CheatSectionCatalog.Default == CheatSection.Character);
        Require(CheatSectionCatalog.IsAvailable(CheatSection.Character));
        Require(CheatSectionCatalog.IsAvailable(CheatSection.Points));
        Require(CheatSectionCatalog.IsAvailable(CheatSection.Items));
        Require(!CheatSectionCatalog.IsAvailable(CheatSection.Combat));
        Require(!CheatSectionCatalog.IsAvailable(CheatSection.World));
        Require(CheatSectionCatalog.IsAvailable(CheatSection.Other));
    });
    Test("cheat category selection ignores unavailable sections and resets", () =>
    {
        CommandPanelState.Reset();
        Require(CommandPanelState.CheatSection == CheatSection.Character);
        CommandPanelState.SelectCheatSection(CheatSection.Points);
        Require(CommandPanelState.CheatSection == CheatSection.Points);
        CommandPanelState.SelectCheatSection(CheatSection.Items);
        Require(CommandPanelState.CheatSection == CheatSection.Items);
        CommandPanelState.SelectCheatSection(CheatSection.Combat);
        Require(CommandPanelState.CheatSection == CheatSection.Items);
        CommandPanelState.SelectCheatSection(CheatSection.Other);
        Require(CommandPanelState.CheatSection == CheatSection.Other);
        CommandPanelState.Reset();
        Require(CommandPanelState.CheatSection == CheatSection.Character);
    });
    Test("available points never go negative", () =>
    {
        Require(CheatMath.Available(10, 4) == 6);
        Require(CheatMath.Available(4, 10) == 0);
        Require(CheatMath.Available(-1, 0) == 0);
    });
    Test("adding points preserves already used points", () =>
    {
        Require(CheatMath.AddPoints(10, 4, 3) == 13);
        Require(CheatMath.AddPoints(10, 10, -5) == 10);
        Require(CheatMath.AddPoints(3, 10, 0) == 10);
        Require(CheatMath.SetAvailable(4, 7) == 11);
        Require(CheatMath.SetAvailable(4, -1) == 4);
    });
    Test("favor edits clamp at zero and keep fractions", () =>
    {
        Require(CheatMath.AdjustFavor(5f, -10f) == 0f);
        Require(CheatMath.AdjustFavor(5.5f, 2.25f) == 7.75f);
        Require(CheatMath.AdjustFavor(0f, 1f) == 1f);
    });
    Test("level and stat edits are bounded", () =>
    {
        Require(CheatMath.ClampLevel(0) == 1);
        Require(CheatMath.ClampLevel(100000) == 9999);
        Require(CheatMath.ClampLevel(12) == 12);
        Require(CheatMath.ClampStat(-1) == 0);
        Require(CheatMath.ClampStat(100000) == 9999);
    });
    Test("cheat edit policy names the blocking game state", () =>
    {
        string? Reason(bool faulted = false, bool busy = false, bool loading = false, bool saveReady = true,
            bool saving = false, bool battle = false, bool map = false, bool dialogue = false,
            bool canMove = true, bool sequence = false) =>
            CheatEditPolicy.BlockReason(faulted, busy, loading, saveReady, saving, battle, map, dialogue, canMove, sequence);
        Require(Reason() == null);
        Require(Reason(faulted: true)!.Contains("停用修改"));
        Require(Reason(busy: true) == "正在存读档");
        Require(Reason(loading: true) == "正在存读档");
        Require(Reason(saveReady: false) == "游戏尚未准备好");
        Require(Reason(saving: true) == "原生存档正在写入");
        Require(Reason(battle: true) == "战斗或结算期间不可修改");
        Require(Reason(map: true) == "地图正在切换或清理");
        Require(Reason(dialogue: true) == "正在对话");
        Require(Reason(canMove: false) == "玩家控制尚未恢复");
        Require(Reason(sequence: true) == "剧情演出正在执行");
    });
    Test("panel layout fills the whole screen", () =>
    {
        var layout = CommandPanelLayout.Create(1920, 1080);
        Require(layout.X == 0 && layout.Y == 0 && layout.Width == 1920 && layout.Height == 1080,
            $"{layout.X},{layout.Y},{layout.Width},{layout.Height}");
        Require(layout.X + layout.Width == 1920 && layout.Y + layout.Height == 1080);
        var narrow = CommandPanelLayout.Create(800, 600);
        Require(narrow.X == 0 && narrow.Y == 0 && narrow.Width == 800 && narrow.Height == 600,
            $"{narrow.X},{narrow.Y},{narrow.Width},{narrow.Height}");
        Require(narrow.X + narrow.Width == 800 && narrow.Y + narrow.Height == 600);
        var tiny = CommandPanelLayout.Create(320, 480);
        Require(tiny.X >= 0 && tiny.Y >= 0 && tiny.X + tiny.Width <= 320 && tiny.Y + tiny.Height <= 480,
            $"{tiny.X},{tiny.Y},{tiny.Width},{tiny.Height}");
    });
    Test("full-screen archive metrics scale the list and large preview", () =>
    {
        var hd = CommandPanelMetrics.Create(CommandPanelLayout.Create(1280, 720));
        Require(hd.Scale is >= 0.85f and <= 1.05f, "scale " + hd);
        Require(hd.ArchiveListWidth is >= 320f and <= 460f, "list " + hd);
        Require(hd.DetailPreviewHeight is >= 380f and <= 470f, "preview " + hd);
        Require(hd.ArchiveRowHeight >= 84f && hd.ArchiveRowSpacing >= 6f && hd.MajorButtonHeight >= 44f, "rows " + hd);
        var fourK = CommandPanelMetrics.Create(CommandPanelLayout.Create(3840, 2160));
        Require(fourK.Scale > hd.Scale && fourK.ArchiveListWidth > hd.ArchiveListWidth &&
                fourK.DetailPreviewHeight > hd.DetailPreviewHeight && fourK.ArchiveRowHeight > hd.ArchiveRowHeight);
    });
    Test("single-check parser rejects script suffixes", () =>
        Require(CheckPreview.Parse("IsPlayerPro('感知','大于等于',15); AwardExp(100)") is null));
    Test("pruning retains five latest saves and an older selected save", () =>
    {
        var pruneRoot = Path.Combine(root, "prune");
        var pruneStore = new CheckpointStore(pruneRoot, "v1");
        string Next() => pruneStore.Create(path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { });
        var generation = Next(); pruneStore.Select(generation);
        for (var index = 0; index < 8; index++) Next();
        pruneStore.Prune();
        Require(pruneStore.Selected() == generation);
        Require(Directory.GetDirectories(pruneRoot).Length <= 6);
    });
    var history = CheckpointHistoryTests.Run();
    checks += history.Checks;
    failures += history.Failures;
    var cheatFeatures = CheatFeatureTests.Run();
    checks += cheatFeatures.Checks;
    failures += cheatFeatures.Failures;
    var itemFeatures = ItemFeatureTests.Run();
    checks += itemFeatures.Checks;
    failures += itemFeatures.Failures;
    var panelRegressions = PanelRegressionTests.Run();
    checks += panelRegressions.Checks;
    failures += panelRegressions.Failures;
}
finally
{
    Directory.Delete(root, true);
}
Console.WriteLine($"RESULT {checks - failures}/{checks} passed");
return failures == 0 ? 0 : 1;
