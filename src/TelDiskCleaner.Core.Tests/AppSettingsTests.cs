using TelDiskCleaner.Core.Configuration;

namespace TelDiskCleaner.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string? _originalContents;

    public AppSettingsTests()
    {
        _settingsPath = AppSettings.GetSettingsFilePath();
        _originalContents = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
    }

    public void Dispose()
    {
        if (_originalContents is null)
        {
            if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        }
        else
        {
            File.WriteAllText(_settingsPath, _originalContents);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsPinnedProjectsAndThreshold()
    {
        var settings = new AppSettings { StalenessThresholdDays = 45 };
        settings.ProjectRoots.Add(@"C:\dev");
        settings.Pin(@"C:\dev\active-app");

        var reloaded = AppSettings.Load();

        Assert.Equal(45, reloaded.StalenessThresholdDays);
        Assert.Contains(@"C:\dev", reloaded.ProjectRoots);
        Assert.True(reloaded.IsIgnored(@"C:\dev\active-app"));
    }

    [Fact]
    public void Unpin_RemovesProjectFromIgnoreList()
    {
        var settings = AppSettings.Load();
        settings.Pin(@"C:\dev\temp-pin-test");
        Assert.True(settings.IsIgnored(@"C:\dev\temp-pin-test"));

        settings.Unpin(@"C:\dev\temp-pin-test");

        var reloaded = AppSettings.Load();
        Assert.False(reloaded.IsIgnored(@"C:\dev\temp-pin-test"));
    }
}
