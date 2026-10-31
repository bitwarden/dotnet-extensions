namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Which ways of naming resources an operation accepts.
/// </summary>
[Flags]
public enum Targeting
{
    /// <summary>
    /// A list of identifiers.
    /// </summary>
    Ids = 1,

    /// <summary>
    /// An expression.
    /// </summary>
    Filter = 2,

    /// <summary>
    /// Either, but not both in one request.
    /// </summary>
    Either = Ids | Filter,
}
