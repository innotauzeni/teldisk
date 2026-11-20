using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.TempCleanup;

public sealed class TempCleanupService
{
    public const string RecycleBinPseudoPath = "recycle-bin://";

    public List<TempLocation> GetLocations() => new()
    {
        new TempLocation { Name = "User Temp (%TEMP%)", Path = Path.GetTempPath(), RequiresElevation = false },
        new TempLocation { Name = "Windows Temp", Path = @"C:\Windows\Temp", RequiresElevation = true },
        new TempLocation { Name = "Recycle Bin", Path = RecycleBinPseudoPath, RequiresElevation = false },
    };

    public void ComputeSize(TempLocation location, CancellationToken cancellationToken)
    {
        location.SizeBytes = location.Path == RecycleBinPseudoPath
            ? RecycleBinInterop.GetSize()
            : DirectoryExistsAndReadable(location.Path)
                ? DirectorySizeCalculator.ComputeSize(location.Path, cancellationToken)
                : 0;
    }

    public DeleteResult Clean(TempLocation location)
    {
        if (location.Path == RecycleBinPseudoPath)
        {
            var freed = location.SizeBytes;
            var success = RecycleBinInterop.Empty();
            return success
                ? new DeleteResult(freed, Array.Empty<SkippedItem>())
                : new DeleteResult(0, new List<SkippedItem> { new(location.Path, "Failed to empty Recycle Bin.") });
        }

        return CleanDirectoryContents(location.Path);
    }

    private static bool DirectoryExistsAndReadable(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes everything inside a temp folder without deleting the folder itself
    /// (it's a well-known system path, not something we should remove).
    /// </summary>
    private static DeleteResult CleanDirectoryContents(string path)
    {
        long freedBytes = 0;
        var skipped = new List<SkippedItem>();

        if (!DirectoryExistsAndReadable(path))
        {
            return new DeleteResult(0, skipped);
        }

        IEnumerable<string> files;
        IEnumerable<string> directories;
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
        freedBytes += fileResult.FreedBytes;
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
