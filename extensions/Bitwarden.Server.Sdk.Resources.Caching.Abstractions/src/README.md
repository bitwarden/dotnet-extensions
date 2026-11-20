# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

**Audience:** engineers changing this package.

The key layout, hash fields and settings of a service-owned cache entry, shared by the service that fills it and the
clients that read it. See [PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Only what both the owner of a cache entry and its readers need. Nothing here connects to Redis or touches
`HttpContext`: filling and reading entries belong in the packages that host a service and call one.

`CacheSecretSetting` is here too: the one rule every secret that derives keys with `CacheKey` is read by, those of the
idempotency keys, the shared rate-limit counts and a service's own keyed rows. It lives here so those readers share one
rule through a small package that connects to nothing; it alone needs Microsoft.Extensions.Configuration.Abstractions
and Bitwarden.Server.Sdk.CryptoPrimitives.

## How it works

- **Names that can't be confused.** `OwnedCacheEntry.ThrowIfNotAName` and `OwnedCacheEntry.ReservedResourceTypes`
  enforce the naming rules in PACKAGE.md, so no two names give the same key and no resource mixes with the service's
  other keys in a scan.
- **Reading a secret.** `CacheSecretSetting.Read` requires a secret outside `Development` (a caller that needs one only
  sometimes reads it only then, as the rate limiter does when counts are shared in Redis), refuses
  `CacheSecret.Development` where one is required, and names the setting in every refusal, never its value or an inner
  exception that might quote it.
- **Development** is compared as `IHostEnvironment.IsDevelopment` compares it, without referencing hosting.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests
```

A change must keep:

- **The layout stable.** Keys and fields are read by services deployed apart from each other. A change to
  any of them moves every entry.
- **Few dependencies.** Nothing that connects to Redis or touches `HttpContext`.
