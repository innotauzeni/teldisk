using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace TelDiskCleaner.Licensing;

/// <summary>
/// "payload.signature", both base64url: the payload is the JSON claims, the signature is ECDSA P-256
/// over it. Only the server holds the private key, so the app can trust a token offline.
/// </summary>
public static class LicenseToken
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Create(LicenseClaims claims, ECDsa signingKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims, Json);
        var signature = signingKey.SignData(payload, HashAlgorithmName.SHA256);
        return ToBase64Url(payload) + "." + ToBase64Url(signature);
    }

    public static bool TryVerify(string? token, string publicKeyBase64, [NotNullWhen(true)] out LicenseClaims? claims)
    {
        claims = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var payload = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256)) return false;

            claims = JsonSerializer.Deserialize<LicenseClaims>(payload, Json);
            return claims is not null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
