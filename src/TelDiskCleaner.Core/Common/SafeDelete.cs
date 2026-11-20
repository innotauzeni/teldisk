namespace TelDiskCleaner.Core.Common;

public sealed record SkippedItem(string Path, string Reason);

public sealed record DeleteResult(long FreedBytes, IReadOnlyList<SkippedItem> Skipped);

public static class SafeDelete
{
    /// <summary>Deletes individual files, skipping ones that are locked or inaccessible.</summary>
    public static DeleteResult DeleteFiles(IEnumerable<string> paths)
    {
        long freedBytes = 0;
        var skipped = new List<SkippedItem>();

        foreach (var path in paths)
        {
            try
            {
                var length = new FileInfo(path).Length;
                File.Delete(path);
                freedBytes += length;
            }
            catch (IOException ex)
            {
                skipped.Add(new SkippedItem(path, ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                skipped.Add(new SkippedItem(path, ex.Message));
            }
        }

        return new DeleteResult(freedBytes, skipped);
    }

    /// <summary>
    /// Deletes a directory tree. Tries the fast recursive delete first; if that fails
    /// (e.g. a locked file partway through), falls back to a per-file walk that skips
    /// and reports anything it can't remove, rather than aborting the whole operation.
    /// </summary>
    public static DeleteResult DeleteDirectory(string path, long knownSize)
    {
        if (!Directory.Exists(path))
        {
            return new DeleteResult(0, new List<SkippedItem> { new(path, "Path does not exist.") });
        }

        try
        {
            Directory.Delete(path, recursive: true);
            return new DeleteResult(knownSize, Array.Empty<SkippedItem>());
        }
        catch (IOException)
        {
            return DeleteDirectoryPerFile(path);
        }
        catch (UnauthorizedAccessException)
        {
            return DeleteDirectoryPerFile(path);
        }
    }

    private static DeleteResult DeleteDirectoryPerFile(string root)
    {
        long freedBytes = 0;
        var skipped = new List<SkippedItem>();
        var directories = new Stack<string>();
        directories.Push(root);
        var allDirectories = new List<string>();

        while (directories.Count > 0)
        {
            var dir = directories.Pop();
            allDirectories.Add(dir);

            IEnumerable<string> files;
            IEnumerable<string> subdirs;
            try
            {
                files = Directory.EnumerateFiles(dir);
                subdirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedItem(dir, ex.Message));
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var length = new FileInfo(file).Length;
                    File.Delete(file);
                    freedBytes += length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(new SkippedItem(file, ex.Message));
                }
            }

            foreach (var subdir in subdirs)
            {
                directories.Push(subdir);
            }
        }

        // Remove emptied directories, deepest first.
        foreach (var dir in allDirectories.OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedItem(dir, ex.Message));
            }
        }

        return new DeleteResult(freedBytes, skipped);
    }
}
