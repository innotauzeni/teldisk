namespace TelDiskCleaner.Core.ProjectCleanup;

/// <param name="RequiredSiblingFile">
/// File pattern that must exist next to the folder for it to count as build output. Names like
/// "build" and "bin" are also used for hand-written folders, so they only match real projects.
/// </param>
public sealed record ArtifactKind(string FolderName, string DisplayName, string Ecosystem, string? RequiredSiblingFile = null);

public static class ArtifactFolderDefinitions
{
    public static readonly ArtifactKind NodeModules = new("node_modules", "node_modules", "Node.js");

    public static readonly IReadOnlyList<ArtifactKind> All = new[]
    {
        NodeModules,
        new ArtifactKind("bin", "bin", ".NET", RequiredSiblingFile: "*.*proj"),
        new ArtifactKind("obj", "obj", ".NET", RequiredSiblingFile: "*.*proj"),
        new ArtifactKind("dist", "dist", "Angular/JS build output"),
        new ArtifactKind(".angular", ".angular", "Angular cache"),
        new ArtifactKind("venv", "venv", "Python"),
        new ArtifactKind(".venv", ".venv", "Python"),
        new ArtifactKind("__pycache__", "__pycache__", "Python"),
        new ArtifactKind(".dart_tool", ".dart_tool", "Flutter/Dart"),
        new ArtifactKind("build", "build", "Flutter build output", RequiredSiblingFile: "pubspec.yaml"),
    };

    private static readonly HashSet<string> NamesLookup =
        new(All.Select(a => a.FolderName), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ArtifactKind> ByName =
        All.ToDictionary(a => a.FolderName, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetKind(string folderName, string projectRoot, out ArtifactKind kind)
    {
        if (!ByName.TryGetValue(folderName, out kind!)) return false;
        if (kind.RequiredSiblingFile is null) return true;

        try
        {
            return Directory.EnumerateFiles(projectRoot, kind.RequiredSiblingFile).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsArtifactFolder(string folderName) => NamesLookup.Contains(folderName);
}
