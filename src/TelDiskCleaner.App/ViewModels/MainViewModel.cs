using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public DiskAnalyzerViewModel DiskAnalyzer { get; }
    public ProjectCleanerViewModel ProjectCleaner { get; }
    public TempCleanupViewModel TempCleanup { get; }

    [ObservableProperty]
    private object _currentView;

    public MainViewModel(
        DiskAnalyzerViewModel diskAnalyzer,
        ProjectCleanerViewModel projectCleaner,
        TempCleanupViewModel tempCleanup)
    {
        DiskAnalyzer = diskAnalyzer;
        ProjectCleaner = projectCleaner;
        TempCleanup = tempCleanup;
        _currentView = diskAnalyzer;
    }

    [RelayCommand]
    private void ShowDiskAnalyzer() => CurrentView = DiskAnalyzer;

    [RelayCommand]
    private void ShowProjectCleaner() => CurrentView = ProjectCleaner;

    [RelayCommand]
    private void ShowTempCleanup() => CurrentView = TempCleanup;
}
