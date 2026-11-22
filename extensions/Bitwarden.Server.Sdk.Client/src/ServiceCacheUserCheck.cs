using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Refuses, as the host starts, a service cache connection that names no Redis user of the service's own, outside
/// <c>Development</c>: as the owner's host does (Bitwarden.Server.Sdk.Resources.Caching's <c>ServiceCacheAcl.RequireUser</c>).
/// </summary>
/// <remarks>
/// A reader connected as Redis's default user is confined to no prefix, so it can write the entries it reads, and every
/// service reading them would trust what it wrote.
/// </remarks>
internal sealed class ServiceCacheUserCheck
{
    // Compared as IHostEnvironment.IsDevelopment compares it.
    private const string DevelopmentEnvironment = "Development";

    // The user every connection without one of its own is.
    private const string DefaultUser = "default";

    /// <summary>
    /// Whether the connection names a user of the service's own; false where none is configured but none is required.
    /// </summary>
    public bool OwnUser { get; set; }

    /// <summary>
    /// Checks the connection read from <paramref name="setting"/>; never echoes it.
    /// </summary>
    /// <exception cref="ArgumentException">No setting name, or a null environment name.</exception>
    /// <exception cref="InvalidOperationException">A connection is set, names no user of its own, and the host is not in <c>Development</c>.</exception>
    public static bool Require(string? configuration, string setting, string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        ArgumentNullException.ThrowIfNull(environmentName);

        if (string.IsNullOrWhiteSpace(configuration) || NamesAUser(configuration))
        {
            return true;
        }

        if (string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new InvalidOperationException(
            $"{setting} must name a Redis user of this service's own (user=...) outside {DevelopmentEnvironment}: without one "
            + "the connection is Redis's default user, which access control can't confine to the service's prefix. "
            + "Give each service its own user (ServiceCacheAcl).");
    }

    private static bool NamesAUser(string configuration)
    {
        try
        {
            var user = ConfigurationOptions.Parse(configuration).User;
            return !string.IsNullOrWhiteSpace(user) && !string.Equals(user, DefaultUser, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
