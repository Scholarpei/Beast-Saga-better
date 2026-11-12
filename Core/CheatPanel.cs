namespace BeastSagaBetter.Core;

public enum CheatSection
{
    Character,
    Points,
    Items,
    Combat,
    World,
    Other
}

public readonly record struct CheatSectionInfo(CheatSection Section, string Label, bool Available, string? Badge = null);

public static class CheatSectionCatalog
{
    public static IReadOnlyList<CheatSectionInfo> All { get; } = new[]
    {
        new CheatSectionInfo(CheatSection.Character, "角色属性", true),
        new CheatSectionInfo(CheatSection.Points, "点数", true),
        new CheatSectionInfo(CheatSection.Items, "物品", true),
        new CheatSectionInfo(CheatSection.Combat, "战斗", false, "第二批"),
        new CheatSectionInfo(CheatSection.World, "世界", true),
        new CheatSectionInfo(CheatSection.Other, "其他", true)
    };

    public static CheatSection Default => CheatSection.Character;

    public static CheatSectionInfo Get(CheatSection section) =>
        All.First(item => item.Section == section);

    public static bool IsAvailable(CheatSection section) =>
        All.Any(item => item.Section == section && item.Available);
}

public enum CheatPointPool
{
    Attribute,
    Life,
    Talent
}

public static class CheatMath
{
    public static int Available(int total, int used)
    {
        var available = (long)total - Math.Max(0, used);
        return available <= 0 ? 0 : available >= int.MaxValue ? int.MaxValue : (int)available;
    }

    public static int AddPoints(int total, int used, int delta)
    {
        var lower = Math.Max(0, used);
        var next = (long)total + delta;
        return next <= lower ? lower : next >= int.MaxValue ? int.MaxValue : (int)next;
    }

    public static int SetAvailable(int used, int available)
    {
        var next = (long)Math.Max(0, used) + Math.Max(0, available);
        return next >= int.MaxValue ? int.MaxValue : (int)next;
    }

    public static float AdjustFavor(float current, float delta)
    {
        if (float.IsNaN(current) || float.IsInfinity(current)) current = 0f;
        current = Math.Max(0f, current);
        if (float.IsNaN(delta) || float.IsInfinity(delta)) return current;
        var next = (double)current + delta;
        return next <= 0d ? 0f : next >= float.MaxValue ? float.MaxValue : (float)next;
    }

    public static int ClampLevel(int value, int min = 1, int max = 9999)
    {
        if (min > max) (min, max) = (max, min);
        return Math.Clamp(value, min, max);
    }

    public static int ClampStat(int value) => Math.Clamp(value, 0, 9999);
}

public enum CheatLevelEditAction
{
    Skip,
    Reject,
    Apply
}

public static class CheatLevelEditPolicy
{
    public static CheatLevelEditAction Resolve(int draft, int current, out int value, int max = 9999)
    {
        value = current;
        if (draft < 0) return CheatLevelEditAction.Reject;
        if (draft == 0)
            return current > 0 ? CheatLevelEditAction.Reject : CheatLevelEditAction.Skip;
        value = CheatMath.ClampLevel(draft, 1, max);
        return CheatLevelEditAction.Apply;
    }
}

public static class CheatStatPolicy
{
    public static bool HasVerifiedFinal(int type) => type is >= 1 and <= 6;
}

public static class CheatPointPolicy
{
    public static bool CanWrite(CheatPointPool pool) =>
        pool is CheatPointPool.Attribute or CheatPointPool.Talent;

    public static string Description(CheatPointPool pool) => pool switch
    {
        CheatPointPool.Attribute => "可用 = 总数 - 已用",
        CheatPointPool.Life => "由生活技能等级换算，只读",
        CheatPointPool.Talent => "可用 = 总数 - 已用",
        _ => ""
    };
}

public static class CheatEditPolicy
{
    public static bool MapTransitionBlocked(bool initOver, bool teleporting, bool teleportUsed) =>
        !initOver || teleporting;

    public static string? BlockReason(
        bool faulted,
        bool busy,
        bool loading,
        bool saveReady,
        bool saving,
        bool battle,
        bool mapTransition,
        bool dialogue,
        bool playerCanMove,
        bool sequencePlaying)
    {
        if (faulted) return "上次操作异常，本次运行已停用修改";
        if (busy || loading) return "正在存读档";
        if (!saveReady) return "游戏尚未准备好";
        if (saving) return "原生存档正在写入";
        if (battle) return "战斗或结算期间不可修改";
        if (mapTransition) return "地图正在切换或清理";
        if (dialogue) return "正在对话";
        if (!playerCanMove) return "玩家控制尚未恢复";
        if (sequencePlaying) return "剧情演出正在执行";
        return null;
    }
}
