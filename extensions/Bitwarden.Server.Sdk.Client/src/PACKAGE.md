# Bitwarden.Server.Sdk.Client

**Audience:** engineers publishing a client package for their Bitwarden service, and engineers writing a service that
calls another through one.

## About

The calling side of a Bitwarden service. A service publishes a client package for the others to call it with, and that
package registers its client through this one. Every call then gets the caller's own credential, the request's
context, one deadline, an idempotency key on each write the callee's contract marks for one, bounded retries, a circuit
breaker per callee, and every failure turned into the exception it means.

It depends on Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors, Bitwarden.Server.Sdk.Http,
Bitwarden.Server.Sdk.Idempotency.Abstractions, Bitwarden.Server.Sdk.Resources.Caching.Abstractions,
Bitwarden.Server.Sdk.Sagas, Microsoft.Extensions.Http, Microsoft.IdentityModel.JsonWebTokens (signing client
assertions), StackExchange.Redis (cached reads) and Newtonsoft.Json (reading
a cached answer into a type NSwag generated). It needs an authorization server that issues client-credentials tokens
with an `audience`, and RFC 8693 token exchange for context tokens.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Client
```

## Configuration

Each client's settings are looked up in order, most specific first. `{Name}` is the name the client registers the
service under. A missing `BaseAddress`, `CallerName`, `IdentityUri`, or `CallerSecret` without a `CallerKey`, stops
the host at startup, naming the keys that would have supplied it, for example
`The widget-service client is not configured. BaseAddress is missing; set Services:widget-service:BaseAddress, or services:widget-service:https:0, services:widget-service:http:0, widget-service:baseUrl.`

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
directions. `Timeout` (30 seconds), `Retries` (2, at most 2) and `RetryDelay` (200 ms) are read from
`Services:{Name}:Timeout`, `:Retries` and `:RetryDelay` where set, after `AddBitwardenClient`'s `configure`, so a
deployment can tune one callee. `BreakAfter` (5), `BreakFor` (5 seconds) and `RefreshAt` (0.8 of a token's life) are
set in code, through `configure`.

Cached reads add these:

| Setting                                          | Default | What it does                                                                                                                                                                                                                                                           |
| ------------------------------------------------ | ------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Caching:Owned:Redis:Configuration`              | None    | The Redis the callees' caches are in. Without it, every cached read is a miss. Outside `Development`, it must name a Redis user of the service's own (`user=...`), or the host refuses to start: a reader connected as Redis's default user could write what it reads. |
| `Caching:Owned:Resources:{owner}:{name}:Enabled` | `true`  | `false` turns one resource's cached reads off.                                                                                                                                                                                                                         |

The request memo adds these (`RequestMemo` in Bitwarden.Server.Sdk.Context):

| Setting                          | Default   | What it does                                                                                              |
| -------------------------------- | --------- | --------------------------------------------------------------------------------------------------------- |
| `Caching:RequestMemo:Enabled`    | `true`    | `false` sends every call each time it is made. For measuring what the memo saves, never for a deployment. |
| `Caching:RequestMemo:MaxEntries` | `256`     | The most answers one request keeps; past it, calls are sent as if there were no memo.                     |
| `Caching:RequestMemo:MaxBytes`   | `4194304` | The most bytes of answers one request keeps; an answer that doesn't fit is passed on and not kept.        |

