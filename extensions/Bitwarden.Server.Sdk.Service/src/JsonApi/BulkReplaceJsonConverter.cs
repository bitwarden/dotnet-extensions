using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Envelopes the resources a bulk replace carries, each shaped as the single-resource replace
/// expects it.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class BulkReplaceJsonConverter<T>
    : CollectionJsonConverter<BulkReplace<T>, T> where T : Resource;
