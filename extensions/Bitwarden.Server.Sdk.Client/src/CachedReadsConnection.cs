using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The service cache's connection, for reading; made on first use, and rested after a failure.
/// </summary>
/// <param name="configuration">The connection string.</param>
/// <param name="time">
/// The clock a read's patience and the rest after a failure are measured on; the system's unless
/// the host registers one. A test that must see an answer come from the cache registers a clock
/// that does not move, so a busy machine cannot turn the read into a miss.
/// </param>
internal sealed class CachedReadsConnection(string configuration, TimeProvider? time = null) : IAsyncDisposable
{
    /// <summary>
    /// How long reads skip the cache after it failed, so a sick Redis does not slow every call.
    /// </summary>
    internal static readonly TimeSpan Rest = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The clock reads wait on Redis by.
    /// </summary>
    public TimeProvider Time { get; } = time ?? TimeProvider.System;

    private readonly Lazy<Task<ConnectionMultiplexer>> _connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(configuration);
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.ConnectAsync(options);
    });

    private long _failedAt = long.MinValue;

    /// <summary>
    /// The database, or null while resting after a failure.
    /// </summary>
    public async Task<IDatabase?> DatabaseAsync()
    {
        var failedAt = Interlocked.Read(ref _failedAt);

        if (failedAt != long.MinValue && Time.GetElapsedTime(failedAt) < Rest)
        {
            return null;
        }

        var connection = await _connection.Value;

        return connection.IsConnected ? connection.GetDatabase() : null;
    }

    /// <summary>
    /// Notes a failure: reads skip the cache for a while.
    /// </summary>
    public void Failed() => Interlocked.Exchange(ref _failedAt, Time.GetTimestamp());

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
        {
            await _connection.Value.Result.DisposeAsync();
        }
    }
}
