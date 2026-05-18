using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
/// Compile-time licensing configuration supplied by the host application at startup. Unlike
/// <see cref="LicensingOptions"/>, these values are not bound from <c>IConfiguration</c> and are
/// expected to be constant for the lifetime of the process.
/// </summary>
public sealed record StaticLicensingOptions
{
    /// <summary>
    /// The value to use for the <c>iss</c> claim when generating licenses and the value expected
    /// when validating them.
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// The value to use for the <c>aud</c> claim when generating licenses and the value expected
    /// when validating them.
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    /// A map from environment name (matching <see cref="IHostEnvironment.EnvironmentName"/>) to
    /// the SHA-1 thumbprint of the certificate expected to sign licenses in that environment.
    /// When the current environment is not present in this map, <see cref="FallbackThumbprint"/>
    /// is used instead.
    /// </summary>
    public required IReadOnlyDictionary<string, string> EnvironmentThumbprints { get; init; }

    /// <summary>
    /// The SHA-1 thumbprint used when no entry in <see cref="EnvironmentThumbprints"/> matches the
    /// current environment.
    /// </summary>
    public required string FallbackThumbprint { get; init; }

    internal string GetThumbprint(string environmentName)
    {
        if (EnvironmentThumbprints.TryGetValue(environmentName, out var thumbprint))
        {
            return thumbprint;
        }

        return FallbackThumbprint;
    }
}
