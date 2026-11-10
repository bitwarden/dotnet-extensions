# Bitwarden.Server.Sdk.WriteSpans.Abstractions

Notice an operation whose writes span services, and say how it handles a failure part way.

Each Bitwarden service has a store of its own, so no transaction surrounds an operation that writes in two of them. An
operation (an endpoint, a message handler or a job) that makes two or more writes, at least one of them to another
service called synchronously, has to say how it survives a failure between those writes. Two writes to the same other
service count too: a failure between them leaves that service half done. This package
counts an operation's writes and carries that declaration.

- **The count.** `ServiceWrites.Begin(operation, declared, guard)` starts counting the writes of the operation running
  now. `ServiceWrites.Outgoing(service, idempotencyKey)` records a write sent to another service, once per key, and
  `ServiceWrites.Saving()` a save to the service's own database. Two or more, one of them to another service, and the
  operation spans services.
- **The declaration.** `[SpansServices(shape, reason)]` names how the operation handles a failure part way, with one
  or more `SpanShape` values. `[SpansServicesUnhandled(reason)]` admits that it doesn't yet.
- **The guard.** `IServiceWritesGuard.Spanned` is told before the write that makes an operation span services, and may
  refuse it by throwing.
- **Telling reads from writes.** `[SendsWrites(false)]` marks a client method that only reads where its name says
  otherwise. `ReadingRoutes.Reads` says whether a write-method address only reads (`.../actions/check`, `...-previews`,
  `validateOnly=true`). The verb names the read and the rule trusts it, so a write named with a reading verb
  (`.../actions/resolve-and-refund`) counts as a read.

## What belongs here

Only what both the side that sends a write and the side that runs the operation need. Nothing here touches
`HttpContext` or `HttpClient`, and nothing here decides what an undeclared span means: a host begins an operation
around each endpoint, message handler and job and provides the guard, and its clients and data layer report the
writes they make. The package has no dependencies.

## Getting started

```bash
dotnet new console -n CountedWrites
cd CountedWrites
dotnet add package Bitwarden.Server.Sdk.WriteSpans.Abstractions --prerelease
```

```csharp
using Bitwarden.Server.Sdk.WriteSpans;

using (var operation = ServiceWrites.Begin("invite-member", declared: null, guard: new PrintingGuard()))
{
    ServiceWrites.Saving();
    ServiceWrites.Outgoing("vault-service", idempotencyKey: "key-1");
    ServiceWrites.Outgoing("vault-service", idempotencyKey: "key-1");

    Console.WriteLine($"Steps: {string.Join(", ", operation.Steps)}; spans services: {operation.Spans}");
}

sealed class PrintingGuard : IServiceWritesGuard
{
    public void Spanned(string operation, IReadOnlyList<string> steps, SpanDeclarationAttribute? declared) =>
        Console.WriteLine($"{operation} spans services, declared: {declared is not null}");
}
```

`dotnet run` prints that `invite-member` spans services, undeclared, then its two steps: `database` and
`vault-service`. The repeated key is one write. A host can do this for you: begin an operation around each endpoint,
handler and job, and have its clients and data layer report their writes.

## Examples

### Declare how an operation handles a failure part way

```csharp
using Bitwarden.Server.Sdk.WriteSpans;

public static class MemberEndpoints
{
    [SpansServices(SpanShape.Compensated, "Each grant made is revoked, newest first, when a later one fails.")]
    public static async Task InviteMember(IMemberCalls members, CancellationToken cancellationToken)
    {
        await members.CreateMembershipAsync(cancellationToken);
        await members.GrantCollectionAsync(cancellationToken);
    }
}
```

| Shape              | The operation                                                                                          |
| ------------------ | ------------------------------------------------------------------------------------------------------ |
| `Compensated`      | has an undo for each write already made, run newest first when a later one fails                       |
| `IrreversibleLast` | makes the step it can't undo (a payment, a cancellation, a mail) after every check and every other one |
| `RetrySafeByState` | writes whole values in an order that lets the same request, or the same message again, finish it       |
| `StageThenCommit`  | has each service stage its part, then one commit makes them take effect, rolling forward after it      |

- `[SpansServices]` goes on an endpoint's handler method, or on a message handler's, outbox handler's or job's class.
- Choose the shape the code really implements, and say how in the reason. The reason is required.
- A message published through the outbox after the commit is not a synchronous write and needs no declaration.

### Admit an operation that doesn't handle it yet

```csharp
using Bitwarden.Server.Sdk.WriteSpans;

[SpansServicesUnhandled("A failure after the seats are bought leaves them bought with no member; tracked as a defect.")]
public sealed class SeatPurchaseHandler
{
}
```

It is never a shape: the guard is still told every time the operation spans services, with this declaration, and
should report it as loudly as an undeclared one. Only a host that refuses undeclared operations lets it through.

### Mark a client method that only reads

A test host can stand in for another service with a double of its client, and then count writes by the client method's
name: `Get`, `List`, `Check`, `Find` and the other reading verbs read, and anything else writes. A method whose name
misleads says what it does:

```csharp
using Bitwarden.Server.Sdk.WriteSpans;

public interface IAccountCalls
{
    [SendsWrites(false)]
    Task<bool> VerifyPasswordAsync(Guid account, string serverHash, CancellationToken cancellationToken);
}
```

The request the client sends is unchanged.

## Configuration

The package reads no settings. Whether an undeclared operation is refused is the guard's choice, configured by the host
that provides it.

## Requirements

- .NET 10.
- No other packages.

## Contributing

Every source file with logic has a matching test class in
[`tests/Bitwarden.Server.Sdk.WriteSpans.Abstractions.Tests`](../tests/Bitwarden.Server.Sdk.WriteSpans.Abstractions.Tests).
Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.WriteSpans.Abstractions/tests/Bitwarden.Server.Sdk.WriteSpans.Abstractions.Tests
```

A change must keep:

- **Names only in the steps.** A step is a service's name or `database`, never a value the write carries.
- **A retry is one write.** A write is counted once per `Idempotency-Key`.
- **A refusal sticks.** Once the guard refuses a write, every later write of the operation is refused the same way,
  including one made by the operation's other work while the guard was deciding, and `ThrowIfRefused` throws it again,
  so an operation can't finish as if its refused write had been made.
- **No dependencies, and nothing about the transport.**
