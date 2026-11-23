using System.Diagnostics;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.FileFinder;

namespace TelDiskCleaner.Core.Backup;

public sealed class BackupJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My backup";

    /// <summary>Fixed when the job is created, so renaming the job never orphans its backups.</summary>
    public string FolderName { get; set; } = string.Empty;

    public List<string> Sources { get; set; } = new();
    public string Destination { get; set; } = string.Empty;
    public int KeepVersions { get; set; } = 5;
    public List<string> ExcludedFolders { get; set; } = DefaultExclusions();
    public bool RunDaily { get; set; }

    public static List<string> DefaultExclusions() => new()
    {
        "node_modules", "bin", "obj", "dist", ".angular", ".dart_tool", "venv", ".venv", "__pycache__", ".vs", ".gradle",
    };
}

public sealed record BackupProgress(int FilesChecked, int FilesCopied, long BytesCopied, string? CurrentFile);

public sealed record BackupResult(
    int Copied, long CopiedBytes, int Unchanged, int Versioned, int Removed, int SkippedOnlineOnly,
    IReadOnlyList<string> Errors, TimeSpan Duration)
{
    public string Summary =>
        $"Copied {Copied:N0} file(s) ({SizeFormatter.Format(CopiedBytes)}), {Unchanged:N0} unchanged" +
        (Versioned + Removed > 0 ? $", kept {Versioned + Removed:N0} older version(s)" : string.Empty) +
        (SkippedOnlineOnly > 0 ? $", skipped {SkippedOnlineOnly:N0} online-only file(s)" : string.Empty) +
        (Errors.Count > 0 ? $", {Errors.Count:N0} file(s) could not be copied" : string.Empty) + ".";
}

public sealed record RestoreResult(int Restored, int Unchanged, int KeptLocalCopies, IReadOnlyList<string> Errors)
{
    public string Summary =>
        $"Restored {Restored:N0} file(s), {Unchanged:N0} already up to date" +
        (KeptLocalCopies > 0 ? $"; {KeptLocalCopies:N0} different local file(s) were kept with \"(before restore)\" in the name" : string.Empty) +
        (Errors.Count > 0 ? $"; {Errors.Count:N0} file(s) could not be restored" : string.Empty) + ".";
}

public sealed class BackupManifest
{
    /// <summary>Folder name inside "current" mapped to the original source path.</summary>
    public Dictionary<string, string> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Layout under the destination: "<job>\current\<source>\..." is a plain copy you can browse in
/// Explorer; "<job>\versions\<date-time>\<source>\..." holds files as they were before a later
/// backup replaced or removed them. Only the newest <see cref="BackupJob.KeepVersions"/> are kept.
/// </summary>
public sealed class BackupService
{
    private const string TempSuffix = ".tdc-tmp";
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2); // FAT/exFAT USB drives round to 2 s

    public static string JobFolder(BackupJob job) => Path.Combine(job.Destination, job.FolderName);

