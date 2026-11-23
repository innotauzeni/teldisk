using System.Diagnostics;
using TelDiskCleaner.Core.Processes;

namespace TelDiskCleaner.Core.Tests;

public class ProcessServiceTests
{
    [Fact]
    public void GetGroups_IncludesThisProcessAndMarksItProtected()
    {
        using var self = Process.GetCurrentProcess();

        var groups = new ProcessService().GetGroups();

        var own = Assert.Single(groups, g => g.ProcessIds.Contains(self.Id));
        Assert.True(own.IsProtected);
        Assert.True(own.MemoryBytes > 0);
        Assert.DoesNotContain(groups, g => g.ProcessIds.Contains(0));
    }

    [Fact]
    public void EndProcesses_EndsTheGivenProcess()
    {
        using var child = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;

        var result = new ProcessService().EndProcesses(new[] { child.Id });

        Assert.Equal(1, result.Ended);
        Assert.Empty(result.Failures);
        Assert.True(child.WaitForExit(5000));
    }

    [Fact]
    public void EndProcesses_NeverEndsThisProcess()
    {
        var result = new ProcessService().EndProcesses(new[] { Environment.ProcessId });

        Assert.Equal(0, result.Ended);
    }

    [Fact]
    public void Classifier_ProtectsWindowsAndLabelsDevTools()
    {
        Assert.True(ProcessClassifier.IsProtected("svchost"));
        Assert.True(ProcessClassifier.IsProtected("CSRSS"));
        Assert.False(ProcessClassifier.IsProtected("node"));
        Assert.NotNull(ProcessClassifier.GetDevHint("node"));
        Assert.Null(ProcessClassifier.GetDevHint("notepad"));
    }

    [Fact]
    public void GetMemoryStatus_ReportsPhysicalMemory()
    {
        var memory = new ProcessService().GetMemoryStatus();

        Assert.True(memory.TotalBytes > 0);
        Assert.InRange(memory.UsedPercent, 0, 100);
    }
}
