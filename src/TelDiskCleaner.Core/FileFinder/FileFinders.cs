using System.Security.Cryptography;

namespace TelDiskCleaner.Core.FileFinder;

public sealed record FoundFile(string Path, long SizeBytes, DateTime ModifiedUtc, string? ProtectedReason)
{
    public static FoundFile From(FileInfo file) =>
        new(file.FullName, file.Length, file.LastWriteTimeUtc, FileProtection.GetReason(file));
}

public sealed record DuplicateGroup(long SizeBytes, IReadOnlyList<FoundFile> Files)
{
    public long WastedBytes => SizeBytes * (Files.Count - 1);
}

public sealed record FinderProgress(int FilesScanned, string Stage);

public static class LargeFileFinder
{
    /// <summary>The largest files at or above <paramref name="minBytes"/>, biggest first.</summary>
    public static List<FoundFile> Find(string root, long minBytes, int maxResults, CancellationToken cancellationToken,
        IProgress<FinderProgress>? progress = null)
    {
        var largest = new PriorityQueue<FileInfo, long>();
        int scanned = 0;

        foreach (var file in FileWalker.EnumerateFiles(root, _ => false, cancellationToken))
        {
            if (++scanned % 2000 == 0) progress?.Report(new FinderProgress(scanned, "Scanning"));
            if (file.Length < minBytes) continue;

            largest.Enqueue(file, file.Length);
            if (largest.Count > maxResults) largest.Dequeue();
        }

        var results = new List<FoundFile>();
        while (largest.TryDequeue(out var file, out _)) results.Add(FoundFile.From(file));
        results.Reverse();
        progress?.Report(new FinderProgress(scanned, "Done"));
        return results;
    }
}

public static class DuplicateFinder
{
    private const int SampleBytes = 64 * 1024;

    /// <summary>
    /// Files with identical content. Compares size first, then a hash of the first 64 KB, and
    /// only fully hashes files that still match, so most files are never read in full.
    /// </summary>
    public static List<DuplicateGroup> Find(string root, long minBytes, CancellationToken cancellationToken,
        IProgress<FinderProgress>? progress = null)
    {
        int scanned = 0;
        var bySize = new Dictionary<long, List<FileInfo>>();

        foreach (var file in FileWalker.EnumerateFiles(root, _ => false, cancellationToken))
        {
            if (++scanned % 2000 == 0) progress?.Report(new FinderProgress(scanned, "Scanning"));
            if (file.Length < minBytes || FileWalker.IsCloudPlaceholder(file.Attributes) || FileProtection.GetReason(file) is not null)
            {
                continue;
            }

            if (!bySize.TryGetValue(file.Length, out var list)) bySize[file.Length] = list = new List<FileInfo>();
            list.Add(file);
        }

        var groups = new List<DuplicateGroup>();
        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        int checkedGroups = 0;

        foreach (var (size, sameSize) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new FinderProgress(scanned, $"Comparing files ({++checkedGroups:N0} of {candidates.Count:N0})"));

            foreach (var sameStart in GroupByHash(sameSize, f => HashSample(f), cancellationToken))
            {
                var fullMatches = size <= SampleBytes
                    ? new[] { sameStart }
                    : GroupByHash(sameStart, f => HashFull(f, cancellationToken), cancellationToken);

                foreach (var match in fullMatches)
                {
                    groups.Add(new DuplicateGroup(size, match.Select(FoundFile.From).OrderBy(f => f.ModifiedUtc).ToList()));
                }
            }
        }

        progress?.Report(new FinderProgress(scanned, "Done"));
        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static IEnumerable<List<FileInfo>> GroupByHash(IEnumerable<FileInfo> files, Func<FileInfo, string?> hash, CancellationToken cancellationToken)
    {
        var byHash = new Dictionary<string, List<FileInfo>>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = hash(file);
            if (key is null) continue;
            if (!byHash.TryGetValue(key, out var list)) byHash[key] = list = new List<FileInfo>();
            list.Add(file);
        }
        return byHash.Values.Where(l => l.Count > 1);
    }

    private static string? HashSample(FileInfo file)
    {
        try
        {
            using var stream = Open(file);
            var buffer = new byte[SampleBytes];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? HashFull(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = Open(file);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileStream Open(FileInfo file) =>
        new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
}
