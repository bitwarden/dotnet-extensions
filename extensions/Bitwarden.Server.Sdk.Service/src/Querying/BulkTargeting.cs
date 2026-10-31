using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// How a bulk operation names the resources it acts on.
/// </summary>
/// <typeparam name="T">The resource being acted on.</typeparam>
/// <remarks>
/// <para>
/// Exactly one of <see cref="Ids"/> and <see cref="Filter"/>, and the platform refuses a
/// request carrying both or neither before an operation sees it. An operation that accepts
/// only one of the two says so with the attribute declaring it.
/// </para>
/// <para>
/// They differ in what happens to a resource that is not there. With <see cref="Ids"/> the
/// caller named it, so a missing one is a failure reported back against their own list. With
/// <see cref="Filter"/> the caller described a set, and a resource outside it is simply
/// outside it.
/// </para>
/// </remarks>
public abstract record BulkTargeting<T> where T : Resource
{
    /// <summary>
    /// The resources to act on, by identifier.
    /// </summary>
    public IReadOnlyList<string>? Ids { get; init; }

    /// <summary>
    /// The resources to act on, by expression.
    /// </summary>
    public Query? Filter { get; init; }

    /// <summary>
    /// What to do when some of them fail.
    /// </summary>
    /// <remarks>
    /// Read from the <c>atomic</c> query parameter by the platform, not from the body, and
    /// already checked against what the operation supports by the time an operation sees it.
    /// </remarks>
    [JsonIgnore]
    public Atomicity Atomicity { get; internal set; } = Atomicity.AllOrNothing;
}
