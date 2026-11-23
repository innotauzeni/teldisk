using System.ComponentModel;
using System.Diagnostics;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.Core.Processes;

public sealed record ProcessGroup(
    string Name,
    IReadOnlyList<int> ProcessIds,
    long MemoryBytes,
    double CpuPercent,
    string? ExecutablePath,
    bool IsProtected,
    string? DevHint);

public sealed record MemoryStatus(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;
    public double UsedPercent => TotalBytes > 0 ? 100.0 * UsedBytes / TotalBytes : 0;
}

public sealed record EndProcessResult(int Ended, IReadOnlyList<string> Failures);

public sealed class ProcessService
{
    private Dictionary<int, TimeSpan> _lastCpuTimes = new();
    private DateTime _lastSampleUtc = DateTime.MinValue;

    /// <summary>
    /// Running processes grouped by name, largest memory first. CPU % is measured since the
    /// previous call, so the first call reports 0 for every process.
    /// </summary>
    public List<ProcessGroup> GetGroups()
    {
        var now = DateTime.UtcNow;
        var elapsedMs = _lastSampleUtc == DateTime.MinValue ? 0 : (now - _lastSampleUtc).TotalMilliseconds;
        var cpuTimes = new Dictionary<int, TimeSpan>();
        var ownId = Environment.ProcessId;
        var rows = new List<(string Name, int Id, long Memory, double Cpu, string? Path)>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == 0) continue; // "Idle" is spare CPU time, not a program

                long memory;
                try
                {
                    memory = process.WorkingSet64;
                }
                catch (InvalidOperationException)
                {
                    continue; // exited while we were looking
                }

                double cpu = 0;
                try
                {
                    var total = process.TotalProcessorTime;
                    cpuTimes[process.Id] = total;
                    if (elapsedMs > 0 && _lastCpuTimes.TryGetValue(process.Id, out var previous))
                    {
                        cpu = (total - previous).TotalMilliseconds / elapsedMs / Environment.ProcessorCount * 100;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // access denied for some system processes: report 0 CPU
                }

                rows.Add((process.ProcessName, process.Id, memory, Math.Max(0, cpu), ProcessInterop.GetImagePath(process.Id)));
            }
        }

        _lastCpuTimes = cpuTimes;
        _lastSampleUtc = now;

        return rows
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessGroup(
                g.Key,
                g.Select(r => r.Id).ToList(),
                g.Sum(r => r.Memory),
                Math.Min(100, g.Sum(r => r.Cpu)),
                g.Select(r => r.Path).FirstOrDefault(p => p is not null),
                ProcessClassifier.IsProtected(g.Key) || g.Any(r => r.Id == ownId),
                ProcessClassifier.GetDevHint(g.Key)))
            .OrderByDescending(g => g.MemoryBytes)
            .ToList();
    }

    public MemoryStatus GetMemoryStatus()
    {
        var (total, available) = ProcessInterop.GetPhysicalMemory();
        return new MemoryStatus(total, available);
    }

    /// <summary>Ends each process and its child processes. Protected processes are skipped.</summary>
    public EndProcessResult EndProcesses(IEnumerable<int> processIds)
    {
        int ended = 0;
        var failures = new List<string>();

        foreach (var id in processIds)
        {
            if (id == Environment.ProcessId) continue;

            try
            {
                using var process = Process.GetProcessById(id);
                if (ProcessClassifier.IsProtected(process.ProcessName)) continue;

                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
                ended++;
            }
            catch (ArgumentException)
            {
                ended++; // already gone, e.g. ended together with its parent's process tree
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                failures.Add($"Process {id}: {ex.Message}");
            }
        }

        return new EndProcessResult(ended, failures);
    }

    /// <summary>Stops all WSL distributions and the VM behind them (also used by Docker Desktop).</summary>
    public Task<ProcessResult> ShutdownWslAsync() =>
        ProcessRunner.RunAsync("wsl.exe", "--shutdown", Environment.SystemDirectory, TimeSpan.FromSeconds(60));
}
