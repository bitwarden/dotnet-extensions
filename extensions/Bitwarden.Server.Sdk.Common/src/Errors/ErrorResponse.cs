namespace Bitwarden.Server.Sdk.Common.Errors;

/// <summary>
/// The body of every unsuccessful response.
/// </summary>
/// <remarks>
/// One shape for every failure, across every service and status code, so a caller has one thing to
/// deserialize.
/// </remarks>
public sealed record ErrorResponse
{
    /// <summary>
    /// The errors that occurred. Always at least one.
    /// </summary>
    public required IReadOnlyList<Error> Errors { get; init; }
}

/// <summary>
/// One problem that occurred while handling a request.
/// </summary>
public sealed record Error
{
    /// <summary>
    /// A unique identifier for this particular occurrence of the problem.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// The HTTP status code applicable to this problem, as a string.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// A stable, machine-readable error code.
    /// </summary>
    /// <remarks>
    /// Callers branch on this and never on <see cref="Title"/> or <see cref="Detail"/>, both of
    /// which are prose and may be reworded or localized without notice.
    /// </remarks>
    public string? Code { get; init; }

    /// <summary>
    /// A short, human-readable summary that does not change from occurrence to occurrence.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// A human-readable explanation specific to this occurrence. May be localized.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// A reference to the primary source of the error.
    /// </summary>
    public ErrorSource? Source { get; init; }

    /// <summary>
    /// Anything else that identifies what went wrong.
    /// </summary>
    /// <remarks>
    /// Carries <c>resource</c> (a <see cref="FailedResource"/>) on a bulk operation whose failed
    /// resource has no position in the request for <see cref="Source"/> to point at.
    /// </remarks>
    public IDictionary<string, object?>? Meta { get; init; }
}

/// <summary>
/// Names the resource a bulk operation could not act on.
/// </summary>
/// <param name="Type">The resource's type, as the wire names it.</param>
/// <param name="Id">Its identifier.</param>
/// <remarks>
/// Goes in <see cref="Error.Meta"/> under <c>resource</c>, when there is nothing in the request to
/// point at.
/// </remarks>
public readonly record struct FailedResource(string Type, string Id);

/// <summary>
/// Where an error came from.
/// </summary>
/// <remarks>
/// At most one member applies to any one error.
/// </remarks>
public sealed record ErrorSource
{
    /// <summary>
    /// A JSON Pointer to the field in error, such as <c>/data/attributes/email</c>.
    /// </summary>
    public string? Pointer { get; init; }

    /// <summary>
    /// The query parameter that caused the error.
    /// </summary>
    public string? Parameter { get; init; }

    /// <summary>
    /// The name of the request header that caused the error.
    /// </summary>
    public string? Header { get; init; }
}
