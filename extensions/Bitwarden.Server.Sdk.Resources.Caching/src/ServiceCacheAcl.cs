using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// The Redis user each service connects to the service cache as: it writes only its own prefix,
/// and reads only the prefixes of the services it calls.
/// </summary>
/// <remarks>
/// <para>
/// Redis 7 key permissions separate reading from writing: <c>%RW~shelf-service:*</c> reads
/// and writes the service's own entries, <c>%R~lending-service:*</c> only reads the lending service's. A write
/// command on a key the user may only read is refused by Redis, not by the client library, and a
/// script runs under the caller's key permissions, so the owner's version-checked scripts cannot
/// reach another prefix either.
/// </para>
/// <para>
/// Commands are the read, write, scripting and connection families, less everything Redis marks
/// dangerous (<c>FLUSHALL</c>, <c>FLUSHDB</c>, <c>KEYS</c>). A command that takes no key is not
/// covered by key permissions, so the keyless commands those families would otherwise grant are
/// taken away too: <c>SCAN</c> and <c>RANDOMKEY</c> return key names whatever the user's key
/// permissions (an account's or an organization's identifier is in every key), <c>DBSIZE</c>
/// is not needed (<c>INFO</c>'s keyspace section still gives the same count), and <c>FUNCTION</c> changes the functions every user shares, as
/// <c>SCRIPT FLUSH</c> and <c>SCRIPT KILL</c> would the scripts. <c>CLIENT TRACKING</c> in broadcast
/// mode pushes the name of every key any user writes, whatever the tracking user's key permissions,
/// so tracking and its companions (<c>CLIENT CACHING</c>, <c>CLIENT TRACKINGINFO</c>,
/// <c>CLIENT GETREDIR</c>) go, and <c>SCRIPT DEBUG</c>, whose synchronous mode blocks the server for
/// every user. The keyless commands left are the rest of the connection family's and <c>INFO</c>,
/// which the client library and the owner's eviction check read; none names a key. No channel but the one the client library watches for a primary
/// changing: the service cache has no backplane, because clients keep no copy of their own.
/// </para>
/// <para>
/// The same rule for every deployment that has a service cache: provisioned as one Azure Cache for
/// Redis access policy assignment per service in cloud, as a user directive per service in a
/// self-hosted Redis's ACL file, and as <c>--user</c> arguments in the package's own ACL tests.
/// A tool writing an ACL file without .NET can read <see cref="Commands"/>, <see cref="Channels"/>,
/// <see cref="OwnKeys"/> and <see cref="ReadKeys"/> from this source, so the rule has one home.
/// </para>
/// </remarks>
public static class ServiceCacheAcl
{
    /// <summary>
    /// The commands every service's user may run.
    /// </summary>
    public const string Commands =
        "-@all +@read +@write +@scripting +@connection -@dangerous +info -script|flush -script|kill "
        + "-scan -randomkey -dbsize -function -fcall -fcall_ro "
        + "-client|tracking -client|caching -client|trackinginfo -client|getredir -script|debug";

    /// <summary>
    /// The channel StackExchange.Redis subscribes to for primary changes.
    /// </summary>
    public const string Channels = "&__Booksleeve_MasterChanged";

    /// <summary>
    /// The key permission over a service's own prefix, with <c>{service}</c> for its name.
    /// </summary>
    public const string OwnKeys = "%RW~{service}:*";

    /// <summary>
    /// The key permission over the prefix of a service it reads, with <c>{service}</c> for that service's name.
    /// </summary>
    public const string ReadKeys = "%R~{service}:*";

    /// <summary>
    /// The Redis user every connection without a user of its own is.
    /// </summary>
    public const string DefaultUser = "default";

    /// <summary>
    /// The most passwords a user holds at once: the current one and, during a rotation, the next.
    /// </summary>
    public const int MaximumPasswords = 2;

    // Compared as IHostEnvironment.IsDevelopment compares it.
    private const string DevelopmentEnvironment = "Development";

    private const string ServicePlaceholder = "{service}";

    /// <summary>
    /// The key permissions of one service.
    /// </summary>
    /// <param name="service">The service, by its name.</param>
    /// <param name="reads">The services whose caches it reads: those whose clients it uses.</param>
    /// <exception cref="ArgumentException">A name holds anything but letters, digits, <c>-</c>, <c>_</c> and <c>.</c>.</exception>
    public static string Keys(string service, IEnumerable<string> reads)
    {
        ThrowIfNotAUserName(service);
        ArgumentNullException.ThrowIfNull(reads);

        var others = reads.Where(read => read != service).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        foreach (var read in others)
        {
            ThrowIfNotAUserName(read, nameof(reads));
        }

        return string.Join(' ', new[] { Pattern(OwnKeys, service) }.Concat(others.Select(read => Pattern(ReadKeys, read))));
    }

