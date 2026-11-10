# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

## About

How an entry in a Bitwarden service's own cache is stored in Redis and keyed, shared by the service that fills it and
the clients that read it: `OwnedCacheEntry` (the key layout, the hash fields and the settings) and `OwnedAnswerKey`
(answers keyed by text such as an email address, stored under a digest and never the text).

Its one dependency is Microsoft.Extensions.Configuration.Abstractions. A service host fills entries and a client reads
them, each building keys with these types.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Resources.Caching.Abstractions
```

## Configuration

| Setting                                            | Read by                                                                 | Default | What it does                                                                                                                  |
| -------------------------------------------------- | ----------------------------------------------------------------------- | ------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `Caching:Owned:AnswerSecret`                       | This package (`OwnedAnswerKey.ReadSecret`)                              | None    | Base64, at least 32 bytes. Keys the digest of answers keyed by text with HMAC-SHA-256. Unset, the digest is plain SHA-256.    |
| `Caching:Owned:Redis:Configuration`                | This package (`OwnedAnswerKey.ReadSecret`), a service host and a client | None    | The service cache's Redis connection. Unset, there is no service cache: owners fill nothing and clients always ask the owner. |
| `Caching:Owned:Resources:{service}:{name}:Enabled` | A service host and a client                                             | `true`  | `false` stops the owner filling that resource, index or answer, and its clients reading it, so they ask the owner.            |

Outside the `Development` environment, a host with a service cache must set `Caching:Owned:AnswerSecret`, or it fails
to start. Every service and client sharing the cache must hold the same secret. See
[The answer secret](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/src/README.md#the-answer-secret)
for why, and what changing it does.

## Getting started

Key an answer by text the same way on both sides:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;

public sealed class AccountByEmailKey(IConfiguration configuration, string environmentName)
{
    // Read once, as the host starts: it throws outside Development when a service cache is configured and no secret is.
    private readonly byte[] _secret = OwnedAnswerKey.ReadSecret(configuration, environmentName);

    public string For(string email) =>
        OwnedCacheEntry.Key(
            OwnedAnswerKey.Template("identity-service", "account-by-email"),
            OwnedAnswerKey.Of("account-by-email", email.Trim().ToLowerInvariant(), _secret));
}
```

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/src/README.md)
for a complete program.

## Documentation

### An entry

One Redis hash per resource, under `{service}:{resource type}:{identifier}` (`OwnedCacheEntry.Template` and `Key`),
and per answer, under `{service}:answer:{name}:{subject}` (`OwnedAnswerKey.Template`). Only the owning service writes
under its own prefix.

| Field  | Holds                                                                      |
| ------ | -------------------------------------------------------------------------- |
| `v`    | The row's version when it was read, or when it changed                     |
| `o`    | The row's owner, as `{"organization":"..."}`; absent for a row nobody owns |
| `d`    | The resource as the service serves it, cached attributes only              |
| `none` | Nothing is recorded under the key                                          |
| `gone` | A tombstone: the row changed or was deleted at version `v`                 |

A reader checks the owner beside the data rather than trusting the payload. A tombstone is a miss to a reader and
refuses a fill of any older version, so a slow read can't put back a value a later change replaced.

### Types

| Type              | Purpose                                                                      |
| ----------------- | ---------------------------------------------------------------------------- |
| `OwnedCacheEntry` | How a service-owned cache entry is stored in Redis, and the settings it uses |
| `OwnedAnswerKey`  | How a service-owned answer is keyed, and the secret that keys its digest     |
