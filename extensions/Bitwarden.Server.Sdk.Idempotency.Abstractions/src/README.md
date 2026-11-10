# Bitwarden.Server.Sdk.Idempotency.Abstractions

Run an operation again without doing its writes to other services twice.

A write that fails in transit may or may not have been done. A client can send an `Idempotency-Key` with each write,
and a service that honours it does the write at most once per key, replaying its first answer to a repeat. This
package names that header and lets a piece of work, such as a message handler or a job, name itself once, so that when
it runs again from the start its writes carry the same keys as the first time.

- **The header.** `IdempotencyKeys` names `Idempotency-Key` and `Idempotency-Replayed`, and says which keys are
  valid: 1 to 255 visible ASCII characters.
- **Operations that repeat their keys.** Inside `IdempotentOperation.Begin(key)`, each write's key is derived from the
  operation's key, the method, the address, the body, and how many identical writes the operation made before.
- **Identifiers that repeat too.** `IdempotentOperation.DeriveId(purpose)` gives the same identifier on every run, for
  something the operation creates or sends.
- **A stated exception.** `[PerCallIdempotencyKeys(reason)]` takes a handler or job out of its operation, with the
  reason written down.

## What belongs here

Only what both the side that sends a write and the side that receives it need. Nothing here touches `HttpContext` or
`HttpClient`, and nothing here sends or honours the header: a client sends it (from `IdempotentOperation.KeyForWrite`,
or a fresh key outside an operation), a service honours it, and a host that runs message handlers or jobs begins an
operation around each run. The package has no dependencies.

## Getting started

```bash
dotnet new console -n RepeatableWork
cd RepeatableWork
dotnet add package Bitwarden.Server.Sdk.Idempotency.Abstractions --prerelease
```

```csharp
using System.Text;
using Bitwarden.Server.Sdk.Idempotency;

// Minted once, before the first attempt, and kept for every attempt after it.
var operation = $"archive-widget:{Guid.NewGuid()}";
var body = Encoding.UTF8.GetBytes("""{"data":{"type":"widget"}}""");

for (var attempt = 1; attempt <= 2; attempt++)
{
    using (IdempotentOperation.Begin(operation))
    {
        var key = IdempotentOperation.KeyForWrite("POST", "https://widget-service/api/v1/widgets", body);
        Console.WriteLine($"Attempt {attempt}: {IdempotencyKeys.Header}: {key}");
    }
}

Console.WriteLine(IdempotentOperation.KeyForWrite("POST", "https://widget-service/api/v1/widgets", body) ?? "Outside an operation: a fresh key per call.");
```

`dotnet run` prints the same key for both attempts, then the line for outside an operation. A client that sends the
header asks `KeyForWrite` for each write, so inside an operation its writes carry these keys.

## Examples

### Retry a piece of work as one operation

Begin one operation per attempt, with a key minted before the first. Each callee does each write once, and replays
its answer to the next attempt:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

public sealed class ArchiveWidgetWorkflow(IWidgetCalls widgets)
{
    public async Task ArchiveAsync(Guid widget, CancellationToken cancellationToken)
    {
        var operation = $"archive-widget:{Guid.NewGuid()}";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using (IdempotentOperation.Begin(operation))
                {
                    await widgets.ArchiveAsync(widget, cancellationToken);
                    return;
                }
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                // Run again from the start: the writes the first attempt finished are replayed, not done again.
            }
        }
    }
}
```

- Begin the operation in the method that makes the writes, or one that awaits it. An operation begun inside an
  awaited method isn't seen by its caller. Once ended, an operation is never current again, even when operations are
  ended out of order.
- An operation key is 1 to 200 visible ASCII characters (`IdempotentOperation.MaximumKeyLength`), and a job's or a
  handler's name for `ForJob` or `ForMessage` must not be blank.
- The key must be stable for the logical operation and unique to it: a message's identifier, a job occurrence, or one
  minted before the first attempt. Never a person's data: no email address, no name.
- A write whose body differs from the first run's gets a key of its own and is done as a new write.

### Name something the operation sends

`DeriveId` gives an identifier that is the same on every run of the operation, and null outside one:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

public static class WelcomeMail
{
    public static Guid MessageId() => IdempotentOperation.DeriveId("welcome-mail") ?? Guid.NewGuid();
}
```

The purpose is hashed with the operation's key and never kept. Asked for twice in one run, the same purpose gives two
identifiers, in order.

### Do every write again

A handler or a job whose writes must be done again when it runs again, because each depends on what the callee has
gained since, says so:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

[PerCallIdempotencyKeys("Each run sends the counts as they are now; replaying the first run's answer would hide newer ones.")]
public sealed class UsageReportJob
{
}
```

A host that begins an operation around each handler or job checks `PerCallIdempotencyKeysAttribute.IsOn(type)` and
begins none around one marked this way. The reason is required.

## Configuration

The package reads no settings.

## Requirements

- .NET 10.
- No other packages.

## Contributing

Every source file has a matching test class in
[`tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests`](../tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests),
with golden vectors for the derived keys and identifiers. Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Idempotency.Abstractions/tests/Bitwarden.Server.Sdk.Idempotency.Abstractions.Tests
```

A change must keep:

- **Keys stable.** A key derived today must be derived the same way by every instance and every later version, or a
  run after an upgrade does its writes twice. The golden vectors in the tests were computed outside .NET; a change
  that breaks one changes the wire format.
- **No person's data in a key.** Keys and identifiers are hashes of what the operation names, and an operation's key
  never holds an email address or a name.
- **No dependencies, and nothing about the transport.**
