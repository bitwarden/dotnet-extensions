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
    private static readonly AmbientStack<IContext> Ambient = new();

    /// <summary>
    /// The context established by the innermost scope, if any.
    /// </summary>
    /// <remarks>
    /// Work started inside a scope and not awaited, such as a task run in the background, keeps the context it started
    /// with after the scope is disposed. Disposing scopes out of order never brings back one already disposed: once both
    /// an outer scope and a scope begun inside it are disposed, neither is current.
    /// </remarks>
    public static IContext? Current => Ambient.Current;

    /// <summary>
    /// Whether the innermost scope's context is an event's (<see cref="IContext.IsEvent"/>), as
    /// <see cref="BeginForEvent"/> establishes it: its person, if any, is the event's actor, there for the record only. A
    /// scope begun inside it with <see cref="Begin"/> and a context that is not an event's is not.
    /// </summary>
    /// <remarks>
    /// It follows <see cref="Current"/>: work started inside an event's scope and not awaited still handles that event
    /// after the scope is disposed.
    /// </remarks>
    public static bool IsEvent => Current?.IsEvent == true;

    /// <summary>
    /// Establishes a context until the returned value is disposed.
    /// </summary>
    /// <param name="context">What the unit of work concerns.</param>
    /// <returns>Restores whatever was established before, when disposed.</returns>
    public static IDisposable Begin(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Ambient.Push(context);
    }

    /// <summary>
    /// Establishes what an event says as the context of handling it, until the returned value is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A consumer acts with its own authority, and the event's actor is for the record only. The person the
    /// event names is whoever's request published it: the consumer reads them for what it records, but a call it makes
    /// to another service must not carry them as the person acting, where the callee would take them as this service's
    /// word (an asserted person, which most operations refuse). The context this establishes reports
    /// <see cref="IContext.IsEvent"/>, so anything handed it, and a client checking <see cref="IsEvent"/>, can tell the
    /// event's actor from a person acting and hold such a call to that.
    /// </para>
    /// <para>
    /// For a host that hands events to a service's handlers, which begins one around each event.
    /// </para>
    /// </remarks>
    /// <param name="context">What the event says.</param>
    /// <returns>Restores whatever was established before, when disposed.</returns>
    public static IDisposable BeginForEvent(IContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Ambient.Push(context.IsEvent ? context : new EventContext(context));
    }

}
