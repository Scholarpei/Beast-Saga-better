using System.Text.Json;
using BeastSagaBetter.Core;

namespace BeastSagaBetter.Tests;

internal static class CheckpointHistoryTests
{
    public static (int Checks, int Failures) Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "BeastSagaBetter-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workingRoot = Path.Combine(root, "working");
            var store = new CheckpointStore(workingRoot, "v1", maxManualCheckpoints: 6, maxPreDialogueCheckpoints: 3);
            string Create(CheckpointKind kind, string map = "禾兴城", int level = 12, int round = 1, bool screenshot = true, int day = 1,
                string? conversation = null)
            {
                return store.Create(
                    path =>
                    {
                        File.WriteAllText(Path.Combine(path, "save.es3"), "full");
                        if (screenshot) File.WriteAllText(Path.Combine(path, "thumbnail.png"), "png");
                    },
                    _ => { },
                    new CheckpointMetadata(kind, DateTime.Now, map, level, round, day, screenshot ? "thumbnail.png" : null, conversation));
            }

            Test("v2 checkpoints retain capture metadata and thumbnail integrity", () =>
            {
                var integrityRoot = Path.Combine(root, "integrity");
                var integrityStore = new CheckpointStore(integrityRoot, "v1");
                var generation = integrityStore.Create(
                    path =>
                    {
                        File.WriteAllText(Path.Combine(path, "save.es3"), "full");
                        File.WriteAllText(Path.Combine(path, "thumbnail.png"), "png");
                    },
                    _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "竹林", 18, 2, 9, "thumbnail.png"));
                var record = integrityStore.List().Single(item => item.Generation == generation);
                Require(record.Kind == CheckpointKind.Manual && record.MapName == "竹林" && record.PlayerLevel == 18 && record.Round == 2);
                Require(record.ScreenshotPath is not null && File.Exists(record.ScreenshotPath));
                File.AppendAllText(record.ScreenshotPath!, "tampered");
                Reject(() => integrityStore.Validate(generation));
            });

            Test("notes are atomic, bounded, optional and reject corruption", () =>
            {
                var notesRoot = Path.Combine(root, "notes");
                var notesStore = new CheckpointStore(notesRoot, "v1");
                var generation = notesStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "禾兴城", 12, 1, 1, null));
                notesStore.SetNote(generation, new string('备', 600));
                Require(notesStore.Notes()[generation].Length == 500, notesStore.Notes()[generation].Length.ToString());
                notesStore.SetNote(generation, "  boss 战前  ");
                Require(notesStore.Notes()[generation] == "boss 战前");
                var notesPath = Path.Combine(notesRoot, "notes.json");
                File.WriteAllText(notesPath, "{broken");
                Reject(() => notesStore.Notes());
                Reject(() => notesStore.SetNote(generation, "重建备注"));
                Require(File.ReadAllText(notesPath) == "{broken");
            });

            Test("manual quotas below the safety floor are rejected before creating storage", () =>
            {
                var unsafeRoot = Path.Combine(root, "unsafe-manual-quota");
                Reject(() => new CheckpointStore(unsafeRoot, "v1", maxManualCheckpoints: 4));
                Require(!Directory.Exists(unsafeRoot));
                var minimalDialogueRoot = Path.Combine(root, "minimal-dialogue-quota");
                new CheckpointStore(minimalDialogueRoot, "v1", maxPreDialogueCheckpoints: 1);
                Require(Directory.Exists(minimalDialogueRoot));
            });

            Test("archive summaries can skip payload hashing while strict listing rejects tampering", () =>
            {
                var archiveRoot = Path.Combine(root, "archive");
                var archiveStore = new CheckpointStore(archiveRoot, "v1");
                var generation = archiveStore.Create(
                    path =>
                    {
                        File.WriteAllText(Path.Combine(path, "save.es3"), "full");
                        File.WriteAllText(Path.Combine(path, "thumbnail.png"), "png");
                    },
                    _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "轻量列表", 22, 3, 11, "thumbnail.png"));
                File.AppendAllText(Path.Combine(archiveRoot, generation, "save.es3"), "tampered");
                var cheapRecord = archiveStore.List(verifyPayloads: false).Single(item => item.Generation == generation);
                Require(cheapRecord.MapName == "轻量列表");
                Reject(() => archiveStore.List(verifyPayloads: true));
                Reject(() => archiveStore.Validate(generation));
            });

            Test("cheap archive listing does not rehash the selected checkpoint", () =>
            {
                var cheapRoot = Path.Combine(root, "cheap-selected");
                var cheapStore = new CheckpointStore(cheapRoot, "v1");
                var generation = cheapStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "轻量选择", 10, 1, 1, null));
                cheapStore.Select(generation);
                File.AppendAllText(Path.Combine(cheapRoot, generation, "save.es3"), "tampered");
                var record = cheapStore.List(verifyPayloads: false).Single(item => item.Generation == generation);
                Require(record.Selected);
                Require(cheapStore.Selected(verifyPayload: false) == generation);
                Reject(() => cheapStore.Selected());
                Reject(() => cheapStore.List());
            });

            Test("manual notes do not consume the rolling checkpoint quota", () =>
            {
                var noted = Create(CheckpointKind.Manual);
                store.SetNote(noted, "长期保留");
                for (var index = 0; index < 10; index++) Create(CheckpointKind.Manual);
                store.Prune();
                var records = store.List().Where(item => item.Kind == CheckpointKind.Manual).ToList();
                Require(records.Count == 7, records.Count.ToString());
                Require(records.Any(item => item.Generation == noted && item.Note == "长期保留"));

                store.SetNote(noted, "");
                Create(CheckpointKind.Manual);
                store.Prune();
                records = store.List().Where(item => item.Kind == CheckpointKind.Manual).ToList();
                Require(records.Count == 6 && records.All(item => item.Generation != noted), records.Count.ToString());
            });

            foreach (var scenario in new[] { (Notes: 1, Total: 6), (Notes: 6, Total: 11) })
            {
                Test($"{scenario.Notes} newest noted manual checkpoints preserve five ordinary checkpoints", () =>
                {
                    var quotaRoot = Path.Combine(root, "newest-notes-" + scenario.Notes);
                    var quotaStore = new CheckpointStore(quotaRoot, "v1", maxManualCheckpoints: 5);
                    var generations = Enumerable.Range(0, scenario.Total).Select(_ => quotaStore.Create(
                        path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                        new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "备注额度", 10, 1, 1, null)))
                        .OrderByDescending(value => value, StringComparer.Ordinal).ToArray();
                    foreach (var generation in generations.Take(scenario.Notes)) quotaStore.SetNote(generation, "保留");

                    quotaStore.Prune();

                    var records = quotaStore.List();
                    Require(records.Count == scenario.Total, $"expected {scenario.Total}, got {records.Count}");
                    Require(records.Count(item => item.Note is null) == 5, "ordinary quota was consumed by notes");
                    Require(generations.All(generation => records.Any(item => item.Generation == generation)),
                        "a noted or ordinary checkpoint was removed");
                });
            }

            Test("cheap archive listing isolates malformed unselected manifests", () =>
            {
                var corruptRoot = Path.Combine(root, "corrupt-list");
                var corruptStore = new CheckpointStore(corruptRoot, "v1");
                string Create() => corruptStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "损坏隔离", 10, 1, 1, null));
                var healthy = Create();
                corruptStore.Select(healthy);
                foreach (var malformed in new[] { "{", "{\"Version\":\"invalid\"}" })
                {
                    var corrupt = Create();
                    File.WriteAllText(Path.Combine(corruptRoot, corrupt, "manifest.json"), malformed);
                }

                var records = corruptStore.List(verifyPayloads: false);
                Require(records.Count == 1 && records[0].Generation == healthy && records[0].Selected,
                    "malformed unselected manifests hid the healthy selected checkpoint");
                Require(corruptStore.Selected() == healthy);
                Reject(() => corruptStore.List());
            });

            Test("pruning isolates malformed manifests without consuming the healthy legacy quota", () =>
            {
                var corruptRoot = Path.Combine(root, "corrupt-prune");
                var corruptStore = new CheckpointStore(corruptRoot, "v1");
                var healthy = Enumerable.Range(0, 5).Select(_ => corruptStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "legacy"), _ => { }))
                    .OrderByDescending(value => value, StringComparer.Ordinal).ToArray();
                var corrupt = "99991231235959999-" + new string('a', 32);
                var corruptFolder = Path.Combine(corruptRoot, corrupt);
                Directory.CreateDirectory(corruptFolder);
                File.WriteAllText(Path.Combine(corruptFolder, "manifest.json"), "{");
                corruptStore.Select(healthy[0]);

                corruptStore.Prune();

                var records = corruptStore.List(verifyPayloads: false);
                Require(records.Count == 5 && healthy.All(generation => records.Any(item => item.Generation == generation)),
                    "malformed manifest displaced a healthy legacy checkpoint");
                foreach (var generation in healthy) corruptStore.Validate(generation);
                Require(Directory.Exists(corruptFolder), "corrupt checkpoint should remain isolated for inspection");
                Require(corruptStore.Selected() == healthy[0]);
            });

            Test("malformed selected manifest fails closed without selecting a healthy fallback", () =>
            {
                var corruptRoot = Path.Combine(root, "corrupt-selected");
                var corruptStore = new CheckpointStore(corruptRoot, "v1", maxManualCheckpoints: 5);
                string Create() => corruptStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "选中损坏", 10, 1, 1, null));
                var healthy = Enumerable.Range(0, 6).Select(_ => Create()).ToArray();
                var corrupt = Create();
                corruptStore.Select(corrupt);
                var selection = File.ReadAllText(Path.Combine(corruptRoot, "selected.json"));
                File.WriteAllText(Path.Combine(corruptRoot, corrupt, "manifest.json"), "{");

                Reject(() => corruptStore.Selected());
                Reject(() => corruptStore.Validate(corrupt));
                Reject(() => corruptStore.Select(corrupt));
                Reject(() => corruptStore.Prune());
                Reject(() => corruptStore.List());
                Require(File.ReadAllText(Path.Combine(corruptRoot, "selected.json")) == selection,
                    "selected corruption changed the persisted selection");
                foreach (var generation in healthy) corruptStore.Validate(generation);
                var records = corruptStore.List(verifyPayloads: false);
                Require(records.Count == 6 && records.All(item => !item.Selected),
                    "cheap listing displayed a corrupt selection or chose a fallback");
            });

            Test("incompatible v1 metadata is isolated from cheap listing and manual retention", () =>
            {
                var incompatibleRoot = Path.Combine(root, "incompatible-v1");
                var incompatibleStore = new CheckpointStore(incompatibleRoot, "v1", maxManualCheckpoints: 5);
                var generations = Enumerable.Range(0, 6).Select(_ => incompatibleStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "格式隔离", 10, 1, 1, null)))
                    .OrderByDescending(value => value, StringComparer.Ordinal).ToArray();
                var incompatible = generations[0];
                var healthy = generations.Skip(1).ToArray();
                incompatibleStore.Select(healthy[0]);
                var manifestPath = Path.Combine(incompatibleRoot, incompatible, "manifest.json");
                var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
                manifest["Version"] = 1;
                File.WriteAllText(manifestPath, manifest.ToJsonString());
                Reject(() => incompatibleStore.Validate(incompatible));

                var listedBefore = incompatibleStore.List(verifyPayloads: false).Any(item => item.Generation == incompatible);
                incompatibleStore.Prune();
                var records = incompatibleStore.List(verifyPayloads: false);
                var listedAfter = records.Any(item => item.Generation == incompatible);
                var healthyRetained = healthy.Count(generation => Directory.Exists(Path.Combine(incompatibleRoot, generation)));
                Require(!listedBefore && !listedAfter && healthyRetained == 5,
                    $"incompatible listed before/after: {listedBefore}/{listedAfter}; healthy retained: {healthyRetained}/5");
                Require(records.Count == 5 && records.Single(item => item.Selected).Generation == healthy[0]);
                foreach (var generation in healthy) incompatibleStore.Validate(generation);
                Require(Directory.Exists(Path.Combine(incompatibleRoot, incompatible)), "incompatible checkpoint was deleted");
                Require(incompatibleStore.Selected() == healthy[0]);
            });

            foreach (var invalidName in new[] { "../outside", @"..\outside", "C:outside", @"C:\outside", "/outside", "", ".", "..", "bad\0name", "bad?name", "bad|name" })
            {
                Test($"invalid payload name {JsonSerializer.Serialize(invalidName)} is isolated from cheap listing and manual retention", () =>
                {
                    var invalidRoot = Path.Combine(root, "invalid-payload-" + Guid.NewGuid().ToString("N"));
                    var invalidStore = new CheckpointStore(invalidRoot, "v1", maxManualCheckpoints: 5);
                    var generations = Enumerable.Range(0, 6).Select(_ => invalidStore.Create(
                        path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                        new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "文件名隔离", 10, 1, 1, null)))
                        .OrderByDescending(value => value, StringComparer.Ordinal).ToArray();
                    var invalid = generations[0];
                    var healthy = generations.Skip(1).ToArray();
                    invalidStore.Select(healthy[0]);
                    var selection = File.ReadAllText(Path.Combine(invalidRoot, "selected.json"));
                    var manifestPath = Path.Combine(invalidRoot, invalid, "manifest.json");
                    var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
                    manifest["Files"]![invalidName] = "not-a-hash";
                    File.WriteAllText(manifestPath, manifest.ToJsonString());
                    Reject(() => invalidStore.Validate(invalid));

                    var listedBefore = invalidStore.List(verifyPayloads: false).Any(item => item.Generation == invalid);
                    invalidStore.Prune();
                    var records = invalidStore.List(verifyPayloads: false);
                    var listedAfter = records.Any(item => item.Generation == invalid);
                    var healthyRetained = healthy.Count(generation => Directory.Exists(Path.Combine(invalidRoot, generation)));
                    Require(!listedBefore && !listedAfter && healthyRetained == 5,
                        $"invalid listed before/after: {listedBefore}/{listedAfter}; healthy retained: {healthyRetained}/5");
                    Require(records.Count == 5 && records.Single(item => item.Selected).Generation == healthy[0]);
                    foreach (var generation in healthy) invalidStore.Validate(generation);
                    Require(Directory.Exists(Path.Combine(invalidRoot, invalid)), "invalid checkpoint was deleted");
                    Require(invalidStore.Selected() == healthy[0]);
                    Require(File.ReadAllText(Path.Combine(invalidRoot, "selected.json")) == selection,
                        "isolating an invalid checkpoint changed the persisted selection");
                });
            }

            Test("pre-dialogue and manual checkpoints use separate quotas", () =>
            {
                var protectedDialogue = Create(CheckpointKind.PreDialogue, "旧地图");
                for (var index = 0; index < 7; index++) Create(CheckpointKind.PreDialogue);
                store.Prune(protectedDialogue);
                var dialogue = store.List().Where(item => item.Kind == CheckpointKind.PreDialogue).ToList();
                Require(dialogue.Count == 4 && dialogue.Any(item => item.Generation == protectedDialogue), dialogue.Count.ToString());
                var manual = store.List().Count(item => item.Kind == CheckpointKind.Manual);
                Require(manual >= 6);
            });

            Test("pruning directories are cleaned and do not consume archive quota", () =>
            {
                var pruningRoot = Path.Combine(root, "pruning-residue");
                var pruningStore = new CheckpointStore(pruningRoot, "v1", maxManualCheckpoints: 5);
                for (var index = 0; index < 6; index++)
                {
                    pruningStore.Create(path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                        new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "清理", 10, 1, 1, null));
                }
                var residue = Path.Combine(pruningRoot,
                    DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".pruning-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(residue);
                File.WriteAllText(Path.Combine(residue, "save.es3"), "interrupted");
                Require(pruningStore.List().All(item => Path.GetDirectoryName(item.ScreenshotPath) != residue));
                pruningStore.Prune();
                Require(!Directory.Exists(residue));
                Require(pruningStore.List().Count == 5, pruningStore.List().Count.ToString());
            });

            Test("checkpoint and load-work operations serialize on the shared data root", () =>
            {
                var lockRoot = Path.Combine(root, "locked-data", "checkpoints");
                var lockedStore = new CheckpointStore(lockRoot, "v1");
                lockedStore.Create(path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "锁", 10, 1, 1, null));
                lockedStore.Select(Directory.GetDirectories(lockRoot).Select(Path.GetFileName).Single()!);
                var workStore = new LoadWorkStore(Path.Combine(Path.GetDirectoryName(lockRoot)!, "load-work"));
                using var gate = AcquireDataRootLock(Path.GetDirectoryName(lockRoot)!);
                var read = Task.Run(() => lockedStore.Selected());
                var cleanup = Task.Run(() => workStore.CleanupExcept(null));
                var preparation = Task.Run(() => workStore.Prepare());
                Require(!read.Wait(100) && !cleanup.Wait(100) && !preparation.Wait(100),
                    "operations bypassed the data-root lock");
                gate.Dispose();
                Require(read.Wait(5000) && cleanup.Wait(5000) && preparation.Wait(5000));
            });

            Test("selection snapshots cannot race checkpoint deletion", () =>
            {
                var raceRoot = Path.Combine(root, "selection-race");
                var first = new CheckpointStore(raceRoot, "v1");
                var second = new CheckpointStore(raceRoot, "v1");
                string Create() => first.Create(path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, "竞态", 10, 1, 1, null));
                var generationA = Create();
                var generationB = Create();
                first.Select(generationA);
                var selectors = Task.Run(() =>
                {
                    for (var index = 0; index < 20; index++) second.Select(index % 2 == 0 ? generationA : generationB);
                });
                var pruner = Task.Run(() =>
                {
                    for (var index = 0; index < 20; index++) second.Prune();
                });
                Task.WaitAll(selectors, pruner);
                var selected = first.Selected();
                Require(selected == generationA || selected == generationB, selected ?? "missing");
                first.Validate(generationA);
                first.Validate(generationB);
            });

            Test("load-work prepared after cleanup preserves active and new directories", () =>
            {
                var workRoot = Path.Combine(root, "load-work-race");
                var store = new LoadWorkStore(workRoot);
                var active = store.Prepare();
                using var gate = AcquireDataRootLock(Path.GetDirectoryName(workRoot)!);
                var cleanup = Task.Run(() => store.CleanupExcept(Path.GetFileName(active)));
                string? prepared = null;
                // A mutex serializes operations but does not guarantee FIFO acquisition.
                // Match LoadPressed: finish cleanup before preparing the next work directory.
                var preparation = Task.Run(async () =>
                {
                    await cleanup;
                    prepared = new LoadWorkStore(workRoot).Prepare();
                });
                Require(!cleanup.Wait(100) && !preparation.Wait(100), "load-work operations bypassed the data-root lock");
                gate.Dispose();
                Task.WaitAll(cleanup, preparation);
                Require(Directory.Exists(active) && Directory.Exists(prepared));
            });

            Test("pre-dialogue conversation titles survive restart and can be filtered or selected", () =>
            {
                var dialogueRoot = Path.Combine(root, "dialogue-history");
                var dialogueStore = new CheckpointStore(dialogueRoot, "v1");
                var dialogue = dialogueStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.PreDialogue, new DateTime(2026, 11, 11, 16, 7, 17),
                        "禾兴官府", 11, 1, 6, null, "巨蛟帮"));
                var manual = dialogueStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"), _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, new DateTime(2026, 11, 11, 16, 6, 56),
                        "禾兴官府", 11, 1, 6, null));

                var reopened = new CheckpointStore(dialogueRoot, "v1");
                var dialogueRecord = reopened.List().Single(item => item.Generation == dialogue);
                Require(dialogueRecord.Kind == CheckpointKind.PreDialogue && dialogueRecord.Conversation == "巨蛟帮");
                Require(dialogueRecord.DisplayTopLine() == "11/11 16:07:17 · 巨蛟帮", dialogueRecord.DisplayTopLine());
                Require(dialogueRecord.DisplayFullTime() == "2026/11/11 16:07:17", dialogueRecord.DisplayFullTime());
                Require(dialogueRecord.BadgeText() == "对话前");
                Require(dialogueRecord.KindText() == "对话前自动检查点");
                var dialogueState = new CommandPanelState.State(true, false, false, false, false, true, false,
                    null, dialogue, null, new[] { dialogueRecord }, dialogueRecord);
                Require(dialogueState.DialogueCheckpointText == "本轮未建立（存档库有历史）");
                Require(dialogueState.SelectedCheckpointText == "对话前 · 巨蛟帮");

                var all = CheckpointArchiveView.Apply(reopened.List(), CheckpointArchiveFilter.All, CheckpointArchiveSortMode.CapturedTime);
                Require(all.Count == 2 && all.Any(item => item.Generation == dialogue) && all.Any(item => item.Generation == manual));
                var dialogueOnly = CheckpointArchiveView.Apply(reopened.List(), CheckpointArchiveFilter.PreDialogue, CheckpointArchiveSortMode.CapturedTime);
                Require(dialogueOnly.Count == 1 && dialogueOnly[0].Generation == dialogue);
                var manualOnly = CheckpointArchiveView.Apply(reopened.List(), CheckpointArchiveFilter.Manual, CheckpointArchiveSortMode.CapturedTime);
                Require(manualOnly.Count == 1 && manualOnly[0].Generation == manual);

                reopened.Select(dialogue);
                Require(new CheckpointStore(dialogueRoot, "v1").Selected() == dialogue);
            });

            Test("legacy v1 checkpoints remain readable and are marked legacy", () =>
            {
                var generation = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
                var folder = Path.Combine(workingRoot, generation);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "save.es3"), "legacy");
                var legacyManifest = new Dictionary<string, object>
                {
                    ["Version"] = 1,
                    ["Fingerprint"] = "v1",
                    ["Files"] = new Dictionary<string, string> { ["save.es3"] = Hash(Path.Combine(folder, "save.es3")) }
                };
                File.WriteAllText(Path.Combine(folder, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(legacyManifest));
                var record = store.List().Single(item => item.Generation == generation);
                Require(record.Kind == CheckpointKind.Legacy && record.MapName is null);
            });

            Test("legacy v1 checkpoints use their previous five-save quota", () =>
            {
                var legacyRoot = Path.Combine(root, "legacy-prune");
                var legacyStore = new CheckpointStore(legacyRoot, "v1", maxManualCheckpoints: 5, maxPreDialogueCheckpoints: 1);
                for (var index = 0; index < 8; index++)
                {
                    var generation = DateTime.UtcNow.AddSeconds(index).ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
                    var folder = Path.Combine(legacyRoot, generation);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "save.es3"), "legacy");
                    var manifest = new Dictionary<string, object>
                    {
                        ["Version"] = 1,
                        ["Fingerprint"] = "v1",
                        ["Files"] = new Dictionary<string, string> { ["save.es3"] = Hash(Path.Combine(folder, "save.es3")) }
                    };
                    File.WriteAllText(Path.Combine(folder, "manifest.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
                }
                legacyStore.Prune();
                Require(legacyStore.List().Count(item => item.Kind == CheckpointKind.Legacy) == 5);
            });

            Test("empty notes are removed while other notes remain", () =>
            {
                var kept = Create(CheckpointKind.Manual);
                var removed = Create(CheckpointKind.Manual);
                store.SetNote(kept, "保留");
                store.SetNote(removed, "待删除");
                store.SetNote(removed, " ");
                var notes = store.Notes();
                Require(notes.ContainsKey(kept) && !notes.ContainsKey(removed));
            });

            Test("panel tabs separate status from the archive browser", () =>
            {
                CommandPanelState.Open = false;
                CommandPanelState.SelectTab(CommandPanelTab.Status);
                Require(CommandPanelState.Tab == CommandPanelTab.Status);
                CommandPanelState.SelectTab(CommandPanelTab.Archive);
                Require(CommandPanelState.Tab == CommandPanelTab.Archive);
                CommandPanelState.SelectTab(CommandPanelTab.Cheats);
                Require(CommandPanelState.Tab == CommandPanelTab.Cheats);
                var summary = new CheckpointSummary("generation", CheckpointKind.Manual, DateTime.Now, "禾兴城", 20, 2, 9, true, "path", "备注", true);
                var state = new CommandPanelState.State(true, false, false, false, false, true, true, null, "generation",
                    null, new[] { summary }, summary, true);
                Require(state.ArchiveCountText == "共 1 份 · 手动 1 · 对话前 0");
                Require(state.FishingInstantSuccess);
                Require(summary.DisplayLine().Contains("禾兴城") && summary.DisplayLine().Contains("Lv.20") && summary.DisplayLine().Contains("第2周目 第9天"));
                Require(summary.NoteText() == "备注" && summary.BadgeText() == "已备注");
            });

            Test("fishing cheat only finishes an active fishing round", () =>
            {
                Require(CheatPolicy.ShouldFinishFishing(true, true, 10));
                Require(!CheatPolicy.ShouldFinishFishing(false, true, 10));
                Require(!CheatPolicy.ShouldFinishFishing(true, false, 10));
                Require(!CheatPolicy.ShouldFinishFishing(true, true, 0));
                Require(CheatPolicy.ShouldSkipFishingCountdown(true, true));
                Require(!CheatPolicy.ShouldSkipFishingCountdown(false, true));
                Require(!CheatPolicy.ShouldSkipFishingCountdown(true, false));
            });

            Test("archive rows split long capture information across two readable lines", () =>
            {
                var captured = new DateTime(2026, 11, 11, 12, 34, 56);
                var summary = new CheckpointSummary("generation", CheckpointKind.Manual, captured, "禾兴铁匠铺", 10, 1, 3,
                    false, null, "长备注", false);
                Require(summary.DisplayTopLine() == "2026/11/11 12:34:56");
                Require(summary.DisplayDetailLine() == "禾兴铁匠铺 · Lv.10 · 第1周目 第3天");
                var unknown = summary with { Round = null, Day = null };
                Require(unknown.DisplayDetailLine() == "禾兴铁匠铺 · Lv.10 · 第?周目 第?天");
            });

            Test("archive sorting supports capture time and game time", () =>
            {
                var older = new CheckpointSummary("older", CheckpointKind.Manual, new DateTime(2026, 1, 1), "地图", 10, 1, 10, false, null, null, false);
                var newer = older with { Generation = "newer", CapturedAt = new DateTime(2026, 1, 2), Day = 2 };
                var laterGameTime = older with { Generation = "later", CapturedAt = new DateTime(2025, 12, 1), Round = 2, Day = 80 };
                var unknownTime = older with { Generation = "unknown", CapturedAt = default, Round = null, Day = null };
                var source = new[] { unknownTime, newer, laterGameTime, older };
                Require(CheckpointArchiveSort.Apply(source, CheckpointArchiveSortMode.CapturedTime).Select(item => item.Generation)
                    .SequenceEqual(new[] { "newer", "older", "later", "unknown" }),
                    string.Join(",", CheckpointArchiveSort.Apply(source, CheckpointArchiveSortMode.CapturedTime).Select(item => item.Generation)));
                Require(CheckpointArchiveSort.Apply(source, CheckpointArchiveSortMode.GameTime).Select(item => item.Generation)
                    .SequenceEqual(new[] { "later", "newer", "older", "unknown" }),
                    string.Join(",", CheckpointArchiveSort.Apply(source, CheckpointArchiveSortMode.GameTime).Select(item => item.Generation)));
            });

            Test("command panel is larger and blocks game clicks inside its bounds", () =>
            {
                var layout = CommandPanelLayout.Create(1280, 720);
                Require(layout.Width >= 620 && layout.Height >= 660, $"{layout.Width}x{layout.Height}");
                CommandPanelState.SetPointerBounds(layout.X, layout.Y, layout.Width, layout.Height);
                Require(CommandPanelState.IsPointerBlocked(layout.X + 1, layout.Y + 1));
                Require(!CommandPanelState.IsPointerBlocked(layout.X - 1, layout.Y + 1));
                CommandPanelState.ClearPointerBounds();
                Require(!CommandPanelState.IsPointerBlocked(layout.X + 1, layout.Y + 1));
            });

            Test("command panel blocks only mouse wheel input inside its bounds", () =>
            {
                CommandPanelState.Open = true;
                CommandPanelState.SetPointerBounds(100, 100, 400, 300);
                Require(CommandPanelState.ShouldBlockMouseWheel("Mouse ScrollWheel", 200, 200));
                Require(!CommandPanelState.ShouldBlockMouseWheel("Mouse ScrollWheel", 50, 200));
                Require(!CommandPanelState.ShouldBlockMouseWheel("Horizontal", 200, 200));
                CommandPanelState.ClearPointerBounds();
                Require(!CommandPanelState.ShouldBlockMouseWheel("Mouse ScrollWheel", 200, 200));
                CommandPanelState.Open = false;
            });

            Test("early panel state tolerates unavailable game managers", () =>
            {
                CommandPanelState.Reset();
                var state = new CommandPanelState.State(false, false, false, false, false, true, false, null, null,
                    "游戏尚未准备好");
                Require(!state.CanSave && !state.CanLoad && state.ArchiveCountText == "暂无存档");
            });

            Test("load work cleanup removes only validated generation folders", () =>
            {
                var loadWorkRoot = Path.Combine(Path.GetTempPath(), "BeastSagaBetter-loadwork-" + Guid.NewGuid().ToString("N"));
                var store = new LoadWorkStore(loadWorkRoot);
                var stale = store.Prepare();
                var active = store.Prepare();
                Directory.CreateDirectory(Path.Combine(loadWorkRoot, "not-a-generation"));
                store.CleanupExcept(Path.GetFileName(active));
                Require(Directory.Exists(active));
                Require(!Directory.Exists(stale));
                Require(Directory.Exists(Path.Combine(loadWorkRoot, "not-a-generation")));
                store.CleanupExcept(null);
                Require(!Directory.Exists(active));
                Require(Directory.Exists(Path.Combine(loadWorkRoot, "not-a-generation")));
                Directory.Delete(loadWorkRoot, true);
            });

            Test("v2 metadata rejects invalid kinds, dates, and undeclared thumbnails", () =>
            {
                var metadataRoot = Path.Combine(root, "metadata");
                var metadataStore = new CheckpointStore(metadataRoot, "v1");
                var create = (string map) => metadataStore.Create(
                    path => File.WriteAllText(Path.Combine(path, "save.es3"), "full"),
                    _ => { },
                    new CheckpointMetadata(CheckpointKind.Manual, DateTime.Now, map, 20, 1, 1, null));

                var invalidKind = create("无效类型");
                RewriteMetadata(metadataRoot, "v1", invalidKind, metadata => metadata with { Kind = (CheckpointKind)99 });
                Require(File.ReadAllText(Path.Combine(metadataRoot, invalidKind, "manifest.json")).Contains("\"Kind\":99"), "rewritten kind");
                Reject(() => metadataStore.Validate(invalidKind));

                var invalidDate = create("无效时间");
                RewriteMetadata(metadataRoot, "v1", invalidDate, metadata => metadata with { CapturedAt = default });
                Reject(() => metadataStore.Validate(invalidDate));

                var undeclaredThumbnail = create("未声明截图");
                File.WriteAllText(Path.Combine(metadataRoot, undeclaredThumbnail, "thumbnail.png"), "undeclared");
                Reject(() => metadataStore.Validate(undeclaredThumbnail));
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
        return (Checks, Failures);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream));
    }

    private static IDisposable AcquireDataRootLock(string dataRoot)
    {
        var lockType = typeof(CheckpointStore).Assembly.GetType("BeastSagaBetter.Core.DataRootLock")
            ?? throw new Exception("Missing data-root lock");
        var acquire = lockType.GetMethod("Acquire", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, binder: null, new[] { typeof(string) }, modifiers: null)
            ?? throw new Exception("Missing data-root lock acquisition");
        return (IDisposable)acquire.Invoke(null, new object[] { dataRoot })!;
    }

    private static void RewriteMetadata(string root, string fingerprint, string generation, Func<CheckpointMetadata, CheckpointMetadata> rewrite, string? includeFile = null)
    {
        var folder = Path.Combine(root, generation);
        var manifestPath = Path.Combine(folder, "manifest.json");
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
        var metadata = document.RootElement.GetProperty("Metadata").Deserialize<CheckpointMetadata>()!;
        var files = document.RootElement.GetProperty("Files").Deserialize<Dictionary<string, string>>()!;
        if (includeFile is not null) files[includeFile] = Hash(Path.Combine(folder, includeFile));
        var manifest = new Dictionary<string, object>
        {
            ["Version"] = 2,
            ["Fingerprint"] = fingerprint,
            ["Files"] = files,
            ["Metadata"] = rewrite(metadata)
        };
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));
    }

    private static void Test(string name, Action action)
    {
        Checks++;
        try
        {
            action();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            Failures++;
            Console.WriteLine($"FAIL {name}: {error.Message}");
        }
    }

    private static void Require(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (InvalidDataException) { return; }
        throw new Exception("Unsafe operation was accepted");
    }

    private static int Checks { get; set; }
    private static int Failures { get; set; }
}
