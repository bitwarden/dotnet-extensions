# Bitwarden.Server.Sdk.Authorization

## About

Who may call a Bitwarden service's operation, on whose behalf, and for which owner. It fills `IContext` from a person's
session at a product service, or from the context Identity signed for one call at a domain service; refuses a context
it can't believe; checks each operation's `[Authorization]` against it; and closes every operation that doesn't say
otherwise. A refused caller gets `401` or `403` with the standard error document, naming the requirement not met.

Bitwarden.Server.Sdk.Service registers and orders all of this for every service, and Bitwarden.Server.Sdk.Resources
settles each operation's `[Authorization]` as it maps it. Reference this package directly to compose a host yourself,
or to declare an operation's requirements in a library. It depends on ASP.NET Core, Bitwarden.Server.Sdk.Authentication,
Bitwarden.Server.Sdk.Context and Bitwarden.Server.Sdk.Errors.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Authorization
```

## Configuration

| Setting                          | Default | What it does                                                                                                                                                         |
| -------------------------------- | ------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ForwardedHeaders:KnownProxies`  | None    | Comma-separated addresses or host names of proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed.                                                     |
| `ForwardedHeaders:KnownNetworks` | None    | Comma-separated CIDR networks of such proxies.                                                                                                                       |
| `CallChain:RefuseReentry`        | `false` | When true, a call back into the service while its own request waits is refused (`500`) rather than logged and counted, unless the operation says `[AcceptsReentry]`. |

A trusted proxy chooses the address a request is recorded and rate-limited under: see "Trust the proxies in front of
the service" below. The bearer handler's settings, `Authentication:Schemes:Bearer:*`, are read by
Bitwarden.Server.Sdk.Authentication.

## Usage

### Compose a host

A host that doesn't build on Bitwarden.Server.Sdk.Service registers the package and adds its middleware in this order:

```csharp
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;

// A domain service, which other services call. A product service, which people call with their own session, is true;
// its IServiceIdentity.IsProductService must be true as well.
var isProductService = false;

var builder = WebApplication.CreateBuilder(args);

// The authorization reads both of these; Bitwarden.Server.Sdk.Service registers them for every service.
builder.Services.AddSingleton<IServiceIdentity>(new WidgetServiceIdentity(isProductService));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddBitwardenAuthentication();
builder.Services.AddCallerAuthorization(isProductService);

var app = builder.Build();

app.UseForwardedClientAddress();

if (isProductService)
{
    app.UseContextRefusal();
}

app.UseBitwardenAuthentication();

if (!isProductService)
{
    app.UseContextHeaders();
}

app.UseAuthorization();
app.UseCallChainCheck();

app.MapGet("/api/v1/context", (IContext context) => new CallerAnswer(context.OrganizationId, context.UserId));

app.Run();

internal sealed record CallerAnswer(Guid? OrganizationId, Guid? UserId);

internal sealed class WidgetServiceIdentity(bool isProductService) : IServiceIdentity
{
    public string Name => "widget-service";

    public string Title => "Widget Service";

    public bool IsProductService => isProductService;
}
```

| Middleware                    | Where                                   | What it does                                                                                                                                                                                                                     |
| ----------------------------- | --------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `UseForwardedClientAddress()` | Before anything reads the connection    | Takes the address and scheme from `X-Forwarded-For` and `X-Forwarded-Proto` only when a trusted proxy sent them.                                                                                                                 |
| `UseContextRefusal()`         | Product services, before authentication | Refuses `400` a request carrying any context header, whoever sends it.                                                                                                                                                           |
| `UseContextHeaders()`         | Domain services, after authentication   | Believes another service's context only as its signed token says: `401` for a missing or forged token, `400` for a malformed or disagreeing context, `403` for an asserted person at an operation that doesn't admit the caller. |
| `UseCallChainCheck()`         | After authorization                     | Keeps the chain of services a call passed through, and notices a call back into this service.                                                                                                                                    |

Bitwarden.Server.Sdk.Service adds its exception handler, status pages, CORS and per-address rate limits between
`UseForwardedClientAddress()` and the rest, and its feature flag checks and `UseIdempotencyKeys()` after
`UseCallChainCheck()`.

Every registration but the authorization handlers and the fallback policy is `TryAdd`: register your own `IContext`,
`ICallerRole`, `ICallerProof` or `IRequestNamedOwner` first to replace it. Make a replacement a singleton that reads the
current request through `IHttpContextAccessor` and holds nothing. The package is not AOT-compatible.

