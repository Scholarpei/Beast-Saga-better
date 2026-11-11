using System.Text.Json;
using SafeDialogueTools.Core;

namespace SafeDialogueTools.Tests;

internal static class CheckpointHistoryTests
{
    public static (int Checks, int Failures) Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "SafeDialogueTools-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workingRoot = Path.Combine(root, "working");
            var store = new CheckpointStore(workingRoot, "v1", maxManualCheckpoints: 6, maxPreDialogueCheckpoints: 3);
            string Create(CheckpointKind kind, string map = "禾兴城", int level = 12, int round = 1, bool screenshot = true, int day = 1)
            {
                return store.Create(
                    path =>
                    {
                        File.WriteAllText(Path.Combine(path, "save.es3"), "full");
                        if (screenshot) File.WriteAllText(Path.Combine(path, "thumbnail.png"), "png");
                    },
                    _ => { },
                    new CheckpointMetadata(kind, DateTime.Now, map, level, round, day, screenshot ? "thumbnail.png" : null));
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
                var legacyStore = new CheckpointStore(legacyRoot, "v1", maxManualCheckpoints: 2, maxPreDialogueCheckpoints: 1);
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
                var summary = new CheckpointSummary("generation", CheckpointKind.Manual, DateTime.Now, "禾兴城", 20, 2, 9, true, "path", "备注", true);
                var state = new CommandPanelState.State(true, false, false, false, false, true, true, null, "generation",
                    null, new[] { summary }, summary);
                Require(state.ArchiveCountText == "1 份手动存档");
                Require(summary.DisplayLine().Contains("禾兴城") && summary.DisplayLine().Contains("Lv.20") && summary.DisplayLine().Contains("第2周目 第9天"));
                Require(summary.NoteText() == "备注" && summary.BadgeText() == "已备注");
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
                Require(!state.CanSave && !state.CanLoad && state.ArchiveCountText == "暂无手动存档");
            });

            Test("load work cleanup removes only validated generation folders", () =>
            {
                var loadWorkRoot = Path.Combine(Path.GetTempPath(), "SafeDialogueTools-loadwork-" + Guid.NewGuid().ToString("N"));
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
