using BeastSagaBetter.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeastSagaBetter;

internal sealed record CombatUnitSnapshot(string Key, int Id, string Name, bool PlayerControlled, CombatVitals Vitals);

internal static class CombatEditor
{
    private static readonly CombatSession session = new();
    private static readonly CombatMotionGate motion = new();
    private static IntPtr managerPointer, playerPointer, dataPointer;
    private static int scene, closedFrame = -1;
    private static bool observedOutside, wasBattle, wasOpen, lastReady;
    private static long epoch, phase, lastFlowVersion;
    private static string lastActor = "";
    private static string battleId = "";
    private static DateTime? startedUtc;
    private static float operationStarted;
    private static bool nativeFinished;
    private static bool stateFailureLogged;
    private static IntPtr nativeIterator;
    private static CombatStamp? stamp;
    private static string? cachedGeneration;
    private static CheckpointSummary? cachedCheckpoint;
    private static float checkpointReadAt = -1;
    internal static bool InBattle { get; private set; }
    internal static bool BattleContext { get; private set; }
    internal static bool BlockGameInput => CombatPolicy.BlockGameInput(CommandPanelState.Open, BattleContext, closedFrame, Time.frameCount);
    internal static string? Block { get; private set; } = "等待游戏就绪";
    internal static string? PhaseBlock { get; private set; } = "等待游戏就绪";
    internal static string Result { get; private set; } = "恢复不复活、不清异常；击败处理当前敌方，胜负由游戏判定。";
    internal static string RequestError { get; private set; } = "";
    internal static string CheckpointText { get; private set; } = "请在战前按 F5 建立并选择检查点";
    internal static IReadOnlyList<CombatUnitSnapshot> Team { get; private set; } = Array.Empty<CombatUnitSnapshot>();
    internal static IReadOnlyList<CombatUnitSnapshot> Enemies { get; private set; } = Array.Empty<CombatUnitSnapshot>();
    internal static CombatStamp? Confirmation => session.Confirmation;
    internal static bool Busy => session.Busy || session.HasPending;
    internal static bool DefeatSubmitted => session.DefeatSubmitted;
    internal static IEnumerable<string> RetainedGenerations => session.RetainedGenerations;
    internal static bool CanOpenInBattle => InBattle && PhaseBlock == null;

    internal static void PanelClosed()
    {
        if (BattleContext) closedFrame = Time.frameCount;
        session.CancelUi();
    }
    internal static void Invalidate(string reason)
    {
        session.CancelUi(); stamp = null; PhaseBlock = Block = reason;
        Team = Enemies = Array.Empty<CombatUnitSnapshot>();
    }

    internal static void Tick()
    {
        try { TickCore(); }
        catch (Exception error)
        {
            session.Complete(true); Invalidate("战斗状态读取异常，本场修改已停用");
            Result = Block!;
            if (!stateFailureLogged) { stateFailureLogged = true; Plugin.Logger.LogError(Result + "：" + error); }
        }
    }

