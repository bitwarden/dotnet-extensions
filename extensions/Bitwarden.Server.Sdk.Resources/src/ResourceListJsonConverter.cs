namespace Bitwarden.Server.Sdk.Resources;

/// <summary>
/// Envelopes the resources in an unpaged collection.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class ResourceListJsonConverter<T>
    : CollectionJsonConverter<ResourceListDocument<T>, T> where T : Resource;
