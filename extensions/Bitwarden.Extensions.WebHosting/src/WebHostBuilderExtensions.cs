using Bitwarden.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Bitwarden.Extensions.WebHosting;

/// <summary>
/// Extensions for <see cref="IWebHostBuilder"/>.
/// </summary>
public static class WebHostBuilderExtensions
{
    /// <summary>
    /// Configures the web host with Bitwarden defaults.
    /// </summary>
    /// <param name="webHostBuilder">Web host builder.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns></returns>
    public static IWebHostBuilder UseBitwardenWebDefaults(this IWebHostBuilder webHostBuilder, Action<BitwardenWebHostOptions>? configure = null)
    {
        var bitwardenWebHostOptions = new BitwardenWebHostOptions();
        configure?.Invoke(bitwardenWebHostOptions);
        return webHostBuilder.UseBitwardenWebDefaults(bitwardenWebHostOptions);
    }

    /// <summary>
    /// Configures the web host with Bitwarden defaults.
    /// </summary>
    /// <param name="webHostBuilder">Web host builder.</param>
    /// <param name="bitwardenWebHostOptions">Web host options.</param>
    /// <returns></returns>
    public static IWebHostBuilder UseBitwardenWebDefaults(this IWebHostBuilder webHostBuilder, BitwardenWebHostOptions bitwardenWebHostOptions)
    {
        // TODO: Add services and default starting middleware
        webHostBuilder.Configure((context, builder) =>
        {
            if (bitwardenWebHostOptions.IncludeRequestLogging)
            {
                builder.UseSerilogRequestLogging();
            }

            // Exception handling middleware?
        });

        webHostBuilder.ConfigureServices(static (context, services) =>
        {
            // Default services that are web specific?
        });

        return webHostBuilder;
    }

    /// <summary>
    /// Configures the web host with Bitwarden defaults via a startup class.
    /// </summary>
    /// <param name="hostBuilder">Host builder.</param>
    /// <param name="configure">Configuration action.</param>
    /// <typeparam name="TStartup">Startup class.</typeparam>
    /// <returns>Configured host builder.</returns>
    public static IHostBuilder UseBitwardenWebDefaults<TStartup>(this IHostBuilder hostBuilder, Action<BitwardenWebHostOptions>? configure = null)
        where TStartup : class
    {
        var bitwardenWebHostOptions = new BitwardenWebHostOptions();
        configure?.Invoke(bitwardenWebHostOptions);

        hostBuilder.UseBitwardenDefaults(bitwardenWebHostOptions);
        return hostBuilder.ConfigureWebHostDefaults(webHost =>
        {
            // Make sure to call our thing first, so that if we add middleware it is first
            webHost.UseBitwardenWebDefaults(bitwardenWebHostOptions);
            webHost.UseStartup<TStartup>();
        });
    }
}
