using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// What a bulk action is given: one body for every resource, or one body each.
/// </summary>
/// <typeparam name="TInput">What the single-resource action accepts as its body.</typeparam>
/// <remarks>
/// <para>
/// One field on the wire, <c>data</c>, holding an object or an array, the shape JSON:API uses
/// for the same reason. An object is the ordinary case: the same instruction for everybody.
/// </para>
/// <para>
/// An array is for an action whose input the server cannot compute and that differs per
/// resource. A key wrapped for each user is the case that forces it: the server holds neither
/// the key nor the means to derive it, so the client is the only party that can produce one per
/// resource.
/// </para>
/// </remarks>
[JsonConverter(typeof(BulkInputJsonConverterFactory))]
public sealed class BulkInput<TInput>
{
    /// <summary>
    /// The body every targeted resource is given, when there is one.
    /// </summary>
    public TInput? Every { get; init; }

    /// <summary>
    /// The body each targeted resource is given, in the order they were named.
    /// </summary>
    public IReadOnlyList<TInput>? Each { get; init; }

    /// <summary>
    /// Whether a body was given per resource rather than one for all of them.
    /// </summary>
    public bool IsPerResource => Each is not null;

    /// <summary>
    /// The body for the resource at a position.
    /// </summary>
    /// <param name="index">Which of the named resources, from zero.</param>
    /// <remarks>
    /// The same body every time for the uniform form, so an operation writes one loop rather
    /// than two and does not care which shape arrived.
    /// </remarks>
    public TInput? At(int index) => Each is null ? Every : Each[index];
}
