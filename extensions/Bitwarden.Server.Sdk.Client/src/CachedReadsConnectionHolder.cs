namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The one connection every client in a process reads the service cache through, if there is one.
/// </summary>
/// <param name="configuration">The connection string, or null when there is no service cache.</param>
/// <param name="time">The clock reads wait on Redis by; the system's when null.</param>
internal sealed class CachedReadsConnectionHolder(string? configuration, TimeProvider? time = null) : IAsyncDisposable
{
    public CachedReadsConnection? Connection { get; } =
        string.IsNullOrWhiteSpace(configuration) ? null : new CachedReadsConnection(configuration, time);

    public ValueTask DisposeAsync() => Connection?.DisposeAsync() ?? ValueTask.CompletedTask;
}
