using Bitwarden.Server.Sdk.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Applies <see cref="ServiceTls"/> to the bearer handler's fetches of Identity's discovery document and keys: with
/// internal certificate authorities named, it trusts those alone (<see cref="InternalTls.ChainPolicy"/>), as Identity
/// is one of the deployment's own services.
/// </summary>
/// <remarks>
/// <para>
/// No other client is given the authorities here. The service clients, the token client and a product client's
/// conventional client trust them through Bitwarden.Server.Sdk.Client (<c>TrustInternalAuthorities</c>), and any other
/// client that calls the deployment's own services opts in the same way. A client that calls addresses a caller
/// supplies, one protected with <c>AddSsrfProtection</c>, must never trust them. A file that can't be read stops the
/// service at startup rather than failing every call.
/// </para>
/// <para>
/// With <see cref="ServiceTls.RequireHttps"/>, the bearer handler's authority must be <c>https://</c>, and
/// so must every client's service and identity address (the Client package checks those), or the service doesn't
/// start.
/// </para>
/// </remarks>
internal static class InternalTlsTrust
{
    /// <summary>
    /// Registers the trust and the check the configuration asks for.
    /// </summary>
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var tls = configuration.GetSection(InternalTls.Section).Get<ServiceTls>() ?? new ServiceTls();
        Add(services, tls);
    }

    /// <summary>
    /// Registers the trust and the check <paramref name="tls"/> asks for.
    /// </summary>
    internal static void Add(IServiceCollection services, ServiceTls tls)
    {
        var authorities = tls.LoadAuthorities();

        if (authorities.Count > 0)
        {
            // A configure step, not a post-configure: the framework's own post-configure builds the bearer handler's
            // ConfigurationManager from BackchannelHttpHandler, and runs before any registered after it. A handler the
            // service sets itself is kept.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.BackchannelHttpHandler ??= new SocketsHttpHandler
                {
                    SslOptions = { CertificateChainPolicy = InternalTls.ChainPolicy(authorities) },
                });
        }

        if (tls.RequireHttps)
        {
            services.AddSingleton<IValidateOptions<JwtBearerOptions>>(new HttpsAuthority());
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();
        }
    }
}
