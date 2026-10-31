using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Writes a service's OpenAPI document to a file, without starting the service.
/// </summary>
/// <remarks>
/// <para>
/// A service's contract belongs in source control beside the code that produces it, so that a
/// change to the API is a reviewable diff rather than something a consumer discovers later. That
/// requires a way to obtain the document at development time.
/// </para>
/// <para>
/// Deliberately not <c>Microsoft.Extensions.ApiDescription.Server</c>, which does this at compile
/// time. To read the document, that tool constructs the application and <em>starts</em> it, so
/// every hosted service runs: building a service whose hosted services open a database and apply
/// migrations would do both. A service that cannot be compiled without
/// its infrastructure present is not one that can be built in CI, so the document is taken from a
/// built-but-unstarted application instead.
/// </para>
/// </remarks>
public static class OpenApiExport
{
    /// <summary>
    /// The switch that asks a service to write its contract and exit.
    /// </summary>
    public const string Switch = "--emit-openapi";

    /// <summary>
    /// Finds the path a caller asked the document to be written to.
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
    /// Everything currently registered as an <see cref="IHostedService"/>.
    /// </summary>
    /// <param name="services">The service collection to read.</param>
    /// <remarks>
    /// Taken before a service registers its own, so <see cref="Isolate"/> can tell the host's
    /// background work (which runs the web server, and without which nothing is routed) from
    /// the service's, which must not run.
    /// </remarks>
    public static IReadOnlyCollection<ServiceDescriptor> HostedServices(IServiceCollection services) =>
        [.. services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService))];

    /// <summary>
    /// Strips a service down to what is needed to describe it.
    /// </summary>
    /// <param name="builder">The builder, before it is built.</param>
    /// <param name="keep">The hosted services to leave registered, from <see cref="HostedServices"/>.</param>
    /// <remarks>
    /// <para>
    /// The application has to be started for the document to be complete: an endpoint mapped
    /// with <c>MapGet</c> is not in it until routing has resolved the application's endpoint
    /// data sources, which happens on start. MVC controllers are found without starting, but every
    /// endpoint here is mapped, so a contract produced without starting would silently miss
    /// them.
    /// </para>
    /// <para>
    /// Starting must not mean doing the service's work. The document is produced in CI and by
    /// developers, where a database, a queue, or Identity may be nowhere in reach, so the
    /// service's own hosted services are dropped and the server is bound to an ephemeral
    /// loopback port rather than its configured one.
    /// </para>
    /// </remarks>
    public static void Isolate(WebApplicationBuilder builder, IReadOnlyCollection<ServiceDescriptor> keep)
    {
        for (var i = builder.Services.Count - 1; i >= 0; i--)
        {
            var descriptor = builder.Services[i];

            if (descriptor.ServiceType == typeof(IHostedService) && !keep.Contains(descriptor))
            {
                builder.Services.RemoveAt(i);
                continue;
            }

            // Options validated with ValidateOnStart, which is how a client package refuses to
            // start unconfigured. Right for a service that is about to serve traffic, wrong
            // for one being asked to describe itself: a contract does not depend on the address
            // of anything it calls, and requiring one would mean a developer could not read the
            // document without first configuring every dependency.
            //
            // Registered as IStartupValidator rather than as a hosted service, so removing
            // those does not reach it.
            if (descriptor.ServiceType == typeof(IStartupValidator))
            {
                builder.Services.RemoveAt(i);
            }
        }

        builder.WebHost.UseUrls("http://127.0.0.1:0");
    }

    /// <summary>
    /// Writes the document to a file, creating the directory if it is not there.
    /// </summary>
    /// <param name="app">A built, unstarted application.</param>
    /// <param name="documentName">The document to write. Registered under this name by <c>AddOpenApi</c>.</param>
    /// <param name="path">Where to write it.</param>
    /// <param name="version">The specification version to serialize as.</param>
    public static async Task WriteAsync(
        WebApplication app,
        string documentName,
        string path,
        OpenApiSpecVersion version)
    {
        // Keyed by document name: a service publishing several documents registers one provider
        // for each, so there is no unkeyed one to resolve.
        await app.StartAsync();

        var document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(documentName)
            .GetOpenApiDocumentAsync();

        RequireTagDescriptions(document);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var text = new StreamWriter(File.Create(path));

        // Indented, and with a trailing newline: this file is committed, so it is read as a diff
        // and has to behave like source.
        document.SerializeAs(version, new OpenApiJsonWriter(text));
        await text.WriteLineAsync();
        await text.FlushAsync();
        await app.StopAsync();
    }

    /// <summary>
    /// Refuses a contract with a section nobody has explained.
    /// </summary>
    /// <remarks>
    /// A section's description is the only place a reader is told what its resources are before
    /// working through the operations (see <see cref="ApiTagAttribute.Description"/>). Checked
    /// here, where the contract is written for committing, so a new endpoints class without one is
    /// caught by the developer adding it rather than by the next reader.
    /// </remarks>
    /// <param name="document">The document about to be written.</param>
    /// <exception cref="InvalidOperationException">A section has no description.</exception>
    public static void RequireTagDescriptions(OpenApiDocument document)
    {
        var described = document.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Description))
            .Select(tag => tag.Name)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        var operations = document.Paths?.Values
            .Where(path => path.Operations is not null)
            .SelectMany(path => path.Operations!.Values) ?? [];

        var undescribed = operations
            .SelectMany(operation => operation.Tags ?? Enumerable.Empty<OpenApiTagReference>())
            .Select(tag => tag.Name)
            .OfType<string>()
            .Where(name => !described.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (undescribed.Length > 0)
        {
            throw new InvalidOperationException(
                "Every section of the contract needs a description; give each endpoints class's "
                + "[ApiTag(Description = ...)] one. Undescribed: " + string.Join(", ", undescribed) + ".");
        }
    }
}
