using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Configuration;

namespace TelDiskCleaner.Core.ProjectCleanup;

public sealed class ProjectCleanupService
{
    private readonly ProjectScanner _scanner = new();
    private readonly ActivityDetector _activityDetector = new();

    /// <summary>
    /// Finds projects with reclaimable artifact folders under the given roots, determines
    /// each one's last activity (git commit date, falling back to file mtime), and returns
    /// only the ones older than <paramref name="thresholdDays"/> and not on the ignore list.
    /// Pinned/ignored projects are still returned (flagged IsPinned) so the UI can show them
    /// as excluded rather than silently hiding them.
    /// </summary>
    public async Task<List<ProjectCandidate>> FindCandidatesAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var rawCandidates = _scanner.ScanForArtifacts(settings.ProjectRoots, cancellationToken);
        var results = new List<ProjectCandidate>();

        foreach (var candidate in rawCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activity = await _activityDetector.DetectAsync(candidate.ProjectRoot).ConfigureAwait(false);
            var isPinned = settings.IsIgnored(candidate.ProjectRoot);

            var isStale = activity.LastActivityUtc is null
                || (DateTime.UtcNow - activity.LastActivityUtc.Value).TotalDays > settings.StalenessThresholdDays;

            if (!isStale && !isPinned) continue; // only surface stale (or explicitly pinned, for visibility) projects

            results.Add(new ProjectCandidate
            {
                ProjectRoot = candidate.ProjectRoot,
                Artifacts = candidate.Artifacts,
                LastActivityUtc = activity.LastActivityUtc,
                ActivitySource = activity.Source,
                IsPinned = isPinned,
            });
        }

        return results.OrderByDescending(c => c.TotalArtifactSize).ToList();
    }

    public DeleteResult DeleteArtifacts(IEnumerable<ArtifactMatch> artifacts)
    {
        long freedBytes = 0;
        var skipped = new List<SkippedItem>();

        foreach (var artifact in artifacts)
        {
            var result = SafeDelete.DeleteDirectory(artifact.Path, artifact.SizeBytes);
            freedBytes += result.FreedBytes;
            skipped.AddRange(result.Skipped);
        }

        return new DeleteResult(freedBytes, skipped);
    }
}
