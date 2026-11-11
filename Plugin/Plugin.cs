using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using BeastSagaBetter.Core;
using UnityEngine;

namespace BeastSagaBetter;

[BepInPlugin("scholarpei.beast-saga-better", "Beast-Saga-better", "0.1.0")]
public sealed partial class Plugin : BasePlugin
{
    internal static Plugin Current = null!;
    internal static ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> EnableCheckpoints = null!;
    internal static ConfigEntry<bool> EnableIndicators = null!;
    internal static ConfigEntry<bool> EnableFishingInstantSuccess = null!;
    internal static ConfigEntry<int> MaxManualCheckpoints = null!;
    internal static ConfigEntry<int> MaxPreDialogueCheckpoints = null!;
    internal static ConfigEntry<bool> CaptureThumbnails = null!;
    internal static ConfigEntry<KeyCode> SaveKey = null!;
    internal static ConfigEntry<KeyCode> LoadKey = null!;
    internal static ConfigEntry<KeyCode> PanelKey = null!;
    internal static ConfigEntry<KeyCode> ClosePanelKey = null!;
    internal static ConfigEntry<bool> PanelKeyMigrated = null!;
    internal static CheckpointStore Store = null!;
    internal static string GameRoot = "";
    internal static string DataRoot = "";
    internal static string Fingerprint = "";
    internal static bool Busy;
    internal static bool Loading;
    internal static bool Faulted;
    internal static string? DialogueCheckpoint;
    internal static string? LastConversation;
    internal static float LastInput;
    internal static float LoadStarted;
    internal static bool ObservedLoading;
    internal static string LoadWork = "";
    internal static string Notice = "";
    internal static float NoticeUntil;
    internal static bool TestMode;
    internal static bool PendingNativeLoad;
    internal static bool NativeLoadCompleted;
    internal static bool ContinueRequested;
    internal static float ContinueRequestedAt;
    internal static bool NativeLoadStarted;
    internal static int ExpectedContinueQuick;
    internal static bool ContinueIndexReady;
    internal static bool ContinueReady;
    internal static int GuiFrames;
    internal static string? PanelSelectedCheckpoint;
    internal static string? PanelArchiveSelection;
    internal static IReadOnlyList<CheckpointSummary>? PanelArchive;
    internal static string PanelNoteDraft = "";
    internal static bool PanelNoteEditing;
    internal static LoadWorkStore LoadWorkStore = null!;
    internal const string KnownFingerprint = "B12412BC21B0B88A07851D15235736AFCFCE48A22AB8D3F1CB3DFB8376360359";
    internal const string ProductionAuthorizationFile = "SAFE_DIALOGUE_PRODUCTION_SL_AUTHORIZED";
    internal static bool CheckpointsActive => SafetyPolicy.CanOperate(
        TestMode, EnableCheckpoints.Value, File.Exists(Path.Combine(GameRoot, ProductionAuthorizationFile)));

    internal static bool TryBeginContinueRequest()
    {
        var now = Time.realtimeSinceStartup;
        var pending = ContinueRequested ? now - ContinueRequestedAt : 0;
        if (!LoadRequestPolicy.AllowContinue(
                TestMode, ContinueRequested, NativeLoadStarted, pending, LoadRequestPolicy.DefaultAbandonAfter))
        {
            Logger.LogInfo("忽略重复的继续游戏请求：上一次读档尚未完成");
            return false;
        }
        if (!ContinueRequested || pending > LoadRequestPolicy.DefaultAbandonAfter)
        {
            ContinueRequested = true;
            ContinueRequestedAt = now;
            NativeLoadStarted = false;
        }
        return true;
    }

    internal static string PanelCloseHint => ClosePanelKey.Value == KeyCode.None || ClosePanelKey.Value == PanelKey.Value
        ? $"{PanelKey.Value} 收起"
        : $"{PanelKey.Value} / {ClosePanelKey.Value} 收起";

    private static bool previousDialogue;

    public override void Load()
    {
        Current = this;
        Logger = Log;
        GameRoot = Paths.GameRootPath;
        DataRoot = Path.Combine(GameRoot, "BeastSagaBetterData");
        var legacyDataRoot = Path.Combine(GameRoot, "SafeDialogueToolsData");
        if (!Directory.Exists(DataRoot) && Directory.Exists(legacyDataRoot)) DataRoot = legacyDataRoot;
        TestMode = File.Exists(Path.Combine(GameRoot, "SAFE_DIALOGUE_TEST_COPY")) &&
            Path.GetFileName(GameRoot).Equals("Game", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(Path.GetDirectoryName(GameRoot)), "sandbox", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(GameRoot, "SaveData", "seed.es3"));
        if (File.Exists(Path.Combine(GameRoot, "SAFE_DIALOGUE_TEST_COPY")) && !TestMode)
            throw new InvalidOperationException("Invalid isolated test copy layout");
        if (TestMode)
        {
            Environment.CurrentDirectory = GameRoot;
            Application.runInBackground = true;
            var expectedPath = Path.Combine(GameRoot, "test-expected-quick.txt");
            if (File.Exists(expectedPath) && int.TryParse(File.ReadAllText(expectedPath).Trim(), out var expected) &&
                expected is >= 1 and <= 3)
                ExpectedContinueQuick = expected;
        }
        EnableCheckpoints = Config.Bind("General", "EnableCheckpoints", false,
            "本版本仅允许隔离副本启用 SL，正式目录即使设为 true 也不会接管原生 F5。");
        EnableIndicators = Config.Bind("General", "EnableIndicators", true, "显示只读判定提示；未知条件不推测结果。");
        EnableFishingInstantSuccess = Config.Bind("Cheats", "FishingInstantSuccess", false,
            "钓鱼开始后进度条自动拉满并立即结算成功。");
        MaxManualCheckpoints = Config.Bind("Checkpoints", "MaxManualCheckpoints", 50, "手动 F5 历史保留数量；已备注存档额外保留。");
        MaxPreDialogueCheckpoints = Config.Bind("Checkpoints", "MaxPreDialogueCheckpoints", 10, "后台保留的对话前检查点数量。");
        CaptureThumbnails = Config.Bind("Checkpoints", "CaptureThumbnails", true, "为手动 F5 历史保存 1280×720 截图。");
        SaveKey = Config.Bind("Keys", "QuickSave", KeyCode.F5, "快存；对话中选择对话前检查点。");
        LoadKey = Config.Bind("Keys", "QuickLoad", KeyCode.F6, "读取已明确选定的检查点。");
        PanelKey = Config.Bind("Keys", "Panel", KeyCode.Tab, "显示或隐藏快速工具面板。");
        ClosePanelKey = Config.Bind("Keys", "ClosePanel", KeyCode.Escape, "面板打开时退出面板。");
        PanelKeyMigrated = Config.Bind("Keys", "PanelKeyMigrated", false, "内部标记：旧版 Home 键位已迁移到 Tab。");
        if (!PanelKeyMigrated.Value)
        {
            if (PanelKey.Value == KeyCode.Home) PanelKey.Value = KeyCode.Tab;
            PanelKeyMigrated.Value = true;
        }
        Fingerprint = ComputeFingerprint(GameRoot);
        if (Fingerprint != KnownFingerprint)
        {
            Log.LogError("游戏或 interop 版本已变化，插件未挂钩任何游戏方法。请重新验证版本。");
            return;
        }
        Store = new CheckpointStore(Path.Combine(DataRoot, "checkpoints"), Fingerprint,
            MaxManualCheckpoints.Value, MaxPreDialogueCheckpoints.Value);
        LoadWorkStore = new LoadWorkStore(Path.Combine(DataRoot, "load-work"));
        var harmony = new Harmony("scholarpei.beast-saga-better");
        harmony.PatchAll(typeof(Plugin).Assembly);
        ItemIcons.Initialize(harmony);
        AddComponent<RuntimeDriver>();
        Log.LogInfo($"Loaded 0.1.0; fingerprint={Fingerprint}; checkpoints={EnableCheckpoints.Value}; testCopy={TestMode}");
    }

    internal static string ComputeFingerprint(string root)
    {
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in new[] { "GameAssembly.dll", "BeastSaga_Data/il2cpp_data/Metadata/global-metadata.dat", "BepInEx/interop/Model.Runtime.dll", "BepInEx/interop/Dialogue.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(root, relative));
            using var algorithm = SHA256.Create();
            aggregate.AppendData(algorithm.ComputeHash(stream));
        }
        return Convert.ToHexString(aggregate.GetHashAndReset());
    }

    internal static void Tip(string message)
    {
        Notice = message;
        NoticeUntil = Time.realtimeSinceStartup + 4;
        Logger.LogInfo(message);
    }

    internal static bool IsDialogue => DialogueManager.hasInstance && DialogueManager.isConversationActive;

    internal static void RefreshPanelSelectedCheckpoint()
    {
        if (!CheckpointsActive || Store == null || Faulted)
        {
            PanelSelectedCheckpoint = null;
            return;
        }
        try { PanelSelectedCheckpoint = Store.Selected(); }
        catch (Exception error)
        {
            PanelSelectedCheckpoint = null;
            Logger.LogMessage("面板无法读取检查点：" + error.Message);
        }
    }

    internal static void SelectArchive(string generation)
    {
        try
        {
            Store.Select(generation);
            PanelArchiveSelection = generation;
            RefreshPanelSelectedCheckpoint();
            RefreshPanelArchive();
            Tip("已设为读取目标；F6 读取");
        }
        catch (Exception error) { Fail("选择历史存档失败", error, false); }
    }

