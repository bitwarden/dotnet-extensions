# Bitwarden.Server.Sdk.Caching

## About

This package enables the use of [`IFusionCache`](https://github.com/ZiggyCreatures/FusionCache) that
is setup for the best experience for the current execution environment. This library is designed to
work well for Bitwarden services running on a Raspberry Pi all the way to a multi-node cloud setup
and everything in between. This library is implements the decision outcome of
[this Architectural Desicion Record](https://contributing.bitwarden.com/architecture/adr/adopt-fusion-cache).

## How to use

```c#
public class MyService([FromKeyed("MyFeature")]IFusionCache cache, IDatabase database)
{
    public async Task<Thing> GetAsync(string id)
    {
        return await cache.GetOrSetAsync(id, () => database.MoreExpensiveOperationAsync(id));
    }
}
```

With no additional setup, you can inject a named `IFusionCache` via keyed services. The
`AddBitwardenCaching` contains the core docs for how to use this package.
