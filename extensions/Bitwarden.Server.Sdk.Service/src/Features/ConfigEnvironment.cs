namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// Where a deployment is and what it links to.
/// </summary>
internal sealed record ConfigEnvironment
{
    /// <summary>
    /// The cloud region the deployment serves, such as <c>US</c> or <c>EU</c>. Absent when self-hosted.
    /// </summary>
    public string? CloudRegion { get; init; }

    /// <summary>
    /// The web vault's address.
    /// </summary>
    public string? Vault { get; init; }

    /// <summary>
    /// Where a client fetches the fill-assist rules from.
    /// </summary>
    public string? FillAssistRules { get; init; }
}
