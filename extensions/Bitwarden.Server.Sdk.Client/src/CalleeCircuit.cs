namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// One callee's circuit breaker, shared by every client of it in the process: after
/// <see cref="BitwardenClientOptions.BreakAfter"/> failures in a row the callee isn't called for
/// <see cref="BitwardenClientOptions.BreakFor"/>, then one call is let through as a probe, whose outcome closes the
/// circuit or opens it again.
/// </summary>
/// <remarks>
/// A failure is the callee not being there (<see cref="Transient"/>) or not answering within the call's deadline.
/// Every answer the callee gave, a refusal or a <c>500</c> included, is a success here: it is up.
/// </remarks>
/// <param name="breakAfter">Failures in a row that open the circuit; 0 never opens it.</param>
/// <param name="breakFor">How long it stays open before a probe.</param>
/// <param name="time">The clock.</param>
internal sealed class CalleeCircuit(int breakAfter, TimeSpan breakFor, TimeProvider time)
{
    private readonly Lock _lock = new();
    private int _failures;
    private DateTimeOffset? _openUntil;
    private bool _probing;

    /// <summary>
    /// How long the circuit stays open.
    /// </summary>
    public TimeSpan BreakFor => breakFor;

    /// <summary>
    /// Failures in a row that open it.
    /// </summary>
    public int BreakAfter => breakAfter;

    /// <summary>
    /// Whether a call may go: always while closed; once, as the probe, when an open circuit's time is up; otherwise not.
    /// </summary>
    /// <param name="probe">Whether this call is the probe, whose outcome must be reported.</param>
    public bool TryEnter(out bool probe)
    {
        probe = false;

        if (breakAfter <= 0)
        {
            return true;
        }

        lock (_lock)
        {
            if (_openUntil is not { } until)
            {
                return true;
            }

            if (_probing || time.GetUtcNow() < until)
            {
                return false;
            }

            _probing = true;
            probe = true;
            return true;
        }
    }

    /// <summary>
    /// The callee answered: the circuit closes.
    /// </summary>
    public void Succeeded()
    {
        lock (_lock)
        {
            _failures = 0;
            _openUntil = null;
            _probing = false;
        }
    }

    /// <summary>
    /// The callee wasn't there: a probe's failure opens the circuit again, and enough in a row open it.
    /// </summary>
    /// <param name="probe">Whether the call was the probe.</param>
    public void Failed(bool probe)
    {
        if (breakAfter <= 0)
        {
            return;
        }

        lock (_lock)
        {
            if (probe)
            {
                _probing = false;
                _openUntil = time.GetUtcNow() + breakFor;
                return;
            }

            if (++_failures >= breakAfter && _openUntil is null)
            {
                _openUntil = time.GetUtcNow() + breakFor;
            }
        }
    }

    /// <summary>
    /// The caller gave up: says nothing about the callee, but frees the probe for the next call.
    /// </summary>
    /// <param name="probe">Whether the call was the probe.</param>
    public void Abandoned(bool probe)
    {
        if (!probe)
        {
            return;
        }

        lock (_lock)
        {
            _probing = false;
        }
    }
}
