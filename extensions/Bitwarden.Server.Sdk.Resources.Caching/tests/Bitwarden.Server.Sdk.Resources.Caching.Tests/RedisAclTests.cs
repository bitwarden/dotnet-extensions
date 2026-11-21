using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// Each service writes only its own prefix, and reads others', enforced by
/// Redis access control rather than by the client library.
/// </summary>
/// <remarks>
/// The users are those a cloud deployment provisions, one per service, in the form
/// <c>ServiceCacheAcl.User</c> writes them; here two services, a shelf that owns books and a library
/// that reads them.
/// </remarks>
public sealed class RedisAclTests(RedisAclTests.AclRedis redis) : IClassFixture<RedisAclTests.AclRedis>
{
    [Fact]
    public async Task Owner_FillsAndInvalidatesItsOwnPrefix()
    {
        await using var shelf = new OwnedCacheConnection(redis.As("shelf-service"));
        var store = new RedisOwnedCacheStore(shelf, NullLogger<RedisOwnedCacheStore>.Instance);
        var key = $"shelf-service:book:{Guid.NewGuid()}";

        Assert.True(await store.FillAsync(key, 1, null, "{}", TimeSpan.FromMinutes(1)));
        await store.InvalidateAsync([new Invalidation(key, 2)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        await using var library = await ConnectionMultiplexer.ConnectAsync(redis.As("library-service"));
        Assert.Equal("1", (await library.GetDatabase().HashGetAsync(key, "gone")).ToString());
    }

    [Fact]
    public async Task Owner_CannotWriteAnotherServicesPrefix()
    {
        await using var shelf = new OwnedCacheConnection(redis.As("shelf-service"));
        var database = await shelf.DatabaseAsync();

        var refusal = await Assert.ThrowsAsync<RedisServerException>(() => database.ScriptEvaluateAsync(
            RedisOwnedCacheStore.FillScript,
            [$"library-service:book:{Guid.NewGuid()}"],
            [1, 60000, "", "{}", "0"]));

        Assert.Contains("NOPERM", refusal.Message);
    }

    [Fact]
    public async Task Reader_ReadsAnotherServicesEntriesButCannotWriteThem()
    {
        var key = $"shelf-service:book:{Guid.NewGuid()}";
        await using (var shelf = await ConnectionMultiplexer.ConnectAsync(redis.As("shelf-service")))
        {
            await shelf.GetDatabase().HashSetAsync(key, [new("v", 1), new("d", "{}")]);
        }

        await using var library = await ConnectionMultiplexer.ConnectAsync(redis.As("library-service"));
        var database = library.GetDatabase();

        Assert.Equal("1", (await database.HashGetAsync(key, "v")).ToString());
        Assert.Contains("NOPERM", (await Assert.ThrowsAsync<RedisServerException>(() => database.HashSetAsync(key, "d", "forged"))).Message);
        Assert.Contains("NOPERM", (await Assert.ThrowsAsync<RedisServerException>(() => database.KeyDeleteAsync(key))).Message);
        Assert.Contains("NOPERM", (await Assert.ThrowsAsync<RedisServerException>(() => database.ScriptEvaluateAsync(
            RedisOwnedCacheStore.InvalidateScript, [key], [60000, 99]))).Message);
    }

    // Keys name accounts and organizations. A command that takes no key isn't confined by key permissions: before
    // the rule took them away, any service could list every other service's keys with SCAN or RANDOMKEY, from a
    // script too, and count them with DBSIZE.
    [Theory]
    [InlineData("return redis.call('SCAN', '0')", "scan")]
    [InlineData("return redis.call('RANDOMKEY')", "randomkey")]
    [InlineData("return redis.call('DBSIZE')", "dbsize")]
    public async Task Service_CannotListOrCountKeys(string script, string command)
    {
        await using (var shelf = await ConnectionMultiplexer.ConnectAsync(redis.As("shelf-service")))
        {
            await shelf.GetDatabase().StringSetAsync($"shelf-service:book:{Guid.NewGuid()}", "{}");
        }

        await using var library = await ConnectionMultiplexer.ConnectAsync(redis.As("library-service"));

        var refusal = await Assert.ThrowsAsync<RedisServerException>(() => library.GetDatabase().ScriptEvaluateAsync(script));

        Assert.Contains($"no permissions to run the '{command}", refusal.Message);
    }

    // Broadcast tracking pushes the name of every key any user writes, whatever the tracking user's key permissions;
    // the synchronous script debugger blocks the server for every user.
    [Theory]
    [InlineData("client|tracking", new[] { "CLIENT", "TRACKING", "ON", "BCAST" })]
    [InlineData("client|caching", new[] { "CLIENT", "CACHING", "YES" })]
    [InlineData("client|trackinginfo", new[] { "CLIENT", "TRACKINGINFO" })]
    [InlineData("client|getredir", new[] { "CLIENT", "GETREDIR" })]
    [InlineData("script|debug", new[] { "SCRIPT", "DEBUG", "SYNC" })]
    public async Task Service_CannotWatchKeysOrBlockTheServer(string command, string[] arguments)
    {
        await using var library = await ConnectionMultiplexer.ConnectAsync(redis.As("library-service"));

        var refusal = await Assert.ThrowsAsync<RedisServerException>(
            () => library.GetDatabase().ExecuteAsync(arguments[0], arguments[1..]));

        Assert.Contains($"no permissions to run the '{command}'", refusal.Message);
    }

    // FUNCTION FLUSH takes no key and empties the functions every user shares; Redis refuses it from a script anyway.
    [Fact]
    public async Task Service_CannotChangeSharedFunctions()
    {
        await using var library = await ConnectionMultiplexer.ConnectAsync(redis.As("library-service"));

        var refusal = await Assert.ThrowsAsync<RedisServerException>(() => library.GetDatabase().ExecuteAsync("FUNCTION", "FLUSH"));

        Assert.Contains("no permissions to run the 'function|flush'", refusal.Message);
    }

    // A rotation: the user holds the current password and the next while every instance moves to the next.
    [Theory]
    [InlineData("rotating-current-secret")]
    [InlineData("rotating-next-secret")]
    public async Task User_DuringARotation_AcceptsEitherPassword(string password)
    {
        await using var rotating = await ConnectionMultiplexer.ConnectAsync($"{redis.Endpoint},user=rotating-service,password={password}");

        Assert.True(await rotating.GetDatabase().StringSetAsync($"rotating-service:book:{Guid.NewGuid()}", "{}"));
    }

    /// <summary>
    /// A Redis whose only users are services', as provisioned in cloud.
    /// </summary>
    public sealed class AclRedis : IAsyncLifetime
    {
        private const int Port = 6379;

        private readonly IContainer _container = new ContainerBuilder()
            .WithImage("redis:7")
            .WithPortBinding(Port, true)
            .WithCommand(
            [
                "redis-server",
                "--user", "default", "off",
                // A user directive's rules are separate arguments on the command line.
                "--user", .. ServiceCacheAcl.User("shelf-service", "shelf-secret", reads: ["library-service"]).Split(' '),
                "--user", .. ServiceCacheAcl.User("library-service", "library-secret", reads: ["shelf-service"]).Split(' '),
                "--user", .. ServiceCacheAcl.User("rotating-service", ["rotating-current-secret", "rotating-next-secret"], reads: []).Split(' '),
            ])
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();

        /// <summary>
        /// A connection string authenticating as one service.
        /// </summary>
        public string As(string service) => $"{Endpoint},user={service},password={service.Split('-')[0]}-secret";

        /// <summary>
        /// The address alone.
        /// </summary>
        public string Endpoint => $"{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

        public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync() => await _container.DisposeAsync();
    }
}
