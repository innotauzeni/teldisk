namespace TelDiskCleaner.Core.ProjectCleanup;

public sealed record ArtifactKind(string FolderName, string DisplayName, string Ecosystem);

public static class ArtifactFolderDefinitions
{
    public static readonly ArtifactKind NodeModules = new("node_modules", "node_modules", "Node.js");

    public static readonly IReadOnlyList<ArtifactKind> All = new[]
    {
        NodeModules,
        new ArtifactKind("bin", "bin", ".NET"),
        new ArtifactKind("obj", "obj", ".NET"),
        new ArtifactKind("dist", "dist", "Angular/JS build output"),
        new ArtifactKind(".angular", ".angular", "Angular cache"),
        new ArtifactKind("venv", "venv", "Python"),
        new ArtifactKind(".venv", ".venv", "Python"),
        new ArtifactKind("__pycache__", "__pycache__", "Python"),
    };

    private static readonly HashSet<string> NamesLookup =
        new(All.Select(a => a.FolderName), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ArtifactKind> ByName =
        All.ToDictionary(a => a.FolderName, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetKind(string folderName, out ArtifactKind kind) =>
        ByName.TryGetValue(folderName, out kind!);

    public static bool IsArtifactFolder(string folderName) => NamesLookup.Contains(folderName);
}
