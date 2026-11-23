using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.TempCleanup;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class TempLocationViewModel : ObservableObject
{
    public required TempLocation Location { get; init; }
    public required bool IsAdministrator { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _sizeDisplay = "Calculating...";

    public string Name => Location.Name;
    public string Category => Location.Category;

    public string Note => Location.RequiresElevation && !IsAdministrator
        ? ("Needs administrator. " + Location.Note).Trim()
        : Location.Note;

    public void RefreshSizeDisplay() => SizeDisplay = SizeFormatter.Format(Location.SizeBytes);
}

public sealed partial class TempCleanupViewModel : ObservableObject
{
    private readonly TempCleanupService _service = new();
    private readonly bool _isAdministrator = AdminHelper.IsAdministrator();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<TempLocationViewModel> Locations { get; } = new();
    public ICollectionView LocationsView { get; }

    public string SelectedTotalDisplay =>
        SizeFormatter.Format(Locations.Where(l => l.IsSelected).Sum(l => l.Location.SizeBytes));

    public TempCleanupViewModel()
    {
        LocationsView = CollectionViewSource.GetDefaultView(Locations);
        LocationsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TempLocationViewModel.Category)));
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Calculating reclaimable space...";
        Locations.Clear();

        var locations = await Task.Run(_service.GetLocations);
        foreach (var location in locations)
        {
            var vm = new TempLocationViewModel
            {
                Location = location,
                IsAdministrator = _isAdministrator,
                IsSelected = location.SelectedByDefault && (!location.RequiresElevation || _isAdministrator),
            };
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TempLocationViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedTotalDisplay));
                }
            };
            Locations.Add(vm);
        }

        // Size each row as soon as it is known; developer caches can take a while.
        foreach (var vm in Locations.ToList())
        {
            await Task.Run(() =>
            {
                try
                {
                    _service.ComputeSize(vm.Location, CancellationToken.None);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    vm.Location.SizeBytes = 0;
                }
            });
            vm.RefreshSizeDisplay();
            OnPropertyChanged(nameof(SelectedTotalDisplay));
        }

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

        IsBusy = false;
        await RefreshAsync();

        StatusMessage = $"Freed {SizeFormatter.Format(totalFreed)}" +
            (totalSkipped > 0 ? $"; {totalSkipped:N0} item(s) skipped because they are in use or need administrator." : ".");
    }
}
