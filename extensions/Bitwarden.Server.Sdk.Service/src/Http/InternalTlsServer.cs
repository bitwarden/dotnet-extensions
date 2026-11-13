using System.Globalization;
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Applies <see cref="InternalTls"/> to what a service serves: its server certificate from settings, the refusal to
/// listen on plain HTTP with <see cref="InternalTls.RequireHttps"/>, and the probes alone on
/// <see cref="InternalTls.HealthPort"/>.
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="InternalTls.ServerCertificate"/> and <see cref="InternalTls.ServerKey"/>, every HTTPS endpoint
/// without a certificate of its own serves that one (Kestrel's HTTPS defaults). A certificate that can't be read stops
/// the service at startup.
/// </para>
/// <para>
/// With <see cref="InternalTls.RequireHttps"/>, the service doesn't start when an address it is configured to listen
/// on (<c>urls</c>, that is <c>ASPNETCORE_URLS</c>; <c>http_ports</c>; <c>Kestrel:Endpoints:*:Url</c>) is plain HTTP,
/// checked before it listens; and it stops when an address it is listening on is, checked once it listens, which
/// catches an address set in code or Kestrel's own default. The health port is the exception for both.
/// </para>
/// </remarks>
internal static partial class InternalTlsServer
{
    /// <summary>
    /// Registers what the configuration asks for.
    /// </summary>
    /// <param name="services">The service's registrations.</param>
    /// <param name="configuration">Its configuration.</param>
    /// <param name="describing">
    /// True when the process only describes itself (<c>--emit-openapi</c>): it listens on a loopback
    /// address of its own choosing, plain HTTP, so the plain-listener refusal is left out.
    /// </param>
    public static void Add(IServiceCollection services, IConfiguration configuration, bool describing = false)
    {
        var tls = configuration.GetSection(InternalTls.Section).Get<InternalTls>() ?? new InternalTls();
        Add(services, tls, describing);
    }

    /// <summary>
    /// Registers what <paramref name="tls"/> asks for.
    /// </summary>
    internal static void Add(IServiceCollection services, InternalTls tls, bool describing = false)
    {
        if (tls.HealthPort is { } port && port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{InternalTls.Section}:HealthPort is {port}, which is no port.");
        }

        var certificate = tls.LoadServerCertificate();
        if (certificate is not null)
        {
            // Held by Kestrel's options for the life of the process, as a certificate Kestrel loads from a file is.
            services.Configure<KestrelServerOptions>(kestrel =>
                kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate ??= certificate));
        }

        if (tls.RequireHttps && !describing)
        {
            services.AddHostedService(provider => new PlainListenerRefusal(
                provider.GetRequiredService<IConfiguration>(), provider.GetService<IServer>(), tls.HealthPort));
        }

        if (tls.HealthPort is { } healthPort)
        {
            services.AddTransient<IStartupFilter>(_ => new ProbesOnlyOnHealthPort(healthPort));
        }
    }

    /// <summary>
    /// The plain-HTTP addresses among <paramref name="addresses"/>, but for those on <paramref name="healthPort"/>.
    /// </summary>
    internal static IEnumerable<string> PlainAddresses(IEnumerable<string> addresses, int? healthPort) =>
        addresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Where(address => Address().Match(address) is { Success: true } parts
                && parts.Groups["scheme"].Value.Equals("http", StringComparison.OrdinalIgnoreCase)
                && (parts.Groups["port"].Success
                    ? int.Parse(parts.Groups["port"].Value, CultureInfo.InvariantCulture)
                    : 80) != healthPort);

    /// <summary>
    /// The addresses a service's configuration has it listen on, with Kestrel's precedence: its endpoints
    /// (<c>Kestrel:Endpoints:*:Url</c>) when there are any; otherwise <c>urls</c> (semicolon separated); otherwise
    /// <c>http_ports</c> (plain HTTP on every interface) and <c>https_ports</c>. The .NET images set
    /// <c>ASPNETCORE_HTTP_PORTS</c>, which Kestrel ignores once <c>ASPNETCORE_URLS</c> is set, and so does this.
    /// </summary>
    internal static IEnumerable<string> ConfiguredAddresses(IConfiguration configuration)
    {
        var endpoints = configuration.GetSection("Kestrel:Endpoints").GetChildren()
            .Select(endpoint => endpoint["Url"])
            .OfType<string>()
            .ToList();
        if (endpoints.Count > 0)
        {
            return endpoints;
        }

        if (!string.IsNullOrWhiteSpace(configuration["urls"]))
        {
            return configuration["urls"]!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        static IEnumerable<string> Ports(string? ports, string scheme) =>
            (ports ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(port => $"{scheme}://*:{port}");

        return Ports(configuration["http_ports"], "http").Concat(Ports(configuration["https_ports"], "https")).ToList();
    }

    // A scheme, a host (bracketed for IPv6, or a name, an address, + or *), and an optional port.
    [GeneratedRegex(@"^(?<scheme>[A-Za-z][A-Za-z0-9+.-]*)://(?<host>\[[^\]]*\]|[^:/\[\]]*)(:(?<port>\d+))?(/.*)?$")]
    private static partial Regex Address();
}
