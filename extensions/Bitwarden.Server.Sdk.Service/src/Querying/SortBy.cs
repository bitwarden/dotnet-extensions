namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// One field of a sort, and the direction it is ordered in.
/// </summary>
/// <param name="Field">The field's published name, as it appears in the resource's schema.</param>
/// <param name="Direction">Which way it is ordered.</param>
public readonly record struct SortBy(string Field, SortDirection Direction)
{
    /// <summary>
    /// Orders a field smallest first.
    /// </summary>
    /// <param name="field">The field's published name.</param>
    public static SortBy Ascending(string field) => new(field, SortDirection.Ascending);

    /// <summary>
    /// Orders a field largest first.
    /// </summary>
    /// <param name="field">The field's published name.</param>
    public static SortBy Descending(string field) => new(field, SortDirection.Descending);

    /// <summary>
    /// Whether the field is ordered largest first.
    /// </summary>
    public bool IsDescending => Direction == SortDirection.Descending;

    /// <summary>
    /// Renders the field the way a caller writes it: <c>email</c>, or <c>-email</c>.
    /// </summary>
    public override string ToString() => IsDescending ? $"-{Field}" : Field;
}
