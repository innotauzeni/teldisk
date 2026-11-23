using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.TempCleanup;

public sealed class TempCleanupService
{
    /// <summary>Only locations that exist on this PC are returned (the Recycle Bin always is).</summary>
    public List<TempLocation> GetLocations() =>
        CleanupCatalog.Build().Where(l => l.IsRecycleBin || l.Paths.Count > 0).ToList();

    public void ComputeSize(TempLocation location, CancellationToken cancellationToken)
    {
        location.SizeBytes = location.IsRecycleBin
            ? RecycleBinInterop.GetSize()
            : location.Paths.Where(Directory.Exists).Sum(p => DirectorySizeCalculator.ComputeSize(p, cancellationToken));
    }

    public DeleteResult Clean(TempLocation location)
    {
        if (location.IsRecycleBin)
        {
            var freed = location.SizeBytes;
            return RecycleBinInterop.Empty()
                ? new DeleteResult(freed, Array.Empty<SkippedItem>())
                : new DeleteResult(0, new List<SkippedItem> { new("Recycle Bin", "Failed to empty Recycle Bin.") });
        }

        long freedBytes = 0;
        var skipped = new List<SkippedItem>();
        foreach (var path in location.Paths)
        {
            var result = CleanDirectoryContents(path);
            freedBytes += result.FreedBytes;
            skipped.AddRange(result.Skipped);
        }

        return new DeleteResult(freedBytes, skipped);
    }

    /// <summary>
    /// Deletes everything inside a folder without deleting the folder itself
    /// (temp and cache folders are expected to exist by the apps that own them).
    /// </summary>
    private static DeleteResult CleanDirectoryContents(string path)
    {
        var skipped = new List<SkippedItem>();
        if (!Directory.Exists(path))
        {
            return new DeleteResult(0, skipped);
        }

        List<string> files;
        List<string> directories;
        try
        {
            files = Directory.EnumerateFiles(path).ToList();
            directories = Directory.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new SkippedItem(path, ex.Message));
            return new DeleteResult(0, skipped);
        }

        var fileResult = SafeDelete.DeleteFiles(files);
        long freedBytes = fileResult.FreedBytes;
        skipped.AddRange(fileResult.Skipped);

        foreach (var dir in directories)
        {
            var size = DirectorySizeCalculator.ComputeSize(dir, CancellationToken.None);
            var dirResult = SafeDelete.DeleteDirectory(dir, size);
            freedBytes += dirResult.FreedBytes;
            skipped.AddRange(dirResult.Skipped);
        }

        return new DeleteResult(freedBytes, skipped);
    }
}
