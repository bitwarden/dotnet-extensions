namespace Bitwarden.Server.Sdk.WriteSpans;

/// <summary>
/// How an operation whose writes span services handles a failure part way: one or more shapes
/// (<see cref="SpansServicesAttribute"/>).
/// </summary>
/// <remarks>
/// Services have stores of their own, so no transaction surrounds an operation that writes in two of them. Each
/// service-to-service write is already safe to repeat (an <c>Idempotency-Key</c>, done once by the callee); which order
/// or undo makes the operation as a whole safe depends on the operation, so it is chosen with it.
/// </remarks>
[Flags]
public enum SpanShape
{
    /// <summary>
    /// None: never valid on its own in a declaration.
    /// </summary>
    None = 0,

    /// <summary>
    /// Every write already made has an undo, run newest first when a later one fails (cancellation and timeouts
    /// included), on a deadline of its own; the operation's own failure stands.
    /// </summary>
    Compensated = 1,

    /// <summary>
    /// The step that can't be undone (a payment, a cancellation, a mail) runs after every check and every step that
    /// can; a step whose answer was lost is asked once more, or left for support with a log naming what is left.
    /// </summary>
    IrreversibleLast = 2,

    /// <summary>
    /// Each write states the whole value, in an order that leaves what the next attempt compares against unchanged,
    /// so the same request again (or the same message delivered again) finishes the operation.
    /// </summary>
    RetrySafeByState = 4,

    /// <summary>
    /// Every service stages its part first, then one commit makes them all take effect, and a failure after the commit
    /// rolls forward (key rotation).
    /// </summary>
    StageThenCommit = 8,
}
