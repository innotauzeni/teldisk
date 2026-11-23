using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TelDiskCleaner.Core.Licensing;

namespace TelDiskCleaner.App.ViewModels;

public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly LicenseManager _license;

    [ObservableProperty]
    private string _statusTitle = string.Empty;

    [ObservableProperty]
    private string _statusDetail = string.Empty;

    [ObservableProperty]
    private string _keyInput = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string? _lockedFeatureMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPro;

    public LicenseViewModel(LicenseManager license)
    {
        _license = license;
        _license.StatusChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Update);
        Update();
    }

    public string BuyContact => LicenseConfig.BuyContact;
    public string MachineName => _license.MachineName;

    public void ShowLocked(string? featureName) =>
        LockedFeatureMessage = featureName is null
            ? null
            : $"{featureName} is part of TelDiskCleaner Pro. Activate a licence key below to use it.";

    public static string SidebarSummary(LicenseStatus status) => status.Mode switch
    {
        LicenseMode.Trial => $"Free trial: {DaysLeft(status)} day(s) left",
        LicenseMode.Pro => $"Pro until {status.ExpiresUtc!.Value.ToLocalTime():d MMM yyyy}",
        LicenseMode.TrialEnded => "Trial ended: get Pro",
        LicenseMode.Expired => "Pro expired: renew",
        LicenseMode.NeedsInternet => "Offline: connect to check licence",
        _ => "Checking licence...",
    };

    private void Update()
    {
        var status = _license.Status;
        IsPro = status.Mode == LicenseMode.Pro;
        (StatusTitle, StatusDetail) = status.Mode switch
        {
            LicenseMode.Trial => ($"Free trial: {DaysLeft(status)} day(s) left",
                $"Every feature is unlocked until {status.ExpiresUtc!.Value.ToLocalTime():d MMMM yyyy}. Get a yearly Pro licence to keep using them after that."),
            LicenseMode.Pro => ("TelDiskCleaner Pro is active",
                $"Licence {status.KeyHint} is active on this PC until {status.ExpiresUtc!.Value.ToLocalTime():d MMMM yyyy}."),
            LicenseMode.TrialEnded => ("Your free trial has ended",
                status.Message is { Length: > 0 } m && m.StartsWith("Pro was removed", StringComparison.Ordinal)
                    ? m
                    : "Disk Analyzer stays free. Get a yearly Pro licence to unlock everything else."),
            LicenseMode.Expired => ("Your Pro licence has ended", status.Message ?? "Renew your licence to keep using Pro."),
            LicenseMode.NeedsInternet => ("Connect to the internet", status.Message ?? "TelDiskCleaner needs to check your licence online."),
            _ => ("Checking your licence...", "This only takes a moment."),
        };
    }

    private static int DaysLeft(LicenseStatus status) =>
        Math.Max(0, (int)Math.Ceiling((status.ExpiresUtc!.Value - DateTime.UtcNow).TotalDays));

    [RelayCommand]
    private async Task ActivateAsync()
    {
        IsBusy = true;
        Message = "Activating...";
        var result = await _license.ActivateAsync(KeyInput);
        Message = result.Message;
        if (result.Success)
        {
            KeyInput = string.Empty;
            LockedFeatureMessage = null;
        }
        IsBusy = false;
    }

    public async Task DeactivateAsync()
    {
        IsBusy = true;
        Message = "Deactivating...";
        Message = (await _license.DeactivateAsync()).Message;
        IsBusy = false;
    }

    [RelayCommand]
    private async Task CheckAgainAsync()
    {
        IsBusy = true;
        Message = "Checking...";
        await _license.RefreshAsync();
        Message = string.Empty;
        IsBusy = false;
    }
}
