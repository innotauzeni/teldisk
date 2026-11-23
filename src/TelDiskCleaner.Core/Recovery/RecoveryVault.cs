using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.Recovery;

public sealed class VaultItem
{
    public Guid Id { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public string VaultPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime DeletedUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class VaultManifest
{
    public List<VaultItem> Items { get; set; } = new();
}

/// <summary>
/// Files removed by TelDiskCleaner are moved here (on the same drive, so it's instant) and can be
/// restored for <see cref="KeepFor"/>; after that they are deleted for good. Their space is only
/// freed then, or when the user empties them sooner.
/// </summary>
public sealed class RecoveryVault
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);
    public const string DriveFolderName = "TelDiskCleaner.Vault";

    private readonly string _manifestPath;
    private readonly Func<string, string> _vaultFolderFor;
    private readonly TimeProvider _clock;
    private readonly object _lock = new();

    public RecoveryVault(string manifestPath, Func<string, string> vaultFolderFor, TimeProvider? clock = null)
    {
        _manifestPath = manifestPath;
        _vaultFolderFor = vaultFolderFor;
        _clock = clock ?? TimeProvider.System;
    }

    public static RecoveryVault CreateDefault() => new(AppPaths.File("vault.json"), DefaultVaultFolder);

    /// <summary>Same drive as the file: %LOCALAPPDATA% on the system drive, a hidden root folder elsewhere.</summary>
    public static string DefaultVaultFolder(string filePath)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.GetPathRoot(Path.GetFullPath(filePath))!;
        return string.Equals(root, Path.GetPathRoot(local), StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(local, "TelDiskCleaner", "Vault")
            : Path.Combine(root, DriveFolderName);
    }

    public static bool IsVaultPath(string path) =>
        path.Contains(Path.DirectorySeparatorChar + DriveFolderName, StringComparison.OrdinalIgnoreCase)
        || path.Contains(Path.Combine("TelDiskCleaner", "Vault"), StringComparison.OrdinalIgnoreCase);

    public DateTime PurgeDate(VaultItem item) => item.DeletedUtc + KeepFor;

    public List<VaultItem> GetItems()
    {
        lock (_lock)
        {
            return JsonFileStore.Load<VaultManifest>(_manifestPath).Items.OrderByDescending(i => i.DeletedUtc).ToList();
        }
    }

    public VaultItem MoveToVault(string path, string reason)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("File not found.", fullPath);

        var vaultRoot = _vaultFolderFor(fullPath);
        var isNewRoot = !Directory.Exists(vaultRoot);
        var id = Guid.NewGuid();
        var folder = Directory.CreateDirectory(Path.Combine(vaultRoot, id.ToString("N"))).FullName;
        if (isNewRoot)
        {
            File.SetAttributes(vaultRoot, File.GetAttributes(vaultRoot) | FileAttributes.Hidden);
        }

        var destination = Path.Combine(folder, info.Name);
        var size = info.Length;
        try
        {
            File.Move(fullPath, destination);
        }
        catch
        {
            TryDeleteFolder(folder);
            throw;
        }

        var item = new VaultItem
        {
            Id = id,
            OriginalPath = fullPath,
            VaultPath = destination,
            SizeBytes = size,
            DeletedUtc = _clock.GetUtcNow().UtcDateTime,
            Reason = reason,
        };
        Update(manifest => manifest.Items.Add(item));
        return item;
    }

    /// <summary>Puts the file back; if something now exists at that path, restores beside it instead.</summary>
    public string Restore(VaultItem item)
    {
        if (!File.Exists(item.VaultPath))
        {
            Update(manifest => manifest.Items.RemoveAll(i => i.Id == item.Id));
            throw new FileNotFoundException("The saved copy is missing; it may have been deleted outside TelDiskCleaner.", item.VaultPath);
        }

        var target = item.OriginalPath;
        if (File.Exists(target))
        {
            target = AvailableName(target);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(item.VaultPath, target);

        TryDeleteFolder(Path.GetDirectoryName(item.VaultPath)!);
        Update(manifest => manifest.Items.RemoveAll(i => i.Id == item.Id));
        return target;
    }

    public void DeleteNow(VaultItem item)
    {
        if (File.Exists(item.VaultPath)) File.Delete(item.VaultPath);
        TryDeleteFolder(Path.GetDirectoryName(item.VaultPath)!);
        Update(manifest => manifest.Items.RemoveAll(i => i.Id == item.Id));
    }

    /// <summary>Permanently deletes items older than <see cref="KeepFor"/>. Returns bytes freed.</summary>
    public long PurgeExpired()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        long freed = 0;
        foreach (var item in GetItems().Where(i => PurgeDate(i) <= now))
        {
            try
            {
                DeleteNow(item);
                freed += item.SizeBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // still locked or protected: try again next time
            }
        }
        return freed;
    }

    private void Update(Action<VaultManifest> change)
    {
        lock (_lock)
        {
            var manifest = JsonFileStore.Load<VaultManifest>(_manifestPath);
            change(manifest);
            JsonFileStore.Save(_manifestPath, manifest);
        }
    }

    private static string AvailableName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, i == 1 ? $"{name} (restored){ext}" : $"{name} (restored {i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // harmless leftover empty folder
        }
    }
}
