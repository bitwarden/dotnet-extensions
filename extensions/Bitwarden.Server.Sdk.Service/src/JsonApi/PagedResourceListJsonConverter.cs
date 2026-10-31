namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes the resources in a page of a collection.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class PagedResourceListJsonConverter<T>
    : CollectionJsonConverter<PagedResourceListDocument<T>, T> where T : Resource;