    private static void TickCore()
    {
        var manager = BattleManager._Instance;
        var save = SaveManager._Instance;
        var nextScene = SceneManager.GetActiveScene().handle;
        var nextManager = manager?.Pointer ?? IntPtr.Zero;
        var nextPlayer = save?._Player?.Pointer ?? IntPtr.Zero;
        var nextData = PlayerDataManager._Instance?._PlayerData?.Pointer ?? IntPtr.Zero;
        var flags = manager == null ? default : new CombatBattleFlags(manager._IsBattle, manager._IsStartBattle, manager._IsEnterBattle, manager._IsGameOver);
        var active = flags.Active;
        var nextBattleId = manager?._BattleTableId ?? "";
        var changed = nextManager != managerPointer || nextPlayer != playerPointer || nextData != dataPointer || nextScene != scene ||
            active && wasBattle && nextBattleId != battleId;
        if (changed || active != wasBattle)
        {
            var knewStart = !changed && observedOutside && active && !wasBattle;
            session.NewBattle(++epoch); phase++; lastReady = false; lastActor = "";
            stateFailureLogged = false;
            startedUtc = knewStart ? DateTime.UtcNow : null;
            nativeIterator = IntPtr.Zero; nativeFinished = false;
            CombatFlowObserver.Reset();
            cachedCheckpoint = null; cachedGeneration = null; checkpointReadAt = -1;
            if (changed && CommandPanelState.Open)
            { PanelClosed(); CommandPanelState.Close(); ItemEditor.Reset("场景或玩家变化"); CheatEditor.ResetPreEditCheckpoint(); }
            if (active) Result = "恢复不复活、不清异常；击败处理当前敌方，胜负由游戏判定。";
        }
        managerPointer = nextManager; playerPointer = nextPlayer; dataPointer = nextData; scene = nextScene;
        battleId = nextBattleId;
        InBattle = wasBattle = active;
        BattleContext = manager == null || flags.Context;
        if (!active && save?.IsLogin == true && save._IsLoadOver && save._Player != null) observedOutside = true;
        if (Plugin.Loading || Plugin.Faulted)
        {
            if (CommandPanelState.Open) { PanelClosed(); CommandPanelState.Close(); }
            Invalidate("正在读档或本次运行已停用修改"); return;
        }
        if (wasOpen && !CommandPanelState.Open) PanelClosed();
        wasOpen = CommandPanelState.Open;
        if (session.Busy)
        {
            if (Time.realtimeSinceStartup - operationStarted >= 60)
            { session.Complete(true); Result = "原生流程等待超过 60 秒；可能已产生奖励或状态变化，本场不再修改，请在安全状态恢复检查点。"; }
            else if (nativeFinished && !CombatFlowObserver.Acting)
            { session.Complete(false); Result = "原生死亡流程已完成，胜负与奖励由游戏处理。"; }
        }
        ReadState(manager, save);
        session.Observe(stamp);
        if (!CommandPanelState.Open) session.CancelUi();
        if (session.HasPending && stamp != null) Execute();
    }