Without a token, `GET /api/v1/context` answers `401` with the error code `unauthenticated`. A token is accepted only
where its `aud` names the service (`IServiceIdentity.Audience`, its name unless overridden); this is not a setting. A
token naming several audiences is accepted when one is this service's. Audiences you add to the bearer options are
accepted at every endpoint: if only one surface should accept an extra audience, check `aud` yourself on the other
operations (an `IAuthorizesOperation`), or host that surface in its own service. At a product service, a token without
a GUID `sub` is refused `401`, except a credential an operation accepts.

### Declare what an operation demands

`AuthorizationDefaults` says what a service's operations demand, and `[Authorization]` on a handler or its class says
where one differs. The most specific wins: the handler's, then its class's, then the defaults.
Bitwarden.Server.Sdk.Resources settles each operation mapped with `WithStandards()` and refuses at startup a declaration
that contradicts the operation. A host without it settles each endpoint with `OperationAuthorization`:

```csharp
using System.Reflection;
using Bitwarden.Server.Sdk.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

public static class WidgetEndpoints
{
    private static readonly AuthorizationDefaults Defaults = new()
    {
        Organization = ContextRequirement.Optional,
        ReadScope = "widgets.read",
        WriteScope = "widgets.write",
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/widgets/{id}", GetWidget).Settled(reads: true);
        endpoints.MapDelete("/api/v1/shared-widgets/{id}", DeleteSharedWidget).Settled(reads: false);
        endpoints.MapGet("/api/v1/widgets/ping", Ping).Settled(reads: true);
    }

    public static string GetWidget(string id) => id;

    [Authorization(Scope = "widgets.admin", Organization = ContextRequirement.Required)]
    public static void DeleteSharedWidget(string id)
    {
    }

    [Authorization(Anonymous = true)]
    public static string Ping() => "pong";

    private static RouteHandlerBuilder Settled(this RouteHandlerBuilder builder, bool reads)
    {
        builder.Add(endpoint =>
        {
            var method = endpoint.Metadata.OfType<MethodInfo>().LastOrDefault();

            OperationAuthorization.Apply(endpoint.Metadata, OperationAuthorization.Settle(
                method?.GetCustomAttribute<AuthorizationAttribute>(inherit: true),
                method?.DeclaringType?.GetCustomAttribute<AuthorizationAttribute>(inherit: true),
                Defaults,
                reads));
        });

        return builder;
    }
}
```

- `GetWidget` demands `widgets.read` and accepts a request naming no organization. `DeleteSharedWidget` demands
  `widgets.admin` and an organization; a caller without them is refused `403`, naming what is missing. `Ping` is open
  to callers with no token. Nothing that touches a caller's data should set `Anonymous`.
- With `ReadScope` and `WriteScope` set, every read demands the first and everything else the second. Set both:
  without them, an operation that declares no scope demands none.
- `Organization`, `User` and `Provider` are each `Required`, `Optional` or `Forbidden`. Unless set, an organization is
  required, a user optional and a provider forbidden.
- A role, a permission and `Anonymous` count on the handler only, never inherited from its class.
- An `[Authorization]` that is never settled demands nothing beyond the fallback policy's authenticated caller.
- Authorization decides whether a caller may invoke an operation, not which rows it reaches. Filter queries by
  `IContext`, or by `IRowOwnership` (Bitwarden.Server.Sdk.Data.Abstractions).

For a check `[Authorization]` can't express, put an attribute implementing `IAuthorizesOperation`
(Bitwarden.Server.Sdk.Resources) on a handler mapped with `WithStandards()`, or on its class. It runs after
authorization and before anything is bound or validated; refuse by throwing `ForbiddenException`.

### Require a role or permission (product services)

Roles and permissions come from a person's session, read through `ICallerRole`, so they work on a product service
only. Set `OrganizationRole`, `Permission` or `ProviderRole` on `[Authorization]`:

- A role is satisfied by it or any role above it (`User`, `Custom`, `Admin`, `Owner`).
- An owner or administrator holds every permission; a custom member must have been granted it.
- A role counts only in the organization the session names.
- On a domain service `ICallerRole` answers `None`, so an operation demanding a role refuses every call `403`. The
  package doesn't refuse such a declaration at startup: authorize a domain service by scope and context instead.

### Accept other callers and owners

Each of these goes on one handler:

