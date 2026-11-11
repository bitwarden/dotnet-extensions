namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Leaves an operation out of idempotency keys: a write another service sends with an <c>Idempotency-Key</c> is run
/// every time it arrives, rather than once with its first answer replayed.
/// </summary>
/// <remarks>
/// <para>
/// Every write from another service is otherwise done at most once per key (<c>Idempotency:*</c>, PACKAGE.md). Opt
/// out an operation that is idempotent by its own state, one that only reads (a <c>POST</c> check, look-up, validation
/// or preview, which must answer from the state now), or one whose answer must never be kept (one larger than the
/// replay store keeps is left out by itself). Service clients retry a keyed write after a failed connection, so an
/// operation opted out here must be safe to receive twice.
/// </para>
/// <para>
/// On the handler method, or its class.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IgnoreIdempotencyKeyAttribute : Attribute;
