using ExcelData;
using SafeDialogueTools.Core;

namespace SafeDialogueTools;

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

internal sealed record CheatPointsSnapshot(
    int AttributeTotal,
    int AttributeUsed,
    int AttributeAvailable,
    int LifeTotal,
    int LifeUsed,
    int LifeAvailable,
    int LifeDerivedAvailable,
    int TalentTotal,
    int TalentUsed,
    int TalentAvailable);

internal static class CheatEditor
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

    internal static void ResetPreEditCheckpoint() => CheatSessionState.Reset();

    internal static IReadOnlyList<CheatCharacterOption> Characters()
    {
        var manager = PlayerFriendDataManager._Instance;
        var data = manager?._FriendData;
        var friends = new Dictionary<string, FriendData>(StringComparer.Ordinal);
        var recruited = new List<(string Key, string Name)>();
        if (data?.AddFriends != null)
        {
            foreach (var pair in data.AddFriends)
            {
                var friend = pair.Value;
                if (friend == null) continue;
                var name = DisplayName(friend, pair.Key);
                var key = "f:" + pair.Key;
                friends[key] = friend;
                recruited.Add((pair.Key, name));
            }
        }

        var known = new List<string>();
        if (data?.AddAllFriends != null)
        {
            foreach (var raw in data.AddAllFriends)
            {
                var name = raw ?? "";
                if (!string.IsNullOrWhiteSpace(name)) known.Add(name);
            }
        }

        return CheatCharacterList.Build(
                "主角",
                recruited.OrderBy(item => item.Name, StringComparer.CurrentCulture),
                known.OrderBy(item => item, StringComparer.CurrentCulture))
            .Select(entry => new CheatCharacterOption(
                entry.Key,
                entry.Name,
                entry.Kind,
                entry.Kind == CheatCharacterKind.Recruited ? friends.GetValueOrDefault(entry.Key) : null))
            .ToArray();
    }

    internal static CheatCharacterOption Resolve(string? key)
    {
        var all = Characters();
        return all.FirstOrDefault(item => item.Key == key) ?? all[0];
    }

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

        var friend = option.Friend;
        if (friend == null) return EmptySnapshot(option);
        var favor = Favor(option, out var favorKey);
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

    internal static CheatPointsSnapshot Points()
    {
        var manager = PlayerDataManager._Instance;
        var player = manager?._PlayerData;
        if (manager == null || player == null) return new CheatPointsSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return new CheatPointsSnapshot(
            player.AddProSpot,
            player.UseProSpot,
            CheatMath.Available(player.AddProSpot, player.UseProSpot),
            player.AddLifeSpot,
            player.UseLifeSpot,
            CheatMath.Available(player.AddLifeSpot, player.UseLifeSpot),
            manager.GetLifeCanPoint(),
            player.TalentAdd,
            player.UseTalent,
            manager.GetPlaTeXingPoint());
    }

    internal static bool AddPlayerExp(int amount, out string message)
    {
        if (amount <= 0)
        {
            message = "请输入大于 0 的经验值";
            return false;
        }
        if (PlayerDataManager._Instance == null)
        {
            message = "游戏尚未准备好";
            return false;
        }
        PlayerDataManager._Instance.AddExp(amount);
        message = $"已添加经验 {amount}";
        return true;
    }

    internal static bool SetStat(CheatCharacterOption option, PlayerProType type, int value, out string message)
    {
        value = CheatMath.ClampStat(value);
        if (option.IsPlayer)
        {
            var player = PlayerDataManager._Instance?._PlayerData;
            if (player == null)
            {
                message = "游戏尚未准备好";
                return false;
            }
            WritePlayerBase(player, type, value);
            message = $"已将{PlayerTypeLabel(type)}基础值设为 {value}";
            return true;
        }
        if (option.Friend == null)
        {
            message = "该角色不可修改属性";
            return false;
        }
        WriteFriendBase(option.Friend, type, value);
        message = $"已将 {option.Name} 的{PlayerTypeLabel(type)}基础值设为 {value}";
        return true;
    }

    internal static bool SetMartial(CheatCharacterOption option, KFType type, int level, out string message)
    {
        level = CheatMath.ClampLevel(level);
        var item = ExcelDataManager.Instance?.GetKongfuExpItemByID(level);
        if (item == null)
        {
            message = $"武学等级 {level} 不在数据表中";
            return false;
        }
        if (option.IsPlayer)
        {
            var player = PlayerDataManager._Instance?._PlayerData;
            if (player?.KongFuTypeLv == null)
            {
                message = "游戏尚未准备好";
                return false;
            }
            SetDictionary(player.KongFuTypeLv, type, item.totalexp);
            message = $"已将{MartialLabel(type)}武学等级设为 {level}";
            return true;
        }
        var friend = option.Friend;
        if (friend?._KongFuTypeLv == null)
        {
            message = "该角色不可修改武学";
            return false;
        }
        SetDictionary(friend._KongFuTypeLv, type, item.totalexp);
        message = $"已将 {option.Name} 的{MartialLabel(type)}武学等级设为 {level}";
        return true;
    }

    internal static bool SetLife(CheatCharacterOption option, LifeType type, int level, out string message)
    {
        level = CheatMath.ClampLevel(level);
        if (option.IsPlayer)
        {
            var player = PlayerDataManager._Instance?._PlayerData;
            var item = ExcelDataManager.Instance?.GetYiExpItemByID(level);
            if (player?.LifeExp == null || item == null)
            {
                message = item == null ? $"生活等级 {level} 不在数据表中" : "游戏尚未准备好";
                return false;
            }
            SetDictionary(player.LifeExp, type, item.totalexp);
            message = $"已将{LifeLabel(type)}生活等级设为 {level}";
            return true;
        }
        var friend = option.Friend;
        if (friend?._LifeTypeLv == null)
        {
            message = "该角色不可修改生活技能";
            return false;
        }
        SetDictionary(friend._LifeTypeLv, type, level);
        message = $"已将 {option.Name} 的{LifeLabel(type)}生活等级设为 {level}";
        return true;
    }

    internal static bool AddFavor(CheatCharacterOption option, float delta, out string message)
    {
        if (!TryFavorKey(option, out var key))
        {
            message = "当前存档没有该角色的好感记录";
            return false;
        }
        var player = PlayerDataManager._Instance?._PlayerData;
        if (player?.FriendLoveNum == null)
        {
            message = "游戏尚未准备好";
            return false;
        }
        var value = CheatMath.AdjustFavor(player.FriendLoveNum[key], delta);
        player.FriendLoveNum[key] = value;
        message = $"已将 {option.Name} 的好感调整为 {value:0.#}";
        return true;
    }

    internal static bool AddPoints(CheatPointPool pool, int delta, out string message)
    {
        var player = PlayerDataManager._Instance?._PlayerData;
        if (player == null)
        {
            message = "游戏尚未准备好";
            return false;
        }
        switch (pool)
        {
            case CheatPointPool.Attribute:
                player.AddProSpot = CheatMath.AddPoints(player.AddProSpot, player.UseProSpot, delta);
                message = "属性点已调整";
                return true;
            case CheatPointPool.Talent:
                player.TalentAdd = CheatMath.AddPoints(player.TalentAdd, player.UseTalent, delta);
                message = "特性点已调整";
                return true;
            default:
                message = "生活点由生活技能等级换算，请在角色属性页调整生活技能等级";
                return false;
        }
    }

    internal static bool SetAvailablePoints(CheatPointPool pool, int available, out string message)
    {
        var player = PlayerDataManager._Instance?._PlayerData;
        if (player == null)
        {
            message = "游戏尚未准备好";
            return false;
        }
        switch (pool)
        {
            case CheatPointPool.Attribute:
                player.AddProSpot = CheatMath.SetAvailable(player.UseProSpot, available);
                message = $"属性点可用值已设为 {Math.Max(0, available)}";
                return true;
            case CheatPointPool.Talent:
                player.TalentAdd = CheatMath.SetAvailable(player.UseTalent, available);
                message = $"特性点可用值已设为 {Math.Max(0, available)}";
                return true;
            default:
                message = "生活点由生活技能等级换算，请在角色属性页调整生活技能等级";
                return false;
        }
    }

    internal static bool SetMovementSpeed(float multiplier, out string message)
    {
        var controller = SunYingManager._Inst?._MoveController;
        if (controller == null)
        {
            message = "当前场景没有可用的移动控制器";
            return false;
        }
        multiplier = Math.Clamp(multiplier, 0.1f, 10f);
        controller.speed = 4.5f * multiplier;
        message = $"移动速度已设为 {multiplier:0.##} 倍";
        return true;
    }

    internal static float MovementSpeed()
    {
        var controller = SunYingManager._Inst?._MoveController;
        return controller == null ? 0f : controller.speed / 4.5f;
    }

    internal static string? EnsurePreEditCheckpoint()
    {
        var canOperate = Plugin.UnsafeReason(false) == null;
        if (!CheatSessionState.TryBeginPreEditCheckpoint(Plugin.CheckpointsActive, canOperate)) return null;
        if (!Plugin.CheckpointsActive || !canOperate) return null;
        try
        {
            var generation = Plugin.Capture(CheckpointKind.Manual);
            try { Plugin.Store.SetNote(generation, "修改前自动检查点"); }
            catch (Exception error) { Plugin.Logger.LogWarning("修改前检查点备注失败：" + error.Message); }
            Plugin.Store.Select(generation);
            Plugin.RefreshPanelSelectedCheckpoint();
            Plugin.RefreshPanelArchive();
            return null;
        }
        catch (Exception error)
        {
            Plugin.Logger.LogError("修改前检查点创建失败：" + error);
            return "修改前检查点创建失败，本次修改未受检查点保护";
        }
    }

    internal static string? EditBlockReason()
    {
        var manager = SaveManager._Instance;
        var battle = BattleManager._Instance;
        var map = MapManager._Instance;
        var sequence = false;
        if (PixelCrushers.DialogueSystem.DialogueManager.hasInstance)
        {
            var view = PixelCrushers.DialogueSystem.DialogueManager.conversationView;
            sequence = view != null && (view.isPlayingResponseMenuSequence ||
                (view.sequencer != null && view.sequencer.isPlaying));
        }
        return CheatEditPolicy.BlockReason(
            Plugin.Faulted,
            Plugin.Busy,
            Plugin.Loading,
            manager != null && manager.IsLogin && manager._IsLoadOver && manager._IsCanGame && manager._Player != null,
            manager?.IsSaveIng ?? false,
            battle == null || battle._IsBattle || battle._IsStartBattle || battle._IsEnterBattle || battle._IsGameOver,
            map == null || !map._IsInitOver || map._ChuanSongIng || map._ChuangSongMap,
            Plugin.IsDialogue,
            manager?._PlayerCanMove ?? false,
            sequence);
    }

    private static string DisplayName(FriendData friend, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(friend._Name)) return friend._Name;
        if (!string.IsNullOrWhiteSpace(friend._AbName)) return friend._AbName;
        return fallback;
    }

    private static CheatCharacterSnapshot EmptySnapshot(CheatCharacterOption option) => new()
    {
        Option = option,
        BaseStats = new float[AttributeTypes.Length],
        FinalStats = new int[AttributeTypes.Length],
        MartialLevels = new int[MartialTypes.Length],
        LifeLevels = new int[LifeTypes.Length]
    };

    private static float ReadPlayerBase(PlayerData player, int type) => type switch
    {
        1 => player.Power,
        2 => player.Perception,
        3 => player.Agility,
        4 => player.PhysicalPower,
        5 => player.Channel,
        6 => player.BerathSkill,
        8 => player.WuXing,
        _ => 0f
    };

    private static float ReadFriendBase(FriendData friend, int type) => type switch
    {
        1 => friend._Power,
        2 => friend._Perception,
        3 => friend._Agility,
        4 => friend._PhysicalPower,
        5 => friend._Channel,
        6 => friend._BerathSkill,
        8 => friend._WuXing,
        _ => 0f
    };

    private static int ReadPlayerFinal(PlayerDataManager manager, int type) =>
        CheatStatPolicy.HasVerifiedFinal(type) ? manager.GetPro((PlayerProType)type) : -1;

    private static int ReadFriendFinal(FriendData friend, int type) =>
        CheatStatPolicy.HasVerifiedFinal(type) ? friend.GetPro((PlayerProType)type) : -1;

    private static int ReadPlayerMartial(PlayerData player, PlayerDataManager manager, KFType type)
    {
        var levels = player.KongFuTypeLv;
        return levels != null && levels.ContainsKey(type) ? manager.GetKongFuTypeLv(type) : 0;
    }

    private static int ReadPlayerLife(PlayerData player, PlayerDataManager manager, LifeType type)
    {
        var levels = player.LifeExp;
        return levels != null && levels.ContainsKey(type) ? manager.GetLifeTypeLV(type) : 0;
    }

    private static int ReadFriendMartial(FriendData friend, KFType type)
    {
        var levels = friend._KongFuTypeLv;
        return levels != null && levels.ContainsKey(type) ? friend.GetKongFuTypeLv(type) : 0;
    }

    private static int ReadFriendLife(FriendData friend, LifeType type)
    {
        var levels = friend._LifeTypeLv;
        return levels != null && levels.ContainsKey(type) ? friend.GetLifeLvbyType(type) : 0;
    }

    private static void WritePlayerBase(PlayerData player, PlayerProType type, int value)
    {
        switch ((int)type)
        {
            case 1: player.Power = value; break;
            case 2: player.Perception = value; break;
            case 3: player.Agility = value; break;
            case 4: player.PhysicalPower = value; break;
            case 5: player.Channel = value; break;
            case 6: player.BerathSkill = value; break;
            case 8: player.WuXing = value; break;
        }
    }

    private static void WriteFriendBase(FriendData friend, PlayerProType type, int value)
    {
        switch ((int)type)
        {
            case 1: friend._Power = value; break;
            case 2: friend._Perception = value; break;
            case 3: friend._Agility = value; break;
            case 4: friend._PhysicalPower = value; break;
            case 5: friend._Channel = value; break;
            case 6: friend._BerathSkill = value; break;
            case 8: friend._WuXing = value; break;
        }
    }

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

    private static bool TryFavorKey(CheatCharacterOption option, out string key)
    {
        key = "";
        var player = PlayerDataManager._Instance?._PlayerData;
        if (player?.FriendLoveNum == null) return false;
        foreach (var candidate in FavorCandidates(option))
        {
            if (string.IsNullOrWhiteSpace(candidate) || !player.FriendLoveNum.ContainsKey(candidate)) continue;
            key = candidate;
            return true;
        }
        return false;
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

    private static void SetDictionary<TKey, TValue>(
        Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key)) dictionary[key] = value;
        else dictionary.Add(key, value);
    }

    private static string PlayerTypeLabel(PlayerProType type) =>
        AttributeTypes.FirstOrDefault(item => item.Value == (int)type).Label ?? ((int)type).ToString();

    private static string MartialLabel(KFType type) =>
        MartialTypes.FirstOrDefault(item => item.Value == (int)type).Label ?? ((int)type).ToString();

    private static string LifeLabel(LifeType type) =>
        LifeTypes.FirstOrDefault(item => item.Value == (int)type).Label ?? ((int)type).ToString();
}
