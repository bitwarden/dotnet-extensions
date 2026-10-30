namespace Bitwarden.Server.Sdk.Common;

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
    private static readonly AsyncLocal<IContext?> Ambient = new();

    /// <summary>
    /// The context established by the innermost scope, if any.
    /// </summary>
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
