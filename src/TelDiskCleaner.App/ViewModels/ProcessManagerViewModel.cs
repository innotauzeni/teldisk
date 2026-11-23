using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Processes;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class ProcessGroupViewModel : ObservableObject
{
    public required string Name { get; init; }

    public IReadOnlyList<int> ProcessIds { get; private set; } = Array.Empty<int>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private int _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryDisplay))]
    private long _memoryBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    private double _cpuPercent;

    [ObservableProperty]
    private string? _executablePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note), nameof(CanEnd))]
    private bool _isProtected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note))]
    private string? _devHint;

    public string DisplayName => Count > 1 ? $"{Name} ({Count})" : Name;
    public string MemoryDisplay => SizeFormatter.Format(MemoryBytes);
    public string CpuDisplay => $"{CpuPercent:0.0}%";
    public string Note => IsProtected ? "Windows / this app (protected)" : DevHint ?? string.Empty;
    public bool CanEnd => !IsProtected;

    public void Update(ProcessGroup group)
    {
        ProcessIds = group.ProcessIds;
        Count = group.ProcessIds.Count;
        MemoryBytes = group.MemoryBytes;
        CpuPercent = group.CpuPercent;
        ExecutablePath = group.ExecutablePath;
        IsProtected = group.IsProtected;
        DevHint = group.DevHint;
    }
}

public sealed partial class ProcessManagerViewModel : ObservableObject
{
    private readonly ProcessService _service = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly bool _isAdministrator = AdminHelper.IsAdministrator();
    private bool _isActive;
    private bool _refreshing;

    public ObservableCollection<ProcessGroupViewModel> Processes { get; } = new();
    public ICollectionView ProcessesView { get; }

    [ObservableProperty]
    private string _memorySummary = string.Empty;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _autoRefresh = true;

    [ObservableProperty]
    private bool _showWslShutdown;

    [ObservableProperty]
    private string _filter = string.Empty;

    public ProcessManagerViewModel()
    {
        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(Processes);
        view.SortDescriptions.Add(new SortDescription(nameof(ProcessGroupViewModel.MemoryBytes), ListSortDirection.Descending));
        view.IsLiveSorting = true;
        view.LiveSortingProperties.Add(nameof(ProcessGroupViewModel.MemoryBytes));
        view.LiveSortingProperties.Add(nameof(ProcessGroupViewModel.CpuPercent));
        view.Filter = item => string.IsNullOrWhiteSpace(Filter)
            || ((ProcessGroupViewModel)item).Name.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase);
        ProcessesView = view;

        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Set while the Processes tab is on screen, so the list only refreshes when visible.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            UpdateTimer();
            if (value) _ = WarmUpAsync();
        }
    }

    partial void OnAutoRefreshChanged(bool value) => UpdateTimer();

    partial void OnFilterChanged(string value) => ProcessesView.Refresh();

    private void UpdateTimer()
    {
        if (IsActive && AutoRefresh) _timer.Start();
        else _timer.Stop();
    }

    // CPU % needs two samples, so take a quick second one instead of showing 0% for 3 seconds.
    private async Task WarmUpAsync()
    {
        await RefreshAsync();
        await Task.Delay(700);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var (groups, memory) = await Task.Run(() => (_service.GetGroups(), _service.GetMemoryStatus()));
            Apply(groups);

            MemoryPercent = memory.UsedPercent;
            MemorySummary = $"Memory in use: {SizeFormatter.Format(memory.UsedBytes)} of " +
                $"{SizeFormatter.Format(memory.TotalBytes)} ({memory.UsedPercent:0}%)";
            ShowWslShutdown = groups.Any(g => g.Name.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(List<ProcessGroup> groups)
    {
        var existing = Processes.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            current.Add(group.Name);
            if (!existing.TryGetValue(group.Name, out var vm))
            {
                vm = new ProcessGroupViewModel { Name = group.Name };
                Processes.Add(vm);
            }
            vm.Update(group);
        }

        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            if (!current.Contains(Processes[i].Name)) Processes.RemoveAt(i);
        }
    }

    public async Task EndGroupAsync(ProcessGroupViewModel group)
    {
        StatusMessage = $"Ending {group.Name}...";
        var result = await Task.Run(() => _service.EndProcesses(group.ProcessIds));

        StatusMessage = result.Failures.Count == 0
            ? $"Ended {group.Name}."
            : $"Could not end {result.Failures.Count} {group.Name} process(es)." +
              (_isAdministrator ? string.Empty : " Some processes need \"Restart as administrator\".");
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ShutdownWslAsync()
    {
        StatusMessage = "Shutting down WSL / Docker virtual machine...";
        var result = await _service.ShutdownWslAsync();
        StatusMessage = result.Success
            ? "WSL is shut down. Docker Desktop and WSL start it again when you next use them."
            : $"Could not shut down WSL: {result.StandardError.Trim()}";
        await RefreshAsync();
    }
}
