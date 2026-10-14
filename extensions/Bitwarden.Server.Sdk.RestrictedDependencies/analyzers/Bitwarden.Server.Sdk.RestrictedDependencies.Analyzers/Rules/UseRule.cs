namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;

/// <summary>
/// The (AllowExistingUses, AllowNewUses) pair that decides how a use is treated: tracked only,
/// gated by the baseline, or forbidden outright. The pair itself is private because the three
/// predicates below are the only questions callers ever ask of it.
/// </summary>
internal sealed class UseRule(
    bool allowExistingUses,
    bool allowNewUses,
    string? tracking,
    string? owner,
    string? replacement)

{
    /// <summary>
    /// Ticket or cluster id printed in every diagnostic and report row.
    /// </summary>
    public string? Tracking { get; } = tracking;

    /// <summary>
    /// Team or department that owns the dissolution.
    /// </summary>
    public string? Owner { get; } = owner;

    /// <summary>
    /// What a caller should use instead. Printed in BW0006.
    /// </summary>
    public string? Replacement { get; } = replacement;

    /// <summary>
    /// Every use is an error, baselined or not.
    /// </summary>
    public bool IsForbidden => !allowExistingUses;

    /// <summary>
    /// Baselined uses pass; anything beyond the baseline is an error.
    /// </summary>
    public bool IsGated => allowExistingUses && !allowNewUses;

    /// <summary>
    /// Counted in the baseline, never a diagnostic. Such a row is expected to rise, so the
    /// shrink-only comparison must leave it alone.
    /// </summary>
    public bool IsTrackedOnly => allowExistingUses && allowNewUses;
}
