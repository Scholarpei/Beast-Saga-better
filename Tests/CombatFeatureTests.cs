using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

internal static class CombatFeatureTests
{
    internal static (int Checks, int Failures) Run()
    {
        var checks = 0;
        var failures = 0;
        void Test(string name, Action action)
        {
            checks++;
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
        }
        void Require(bool value) { if (!value) throw new Exception("Assertion failed"); }
        var ready = new CombatConditions { Ready = true, Battle = true, Supported = true,
            ObserversReady = true, ManualActor = true, BattleUi = true };
        var stamp = new CombatStamp(1, 3, "actor", "generation", "enemy:1,enemy:2");
        Test("ordinary native battle remains active when only started and entered flags are set", () =>
        {
            Require(new CombatBattleFlags(false, true, true, false).Active);
            Require(new CombatBattleFlags(false, false, true, false).Active);
            Require(!new CombatBattleFlags(false, false, false, true).Active);
            Require(new CombatBattleFlags(false, false, false, true).Context);
            Require(!new CombatBattleFlags(false, false, false, false).Context);
        });
        Test("combat readiness requires positive manual idle evidence", () =>
        {
            Require(CombatPolicy.BlockReason(ready) == null);
            foreach (var blocked in new[] { ready with { Ready = false }, ready with { Battle = false },
                ready with { Supported = false }, ready with { ObserversReady = false }, ready with { ManualActor = false },
                ready with { BattleUi = false }, ready with { Acting = true }, ready with { Resolving = true },
                ready with { Settling = true }, ready with { Dialogue = true }, ready with { Transition = true },
                ready with { Busy = true }, ready with { Saving = true }, ready with { Faulted = true } })
                Require(CombatPolicy.BlockReason(blocked) != null);
        });
        Test("combat motion requires an unchanged actor and a stable observed position", () =>
        {
            var motion = new CombatMotionGate();
            Require(!motion.Observe("a", 1, 2, 0, 0));
            Require(motion.Observe("a", 1, 2, 0, .2));
            Require(!motion.Observe("a", 2, 2, 0, .3));
            Require(!motion.Observe("a", 2, 2, 1, .6));
            Require(!motion.Observe("b", 2, 2, 0, .8));
            Require(!motion.Observe("b", float.NaN, 2, 0, 1));
        });
        Test("combat checkpoint excludes legacy future missing metadata and unknown battle start", () =>
        {
            var started = DateTime.UtcNow;
            var summary = new CheckpointSummary("generation", CheckpointKind.Manual, started.AddSeconds(-1),
                "地图", 10, 1, 2, false, null, null, true);
            Require(CombatPolicy.CheckpointReason(summary, started) == null);
            Require(CombatPolicy.CheckpointReason(summary, null) != null);
            Require(CombatPolicy.CheckpointReason(null, started) != null);
            Require(CombatPolicy.CheckpointReason(summary with { Kind = CheckpointKind.Legacy }, started) != null);
            Require(CombatPolicy.CheckpointReason(summary with { CapturedAt = started }, started) != null);
            Require(CombatPolicy.CheckpointReason(summary with { MapName = null }, started) != null);
        });
        Test("combat restore never commits after failed checkpoint validation", () =>
        {
            var session = new CombatSession(); session.NewBattle(1);
            Require(session.QueueRestore(stamp, "unit"));
            Require(!session.TryCommit(stamp, () => false, out _));
            Require(!session.Busy && !session.DefeatSubmitted && session.RetainedGenerations.Count == 0);
            Require(session.QueueRestore(stamp, "unit"));
            Require(session.TryCommit(stamp, () => true, out var request) && request?.UnitKey == "unit");
            session.Complete(false);
        });
        Test("combat confirmation binds battle phase actor checkpoint and enemy identities", () =>
        {
            foreach (var changed in new[] { stamp with { Battle = 2 }, stamp with { Phase = 4 },
                stamp with { Actor = "other" }, stamp with { Generation = "other" }, stamp with { Enemies = "enemy:3" } })
            {
                var session = new CombatSession(); session.NewBattle(1);
                Require(session.PrepareDefeat(stamp));
                Require(!session.ConfirmDefeat(changed));
                Require(session.Confirmation == null && !session.Busy);
            }
        });
        Test("combat defeat cannot repeat after close reopen completion or timeout", () =>
        {
            var session = new CombatSession(); session.NewBattle(1);
            Require(session.PrepareDefeat(stamp) && session.ConfirmDefeat(stamp));
            Require(session.TryCommit(stamp, () => true, out _));
            Require(!session.QueueRestore(stamp, null) && !session.PrepareDefeat(stamp));
            session.CancelUi(); session.Complete(false);
            Require(session.DefeatSubmitted && !session.PrepareDefeat(stamp));
            Require(session.RetainedGenerations.Contains(stamp.Generation));
            session.NewBattle(2);
            Require(session.PrepareDefeat(stamp with { Battle = 2 }));
        });
        Test("combat uncertain completion disables this battle without leaking to the next", () =>
        {
            var session = new CombatSession(); session.NewBattle(1);
            Require(session.QueueRestore(stamp, null) && session.TryCommit(stamp, () => true, out _));
            session.Complete(true); session.CancelUi();
            Require(session.Disabled && !session.QueueRestore(stamp, null));
            session.NewBattle(2); Require(!session.Disabled && !session.Busy);
        });
        Test("combat callback invalidation cannot authorize stale commit", () =>
        {
            var session = new CombatSession(); session.NewBattle(1); session.QueueRestore(stamp, null);
            Require(!session.TryCommit(stamp, () => { session.NewBattle(2); return true; }, out _));
            Require(!session.Busy && session.RetainedGenerations.Count == 0);
        });
        Test("combat canceled request cannot commit a replacement with identical values", () =>
        {
            var session = new CombatSession(); session.NewBattle(1); session.QueueRestore(stamp, "unit");
            Require(!session.TryCommit(stamp, () =>
            {
                session.CancelUi(); session.QueueRestore(stamp, "unit"); return true;
            }, out _));
            Require(!session.Busy && session.HasPending);
            Require(session.TryCommit(stamp, () => true, out _));
        });
        Test("combat phase invalidation cancels queued operations and confirmation", () =>
        {
            var session = new CombatSession(); session.NewBattle(1);
            session.PrepareDefeat(stamp); session.Observe(stamp with { Phase = 4 });
            Require(session.Confirmation == null);
            session.QueueRestore(stamp, null); session.CancelUi();
            Require(!session.TryCommit(stamp, () => true, out _));
        });
        Test("combat input guard consumes the closing frame and releases afterward", () =>
        {
            Require(CombatPolicy.BlockGameInput(true, true, -1, 10));
            Require(CombatPolicy.BlockGameInput(false, true, 10, 10));
            Require(!CombatPolicy.BlockGameInput(false, true, 10, 11));
            Require(!CombatPolicy.BlockGameInput(true, false, -1, 10));
        });
        Test("combat restoration reports actual partial values and excludes dead units", () =>
        {
            Require(!new CombatVitals(0, 100, 0, 50, 0, 30).CanRestore);
            Require(!new CombatVitals(float.NaN, 100, 0, 50, 0, 30).CanRestore);
            var result = CombatPolicy.RestoreReport("我方", new(10, 100, 0, 50, 0, 30), new(10, 100, 50, 50, 30, 30));
            Require(result.Contains("气血未满") && result.Contains("护甲") && result.Contains("真气"));
        });
        Test("combat closed panel skips detailed unit snapshots until requested", () =>
        {
            Require(!CombatPolicy.NeedsSnapshot(true, false, false, false));
            Require(CombatPolicy.NeedsSnapshot(true, true, false, false));
            Require(CombatPolicy.NeedsSnapshot(true, false, true, false));
            Require(CombatPolicy.NeedsSnapshot(true, false, false, true));
            Require(!CombatPolicy.NeedsSnapshot(false, true, true, true));
        });
        Test("combat batch validates every native write and stops after checkpoint corruption", () =>
        {
            var valid = true; var writes = 0; var checksMade = 0;
            bool Validate() { checksMade++; return valid; }
            Require(CombatSubmission.TryRun(Validate, () => true, () => { writes++; valid = false; }));
            Require(!CombatSubmission.TryRun(Validate, () => true, () => writes++));
            Require(writes == 1 && checksMade == 2);
            var phaseValid = true;
            Require(!CombatSubmission.TryRun(() => { phaseValid = false; return true; }, () => phaseValid, () => writes++));
            Require(writes == 1);
        });
        Test("combat category is enabled and battle disables save/load affordances", () =>
        {
            Require(CheatSectionCatalog.IsAvailable(CheatSection.Combat));
            var state = new CommandPanelState.State(true, false, false, false, false, true, true,
                null, "generation", "战斗", Combat: true);
            Require(!state.CanSave && !state.CanLoad);
        });
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (2560, 1440), (3840, 2160) })
            Test($"combat measured text and confirmation remain scrollable at {width}x{height}", () =>
            {
                var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(width, height)).Scale;
                var page = CombatPanelGeometry.Create((width - 28 * scale) / scale - 206, height / scale - 220,
                    8, 84, 120, 150, 180);
                Require(page.Result.Y > page.Defeat.Bottom && page.ContentHeight > page.ViewHeight);
                Require(page.Confirmation.HasValue && page.Result.Y > page.Confirmation.Value.Bottom);
                Require(page.RestoreSelected.Right < page.RestoreAll.X && page.RestoreAll.Right <= page.Width);
                Require(page.Units.All(row => row.IsFinitePositive && row.Right <= page.Width));
                Require(page.Units[0].Y > page.Checkpoint.Bottom);
                Require(page.Units[^1].Bottom < page.RestoreSelected.Y);
            });
        return (checks, failures);
    }
}
