using System.Text.Json;

namespace TelDiskCleaner.Core.Common;

public static class AppPaths
{
    public static string DataFolder
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TelDiskCleaner");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string File(string name) => Path.Combine(DataFolder, name);
}

/// <summary>Small JSON files for state; a missing or corrupt file reads as a new object.</summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            return System.IO.File.Exists(path)
                ? JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(path), Options) ?? new T()
                : new T();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        System.IO.File.Move(temp, path, overwrite: true);
    }
}
