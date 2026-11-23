using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// The request bodies of the bulk operations and search, which the standard filter checks and the
/// OpenAPI description describes.
/// </summary>
/// <remarks>
/// Matched structurally rather than by interface, because these are records a caller sends and a
/// marker interface would put platform vocabulary into the published schema for no benefit to the
/// reader.
/// </remarks>
internal static class BulkBodies
{
    private static readonly Type[] Definitions =
    [
        typeof(BulkCreate<>), typeof(BulkReplace<>), typeof(BulkUpdate<>), typeof(BulkDelete<>),
        typeof(BulkAction<>), typeof(BulkAction<,>), typeof(Search<>),
    ];

    /// <summary>
    /// Whether the type is one of the bulk bodies, closed over a resource.
    /// </summary>
    /// <param name="type">The type.</param>
    public static bool Contains(Type type) =>
        type.IsGenericType && Definitions.Contains(type.GetGenericTypeDefinition());
}
