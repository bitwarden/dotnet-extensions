using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The wholesale replacement of many resources.
/// </summary>
/// <typeparam name="T">The resource being replaced.</typeparam>
/// <remarks>
/// <c>PUT</c> once per resource, with the same replace-everything semantics: a field left out
/// is cleared, not kept. Each item carries its own identifier, and its version where the
/// service checks one.
/// </remarks>
public sealed record BulkReplace<T> where T : Resource
{
    /// <summary>
    /// The resources to replace, each carrying its identifier.
    /// </summary>
    public required IReadOnlyList<T> Data { get; init; }

    /// <inheritdoc cref="BulkTargeting{T}.Atomicity"/>
    [JsonIgnore]
    public Atomicity Atomicity { get; internal set; } = Atomicity.AllOrNothing;
}
