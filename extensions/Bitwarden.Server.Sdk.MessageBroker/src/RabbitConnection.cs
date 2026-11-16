using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Manages the shared Rabbit <see cref="IConnection"/> for the lifetime of the host. The connection
/// is opened at startup if the broker is reachable, and opened again on demand whenever it is not
/// open: after a broker outage, or when the broker was down at startup.
/// </summary>
internal sealed class RabbitConnection : IHostedService, IAsyncDisposable
{
    /// <summary>
    /// How long one attempt to connect may take. Shorter than the client's 30-second default, so a
    /// broker whose address doesn't answer at all (a network partition rather than a refusal)
    /// fails a send within seconds.
    /// </summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly IOptions<MessagingOptions> _options;
    private readonly object _sync = new();
    private IConnection? _connection;
    private Task<IConnection>? _connecting;

    private readonly CancellationTokenSource _closed = new();
    private volatile bool _isClosing;

    public RabbitConnection(IOptions<MessagingOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// True once the connection has started to close. Subscribers read it to tell a shutdown from
    /// a broker failure, whatever the Rabbit client surfaces while the connection closes.
    /// </summary>
    public bool IsClosing => _isClosing;

    /// <summary>
    /// Cancelled when the connection starts to close. A call still waiting for the broker's reply,
    /// or for a connection attempt, would otherwise wait out the client's own timeout.
    /// </summary>
    public CancellationToken Closed => _closed.Token;

    /// <summary>
    /// Returns the shared connection, opening it first if it isn't open. Every caller that arrives
    /// while an attempt to connect is under way waits for that one attempt and gets its result, so
    /// an outage costs each caller at most one attempt (<see cref="ConnectTimeout"/>). A failed
    /// attempt throws the client's own exception (such as
    /// <see cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException"/>), and the next call
    /// tries again.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The connection has been closed with the host.</exception>
    public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        Task<IConnection> attempt;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_isClosing, this);
            if (_connection is { IsOpen: true } open)
                return Task.FromResult(open);
            if (_connecting is null || _connecting.IsCompleted)
                _connecting = ConnectAsync(TakeDeadConnection());
            attempt = _connecting;
        }
        return attempt.WaitAsync(cancellationToken);
    }

    // Called under _sync: a connection the broker closed is replaced, and disposed off the lock.
    private IConnection? TakeDeadConnection()
    {
        var dead = _connection;
        _connection = null;
        return dead;
    }

    private async Task<IConnection> ConnectAsync(IConnection? dead)
    {
        // Called under _sync: return to the caller first, so that disposing the dead connection and
        // connecting again run off the lock rather than holding up every other caller.
        await Task.Yield();

        if (dead is not null)
        {
            // Its channels are gone with it; publishers and subscribers open new ones on the new
            // connection. Disposing a dead connection can fail, and it holds nothing to recover.
            try
            {
                await dead.DisposeAsync();
            }
            catch (Exception)
            {
            }
        }

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_options.Value.RabbitUri!),
            RequestedConnectionTimeout = ConnectTimeout,
            // This class reconnects on demand, and publishers and subscribers reopen their
            // channels. The client's own recovery would restore the same connection behind their
            // backs, with channels they have already given up on.
            AutomaticRecoveryEnabled = false,
            TopologyRecoveryEnabled = false,
        };
        // Not the caller's token: other callers share this attempt. Closing the connection ends it.
        var connection = await factory.CreateConnectionAsync(_closed.Token);
        lock (_sync)
        {
            if (!_isClosing)
            {
                _connection = connection;
                return connection;
            }
        }
        // The host closed while this attempt was finishing.
        await connection.DisposeAsync();
        throw new ObjectDisposedException(nameof(RabbitConnection));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.Value.RabbitUri))
            return;

        try
        {
            await GetConnectionAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Let the host start while the broker is down. Publishers throw
            // BrokerUnavailableException until it is back, and hosted consumers keep trying to
            // subscribe; each GetConnectionAsync call tries to connect again.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        IConnection? connection;
        lock (_sync)
        {
            _isClosing = true;
            connection = _connection;
            _connection = null;
        }

        // Cancel first: an attempt to connect still under way ends now rather than holding up
        // shutdown, and so do calls waiting for the broker's reply. _closed is never disposed: a
        // subscriber that starts after the connection closed still reads Closed.
        _closed.Cancel();

        if (connection is not null)
            await connection.DisposeAsync();
    }
}
