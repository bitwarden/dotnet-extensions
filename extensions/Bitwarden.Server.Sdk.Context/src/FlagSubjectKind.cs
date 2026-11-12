namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// The kind of party feature flags are evaluated for: who a request was made by, as the token it
/// presented says.
/// </summary>
/// <remarks>
/// For feature flag targeting only. It grants nothing: no authorization, row filter, cache or audit
/// record may read it. Written ALL_CAPS on the wire (<see cref="ContextHeader"/>).
/// </remarks>
public enum FlagSubjectKind
{
    /// <summary>
    /// A person, or nobody in particular: the default. Keyed by the person, or anonymous.
    /// </summary>
    User = 0,

    /// <summary>
    /// An organization acting through a credential of its own, keyed by the organization.
    /// </summary>
    Organization,

    /// <summary>
    /// A Secrets Manager machine account, keyed by the machine account, or by its organization when
    /// the token names no machine account.
    /// </summary>
    ServiceAccount,

    /// <summary>
    /// A PAM access connector, keyed by the connector.
    /// </summary>
    AccessConnector,
}
