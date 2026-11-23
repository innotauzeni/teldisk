namespace TelDiskCleaner.Core.TempCleanup;

internal static class CleanupCatalog
{
    public const string WindowsCategory = "Windows";
    public const string BrowserCategory = "Browser caches";
    public const string DeveloperCategory = "Developer caches";

    private const string CloseBrowserNote =
        "Close the browser first; it locks its cache while open. Only cached web files are removed, not logins or history.";

    public static IEnumerable<TempLocation> Build()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        yield return Folder("User temp files (%TEMP%)", WindowsCategory, new[] { Path.GetTempPath() });
        yield return Folder("Windows temp files", WindowsCategory, new[] { Path.Combine(windows, "Temp") }, admin: true);
        yield return new TempLocation { Name = "Recycle Bin", Category = WindowsCategory, IsRecycleBin = true };
        yield return Folder("Windows Update downloads", WindowsCategory,
            new[] { Path.Combine(windows, "SoftwareDistribution", "Download") }, admin: true,
            note: "Setup files for updates that are already installed.");
        yield return Folder("App crash dumps", WindowsCategory, new[] { Path.Combine(local, "CrashDumps") });
        yield return Folder("System crash dumps", WindowsCategory, new[] { Path.Combine(windows, "Minidump") }, admin: true);
        yield return Folder("Windows error reports", WindowsCategory, new[]
        {
            Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
            Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"),
        }, admin: true);

        yield return Folder("Google Chrome", BrowserCategory,
            ChromiumCaches(Path.Combine(local, "Google", "Chrome", "User Data")), note: CloseBrowserNote);
        yield return Folder("Microsoft Edge", BrowserCategory,
            ChromiumCaches(Path.Combine(local, "Microsoft", "Edge", "User Data")), note: CloseBrowserNote);
        yield return Folder("Brave", BrowserCategory,
            ChromiumCaches(Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")), note: CloseBrowserNote);
        yield return Folder("Mozilla Firefox", BrowserCategory,
            FirefoxCaches(Path.Combine(local, "Mozilla", "Firefox", "Profiles")), note: CloseBrowserNote);

        yield return Folder("npm cache", DeveloperCategory, new[] { Path.Combine(local, "npm-cache") },
            note: "Packages download again on the next npm install.");
        yield return Folder("Yarn cache", DeveloperCategory, new[] { Path.Combine(local, "Yarn", "Cache") },
            note: "Packages download again on the next yarn install.");
        yield return Folder("NuGet download cache", DeveloperCategory, new[] { Path.Combine(local, "NuGet", "v3-cache") });
        yield return Folder("NuGet packages", DeveloperCategory, new[] { Path.Combine(home, ".nuget", "packages") },
            selected: false, note: ".NET projects download their packages again on the next build. Close Visual Studio first.");
        yield return Folder("pip cache", DeveloperCategory, new[] { Path.Combine(local, "pip", "Cache") });
        yield return Folder("Flutter/Dart pub cache", DeveloperCategory, new[] { Path.Combine(local, "Pub", "Cache") },
            selected: false, note: "Run flutter pub get in each project afterwards.");
        yield return Folder("Gradle caches", DeveloperCategory, new[] { Path.Combine(home, ".gradle", "caches") },
            selected: false, note: "Android/Java builds download dependencies again; the first build is slow.");
        yield return Folder("Maven repository", DeveloperCategory, new[] { Path.Combine(home, ".m2", "repository") },
            selected: false, note: "Java builds download dependencies again.");
        yield return Folder("Go build cache", DeveloperCategory, new[] { Path.Combine(local, "go-build") },
            note: "Go rebuilds it automatically.");
        yield return Folder("Composer cache", DeveloperCategory, new[] { Path.Combine(local, "Composer") },
            note: "Packages download again on the next composer install.");
    }

    private static TempLocation Folder(
        string name, string category, IEnumerable<string> paths,
        bool admin = false, bool selected = true, string note = "") => new()
    {
        Name = name,
        Category = category,
        Paths = paths.Where(Directory.Exists).ToList(),
        RequiresElevation = admin,
        SelectedByDefault = selected,
        Note = note,
    };

    private static IEnumerable<string> ChromiumCaches(string userDataDir)
    {
        if (!Directory.Exists(userDataDir)) return Array.Empty<string>();

        return SafeSubdirectories(userDataDir)
            .Where(profile =>
            {
                var name = Path.GetFileName(profile);
                return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
            })
            .SelectMany(profile => new[] { "Cache", "Code Cache", "GPUCache" }.Select(sub => Path.Combine(profile, sub)));
    }

    private static IEnumerable<string> FirefoxCaches(string profilesDir) =>
        Directory.Exists(profilesDir)
            ? SafeSubdirectories(profilesDir).Select(profile => Path.Combine(profile, "cache2"))
            : Array.Empty<string>();

    private static IEnumerable<string> SafeSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
