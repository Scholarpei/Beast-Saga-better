using System.Text.Json;
using PixelCrushers.DialogueSystem;
using SafeDialogueTools.Core;
using UnityEngine;

namespace SafeDialogueTools;

internal static class TestBridge
{
    private static float lastPoll;
    internal static string CoroutineStatus = "";

    internal static void Tick()
    {
        if (!Plugin.TestMode) return;
        if (Time.realtimeSinceStartup - lastPoll < 0.5f) return;
        lastPoll = Time.realtimeSinceStartup;
        var commandPath = Path.Combine(Plugin.GameRoot, "test-command.txt");
        if (!File.Exists(commandPath)) return;
        var command = File.ReadAllText(commandPath).Trim();
        File.Delete(commandPath);
        try
        {
            if (command == "state") Snapshot();
            else if (command == "menu-continue")
            {
                var login = LJG.Framework.Main.m_UI.GetOpenedUI<LoginUILogicResident>();
                if (login == null) throw new InvalidOperationException("登录界面尚未打开");
                login.OnContinueGame();
            }
            else if (command == "save") Plugin.SavePressed();
            else if (command == "load") Plugin.LoadPressed();
            else if (command == "enable") Plugin.EnableCheckpoints.Value = true;
            else if (command == "panel")
            {
                Plugin.TogglePanel();
            }
            else if (command.StartsWith("panel-tab|"))
            {
                if (!Enum.TryParse<CommandPanelTab>(command[10..], true, out var tab))
                    throw new InvalidOperationException("Invalid panel tab");
                CommandPanelState.SelectTab(tab);
            }
            else if (command.StartsWith("cheat-section|"))
            {
                if (!Enum.TryParse<CheatSection>(command[14..], true, out var section))
                    throw new InvalidOperationException("Invalid cheat section");
                CommandPanelState.SelectCheatSection(section);
            }
            else if (command.StartsWith("note:"))
            {
                var payload = command[5..];
                var separator = payload.IndexOf('|');
                var parts = separator >= 0
                    ? new[] { payload[..separator], payload[(separator + 1)..] }
                    : payload.Split(':', 2);
                if (parts.Length != 2) throw new InvalidOperationException("Invalid note command");
                Plugin.SaveArchiveNote(parts[0], parts[1]);
            }
            else if (command.StartsWith("select-checkpoint:")) Plugin.SelectArchive(command[18..]);
            else if (command.StartsWith("native-load-quick:"))
            {
                if (!int.TryParse(command.AsSpan(18), out var quickIndex)) throw new InvalidOperationException("Invalid quick save index");
                SaveManager._Instance.SelectQuickGame(quickIndex);
            }
            else if (command == "load-seed")
            {
                var manager = SaveManager._Instance;
                OpenUIManager._Inst.OpenLoading(2, Array.Empty<Il2CppSystem.Object>());
                manager.fileName = Path.Combine(Plugin.GameRoot, "SaveData", "seed.es3");
                manager.fileUtilName = Path.Combine(Plugin.GameRoot, "SaveData", "seed-util.es3");
                var info = new ES3File(manager.fileName).Load<SaveInfo>(SaveManager.SaveInfoData);
                manager.LoadGameSave(1, SaveType.Quick, info._Dlc);
            }
            else if (command.StartsWith("talk:")) DialogueManager.StartConversation(command[5..]);
            else if (command.StartsWith("talk-entry:"))
            {
                var parts = command.Split(':');
                var conversation = DialogueManager.masterDatabase.GetConversation(int.Parse(parts[1]));
                DialogueManager.instance.StartConversation(conversation.Title, null, null, int.Parse(parts[2]));
            }
            else if (command == "test-previews") PreviewTests(false);
            else if (command == "test-previews-stale-party") PreviewTests(true);
            else if (command == "freeze") Time.timeScale = 0;
            else if (command == "unfreeze") Time.timeScale = 1;
            else if (command == "native-baseline-load")
            {
                var generation = Plugin.Store.Selected() ?? throw new InvalidOperationException("No checkpoint");
                var source = Plugin.Store.Validate(generation);
                var manager = SaveManager._Instance;
                var path = Path.Combine(manager.Path, "quick1.es3");
                File.Copy(Path.Combine(source, "save.es3"), path, true);
                var info = NativeSave.ReadInfo(source);
                manager.GetQuickSaveInfo()[1] = info;
                manager.SaveDataInfo();
                Login._Instance.LoadSaveDataInGame(1, 1);
            }
            else if (command == "dialogue-continue")
            {
                DialogueManager.conversationView.HandleContinueButtonClick();
            }
            else if (command == "quit") Application.Quit();
            else if (command == "dump-db") DumpDatabase();
            else if (command == "capture") ScreenCapture.CaptureScreenshot(Path.Combine(Plugin.GameRoot, "test-frame.png"));
            else if (command == "cheat-state") WriteCheatState();
            else if (command == "cheat-probe") WriteCheatProbe();
            else if (command.StartsWith("cheat-set-stat|")) RunCheatSetStat(command);
            else if (command.StartsWith("cheat-set-martial|")) RunCheatSetMartial(command);
            else if (command.StartsWith("cheat-set-life|")) RunCheatSetLife(command);
            else if (command.StartsWith("cheat-add-favor|")) RunCheatAddFavor(command);
            else if (command.StartsWith("cheat-add-exp|")) RunCheatAddExp(command);
            else if (command.StartsWith("cheat-add-points|")) RunCheatAddPoints(command);
            else if (command.StartsWith("cheat-set-points|")) RunCheatSetPoints(command);
            else if (command.StartsWith("cheat-speed|")) RunCheatSpeed(command);
            else if (command.StartsWith("snapshot:"))
            {
                var name = command[9..];
                if (name is not ("before" or "after")) throw new InvalidOperationException("Invalid snapshot name");
                var folder = Path.Combine(Plugin.DataRoot, "test-snapshots", name + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                NativeSave.Write(folder);
                NativeSave.Validate(folder);
                File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-snapshot-" + name + ".txt"), folder);
            }
            else if (command == "offsets")
            {
                foreach (var field in typeof(MapManager).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
                    if (field.Name.StartsWith("NativeFieldInfoPtr_"))
                    {
                        var pointer = (IntPtr)field.GetValue(null)!;
                        Plugin.Logger.LogInfo("FIELD " + field.Name + " offset=" + Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(pointer));
                    }
            }
            File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-result.txt"), "OK " + command);
        }
        catch (Exception error)
        {
            Plugin.Logger.LogError(error);
            File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-result.txt"), error.ToString());
        }
    }

    private static CheatCharacterOption FindCheatCharacter(string key) =>
        CheatEditor.Characters().FirstOrDefault(item => item.Key == key)
        ?? throw new InvalidOperationException("Unknown cheat character: " + key);

    private static string[] CheatParts(string command, int count)
    {
        var parts = command.Split('|');
        if (parts.Length != count + 1) throw new InvalidOperationException("Invalid cheat command: " + command);
        return parts;
    }

    private static void WriteCheatResult(bool ok, string message)
    {
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-cheat-result.json"),
            JsonSerializer.Serialize(new { ok, message }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RunCheatSetStat(string command)
    {
        var parts = CheatParts(command, 3);
        if (!int.TryParse(parts[2], out var type) || !int.TryParse(parts[3], out var value))
            throw new InvalidOperationException("Invalid stat command");
        var ok = CheatEditor.SetStat(FindCheatCharacter(parts[1]), (PlayerProType)type, value, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatSetMartial(string command)
    {
        var parts = CheatParts(command, 3);
        if (!int.TryParse(parts[2], out var type) || !int.TryParse(parts[3], out var level))
            throw new InvalidOperationException("Invalid martial command");
        var ok = CheatEditor.SetMartial(FindCheatCharacter(parts[1]), (KFType)type, level, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatSetLife(string command)
    {
        var parts = CheatParts(command, 3);
        if (!int.TryParse(parts[2], out var type) || !int.TryParse(parts[3], out var level))
            throw new InvalidOperationException("Invalid life command");
        var ok = CheatEditor.SetLife(FindCheatCharacter(parts[1]), (LifeType)type, level, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatAddFavor(string command)
    {
        var parts = CheatParts(command, 2);
        if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var delta))
            throw new InvalidOperationException("Invalid favor command");
        var ok = CheatEditor.AddFavor(FindCheatCharacter(parts[1]), delta, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatAddExp(string command)
    {
        var parts = CheatParts(command, 1);
        if (!int.TryParse(parts[1], out var amount)) throw new InvalidOperationException("Invalid exp command");
        var ok = CheatEditor.AddPlayerExp(amount, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatAddPoints(string command)
    {
        var parts = CheatParts(command, 2);
        if (!Enum.TryParse<CheatPointPool>(parts[1], true, out var pool) || !int.TryParse(parts[2], out var delta))
            throw new InvalidOperationException("Invalid points command");
        var ok = CheatEditor.AddPoints(pool, delta, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatSetPoints(string command)
    {
        var parts = CheatParts(command, 2);
        if (!Enum.TryParse<CheatPointPool>(parts[1], true, out var pool) || !int.TryParse(parts[2], out var available))
            throw new InvalidOperationException("Invalid set-points command");
        var ok = CheatEditor.SetAvailablePoints(pool, available, out var message);
        WriteCheatResult(ok, message);
    }

    private static void RunCheatSpeed(string command)
    {
        var parts = CheatParts(command, 1);
        if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var multiplier))
            throw new InvalidOperationException("Invalid speed command");
        var ok = CheatEditor.SetMovementSpeed(multiplier, out var message);
        WriteCheatResult(ok, message);
    }

    private static void WriteCheatState()
    {
        var characters = CheatEditor.Characters().Select(option =>
        {
            var snapshot = CheatEditor.Snapshot(option);
            return new
            {
                option.Key,
                option.Name,
                Kind = option.Kind.ToString(),
                CanEditStats = option.CanEditStats,
                snapshot.Level,
                snapshot.Exp,
                snapshot.HasFavor,
                snapshot.Favor,
                Stats = CheatEditor.AttributeTypes.Select((type, index) => new
                {
                    type.Value,
                    type.Label,
                    Base = snapshot.BaseStats[index],
                    Final = snapshot.FinalStats[index]
                }).ToArray(),
                Martial = CheatEditor.MartialTypes.Select((type, index) => new
                {
                    type.Value,
                    type.Label,
                    Level = snapshot.MartialLevels[index]
                }).ToArray(),
                Life = CheatEditor.LifeTypes.Select((type, index) => new
                {
                    type.Value,
                    type.Label,
                    Level = snapshot.LifeLevels[index]
                }).ToArray()
            };
        }).ToArray();
        var points = CheatEditor.Points();
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-cheat-state.json"),
            JsonSerializer.Serialize(new
            {
                BlockReason = CheatEditor.EditBlockReason(),
                Characters = characters,
                Points = points,
                Speed = CheatEditor.MovementSpeed(),
                FishingInstantSuccess = Plugin.EnableFishingInstantSuccess.Value
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteCheatProbe()
    {
        var manager = PlayerDataManager._Instance;
        var player = manager?._PlayerData;
        var probes = player == null
            ? Array.Empty<object>()
            : Enumerable.Range(1, 8).Select(type => new
            {
                Type = type,
                Pro = manager!.GetPro((PlayerProType)type)
            }).Cast<object>().ToArray();
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-cheat-probe.json"),
            JsonSerializer.Serialize(new
            {
                Player = player == null ? null : new
                {
                    player.Power,
                    player.Perception,
                    player.Agility,
                    player.PhysicalPower,
                    player.Channel,
                    player.BerathSkill,
                    player.WuXing
                },
                Probes = probes
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Snapshot()
    {
        var manager = SaveManager._Instance;
        var player = PlayerDataManager._Instance;
        var map = MapManager._Instance;
        var battle = BattleManager._Instance;
        var values = new Dictionary<string, object?>
        {
            ["dialogue"] = Plugin.IsDialogue, ["guard"] = Plugin.UnsafeReason(false),
            ["isLogin"] = manager?.IsLogin, ["loadOver"] = manager?._IsLoadOver,
            ["canGame"] = manager?._IsCanGame, ["canMove"] = manager?._PlayerCanMove,
            ["saving"] = manager?.IsSaveIng, ["dlc"] = manager?._CurrentDlcIndex,
            ["nativePath"] = manager?.Path, ["file"] = manager?.fileName,
            ["utilPath"] = SaveManager.SaveTempUtilDataPath, ["mapInit"] = map?._IsInitOver,
            ["mapClean"] = map?.IsClean, ["mapTeleport"] = map?._ChuangSongMap,
            ["mapTeleporting"] = map?._ChuanSongIng, ["battle"] = battle?._IsBattle,
            ["level"] = player?._PlayerData?.Lv, ["notice"] = Plugin.Notice,
            ["checkpoint"] = Plugin.DialogueCheckpoint, ["loading"] = Plugin.Loading,
            ["faulted"] = Plugin.Faulted, ["selected"] = Plugin.Store?.Selected(),
            ["archiveCount"] = Plugin.PanelArchive?.Count,
            ["panelOpen"] = CommandPanelState.Open,
            ["conversation"] = DialogueManager.hasInstance ? DialogueManager.lastConversationID : null
        };
        values["guiFrames"] = Plugin.GuiFrames;
        if (Plugin.IsDialogue)
        {
            var subtitle = DialogueManager.conversationController.currentState?.subtitle;
            values["entry"] = subtitle?.dialogueEntry?.id;
            values["text"] = subtitle?.formattedText?.text;
            values["hoverHint"] = Indicators.HoverHint;
            values["sequencerPlaying"] = DialogueManager.conversationView?.sequencer?.isPlaying;
        }
        values["coroutine"] = CoroutineStatus;
        if (player?._PlayerData != null)
        {
            var experience = new Dictionary<string, int>();
            if (player._PlayerData.LifeExp != null)
                foreach (var pair in player._PlayerData.LifeExp) experience[((int)pair.Key).ToString()] = pair.Value;
            values["lifeExp"] = experience;
            values["position"] = manager?._Player?.transform.position.ToString();
        }
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-state.json"), JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DumpDatabase()
    {
        var database = DialogueManager.masterDatabase;
        var conversations = new List<object>();
        foreach (var conversation in database.conversations)
        {
            var entries = new List<object>();
            foreach (var entry in conversation.dialogueEntries)
            {
                var links = new List<object>();
                foreach (var link in entry.outgoingLinks) links.Add(new { link.destinationConversationID, link.destinationDialogueID });
                entries.Add(new { entry.id, entry.conversationID, entry.DialogueText, entry.MenuText, entry.conditionsString, entry.userScript, entry.Sequence, links });
            }
            conversations.Add(new { conversation.id, conversation.Title, entries });
        }
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-db.json"), JsonSerializer.Serialize(conversations));
    }

    private static void PreviewTests(bool staleParty)
    {
        var party = PlayerFriendDataManager._Instance._FriendData.BattleFriend;
        var sentinel = "SafeDialogueToolsMissingFriend_" + Guid.NewGuid().ToString("N");
        if (staleParty) party.Add(sentinel);
        try
        {
        var folder = Path.Combine(Plugin.DataRoot, "preview-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var before = Path.Combine(folder, "before");
        var after = Path.Combine(folder, "after");
        Directory.CreateDirectory(before);
        Directory.CreateDirectory(after);
        NativeSave.Write(before);
        var randomState = UnityEngine.Random.state;
        var samples = new List<object>();
        foreach (var conversation in DialogueManager.masterDatabase.conversations)
        {
            foreach (var entry in conversation.dialogueEntries)
            {
                if (samples.Count >= 256) break;
                if (!entry.conditionsString.Contains("IsPlayer", StringComparison.Ordinal)) continue;
                var hint = Indicators.Hint(entry);
                samples.Add(new { conversation.id, entry = entry.id, entry.conditionsString, hint });
            }
            if (samples.Count >= 256) break;
        }
        var randomUnchanged = randomState.Equals(UnityEngine.Random.state);
        NativeSave.Write(after);
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-snapshot-before.txt"), before);
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-snapshot-after.txt"), after);
        var stalePartyUnchanged = !staleParty || party.Contains(sentinel);
        File.WriteAllText(Path.Combine(Plugin.GameRoot, "test-previews.json"), JsonSerializer.Serialize(new { randomUnchanged, stalePartyUnchanged, samples }));
        }
        finally { if (staleParty) party.Remove(sentinel); }
    }
}

[HarmonyLib.HarmonyPatch(typeof(MapManager._LoadMapSaveData_d__90), "MoveNext")]
internal static class MapDiagnosticPatch
{
    private static int previous = int.MinValue;
    private static void Postfix(MapManager._LoadMapSaveData_d__90 __instance)
    {
        if (!Plugin.TestMode || previous == __instance.__1__state) return;
        previous = __instance.__1__state;
        Plugin.Logger.LogInfo("Native map load state=" + previous + " yield=" + __instance.__2__current?.GetIl2CppType().FullName);
    }
}

[HarmonyLib.HarmonyPatch(typeof(SaveManager._LoadSaveGame_d__111), "MoveNext")]
internal static class LoadDiagnosticPatch
{
    private static void Postfix(SaveManager._LoadSaveGame_d__111 __instance, bool __result)
    {
        if (!__result && Plugin.Loading && !Plugin.PendingNativeLoad &&
            __instance.__4__this.fileName == Path.Combine(Plugin.LoadWork, "save.es3")) Plugin.NativeLoadCompleted = true;
        if (!Plugin.TestMode) return;
        var status = "state=" + __instance.__1__state + " yield=" + __instance.__2__current?.GetIl2CppType().FullName;
        if (status == TestBridge.CoroutineStatus) return;
        TestBridge.CoroutineStatus = status;
        Plugin.Logger.LogInfo("Native load " + status);
    }
}
