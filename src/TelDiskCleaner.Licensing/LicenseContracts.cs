namespace TelDiskCleaner.Licensing;

public static class LicensePlans
{
    public const string Trial = "trial";
    public const string Pro = "pro";
}

public static class LicenseErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidKey = "invalid_key";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
    public const string DeviceLimit = "device_limit";
    public const string NotActivated = "not_activated";
    public const string TrialEnded = "trial_ended";
}

public sealed record TrialRequest(string DeviceId, string MachineName);

public sealed record ActivateRequest(string Key, string DeviceId, string MachineName);

public sealed record DeviceKeyRequest(string Key, string DeviceId);

public sealed record TokenResponse(string Token);

public sealed record ErrorResponse(string Code, string Message, DateTime? ExpiresUtc = null);

/// <summary>What a signed licence token says about one PC.</summary>
/// <param name="ExpiresUtc">When the trial or subscription ends.</param>
/// <param name="ValidUntilUtc">The app must refresh the token online before this; it is never after ExpiresUtc.</param>
public sealed record LicenseClaims(
    string DeviceId,
    string Plan,
    DateTime ExpiresUtc,
    DateTime IssuedUtc,
    DateTime ValidUntilUtc,
    string? LicenseKeyHint);
