using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.TempCleanup;

public sealed class AutoCleanStatus
{
    public DateTime RunUtc { get; set; }
    public long FreedBytes { get; set; }
    public int SkippedItems { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// One-click cleaning of the junk locations that are ticked by default in Junk Cleanup
/// (never the slow-to-redownload caches), used by the dashboard and the weekly auto-clean.
/// </summary>
public sealed class QuickCleaner
{
    private readonly TempCleanupService _service;
    private readonly bool _isAdministrator;

    public QuickCleaner(TempCleanupService service, bool isAdministrator)
    {
        _service = service;
        _isAdministrator = isAdministrator;
    }

    public static string StatusPath => AppPaths.File("autoclean-status.json");

    public List<TempLocation> GetDefaultLocations() =>
        _service.GetLocations()
            .Where(l => l.SelectedByDefault && (!l.RequiresElevation || _isAdministrator))
            .ToList();

    public long MeasureJunk(CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var location in GetDefaultLocations())
        {
            try
            {
                _service.ComputeSize(location, cancellationToken);
                total += location.SizeBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unreadable location: leave it out of the total
            }
        }
        return total;
    }

    public DeleteResult CleanDefaults(CancellationToken cancellationToken)
    {
        long freed = 0;
        var skipped = new List<SkippedItem>();
        foreach (var location in GetDefaultLocations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _service.ComputeSize(location, cancellationToken);
                var result = _service.Clean(location);
                freed += result.FreedBytes;
                skipped.AddRange(result.Skipped);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedItem(location.Name, ex.Message));
            }
        }
        return new DeleteResult(freed, skipped);
    }
}
