namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Marks an operation that a repeat would do twice, so a write another service sends with an <c>Idempotency-Key</c> is
/// done at most once, with its first answer replayed to a repeat.
/// </summary>
/// <remarks>
/// <para>
/// Keys are honoured only where this is declared, and the service's contract says so
/// (<c>x-bitwarden-idempotency-key</c>), so a client package sends a key only to these operations. Declare it on an
/// operation whose repeat can't be told from a new request by the state it leaves: one that creates with an identifier
/// it chooses itself, charges or buys, sends a message or a mail, issues a new secret, or adds to a count. Leave it off
/// an operation that is idempotent by its own state (a replace, a delete, a write that sets a value), and off one that
/// only reads (a <c>POST</c> check, look-up, validation or preview), which must answer from the state now.
/// </para>
/// <para>
/// Each keyed write costs the service two round trips to its idempotency store and holds the answer for
/// <see cref="IdempotencyOptions.Retention"/>.
/// </para>
/// <para>
/// On the handler method, or its class.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AcceptsIdempotencyKeyAttribute : Attribute;
