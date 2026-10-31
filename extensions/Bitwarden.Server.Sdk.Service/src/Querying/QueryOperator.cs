namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// What a query term does.
/// </summary>
public enum QueryOperator
{
    /// <summary>
    /// References a field of the resource.
    /// </summary>
    Var,

    /// <summary>
    /// Equal.
    /// </summary>
    Equal,

    /// <summary>
    /// Not equal.
    /// </summary>
    NotEqual,

    /// <summary>
    /// Greater than.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater than or equal.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Less than.
    /// </summary>
    LessThan,

    /// <summary>
    /// Less than or equal.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Membership in a list, or a substring match.
    /// </summary>
    In,

    /// <summary>
    /// Every term must hold.
    /// </summary>
    And,

    /// <summary>
    /// At least one term must hold.
    /// </summary>
    Or,

    /// <summary>
    /// Negation.
    /// </summary>
    Not,
}
