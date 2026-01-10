# Bitwarden.Server.Sdk.Caching

## About

This package provides a pre-configured [`IFusionCache`](https://github.com/ZiggyCreatures/FusionCache)
implementation for quick setup with extensive customization options. This library
implements the decision outcome of
[this Architectural Decision Record](https://contributing.bitwarden.com/architecture/adr/adopt-fusion-cache).

## Usage

```c#
public class MyService([FromKeyed("MyFeature")]IFusionCache cache, IDatabase database)
{
    public async Task<Thing> GetAsync(string id)
    {
        return await cache.GetOrSetAsync(id, () => database.MoreExpensiveOperationAsync(id));
    }
}
```

Inject a named `IFusionCache` instance using keyed services. No additional setup is required. See
`AddBitwardenCaching` for complete customization documentation.

## Customization

### FusionCacheOptions

The most common customization point for libraries is `FusionCacheOptions`. Customize using named
options:

```c#
services.Configure<FusionCacheOptions>("MyFeature", options =>
{
    options.DefaultEntryOptions.Duration = TimeSpan.FromSeconds(10);
});
```

### IDistributedCache

Customize the backend used for `IDistributedCache`:

```c#
services.TryAddKeyedSingleton<IDistributedCache, MyCustomCache>("MyFeature");
```

By default, `IDistributedCache` uses Redis when configured, falling back to a non-keyed
`IDistributedCache` registration. Hosts may provide alternate globally configured instances. For
example, a host may register a keyed `IDistributedCache` with key `"persistent"` that uses CosmosDB
instead of Redis. Use that cache for your feature:

```c#
services.TryAddKeyedSingleton("MyFeature",
    (s, _) => s.GetRequiredKeyedService<IDistributedCache>("persistent")
);
```
