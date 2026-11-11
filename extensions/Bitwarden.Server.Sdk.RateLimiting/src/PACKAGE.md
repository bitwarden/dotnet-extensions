# Bitwarden.Server.Sdk.RateLimiting

## About

Per-address request limits for Bitwarden services, from rules in the `IpRateLimitOptions` settings, counted in the
process or in a Redis every instance of a service shares. A request over a limit is refused before anything else
answers it, with `429`, a `Retry-After` header and the standard error document. Limits are off on a self-hosted
install.

Bitwarden.Server.Sdk.Service registers and orders it for every service. It depends on ASP.NET Core,
Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors and StackExchange.Redis.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.RateLimiting
```

## Configuration

| Setting                                                              | Default | What it does                                                                                                           |
| -------------------------------------------------------------------- | ------- | ---------------------------------------------------------------------------------------------------------------------- |
| `IpRateLimitOptions:Enabled`                                         | `true`  | Whether limits apply. Off when self-hosted, and with no rules, whatever this says.                                     |
| `IpRateLimitOptions:GeneralRules`                                    | None    | The rules: `Endpoint` (default `*`), `Period` (default `1m`), `Limit` (default `0`, refusing every request), `Grants`. |
| `IpRateLimitOptions:EnableEndpointRateLimiting`                      | `true`  | Counts each method and path apart. When false, only `*` rules apply, counting all of an address's requests.            |
| `IpRateLimitOptions:IpWhitelist`                                     | None    | Addresses and CIDR networks never limited.                                                                             |
| `IpRateLimitOptions:EndpointWhitelist`                               | None    | `verb:path` patterns never limited.                                                                                    |
| `IpRateLimitOptions:HttpStatusCode`                                  | `429`   | The status a request over a limit is refused with.                                                                     |
| `globalSettings:distributedIpRateLimiting:redisConnectionString`     | None    | A Redis where every instance keeps one shared count. Unset, each process counts on its own.                            |
| `globalSettings:distributedIpRateLimiting:enabled`                   | `true`  | Whether a configured Redis is used.                                                                                    |
| `globalSettings:distributedIpRateLimiting:maxRedisTimeoutsThreshold` | `10`    | How many Redis failures within the window set Redis aside, letting requests through unlimited.                         |
| `globalSettings:distributedIpRateLimiting:slidingWindowSeconds`      | `120`   | The window, in seconds, those failures are counted in.                                                                 |
| `globalSettings:selfHosted`                                          | `false` | When true, there are no limits.                                                                                        |

A shared Redis holds each address as an unsalted hash, which anyone who can read it can reverse for IPv4: protect it
as you would the logs. When Redis fails, requests go through unlimited. See
[Network and request limits](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/network-and-request-limits.md)
for the rules' matching and these trade-offs.

## Getting started

A host that composes the package itself, rather than building on Bitwarden.Server.Sdk.Service, registers it and adds
its middleware after CORS and before authentication:

```csharp
using Bitwarden.Server.Sdk.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIpRateLimiting();

var app = builder.Build();

app.UseIpRateLimiting();

app.MapGet("/api/v1/widgets", () => "widgets");

app.Run();
```

With the rule `{ "Endpoint": "get:/api/v1/widgets", "Period": "10s", "Limit": 2 }` in `IpRateLimitOptions:GeneralRules`,
a third request within ten seconds is refused `429`. See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.RateLimiting/src/README.md)
for the whole setup.

## Documentation

- **Matching.** `Endpoint` is `*` or `verb:path`, with `*` for any run of characters and `?` for any one, matched
  without case against the path in lower case, without a trailing slash and with repeated slashes collapsed.
- **Counting.** Windows are fixed. Of the rules a request matches, the lowest limit for each period counts, shortest
  period first, and counting stops at the first over its limit.
- **Grants.** A rule with `Grants` counts a token request only when its form names exactly one `grant_type` and it is
  one of them.
- **The client's address.** Behind a proxy, take it from a trusted proxy first, with Bitwarden.Server.Sdk.Authorization's
  `UseForwardedClientAddress()`.
- **Bad settings.** A `Period` that doesn't parse, or an `IpWhitelist` entry that is neither an address nor a network,
  stops the host from starting.

| Type                                                                    | Purpose                                                                     |
| ----------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| `IpRateLimitOptions`, `IpRateLimitRule`                                 | The `IpRateLimitOptions` settings and one rule                              |
| `DistributedIpRateLimitingOptions`                                      | The `globalSettings:distributedIpRateLimiting` settings                     |
| `IIpRateLimitRefusal`                                                   | Writes a refusal's body, for a route whose answer shape somebody else fixed |
| `IRateLimitCounters`, `MemoryRateLimitCounters`                         | Where counts are kept, for limits of your own too                           |
| `IpRateLimitRules`, `IpRateLimitCount`                                  | Which rules count a request, in which order and bucket                      |
| `IpRateLimitingServiceCollectionExtensions`, `IpRateLimitingExtensions` | `AddIpRateLimiting()` and `UseIpRateLimiting()`                             |
