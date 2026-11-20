using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.ProjectCleanup;

public sealed record ActivityResult(DateTime? LastActivityUtc, ProjectActivitySource Source);

public sealed class ActivityDetector
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(5);

    public async Task<ActivityResult> DetectAsync(string projectRoot)
    {
        if (Directory.Exists(Path.Combine(projectRoot, ".git")))
        {
            var gitResult = await TryGetGitLastCommitUtc(projectRoot).ConfigureAwait(false);
            if (gitResult.HasValue)
            {
                return new ActivityResult(gitResult.Value, ProjectActivitySource.Git);
            }
        }

        var mtime = GetNewestSourceFileMtimeUtc(projectRoot);
        return new ActivityResult(mtime, mtime.HasValue ? ProjectActivitySource.FileModifiedTime : ProjectActivitySource.Unknown);
    }

    private static async Task<DateTime?> TryGetGitLastCommitUtc(string projectRoot)
    {
        var result = await ProcessRunner.RunAsync("git", "log -1 --format=%ct", projectRoot, GitTimeout)
            .ConfigureAwait(false);

        if (!result.Success) return null;

        var output = result.StandardOutput.Trim();
        if (output.Length == 0) return null; // repo with no commits yet

        if (!long.TryParse(output, out var unixSeconds)) return null;

        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
    }

    private static DateTime? GetNewestSourceFileMtimeUtc(string projectRoot)
    {
        DateTime? newest = null;
        var pending = new Stack<string>();
        pending.Push(projectRoot);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            var dirName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));

            if (dirName.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            if (ArtifactFolderDefinitions.IsArtifactFolder(dirName)) continue;

            IEnumerable<string> files;
            IEnumerable<string> subdirs;
            try
            {
                files = Directory.EnumerateFiles(dir);
                subdirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var mtime = File.GetLastWriteTimeUtc(file);
                    if (newest is null || mtime > newest) newest = mtime;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // skip unreadable file
                }
            }

            foreach (var subdir in subdirs)
            {
                pending.Push(subdir);
            }
        }

        return newest;
    }
}
