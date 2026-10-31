namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A page of resources, and where the next one starts.
/// </summary>
/// <typeparam name="T">The resource.</typeparam>
/// <param name="Items">The resources on this page.</param>
/// <param name="Cursor">Where the next page starts, or null when this is the last.</param>
public readonly record struct CursorPaged<T>(IReadOnlyList<T> Items, string? Cursor)
{
    /// <summary>
    /// Whether another page follows this one.
    /// </summary>
    public bool HasMore => Cursor is not null;
}
