using Microsoft.Build.Utilities.ProjectCreation;
using Bitwarden.Server.Sdk.Features;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public static class CustomProjectCreatorTemplates
{
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

    public static ProjectCreator SdkProject(this ProjectCreatorTemplates templates, Action<ProjectCreator>? customAction = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        return ProjectCreator.Templates.SdkCsproj(
                path: Path.Combine(dir, "Test.csproj"),
                sdk: "Microsoft.NET.Sdk.Web",
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
        // Use this as a list of marker types for assemblies that should be added as available in a
        // pseudo nuget feed.
        List<Type> packages = [typeof(IFeatureService)];

        var feeds = new List<Uri>();

        foreach (var package in packages)
        {
            var assembly = package.Assembly;
            var assemblyName = assembly.GetName()!;
            var pr = PackageFeed.Create(new FileInfo(assembly.Location).Directory!)
                .Package(assemblyName.Name!, assemblyName.Version!.ToString(3))
                .FileCustom(Path.Combine("lib", TargetFramework, assemblyName.Name + ".dll"), new FileInfo(assembly.Location))
                .Save();

            feeds.Add(pr);
        }

        return PackageRepository.Create(project.GetProjectDirectory(), [new Uri("https://api.nuget.org/v3/index.json"), .. feeds]);
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