    /// <summary>
    /// One service's user, as an ACL rule: <c>ACL SETUSER</c>'s arguments, or a <c>user</c> directive.
    /// </summary>
    /// <param name="service">The service, by its name; also the user's name.</param>
    /// <param name="password">The user's password, from the service's secrets. Never logged.</param>
    /// <param name="reads">The services whose caches it reads.</param>
    public static string User(string service, string password, IEnumerable<string> reads) =>
        User(service, [password], reads);

    /// <summary>
    /// One service's user holding more than one password, for a rotation: provisioned with the current password and the
    /// next, every instance of the service moved to the next, then provisioned with the next alone.
    /// </summary>
    /// <param name="service">The service, by its name; also the user's name.</param>
    /// <param name="passwords">Every password the user accepts, at most <see cref="MaximumPasswords"/>. Never logged.</param>
    /// <param name="reads">The services whose caches it reads.</param>
    /// <remarks>
    /// The rule resets the user's passwords before setting these, so applied with <c>ACL SETUSER</c> to a user that
    /// exists it leaves exactly these: the step that drops the old password is the same rule with the next alone.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// No password, more than <see cref="MaximumPasswords"/>, one repeated, or one blank or holding whitespace, which
    /// would end the password and start another rule.
    /// </exception>
    public static string User(string service, IReadOnlyCollection<string> passwords, IEnumerable<string> reads)
    {
        ArgumentNullException.ThrowIfNull(passwords);

        if (passwords.Count is 0 or > MaximumPasswords)
        {
            throw new ArgumentException(
                $"A service cache user holds one password, or two while it is rotated; {passwords.Count} were given.", nameof(passwords));
        }

        foreach (var password in passwords)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(passwords));
            if (password.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException("A service cache password may not contain whitespace.", nameof(passwords));
            }
        }

        if (passwords.Distinct(StringComparer.Ordinal).Count() != passwords.Count)
        {
            throw new ArgumentException("A service cache user's passwords must differ.", nameof(passwords));
        }

        var keys = Keys(service, reads);

        return $"{service} on resetpass {string.Join(' ', passwords.Select(password => $">{password}"))} "
            + $"resetkeys {keys} resetchannels {Channels} {Commands}";
    }

    /// <summary>
    /// Whether a service cache connection string names a Redis user of its own.
    /// </summary>
    /// <param name="configuration">The connection string. Never logged: it may hold a password.</param>
    /// <remarks>
    /// Without one the connection is Redis's <c>default</c> user, which access control does not confine to the
    /// service's prefix, so nothing stops another holder of the connection string writing this service's entries.
    /// Naming <c>default</c> is the same.
    /// </remarks>
    public static bool NamesAUser(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return false;
        }

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

    /// <summary>
    /// Refuses a service cache connection that names no Redis user of the service's own, outside <c>Development</c>.
    /// </summary>
    /// <param name="configuration">The connection string, or null when there is no cache. Never logged.</param>
    /// <param name="setting">The setting it was read from, which the refusal names.</param>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <returns>Whether the connection names a user of its own; false in <c>Development</c> when it doesn't.</returns>
    /// <remarks>
    /// A host calls this as it starts. Entries carry no signature: a cached membership or entitlement is trusted because
    /// only its owner can write its prefix, and only a user of the owner's own makes that so. Every deployment has a
    /// Redis of its own, provisioned with a user per service; only a developer's own Redis has no users, and that runs
    /// in <c>Development</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">No setting name, or no environment name.</exception>
    /// <exception cref="InvalidOperationException">A connection is set, names no user of its own, and the host is not in <c>Development</c>.</exception>
    public static bool RequireUser(string? configuration, string setting, string environmentName)
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

    /// <summary>
    /// Warns, once, that the service cache is used without a Redis user of the service's own.
    /// </summary>
    /// <param name="configuration">The connection string. Never logged.</param>
    /// <param name="logger">Where the warning goes.</param>
    /// <returns>Whether it warned.</returns>
    /// <remarks>
    /// A warning, not a refusal, only because outside <c>Development</c> the host has already refused to start
    /// (<see cref="RequireUser"/>): a developer's own Redis has no users, and the cache still answers.
    /// </remarks>
    public static bool WarnWithoutUser(string configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (NamesAUser(configuration))
        {
            return false;
        }

        logger.LogWarning(
            "The service cache connection names no Redis user, so access control can't confine this service to its own "
            + "prefix: whoever else holds the connection can write its entries. Give each service its own user (ServiceCacheAcl).");
        return true;
    }

    private static string Pattern(string template, string service) => template.Replace(ServicePlaceholder, service, StringComparison.Ordinal);

    /// <summary>
    /// A name that is also a Redis user name and the start of a key pattern: one holding a space would end the rule and
    /// start another, and one holding <c>*</c>, <c>?</c> or <c>[</c> would match other services' prefixes.
    /// </summary>
    private static void ThrowIfNotAUserName(string name, string? paramName = null)
    {
        // The key's own rule first: no ':', '{' or '}'.
        _ = OwnedCacheEntry.Prefix(name);

        if (!name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new ArgumentException(
                "A service cache user's name may hold only letters, digits, '-', '_' and '.'.", paramName ?? nameof(name));
        }
    }
}
