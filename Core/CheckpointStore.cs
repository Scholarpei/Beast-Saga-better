using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SafeDialogueTools.Core;

public sealed class CheckpointStore
{
    private readonly string root;
    private readonly string fingerprint;
    private sealed record Manifest(int Version, string Fingerprint, Dictionary<string, string> Files);

    public CheckpointStore(string root, string fingerprint)
    {
        this.root = Path.GetFullPath(root);
        this.fingerprint = fingerprint;
        Directory.CreateDirectory(this.root);
        CheckDirectory(this.root);
    }

    public string Create(Action<string> write, Action<string> validate)
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
            DurableWrite(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(new Manifest(1, fingerprint, files)));
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

    public string? Selected()
    {
        var path = Path.Combine(root, "selected.json");
        if (!File.Exists(path)) return null;
        CheckFile(path);
        try
        {
            var generation = JsonSerializer.Deserialize<string>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty read target");
            Validate(generation);
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
        if (manifest.Version != 1 || manifest.Fingerprint != fingerprint || manifest.Files is null || !manifest.Files.ContainsKey("save.es3"))
            throw new InvalidDataException("Incompatible checkpoint");
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
        keep.Add(Selected());
        var candidates = Directory.GetDirectories(root).Select(Path.GetFileName)
            .Where(name => name is not null && Regex.IsMatch(name, @"\A[0-9]{17}-[a-f0-9]{32}\z"))
            .OrderByDescending(name => name, StringComparer.Ordinal).ToArray();
        foreach (var generation in candidates.Take(5)) keep.Add(generation);
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
