# Bitwarden.Server.Sdk.Sagas

## About

A business operation whose writes span Bitwarden services, with no transaction around it, is a saga: no transaction
spans services, so a failure between its writes leaves it part way done. This package notices a saga and makes the
operation declare how it handles a failure part way, with `[SpansServices]`. It counts the writes of the operation
running now, so one that makes two or more writes, at least one of them to another service called synchronously, is
noticed before the write that makes it so. It does not run or orchestrate sagas.

It has no dependencies. A host begins an operation around each endpoint, message handler and job, its clients and data
layer report the writes they make, and it provides the guard that decides what an undeclared span means.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Sagas
```

## Configuration

The package reads no settings.

## Getting started

Declare how an operation that writes in two services handles a failure between those writes:

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

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Sagas/src/README.md)
for a complete program.

## Documentation

### How writes are counted

- `ServiceWrites.Begin` starts an operation, which flows on the execution context: whatever it awaits is counted with
  it, and nothing outside it is.
- A write sent to another service is counted once per `Idempotency-Key`, so a retry is one write. A save to the
  service's own database is counted each time it is reported.
- Two or more writes, one of them to another service, and the operation spans services; two writes to the same other
  service count. The guard is told once, before
  the write that makes it so, with the steps by name only.
- A guard that throws refuses that write and every later one of the operation, including one made while it was
  deciding. `ServiceWriteScope.ThrowIfRefused`
  throws the refusal again if the operation caught it and went on.

### Shapes

| Shape              | In saga terms                              | The operation                                                                                          |
| ------------------ | ------------------------------------------ | ------------------------------------------------------------------------------------------------------ |
| `Compensated`      | Compensatable steps                        | has an undo for each write already made, run newest first when a later one fails                       |
| `IrreversibleLast` | The pivot step                             | makes the step it can't undo (a payment, a cancellation, a mail) after every check and every other one |
| `RetrySafeByState` | Retriable steps                            | writes whole values in an order that lets the same request, or the same message again, finish it       |
| `StageThenCommit`  | Stage, then commit (like two-phase commit) | has each service stage its part, then one commit makes them take effect, rolling forward after it      |

An operation with no handling yet admits it with `[SpansServicesUnhandled(reason)]`, never a shape it doesn't
implement.

### Types

| Type                                    | Purpose                                                                |
| --------------------------------------- | ---------------------------------------------------------------------- |
| `ServiceWrites`, `ServiceWriteScope`    | Count the writes of the operation running now                          |
| `IServiceWritesGuard`                   | Told before the write that makes an operation span services            |
| `SpansServicesAttribute`, `SpanShape`   | How an operation whose writes span services handles a failure part way |
| `SpansServicesUnhandledAttribute`       | Admits an operation that doesn't handle one yet                        |
| `SpanDeclarationAttribute`              | Either declaration                                                     |
| `SendsWritesAttribute`, `ReadingRoutes` | Whether a client method, or a write-method address, only reads         |
