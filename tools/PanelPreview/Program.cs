using System.Text.Json;
using SafeDialogueTools.Core;

var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var serviceArgs = args.Where(arg => !arg.StartsWith("--urls", StringComparison.OrdinalIgnoreCase)).ToArray();
var checkpointRoot = Path.GetFullPath(serviceArgs.Length > 0 ? serviceArgs[0] : Path.Combine(projectRoot, "sandbox/Game/SafeDialogueToolsData/checkpoints"));
if (!checkpointRoot.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Checkpoint root must stay inside the project: {checkpointRoot}");
    Environment.Exit(1);
}
if (!Directory.Exists(checkpointRoot))
{
    Console.Error.WriteLine($"Checkpoint root does not exist: {checkpointRoot}");
    Environment.Exit(1);
}
var store = new CheckpointStore(checkpointRoot, PluginFingerprint.Value);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = serviceArgs.Length > 1 ? serviceArgs[1..] : Array.Empty<string>(),
    ContentRootPath = projectRoot,
    WebRootPath = Path.Combine(projectRoot, "tools/PanelPreview/wwwroot")
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
var previewNotes = store.Notes().Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
var selectedGeneration = TrySelected();

app.MapGet("/api/state", () =>
{
    var summaries = SafeList();
    selectedGeneration = summaries.FirstOrDefault(item => item.Generation == selectedGeneration)?.Generation ??
                         summaries.FirstOrDefault(item => item.Kind == CheckpointKind.Manual)?.Generation;
    var selection = summaries.FirstOrDefault(item => item.Generation == selectedGeneration);
    return Results.Json(new
    {
        checkpointRoot = checkpointRoot,
        layout = CommandPanelMetrics.Create(CommandPanelLayout.Create(1280, 720)),
        state = new
        {
            status = "存读档可用",
            mode = "隔离副本",
            dialogueCheckpoint = "已建立",
            selectedCheckpoint = selectedGeneration,
            blocked = "安全检查通过",
            archiveCount = summaries.Count(item => item.Kind == CheckpointKind.Manual)
        },
        checkpoints = summaries.Select(item => new
        {
            item.Generation,
            kind = item.Kind.ToString(),
            item.CapturedAt,
            item.MapName,
            item.PlayerLevel,
            item.Round,
            item.Day,
            item.HasScreenshot,
            note = previewNotes.GetValueOrDefault(item.Generation, item.Note ?? ""),
            item.Selected,
            screenshotUrl = item.HasScreenshot ? $"/api/screenshots/{item.Generation}" : null
        }),
        selection = selection is null ? null : new
        {
            selection.Generation,
            selection.CapturedAt,
            selection.MapName,
            selection.PlayerLevel,
            selection.Round,
            selection.Day,
            selection.HasScreenshot,
            note = previewNotes.GetValueOrDefault(selection.Generation, selection.Note ?? ""),
            selection.Selected
        }
    });
});

app.MapPost("/api/select", async (HttpContext context) =>
{
    var payload = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(context.Request.Body);
    var generation = payload?.GetValueOrDefault("generation");
    if (string.IsNullOrEmpty(generation) || SafeList().All(item => item.Generation != generation))
        return Results.BadRequest(new { error = "Unknown checkpoint" });
    selectedGeneration = generation;
    return Results.Ok(new { generation });
});

app.MapGet("/api/screenshots/{generation}", (string generation) =>
{
    var summary = SafeList().FirstOrDefault(item => item.Generation == generation);
    if (summary?.ScreenshotPath is null || !summary.HasScreenshot) return Results.NotFound();
    return Results.File(summary.ScreenshotPath, "image/png");
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", checkpointRoot }));

IReadOnlyList<CheckpointSummary> SafeList()
{
    try { return store.List(verifyPayloads: false); }
    catch (InvalidDataException) { return Array.Empty<CheckpointSummary>(); }
}

string? TrySelected()
{
    try { return store.Selected(); }
    catch (InvalidDataException) { return null; }
}

await app.RunAsync();

internal static class PluginFingerprint
{
    public const string Value = "B12412BC21B0B88A07851D15235736AFCFCE48A22AB8D3F1CB3DFB8376360359";
}
