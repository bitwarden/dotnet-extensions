namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// Declares that an operation is called back on purpose while a request of this service's own is still waiting on the
/// call that led here: an accepted re-entry, with the reason.
/// </summary>
/// <remarks>
/// <para>
/// A service notices being called back by the call chain its callers send (<see cref="Bitwarden.Server.Sdk.Context.BitwardenHeaders.CallChain"/>):
/// its own name is already in it. That is a synchronous loop-back, which holds the first request, and whatever lock or
/// transaction it has, open on work that may need them, so it is logged as a warning, counted and, with
/// <c>CallChain:RefuseReentry</c> (tests and Development), refused. An operation where a loop-back is the right shape,
/// usually because the caller needs the answer before it can go on, says so here and is counted, not refused.
/// </para>
/// <para>
/// One per operation, on the handler method, so each is a reviewed, named exception. The reason should say why the
/// call can't wait until after the calling service's own write has committed.
/// </para>
/// </remarks>
/// <param name="reason">Why the loop-back stays.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AcceptsReentryAttribute(string reason) : Attribute
{
    /// <summary>
    /// Why the loop-back stays.
    /// </summary>
    public string Reason { get; } = !string.IsNullOrWhiteSpace(reason)
        ? reason
        : throw new ArgumentException("An accepted re-entry says why.", nameof(reason));
}
