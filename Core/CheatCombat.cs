namespace BeastSagaBetter.Core;

public readonly record struct CombatBattleFlags(bool Battle, bool Started, bool Entered, bool GameOver)
{
    public bool Active => Battle || Started || Entered;
    public bool Context => Active || GameOver;
}

public sealed class CombatMotionGate
{
    private string actor = "";
    private float x, y;
    private double stableSince;
    public bool Observe(string key, float nextX, float nextY, float speedSquared, double now)
    {
        var valid = key.Length > 0 && float.IsFinite(nextX) && float.IsFinite(nextY) && float.IsFinite(speedSquared) && double.IsFinite(now);
        if (!valid || key != actor || Math.Abs(nextX - x) > .001f || Math.Abs(nextY - y) > .001f || speedSquared > .0001f)
        { actor = valid ? key : ""; x = nextX; y = nextY; stableSince = now; return false; }
        return now - stableSince >= .15;
    }
}

public sealed record CombatConditions
{
    public bool Faulted { get; init; }
    public bool Busy { get; init; }
    public bool Ready { get; init; }
    public bool Saving { get; init; }
    public bool Battle { get; init; }
    public bool Supported { get; init; }
    public bool ObserversReady { get; init; }
    public bool ManualActor { get; init; }
    public bool BattleUi { get; init; }
    public bool Acting { get; init; }
    public bool Resolving { get; init; }
    public bool Settling { get; init; }
    public bool Transition { get; init; }
    public bool Dialogue { get; init; }
}

public readonly record struct CombatVitals(float Hp, float MaxHp, float Armor, float MaxArmor, float Gas, float MaxGas)
{
    public bool CanRestore => Hp > 0 && MaxHp > 0 && MaxArmor >= 0 && MaxGas >= 0 &&
        float.IsFinite(Hp) && float.IsFinite(MaxHp) && float.IsFinite(Armor) && float.IsFinite(MaxArmor) &&
        float.IsFinite(Gas) && float.IsFinite(MaxGas);
}

public static class CombatPolicy
{
    public static bool NeedsSnapshot(bool battle, bool open, bool pending, bool diagnostic) => battle && (open || pending || diagnostic);
    public static string? BlockReason(CombatConditions state)
    {
        if (state.Faulted) return "本次运行已停用修改，请重启游戏";
        if (state.Busy) return "正在存读档或执行战斗操作";
        if (!state.Ready) return "游戏尚未准备好";
        if (state.Saving) return "原生存档正在写入";
        if (!state.Battle) return "当前不在战斗中";
        if (state.Transition) return "地图正在切换或清理";
        if (state.Dialogue) return "对话或剧情演出正在执行";
        if (state.Settling) return "战斗正在结算";
        if (!state.ObserversReady) return "战斗流程观察器未就绪，已禁用战斗修改";
        if (!state.Supported) return "仅支持普通场景遭遇战；特殊目标、援军或对话战斗暂不可修改";
        if (state.Resolving) return "正在处理死亡或回合交接";
        if (state.Acting) return "正在移动、攻击或执行技能";
        if (!state.ManualActor || !state.BattleUi) return "请等待存活我方单位的手动操作回合";
        return null;
    }

    public static string? CheckpointReason(CheckpointSummary? checkpoint, DateTime? startedUtc)
    {
        if (startedUtc == null) return "无法确认本场开战时间，请在战前加载并重新进入战斗";
        if (checkpoint == null) return "请在战前安全场景按 F5 建立并选择检查点";
        if (checkpoint.Kind is not (CheckpointKind.Manual or CheckpointKind.PreDialogue) ||
            string.IsNullOrWhiteSpace(checkpoint.MapName) || checkpoint.PlayerLevel == null ||
            checkpoint.Round == null || checkpoint.Day == null || checkpoint.CapturedAt == default)
            return "所选检查点缺少有效元数据，请在战前重新建立";
        if (checkpoint.CapturedAt.ToUniversalTime() >= startedUtc.Value.ToUniversalTime())
            return "所选检查点不早于本场开战时间";
        return null;
    }

