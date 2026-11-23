using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TelDiskCleaner.App.ViewModels;

namespace TelDiskCleaner.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<DiskAnalyzerViewModel>();
        services.AddSingleton<ProjectCleanerViewModel>();
        services.AddSingleton<TempCleanupViewModel>();
        services.AddSingleton<StartupAppsViewModel>();
        services.AddSingleton<ProcessManagerViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
