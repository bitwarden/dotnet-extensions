using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Idempotency.Tests;

/// <summary>
/// Records are kept in Redis only when it is configured and every instance can open what another sealed; otherwise in
/// this process.
/// </summary>
public sealed class IdempotencyServiceCollectionExtensionsTests
{
    // An address nothing listens on: the Redis store connects on first use, never at registration.
    private const string Redis = "127.0.0.1:1";

    [Fact]
    public void WithoutARedisSetting_KeepsRecordsInMemory()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.IsType<MemoryIdempotencyStore>(provider.GetRequiredService<IIdempotencyStore>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WithABlankRedisSetting_KeepsRecordsInMemory(string connection)
    {
        using var provider = Build(new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = connection }, sharedKeyRing: true);

        Assert.IsType<MemoryIdempotencyStore>(provider.GetRequiredService<IIdempotencyStore>());
    }

    // Without a key ring every instance reads, one instance's record would be unopenable at the next, so records stay
    // in the process that can open them.
    [Fact]
    public void WithARedisSettingButNoPersistedKeyRing_KeepsRecordsInMemory()
    {
        using var provider = Build(new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = Redis });

        Assert.IsType<MemoryIdempotencyStore>(provider.GetRequiredService<IIdempotencyStore>());
    }

    [Fact]
    public void WithARedisSettingAndAPersistedKeyRing_KeepsRecordsInRedis()
    {
        using var provider = Build(new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = Redis }, sharedKeyRing: true);

        Assert.IsType<RedisIdempotencyStore>(provider.GetRequiredService<IIdempotencyStore>());
    }

    [Fact]
    public void Options_AreBoundFromTheIdempotencySection()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Idempotency:Retention"] = "06:00:00",
            ["Idempotency:Lease"] = "00:00:30",
            ["Idempotency:MaximumBytes"] = "2048",
            ["Idempotency:Enabled"] = "false",
        });

        var options = provider.GetRequiredService<IOptionsMonitor<IdempotencyOptions>>().CurrentValue;

        Assert.Equal(TimeSpan.FromHours(6), options.Retention);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Lease);
        Assert.Equal(2048, options.MaximumBytes);
        Assert.False(options.Enabled);
    }

    // The middleware seals records with data protection, so registering the package registers it.
    [Fact]
    public void DataProtection_IsRegistered()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.NotNull(provider.GetService<IDataProtectionProvider>());
    }

    [Fact]
    public void AServicesOwnStore_IsKept()
    {
        var own = new OwnStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(own);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = Redis })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddIdempotencyKeys(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Same(own, provider.GetRequiredService<IIdempotencyStore>());
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, bool sharedKeyRing = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddIdempotencyKeys(configuration);

        if (sharedKeyRing)
        {
            // Never written to: the store is chosen by whether a repository is set, and no key is made here.
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "widget-service-keys")));
        }

        return services.BuildServiceProvider();
    }

    private sealed class OwnStore : IIdempotencyStore
    {
        public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease) => ValueTask.FromResult(Claim.Unavailable);

        public ValueTask CompleteAsync(string key, string answer, TimeSpan retention) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(string key, string pending) => ValueTask.CompletedTask;
    }
}
