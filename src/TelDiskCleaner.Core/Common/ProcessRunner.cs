using System.Diagnostics;

namespace TelDiskCleaner.Core.Common;

public sealed record ProcessResult(bool Success, int ExitCode, string StandardOutput, string StandardError);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        try
        {
            process.Start();

            using var cts = new CancellationTokenSource(timeout);
            string stdout;
            string stderr;
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                stdout = await stdoutTask.ConfigureAwait(false);
                stderr = await stderrTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new ProcessResult(false, -1, string.Empty, "Process timed out.");
            }

            return new ProcessResult(process.ExitCode == 0, process.ExitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // e.g. git.exe not found on PATH
            return new ProcessResult(false, -1, string.Empty, ex.Message);
        }
    }
}
