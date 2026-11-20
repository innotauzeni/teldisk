namespace TelDiskCleaner.Core.DiskAnalysis;

public sealed class DiskAnalyzerService
{
    private readonly FileSystemScanner _scanner = new();

    public Task<ScanNode> ScanAsync(string rootPath, CancellationToken cancellationToken, IProgress<ScanProgress>? progress = null) =>
        _scanner.ScanAsync(rootPath, cancellationToken, progress);
}
