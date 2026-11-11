using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bitwarden.Server.Sdk.Idempotency;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Idempotency;

/// <summary>
/// Which store a service gets, and what a write does when its store can't answer: it runs, as it would without a key.
/// The in-process store itself is tested in Bitwarden.Server.Sdk.Idempotency's own tests.
/// </summary>
public sealed class IdempotencyStoreTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A store that can't answer never stops a write: it runs, as it would without a key.
    [Fact]
    public async Task AStoreThatCannotAnswer_LetsTheWriteRun()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            services: services => services.AddSingleton<IIdempotencyStore>(new Unavailable()));
        var counter = service.Resolve<IdempotencyProbeCounter>();

        using var first = await SendAsync(service, "key-1");
        using var second = await SendAsync(service, "key-1");

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(2, counter.Count);
    }

    // A record this instance can't open (another key ring) is taken as running, never run past: running it could do
    // the write twice.
    [Fact]
    public async Task ARecordThatCannotBeOpened_IsRefusedAsRunning()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            services: services => services.AddSingleton<IIdempotencyStore>(new Holding("sealed-by-another-key-ring")));
        var counter = service.Resolve<IdempotencyProbeCounter>();

        using var refused = await SendAsync(service, "key-1");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.NotNull(refused.Headers.RetryAfter);
        Assert.Equal(0, counter.Count);
    }

    // Redis only with a key ring every instance reads: otherwise one instance's record would be
    // unopenable at the next, so records stay in the process.
    [Fact]
    public async Task TheRedisStore_WithoutASharedKeyRing_IsNotUsed()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            configuration: new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = "127.0.0.1:1" });

        Assert.IsType<MemoryIdempotencyStore>(service.Resolve<IIdempotencyStore>());
    }

    [Fact]
    public async Task TheRedisStore_WithASharedKeyRing_IsUsed()
    {
        var keys = Directory.CreateTempSubdirectory("idempotency-keys-");
        try
        {
            await using var service = await SessionHost.StartAsync(
                productService: false,
                configuration: new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = "127.0.0.1:1" },
                services: services => services.AddDataProtection().PersistKeysToFileSystem(keys));

            Assert.IsType<RedisIdempotencyStore>(service.Resolve<IIdempotencyStore>());
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    // The service-owned cache's setting is the cache's own: idempotency records follow Idempotency:Redis alone.
    [Fact]
    public async Task TheServiceCacheSetting_IsNotRead()
    {
        var keys = Directory.CreateTempSubdirectory("idempotency-keys-");
        try
        {
            await using var service = await SessionHost.StartAsync(
                productService: false,
                configuration: new Dictionary<string, string?> { ["Caching:Owned:Redis:Configuration"] = "127.0.0.1:1" },
                services: services => services.AddDataProtection().PersistKeysToFileSystem(keys));

            Assert.IsType<MemoryIdempotencyStore>(service.Resolve<IIdempotencyStore>());
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    // Idempotency:Enabled false: keys are not honoured.
    [Fact]
    public async Task TurnedOff_KeysAreIgnored()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            configuration: new Dictionary<string, string?> { ["Idempotency:Enabled"] = "false" });
        var counter = service.Resolve<IdempotencyProbeCounter>();

        using var first = await SendAsync(service, "key-1");
        using var second = await SendAsync(service, "key-1");

        Assert.Equal(2, counter.Count);
    }

    // A body past the limit is run without a key rather than fingerprinted.
    [Fact]
    public async Task ABodyPastTheLimit_RunsWithoutAKey()
    {
        await using var service = await SessionHost.StartAsync(
            productService: false,
            configuration: new Dictionary<string, string?> { ["Idempotency:MaximumBytes"] = "10" });
        var counter = service.Resolve<IdempotencyProbeCounter>();

        using var first = await SendAsync(service, "key-1", "a note longer than ten bytes");
        using var second = await SendAsync(service, "key-1", "a note longer than ten bytes");

        Assert.Equal(2, counter.Count);
    }

    private static async Task<HttpResponseMessage> SendAsync(SessionHost service, string key, string note = "a")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/idempotency-probes")
        {
            Content = JsonContent.Create(new IdempotencyProbeNote(note)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", SessionHost.Token(user: null, SessionHost.Scope, SessionHost.WriteScope));
        request.Headers.TryAddWithoutValidation(IdempotencyKeys.Header, key);

        return await service.Client().SendAsync(request, Token);
    }

    private sealed class Unavailable : IIdempotencyStore
    {
        public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease) => ValueTask.FromResult(Claim.Unavailable);

        public ValueTask CompleteAsync(string key, string answer, TimeSpan retention) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string key, string pending) => ValueTask.CompletedTask;
    }

    private sealed class Holding(string existing) : IIdempotencyStore
    {
        public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease) => ValueTask.FromResult(Claim.Found(existing));

        public ValueTask CompleteAsync(string key, string answer, TimeSpan retention) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string key, string pending) => ValueTask.CompletedTask;
    }
}
