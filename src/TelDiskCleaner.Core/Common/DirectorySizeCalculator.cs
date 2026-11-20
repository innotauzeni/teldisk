namespace TelDiskCleaner.Core.Common;

public static class DirectorySizeCalculator
{
    private const int CancellationCheckInterval = 200;

    /// <summary>Sums file sizes under a directory without building a full tree model.</summary>
    public static long ComputeSize(string path, CancellationToken cancellationToken)
    {
        long total = 0;
        int sinceCheck = 0;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            sinceCheck++;
            if (sinceCheck >= CancellationCheckInterval)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sinceCheck = 0;
            }

            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                    if (entry is DirectoryInfo)
                    {
                        pending.Push(entry.FullName);
                    }
                    else if (entry is FileInfo fileInfo)
                    {
                        try
                        {
                            total += fileInfo.Length;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // skip unreadable file
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // skip inaccessible directory
            }
        }

        return total;
    }
}
