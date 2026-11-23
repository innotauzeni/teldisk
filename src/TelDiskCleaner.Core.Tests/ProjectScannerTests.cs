using TelDiskCleaner.Core.ProjectCleanup;

namespace TelDiskCleaner.Core.Tests;

public class ProjectScannerTests : IDisposable
{
    private readonly string _root;

    public ProjectScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TelDiskCleanerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ScanForArtifacts_FindsNodeModulesAndReportsCorrectSize()
    {
        var project = Path.Combine(_root, "my-angular-app");
        var nodeModules = Path.Combine(project, "node_modules");
        var nested = Path.Combine(nodeModules, "some-package");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "index.js"), new byte[500]);
        File.WriteAllBytes(Path.Combine(nodeModules, "package.json"), new byte[100]);

        var scanner = new ProjectScanner();
        var candidates = scanner.ScanForArtifacts(new[] { _root }, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(project, candidate.ProjectRoot);
        var artifact = Assert.Single(candidate.Artifacts);
        Assert.Equal(ArtifactFolderDefinitions.NodeModules, artifact.Kind);
        Assert.Equal(600, artifact.SizeBytes);
    }

    [Fact]
    public void ScanForArtifacts_DoesNotDescendIntoArtifactFolder()
    {
        // A node_modules folder that itself contains a nested "node_modules" (common in real trees)
        // should not be reported as a second, separate candidate - only the outer one counts.
        var project = Path.Combine(_root, "app");
        var nodeModules = Path.Combine(project, "node_modules");
        var nestedNodeModules = Path.Combine(nodeModules, "some-package", "node_modules");
        Directory.CreateDirectory(nestedNodeModules);
        File.WriteAllBytes(Path.Combine(nestedNodeModules, "x.js"), new byte[42]);

        var scanner = new ProjectScanner();
        var candidates = scanner.ScanForArtifacts(new[] { _root }, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        var artifact = Assert.Single(candidate.Artifacts);
        Assert.Equal(nodeModules, artifact.Path);
        Assert.Equal(42, artifact.SizeBytes);
    }

    [Fact]
    public void ScanForArtifacts_FlutterBuildOnlyCountsNextToPubspec()
    {
        var flutterApp = Path.Combine(_root, "flutter_app");
        Directory.CreateDirectory(Path.Combine(flutterApp, "build"));
        Directory.CreateDirectory(Path.Combine(flutterApp, ".dart_tool"));
        File.WriteAllText(Path.Combine(flutterApp, "pubspec.yaml"), "name: flutter_app");

        var scripts = Path.Combine(_root, "plain_repo");
        Directory.CreateDirectory(Path.Combine(scripts, "build"));

        var candidates = new ProjectScanner().ScanForArtifacts(new[] { _root }, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(flutterApp, candidate.ProjectRoot);
        Assert.Equal(2, candidate.Artifacts.Count);
    }

    [Fact]
    public void ScanForArtifacts_BinAndObjOnlyCountNextToAProjectFile()
    {
        var dotnetApp = Path.Combine(_root, "dotnet_app");
        Directory.CreateDirectory(Path.Combine(dotnetApp, "bin"));
        Directory.CreateDirectory(Path.Combine(dotnetApp, "obj"));
        File.WriteAllText(Path.Combine(dotnetApp, "App.csproj"), "<Project />");

        var nodeTool = Path.Combine(_root, "node_tool");
        Directory.CreateDirectory(Path.Combine(nodeTool, "bin"));

        var candidates = new ProjectScanner().ScanForArtifacts(new[] { _root }, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(dotnetApp, candidate.ProjectRoot);
        Assert.Equal(2, candidate.Artifacts.Count);
    }

    [Fact]
    public void ScanForArtifacts_GroupsMultipleArtifactsUnderSameProjectRoot()
    {
        var project = Path.Combine(_root, "angular-app");
        Directory.CreateDirectory(Path.Combine(project, "node_modules"));
        Directory.CreateDirectory(Path.Combine(project, "dist"));

        var scanner = new ProjectScanner();
        var candidates = scanner.ScanForArtifacts(new[] { _root }, CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(2, candidate.Artifacts.Count);
    }
}
