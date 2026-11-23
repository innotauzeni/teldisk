using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace TelDiskCleaner.Core.Licensing;

public static class LicenseConfig
{
    /// <summary>Public half of the server's signing key (from LicenseServer --generate-keys).</summary>
    public const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEAhWf4pvKEAlbLpPRg2u3P8+gpQc09w3kCjA52BJwhPX1yh0cjba1NCXnsQix01F+hv1XM+GxyrLlncF6+vm01Q==";

    public const string BuyContact = "+263 77 491 4150 (call or WhatsApp) · info@daraerp.co.zw";

    /// <summary>
    /// Set at build time with -p:LicenseServerUrl=...; the TELDISK_LICENSE_SERVER environment
    /// variable overrides it for local testing.
    /// </summary>
    public static string ServerUrl =>
        Environment.GetEnvironmentVariable("TELDISK_LICENSE_SERVER") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
                  .FirstOrDefault(a => a.Key == "LicenseServerUrl")?.Value
              ?? "http://localhost:5080";
}

public static class DeviceIdentity
{
    /// <summary>
    /// A stable id for this Windows installation: a hash of MachineGuid, so the raw id
    /// never leaves the PC.
    /// </summary>
    public static string GetDeviceId()
    {
        string? machineGuid;
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
        {
            machineGuid = key?.GetValue("MachineGuid") as string;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("TelDiskCleaner|" + (machineGuid ?? Environment.MachineName)));
        return Convert.ToHexString(hash)[..32];
    }
}
