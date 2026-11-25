namespace Bitwarden.Server.Sdk.Resources;

/// <summary>
/// Envelopes the resources in a page of a collection.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class PagedResourceListJsonConverter<T>
    : CollectionJsonConverter<PagedResourceListDocument<T>, T> where T : Resource;
