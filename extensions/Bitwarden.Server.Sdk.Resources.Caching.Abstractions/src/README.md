# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

**Audience:** engineers changing this package.

The key layout, hash fields and settings of a service-owned cache entry, shared by the service that fills it and the
clients that read it. See [PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Only what both the owner of a cache entry and its readers need. Nothing here connects to Redis or touches
`HttpContext`: filling entries is in Bitwarden.Server.Sdk.Resources.Caching and reading them in
Bitwarden.Server.Sdk.Client. The one dependency, Microsoft.Extensions.Configuration.Abstractions, is for `ReadSecret`.

## How it works

- **A text subject.** `OwnedAnswerKey.Of` digests the answer's name, a line feed and the key, as strict UTF-8, and
  takes the first 16 bytes as a big-endian GUID, so the subject's hexadecimal is the digest's. Strict encoding makes a
  lone surrogate throw rather than encode as U+FFFD and collide with text that holds U+FFFD. The line feed is why an
  answer's name may not contain one.
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

- **The layout stable.** Keys, fields and the digest are read by services deployed apart from each other. A change to
  any of them moves every entry.
- **No text in a key.** A key written as text is only ever stored as its digest.
- **Few dependencies.** Nothing that connects to Redis or touches `HttpContext`.
