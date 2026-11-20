using TelDiskCleaner.Core.TempCleanup;

namespace TelDiskCleaner.Core.Tests;

public class TempCleanupServiceTests : IDisposable
{
    private readonly string _root;

    public TempCleanupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TelDiskCleanerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ComputeSize_SumsFilesAndSubdirectories()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.tmp"), new byte[100]);
        var subdir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(subdir);
        File.WriteAllBytes(Path.Combine(subdir, "b.tmp"), new byte[200]);

        var location = new TempLocation { Name = "Test", Path = _root, RequiresElevation = false };
        var service = new TempCleanupService();
        service.ComputeSize(location, CancellationToken.None);

        Assert.Equal(300, location.SizeBytes);
    }

    [Fact]
    public void Clean_RemovesContentsButKeepsRootFolder()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.tmp"), new byte[100]);
        var subdir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(subdir);
        File.WriteAllBytes(Path.Combine(subdir, "b.tmp"), new byte[200]);

        var location = new TempLocation { Name = "Test", Path = _root, RequiresElevation = false };
        var service = new TempCleanupService();
        service.ComputeSize(location, CancellationToken.None);

        var result = service.Clean(location);

        Assert.Equal(300, result.FreedBytes);
        Assert.Empty(result.Skipped);
        Assert.True(Directory.Exists(_root), "the root temp folder itself should not be deleted");
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void Clean_SkipsLockedFileWithoutAbortingTheWholeBatch()
    {
        var lockedPath = Path.Combine(_root, "locked.tmp");
        File.WriteAllBytes(lockedPath, new byte[50]);
        File.WriteAllBytes(Path.Combine(_root, "unlocked.tmp"), new byte[150]);

        var location = new TempLocation { Name = "Test", Path = _root, RequiresElevation = false };
        var service = new TempCleanupService();
        service.ComputeSize(location, CancellationToken.None);

        using var lockHandle = File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = service.Clean(location);

        Assert.Equal(150, result.FreedBytes);
        Assert.Single(result.Skipped);
        Assert.False(File.Exists(Path.Combine(_root, "unlocked.tmp")));
        Assert.True(File.Exists(lockedPath));
    }
}
