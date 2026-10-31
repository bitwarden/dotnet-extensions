namespace Bitwarden.Server.Sdk.Service.Features;

/// <summary>
/// How a deployment is set up.
/// </summary>
internal sealed record ConfigSettings
{
    /// <summary>
    /// Whether new accounts cannot be created.
    /// </summary>
    public bool DisableUserRegistration { get; init; }

    /// <summary>
    /// Whether clients skip the onboarding screens shown after sign-in.
    /// </summary>
    public bool SuppressOnboardingInterstitials { get; init; }

    /// <summary>
    /// Whether the deployment is a self-hosted install (<c>globalSettings:selfHosted</c>) rather than Bitwarden's cloud.
    /// </summary>
    public bool SelfHosted { get; init; }
}
