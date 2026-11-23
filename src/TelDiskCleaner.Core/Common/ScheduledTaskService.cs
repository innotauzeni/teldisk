using System.Security;
using System.Security.Principal;
using System.Text;

namespace TelDiskCleaner.Core.Common;

public enum TaskSchedule
{
    Daily,
    Weekly,
}

/// <summary>Windows Task Scheduler tasks that start TelDiskCleaner in the background.</summary>
public sealed class ScheduledTaskService
{
    public const string AutoCleanTaskName = "TelDiskCleaner Auto Clean";

    public static string BackupTaskName(string jobId) => $"TelDiskCleaner Backup {jobId}";

    public async Task<ProcessResult> CreateAsync(string name, string exePath, string arguments, TaskSchedule schedule, string description)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"tdc-task-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(xmlPath, BuildXml(exePath, arguments, schedule, description), Encoding.Unicode);
        try
        {
            return await Run($"/Create /TN \"{name}\" /XML \"{xmlPath}\" /F");
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public Task<ProcessResult> DeleteAsync(string name) => Run($"/Delete /TN \"{name}\" /F");

    public async Task<bool> ExistsAsync(string name) => (await Run($"/Query /TN \"{name}\"")).Success;

    private static Task<ProcessResult> Run(string arguments) =>
        ProcessRunner.RunAsync("schtasks.exe", arguments, Environment.SystemDirectory, TimeSpan.FromSeconds(30));

    // XML instead of schtasks switches so missed runs (PC was off) happen at next start-up,
    // and runs on battery aren't skipped.
    internal static string BuildXml(string exePath, string arguments, TaskSchedule schedule, string description)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var trigger = schedule == TaskSchedule.Weekly
            ? "<ScheduleByWeek><DaysOfWeek><Sunday /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>"
            : "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{SecurityElement.Escape(description)}</Description></RegistrationInfo>
              <Triggers>
                <CalendarTrigger>
                  <StartBoundary>2026-01-04T12:00:00</StartBoundary>
                  <Enabled>true</Enabled>
                  {trigger}
                </CalendarTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(identity.Name)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT4H</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                  <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
