using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TelDiskCleaner.App.ViewModels;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.Configuration;
using TelDiskCleaner.Core.Licensing;
using TelDiskCleaner.Core.Recovery;

namespace TelDiskCleaner.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (HeadlessTasks.IsHeadless(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await HeadlessTasks.RunAsync(e.Args));
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton(AppSettings.Load());
        services.AddSingleton(_ => LicenseManager.CreateDefault());
        services.AddSingleton(_ => RecoveryVault.CreateDefault());
        services.AddSingleton<RestorePointService>();
        services.AddSingleton<ScheduledTaskService>();

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<DiskAnalyzerViewModel>();
        services.AddSingleton<ProjectCleanerViewModel>();
        services.AddSingleton<TempCleanupViewModel>();
        services.AddSingleton<FileFinderViewModel>();
        services.AddSingleton<StartupAppsViewModel>();
        services.AddSingleton<ProcessManagerViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<RecoveryViewModel>();
        services.AddSingleton<LicenseViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var license = _serviceProvider.GetRequiredService<LicenseManager>();
        license.LoadFromDisk();
        _serviceProvider.GetRequiredService<MainWindow>().Show();

        _ = license.RefreshAsync();
        var vault = _serviceProvider.GetRequiredService<RecoveryVault>();
        _ = Task.Run(() =>
        {
            try
            {
                vault.PurgeExpired();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // retried on next start
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
