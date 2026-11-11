# Bitwarden.Server.Sdk.Authorization

Context authorization for Bitwarden services: who may call an operation, on whose behalf, and for which owner. See
[PACKAGE.md](PACKAGE.md) for how to use it.

## What belongs here

Deciding whom a request concerns and whether its caller may invoke the operation: filling `IContext`, believing or
refusing the context headers, the requirements an operation declares and the checks behind them, trusted proxies and
the call chain. Validating the bearer token itself is Bitwarden.Server.Sdk.Authentication's; the error document is
Bitwarden.Server.Sdk.Errors'. Which rows a caller reaches is the service's, not this package's.

## How it works

- **Product or domain.** `AddCallerAuthorization(isProductService)` picks where the context comes from.
  `CurrentContext` reads the session at a product service and the verified context (`VerifiedContext`, put on the
  request by `ContextHeaders`) at a domain service. `CallerDelegation` verifies the context token. A product service
  also gets `PersonSessionTokens`, which fails a token without a GUID `sub` at authentication, and `RequestNamedOwner`.
- **Stateless singletons.** `IContext` and the other replaceable services are singletons, because a scoped instance
  captured by something longer-lived, such as an Entity Framework model, would answer for whichever request built it.
  The authorization handlers are scoped.
- **One settled requirement.** ASP.NET Core composes authorization requirements as _and_, so an operation could
  tighten its class but never relax it. `OperationAuthorization.Settle` folds the defaults, the class and the handler
  into one `AuthorizationAttribute` marked settled, and only a settled attribute yields a requirement
  (`GetRequirements`). `Apply` adds `AllowAnonymousAttribute` for `Anonymous`, because the authorization middleware
  challenges an unauthenticated caller before any requirement runs.
- **Audiences.** `ServiceAudience` post-configures the bearer options to add `IServiceIdentity.Audience` to the valid
  audiences and turn audience validation on, after any service's own configuration.
- **Middleware order.** `ContextRefusal` runs before authentication, so its answer doesn't depend on the token.
  `ContextHeaders` runs after authentication and before authorization. `CallChainCheck` runs after authorization, so
  an operation naming its own scheme has authenticated its caller before the chain is believed.
- **Refusals.** `AuthorizationResultHandler` and `ContextHeaders.RefuseAsync` write the standard error document with
  the host's JSON options, by reflection, which is why the package is not AOT-compatible.

## Testing

Every type with logic has a test class of the same name in `tests/Bitwarden.Server.Sdk.Authorization.Tests`. The
middleware is also tested inside a whole service host, in Bitwarden.Server.Sdk.Service's tests.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Authorization/tests/Bitwarden.Server.Sdk.Authorization.Tests
```

A change must keep:

- **Closed by default.** An operation that says nothing is authenticated, and a requirement that can't be checked is
  refused, never skipped.
- **Refusal before belief.** A context that is malformed, unsigned, or disagrees with its token is refused whole.
- **One settled requirement per operation.** Declarations compose by overriding, never by adding requirements.
- **No secret in a refusal.** A message names the requirement or the header at fault, never a token or its claims.
