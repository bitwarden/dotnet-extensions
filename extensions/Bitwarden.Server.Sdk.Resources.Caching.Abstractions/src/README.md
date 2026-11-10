# Bitwarden.Server.Sdk.Resources.Caching.Abstractions

How an entry in a Bitwarden service's own cache is stored and keyed, so the service that fills it and the clients that
read it agree.

A service can let its callers read some of its answers straight from a shared Redis instead of calling it. The service
that owns the data fills the entries, under its own prefix, and its callers read them. Both sides must agree on every
key, field and setting without one referencing the other, so those live here.

- **One entry layout.** `OwnedCacheEntry` names the Redis hash fields (`v` the version, `o` the owner, `d` the data,
  `none`, and `gone` for a tombstone), the key layout `{service}:{resource type}:{identifier}`, and the settings both
  sides read.
- **Answers keyed by text.** `OwnedAnswerKey` turns text such as an email address into a subject that never holds the
  text, by a digest both sides compute alike.
- **A secret where it matters.** `OwnedAnswerKey.ReadSecret` reads the secret that keys that digest, and refuses to go
  without one outside `Development` when a service cache is configured.

## What belongs here

Only what both the owner of a cache entry and its readers need. Nothing here connects to Redis or touches
`HttpContext`: a service host fills entries under its own prefix and a client reads them, each building keys with these
types. Its one dependency is Microsoft.Extensions.Configuration.Abstractions, for `ReadSecret`.

## Getting started

```bash
dotnet new console -n AnswerKeys
cd AnswerKeys
dotnet add package Bitwarden.Server.Sdk.Resources.Caching.Abstractions --prerelease
dotnet add package Microsoft.Extensions.Configuration.EnvironmentVariables
```

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

// Throws outside Development when a service cache is configured and no secret is.
var secret = OwnedAnswerKey.ReadSecret(configuration, environment);

// The owner and every client normalize the text the same way before keying it.
var subject = OwnedAnswerKey.Of("account-by-email", "Alice@Example.com".Trim().ToLowerInvariant(), secret);
var key = OwnedCacheEntry.Key(OwnedAnswerKey.Template("identity-service", "account-by-email"), subject);

Console.WriteLine(key);
```

Run it with a secret, and it prints the key the answer is stored under, `identity-service:answer:account-by-email:`
followed by the subject. The address appears nowhere in it:

```bash
Caching__Owned__AnswerSecret=$(openssl rand -base64 32) dotnet run
```

## Examples

### Key a cached resource by its identifier

A resource is stored under its identifier, as written:

```csharp
using Bitwarden.Server.Sdk.Resources.Caching;

public static class WidgetCacheKeys
{
    public static string For(Guid widget) =>
        OwnedCacheEntry.Key(OwnedCacheEntry.Template("widget-service", "widget"), widget);
}
```

`WidgetCacheKeys.For(id)` is `widget-service:widget:{id}`. Only the owning service writes under
`OwnedCacheEntry.Prefix("widget-service")`.

## Configuration

| Setting                                            | Read by                                                                 | Default | What it does                                                                                                                  |
| -------------------------------------------------- | ----------------------------------------------------------------------- | ------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `Caching:Owned:AnswerSecret`                       | This package (`OwnedAnswerKey.ReadSecret`)                              | None    | Base64, at least 32 bytes. Keys the digest of answers keyed by text with HMAC-SHA-256. Unset, the digest is plain SHA-256.    |
| `Caching:Owned:Redis:Configuration`                | This package (`OwnedAnswerKey.ReadSecret`), a service host and a client | None    | The service cache's Redis connection. Unset, there is no service cache: owners fill nothing and clients always ask the owner. |
| `Caching:Owned:Resources:{service}:{name}:Enabled` | A service host and a client                                             | `true`  | `false` stops the owner filling that resource, index or answer, and its clients reading it, so they ask the owner.            |

The keys are public constants: `OwnedAnswerKey.SecretKey`, `OwnedCacheEntry.ConnectionKey` and
`OwnedCacheEntry.ResourcesSection`.

### The answer secret

An answer keyed by text, such as an email address, is stored under a digest of that text, never the text itself. With
no secret the digest is plain SHA-256, which anyone can compute. Someone who can read the cache and guess an address
can then confirm it is there.

So outside the `Development` environment, a host with a service cache requires `Caching:Owned:AnswerSecret`, which
makes the digest an HMAC that only holders of the secret can compute. `OwnedAnswerKey.ReadSecret(configuration,
environmentName)` throws without one, and a host calls it as it starts, so it fails to start. In `Development` the
plain digest is allowed, so a developer's machine needs no secret. Treat it like any other secret:

- **Clients learn of the secret from their own configuration, not from the contract.** An owner publishes
  `"digest": "sha-256"` (`OwnedAnswerKey.Digest`) whether or not a secret keys it, because the contract is the same in
  every deployment and the secret is not.
- **Every service and client sharing the cache must hold the same secret.** One holding a different secret, or none,
  computes subjects that no owner fills. It never reads a wrong answer: it always misses and asks the owner, so a
  mismatch costs speed, not correctness.
- **Changing the secret moves every answer keyed by text.** The cache misses until owners fill the new subjects, and
  entries under the old ones expire on their own.
- **A secret that is not base64 or is shorter than 32 bytes** makes `OwnedAnswerKey.ReadSecret` throw, without echoing
  the value, and the host fails to start.

Answers keyed by an identifier are stored under the identifier itself and are not affected.

## Security

- **What an entry holds is the host's choice.** This package names the fields, not what goes in `d`. A service host
  fills an entry only with attributes every reader of the cache may see. Whatever it puts there is Data at Rest in
  Redis, readable by anyone with read access to the owner's prefix.
- **The owner sits beside the data.** A reader checks the `o` field itself, so it never trusts the payload for whose
  row it is.
- **A digest is not encryption.** Even keyed, it only keeps the text out of the cache. Anyone holding the secret and
  read access can still confirm a guess.

## Requirements

- .NET 10.
- Microsoft.Extensions.Configuration.Abstractions.

## Contributing

Every source file has a matching test class in
[`tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests`](../tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests). Run
them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Resources.Caching.Abstractions/tests/Bitwarden.Server.Sdk.Resources.Caching.Abstractions.Tests
```

A change must keep:

- **The layout stable.** Keys, fields and the digest are read by services deployed apart from each other. A change to
  any of them moves every entry.
- **No text in a key.** A key written as text is only ever stored as its digest.
- **Names that can't be confused.** A service, resource type or answer name holds no `:`, `{` or `}`, and no resource
  type is `answer`, so no two names give the same key.
- **Few dependencies.** Nothing that connects to Redis or touches `HttpContext`.
