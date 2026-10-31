using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// An all-or-nothing bulk operation refused, with every reason rather than the first.
/// </summary>
/// <remarks>
/// The standards' all-or-nothing failure: nothing has changed, and the response is an ordinary
/// error document carrying every failure, each pointed at the item it concerns. <c>422</c>, because
/// a version conflict is never among them (a bulk operation that can conflict reports it per
/// resource).
/// </remarks>
/// <param name="errors">Every failure, in request order.</param>
public sealed class BulkRefusedException(IReadOnlyList<Error> errors)
    : Exception("The request was refused; nothing was changed."), IProvideErrors
{
    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; } = errors;
}
