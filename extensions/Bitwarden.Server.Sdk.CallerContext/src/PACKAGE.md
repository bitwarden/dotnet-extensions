# Bitwarden.Server.Sdk.CallerContext

## About

Who and what a call between Bitwarden services concerns, and how the call carries it: `IContext` (the organization,
user and provider an operation is about, and where the request came from), `ContextScope` for work that arrives
without a request, and the headers, formats and token names that carry the context from one service to the next.

It holds only what a service and the services calling it both need, and has no dependencies. A service host fills
`IContext` from a request, and a client writes the headers on a service's outgoing calls.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.CallerContext
```

## Configuration

The package reads no settings.

## Getting started

Take `IContext` and read what you need. A service host registers it, reading the request when there is one and the
current `ContextScope` otherwise.

```csharp
using Bitwarden.Server.Sdk.CallerContext;

public sealed class WidgetQueries(IContext context, IWidgetStore store)
{
    public Task<IReadOnlyList<Widget>> ListAsync(CancellationToken cancellationToken) =>
        context.OrganizationId is { } organization
            ? store.ListForOrganizationAsync(organization, cancellationToken)
            : store.ListForUserAsync(context.UserId ?? throw new InvalidOperationException("Nobody is named."), cancellationToken);
}
```

The organization and the user say what a request concerns. They are never permission to do it: the service still
enforces its own access rules. See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.CallerContext/src/README.md)
for a complete program.

## Documentation

### Do work that didn't come from a request

A job or a message has no request to say whom it concerns. State it for the length of the work:

```csharp
using Bitwarden.Server.Sdk.CallerContext;

public sealed class WidgetJob(IWidgetBilling billing)
{
    public async Task RunAsync(Guid organization, CancellationToken cancellationToken)
    {
        using (ContextScope.Begin(new StatedContext { OrganizationId = organization }))
        {
            await billing.RecalculateAsync(organization, cancellationToken);
        }
    }
}
```

Don't do this while handling a request. There, the context comes from the request, and a scope would let code name an
organization the caller never did.

### Carry the context between services

`BitwardenHeaders.All` lists the five caller-context headers: `Bitwarden-Organization`, `Bitwarden-User`,
`Bitwarden-Provider`, `Bitwarden-Context` and `Bitwarden-Delegation`. `CallerContextHeader` writes and strictly reads
the `Bitwarden-Context` object. `CallerContextToken` names the token type (`bitwarden-context+jwt`), claims and modes
(`DELEGATED`, `ASSERTED`) of the signed form. `Bitwarden-Call-Chain` (`CallChain`) carries the services a request
passed through, so a service notices being called back.

- Context is not authentication. A service believes the headers only as the signed token says, and only from another
  service. Every proxy in front of a service must strip them from what clients send.
- `Credential`, `RecordedUserId` and `RecordedProviderId` are audit-only, and `UserOrganizationIds` is for feature flag
  targeting only. None of them may drive authorization, a row filter or a cache.
- The context carries identifiers only, never a key or an encrypted value.

### Types

| Type                                                | Purpose                                                                |
| --------------------------------------------------- | ---------------------------------------------------------------------- |
| `IContext`                                          | Who and what the current operation concerns                            |
| `ContextScope`, `StatedContext`                     | The context of work that has no request                                |
| `BitwardenHeaders`                                  | The headers that carry caller context between services                 |
| `CallerContextHeader`, `CallerContextValue`         | Writes and strictly reads the `Bitwarden-Context` object               |
| `CallerCredential`, `CallerCredentialJsonConverter` | The kind of credential that started a chain of calls, written ALL_CAPS |
| `CallerContextToken`, `CallerContextAssertion`      | The signed form of a call's context, and what one call asserts         |
| `ICallerProof`                                      | The token a call's signed context is exchanged from                    |
| `CallChain`                                         | The services a request passed through, to notice a call back           |
| `IRequestNamedOwner`, `IOrganizationCreation`       | Owners an operation names itself, for its outgoing calls               |
| `IServiceIdentity`, `TokenAudience`                 | Which service this is, and which token audience it accepts             |
