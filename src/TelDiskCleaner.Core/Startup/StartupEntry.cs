using Microsoft.Win32;

namespace TelDiskCleaner.Core.Startup;

public enum StartupSourceKind
{
    RegistryRun,
    Folder,
}

public sealed record StartupSource(
    string DisplayName,
    StartupSourceKind Kind,
    RegistryHive Hive,
    string ApprovedKeyPath,
    string? RunKeyPath = null,
    string? FolderPath = null,
    bool RequiresAdmin = false);

public sealed class StartupEntry
{
    /// <summary>Registry value name, or file name for Startup-folder shortcuts.</summary>
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required StartupSource Source { get; init; }
    public bool IsEnabled { get; set; }

    public string DisplayName => Source.Kind == StartupSourceKind.Folder
        ? Path.GetFileNameWithoutExtension(Name)
        : Name;
}
