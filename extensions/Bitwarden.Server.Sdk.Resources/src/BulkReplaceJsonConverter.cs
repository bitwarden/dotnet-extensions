
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources;

/// <summary>
/// Envelopes the resources a bulk replace carries, each shaped as the single-resource replace
/// expects it.
/// </summary>
/// <typeparam name="T">The resource type.</typeparam>
internal sealed class BulkReplaceJsonConverter<T>
    : CollectionJsonConverter<BulkReplace<T>, T> where T : Resource
{
    protected override bool CleansResources => true;
}
