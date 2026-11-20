namespace TelDiskCleaner.Core.ProjectCleanup;

public enum ProjectActivitySource
{
    Git,
    FileModifiedTime,
    Unknown,
}

public sealed record ArtifactMatch(ArtifactKind Kind, string Path, long SizeBytes);

public sealed class ProjectCandidate
{
    public required string ProjectRoot { get; init; }
    public required List<ArtifactMatch> Artifacts { get; init; }
    public required DateTime? LastActivityUtc { get; init; }
    public required ProjectActivitySource ActivitySource { get; init; }
    public bool IsPinned { get; set; }

    public long TotalArtifactSize => Artifacts.Sum(a => a.SizeBytes);
}
