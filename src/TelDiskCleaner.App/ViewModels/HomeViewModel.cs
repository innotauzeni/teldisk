using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Licensing;
using TelDiskCleaner.Core.Processes;
using TelDiskCleaner.Core.Recovery;
using TelDiskCleaner.Core.Startup;
using TelDiskCleaner.Core.TempCleanup;

namespace TelDiskCleaner.App.ViewModels;

public sealed class DriveCard
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required double UsedPercent { get; init; }
    public required bool IsLow { get; init; }
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly LicenseManager _license;
    private readonly RecoveryVault _vault;
    private readonly ScheduledTaskService _tasks;
    private readonly QuickCleaner _cleaner = new(new TempCleanupService(), AdminHelper.IsAdministrator());
    private readonly ProcessService _processes = new();

    public ObservableCollection<DriveCard> Drives { get; } = new();

    [ObservableProperty] private string _memorySummary = string.Empty;
    [ObservableProperty] private double _memoryPercent;
    [ObservableProperty] private string _junkSummary = "Looking for junk files...";
    [ObservableProperty] private string _startupSummary = string.Empty;
    [ObservableProperty] private string _recoverySummary = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _lastAutoClean = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpgradeHint))]
    private bool _hasFullAccess;

    public bool ShowUpgradeHint => !HasFullAccess;

    [RelayCommand]
    private void Upgrade() => RequestUpgrade?.Invoke();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoCleanButtonText), nameof(AutoCleanSummary))]
    private bool _autoCleanOn;

    public HomeViewModel(LicenseManager license, RecoveryVault vault, ScheduledTaskService tasks)
    {
        _license = license;
        _vault = vault;
        _tasks = tasks;
        HasFullAccess = license.Status.HasFullAccess;
        license.StatusChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => HasFullAccess = _license.Status.HasFullAccess);
        _ = RefreshAsync();
    }

    /// <summary>Set by the main window so locked buttons can open the Licence page.</summary>
    public Action? RequestUpgrade { get; set; }

    public long JunkBytes { get; private set; }

    public string AutoCleanButtonText => AutoCleanOn ? "Turn off weekly auto-clean" : "Turn on weekly auto-clean";

    public string AutoCleanSummary => AutoCleanOn
        ? "Junk is cleaned automatically every Sunday (or at the next start-up if the PC was off)."
        : "Auto-clean is off.";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            var used = drive.TotalSize - drive.TotalFreeSpace;
            var percent = drive.TotalSize > 0 ? 100.0 * used / drive.TotalSize : 0;
            Drives.Add(new DriveCard
            {
                Title = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})",
                Detail = $"{SizeFormatter.Format(drive.TotalFreeSpace)} free of {SizeFormatter.Format(drive.TotalSize)}",
                UsedPercent = percent,
                IsLow = drive.TotalFreeSpace < drive.TotalSize / 10,
            });
        }

        var memory = _processes.GetMemoryStatus();
        MemoryPercent = memory.UsedPercent;
        MemorySummary = $"{SizeFormatter.Format(memory.UsedBytes)} of {SizeFormatter.Format(memory.TotalBytes)} in use ({memory.UsedPercent:0}%)";

        try
        {
            var startup = new StartupService().GetEntries();
            StartupSummary = $"{startup.Count(e => e.IsEnabled)} app(s) start with Windows";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            StartupSummary = string.Empty;
        }

        var vaultItems = _vault.GetItems();
        RecoverySummary = vaultItems.Count == 0
            ? "No removed files waiting in Recovery"
            : $"{vaultItems.Count} removed file(s), {SizeFormatter.Format(vaultItems.Sum(i => i.SizeBytes))}, can still be restored";

        var last = JsonFileStore.Load<AutoCleanStatus>(QuickCleaner.StatusPath);
        LastAutoClean = last.RunUtc == default
            ? string.Empty
            : $"Last auto-clean: {last.RunUtc.ToLocalTime():d MMM yyyy HH:mm}, freed {SizeFormatter.Format(last.FreedBytes)}";

        AutoCleanOn = await _tasks.ExistsAsync(ScheduledTaskService.AutoCleanTaskName);

        JunkSummary = "Looking for junk files...";
        JunkBytes = await Task.Run(() => _cleaner.MeasureJunk(CancellationToken.None));
        JunkSummary = $"{SizeFormatter.Format(JunkBytes)} of junk can be removed safely";
    }

    public async Task CleanNowAsync()
    {
        if (!HasFullAccess)
        {
            RequestUpgrade?.Invoke();
            return;
        }

        IsBusy = true;
        StatusMessage = "Cleaning junk files...";
        var result = await Task.Run(() => _cleaner.CleanDefaults(CancellationToken.None));
        StatusMessage = $"Freed {SizeFormatter.Format(result.FreedBytes)}" +
            (result.Skipped.Count > 0 ? $"; {result.Skipped.Count:N0} file(s) were in use and skipped." : ".");
        IsBusy = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ToggleAutoCleanAsync()
    {
        if (!HasFullAccess)
        {
            RequestUpgrade?.Invoke();
            return;
        }

        IsBusy = true;
        ProcessResult result;
        if (AutoCleanOn)
        {
            result = await _tasks.DeleteAsync(ScheduledTaskService.AutoCleanTaskName);
        }
        else
        {
            result = await _tasks.CreateAsync(ScheduledTaskService.AutoCleanTaskName, Environment.ProcessPath!,
                "--auto-clean", TaskSchedule.Weekly, "Weekly junk clean-up by TelDiskCleaner.");
        }

        AutoCleanOn = await _tasks.ExistsAsync(ScheduledTaskService.AutoCleanTaskName);
        StatusMessage = result.Success
            ? (AutoCleanOn ? "Weekly auto-clean is on. Keep TelDiskCleaner in the same folder so the schedule can find it." : "Weekly auto-clean is off.")
            : $"Windows Task Scheduler refused the change: {result.StandardError.Trim()}";
        IsBusy = false;
    }
}
