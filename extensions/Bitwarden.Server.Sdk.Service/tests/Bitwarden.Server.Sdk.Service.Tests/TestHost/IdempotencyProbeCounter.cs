namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// How many probe writes ran; and a gate the slow one waits at.
/// </summary>
public sealed class IdempotencyProbeCounter
{
    private int _count;

    /// <summary>
    /// How many ran.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Opened by a test to let the slow write finish.
    /// </summary>
    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Counts one.
    /// </summary>
    public int Increment() => Interlocked.Increment(ref _count);
}
