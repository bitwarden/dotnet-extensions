namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes the resources in an unpaged collection.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class ResourceListJsonConverter<T>
    : CollectionJsonConverter<ResourceListDocument<T>, T> where T : Resource;
