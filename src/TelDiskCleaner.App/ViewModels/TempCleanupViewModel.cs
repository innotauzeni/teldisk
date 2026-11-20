using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.TempCleanup;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class TempLocationViewModel : ObservableObject
{
    public required TempLocation Location { get; init; }

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _sizeDisplay = "Calculating...";

    public string Name => Location.Name;
    public bool RequiresElevation => Location.RequiresElevation;

    public void RefreshSizeDisplay() => SizeDisplay = SizeFormatter.Format(Location.SizeBytes);
}

public sealed partial class TempCleanupViewModel : ObservableObject
{
    private readonly TempCleanupService _service = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<TempLocationViewModel> Locations { get; } = new();

    public TempCleanupViewModel()
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Calculating reclaimable space...";
        Locations.Clear();

        var locations = _service.GetLocations();
        foreach (var location in locations)
        {
            Locations.Add(new TempLocationViewModel { Location = location });
        }

        await Task.Run(() =>
        {
            foreach (var location in locations)
            {
                try
                {
                    _service.ComputeSize(location, CancellationToken.None);
                }
                catch (Exception)
                {
                    location.SizeBytes = 0;
                }
            }
        });

        foreach (var vm in Locations) vm.RefreshSizeDisplay();

        IsBusy = false;
        StatusMessage = string.Empty;
    }

    public List<TempLocationViewModel> GetSelectedLocations() =>
        Locations.Where(l => l.IsSelected).ToList();

    public async Task CleanLocationsAsync(IEnumerable<TempLocationViewModel> selected)
    {
        var selectedList = selected.ToList();
        if (selectedList.Count == 0) return;

        IsBusy = true;
        StatusMessage = "Cleaning...";

        long totalFreed = 0;
        int totalSkipped = 0;

        await Task.Run(() =>
        {
            foreach (var location in selectedList)
            {
                var result = _service.Clean(location.Location);
                totalFreed += result.FreedBytes;
                totalSkipped += result.Skipped.Count;
            }
        });

        StatusMessage = $"Freed {SizeFormatter.Format(totalFreed)}" +
            (totalSkipped > 0 ? $"; {totalSkipped} item(s) skipped (in use)." : ".");

        IsBusy = false;
        await RefreshAsync();
    }
}
