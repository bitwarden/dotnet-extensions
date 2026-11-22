using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Writes the infrastructure a service declares (<c>Bitwarden.Server.Sdk.Infrastructure</c>'s requirements, such as
/// MessageBroker's topics and queues) to a file, without starting the service.
/// </summary>
/// <remarks>
/// <para>
/// The file has <c>InfrastructureManifest.ToJsonString</c>'s shape (schema version 1), so whatever reads a manifest
/// collected in a test reads this one. It can be committed beside the service and checked against the code, as its
/// OpenAPI document is.
/// </para>
/// <para>
/// Answered from the built application, as <c>--emit-openapi</c> builds it: describing, so no work of the service's
/// runs and nothing it depends on has to be reachable. Unlike the OpenAPI document it isn't even started, since the
/// requirements are all declared by registration. The hosting mode is the one the process is configured with, so
/// <c>--globalSettings:selfHosted=true</c> writes the self-hosted manifest.
/// </para>
/// </remarks>
public static class InfrastructureExport
{
    /// <summary>
    /// The switch that asks a service to write its manifest and exit.
    /// </summary>
    public const string Switch = "--emit-infrastructure";

    /// <summary>
    /// The manifest format written, as <c>InfrastructureManifest.CurrentSchemaVersion</c>.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Finds the path a caller asked the manifest to be written to.
    /// </summary>
    /// <param name="args">The process command line.</param>
    /// <param name="path">The path to write to, when the switch is present.</param>
    /// <returns>Whether the service was asked to export rather than run.</returns>
    public static bool Requested(string[] args, out string path)
    {
        var at = Array.IndexOf(args, Switch);
        if (at >= 0 && at + 1 < args.Length)
        {
            path = args[at + 1];
            return true;
        }

        path = string.Empty;
        return false;
    }

    /// <summary>
    /// Writes the manifest to a file, creating the directory if it isn't there.
    /// </summary>
    /// <param name="app">A built, unstarted application.</param>
    /// <param name="path">Where to write it.</param>
    public static async Task WriteAsync(WebApplication app, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Indented, with a trailing newline: committed, so read as a diff.
        await File.WriteAllTextAsync(path, Manifest(app.Services, app.Environment).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>
    /// The manifest, in <c>InfrastructureManifest.ToJsonString</c>'s property order.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="host">The application's host environment.</param>
    internal static JsonObject Manifest(IServiceProvider services, IHostEnvironment host)
    {
        var environment = services.GetRequiredService<IBitwardenEnvironment>();
        var requirements = new JsonObject();
        foreach (var (kind, list) in services.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            requirements[kind] = new JsonArray([.. list.Select(requirement => requirement.DeepClone())]);
        }

        return new JsonObject
        {
            ["schemaVersion"] = SchemaVersion,
            ["selfHosted"] = environment.SelfHosted,
            ["selfHostFlavor"] = environment.SelfHosted ? environment.SelfHostFlavor : null,
            ["environment"] = host.EnvironmentName,
            ["requirements"] = requirements,
        };
    }
}
