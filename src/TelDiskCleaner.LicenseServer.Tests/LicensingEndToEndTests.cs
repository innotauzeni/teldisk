using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TelDiskCleaner.Core.Licensing;
using TelDiskCleaner.Licensing;
using TelDiskCleaner.LicenseServer.Licensing;

namespace TelDiskCleaner.LicenseServer.Tests;

public sealed class LicensingEndToEndTests : IDisposable
{
    private const string AdminKey = "test-admin-key-1234567890";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "TelDiskCleanerLicTests_" + Guid.NewGuid());
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));
    private readonly string _publicKey;
    private readonly WebApplicationFactory<Program> _factory;

    public LicensingEndToEndTests()
    {
        Directory.CreateDirectory(_folder);
        var keyPath = Path.Combine(_folder, "signing.pem");
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
            _publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Licenses", $"Data Source={Path.Combine(_folder, "licenses.db")};Pooling=False");
            builder.UseSetting("Licensing:SigningKeyPath", keyPath);
            builder.UseSetting("Admin:ApiKey", AdminKey);
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(_clock));
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private LicenseManager App(string device, string install = "a") => new(
        new LicenseOptions
        {
            ServerUrl = "http://localhost",
            PublicKey = _publicKey,
            StorePath = Path.Combine(_folder, $"license-{device}-{install}.json"),
            DeviceId = device,
            MachineName = "PC-" + device,
        },
        _factory.Server.CreateHandler(),
        _clock);

    private async Task<LicenseView> CreateLicense(int months = 12)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);
        var response = await client.PostAsJsonAsync("/api/admin/licenses", new CreateLicenseRequest("Test School", "0770000000", months));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LicenseView>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private async Task Admin(string key, string action, object? body = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", AdminKey);
        var response = await client.PostAsJsonAsync($"/api/admin/licenses/{key}/{action}", body ?? new { });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Trial_StartsOnFirstRun_AndReinstallingKeepsTheSameEndDate()
    {
        using var first = App("pc1", "install1");
        var status = await first.RefreshAsync();
        Assert.Equal(LicenseMode.Trial, status.Mode);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddDays(7), status.ExpiresUtc);

        _clock.Advance(TimeSpan.FromDays(2));
        using var reinstalled = App("pc1", "install2");
        var again = await reinstalled.RefreshAsync();

        Assert.Equal(LicenseMode.Trial, again.Mode);
        Assert.Equal(status.ExpiresUtc, again.ExpiresUtc);
    }

    [Fact]
    public async Task Trial_EndsAfterSevenDays_EvenOffline()
    {
        using var app = App("pc1");
        await app.RefreshAsync();

        _clock.Advance(TimeSpan.FromDays(8));

        Assert.Equal(LicenseMode.TrialEnded, app.LoadFromDisk().Mode);
        Assert.Equal(LicenseMode.TrialEnded, (await app.RefreshAsync()).Mode);
    }

    [Fact]
    public async Task Activate_TurnsOnPro_AndTheKeyIsRefusedOnASecondPc()
    {
        var license = await CreateLicense();
        Assert.Matches(new Regex("^TDC(-[A-Z2-9]{4}){4}$"), license.Key);

        using var pc1 = App("pc1");
        var result = await pc1.ActivateAsync(license.Key.ToLowerInvariant());
        Assert.True(result.Success, result.Message);
        Assert.Equal(LicenseMode.Pro, pc1.Status.Mode);
        Assert.Equal(license.ExpiresUtc, pc1.Status.ExpiresUtc);

        using var pc2 = App("pc2");
        var refused = await pc2.ActivateAsync(license.Key);
        Assert.False(refused.Success);
        Assert.Contains("PC-pc1", refused.Message);
    }

    [Fact]
    public async Task Deactivate_FreesTheKeyForAnotherPc()
    {
        var license = await CreateLicense();
        using var pc1 = App("pc1");
        await pc1.ActivateAsync(license.Key);

        var deactivated = await pc1.DeactivateAsync();
        Assert.True(deactivated.Success, deactivated.Message);
        Assert.False(pc1.Status.HasFullAccess);

        using var pc2 = App("pc2");
        Assert.True((await pc2.ActivateAsync(license.Key)).Success);
    }

    [Fact]
    public async Task RevokedKey_StopsProOnTheNextCheck()
    {
        var license = await CreateLicense();
        using var app = App("pc1");
        await app.ActivateAsync(license.Key);

        await Admin(license.Key, "revoke");

        Assert.False((await app.RefreshAsync()).HasFullAccess);
    }

    [Fact]
    public async Task ExpiredLicence_ComesBackAfterRenewal()
    {
        var license = await CreateLicense(months: 1);
        using var app = App("pc1");
        await app.ActivateAsync(license.Key);

        _clock.Advance(TimeSpan.FromDays(40));
        Assert.Equal(LicenseMode.Expired, (await app.RefreshAsync()).Mode);

        await Admin(license.Key, "renew", new RenewRequest(12));
        Assert.Equal(LicenseMode.Pro, (await app.RefreshAsync()).Mode);
    }

    [Fact]
    public async Task EditedLicenceFile_IsNotTrusted()
    {
        var license = await CreateLicense();
        using var app = App("pc1");
        await app.ActivateAsync(license.Key);

        var path = Path.Combine(_folder, "license-pc1-a.json");
        var json = File.ReadAllText(path);
        var token = JsonDocument.Parse(json).RootElement.GetProperty("token").GetString()!;
        var parts = token.Split('.');
        var forgedPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                "{\"deviceId\":\"pc1\",\"plan\":\"pro\",\"expiresUtc\":\"2099-01-01T00:00:00Z\",\"issuedUtc\":\"2026-11-23T00:00:00Z\",\"validUntilUtc\":\"2099-01-01T00:00:00Z\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        File.WriteAllText(path, json.Replace(token, forgedPayload + "." + parts[1]));

        Assert.NotEqual(LicenseMode.Pro, app.LoadFromDisk().Mode);
    }

    [Fact]
    public async Task Pro_KeepsWorkingOfflineForTwoWeeks_ThenAsksToGoOnline()
    {
        var license = await CreateLicense();
        using var app = App("pc1");
        await app.ActivateAsync(license.Key);

        _clock.Advance(TimeSpan.FromDays(13));
        Assert.Equal(LicenseMode.Pro, app.LoadFromDisk().Mode);

        _clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(LicenseMode.NeedsInternet, app.LoadFromDisk().Mode);
    }

    [Fact]
    public async Task UnknownKey_IsRejectedWithAFriendlyMessage()
    {
        using var app = App("pc1");
        var result = await app.ActivateAsync("TDC-AAAA-BBBB-CCCC-DDDD");

        Assert.False(result.Success);
        Assert.Contains("doesn't exist", result.Message);
    }

    [Fact]
    public async Task LicensingSwitchedOff_UnlocksEverything_WithoutContactingAServer()
    {
        using var app = new LicenseManager(new LicenseOptions
        {
            ServerUrl = "http://127.0.0.1:9", // nothing listens here; any request would fail
            PublicKey = _publicKey,
            StorePath = Path.Combine(_folder, "license-off.json"),
            DeviceId = "pc1",
            MachineName = "PC-pc1",
            Enabled = false,
        });

        Assert.Equal(LicenseMode.Unlocked, app.LoadFromDisk().Mode);
        var refreshed = await app.RefreshAsync();
        Assert.Equal(LicenseMode.Unlocked, refreshed.Mode);
        Assert.True(refreshed.HasFullAccess);
        Assert.False((await app.ActivateAsync("TDC-AAAA-BBBB-CCCC-DDDD")).Success);
        Assert.False(File.Exists(Path.Combine(_folder, "license-off.json")));
    }

    [Fact]
    public async Task AdminApi_RequiresTheAdminKey()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/licenses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        client.DefaultRequestHeaders.Add("X-Admin-Key", "wrong-key-but-long-enough");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/licenses")).StatusCode);
    }

    [Fact]
    public async Task AdminPage_IsServed()
    {
        var html = await _factory.CreateClient().GetStringAsync("/admin.html");
        Assert.Contains("TelDiskCleaner licences", html);
    }
}