    private static void ReadState(BattleManager? manager, SaveManager? save, bool forceSnapshot = false, bool ignoreOwnOperation = false)
    {
        Team = Enemies = Array.Empty<CombatUnitSnapshot>(); stamp = null;
        if (manager == null) { PhaseBlock = Block = "战斗管理器不可用"; return; }
        var detailed = CombatPolicy.NeedsSnapshot(InBattle, CommandPanelState.Open, session.HasPending, forceSnapshot);
        if (detailed) { Team = ReadUnits(manager.TeamDic); Enemies = ReadUnits(manager.EnemyDic); }
        var actor = manager._CurrentBattleUtil;
        var actorUnit = actor == null ? null : actor.GetComponent<BattleUnit>();
        var actorData = actor == null ? null : actor.GetComponent<BattleUnitData>();
        var actorKey = actorData == null ? "" : Key(actorData);
        var actorPosition = actor == null ? Vector3.zero : actor.transform.position;
        var body = actor == null ? null : actor.GetComponent<Rigidbody2D>();
        var controller = actor == null ? null : actor.GetComponent<MoveController>();
        var speedSquared = Math.Max(body?.velocity.sqrMagnitude ?? 0, Math.Max(controller?.movement.sqrMagnitude ?? 0, controller?.update_move.sqrMagnitude ?? 0));
        var stationary = motion.Observe(actorKey, actorPosition.x, actorPosition.y, speedSquared, Time.realtimeSinceStartup);
        var map = MapManager._Instance;
        var view = PixelCrushers.DialogueSystem.DialogueManager.hasInstance
            ? PixelCrushers.DialogueSystem.DialogueManager.conversationView : null;
        var sequence = view != null && (view.isPlayingResponseMenuSequence || view.sequencer?.isPlaying == true);
        var manual = actor != null && actorUnit != null && actorData != null && actorData._Hp > 0 &&
            actorUnit._PlayerControRole && !actorUnit._IsAuto && manager.TeamDic != null &&
            manager.TeamDic.ContainsKey(actorData._Id) && manager.TeamDic[actorData._Id] == actor &&
            !manager._IsAutoAI && manager._AutoSetNum <= 0 && manager._IsStartBattle && manager._IsEnterBattle;
        var state = new CombatConditions
        {
            Faulted = Plugin.Faulted,
            Busy = Plugin.Busy || Plugin.Loading || !ignoreOwnOperation && session.Busy,
            Ready = save != null && save.IsLogin && save._IsLoadOver && save._IsCanGame && save._Player != null,
            Saving = save?.IsSaveIng ?? true, Battle = InBattle,
            Supported = Supported(manager, detailed), ObserversReady = CombatFlowObserver.Ready,
            ManualActor = manual, BattleUi = LJG.Framework.Main.m_UI?.GetOpenedUI<BattleUILogicResident>() != null,
            // _IsBattleMove is set by MoveCanRange(isMove): it describes movement selection,
            // not locomotion. Native PlayerJump requires it true on a normal manual turn.
            Acting = manager._IsBattleTime || !stationary || actorUnit?._PlayerAttackIng == true || CombatFlowObserver.Acting,
            Resolving = CombatFlowObserver.Resolving, Settling = manager._IsGameOver,
            Transition = CheatEditPolicy.MapTransitionBlocked(map?._IsInitOver ?? false, map?._ChuanSongIng ?? false, map?._ChuangSongMap ?? false),
            Dialogue = Plugin.IsDialogue || sequence
        };
        PhaseBlock = session.Disabled && !Plugin.Faulted
            ? "本场战斗修改已停用；可能已部分生效，请在安全状态恢复检查点"
            : CombatPolicy.BlockReason(state);
        var ready = PhaseBlock == null;
        if (ready != lastReady || actorKey != lastActor || lastFlowVersion != CombatFlowObserver.Version) phase++;
        lastReady = ready; lastActor = actorKey; lastFlowVersion = CombatFlowObserver.Version;
        if (InBattle && Plugin.CheckpointsActive && (CommandPanelState.Open || session.HasPending) &&
            (checkpointReadAt < 0 || Time.realtimeSinceStartup - checkpointReadAt >= .5f))
        {
            cachedGeneration = Plugin.Store.Selected(verifyPayload: false);
            cachedCheckpoint = cachedGeneration == null ? null : Plugin.Store.List(verifyPayloads: false)
                .FirstOrDefault(item => item.Generation == cachedGeneration);
            checkpointReadAt = Time.realtimeSinceStartup;
        }
        var selected = cachedGeneration;
        var summary = cachedCheckpoint;
        // No checkpoint IO while the panel is closed; payload validation only on submission.
        var checkpointBlock = !Plugin.CheckpointsActive ? "需先启用并授权检查点" : CombatPolicy.CheckpointReason(summary, startedUtc);
        CheckpointText = summary == null ? checkpointBlock ?? "未选择检查点" :
            $"{summary.CapturedAt.ToLocalTime():yyyy/MM/dd HH:mm:ss} · {summary.MapName} · Lv.{summary.PlayerLevel} · 第{summary.Round}周目 第{summary.Day}天（执行时完整校验）";
        Block = PhaseBlock ?? checkpointBlock;
        if (Block == null && selected != null)
            stamp = new(epoch, phase, actorKey, selected, string.Join(",", Enemies.Select(unit => unit.Key).OrderBy(key => key, StringComparer.Ordinal)));
    }

    private static bool Supported(BattleManager manager, bool detailed) => (int)manager._BattleType == 0 && !manager._IsLvWin &&
        // _DSTime/_DSTime01 are ordinary elapsed timers (OnBattleTime increments +0xe8),
        // not evidence of a special countdown/result contract.
        (int)manager._DsBattleResultType == 0 && manager._DSTimeChiXu == 0 &&
        !manager._IsSelectBattle && manager.TeamDic != null && manager.EnemyDic != null &&
        (manager._BattleEnemyLater?.Count ?? 0) == 0 && (manager._BattleEnemyClear?.Count ?? 0) == 0 &&
        (manager._BattleFriendAILater?.Count ?? 0) == 0 && (manager._BattleFriendAIClear?.Count ?? 0) == 0 &&
        string.IsNullOrEmpty(manager._CallBackMessageSuccess) && string.IsNullOrEmpty(manager._CallBackMessageFail) &&
        string.IsNullOrEmpty(manager._CallBackMessageCheTui) && string.IsNullOrEmpty(manager.ConversationIdSuccess) &&
        string.IsNullOrEmpty(manager.ConversationIdFail) && string.IsNullOrEmpty(manager.ConversationIdCheTui) &&
        (manager.CallBackMessage?.Count ?? 0) == 0 && (!detailed || TeamConsistent(manager));

