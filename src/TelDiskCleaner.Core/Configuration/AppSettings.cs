using System.Text.Json;
using System.Text.Json.Serialization;
using TelDiskCleaner.Core.Backup;

namespace TelDiskCleaner.Core.Configuration;

public sealed class IgnoredProject
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("pinnedAtUtc")]
    public DateTime PinnedAtUtc { get; set; }
}

public sealed class AppSettings
{
    [JsonPropertyName("stalenessThresholdDays")]
    public int StalenessThresholdDays { get; set; } = 60;

    [JsonPropertyName("projectRoots")]
    public List<string> ProjectRoots { get; set; } = new();

    [JsonPropertyName("ignoredProjects")]
    public List<IgnoredProject> IgnoredProjects { get; set; } = new();

    [JsonPropertyName("backupJobs")]
    public List<BackupJob> BackupJobs { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string GetSettingsFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelDiskCleaner");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        var path = GetSettingsFilePath();
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var path = GetSettingsFilePath();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public bool IsIgnored(string projectPath) =>
        IgnoredProjects.Any(p => string.Equals(
            Path.GetFullPath(p.Path).TrimEnd('\\'),
            Path.GetFullPath(projectPath).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase));

    public void Pin(string projectPath)
    {
        if (IsIgnored(projectPath)) return;
        IgnoredProjects.Add(new IgnoredProject { Path = projectPath, PinnedAtUtc = DateTime.UtcNow });
        Save();
    }

    public void Unpin(string projectPath)
    {
        IgnoredProjects.RemoveAll(p => string.Equals(
            Path.GetFullPath(p.Path).TrimEnd('\\'),
            Path.GetFullPath(projectPath).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
