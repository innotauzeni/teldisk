namespace TelDiskCleaner.Core.TempCleanup;

public sealed class TempLocation
{
    public required string Name { get; init; }
    public required string Category { get; init; }

    /// <summary>Folders whose contents are cleaned; the folders themselves are kept.</summary>
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    public bool IsRecycleBin { get; init; }
    public bool RequiresElevation { get; init; }
    public bool SelectedByDefault { get; init; } = true;
    public string Note { get; init; } = string.Empty;
    public long SizeBytes { get; set; }
}
