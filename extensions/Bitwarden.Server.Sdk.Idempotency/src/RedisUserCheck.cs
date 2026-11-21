using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Refuses, as the host starts, an idempotency Redis connection that names no Redis user of the service's own, outside
/// <c>Development</c>, as a host with a service cache refuses one (Bitwarden.Server.Sdk.Resources.Caching's
/// <c>ServiceCacheAcl.RequireUser</c>).
/// </summary>
/// <remarks>
/// Records are sealed, so another service can't read or forge one. But connected as Redis's default user, which no
/// prefix confines, any holder of the connection can claim a key before the service does or overwrite a record, and so
/// refuse the service's writes: the service's own user, which alone may write its prefix, prevents that.
/// </remarks>
internal sealed class RedisUserCheck
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
            + "the connection is Redis's default user, which access control can't confine to the service's prefix.");
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
