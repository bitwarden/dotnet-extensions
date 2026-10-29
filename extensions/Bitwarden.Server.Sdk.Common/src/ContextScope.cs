namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Establishes what the current unit of work concerns, where there is no request to say.
/// </summary>
/// <remarks>
/// <para>
/// A service reads its context from the request it is handling. Work that arrives without a
/// request — a message taken off a queue — has the same need and no headers to meet it, so a
/// call it makes to another service would carry no organization and be refused, or worse,
/// be attributed to nobody.
/// </para>
/// <para>
/// Ambient rather than passed, because the thing that reads it is a client handler pooled for
/// the life of the process, which is precisely where a value cannot be injected. It flows on the
/// execution context, as the request does, so a scope begun around one message is seen by every
/// call that message's handling makes and by nothing else.
/// </para>
/// <para>
/// Not for request handling. A request's context is its headers; beginning a scope inside a
/// request would let code substitute an organization the caller never named.
/// </para>
/// </remarks>
public static class ContextScope
{
    private static readonly AsyncLocal<IContext?> Ambient = new();

    /// <summary>The context established by the innermost scope, if any.</summary>
    public static IContext? Current => Ambient.Value;

    /// <summary>
    /// Establishes a context until the returned value is disposed.
    /// </summary>
    /// <param name="context">What the unit of work concerns.</param>
    /// <returns>Restores whatever was established before, when disposed.</returns>
    public static IDisposable Begin(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = Ambient.Value;
        Ambient.Value = context;

        return new Restore(previous);
    }

    private sealed class Restore(IContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Ambient.Value = previous;
            _disposed = true;
        }
    }
}

/// <summary>
/// A context stated outright rather than read from a request.
/// </summary>
/// <remarks>
/// What a message consumer establishes with <see cref="ContextScope.Begin"/>, from the context
/// the message was published with.
/// </remarks>
public sealed record StatedContext : IContext
{
    /// <inheritdoc />
    public Guid? OrganizationId { get; init; }

    /// <inheritdoc />
    public Guid? UserId { get; init; }

    /// <inheritdoc />
    public Guid? ProviderId { get; init; }

    /// <inheritdoc />
    public string? ClientIpAddress { get; init; }

    /// <inheritdoc />
    public string? DeviceType { get; init; }

    /// <inheritdoc />
    public string? DeviceIdentifier { get; init; }

    /// <inheritdoc />
    public CallerCredential? Credential { get; init; }

    /// <inheritdoc />
    public Guid? RecordedUserId { get; init; }

    /// <inheritdoc />
    public Guid? RecordedProviderId { get; init; }

    /// <inheritdoc />
    /// <remarks>A message carries none: its consumer evaluates no flags for the person who caused it.</remarks>
    public IReadOnlyList<Guid>? UserOrganizationIds { get; init; }
}
