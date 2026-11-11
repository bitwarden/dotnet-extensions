# Bitwarden.Server.Sdk.Client

## About

The calling side of a Bitwarden service. A service publishes a client package for the others to call it with, and
that package registers its client through this one: the caller's credential, the request's context, one deadline per
call, an idempotency key on every write, bounded retries, a circuit breaker per callee, and every failure turned into
the exception it means. It also holds `OutgoingCall`, which states whom one call is about, `Undo`, which takes back
writes that span services, `ProductClient` for calling a product service with somebody's own token, and
`ICachedReads<TClient>` for answering a read from the callee's own cache.

It depends on Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors, Bitwarden.Server.Sdk.Http,
Bitwarden.Server.Sdk.Idempotency.Abstractions, Bitwarden.Server.Sdk.Resources.Caching.Abstractions,
Bitwarden.Server.Sdk.Sagas, Microsoft.Extensions.Http, StackExchange.Redis and Newtonsoft.Json.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Client
```

## Configuration

Each client's settings are looked up in order, most specific first. `{Name}` is the name the client registers the
service under. A missing `BaseAddress`, `CallerName`, `IdentityUri`, or `CallerSecret` without a `CallerKey`, stops
the host at startup, naming the keys that would have supplied it.

| Setting          | Keys tried, in order                                                                                                                                                 |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `BaseAddress`    | `Services:{Name}:BaseAddress`, `services:{name}:https:0`, `services:{name}:http:0`, `{name}:baseUrl`                                                                 |
| `CallerName`     | `Services:{Name}:CallerName`, `Bitwarden:ServiceName`, else the entry assembly's name, lower case: its second part when it has more than one                         |
| `Audience`       | `Services:{Name}:Audience`, else `{Name}`                                                                                                                            |
| `IdentityUri`    | `Services:{Name}:IdentityUri`, `globalSettings:baseServiceUri:internalIdentity`, `globalSettings:baseServiceUri:identity`, `Authentication:Schemes:Bearer:Authority` |
| `CallerSecret`   | `Services:{Name}:CallerSecret`, `globalSettings:internalIdentityKey`                                                                                                 |
| `CallerKey`      | `Services:{Name}:CallerKey`, `Bitwarden:ClientKey`: a PKCS #8 private key, base64, P-256 or RSA of 2048 bits or more                                                 |
| `RequireHttps`   | `Tls:RequireHttps`: `BaseAddress` and `IdentityUri` must then be `https://`                                                                                          |
| Event actor rule | `OutgoingCall:RefuseEventActor` (`false`): refuse, rather than log, a call that carries an event's actor                                                             |