- `[AnyScope("widgets.read", "widgets.invitations")]` admits a caller holding any one of several scopes. The operation
  then decides what each scope may ask, with `CallerScopes.Holds(principal, scope)`.
- `[AcceptsOrganizationCredential(scope)]` and `[AcceptsSendCredential]` (product services) accept a token issued to
  an organization, or minted for one Send's link.
- `[NamesOrganization]`, `[NamesProvider]` and `[CreatesOrganization]` (product services) mark an operation about an
  organization or provider the session doesn't name. It is carried on the operation's outgoing calls, never into its
  own `IContext`.
- `[AcceptsAssertedContext(reason, callers)]` (domain services): see the next section.

### Know what each kind of service believes

|                                | Domain service                                                                         | Product service                                                    |
| ------------------------------ | -------------------------------------------------------------------------------------- | ------------------------------------------------------------------ |
| `IContext.UserId`              | The context token's user, from another service                                         | The validated token's `sub`                                        |
| `IContext.OrganizationId`      | The context token's organization                                                       | The token's signed `organization` claim                            |
| `IContext.ProviderId`          | The context token's provider                                                           | A provider session's `provider` claim                              |
| Address, device and credential | `Bitwarden-Context`, as the token signed it                                            | The connection's address, the `device` claim, the client's headers |
| A context header               | Believed from an internal service with a valid context token; ignored from anyone else | Refused `400`, whoever sends it                                    |
| `ICallerRole`                  | Always `None`                                                                          | From the session, for the organization it names                    |

The context headers are `BitwardenHeaders.All`: `Bitwarden-Organization`, `Bitwarden-User`, `Bitwarden-Provider`,
`Bitwarden-Context` and `Bitwarden-Delegation`, the context token. A domain service believes them only from another
service (a token whose `client_id` is `internal.*`), and only when the context token, of type `bitwarden-context+jwt`,
is:

- signed by a key, and issued by the issuer, that the service's bearer handler trusts;
- unexpired, and addressed (`aud`) to this service by its own name;
- issued to the caller presenting the service token (its `client_id` matches).

A domain service is called with the calling service's own token, never a person's: the caller exchanges a context
token from Identity for each callee instead.

The context is then the token's, and a plain header that is present must say the same. From a person's token, a Send's
token or no token the headers are not read and the context is empty. Each refusal names the header at fault in the
error's `source.header`:

| The call                                                                      | Status | Header named           |
| ----------------------------------------------------------------------------- | ------ | ---------------------- |
| Context but no context token                                                  | `401`  | `Bitwarden-Delegation` |
| A context token that is forged, expired, for another service or caller        | `401`  | `Bitwarden-Delegation` |
| A plain header that disagrees with the context token                          | `400`  | That header            |
| A context header sent more than once                                          | `400`  | That header            |
| A malformed `Bitwarden-Context` object in the token                           | `400`  | `Bitwarden-Context`    |
| An asserted context naming a person, at an operation not admitting the caller | `403`  | `Bitwarden-Delegation` |

A context token is either delegated, proven by a person's, organization's or machine account's own token, or asserted,
the calling service's own word. An asserted context naming nobody, and a delegated one, are accepted everywhere. An
operation that accepts an asserted context naming a person names the services that may send one, and why:

```csharp
using Bitwarden.Server.Sdk.Authorization;

public static class WidgetExpiryEndpoints
{
    [AcceptsAssertedContext("The widget jobs expire widgets on their owners' behalf.", "widget-jobs")]
    [Authorization(User = ContextRequirement.Required)]
    public static void Expire(string id)
    {
    }
}
```

`IContext.Credential`, `RecordedUserId` and `RecordedProviderId` are for audit records, and `UserOrganizationIds` for
feature flag targeting only: never let any of them drive authorization, a row filter or a cache.

### Trust the proxies in front of the service

```json
{
    "ForwardedHeaders": {
        "KnownProxies": "proxy",
        "KnownNetworks": "10.0.0.0/8"
    }
}
```

- A request's address is its connection's. When the connection is a trusted proxy's and the request carries
  `X-Forwarded-For`, it is the last address there, the one that proxy added. The last `X-Forwarded-Proto` (`http` or
  `https`) from a trusted proxy is the request's scheme.
- Loopback is always trusted. A host name in `KnownProxies` is looked up again at most every 30 seconds, so a proxy
  that starts later or moves is trusted without a restart; one that can't be looked up trusts nobody. A network that
  doesn't parse is ignored.
