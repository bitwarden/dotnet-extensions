namespace Bitwarden.Server.Sdk.WriteSpans;

/// <summary>
/// Told when an operation's writes first span services (<see cref="ServiceWrites"/>), before the write that makes them.
/// </summary>
/// <remarks>
/// <para>
/// A host's implementation might log and count an undeclared one and refuse it by throwing, which stops the write from
/// being sent and every later write of the operation (<see cref="ServiceWriteScope.ThrowIfRefused"/>).
/// </para>
/// <para>
/// It is told while the operation's other writes wait for its decision, so a write made meanwhile is refused with it.
/// It must not wait for another write of the same operation.
/// </para>
/// </remarks>
public interface IServiceWritesGuard
{
    /// <summary>
    /// The operation's writes now span services.
    /// </summary>
    /// <param name="operation">The operation: an endpoint, or a message handler's, outbox handler's or job's name.</param>
    /// <param name="steps">
    /// Where each write went, in order, the one about to be made last: a service's name, or
    /// <see cref="ServiceWrites.OwnDatabase"/>. Names only, never a value.
    /// </param>
    /// <param name="declared">
    /// The operation's declaration, if it has one: its shape (<see cref="SpansServicesAttribute"/>), or an admitted
    /// defect (<see cref="SpansServicesUnhandledAttribute"/>).
    /// </param>
    void Spanned(string operation, IReadOnlyList<string> steps, SpanDeclarationAttribute? declared);
}