The `services:{name}:...` keys are what .NET Aspire publishes for a referenced service, and
`Authentication:Schemes:Bearer:Authority` is the key the serving side reads, so one setting configures both
directions. `Timeout` (30 seconds), `Retries` (2, at most 2), `RetryDelay` (200 ms), `BreakAfter` (5), `BreakFor`
(5 seconds) and `RefreshAt` (0.8 of a token's life) are set in code, through `AddBitwardenClient`'s `configure`.

Cached reads add these:

| Setting                                          | Default    | What it does                                                                     |
| ------------------------------------------------ | ---------- | -------------------------------------------------------------------------------- |
| `Caching:Owned:Redis:Configuration`              | None       | The Redis the callees' caches are in. Without it, every cached read is a miss.   |
| `Caching:Owned:AnswerSecret`                     | None       | The key for digests of text keys. Required outside `Development` with Redis set. |
| `Caching:Owned:Resources:{owner}:{name}:Enabled` | `true`     | `false` turns one resource's, index's or answer's cached reads off.              |
| `Caching:Owned:InProcess:Enabled`                | `true`     | `false` reads every index answer from Redis in full.                             |
| `Caching:Owned:InProcess:Lifetime`               | `00:00:30` | How long an index answer is held in the process.                                 |
| `Caching:Owned:InProcess:Capacity`               | `10000`    | How many index answers one client holds.                                         |

With `CallerSecret`, services that share one secret can't be told apart by it. Prefer a `CallerKey`; see the
[README's security notes](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Client/src/README.md#security).

## Getting started

A service's client package registers its client, and a consumer calls that package's registration:

```csharp
using Microsoft.Extensions.DependencyInjection;

public static class WidgetServiceClientRegistration
{
    public static IHttpClientBuilder AddWidgetClient(
        this IServiceCollection services,
        Action<Bitwarden.Server.Sdk.Client.BitwardenClientOptions>? configure = null) =>
        services.AddBitwardenClient<IWidgetClient, WidgetClient>("widget-service", ["widgets.read"], configure);
}

public interface IWidgetClient
{
    Task<string> GetAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class WidgetClient(HttpClient http) : IWidgetClient
{
    public Task<string> GetAsync(string id, CancellationToken cancellationToken = default) =>
        http.GetStringAsync($"widgets/{id}", cancellationToken);
}
```

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Client/src/README.md#getting-started)
for a complete program.

## Documentation

### The credential

The caller asks the authorization server for a token with client credentials, as `internal.{CallerName}`, for the
scopes the client package declares and the one service it calls (`audience`), and presents it on every call. The
first call obtains one; a timer replaces it at `RefreshAt` of its life, and a call after that time obtains one itself.
With a `CallerKey`, every token request carries a client assertion (RFC 7523's `private_key_jwt`, `ClientAssertion`)
in place of the secret: a JWT of type `client-authentication+jwt`, signed ES256 or RS256, naming the client as `iss`
and `sub` and the authorization server's issuer, read once from `{IdentityUri}/.well-known/openid-configuration`, as
`aud`. `ClientAssertion.PublicKey()` is what the authorization server registers for the client.

### Context

`IContext` (Bitwarden.Server.Sdk.Context) says what the request is about. `ContextHandler` writes, on every call:

- `Bitwarden-Organization` and `Bitwarden-User`, and `Bitwarden-Provider` only when the context has one;
- `Bitwarden-Context`, one closed, versioned JSON object: where the request came from, the caller's credential, and on
  a call made as the acting person their organizations, for feature flags (at most 300, else left out);
- `Bitwarden-Call-Chain`, the services the request passed through with this one last, so a service called back while
  its own request waits can tell;
- `Bitwarden-Delegation`, a context token the authorization server signs for this callee and this caller, exchanged
  (RFC 8693) from the request's own proof (`ICallerProof`) when the call carries the request's person, and from the
  service's own token otherwise.

A header a call site set is removed first. A value of a shape the callee would refuse is left out rather than sent.
`IContext` is resolved from a fresh scope on each call, because the HTTP client factory pools handlers, so an
implementation must read ambient state or be a singleton. Without one, no owner headers are sent and
`Bitwarden-Context` carries only its version. Trace context needs nothing: `HttpClient` sends `traceparent` itself.

### Stating whom one call is about

`OutgoingCall` changes the next call's context, and only that call's:

| Statement                                        | The call carries                                                                    |
| ------------------------------------------------ | ----------------------------------------------------------------------------------- |
| `AboutUser(user)`                                | The user as the acting person                                                       |
| `AboutOrganization(organization)`                | The organization                                                                    |
| `About(user, organization)`                      | Both                                                                                |
| `AboutCreatedOrganization(organization)`         | The organization the request just created; allowed in a product service             |
| `AsSystem(organization)`                         | The organization, no user and no provider; the credential `SYSTEM`                  |
| `AsSystem(organization, recordedAs)`             | As `AsSystem(organization)`, with the person as `recordedUser` for the audit record |
| `AsSystemAbout(user)`                            | The user, no organization and no provider; the credential `SYSTEM`                  |
| `AsSystem()`                                     | No user, organization or provider; the credential `SYSTEM`                          |
| `AsSystemForProvider(provider)`                  | The provider, no user and no organization; the credential `SYSTEM`                  |
| `WithoutProvider()`                              | Everything but the provider; allowed in a product service                           |
| `AboutOrganizationWithoutProvider(organization)` | The organization, and everything but the provider                                   |

- **One call.** The first call inside a statement uses it up; a second throws `InvalidOperationException`. Statements
  nest, and disposing one restores the one before.
- **Outgoing only.** `IContext`, row-level security and the service's own authorization never see a statement.
- **Not in a product service**, but for `AboutCreatedOrganization` on an operation declared to create one
  (`IOrganizationCreation`) and `WithoutProvider`: the call throws and sends nothing.
- **Not on an event's actor.** While a service handles an event (`ContextScope.IsEvent`), a call is the service's own
  (an `AsSystem` statement), names its person outright, or carries no person because the event names none. Any other
  call is logged as a warning, or refused before it is sent with `OutgoingCall:RefuseEventActor`.
- **Proves nothing.** The callee believes a statement as this service's word.

### Failures

| Error code                                                                                                                      | Exception                                                                                        |
| ------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `not-found`                                                                                                                     | `NotFoundException`                                                                              |
| `conflict`                                                                                                                      | `ConflictException`                                                                              |
| `unauthenticated`                                                                                                               | `UnauthenticatedException`                                                                       |
| `forbidden`                                                                                                                     | `ForbiddenException`                                                                             |
| `not-implemented`                                                                                                               | `NotImplementedException`                                                                        |
| `key-rotation-in-progress` (a `409` or a `503`)                                                                                 | `RelayedRefusalException`, carrying the status and errors to answer on unchanged                 |
| `field-required`, `invalid-type`, `invalid-value`, `invalid-shape`, `malformed-json`, `already-exists`, `invalid-resource-type` | `InvalidRequestException`, or `RequestRefusedException` when a `422` carries more than one error |
| anything else                                                                                                                   | `ServiceCallException`                                                                           |

A callee that can't be reached, doesn't answer within `Timeout`, or answers `502`, `503` or `504` (but for a key
rotation) is a `ServiceUnavailableException` naming it. The deadline covers the tokens the call obtains too; the
authorization server failing then is a `ServiceUnavailableException` naming `identity`, and refusing the credential is
a `ServiceCallException` with status `500`, this service's own failure.

### Resilience

- **Idempotency keys.** Every call but a `GET`, `HEAD`, `OPTIONS` or `TRACE` carries an `Idempotency-Key`: the one the
  call site set, one derived from the operation inside an `IdempotentOperation`, or a fresh one.
- **Retries.** A `GET`, a `HEAD`, or a write with a key, is tried again up to `Retries` times when the connection
  failed or something in front of the callee answered `502`, `503` or `504`, with waits doubling from `RetryDelay`, all
  within the one deadline. A keyed write the callee is still running (`409` with `Retry-After`) is waited for; still
  running after the last retry, it is a `ServiceUnavailableException`. The callee's own answers are never retried.
- **Circuit breaker.** After `BreakAfter` attempts in a row find the callee not there, calls to it are refused at once
  for `BreakFor`, then one is let through to see whether it is back. Every client of the callee in the process shares
  the circuit; the authorization server failing never counts against it.

### Writes that span services

Each write a client sends is reported to the operation running now (`ServiceWrites`, Bitwarden.Server.Sdk.Sagas)
before it is sent, once per idempotency key. A write-method call that only reads (`ReadingRoutes`, or
`BitwardenClientOptions.ReadingPaths`) is not counted. `Undo` records each write made, with what takes it back, and
`GuardAsync` runs the next step: on a failure `UndoOn` says takes them back, every write recorded is taken back,
newest first, each on its own 30-second deadline (`Undo.Deadline`), and the step's own failure is thrown.
`Undo.IsRefusal` says whether a failure shows the callee wrote nothing; any other failure may follow a write that
landed.

### Product clients

A product service serves people, so its client presents a token the caller hands each call, never the caller's own:

```csharp
using Bitwarden.Server.Sdk.Client;
using Microsoft.Extensions.DependencyInjection;

public sealed class GadgetsClient(HttpClient http) : ProductClient(http)
{
    public Task<ProductAnswer> GetGadgetAsync(string bearer, Guid id, CancellationToken cancellationToken) =>
        SendAsync(bearer, HttpMethod.Get, $"gadgets/{id}", json: null, cancellationToken);
}

public static class GadgetsClientRegistration
{
    public static IHttpClientBuilder AddGadgetsClient(this IServiceCollection services) =>
        services.AddProductClient<GadgetsClient, GadgetsClient>("gadget-service");
}
```

Every answer is a `ProductAnswer`: the status and body as the service wrote them, a refusal included, and from a
generated call (`CallAsync`) the document the generated client read. A service that can't be reached is the
`HttpClient`'s exception. A call sends exactly one request. With `Tls:RequireHttps`, a plain conventional address
stops the host at startup.

### Cached reads

A service may publish reads its clients can answer from its own Redis cache (`x-bitwarden-cache`,
`x-bitwarden-index` and `x-bitwarden-answer` in its contract). The client package embeds the contract as
`openapi.json` and calls `AddCachedReads<TClient>(name)`. `ICachedReads<TClient>` answers only where the callee would
have answered this caller the same: the scope is one the client holds, every attribute asked for is cached, the call
names the organization, user and provider the operation requires, the entry is not a tombstone, and its owner is the
one the call names. Anything else, and any doubt (Redis unreachable or slower than 250 ms), is a miss. A hit uses up
an `OutgoingCall` statement as the call would have. `Fieldsets` builds a sparse fieldset from a generated attributes
class, keeping the attributes its contract requires.

### Types

| Type                                                                         | Purpose                                              |
| ---------------------------------------------------------------------------- | ---------------------------------------------------- |
| `BitwardenClientServiceCollectionExtensions`                                 | `AddBitwardenClient` and `AddCachedReads`            |
| `BitwardenClientOptions`                                                     | A client's settings                                  |
| `IServiceTokenProvider`, `ServiceTokenProvider`                              | The caller's token and its context tokens            |
| `ClientAssertion`                                                            | A signed client assertion in place of a secret       |
| `OutgoingCall`, `OutgoingCallOptions`                                        | Stating whom one call is about; the event actor rule |
| `ServiceCallException`, `RequestRefusedException`, `RelayedRefusalException` | Failures with no more specific exception             |
| `Undo`, `UndoOn`                                                             | Taking back writes that span services                |
| `ProductClient`, `ProductAnswer`, `ProductAnswer<T>`                         | Calling a product service with somebody's own token  |
| `ProductClientServiceCollectionExtensions`                                   | `AddProductClient`                                   |
| `ICachedReads<TClient>`, `CachedAnswer`, `CachedAnswerKind`                  | Reads answered from the callee's cache               |
| `CacheableRead`, `CacheableIndexRead`, `CacheableAnswerRead`                 | What a contract publishes as cacheable               |
| `CachedResources`                                                            | Reading a cached resource into its generated type    |
| `Fieldsets`                                                                  | Sparse fieldsets from a generated contract           |
| `UtcDateJsonConverter`                                                       | Dates sent as ISO 8601 UTC with a literal `Z`        |
