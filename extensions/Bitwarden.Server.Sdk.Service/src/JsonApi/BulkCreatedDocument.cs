using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// What a bulk create made: each created resource, in the order the request named them.
/// </summary>
/// <typeparam name="T">The resource.</typeparam>
/// <remarks>
/// <para>
/// Its own type rather than a <see cref="ResourceListDocument{T}"/>, for two things the
/// standards ask of a bulk create's answer: <c>meta.affectedCount</c>, always, and the
/// <c>lid</c> each item carried, echoed beside the created resource's <c>id</c> and
/// <c>type</c>. Neither belongs on an ordinary list, and publishing them there would tell every
/// reader of a list to expect fields that never arrive.
/// </para>
/// <para>
/// For an all-or-nothing create, where the request fails whole or every item succeeds, so
/// <see cref="Data"/> lines up with the request one for one.
/// </para>
/// </remarks>
public sealed record BulkCreatedDocument<T> where T : Resource
{
    /// <summary>
    /// The created resources, in request order.
    /// </summary>
    public required IReadOnlyList<T> Data { get; init; }

    /// <summary>
    /// <c>affectedCount</c>: how many were created.
    /// </summary>
    [ReadOnly]
    public IDictionary<string, object?>? Meta { get; init; }

    /// <summary>
    /// The <c>lid</c> to echo on each created resource, position for position, or null where
    /// its item carried none.
    /// </summary>
    /// <remarks>
    /// Written beside each resource's <c>id</c> and <c>type</c> rather than among its
    /// attributes, which is where JSON:API puts a local identifier.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<string?>? Lids { get; init; }

    /// <summary>
    /// Answers a bulk create with what it made.
    /// </summary>
    /// <param name="request">What was asked for, whose <c>lid</c>s are echoed.</param>
    /// <param name="created">What was made, in request order, one for each item asked for.</param>
    /// <exception cref="ArgumentException">There is not one created resource for each item asked for.</exception>
    public static BulkCreatedDocument<T> For(BulkCreate<T> request, IReadOnlyList<T> created)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(created);

        if (created.Count != request.Data.Count)
        {
            throw new ArgumentException(
                $"{request.Data.Count} resources were asked for and {created.Count} were made. "
                + "An all-or-nothing create answers with one for each, in request order.",
                nameof(created));
        }

        return new BulkCreatedDocument<T>
        {
            Data = created,
            Meta = new Dictionary<string, object?> { ["affectedCount"] = created.Count },
            Lids = request.Lids,
        };
    }
}
