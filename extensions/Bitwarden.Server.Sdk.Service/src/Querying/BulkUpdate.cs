using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A change applied to every resource an expression matches.
/// </summary>
/// <typeparam name="T">The resource being changed.</typeparam>
/// <remarks>
/// <para>
/// Not a resource document. A bulk update names no resource and replaces none: it carries the
/// fields being changed and the expression selecting what to change them on, which is a
/// different thing from a representation of one resource.
/// </para>
/// <para>
/// The filter is required by the standards and is required here, with no default. An absent
/// filter on a bulk update would mean "every resource in scope", which is never what somebody
/// meant to leave out.
/// </para>
/// </remarks>
public sealed record BulkUpdate<T> : BulkTargeting<T> where T : Resource
{
    /// <summary>
    /// The fields being changed, by their published names.
    /// </summary>
    /// <remarks>
    /// A partial by construction: a field absent from it is a field left alone, which is what
    /// distinguishes this from the replace semantics of a <c>PUT</c>.
    /// </remarks>
    public required IReadOnlyDictionary<string, JsonElement> Update { get; init; }
}
