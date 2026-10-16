namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// The restricted-dependency diagnostic ids, so a consuming repository's own checks reference them
/// rather than spell them. The ids sit in a range shared with the rest of the package's diagnostics,
/// so match against these sets rather than a prefix.
/// </summary>
public static class DiagnosticConstants
{
    /// <summary>
    /// Restricted dependency injected at a new site.
    /// </summary>
    public const string Injection = "BW0005";

    /// <summary>
    /// Restricted member used beyond its budget.
    /// </summary>
    public const string MemberUse = "BW0006";

    /// <summary>
    /// Restricted dependency resolved from the service locator.
    /// </summary>
    public const string Locator = "BW0007";

    /// <summary>
    /// Implementation of a restricted dependency referenced directly.
    /// </summary>
    public const string Concrete = "BW0008";

    /// <summary>
    /// Restricted dependency escapes its consumer.
    /// </summary>
    public const string Escape = "BW0009";

    /// <summary>
    /// Restricted dependency exception is incomplete.
    /// </summary>
    public const string ExceptionInvalid = "BW0010";

    /// <summary>
    /// Restricted dependency exception has expired.
    /// </summary>
    public const string ExceptionExpired = "BW0011";

    /// <summary>
    /// Restricted dependency diagnostic suppressed without an exception.
    /// </summary>
    public const string UnstructuredSuppression = "BW0012";

    /// <summary>
    /// Baseline entry no longer matches the code.
    /// </summary>
    public const string StaleBaseline = "BW0013";

    /// <summary>
    /// Sealed type gained a member.
    /// </summary>
    public const string SealedTypeGrew = "BW0014";

    /// <summary>
    /// Restricted dependency attribute or baseline is invalid.
    /// </summary>
    public const string AttributeInvalid = "BW0015";

    /// <summary>
    /// Restricted dependency diagnostic configured below error.
    /// </summary>
    public const string SeverityLowered = "BW0016";

    /// <summary>
    /// Every id in the family, including the expiry warning and the observation channel.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        Injection, MemberUse, Locator, Concrete, Escape, ExceptionInvalid, ExceptionExpired,
        UnstructuredSuppression, StaleBaseline, SealedTypeGrew, AttributeInvalid, SeverityLowered,
        ObservationConstants.DiagnosticId,
    ];

    /// <summary>
    /// The ids that are errors and cannot be configured otherwise. The expiry warning is a warning
    /// by design, and the observation channel is configured on and off by the tool that reads it.
    /// </summary>
    public static readonly IReadOnlyList<string> MustRemainErrors =
    [
        Injection, MemberUse, Locator, Concrete, Escape, ExceptionInvalid,
        UnstructuredSuppression, StaleBaseline, SealedTypeGrew, AttributeInvalid, SeverityLowered,
    ];
}
