using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Client;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers a typed client for a product service, called with somebody's own token (<see cref="ProductClient"/>).
/// </summary>
/// <remarks>
/// Called by the client package a product service publishes, not by a consumer. Unlike <c>AddBitwardenClient</c> it
/// installs no credential of the caller's, no context and no translation of refusals into exceptions: the token
/// is the one each call is given, and a refusal is an answer (<see cref="ProductAnswer"/>).
/// </remarks>
public static class ProductClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as the client for a product service, over the named client
    /// <paramref name="name"/>.
    /// </summary>
    /// <typeparam name="TClient">The interface a consumer injects.</typeparam>
    /// <typeparam name="TImplementation">The client behind it.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceName">The service being called, as it appears in configuration.</param>
    /// <param name="name">
    /// The named client to call it over, when the consumer already configures one for the service (its address,
    /// timeout and handlers); null for <paramref name="serviceName"/>, whose address is then the conventional one, as
    /// <c>AddBitwardenClient</c> finds it.
    /// </param>
    /// <returns>The named client's builder, to configure further.</returns>
    public static IHttpClientBuilder AddProductClient<TClient,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services,
        string serviceName,
        string? name = null)
        where TClient : class
        where TImplementation : ProductClient, TClient
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var builder = services.AddHttpClient<TClient, TImplementation>(name ?? serviceName);
        if (name is null)
        {
            // As AddBitwardenClient does: where the deployment requires TLS, a plain address stops the host at startup
            // rather than carry a person's token in the clear. A named client of the consumer's is the consumer's to check.
            services.AddOptions<ProductClientAddress>(serviceName)
                .Configure<IConfiguration>((address, configuration) =>
                {
                    var options = new BitwardenClientOptions();
                    Conventions.Apply(options, configuration, serviceName);
                    address.BaseAddress = options.BaseAddress;
                    address.RequireHttps = options.RequireHttps;
                })
                .Validate(
                    address => address.Allowed,
                    $"Tls:RequireHttps is set, and the {serviceName} product client's BaseAddress is not https.")
                .ValidateOnStart();

            builder.ConfigureHttpClient((provider, http) =>
            {
                var options = new BitwardenClientOptions();
                Conventions.Apply(options, provider.GetRequiredService<IConfiguration>(), serviceName);
                if (!string.IsNullOrWhiteSpace(options.BaseAddress))
                {
                    http.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + "/");
                }
            });
        }

        return builder.AddHttpMessageHandler(() => new ProductCallHandler());
    }
}
