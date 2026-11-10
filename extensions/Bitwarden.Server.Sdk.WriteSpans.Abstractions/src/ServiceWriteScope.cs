using System.Runtime.ExceptionServices;

namespace Bitwarden.Server.Sdk.WriteSpans;

/// <summary>
/// One operation's writes, as <see cref="ServiceWrites"/> counts them.
/// </summary>
public sealed class ServiceWriteScope : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<string> _steps = [];
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly SpanDeclarationAttribute? _declared;
    private readonly IServiceWritesGuard? _guard;
    private readonly ServiceWriteScope? _outer;
    private bool _outgoing;
    private bool _spanned;
    private ExceptionDispatchInfo? _refusal;

    internal ServiceWriteScope(string operation, SpanDeclarationAttribute? declared, IServiceWritesGuard? guard, ServiceWriteScope? outer)
    {
        Operation = operation;
        _declared = declared;
        _guard = guard;
        _outer = outer;
    }

    /// <summary>
    /// Names the operation.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Where each write went so far, in order: a service's name, or <see cref="ServiceWrites.OwnDatabase"/>.
    /// </summary>
    public IReadOnlyList<string> Steps
    {
        get
        {
            lock (_lock)
            {
                return [.. _steps];
            }
        }
    }

    /// <summary>
    /// Whether the writes span services: two or more, at least one to another service.
    /// </summary>
    public bool Spans
    {
        get
        {
            lock (_lock)
            {
                return _spanned;
            }
        }
    }

    internal bool Ended { get; private set; }

    /// <summary>
    /// Throws the guard's refusal again if the operation caught it and went on, so an operation can't finish as if
    /// its refused write had been made.
    /// </summary>
    public void ThrowIfRefused()
    {
        ExceptionDispatchInfo? refusal;

        lock (_lock)
        {
            refusal = _refusal;
        }

        refusal?.Throw();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Ended)
        {
            return;
        }

        Ended = true;
        ServiceWrites.Restore(_outer);
    }

    internal void Record(string target, string? key, bool outgoing)
    {
        lock (_lock)
        {
            // Refused once, refused for good: every later write of the operation is refused the same way.
            _refusal?.Throw();

            if (key is not null && !_keys.Add(key))
            {
                return;
            }

            _steps.Add(target);
            _outgoing |= outgoing;

            if (_spanned || !_outgoing || _steps.Count < 2)
            {
                return;
            }

            _spanned = true;

            // The guard decides under the lock, so a write the operation's other work makes meanwhile waits for the
            // decision and, if it is a refusal, is refused with it rather than recorded as made.
            try
            {
                _guard?.Spanned(Operation, [.. _steps], _declared);
            }
            catch (Exception refusal)
            {
                _refusal = ExceptionDispatchInfo.Capture(refusal);
                throw;
            }
        }
    }
}
