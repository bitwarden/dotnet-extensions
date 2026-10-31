namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes the resources in a page of a collection paged by cursor.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class CursorPagedResourceListJsonConverter<T>
    : CollectionJsonConverter<CursorPagedResourceListDocument<T>, T> where T : Resource;
