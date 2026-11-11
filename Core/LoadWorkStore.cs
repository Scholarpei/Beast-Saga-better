using System.Text.RegularExpressions;

namespace SafeDialogueTools.Core;

public sealed class LoadWorkStore
{
    private static readonly Regex GenerationPattern = new(@"\A[0-9a-f]{32}\z", RegexOptions.CultureInvariant);
    private readonly string root;

    public LoadWorkStore(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public string Prepare()
    {
        var path = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void CleanupExcept(string? active)
    {
        foreach (var directory in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(directory);
            var activeName = active is null ? null : Path.GetFileName(Path.GetFullPath(Path.Combine(root, active)));
            if (activeName == name || !GenerationPattern.IsMatch(name)) continue;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(directory, true);
        }
    }
}
