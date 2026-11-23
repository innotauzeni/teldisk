namespace TelDiskCleaner.Core.Processes;

public static class ProcessClassifier
{
    // Ending any of these crashes or destabilises Windows (or the desktop), so they are never offered.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System", "smss", "csrss", "wininit",
        "winlogon", "services", "lsass", "LsaIso", "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon",
        "explorer", "spoolsv", "audiodg", "conhost", "RuntimeBroker", "taskhostw", "WmiPrvSE", "dllhost",
        "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost", "WUDFHost",
        "MsMpEng", "NisSrv", "MpDefenderCoreService", "SecurityHealthService", "SecurityHealthSystray",
    };

    private static readonly Dictionary<string, string> DevToolHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "Node.js: dev servers, Angular/React builds, VS Code helpers",
        ["dotnet"] = ".NET: running apps, builds or language servers",
        ["VBCSCompiler"] = "C# compiler server, kept alive after builds",
        ["MSBuild"] = "Leftover MSBuild build worker",
        ["java"] = "Java: Gradle/Android builds or IDE servers",
        ["javaw"] = "Java: Gradle/Android builds or IDE servers",
        ["dart"] = "Dart/Flutter tooling",
        ["qemu-system-x86_64"] = "Android emulator",
        ["emulator"] = "Android emulator",
        ["adb"] = "Android Debug Bridge",
        ["php"] = "PHP",
        ["php-cgi"] = "PHP (web server)",
        ["mysqld"] = "MySQL database server",
        ["httpd"] = "Apache web server",
        ["nginx"] = "Nginx web server",
        ["python"] = "Python",
        ["gopls"] = "Go language server",
        ["sqlservr"] = "SQL Server (a Windows service; needs administrator)",
        ["com.docker.backend"] = "Docker Desktop",
        ["vmmem"] = "WSL / Docker virtual machine: use Shut down WSL",
        ["vmmemWSL"] = "WSL / Docker virtual machine: use Shut down WSL",
    };

    public static bool IsProtected(string processName) => ProtectedNames.Contains(processName);

    public static string? GetDevHint(string processName) =>
        DevToolHints.TryGetValue(processName, out var hint) ? hint : null;
}
