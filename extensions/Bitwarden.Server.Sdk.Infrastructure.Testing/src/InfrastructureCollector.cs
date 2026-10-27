using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Infrastructure.Testing;

/// <summary>
/// Collects the infrastructure a service declares during its startup.
/// </summary>
public static class InfrastructureCollector
{
    private const string SelfHostedKey = "globalSettings:selfHosted";

    /// <summary>
    /// Runs the startup of the service whose entry point is in the same assembly as
    /// <typeparamref name="TEntryPoint"/> and returns the infrastructure it declares.
    /// </summary>
    /// <remarks>
    /// The service's hosted services and startup options validation are removed before it starts,
    /// so it does not connect to any infrastructure and does not need valid configuration for it.
    /// </remarks>
    /// <typeparam name="TEntryPoint">A type in the service's entry point assembly, typically <c>Program</c>.</typeparam>
    /// <param name="configure">Adjusts how the service is run.</param>
    /// <returns>
    /// The declared requirements, in the order they were first declared. Each is a JSON object with a
    /// <c>kind</c> property and the properties it was declared with.
    /// </returns>
    public static IReadOnlyList<JsonObject> CollectInfrastructure<TEntryPoint>(
        Action<InfrastructureCollectionOptions>? configure = null)
        where TEntryPoint : class
    {
        var options = new InfrastructureCollectionOptions();
        configure?.Invoke(options);

        var hostConfiguration = new Dictionary<string, string?>(options.Configuration)
        {
            [HostDefaults.EnvironmentKey] = options.Environment,
            [SelfHostedKey] = options.SelfHosted ? "true" : "false",
        };

        using var factory = new CollectorFactory<TEntryPoint>(hostConfiguration);
        return factory.Services.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements;
    }

    private sealed class CollectorFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
        where TEntryPoint : class
    {
        private readonly IReadOnlyDictionary<string, string?> _hostConfiguration;

        public CollectorFactory(IReadOnlyDictionary<string, string?> hostConfiguration)
        {
            _hostConfiguration = hostConfiguration;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // With minimal hosting, host configuration is handed to the entry point as command line
            // arguments, so it is visible while Program builds the host rather than only afterwards.
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(_hostConfiguration));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IStartupValidator>();
            });
        }
    }
}
