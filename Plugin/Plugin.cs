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
using SafeDialogueTools.Core;
using UnityEngine;

namespace SafeDialogueTools;

[BepInPlugin("beastsaga.safedialoguetools", "SafeDialogueTools", "0.1.0")]
public sealed class Plugin : BasePlugin
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

    internal static string PanelCloseHint => ClosePanelKey.Value == KeyCode.None || ClosePanelKey.Value == PanelKey.Value
        ? $"{PanelKey.Value} 收起"
        : $"{PanelKey.Value} / {ClosePanelKey.Value} 收起";

    private static bool previousDialogue;

    public override void Load()
    {
        Current = this;
        Logger = Log;
        GameRoot = Paths.GameRootPath;
        DataRoot = Path.Combine(GameRoot, "SafeDialogueToolsData");
        TestMode = File.Exists(Path.Combine(GameRoot, "SAFE_DIALOGUE_TEST_COPY")) &&
            Path.GetFileName(GameRoot).Equals("Game", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(Path.GetDirectoryName(GameRoot)), "sandbox", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(GameRoot, "SaveData", "seed.es3"));
        if (File.Exists(Path.Combine(GameRoot, "SAFE_DIALOGUE_TEST_COPY")) && !TestMode)
            throw new InvalidOperationException("Invalid isolated test copy layout");
        if (TestMode) Environment.CurrentDirectory = GameRoot;
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
        new Harmony("beastsaga.safedialoguetools").PatchAll(typeof(Plugin).Assembly);
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
            Store.Prune(DialogueCheckpoint);
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
            Store.Prune(DialogueCheckpoint);
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
        var dialogue = IsDialogue;
        if (previousDialogue && !dialogue) DialogueCheckpoint = null;
        previousDialogue = dialogue;
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

public sealed class RuntimeDriver : MonoBehaviour
{
    private GUIStyle? messageStyle;
    private GUIStyle? futureStyle;
    private GUIStyle? futureTitleStyle;
    private GUIStyle? connectorStyle;
    private Dictionary<FuturePreviewKind, GUIStyle>? futureStyles;
    private GUIStyle? panelTitleStyle;
    private GUIStyle? panelSubtitleStyle;
    private GUIStyle? panelSectionStyle;
    private GUIStyle? panelLabelStyle;
    private GUIStyle? panelValueStyle;
    private GUIStyle? panelButtonStyle;
    private GUIStyle? panelDisabledButtonStyle;
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
    private GUIStyle? panelArchiveRowStyle;
    private GUIStyle? panelArchiveSelectedRowStyle;
    private GUIStyle? panelBadgeStyle;
    private GUIStyle? panelTextFieldStyle;
    private GUIStyle? panelSmallButtonStyle;
    private GUIStyle? panelArchiveLineStyle;
    private GUIStyle? panelArchiveNoteStyle;
    private GUIStyle? panelNoteHighlightStyle;
    private Vector2 archiveScroll;
    private string? thumbnailGeneration;
    private Texture2D? thumbnailTexture;
    private IReadOnlyList<CheckpointSummary>? archiveView;
    private IReadOnlyList<CheckpointSummary>? archiveViewSource;
    private CheckpointArchiveSortMode archiveViewMode = (CheckpointArchiveSortMode)(-1);
    private CheckpointArchiveFilter archiveViewFilter = (CheckpointArchiveFilter)(-1);

    public RuntimeDriver(IntPtr pointer) : base(pointer) { }

    public void OnDestroy()
    {
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
        if (Time.realtimeSinceStartup <= Plugin.NoticeUntil && !string.IsNullOrEmpty(Plugin.Notice))
            DrawMessage(new Rect(Screen.width * 0.2f, 24, Screen.width * 0.6f, 46), Plugin.Notice);
        RuntimeDriver.ClearStaleHover(Indicators.HoveredButton);
        if (CommandPanelState.Open)
            DrawCommandPanel();
        if (Plugin.EnableIndicators.Value && Plugin.IsDialogue && Indicators.HoveredButton != null && !string.IsNullOrEmpty(Indicators.HoverHint))
            DrawFutureWindow();
        if (!CommandPanelState.Open) CommandPanelState.ClearPointerBounds();
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
        var header = new Rect(bounds.x + 14, bounds.y + 12, bounds.width - 28, 78 * metrics.Scale);
        GUI.Box(header, GUIContent.none, panelCardStyle!);
        GUI.Label(new Rect(header.x + 18, header.y + 13, 320, 30), "Safe Dialogue Tools", panelTitleStyle);
        GUI.Label(new Rect(header.x + 18, header.y + 46, 320, 22), "对话安全与预测工具", panelSubtitleStyle);
        DrawStatusDot(new Rect(header.xMax - 38, header.y + 16, 12, 12), state);
        if (GUI.Button(new Rect(header.xMax - 152, header.y + 10, 112, 30), "关闭面板", panelSmallButtonStyle!))
        {
            CommandPanelState.Open = false;
            Plugin.PanelNoteEditing = false;
        }
        var tabHeight = Math.Max(34f, 38f * metrics.Scale);
        var tabGap = 6f;
        var tabWidth = (bounds.width - 28 - tabGap * 2) / 3f;
        var statusTab = new Rect(bounds.x + 14, header.yMax + 12, tabWidth, tabHeight);
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
        var contentTop = statusTab.yMax + 16;
        GUI.Label(new Rect(bounds.x + 20, contentTop, 200, 24), "状态", panelSectionStyle);
        DrawInfoRow(new Rect(bounds.x + 20, contentTop + 30, bounds.width - 40, 30), "运行模式", state.Mode, 96);
        DrawInfoRow(new Rect(bounds.x + 20, contentTop + 66, bounds.width - 40, 30), "对话前", state.DialogueCheckpointText, 96);
        DrawInfoRow(new Rect(bounds.x + 20, contentTop + 102, bounds.width - 40, 30), "读取目标", state.SelectedCheckpointText, 96);
        GUI.Label(new Rect(bounds.x + 20, contentTop + 140, bounds.width - 40, 36), state.BlockedText, panelValueStyle);
        GUI.Label(new Rect(bounds.x + 20, contentTop + 196, 200, 24), "快速操作", panelSectionStyle);
        var actionBounds = new Rect(bounds.x + 20, contentTop + 228, bounds.width - 40, metrics.MajorButtonHeight);
        var saveBounds = new Rect(actionBounds.x, actionBounds.y, actionBounds.width, metrics.MajorButtonHeight);
        if (DrawActionButton(saveBounds, "快速保存（F5）", state.CanSave, panelButtonStyle!, panelButtonHover!, panelDisabledButtonStyle!))
        {
            CommandPanelState.RunSave(state, new CommandPanelState.Actions(Plugin.SavePressed, Plugin.LoadPressed));
            Plugin.RefreshPanelSelectedCheckpoint();
            Plugin.RefreshPanelArchive();
        }
        var loadBounds = new Rect(actionBounds.x, actionBounds.y + metrics.MajorButtonHeight + 12, actionBounds.width, metrics.MajorButtonHeight);
        if (DrawActionButton(loadBounds, "读取检查点（F6）", state.CanLoad, panelButtonStyle!, panelButtonHover!, panelDisabledButtonStyle!))
            CommandPanelState.RunLoad(state, new CommandPanelState.Actions(Plugin.SavePressed, Plugin.LoadPressed));
        GUI.Label(new Rect(bounds.x + 20, loadBounds.yMax + 34, 200, 24), "辅助", panelSectionStyle);
        var toggleBounds = new Rect(bounds.x + 20, loadBounds.yMax + 64, bounds.width - 40, Math.Max(40f, 44f * metrics.Scale));
        GUI.Box(toggleBounds, GUIContent.none, panelCardStyle!);
        var enabled = GUI.Toggle(new Rect(toggleBounds.x + 16, toggleBounds.y + 9, toggleBounds.width - 32, 26),
            state.IndicatorsEnabled, "悬停显示判定与未来预览", panelToggleStyle);
        if (enabled != state.IndicatorsEnabled) Plugin.EnableIndicators.Value = enabled;
        GUI.Label(new Rect(bounds.x + 20, bounds.y + bounds.height - 34, bounds.width - 40, 20),
            $"{Plugin.PanelCloseHint} · {Plugin.SaveKey.Value} 快存 · {Plugin.LoadKey.Value} 读取", panelSubtitleStyle);
    }

    private void DrawInfoRow(Rect bounds, string label, string value, float labelWidth)
    {
        GUI.Label(new Rect(bounds.x, bounds.y, labelWidth, bounds.height), label, panelValueStyle);
        GUI.Label(new Rect(bounds.x + labelWidth, bounds.y, bounds.width - labelWidth, bounds.height), value, panelValueStyle);
    }

    private void DrawCheatsTab(Rect bounds, CommandPanelState.State state)
    {
        var metrics = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height));
        var scale = metrics.Scale;
        var tabBottom = bounds.y + 12 + 78 * metrics.Scale + 12 + Math.Max(34f, 38f * metrics.Scale) + 16;
        var contentBounds = new Rect(bounds.x + 20, tabBottom, bounds.width - 40, bounds.y + bounds.height - 50 - tabBottom);
        GUI.Label(new Rect(contentBounds.x, contentBounds.y, 240, Math.Max(24f, 26f * scale)), "修改作弊", panelSectionStyle);
        var card = new Rect(contentBounds.x, contentBounds.y + Math.Max(30f, 34f * scale), contentBounds.width,
            Math.Max(94f, 104f * scale));
        GUI.Box(card, GUIContent.none, panelCardStyle!);
        var toggleBounds = new Rect(card.x + 18, card.y + 14, card.width - 36, Math.Max(28f, 32f * scale));
        var enabled = GUI.Toggle(toggleBounds, state.FishingInstantSuccess, "钓鱼直接成功", panelToggleStyle);
        if (enabled != state.FishingInstantSuccess) Plugin.EnableFishingInstantSuccess.Value = enabled;
        GUI.Label(new Rect(card.xMax - Math.Max(90f, 110f * scale) - 18, card.y + 15,
                Math.Max(90f, 110f * scale), 28),
            enabled ? "已开启" : "已关闭", panelBadgeStyle);
        GUI.Label(new Rect(card.x + 18, card.y + Math.Max(50f, 56f * scale), card.width - 36, Math.Max(30f, 36f * scale)),
            "钓鱼开始后进度条自动拉满，并立即结算成功。", panelSubtitleStyle);
        GUI.Label(new Rect(contentBounds.x, card.yMax + 22, contentBounds.width, 24),
            "更多修改功能将在此页陆续加入。", panelSubtitleStyle);
        GUI.Label(new Rect(bounds.x + 20, bounds.y + bounds.height - 34, bounds.width - 40, 20),
            $"{Plugin.PanelCloseHint} · 修改作弊", panelSubtitleStyle);
    }

    private void DrawArchiveTab(Rect bounds, CommandPanelState.State state)
    {
        var source = state.Archive ?? Array.Empty<CheckpointSummary>();
        if (!ReferenceEquals(source, archiveViewSource) ||
            archiveViewMode != PanelArchiveData.SortMode ||
            archiveViewFilter != PanelArchiveData.Filter)
        {
            archiveView = CheckpointArchiveView.Apply(source, PanelArchiveData.Filter, PanelArchiveData.SortMode);
            archiveViewSource = source;
            archiveViewMode = PanelArchiveData.SortMode;
            archiveViewFilter = PanelArchiveData.Filter;
        }
        var archive = archiveView!;
        var metrics = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height));
        var scale = metrics.Scale;
        var tabBottom = bounds.y + 12 + 78 * metrics.Scale + 12 + Math.Max(34f, 38f * metrics.Scale) + 16;
        var contentBounds = new Rect(bounds.x + 20, tabBottom, bounds.width - 40, bounds.y + bounds.height - 50 - tabBottom);
        var countHeight = Math.Max(24f, 26f * scale);
        var filterHeight = Math.Max(30f, 34f * scale);
        var sortWidth = Math.Max(118f, 126f * scale);
        GUI.Label(new Rect(contentBounds.x, contentBounds.y, contentBounds.width - 12, countHeight), state.ArchiveCountText, panelSubtitleStyle!);
        var filterY = contentBounds.y + countHeight + 2;
        var filterX = contentBounds.x;
        var filters = new[]
        {
            (CheckpointArchiveFilter.All, "全部", Math.Max(34f, 46f * metrics.Scale)),
            (CheckpointArchiveFilter.Manual, "手动", Math.Max(34f, 46f * metrics.Scale)),
            (CheckpointArchiveFilter.PreDialogue, "对话前", Math.Max(46f, 62f * metrics.Scale))
        };
        foreach (var (filter, label, width) in filters)
        {
            var selectedFilter = PanelArchiveData.Filter == filter;
            if (GUI.Button(new Rect(filterX, filterY, width, filterHeight), label,
                    selectedFilter ? panelTabActiveStyle! : panelTabStyle!))
            {
                PanelArchiveData.Filter = filter;
                archiveScroll = Vector2.zero;
            }
            filterX += width + 6;
        }
        var sortLabel = PanelArchiveData.SortMode == CheckpointArchiveSortMode.CapturedTime ? "按存档时间" : "按游戏时间";
        if (GUI.Button(new Rect(contentBounds.x + metrics.ArchiveListWidth - sortWidth, filterY, sortWidth, filterHeight),
                sortLabel, panelSmallButtonStyle!))
            PanelArchiveData.SortMode = PanelArchiveData.SortMode == CheckpointArchiveSortMode.CapturedTime
                ? CheckpointArchiveSortMode.GameTime
                : CheckpointArchiveSortMode.CapturedTime;
        var listTop = filterY + filterHeight + Math.Max(8f, 10f * scale);
        var listBounds = new Rect(contentBounds.x, listTop, metrics.ArchiveListWidth, contentBounds.yMax - listTop);
        archiveScroll = GUI.BeginScrollView(listBounds, archiveScroll,
            new Rect(0, 0, listBounds.width - 18, archive.Count * (metrics.ArchiveRowHeight + metrics.ArchiveRowSpacing)), false, true);
        for (var index = 0; index < archive.Count; index++)
        {
            var item = archive[index];
            var row = new Rect(1, index * (metrics.ArchiveRowHeight + metrics.ArchiveRowSpacing), listBounds.width - 20, metrics.ArchiveRowHeight);
            var selected = state.ArchiveSelection?.Generation == item.Generation;
            if (GUI.Button(row, GUIContent.none, selected ? panelArchiveSelectedRowStyle! : panelArchiveRowStyle!))
            {
                Plugin.PanelArchiveSelection = item.Generation;
                Plugin.PanelNoteDraft = item.Note ?? "";
                Plugin.PanelNoteEditing = false;
            }
            var badgeWidth = Math.Max(100f, 106f * scale);
            GUI.Label(new Rect(row.x + 14, row.y + 9, row.width - badgeWidth - 28, 28), item.DisplayTopLine(), panelArchiveLineStyle!);
            GUI.Label(new Rect(row.xMax - badgeWidth - 12, row.y + 10, badgeWidth, 28), item.BadgeText(), panelBadgeStyle);
            GUI.Label(new Rect(row.x + 14, row.y + 39, row.width - 28, 28), item.DisplayDetailLine(), panelValueStyle);
            GUI.Box(new Rect(row.x + 10, row.y + 66, row.width - 20, 28), GUIContent.none,
                string.IsNullOrWhiteSpace(item.Note) ? panelCardStyle! : panelNoteHighlightStyle!);
            GUI.Label(new Rect(row.x + 20, row.y + 68, row.width - 40, 24), item.NoteText(), panelArchiveNoteStyle!);
        }
        GUI.EndScrollView();

        var selection = state.ArchiveSelection;
        var detail = new Rect(listBounds.xMax + 16, contentBounds.y, contentBounds.xMax - listBounds.xMax - 16, contentBounds.height);
        GUI.Box(detail, GUIContent.none, panelCardStyle!);
        if (selection is null)
        {
            GUI.Label(new Rect(detail.x + 18, detail.y + 18, detail.width - 36, 30), "选择一份存档", panelValueStyle);
            GUI.Label(new Rect(detail.x + 18, detail.y + 54, detail.width - 36, detail.height - 72),
                "列表包含手动 F5 与对话前自动检查点；旧版检查点不显示。", panelSubtitleStyle);
        }
        else
        {
            var sectionLine = Math.Max(26f, 30f * scale);
            var valueLine = Math.Max(28f, 34f * scale);
            var smallLine = Math.Max(24f, 28f * scale);
            var buttonHeight = Math.Max(30f, 36f * scale);
            var editorHeight = Math.Max(32f, 40f * scale);
            var note = selection.NoteText();
            var noteHeight = (string.IsNullOrWhiteSpace(note) ? 34f : 52f) * Math.Max(1f, scale * 0.9f);
            var infoNeeded = sectionLine + 2 + valueLine + 2 + valueLine + 2 + valueLine + 2 +
                             smallLine + 2 + sectionLine + 2 +
                             (string.IsNullOrWhiteSpace(selection.Conversation) ? 0f : valueLine + 2) +
                             noteHeight + 8 + buttonHeight + 16;
            var preview = LoadThumbnail(selection);
            var previewHeight = selection.HasScreenshot
                ? Math.Max(120f, Math.Min(metrics.DetailPreviewHeight,
                    Math.Min((detail.width - 32f) * 9f / 16f, detail.height - 44f - infoNeeded)))
                : Math.Min(150f, Math.Max(90f, detail.height * 0.14f));
            var previewBounds = new Rect(detail.x + 16, detail.y + 16, detail.width - 32, previewHeight);
            if (preview != null) GUI.DrawTexture(previewBounds, preview, ScaleMode.ScaleToFit, false);
            else GUI.Label(previewBounds,
                selection.Kind == CheckpointKind.PreDialogue ? "对话前自动检查点\n无缩略截图" : "截图不可用",
                panelSubtitleStyle);
            var infoX = previewBounds.x;
            var infoWidth = previewBounds.width;
            var infoY = previewBounds.yMax + 12;
            GUI.Label(new Rect(infoX, infoY, infoWidth, sectionLine), selection.KindText(), panelSectionStyle);
            infoY += sectionLine + 2;
            if (!string.IsNullOrWhiteSpace(selection.Conversation))
            {
                GUI.Label(new Rect(infoX, infoY, infoWidth, valueLine), selection.Conversation, panelValueStyle);
                infoY += valueLine + 2;
            }
            GUI.Label(new Rect(infoX, infoY, infoWidth, valueLine), selection.DisplayFullTime(), panelArchiveLineStyle!);
            infoY += valueLine + 2;
            GUI.Label(new Rect(infoX, infoY, infoWidth, valueLine), selection.DisplayDetailLine(), panelValueStyle);
            infoY += valueLine + 2;
            GUI.Label(new Rect(infoX, infoY, infoWidth, smallLine),
                selection.Selected ? "当前读取目标" : "未设为读取目标", panelSubtitleStyle);
            infoY += smallLine + 2;
            GUI.Label(new Rect(infoX, infoY, infoWidth, sectionLine), "备注", panelSectionStyle);
            infoY += sectionLine + 2;
            if (!Plugin.PanelNoteEditing)
            {
                var noteBounds = new Rect(infoX, infoY, infoWidth, noteHeight);
                GUI.Box(noteBounds, GUIContent.none, panelNoteHighlightStyle!);
                GUI.Label(new Rect(infoX + 12, infoY + 5, infoWidth - 24, noteHeight - 10), note, panelArchiveNoteStyle!);
                var buttons = new Rect(infoX, infoY + noteHeight + 8, infoWidth, buttonHeight);
                if (GUI.Button(noteBounds, GUIContent.none, GUIStyle.none) || Event.current.type == EventType.MouseDown && noteBounds.Contains(Event.current.mousePosition))
                {
                    Plugin.PanelNoteDraft = selection.Note ?? "";
                    Plugin.PanelNoteEditing = true;
                    Event.current.Use();
                }
                if (GUI.Button(new Rect(buttons.x + 128 * scale, buttons.y, Math.Max(148f, 160f * scale), buttonHeight),
                        "设为读取目标", panelSmallButtonStyle!)) Plugin.SelectArchive(selection.Generation);
            }
            else
            {
                var editor = new Rect(infoX, infoY + 2, infoWidth, editorHeight);
                Plugin.PanelNoteDraft = GUI.TextField(editor, Plugin.PanelNoteDraft, panelTextFieldStyle!);
                var noteButtonWidth = Math.Max(58f, 64f * scale);
                var saveNote = GUI.Button(new Rect(editor.xMax - noteButtonWidth * 2 - 8, editor.yMax + 6, noteButtonWidth, buttonHeight),
                                   "保存", panelSmallButtonStyle!) ||
                               (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter));
                if (saveNote)
                {
                    Plugin.SaveArchiveNote(selection.Generation, Plugin.PanelNoteDraft);
                    Plugin.PanelNoteEditing = false;
                    if (Event.current.type == EventType.KeyDown) Event.current.Use();
                }
                if (GUI.Button(new Rect(editor.xMax - noteButtonWidth, editor.yMax + 6, noteButtonWidth, buttonHeight),
                        "取消", panelSmallButtonStyle!))
                {
                    Plugin.PanelNoteDraft = selection.Note ?? "";
                    Plugin.PanelNoteEditing = false;
                }
            }
        }
        GUI.Label(new Rect(bounds.x + 20, bounds.y + bounds.height - 34, bounds.width - 40, 20),
            $"{Plugin.PanelCloseHint} · {Plugin.LoadKey.Value} 读取当前目标", panelSubtitleStyle);
    }

    private bool DrawActionButton(Rect bounds, string text, bool enabled, GUIStyle normal, GUIStyle hover, GUIStyle disabled)
    {
        var style = !enabled ? disabled : bounds.Contains(Event.current.mousePosition) ? hover : normal;
        return enabled && GUI.Button(bounds, text, style);
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
        if (panelTitleStyle != null && PanelTexturesAlive()) return;
        DestroyPanelTextures();
        panelTitleStyle = null;
        panelSubtitleStyle = null;
        panelSectionStyle = null;
        panelLabelStyle = null;
        panelValueStyle = null;
        panelButtonStyle = null;
        panelButtonHover = null;
        panelDisabledButtonStyle = null;
        panelTabStyle = null;
        panelTabActiveStyle = null;
        panelArchiveRowStyle = null;
        panelArchiveSelectedRowStyle = null;
        panelArchiveLineStyle = null;
        panelArchiveNoteStyle = null;
        panelNoteHighlightStyle = null;
        panelBadgeStyle = null;
        panelTextFieldStyle = null;
        panelSmallButtonStyle = null;
        panelToggleStyle = null;
        panelSurfaceStyle = null;
        panelCardStyle = null;
        panelLineStyle = null;
        panelDotStyle = null;
        panelSurface = CreateTexture(new Color32(10, 15, 24, 236));
        panelCard = CreateTexture(new Color32(26, 36, 51, 72));
        panelLine = CreateTexture(new Color32(93, 182, 197, 120));
        panelButton = CreateTexture(new Color32(23, 34, 48, 210));
        panelButtonHoverTexture = CreateTexture(new Color32(37, 56, 75, 235));
        panelTabTexture = CreateTexture(new Color32(20, 29, 42, 190));
        panelTabActiveTexture = CreateTexture(new Color32(35, 64, 76, 235));
        panelArchiveRowTexture = CreateTexture(new Color32(20, 29, 42, 180));
        panelArchiveSelectedTexture = CreateTexture(new Color32(38, 68, 81, 230));
        panelNoteHighlightTexture = CreateTexture(new Color32(64, 51, 16, 230));
        var scale = CommandPanelMetrics.Create(CommandPanelLayout.Create(Screen.width, Screen.height)).Scale;
        panelTitleStyle = new GUIStyle { normal = { textColor = new Color32(235, 243, 255, 255) }, fontSize = Math.Max(20, (int)(25 * scale)), fontStyle = FontStyle.Bold };
        panelSubtitleStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = Math.Max(13, (int)(15 * scale)) };
        panelSectionStyle = new GUIStyle { normal = { textColor = new Color32(134, 217, 229, 255) }, fontSize = Math.Max(14, (int)(16 * scale)), fontStyle = FontStyle.Bold };
        panelLabelStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = Math.Max(13, (int)(15 * scale)) };
        panelValueStyle = new GUIStyle { normal = { textColor = new Color32(212, 226, 241, 255) }, fontSize = Math.Max(15, (int)(18 * scale)), wordWrap = true };
        panelButtonStyle = new GUIStyle(panelLabelStyle) { normal = { background = panelButton, textColor = new Color32(229, 241, 255, 255) }, alignment = TextAnchor.MiddleCenter, fontSize = Math.Max(15, (int)(18 * scale)) };
        panelButtonHover = new GUIStyle(panelButtonStyle) { normal = { background = panelButtonHoverTexture } };
        panelDisabledButtonStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelButton, textColor = new Color32(110, 122, 138, 255) } };
        panelTabStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelTabTexture, textColor = new Color32(181, 195, 212, 255) } };
        panelTabActiveStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelTabActiveTexture, textColor = new Color32(154, 235, 244, 255) } };
        panelArchiveRowStyle = new GUIStyle { normal = { background = panelArchiveRowTexture } };
        panelArchiveSelectedRowStyle = new GUIStyle { normal = { background = panelArchiveSelectedTexture } };
        panelArchiveLineStyle = new GUIStyle(panelValueStyle) { fontSize = Math.Max(16, (int)(19 * scale)), wordWrap = false };
        panelArchiveNoteStyle = new GUIStyle(panelValueStyle)
        {
            fontSize = Math.Max(16, (int)(18 * scale)),
            normal = { textColor = new Color32(255, 226, 125, 255) },
            wordWrap = true
        };
        panelNoteHighlightStyle = new GUIStyle { normal = { background = panelNoteHighlightTexture } };
        panelBadgeStyle = new GUIStyle(panelSubtitleStyle) { fontSize = Math.Max(12, (int)(14 * scale)), alignment = TextAnchor.UpperRight };
        panelTextFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = Math.Max(15, (int)(18 * scale)) };
        panelSmallButtonStyle = new GUIStyle(panelButtonStyle) { fontSize = Math.Max(14, (int)(16 * scale)) };
        panelToggleStyle = new GUIStyle(panelValueStyle) { fontSize = Math.Max(14, (int)(17 * scale)) };
        panelSurfaceStyle = new GUIStyle { normal = { background = panelSurface } };
        panelCardStyle = new GUIStyle { normal = { background = panelCard } };
        panelLineStyle = new GUIStyle { normal = { background = panelLine } };
        panelDotStyle = new GUIStyle { normal = { background = Texture2D.whiteTexture } };
    }

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
