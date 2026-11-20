using System.Diagnostics;
using TelDiskCleaner.Core.ProjectCleanup;

namespace TelDiskCleaner.Core.Tests;

public class ActivityDetectorTests : IDisposable
{
    private readonly string _root;

    public ActivityDetectorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TelDiskCleanerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;

        // git marks some object files read-only on Windows; clear that before deleting.
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task DetectAsync_GitRepoWithCommit_UsesGitSource()
    {
        RunGit("init");
        RunGit("config user.email test@example.com");
        RunGit("config user.name Test");
        File.WriteAllText(Path.Combine(_root, "file.txt"), "hello");
        RunGit("add .");
        RunGit("commit -m initial");

        var detector = new ActivityDetector();
        var result = await detector.DetectAsync(_root);

        Assert.Equal(ProjectActivitySource.Git, result.Source);
        Assert.NotNull(result.LastActivityUtc);
        Assert.True((DateTime.UtcNow - result.LastActivityUtc!.Value).TotalMinutes < 5);
    }

    [Fact]
    public async Task DetectAsync_NoGitRepo_FallsBackToFileModifiedTime()
    {
        var filePath = Path.Combine(_root, "file.txt");
        File.WriteAllText(filePath, "hello");

        var detector = new ActivityDetector();
        var result = await detector.DetectAsync(_root);

        Assert.Equal(ProjectActivitySource.FileModifiedTime, result.Source);
        Assert.NotNull(result.LastActivityUtc);
    }

    [Fact]
    public async Task DetectAsync_FallbackIgnoresArtifactFolders()
    {
        var nodeModules = Path.Combine(_root, "node_modules");
        Directory.CreateDirectory(nodeModules);
        File.WriteAllText(Path.Combine(nodeModules, "newer.txt"), "x");
        // Backdate the only real source file so we can confirm node_modules wasn't the source of the result.
        var sourceFile = Path.Combine(_root, "source.txt");
        File.WriteAllText(sourceFile, "hello");
        File.SetLastWriteTimeUtc(sourceFile, DateTime.UtcNow.AddDays(-100));

        var detector = new ActivityDetector();
        var result = await detector.DetectAsync(_root);

        Assert.Equal(ProjectActivitySource.FileModifiedTime, result.Source);
        Assert.True((DateTime.UtcNow - result.LastActivityUtc!.Value).TotalDays >= 99);
    }

    private void RunGit(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        process.WaitForExit();
    }
}
