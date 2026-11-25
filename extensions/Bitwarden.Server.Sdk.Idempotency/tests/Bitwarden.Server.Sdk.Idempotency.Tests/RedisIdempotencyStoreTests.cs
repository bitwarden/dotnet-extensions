using System.Reflection;
using System.Runtime.ExceptionServices;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Idempotency.Tests;

/// <summary>
/// A Redis of its own for the idempotency keys, and one connection to it that the tests read through, opened once and
/// closed before the container: under the whole suite's load, reads through a connection opened per read and never
/// closed failed inside the Redis client.
/// </summary>
public sealed class IdempotencyRedisFixture : IAsyncLifetime
{
    private const int InnerRedisPort = 6379;

    private readonly IContainer _redis = new ContainerBuilder()
        .WithImage("redis:alpine")
        .WithPortBinding(InnerRedisPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
        .Build();

    private ConnectionMultiplexer? _connection;

    public string ConnectionString => $"{_redis.Hostname}:{_redis.GetMappedPublicPort(InnerRedisPort)}";

    public IDatabase Database => (_connection ?? throw new InvalidOperationException("The test Redis isn't started.")).GetDatabase();

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync(TestContext.Current.CancellationToken);
        _connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _redis.DisposeAsync();
    }
}

/// <summary>
/// Keys in the service cache: one record per key across every instance of a service, a claim atomic, and an outage
/// never stopping a write.
/// </summary>
public sealed class RedisIdempotencyStoreTests(IdempotencyRedisFixture redis) : IClassFixture<IdempotencyRedisFixture>
{
    // Two instances receiving one key: one runs it, the other finds it held.
    [Fact]
    public async Task TwoInstances_ClaimingOneKey_OneHoldsIt()
    {
        using var first = Store();
        using var second = Store();
        var key = Key();

        var claims = await Task.WhenAll(
            first.ClaimAsync(key, "first", TimeSpan.FromMinutes(1)).AsTask(),
            second.ClaimAsync(key, "second", TimeSpan.FromMinutes(1)).AsTask());

        Assert.Single(claims, claim => claim.Outcome == ClaimOutcome.Held);
        Assert.Single(claims, claim => claim.Outcome == ClaimOutcome.Found);
    }

    [Fact]
    public async Task AnAnswer_IsWhatAnotherInstanceFinds()
    {
        using var first = Store();
        using var second = Store();
        var key = Key();
        await first.ClaimAsync(key, "pending", TimeSpan.FromMinutes(1));

        await first.CompleteAsync(key, "answer", TimeSpan.FromMinutes(5));
        var found = await second.ClaimAsync(key, "again", TimeSpan.FromMinutes(1));

        Assert.Equal("answer", found.Existing);
        Assert.InRange((await Database().KeyTimeToLiveAsync(key))!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Release_LetsGoOnlyOfItsOwn()
    {
        using var store = Store();
        var key = Key();
        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync(key, "mine", TimeSpan.FromMinutes(1))).Outcome);

        await store.ReleaseAsync(key, "not-mine");
        Assert.True(await Database().KeyExistsAsync(key));

        await store.ReleaseAsync(key, "mine");
        Assert.False(await Database().KeyExistsAsync(key));
    }

    [Fact]
    public async Task AClaim_ExpiresWithItsLease()
    {
        using var store = Store();
        var key = Key();

        await store.ClaimAsync(key, "pending", TimeSpan.FromMilliseconds(200));
        await Task.Delay(400, TestContext.Current.CancellationToken);

        Assert.Equal(ClaimOutcome.Held, (await store.ClaimAsync(key, "again", TimeSpan.FromMinutes(1))).Outcome);
    }

    // Redis away: the write runs without its key.
    [Fact]
    public async Task RedisAway_IsUnavailable()
    {
        using var store = new RedisIdempotencyStore(
            RedisIdempotencyStore.Connect("127.0.0.1:1,connectTimeout=200"),
            NullLogger<RedisIdempotencyStore>.Instance);

        Assert.Equal(ClaimOutcome.Unavailable, (await store.ClaimAsync(Key(), "pending", TimeSpan.FromMinutes(1))).Outcome);
        await store.CompleteAsync(Key(), "answer", TimeSpan.FromMinutes(1));
        await store.ReleaseAsync(Key(), "pending");
    }

    // A record that expires between the SET and the GET is gone: the second SET takes it.
    [Fact]
    public async Task ARecordThatExpiresBetweenTheSetAndTheGet_IsTakenByTheSecondSet()
    {
        var key = Key();
        await Database().StringSetAsync(key, "other", TimeSpan.FromMinutes(1));
        var expired = false;
        using var store = Store(database => Intercepting<IDatabase>.Over(database, (method, args, forward) =>
        {
            if (method.Name == nameof(IDatabase.StringGetAsync) && args?[0] is RedisKey read && read == key && !expired)
            {
                expired = true;
                database.KeyDelete(key);
            }

            return forward();
        }));

        var claim = await store.ClaimAsync(key, "pending", TimeSpan.FromMinutes(1));

        Assert.True(expired);
        Assert.Equal(ClaimOutcome.Held, claim.Outcome);
        Assert.Equal("pending", (string?)await Database().StringGetAsync(key));
    }

    // Redis failing mid-command: a claim is unavailable, and the write runs without its key.
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task RedisFailing_AClaimIsUnavailable(Exception failure)
    {
        using var store = Failing(failure);

        Assert.Equal(ClaimOutcome.Unavailable, (await store.ClaimAsync(Key(), "pending", TimeSpan.FromMinutes(1))).Outcome);
    }

