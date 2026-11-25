using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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

    // Outside Development, records go only to a Redis user of the service's own: as Redis's default user, any holder of
    // the connection could claim the service's keys or overwrite its records, and so refuse its writes.
    [Theory]
    [InlineData("127.0.0.1:1")]
    [InlineData("127.0.0.1:1,password=secret-value")]
    [InlineData("127.0.0.1:1,user=default,password=secret-value")]
    public void Start_WithRedisButNoUserOfItsOwn_OutsideDevelopment_Refused(string connection)
    {
        using var provider = Build(new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = connection }, environment: "Production");

        var refusal = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(IdempotencyOptions.RedisConfigurationKey, refusal.Message);
        Assert.DoesNotContain("secret-value", refusal.Message);
        Assert.DoesNotContain("127.0.0.1", refusal.Message);
    }

    [Theory]
    [InlineData("Development", "127.0.0.1:1")]
    [InlineData("Production", "127.0.0.1:1,user=widget-service,password=secret-value")]
    [InlineData("Production", null)]
    public void Start_WithAUserOfItsOwnOrInDevelopmentOrWithoutRedis_Starts(string environment, string? connection)
    {
        using var provider = Build(new Dictionary<string, string?> { [IdempotencyOptions.RedisConfigurationKey] = connection }, environment: environment);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, bool sharedKeyRing = false, string environment = "Development")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
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

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "widget-service";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
