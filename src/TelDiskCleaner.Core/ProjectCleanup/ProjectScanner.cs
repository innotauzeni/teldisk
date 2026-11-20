using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.ProjectCleanup;

public sealed class ProjectScanner
{
    private const int CancellationCheckInterval = 200;

    /// <summary>
    /// Walks each root looking for artifact folders (node_modules, bin/obj, dist, venv, ...).
    /// Does not descend into an artifact folder once found - only its total size is needed,
    /// not a full file tree, since node_modules can contain 100k+ small files.
    /// </summary>
    public List<ProjectCandidate> ScanForArtifacts(IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        var byProjectRoot = new Dictionary<string, List<ArtifactMatch>>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            FindArtifactsUnder(root, byProjectRoot, cancellationToken);
        }

        return byProjectRoot
            .Select(kv => new ProjectCandidate
            {
                ProjectRoot = kv.Key,
                Artifacts = kv.Value,
                LastActivityUtc = null,
                ActivitySource = ProjectActivitySource.Unknown,
            })
            .ToList();
    }

    private static void FindArtifactsUnder(
        string root,
        Dictionary<string, List<ArtifactMatch>> byProjectRoot,
        CancellationToken cancellationToken)
    {
        int sinceCheck = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            sinceCheck++;
            if (sinceCheck >= CancellationCheckInterval)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sinceCheck = 0;
            }

            var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                string subdirName;
                try
                {
                    var info = new DirectoryInfo(subdir);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    subdirName = info.Name;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (ArtifactFolderDefinitions.TryGetKind(subdirName, out var kind))
                {
                    var projectRoot = dir;
                    var size = DirectorySizeCalculator.ComputeSize(subdir, cancellationToken);

                    if (!byProjectRoot.TryGetValue(projectRoot, out var list))
                    {
                        list = new List<ArtifactMatch>();
                        byProjectRoot[projectRoot] = list;
                    }
                    list.Add(new ArtifactMatch(kind, subdir, size));
                    // Do not descend into the artifact folder itself.
                    continue;
                }

                pending.Push(subdir);
            }
        }
    }
}
