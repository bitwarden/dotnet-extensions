namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// A value that flows on the execution context as a stack of disposable entries: the innermost entry is current, and
/// disposing it restores the one it was pushed inside.
/// </summary>
/// <remarks>
/// <para>
/// The execution context carries a reference to the entry, so a flow forked inside an entry, such as a task started
/// and not awaited, shares it with its parent. Disposing an entry therefore touches the ambient value only on the flow
/// where that entry is current: a forked flow keeps the value it started with after its parent's entry ends.
/// </para>
/// <para>
/// Disposing entries out of order never brings back one already disposed. An entry disposed while an entry pushed
/// inside it is still current is passed over when that inner entry restores, so disposing an outer entry and then the
/// inner one leaves none of them current. An entry disposed while it was current is not passed over: a forked flow
/// that pushes and disposes an entry of its own returns to the value it started with.
/// </para>
/// </remarks>
/// <typeparam name="T">The value each entry holds.</typeparam>
internal sealed class AmbientStack<T>
{
    private readonly AsyncLocal<Entry?> _ambient = new();

    /// <summary>
    /// The innermost entry's value on this flow, or the default when there is none.
    /// </summary>
    public T? Current => _ambient.Value is { } entry ? entry.Value : default;

    /// <summary>
    /// Makes <paramref name="value"/> current until the returned value is disposed.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>Restores whatever was current before, when disposed.</returns>
    public IDisposable Push(T value)
    {
        var entry = new Entry(this, value, _ambient.Value);
        _ambient.Value = entry;

        return entry;
    }

    private sealed class Entry(AmbientStack<T> stack, T value, Entry? outer) : IDisposable
    {
        private bool _ended;

        // Disposed while an entry pushed inside it was current, on the flow disposing it.
        private bool _disposedOutOfOrder;

        public T Value { get; } = value;

        public Entry? Outer { get; } = outer;

        public void Dispose()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;

            // Only the flow this entry is current on moves. Another flow holding it, forked inside it, keeps it, and an
            // inner entry still current here stays current until it is disposed itself.
            if (!ReferenceEquals(stack._ambient.Value, this))
            {
                _disposedOutOfOrder = true;
                return;
            }

            var restored = Outer;

            while (restored is { _disposedOutOfOrder: true })
            {
                restored = restored.Outer;
            }

            stack._ambient.Value = restored;
        }
    }
}
