using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Recovery;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class VaultItemViewModel : ObservableObject
{
    public required VaultItem Item { get; init; }
    public required DateTime PurgeDateUtc { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public string Name => Path.GetFileName(Item.OriginalPath);
    public string OriginalFolder => Path.GetDirectoryName(Item.OriginalPath) ?? string.Empty;
    public string SizeDisplay => SizeFormatter.Format(Item.SizeBytes);
    public string RemovedDisplay => Item.DeletedUtc.ToLocalTime().ToString("d MMM yyyy HH:mm");
    public string PurgeDisplay => PurgeDateUtc.ToLocalTime().ToString("d MMM yyyy");
}

public sealed partial class RecoveryViewModel : ObservableObject
{
    private readonly RecoveryVault _vault;
    private readonly RestorePointService _restorePoints;

    public ObservableCollection<VaultItemViewModel> Items { get; } = new();
    public ObservableCollection<RestorePoint> RestorePoints { get; } = new();

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _restorePointStatus = string.Empty;
    [ObservableProperty] private string _totalDisplay = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public RecoveryViewModel(RecoveryVault vault, RestorePointService restorePoints)
    {
        _vault = vault;
        _restorePoints = restorePoints;
        Refresh();
    }

    public bool IsAdministrator { get; } = AdminHelper.IsAdministrator();

    public string RestorePointHelp => IsAdministrator
        ? "A restore point lets Windows roll system settings and programs back to how they were. Your documents are not affected."
        : "Restore points need administrator rights. Use \"Restart as administrator\" in the sidebar to create or list them.";

    [RelayCommand]
    public void Refresh()
    {
        Items.Clear();
        foreach (var item in _vault.GetItems())
        {
            Items.Add(new VaultItemViewModel { Item = item, PurgeDateUtc = _vault.PurgeDate(item) });
        }
        TotalDisplay = Items.Count == 0
            ? "Nothing to restore. Files you remove with File Finder appear here for 7 days."
            : $"{Items.Count} file(s), {SizeFormatter.Format(Items.Sum(i => i.Item.SizeBytes))}. This space is freed when they are deleted for good.";
    }

    public List<VaultItemViewModel> Selected() => Items.Where(i => i.IsSelected).ToList();

    [RelayCommand]
    private async Task RestoreSelectedAsync()
    {
        var selected = Selected();
        if (selected.Count == 0)
        {
            StatusMessage = "Tick the files you want back first.";
            return;
        }

        var restored = 0;
        var failures = new List<string>();
        await Task.Run(() =>
        {
            foreach (var item in selected)
            {
                try
                {
                    _vault.Restore(item.Item);
                    restored++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{item.Name}: {ex.Message}");
                }
            }
        });

        Refresh();
        StatusMessage = $"Restored {restored} file(s) to their original folders." +
            (failures.Count > 0 ? " Could not restore: " + string.Join("; ", failures) : string.Empty);
    }

    public async Task DeleteSelectedNowAsync()
    {
        var selected = Selected();
        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var item in selected)
            {
                try
                {
                    _vault.DeleteNow(item.Item);
                    freed += item.Item.SizeBytes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // still in use; it will be retried when the 7 days are up
                }
            }
        });

        Refresh();
        StatusMessage = $"Deleted for good; freed {SizeFormatter.Format(freed)}.";
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        IsBusy = true;
        RestorePointStatus = "Creating a restore point. Windows can take a minute or two...";
        var result = await _restorePoints.CreateAsync("Manual restore point");
        RestorePointStatus = result.Message;
        IsBusy = false;
        if (result.Created) await LoadRestorePointsAsync();
    }

    [RelayCommand]
    private async Task LoadRestorePointsAsync()
    {
        IsBusy = true;
        var (points, error) = await _restorePoints.ListAsync();
        RestorePoints.Clear();
        foreach (var point in points) RestorePoints.Add(point);
        if (error is not null) RestorePointStatus = error;
        else if (points.Count == 0) RestorePointStatus = "There are no restore points on this PC yet.";
        IsBusy = false;
    }

    [RelayCommand]
    private void OpenSystemRestore() => RestorePointService.OpenSystemRestore();
}
