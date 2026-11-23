using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TelDiskCleaner.LicenseServer.Data;
using TelDiskCleaner.LicenseServer.Licensing;

var builder = WebApplication.CreateBuilder(args);

var licensingOptions = builder.Configuration.GetSection("Licensing").Get<LicensingOptions>() ?? new LicensingOptions();

if (args.Contains("--generate-keys"))
{
    var keyPath = Path.Combine(builder.Environment.ContentRootPath, licensingOptions.SigningKeyPath);
    var publicKey = TokenIssuer.GenerateKeyFile(keyPath);
    Console.WriteLine($"Private signing key written to {keyPath}. Keep it secret and back it up.");
    Console.WriteLine("Public key for the app (LicenseConfig.PublicKey):");
    Console.WriteLine(publicKey);
    return;
}

builder.Services.AddSingleton(licensingOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddDbContext<LicenseDb>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Licenses") ?? "Data Source=licenses.db"));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<LicenseDb>().Database.EnsureCreated();
}

app.UseRateLimiter();
app.UseStaticFiles();
app.MapGet("/", () => Results.Text("TelDiskCleaner licence server is running."));
app.MapGet("/admin", () => Results.Redirect("/admin.html"));
app.MapLicenseApi();
app.MapAdminApi();

app.Run();

public partial class Program;