    private static bool TeamConsistent(BattleManager manager) => manager.TeamDic.Count == Team.Count && manager.EnemyDic.Count == Enemies.Count;
    private static string Key(BattleUnitData data) => $"{epoch}:{data._Id}:{data.Pointer.ToInt64():X}";
    private static CombatVitals Vitals(BattleUnitData data) => new(data._Hp, data._MaxHp, data._Armor, data._ArmorMaxUI, data._Gas, data._GasMaxUI);
    private static IReadOnlyList<CombatUnitSnapshot> ReadUnits(Il2CppSystem.Collections.Generic.Dictionary<int, GameObject>? units)
    {
        var rows = new List<CombatUnitSnapshot>();
        if (units == null) return rows;
        foreach (var pair in units)
        {
            var obj = pair.Value;
            if (obj == null || !obj.activeInHierarchy) continue;
            var data = obj.GetComponent<BattleUnitData>(); var unit = obj.GetComponent<BattleUnit>();
            if (data == null || unit == null || data._Id != pair.Key) continue;
            rows.Add(new(Key(data), data._Id, data._Name ?? "未知单位", unit._PlayerControRole, Vitals(data)));
        }
        return rows.OrderBy(unit => unit.Id).ToArray();
    }

    internal static bool RequestRestore(string? key)
    {
        if (Block != null || stamp == null || !CommandPanelState.Open) return Reject(Block ?? "请先打开战斗面板");
        if (key != null && !Team.Any(unit => unit.Key == key && unit.Vitals.CanRestore)) return Reject("选中单位已失效或不可恢复");
        var ok = session.QueueRestore(stamp, key);
        if (ok) Result = "恢复请求已排队，执行前将重新校验检查点与单位状态。";
        else Reject("操作正在执行或等待确认");
        return ok;
    }
    internal static bool PrepareDefeat()
    {
        if (Block != null || stamp == null || !CommandPanelState.Open || Enemies.Count == 0) return Reject(Block ?? "没有有效敌方目标");
        var ok = session.PrepareDefeat(stamp);
        if (ok) Result = "请核对当前敌方与战前检查点后确认。";
        else Reject("本场已提交击败，或另一操作尚未结束");
        return ok;
    }
    internal static bool ConfirmDefeat()
    {
        if (Block != null || stamp == null || !CommandPanelState.Open) { session.CancelUi(); return Reject(Block ?? "确认已失效"); }
        var ok = session.ConfirmDefeat(stamp);
        if (ok) Result = "击败请求已排队，执行前将重新校验检查点与敌方状态。";
        else Reject("确认已失效，请重新检查当前目标");
        return ok;
    }
    internal static void CancelConfirmation() => session.CancelUi();
    private static bool Reject(string message)
    {
        RequestError = message;
        if (!session.Busy) Result = message;
        return false;
    }

