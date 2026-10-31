namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Where the rest of a collection is.
/// </summary>
/// <remarks>
/// A link is absent when it would not go anywhere: <c>prev</c> on the first page, <c>next</c> on
/// the last. Absent rather than null, so a caller can test for the link itself.
/// </remarks>
public sealed record PageLinks
{
    /// <summary>
    /// The first page.
    /// </summary>
    public string? First { get; init; }

    /// <summary>
    /// The page before this one.
    /// </summary>
    public string? Prev { get; init; }

    /// <summary>
    /// The page after this one.
    /// </summary>
    public string? Next { get; init; }

    /// <summary>
    /// The last page.
    /// </summary>
    public string? Last { get; init; }
}
