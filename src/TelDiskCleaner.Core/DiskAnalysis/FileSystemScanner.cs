namespace TelDiskCleaner.Core.DiskAnalysis;

public sealed class FileSystemScanner
{
    private const int CancellationCheckInterval = 200;
    private const int ProgressReportIntervalMs = 100;

    /// <summary>
    /// Builds a full tree of the given folder. Uses an explicit stack (not recursive calls)
    /// so arbitrarily deep trees (e.g. nested node_modules) can't overflow the call stack,
    /// and so cancellation can be checked between directories without unwinding anything.
    /// </summary>
    public ScanNode Scan(string rootPath, CancellationToken cancellationToken, IProgress<ScanProgress>? progress = null)
    {
        var trimmedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar);
        var root = new ScanNode
        {
            Name = Path.GetFileName(trimmedRoot) is { Length: > 0 } name ? name : rootPath,
            FullPath = rootPath,
            IsDirectory = true,
        };

        long filesScanned = 0;
        long bytesScanned = 0;
        int directoriesSinceCheck = 0;
        var lastReport = DateTime.UtcNow;

        var pending = new Stack<ScanNode>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            directoriesSinceCheck++;
            if (directoriesSinceCheck >= CancellationCheckInterval)
            {
                cancellationToken.ThrowIfCancellationRequested();
                directoriesSinceCheck = 0;
            }

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(current.FullPath).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                current.AccessDenied = true;
                continue;
            }

            try
            {
                foreach (var entry in entries)
                {
                    // Skip reparse points (symlinks/junctions): don't follow them, don't count
                    // their size, so we never double-count or loop forever on a cyclic link.
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        var childNode = new ScanNode
                        {
                            Name = entry.Name,
                            FullPath = entry.FullName,
                            IsDirectory = true,
                        };
                        current.Children.Add(childNode);
                        pending.Push(childNode);
                    }
                    else if (entry is FileInfo fileInfo)
                    {
                        long length;
                        try
                        {
                            length = fileInfo.Length;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            continue;
                        }

                        current.Children.Add(new ScanNode
                        {
                            Name = entry.Name,
                            FullPath = entry.FullName,
                            IsDirectory = false,
                            TotalSize = length,
                        });

                        filesScanned++;
                        bytesScanned += length;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Enumeration can fail partway through a directory; keep whatever we saw.
                current.AccessDenied = true;
            }

            if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds >= ProgressReportIntervalMs)
            {
                progress.Report(new ScanProgress(filesScanned, bytesScanned, current.FullPath));
                lastReport = DateTime.UtcNow;
            }
        }

        RollUpSizes(root);
        progress?.Report(new ScanProgress(filesScanned, bytesScanned, rootPath));
        return root;
    }

    public Task<ScanNode> ScanAsync(string rootPath, CancellationToken cancellationToken, IProgress<ScanProgress>? progress = null) =>
        Task.Run(() => Scan(rootPath, cancellationToken, progress), cancellationToken);

    private static void RollUpSizes(ScanNode node)
    {
        if (!node.IsDirectory) return;

        long total = 0;
        foreach (var child in node.Children)
        {
            if (child.IsDirectory)
            {
                RollUpSizes(child);
            }
            total += child.TotalSize;
        }

        node.TotalSize = total;
        node.SortChildrenBySizeDescending();
    }
}
