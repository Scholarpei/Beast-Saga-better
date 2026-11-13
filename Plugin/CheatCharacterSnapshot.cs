using ExcelData;
using BeastSagaBetter.Core;

namespace BeastSagaBetter;

internal sealed record CheatCharacterOption(string Key, string Name, CheatCharacterKind Kind, FriendData? Friend)
{
    public bool IsPlayer => Kind == CheatCharacterKind.Player;

    public bool CanEditStats => Kind != CheatCharacterKind.Known;
}

internal sealed class CheatCharacterSnapshot
{
    public CheatCharacterOption Option { get; init; } = null!;
    public int Level { get; init; }
    public float Exp { get; init; }
    public float[] BaseStats { get; init; } = Array.Empty<float>();
    public int[] FinalStats { get; init; } = Array.Empty<int>();
    public int[] MartialLevels { get; init; } = Array.Empty<int>();
    public int[] LifeLevels { get; init; } = Array.Empty<int>();
    public float Favor { get; init; }
    public bool HasFavor { get; init; }
    public string? FavorKey { get; init; }
}

internal static partial class CheatEditor
{
    internal static readonly (int Value, string Label)[] AttributeTypes =
    {
        (1, "力道"), (2, "感知"), (3, "灵巧"), (4, "体魄"),
        (5, "经脉"), (6, "速度"), (8, "悟性")
    };

    internal static readonly (int Value, string Label)[] MartialTypes =
    {
        (1, "内"), (2, "轻"), (3, "绝"), (4, "刀"), (5, "剑"), (6, "拳"),
        (7, "棍"), (8, "腿"), (9, "鞭"), (10, "掌"), (11, "指"), (12, "音"), (13, "破")
    };

    internal static readonly (int Value, string Label)[] LifeTypes =
    {
        (0, "铁匠"), (1, "巧艺"), (2, "织锦"), (3, "厨艺"), (4, "医术"),
        (5, "钓鱼"), (6, "商业"), (7, "说服"), (8, "开锁")
    };

    internal static CheatCharacterSnapshot Snapshot(CheatCharacterOption option)
    {
        if (option.IsPlayer)
        {
            var manager = PlayerDataManager._Instance;
            var player = manager?._PlayerData;
            if (manager == null || player == null) return EmptySnapshot(option);
            return new CheatCharacterSnapshot
            {
                Option = option,
                Level = player.Lv,
                Exp = player.Exp,
                BaseStats = AttributeTypes.Select(type => ReadPlayerBase(player, type.Value)).ToArray(),
                FinalStats = AttributeTypes.Select(type => ReadPlayerFinal(manager, type.Value)).ToArray(),
                MartialLevels = MartialTypes.Select(type => ReadPlayerMartial(player, manager, (KFType)type.Value)).ToArray(),
                LifeLevels = LifeTypes.Select(type => ReadPlayerLife(player, manager, (LifeType)type.Value)).ToArray()
            };
        }

        var favor = Favor(option, out var favorKey);
        var friend = option.Friend;
        if (friend == null) return EmptySnapshot(option, favor, favorKey);
        return new CheatCharacterSnapshot
        {
            Option = option,
            Level = friend._Lv,
            BaseStats = AttributeTypes.Select(type => ReadFriendBase(friend, type.Value)).ToArray(),
            FinalStats = AttributeTypes.Select(type => ReadFriendFinal(friend, type.Value)).ToArray(),
            MartialLevels = MartialTypes.Select(type => ReadFriendMartial(friend, (KFType)type.Value)).ToArray(),
            LifeLevels = LifeTypes.Select(type => ReadFriendLife(friend, (LifeType)type.Value)).ToArray(),
            Favor = favor,
            HasFavor = favorKey != null,
            FavorKey = favorKey
        };
    }

    private static CheatCharacterSnapshot EmptySnapshot(CheatCharacterOption option, float favor = 0, string? favorKey = null) => new()
    {
        Option = option,
        BaseStats = new float[AttributeTypes.Length],
        FinalStats = new int[AttributeTypes.Length],
        MartialLevels = new int[MartialTypes.Length],
        LifeLevels = new int[LifeTypes.Length],
        Favor = favor,
        HasFavor = favorKey != null,
        FavorKey = favorKey
    };

    private static float Favor(CheatCharacterOption option, out string? key)
    {
        key = null;
        var player = PlayerDataManager._Instance?._PlayerData;
        if (player?.FriendLoveNum == null) return 0f;
        foreach (var candidate in FavorCandidates(option))
        {
            if (string.IsNullOrWhiteSpace(candidate) || !player.FriendLoveNum.ContainsKey(candidate)) continue;
            key = candidate;
            return player.FriendLoveNum[candidate];
        }
        return 0f;
    }

    private static IEnumerable<string> FavorCandidates(CheatCharacterOption option)
    {
        if (option.Kind == CheatCharacterKind.Recruited && option.Friend != null)
        {
            if (!string.IsNullOrWhiteSpace(option.Friend._Name)) yield return option.Friend._Name;
            if (!string.IsNullOrWhiteSpace(option.Friend._AbName)) yield return option.Friend._AbName;
        }
        else if (option.Kind == CheatCharacterKind.Known)
        {
            yield return option.Name;
        }
    }

}
