namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Which failures of a step guarded by <see cref="Undo.GuardAsync(Func{Task}, UndoOn)"/> take back the writes recorded
/// before it.
/// </summary>
public enum UndoOn
{
    /// <summary>
    /// Any failure, cancellation included. For a step that writes nothing elsewhere, or whose write is known not to
    /// have landed when it fails: a local save and commit, or a run of writes each of which recorded its own undo once
    /// it succeeded.
    /// </summary>
    AnyFailure,

    /// <summary>
    /// Only a refusal (<see cref="Undo.IsRefusal"/>): the callee answered and wrote nothing. For a step whose own write
    /// may have landed when it fails otherwise (a timeout, an unreachable service, a <c>5xx</c>, the request
    /// cancelled, the callee still running an earlier attempt of the write when the call ends); the writes before it
    /// are then kept, never taken back on a write that may have landed.
    /// </summary>
    Refusal,
}
