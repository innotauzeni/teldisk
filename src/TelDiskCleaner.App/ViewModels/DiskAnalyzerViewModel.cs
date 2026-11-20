using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.DiskAnalysis;

namespace TelDiskCleaner.App.ViewModels;

public sealed class ScanNodeViewModel
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public required long TotalSize { get; init; }
    public required double PercentOfParent { get; init; }
    public string SizeDisplay => SizeFormatter.Format(TotalSize);
    public ObservableCollection<ScanNodeViewModel> Children { get; } = new();

    public static ScanNodeViewModel FromScanNode(ScanNode node, long parentSize)
    {
        var vm = new ScanNodeViewModel
        {
            Name = node.Name,
            FullPath = node.FullPath,
            IsDirectory = node.IsDirectory,
            TotalSize = node.TotalSize,
            PercentOfParent = parentSize > 0 ? Math.Min(100.0, 100.0 * node.TotalSize / parentSize) : 100.0,
        };

        foreach (var child in node.Children)
        {
            vm.Children.Add(FromScanNode(child, node.TotalSize));
        }

        return vm;
    }
}

public sealed partial class DiskAnalyzerViewModel : ObservableObject
{
    private readonly DiskAnalyzerService _service = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _pathToScan = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<ScanNodeViewModel> Results { get; } = new();

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(PathToScan) || !Directory.Exists(PathToScan))
        {
            StatusMessage = "That folder doesn't exist.";
            return;
        }

        Results.Clear();
        IsScanning = true;
        StatusMessage = "Scanning...";
        _cts = new CancellationTokenSource();

        var progress = new Progress<ScanProgress>(p =>
            StatusMessage = $"Scanning... {p.FilesScanned:N0} files, {SizeFormatter.Format(p.BytesScanned)} so far");

        try
        {
            var root = await _service.ScanAsync(PathToScan, _cts.Token, progress);
            Results.Add(ScanNodeViewModel.FromScanNode(root, root.TotalSize));
            StatusMessage = $"Done. Total size: {SizeFormatter.Format(root.TotalSize)}";
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
}
