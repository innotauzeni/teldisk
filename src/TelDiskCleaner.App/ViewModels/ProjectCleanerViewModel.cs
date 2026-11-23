using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Configuration;
using TelDiskCleaner.Core.ProjectCleanup;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class ProjectCandidateViewModel : ObservableObject
{
    public required ProjectCandidate Candidate { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isPinned;

    public string ProjectRoot => Candidate.ProjectRoot;
    public string ArtifactBreakdown => string.Join(", ",
        Candidate.Artifacts.Select(a => $"{a.Kind.DisplayName}: {SizeFormatter.Format(a.SizeBytes)}"));
    public string TotalSizeDisplay => SizeFormatter.Format(Candidate.TotalArtifactSize);
    public string LastActivityDisplay => Candidate.LastActivityUtc is { } dt
        ? $"{dt.ToLocalTime():yyyy-MM-dd} ({(Candidate.ActivitySource == ProjectActivitySource.Git ? "git" : "file mtime")})"
        : "Unknown";

    public void MarkPinned()
    {
        Candidate.IsPinned = true;
        IsPinned = true;
    }
}

public sealed partial class ProjectCleanerViewModel : ObservableObject
{
    private readonly ProjectCleanupService _service = new();
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _newRootPath = string.Empty;

    [ObservableProperty]
    private int _thresholdDays;

    public ObservableCollection<string> ProjectRoots { get; } = new();
    public ObservableCollection<ProjectCandidateViewModel> Candidates { get; } = new();

    public long SelectedTotalBytes => Candidates.Where(c => c.IsSelected).Sum(c => c.Candidate.TotalArtifactSize);
    public string SelectedTotalDisplay => SizeFormatter.Format(SelectedTotalBytes);

    public ProjectCleanerViewModel(AppSettings settings)
    {
        _settings = settings;
        ThresholdDays = _settings.StalenessThresholdDays;
        foreach (var root in _settings.ProjectRoots) ProjectRoots.Add(root);
    }

    partial void OnThresholdDaysChanged(int value)
    {
        _settings.StalenessThresholdDays = value;
        _settings.Save();
    }

    [RelayCommand]
    private void AddRoot()
    {
        if (string.IsNullOrWhiteSpace(NewRootPath) || !Directory.Exists(NewRootPath)) return;
        if (ProjectRoots.Contains(NewRootPath, StringComparer.OrdinalIgnoreCase)) return;

        ProjectRoots.Add(NewRootPath);
        _settings.ProjectRoots.Add(NewRootPath);
        _settings.Save();
        NewRootPath = string.Empty;
    }

    [RelayCommand]
    private void RemoveRoot(string root)
    {
        ProjectRoots.Remove(root);
        _settings.ProjectRoots.RemoveAll(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase));
        _settings.Save();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (ProjectRoots.Count == 0)
        {
            StatusMessage = "Add at least one project root to scan.";
            return;
        }

        Candidates.Clear();
        IsScanning = true;
        StatusMessage = "Scanning for node_modules and other build artifacts...";
        _cts = new CancellationTokenSource();

        try
        {
            var found = await _service.FindCandidatesAsync(_settings, _cts.Token);
            foreach (var candidate in found)
            {
                var vm = new ProjectCandidateViewModel { Candidate = candidate, IsPinned = candidate.IsPinned };
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ProjectCandidateViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(SelectedTotalBytes));
                        OnPropertyChanged(nameof(SelectedTotalDisplay));
                    }
                };
                Candidates.Add(vm);
            }
            StatusMessage = $"Found {found.Count} project(s) with reclaimable artifacts.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
        }
        finally
        {
            IsScanning = false;
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void Pin(ProjectCandidateViewModel candidate)
    {
        _settings.Pin(candidate.ProjectRoot);
        candidate.MarkPinned();
    }

    public List<ProjectCandidateViewModel> GetSelectedCandidates() =>
        Candidates.Where(c => c.IsSelected).ToList();

    public void DeleteCandidates(IEnumerable<ProjectCandidateViewModel> selected)
    {
        var selectedList = selected.ToList();
        var artifacts = selectedList.SelectMany(c => c.Candidate.Artifacts);
        var result = _service.DeleteArtifacts(artifacts);

        foreach (var candidate in selectedList)
        {
            Candidates.Remove(candidate);
        }

        StatusMessage = $"Freed {SizeFormatter.Format(result.FreedBytes)}" +
            (result.Skipped.Count > 0 ? $"; {result.Skipped.Count} item(s) skipped (in use)." : ".");
    }
}
