using Bitwarden.Server.Sdk.Environment.Setup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Infrastructure.Testing;

/// <summary>
/// Collects the infrastructure a service declares during its startup.
/// </summary>
public static class InfrastructureCollector
{
    private const string UnknownSelfHostFlavor = "unknown";

    /// <summary>
    /// Runs the startup of the service whose entry point is in the same assembly as
    /// <typeparamref name="TEntryPoint"/> and returns the infrastructure it declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The service is started through <c>WebApplicationFactory</c>, so all of <c>Program</c> runs,
    /// including hosted services, startup options validation, code after <c>builder.Build()</c> and
    /// building the request pipeline. Provide any configuration they need through
    /// <see cref="InfrastructureCollectionOptions.Configuration"/>. Only web services are supported.
    /// </para>
    /// <para>
    /// The hosting mode is applied by registering <c>IBitwardenEnvironment</c> if needed and
    /// post-configuring <see cref="SelfHostDetails"/>, so it is visible to declarations made by
    /// configuring <see cref="InfrastructureOptions"/> with <c>IBitwardenEnvironment</c>. It is not
    /// visible to code that reads configuration while <c>Program</c> registers services.
    /// </para>
    /// <para>
    /// The environment and configuration are applied as host configuration, so they are visible
    /// while <c>Program</c> builds the host.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntryPoint">A type in the service's entry point assembly, typically <c>Program</c>.</typeparam>
    /// <param name="configure">Adjusts how the service is run.</param>
    /// <returns>The declared requirements, together with the hosting mode they were collected for.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="InfrastructureCollectionOptions.SelfHostFlavor"/> is set without
    /// <see cref="InfrastructureCollectionOptions.SelfHosted"/>.
    /// </exception>
    public static InfrastructureManifest CollectInfrastructure<TEntryPoint>(
        Action<InfrastructureCollectionOptions>? configure = null)
        where TEntryPoint : class
    {
        var options = new InfrastructureCollectionOptions();
        configure?.Invoke(options);

        if (options.SelfHostFlavor is not null && !options.SelfHosted)
        {
            throw new InvalidOperationException(
                $"{nameof(InfrastructureCollectionOptions.SelfHostFlavor)} requires {nameof(InfrastructureCollectionOptions.SelfHosted)}.");
        }

        var hostConfiguration = new Dictionary<string, string?>(options.Configuration)
        {
            [HostDefaults.EnvironmentKey] = options.Environment,
        };

        using var factory = new CollectorFactory<TEntryPoint>(hostConfiguration, services => ConfigureEnvironment(services, options));
        var requirements = factory.Services.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements;

        return new InfrastructureManifest(
            options.SelfHosted,
            options.SelfHosted ? options.SelfHostFlavor ?? UnknownSelfHostFlavor : null,
            options.Environment,
            requirements);
    }

    private static void ConfigureEnvironment(IServiceCollection services, InfrastructureCollectionOptions options)
    {
        services.AddBitwardenEnvironment();
        services.PostConfigure<SelfHostDetails>(details =>
        {
            if (options.SelfHosted)
            {
                details.MakeSelfHost(options.SelfHostFlavor ?? UnknownSelfHostFlavor);
            }
            else
            {
                details.MakeCloud();
            }
        });
    }

    private sealed class CollectorFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
        where TEntryPoint : class
    {
        private readonly IReadOnlyDictionary<string, string?> _hostConfiguration;
        private readonly Action<IServiceCollection> _configureServices;

        public CollectorFactory(IReadOnlyDictionary<string, string?> hostConfiguration, Action<IServiceCollection> configureServices)
        {
            _hostConfiguration = hostConfiguration;
            _configureServices = configureServices;
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
            builder.ConfigureTestServices(_configureServices);
        }
    }
}