    internal static void SaveArchiveNote(string generation, string note)
    {
        try
        {
            Store.SetNote(generation, note);
            PanelNoteDraft = Store.Notes().GetValueOrDefault(generation, "");
            RefreshPanelArchive();
            Tip("存档备注已保存");
        }
        catch (Exception error) { Fail("保存存档备注失败", error, false); }
    }

    internal static void RefreshPanelArchive()
    {
        PanelArchive = Array.Empty<CheckpointSummary>();
        if (!CheckpointsActive || Store == null || Faulted) return;
        try
        {
            var watch = Stopwatch.StartNew();
            PanelArchive = Store.List(verifyPayloads: false).ToArray();
            if (watch.ElapsedMilliseconds >= 30)
                Logger.LogInfo($"面板存档列表刷新 {PanelArchive.Count} 项，耗时 {watch.ElapsedMilliseconds} ms");
        }
        catch (Exception error) { Logger.LogMessage("面板无法读取历史存档：" + error.Message); }
    }

    internal static string? UnsafeReason(bool permitDialogue)
    {
        if (Faulted) return "上次操作异常，本次运行已停用存读档，请重启游戏";
        if (Busy || Loading) return "正在存读档";
        var manager = SaveManager._Instance;
        if (manager == null || !manager.IsLogin || !manager._IsLoadOver || !manager._IsCanGame || manager._Player == null) return "游戏尚未准备好";
        if (manager.IsSaveIng) return "原生存档正在写入";
        var battle = BattleManager._Instance;
        if (battle == null || battle._IsBattle || battle._IsStartBattle || battle._IsEnterBattle || battle._IsGameOver) return "战斗或结算期间不可用";
        var map = MapManager._Instance;
        if (map == null || !map._IsInitOver || map._ChuanSongIng || map._ChuangSongMap) return "地图正在切换或清理";
        if (!permitDialogue && IsDialogue) return "正在对话";
        if (!permitDialogue && !manager._PlayerCanMove) return "玩家控制尚未恢复";
        if (DialogueManager.hasInstance)
        {
            var view = DialogueManager.conversationView;
            if (view != null && (view.isPlayingResponseMenuSequence || (view.sequencer != null && view.sequencer.isPlaying))) return "剧情演出正在执行";
        }
        return null;
    }

    internal static void SavePressed()
    {
        if (!CheckpointsActive) return;
        if (Busy || Loading || Faulted) { Tip(UnsafeReason(true) ?? "存读档不可用"); return; }
        try
        {
            if (IsDialogue)
            {
                if (DialogueCheckpoint == null) { Tip("本轮对话没有安全检查点，未改变读取目标"); return; }
                Store.Select(DialogueCheckpoint);
                Tip("已选用对话前存档；F6 将回到对话开始前");
                return;
            }
            var reason = UnsafeReason(false);
            if (reason != null) { Tip(reason); return; }
            var generation = Capture(CheckpointKind.Manual);
            Store.Select(generation);
            Tip("快速存档完成");
            Store.Prune(DialogueCheckpoint, ItemEditor.RetainedGeneration);
        }
        catch (Exception error) { Fail("保存失败，原读取目标保留", error, false); }
    }

    internal static string Capture()
    {
        Busy = true;
        try { return Store.Create(NativeSave.Write, NativeSave.Validate); }
        finally { Busy = false; }
    }

    internal static string Capture(CheckpointKind kind, string? conversation = null)
    {
        Busy = true;
        try
        {
            var capturedAt = DateTime.Now;
            var mapName = MapManager._Instance.GetMapNameInfo();
            var playerLevel = PlayerDataManager._Instance._PlayerData.Lv;
            var round = PlayerDataManager._Instance._PlayerData._Round;
            var day = TimeController._Instance._TimeSaveData.dayTime;
            string? screenshot = null;
            var generation = Store.Create(path =>
                {
                    NativeSave.Write(path);
                    if (kind == CheckpointKind.Manual && CaptureThumbnails.Value)
                    {
                        try { NativeSave.WriteThumbnailWithoutPanel(path); }
                        catch (Exception error) { Logger.LogWarning("缩略截图失败，检查点仍保留：" + error.Message); }
                        screenshot = File.Exists(Path.Combine(path, "thumbnail.png")) ? "thumbnail.png" : null;
                    }
                },
                NativeSave.Validate,
                new CheckpointMetadata(
                    kind, capturedAt, mapName, playerLevel, round, day, screenshot,
                    kind == CheckpointKind.PreDialogue ? conversation : null));
            return generation;
        }
        finally { Busy = false; }
    }

    internal static void BeforeConversation(string title)
    {
        if (!CheckpointsActive || Busy || Loading || IsDialogue) return;
        DialogueCheckpoint = null;
        LastConversation = title;
        var reason = UnsafeReason(false);
        if (reason != null) { Logger.LogInfo("Checkpoint skipped: " + reason); return; }
        try
        {
            var conversation = string.IsNullOrWhiteSpace(title)
                ? null
                : title.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (conversation is { Length: > 120 }) conversation = conversation[..120];
            DialogueCheckpoint = Capture(CheckpointKind.PreDialogue, conversation);
            Store.Prune(DialogueCheckpoint, ItemEditor.RetainedGeneration);
            Logger.LogInfo("Pre-dialogue checkpoint: " + title + " / " + DialogueCheckpoint);
        }
        catch (Exception error) { Fail("对话前检查点未建立，本轮对话中 F5 不可用", error, false); }
    }

    internal static void LoadPressed()
    {
        if (!CheckpointsActive) return;
        var reason = UnsafeReason(true);
        if (reason != null) { Tip(reason); return; }
        try
        {
            var generation = Store.Selected();
            if (generation == null) { Tip("尚未选择有效快存"); return; }
            var source = Store.Validate(generation);
            NativeSave.Validate(source);
            LoadWorkStore.CleanupExcept(null);
            LoadWork = LoadWorkStore.Prepare();
            foreach (var file in Directory.GetFiles(source).Where(path => Path.GetFileName(path) != "manifest.json"))
                File.Copy(file, Path.Combine(LoadWork, Path.GetFileName(file)));
            NativeSave.Validate(LoadWork);
            if (Login._Instance == null || LoadMain._Instance == null) throw new InvalidOperationException("官方场景重载入口不可用");
            CommandPanelState.Close();
            ItemEditor.Reset("开始读档");
            PanelNoteEditing = false;
            Loading = true;
            LoadStarted = Time.realtimeSinceStartup;
            ObservedLoading = false;
            NativeLoadCompleted = false;
            PendingNativeLoad = true;
            Login._Instance.LoadSaveDataInGame(1, 1);
            Tip("正在读取检查点");
        }
        catch (Exception error) { Fail("读取失败，已停用本次运行的存读档", error, true); }
    }

    internal static void TogglePanel()
    {
        var reason = UnsafeReason(true);
        var canShowPanel = reason is null or "正在对话";
        CommandPanelState.Toggle(true, false, canShowPanel);
        if (!canShowPanel)
        {
            PanelNoteEditing = false;
            Tip(reason ?? "请在游戏中打开面板");
            return;
        }
        if (CommandPanelState.Open)
        {
            RefreshPanelSelectedCheckpoint();
            RefreshPanelArchive();
        }
    }

    internal static void Tick()
    {
        if (Loading)
        {
            var manager = SaveManager._Instance;
            if (manager != null && !manager._IsLoadOver) ObservedLoading = true;
            if (ObservedLoading && NativeLoadCompleted && manager != null && manager._IsLoadOver && manager._IsCanGame && Time.realtimeSinceStartup - LoadStarted > 1)
            {
                Loading = false;
                PendingNativeLoad = false;
                DialogueCheckpoint = null;
                Tip("检查点读取完成");
            }
            else if (Time.realtimeSinceStartup - LoadStarted > 90)
                Fail("读档超时，已停用本次运行的存读档，请重启游戏", new TimeoutException(), true);
        }
        if (TestMode && ContinueRequested && !NativeLoadStarted &&
            Time.realtimeSinceStartup - ContinueRequestedAt > LoadRequestPolicy.DefaultAbandonAfter)
        {
            ContinueRequested = false;
            Logger.LogWarning("继续游戏请求未进入读档，已解除重复请求锁");
        }
        var dialogue = IsDialogue;
        if (previousDialogue && !dialogue) DialogueCheckpoint = null;
        previousDialogue = dialogue;
        ItemEditor.Tick();
        if (TestMode) TestBridge.Tick();
    }

    internal static void Fail(string message, Exception error, bool fatal)
    {
        if (fatal) { Faulted = true; Loading = false; }
        Logger.LogError(error);
        Tip(message);
    }

    internal static CommandPanelState.State PanelState()
    {
        PanelArchive ??= Array.Empty<CheckpointSummary>();
        var archive = PanelArchive;
        CheckpointSummary? selection = null;
        if (CheckpointsActive && !Faulted)
        {
            foreach (var item in archive)
            {
                if (item.Kind is not (CheckpointKind.Manual or CheckpointKind.PreDialogue)) continue;
                if (item.Generation == PanelArchiveSelection) { selection = item; break; }
                if (selection is null && item.Generation == PanelSelectedCheckpoint) selection = item;
            }
        }
        return new CommandPanelState.State(
            CheckpointsActive, IsDialogue, Busy, Loading, Faulted, EnableIndicators.Value, TestMode,
            DialogueCheckpoint, PanelSelectedCheckpoint, UnsafeReason(IsDialogue), archive, selection,
            EnableFishingInstantSuccess.Value);
    }
}

