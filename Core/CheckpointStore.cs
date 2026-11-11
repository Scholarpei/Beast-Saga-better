using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BeastSagaBetter.Core;

public enum CheckpointKind { Manual, PreDialogue, Legacy }

public sealed record CheckpointMetadata(
    CheckpointKind Kind, DateTime CapturedAt, string MapName, int PlayerLevel, int Round, int Day,
    string? Screenshot, string? Conversation = null);

public sealed record CheckpointSummary(
    string Generation, CheckpointKind Kind, DateTime CapturedAt, string? MapName, int? PlayerLevel, int? Round,
    int? Day, bool HasScreenshot, string? ScreenshotPath, string? Note, bool Selected, string? Conversation = null);

public sealed class CheckpointStore
{
    private readonly string root;
    private readonly string fingerprint;
    private readonly int maxManualCheckpoints;
    private readonly int maxPreDialogueCheckpoints;
    private sealed record Manifest(int Version, string Fingerprint, Dictionary<string, string> Files, CheckpointMetadata? Metadata = null);
    private sealed record NoteIndex(int Version, Dictionary<string, string> Notes);


    public CheckpointStore(string root, string fingerprint, int maxManualCheckpoints = 50, int maxPreDialogueCheckpoints = 10)
    {
        this.root = Path.GetFullPath(root);
        this.fingerprint = fingerprint;
        this.maxManualCheckpoints = Math.Max(1, maxManualCheckpoints);
        this.maxPreDialogueCheckpoints = Math.Max(1, maxPreDialogueCheckpoints);
        Directory.CreateDirectory(this.root);
        CheckDirectory(this.root);
    }