    private static void Execute()
    {
        var current = stamp!;
        if (!session.TryCommit(current, () =>
        {
            NativeSave.Validate(Plugin.Store.Validate(current.Generation));
            ReadState(BattleManager._Instance, SaveManager._Instance);
            return CommandPanelState.Open && stamp == current && Plugin.Store.Selected(false) == current.Generation;
        }, out var request)) { Result = "检查点完整校验失败，或战斗状态已变化；本次操作未执行。"; return; }
        try
        {
            var manager = BattleManager._Instance;
            if (request!.Operation == CombatOperation.Restore)
            {
                var results = new List<string>();
                var targets = Team.Where(unit => request.UnitKey == null || unit.Key == request.UnitKey).ToArray();
                foreach (var target in targets)
                {
                    if (manager.Pointer != managerPointer || SceneManager.GetActiveScene().handle != scene ||
                        SaveManager._Instance?._Player?.Pointer != playerPointer || manager._IsGameOver || Plugin.Loading || Plugin.Faulted)
                        throw new InvalidOperationException("恢复期间游戏状态已变化");
                    if (!manager.TeamDic.ContainsKey(target.Id)) throw new InvalidOperationException("我方单位集合已变化");
                    var obj = manager.TeamDic[target.Id];
                    var data = obj == null ? null : obj.GetComponent<BattleUnitData>();
                    if (data == null || Key(data) != target.Key) throw new InvalidOperationException("我方单位身份已变化");
                    var before = Vitals(data); if (!before.CanRestore) { results.Add(target.Name + "：已死亡或数值无效，跳过"); continue; }
                    if (!CombatSubmission.TryRun(() =>
                    {
                        NativeSave.Validate(Plugin.Store.Validate(current.Generation));
                        return Plugin.CheckpointsActive && Plugin.Store.Selected(false) == current.Generation;
                    }, () =>
                    {
                        if (BattleManager._Instance?.Pointer != managerPointer || SceneManager.GetActiveScene().handle != scene ||
                            SaveManager._Instance?._Player?.Pointer != playerPointer ||
                            PlayerDataManager._Instance?._PlayerData?.Pointer != dataPointer || (manager._BattleTableId ?? "") != battleId)
                            return false;
                        ReadState(manager, SaveManager._Instance, forceSnapshot: true, ignoreOwnOperation: true);
                        return stamp == current && manager.TeamDic.ContainsKey(target.Id) &&
                            manager.TeamDic[target.Id]?.GetComponent<BattleUnitData>()?.Pointer == data.Pointer;
                    }, () => data.SkillCure(1f))) throw new InvalidOperationException("恢复提交前检查点或战斗状态已变化");
                    var after = Vitals(data);
                    if (!after.CanRestore) throw new InvalidOperationException("恢复后单位数值异常");
                    data._BattleRoleWorld?.RendererHPS(data, false, false);
                    results.Add(CombatPolicy.RestoreReport(target.Name, before, after));
                }
                Result = results.Count == 0 ? "未找到本场有效我方单位，未执行恢复。" : string.Join("\n", results);
                session.Complete(false);
                var currentData = manager._CurrentBattleUtil?.GetComponent<BattleUnitData>();
                if (currentData != null) LJG.Framework.Main.m_UI?.GetOpenedUI<BattleUILogicResident>()?.ChangeQuick(currentData);
                ReadState(manager, SaveManager._Instance);
            }
            else
            {
                var targets = new Il2CppSystem.Collections.Generic.List<GameObject>();
                foreach (var enemy in Enemies)
                {
                    if (!manager.EnemyDic.ContainsKey(enemy.Id)) throw new InvalidOperationException("敌方集合已变化");
                    var obj = manager.EnemyDic[enemy.Id]; var data = obj == null ? null : obj.GetComponent<BattleUnitData>();
                    if (data == null || Key(data) != enemy.Key || data._Hp <= 0 || obj!.tag != "Enemy")
                        throw new InvalidOperationException("敌方身份或存活状态无法确认");
                    targets.Add(obj!);
                }
                if (targets.Count == 0) throw new InvalidOperationException("没有有效敌方目标");
                operationStarted = Time.realtimeSinceStartup; nativeFinished = false;
                var iterator = manager.EnemyHurt(targets, false, false, true);
                nativeIterator = iterator.Pointer;
                manager.StartCoroutine(iterator);
                Result = "已提交原生死亡流程，正在等待游戏结算；本场不会再次提交。";
            }
            Plugin.Logger.LogInfo("战斗修改：" + Result);
        }
        catch (Exception error)
        {
            session.Complete(true); Result = "战斗操作异常，可能已部分生效；本场不再修改，请在安全状态恢复检查点。";
            Plugin.Logger.LogError(Result + "：" + error);
        }
    }