`CallerSecret` and `Tls:RequireHttps` carry a security trade-off; see
[Gotchas and constraints](#gotchas-and-constraints).

## Key concepts

- **The caller's own credential, for one callee.** The caller signs in to the authorization server with client
  credentials as `internal.{CallerName}`, for the scopes the client package declares and the one service it calls
  (`audience`), and presents the token on every call. The first call obtains one; a timer replaces it at `RefreshAt` of
  its life, and a call after that time, should the timer be late or the renewal fail, obtains one itself. Each token
  names the one service it is for, so a token leaked at one callee is refused at every other. Tokens, keys and
  assertions are never logged.
- **One deadline per call.** `Timeout` covers the tokens the call obtains, every retry and every wait.
- **Whose word each header is.** The owner headers and `Bitwarden-Context` are this service's word;
  `Bitwarden-Delegation` is the authorization server's. The client removes any of these a call site set, and leaves out
  a value of a shape the callee would refuse rather than send it.
- **A statement is about one outgoing call, and proves nothing.** `IContext`, row-level security and the service's own
  authorization never see an `OutgoingCall` statement, and the callee believes it as this service's word.

## Usage

### Publish a client for your service

A service's client package registers its client, and a consumer calls that package's registration. In the client
package's `WidgetServiceClientRegistration.cs`:

```csharp
using Bitwarden.Server.Sdk.Client;
using Microsoft.Extensions.DependencyInjection;

public static class WidgetServiceClientRegistration
{
    public static IHttpClientBuilder AddWidgetClient(
        this IServiceCollection services,
        Action<BitwardenClientOptions>? configure = null) =>
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

The client package embeds its service's contract as the resource `openapi.json`. It is where the client reads which
operations take an idempotency key and which reads may be answered from the cache; a client that embeds none sends no
keys. Embed it in the client implementation's project with
`<EmbeddedResource Include="..\..\openapi.json" LogicalName="openapi.json" />`; the logical name must be exactly
`openapi.json`.

### Sign in with a key instead of a secret

With a `CallerKey`, every token request carries a client assertion (RFC 7523's `private_key_jwt`) in place of the
secret: a JWT of type `client-authentication+jwt`, signed ES256 or RS256, naming the client as `iss` and `sub` and the
authorization server's issuer, read once from `{IdentityUri}/.well-known/openid-configuration`, as `aud`. Register the
client at the authorization server with `ClientAssertion.FromPrivateKey(key).PublicKey()`, the base64
SubjectPublicKeyInfo of the key's public half.

### Catch what the callee refused

A refusal arrives as the exception the callee threw, so a caller catches it by type. In `WidgetLookup.cs`:

```csharp
using Bitwarden.Server.Sdk.Errors;

public sealed class WidgetLookup(IWidgetClient widgets)
{
    public async Task<string?> FindAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await widgets.GetAsync(id, cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
```

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
rotation) is a `ServiceUnavailableException` naming it. The deadline covers the tokens the call obtains too. The
authorization server failing then is a `ServiceUnavailableException` naming `identity`, such as
`The identity service could not be reached for a token for calls to widget-service.`, and the call never reaches the
callee. The authorization server refusing the credential is a `ServiceCallException` with status `500`, this service's
own failure.

An exception the caller doesn't catch reaches a service host's exception handler, which answers the caller's own
caller as the callee answered: a `NotFoundException` as `404`, a `ServiceUnavailableException` as `503`.

### Know what each call carries

Every call carries these headers, from `IContext` (Bitwarden.Server.Sdk.Context):

- `Bitwarden-Organization` and `Bitwarden-User`, and `Bitwarden-Provider` only when the context has one;
- `Bitwarden-Context`, one closed, versioned JSON object: where the request came from, the caller's credential, and,
  for feature flags, the party the request's flags are evaluated for and the client's version on every call, and that
  party's organizations on a call that keeps the actor (at most 300, else left out);
- `Bitwarden-Call-Chain`, the services the request passed through with this one last, so a service called back while
  its own request waits can tell;
- `Bitwarden-Delegation`, a context token the authorization server signs for this callee and this caller, exchanged
  (RFC 8693) from the request's own proof (`ICallerProof`) when the call carries the request's person, and from the
  service's own token otherwise.

Without an `IContext`, no owner headers are sent and `Bitwarden-Context` carries only its version. Trace context
needs nothing: `HttpClient` sends `traceparent` itself.

### State whom one call is about

A domain service sometimes asks about a person the request it is handling doesn't name. Wrap exactly that call, as in
`WidgetReads.cs`:

```csharp
using Bitwarden.Server.Sdk.Client;

public sealed class WidgetReads(IWidgetClient widgets)
{
    public async Task<string> ForPersonAsync(Guid person, string widget, CancellationToken cancellationToken)
    {
        // The request names somebody else, or nobody: this one call is about person.
        using (OutgoingCall.AboutUser(person))
        {
            return await widgets.GetAsync(widget, cancellationToken);
        }
    }

    public async Task<string> AsTheServiceAsync(Guid organization, string widget, CancellationToken cancellationToken)
    {
        // The service's own call about the organization: no user or provider is sent.
        using (OutgoingCall.AsSystem(organization))
        {
            return await widgets.GetAsync(widget, cancellationToken);
        }
    }
}
```

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

Statements nest, and disposing one restores the one before.

### Rely on retries and idempotency keys

- **Idempotency keys go only to marked operations.** A write to an operation the callee's contract marks
  `x-bitwarden-idempotency-key` carries an `Idempotency-Key`: the one the call site set, one derived from the operation
  and the service's `Idempotency:Secret` inside an `IdempotentOperation`, or a fresh one. Inside an operation the key is
  also derived from whom the call concerns (its `Bitwarden-Organization`, `Bitwarden-User` and `Bitwarden-Provider`), so
  writes for different users or organizations keep their own keys whatever order a run makes them in. Every attempt of
  the call carries the same key and the same context. Any other write carries none.
- **Retries.** A `GET`, a `HEAD`, or a write with a key, is tried again up to `Retries` times when the connection
  failed or something in front of the callee answered `502`, `503` or `504`, with waits doubling from `RetryDelay`, all
  within the one deadline. The callee's own answers are never retried.
- **Still running.** A keyed write the callee is still running (`409` with `Retry-After`) is waited for; still running
  after the last retry, it is a `ServiceUnavailableException`.
- **Circuit breaker.** After `BreakAfter` attempts in a row find the callee not there, calls to it are refused at once
  for `BreakFor`, then one is let through to see whether it is back. Every client of the callee in the process shares
  the circuit; the authorization server failing never counts against it.

### Take back writes when a later step is refused

`Undo` records each write an operation made in another service, with what takes it back, and runs the next step inside
a guard. In `Grants.cs`:

```csharp
using Bitwarden.Server.Sdk.Client;
using Microsoft.Extensions.Logging;

public sealed class Grants(IWidgetGrants widgets, IGadgetStore gadgets, ILogger<Grants> logger)
{
    public async Task GrantAndDeleteAsync(Guid widget, Guid gadget, CancellationToken cancellationToken)
    {
        var undo = new Undo(logger);

        var grant = await widgets.GrantAsync(widget, cancellationToken);
        undo.Add($"grant {grant} on widget {widget}", token => widgets.RevokeAsync(widget, grant, token));

        // Refused, the grant is taken back and the refusal thrown. Any other failure may follow a delete that
        // landed, so the grant stays.
        await undo.GuardAsync(() => gadgets.DeleteAsync(gadget, cancellationToken), UndoOn.Refusal);
    }
}

public interface IWidgetGrants
{
    Task<Guid> GrantAsync(Guid widget, CancellationToken cancellationToken);

    Task RevokeAsync(Guid widget, Guid grant, CancellationToken cancellationToken);
}

public interface IGadgetStore
{
    Task DeleteAsync(Guid gadget, CancellationToken cancellationToken);
}
```

Undos run newest first, each on its own 30-second deadline (`Undo.Deadline`) rather than the request's token, and the
step's own failure is thrown. `Undo.IsRefusal` says whether a failure shows the callee wrote nothing; any other failure
may follow a write that landed.

Each write a client sends is also reported, before it is sent and once per call (a repeat carrying the same idempotency
key is not counted again), to the operation running now (`ServiceWrites`, Bitwarden.Server.Sdk.Sagas). A write-method
call that only reads (`ReadingRoutes`, or `BitwardenClientOptions.ReadingPaths`) is not counted.

### Call a product service

A product service serves people, so its client presents a token the caller hands each call, never the caller's own.
In `GadgetsClient.cs`:

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
`HttpClient`'s exception. A call sends exactly one request. A `ProductAnswer`'s `ToString()` names its status, never
its body, which may carry Protected Data.

### Ask once in a request

Nothing to write: while a service handles an inbound HTTP request, every `GET` and `HEAD` its service clients send
(`AddBitwardenClient`, and a product client's calls) is remembered for the rest of the request, and the same call made
again is answered from memory without being sent. The memo, `RequestMemo` in Bitwarden.Server.Sdk.Context, is begun
by the service host for each request and ended with it. Ask for what you need where you need it, as often as you need
it; don't pass answers around, or keep them in a field, to save a call.

- **The same call** is the same method and address, its query included (fields, filters, paging), with the same
  context headers (organization, user, provider, `Bitwarden-Context`, the call chain), delegated or asserted alike, from
  the same service. Anything different is sent. The credentials aren't part of it; the decision they carry is.
- **What is kept** is a `2xx` or `404` answer's status, headers and body. Each caller gets a response of its own. A
  refusal, a `5xx`, a `429`, a timeout or a failure to connect is never kept, so the next call is sent.
- **Identical calls in flight at once** (`Task.WhenAll`) are sent once and both answered; if that call fails, both
  fail, and nothing is kept.
- **A write forgets everything.** A write (any method but `GET`, `HEAD`, `OPTIONS` and `TRACE`) sent through a service
  client makes the request forget all it remembered, before it is sent and again once it is answered or has failed; so does a save to the service's own
  database (Bitwarden.Server.Sdk.Data). A request always sees its own writes.
- **The owners' caches too.** A read answered from a callee's cache (`ICachedReads`) is remembered the same way, so a
  repeated read waits on Redis no more than a repeated call waits on the service. Whether the remembered entry answers
  is decided for each call, as without the memo.

To read again on purpose, say so around the call (`OutgoingCall.Fresh()`): it is sent, or the cache read again, and
its answer replaces the remembered one. Use it after a write the platform can't see changed what another service
answers, or each time round a loop that waits for another service's answer to move. In `WidgetPolling.cs`:

```csharp
using Bitwarden.Server.Sdk.Client;

public sealed class WidgetPolling(IGadgetsClient gadgets, TimeProvider time)
{
    public async Task<Gadget> WaitUntilReadyAsync(Guid id, CancellationToken cancellationToken)
    {
        while (true)
        {
            Gadget gadget;
            using (OutgoingCall.Fresh())
            {
                gadget = await gadgets.GetGadgetAsync(id, cancellationToken);
            }

            if (gadget.Ready)
            {
                return gadget;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), time, cancellationToken);
        }
    }
}
```

An operation that does nothing but poll or wait is better marked `[WithoutRequestMemo]`
(Bitwarden.Server.Sdk.Context) on its handler: its request has no memo at all.

A message handler, an outbox handler, a job or a background service has no memo. Where one item of its work asks the
same thing more than once, begin one around that item, never around a loop over many. In `WidgetChangedHandler.cs`:

```csharp
using Bitwarden.Server.Sdk.Context;

public sealed class WidgetChangedHandler(IWidgetNotifier notifier, IWidgetAuditor auditor)
{
    public async Task HandleAsync(WidgetChanged message, CancellationToken cancellationToken)
    {
        using var memo = RequestMemo.Begin();

        // Both read the organization's settings; the Organization Service is asked once.
        await notifier.NotifyAsync(message.Organization, cancellationToken);
        await auditor.RecordAsync(message.Organization, cancellationToken);
    }
}
```

The meter `Bitwarden.Server.Sdk.Client.RequestMemo` counts `bitwarden.request_memo.reads` by service called
(`bitwarden.client.service`), what was read (`bitwarden.request_memo.source`: `call` or `cache`) and where its answer
came from (`bitwarden.request_memo.outcome`: `hit`, `joined`, `miss` or `bypassed`). Nothing asked or answered is
recorded.

### Answer a read from the callee's cache

A service may publish reads its clients can answer from its own Redis cache (`x-bitwarden-cache` in its
contract). The client package calls `AddCachedReads<TClient>(name)`.
`ICachedReads<TClient>` answers only where the callee would have answered this caller the same: the scope is one the
client holds, no attribute asked for is one the callee works out per caller (`perCaller`; a read naming no fields asks
for every attribute, and hits only where there are none), the contract's and the entry's format are this client's, the
call names the organization, user and provider the operation requires, a context the call asserts naming a person
comes from a service the operation admits it from (`assertedBy`), the entry is not a tombstone, and its owner is the one
the call names. A call's context is asserted, on this service's own word, exactly where the call itself
would assert it: an `OutgoingCall` statement naming somebody else or making the call the system's, or no request
behind the call (a message handler, a job); a call continuing the request's own person is delegated. A hit with no fields is the whole entry; with fields, only those. Anything else, and any doubt (Redis
unreachable or slower than 250 ms), is a miss, and after Redis fails every read skips the cache for 5 seconds. A hit
uses up an `OutgoingCall` statement as the call would have. Nothing in this package writes to Redis.

A key template names what fills it: `{key}` the identifier or key the read is asked by (`ReadAsync`), and
`{organization}`, `{user}` or `{provider}` a party the call carries, the owner of a key within its owner or the key of
a read of `current` (`ReadCurrentAsync`); a placeholder the call leaves unfilled is a miss. A client composing one
answer from several of its owner's reads (a membership's `providerStaff` from the organization's provider link and the
person's place in that provider) learns what the call carries with `NextCall()`, reads each as a call carrying the
parties it is about (`ReadAsync(operationId, key, fields, call)`, which uses up no statement), and calls `Answered()`
once the composed answer stands; where it doesn't, it makes the call, which uses the statement. `Fieldsets` builds
a sparse fieldset from a generated attributes class, keeping the attributes its contract requires.

## Gotchas and constraints

- **A shared secret proves less than a key.** With `CallerSecret`, services that share one secret can't be told apart
  by it; the authorization server limits only what each client id may ask for. A `CallerKey` gives each service its
  own key, and the authorization server holds only its public half. Prefer a `CallerKey`.
- **Calls are a Secure Channel only over TLS.** Set `Tls:RequireHttps` so a plain address stops the host at startup:
  every `AddBitwardenClient` address and the authorization server's, and a product client's conventional address. A
  product client over a named client of your own is yours to check.
- **`IContext` is resolved from a fresh scope on each call,** because the HTTP client factory pools handlers, so an
  implementation must read ambient state or be a singleton.
- **A statement is used up by one call.** The first call inside a statement uses it up; a second throws
  `InvalidOperationException`.
- **No statements in a product service**, but for `AboutCreatedOrganization` on an operation declared to create one
  (`IOrganizationCreation`) and `WithoutProvider`: the call throws and sends nothing.
- **Not on an event's actor.** While a service handles an event (`ContextScope.IsEvent`), the event's context records
  its actor and names nobody acting, so a call with no statement is the service's own. A call is that, an `AsSystem`
  statement, or one naming its person outright; one inside a statement that names nobody and is not the system's
  (`AboutOrganization`, say) is logged as a warning, or refused before it is sent with `OutgoingCall:RefuseEventActor`.
- **A write without a key is retried only when its connection never opened** (refused, a name that didn't resolve, a
  proxy that wouldn't tunnel). What that means for the operation is in Bitwarden.Server.Sdk.Idempotency's
  [Opt an operation in](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md#opt-an-operation-in).
- **Guard a step that calls another service with `UndoOn.Refusal`.** `GuardAsync` takes the writes back on any failure
  by default (`UndoOn.AnyFailure`), which suits a step that writes nothing elsewhere, such as a local save. After a
  timeout, an unreachable service, a `5xx` or a cancelled request, the step's write may have landed, and
  `UndoOn.Refusal` keeps the writes before it.
- **A failed undo is logged by the text given to `Add`,** so that text names identifiers only.
- **The memo is for one request, in memory.** It holds only what a callee already answered this service for this
  request, and nothing in it outlives the request or reaches another caller. A WebSocket connection has none: it lives
  far longer than an answer stays true. Work a request starts and doesn't await finds the memo ended once the request
  is answered.
- **Only service clients.** An `HttpClient` of your own (a payment processor, a webhook), the token client and any
  address under `/connect/` never pass through the memo.
- **A read-only action forgets too.** An action sent as `POST` (`.../actions/holds`, `.../actions/check`) is not
  remembered, and makes the request forget like any other `POST`.

## Types

| Type                                                                         | Purpose                                                                               |
| ---------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| `BitwardenClientServiceCollectionExtensions`                                 | `AddBitwardenClient` and `AddCachedReads`                                             |
| `BitwardenClientOptions`                                                     | A client's settings                                                                   |
| `IServiceTokenProvider`, `ServiceTokenProvider`                              | The caller's token and its context tokens                                             |
| `ClientAssertion`                                                            | A signed client assertion in place of a secret                                        |
| `OutgoingCall`, `OutgoingCallOptions`                                        | Stating whom one call is about; the event actor rule; `Fresh()` past the request memo |
| `ServiceCallException`, `RequestRefusedException`, `RelayedRefusalException` | Failures with no more specific exception                                              |
| `Undo`, `UndoOn`                                                             | Taking back writes that span services                                                 |
| `ProductClient`, `ProductAnswer`, `ProductAnswer<T>`                         | Calling a product service with somebody's own token                                   |
| `ProductClientServiceCollectionExtensions`                                   | `AddProductClient`                                                                    |
| `ICachedReads<TClient>`, `CachedAnswer`, `CachedAnswerKind`                  | Reads answered from the callee's cache                                                |
| `CacheableRead`                                                              | What a contract publishes as cacheable                                                |
| `CachedResources`                                                            | Reading a cached resource into its generated type                                     |
| `Fieldsets`                                                                  | Sparse fieldsets from a generated contract                                            |
| `UtcDateJsonConverter`                                                       | Dates sent as ISO 8601 UTC with a literal `Z`                                         |

A client NSwag generates sends dates as UTC with `/JsonConverters:Bitwarden.Server.Sdk.Client.UtcDateJsonConverter`,
`/ParameterDateTimeFormat:yyyy-MM-ddTHH:mm:ss.FFFFFFFZ`, and `/TemplateDirectory` naming the parameter templates in
[NSwag](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Client/NSwag), which
move a date to UTC before it is formatted.
