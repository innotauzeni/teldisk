using System.Diagnostics;
using System.Globalization;
using System.Text;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.Recovery;

public sealed record RestorePoint(int SequenceNumber, string Description, DateTime Created);

public sealed record RestorePointResult(bool Created, string Message);

/// <summary>Windows System Restore via Windows PowerShell's built-in cmdlets (needs administrator).</summary>
public sealed class RestorePointService
{
    public async Task<RestorePointResult> CreateAsync(string description)
    {
        var safe = new string(description.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or ':' or '.').ToArray());
        var script =
            "$w = $null; " +
            $"Checkpoint-Computer -Description 'TelDiskCleaner: {safe}' -RestorePointType MODIFY_SETTINGS " +
            "-ErrorAction Stop -WarningVariable w -WarningAction SilentlyContinue; " +
            "if ($w) { Write-Output ('WARNING: ' + ($w -join ' ')) } else { Write-Output 'OK' }";

        var result = await RunPowerShellAsync(script, TimeSpan.FromMinutes(4));
        if (!result.Success)
        {
            return new RestorePointResult(false, Explain(result.StandardError));
        }

        var output = result.StandardOutput.Trim();
        return output.StartsWith("WARNING:", StringComparison.Ordinal)
            ? new RestorePointResult(false, output["WARNING:".Length..].Trim())
            : new RestorePointResult(true, "Restore point created.");
    }

    public async Task<(List<RestorePoint> Points, string? Error)> ListAsync()
    {
        const string script =
            "Get-ComputerRestorePoint -ErrorAction Stop | ForEach-Object { " +
            "'{0}|{1}|{2}' -f $_.SequenceNumber, $_.ConvertToDateTime($_.CreationTime).ToString('o'), $_.Description }";

        var result = await RunPowerShellAsync(script, TimeSpan.FromMinutes(1));
        if (!result.Success) return (new List<RestorePoint>(), Explain(result.StandardError));

        var points = new List<RestorePoint>();
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3
                && int.TryParse(parts[0], out var number)
                && DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
            {
                points.Add(new RestorePoint(number, parts[2], created));
            }
        }
        return (points.OrderByDescending(p => p.Created).ToList(), null);
    }

    /// <summary>Opens Windows' own System Restore wizard, which does the actual rollback.</summary>
    public static void OpenSystemRestore() =>
        Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });

    private static Task<ProcessResult> RunPowerShellAsync(string script, TimeSpan timeout)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return ProcessRunner.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            Environment.SystemDirectory, timeout);
    }

    private static string Explain(string error)
    {
        var text = error.Trim();
        if (text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("administrator", StringComparison.OrdinalIgnoreCase))
        {
            return "Restore points need administrator rights. Use \"Restart as administrator\" in the sidebar.";
        }
        if (text.Contains("disabled", StringComparison.OrdinalIgnoreCase) || text.Contains("0x80070422", StringComparison.OrdinalIgnoreCase))
        {
            return "System Protection is turned off for drive C:. Turn it on in Windows (search for \"Create a restore point\") and try again.";
        }

        var firstLine = text.Split('\n').FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(firstLine) ? "Windows couldn't complete the restore point request." : firstLine;
    }
}
