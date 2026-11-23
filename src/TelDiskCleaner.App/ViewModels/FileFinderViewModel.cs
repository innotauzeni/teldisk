using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.FileFinder;
using TelDiskCleaner.Core.Recovery;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class FoundFileViewModel : ObservableObject
{
    public required FoundFile File { get; init; }
    public string GroupLabel { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public string Name => Path.GetFileName(File.Path);
    public string Folder => Path.GetDirectoryName(File.Path) ?? string.Empty;
    public string SizeDisplay => SizeFormatter.Format(File.SizeBytes);
    public string Modified => File.ModifiedUtc.ToLocalTime().ToString("d MMM yyyy");
    public bool CanRemove => File.ProtectedReason is null;
    public string Note => File.ProtectedReason ?? string.Empty;
}

public sealed partial class FileFinderViewModel : ObservableObject
{
    private readonly RecoveryVault _vault;
    private CancellationTokenSource? _cts;

    public ObservableCollection<FoundFileViewModel> LargeFiles { get; } = new();
    public ObservableCollection<FoundFileViewModel> Duplicates { get; } = new();
    public ICollectionView DuplicatesView { get; }

    [ObservableProperty] private string _folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    [ObservableProperty] private int _largeMinMb = 100;
    [ObservableProperty] private int _duplicateMinMb = 1;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _largeStatus = "Choose a folder or drive and click Find large files.";
    [ObservableProperty] private string _duplicateStatus = "Choose a folder or drive and click Find duplicates.";

    public FileFinderViewModel(RecoveryVault vault)
    {
        _vault = vault;
        DuplicatesView = CollectionViewSource.GetDefaultView(Duplicates);
        DuplicatesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FoundFileViewModel.GroupLabel)));
    }

    public string LargeSelectedDisplay => SizeFormatter.Format(LargeFiles.Where(f => f.IsSelected).Sum(f => f.File.SizeBytes));
    public string DuplicateSelectedDisplay => SizeFormatter.Format(Duplicates.Where(f => f.IsSelected).Sum(f => f.File.SizeBytes));

    [RelayCommand]
    private async Task FindLargeFilesAsync()
    {
        if (!CheckFolder(out var problem)) { LargeStatus = problem; return; }

        LargeFiles.Clear();
        OnPropertyChanged(nameof(LargeSelectedDisplay));
        IsScanning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<FinderProgress>(p => LargeStatus = $"Scanning... {p.FilesScanned:N0} files checked");

        try
        {
            var minBytes = Math.Max(1, LargeMinMb) * 1024L * 1024L;
            var found = await Task.Run(() => LargeFileFinder.Find(Folder, minBytes, 300, _cts.Token, progress));
            foreach (var file in found) LargeFiles.Add(Track(new FoundFileViewModel { File = file }, nameof(LargeSelectedDisplay)));
            LargeStatus = found.Count == 0
                ? $"No files of {LargeMinMb} MB or more in this folder."
                : $"{found.Count} file(s) of {LargeMinMb} MB or more, {SizeFormatter.Format(found.Sum(f => f.SizeBytes))} in total. Tick the ones you don't need.";
        }
        catch (OperationCanceledException)
        {
            LargeStatus = "Search cancelled.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        if (!CheckFolder(out var problem)) { DuplicateStatus = problem; return; }

        Duplicates.Clear();
        OnPropertyChanged(nameof(DuplicateSelectedDisplay));
        IsScanning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<FinderProgress>(p => DuplicateStatus = $"{p.Stage}... {p.FilesScanned:N0} files checked");

        try
        {
            var minBytes = Math.Max(1, DuplicateMinMb) * 1024L * 1024L;
            var groups = await Task.Run(() => DuplicateFinder.Find(Folder, minBytes, _cts.Token, progress));

            foreach (var group in groups)
            {
                var label = $"{group.Files.Count} copies of {Path.GetFileName(group.Files[0].Path)}: " +
                            $"{SizeFormatter.Format(group.SizeBytes)} each, {SizeFormatter.Format(group.WastedBytes)} can be freed";
                for (int i = 0; i < group.Files.Count; i++)
                {
                    // Keep the oldest copy (most likely the original) and tick the rest.
                    Duplicates.Add(Track(new FoundFileViewModel { File = group.Files[i], GroupLabel = label, IsSelected = i > 0 },
                        nameof(DuplicateSelectedDisplay)));
                }
            }

            OnPropertyChanged(nameof(DuplicateSelectedDisplay));
            DuplicateStatus = groups.Count == 0
                ? "No duplicate files found."
                : $"{groups.Count} set(s) of duplicates; {SizeFormatter.Format(groups.Sum(g => g.WastedBytes))} can be freed. The oldest copy in each set is kept.";
        }
        catch (OperationCanceledException)
        {
            DuplicateStatus = "Search cancelled.";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    public List<FoundFileViewModel> SelectedLarge() => LargeFiles.Where(f => f.IsSelected && f.CanRemove).ToList();

    public List<FoundFileViewModel> SelectedDuplicates() => Duplicates.Where(f => f.IsSelected && f.CanRemove).ToList();

    /// <summary>Moves files to Recovery, where they can be restored for 7 days.</summary>
    public async Task<string> MoveToRecoveryAsync(IReadOnlyList<FoundFileViewModel> items, string reason)
    {
        var moved = new List<FoundFileViewModel>();
        var failures = new List<string>();

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    _vault.MoveToVault(item.File.Path, reason);
                    moved.Add(item);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{item.Name}: {ex.Message}");
                }
            }
        });

        foreach (var item in moved)
        {
            LargeFiles.Remove(item);
            Duplicates.Remove(item);
        }
        OnPropertyChanged(nameof(LargeSelectedDisplay));
        OnPropertyChanged(nameof(DuplicateSelectedDisplay));

        var size = SizeFormatter.Format(moved.Sum(m => m.File.SizeBytes));
        return $"Moved {moved.Count} file(s) ({size}) to Recovery. You can restore them for 7 days." +
               (failures.Count > 0 ? $" {failures.Count} file(s) couldn't be moved because they are in use or protected." : string.Empty);
    }

    public static void OpenLocation(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private FoundFileViewModel Track(FoundFileViewModel item, string totalProperty)
    {
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(FoundFileViewModel.IsSelected)) OnPropertyChanged(totalProperty);
        };
        return item;
    }

    private bool CheckFolder(out string problem)
    {
        problem = string.Empty;
        if (!string.IsNullOrWhiteSpace(Folder) && Directory.Exists(Folder)) return true;
        problem = "That folder doesn't exist.";
        return false;
    }
}
