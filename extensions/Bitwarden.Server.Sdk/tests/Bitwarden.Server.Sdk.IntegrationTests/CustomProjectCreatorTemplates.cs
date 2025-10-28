using Microsoft.Build.Utilities.ProjectCreation;
using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public static class CustomProjectCreatorTemplates
{
    static CustomProjectCreatorTemplates()
    {
        // Use this as a list of marker types for assemblies that should be added as available in a
        // pseudo nuget feed.
        var packages = new (Type MarkerType, string Version, (string Dependency, string Version)[])[]
        {
            ( typeof(IFeatureService), "0.1.0", [("LaunchDarkly.ServerSdk", "8.10.3")] ),
            ( typeof(BitwardenAuthenticationServiceCollectionExtensions), "0.1.0", [("Microsoft.AspNetCore.Authentication.JwtBearer", "8.0.20")] ),
        };

        var feeds = new List<Uri>(packages.Length);

        foreach (var (package, version, dependencies) in packages)
        {
            var assembly = package.Assembly;
            var assemblyName = assembly.GetName()!;
            var pr = PackageFeed.Create(new FileInfo(assembly.Location).Directory!)
                .Package(assemblyName.Name!, version)
                .FileCustom(Path.Combine("lib", TargetFramework, assemblyName.Name + ".dll"), new FileInfo(assembly.Location));

            foreach (var (dependencyId, dependencyVersion) in dependencies)
            {
                pr.Dependency(TargetFramework, dependencyId, dependencyVersion);
            }

            feeds.Add(pr.Save());
        }

        Feeds = feeds;
    }

    public static List<Uri> Feeds { get; }

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
        return PackageRepository.Create(project.GetProjectDirectory(), [.. Feeds, new Uri("https://api.nuget.org/v3/index.json")]);
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
