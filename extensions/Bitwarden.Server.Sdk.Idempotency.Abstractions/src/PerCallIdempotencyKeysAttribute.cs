using System.Reflection;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Declares that a message handler or job must not run inside an <see cref="IdempotentOperation"/>, and why: each
/// write it makes then carries a fresh idempotency key, so running it again does every write again.
/// </summary>
/// <remarks>
/// For a handler whose writes must be done again when it runs again: a write whose effect depends on state the
/// callee has gained since, which a replay of the first answer would miss. The reason is required and is the
/// documentation. A host that begins an operation around each handler or job checks <see cref="IsOn"/> and begins none
/// around one marked this way. The handler may still begin operations of its own inside.
/// </remarks>
/// <param name="reason">Why each run's writes must be done again.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PerCallIdempotencyKeysAttribute(string reason) : Attribute
{
    /// <summary>
    /// Why each run's writes must be done again.
    /// </summary>
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason)
        ? throw new ArgumentException("Say why the writes must be done again.", nameof(reason))
        : reason;

    /// <summary>
    /// Whether <paramref name="type"/> declares it.
    /// </summary>
    /// <param name="type">A handler's or job's type.</param>
    /// <exception cref="ArgumentException">The type declares it with a blank reason.</exception>
    public static bool IsOn(Type type) =>
        // Builds the attribute rather than reading its metadata, so a blank reason is refused here too.
        (type ?? throw new ArgumentNullException(nameof(type)))
            .GetCustomAttribute<PerCallIdempotencyKeysAttribute>(inherit: false) is not null;
}
