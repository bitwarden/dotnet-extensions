# Bitwarden.Server.Sdk.CallerContext

Who and what a call between Bitwarden services concerns, and how the call carries it.

A request is about someone: an organization, a person, a provider, and the client and address it came from. A service
reads that through `IContext`, whether it came from a person's session, from another service's signed call, or from a
message taken off a queue. This package holds `IContext` and the headers, token names and formats that carry it from
one service to the next, so the service that writes them and the service that reads them agree.

- **One view of the caller.** `IContext` says which organization, user and provider an operation concerns, and, for
  audit records, where the request came from and what credential started it.
- **Work without a request.** `ContextScope` and `StatedContext` state the context for a background job or a message.
- **The headers between services.** `BitwardenHeaders` names them. `CallerContextHeader` writes and strictly reads the
  `Bitwarden-Context` object (`CallerContextValue`), refusing anything it doesn't recognize.
- **The signed form.** `CallerContextToken` names the token type, claims and modes of the short-lived token that signs
  a call's context for one caller and one callee.
- **Loop-backs.** `CallChain` carries the services a request passed through, so a service notices being called back
  while its own request waits.
- **Which service this is.** `IServiceIdentity` names the service and the token audience it accepts (`TokenAudience`).

## What belongs here

Only what both a service and the services that call it need. Nothing here touches `HttpContext`: a service host fills
`IContext` from the request, and a client writes the headers on the calls it sends. `IRequestNamedOwner`,
`IOrganizationCreation` and `ICallerProof` are defined here for a host to implement, so a client can ask about the
request without referencing the code that hosts the service.

The package has no dependencies.

## Getting started

```bash
dotnet new console -n WidgetJobs
cd WidgetJobs
dotnet add package Bitwarden.Server.Sdk.CallerContext --prerelease
```

```csharp
using Bitwarden.Server.Sdk.CallerContext;

var organization = Guid.Parse("4b1f0e6a-2c3d-4e5f-8a9b-0c1d2e3f4a5b");

// A job has no request to say whom it is for, so it states that itself for the length of the work.
using (ContextScope.Begin(new StatedContext { OrganizationId = organization }))
{
    await RecalculateAsync();
}

Console.WriteLine(ContextScope.Current is null ? "No context outside the scope." : "Still in a scope.");

static Task RecalculateAsync()
{
    // Everything the work calls, however deep, sees the same context.
    Console.WriteLine($"Recalculating for organization {ContextScope.Current?.OrganizationId}.");
    return Task.CompletedTask;
}
```

`dotnet run` prints the organization inside the scope, then `No context outside the scope.`

## Examples

### Read whom a request concerns

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
enforces its own access rules.

### Write and read the `Bitwarden-Context` header

`CallerContextHeader` writes the object the header carries, and reads it strictly: an unknown member, a repeated one,
a value of the wrong shape, or anything longer than 16,384 bytes is refused with a sentence naming the member, never
the value:

```csharp
using System.Net;
using Bitwarden.Server.Sdk.CallerContext;

public static class ContextHeaderExample
{
    public static string Write(IPAddress client) =>
        CallerContextHeader.Write(new CallerContextValue
        {
            ClientIp = client.ToString(),
            DeviceType = "Android",
            Credential = CallerCredential.User,
        });

    public static CallerContextValue? Read(string header) =>
        CallerContextHeader.TryParse(header, out var value, out var problem)
            ? value
            : throw new InvalidOperationException($"The caller context was refused: {problem}");
}
```

A service host's own clients write it and the host reads it, so write it yourself only for a client of your own.

### Notice a call back into this service

`CallChain` carries the services a request passed through, oldest first. A service whose own name is already in the
chain is being called back while its own request waits. A host enters the chain a request arrived with
(`CallChain.Enter`), and a client sends it on, with the calling service added, on every call:

```csharp
using Bitwarden.Server.Sdk.CallerContext;

public static class LoopBack
{
    public static bool IsReentry(string? header, string self) =>
        CallChain.TryParse(header, out var chain) && chain!.Contains(self, StringComparer.Ordinal);

    public static string Outgoing(string self) => CallChain.Format(CallChain.Append(CallChain.Current, self));
}
```

A chain holds service names only, at most 16 of them, so it can go into logs and metric tags. A value with anything
else in it, such as an identifier or free text, is not a chain: `TryParse` refuses it, `Enter` and `Append` read it as
none, and `Format` writes an empty string for it.

## Configuration

The package reads no settings.

## Security

- **Context is not authentication.** Who the caller is comes from its token. The headers say only whom a call
  concerns, and a service believes them only as the signed `Bitwarden-Delegation` token says, and only from another
  service. A context a service asserted on its own authority is only that service's word.
- **Strip the headers at the edge.** Every proxy in front of a service must remove `BitwardenHeaders.All` from what
  clients send.
- **Audit and targeting members grant nothing.** `Credential`, `RecordedUserId` and `RecordedProviderId` are for audit
  records, and `UserOrganizationIds` is for feature flag targeting only. None of them may drive authorization, a row
  filter or a cache.
- **Identifiers only.** The context and its token carry identifiers, never a key or an encrypted value. Vault Data
  moves between services only as Protected Data, in request bodies, and never in a header that reaches logs.

## Requirements

- .NET 10.
- No other packages.

## Contributing

Every source file with logic has a matching test class in
[`tests/Bitwarden.Server.Sdk.CallerContext.Tests`](../tests/Bitwarden.Server.Sdk.CallerContext.Tests). Run them from
the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.CallerContext/tests/Bitwarden.Server.Sdk.CallerContext.Tests
```

A change must keep:

- **The wire stable.** Header names, the `Bitwarden-Context` members and version, the token's type, claims and modes,
  and the `CallerCredential` names are read by services deployed apart from each other. A new member needs a new
  version that every reader accepts first.
- **Strict reading.** A malformed value is refused whole, never partly believed, and a refusal never echoes the value.
- **No dependencies, and nothing about the transport.** No `HttpContext` or `HttpClient`.
