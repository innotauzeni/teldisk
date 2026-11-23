using System.IO;
using TelDiskCleaner.Core.Backup;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Configuration;
using TelDiskCleaner.Core.Licensing;
using TelDiskCleaner.Core.Recovery;
using TelDiskCleaner.Core.TempCleanup;

namespace TelDiskCleaner.App;

/// <summary>Runs scheduled work (started by Windows Task Scheduler) without showing a window.</summary>
internal static class HeadlessTasks
{
    public static bool IsHeadless(string[] args) => args.Contains("--auto-clean") || args.Contains("--backup");

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            using var license = LicenseManager.CreateDefault();
            if (!license.LoadFromDisk().HasFullAccess && !(await license.RefreshAsync()).HasFullAccess)
            {
                Log("Skipped scheduled task: it needs TelDiskCleaner Pro or the free trial.");
                return 2;
            }

            if (args.Contains("--auto-clean"))
            {
                var purged = RecoveryVault.CreateDefault().PurgeExpired();
                var result = new QuickCleaner(new TempCleanupService(), AdminHelper.IsAdministrator()).CleanDefaults(CancellationToken.None);
                JsonFileStore.Save(QuickCleaner.StatusPath, new AutoCleanStatus
                {
                    RunUtc = DateTime.UtcNow,
                    FreedBytes = result.FreedBytes + purged,
                    SkippedItems = result.Skipped.Count,
                });
                Log($"Auto-clean freed {SizeFormatter.Format(result.FreedBytes + purged)}; {result.Skipped.Count} item(s) skipped.");
                return 0;
            }

            var index = Array.IndexOf(args, "--backup");
            var jobId = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            var job = AppSettings.Load().BackupJobs.FirstOrDefault(j => j.Id == jobId);
            if (job is null)
            {
                Log($"Scheduled backup {jobId} no longer exists.");
                return 1;
            }

            var backup = await new BackupService().RunAsync(job, null, CancellationToken.None);
            BackupStatusStore.Record(job.Id, new BackupRunStatus { RunUtc = DateTime.UtcNow, Success = backup.Errors.Count == 0, Summary = backup.Summary });
            Log($"Backup \"{job.Name}\": {backup.Summary}");
            return backup.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log($"Scheduled task failed: {ex.Message}");
            return 1;
        }
    }

    private static void Log(string message)
    {
        try
        {
            var path = AppPaths.File("background.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // logging must never stop the task
        }
    }
}
