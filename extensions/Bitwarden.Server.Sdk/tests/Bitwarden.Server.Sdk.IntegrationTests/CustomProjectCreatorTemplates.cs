using Microsoft.Build.Utilities.ProjectCreation;
using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public static class CustomProjectCreatorTemplates
{
    static CustomProjectCreatorTemplates()
    {
        // A bit hacky but we want to traverse to the extensions root directory so that we can easily traverse into each
        // projects directory. Since all packable projects are going to generate a nupkg on build we can
        // use the genuine nupkg file instead of maintaining their dependency tree twice. It is important to note though
        // that these locally built nuget packages are generally not used. The SDK will usually reference a published
        // to nuget version and the local package will be on vNext. This local file referencing stuff is only for testing
        // integrating new changes in one of the packages into the SDK. To test that you'd bump the version the SDK references
        // to what the current local version of the package is and then it will take precendence and get used in the
        // tests.
        var extensionsRoot = new DirectoryInfo(Path.Combine(ThisAssemblyDirectory, "..", "..", "..", "..", "..", ".."));
        Debug.WriteLine($"ExtensionsRoot: {extensionsRoot.FullName}");

        Type[] markerTypes = [typeof(IFeatureService), typeof(BitwardenAuthenticationServiceCollectionExtensions)];
        var nugetPackages = new FileInfo[markerTypes.Length];

        for (var i = 0; i < nugetPackages.Length; i++)
        {
            var type = markerTypes[i];
            var assemblyName = type.Assembly.GetName();
            var nugetPackageDirectory = new DirectoryInfo(Path.Combine(extensionsRoot.FullName, assemblyName.Name!, "src", "bin", "Debug"));

            var searchFile = $"{assemblyName.Name}.{assemblyName.Version!.ToString(3)}.nupkg";

            var nugetPackageFile = nugetPackageDirectory
                .EnumerateFiles(searchFile)
                .SingleOrDefault();

            if (nugetPackageFile == null)
            {
                throw new InvalidOperationException($"Could not find nupkg file {searchFile} in directory {nugetPackageDirectory}");
            }

            nugetPackages[i] = nugetPackageFile;
        }

        NugetPackages = nugetPackages;
    }

    public static IReadOnlyCollection<FileInfo> NugetPackages { get; }
    private const string TargetFramework = "net8.0";
    private static readonly string ThisAssemblyDirectory = Path.GetDirectoryName(typeof(CustomProjectCreatorTemplates).Assembly.Location)!;

    public static ProjectCreator SdkProject(this ProjectCreatorTemplates templates,
        out bool result,
        out BuildOutput buildOutput,
        string? additional = null,
        Action<ProjectCreator>? customAction = null)
    {
        var project = templates.SdkProject(customAction);

        project.AdditionalFile("Program.cs", $"""
            var builder = WebApplication.CreateBuilder(args);
            builder.UseBitwardenSdk();

            var app = builder.Build();

            {additional}

            app.Run();
            """
        );

        using (project.CreateDefaultPackageRepository())
        {
            return project.TryBuild(restore: true, out result, out buildOutput);
        }
    }

    public static ProjectCreator SdkProject(this ProjectCreatorTemplates templates, Action<ProjectCreator>? customAction = null, string sdk = "Microsoft.NET.Sdk.Web")
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        return ProjectCreator.Templates.SdkCsproj(
                path: Path.Combine(dir, "Test.csproj"),
                sdk: sdk,
                targetFramework: TargetFramework)
            .Import(Path.Combine(ThisAssemblyDirectory, "Sdk", "Sdk.props"))
            .CustomAction(customAction)
            .Import(Path.Combine(ThisAssemblyDirectory, "Sdk", "Sdk.targets"));
    }

    public static void AdditionalFile(this ProjectCreator project, string fileName, string fileContents)
    {
        var parentDir = project.GetProjectDirectory();
        File.WriteAllText(Path.Combine(parentDir, fileName), fileContents);
    }

    public static PackageRepository CreateDefaultPackageRepository(this ProjectCreator project)
    {
        var repo = PackageRepository.Create(project.GetProjectDirectory(), new Uri("https://api.nuget.org/v3/index.json"));

        foreach (var packageFile in NugetPackages)
        {
            repo.Package(packageFile, out _);
        }

        return repo;
    }

    public static string GetProjectDirectory(this ProjectCreator project)
    {
        var parentDir = Directory.GetParent(project.FullPath);
        Assert.NotNull(parentDir);
        return parentDir.FullName;
    }

    public static ProjectCreator TryGetConstant(this ProjectCreator project, string constant, out bool result)
    {
        result = false;
        project = project.TryGetPropertyValue("DefineConstants", out var constants);

        if (string.IsNullOrEmpty(constants))
        {
            return project;
        }

        var allConstants = constants.Split(';');

        if (!allConstants.Contains(constant))
        {
            return project;
        }

        result = true;
        return project;
    }
}
