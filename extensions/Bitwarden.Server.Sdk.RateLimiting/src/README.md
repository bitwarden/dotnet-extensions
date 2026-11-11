# Bitwarden.Server.Sdk.RateLimiting

Per-address request limits for Bitwarden services, counted in the process or in a shared Redis. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Limits counted per client address, from the `IpRateLimitOptions` and `globalSettings:distributedIpRateLimiting`
settings operators already have, and the counting store a service can reuse for limits of its own. Keep the settings'
shape: renaming or moving one breaks existing configuration. Working out the client's address from a proxy belongs to
Bitwarden.Server.Sdk.Authorization, which runs first; this package only reads `HttpContext.Connection.RemoteIpAddress`.

## How it works

| File                                           | Purpose                                                                                      |
| ---------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `IpRateLimiting.cs`                            | Reads the settings once, skips whitelisted addresses and endpoints, reads the grant, refuses |
| `IpRateLimitRules.cs`                          | Chooses the rules that count a request, their order and their buckets                        |
| `IpRateLimitMiddleware.cs`                     | Calls `IpRateLimiting` and short-circuits the pipeline on a refusal                          |
| `MemoryRateLimitCounters.cs`                   | Fixed windows in a `ConcurrentDictionary`, swept at most once a minute                       |
| `RedisRateLimitCounters.cs`                    | Fixed windows in Redis, with the failure threshold                                           |
| `PlatformIpRateLimitRefusal.cs`                | The default refusal body, the error document with code `too-many-requests`                   |
| `IpRateLimitingServiceCollectionExtensions.cs` | Registers everything with `TryAdd`, choosing Redis or memory                                 |

- **Buckets.** A rule's bucket joins its pattern, period, limit and sorted grants, so two rules differing only in their
  limit or grants never share a window. With endpoint limiting the method and normalized path follow, then the address.
  Redis keys are `{service}:ip-rate-limit:` and the bucket's SHA-256, where the service is `IServiceIdentity.Name` or
  else the host's application name, so two services with a rule for the same route don't count each other's requests.
- **One Redis step.** A Lua script increments the key and sets the window's expiry when the key has none, so a window
  can't be left without one.
- **Normalizing the path.** Routing serves `/connect/token/` and `//connect/token` as `/connect/token`, so rules match
  the normalized path; they also match the path as sent, so normalizing never takes a request out of a rule.
- **Reading the grant.** Only when a matching rule names grants, and only from a form: the body is buffered and rewound
  so the endpoint reads the same form. A form the server won't read names no grant. A repeated `grant_type` names none,
  rather than the joined values.
- **Startup checks.** `IpRateLimiting` is a singleton the middleware takes in its constructor, so the settings it parses
  (periods, whitelist networks, the Redis connection string) fail as the pipeline is built.
- **Not AOT-compatible.** The refusal writes the error document with the host's own JSON options, by reflection, so
  the project sets `IsAotCompatible` to false rather than suppressing the warnings.

A change must keep:

- **Nothing a client writes chooses its address.** `IpRateLimiting.TryTakeAsync` reads only
  `Connection.RemoteIpAddress`.
- **No address in clear in a shared store.**
- **An outage never refuses.** `RedisRateLimitCounters.TryTakeAsync` returns true when Redis fails.

## Testing

Every type with logic has a test class of the same name in `tests/Bitwarden.Server.Sdk.RateLimiting.Tests`. The Redis
counters' tests start a Redis container, so they need Docker.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.RateLimiting/tests/Bitwarden.Server.Sdk.RateLimiting.Tests
```
