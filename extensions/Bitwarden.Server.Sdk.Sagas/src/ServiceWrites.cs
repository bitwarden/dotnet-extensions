namespace Bitwarden.Server.Sdk.Sagas;

/// <summary>
/// Counts the writes of the operation running now, so one that makes two or more writes, at least one of them to
/// another service called synchronously, is noticed before the write that makes it so
/// (<see cref="SpansServicesAttribute"/>). Two writes to the same other service count: a failure between them leaves
/// that service half done.
/// </summary>
/// <remarks>
/// <para>
/// A host begins one around each endpoint, message handler or job run; a client reports each write it sends to another
/// service (<see cref="Outgoing"/>) and the service's data layer each save (<see cref="Saving"/>). The operation flows
/// on the execution context, so whatever the operation awaits is counted with it and nothing outside it is.
/// </para>
/// <para>
/// A write to another service is counted once per idempotency key per service, so a call retried, or an attempt of a
/// retry loop sending the same key, is one write. A blank key is no key. Every save is counted. A message published
/// after the commit is not a synchronous write, so a save that holds only such messages needn't be reported.
/// </para>
/// </remarks>
public static class ServiceWrites
{
    /// <summary>
    /// The name a save to the service's own database is recorded under.
    /// </summary>
    public const string OwnDatabase = "database";

    private static readonly AsyncLocal<ServiceWriteScope?> Ambient = new();

    /// <summary>
    /// The operation running now, or null outside one.
    /// </summary>
    public static ServiceWriteScope? Current => Ambient.Value is { Ended: false } scope ? scope : null;

    /// <summary>
    /// Begins counting an operation's writes until the result is disposed.
    /// </summary>
    /// <param name="operation">
    /// Names the operation in logs and metrics: an endpoint's route template, or a handler's or job's name. It reaches the
    /// guard as given, so never pass the request's path.
    /// </param>
    /// <param name="declared">
    /// The operation's declaration, if it has one: its shape (<see cref="SpansServicesAttribute"/>), or an admitted
    /// defect (<see cref="SpansServicesUnhandledAttribute"/>).
    /// </param>
    /// <param name="guard">Told when the writes first span services; null to only count.</param>
    /// <returns>The operation, which restores whatever was current before when disposed.</returns>
    public static ServiceWriteScope Begin(string operation, SpanDeclarationAttribute? declared, IServiceWritesGuard? guard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var scope = new ServiceWriteScope(operation, declared, guard, Ambient.Value);
        Ambient.Value = scope;

        return scope;
    }

    /// <summary>
    /// The current operation is about to send a write to another service.
    /// </summary>
    /// <param name="service">The callee's name.</param>
    /// <param name="idempotencyKey">
    /// The write's key, so a repeat of it to the same service is not counted again; null or blank when it has none.
    /// </param>
    /// <exception cref="Exception">Whatever the guard throws to refuse the write.</exception>
    public static void Outgoing(string service, string? idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        Current?.Record(service, idempotencyKey, outgoing: true);
    }

    /// <summary>
    /// The current operation is about to save changes to the service's own database.
    /// </summary>
    /// <exception cref="Exception">Whatever the guard throws to refuse the save.</exception>
    public static void Saving() => Current?.Record(OwnDatabase, key: null, outgoing: false);

    internal static void Restore(ServiceWriteScope? outer) => Ambient.Value = outer;
}
