using TelDiskCleaner.Core.Recovery;

namespace TelDiskCleaner.Core.FileFinder;

public static class FileWalker
{
    // OneDrive and other cloud "online-only" files: reading them starts a download.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static bool IsCloudPlaceholder(FileAttributes attributes) =>
        (attributes & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0;

    /// <summary>All files under root without following links or entering TelDiskCleaner's recovery area.</summary>
    public static IEnumerable<FileInfo> EnumerateFiles(string root, Func<DirectoryInfo, bool> skipDirectory, CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            List<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                if (entry is DirectoryInfo subdir)
                {
                    if (!RecoveryVault.IsVaultPath(subdir.FullName) && !skipDirectory(subdir)) pending.Push(subdir);
                }
                else if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }
    }
}

public static class FileProtection
{
    private static readonly string[] ProtectedRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    };

    /// <summary>Why a file must not be removed by the file finder, or null when it's safe to offer.</summary>
    public static string? GetReason(FileInfo file)
    {
        if (file.Attributes.HasFlag(FileAttributes.System)) return "Windows system file";

        foreach (var root in ProtectedRoots.Where(r => r.Length > 0))
        {
            if (file.FullName.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return root == ProtectedRoots[0] ? "Part of Windows" : "Installed program: uninstall it instead";
            }
        }
        return null;
    }
}