    internal static void IteratorFinished(IntPtr pointer) { if (pointer == nativeIterator) nativeFinished = true; }
    internal static object Diagnostics()
    {
        ReadState(BattleManager._Instance, SaveManager._Instance, forceSnapshot: true);
        return new { epoch, phase, battleId, startedUtc, InBattle, BattleContext, PhaseBlock, Block, Result, RequestError,
        CheckpointText, stamp, Team, Enemies, session.Busy, session.DefeatSubmitted, session.Disabled,
        confirmation = session.Confirmation, retained = session.RetainedGenerations, observer = CombatFlowObserver.Status,
        activeFlows = CombatFlowObserver.Active, flowVersion = CombatFlowObserver.Version, nativeFinished,
        battleType = BattleManager._Instance == null ? -1 : (int)BattleManager._Instance._BattleType,
        playerProgress = ProgressDiagnostics(),
        native = BattleManager._Instance == null ? null : new
        {
            BattleManager._Instance._IsBattle, BattleManager._Instance._IsStartBattle, BattleManager._Instance._IsEnterBattle,
            BattleManager._Instance._IsBattleTime, BattleManager._Instance._IsGameOver, BattleManager._Instance.IsDies,
            BattleManager._Instance._IsAutoAI, BattleManager._Instance._AutoSetNum, BattleManager._Instance._DSTime,
            BattleManager._Instance._DSTime01, BattleManager._Instance._DSTimeChiXu,
            result = (int)BattleManager._Instance._DsBattleResultType,
            ui = LJG.Framework.Main.m_UI?.GetOpenedUI<BattleUILogicResident>() != null,
            actor = BattleManager._Instance._CurrentBattleUtil?.name
        } };
    }

    private static object? ProgressDiagnostics()
    {
        var player = PlayerDataManager._Instance?._PlayerData;
        if (player == null) return null;
        var items = new Dictionary<string, int>(); var equipment = new Dictionary<string, int>();
        if (player.ItemHad != null) foreach (var pair in player.ItemHad) items[pair.Key] = pair.Value?._Count ?? -1;
        if (player.EquipHad != null) foreach (var pair in player.EquipHad) equipment[pair.Key] = pair.Value?.Count ?? -1;
        return new { player.Exp, player.Lv, items, equipment };
    }

    internal static object TriggerDiagnostics()
    {
        if (!Plugin.TestMode) throw new InvalidOperationException("Only TestMode supports combat diagnostics");
        return UnityEngine.Object.FindObjectsOfType<BattleTrigger>().Select(trigger => new
        {
            key = SceneManager.GetActiveScene().handle + ":" + trigger.GetInstanceID(), trigger.name,
            position = trigger.transform.position.ToString(), trigger.CanEnterBattle, trigger.isCanTrigger,
            trigger._IsBattle, trigger._IsGoBattle, trigger._CanBanttleTrigger,
            player = trigger._Player == null ? null : trigger._Player.name,
            enemies = trigger.EnemyTeam?.Count, team = trigger.PlayerTeam?.Count
        }).ToArray();
    }

    internal static void StartTriggerForTest(string key)
    {
        if (!Plugin.TestMode) throw new InvalidOperationException("Only TestMode supports test encounters");
        var reason = Plugin.UnsafeReason(false);
        if (reason != null || !Plugin.CheckpointsActive) throw new InvalidOperationException(reason ?? "检查点未启用");
        var selected = Plugin.Store.Selected() ?? throw new InvalidOperationException("请先建立并选择战前检查点");
        NativeSave.Validate(Plugin.Store.Validate(selected));
        var trigger = UnityEngine.Object.FindObjectsOfType<BattleTrigger>().Single(item =>
            SceneManager.GetActiveScene().handle + ":" + item.GetInstanceID() == key);
        if (trigger._IsBattle || trigger._IsGoBattle || trigger._Player == null || trigger._Player != SaveManager._Instance._Player ||
            trigger.EnemyTeam == null || trigger.EnemyTeam.Count == 0)
            throw new InvalidOperationException("该真实触发器尚未具备原生入场条件，请先移动到遭遇区域");
        trigger.EnterBattleEnemy(true);
    }
}
