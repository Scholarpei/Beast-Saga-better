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
    internal const string KnownFingerprint = "B12412BC21B0B88A07851D15235736AFCFCE48A22AB8D3F1CB3DFB8376360359";
    internal static bool CheckpointsActive => SafetyPolicy.CanOperate(TestMode, EnableCheckpoints.Value);
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

    public RuntimeDriver(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        try
        {
            Plugin.Tick();
            if (!Plugin.CheckpointsActive || !Application.isFocused) return;
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                var selected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (selected != null && (selected.GetComponent<UnityEngine.UI.InputField>() != null || selected.GetComponent<TMPro.TMP_InputField>() != null)) return;
            }
            if (FairyGUI.Stage.inst != null && FairyGUI.Stage.inst.focus != null && FairyGUI.Stage.inst.focus.TryCast<FairyGUI.InputTextField>() != null) return;
            var save = Input.GetKeyDown(Plugin.SaveKey.Value);
            var load = Input.GetKeyDown(Plugin.LoadKey.Value);
            if ((!save && !load) || Time.realtimeSinceStartup - Plugin.LastInput < 0.35f) return;
            Plugin.LastInput = Time.realtimeSinceStartup;
            if (save && !load) Plugin.SavePressed();
            else if (load && !save) Plugin.LoadPressed();
        }
        catch (Exception error) { Plugin.Fail("插件操作已中止", error, true); }
    }

    public void OnGUI()
    {
        Plugin.GuiFrames++;
        if (Time.realtimeSinceStartup <= Plugin.NoticeUntil && !string.IsNullOrEmpty(Plugin.Notice))
            DrawMessage(new Rect(Screen.width * 0.2f, 24, Screen.width * 0.6f, 46), Plugin.Notice);
        if (Plugin.EnableIndicators.Value && Plugin.IsDialogue && !string.IsNullOrEmpty(Indicators.SubtitleHint))
            DrawMessage(new Rect(Screen.width * 0.22f, Screen.height * 0.62f - 30, Screen.width * 0.56f, 42), Indicators.SubtitleHint);
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
