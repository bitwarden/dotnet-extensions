using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// The service cache's Redis connection, made on first use.
/// </summary>
/// <remarks>
/// Never at startup: a service whose cache is unreachable still serves every read from its
/// database, and refusing to start for want of a cache would turn an optimisation into an outage.
/// </remarks>
internal sealed class OwnedCacheConnection(string configuration) : IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection = new(() => Connect(configuration));

    public async Task<IDatabase> DatabaseAsync() => (await _connection.Value).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
        {
            await _connection.Value.Result.DisposeAsync();
        }
    }

    private static Task<ConnectionMultiplexer> Connect(string configuration)
    {
        var options = ConfigurationOptions.Parse(configuration);

        // Keep trying in the background rather than failing every call until a restart.
        options.AbortOnConnectFail = false;

        return ConnectionMultiplexer.ConnectAsync(options);
    }
}
