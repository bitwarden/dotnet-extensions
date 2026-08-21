using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.IntegrationTests;

/// <summary>
/// Creates a temporary .NET project directory and drives builds out-of-process via the
/// dotnet CLI. This avoids in-process MSBuild version conflicts that arise when multiple
/// SDK versions are installed on the same machine (e.g. the preinstalled SDK on CI plus
/// the pinned SDK added by actions/setup-dotnet).
/// </summary>
internal sealed class TempDotNetProject : IDisposable
{
    private const string TargetFramework = "net10.0";

    private static readonly string ThisAssemblyDirectory =
        Path.GetDirectoryName(typeof(TempDotNetProject).Assembly.Location)!;

    /// <summary>
    /// Locally built NuGet packages discovered from the repo's package projects.
    /// These are copied into each project's local feed so that the temp project can
    /// pick up an in-development version when <c>Sdk.targets</c> references that version.
    /// </summary>
    public static IReadOnlyCollection<FileInfo> LocalNugetPackages { get; } = DiscoverLocalPackages();

    private readonly string _directory;
    private readonly string _projectPath;
    private readonly string _sdk;
    private readonly List<(string Name, string Value)> _properties = [];
    private readonly List<(string Type, string Include, Dictionary<string, string?> Metadata)> _items = [];
    private bool _projectWritten;
    private bool _hasRestored;

    public TempDotNetProject(string sdk = "Microsoft.NET.Sdk.Web")
    {
        _sdk = sdk;
        _directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(_directory);
        _projectPath = Path.Combine(_directory, "Test.csproj");

        CopyGlobalJson();
        WriteNugetConfig();
    }

    public string Directory => _directory;

    /// <summary>Adds a property to the generated project file, placed after the Sdk.props import.</summary>
    public TempDotNetProject WithProperty(string name, string value)
    {
        _properties.Add((name, value));
        return this;
    }

    /// <summary>Adds an MSBuild item to the generated project file.</summary>
    public TempDotNetProject WithItem(string type, string include, Dictionary<string, string?>? metadata = null)
    {
        _items.Add((type, include, metadata ?? []));
        return this;
    }

