using System.Security;
using Microsoft.Win32;

namespace TelDiskCleaner.Core.Startup;

public sealed class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private readonly IReadOnlyList<StartupSource> _sources;

    public StartupService() : this(DefaultSources())
    {
    }

    public StartupService(IReadOnlyList<StartupSource> sources)
    {
        _sources = sources;
    }

    public static IReadOnlyList<StartupSource> DefaultSources() => new[]
    {
        new StartupSource("Registry (this user)", StartupSourceKind.RegistryRun, RegistryHive.CurrentUser,
            ApprovedPath + @"\Run", RunKeyPath: RunPath),
        new StartupSource("Registry (all users)", StartupSourceKind.RegistryRun, RegistryHive.LocalMachine,
            ApprovedPath + @"\Run", RunKeyPath: RunPath, RequiresAdmin: true),
        new StartupSource("Registry (all users, 32-bit)", StartupSourceKind.RegistryRun, RegistryHive.LocalMachine,
            ApprovedPath + @"\Run32", RunKeyPath: Run32Path, RequiresAdmin: true),
        new StartupSource("Startup folder (this user)", StartupSourceKind.Folder, RegistryHive.CurrentUser,
            ApprovedPath + @"\StartupFolder", FolderPath: Environment.GetFolderPath(Environment.SpecialFolder.Startup)),
        new StartupSource("Startup folder (all users)", StartupSourceKind.Folder, RegistryHive.LocalMachine,
            ApprovedPath + @"\StartupFolder", FolderPath: Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            RequiresAdmin: true),
    };

    public List<StartupEntry> GetEntries()
    {
        var entries = new List<StartupEntry>();

        foreach (var source in _sources)
        {
            using var baseKey = RegistryKey.OpenBaseKey(source.Hive, RegistryView.Registry64);
            using var approved = baseKey.OpenSubKey(source.ApprovedKeyPath);

            foreach (var (name, command) in ReadItems(source, baseKey))
            {
                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = command,
                    Source = source,
                    IsEnabled = StartupApprovedValue.IsEnabled(approved?.GetValue(name) as byte[]),
                });
            }
        }

        return entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Switches an entry on or off the way Task Manager does, leaving the entry itself in place.
    /// Throws <see cref="UnauthorizedAccessException"/> for all-users entries without admin rights.
    /// </summary>
    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(entry.Source.Hive, RegistryView.Registry64);
            using var approved = baseKey.CreateSubKey(entry.Source.ApprovedKeyPath, writable: true);
            approved.SetValue(entry.Name, StartupApprovedValue.Create(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
        }
        catch (SecurityException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }

        entry.IsEnabled = enabled;
    }

    private static List<(string Name, string Command)> ReadItems(StartupSource source, RegistryKey baseKey)
    {
        var items = new List<(string, string)>();

        if (source.Kind == StartupSourceKind.Folder)
        {
            if (source.FolderPath is null || !Directory.Exists(source.FolderPath)) return items;

            try
            {
                foreach (var file in Directory.EnumerateFiles(source.FolderPath))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    items.Add((name, file));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unreadable Startup folder: show nothing from it
            }
            return items;
        }

        using var run = baseKey.OpenSubKey(source.RunKeyPath!);
        if (run is null) return items;

        foreach (var name in run.GetValueNames())
        {
            if (name.Length == 0) continue; // the key's unnamed default value
            items.Add((name, run.GetValue(name)?.ToString() ?? string.Empty));
        }
        return items;
    }
}
