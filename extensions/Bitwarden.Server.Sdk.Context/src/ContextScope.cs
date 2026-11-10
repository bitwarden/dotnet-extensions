namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// Establishes what the current unit of work concerns, where there is no request to say.
/// </summary>
/// <remarks>
/// <para>
/// Work that arrives without a request, such as a message taken off a queue, has no headers to say
/// which organization or user it concerns. A scope begun around that work flows on the execution
/// context, so every call the work makes sees it and nothing else does.
/// </para>
/// <para>
/// Not for request handling. A request's context is its headers, and beginning a scope inside a
/// request would let code substitute an organization the caller never named.
/// </para>
/// </remarks>
public static class ContextScope
{
    private static readonly AsyncLocal<Scope?> Ambient = new();

    /// <summary>
    /// The context established by the innermost scope, if any. A scope already ended, by disposing a scope it was
    /// begun inside, establishes nothing.
    /// </summary>
    public static IContext? Current => Ambient.Value is { Ended: false } scope ? scope.Context : null;

    /// <summary>
    /// Whether the innermost scope was begun for an event being handled (<see cref="BeginForEvent"/>): its person, if
    /// any, is the event's actor, there for the record only. A scope begun inside it with <see cref="Begin"/> is not.
    /// </summary>
    public static bool IsEvent => Ambient.Value is { Ended: false, ForEvent: true };

    /// <summary>
    /// Establishes a context until the returned value is disposed.
    /// </summary>
    /// <param name="context">What the unit of work concerns.</param>
    /// <returns>Restores whatever was established before, when disposed.</returns>
    public static IDisposable Begin(IContext context) => Push(context, forEvent: false);

    /// <summary>
    /// Establishes what an event says as the context of handling it, until the returned value is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A consumer acts with its own authority, and the event's actor is for the record only. The person the
    /// event names is whoever's request published it: the consumer reads them for what it records, but a call it makes
    /// to another service must not carry them as the person acting, where the callee would take them as this service's
    /// word (an asserted person, which most operations refuse). A client can check <see cref="IsEvent"/> and hold
    /// such a call to that.
    /// </para>
    /// <para>
    /// For a host that hands events to a service's handlers, which begins one around each event.
    /// </para>
    /// </remarks>
    /// <param name="context">What the event says.</param>
    /// <returns>Restores whatever was established before, when disposed.</returns>
    public static IDisposable BeginForEvent(IContext context) => Push(context, forEvent: true);

    private static IDisposable Push(IContext context, bool forEvent)
    {
        ArgumentNullException.ThrowIfNull(context);

        var scope = new Scope(context, forEvent, Ambient.Value);
        Ambient.Value = scope;

        return scope;
    }

    private sealed class Scope(IContext context, bool forEvent, Scope? outer) : IDisposable
    {
        public IContext Context { get; } = context;

        public bool ForEvent { get; } = forEvent;

        public bool Ended { get; private set; }

        public void Dispose()
        {
            if (Ended)
            {
                return;
            }

            Ended = true;
            Ambient.Value = outer;
        }
    }
}
