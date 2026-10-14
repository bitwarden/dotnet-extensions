using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Manages the shared Rabbit <see cref="IConnection"/> for the lifetime of the host. The connection
/// is opened at startup and signalled to publishers and subscribers when ready.
/// </summary>
internal sealed class RabbitConnection : IHostedService, IAsyncDisposable
{
    private readonly IOptions<MessagingOptions> _options;
    private readonly TaskCompletionSource<IConnection> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RabbitConnection(IOptions<MessagingOptions> options)
    {
        _options = options;
    }

    /// <summary>Returns the shared connection, waiting until startup has initialized it.</summary>
    public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default) =>
        _tcs.Task.WaitAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var uri = _options.Value.RabbitUri;
        if (string.IsNullOrEmpty(uri))
            return;

        try
        {
            var factory = new ConnectionFactory { Uri = new Uri(uri) };
            var conn = await factory.CreateConnectionAsync(cancellationToken);
            _tcs.TrySetResult(conn);
        }
        catch (Exception ex)
        {
            // Fault the TCS so publishers and subscribers that are awaiting the connection
            // fail immediately rather than hanging indefinitely. Do not rethrow — letting
            // StartAsync complete without throwing allows the host to start even when the
            // broker is temporarily unavailable; the first publish/subscribe will surface
            // the exception via BrokerUnavailableException.
            // TODO: Add a retry mechanism. The one-shot TaskCompletionSource means a transient
            // outage at startup permanently disables messaging for the lifetime of the process —
            // even after Rabbit recovers, GetConnectionAsync returns the same faulted task forever.
            _tcs.TrySetException(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_tcs.Task.IsCompletedSuccessfully)
            await (await _tcs.Task).DisposeAsync();
    }
}
