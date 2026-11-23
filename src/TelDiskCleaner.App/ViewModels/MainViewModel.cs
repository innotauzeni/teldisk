using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public DiskAnalyzerViewModel DiskAnalyzer { get; }
    public ProjectCleanerViewModel ProjectCleaner { get; }
    public TempCleanupViewModel TempCleanup { get; }
    public StartupAppsViewModel StartupApps { get; }
    public ProcessManagerViewModel ProcessManager { get; }

    public bool IsAdministrator { get; } = AdminHelper.IsAdministrator();
    public bool IsNotAdministrator => !IsAdministrator;

    [ObservableProperty]
    private object _currentView;

    public MainViewModel(
        DiskAnalyzerViewModel diskAnalyzer,
        ProjectCleanerViewModel projectCleaner,
        TempCleanupViewModel tempCleanup,
        StartupAppsViewModel startupApps,
        ProcessManagerViewModel processManager)
    {
        DiskAnalyzer = diskAnalyzer;
        ProjectCleaner = projectCleaner;
        TempCleanup = tempCleanup;
        StartupApps = startupApps;
        ProcessManager = processManager;
        _currentView = diskAnalyzer;
    }

    partial void OnCurrentViewChanged(object value) => ProcessManager.IsActive = value == ProcessManager;

    [RelayCommand]
    private void ShowDiskAnalyzer() => CurrentView = DiskAnalyzer;

    [RelayCommand]
    private void ShowProjectCleaner() => CurrentView = ProjectCleaner;

    [RelayCommand]
    private void ShowTempCleanup() => CurrentView = TempCleanup;

    [RelayCommand]
    private void ShowStartupApps() => CurrentView = StartupApps;

    [RelayCommand]
    private void ShowProcessManager() => CurrentView = ProcessManager;

    [RelayCommand]
    private void RestartAsAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception)
        {
            return; // the Windows permission prompt was cancelled
        }
        Application.Current.Shutdown();
    }
}
