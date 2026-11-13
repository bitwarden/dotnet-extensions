# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

**Audience:** engineers changing this package.

The key layout, hash fields and settings of a service-owned cache entry, shared by the service that fills it and the
clients that read it. See [PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Only what both the owner of a cache entry and its readers need. Nothing here connects to Redis or touches
`HttpContext`: filling entries is in Bitwarden.Server.Sdk.Resources.Caching and reading them in
Bitwarden.Server.Sdk.Client. Microsoft.Extensions.Configuration.Abstractions is for `ReadSecret`, and Bitwarden.Server.Sdk.CryptoPrimitives
derives text subjects.

## How it works

- **A text subject.** `OwnedAnswerKey.Of` derives `CacheKey.DeriveCacheKey(secret, answer, key)` and takes the first
  16 bytes as a big-endian GUID, so the subject's hexadecimal is the derived key's. The derivation and its encoding
  (length-prefixed strict UTF-8) belong to Bitwarden.Server.Sdk.CryptoPrimitives, owned by Key Management; this package
  only chooses the parameters and their order.
- **Names that can't be confused.** `OwnedCacheEntry.ThrowIfNotAName` and `OwnedCacheEntry.ReservedResourceTypes`
  enforce the naming rules in PACKAGE.md, so no two names give the same key and no resource mixes with the service's
  other keys in a scan.
- **Development** is compared as `IHostEnvironment.IsDevelopment` compares it, without referencing hosting.

## Testing

Every source file has a matching test class in `tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests
```

A change must keep:

- **The layout stable.** Keys, fields and the derivation are read by services deployed apart from each other. A change to
  any of them moves every entry.
- **No text in a key.** A key written as text is only ever stored as its digest.
- **Few dependencies.** Nothing that connects to Redis or touches `HttpContext`.
