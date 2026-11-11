# Bitwarden.Server.Sdk.Authorization

Decide who may call a Bitwarden service's operation, on whose behalf, and for which owner.

Every request to a Bitwarden service concerns someone: an organization, a person, a provider. A product service, which
people call with their own session, reads that from the session. A domain service, which only other services call,
reads it from a context Identity signed for that one call. This package fills `IContext` from either, refuses a context
it can't believe, and checks each operation's declared requirements against it, so an operation is closed until
someone opens it.

- **Closed by default.** A fallback policy authenticates every operation that doesn't say otherwise.
- **Declared requirements.** `[Authorization]` and `AuthorizationDefaults` say which scope an operation needs and
  whether it requires, accepts or forbids an organization, a user and a provider; on a product service, also a role or
  a permission.
- **Context you can believe.** At a domain service, the context headers count only from another service,
  and only as its signed context token says. At a product service, a request carrying them is refused.
- **Audiences always checked.** A token is accepted only where its `aud` names the service.
- **Trusted proxies only.** A request's address and scheme come from a forwarded header only when a trusted proxy sent
  it.
- **Loop-backs noticed.** A call back into a service while its own request waits is logged and counted, and can be
  refused.
- **Standard refusals.** A refused caller gets `401` or `403` with the standard error document, saying which
  requirement was not met.

Bitwarden.Server.Sdk.Service registers and orders all of this for every service, and Bitwarden.Server.Sdk.Resources
settles each operation's `[Authorization]` as it maps it. Reference this package directly only to compose a host
yourself, or to declare an operation's requirements in a library.

## Getting started

A host that composes the package itself registers it, then adds its middleware in the order Bitwarden.Server.Sdk.Service
uses:

```bash
dotnet new web -n WidgetService
cd WidgetService
dotnet add package Bitwarden.Server.Sdk.Authorization --prerelease
```

```csharp
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;

// A domain service, which other services call. A product service, which people call with their own session, is true.
var isProductService = false;

var builder = WebApplication.CreateBuilder(args);

// Bitwarden.Server.Sdk.Service registers these two for every service; the authorization reads both.
builder.Services.AddSingleton<IServiceIdentity>(new WidgetServiceIdentity());
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

internal sealed class WidgetServiceIdentity : IServiceIdentity
{
    public string Name => "widget-service";

    public string Title => "Widget Service";
}
```

Point the bearer handler at the Identity service that issues your tokens, in `appsettings.json`:

```json
{
    "Authentication": {
        "Schemes": {
            "Bearer": {
                "Authority": "https://identity.example.com"
            }
        }
    }
}
```

`dotnet run`, then `GET /api/v1/context` without a token answers `401` with an error document whose code is
`unauthenticated`. With a token issued for `widget-service`, it answers the organization and user the call concerns.

Bitwarden.Server.Sdk.Service adds its exception handler, status pages, CORS and per-address rate limits between
`UseForwardedClientAddress()` and the rest, its feature flag checks and idempotency keys after `UseCallChainCheck()`.

## Examples

### Declare what an operation demands

An `[Authorization]` takes effect once it is settled: the service's defaults, then the handler's class, then the
handler, the most specific winning, into the one requirement the pipeline checks. Bitwarden.Server.Sdk.Resources does
this for every operation mapped with `WithStandards()`, and also refuses at startup a declaration that contradicts
itself. A host without it settles each endpoint with `OperationAuthorization`:

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
  `widgets.admin` and an organization: a caller without them is refused `403`, saying which. `Ping` is open.
- A role, a permission and `Anonymous` count on the handler only, never inherited from its class.
- An `[Authorization]` that is never settled demands nothing beyond the fallback policy's authenticated caller.
- `OrganizationRole`, `Permission` and `ProviderRole` come from a person's session, so they work on a product service
  only. On a domain service `ICallerRole` answers `None`, and an operation demanding a role refuses every call.

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

## Developer guide

Bitwarden.Server.Sdk.Service's guide covers writing a service with this package:

- [Authentication and authorization](../../Bitwarden.Server.Sdk.Service/docs/running-a-service/authentication-and-authorization.md):
  defaults, declarations, roles and permissions, and the declarations for credentials and owners an operation names.
- [Context](../../Bitwarden.Server.Sdk.Service/docs/running-a-service/context.md): what each kind of
  service believes, the signed context token, and what a domain service refuses.
- [Network and request limits](../../Bitwarden.Server.Sdk.Service/docs/running-a-service/network-and-request-limits.md):
  trusted proxies.

## Configuration

| Setting                          | Default | What it does                                                                                                                                                         |
| -------------------------------- | ------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ForwardedHeaders:KnownProxies`  | None    | Comma-separated addresses or host names of proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are believed.                                                     |
| `ForwardedHeaders:KnownNetworks` | None    | Comma-separated CIDR networks of such proxies.                                                                                                                       |
| `CallChain:RefuseReentry`        | `false` | When true, a call back into the service while its own request waits is refused (`500`) rather than logged and counted, unless the operation says `[AcceptsReentry]`. |

The bearer handler's own settings, `Authentication:Schemes:Bearer:*`, are read by Bitwarden.Server.Sdk.Authentication.

## Security

- **Trusted proxies choose the address.** A request's address is recorded and rate-limited as a trusted proxy reports
  it. Name only the proxies in front of the service, and keep `KnownNetworks` as narrow as their network. Loopback is
  always trusted. A host name is looked up again at most every 30 seconds, and one that can't be looked up trusts
  nobody.
- **Strip the context headers at the proxy.** Every proxy must remove `BitwardenHeaders.All` from what clients
  send. A product service refuses them anyway; a domain service reached through a public entry point relies on the
  proxy.
- **A context is not a permission.** It says whom a call concerns. An asserted context is only the calling service's
  word, so a service still enforces its own access rules.
- **Tokens carry identifiers and scopes, never keys.** Authorization never reads Vault Data, which reaches a service
  only as Protected Data.

## Requirements

- .NET 10 and ASP.NET Core.
- Bitwarden.Server.Sdk.Authentication, Bitwarden.Server.Sdk.Context and Bitwarden.Server.Sdk.Errors.
- An `IServiceIdentity` and a `TimeProvider` registration, which Bitwarden.Server.Sdk.Service provides.

## Contributing

Every type with logic has a test class of the same name in the package's `tests` folder. Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Authorization/tests/Bitwarden.Server.Sdk.Authorization.Tests
```

The middleware is also tested inside a whole service host, in Bitwarden.Server.Sdk.Service's tests. A change must
keep:

- **Closed by default.** An operation that says nothing is authenticated, and a requirement that can't be checked is
  refused, never skipped.
- **Refusal before belief.** A context that is malformed, unsigned, or disagrees with its token is refused whole.
- **One settled requirement per operation.** Declarations compose by overriding, never by adding requirements.
- **No secret in a refusal.** A message names the requirement or the header at fault, never a token or its claims.
