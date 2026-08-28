using Microsoft.Build.Utilities.ProjectCreation;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkListDeploymentFilesTests : MSBuildTestBase
{
    private static readonly string SdkPropsPath = Path.Combine(
        Path.GetDirectoryName(typeof(SdkListDeploymentFilesTests).Assembly.Location)!,
        "Sdk", "Sdk.props");

    private static readonly string SdkTargetsPath = Path.Combine(
        Path.GetDirectoryName(typeof(SdkListDeploymentFilesTests).Assembly.Location)!,
        "Sdk", "Sdk.targets");

    private static ProjectCreator CreateProject(string path) =>
        ProjectCreator.Templates.SdkCsproj(
                path: path,
                sdk: "Microsoft.NET.Sdk",
                targetFramework: "net10.0")
            .Import(SdkPropsPath)
            .Import(SdkTargetsPath);

    [Fact]
    public void ListDeploymentFiles_IncludesCsprojPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.Contains(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("Test.csproj", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_IncludesSourceFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "MyClass.cs"), "public class MyClass {}");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.Contains(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("MyClass.cs", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_IncludesBitDeploymentInputItems()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{}");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .ItemInclude("BitDeploymentInput", "appsettings.json")
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.Contains(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_NoDuplicatesWithDiamondDependencies()
    {
        var dDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dDir);
        File.WriteAllText(Path.Combine(dDir, "D.cs"), "public class D {}");
        var dProject = CreateProject(Path.Combine(dDir, "D.csproj")).Save();

        var bDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(bDir);
        var bProject = CreateProject(Path.Combine(bDir, "B.csproj"))
            .ItemProjectReference(dProject.FullPath)
            .Save();

        var cDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(cDir);
        var cProject = CreateProject(Path.Combine(cDir, "C.csproj"))
            .ItemProjectReference(dProject.FullPath)
            .Save();

        var aDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(aDir);

        CreateProject(Path.Combine(aDir, "A.csproj"))
            .ItemProjectReference(bProject.FullPath)
            .ItemProjectReference(cProject.FullPath)
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());

        var messages = buildOutput.MessageEvents
            .Select(m => m.Message)
            .Where(m => m is not null)
            .ToList();

        Assert.Single(messages, m => m!.EndsWith("D.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Single(messages, m => m!.EndsWith("D.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListDeploymentFiles_IncludesFilesFromProjectReferences()
    {
        var libDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "LibClass.cs"), "public class LibClass {}");
        var libProject = CreateProject(Path.Combine(libDir, "Lib.csproj")).Save();

        var appDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "AppClass.cs"), "public class AppClass {}");

        CreateProject(Path.Combine(appDir, "App.csproj"))
            .ItemProjectReference(libProject.FullPath)
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());

        var messages = buildOutput.MessageEvents.Select(m => m.Message).ToList();
        Assert.Contains(messages, m => m?.EndsWith("AppClass.cs", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(messages, m => m?.EndsWith("LibClass.cs", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_IncludesEmbeddedResources()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Strings.resx"), "<root />");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .ItemInclude("EmbeddedResource", "Strings.resx")
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.Contains(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("Strings.resx", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_IncludesContentCopiedToOutput_ExcludesNever()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "template.txt"), "template");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .ItemInclude("Content", "appsettings.json", metadata: new Dictionary<string, string?>
            {
                { "CopyToOutputDirectory", "PreserveNewest" },
            })
            .ItemInclude("Content", "template.txt", metadata: new Dictionary<string, string?>
            {
                { "CopyToOutputDirectory", "Never" },
            })
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());

        var messages = buildOutput.MessageEvents.Select(m => m.Message).ToList();
        Assert.Contains(messages, m => m?.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(messages, m => m?.EndsWith("template.txt", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_IncludesLockFileWhenEnabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "packages.lock.json"), "{}");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .Property("RestorePackagesWithLockFile", "true")
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.Contains(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_ExcludesLockFileWhenDisabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "packages.lock.json"), "{}");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.DoesNotContain(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ListDeploymentFiles_RespectsCustomLockFilePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var lockFilePath = Path.Combine(dir, "custom.lock.json");
        File.WriteAllText(lockFilePath, "{}");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .Property("RestorePackagesWithLockFile", "true")
            .Property("NuGetLockFilePath", lockFilePath)
            .TryBuild("ListDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.Contains(buildOutput.MessageEvents,
            m => m.Message?.EndsWith("custom.lock.json", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void WriteDeploymentFiles_WritesFileToDefaultPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "MyClass.cs"), "public class MyClass {}");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .TryBuild("WriteDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());

        var outputPath = Path.Combine(dir, "obj", "deployment-files.txt");
        Assert.True(File.Exists(outputPath));

        var lines = File.ReadAllLines(outputPath);
        Assert.Contains(lines, l => l.EndsWith("Test.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.EndsWith("MyClass.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WriteDeploymentFiles_RespectsCustomOutputPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var outputPath = Path.Combine(dir, "custom-output.txt");

        CreateProject(Path.Combine(dir, "Test.csproj"))
            .Property("DeploymentFilesOutputPath", outputPath)
            .TryBuild("WriteDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public void WriteDeploymentFiles_NoDuplicatesWithDiamondDependencies()
    {
        var dDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dDir);
        File.WriteAllText(Path.Combine(dDir, "D.cs"), "public class D {}");
        var dProject = CreateProject(Path.Combine(dDir, "D.csproj")).Save();

        var bDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(bDir);
        var bProject = CreateProject(Path.Combine(bDir, "B.csproj"))
            .ItemProjectReference(dProject.FullPath)
            .Save();

        var cDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(cDir);
        var cProject = CreateProject(Path.Combine(cDir, "C.csproj"))
            .ItemProjectReference(dProject.FullPath)
            .Save();

        var aDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(aDir);
        var outputPath = Path.Combine(aDir, "deployment-files.txt");

        CreateProject(Path.Combine(aDir, "A.csproj"))
            .ItemProjectReference(bProject.FullPath)
            .ItemProjectReference(cProject.FullPath)
            .Property("DeploymentFilesOutputPath", outputPath)
            .TryBuild("WriteDeploymentFiles", out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());

        var lines = File.ReadAllLines(outputPath);
        Assert.Single(lines, l => l.EndsWith("D.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Single(lines, l => l.EndsWith("D.cs", StringComparison.OrdinalIgnoreCase));
    }
}
