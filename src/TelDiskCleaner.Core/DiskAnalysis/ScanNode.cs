namespace TelDiskCleaner.Core.DiskAnalysis;

public sealed class ScanNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public long TotalSize { get; set; }
    public bool AccessDenied { get; set; }
    public List<ScanNode> Children { get; } = new();

    public void SortChildrenBySizeDescending()
    {
        Children.Sort((a, b) => b.TotalSize.CompareTo(a.TotalSize));
    }
}