    // Redis failing mid-command: an answer that can't be kept, or a key that can't be let go of, never fails the write.
    [Theory]
    [MemberData(nameof(Failures))]
    public async Task RedisFailing_CompleteAndReleaseDoNotThrow(Exception failure)
    {
        using var store = Failing(failure);

        await store.CompleteAsync(Key(), "answer", TimeSpan.FromMinutes(1));
        await store.ReleaseAsync(Key(), "pending");
    }

    // A connection that is not up is not asked: a command on it would wait out its timeout on the request.
    [Fact]
    public async Task ConnectionNotUp_IsUnavailableWithoutAsking()
    {
        var asked = 0;
        var database = Intercepting<IDatabase>.Over(null, (method, args, forward) =>
        {
            asked++;
            return method.Name == nameof(IDatabase.StringSetAsync) ? Task.FromResult(true) : forward();
        });
        using var store = new RedisIdempotencyStore(
            () => Task.FromResult(Connection(connected: false, database)),
            NullLogger<RedisIdempotencyStore>.Instance);

        Assert.Equal(ClaimOutcome.Unavailable, (await store.ClaimAsync(Key(), "pending", TimeSpan.FromMinutes(1))).Outcome);
        await store.CompleteAsync(Key(), "answer", TimeSpan.FromMinutes(1));
        await store.ReleaseAsync(Key(), "pending");
        Assert.Equal(0, asked);
    }

    public static TheoryData<Exception> Failures() =>
    [
        new RedisConnectionException(ConnectionFailureType.SocketFailure, "The test Redis failed."),
        new RedisTimeoutException("The test Redis timed out.", CommandStatus.Sent),
        new TimeoutException("The test Redis timed out."),
    ];

    /// <summary>
    /// A store on a connection that is up, whose every command fails with <paramref name="failure"/>.
    /// </summary>
    private static RedisIdempotencyStore Failing(Exception failure)
    {
        var database = Intercepting<IDatabase>.Over(null, (method, args, forward) => method.ReturnType.IsGenericType
            ? typeof(Task).GetMethod(nameof(Task.FromException), 1, [typeof(Exception)])!
                .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(null, [failure])
            : Task.FromException(failure));

        return new RedisIdempotencyStore(
            () => Task.FromResult(Connection(connected: true, database)),
            NullLogger<RedisIdempotencyStore>.Instance);
    }

    private static IConnectionMultiplexer Connection(bool connected, IDatabase database) =>
        Intercepting<IConnectionMultiplexer>.Over(null, (method, args, forward) => method.Name switch
        {
            "get_IsConnected" => connected,
            nameof(IConnectionMultiplexer.GetDatabase) => database,
            nameof(IDisposable.Dispose) => null,
            _ => forward(),
        });

    /// <summary>
    /// A store on the test Redis whose database is <paramref name="wrap"/>ped, once its connection is up.
    /// </summary>
    private RedisIdempotencyStore Store(Func<IDatabase, IDatabase> wrap)
    {
        var connect = RedisIdempotencyStore.Connect(redis.ConnectionString);

        return Warm(new RedisIdempotencyStore(
            async () => Intercepting<IConnectionMultiplexer>.Over(await connect(), (method, args, forward) =>
                method.Name == nameof(IConnectionMultiplexer.GetDatabase) ? wrap((IDatabase)forward()!) : forward()),
            NullLogger<RedisIdempotencyStore>.Instance));
    }

    /// <summary>
    /// A store whose connection is up: under load the first command can find it still connecting.
    /// </summary>
    private RedisIdempotencyStore Store() =>
        Warm(new RedisIdempotencyStore(RedisIdempotencyStore.Connect(redis.ConnectionString), NullLogger<RedisIdempotencyStore>.Instance));

    private static RedisIdempotencyStore Warm(RedisIdempotencyStore store)
    {
        for (var tries = 0; store.ClaimAsync(Key(), "warm", TimeSpan.FromSeconds(1)).AsTask().GetAwaiter().GetResult().Outcome
                 == ClaimOutcome.Unavailable; tries++)
        {
            Assert.True(tries < 100, "The test Redis never connected.");
            Thread.Sleep(50);
        }

        return store;
    }

    private static string Key() => $"test-service:idempotency:{Guid.NewGuid():N}";

    private IDatabase Database() => redis.Database;

    /// <summary>
    /// An <typeparamref name="T"/> that passes each call to <c>intercept</c>, which answers it or forwards it to the
    /// inner one (none: forwarding fails).
    /// </summary>
    public class Intercepting<T> : DispatchProxy
        where T : class
    {
        private T? _inner;

        private Func<MethodInfo, object?[]?, Func<object?>, object?> _intercept = (_, _, forward) => forward();

        public static T Over(T? inner, Func<MethodInfo, object?[]?, Func<object?>, object?> intercept)
        {
            var proxy = Create<T, Intercepting<T>>();
            var intercepting = (Intercepting<T>)(object)proxy;
            intercepting._inner = inner;
            intercepting._intercept = intercept;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            _intercept(method!, args, () =>
            {
                if (_inner is null)
                {
                    throw new NotSupportedException($"{typeof(T).Name}.{method!.Name} is not faked.");
                }

                try
                {
                    return method!.Invoke(_inner, args);
                }
                catch (TargetInvocationException thrown) when (thrown.InnerException is not null)
                {
                    ExceptionDispatchInfo.Throw(thrown.InnerException);
                    throw;
                }
            });
    }
}
