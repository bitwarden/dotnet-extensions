# Bitwarden.Server.Sdk.Context

**Audience:** engineers writing a Bitwarden service, or a package for one, that needs to know whom an operation
concerns or carries that across a call to another service.

## About

Who and what a call between Bitwarden services concerns, and how the call carries it: `IContext` (the organization,
user and provider an operation is about, and where the request came from), `ContextScope` for work that arrives
without a request, and the headers, formats and token names that carry the context from one service to the next. It has no dependencies.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Context
```

## Key concepts

- **Context is not authentication.** Who the caller is comes from its token. The organization and the user say only
  what a call concerns, never permission to do it: the service still enforces its own access rules. A service
  believes the headers only as the signed `Bitwarden-Delegation` token (`ContextToken`) says.
- **Where it comes from.** A service host registers `IContext`, reading the request when there is one and the current
  `ContextScope` otherwise, and a client writes the headers on a service's outgoing calls. Any member may be absent.
- **A scope flows on the execution context,** so everything the work calls, however deep, sees it, and nothing outside
  it does. Disposing it restores the scope that was current before. Work started inside it and not awaited keeps the
  context it started with.

## Usage

### Find out whom a request concerns

Take `IContext` and read what you need. In `WidgetQueries.cs`:

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

### Do work that didn't come from a request

A job or a message has no request to say whom it concerns. State it for the length of the work. In `WidgetJob.cs`:

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

A host handing an event to a service's handlers begins its scope with `ContextScope.BeginForEvent`. The context it
establishes reports `IContext.IsEvent`, and so does `ContextScope.IsEvent`: the person in `UserId` is the event's
actor, there for the record only, and a client can hold a handler's calls to that. A scope begun inside it with `Begin`
and a `StatedContext` is not an event's. No header carries `IsEvent`, so a request's context is never an event's.

### Write and read the `Bitwarden-Context` header

`ContextHeader` writes the object the header carries, and reads it strictly: an unknown member, a repeated one, a
value of the wrong shape, a version other than `ContextHeader.Version`, or anything longer than 16,384 bytes is
refused with a sentence naming the member, never the value. In `ContextHeaderExample.cs`:

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

Besides where the request came from and its credential, the object carries what feature flags are evaluated for: the
party (`FlagKind`, `USER` by default and then left out, and `FlagKey`), its organizations, and the client's version
(`ClientVersion`, with `ClientVersionIsPrerelease` written only as `true`).

### Notice a call back into this service

`CallChain` carries the services a request passed through, oldest first, in `Bitwarden-Call-Chain`. A service whose
own name is already in the chain is being called back while its own request waits. A host enters the chain a request
arrived with (`CallChain.Enter`), and a client sends it on, with the calling service added, on every call. In
`LoopBack.cs`:

```csharp
using Bitwarden.Server.Sdk.Context;

public static class LoopBack
{
    public static bool IsReentry(string? header, string self) =>
        CallChain.TryParse(header, out var chain) && chain.Contains(self, StringComparer.Ordinal);

    public static string Outgoing(string self) => CallChain.Format(CallChain.Append(CallChain.Current, self));
}
```

## Gotchas and constraints

- **Audit and targeting fields don't authorize.** `Credential`, `RecordedUserId` and `RecordedProviderId` are for
  audit records, and `FlagSubjectKind`, `FlagSubjectKey`, `UserOrganizationIds`, `ClientVersion` and
  `ClientVersionIsPrerelease` are for feature flag targeting only. None of them may drive authorization, a row filter
  or a cache. `DeviceIdentifier` is never proof of anything either.
- **Don't begin a `ContextScope` while handling a request.** There, the context comes from the request, and a scope
  would let code name an organization the caller never did.
- **`ContextHeader.Write` throws `ArgumentException`** for a value `TryParse` would refuse, such as more than 300
  organizations: leave `Organizations` out for a person in more. A service host's own clients write the header and the
  host reads it, so write it yourself only for a client of your own.
- **A chain holds names shaped like a service's,** at most 16 of them. A value with anything else in it, such as a
  GUID, an address or free text, is not a chain: `TryParse` refuses it, `Enter` and `Append` read it as none, and
  `Format` writes an empty string for it. The shape refuses a GUID written as 32 hexadecimal digits or in hyphenated
  form, alone or inside a name, but not identifiers in general: a person's name or a username of the same shape
  passes. Checking each name against the services it knows, before writing a chain that arrived from another
  service to a span, a metric tag or a log, is the host's responsibility; this package checks the shape only. The
  chain is not signed and grants nothing.
- **Keeping the context trustworthy is the service's job.** What a service must do, such as stripping the headers at
  its proxy, is in Bitwarden.Server.Sdk.Authorization's
  [Gotchas and constraints](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Authorization/src/PACKAGE.md#gotchas-and-constraints).

## Types

| Type                                                | Purpose                                                                |
| --------------------------------------------------- | ---------------------------------------------------------------------- |
| `IContext`                                          | Who and what the current operation concerns                            |
| `ContextScope`, `StatedContext`                     | The context of work that has no request                                |
| `BitwardenHeaders`                                  | The headers that carry context between services                        |
| `ContextHeader`, `ContextValue`                     | Writes and strictly reads the `Bitwarden-Context` object               |
| `CallerCredential`, `CallerCredentialJsonConverter` | The kind of credential that started a chain of calls, written ALL_CAPS |
| `FlagSubjectKind`                                   | The kind of party feature flags are evaluated for, written ALL_CAPS    |
| `ContextToken`, `ContextAssertion`                  | The signed form of a call's context, and what one call asserts         |
| `ICallerProof`                                      | The token a call's signed context is exchanged from                    |
| `CallChain`                                         | The services a request passed through, to notice a call back           |
| `IRequestNamedOwner`, `IOrganizationCreation`       | Owners an operation names itself, for its outgoing calls               |
| `IServiceIdentity`, `TokenAudience`                 | Which service this is, and which token audience it accepts             |