    public string Create(Action<string> write, Action<string> validate, CheckpointMetadata? metadata = null)
    {
        var generation = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, generation + ".pending");
        Directory.CreateDirectory(staging);
        try
        {
            write(staging);
            validate(staging);
            CheckDirectory(staging);
            if (!File.Exists(Path.Combine(staging, "save.es3"))) throw new InvalidDataException("Missing save.es3");
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(staging))
            {
                CheckFile(path);
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) stream.Flush(true);
                files.Add(Path.GetFileName(path), Hash(path));
            }
            if (Directory.GetDirectories(staging).Length != 0) throw new InvalidDataException("Unexpected nested directory");
            if (metadata is not null)
            {
                if (!Enum.IsDefined(metadata.Kind) || metadata.Kind == CheckpointKind.Legacy)
                    throw new InvalidDataException("Invalid checkpoint kind");
                if (metadata.Screenshot is not null && (metadata.Screenshot != "thumbnail.png" || !files.ContainsKey(metadata.Screenshot)))
                    throw new InvalidDataException("Missing checkpoint screenshot");
            }
            DurableWrite(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(new Manifest(
                metadata is null ? 1 : 2, fingerprint, files, metadata)));
            Directory.Move(staging, Path.Combine(root, generation));
            Validate(generation);
            return generation;
        }
        finally
        {
            if (Directory.Exists(staging) && (File.GetAttributes(staging) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(staging, true);
        }
    }

    public void Select(string generation)
    {
        Validate(generation);
        var pending = Path.Combine(root, "selected-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            DurableWrite(pending, JsonSerializer.Serialize(generation));
            var target = Path.Combine(root, "selected.json");
            if (File.Exists(target)) CheckFile(target);
            File.Move(pending, target, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public string? Selected(bool verifyPayload = true)
    {
        var path = Path.Combine(root, "selected.json");
        if (!File.Exists(path)) return null;
        CheckFile(path);
        try
        {
            var generation = JsonSerializer.Deserialize<string>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty read target");
            if (verifyPayload) Validate(generation);
            else CheckSelectionTarget(generation);
            return generation;
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid read target", error); }
    }

    public string Validate(string generation)
    {
        if (!Regex.IsMatch(generation, @"\A[0-9]{17}-[a-f0-9]{32}\z")) throw new InvalidDataException("Invalid generation identifier");
        CheckDirectory(root);
        var folder = Path.Combine(root, generation);
        CheckDirectory(folder);
        var manifestPath = Path.Combine(folder, "manifest.json");
        CheckFile(manifestPath);
        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("Empty manifest"); }
        catch (JsonException error) { throw new InvalidDataException("Invalid manifest", error); }
        if ((manifest.Version != 1 && manifest.Version != 2) || manifest.Fingerprint != fingerprint || manifest.Files is null || !manifest.Files.ContainsKey("save.es3"))
            throw new InvalidDataException("Incompatible checkpoint");
        if (manifest.Version == 2 && manifest.Metadata is null) throw new InvalidDataException("Missing checkpoint metadata");
        if (manifest.Version == 1 && manifest.Metadata is not null) throw new InvalidDataException("Invalid checkpoint metadata");
        if (manifest.Version == 2 && !IsValidMetadata(manifest.Metadata!)) throw new InvalidDataException("Invalid checkpoint metadata");
        foreach (var entry in manifest.Files)
        {
            if (string.IsNullOrEmpty(entry.Key) || entry.Key != Path.GetFileName(entry.Key) || entry.Key.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new InvalidDataException("Invalid payload name");
            var path = Path.Combine(folder, entry.Key);
            CheckFile(path);
            if (Hash(path) != entry.Value) throw new InvalidDataException("Checkpoint checksum mismatch: " + entry.Key);
        }
        if (Directory.GetDirectories(folder).Length != 0 || Directory.GetFiles(folder).Length != manifest.Files.Count + 1)
            throw new InvalidDataException("Unexpected checkpoint contents");
        return folder;
    }

    public void Prune(params string?[] protectedGenerations)
    {
        var keep = protectedGenerations.Where(value => value is not null).ToHashSet(StringComparer.Ordinal);
        var selected = Selected();
        keep.Add(selected);
        var candidates = Directory.GetDirectories(root).Select(Path.GetFileName)
            .Where(name => name is not null && Regex.IsMatch(name, @"\A[0-9]{17}-[a-f0-9]{32}\z"))
            .OrderByDescending(name => name, StringComparer.Ordinal).ToArray();
        var notes = Notes();
        var kinds = candidates.ToDictionary(name => name!, name => ReadKind(name!));
        foreach (var generation in candidates.Where(name => kinds[name!] == CheckpointKind.Manual).Take(maxManualCheckpoints)) keep.Add(generation);
        foreach (var generation in candidates.Where(name => kinds[name!] == CheckpointKind.PreDialogue).Take(maxPreDialogueCheckpoints)) keep.Add(generation);
        foreach (var generation in candidates.Where(name => kinds[name!] == CheckpointKind.Legacy).Take(5)) keep.Add(generation);
        foreach (var generation in candidates.Where(name => kinds[name!] == CheckpointKind.Manual && !string.IsNullOrWhiteSpace(notes.GetValueOrDefault(name!)))) keep.Add(generation);
        foreach (var generation in candidates.Where(name => !keep.Contains(name)))
        {
            try
            {
                var folder = Validate(generation!);
                Directory.Delete(folder, true);
            }
            catch (InvalidDataException) { }
        }
    }

    public IReadOnlyList<CheckpointSummary> List(bool verifyPayloads = true)
    {
        var selected = TrySelected(out var selectedGeneration, verifyPayloads) ? selectedGeneration : null;
        var notes = Notes();
        var summaries = new List<CheckpointSummary>();
        foreach (var name in Directory.GetDirectories(root).Select(Path.GetFileName).Where(name => name is not null).OrderByDescending(name => name, StringComparer.Ordinal))
        {
            if (!Regex.IsMatch(name!, @"\A[0-9]{17}-[a-f0-9]{32}\z")) continue;
            try
            {
                if (verifyPayloads) Validate(name!);
                var manifest = ReadValidatedManifest(name!);
                var metadata = manifest.Metadata;
                var screenshot = metadata?.Screenshot;
                if (screenshot is null && manifest.Files.ContainsKey("thumbnail.png")) screenshot = "thumbnail.png";
                var screenshotPath = screenshot is null ? null : Path.Combine(root, name!, screenshot);
                summaries.Add(new CheckpointSummary(
                    name!, metadata?.Kind ?? CheckpointKind.Legacy, metadata?.CapturedAt ?? default,
                    metadata?.MapName, metadata?.PlayerLevel, metadata?.Round, metadata?.Day,
                    screenshot is not null && manifest.Files.ContainsKey(screenshot),
                    screenshotPath, notes.TryGetValue(name!, out var note) ? note : null,
                    selected == name, metadata?.Conversation));
            }
            catch (InvalidDataException) when (!verifyPayloads) { }
        }
        return summaries;
    }

    public IReadOnlyDictionary<string, string> Notes()
    {
        var path = Path.Combine(root, "notes.json");
        if (!File.Exists(path)) return new Dictionary<string, string>();
        CheckFile(path);
        try
        {
            var index = JsonSerializer.Deserialize<NoteIndex>(File.ReadAllText(path));
            if (index is null || index.Version != 1 || index.Notes is null) return new Dictionary<string, string>();
            return index.Notes;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Invalid checkpoint notes", error);
        }
    }

    public void SetNote(string generation, string value)
    {
        Validate(generation);
        var note = value.Trim();
        if (note.Length > 500) note = note[..500];
        var notes = Notes().Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (note.Length == 0) notes.Remove(generation);
        else notes[generation] = note;
        var pending = Path.Combine(root, "notes-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            DurableWrite(pending, JsonSerializer.Serialize(new NoteIndex(1, notes)));
            var target = Path.Combine(root, "notes.json");
            if (File.Exists(target)) CheckFile(target);
            File.Move(pending, target, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private bool TrySelected(out string? generation, bool verifyPayload = true)
    {
        try { generation = Selected(verifyPayload); return generation is not null; }
        catch (InvalidDataException) { generation = null; return false; }
    }

    private void CheckSelectionTarget(string generation)
    {
        if (!Regex.IsMatch(generation, @"\A[0-9]{17}-[a-f0-9]{32}\z")) throw new InvalidDataException("Invalid generation identifier");
        CheckDirectory(root);
        CheckDirectory(Path.Combine(root, generation));
    }

    private Manifest ReadManifest(string generation)
    {
        var manifestPath = Path.Combine(root, generation, "manifest.json");
        CheckFile(manifestPath);
        var metadata = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
        if (metadata?.Metadata is not null && metadata.Metadata.Day == 0)
            metadata = metadata with { Metadata = metadata.Metadata with { Day = 1 } };
        return metadata ?? throw new InvalidDataException("Empty manifest");
    }

    private CheckpointKind ReadKind(string generation)
    {
        var manifest = ReadKindManifest(generation, out var valid);
        return valid ? manifest!.Metadata?.Kind ?? CheckpointKind.Legacy : CheckpointKind.Legacy;
    }

    private Manifest ReadValidatedManifest(string generation)
    {
        var manifest = ReadKindManifest(generation, out var valid);
        if (!valid) throw new InvalidDataException("Incompatible checkpoint");
        return manifest!;
    }

    private Manifest? ReadKindManifest(string generation, out bool valid)
    {
        try
        {
            var manifest = ReadManifest(generation);
            valid = (manifest.Version == 1 || manifest.Version == 2) && manifest.Fingerprint == fingerprint &&
                    manifest.Files is not null && manifest.Files.ContainsKey("save.es3") &&
                    (manifest.Version == 1 || manifest.Metadata is not null && IsValidMetadata(manifest.Metadata));
            return manifest;
        }
        catch (InvalidDataException)
        {
            valid = false;
            return null;
        }
    }

    private static void CheckDirectory(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Missing or redirected checkpoint directory");
    }

    private static void CheckFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Missing or redirected checkpoint file");
    }

    private static bool IsValidMetadata(CheckpointMetadata metadata) =>
        (metadata.Kind is CheckpointKind.Manual or CheckpointKind.PreDialogue) &&
        metadata.CapturedAt != default &&
        metadata.PlayerLevel >= 1 &&
        metadata.Round >= 1 &&
        (metadata.Conversation is null || metadata.Conversation.Length is > 0 and <= 120) &&
        (metadata.Screenshot is null || metadata.Screenshot == "thumbnail.png");

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream));
    }

    private static void DurableWrite(string path, string value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var data = System.Text.Encoding.UTF8.GetBytes(value);
        stream.Write(data);
        stream.Flush(true);
    }
}
