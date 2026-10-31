using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Reads and validates a resource document from a request body, for its <c>BindAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// The platform owns the whole path from bytes to model. That is what lets it distinguish a body it
/// could not parse from one it could, report every error at the failing level rather than the
/// first, and drop what a caller may not set.
/// </para>
/// <para>
/// The guarantee this exists to make: a handler runs only for a request that passed validation, so
/// a service never writes a null check or a shape check of its own.
/// </para>
/// </remarks>
internal static class ResourceDocumentReading
{
    /// <summary>
    /// Where a failure is left for the standard endpoint filter to refuse the request with.
    /// </summary>
    internal const string FailureKey = "bitwarden.document-failure";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, JsonSerializerOptions), IReadOnlySet<string>> ReadOnly = new();

    /// <summary>
    /// The document, or the failure the request is refused with, left in the request's items too.
    /// </summary>
    public static async Task<(object? Document, DocumentFailure? Failure)> ReadAsync(
        Microsoft.AspNetCore.Http.HttpContext http,
        Type documentType,
        Type resourceType,
        JsonSerializerOptions options)
    {
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(http.RequestAborted);

        var failure = ResourceDocumentBinding.TryBind(
            body,
            options.GetTypeInfo(resourceType),
            ResourceTypes.For(resourceType),
            ReadOnlyDocumentMembers(documentType, options),
            out var document);

        if (failure is not null)
        {
            http.Items[FailureKey] = failure;

            return (null, failure);
        }

        return (document.Deserialize(documentType, options), null);
    }

    /// <summary>
    /// The document's own members a caller may not set, by wire name.
    /// </summary>
    /// <remarks>
    /// Read from the CLR type rather than from serializer metadata: a document carries a
    /// converter, and a type with a converter reports no properties through System.Text.Json.
    /// </remarks>
    private static IReadOnlySet<string> ReadOnlyDocumentMembers(Type documentType, JsonSerializerOptions options) =>
        ReadOnly.GetOrAdd((documentType, options), key => key.Item1
            .GetProperties()
            .Where(property => property.IsDefined(typeof(ReadOnlyAttribute), inherit: true))
            .Select(property => key.Item2.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name)
            .ToHashSet(StringComparer.Ordinal));
}
