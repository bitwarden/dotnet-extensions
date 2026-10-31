namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Which way a field is ordered.
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Smallest first: A before Z, older before newer.
    /// </summary>
    Ascending,

    /// <summary>
    /// Largest first: Z before A, newer before older.
    /// </summary>
    Descending,
}
