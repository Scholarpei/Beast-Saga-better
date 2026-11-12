using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

internal static class CheatFeatureTests
{
    public static (int Checks, int Failures) Run()
    {
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
        void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        Test("cheat sections are unique, ordered and labelled", () =>
        {
            var all = CheatSectionCatalog.All;
            Require(all.Count == 6, all.Count.ToString());
            Require(all.Select(item => item.Section).Distinct().Count() == all.Count);
            Require(all.Select(item => item.Label).Distinct().Count() == all.Count);
            Require(all.All(item => !string.IsNullOrWhiteSpace(item.Label)));
            Require(CheatSectionCatalog.Default == CheatSection.Character);
            Require(CheatSectionCatalog.Get(CheatSection.Points).Label == "点数");
            Require(CheatSectionCatalog.Get(CheatSection.Items).Available);
            Require(CheatSectionCatalog.Get(CheatSection.Items).Badge == null);
        });

        Test("unknown cheat sections do not become selectable and Get rejects them", () =>
        {
            CommandPanelState.Reset();
            CommandPanelState.SelectCheatSection(CheatSection.Points);
            CommandPanelState.SelectCheatSection((CheatSection)999);
            Require(CommandPanelState.CheatSection == CheatSection.Points);
            Throws<InvalidOperationException>(() => CheatSectionCatalog.Get((CheatSection)999));
        });

        Test("integer input accepts signs, whitespace and full-width digits", () =>
        {
            Require(CheatInput.TryParseInt("42", out var value) && value == 42);
            Require(CheatInput.TryParseInt(" 42 ", out value) && value == 42);
            Require(CheatInput.TryParseInt("+7", out value) && value == 7);
            Require(CheatInput.TryParseInt("＋７", out value) && value == 7);
            Require(CheatInput.TryParseInt("-3", out value) && value == -3);
            Require(CheatInput.TryParseInt("−３", out value) && value == -3);
            Require(CheatInput.TryParseInt("007", out value) && value == 7);
            Require(CheatInput.TryParseInt("0", out value) && value == 0);
            Require(CheatInput.TryParseInt("１２３", out value) && value == 123);
            Require(CheatInput.TryParseInt("－５", out value) && value == -5);
            Require(!CheatInput.TryParseInt("", out _));
            Require(!CheatInput.TryParseInt("   ", out _));
            Require(!CheatInput.TryParseInt("abc", out _));
            Require(!CheatInput.TryParseInt("1.0", out _));
            Require(!CheatInput.TryParseInt("1,000", out _));
            Require(!CheatInput.TryParseInt("1 000", out _));
            Require(!CheatInput.TryParseInt("1\t2", out _));
            Require(!CheatInput.TryParseInt("1\n2", out _));
            Require(!CheatInput.TryParseInt("999999999999999", out _));
        });

        Test("float input rejects non-finite and comma decimal input", () =>
        {
            Require(CheatInput.TryParseFloat("1.5", out var value) && Math.Abs(value - 1.5f) < 0.0001f);
            Require(CheatInput.TryParseFloat("１.５", out value) && Math.Abs(value - 1.5f) < 0.0001f);
            Require(CheatInput.TryParseFloat("-0.5", out value) && Math.Abs(value + 0.5f) < 0.0001f);
            Require(CheatInput.TryParseFloat(" 2.25 ", out value) && Math.Abs(value - 2.25f) < 0.0001f);
            Require(CheatInput.TryParseFloat("5", out value) && value == 5f);
            Require(!CheatInput.TryParseFloat("", out _));
            Require(!CheatInput.TryParseFloat("NaN", out _));
            Require(!CheatInput.TryParseFloat("Infinity", out _));
            Require(!CheatInput.TryParseFloat("1,5", out _));
            Require(!CheatInput.TryParseFloat("1 . 5", out _));
            Require(!CheatInput.TryParseFloat("1e3", out _));
        });

        Test("integer input accepts exact 32-bit boundaries and rejects junk", () =>
        {
            Require(CheatInput.TryParseInt("2147483647", out var value) && value == int.MaxValue);
            Require(CheatInput.TryParseInt("-2147483648", out value) && value == int.MinValue);
            Require(CheatInput.TryParseInt("+2147483647", out value) && value == int.MaxValue);
            Require(!CheatInput.TryParseInt("2147483648", out _));
            Require(!CheatInput.TryParseInt("-2147483649", out _));
            Require(!CheatInput.TryParseInt("--1", out _));
            Require(!CheatInput.TryParseInt("1-", out _));
            Require(!CheatInput.TryParseInt("+", out _));
            Require(!CheatInput.TryParseInt("-", out _));
        });

        Test("point arithmetic survives extreme and reversed values", () =>
        {
            Require(CheatMath.Available(10, 4) == 6);
            Require(CheatMath.Available(4, 10) == 0);
            Require(CheatMath.Available(10, -5) == 10);
            Require(CheatMath.Available(-5, -10) == 0);
            Require(CheatMath.Available(int.MinValue, int.MaxValue) == 0);
            Require(CheatMath.Available(int.MaxValue, int.MinValue) == int.MaxValue);
            Require(CheatMath.AddPoints(10, 4, 3) == 13);
            Require(CheatMath.AddPoints(10, 10, -5) == 10);
            Require(CheatMath.AddPoints(3, 10, 0) == 10);
            Require(CheatMath.AddPoints(int.MaxValue, 0, 1) == int.MaxValue);
            Require(CheatMath.AddPoints(0, 0, -1) == 0);
            Require(CheatMath.AddPoints(int.MinValue, 0, -1) == 0);
            Require(CheatMath.SetAvailable(4, 7) == 11);
            Require(CheatMath.SetAvailable(4, -1) == 4);
            Require(CheatMath.SetAvailable(int.MaxValue, 1) == int.MaxValue);
            Require(CheatMath.SetAvailable(-5, 3) == 3);
        });

        Test("favor arithmetic sanitizes NaN and infinity", () =>
        {
            Require(CheatMath.AdjustFavor(5f, -10f) == 0f);
            Require(Math.Abs(CheatMath.AdjustFavor(5.5f, 2.25f) - 7.75f) < 0.0001f);
            Require(CheatMath.AdjustFavor(0f, 1f) == 1f);
            Require(CheatMath.AdjustFavor(float.NaN, 5f) == 5f);
            Require(CheatMath.AdjustFavor(5f, float.NaN) == 5f);
            Require(CheatMath.AdjustFavor(float.PositiveInfinity, 5f) == 5f);
            Require(CheatMath.AdjustFavor(5f, float.PositiveInfinity) == 5f);
            Require(CheatMath.AdjustFavor(-5f, float.NaN) == 0f);
            Require(CheatMath.AdjustFavor(-5f, float.PositiveInfinity) == 0f);
            Require(CheatMath.AdjustFavor(-5f, 2f) == 2f);
            Require(CheatMath.AdjustFavor(5f, -float.MaxValue) == 0f);
            Require(CheatMath.AdjustFavor(float.MaxValue, float.MaxValue) == float.MaxValue);
        });

        Test("level and stat clamping handles reversed and extreme bounds", () =>
        {
            Require(CheatMath.ClampLevel(0) == 1);
            Require(CheatMath.ClampLevel(100000) == 9999);
            Require(CheatMath.ClampLevel(12) == 12);
            Require(CheatMath.ClampLevel(0, 10, 1) == 1);
            Require(CheatMath.ClampLevel(11, 10, 1) == 10);
            Require(CheatMath.ClampLevel(5, 5, 5) == 5);
            Require(CheatMath.ClampLevel(5, 0, 0) == 0);
            Require(CheatMath.ClampStat(-1) == 0);
            Require(CheatMath.ClampStat(int.MinValue) == 0);
            Require(CheatMath.ClampStat(100000) == 9999);
        });

        Test("level edit policy distinguishes skip, reject and clamped apply", () =>
        {
            Require(CheatLevelEditPolicy.Resolve(0, 0, out var value) == CheatLevelEditAction.Skip && value == 0);
            Require(CheatLevelEditPolicy.Resolve(0, 3, out value) == CheatLevelEditAction.Reject && value == 3);
            Require(CheatLevelEditPolicy.Resolve(-1, 0, out value) == CheatLevelEditAction.Reject && value == 0);
            Require(CheatLevelEditPolicy.Resolve(-1, 7, out value) == CheatLevelEditAction.Reject && value == 7);
            Require(CheatLevelEditPolicy.Resolve(1, 0, out value) == CheatLevelEditAction.Apply && value == 1);
            Require(CheatLevelEditPolicy.Resolve(12, 12, out value) == CheatLevelEditAction.Apply && value == 12);
            Require(CheatLevelEditPolicy.Resolve(100000, 0, out value) == CheatLevelEditAction.Apply && value == 9999);
            Require(CheatLevelEditPolicy.Resolve(20, 0, out value, 13) == CheatLevelEditAction.Apply && value == 13);
        });

        Test("only the six verified base stats expose a final value", () =>
        {
            for (var type = 1; type <= 6; type++)
                Require(CheatStatPolicy.HasVerifiedFinal(type));
            Require(!CheatStatPolicy.HasVerifiedFinal(7));
            Require(!CheatStatPolicy.HasVerifiedFinal(8));
            Require(!CheatStatPolicy.HasVerifiedFinal(0));
            Require(!CheatStatPolicy.HasVerifiedFinal(999));
        });

        Test("batch edits only return values that actually changed", () =>
        {
            var ints = CheatBatch.Changed(new[]
            {
                ("a", 1, 1), ("b", 2, 3), ("c", 4, 4)
            });
            Require(ints.Count == 1 && ints[0].Key == "b" && ints[0].Value == 2);
            var floats = CheatBatch.Changed(new[]
            {
                ("x", 1.0f, 1.0f), ("y", 2.5f, 2.0f)
            });
            Require(floats.Count == 1 && floats[0].Key == "y" && Math.Abs(floats[0].Value - 2.5f) < 0.0001f);
            Require(CheatBatch.Changed(Array.Empty<(string, int, int)>()).Count == 0);
        });

        Test("batch edits preserve order and duplicate keys", () =>
        {
            var changed = CheatBatch.Changed(new[]
            {
                ("a", 1, 0), ("a", 2, 0), ("b", 3, 3)
            });
            Require(changed.Count == 2);
            Require(changed[0].Key == "a" && changed[0].Value == 1);
            Require(changed[1].Key == "a" && changed[1].Value == 2);
        });

        Test("batch edits treat null and equal text consistently", () =>
        {
#pragma warning disable CS8631 // This case deliberately exercises null text against the existing generic equality API.
            var changed = CheatBatch.Changed(new[]
            {
                ("a", (string?)null, "当前"),
                ("b", "相同", "相同"),
                ("c", "新值", (string?)null)
            });
#pragma warning restore CS8631
            Require(changed.Count == 2);
            Require(changed[0].Key == "a" && changed[0].Value is null);
            Require(changed[1].Key == "c" && changed[1].Value == "新值");
        });

        Test("character list keeps player first and removes duplicates", () =>
        {
            var entries = CheatCharacterList.Build(
                "主角",
                new[] { ("a", "狼渊"), ("b", "猫花来"), ("a", "重复") },
                new[] { "鹿柒", "狼渊", "", "  ", "阿守", "鹿柒" });
            Require(entries.Count == 5, entries.Count.ToString());
            Require(entries[0].Kind == CheatCharacterKind.Player && entries[0].Name == "主角" && entries[0].Key == "@player");
            Require(entries[1].Kind == CheatCharacterKind.Recruited && entries[1].Name == "狼渊" && entries[1].Key == "f:a");
            Require(entries[2].Kind == CheatCharacterKind.Recruited && entries[2].Name == "猫花来" && entries[2].Key == "f:b");
            Require(entries[3].Kind == CheatCharacterKind.Known && entries[3].Name == "鹿柒" && entries[3].Key == "k:鹿柒");
            Require(entries[4].Kind == CheatCharacterKind.Known && entries[4].Name == "阿守");
        });

        Test("character list handles an empty player name and player-name collision", () =>
        {
            var entries = CheatCharacterList.Build(
                "",
                new[] { ("p", "主角") },
                Array.Empty<string>());
            Require(entries[0].Name == "主角" && entries[0].Kind == CheatCharacterKind.Player);
            Require(entries[1].Name == "主角" && entries[1].Kind == CheatCharacterKind.Recruited);
        });

        Test("character list always exposes the player and ignores blank entries", () =>
        {
            var entries = CheatCharacterList.Build(
                "   ",
                new[] { ("", "无键"), ("  ", "空键"), ("ok", "   ") },
                new[] { "", "   ", "\t" });
            Require(entries.Count == 1, entries.Count.ToString());
            Require(entries[0].Kind == CheatCharacterKind.Player && entries[0].Key == CheatCharacterList.PlayerKey);
            Require(entries[0].Name == "主角");
        });

        Test("known friends are trimmed, deduplicated and keep source order", () =>
        {
            var entries = CheatCharacterList.Build(
                "主角",
                Array.Empty<(string Key, string Name)>(),
                new[] { " 鹿柒 ", "阿守", "鹿柒", " 阿守", "白露" });
            Require(entries.Count == 4, entries.Count.ToString());
            Require(entries[1].Name == "鹿柒" && entries[1].Key == "k:鹿柒");
            Require(entries[2].Name == "阿守" && entries[2].Key == "k:阿守");
            Require(entries[3].Name == "白露" && entries[3].Key == "k:白露");
        });

        Test("character permissions only expose valid edit surfaces", () =>
        {
            Require(CheatCharacterPolicy.CanEditStats(CheatCharacterKind.Player));
            Require(!CheatCharacterPolicy.CanEditFavor(CheatCharacterKind.Player));
            Require(CheatCharacterPolicy.CanEditStats(CheatCharacterKind.Recruited));
            Require(CheatCharacterPolicy.CanEditFavor(CheatCharacterKind.Recruited));
            Require(!CheatCharacterPolicy.CanEditStats(CheatCharacterKind.Known));
            Require(CheatCharacterPolicy.CanEditFavor(CheatCharacterKind.Known));
        });

        Test("character selection falls back when a save removes the selected friend", () =>
        {
            var keys = new[] { "@player", "f:a", "f:b" };
            Require(CheatSelection.Resolve(keys, "f:b", "@player") == "f:b");
            Require(CheatSelection.Resolve(keys, "f:gone", "@player") == "@player");
            Require(CheatSelection.Resolve(keys, null, "@player") == "@player");
            Require(CheatSelection.Resolve(Array.Empty<string>(), "f:b", "@player") == "@player");
        });

        Test("only attribute and talent points are writable", () =>
        {
            Require(CheatPointPolicy.CanWrite(CheatPointPool.Attribute));
            Require(CheatPointPolicy.CanWrite(CheatPointPool.Talent));
            Require(!CheatPointPolicy.CanWrite(CheatPointPool.Life));
            Require(!CheatPointPolicy.CanWrite((CheatPointPool)999));
            Require(CheatPointPolicy.Description(CheatPointPool.Life).Contains("只读"));
            Require(CheatPointPolicy.Description(CheatPointPool.Attribute).Contains("可用"));
            Require(CheatPointPolicy.Description(CheatPointPool.Talent).Contains("可用"));
            Require(CheatPointPolicy.Description((CheatPointPool)999) == "");
        });

        Test("pre-edit checkpoint reservation succeeds once and releases after failure", () =>
        {
            CheatSessionState.Reset();
            Require(CheatSessionState.TryBeginPreEditCheckpoint(true, true, out var token));
            Require(!CheatSessionState.TryBeginPreEditCheckpoint(true, true, out _));
            CheatSessionState.ReleaseFailedPreEditCheckpoint(token);
            Require(CheatSessionState.TryBeginPreEditCheckpoint(true, true, out _));
            CheatSessionState.Reset();
            Require(!CheatSessionState.TryBeginPreEditCheckpoint(false, true, out _));
            Require(!CheatSessionState.TryBeginPreEditCheckpoint(true, false, out _));
            Require(CheatSessionState.TryBeginPreEditCheckpoint(true, true, out _));
        });

        Test("successful pre-edit generation is retained and cleared with the session", () =>
        {
            CheatSessionState.Reset();
            Require(CheatSessionState.TryBeginPreEditCheckpoint(true, true, out var token));
            CheatSessionState.CompletePreEditCheckpoint(token, "generation-a");
            Require(CheatSessionState.ProtectedGeneration == "generation-a");
            Require(!CheatSessionState.TryBeginPreEditCheckpoint(true, true, out _));
            CheatSessionState.Reset();
            Require(CheatSessionState.ProtectedGeneration == null);
            Require(CheatSessionState.TryBeginPreEditCheckpoint(true, true, out _));
        });

        Test("panel reset restores the default cheat section and closes the panel", () =>
        {
            CommandPanelState.Reset();
            CommandPanelState.Open = true;
            CommandPanelState.SelectTab(CommandPanelTab.Cheats);
            CommandPanelState.SelectCheatSection(CheatSection.Points);
            CommandPanelState.SelectCheatSection(CheatSection.Items);
            Require(CommandPanelState.CheatSection == CheatSection.Items);
            CommandPanelState.Reset();
            Require(!CommandPanelState.Open);
            Require(CommandPanelState.Tab == CommandPanelTab.Status);
            Require(CommandPanelState.CheatSection == CheatSectionCatalog.Default);
        });

        Test("edit policy uses the most important blocking state first", () =>
        {
            string? Reason(bool faulted = false, bool busy = false, bool loading = false, bool saveReady = true,
                bool saving = false, bool battle = false, bool map = false, bool dialogue = false,
                bool canMove = true, bool sequence = false) =>
                CheatEditPolicy.BlockReason(faulted, busy, loading, saveReady, saving, battle, map, dialogue, canMove, sequence);
            Require(Reason(faulted: true, busy: true, battle: true)!.Contains("停用修改"));
            Require(Reason(busy: true, loading: true) == "正在存读档");
            Require(Reason(saveReady: false, battle: true) == "游戏尚未准备好");
            Require(Reason(saving: true, battle: true) == "原生存档正在写入");
            Require(Reason(battle: true, map: true) == "战斗或结算期间不可修改");
            Require(Reason(map: true, dialogue: true) == "地图正在切换或清理");
            Require(Reason(dialogue: true, canMove: false) == "正在对话");
            Require(Reason(canMove: false, sequence: true) == "玩家控制尚未恢复");
            Require(Reason() is null);
        });

        Test("completed map teleports remain editable", () =>
        {
            Require(!CheatEditPolicy.MapTransitionBlocked(
                initOver: true, teleporting: false, teleportUsed: true));
            Require(CheatEditPolicy.MapTransitionBlocked(
                initOver: false, teleporting: false, teleportUsed: false));
            Require(CheatEditPolicy.MapTransitionBlocked(
                initOver: true, teleporting: true, teleportUsed: false));
        });

        CheatSessionState.Reset();
        return (checks, failures);
    }
}
