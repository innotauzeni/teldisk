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

    private TempLocation Location(params string[] paths) =>
        new() { Name = "Test", Category = "Test", Paths = paths };

    [Fact]
    public void ComputeSize_SumsFilesAndSubdirectories()
    {
        File.WriteAllBytes(Path.Combine(_root, "a.tmp"), new byte[100]);
        var subdir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(subdir);
        File.WriteAllBytes(Path.Combine(subdir, "b.tmp"), new byte[200]);

        var location = Location(_root);
        new TempCleanupService().ComputeSize(location, CancellationToken.None);

        Assert.Equal(300, location.SizeBytes);
    }

    [Fact]
    public void ComputeSize_AddsUpEveryPath()
    {
        var first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        File.WriteAllBytes(Path.Combine(first, "a.bin"), new byte[40]);
        File.WriteAllBytes(Path.Combine(second, "b.bin"), new byte[60]);

        var location = Location(first, second);
        new TempCleanupService().ComputeSize(location, CancellationToken.None);

        Assert.Equal(100, location.SizeBytes);
    }

    [Fact]
    public void Clean_RemovesContentsButKeepsRootFolders()
    {
        var first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        File.WriteAllBytes(Path.Combine(first, "a.tmp"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(second, "nested"));
        File.WriteAllBytes(Path.Combine(second, "nested", "b.tmp"), new byte[200]);

        var service = new TempCleanupService();
        var location = Location(first, second);
        service.ComputeSize(location, CancellationToken.None);

        var result = service.Clean(location);

        Assert.Equal(300, result.FreedBytes);
        Assert.Empty(result.Skipped);
        Assert.True(Directory.Exists(first), "cache folders themselves must not be deleted");
        Assert.True(Directory.Exists(second));
        Assert.Empty(Directory.EnumerateFileSystemEntries(first));
        Assert.Empty(Directory.EnumerateFileSystemEntries(second));
    }

    [Fact]
    public void Clean_SkipsLockedFileWithoutAbortingTheWholeBatch()
    {
        var lockedPath = Path.Combine(_root, "locked.tmp");
        File.WriteAllBytes(lockedPath, new byte[50]);
        File.WriteAllBytes(Path.Combine(_root, "unlocked.tmp"), new byte[150]);

        var service = new TempCleanupService();
        var location = Location(_root);
        service.ComputeSize(location, CancellationToken.None);

        using var lockHandle = File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = service.Clean(location);

        Assert.Equal(150, result.FreedBytes);
        Assert.Single(result.Skipped);
        Assert.False(File.Exists(Path.Combine(_root, "unlocked.tmp")));
        Assert.True(File.Exists(lockedPath));
    }

    [Fact]
    public void GetLocations_OnlyListsFoldersThatExist()
    {
        var locations = new TempCleanupService().GetLocations();

        Assert.Contains(locations, l => l.IsRecycleBin);
        Assert.All(locations.Where(l => !l.IsRecycleBin), l =>
        {
            Assert.NotEmpty(l.Paths);
            Assert.All(l.Paths, p => Assert.True(Directory.Exists(p), p));
        });
    }
}
