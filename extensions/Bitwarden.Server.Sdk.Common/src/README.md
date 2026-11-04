# Bitwarden.Server.Sdk.Common

Types that a Bitwarden service and the code that calls it both need to agree on. See
[PACKAGE.md](PACKAGE.md) for how to use them.

## What belongs here

Only what both a service and a caller of that service need. The test is whether removing a type
would force a caller to reference the package that hosts the service.

Things that look shared but are not:

- Resource declarations a service authors. A client consumes generated types for them instead.
- Anything touching `HttpContext`, MVC or `HttpClient`.

## Why it exists

The hosting package must be free to change without considering every caller. That is only true if
callers do not reference it, which is only true if the types they share live somewhere neither
owns.

## Configuration

The package reads one setting itself: `OwnedAnswerKey.ReadSecret` reads and checks
`Caching:Owned:AnswerSecret`. It names the rest, which the hosting and client packages read, so a
service and its callers agree on them.

Every setting the package names is a public constant on the type it belongs to, named `Key` or
`Section` or ending in one of them (for example `OwnedCacheEntry.ConnectionKey`,
`OwnedCacheEntry.ResourcesSection` and `InternalTls.Section`). Its XML documentation says what the
setting does and what happens when it is unset. Look there for the full list; this file does not
repeat it.

### The answer secret

An answer keyed by text, such as an email address, is stored under a digest of that text, never
the text itself. With no secret the digest is plain SHA-256, which anyone can compute. Someone who
can read the cache and guess an address can then confirm it is there.

So outside the `Development` environment, a host with a service cache requires
`Caching:Owned:AnswerSecret`, which makes the digest an HMAC that only holders of the secret can
compute. `OwnedAnswerKey.ReadSecret(configuration, environmentName)` throws without one, and a
host calls it as it starts, so it fails to start. In `Development` the plain digest is allowed, so
a developer's machine needs no secret. Treat it like any other secret:

- **Clients learn of the secret from their own configuration, not from the contract.** An owner
  publishes `"digest": "sha-256"` whether or not a secret keys it, because the contract is the same
  in every deployment and the secret is not.
- **Every service and client sharing the cache must hold the same secret.** One holding a different
  secret, or none, computes subjects that no owner fills. It never reads a wrong answer: it always
  misses and asks the owner, so a mismatch costs speed, not correctness.
- **Changing the secret moves every answer keyed by text.** The cache misses until owners fill the
  new subjects, and entries under the old ones expire on their own.
- **A secret that is not base64 or is shorter than 32 bytes** makes `OwnedAnswerKey.ReadSecret`
  throw, without echoing the value, and the host fails to start.

Answers keyed by an identifier are stored under the identifier itself and are not affected.

## Testing

Every source file has a matching test class in
`tests/Bitwarden.Server.Sdk.Common.Tests`, in a folder that mirrors `src`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Common/tests/Bitwarden.Server.Sdk.Common.Tests
```
