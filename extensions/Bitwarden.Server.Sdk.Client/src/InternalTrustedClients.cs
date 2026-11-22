namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The names of the clients that call the deployment's own services, and so trust its internal authorities
/// (<see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderInternalTlsExtensions.TrustInternalAuthorities"/>).
/// </summary>
internal sealed class InternalTrustedClients
{
    public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
}
