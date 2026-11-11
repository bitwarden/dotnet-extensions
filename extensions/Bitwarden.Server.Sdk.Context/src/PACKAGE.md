# Bitwarden.Server.Sdk.Context

## About

Who and what a call between Bitwarden services concerns, and how the call carries it: `IContext` (the organization,
user and provider an operation is about, and where the request came from), `ContextScope` for work that arrives
without a request, and the headers, formats and token names that carry the context from one service to the next.

It has no dependencies. A service host fills `IContext` from a request, and a client writes the headers on a
service's outgoing calls.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Context
```

## Usage

### Find out whom a request concerns

Take `IContext` and read what you need. A service host registers it, reading the request when there is one and the
current `ContextScope` otherwise:

```csharp
using Bitwarden.Server.Sdk.Context;

public sealed class WidgetQueries(IContext context, IWidgetStore store)
{
    public Task<IReadOnlyList<Widget>> ListAsync(CancellationToken cancellationToken) =>
        context.OrganizationId is { } organization
            ? store.ListForOrganizationAsync(organization, cancellationToken)
            : store.ListForUserAsync(
                context.UserId ?? throw new InvalidOperationException("Nobody is named."), cancellationToken);
}
```

The organization and the user say what a request concerns. They are never permission to do it: the service still
enforces its own access rules. Any member may be absent.

`Credential`, `RecordedUserId` and `RecordedProviderId` are for audit records, and `UserOrganizationIds` is for
feature flag targeting only. None of them may drive authorization, a row filter or a cache. `DeviceIdentifier` is
never proof of anything either.

### Do work that didn't come from a request

A job or a message has no request to say whom it concerns. State it for the length of the work:

```csharp
using Bitwarden.Server.Sdk.Context;

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

The scope flows on the execution context, so everything the work calls, however deep, sees it, and nothing outside
it does. Disposing it restores the scope that was current before.

Don't do this while handling a request. There, the context comes from the request, and a scope would let code name an
organization the caller never did.

A host handing an event to a service's handlers begins its scope with `ContextScope.BeginForEvent`, and
`ContextScope.IsEvent` says the innermost scope is one: the person it names is the event's actor, there for the record
only, and a client can hold a handler's calls to that. A scope begun inside it with `Begin` is not an event's.

### Write and read the `Bitwarden-Context` header

`ContextHeader` writes the object the header carries, and reads it strictly: an unknown member, a repeated one, a
value of the wrong shape, a version other than `ContextHeader.Version`, or anything longer than 16,384 bytes is
refused with a sentence naming the member, never the value:

```csharp
using System.Net;
using Bitwarden.Server.Sdk.Context;

public static class ContextHeaderExample
{
    public static string Write(IPAddress client) =>
        ContextHeader.Write(new ContextValue
        {
            ClientIp = client.ToString(),
            DeviceType = "Android",
            Credential = CallerCredential.User,
        });

    public static ContextValue? Read(string header) =>
        ContextHeader.TryParse(header, out var value, out var problem)
            ? value
            : throw new InvalidOperationException($"The context was refused: {problem}");
}
```

`Write` throws `ArgumentException` for a value `TryParse` would refuse, such as more than 300 organizations: leave
`Organizations` out for a person in more. A service host's own clients write the header and the host reads it, so
write it yourself only for a client of your own.

### Notice a call back into this service

`CallChain` carries the services a request passed through, oldest first, in `Bitwarden-Call-Chain`. A service whose
own name is already in the chain is being called back while its own request waits. A host enters the chain a request
arrived with (`CallChain.Enter`), and a client sends it on, with the calling service added, on every call:

```csharp
using Bitwarden.Server.Sdk.Context;

public static class LoopBack
{
    public static bool IsReentry(string? header, string self) =>
        CallChain.TryParse(header, out var chain) && chain!.Contains(self, StringComparer.Ordinal);

    public static string Outgoing(string self) => CallChain.Format(CallChain.Append(CallChain.Current, self));
}
```

A chain holds service names only, at most 16 of them, so it can go into logs and metric tags. A value with anything
else in it, such as an identifier or free text, is not a chain: `TryParse` refuses it, `Enter` and `Append` read it as
none, and `Format` writes an empty string for it. The chain is not signed and grants nothing.

### Keep the context trustworthy

Context is not authentication. Who the caller is comes from its token; the headers say only whom a call concerns, and
a service believes them only as the signed `Bitwarden-Delegation` token (`ContextToken`) says. What a service must do to
keep that true, such as stripping the headers at its proxy, is in Bitwarden.Server.Sdk.Authorization's
[Keep the security guarantees](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md#keep-the-security-guarantees).

## Types

| Type                                                | Purpose                                                                |
| --------------------------------------------------- | ---------------------------------------------------------------------- |
| `IContext`                                          | Who and what the current operation concerns                            |
| `ContextScope`, `StatedContext`                     | The context of work that has no request                                |
| `BitwardenHeaders`                                  | The headers that carry context between services                        |
| `ContextHeader`, `ContextValue`                     | Writes and strictly reads the `Bitwarden-Context` object               |
| `CallerCredential`, `CallerCredentialJsonConverter` | The kind of credential that started a chain of calls, written ALL_CAPS |
| `ContextToken`, `ContextAssertion`                  | The signed form of a call's context, and what one call asserts         |
| `ICallerProof`                                      | The token a call's signed context is exchanged from                    |
| `CallChain`                                         | The services a request passed through, to notice a call back           |
| `IRequestNamedOwner`, `IOrganizationCreation`       | Owners an operation names itself, for its outgoing calls               |
| `IServiceIdentity`, `TokenAudience`                 | Which service this is, and which token audience it accepts             |
