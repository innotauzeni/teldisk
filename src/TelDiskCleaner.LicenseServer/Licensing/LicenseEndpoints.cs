using Microsoft.EntityFrameworkCore;
using TelDiskCleaner.Licensing;
using TelDiskCleaner.LicenseServer.Data;

namespace TelDiskCleaner.LicenseServer.Licensing;

public static class LicenseEndpoints
{
    public static void MapLicenseApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting("api");
        api.MapPost("/trial/start", StartTrial);
        api.MapPost("/licenses/activate", Activate);
        api.MapPost("/licenses/validate", Validate);
        api.MapPost("/licenses/deactivate", Deactivate);
    }

    private static async Task<IResult> StartTrial(TrialRequest request, LicenseDb db, TokenIssuer issuer, LicensingOptions options)
    {
        if (!IsValidDevice(request.DeviceId)) return Error(400, LicenseErrorCodes.InvalidRequest, "Missing device id.");

        // The trial is tied to the PC, not the install, so reinstalling the app doesn't restart it.
        var trial = await db.Trials.FindAsync(request.DeviceId);
        if (trial is null)
        {
            trial = new TrialRecord { DeviceId = request.DeviceId, MachineName = Trim(request.MachineName), StartedUtc = issuer.UtcNow };
            db.Trials.Add(trial);
            await db.SaveChangesAsync();
        }

        var ends = trial.StartedUtc.AddDays(options.TrialDays);
        if (ends <= issuer.UtcNow)
        {
            return Error(403, LicenseErrorCodes.TrialEnded, "Your free trial has ended.", ends);
        }

        return Results.Ok(new TokenResponse(issuer.Issue(request.DeviceId, LicensePlans.Trial, ends, null)));
    }

    private static async Task<IResult> Activate(ActivateRequest request, LicenseDb db, TokenIssuer issuer)
    {
        if (!IsValidDevice(request.DeviceId) || string.IsNullOrWhiteSpace(request.Key))
        {
            return Error(400, LicenseErrorCodes.InvalidRequest, "Enter a licence key.");
        }

        var license = await FindLicense(db, request.Key);
        var problem = CheckUsable(license, issuer);
        if (problem is not null) return problem;

        var active = license!.Activations.Where(a => a.DeactivatedUtc is null).ToList();
        var existing = active.FirstOrDefault(a => a.DeviceId == request.DeviceId);
        if (existing is null)
        {
            if (active.Count >= license.MaxDevices)
            {
                var others = string.Join(", ", active.Select(a => a.MachineName));
                return Error(409, LicenseErrorCodes.DeviceLimit,
                    $"This key is already in use on {others}. Deactivate it there first, or contact support.");
            }

            existing = new ActivationRecord
            {
                DeviceId = request.DeviceId,
                MachineName = Trim(request.MachineName),
                ActivatedUtc = issuer.UtcNow,
            };
            license.Activations.Add(existing);
        }

        existing.LastSeenUtc = issuer.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new TokenResponse(issuer.Issue(request.DeviceId, LicensePlans.Pro, license.ExpiresUtc, LicenseKeyFormat.Hint(license.Key))));
    }

    private static async Task<IResult> Validate(DeviceKeyRequest request, LicenseDb db, TokenIssuer issuer)
    {
        var license = await FindLicense(db, request.Key);
        var problem = CheckUsable(license, issuer);
        if (problem is not null) return problem;

        var activation = license!.Activations.FirstOrDefault(a => a.DeviceId == request.DeviceId && a.DeactivatedUtc is null);
        if (activation is null)
        {
            return Error(403, LicenseErrorCodes.NotActivated, "This PC is no longer activated. Enter your licence key again.");
        }

        activation.LastSeenUtc = issuer.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new TokenResponse(issuer.Issue(request.DeviceId, LicensePlans.Pro, license.ExpiresUtc, LicenseKeyFormat.Hint(license.Key))));
    }

    private static async Task<IResult> Deactivate(DeviceKeyRequest request, LicenseDb db, TokenIssuer issuer)
    {
        var license = await FindLicense(db, request.Key);
        if (license is null) return Error(404, LicenseErrorCodes.InvalidKey, "That licence key doesn't exist.");

        foreach (var activation in license.Activations.Where(a => a.DeviceId == request.DeviceId && a.DeactivatedUtc is null))
        {
            activation.DeactivatedUtc = issuer.UtcNow;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static IResult? CheckUsable(LicenseRecord? license, TokenIssuer issuer)
    {
        if (license is null) return Error(404, LicenseErrorCodes.InvalidKey, "That licence key doesn't exist. Check it and try again.");
        if (license.Revoked) return Error(403, LicenseErrorCodes.Revoked, "This licence key has been cancelled. Contact support.");
        if (license.ExpiresUtc <= issuer.UtcNow)
        {
            return Error(403, LicenseErrorCodes.Expired, "This licence has expired. Renew it to keep using Pro.", license.ExpiresUtc);
        }
        return null;
    }

    private static Task<LicenseRecord?> FindLicense(LicenseDb db, string key)
    {
        var normalized = LicenseKeyFormat.Normalize(key ?? string.Empty);
        return db.Licenses.Include(l => l.Activations).FirstOrDefaultAsync(l => l.Key == normalized);
    }

    private static bool IsValidDevice(string? deviceId) => !string.IsNullOrWhiteSpace(deviceId) && deviceId.Length <= 128;

    private static string Trim(string? machineName) =>
        string.IsNullOrWhiteSpace(machineName) ? "Unknown PC" : machineName.Trim()[..Math.Min(machineName.Trim().Length, 100)];

    private static IResult Error(int status, string code, string message, DateTime? expiresUtc = null) =>
        Results.Json(new ErrorResponse(code, message, expiresUtc), statusCode: status);
}
