# Bitwarden.Server.Sdk.Client

**Audience:** engineers changing this package.

The HTTP pipeline a Bitwarden service's client package calls that service through. See [PACKAGE.md](PACKAGE.md) for how
to use it.

## What belongs here

Only the calling side: what a program needs to call a service, without referencing the framework that hosts one. No
ASP.NET Core, and nothing a service needs to serve. Types a service and its callers must both agree on (headers, the
context, idempotency keys, cache entry layout) live in the packages this one references. Cached reads only read: no
code here writes to Redis.

## How it works

`AddBitwardenClient` builds one named `HttpClient` per callee, with these handlers, outermost first
(`BitwardenClientServiceCollectionExtensions`):

| Order | Handler                 | Why it sits there                                                                                                               |
| ----- | ----------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| 1     | `ServiceFaultHandler`   | The call's one deadline around everything below, and the translation of every failure.                                          |
| 2     | `IdempotencyKeyHandler` | Once, before any attempt, so every attempt of a write carries the same key.                                                     |
| 3     | `ServiceWriteHandler`   | Reports the write to the running operation once per call, not once per attempt; a repeat with the same key isn't counted again. |
| 4     | `RetryHandler`          | Retries within the deadline.                                                                                                    |
| 5     | `ServiceTokenHandler`   | The token, obtained for each attempt (held between them).                                                                       |
| 6     | `ContextHandler`        | The context headers and context token, written in this one place.                                                               |
| 7     | `CircuitBreakerHandler` | Innermost, so it counts attempts at the callee itself and never the authorization server's failures.                            |

- **The client's own timeout is infinite.** `ServiceFaultHandler` enforces `Timeout` instead, so it can tell the callee
  not answering from the caller giving up.
- **Which writes take a key** is read once per client from the contract embedded in the client assembly
  (`KeyedOperations.FromAssembly`). Retrying a write without one is decided by `Transient.NeverSent`.
- **Tokens.** `ServiceTokenProvider` renews the service token on a timer at `RefreshAt` of its life, so calls don't
  usually wait; the first call, and a call after a failed renewal, obtains one itself. The timer runs on the monotonic
  clock and the token expires by the wall clock, so a held token is also checked against its renewal time. Context
  tokens are kept per subject, callee and context until `RefreshAt`, keyed by a digest so no token is held as a key.
  Token requests use their own `HttpClient` with a 30-second timeout (`ServiceTokenProvider.RequestTimeout`), and a
  call's deadline bounds them too.
- **Cached reads** wait on Redis for `CachedReads.Patience` and rest it for `CachedReadsConnection.Rest` after a
  failure.

`NSwag/` holds three of NSwag's parameter templates (header, path and query), each changed only to move a date to UTC
before it is formatted. They are not part of the NuGet package.

## Testing

The tests are in `tests/Bitwarden.Server.Sdk.Client.Tests`, mostly one class per source file; `ClientPipelineTests`
runs the whole pipeline. The cached-read tests start Redis with Testcontainers, so they need
Docker.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Client/tests/Bitwarden.Server.Sdk.Client.Tests
```

A change must keep:

- **One deadline per call**, covering the tokens the call obtains as well as the call, with the authorization server's
  failure said as its own and never as the callee's.
- **Retries only of what is safe to send twice**: a `GET` or `HEAD`, a write carrying an idempotency key, or a write
  whose connection never opened, and never an answer the callee gave itself.
- **Keys only to marked operations**, and the same key on every attempt of one call.
- **Context written in one place.** `ContextHandler` writes every context header and removes whatever a call site set.
- **No secret in a log, a message or `ToString()`.**
- **Tokens made and checked by Microsoft.IdentityModel, never by hand.** `ClientAssertion` creates and signs its JWT
  with `JsonWebTokenHandler` and takes its `kid` from the library's RFC 7638 thumbprint. `System.Security.Cryptography`
  is used directly only to load and export keys and to generate random bytes; no JWS, signature or JWK is assembled by
  hand.
