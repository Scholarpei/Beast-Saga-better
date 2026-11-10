using SafeDialogueTools.Core;
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
    Test("first rendered craft option shows its check before selection", () =>
    {
        var button = new StandardUIResponseButton();
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        Require(button.text.StartsWith("3.") && button.text.Contains("✗ 巧艺 10 ≥ 20"), button.text);
    });
    Test("reused response button cannot preview the previous option", () =>
    {
        var button = new StandardUIResponseButton
        {
            response = new Response { destinationEntry = new DialogueEntry { conditionsString = "IsPlayerPro('感知','大于等于',5)" } }
        };
        IndicatorHarness.Render(button, IndicatorHarness.CraftOption());
        Require(button.text.Contains("✗ 巧艺 10 ≥ 20") && !button.text.Contains("感知"), button.text);
    });
    Test("ordinary option stays unmarked when a checked button is reused", () =>
    {
        var button = new StandardUIResponseButton { response = IndicatorHarness.CraftOption() };
        IndicatorHarness.Render(button, new Response { formattedText = new FormattedText { text = "离开" } });
        Require(button.text == "3.离开", button.text);
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