[HarmonyPatch(typeof(FishGoUILogicResident), nameof(FishGoUILogicResident.OnFixedUpdate))]
internal static class FishingInstantSuccessPatch
{
    private static bool reportedFailure;

    private static void Postfix(FishGoUILogicResident __instance)
    {
        if (!Plugin.EnableFishingInstantSuccess.Value) return;
        try
        {
            var bar = __instance.ui_HPBar;
            if (!CheatPolicy.ShouldFinishFishing(
                    Plugin.EnableFishingInstantSuccess.Value, __instance._Start, bar?.max ?? 0)) return;
            if (bar!.value < bar.max) bar.value = bar.max;
            __instance.CheckResult();
        }
        catch (Exception error)
        {
            Plugin.EnableFishingInstantSuccess.Value = false;
            if (reportedFailure) return;
            reportedFailure = true;
            Plugin.Logger.LogError("钓鱼直接成功执行失败，已停用：" + error);
            Plugin.Tip("钓鱼直接成功已停用");
        }
    }
}

[HarmonyPatch(typeof(FishGoUILogicResident), nameof(FishGoUILogicResident.InitData))]
internal static class FishingCountdownSkipPatch
{
    private static bool reportedFailure;

    private static void Postfix(FishGoUILogicResident __instance)
    {
        if (!Plugin.EnableFishingInstantSuccess.Value) return;
        try
        {
            var countdown = __instance.ui_TCountDown;
            if (countdown == null ||
                !CheatPolicy.ShouldSkipFishingCountdown(Plugin.EnableFishingInstantSuccess.Value, countdown.playing)) return;
            countdown.Stop(true, false);
            __instance.PlayEndCountDown();
        }
        catch (Exception error)
        {
            if (reportedFailure) return;
            reportedFailure = true;
            Plugin.Logger.LogError("跳过钓鱼倒计时失败：" + error);
        }
    }
}

internal static class PanelArchiveData
{
    internal static CheckpointArchiveSortMode SortMode = CheckpointArchiveSortMode.CapturedTime;
    internal static CheckpointArchiveFilter Filter = CheckpointArchiveFilter.All;
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SelectQuickGame))]
internal static class IsolatedLoadPatch
{
    private static bool Prefix(SaveManager __instance, int __0, ref bool __result)
    {
        var route = SafetyPolicy.Route(Plugin.PendingNativeLoad, Plugin.Loading, Plugin.Faulted, __0);
        if (route == LoadRoute.Original) return true;
        __result = false;
        if (route == LoadRoute.Reject)
        {
            Plugin.Tip("已拦截失效的检查点读取请求，请重启游戏");
            return false;
        }
        try
        {
            if (__0 != 1) throw new InvalidOperationException("Unexpected native load request");
            NativeSave.Validate(Plugin.LoadWork);
            var info = NativeSave.ReadInfo(Plugin.LoadWork);
            __instance.fileName = Path.Combine(Plugin.LoadWork, "save.es3");
            __instance.fileUtilName = Path.Combine(Plugin.LoadWork, "map-util.es3");
            SaveManager.SaveTempUtilDataPath = Path.Combine(Plugin.LoadWork, "runtime-util.es3");
            Plugin.PendingNativeLoad = false;
            __instance.LoadGameSave(1, SaveType.Quick, info._Dlc);
            __result = true;
        }
        catch (Exception error) { Plugin.Fail("检查点读取中止，请重启游戏", error, true); }
        return false;
    }
}

public sealed partial class RuntimeDriver : MonoBehaviour
{
    private GUIStyle? messageStyle;
    private GUIStyle? futureStyle;
    private GUIStyle? futureTitleStyle;
    private GUIStyle? connectorStyle;
    private Dictionary<FuturePreviewKind, GUIStyle>? futureStyles;
    private GUIStyle? panelTitleStyle;
    private GUIStyle? panelSubtitleStyle;
    private GUIStyle? panelContentSubtitleStyle;
    private GUIStyle? panelWrappedSubtitleStyle;
    private GUIStyle? panelHeaderStatusStyle;
    private GUIStyle? panelHeaderButtonStyle;
    private GUIStyle? panelSectionStyle;
    private GUIStyle? panelLabelStyle;
    private GUIStyle? panelValueStyle;
    private GUIStyle? panelStatusLabelStyle;
    private GUIStyle? panelArchiveListDetailStyle;
    private GUIStyle? panelArchiveListNoteStyle;
    private GUIStyle? panelButtonStyle;
    private GUIStyle? panelDisabledButtonStyle;
    private GUIStyle? panelActionButtonStyle;
    private GUIStyle? panelActionButtonHover;
    private GUIStyle? panelActionButtonDisabled;
    private GUIStyle? panelToggleStyle;
    private GUIStyle? panelSurfaceStyle;
    private GUIStyle? panelCardStyle;
    private GUIStyle? panelLineStyle;
    private GUIStyle? panelDotStyle;
    private Texture2D? panelSurface;
    private Texture2D? panelCard;
    private Texture2D? panelLine;
    private Texture2D? panelButton;
    private Texture2D? panelButtonHoverTexture;
    private Texture2D? panelTabTexture;
    private Texture2D? panelTabActiveTexture;
    private Texture2D? panelArchiveRowTexture;
    private Texture2D? panelArchiveSelectedTexture;
    private Texture2D? panelNoteHighlightTexture;
    private GUIStyle? panelButtonHover;
    private GUIStyle? panelTabStyle;
    private GUIStyle? panelTabActiveStyle;
    private GUIStyle? panelFilterStyle;
    private GUIStyle? panelFilterActiveStyle;
    private GUIStyle? panelArchiveRowStyle;
    private GUIStyle? panelArchiveSelectedRowStyle;
    private GUIStyle? panelBadgeStyle;
    private GUIStyle? panelTextFieldStyle;
    private GUIStyle? panelSmallButtonStyle;
    private GUIStyle? panelArchiveLineStyle;
    private GUIStyle? panelArchiveNoteStyle;
    private GUIStyle? panelDetailSectionStyle;
    private GUIStyle? panelDetailValueStyle;
    private GUIStyle? panelDetailSubtitleStyle;
    private GUIStyle? panelDetailWrappedSubtitleStyle;
    private GUIStyle? panelDetailNoteStyle;
    private GUIStyle? panelDetailButtonStyle;
    private GUIStyle? panelDetailTextFieldStyle;
    private GUIStyle? panelNoteHighlightStyle;
    private GUIStyle? panelSidebarStyle;
    private GUIStyle? panelSidebarActiveStyle;
    private GUIStyle? panelSidebarDisabledStyle;
    private Vector2 archiveScroll;
    private Vector2 statusScroll;
    private Vector2 archiveBodyScroll;
    private Vector2 archiveDetailScroll;
    private readonly PanelStyleGeneration panelStyleGeneration = new();
    private readonly PanelGuiFailurePolicy guiFailurePolicy = new();
    private bool guiNoticeFailed;
    private bool futureRenderFailed;
    private string? thumbnailGeneration;
    private Texture2D? thumbnailTexture;
    private IReadOnlyList<CheckpointSummary>? archiveView;
    private IReadOnlyList<CheckpointSummary>? archiveViewSource;
    private CheckpointArchiveSortMode archiveViewMode = (CheckpointArchiveSortMode)(-1);
    private CheckpointArchiveFilter archiveViewFilter = (CheckpointArchiveFilter)(-1);

    public RuntimeDriver(IntPtr pointer) : base(pointer) { if (Plugin.TestMode) TestBridge.Runtime = this; }

    public void OnDestroy()
    {
        ItemEditor.Reset("插件销毁");
        if (thumbnailTexture != null) UnityEngine.Object.Destroy(thumbnailTexture);
        thumbnailTexture = null;
        thumbnailGeneration = null;
        if (panelSurface != null) UnityEngine.Object.Destroy(panelSurface);
        if (panelCard != null) UnityEngine.Object.Destroy(panelCard);
        if (panelLine != null) UnityEngine.Object.Destroy(panelLine);
        if (panelButton != null) UnityEngine.Object.Destroy(panelButton);
        if (panelButtonHoverTexture != null) UnityEngine.Object.Destroy(panelButtonHoverTexture);
        if (panelTabTexture != null) UnityEngine.Object.Destroy(panelTabTexture);
        if (panelTabActiveTexture != null) UnityEngine.Object.Destroy(panelTabActiveTexture);
        if (panelArchiveRowTexture != null) UnityEngine.Object.Destroy(panelArchiveRowTexture);
        if (panelArchiveSelectedTexture != null) UnityEngine.Object.Destroy(panelArchiveSelectedTexture);
        if (panelNoteHighlightTexture != null) UnityEngine.Object.Destroy(panelNoteHighlightTexture);
          DestroyCheatTextures();
    }

    private void OnApplicationQuit()
    {
        try { Plugin.LoadWorkStore?.CleanupExcept(null); }
        catch (Exception error) { Plugin.Logger.LogWarning("读档临时目录清理失败：" + error.Message); }
    }

