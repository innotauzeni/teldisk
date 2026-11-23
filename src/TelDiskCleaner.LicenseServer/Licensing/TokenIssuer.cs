using System.Security.Cryptography;
using TelDiskCleaner.Licensing;

namespace TelDiskCleaner.LicenseServer.Licensing;

public sealed class LicensingOptions
{
    public string SigningKeyPath { get; set; } = "keys/license-signing.pem";
    public int TrialDays { get; set; } = 7;
    public int TokenValidityDays { get; set; } = 14;
}

public sealed class TokenIssuer : IDisposable
{
    private readonly ECDsa _key;
    private readonly TimeProvider _clock;
    private readonly LicensingOptions _options;

    public TokenIssuer(LicensingOptions options, IHostEnvironment environment, TimeProvider clock)
    {
        _options = options;
        _clock = clock;

        var path = Path.Combine(environment.ContentRootPath, options.SigningKeyPath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Signing key not found at {path}. Run the server once with --generate-keys to create it.");
        }

        _key = ECDsa.Create();
        _key.ImportFromPem(File.ReadAllText(path));
    }

    public DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    public string Issue(string deviceId, string plan, DateTime expiresUtc, string? licenseKeyHint)
    {
        var now = UtcNow;
        var validUntil = now.AddDays(_options.TokenValidityDays);
        if (validUntil > expiresUtc) validUntil = expiresUtc;

        return LicenseToken.Create(new LicenseClaims(deviceId, plan, expiresUtc, now, validUntil, licenseKeyHint), _key);
    }

    public void Dispose() => _key.Dispose();

    /// <summary>Creates a new P-256 key pair; returns the public key to paste into the app.</summary>
    public static string GenerateKeyFile(string path)
    {
        if (File.Exists(path))
        {
            throw new InvalidOperationException($"{path} already exists. Delete it only if you really want new keys: every installed app would need updating.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }
}
