using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A question too rich for query parameters, asked of a collection.
/// </summary>
/// <typeparam name="T">The resource being searched.</typeparam>
/// <remarks>
/// The expression is required, as on the bulk operations. A search with no filter is a
/// read-many, and a caller who meant one should use it.
/// </remarks>
public sealed record Search<T> where T : Resource
{
    /// <summary>
    /// Which resources to return.
    /// </summary>
    public required Query Filter { get; init; }
}