    /// <summary>Writes a file into the project directory.</summary>
    public TempDotNetProject WithFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_directory, name), content);
        return this;
    }

    /// <summary>
    /// Adds the standard Bitwarden SDK web application entry point.  The
    /// <paramref name="additional"/> snippet is injected after <c>app.Build()</c>.
    /// </summary>
    public TempDotNetProject WithDefaultProgramCs(string? additional = null)
    {
        return WithFile("Program.cs", $"""
            var builder = WebApplication.CreateBuilder(args);
            builder.UseBitwardenSdk();

            var app = builder.Build();

            {additional}

            app.Run();
            """);
    }

    /// <summary>Restores and builds the project, returning structured diagnostics.</summary>
    public DotNetBuildResult Build()
    {
        EnsureProjectWritten();
        var result = RunDotNet(["build", _projectPath, "--nologo"]);
        _hasRestored = true;
        return result;
    }

    /// <summary>
    /// Runs MSBuild targets (e.g. <c>Publish</c>, <c>PublishContainer</c>) on the project.
    /// </summary>
    public DotNetBuildResult MsBuild(
        bool restore,
        string[] targets,
        IReadOnlyDictionary<string, string>? extraProperties = null)
    {
        EnsureProjectWritten();

        var args = new List<string> { "msbuild", _projectPath, "--nologo" };
        if (restore) args.Add("-restore");
        foreach (var target in targets)
            args.Add($"-t:{target}");
        if (extraProperties != null)
            foreach (var (k, v) in extraProperties)
                args.Add($"-p:{k}={v}");

        var result = RunDotNet(args);
        if (restore) _hasRestored = true;
        return result;
    }

    /// <summary>
    /// Reads a single MSBuild property value from the project.  Ensures packages are
    /// restored first so that NuGet-generated imports are available.
    /// </summary>
    public string? GetProperty(string name)
    {
        EnsureRestored();

        using var process = Process.Start(MakeProcessStartInfo(
            $"msbuild \"{_projectPath}\" --nologo -getProperty:{name}"))!;

        // Drain stderr concurrently even though we don't use it; failing to read it
        // can fill the pipe buffer and deadlock if the process writes enough there.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        process.WaitForExit();

        var output = stdoutTask.Result.Trim();
        return string.IsNullOrEmpty(output) ? null : output;
    }

    /// <summary>Returns true if <paramref name="name"/> appears in <c>DefineConstants</c>.</summary>
    public bool HasConstant(string name)
    {
        var constants = GetProperty("DefineConstants") ?? "";
        return constants.Split(';').Any(c => string.Equals(c.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_directory, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private void EnsureProjectWritten()
    {
        if (_projectWritten) return;
        _projectWritten = true;
        WriteProjectFile();
    }

    private void EnsureRestored()
    {
        if (_hasRestored) return;
        _hasRestored = true;
        EnsureProjectWritten();
        RunDotNet(["restore", _projectPath, "--nologo"]);
    }

    private void WriteProjectFile()
    {
        var sdkPropsPath = Path.Combine(ThisAssemblyDirectory, "Sdk", "Sdk.props");
        var sdkTargetsPath = Path.Combine(ThisAssemblyDirectory, "Sdk", "Sdk.targets");

        var propertiesXml = _properties.Count == 0 ? "" : $"""

              <PropertyGroup>
                {string.Join("\n    ", _properties.Select(p => $"<{p.Name}>{SecurityElement.Escape(p.Value)}</{p.Name}>"))}
              </PropertyGroup>
            """;

        var itemsXml = _items.Count == 0 ? "" : $"""

            {BuildItemsXml()}
            """;

        File.WriteAllText(_projectPath, $"""
            <Project Sdk="{_sdk}">
              <PropertyGroup>
                <TargetFramework>{TargetFramework}</TargetFramework>
              </PropertyGroup>
              <Import Project="{sdkPropsPath}" />{propertiesXml}{itemsXml}
              <Import Project="{sdkTargetsPath}" />
            </Project>
            """);
    }

    private string BuildItemsXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("  <ItemGroup>");
        foreach (var (type, include, metadata) in _items)
        {
            sb.Append($"    <{type} Include=\"{SecurityElement.Escape(include)}\"");
            if (metadata.Count == 0)
            {
                sb.AppendLine(" />");
            }
            else
            {
                sb.AppendLine(">");
                foreach (var (key, value) in metadata)
                    sb.AppendLine($"      <{key}>{SecurityElement.Escape(value ?? "")}</{key}>");
                sb.AppendLine($"    </{type}>");
            }
        }
        sb.Append("  </ItemGroup>");
        return sb.ToString();
    }

    private void CopyGlobalJson()
    {
        // Copy global.json so the subprocess uses the same feature-band SDK as the repo,
        // preventing dotnet from selecting the highest installed SDK (which may carry a
        // different MSBuild version and break package imports).
        // Override rollForward to "latestPatch" so the subprocess succeeds even when the
        // exact pinned patch (e.g. 10.0.202) is absent locally — it will roll to the
        // nearest higher patch in the same feature band (10.0.2xx) instead.
        var globalJson = Path.GetFullPath(
            Path.Combine(ThisAssemblyDirectory, "..", "..", "..", "..", "..", "..", "..", "global.json"));
        if (!File.Exists(globalJson)) return;

        var content = File.ReadAllText(globalJson)
            .Replace("\"rollForward\": \"disable\"", "\"rollForward\": \"latestPatch\"");
        File.WriteAllText(Path.Combine(_directory, "global.json"), content);
    }

    private void WriteNugetConfig()
    {
        // Copy locally built packages into a flat local feed directory so that
        // the temp project can pick up an in-development version if Sdk.targets
        // references that exact version.  For published versions, nuget.org is used.
        var localPackagesDir = Path.Combine(_directory, "local-packages");
        System.IO.Directory.CreateDirectory(localPackagesDir);
        foreach (var package in LocalNugetPackages)
            File.Copy(package.FullName, Path.Combine(localPackagesDir, package.Name), overwrite: true);

        File.WriteAllText(Path.Combine(_directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{localPackagesDir}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    private DotNetBuildResult RunDotNet(IEnumerable<string> args)
    {
        // Quote any arg that contains spaces but isn't already a flag.
        var arguments = string.Join(" ", args.Select(a =>
            a.StartsWith('-') || !a.Contains(' ') ? a : $"\"{a}\""));

        using var process = Process.Start(MakeProcessStartInfo(arguments))!;

        // Read stdout and stderr concurrently. Reading them sequentially risks a deadlock:
        // the process fills the stderr pipe buffer waiting for it to be drained, while we
        // block on ReadToEnd() waiting for stdout — neither side can make progress.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        process.WaitForExit();

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;
        var fullOutput = (stdout.Length > 0 && stderr.Length > 0)
            ? stdout + System.Environment.NewLine + stderr
            : stdout + stderr;

        return new DotNetBuildResult(
            process.ExitCode == 0,
            fullOutput,
            ParseDiagnostics(fullOutput, "warning"),
            ParseDiagnostics(fullOutput, "error"));
    }

    private ProcessStartInfo MakeProcessStartInfo(string arguments) =>
        new("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _directory,
        };

    private static IReadOnlyList<BuildDiagnostic> ParseDiagnostics(string output, string severity)
    {
        // MSBuild diagnostic format (examples):
        //   /path/to/file.cs(5,1): error CS0234: The type ... [/path/to/Test.csproj]
        //   /path/to/Sdk.targets(95,5): warning BW0004: BitInclude... [/path/to/Test.csproj]
        //
        // The trailing "[/path/to/Project.csproj]" is stripped by anchoring to an absolute
        // path (Unix "/" or Windows "X:\") rather than a bare "[.*]". A bare bracket match
        // is ambiguous: a message that contains "[Something]" would be cut short because the
        // greedy inner quantifier would consume the content up to the last "]" on the line.
        var pattern = new Regex(
            $@"\b{Regex.Escape(severity)}\s+([A-Za-z]{{2,}}\d+)\s*:\s*(.+?)(?:\s+\[(?:[A-Za-z]:\\|/)[^\]]+\])?\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Deduplicate: MSBuild's outer/inner build passes can emit the same warning
        // (same code + message) twice — from the outer project evaluation and from the
        // inner build. DistinctBy removes exact duplicates while preserving distinct
        // warnings that share a code but differ in message (e.g. different source locations).
        return pattern.Matches(output)
            .Select(m => new BuildDiagnostic(m.Groups[1].Value, m.Groups[2].Value.Trim()))
            .DistinctBy(d => (d.Code, d.Message))
            .ToList();
    }

    private static IReadOnlyCollection<FileInfo> DiscoverLocalPackages()
    {
        // Walk up from the test output directory to the extensions root, then into
        // each package's Debug output to find the locally built .nupkg.
        var extensionsRoot = new DirectoryInfo(
            Path.Combine(ThisAssemblyDirectory, "..", "..", "..", "..", "..", ".."));

        Type[] markerTypes =
        [
            typeof(IFeatureService),
            typeof(BitwardenAuthenticationServiceCollectionExtensions),
            typeof(IBitwardenEnvironment),
        ];

        return markerTypes.Select(type =>
        {
            var assemblyName = type.Assembly.GetName();
            var dir = new DirectoryInfo(
                Path.Combine(extensionsRoot.FullName, assemblyName.Name!, "src", "bin", "Debug"));
            var searchFile = $"{assemblyName.Name}.{assemblyName.Version!.ToString(3)}.nupkg";

            return dir.EnumerateFiles(searchFile).SingleOrDefault()
                ?? throw new InvalidOperationException(
                    $"Could not find nupkg file {searchFile} in directory {dir.FullName}");
        }).ToArray();
    }
}

/// <summary>The result of a <c>dotnet build</c> or <c>dotnet msbuild</c> invocation.</summary>
internal sealed class DotNetBuildResult
{
    public DotNetBuildResult(
        bool succeeded,
        string output,
        IReadOnlyList<BuildDiagnostic> warnings,
        IReadOnlyList<BuildDiagnostic> errors)
    {
        Succeeded = succeeded;
        Output = output;
        Warnings = warnings;
        Errors = errors;
    }

    public bool Succeeded { get; }
    public string Output { get; }
    public IReadOnlyList<BuildDiagnostic> Warnings { get; }
    public IReadOnlyList<BuildDiagnostic> Errors { get; }

    // Aliases so tests read naturally with either name.
    public IReadOnlyList<BuildDiagnostic> WarningEvents => Warnings;
    public IReadOnlyList<BuildDiagnostic> ErrorEvents => Errors;

    public string GetConsoleLog() => Output;
}

/// <summary>A single MSBuild diagnostic (warning or error) parsed from build output.</summary>
internal sealed class BuildDiagnostic
{
    public BuildDiagnostic(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }
}
