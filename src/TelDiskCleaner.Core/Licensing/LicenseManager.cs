using System.Net.Http.Json;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Licensing;

namespace TelDiskCleaner.Core.Licensing;

public enum LicenseMode
{
    Checking,
    Trial,
    Pro,
    TrialEnded,
    Expired,
    NeedsInternet,
}

public sealed record LicenseStatus(LicenseMode Mode, DateTime? ExpiresUtc = null, string? KeyHint = null, string? Message = null)
{
    public bool HasFullAccess => Mode is LicenseMode.Trial or LicenseMode.Pro;
}

public sealed record LicenseActionResult(bool Success, string Message);

public sealed class LicenseOptions
{
    public required string ServerUrl { get; init; }
    public required string PublicKey { get; init; }
    public required string StorePath { get; init; }
    public required string DeviceId { get; init; }
    public required string MachineName { get; init; }
}

public sealed class StoredLicense
{
    public string? Token { get; set; }
    public string? LicenseKey { get; set; }
    public bool TrialEnded { get; set; }
}

public sealed class LicenseManager : IDisposable
{
    private readonly LicenseOptions _options;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LicenseManager(LicenseOptions options, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(options.ServerUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public static LicenseManager CreateDefault() => new(new LicenseOptions
    {
        ServerUrl = LicenseConfig.ServerUrl,
        PublicKey = LicenseConfig.PublicKey,
        StorePath = AppPaths.File("license.json"),
        DeviceId = DeviceIdentity.GetDeviceId(),
        MachineName = Environment.MachineName,
    });

    public LicenseStatus Status { get; private set; } = new(LicenseMode.Checking);

    public string MachineName => _options.MachineName;

    /// <summary>Raised from whichever thread made the change.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Status from the saved token alone, with no network call.</summary>
    public LicenseStatus LoadFromDisk()
    {
        SetStatus(Evaluate(JsonFileStore.Load<StoredLicense>(_options.StorePath)));
        return Status;
    }

    /// <summary>Gets a fresh token from the server, or keeps the saved one when offline.</summary>
    public async Task<LicenseStatus> RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var stored = JsonFileStore.Load<StoredLicense>(_options.StorePath);
            try
            {
                if (stored.LicenseKey is not null)
                {
                    await RefreshProAsync(stored);
                }
                else if (stored.TrialEnded)
                {
                    SetStatus(Evaluate(stored));
                }
                else
                {
                    await RefreshTrialAsync(stored);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                var offline = Evaluate(stored);
                SetStatus(offline.Mode == LicenseMode.Checking
                    ? new LicenseStatus(LicenseMode.NeedsInternet, Message: "Connect to the internet to start your free 7-day trial.")
                    : offline);
            }
            return Status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LicenseActionResult> ActivateAsync(string key)
    {
        var normalized = LicenseKeyFormat.Normalize(key);
        if (normalized.Length == 0) return new LicenseActionResult(false, "Enter your licence key.");

        await _gate.WaitAsync();
        try
        {
            using var response = await _http.PostAsJsonAsync("api/licenses/activate",
                new ActivateRequest(normalized, _options.DeviceId, _options.MachineName));

            if (!response.IsSuccessStatusCode)
            {
                return new LicenseActionResult(false, (await ReadError(response)).Message);
            }

            var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
            var stored = new StoredLicense { Token = token, LicenseKey = normalized };
            var status = Evaluate(stored);
            if (status.Mode != LicenseMode.Pro)
            {
                return new LicenseActionResult(false, "The server's reply could not be verified. Make sure the app is up to date.");
            }

            JsonFileStore.Save(_options.StorePath, stored);
            SetStatus(status);
            return new LicenseActionResult(true, "TelDiskCleaner Pro is activated on this PC. Thank you!");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new LicenseActionResult(false, "Couldn't reach the licence server. Check your internet connection and try again.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Frees this PC's seat so the key can be used on another PC.</summary>
    public async Task<LicenseActionResult> DeactivateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var stored = JsonFileStore.Load<StoredLicense>(_options.StorePath);
            if (stored.LicenseKey is null) return new LicenseActionResult(false, "This PC has no licence key to deactivate.");

            using var response = await _http.PostAsJsonAsync("api/licenses/deactivate",
                new DeviceKeyRequest(stored.LicenseKey, _options.DeviceId));
            if (!response.IsSuccessStatusCode)
            {
                return new LicenseActionResult(false, (await ReadError(response)).Message);
            }

            // The trial on this PC is normally long over, so after deactivating it needs a key again.
            JsonFileStore.Save(_options.StorePath, new StoredLicense { TrialEnded = true });
            SetStatus(new LicenseStatus(LicenseMode.TrialEnded, Message: "Pro was removed from this PC. You can now activate your key on another PC."));
            return new LicenseActionResult(true, "This PC is deactivated. You can now use your key on another PC.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new LicenseActionResult(false, "Couldn't reach the licence server. Check your internet connection and try again.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshProAsync(StoredLicense stored)
    {
        using var response = await _http.PostAsJsonAsync("api/licenses/validate",
            new DeviceKeyRequest(stored.LicenseKey!, _options.DeviceId));

        if (response.IsSuccessStatusCode)
        {
            stored.Token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
            JsonFileStore.Save(_options.StorePath, stored);
            SetStatus(Evaluate(stored));
            return;
        }

        var error = await ReadError(response);
        if (error.Code == LicenseErrorCodes.Expired)
        {
            // Keep the key: once it is renewed on the server, the next refresh turns Pro back on.
            stored.Token = null;
            JsonFileStore.Save(_options.StorePath, stored);
            SetStatus(new LicenseStatus(LicenseMode.Expired, error.ExpiresUtc, LicenseKeyFormat.Hint(stored.LicenseKey!), error.Message));
            return;
        }

        if (error.Code is LicenseErrorCodes.Revoked or LicenseErrorCodes.InvalidKey or LicenseErrorCodes.NotActivated)
        {
            JsonFileStore.Save(_options.StorePath, new StoredLicense { TrialEnded = true });
            SetStatus(new LicenseStatus(LicenseMode.Expired, Message: error.Message));
            return;
        }

        SetStatus(Evaluate(stored)); // server trouble: fall back to the saved token
    }

    private async Task RefreshTrialAsync(StoredLicense stored)
    {
        using var response = await _http.PostAsJsonAsync("api/trial/start",
            new TrialRequest(_options.DeviceId, _options.MachineName));

        if (response.IsSuccessStatusCode)
        {
            stored.Token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!.Token;
            JsonFileStore.Save(_options.StorePath, stored);
            SetStatus(Evaluate(stored));
            return;
        }

        var error = await ReadError(response);
        if (error.Code == LicenseErrorCodes.TrialEnded)
        {
            JsonFileStore.Save(_options.StorePath, new StoredLicense { TrialEnded = true });
            SetStatus(new LicenseStatus(LicenseMode.TrialEnded, error.ExpiresUtc, Message: error.Message));
            return;
        }

        SetStatus(Evaluate(stored));
    }

    private LicenseStatus Evaluate(StoredLicense stored)
    {
        if (stored.LicenseKey is null && stored.TrialEnded)
        {
            return new LicenseStatus(LicenseMode.TrialEnded, Message: "Your free trial has ended.");
        }

        if (!LicenseToken.TryVerify(stored.Token, _options.PublicKey, out var claims) || claims.DeviceId != _options.DeviceId)
        {
            return stored.LicenseKey is not null
                ? new LicenseStatus(LicenseMode.NeedsInternet, KeyHint: LicenseKeyFormat.Hint(stored.LicenseKey),
                    Message: "Connect to the internet so TelDiskCleaner can confirm your licence.")
                : new LicenseStatus(LicenseMode.Checking);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var isPro = claims.Plan == LicensePlans.Pro;

        if (claims.ExpiresUtc <= now)
        {
            return isPro
                ? new LicenseStatus(LicenseMode.Expired, claims.ExpiresUtc, claims.LicenseKeyHint, "Your Pro licence has expired.")
                : new LicenseStatus(LicenseMode.TrialEnded, claims.ExpiresUtc, Message: "Your free trial has ended.");
        }

        if (claims.ValidUntilUtc <= now)
        {
            return new LicenseStatus(LicenseMode.NeedsInternet, claims.ExpiresUtc, claims.LicenseKeyHint,
                "Connect to the internet so TelDiskCleaner can confirm your licence.");
        }

        return new LicenseStatus(isPro ? LicenseMode.Pro : LicenseMode.Trial, claims.ExpiresUtc, claims.LicenseKeyHint);
    }

    private static async Task<ErrorResponse> ReadError(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>()
                ?? new ErrorResponse("unknown", $"The licence server returned an error ({(int)response.StatusCode}).");
        }
        catch (System.Text.Json.JsonException)
        {
            return new ErrorResponse("unknown", $"The licence server returned an error ({(int)response.StatusCode}). Please try again later.");
        }
    }

    private void SetStatus(LicenseStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
