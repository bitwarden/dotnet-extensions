# Bitwarden.Server.Sdk.Caching

## About

This package provides a pre-configured [`IFusionCache`](https://github.com/ZiggyCreatures/FusionCache)
implementation for quick setup with extensive customization options. This library
implements the decision outcome of
[this Architectural Decision Record](https://contributing.bitwarden.com/architecture/adr/adopt-fusion-cache).

## Usage

```c#
public class MyService([FromKeyedServices("MyFeature")] IFusionCache cache, IDatabase database)
{
    public async Task<Thing> GetAsync(string id)
    {
        return await cache.GetOrSetAsync(id, () => database.MoreExpensiveOperationAsync(id));
    }
}
```

Inject a named `IFusionCache` instance using keyed services. No additional setup is required beyond
registering caching in your host. See `AddBitwardenCaching` for complete customization documentation.

## Deployment Modes

The package supports three deployment modes, determined at runtime by which services are registered:

### Memory + Redis (recommended for cloud/multi-instance)

When a Redis connection string is configured, each named cache gets its own `FusionCache` instance
backed by Redis as L2 and a Redis backplane for cross-instance invalidation.

```csharp
services.AddBitwardenCaching();
// Configure Redis via appsettings:
// Caching:Redis:Configuration = "redis.example.com:6379,ssl=true,password=..."
```

### Memory + Custom Distributed Cache

When no Redis is configured but a non-keyed `IDistributedCache` is registered, it is used as L2.
This path covers SQL Server or EF cache in self-hosted deployments, and supports the persistent
Cosmos DB keyed cache pattern (see [Pairing with Cosmos DB](#pairing-with-cosmos-db) below). No
backplane is available without Redis — L1 entries on other instances expire on their own TTL.

```csharp
// SQL Server cache (self-hosted):
services.AddDistributedSqlServerCache(options => { ... });
services.AddBitwardenCaching();
```

### Memory-only (development, testing, or single-instance)

To use memory-only caching — with all of FusionCache's stampede protection, fail-safe, and eager
refresh benefits but no distributed layer — register an in-process `IDistributedCache`:

```csharp
// Memory-backed IDistributedCache: no Redis, no network, no shared state across instances.
services.AddDistributedMemoryCache();
services.AddBitwardenCaching();
```

> **Note**: `AddBitwardenCaching` requires a non-keyed `IDistributedCache` or Redis configuration.
> Without either, resolving an `IFusionCache` instance throws `InvalidOperationException`. This is
> intentional: misconfiguration surfaces at startup rather than silently degrading to process-local
> caching in a multi-instance environment.

## Customization

### FusionCacheOptions

The most common customization point is `FusionCacheOptions`. Use named options to configure a
specific cache:

```c#
services.Configure<FusionCacheOptions>("MyFeature", options =>
{
    options.DefaultEntryOptions.Duration = TimeSpan.FromSeconds(10);
    options.DefaultEntryOptions.IsFailSafeEnabled = true;
    options.DefaultEntryOptions.FailSafeMaxDuration = TimeSpan.FromHours(1);
    options.DefaultEntryOptions.EagerRefreshThreshold = 0.9f; // Refresh at 90% of TTL
});
```

Or use `ConfigureAll` to apply defaults to every named cache (useful for migrating from a legacy
settings object):

```csharp
services.ConfigureAll<FusionCacheOptions>(opts =>
{
    opts.DefaultEntryOptions.Duration = legacySettings.CacheDuration;
    opts.DefaultEntryOptions.IsFailSafeEnabled = legacySettings.IsFailSafeEnabled;
});
```

### IDistributedCache

Override the distributed cache backend for a specific named cache:

```c#
services.TryAddKeyedSingleton<IDistributedCache, MyCustomCache>("MyFeature");
```

By default, `IDistributedCache` uses Redis when configured, falling back to a non-keyed
`IDistributedCache` registration. User-registered keyed instances take precedence over both.

#### Pairing with Cosmos DB

The server registers a `"persistent"` keyed `IDistributedCache` backed by Cosmos DB for
long-lived data. To use it as L2 under `IFusionCache`, alias it under your cache name before
calling `AddBitwardenCaching`:

```csharp
// Register the persistent (Cosmos) keyed cache first:
services.AddDistributedCache(globalSettings);

// Alias it under your cache name so AddBitwardenCaching picks it up:
services.TryAddKeyedSingleton("MyLongLivedCache",
    (sp, _) => sp.GetRequiredKeyedService<IDistributedCache>("persistent"));

services.AddBitwardenCaching();
services.Configure<FusionCacheOptions>("MyLongLivedCache", options =>
{
    options.DefaultEntryOptions.Duration = TimeSpan.FromHours(24);
});
```

Note: Cosmos does not support a backplane. L1 entries on other instances are not invalidated on
write — they expire on their own TTL.

### MemoryCacheOptions

Customize the per-cache `IMemoryCache` using named options:

```csharp
services.Configure<MemoryCacheOptions>("MyFeature", options =>
{
    options.TrackStatistics = true;
});
```

## Configuration Schema

All settings support a global default (applied to every cache) and per-cache overrides. Per-cache
values take precedence over the global default.

```json
{
  "Caching": {
    "Redis": {
      "Configuration": "redis.example.com:6379,ssl=true,password=...",
      "InstanceName": "optional-key-prefix:"
    },
    "Fusion": {
      "DistributedCacheCircuitBreakerDuration": "00:00:05",
      "DefaultEntryOptions": {
        "Duration": "00:05:00",
        "IsFailSafeEnabled": true,
        "FailSafeMaxDuration": "01:00:00",
        "FailSafeThrottleDuration": "00:00:30",
        "EagerRefreshThreshold": "0.9",
        "FactorySoftTimeout": "00:00:01",
        "FactoryHardTimeout": "00:00:05",
        "DistributedCacheSoftTimeout": "00:00:02",
        "DistributedCacheHardTimeout": "00:00:10",
        "AllowBackgroundDistributedCacheOperations": true,
        "JitterMaxDuration": "00:00:05"
      }
    },
    "Uses": {
      "MyFeature": {
        "Redis": {
          "Configuration": "override-redis:6379",
          "InstanceName": "myfeature:"
        },
        "Fusion": {
          "DefaultEntryOptions": {
            "Duration": "00:10:00",
            "IsFailSafeEnabled": false,
            "EagerRefreshThreshold": null
          }
        }
      }
    }
  }
}
```

Set `EagerRefreshThreshold` to `null` (or omit it) to disable background eager refresh for a
specific cache.

## Testing

For unit tests, register an in-process `IDistributedCache` — no Redis container needed:

```csharp
[Fact]
public async Task CacheHit_ReturnsCachedValue()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    services.AddDistributedMemoryCache();  // memory-only, no Redis required
    services.AddBitwardenCaching();

    var provider = services.BuildServiceProvider();
    var cache = provider.GetRequiredKeyedService<IFusionCache>("MyFeature");

    await cache.SetAsync("key", "value");
    var result = await cache.GetOrDefaultAsync<string>("key");

    Assert.Equal("value", result);
}
```

For integration tests that exercise the Redis backplane, use Testcontainers to spin up a real
Redis instance (see the package's own test suite for an example using `RedisFixture`).
