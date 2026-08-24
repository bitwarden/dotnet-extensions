using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Describes a single piece of Rabbit topology to declare at startup.
/// An exchange-only entry (no queue) is registered by <c>AddPublisher</c>;
/// an exchange+queue entry is registered by <c>AddSubscriber</c>.
/// </summary>
internal sealed record RabbitTopologyDeclaration(string ExchangeName, string? QueueName = null);

/// <summary>
/// Manages the shared Rabbit <see cref="IConnection"/> for the lifetime of the host: creates it,
/// declares all exchanges and queues registered via <c>AddPublisher</c> / <c>AddSubscriber</c>
/// during startup, then signals publishers and subscribers that the connection is ready.
/// </summary>
internal sealed class RabbitConnection : IHostedService, IAsyncDisposable
{
    private readonly IOptions<MessagingOptions> _options;
    private readonly IEnumerable<RabbitTopologyDeclaration> _declarations;
    private readonly TaskCompletionSource<IConnection> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RabbitConnection(IOptions<MessagingOptions> options, IEnumerable<RabbitTopologyDeclaration> declarations)
    {
        _options = options;
        _declarations = declarations;
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
            try
            {
                await using var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);

                foreach (var decl in _declarations)
                {
                    await channel.ExchangeDeclareAsync(decl.ExchangeName, ExchangeType.Fanout, durable: true,
                        cancellationToken: cancellationToken);

                    if (decl.QueueName is not null)
                    {
                        await channel.QueueDeclareAsync(decl.QueueName, durable: true, exclusive: false,
                            autoDelete: false,
                            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
                            cancellationToken: cancellationToken);
                        await channel.QueueBindAsync(decl.QueueName, decl.ExchangeName, routingKey: "",
                            cancellationToken: cancellationToken);
                    }
                }

                _tcs.TrySetResult(conn);
            }
            catch
            {
                // Topology declaration failed; dispose the connection we opened so it is not leaked.
                await conn.DisposeAsync();
                throw;
            }
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
