namespace TelDiskCleaner.Core.Startup;

/// <summary>
/// The 12-byte values under Explorer\StartupApproved that Task Manager uses to switch startup
/// apps off without deleting their entry: an odd first byte means disabled, and bytes 4-11 hold
/// the FILETIME it was disabled at. A missing value means enabled.
/// </summary>
public static class StartupApprovedValue
{
    public static bool IsEnabled(byte[]? value) => value is null || value.Length == 0 || (value[0] & 1) == 0;

    public static byte[] Create(bool enabled, DateTime utcNow)
    {
        var bytes = new byte[12];
        bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        }
        return bytes;
    }
}
