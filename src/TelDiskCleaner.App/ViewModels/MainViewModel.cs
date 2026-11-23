using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Common;
using LicenseManager = TelDiskCleaner.Core.Licensing.LicenseManager;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly LicenseManager _license;
    private bool _currentNeedsPro;

    public HomeViewModel Home { get; }
    public DiskAnalyzerViewModel DiskAnalyzer { get; }
    public ProjectCleanerViewModel ProjectCleaner { get; }
    public TempCleanupViewModel TempCleanup { get; }
    public FileFinderViewModel FileFinder { get; }
    public StartupAppsViewModel StartupApps { get; }
    public ProcessManagerViewModel ProcessManager { get; }
    public BackupViewModel Backup { get; }
    public RecoveryViewModel Recovery { get; }
    public LicenseViewModel License { get; }

    public bool IsAdministrator { get; } = AdminHelper.IsAdministrator();
    public bool IsNotAdministrator => !IsAdministrator;

    [ObservableProperty]
    private object _currentView;

    [ObservableProperty]
    private string _licenseSummary = string.Empty;

    public MainViewModel(
        LicenseManager license,
        HomeViewModel home,
        DiskAnalyzerViewModel diskAnalyzer,
        ProjectCleanerViewModel projectCleaner,
        TempCleanupViewModel tempCleanup,
        FileFinderViewModel fileFinder,
        StartupAppsViewModel startupApps,
        ProcessManagerViewModel processManager,
        BackupViewModel backup,
        RecoveryViewModel recovery,
        LicenseViewModel licenseView)
    {
        _license = license;
        Home = home;
        DiskAnalyzer = diskAnalyzer;
        ProjectCleaner = projectCleaner;
        TempCleanup = tempCleanup;
        FileFinder = fileFinder;
        StartupApps = startupApps;
        ProcessManager = processManager;
        Backup = backup;
        Recovery = recovery;
        License = licenseView;

        _currentView = home;
        Home.RequestUpgrade = () => Navigate(License, null, needsPro: false);
        LicenseSummary = LicenseViewModel.SidebarSummary(license.Status);
        license.StatusChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(OnLicenseChanged);
    }

    private void OnLicenseChanged()
    {
        LicenseSummary = LicenseViewModel.SidebarSummary(_license.Status);
        if (_currentNeedsPro && !_license.Status.HasFullAccess)
        {
            Navigate(License, null, needsPro: false);
        }
    }

    // Home (status only) and Disk Analyzer stay free after the trial; everything else needs Pro.
    private void Navigate(object view, string? featureName, bool needsPro = true)
    {
        if (needsPro && !_license.Status.HasFullAccess)
        {
            License.ShowLocked(featureName);
            view = License;
            needsPro = false;
        }
        else if (view != License)
        {
            License.ShowLocked(null);
        }

        _currentNeedsPro = needsPro;
        CurrentView = view;
    }

    partial void OnCurrentViewChanged(object value) => ProcessManager.IsActive = value == ProcessManager;

    [RelayCommand]
    private void ShowHome()
    {
        Navigate(Home, null, needsPro: false);
        Home.RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private void ShowDiskAnalyzer() => Navigate(DiskAnalyzer, null, needsPro: false);

    [RelayCommand]
    private void ShowProjectCleaner() => Navigate(ProjectCleaner, "Dev Project Cleaner");

    [RelayCommand]
    private void ShowTempCleanup() => Navigate(TempCleanup, "Junk Cleanup");

    [RelayCommand]
    private void ShowFileFinder() => Navigate(FileFinder, "File Finder");

    [RelayCommand]
    private void ShowStartupApps() => Navigate(StartupApps, "Startup Apps");

    [RelayCommand]
    private void ShowProcessManager() => Navigate(ProcessManager, "Processes");

    [RelayCommand]
    private void ShowBackup() => Navigate(Backup, "Backup & Restore");

    [RelayCommand]
    private void ShowRecovery()
    {
        Navigate(Recovery, "Recovery");
        Recovery.Refresh();
    }

    [RelayCommand]
    private void ShowLicense() => Navigate(License, null, needsPro: false);

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
