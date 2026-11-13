using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// What a probe reports about the request that reached it.
/// </summary>
public sealed record Probe : Resource
{
    /// <summary>
    /// The acting user, as the handler saw it.
    /// </summary>
    [ReadOnly]
    public Guid? ActingUser { get; init; }

    /// <summary>
    /// An ordinary string, held to the platform's default length.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// Ciphertext, with a bound of its own.
    /// </summary>
    [Encrypted(MaxLength = 10_000)]
    public string? Secret { get; init; }

    /// <summary>
    /// Costs something to work out, so a caller who doesn't need it can leave it out.
    /// </summary>
    [ReadOnly]
    public string? Costly { get; init; }
}
