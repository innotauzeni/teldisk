using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Backup;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Configuration;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class BackupJobViewModel : ObservableObject
{
    public BackupJob Job { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _destination;
    [ObservableProperty] private int _keepVersions;
    [ObservableProperty] private string _exclusionsText;
    [ObservableProperty] private bool _runDaily;
    [ObservableProperty] private string _lastRun = "Not backed up yet.";

    public ObservableCollection<string> Sources { get; }

    public BackupJobViewModel(BackupJob job)
    {
        Job = job;
        _name = job.Name;
        _destination = job.Destination;
        _keepVersions = job.KeepVersions;
        _exclusionsText = string.Join(", ", job.ExcludedFolders);
        _runDaily = job.RunDaily;
        Sources = new ObservableCollection<string>(job.Sources);
    }

    public void ApplyToJob()
    {
        Job.Name = Name.Trim();
        Job.Destination = Destination.Trim();
        Job.KeepVersions = KeepVersions;
        Job.RunDaily = RunDaily;
        Job.Sources = Sources.ToList();
        Job.ExcludedFolders = ExclusionsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (string.IsNullOrEmpty(Job.FolderName))
        {
            Job.FolderName = $"TelDiskCleaner - {BackupService.SafeName(Job.Name)} ({Job.Id[..4]})";
        }
    }
}

public sealed partial class BackupViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly BackupService _backup = new();
    private readonly ScheduledTaskService _tasks;
    private CancellationTokenSource? _cts;

    public ObservableCollection<BackupJobViewModel> Jobs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(HasNoSelection))]
    private BackupJobViewModel? _selectedJob;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public BackupViewModel(AppSettings settings, ScheduledTaskService tasks)
    {
        _settings = settings;
        _tasks = tasks;
        foreach (var job in settings.BackupJobs) Jobs.Add(new BackupJobViewModel(job));
        LoadLastRuns();
        SelectedJob = Jobs.FirstOrDefault();
    }

    public bool HasSelection => SelectedJob is not null;
    public bool HasNoSelection => SelectedJob is null;

    [RelayCommand]
    private void NewJob()
    {
        var job = new BackupJob
        {
            Name = "My documents",
            Sources = { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        };
        var vm = new BackupJobViewModel(job);
        Jobs.Add(vm);
        SelectedJob = vm;
        StatusMessage = "Choose the folders to back up and where to save them (a USB or external drive is best), then click Save.";
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedJob is null) return;
        var job = SelectedJob.Job;
        _settings.BackupJobs.RemoveAll(j => j.Id == job.Id);
        _settings.Save();
        await _tasks.DeleteAsync(ScheduledTaskService.BackupTaskName(job.Id));
        Jobs.Remove(SelectedJob);
        SelectedJob = Jobs.FirstOrDefault();
        StatusMessage = "Backup removed. Files already backed up were left on the backup drive.";
    }

    public void AddSource(string path)
    {
        if (SelectedJob is null || SelectedJob.Sources.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        SelectedJob.Sources.Add(path);
    }

    [RelayCommand]
    private void RemoveSource(string path) => SelectedJob?.Sources.Remove(path);

    [RelayCommand]
    private Task SaveAsync() => SaveSelectedAsync();

    private async Task<bool> SaveSelectedAsync()
    {
        if (SelectedJob is null) return false;
        SelectedJob.ApplyToJob();
        var job = SelectedJob.Job;

        var problem = BackupService.Validate(job);
        if (problem is not null)
        {
            StatusMessage = problem;
            return false;
        }

        if (!_settings.BackupJobs.Any(j => j.Id == job.Id)) _settings.BackupJobs.Add(job);
        _settings.Save();

        var taskName = ScheduledTaskService.BackupTaskName(job.Id);
        var schedule = job.RunDaily
            ? await _tasks.CreateAsync(taskName, Environment.ProcessPath!, $"--backup {job.Id}", TaskSchedule.Daily, $"Daily backup \"{job.Name}\" by TelDiskCleaner.")
            : await _tasks.DeleteAsync(taskName);

        StatusMessage = job.RunDaily && !schedule.Success
            ? $"Saved, but the daily schedule couldn't be set: {schedule.StandardError.Trim()}"
            : job.RunDaily
                ? "Saved. This backup runs every day at 12:00, or at the next start-up if the PC was off."
                : "Saved.";
        return true;
    }

    [RelayCommand]
    private async Task BackUpNowAsync()
    {
        if (!await SaveSelectedAsync()) return;
        var vm = SelectedJob!;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<BackupProgress>(p =>
            StatusMessage = $"Backing up... {p.FilesChecked:N0} files checked, {p.FilesCopied:N0} copied ({SizeFormatter.Format(p.BytesCopied)})");

        try
        {
            var result = await _backup.RunAsync(vm.Job, progress, _cts.Token);
            BackupStatusStore.Record(vm.Job.Id, new BackupRunStatus { RunUtc = DateTime.UtcNow, Success = result.Errors.Count == 0, Summary = result.Summary });
            StatusMessage = "Backup finished. " + result.Summary;
            LoadLastRuns();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Backup cancelled. Files copied so far are kept; the next backup continues from there.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreAsync(string? targetFolder)
    {
        if (SelectedJob is null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<BackupProgress>(p => StatusMessage = $"Restoring... {p.FilesCopied:N0} file(s) restored");

        try
        {
            var result = await _backup.RestoreAsync(SelectedJob.Job, targetFolder, progress, _cts.Token);
            StatusMessage = "Restore finished. " + result.Summary;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Restore cancelled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenBackupFolder()
    {
        if (SelectedJob is null) return;
        var folder = BackupService.JobFolder(SelectedJob.Job);
        if (string.IsNullOrEmpty(SelectedJob.Job.FolderName) || !Directory.Exists(folder))
        {
            StatusMessage = "There is no backup folder yet. Click Back up now first.";
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void LoadLastRuns()
    {
        var statuses = BackupStatusStore.Load();
        foreach (var vm in Jobs)
        {
            if (statuses.TryGetValue(vm.Job.Id, out var status))
            {
                vm.LastRun = $"Last backup {status.RunUtc.ToLocalTime():d MMM yyyy HH:mm}: {status.Summary}";
            }
        }
    }
}
