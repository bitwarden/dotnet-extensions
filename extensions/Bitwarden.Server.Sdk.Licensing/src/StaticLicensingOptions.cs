namespace Bitwarden.Server.Sdk.Licensing;

/// <summary>
///
/// </summary>
public sealed record StaticLicensingOptions
{
    /// <summary>
    ///
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    ///
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    ///
    /// </summary>
    public required IReadOnlyDictionary<string, string> EnvironmentThumbprints { get; init; }

    /// <summary>
    ///
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