- A product service records this address and passes it on in `Bitwarden-Context`, which it refuses from a client, so a
  client can't choose the address its actions are recorded with.

### Notice a call back into the service

Calls between services carry `Bitwarden-Call-Chain`, the services a request passed through, believed only from another
service. A call whose chain already names this service is a re-entry: a synchronous loop-back while one of its own
requests waits, holding whatever lock or transaction it took. `UseCallChainCheck()` counts it in
`bitwarden.call.reentries`, tags the span `bitwarden.call.reentry`, logs a warning and, with `CallChain:RefuseReentry`,
refuses it `500`. Turn that on in tests and Development, so a new loop-back fails there. Identity's token endpoint and
the health, readiness and startup probes are never a re-entry.

Remove a loop-back where you can. Where it must stay, declare it on the handler with the reason; it is then counted and
logged at information, never refused:

```csharp
using Bitwarden.Server.Sdk.Authorization;

public static class WidgetTransferEndpoints
{
    [AcceptsReentry("The transfer needs the owner's current limits before it can write.")]
    public static void Transfer(string id)
    {
    }
}
```

### Validate a token from another issuer

`DiscoveredIssuerTokens` verifies an access token another OpenID Connect issuer signed: a key its discovery document
publishes, the issuer it names, the lifetime with five minutes' skew, and the type `at+jwt`:

```csharp
using Bitwarden.Server.Sdk.Authorization;

public sealed class PartnerTokens(IHttpClientFactory clients)
{
    // One per issuer: it reads the discovery document and keys once, and refreshes them.
    private readonly DiscoveredIssuerTokens _issuer =
        new(new Uri("https://identity.partner.example"), clients.CreateClient("partner-identity"));

    public async Task<string?> SubjectAsync(string token, CancellationToken cancellationToken)
    {
        var result = await _issuer.ValidateAsync(token, cancellationToken);
        return result.IsValid ? result.ClaimsIdentity.FindFirst("sub")?.Value : null;
    }
}
```

It doesn't check the audience. If the issuer sets one, compare `aud` with the value you expect before trusting the
token.

### Keep the security guarantees

- **Strip the context headers at the proxy.** Every proxy must remove `BitwardenHeaders.All` from what clients send. A
  product service refuses them anyway; a domain service reached through a public entry point relies on the proxy.
- **Name only the proxies in front of the service**, and keep `KnownNetworks` as narrow as their network. Each must add
  the address it took the request from last in `X-Forwarded-For`. Behind a proxy that isn't trusted, every request
  carries the proxy's address.
- **A context is not a permission.** It says whom a call concerns. An asserted context is only the calling service's
  word, so a service still enforces its own access rules.
- **Tokens and contexts carry identifiers and scopes only.** Never put a key or an encrypted value in a context. Vault
  Data reaches a service only as Protected Data, in request bodies, and never in a header that reaches logs;
  authorization never reads it.

## Types

| Type                                                                                                                                                 | Purpose                                                                    |
| ---------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| `AuthorizationAttribute`, `AuthorizationDefaults`, `ContextRequirement`                                                                              | What an operation demands: a scope, and an organization, user and provider |
| `OperationAuthorization`                                                                                                                             | Settles an operation's declarations into the one requirement checked       |
| `OrganizationRole`, `OrganizationPermission`, `ProviderRole`, `ICallerRole`                                                                          | Roles and permissions from a person's session (product services)           |
| `AnyScopeAttribute`, `CallerScopes`                                                                                                                  | An operation reachable with any one of several scopes                      |
| `AcceptsOrganizationCredentialAttribute`, `AcceptsSendCredentialAttribute`, `SendCredential`                                                         | Credentials other than a person's session (product services)               |
| `NamesOrganizationAttribute`, `NamesProviderAttribute`, `CreatesOrganizationAttribute`, `INamesOrganization`, `INamesProvider`, `RequestNamedOwners` | Owners an operation names that the session doesn't (product services)      |
| `AcceptsAssertedContextAttribute`                                                                                                                    | Callers whose own word for a person an operation accepts (domain services) |
| `InternalCaller`                                                                                                                                     | Whether the caller is another service                                      |
| `DiscoveredIssuerTokens`                                                                                                                             | Verifies an access token another OpenID Connect issuer signed              |
| `CallChainOptions`, `AcceptsReentryAttribute`, `CallReentryException`                                                                                | Calls back into a service while its own request waits                      |
