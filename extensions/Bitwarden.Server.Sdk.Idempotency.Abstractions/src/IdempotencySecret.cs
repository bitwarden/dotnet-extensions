using Bitwarden.Server.Sdk.CryptoPrimitives;
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// The secret a service derives its operations' write keys and identifiers with
/// (<see cref="IdempotentOperation.Begin(string, IdempotencySecret)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The secret is configured under <see cref="SecretKey"/>, one per service. Only the service that derived a key ever
/// derives it again, when it runs the same operation again, so the secret need only be the same on every instance of
/// the service, and stay the same while work is in flight: an operation run again under another secret sends new keys,
/// and its callees do its writes again.
/// </para>
/// <para>
/// Outside <c>Development</c> a secret is required (<see cref="ReadSecret(IConfiguration, string, string)"/>), so a key or
/// identifier is never one anyone can compute from the operation's key. In <c>Development</c> it falls back to
/// <see cref="CacheSecret.Development"/>.
/// </para>
/// </remarks>
public sealed class IdempotencySecret
{
    /// <summary>
    /// The configuration key of the secret: base64 of <see cref="CacheSecret.Length"/> bytes.
    /// </summary>
    public const string SecretKey = "Idempotency:Secret";

    // Internal, so every secret an operation begins with went through ReadSecret's checks.
    internal IdempotencySecret(CacheSecret value, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        Value = value;
        ServiceName = serviceName;
    }

    // Internal, so nothing outside the package can read the secret back out and log it.
    internal CacheSecret Value { get; }

    // Derived from with the secret, so two services that end up sharing a secret still derive keys of their own.
    internal string ServiceName { get; }

    /// <summary>
    /// A fixed marker, never the secret, so writing one to a log, an exception message or a debugger gives nothing away.
    /// </summary>
    public override string ToString() => "[idempotency secret]";

    /// <summary>
    /// Reads the configured secret, and refuses to go without one outside the <c>Development</c> environment.
    /// </summary>
    /// <param name="configuration">The configuration holding <see cref="SecretKey"/>.</param>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <param name="serviceName">
    /// The service's name, such as the host's <c>IHostEnvironment.ApplicationName</c>. Every key and identifier is
    /// derived with it, so they are the service's own even if another service has the same secret.
    /// </param>
    /// <returns>
    /// The secret, or <see cref="CacheSecret.Development"/> when none is configured in <c>Development</c>: the only way,
    /// besides <see cref="Microsoft.Extensions.DependencyInjection.IdempotencySecretServiceCollectionExtensions.AddIdempotencySecret"/>,
    /// to get one.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The secret is unset or is <see cref="CacheSecret.Development"/> outside <c>Development</c>, or is not base64 of
    /// <see cref="CacheSecret.Length"/> bytes. The message names the key, never the value.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="serviceName"/> is null, empty or white space.</exception>
    public static IdempotencySecret ReadSecret(IConfiguration configuration, string environmentName, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new IdempotencySecret(ReadValue(configuration[SecretKey], environmentName), serviceName);
    }

    /// <summary>
    /// Reads <paramref name="value"/> as <see cref="ReadSecret(IConfiguration, string, string)"/> reads the configured one.
    /// </summary>
    internal static CacheSecret ReadValue(string? value, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(environmentName);

        return CacheSecretSetting.Read(value, SecretKey, CacheSecretSetting.RequiredIn(environmentName));
    }
}
