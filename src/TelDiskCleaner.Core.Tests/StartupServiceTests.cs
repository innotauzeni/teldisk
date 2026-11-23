using Microsoft.Win32;
using TelDiskCleaner.Core.Startup;

namespace TelDiskCleaner.Core.Tests;

public class StartupServiceTests : IDisposable
{
    private readonly string _keyRoot = @"Software\TelDiskCleanerTests\" + Guid.NewGuid();
    private readonly string _folder;

    public StartupServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "TelDiskCleanerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyRoot, throwOnMissingSubKey: false);
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private StartupService CreateService() => new(new[]
    {
        new StartupSource("Test registry", StartupSourceKind.RegistryRun, RegistryHive.CurrentUser,
            _keyRoot + @"\Approved\Run", RunKeyPath: _keyRoot + @"\Run"),
        new StartupSource("Test folder", StartupSourceKind.Folder, RegistryHive.CurrentUser,
            _keyRoot + @"\Approved\StartupFolder", FolderPath: _folder),
    });

    [Fact]
    public void ApprovedValue_RoundTrips()
    {
        Assert.True(StartupApprovedValue.IsEnabled(null));
        Assert.True(StartupApprovedValue.IsEnabled(StartupApprovedValue.Create(true, DateTime.UtcNow)));
        Assert.False(StartupApprovedValue.IsEnabled(StartupApprovedValue.Create(false, DateTime.UtcNow)));
        Assert.Equal(12, StartupApprovedValue.Create(false, DateTime.UtcNow).Length);
    }

    [Fact]
    public void GetEntries_ReadsRunKeyAndStartupFolder()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_keyRoot + @"\Run"))
        {
            run.SetValue("ChatApp", @"""C:\Apps\chat.exe"" --minimized");
        }
        File.WriteAllText(Path.Combine(_folder, "Notes.lnk"), "");
        File.WriteAllText(Path.Combine(_folder, "desktop.ini"), "");

        var entries = CreateService().GetEntries();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "ChatApp" && e.Command.Contains("chat.exe") && e.IsEnabled);
        Assert.Contains(entries, e => e.DisplayName == "Notes" && e.IsEnabled);
    }

    [Fact]
    public void SetEnabled_DisablesWithoutDeletingTheEntry_AndCanReEnable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_keyRoot + @"\Run"))
        {
            run.SetValue("ChatApp", @"C:\Apps\chat.exe");
        }
        var service = CreateService();
        var entry = service.GetEntries().Single();

        service.SetEnabled(entry, false);

        var reloaded = service.GetEntries().Single();
        Assert.False(reloaded.IsEnabled);
        using (var run = Registry.CurrentUser.OpenSubKey(_keyRoot + @"\Run"))
        {
            Assert.Equal(@"C:\Apps\chat.exe", run!.GetValue("ChatApp"));
        }

        service.SetEnabled(reloaded, true);
        Assert.True(service.GetEntries().Single().IsEnabled);
    }
}
