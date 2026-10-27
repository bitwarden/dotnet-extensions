using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Infrastructure.Testing;

/// <summary>
/// Controls how <see cref="InfrastructureCollector.CollectInfrastructure{TEntryPoint}"/> runs a
/// service's startup.
/// </summary>
public sealed class InfrastructureCollectionOptions
{
    /// <summary>
    /// Whether to run the service as self-hosted by setting <c>globalSettings:selfHosted</c>.
    /// Defaults to <see langword="false"/>, which runs it as cloud.
    /// </summary>
    public bool SelfHosted { get; set; }

    /// <summary>
    /// The hosting environment to run the service in. Defaults to
    /// <see cref="Environments.Production"/> so the result matches what ships.
    /// </summary>
    public string Environment { get; set; } = Environments.Production;

    /// <summary>
    /// Additional configuration values for the service. They are applied as host configuration,
    /// so they are visible to code that reads configuration while building the host.
    /// </summary>
    public IDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>();
}