    public void Update()
    {
        try
        {
            Plugin.Tick();
            var panel = PanelKeyGate.ReadKeyDown(Plugin.PanelKey.Value);
            var close = PanelKeyGate.ReadKeyDown(Plugin.ClosePanelKey.Value);
            var save = Plugin.CheckpointsActive && Input.GetKeyDown(Plugin.SaveKey.Value);
            var load = Plugin.CheckpointsActive && Input.GetKeyDown(Plugin.LoadKey.Value);
            if (!Application.isFocused) return;
            if (CommandPanelState.Open && (panel || close))
            {
                Plugin.LastInput = Time.realtimeSinceStartup;
                PanelKeyGate.Consume();
                if (CommandPanelState.Tab == CommandPanelTab.Archive && Plugin.PanelNoteEditing)
                {
                    if (close)
                    {
                        Plugin.PanelNoteDraft = Plugin.PanelState().ArchiveSelection?.Note ?? "";
                        Plugin.PanelNoteEditing = false;
                    }
                    return;
                }
                CommandPanelState.Close();
                ItemEditor.Reset("面板关闭");
                Plugin.PanelNoteEditing = false;
                return;
            }
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                var selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (selected != null && (selected.GetComponent<UnityEngine.UI.InputField>() != null || selected.GetComponent<TMPro.TMP_InputField>() != null)) return;
            }
            if (FairyGUI.Stage.inst != null && FairyGUI.Stage.inst.focus != null && FairyGUI.Stage.inst.focus.TryCast<FairyGUI.InputTextField>() != null) return;
            if (panel && !save && !load && Time.realtimeSinceStartup - Plugin.LastInput >= 0.35f)
            {
                Plugin.LastInput = Time.realtimeSinceStartup;
                PanelKeyGate.Consume();
                Plugin.TogglePanel();
                return;
            }
            if (!panel && ((save && !load) || (load && !save)) && Time.realtimeSinceStartup - Plugin.LastInput < 0.35f) return;
            if (panel || !save && !load) return;
            Plugin.LastInput = Time.realtimeSinceStartup;
            if (save && !load)
            {
                Plugin.SavePressed();
                if (CommandPanelState.Open)
                {
                    Plugin.RefreshPanelSelectedCheckpoint();
                    Plugin.RefreshPanelArchive();
                }
            }
            else if (load && !save) Plugin.LoadPressed();
        }
        catch (Exception error) { Plugin.Fail("插件操作已中止", error, true); }
    }

    public void OnGUI()
    {
        Plugin.GuiFrames++;
        var matrix = GUI.matrix;
        var color = GUI.color;
        var backgroundColor = GUI.backgroundColor;
        var contentColor = GUI.contentColor;
        var enabled = GUI.enabled;
        var drawingNotice = false;
        var drawingFuture = false;
        var failed = false;
        try
        {
            if (!guiNoticeFailed && Time.realtimeSinceStartup <= Plugin.NoticeUntil && !string.IsNullOrEmpty(Plugin.Notice))
            {
                drawingNotice = true;
                DrawMessage(new Rect(Screen.width * 0.2f, 24, Screen.width * 0.6f, 46), Plugin.Notice);
                drawingNotice = false;
            }
            RuntimeDriver.ClearStaleHover(Indicators.HoveredButton);
            if (CommandPanelState.Open) DrawCommandPanel();
            if (!futureRenderFailed && Plugin.EnableIndicators.Value && Plugin.IsDialogue && Indicators.HoveredButton != null && !string.IsNullOrEmpty(Indicators.HoverHint))
            {
                drawingFuture = true;
                DrawFutureWindow();
            }
        }
        catch (Exception error)
        {
            failed = true;
            if (drawingNotice) guiNoticeFailed = true;
            if (drawingFuture) futureRenderFailed = true;
            CommandPanelState.Close();
            Plugin.PanelNoteEditing = false;
            Plugin.PanelNoteDraft = "";
            cheatCharacterMenuOpen = false;
            ClearCheatDrafts();
            var firstFailure = guiFailurePolicy.RecordFailure();
            if (firstFailure || drawingFuture)
            {
                Plugin.Logger.LogError((drawingFuture ? "悬停预览绘制异常，已停用该预览：" : "面板绘制异常，已关闭面板：") + error);
                Plugin.Notice = drawingFuture
                    ? "悬停预览绘制异常，该预览已停用；详细错误已写入插件日志"
                    : "面板绘制异常，已关闭；详细错误已写入插件日志";
                Plugin.NoticeUntil = Time.realtimeSinceStartup + 8;
            }
            // Abort this event completely: never continue a save/edit after a rendering failure.
            // Cleanup itself can touch native resources, so it must not escape this containment boundary.
            try
            {
                ItemEditor.Reset("面板绘制异常");
                GUI.FocusControl(null);
            }
            catch (Exception cleanupError)
            {
                if (firstFailure) Plugin.Logger.LogWarning("面板异常清理未完整完成：" + cleanupError);
            }
        }
        finally
        {
            GUI.matrix = matrix;
            GUI.color = color;
            GUI.backgroundColor = backgroundColor;
            GUI.contentColor = contentColor;
            GUI.enabled = enabled;
            if (!CommandPanelState.Open) CommandPanelState.ClearPointerBounds();
            if (failed)
            {
                // Inner scroll/group/skin finally blocks have unwound before resources are invalidated.
                panelStyleGeneration.Invalidate();
                cheatStyleGeneration.Invalidate();
                messageStyle = null;
                futureStyles = null;
                futureStyle = null;
                futureTitleStyle = null;
                connectorStyle = null;
            }
        }
    }

    private void DrawMessage(Rect bounds, string message)
    {
        messageStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        messageStyle.fontSize = Math.Clamp((int)(Screen.height / 50f), 18, 28);
        GUI.Box(bounds, "");
        GUI.Label(bounds, message, messageStyle);
    }

    private void DrawCommandPanel()
    {
        var state = Plugin.PanelState();
        var layout = CommandPanelLayout.Create(Screen.width, Screen.height);
        var bounds = new Rect(layout.X, layout.Y, layout.Width, layout.Height);
        var metrics = CommandPanelMetrics.Create(layout);
        EnsurePanelStyles();
        CommandPanelState.SetPointerBounds(bounds.x, bounds.y, bounds.width, bounds.height);
        GUI.Box(bounds, GUIContent.none, panelSurfaceStyle!);
        GUI.Box(new Rect(bounds.x, bounds.y, bounds.width, 1), GUIContent.none, panelLineStyle!);
        var scale = metrics.Scale;
        var outerPad = 14 * scale;
        var header = new Rect(bounds.x + outerPad, bounds.y + 12 * scale, bounds.width - outerPad * 2, 78 * scale);
        GUI.Box(header, GUIContent.none, panelCardStyle!);
        GUI.Label(new Rect(header.x + 18 * scale, header.y + 13 * scale, 320 * scale, 30 * scale),
            "Beast-Saga-better", panelTitleStyle);
        GUI.Label(new Rect(header.x + 18 * scale, header.y + 46 * scale, 320 * scale, 22 * scale),
            "对话安全与预测工具", panelSubtitleStyle);
        var closeWidth = 84 * scale;
        var closeHeight = 30 * scale;
        var closeRect = new Rect(header.xMax - 18 * scale - closeWidth,
            header.y + (header.height - closeHeight) / 2, closeWidth, closeHeight);
        var statusRight = closeRect.x - 14 * scale;
        var statusText = state.Faulted || state.BlockedReason is not null
            ? "安全检查未通过 · " + state.Mode
            : "安全检查通过 · " + state.Mode;
        var statusSize = panelSubtitleStyle!.CalcSize(new GUIContent(statusText));
        var statusTextRect = new Rect(statusRight - statusSize.x, header.y, statusSize.x, header.height);
        GUI.Label(statusTextRect, statusText, panelHeaderStatusStyle);
        var dotSize = 10 * scale;
        var dot = new Rect(statusTextRect.x - 16 * scale,
            header.y + (header.height - dotSize) / 2, dotSize, dotSize);
        DrawStatusDot(dot, state);
        if (GUI.Button(closeRect, "关闭面板", panelHeaderButtonStyle!))
        {
            CommandPanelState.Open = false;
            Plugin.PanelNoteEditing = false;
        }
        var tabHeight = 38 * scale;
        var tabGap = 6 * scale;
        var tabWidth = (bounds.width - outerPad * 2 - tabGap * 2) / 3f;
        var statusTab = new Rect(bounds.x + outerPad, header.yMax + 12 * scale, tabWidth, tabHeight);
        var archiveTab = new Rect(statusTab.xMax + tabGap, statusTab.y, tabWidth, tabHeight);
        var cheatsTab = new Rect(archiveTab.xMax + tabGap, statusTab.y, tabWidth, tabHeight);
        if (GUI.Button(statusTab, "状态", CommandPanelState.Tab == CommandPanelTab.Status ? panelTabActiveStyle! : panelTabStyle!))
        {
            CommandPanelState.SelectTab(CommandPanelTab.Status);
            Plugin.PanelNoteEditing = false;
        }
        if (GUI.Button(archiveTab, "存档库", CommandPanelState.Tab == CommandPanelTab.Archive ? panelTabActiveStyle! : panelTabStyle!))
        {
            CommandPanelState.SelectTab(CommandPanelTab.Archive);
            Plugin.RefreshPanelArchive();
            Plugin.PanelNoteEditing = false;
        }
        if (GUI.Button(cheatsTab, "修改作弊", CommandPanelState.Tab == CommandPanelTab.Cheats ? panelTabActiveStyle! : panelTabStyle!))
        {
            CommandPanelState.SelectTab(CommandPanelTab.Cheats);
            Plugin.PanelNoteEditing = false;
        }
        if (CommandPanelState.Tab == CommandPanelTab.Archive)
        {
            DrawArchiveTab(bounds, state);
            return;
        }
        if (CommandPanelState.Tab == CommandPanelTab.Cheats)
        {
            DrawCheatsTab(bounds, state);
            return;
        }
        DrawStatusTab(bounds, state, scale);
    }

    private static Rect UnityRect(PanelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private float TextHeight(GUIStyle style, string text, float width, float minimum) =>
        Math.Max(minimum, Math.Max(style.lineHeight + 4, style.CalcHeight(new GUIContent(text), Math.Max(1, width))));

    private void DrawStatusTab(Rect bounds, CommandPanelState.State state, float scale)
    {
        var body = PanelBodyLayout.Create(bounds.width, bounds.height, scale);
        var contentScale = Math.Min(scale, 1.5f);
        var width = Math.Max(1, Math.Min(body.View.Width - 20 * contentScale, 1040 * contentScale));
        var contentX = Math.Max(0, (body.View.Width - width) / 2);
        var labels = new[] { "运行模式", "对话前", "读取目标", "安全检查" };
        var values = new[] { state.Mode, state.DialogueCheckpointText, state.SelectedCheckpointText, state.BlockedText };
        var probe = StatusPanelGeometry.Create(width, contentScale, new float[4]);
        var heights = values.Select((text, index) => TextHeight(panelValueStyle!, text, probe.Values[index].Width, 28 * contentScale)).ToArray();
        var geometry = StatusPanelGeometry.Create(width, contentScale, heights);
        statusScroll = GUI.BeginScrollView(UnityRect(body.View), statusScroll,
            new Rect(0, 0, body.View.Width, Math.Max(body.View.Height, geometry.Height)), false, true);
        GUI.BeginGroup(new Rect(contentX, 0, width, geometry.Height));
        try
        {
            GUI.Box(UnityRect(geometry.Card), GUIContent.none, panelCardStyle!);
            GUI.Label(new Rect(16 * contentScale, 8 * contentScale, width - 32 * contentScale, 24 * contentScale), "状态", panelSectionStyle);
            for (var index = 0; index < values.Length; index++)
            {
                GUI.Label(UnityRect(geometry.Labels[index]), labels[index], panelStatusLabelStyle!);
                GUI.Label(UnityRect(geometry.Values[index]), values[index], panelValueStyle!);
            }
            GUI.Label(new Rect(0, geometry.Card.Bottom + 12 * contentScale, width, 24 * contentScale), "快速操作", panelSectionStyle);
            if (DrawActionButton(UnityRect(geometry.Save), "快速保存（F5）", state.CanSave, panelActionButtonStyle!, panelActionButtonHover!, panelActionButtonDisabled!))
            {
                CommandPanelState.RunSave(state, new CommandPanelState.Actions(Plugin.SavePressed, Plugin.LoadPressed));
                Plugin.RefreshPanelSelectedCheckpoint();
                Plugin.RefreshPanelArchive();
            }
            if (DrawActionButton(UnityRect(geometry.Load), "读取检查点（F6）", state.CanLoad, panelActionButtonStyle!, panelActionButtonHover!, panelActionButtonDisabled!))
                CommandPanelState.RunLoad(state, new CommandPanelState.Actions(Plugin.SavePressed, Plugin.LoadPressed));
            GUI.Label(new Rect(0, geometry.Load.Bottom + 18 * contentScale, width, 24 * contentScale), "辅助", panelSectionStyle);
            var toggle = UnityRect(geometry.Toggle);
            GUI.Box(toggle, GUIContent.none, panelCardStyle!);
            var next = GUI.Toggle(new Rect(toggle.x + 16 * contentScale, toggle.y + 8 * contentScale, Math.Max(1, toggle.width - 32 * contentScale), 28 * contentScale),
                state.IndicatorsEnabled, "悬停显示判定与未来预览", panelToggleStyle);
            if (next != state.IndicatorsEnabled) Plugin.EnableIndicators.Value = next;
        }
        finally
        {
            GUI.EndGroup();
            GUI.EndScrollView();
        }
        DrawFooterHint(new Rect(contentX, body.Footer.Y, width, body.Footer.Height),
            $"{Plugin.PanelCloseHint} · {Plugin.SaveKey.Value} 快存 · {Plugin.LoadKey.Value} 读取");
    }

    private void DrawArchiveTab(Rect bounds, CommandPanelState.State state)
    {
        var source = state.Archive ?? Array.Empty<CheckpointSummary>();
        if (!ReferenceEquals(source, archiveViewSource) || archiveViewMode != PanelArchiveData.SortMode || archiveViewFilter != PanelArchiveData.Filter)
        {
            archiveView = CheckpointArchiveView.Apply(source, PanelArchiveData.Filter, PanelArchiveData.SortMode);
            archiveViewSource = source;
            archiveViewMode = PanelArchiveData.SortMode;
            archiveViewFilter = PanelArchiveData.Filter;
        }
        var archive = archiveView!;
        var metrics = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height));
        var scale = metrics.Scale;
        var contentScale = metrics.ContentScale;
        var body = PanelBodyLayout.Create(bounds.width, bounds.height, scale);
        var pageWidth = Math.Max(1, body.View.Width - 20 * contentScale);
        var columns = ArchiveColumns.Create(pageWidth, body.View.Height, contentScale);
        archiveBodyScroll = GUI.BeginScrollView(UnityRect(body.View), archiveBodyScroll,
            new Rect(0, 0, pageWidth, columns.Height), false, columns.Height > body.View.Height);
        try
        {
            var listWidth = columns.List.Width;
            GUI.Label(new Rect(0, 0, listWidth, 24 * contentScale), state.ArchiveCountText, panelContentSubtitleStyle!);
            var filterY = 28 * contentScale;
            var filterX = 0f;
            foreach (var (filter, label, width) in new[]
            {
                (CheckpointArchiveFilter.All, "全部", 46f * contentScale),
                (CheckpointArchiveFilter.Manual, "手动", 46f * contentScale),
                (CheckpointArchiveFilter.PreDialogue, "对话前", 62f * contentScale)
            })
            {
                if (GUI.Button(new Rect(filterX, filterY, width, 32 * contentScale), label,
                    PanelArchiveData.Filter == filter ? panelFilterActiveStyle! : panelFilterStyle!))
                {
                    PanelArchiveData.Filter = filter;
                    archiveScroll = Vector2.zero;
                }
                filterX += width + 6 * contentScale;
            }
            var sortWidth = Math.Min(listWidth, 126 * contentScale);
            var sortY = columns.SeparateSortRow ? filterY + 38 * contentScale : filterY;
            if (GUI.Button(new Rect(listWidth - sortWidth, sortY, sortWidth, 32 * contentScale),
                PanelArchiveData.SortMode == CheckpointArchiveSortMode.CapturedTime ? "按存档时间" : "按游戏时间", panelSmallButtonStyle!))
                PanelArchiveData.SortMode = PanelArchiveData.SortMode == CheckpointArchiveSortMode.CapturedTime
                    ? CheckpointArchiveSortMode.GameTime : CheckpointArchiveSortMode.CapturedTime;
            var rowWidth = Math.Max(1, listWidth - 20 * contentScale);
            var rowGeometry = ArchiveRowGeometry.Create(rowWidth, contentScale,
                Math.Max(22 * contentScale, panelArchiveLineStyle!.lineHeight + 4 * contentScale),
                Math.Max(18 * contentScale, panelBadgeStyle!.lineHeight + 4 * contentScale),
                Math.Max(20 * contentScale, panelArchiveListDetailStyle!.lineHeight + 4 * contentScale),
                Math.Max(22 * contentScale, panelArchiveListNoteStyle!.lineHeight + 4 * contentScale));
            var rowHeight = Math.Max(metrics.ArchiveRowHeight, rowGeometry.Height);
            var stride = rowHeight + metrics.ArchiveRowSpacing;
            archiveScroll = GUI.BeginScrollView(UnityRect(columns.List), archiveScroll,
                new Rect(0, 0, rowWidth, Math.Max(columns.List.Height, archive.Count * stride)), false, true);
            try
            {
                for (var index = 0; index < archive.Count; index++)
                {
                    var item = archive[index];
                    var row = new Rect(0, index * stride, rowWidth, rowHeight);
                    if (GUI.Button(row, GUIContent.none,
                        state.ArchiveSelection?.Generation == item.Generation ? panelArchiveSelectedRowStyle! : panelArchiveRowStyle!))
                    {
                        Plugin.PanelArchiveSelection = item.Generation;
                        Plugin.PanelNoteDraft = item.Note ?? "";
                        Plugin.PanelNoteEditing = false;
                        archiveDetailScroll = Vector2.zero;
                    }
                    Rect At(PanelRect part) => new(part.X, row.y + part.Y, part.Width, part.Height);
                    GUI.Label(At(rowGeometry.Time), item.DisplayTopLine(), panelArchiveLineStyle!);
                    GUI.Label(At(rowGeometry.Badge), item.BadgeText(), panelBadgeStyle!);
                    GUI.Label(At(rowGeometry.Detail), item.DisplayDetailLine(), panelArchiveListDetailStyle!);
                    GUI.Box(At(rowGeometry.Note), GUIContent.none,
                        string.IsNullOrWhiteSpace(item.Note) ? panelCardStyle! : panelNoteHighlightStyle!);
                    var note = At(rowGeometry.Note);
                    GUI.Label(new Rect(note.x + 6 * contentScale, note.y, Math.Max(1, note.width - 12 * contentScale), note.height),
                        item.NoteText(), panelArchiveListNoteStyle!);
                }
            }
            finally { GUI.EndScrollView(); }
            DrawArchiveDetail(UnityRect(columns.Detail), state.ArchiveSelection, contentScale);
        }
        finally { GUI.EndScrollView(); }
        DrawFooterHint(UnityRect(body.Footer), $"{Plugin.PanelCloseHint} · {Plugin.LoadKey.Value} 读取当前目标");
    }

    private void DrawArchiveDetail(Rect detail, CheckpointSummary? selection, float scale)
    {
        GUI.Box(detail, GUIContent.none, panelCardStyle!);
        var detailScale = Math.Min(scale, 1.25f);
        var scrollWidth = Math.Max(1, detail.width - 20 * detailScale);
        var textWidth = Math.Max(1, scrollWidth - 32 * detailScale);
        if (selection is null)
        {
            var empty = PanelVerticalStack.Create(scrollWidth, detailScale, new[]
            {
                TextHeight(panelDetailValueStyle!, "选择一份存档", textWidth, 30 * detailScale),
                TextHeight(panelDetailWrappedSubtitleStyle!, "列表包含手动 F5 与对话前自动检查点；旧版检查点不显示。", textWidth, 30 * detailScale)
            });
            archiveDetailScroll = GUI.BeginScrollView(detail, archiveDetailScroll, new Rect(0, 0, scrollWidth, Math.Max(detail.height, empty.Height)), false, false);
            try
            {
                GUI.Label(UnityRect(empty.Blocks[0]), "选择一份存档", panelDetailValueStyle!);
                GUI.Label(UnityRect(empty.Blocks[1]), "列表包含手动 F5 与对话前自动检查点；旧版检查点不显示。", panelDetailWrappedSubtitleStyle!);
            }
            finally { GUI.EndScrollView(); }
            return;
        }
        var previewHeight = Math.Max(40 * scale, Math.Min(480 * scale, textWidth * 9 / 16));
        var conversation = selection.Conversation ?? "";
        var noteText = selection.NoteText();
        var noteHeight = Plugin.PanelNoteEditing ? 40 * detailScale
            : TextHeight(panelDetailNoteStyle!, noteText, Math.Max(1, textWidth - 24 * detailScale), 34 * detailScale) + 12 * detailScale;
        var stack = PanelVerticalStack.Create(scrollWidth, detailScale, new[]
        {
            previewHeight,
            TextHeight(panelDetailSectionStyle!, selection.KindText(), textWidth, 24 * detailScale),
            string.IsNullOrWhiteSpace(conversation) ? 1 : TextHeight(panelDetailValueStyle!, conversation, textWidth, 28 * detailScale),
            TextHeight(panelDetailValueStyle!, selection.DisplayFullTime(), textWidth, 28 * detailScale),
            TextHeight(panelDetailValueStyle!, selection.DisplayDetailLine(), textWidth, 28 * detailScale),
            TextHeight(panelDetailSubtitleStyle!, selection.Selected ? "当前读取目标" : "未设为读取目标", textWidth, 22 * detailScale),
            24 * detailScale, noteHeight, 36 * detailScale
        });
        archiveDetailScroll = GUI.BeginScrollView(detail, archiveDetailScroll,
            new Rect(0, 0, scrollWidth, Math.Max(detail.height, stack.Height)), false, false);
        try
        {
            var parts = stack.Blocks;
            var preview = LoadThumbnail(selection);
            if (preview != null) GUI.DrawTexture(UnityRect(parts[0]), preview, ScaleMode.ScaleToFit, false);
            else GUI.Label(UnityRect(parts[0]), selection.Kind == CheckpointKind.PreDialogue ? "对话前自动检查点\n无缩略截图" : "截图不可用", panelDetailWrappedSubtitleStyle!);
            GUI.Label(UnityRect(parts[1]), selection.KindText(), panelDetailSectionStyle!);
            if (!string.IsNullOrWhiteSpace(conversation)) GUI.Label(UnityRect(parts[2]), conversation, panelDetailValueStyle!);
            GUI.Label(UnityRect(parts[3]), selection.DisplayFullTime(), panelDetailValueStyle!);
            GUI.Label(UnityRect(parts[4]), selection.DisplayDetailLine(), panelDetailValueStyle!);
            GUI.Label(UnityRect(parts[5]), selection.Selected ? "当前读取目标" : "未设为读取目标", panelDetailSubtitleStyle!);
            GUI.Label(UnityRect(parts[6]), "备注", panelDetailSectionStyle!);
            var note = UnityRect(parts[7]);
            var buttons = UnityRect(parts[8]);
            if (!Plugin.PanelNoteEditing)
            {
                GUI.Box(note, GUIContent.none, panelNoteHighlightStyle!);
                GUI.Label(new Rect(note.x + 12 * detailScale, note.y + 6 * detailScale,
                    Math.Max(1, note.width - 24 * detailScale), note.height - 12 * detailScale),
                    noteText, panelDetailNoteStyle!);
                if (GUI.Button(note, GUIContent.none, GUIStyle.none))
                {
                    Plugin.PanelNoteDraft = selection.Note ?? "";
                    Plugin.PanelNoteEditing = true;
                }
                if (GUI.Button(buttons, "设为读取目标", panelDetailButtonStyle!)) Plugin.SelectArchive(selection.Generation);
            }
            else
            {
                Plugin.PanelNoteDraft = GUI.TextField(note, Plugin.PanelNoteDraft, panelDetailTextFieldStyle!);
                var gap = 8 * detailScale;
                var halfWidth = Math.Max(1, (buttons.width - gap) / 2);
                var save = GUI.Button(new Rect(buttons.x, buttons.y, halfWidth, buttons.height), "保存", panelDetailButtonStyle!) ||
                    Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                if (save)
                {
                    Plugin.SaveArchiveNote(selection.Generation, Plugin.PanelNoteDraft);
                    Plugin.PanelNoteEditing = false;
                    if (Event.current.type == EventType.KeyDown) Event.current.Use();
                }
                if (GUI.Button(new Rect(buttons.x + halfWidth + gap, buttons.y, halfWidth, buttons.height), "取消", panelDetailButtonStyle!))
                {
                    Plugin.PanelNoteDraft = selection.Note ?? "";
                    Plugin.PanelNoteEditing = false;
                }
            }
        }
        finally { GUI.EndScrollView(); }
    }
    private bool DrawActionButton(Rect bounds, string text, bool enabled, GUIStyle normal, GUIStyle hover, GUIStyle disabled)
    {
        var style = !enabled ? disabled : bounds.Contains(Event.current.mousePosition) ? hover : normal;
        var previous = GUI.enabled;
        try
        {
            GUI.enabled = previous && enabled;
            return GUI.Button(bounds, text, style) && previous && enabled;
        }
        finally { GUI.enabled = previous; }
    }

    private void DrawFooterHint(Rect area, string text)
    {
        var size = panelContentSubtitleStyle!.CalcSize(new GUIContent(text));
        var height = Math.Min(area.height, size.y + 2);
        GUI.Label(new Rect(area.x, area.y + (area.height - height) / 2,
            Math.Min(area.width, size.x + 2), height), text, panelContentSubtitleStyle);
    }

    private void DrawStatusDot(Rect bounds, CommandPanelState.State state)
    {
        var color = state.Faulted ? new Color32(255, 111, 97, 255)
            : state.CheckpointsActive ? new Color32(95, 238, 178, 255)
            : new Color32(255, 196, 84, 255);
        var old = GUI.color;
        GUI.color = color;
        GUI.Box(bounds, GUIContent.none, panelDotStyle!);
        GUI.color = old;
    }

    private static Texture2D CreateTexture(Color32 color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.DontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private Texture2D? LoadThumbnail(CheckpointSummary summary)
    {
        if (!summary.HasScreenshot || summary.ScreenshotPath is null) return null;
        if (thumbnailGeneration == summary.Generation && thumbnailTexture != null) return thumbnailTexture;
        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.DontSave;
            if (!texture.LoadImage(File.ReadAllBytes(summary.ScreenshotPath)))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            if (thumbnailTexture != null) UnityEngine.Object.Destroy(thumbnailTexture);
            thumbnailGeneration = summary.Generation;
            thumbnailTexture = texture;
            return texture;
        }
        catch
        {
            return null;
        }
    }

    private void EnsurePanelStyles()
    {
        var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height)).Scale;
        if (panelStyleGeneration.Matches(Screen.width, Screen.height, scale, PanelStylesAlive() && PanelTexturesAlive())) return;
        panelStyleGeneration.Invalidate();
        DestroyPanelTextures();
        panelTitleStyle = null;
        panelSubtitleStyle = null;
        panelContentSubtitleStyle = null;
        panelWrappedSubtitleStyle = null;
        panelHeaderStatusStyle = null;
        panelHeaderButtonStyle = null;
        panelSectionStyle = null;
        panelLabelStyle = null;
        panelValueStyle = null;
        panelButtonStyle = null;
        panelButtonHover = null;
        panelDisabledButtonStyle = null;
        panelActionButtonStyle = null;
        panelActionButtonHover = null;
        panelActionButtonDisabled = null;
        panelTabStyle = null;
        panelTabActiveStyle = null;
        panelFilterStyle = null;
        panelFilterActiveStyle = null;
        panelArchiveRowStyle = null;
        panelArchiveSelectedRowStyle = null;
        panelArchiveLineStyle = null;
        panelArchiveNoteStyle = null;
        panelDetailSectionStyle = null;
        panelDetailValueStyle = null;
        panelDetailSubtitleStyle = null;
        panelDetailWrappedSubtitleStyle = null;
        panelDetailNoteStyle = null;
        panelDetailButtonStyle = null;
        panelDetailTextFieldStyle = null;
        panelNoteHighlightStyle = null;
        panelBadgeStyle = null;
        panelTextFieldStyle = null;
        panelSmallButtonStyle = null;
        panelToggleStyle = null;
        panelSidebarStyle = null;
        panelSidebarActiveStyle = null;
        panelSidebarDisabledStyle = null;
        panelSurfaceStyle = null;
        panelCardStyle = null;
        panelLineStyle = null;
        panelDotStyle = null;
        panelSurface = CreateTexture(new Color32(10, 15, 24, 247));
        panelCard = CreateTexture(new Color32(26, 36, 51, 168));
        panelLine = CreateTexture(new Color32(93, 182, 197, 120));
        panelButton = CreateTexture(new Color32(23, 34, 48, 210));
        panelButtonHoverTexture = CreateTexture(new Color32(37, 56, 75, 235));
        panelTabTexture = CreateTexture(new Color32(20, 29, 42, 190));
        panelTabActiveTexture = CreateTexture(new Color32(35, 64, 76, 235));
        panelArchiveRowTexture = CreateTexture(new Color32(20, 29, 42, 180));
        panelArchiveSelectedTexture = CreateTexture(new Color32(38, 68, 81, 230));
        panelNoteHighlightTexture = CreateTexture(new Color32(64, 51, 16, 230));
        var contentScale = Math.Min(scale, 1.5f);
        panelTitleStyle = new GUIStyle { normal = { textColor = new Color32(235, 243, 255, 255) }, fontSize = Math.Max(20, (int)(25 * scale)), fontStyle = FontStyle.Bold };
        panelSubtitleStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = Math.Max(13, (int)(15 * scale)) };
        panelContentSubtitleStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = Math.Max(12, (int)(13 * contentScale)) };
        panelWrappedSubtitleStyle = new GUIStyle(panelContentSubtitleStyle) { wordWrap = true };
        panelHeaderStatusStyle = new GUIStyle(panelSubtitleStyle) { alignment = TextAnchor.MiddleRight };
        panelSectionStyle = new GUIStyle { normal = { textColor = new Color32(134, 217, 229, 255) }, fontSize = Math.Max(12, (int)(16 * contentScale)), fontStyle = FontStyle.Bold };
        panelLabelStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = Math.Max(12, (int)(14 * contentScale)) };
        panelValueStyle = new GUIStyle { normal = { textColor = new Color32(212, 226, 241, 255) }, fontSize = Math.Max(13, (int)(17 * contentScale)), wordWrap = true };
        panelStatusLabelStyle = new GUIStyle(panelValueStyle) { wordWrap = false, clipping = TextClipping.Clip };
        panelButtonStyle = new GUIStyle(panelLabelStyle) { normal = { background = panelButton, textColor = new Color32(229, 241, 255, 255) }, alignment = TextAnchor.MiddleCenter, fontSize = Math.Max(15, (int)(18 * scale)) };
        panelButtonHover = new GUIStyle(panelButtonStyle) { normal = { background = panelButtonHoverTexture } };
        panelDisabledButtonStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelButton, textColor = new Color32(110, 122, 138, 255) } };
        panelActionButtonStyle = new GUIStyle(panelButtonStyle) { fontSize = Math.Max(13, (int)(16 * contentScale)) };
        panelActionButtonHover = new GUIStyle(panelActionButtonStyle) { normal = { background = panelButtonHoverTexture } };
        panelActionButtonDisabled = new GUIStyle(panelActionButtonStyle) { normal = { background = panelButton, textColor = new Color32(110, 122, 138, 255) } };
        panelTabStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelTabTexture, textColor = new Color32(181, 195, 212, 255) } };
        panelTabActiveStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelTabActiveTexture, textColor = new Color32(154, 235, 244, 255) } };
        panelFilterStyle = new GUIStyle(panelActionButtonStyle)
        {
            fontSize = Math.Max(12, (int)(14 * contentScale)),
            normal = { background = panelTabTexture, textColor = new Color32(181, 195, 212, 255) }
        };
        panelFilterActiveStyle = new GUIStyle(panelFilterStyle)
        {
            normal = { background = panelTabActiveTexture, textColor = new Color32(154, 235, 244, 255) }
        };
        panelArchiveRowStyle = new GUIStyle { normal = { background = panelArchiveRowTexture } };
        panelArchiveSelectedRowStyle = new GUIStyle { normal = { background = panelArchiveSelectedTexture } };
        panelArchiveLineStyle = new GUIStyle(panelValueStyle) { fontSize = Math.Max(14, (int)(16 * contentScale)), wordWrap = false };
        panelArchiveLineStyle.clipping = TextClipping.Clip;
        panelArchiveNoteStyle = new GUIStyle(panelValueStyle)
        {
            fontSize = Math.Max(14, (int)(16 * contentScale)),
            normal = { textColor = new Color32(255, 226, 125, 255) },
            wordWrap = true
        };
        var detailScale = Math.Min(contentScale, 1.25f);
        panelDetailSectionStyle = new GUIStyle
        {
            normal = { textColor = new Color32(134, 217, 229, 255) },
            fontSize = Math.Max(12, (int)(16 * detailScale)),
            fontStyle = FontStyle.Bold
        };
        panelDetailValueStyle = new GUIStyle
        {
            normal = { textColor = new Color32(212, 226, 241, 255) },
            fontSize = Math.Max(13, (int)(17 * detailScale)),
            wordWrap = true
        };
        panelDetailSubtitleStyle = new GUIStyle
        {
            normal = { textColor = new Color32(145, 164, 190, 255) },
            fontSize = Math.Max(11, (int)(13 * detailScale))
        };
        panelDetailWrappedSubtitleStyle = new GUIStyle(panelDetailSubtitleStyle) { wordWrap = true };
        panelDetailNoteStyle = new GUIStyle(panelDetailValueStyle)
        {
            fontSize = Math.Max(13, (int)(16 * detailScale)),
            normal = { textColor = new Color32(255, 226, 125, 255) },
            wordWrap = true
        };
        panelDetailButtonStyle = new GUIStyle(panelButtonStyle)
        {
            fontSize = Math.Max(12, (int)(15 * detailScale))
        };
        panelDetailTextFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = Math.Max(13, (int)(16 * detailScale))
        };
        panelNoteHighlightStyle = new GUIStyle { normal = { background = panelNoteHighlightTexture } };
        panelArchiveListDetailStyle = new GUIStyle(panelValueStyle)
        {
            fontSize = Math.Max(12, (int)(14 * contentScale)),
            wordWrap = false,
            clipping = TextClipping.Clip
        };
        panelArchiveListNoteStyle = new GUIStyle(panelArchiveNoteStyle) { wordWrap = false, clipping = TextClipping.Clip };
        panelBadgeStyle = new GUIStyle(panelContentSubtitleStyle) { fontSize = Math.Max(10, (int)(12 * contentScale)), alignment = TextAnchor.UpperRight };
        panelTextFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = Math.Max(13, (int)(16 * contentScale)) };
        panelSmallButtonStyle = new GUIStyle(panelButtonStyle) { fontSize = Math.Max(12, (int)(15 * contentScale)) };
        panelHeaderButtonStyle = new GUIStyle(panelButtonStyle) { fontSize = Math.Max(12, (int)(13 * scale)) };
        panelToggleStyle = new GUIStyle(panelValueStyle) { fontSize = Math.Max(13, (int)(16 * contentScale)) };
        panelSidebarStyle = new GUIStyle(panelTabStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 0, 0),
            fontSize = Math.Max(17, (int)(18 * scale))
        };
        panelSidebarActiveStyle = new GUIStyle(panelTabActiveStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 0, 0),
            fontSize = Math.Max(17, (int)(18 * scale))
        };
        panelSidebarDisabledStyle = new GUIStyle(panelSidebarStyle) { normal = { background = panelButton, textColor = new Color32(110, 122, 138, 255) } };
        panelSurfaceStyle = new GUIStyle { normal = { background = panelSurface } };
        panelCardStyle = new GUIStyle { normal = { background = panelCard } };
        panelLineStyle = new GUIStyle { normal = { background = panelLine } };
        panelDotStyle = new GUIStyle { normal = { background = Texture2D.whiteTexture } };
        if (!PanelStylesAlive() || !PanelTexturesAlive()) throw new InvalidOperationException("面板样式未完整初始化");
        panelStyleGeneration.Complete(Screen.width, Screen.height, scale);
    }

    private bool PanelStylesAlive() => panelTitleStyle is not null && panelSubtitleStyle is not null &&
        panelContentSubtitleStyle is not null && panelWrappedSubtitleStyle is not null &&
        panelHeaderStatusStyle is not null && panelHeaderButtonStyle is not null &&
        panelSectionStyle is not null && panelLabelStyle is not null && panelValueStyle is not null &&
        panelStatusLabelStyle is not null && panelArchiveListDetailStyle is not null && panelArchiveListNoteStyle is not null &&
        panelButtonStyle is not null && panelButtonHover is not null && panelDisabledButtonStyle is not null &&
        panelActionButtonStyle is not null && panelActionButtonHover is not null && panelActionButtonDisabled is not null &&
        panelToggleStyle is not null && panelSurfaceStyle is not null && panelCardStyle is not null &&
        panelLineStyle is not null && panelDotStyle is not null && panelTabStyle is not null && panelTabActiveStyle is not null &&
        panelFilterStyle is not null && panelFilterActiveStyle is not null &&
        panelArchiveRowStyle is not null && panelArchiveSelectedRowStyle is not null && panelBadgeStyle is not null &&
        panelTextFieldStyle is not null && panelSmallButtonStyle is not null && panelArchiveLineStyle is not null &&
        panelArchiveNoteStyle is not null && panelDetailSectionStyle is not null &&
        panelDetailValueStyle is not null && panelDetailSubtitleStyle is not null &&
        panelDetailWrappedSubtitleStyle is not null && panelDetailNoteStyle is not null &&
        panelDetailButtonStyle is not null && panelDetailTextFieldStyle is not null &&
        panelNoteHighlightStyle is not null && panelSidebarStyle is not null &&
        panelSidebarActiveStyle is not null && panelSidebarDisabledStyle is not null;

    private bool PanelTexturesAlive() =>
        panelSurface != null && panelCard != null && panelLine != null && panelButton != null &&
        panelButtonHoverTexture != null && panelTabTexture != null && panelTabActiveTexture != null &&
        panelArchiveRowTexture != null && panelArchiveSelectedTexture != null && panelNoteHighlightTexture != null;

    private void DestroyPanelTextures()
    {
        if (panelSurface != null) UnityEngine.Object.Destroy(panelSurface);
        if (panelCard != null) UnityEngine.Object.Destroy(panelCard);
        if (panelLine != null) UnityEngine.Object.Destroy(panelLine);
        if (panelButton != null) UnityEngine.Object.Destroy(panelButton);
        if (panelButtonHoverTexture != null) UnityEngine.Object.Destroy(panelButtonHoverTexture);
        if (panelTabTexture != null) UnityEngine.Object.Destroy(panelTabTexture);
        if (panelTabActiveTexture != null) UnityEngine.Object.Destroy(panelTabActiveTexture);
        if (panelArchiveRowTexture != null) UnityEngine.Object.Destroy(panelArchiveRowTexture);
        if (panelArchiveSelectedTexture != null) UnityEngine.Object.Destroy(panelArchiveSelectedTexture);
        if (panelNoteHighlightTexture != null) UnityEngine.Object.Destroy(panelNoteHighlightTexture);
        panelSurface = null;
        panelCard = null;
        panelLine = null;
        panelButton = null;
        panelButtonHoverTexture = null;
        panelTabTexture = null;
        panelTabActiveTexture = null;
        panelArchiveRowTexture = null;
        panelArchiveSelectedTexture = null;
        panelNoteHighlightTexture = null;
    }

    private void DrawFutureWindow()
    {
        var baseSize = Math.Clamp((int)(Screen.height / 50f), 18, 28);
        futureStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
        connectorStyle ??= new GUIStyle
        {
            normal = { background = Texture2D.whiteTexture, textColor = new Color32(125, 138, 158, 255) },
            alignment = TextAnchor.MiddleCenter
        };
        futureTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            wordWrap = false,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
        futureStyles ??= new Dictionary<FuturePreviewKind, GUIStyle>
        {
            [FuturePreviewKind.Title] = futureTitleStyle,
            [FuturePreviewKind.Check] = Copy(futureStyle, new Color32(120, 210, 255, 255)),
            [FuturePreviewKind.Affection] = Copy(futureStyle, new Color32(255, 176, 205, 255)),
            [FuturePreviewKind.Gain] = Copy(futureStyle, new Color32(140, 235, 165, 255)),
            [FuturePreviewKind.Loss] = Copy(futureStyle, new Color32(255, 140, 120, 255)),
            [FuturePreviewKind.Equipment] = Copy(futureStyle, new Color32(195, 180, 255, 255)),
            [FuturePreviewKind.Party] = Copy(futureStyle, new Color32(120, 225, 255, 255)),
            [FuturePreviewKind.Progression] = Copy(futureStyle, new Color32(220, 190, 255, 255)),
            [FuturePreviewKind.State] = Copy(futureStyle, new Color32(205, 215, 225, 255)),
            [FuturePreviewKind.Unknown] = Copy(futureStyle, new Color32(255, 218, 110, 255)),
            [FuturePreviewKind.Boundary] = Copy(futureStyle, new Color32(175, 185, 200, 255))
        };
        var lines = Indicators.HoverHint.Split('\n');
        foreach (var style in futureStyles.Values) style.fontSize = baseSize;
        var presentation = lines.Select(FuturePreviewPresentation.Classify).ToArray();
        var width = Math.Clamp(Screen.width * 0.32f, 320, Math.Min(560, Screen.width - 24));
        var contentHeight = 0f;
        var rowHeights = new float[lines.Length];
        for (var index = 0; index < lines.Length; index++)
        {
            rowHeights[index] = Math.Max(futureStyles[presentation[index].Kind].CalcHeight(new GUIContent(lines[index]), width - 26), futureStyle.lineHeight);
            contentHeight += rowHeights[index];
        }
        var height = Math.Min(contentHeight + 28, (int)(Screen.height * 0.62f));
        var position = Event.current.mousePosition;
        var bounds = new Rect(position.x + 18, position.y + 16, width, height);
        if (bounds.xMax > Screen.width - 12) bounds.x = Math.Max(12, Screen.width - 12 - width);
        if (bounds.yMax > Screen.height - 12) bounds.y = Math.Max(12, position.y - 16 - height);
        var oldColor = GUI.color;
        GUI.color = new Color32(16, 18, 26, 234);
        GUI.Box(bounds, "");
        GUI.color = new Color32(79, 89, 110, 170);
        GUI.Box(new Rect(bounds.x, bounds.y, bounds.width, 2), "");
        GUI.Box(new Rect(bounds.x, bounds.y + bounds.height - 2, bounds.width, 2), "");
        GUI.color = oldColor;
        var rowTop = 0f;
        GUI.BeginGroup(new Rect(bounds.x + 11, bounds.y + 9, bounds.width - 22, bounds.height - 18));
        var branchLeft = 0f;
        var branchBottoms = new Dictionary<int, float>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("├─ ") || lines[index].StartsWith("└─ ")) branchLeft = futureStyle.CalcSize(new GUIContent("├─ ")).x;
            else if (branchBottoms.Count > 0 && !string.IsNullOrWhiteSpace(lines[index]) && lines[index].StartsWith("│  ")) { }
            else if (branchBottoms.Count > 0 && !string.IsNullOrWhiteSpace(lines[index])) branchBottoms.Clear();
            var line = new Rect(0, rowTop, bounds.width - 22, rowHeights[index]);
            rowTop += rowHeights[index];
            if (line.yMax > bounds.height - 18) break;
            if (lines[index].StartsWith("├─ ") || lines[index].StartsWith("└─ "))
            {
                GUI.Label(new Rect(0, line.y, branchLeft, line.height), lines[index][..2], futureStyles[presentation[index].Kind]);
                GUI.Label(new Rect(branchLeft, line.y, line.width - branchLeft, line.height), lines[index][3..], futureStyles[presentation[index].Kind]);
                if (lines[index].StartsWith("├─ ")) branchBottoms[index] = line.yMax;
            }
            else
            {
                var indent = lines[index].StartsWith("│  ") ? branchLeft : 0;
                GUI.Label(new Rect(indent, line.y, line.width - indent, line.height), lines[index].Replace("│  ", ""), futureStyles[presentation[index].Kind]);
            }
            if (branchBottoms.Count > 0 && rowTop >= branchBottoms.Values.Max() - 0.5f)
            {
                var oldLineColor = GUI.color;
                foreach (var branchStart in branchBottoms.Keys)
                {
                    var top = branchBottoms[branchStart] - rowHeights[branchStart];
                    GUI.Label(new Rect(branchLeft * 0.42f, top + 2, 1.5f, rowTop - top - 4), GUIContent.none, connectorStyle);
                }
                _ = oldLineColor;
            }
        }
        GUI.EndGroup();
    }

    private static GUIStyle Copy(GUIStyle style, Color32 color) => new(style) { normal = { textColor = color } };

    internal static void ClearStaleHover(StandardUIResponseButton? button)
    {
        if (button == null || button.isVisible) return;
        Indicators.HoveredButton = null;
        Indicators.HoverHint = "";
    }
}

[HarmonyPatch(typeof(KeyCodeManager), nameof(KeyCodeManager.KeyCodeQuickSave))]
internal static class NativeQuickSavePatch
{
    private static bool Prefix() => !Plugin.CheckpointsActive;
}

[HarmonyPatch(typeof(DialogueSystemController), nameof(DialogueSystemController.StartConversation),
    new[] { typeof(string), typeof(Transform), typeof(Transform), typeof(int) })]
internal static class ConversationStartPatch
{
    private static void Prefix(string __0)
    {
        try { Plugin.BeforeConversation(__0); }
        catch (Exception error)
        {
            Plugin.DialogueCheckpoint = null;
            Plugin.Logger.LogWarning("Pre-dialogue checkpoint skipped: " + error.Message);
        }
    }
}
