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
    Test("future conditional node does not claim its reward", () =>
    {
        var entry = new DialogueEntry();
        entry.outgoingLinks.Add(new Link(new DialogueEntry { conditionsString = "Mystery()", userScript = "AwardItem('宝物',1)" }));
        var hint = Indicators.Hint(entry);
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
        Require(lines[6].Kind == FuturePreviewKind.State);
        Require(lines[7].Kind == FuturePreviewKind.State);
        Require(lines[8].Kind == FuturePreviewKind.Unknown);
        Require(lines[9].Kind == FuturePreviewKind.Boundary);
    });
    Test("quest state previews name the quest once", () =>
    {
        var entry = new DialogueEntry
        {
            userScript = "SetQuestState(\"打铁教程\", \"active\");\nSetQuestEntryState(\"打铁教程\", 1, \"active\");\nAwardItem('1粗铁锭图纸', 1); AwardItem('1铁矿', 3)"
        };
        var hint = Indicators.Hint(entry);
        Require(hint.Contains("任务：打铁教程 状态会变化") && hint.IndexOf("任务：") == hint.LastIndexOf("任务："), hint);
        Require(hint.Contains("获得：1粗铁锭图纸 ×1") && hint.Contains("获得：<color=#FFFFFF>1铁矿</color> ×3"), hint);
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
}
finally
{
    Directory.Delete(root, true);
}
Console.WriteLine($"RESULT {checks - failures}/{checks} passed");
return failures == 0 ? 0 : 1;
