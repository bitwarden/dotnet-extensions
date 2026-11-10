namespace Bitwarden.Server.Sdk.WriteSpans;

/// <summary>
/// Declares that an operation's writes span services (two or more, at least one of them to another service called
/// synchronously), and how it handles a failure part way: its <see cref="SpanShape"/>, with the reason.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ServiceWrites"/> counts an operation's writes: each write sent to another service, once however often it
/// is retried with the same idempotency key, and each save to the service's own database. Two or more, one of them to
/// another service, and the operation spans services. The host's guard (<see cref="IServiceWritesGuard"/>) is told
/// with this declaration, and decides what an undeclared span means: it might log it, count it, or refuse the write so
/// that the test that first runs it fails. A message published after the commit is not a synchronous write.
/// </para>
/// <para>
/// On an endpoint's handler method, or on a message handler's, outbox handler's or job's class. Choose the shape the
/// code really implements, and say in the reason how the code implements it and what settled the choice. An operation
/// with no handling yet is a defect to fix, never a shape to declare: admit it with
/// <see cref="SpansServicesUnhandledAttribute"/> until it is fixed.
/// </para>
/// </remarks>
/// <param name="shape">How a failure part way is handled: one or more shapes.</param>
/// <param name="reason">How the operation implements the shape, and what settled the choice.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SpansServicesAttribute(SpanShape shape, string reason)
    : SpanDeclarationAttribute(reason, "A declaration says how the operation handles a failure part way.")
{
    /// <summary>
    /// How a failure part way is handled.
    /// </summary>
    public SpanShape Shape { get; } = shape != SpanShape.None && (shape & ~All) == 0
        ? shape
        : throw new ArgumentException("A declaration names one or more shapes.", nameof(shape));

    private const SpanShape All =
        SpanShape.Compensated | SpanShape.IrreversibleLast | SpanShape.RetrySafeByState | SpanShape.StageThenCommit;
}
