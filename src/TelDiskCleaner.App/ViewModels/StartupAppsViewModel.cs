using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Startup;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class StartupEntryViewModel : ObservableObject
{
    public required StartupEntry Entry { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDisplay), nameof(ToggleText))]
    private bool _isEnabled;

    public string Name => Entry.DisplayName;
    public string Command => Entry.Command;
    public string Source => Entry.Source.DisplayName;
    public string StatusDisplay => IsEnabled ? "Enabled" : "Disabled";
    public string ToggleText => IsEnabled ? "Disable" : "Enable";
}

public sealed partial class StartupAppsViewModel : ObservableObject
{
    private readonly StartupService _service = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<StartupEntryViewModel> Entries { get; } = new();

    public StartupAppsViewModel()
    {
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        try
        {
            foreach (var entry in _service.GetEntries())
            {
                Entries.Add(new StartupEntryViewModel { Entry = entry, IsEnabled = entry.IsEnabled });
            }
            StatusMessage = $"{Entries.Count(e => e.IsEnabled)} of {Entries.Count} startup apps are enabled.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            StatusMessage = $"Could not read startup apps: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Toggle(StartupEntryViewModel item)
    {
        var enable = !item.IsEnabled;
        try
        {
            _service.SetEnabled(item.Entry, enable);
            item.IsEnabled = enable;
            StatusMessage = enable
                ? $"{item.Name} will start with Windows again from your next sign-in."
                : $"{item.Name} will no longer start with Windows from your next sign-in.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusMessage = $"{item.Name} starts for all users, so changing it needs administrator rights. " +
                "Use \"Restart as administrator\" in the sidebar.";
        }
        catch (IOException ex)
        {
            StatusMessage = $"Could not change {item.Name}: {ex.Message}";
        }
    }
}
