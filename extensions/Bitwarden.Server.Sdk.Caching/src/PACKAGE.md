# Bitwarden.Server.Sdk.Caching

## About

This package enables the use of [`IFusionCache`](https://github.com/ZiggyCreatures/FusionCache) that
is setup for the best experience for the current execution environment. This library is designed to
work well for Bitwarden services running on a Raspberry Pi all the way to a multi-node cloud setup
and everything in between.

## How to use

```c#
public class MyService([FromKeyed("MyFeature")]IFusionCache cache, IDatabase database)
{
    async Task<Thing> GetAsync(string id)
    {
        // TODO: Check myself that this is the valid syntax
        return cache.GetOrCreateAsync(id, () => database.MoreExpensiveOperationAsync(id));
    }
}
```

With no additional setup, you can inject a named `IFusionCache` via keyed services.

### Configuration

A lot of

```json
{
  "Caching": {
    "Redis": {
      "ConnectionString": ""
    },
    "Uses": {
      "MyFeature": {
        "Fusion": {
          "DefaultEntryOptions": {
            "SkipDistributedCacheRead": false,
            "SkipDistributedCacheWrite": true,
          }
        }
      }
    }
  }
}
```

## Should I use caching
