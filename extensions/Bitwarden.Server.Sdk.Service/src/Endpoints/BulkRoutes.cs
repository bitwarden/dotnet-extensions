using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Builds the absolute route a bulk operation answers on.
/// </summary>
/// <remarks>
/// Absolute (prefixed <c>~/</c>) because the standards put the operation at
/// <c>{collection}:bulk-update</c>, a suffix on the collection itself. A route group joins its
/// prefix to an endpoint's route with a slash and offers no way to append without one, so the
/// endpoint is mapped at its whole path, outside the collection's group, and the attribute states
/// that whole path for the startup check to compare it with.
/// </remarks>
internal static class BulkRoutes
{
    /// <summary>
    /// The names a bulk action may not take.
    /// </summary>
    /// <remarks>
    /// Bulk actions share a namespace with the other bulk operations, so an action called
    /// "delete" would publish <c>{collection}:bulk-delete</c> and collide with the real one.
    /// Refused at startup rather than left to whichever route wins.
    /// </remarks>
    private static readonly string[] Reserved = ["create", "update", "replace", "delete"];

    public static string For(string collection, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);

        return $"~/{collection.Trim('/')}:{operation}";
    }

    public static string ForAction(string collection, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var name = ResourceTypes.KebabCase(action);

        if (Reserved.Contains(name, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{name}' cannot be the name of a bulk action: it would publish "
                + $"{collection.Trim('/')}:bulk-{name}, which is the bulk {name} operation. "
                + "Bulk actions share a namespace with the other bulk operations.");
        }

        return For(collection, $"bulk-{name}");
    }
}
