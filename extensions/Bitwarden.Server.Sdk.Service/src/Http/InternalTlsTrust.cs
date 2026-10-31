using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Applies <see cref="InternalTls"/> to everything a service calls over HTTP: every client the HTTP client factory
/// makes (other services' clients, the token requests, any others the host registers), and the bearer handler's
/// fetches of Identity's discovery document and keys.
/// </summary>
/// <remarks>
/// <para>
/// With certificate authorities named, a server's certificate is trusted when the system trusts it or it chains to
/// one of them (<see cref="InternalTls.Trusts"/>); nothing changes otherwise. A file
/// that can't be read stops the service at startup rather than failing every call.
/// </para>
/// <para>
/// With <see cref="InternalTls.RequireHttps"/>, the bearer handler's authority must be <c>https://</c>, and
/// so must every client's service and identity address (the Client package checks those), or the service doesn't
/// start. A client that sets its own primary handler after the platform keeps its own trust.
/// </para>
/// </remarks>
internal static class InternalTlsTrust
{
    /// <summary>
    /// Registers the trust and the check the configuration asks for.
    /// </summary>
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        var tls = configuration.GetSection(InternalTls.Section).Get<InternalTls>() ?? new InternalTls();
        Add(services, tls);
    }

    /// <summary>
    /// Registers the trust and the check <paramref name="tls"/> asks for.
    /// </summary>
    internal static void Add(IServiceCollection services, InternalTls tls)
    {
        var authorities = tls.LoadAuthorities();

        if (authorities.Count > 0)
        {
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler((handler, _) =>
            {
                switch (handler)
                {
                    case SocketsHttpHandler sockets:
                        sockets.SslOptions.RemoteCertificateValidationCallback =
                            (_, certificate, chain, errors) => InternalTls.Trusts(authorities, certificate, chain, errors);
                        break;
                    case HttpClientHandler client:
                        client.ServerCertificateCustomValidationCallback =
                            (_, certificate, chain, errors) => InternalTls.Trusts(authorities, certificate, chain, errors);
                        break;
                }
            }));

            // A configure step, not a post-configure: the framework's own post-configure builds the bearer handler's
            // ConfigurationManager from BackchannelHttpHandler, and runs before any registered after it.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.BackchannelHttpHandler ??= new SocketsHttpHandler
                {
                    SslOptions =
                    {
                        RemoteCertificateValidationCallback =
                            (_, certificate, chain, errors) => InternalTls.Trusts(authorities, certificate, chain, errors),
                    },
                });
        }

        if (tls.RequireHttps)
        {
            services.AddSingleton<IValidateOptions<JwtBearerOptions>>(new HttpsAuthority());
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();
        }
    }

    /// <summary>
    /// Refuses a plain-HTTP authority for the bearer handler, which is where Identity's keys come from.
    /// </summary>
    internal sealed class HttpsAuthority : IValidateOptions<JwtBearerOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtBearerOptions options) =>
            name != JwtBearerDefaults.AuthenticationScheme
            || options.Authority is null
            || options.Authority.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"{InternalTls.Section}:RequireHttps is set, and the bearer handler's authority, '{options.Authority}', is not https.");
    }
}
