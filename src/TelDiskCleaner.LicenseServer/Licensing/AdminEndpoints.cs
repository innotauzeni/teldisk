using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TelDiskCleaner.Licensing;
using TelDiskCleaner.LicenseServer.Data;

namespace TelDiskCleaner.LicenseServer.Licensing;

public sealed record CreateLicenseRequest(string CustomerName, string? Contact, int Months = 12, int MaxDevices = 1);

public sealed record RenewRequest(int Months = 12);

public sealed record DeviceView(string MachineName, DateTime ActivatedUtc, DateTime LastSeenUtc);

public sealed record LicenseView(
    string Key, string CustomerName, string Contact, DateTime CreatedUtc, DateTime ExpiresUtc,
    int MaxDevices, bool Revoked, IReadOnlyList<DeviceView> Devices);

public static class AdminEndpoints
{
    public static void MapAdminApi(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").AddEndpointFilter(RequireAdminKey);
        admin.MapGet("/licenses", List);
        admin.MapPost("/licenses", Create);
        admin.MapPost("/licenses/{key}/renew", Renew);
        admin.MapPost("/licenses/{key}/revoke", Revoke);
        admin.MapPost("/licenses/{key}/reset-devices", ResetDevices);
    }

    private static async ValueTask<object?> RequireAdminKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(configured) || configured.Length < 16)
        {
            return Results.Problem("Admin API is disabled until Admin:ApiKey (16+ characters) is configured.", statusCode: 503);
        }

        var supplied = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        var matches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(configured));
        return matches ? await next(context) : Results.Unauthorized();
    }

    private static async Task<IResult> List(LicenseDb db)
    {
        var licenses = await db.Licenses.Include(l => l.Activations).OrderByDescending(l => l.CreatedUtc).ToListAsync();
        return Results.Ok(licenses.Select(ToView));
    }

    private static async Task<IResult> Create(CreateLicenseRequest request, LicenseDb db, TokenIssuer issuer)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.Months is < 1 or > 120 || request.MaxDevices is < 1 or > 1000)
        {
            return Results.BadRequest(new ErrorResponse(LicenseErrorCodes.InvalidRequest,
                "Customer name is required, months must be 1-120 and devices 1-1000."));
        }

        var license = new LicenseRecord
        {
            Key = LicenseKeyFormat.Generate(),
            CustomerName = request.CustomerName.Trim(),
            Contact = request.Contact?.Trim() ?? string.Empty,
            CreatedUtc = issuer.UtcNow,
            ExpiresUtc = issuer.UtcNow.AddMonths(request.Months),
            MaxDevices = request.MaxDevices,
        };
        db.Licenses.Add(license);
        await db.SaveChangesAsync();
        return Results.Ok(ToView(license));
    }

    private static async Task<IResult> Renew(string key, RenewRequest request, LicenseDb db, TokenIssuer issuer)
    {
        var license = await Find(db, key);
        if (license is null) return Results.NotFound();
        if (request.Months is < 1 or > 120) return Results.BadRequest();

        // Renewing early adds to the remaining time; renewing late starts from today.
        var from = license.ExpiresUtc > issuer.UtcNow ? license.ExpiresUtc : issuer.UtcNow;
        license.ExpiresUtc = from.AddMonths(request.Months);
        license.Revoked = false;
        await db.SaveChangesAsync();
        return Results.Ok(ToView(license));
    }

    private static async Task<IResult> Revoke(string key, LicenseDb db)
    {
        var license = await Find(db, key);
        if (license is null) return Results.NotFound();
        license.Revoked = true;
        await db.SaveChangesAsync();
        return Results.Ok(ToView(license));
    }

    private static async Task<IResult> ResetDevices(string key, LicenseDb db, TokenIssuer issuer)
    {
        var license = await Find(db, key);
        if (license is null) return Results.NotFound();
        foreach (var activation in license.Activations.Where(a => a.DeactivatedUtc is null))
        {
            activation.DeactivatedUtc = issuer.UtcNow;
        }
        await db.SaveChangesAsync();
        return Results.Ok(ToView(license));
    }

    private static Task<LicenseRecord?> Find(LicenseDb db, string key)
    {
        var normalized = LicenseKeyFormat.Normalize(key);
        return db.Licenses.Include(l => l.Activations).FirstOrDefaultAsync(l => l.Key == normalized);
    }

    private static LicenseView ToView(LicenseRecord l) => new(
        l.Key, l.CustomerName, l.Contact, l.CreatedUtc, l.ExpiresUtc, l.MaxDevices, l.Revoked,
        l.Activations.Where(a => a.DeactivatedUtc is null)
            .Select(a => new DeviceView(a.MachineName, a.ActivatedUtc, a.LastSeenUtc)).ToList());
}
