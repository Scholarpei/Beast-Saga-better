using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
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
    internal static ConfigEntry<KeyCode> SaveKey = null!;
    internal static ConfigEntry<KeyCode> LoadKey = null!;
    internal static ConfigEntry<KeyCode> PanelKey = null!;
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
    internal const string KnownFingerprint = "B12412BC21B0B88A07851D15235736AFCFCE48A22AB8D3F1CB3DFB8376360359";
    internal const string ProductionAuthorizationFile = "SAFE_DIALOGUE_PRODUCTION_SL_AUTHORIZED";
    internal static bool CheckpointsActive => SafetyPolicy.CanOperate(
        TestMode, EnableCheckpoints.Value, File.Exists(Path.Combine(GameRoot, ProductionAuthorizationFile)));
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
        SaveKey = Config.Bind("Keys", "QuickSave", KeyCode.F5, "快存；对话中选择对话前检查点。");
        LoadKey = Config.Bind("Keys", "QuickLoad", KeyCode.F6, "读取已明确选定的检查点。");
        PanelKey = Config.Bind("Keys", "Panel", KeyCode.Home, "显示或隐藏快速工具面板。");
        Fingerprint = ComputeFingerprint(GameRoot);
        if (Fingerprint != KnownFingerprint)
        {
            Log.LogError("游戏或 interop 版本已变化，插件未挂钩任何游戏方法。请重新验证版本。");
            return;
        }
        Store = new CheckpointStore(Path.Combine(DataRoot, "checkpoints"), Fingerprint);
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
            var generation = Capture();
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

    internal static void BeforeConversation(string title)
    {
        if (!CheckpointsActive || Busy || Loading || IsDialogue) return;
        DialogueCheckpoint = null;
        LastConversation = title;
        var reason = UnsafeReason(false);
        if (reason != null) { Logger.LogInfo("Checkpoint skipped: " + reason); return; }
        try
        {
            DialogueCheckpoint = Capture();
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
            LoadWork = Path.Combine(DataRoot, "load-work", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LoadWork);
            foreach (var file in Directory.GetFiles(source).Where(path => Path.GetFileName(path) != "manifest.json"))
                File.Copy(file, Path.Combine(LoadWork, Path.GetFileName(file)));
            NativeSave.Validate(LoadWork);
            Loading = true;
            LoadStarted = Time.realtimeSinceStartup;
            ObservedLoading = false;
            NativeLoadCompleted = false;
            if (Login._Instance == null || LoadMain._Instance == null) throw new InvalidOperationException("官方场景重载入口不可用");
            PendingNativeLoad = true;
            Login._Instance.LoadSaveDataInGame(1, 1);
            Tip("正在读取检查点");
        }
        catch (Exception error) { Fail("读取失败，已停用本次运行的存读档", error, true); }
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
        return new CommandPanelState.State(
            CheckpointsActive, IsDialogue, Busy, Loading, Faulted, EnableIndicators.Value, TestMode,
            DialogueCheckpoint, PanelSelectedCheckpoint, UnsafeReason(IsDialogue));
    }
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
    private GUIStyle? panelButtonHover;

    public RuntimeDriver(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try
        {
            Plugin.Tick();
            var panel = Input.GetKeyDown(Plugin.PanelKey.Value);
            var save = Plugin.CheckpointsActive && Input.GetKeyDown(Plugin.SaveKey.Value);
            var load = Plugin.CheckpointsActive && Input.GetKeyDown(Plugin.LoadKey.Value);
            if (!Application.isFocused) return;
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                var selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (selected != null && (selected.GetComponent<UnityEngine.UI.InputField>() != null || selected.GetComponent<TMPro.TMP_InputField>() != null)) return;
            }
            if (FairyGUI.Stage.inst != null && FairyGUI.Stage.inst.focus != null && FairyGUI.Stage.inst.focus.TryCast<FairyGUI.InputTextField>() != null) return;
            if (panel && !save && !load && Time.realtimeSinceStartup - Plugin.LastInput >= 0.35f)
            {
                Plugin.LastInput = Time.realtimeSinceStartup;
                CommandPanelState.ToggleHome(true, false);
                if (CommandPanelState.Open) Plugin.RefreshPanelSelectedCheckpoint();
                return;
            }
            if (!panel && ((save && !load) || (load && !save)) && Time.realtimeSinceStartup - Plugin.LastInput < 0.35f) return;
            if (panel || !save && !load) return;
            Plugin.LastInput = Time.realtimeSinceStartup;
            if (save && !load)
            {
                Plugin.SavePressed();
                if (CommandPanelState.Open) Plugin.RefreshPanelSelectedCheckpoint();
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
        EnsurePanelStyles();
        GUI.Box(bounds, GUIContent.none, panelSurfaceStyle!);
        GUI.Box(new Rect(bounds.x, bounds.y, bounds.width, 1), GUIContent.none, panelLineStyle!);
        GUI.Box(new Rect(bounds.x + 13, bounds.y + 10, bounds.width - 26, 74), GUIContent.none, panelCardStyle!);
        GUI.Box(new Rect(bounds.x + 20, bounds.y + 90, bounds.width - 40, 160), GUIContent.none, panelCardStyle!);
        GUI.Label(new Rect(bounds.x + 29, bounds.y + 24, 190, 26), "Safe Dialogue Tools", panelTitleStyle);
        GUI.Label(new Rect(bounds.x + 29, bounds.y + 52, 220, 20), "对话安全与预测工具", panelSubtitleStyle);
        DrawStatusDot(new Rect(bounds.xMax - 37, bounds.y + 26, 11, 11), state);
        GUI.Label(new Rect(bounds.x + 20, bounds.y + 96, 200, 20), "状态", panelSectionStyle);
        DrawInfoRow(new Rect(bounds.x + 20, bounds.y + 124, bounds.width - 40, 24), "运行模式", state.Mode, 70);
        DrawInfoRow(new Rect(bounds.x + 20, bounds.y + 152, bounds.width - 40, 24), "对话前", state.DialogueCheckpointText, 70);
        DrawInfoRow(new Rect(bounds.x + 20, bounds.y + 180, bounds.width - 40, 24), "读取目标", state.SelectedCheckpointText, 70);
        GUI.Label(new Rect(bounds.x + 20, bounds.y + 212, bounds.width - 40, 38), state.BlockedText, panelValueStyle);
        GUI.Label(new Rect(bounds.x + 20, bounds.y + 254, 200, 20), "快速操作", panelSectionStyle);
        var actionBounds = new Rect(bounds.x + 20, bounds.y + 282, bounds.width - 40, 42);
        var saveBounds = new Rect(actionBounds.x, actionBounds.y, actionBounds.width, 42);
        if (DrawActionButton(saveBounds, "快速保存（F5）", state.CanSave, panelButtonStyle!, panelButtonHover!, panelDisabledButtonStyle!))
        {
            CommandPanelState.RunSave(state, new CommandPanelState.Actions(Plugin.SavePressed, Plugin.LoadPressed));
            Plugin.RefreshPanelSelectedCheckpoint();
        }
        var loadBounds = new Rect(actionBounds.x, actionBounds.y + 50, actionBounds.width, 42);
        if (DrawActionButton(loadBounds, "读取检查点（F6）", state.CanLoad, panelButtonStyle!, panelButtonHover!, panelDisabledButtonStyle!))
            CommandPanelState.RunLoad(state, new CommandPanelState.Actions(Plugin.SavePressed, Plugin.LoadPressed));
        GUI.Label(new Rect(bounds.x + 20, bounds.y + 386, 200, 20), "辅助", panelSectionStyle);
        var toggleBounds = new Rect(bounds.x + 20, bounds.y + 414, bounds.width - 40, 36);
        GUI.Box(toggleBounds, GUIContent.none, panelCardStyle!);
        var enabled = GUI.Toggle(new Rect(toggleBounds.x + 14, toggleBounds.y + 7, toggleBounds.width - 28, 22),
            state.IndicatorsEnabled, "悬停显示判定与未来预览", panelToggleStyle);
        if (enabled != state.IndicatorsEnabled) Plugin.EnableIndicators.Value = enabled;
        GUI.Label(new Rect(bounds.x + 20, bounds.y + bounds.height - 34, bounds.width - 40, 20),
            $"{Plugin.PanelKey.Value} 收起 · {Plugin.SaveKey.Value} 快存 · {Plugin.LoadKey.Value} 读取", panelSubtitleStyle);
    }

    private void DrawInfoRow(Rect bounds, string label, string value, float labelWidth)
    {
        GUI.Label(new Rect(bounds.x, bounds.y, labelWidth, bounds.height), label, panelValueStyle);
        GUI.Label(new Rect(bounds.x + labelWidth, bounds.y, bounds.width - labelWidth, bounds.height), value, panelValueStyle);
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
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void EnsurePanelStyles()
    {
        if (panelTitleStyle != null) return;
        panelSurface = CreateTexture(new Color32(10, 15, 24, 236));
        panelCard = CreateTexture(new Color32(26, 36, 51, 72));
        panelLine = CreateTexture(new Color32(93, 182, 197, 120));
        panelButton = CreateTexture(new Color32(23, 34, 48, 210));
        var buttonHoverTexture = CreateTexture(new Color32(37, 56, 75, 235));
        panelTitleStyle = new GUIStyle { normal = { textColor = new Color32(235, 243, 255, 255) }, fontSize = 19, fontStyle = FontStyle.Bold };
        panelSubtitleStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = 12 };
        panelSectionStyle = new GUIStyle { normal = { textColor = new Color32(134, 217, 229, 255) }, fontSize = 12, fontStyle = FontStyle.Bold };
        panelLabelStyle = new GUIStyle { normal = { textColor = new Color32(145, 164, 190, 255) }, fontSize = 12 };
        panelValueStyle = new GUIStyle { normal = { textColor = new Color32(212, 226, 241, 255) }, fontSize = 13, wordWrap = true };
        panelButtonStyle = new GUIStyle(panelLabelStyle) { normal = { background = panelButton, textColor = new Color32(229, 241, 255, 255) }, alignment = TextAnchor.MiddleCenter, fontSize = 14 };
        panelButtonHover = new GUIStyle(panelButtonStyle) { normal = { background = buttonHoverTexture } };
        panelDisabledButtonStyle = new GUIStyle(panelButtonStyle) { normal = { background = panelButton, textColor = new Color32(110, 122, 138, 255) } };
        panelToggleStyle = new GUIStyle(panelValueStyle) { fontSize = 13 };
        panelSurfaceStyle = new GUIStyle { normal = { background = panelSurface } };
        panelCardStyle = new GUIStyle { normal = { background = panelCard } };
        panelLineStyle = new GUIStyle { normal = { background = panelLine } };
        panelDotStyle = new GUIStyle { normal = { background = Texture2D.whiteTexture } };
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
