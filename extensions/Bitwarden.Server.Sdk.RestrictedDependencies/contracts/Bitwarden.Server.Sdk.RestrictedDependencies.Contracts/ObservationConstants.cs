namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// The property names carried by a BW0017 observation diagnostic. BW0017 is disabled in every
/// real build; a repository's baseline tool enables it and reads these back, which is how it
/// learns what the analyzer saw without a second scanner.
/// </summary>
public static class ObservationConstants
{
    /// <summary>
    /// The diagnostic id observations are reported under.
    /// </summary>
    public const string DiagnosticId = "BW0017";

    /// <summary>
    /// Names which row shape an observation carries. Always present.
    /// </summary>
    public const string Row = "row";

    /// <summary>
    /// One use of a restricted type, aggregated per site.
    /// </summary>
    public const string UsageRow = "usage";

    /// <summary>
    /// One member the seal on a restricted type covers.
    /// </summary>
    public const string DeclaredMemberRow = "declared-member";

    /// <summary>
    /// One <c>[RestrictedDependencyException]</c>, valid or not.
    /// </summary>
    public const string ExceptionRow = "exception";

    /// <summary>
    /// The owner and tracking id of one restricted type.
    /// </summary>
    public const string RestrictedTypeRow = "restricted-type";

    /// <summary>
    /// Fully qualified metadata name of the restricted type. Present on every row.
    /// </summary>
    public const string Type = "type";

    /// <summary>
    /// The access shape token, as <see cref="DependencyUsageTypeExtensions.ToToken"/> renders it.
    /// </summary>
    public const string UsageKind = "usageKind";

    /// <summary>
    /// Documentation-comment id of a member: the restricted member on a usage row, the sealed
    /// member on a declared-member row.
    /// </summary>
    public const string Member = "member";

    /// <summary>
    /// Documentation-comment id of the member that contains a use.
    /// </summary>
    public const string Site = "site";

    /// <summary>
    /// How many uses of this shape the containing member holds.
    /// </summary>
    public const string Count = "count";

    /// <summary>
    /// Repo-relative path of the first location, for reports only.
    /// </summary>
    public const string File = "file";

    /// <summary>
    /// <c>true</c> when a valid exception covers the use, which keeps it out of the baseline.
    /// </summary>
    public const string Excepted = "excepted";

    /// <summary>
    /// <c>true</c> when the rule governing the use allows new ones, so the row is a snapshot for
    /// reporting rather than a ceiling. A baseline tool writes this onto the row it emits, which is
    /// how <c>BudgetRatchet.FindGrowth</c> knows to leave the row out of the shrink-only comparison.
    /// </summary>
    public const string Tracked = "tracked";

    /// <summary>
    /// Documentation-comment id of the class or member an exception sits on.
    /// </summary>
    public const string Target = "target";

    /// <summary>
    /// Team that owns the dissolution, or carries the excepted debt.
    /// </summary>
    public const string Owner = "owner";

    /// <summary>
    /// Ticket or short reason recorded on an exception.
    /// </summary>
    public const string Reason = "reason";

    /// <summary>
    /// An exception's declared expiry, verbatim, so an unparseable value can still be shown.
    /// </summary>
    public const string Expires = "expires";

    /// <summary>
    /// <c>false</c> when an exception is missing a required property.
    /// </summary>
    public const string Valid = "valid";

    /// <summary>
    /// <c>true</c> when an exception is valid but its date has passed.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// Ticket or cluster id on a restricted type.
    /// </summary>
    public const string Tracking = "tracking";

    /// <summary>
    /// The token a boolean property is written as.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns><c>"true"</c> or <c>"false"</c>.</returns>
    public static string ToToken(bool value) => value ? "true" : "false";
}
