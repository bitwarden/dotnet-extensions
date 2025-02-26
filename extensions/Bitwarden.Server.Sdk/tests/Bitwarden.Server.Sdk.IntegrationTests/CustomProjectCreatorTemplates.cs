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
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        // TODO: Create a package repo that contains Bitwarden.Server.Sdk.Features as well as nuget
        using (CreateDefaultPackageRepository(dir))
        {
            File.WriteAllText(Path.Combine(dir, "Program.cs"), $"""
                var builder = WebApplication.CreateBuilder(args);
                builder.UseBitwardenSdk();

                var app = builder.Build();

                {additional}

                app.Run();
                """
            );

            return ProjectCreator.Templates.SdkCsproj(
                    path: Path.Combine(dir, "Test.csproj"),
                    sdk: "Microsoft.NET.Sdk.Web",
                    targetFramework: TargetFramework)
                .Import(Path.Combine(ThisAssemblyDirectory, "Sdk", "Sdk.props"))
                .CustomAction(customAction)
                .Import(Path.Combine(ThisAssemblyDirectory, "Sdk", "Sdk.targets"))
                .TryBuild(restore: true, out result, out buildOutput);
        }
    }

    public static PackageRepository CreateDefaultPackageRepository(string dir)
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

        return PackageRepository.Create(dir, [new Uri("https://api.nuget.org/v3/index.json"), ..feeds]);
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
