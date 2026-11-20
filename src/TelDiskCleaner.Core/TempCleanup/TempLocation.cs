namespace TelDiskCleaner.Core.TempCleanup;

public sealed class TempLocation
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool RequiresElevation { get; init; }
    public long SizeBytes { get; set; }
}
