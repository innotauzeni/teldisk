using TelDiskCleaner.Core.DiskAnalysis;

namespace TelDiskCleaner.Core.Tests;

public class FileSystemScannerTests : IDisposable
{
    private readonly string _root;

    public FileSystemScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TelDiskCleanerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_SumsFileSizesAcrossSubdirectories()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.txt"), new byte[100]);
        var subdir = Path.Combine(_root, "sub");
        Directory.CreateDirectory(subdir);
        File.WriteAllBytes(Path.Combine(subdir, "b.txt"), new byte[250]);

        var scanner = new FileSystemScanner();
        var result = scanner.Scan(_root, CancellationToken.None);

        Assert.Equal(350, result.TotalSize);
    }

    [Fact]
    public void Scan_ChildrenAreSortedLargestFirst()
    {
        File.WriteAllBytes(Path.Combine(_root, "small.txt"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_root, "large.txt"), new byte[1000]);

        var scanner = new FileSystemScanner();
        var result = scanner.Scan(_root, CancellationToken.None);

        Assert.Equal("large.txt", result.Children[0].Name);
        Assert.Equal("small.txt", result.Children[1].Name);
    }

    [Fact]
    public void Scan_Cancellation_ThrowsOperationCanceledException()
    {
        for (int i = 0; i < 500; i++)
        {
            var dir = Path.Combine(_root, $"dir{i}");
            Directory.CreateDirectory(dir);
        }

        var scanner = new FileSystemScanner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => scanner.Scan(_root, cts.Token));
    }
}
