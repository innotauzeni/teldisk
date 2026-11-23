using System.Security.Cryptography;

namespace TelDiskCleaner.Licensing;

/// <summary>Keys look like TDC-7KQ2-M9XA-4HTP-WZ3R (16 random characters, about 78 bits).</summary>
public static class LicenseKeyFormat
{
    // No I, L, O, U, 0 or 1, so keys read out over the phone can't be misheard or mistyped.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public static string Generate()
    {
        var groups = new string[4];
        for (int g = 0; g < groups.Length; g++)
        {
            var chars = new char[4];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
            groups[g] = new string(chars);
        }
        return "TDC-" + string.Join('-', groups);
    }

    public static string Normalize(string key) =>
        new string(key.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    /// <summary>Shows only the last group, so a key isn't fully readable in screenshots.</summary>
    public static string Hint(string key)
    {
        var normalized = Normalize(key);
        return normalized.Length >= 4 ? "TDC-…-" + normalized[^4..] : "TDC-…";
    }
}
