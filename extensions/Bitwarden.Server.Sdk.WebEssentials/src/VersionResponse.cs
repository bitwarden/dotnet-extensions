namespace Bitwarden.Server.Sdk.WebEssentials;

/// <summary>
/// Represents the response body returned by the <c>GET /version</c> endpoint.
/// </summary>
/// <param name="Version">The informational version of the entry assembly, or <see langword="null"/> if unavailable.</param>
public sealed record VersionResponse(string? Version);