    public static string? Validate(BackupJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Name)) return "Give the backup a name.";
        if (job.Sources.Count == 0) return "Add at least one folder to back up.";
        if (string.IsNullOrWhiteSpace(job.Destination)) return "Choose where to save the backup.";
        if (job.KeepVersions is < 1 or > 100) return "Keep between 1 and 100 older versions.";

        var destination = Normalize(job.Destination);
        foreach (var source in job.Sources)
        {
            if (!Directory.Exists(source)) return $"{source} doesn't exist.";
            var src = Normalize(source);
            if (IsSameOrInside(destination, src)) return $"The backup location can't be inside {source}.";
            if (IsSameOrInside(src, destination)) return $"{source} can't be inside the backup location.";
        }
        return null;
    }

    public Task<BackupResult> RunAsync(BackupJob job, IProgress<BackupProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(job, progress, cancellationToken), cancellationToken);

    public BackupResult Run(BackupJob job, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var problem = Validate(job);
        if (problem is not null) throw new InvalidOperationException(problem);

        var stopwatch = Stopwatch.StartNew();
        var jobFolder = JobFolder(job);
        var currentRoot = Path.Combine(jobFolder, "current");
        var versionRoot = Path.Combine(jobFolder, "versions", DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
        var manifestPath = Path.Combine(jobFolder, "backup.json");
        Directory.CreateDirectory(currentRoot);

        var manifest = JsonFileStore.Load<BackupManifest>(manifestPath);
        var excluded = new HashSet<string>(job.ExcludedFolders, StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        int checkedCount = 0, copied = 0, unchanged = 0, versioned = 0, removed = 0, onlineOnly = 0;
        long copiedBytes = 0;
        var lastReport = Stopwatch.StartNew();

        foreach (var source in job.Sources)
        {
            var label = LabelFor(manifest, source);
            JsonFileStore.Save(manifestPath, manifest);

            var sourceRoot = Path.GetFullPath(source);
            var targetRoot = Path.Combine(currentRoot, label);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in FileWalker.EnumerateFiles(sourceRoot, d => excluded.Contains(d.Name), cancellationToken))
            {
                var relative = Path.GetRelativePath(sourceRoot, file.FullName);
                seen.Add(relative);
                checkedCount++;

                if (FileWalker.IsCloudPlaceholder(file.Attributes))
                {
                    onlineOnly++;
                    continue;
                }

                var target = Path.Combine(targetRoot, relative);
                var existing = new FileInfo(target);
                if (existing.Exists && existing.Length == file.Length
                    && (existing.LastWriteTimeUtc - file.LastWriteTimeUtc).Duration() <= TimestampTolerance)
                {
                    unchanged++;
                    continue;
                }

                // Copy to a temporary name first, so a failed copy never loses the previous version.
                var temp = target + TempSuffix;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file.FullName, temp, overwrite: true);
                    File.SetLastWriteTimeUtc(temp, file.LastWriteTimeUtc);

                    if (existing.Exists)
                    {
                        MoveToVersions(target, Path.Combine(versionRoot, label, relative));
                        versioned++;
                    }
                    File.Move(temp, target);
                    copied++;
                    copiedBytes += file.Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{file.FullName}: {ex.Message}");
                    TryDelete(temp);
                }

                if (lastReport.ElapsedMilliseconds > 150)
                {
                    progress?.Report(new BackupProgress(checkedCount, copied, copiedBytes, file.FullName));
                    lastReport.Restart();
                }
            }

            removed += MoveRemovedFiles(targetRoot, seen, Path.Combine(versionRoot, label), cancellationToken);
        }

        PruneVersions(Path.Combine(jobFolder, "versions"), job.KeepVersions);
        progress?.Report(new BackupProgress(checkedCount, copied, copiedBytes, null));

        return new BackupResult(copied, copiedBytes, unchanged, versioned, removed, onlineOnly, errors, stopwatch.Elapsed);
    }

    /// <summary>
    /// Copies the latest backup back. With no target folder, files go to their original folders.
    /// Local files are never overwritten: a local file that differs is renamed and kept.
    /// </summary>
    public Task<RestoreResult> RestoreAsync(BackupJob job, string? targetFolder, IProgress<BackupProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var jobFolder = JobFolder(job);
            var currentRoot = Path.Combine(jobFolder, "current");
            if (!Directory.Exists(currentRoot)) throw new InvalidOperationException("There is no backup to restore yet.");

            var manifest = JsonFileStore.Load<BackupManifest>(Path.Combine(jobFolder, "backup.json"));
            var errors = new List<string>();
            int restored = 0, unchanged = 0, kept = 0, checkedCount = 0;
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss");

            foreach (var labelDir in Directory.EnumerateDirectories(currentRoot))
            {
                var label = Path.GetFileName(labelDir);
                string destinationRoot;
                if (targetFolder is not null)
                {
                    destinationRoot = Path.Combine(targetFolder, label);
                }
                else if (manifest.Sources.TryGetValue(label, out var original))
                {
                    destinationRoot = original;
                }
                else
                {
                    errors.Add($"{label}: original folder unknown; restore to a folder instead.");
                    continue;
                }

                foreach (var file in FileWalker.EnumerateFiles(labelDir, _ => false, cancellationToken))
                {
                    if (file.Name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                    checkedCount++;
                    var destination = Path.Combine(destinationRoot, Path.GetRelativePath(labelDir, file.FullName));

                    try
                    {
                        var local = new FileInfo(destination);
                        if (local.Exists)
                        {
                            if (local.Length == file.Length && (local.LastWriteTimeUtc - file.LastWriteTimeUtc).Duration() <= TimestampTolerance)
                            {
                                unchanged++;
                                continue;
                            }
                            File.Move(destination, BeforeRestoreName(destination, stamp));
                            kept++;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(file.FullName, destination);
                        restored++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{destination}: {ex.Message}");
                    }

                    progress?.Report(new BackupProgress(checkedCount, restored, 0, destination));
                }
            }

            return new RestoreResult(restored, unchanged, kept, errors);
        }, cancellationToken);

    private static string LabelFor(BackupManifest manifest, string source)
    {
        var full = Normalize(source);
        var existing = manifest.Sources.FirstOrDefault(kv => string.Equals(Normalize(kv.Value), full, StringComparison.OrdinalIgnoreCase));
        if (existing.Key is not null) return existing.Key;

        var baseName = Path.GetFileName(full);
        if (string.IsNullOrEmpty(baseName)) baseName = full.TrimEnd(':', '\\') + "-drive";
        baseName = SafeName(baseName);

        var label = baseName;
        for (int i = 2; manifest.Sources.ContainsKey(label); i++) label = $"{baseName}-{i}";
        manifest.Sources[label] = full;
        return label;
    }

    private static int MoveRemovedFiles(string targetRoot, HashSet<string> seen, string versionFolder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(targetRoot)) return 0;

        int removed = 0;
        foreach (var file in FileWalker.EnumerateFiles(targetRoot, _ => false, cancellationToken).ToList())
        {
            if (file.Name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(file.FullName); // leftover from an interrupted backup
                continue;
            }

            var relative = Path.GetRelativePath(targetRoot, file.FullName);
            if (seen.Contains(relative)) continue;

            try
            {
                MoveToVersions(file.FullName, Path.Combine(versionFolder, relative));
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // leave it in place; it will be retried next run
            }
        }

        DeleteEmptyFolders(targetRoot);
        return removed;
    }

    private static void MoveToVersions(string file, string versionPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(versionPath)!);
        File.Move(file, versionPath, overwrite: true);
    }

    private static void PruneVersions(string versionsFolder, int keep)
    {
        if (!Directory.Exists(versionsFolder)) return;
        foreach (var old in Directory.EnumerateDirectories(versionsFolder).OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).Skip(keep))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // try again next run
            }
        }
    }

    private static void DeleteEmptyFolders(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // not important
            }
        }
    }

    private static string BeforeRestoreName(string path, string stamp) =>
        Path.Combine(Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)} (before restore {stamp}){Path.GetExtension(path)}");

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '.');
        return cleaned.Length == 0 ? "Backup" : cleaned;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static bool IsSameOrInside(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not important
        }
    }
}

public sealed class BackupRunStatus
{
    public DateTime RunUtc { get; set; }
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Last result per job, kept apart from settings so a scheduled run can't overwrite UI edits.</summary>
public static class BackupStatusStore
{
    private static string FilePath => AppPaths.File("backup-status.json");

    public static Dictionary<string, BackupRunStatus> Load() =>
        JsonFileStore.Load<Dictionary<string, BackupRunStatus>>(FilePath);

    public static void Record(string jobId, BackupRunStatus status)
    {
        var all = Load();
        all[jobId] = status;
        JsonFileStore.Save(FilePath, all);
    }
}
