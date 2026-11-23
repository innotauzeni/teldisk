using Microsoft.Extensions.Time.Testing;
using TelDiskCleaner.Core.Backup;
using TelDiskCleaner.Core.Common;
using TelDiskCleaner.Core.FileFinder;
using TelDiskCleaner.Core.Recovery;

namespace TelDiskCleaner.Core.Tests;

public sealed class ProFeatureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TelDiskCleanerTests_" + Guid.NewGuid());

    public ProFeatureTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        foreach (var dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(dir, FileAttributes.Directory);
        }
        Directory.Delete(_root, recursive: true);
    }

    private string Write(string relative, int size, byte fill = 1, DateTime? modifiedUtc = null)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, size).ToArray());
        if (modifiedUtc is not null) File.SetLastWriteTimeUtc(path, modifiedUtc.Value);
        return path;
    }

    private RecoveryVault Vault(TimeProvider clock) =>
        new(Path.Combine(_root, "vault.json"), _ => Path.Combine(_root, "vault"), clock);

    [Fact]
    public void Vault_MovesFileAway_AndRestoresItToTheSamePlace()
    {
        var file = Write("docs/report.pdf", 500);
        var vault = Vault(TimeProvider.System);

        var item = vault.MoveToVault(file, "Test");
        Assert.False(File.Exists(file));
        Assert.Single(vault.GetItems());

        var restoredTo = vault.Restore(item);
        Assert.Equal(file, restoredTo);
        Assert.True(File.Exists(file));
        Assert.Empty(vault.GetItems());
    }

    [Fact]
    public void Vault_RestoresBesideANewFileInsteadOfOverwritingIt()
    {
        var file = Write("docs/report.pdf", 500);
        var vault = Vault(TimeProvider.System);
        var item = vault.MoveToVault(file, "Test");
        Write("docs/report.pdf", 10, fill: 9);

        var restoredTo = vault.Restore(item);

        Assert.EndsWith("report (restored).pdf", restoredTo);
        Assert.Equal(10, new FileInfo(file).Length);
        Assert.Equal(500, new FileInfo(restoredTo).Length);
    }

    [Fact]
    public void Vault_PurgesItemsAfterSevenDays()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var vault = Vault(clock);
        var old = vault.MoveToVault(Write("a.bin", 100), "Test");
        clock.Advance(TimeSpan.FromDays(5));
        vault.MoveToVault(Write("b.bin", 50), "Test");

        clock.Advance(TimeSpan.FromDays(3));
        var freed = vault.PurgeExpired();

        Assert.Equal(100, freed);
        Assert.False(File.Exists(old.VaultPath));
        Assert.Single(vault.GetItems());
    }

    [Fact]
    public void LargeFileFinder_ReturnsBiggestFilesFirst_AboveTheMinimum()
    {
        Write("small.txt", 10);
        Write("a/medium.bin", 3000);
        Write("b/large.bin", 9000);
        Write("b/c/huge.bin", 20000);

        var found = LargeFileFinder.Find(_root, minBytes: 1000, maxResults: 2, CancellationToken.None);

        Assert.Equal(new[] { "huge.bin", "large.bin" }, found.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void DuplicateFinder_GroupsIdenticalFilesOnly()
    {
        var older = Write("photos/a.jpg", 200_000, fill: 7, DateTime.UtcNow.AddDays(-10));
        Write("backup/a-copy.jpg", 200_000, fill: 7);
        Write("other/same-size-different.jpg", 200_000, fill: 8);
        Write("tiny1.txt", 5, fill: 1);
        Write("tiny2.txt", 5, fill: 1);

        var groups = DuplicateFinder.Find(_root, minBytes: 1024, CancellationToken.None);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(older, group.Files[0].Path); // oldest first, so the original is the one kept by default
        Assert.Equal(200_000, group.WastedBytes);
    }

    [Fact]
    public void Backup_CopiesOnlyChanges_SkipsExcludedFolders_AndKeepsOldVersions()
    {
        var source = Path.Combine(_root, "Projects");
        Write("Projects/app/main.cs", 100, fill: 1, DateTime.UtcNow.AddDays(-2));
        Write("Projects/app/node_modules/lib.js", 100);
        var removedLater = Write("Projects/notes.txt", 20);

        var job = new BackupJob { Name = "Test", FolderName = "Test", Sources = { source }, Destination = Path.Combine(_root, "USB") };
        var service = new BackupService();

        var first = service.Run(job, null, CancellationToken.None);
        Assert.Equal(2, first.Copied);
        var current = Path.Combine(_root, "USB", "Test", "current", "Projects");
        Assert.True(File.Exists(Path.Combine(current, "app", "main.cs")));
        Assert.False(Directory.Exists(Path.Combine(current, "app", "node_modules")));

        Write("Projects/app/main.cs", 150, fill: 2);
        File.Delete(removedLater);
        var second = service.Run(job, null, CancellationToken.None);

        Assert.Equal(1, second.Copied);
        Assert.Equal(1, second.Versioned);
        Assert.Equal(1, second.Removed);
        Assert.Equal(150, new FileInfo(Path.Combine(current, "app", "main.cs")).Length);
        var versions = Directory.GetDirectories(Path.Combine(_root, "USB", "Test", "versions"));
        var oldMain = Path.Combine(Assert.Single(versions), "Projects", "app", "main.cs");
        Assert.Equal(100, new FileInfo(oldMain).Length);

        var third = service.Run(job, null, CancellationToken.None);
        Assert.Equal(0, third.Copied);
        Assert.Equal(1, third.Unchanged);
    }

    [Fact]
    public async Task Restore_ToOriginalFolder_KeepsDifferentLocalFiles()
    {
        var source = Path.Combine(_root, "Docs");
        var cv = Write("Docs/cv.docx", 100);
        Write("Docs/letter.docx", 50);
        var job = new BackupJob { Name = "Docs", FolderName = "Docs", Sources = { source }, Destination = Path.Combine(_root, "USB") };
        var service = new BackupService();
        service.Run(job, null, CancellationToken.None);

        File.Delete(Path.Combine(source, "letter.docx"));
        Write("Docs/cv.docx", 999, fill: 5);

        var result = await service.RestoreAsync(job, null, null, CancellationToken.None);

        Assert.Equal(2, result.Restored);
        Assert.Equal(1, result.KeptLocalCopies);
        Assert.Equal(100, new FileInfo(cv).Length);
        Assert.True(File.Exists(Path.Combine(source, "letter.docx")));
        Assert.Single(Directory.GetFiles(source, "cv (before restore *).docx"));
    }

    [Fact]
    public void Backup_RefusesADestinationInsideTheSource()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "Data")).FullName;
        var job = new BackupJob { Name = "Bad", FolderName = "Bad", Sources = { source }, Destination = Path.Combine(source, "backup") };

        Assert.NotNull(BackupService.Validate(job));
    }

    [Fact]
    public async Task ScheduledTask_CanBeCreatedAndDeleted()
    {
        var service = new ScheduledTaskService();
        var name = "TelDiskCleaner Test " + Guid.NewGuid().ToString("N")[..8];

        var created = await service.CreateAsync(name, @"C:\Windows\System32\cmd.exe", "/c exit", TaskSchedule.Weekly, "Test task");
        try
        {
            Assert.True(created.Success, created.StandardError);
            Assert.True(await service.ExistsAsync(name));
        }
        finally
        {
            await service.DeleteAsync(name);
        }
        Assert.False(await service.ExistsAsync(name));
    }
}
