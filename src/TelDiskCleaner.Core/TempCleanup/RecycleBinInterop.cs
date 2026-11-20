using System.Runtime.InteropServices;

namespace TelDiskCleaner.Core.TempCleanup;

internal static class RecycleBinInterop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public static long GetSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBin(null, ref info);
        return hr == 0 ? info.i64Size : 0;
    }

    public static bool Empty()
    {
        var hr = SHEmptyRecycleBin(
            IntPtr.Zero,
            null,
            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

        // S_OK (0) or S_FALSE-ish "already empty" (-2147418113 / 0x8000FFFF in some OS versions)
        // are both treated as success; anything else is a real failure.
        return hr == 0;
    }
}
