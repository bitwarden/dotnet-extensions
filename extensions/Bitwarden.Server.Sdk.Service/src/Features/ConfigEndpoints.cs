using System.Reflection;
using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Features;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// The <c>/api/v1/config</c> resource, which every product service serves.
/// </summary>
/// <remarks>
/// <para>
/// Mapped by the platform for a product service only. A domain service is never reached by a
/// browser, so it has no client to configure.
/// </para>
/// <para>
/// Anonymous, because a client reads it before anyone has signed in. A session token is still
/// honoured when one is sent: authentication runs for every request, so the acting user is
/// known, and the flags are evaluated for them. An invalid token is not refused here; it is
/// simply not a session, and the answer is the anonymous one.
/// </para>
/// </remarks>
[ApiTag("Config", Description =
    "What a client needs to know about the platform: the version that answered, where the web "
    + "vault and the fill-assist rules are, how the deployment is set up, and the feature flags "
    + "evaluated for the caller. Readable before sign-in; a session token, when sent, targets "
    + "the flags at the person.")]
internal static class ConfigEndpoints
{
    /// <summary>
    /// Maps the configuration, for a product service.
    /// </summary>
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup("api/v1/config").WithStandards().MapGet("", GetConfig);

    internal const string EnvironmentSection = "Service:Environment";
    internal const string SettingsSection = "Service:Settings";

    /// <summary>
    /// Get the configuration
    /// </summary>
    /// <param name="features">Evaluates the flags for the current request.</param>
    /// <param name="environment">The service's version.</param>
    /// <param name="host">Names the service's own assembly, for the version when the environment has none.</param>
    /// <param name="configuration">Where the environment and settings are read from, on each request.</param>
    /// <returns>The service's version, environment, settings and feature flags</returns>
    /// <remarks>
    /// No credential is required. With a session token the feature flags are those targeted at
    /// the person and their device; without one they are the anonymous ones.
    /// </remarks>
    [Authorization(Anonymous = true)]
    [StandardReadOne("")]
    public static ResourceDocument<Config> GetConfig(
        IFeatureService features,
        IBitwardenEnvironment environment,
        IHostEnvironment host,
        IConfiguration configuration)
    {
        var environmentSection = configuration.GetSection(EnvironmentSection);
        var settingsSection = configuration.GetSection(SettingsSection);

        return StandardResponses.ReadOneResponse(new Config
        {
            Version = ServiceVersion(environment, host),
            Environment = new ConfigEnvironment
            {
                CloudRegion = Value(environmentSection, nameof(ConfigEnvironment.CloudRegion)),
                Vault = Value(environmentSection, nameof(ConfigEnvironment.Vault)),
                FillAssistRules = Value(environmentSection, nameof(ConfigEnvironment.FillAssistRules)),
            },
            Settings = new ConfigSettings
            {
                DisableUserRegistration = Flag(settingsSection, nameof(ConfigSettings.DisableUserRegistration)),
                SuppressOnboardingInterstitials = Flag(settingsSection, nameof(ConfigSettings.SuppressOnboardingInterstitials)),
                SelfHosted = environment.SelfHosted,
            },
            FeatureFlags = features.GetAll().ToDictionary(flag => flag.Key, object (flag) => flag.Value),
        });
    }

    private static string? Value(IConfigurationSection section, string key) =>
        section[key] is { Length: > 0 } value ? value : null;

    private static bool Flag(IConfigurationSection section, string key) =>
        bool.TryParse(section[key], out var value) && value;

    /// <remarks>
    /// The platform's version first, which is the service assembly's informational version
    /// parsed. Where that does not parse, the informational version itself, without its build
    /// metadata, so an unusual version string is still reported rather than an empty one.
    /// </remarks>
    private static string? ServiceVersion(IBitwardenEnvironment environment, IHostEnvironment host)
    {
        if (!string.IsNullOrEmpty(environment.Version))
        {
            return environment.Version;
        }

        try
        {
            var informational = Assembly.Load(new AssemblyName(host.ApplicationName))
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            return informational?.Split('+', 2)[0] is { Length: > 0 } version ? version : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
