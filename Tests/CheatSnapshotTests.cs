using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

internal static class CheatSnapshotTests
{
    public static (int Checks, int Failures) Run()
    {
        var checks = 0;
        var failures = 0;
        void Test(string name, Action run)
        {
            checks++;
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine($"FAIL {name}: {error.Message}"); }
        }
        void Require(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        Test("known character snapshots expose existing favor without recruited data", () =>
        {
            IndicatorHarness.ResetIndicatorState();
            PlayerDataManager._Instance._PlayerData.FriendLoveNum["已认识角色"] = 23.5f;
            var option = new CheatCharacterOption("k:已认识角色", "已认识角色", CheatCharacterKind.Known, null);
            var snapshot = CheatEditor.Snapshot(option);
            Require(snapshot.HasFavor && snapshot.FavorKey == "已认识角色" && snapshot.Favor == 23.5f,
                "known character favor must be read even when Friend is null");
            Require(!snapshot.Option.CanEditStats && snapshot.Level == 0 && snapshot.BaseStats.All(value => value == 0),
                "favor availability must not grant recruited stats");
        });
        Test("missing known favor stays unavailable and creates no record", () =>
        {
            IndicatorHarness.ResetIndicatorState();
            var snapshot = CheatEditor.Snapshot(new("k:无记录", "无记录", CheatCharacterKind.Known, null));
            Require(!snapshot.HasFavor && snapshot.FavorKey == null, "missing records must remain unavailable");
            Require(PlayerDataManager._Instance._PlayerData.FriendLoveNum.Count == 0, "snapshot must not create favor records");
        });
        Test("recruited snapshots keep alias favor and populated stats", () =>
        {
            IndicatorHarness.ResetIndicatorState();
            PlayerDataManager._Instance._PlayerData.FriendLoveNum["角色别名"] = 7.25f;
            var friend = new FriendData { _Lv = 12, _Name = "角色本名", _AbName = "角色别名" };
            var snapshot = CheatEditor.Snapshot(new("f:角色", "角色本名", CheatCharacterKind.Recruited, friend));
            Require(snapshot.HasFavor && snapshot.FavorKey == "角色别名" && snapshot.Favor == 7.25f,
                "recruited alias lookup must stay intact");
            Require(snapshot.Level == 12 && snapshot.BaseStats.Any(value => value != 0), "recruited stats must remain populated");
        });
        Test("player snapshot does not expose another character favor", () =>
        {
            IndicatorHarness.ResetIndicatorState();
            PlayerDataManager._Instance._PlayerData.FriendLoveNum["主角"] = 99;
            var snapshot = CheatEditor.Snapshot(new(CheatCharacterList.PlayerKey, "主角", CheatCharacterKind.Player, null));
            Require(!snapshot.HasFavor && snapshot.FavorKey == null && snapshot.Level == 1,
                "player snapshot must keep the player path");
        });
        IndicatorHarness.ResetIndicatorState();
        return (checks, failures);
    }
}
