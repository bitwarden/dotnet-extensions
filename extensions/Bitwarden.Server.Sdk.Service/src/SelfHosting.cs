using Bitwarden.Server.Sdk.Environment.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Whether this process is part of a self-hosted install, read from <c>globalSettings:selfHosted</c>.
/// A service learns it from <c>IBitwardenEnvironment.SelfHosted</c>.
/// </summary>
/// <remarks>
/// Two steps: <c>appsettings.SelfHosted.json</c> is layered in, and <see cref="SelfHostDetails"/> is bound from the
/// setting.
/// </remarks>
internal static class SelfHosting
{
    /// <summary>
    /// The setting that marks a self-hosted install.
    /// </summary>
    public const string Key = "globalSettings:selfHosted";

    /// <summary>
    /// The self-hosted install's flavor.
    /// </summary>
    public const string FlavorKey = "globalSettings:selfHostFlavor";

    /// <summary>
    /// When self-hosted, adds <c>appsettings.SelfHosted.json</c> (optional) right after
    /// <c>appsettings.{Environment}.json</c>, so it overrides the service's own settings and is overridden by user secrets,
    /// environment variables and the command line.
    /// </summary>
    public static void LayerSettings(IConfigurationManager configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue(Key, false))
        {
            return;
        }

        var sources = ((IConfigurationBuilder)configuration).Sources;
        var environmentSettings = $"appsettings.{environment.EnvironmentName}.json";
        var at = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is FileConfigurationSource { Path: var path } && path == environmentSettings)
            {
                at = i + 1;
                break;
            }
        }

        sources.Insert(at, new JsonConfigurationSource
        {
            Path = "appsettings.SelfHosted.json",
            Optional = true,
            ReloadOnChange = true,
        });
    }

    /// <summary>
    /// Binds <see cref="SelfHostDetails"/> from the setting.
    /// </summary>
    public static void AddDetails(IServiceCollection services) =>
        services.AddOptions<SelfHostDetails>()
            .Configure<IConfiguration>((details, configuration) =>
            {
                if (configuration.GetValue(Key, false))
                {
                    details.MakeSelfHost(configuration[FlavorKey] ?? "unknown");
                }
                else
                {
                    details.MakeCloud();
                }
            });
}
