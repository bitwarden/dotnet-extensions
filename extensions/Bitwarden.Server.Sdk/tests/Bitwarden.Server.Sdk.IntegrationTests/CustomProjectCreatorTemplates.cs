using Microsoft.Build.Utilities.ProjectCreation;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public static class CustomProjectCreatorTemplates
{
    private static readonly string ThisAssemblyDirectory = Path.GetDirectoryName(typeof(CustomProjectCreatorTemplates).Assembly.Location)!;

    public static ProjectCreator SdkProject(this ProjectCreatorTemplates templates,
        string? additional = null,
        Action<ProjectCreator>? customAction = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(dir);

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
                targetFramework: "net8.0")
            .Import(Path.Combine(ThisAssemblyDirectory, "sdk", "Sdk.props"))
            .CustomAction(customAction)
            .Import(Path.Combine(ThisAssemblyDirectory, "sdk", "Sdk.targets"));
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
