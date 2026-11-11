# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

**Audience:** engineers writing a Bitwarden service that fills a shared cache of its answers, or a client that reads
one.

## About

The keys, hash fields and settings that let a service's callers read some of its answers straight from a shared Redis
instead of calling it, agreed by the owner and its callers without one referencing the other. Nothing here connects to
Redis. Its one dependency is Microsoft.Extensions.Configuration.Abstractions.

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

The keys are public constants: `OwnedAnswerKey.SecretKey`, `OwnedCacheEntry.ConnectionKey` and
`OwnedCacheEntry.ResourcesSection`.

An answer keyed by text is stored under a digest of that text. With no `Caching:Owned:AnswerSecret` the digest is
plain SHA-256, which anyone can compute: someone who can read the cache and guess an address can confirm it is there.
With the secret the digest is an HMAC that only holders of the secret can compute. Treat it like any other secret, and
generate one with `openssl rand -base64 32`.

## Key concepts

- **The owner fills, its callers read.** The service that owns the data fills entries under its own prefix,
  `OwnedCacheEntry.Prefix(service)`, and only it writes there. `OwnedCacheEntry` holds the key layout, the hash fields
  and the settings; `OwnedAnswerKey` keys answers by text.
- **An answer keyed by text is stored under a digest, never the text.** Its subject is a GUID computed from the text.
  Answers keyed by an identifier are stored under the identifier itself.
- **Each resource or answer is one Redis hash:**

| Field  | Holds                                                                      |
| ------ | -------------------------------------------------------------------------- |
| `v`    | The row's version when it was read, or when it changed                     |
| `o`    | The row's owner, as `{"organization":"..."}`; absent for a row nobody owns |
| `d`    | The resource as the service serves it, cached attributes only              |
| `none` | Nothing is recorded under the key                                          |
| `gone` | A tombstone: the row changed or was deleted at version `v`                 |

## Usage

### Key a cached resource by its identifier

`WidgetCacheKeys.cs`:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public static class WidgetCacheKeys
{
    public static string For(Guid widget) =>
        OwnedCacheEntry.Key(OwnedCacheEntry.Template("widget-service", "widget"), widget);
}
```

`WidgetCacheKeys.For(id)` is `widget-service:widget:{id}`. A service or resource type may not contain `:`, `{` or `}`, and the
resource types `answer`, `index`, `gen` and `idempotency` are reserved for the service's other keys; `Template` throws
`ArgumentException` for either.

### Key an answer by text

Owner and clients key the answer the same way, from the same normalized text. `AccountByEmailKey.cs`:

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

The key is `identity-service:answer:account-by-email:` followed by the subject, a GUID. The address appears nowhere in
it. An answer's name may not be empty or contain `:`, `{`, `}` or a line feed, and text that isn't valid UTF-16 (a lone
surrogate) is refused. Each throws an `ArgumentException` that doesn't quote the text.

## Gotchas and constraints

- **The secret is required outside `Development`.** `OwnedAnswerKey.ReadSecret(configuration, environmentName)` throws
  when the secret is unset, `Caching:Owned:Redis:Configuration` is set and the environment is not `Development`. A host
  calls it as it starts, so it fails to start. In `Development` a developer's machine needs no secret.
- **A malformed secret is refused.** One that is not base64 or is shorter than 32 bytes makes `ReadSecret` throw,
  without echoing the value, and the host fails to start.
- **Every service and client sharing the cache must hold the same secret.** One holding a different secret, or none,
  computes subjects that no owner fills. It never reads a wrong answer: it always misses and asks the owner, so a
  mismatch costs speed, not correctness.
- **Clients learn of the secret from their own configuration, not from the contract.** An owner publishes
  `"digest": "sha-256"` (`OwnedAnswerKey.Digest`) whether or not a secret keys it, because the contract is the same in
  every deployment and the secret is not.
- **Changing the secret moves every answer keyed by text.** The cache misses until owners fill the new subjects, and
  entries under the old ones expire on their own.
- **Check the owner beside the data.** A reader checks `o` itself and never trusts the payload for whose row it is.
- **A tombstone is a miss.** It also refuses a fill of any older version, so a slow read can't put back a value a later
  change replaced.
- **What an entry holds is the owner's choice.** This package names the fields, not what goes in `d`. An owner fills an
  entry only with attributes every reader of the cache may see: it is Data at Rest in Redis, readable by anyone with
  read access to the owner's prefix.
- **A digest is not encryption.** Even keyed, it only keeps the text out of the cache. Anyone holding the secret and
  read access can still confirm a guess.

## Types

| Type              | Purpose                                                                      |
| ----------------- | ---------------------------------------------------------------------------- |
| `OwnedCacheEntry` | How a service-owned cache entry is stored in Redis, and the settings it uses |
| `OwnedAnswerKey`  | How a service-owned answer is keyed, and the secret that keys its digest     |
