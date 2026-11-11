# Bitwarden.Server.Sdk.Sagas

## About

Each Bitwarden service has a store of its own, so no transaction spans services. An operation (an endpoint, a message
handler or a job) that makes two or more writes, at least one of them to another service called synchronously, is a
saga: a failure between its writes leaves it part way done. Two writes to the same other service count too: a failure
between them leaves that service half done.

This package notices a saga and makes its operation declare how it handles a failure part way. It counts the writes of
the operation running now and carries that declaration. It does not run or orchestrate sagas: the operation's own code
makes each write, in the order and with the undo it declares.

It has no dependencies. In a service built on Bitwarden.Server.Sdk.Service, the SDK's Service, Messaging, Jobs, Data
and Client packages count the writes of endpoints, message handlers, outbox handlers and jobs for you. The Service
package's guard logs and counts every span, and refuses an undeclared one when `SpansServices:RefuseUndeclared` is on.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Sagas
```

## Usage

### Declare how an operation handles a failure part way

```csharp
using Bitwarden.Server.Sdk.Sagas;

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

Name one or more shapes:

| Shape              | In saga terms                              | The operation                                                                                          |
| ------------------ | ------------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| `Compensated`      | Compensatable steps                        | has an undo for each write already made, run newest first when a later one fails                       |
| `IrreversibleLast` | The pivot step                             | makes the step it can't undo (a payment, a cancellation, a mail) after every check and every other one |
| `RetrySafeByState` | Retriable steps                            | writes whole values in an order that lets the same request, or the same message again, finish it       |
| `StageThenCommit`  | Stage, then commit (like two-phase commit) | has each service stage its part, then one commit makes them take effect, rolling forward after it      |

- `[SpansServices]` goes on an endpoint's handler method, or on a message handler's, outbox handler's or job's class.
- Choose the shape the code really implements, and say in the reason how it implements it. A blank reason, or no
  shape, throws `ArgumentException`.
- A message published through the outbox after the commit is not a synchronous write and needs no declaration.

### Admit an operation that doesn't handle it yet

An operation with no handling yet is a defect. Never declare a shape it doesn't implement; admit it, saying what a
failure part way leaves behind and where the defect is recorded:

```csharp
using Bitwarden.Server.Sdk.Sagas;

[SpansServicesUnhandled("A failure after the seats are bought leaves them bought with no member; tracked as a defect.")]
public sealed class SeatPurchaseHandler
{
}
```

The guard is still told every time the operation spans services, with this declaration, and should report it as loudly
as an undeclared one. A host that refuses undeclared operations lets it through.

### Mark a client method that only reads

A test host can stand in for another service with a double of its client, and then count writes by the client method's
name: `Get`, `List`, `Check`, `Find` and the other reading verbs read, and anything else writes. A method whose name
misleads says what it does on the client's interface:

```csharp
using Bitwarden.Server.Sdk.Sagas;

public interface IAccountCalls
{
    [SendsWrites(false)]
    Task<bool> VerifyPasswordAsync(Guid account, string serverHash, CancellationToken cancellationToken);
}
```

The request the client sends is unchanged.

### Name a reading POST so it isn't counted

`ReadingRoutes.Reads` tells a client which write-method requests only read, by address: an action named `check`,
`find`, `validate`, `look-up`, `resolve`, `get`, `filter` or `holds`, alone or followed by more words
(`.../actions/find-by-identifier`), a collection of previews (`/api/v1/invoice-previews`), or `validateOnly=true` in the
query. The rule trusts the name, so a write named with a reading verb (`.../actions/resolve-and-refund`) counts as a
read. Name a write with a verb that says it writes.

### Count an operation's writes in your own host

A host begins an operation around each endpoint, message handler and job, its clients report each write they send to
another service, and its data layer each save:

```csharp
using Bitwarden.Server.Sdk.Sagas;

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

This prints that `invite-member` spans services, undeclared, then its two steps: `database` and `vault-service`.

- The operation flows on the execution context: whatever it awaits is counted with it, and nothing outside it is.
- A write to another service is counted once per idempotency key, so a retry that sends the same key is one write. A
  write sent without a key is counted every time, so an operation that retries one itself makes two writes. Every save
  is counted.
- The guard is told once, before the write that makes the operation span services, with the steps by name only
  (a service's name or `database`), never a value the write carries.
- A guard refuses by throwing. That write and every later write of the operation are refused the same way, including
  one made by the operation's other work while the guard was deciding. A guard must not wait for another write of the
  same operation.
- Call `ServiceWriteScope.ThrowIfRefused` when the operation returns: it throws the refusal again if the operation
  caught it and went on, so it can't finish as if its refused write had been made.

## Types

| Type                                    | Purpose                                                                |
| --------------------------------------- | ---------------------------------------------------------------------- |
| `ServiceWrites`, `ServiceWriteScope`    | Count the writes of the operation running now                          |
| `IServiceWritesGuard`                   | Told before the write that makes an operation span services            |
| `SpansServicesAttribute`, `SpanShape`   | How an operation whose writes span services handles a failure part way |
| `SpansServicesUnhandledAttribute`       | Admits an operation that doesn't handle one yet                        |
| `SpanDeclarationAttribute`              | Either declaration                                                     |
| `SendsWritesAttribute`, `ReadingRoutes` | Whether a client method, or a write-method address, only reads         |
