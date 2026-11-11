# Bitwarden.Server.Sdk.Authorization

## About

Context authorization for Bitwarden services: who may call an operation, on whose behalf, and for which owner.
It fills `IContext` from a person's session at a product service, or from the context Identity signed for one call at
a domain service; refuses a context it can't believe; checks each operation's `[Authorization]` against it; and closes
every operation that doesn't say otherwise.

Bitwarden.Server.Sdk.Service registers and orders it for every service, and Bitwarden.Server.Sdk.Resources settles
each operation's `[Authorization]` as it maps it. It depends on ASP.NET Core, Bitwarden.Server.Sdk.Authentication,
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

A trusted proxy chooses the address a request is recorded and rate-limited under, so name only the proxies in front
of the service. See
[Network and request limits](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/network-and-request-limits.md).
The bearer handler's settings, `Authentication:Schemes:Bearer:*`, are read by Bitwarden.Server.Sdk.Authentication.

## Getting started

A host that composes the package itself, rather than building on Bitwarden.Server.Sdk.Service, registers it and adds
its middleware in the order Service uses:

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

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/README.md)
for settling an operation's `[Authorization]` without Bitwarden.Server.Sdk.Resources.

## Documentation

- [Authentication and authorization](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/authentication-and-authorization.md):
  defaults, declarations, roles and permissions, and tokens from another issuer.
- [Context](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Service/docs/running-a-service/context.md):
  what each kind of service believes, the signed context token, and what a domain service refuses.

### What the middleware does

| Middleware                    | Where                                   | What it does                                                                                                                                                                                                                     |
| ----------------------------- | --------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `UseForwardedClientAddress()` | Before anything reads the connection    | Takes the address and scheme from `X-Forwarded-For` and `X-Forwarded-Proto` only when a trusted proxy sent them.                                                                                                                 |
| `UseContextRefusal()`         | Product services, before authentication | Refuses `400` a request carrying any context header, whoever sends it.                                                                                                                                                           |
| `UseContextHeaders()`         | Domain services, after authentication   | Believes another service's context only as its signed token says: `401` for a missing or forged token, `400` for a malformed or disagreeing context, `403` for an asserted person at an operation that doesn't admit the caller. |
| `UseCallChainCheck()`         | After authorization                     | Keeps the chain of services a call passed through, and notices a call back into this service.                                                                                                                                    |

### Types

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