    public static bool BlockGameInput(bool open, bool battle, int closedFrame, int frame) =>
        battle && (open || closedFrame == frame);

    public static string RestoreReport(string name, CombatVitals before, CombatVitals after) =>
        $"{name}：气血 {before.Hp:0.#}→{after.Hp:0.#}/{after.MaxHp:0.#}，" +
        $"护甲 {before.Armor:0.#}→{after.Armor:0.#}/{after.MaxArmor:0.#}，" +
        $"真气 {before.Gas:0.#}→{after.Gas:0.#}/{after.MaxGas:0.#}" +
        (after.Hp + .01f < after.MaxHp ? "（气血未满，可能受滞息等状态影响）" : "");
}

public static class CombatSubmission
{
    public static bool TryRun(Func<bool> validateProtection, Func<bool> currentState, Action nativeWrite)
    {
        if (!currentState() || !validateProtection() || !currentState()) return false;
        nativeWrite();
        return true;
    }
}

public sealed record CombatStamp(long Battle, long Phase, string Actor, string Generation, string Enemies);
public enum CombatOperation { Restore, Defeat }
public sealed record CombatRequest(CombatOperation Operation, CombatStamp Stamp, string? UnitKey);

// Battle lifetime is deliberately independent of the panel lifetime. Closing the panel must
// never unlock an already dispatched, non-idempotent native death/reward operation.
public sealed class CombatSession
{
    private long battle;
    private CombatRequest? pending;
    private readonly HashSet<string> retained = new(StringComparer.Ordinal);
    public CombatStamp? Confirmation { get; private set; }
    public bool Busy { get; private set; }
    public bool Disabled { get; private set; }
    public bool DefeatSubmitted { get; private set; }
    public bool HasPending => pending != null;
    public IReadOnlyCollection<string> RetainedGenerations => retained;
    public void NewBattle(long epoch)
    {
        battle = epoch; pending = null; Confirmation = null;
        Busy = Disabled = DefeatSubmitted = false; retained.Clear();
    }
    public void CancelUi() { pending = null; Confirmation = null; }
    public void Observe(CombatStamp? current)
    {
        if (Confirmation != current) Confirmation = null;
        if (pending?.Stamp != current) pending = null;
    }
    private bool CanRequest(CombatStamp stamp) => stamp.Battle == battle && !Busy && !Disabled && pending == null &&
        !string.IsNullOrWhiteSpace(stamp.Generation);
    public bool QueueRestore(CombatStamp stamp, string? unitKey)
    {
        if (!CanRequest(stamp) || Confirmation != null) return false;
        pending = new(CombatOperation.Restore, stamp, unitKey); return true;
    }
    public bool PrepareDefeat(CombatStamp stamp)
    {
        if (!CanRequest(stamp) || DefeatSubmitted) return false;
        Confirmation = stamp; return true;
    }
    public bool ConfirmDefeat(CombatStamp current)
    {
        var expected = Confirmation; Confirmation = null;
        if (expected == null || expected != current || !CanRequest(current) || DefeatSubmitted) return false;
        pending = new(CombatOperation.Defeat, current, null); return true;
    }
    public bool TryCommit(CombatStamp current, Func<bool> validate, out CombatRequest? request)
    {
        request = null;
        var candidate = pending;
        if (candidate == null || candidate.Stamp != current || current.Battle != battle || Busy || Disabled)
        { CancelUi(); return false; }
        bool valid;
        try { valid = validate(); } catch { valid = false; }
        // A validation callback may close/reopen the UI and queue a replacement with the
        // same values. Only the exact request being validated may commit; do not clear a
        // newer request belonging to the replacement UI or battle.
        if (!ReferenceEquals(pending, candidate) || battle != current.Battle) return false;
        if (!valid) { CancelUi(); return false; }
        pending = null; Busy = true;
        if (candidate.Operation == CombatOperation.Defeat) DefeatSubmitted = true;
        retained.Add(current.Generation); request = candidate; return true;
    }
    public void Complete(bool uncertain) { Busy = false; Disabled |= uncertain; }
}
