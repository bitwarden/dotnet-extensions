using Bitwarden.Server.Sdk.Client;
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Trust in the deployment's internal certificate authorities, for a client that calls the deployment's own services.
/// </summary>
public static class HttpClientBuilderInternalTlsExtensions
{
    /// <summary>
    /// Has the client trust the internal root certificate authorities named in <c>Tls:CertificateAuthorities</c>, and
    /// only those, when any are named.
    /// </summary>
    /// <param name="builder">The client's builder.</param>
    /// <returns>The same builder, to configure further.</returns>
    /// <remarks>
    /// <para>
    /// The handler the client sends with, once all its configuration has run, gets <see cref="InternalTls.ChainPolicy"/>
    /// as its certificate chain policy: no system roots, no validation callback, no revocation check (certificate
    /// lifetime is the control) and no downloads. <see cref="System.Net.Security.SslStream"/> still checks the host name
    /// and the server-authentication usage. With no authority named, nothing changes and the system's roots apply. The
    /// client's primary handler is kept whenever and however it was set, and one that already has a chain policy keeps
    /// it. With an authority named, a primary handler that isn't a <see cref="SocketsHttpHandler"/> is refused when the
    /// client is made, as it couldn't take the policy.
    /// </para>
    /// <para>
    /// The files are read once, and one that can't be read stops the host as it starts. Use this only on a client that
    /// calls the deployment's own services, never on one protected with <c>AddSsrfProtection</c>, which calls
    /// addresses a caller supplies.
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder TrustInternalAuthorities(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<InternalAuthorities>, InternalAuthorities.FromConfiguration>());
        builder.Services.AddOptions<InternalAuthorities>().ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, InternalTrustFilter>());
        builder.Services.Configure<InternalTrustedClients>(clients => clients.Names.Add(builder.Name));

        return builder;
    }
}
