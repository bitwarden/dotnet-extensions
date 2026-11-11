# Bitwarden.Server.Sdk.Idempotency.Abstractions

## About

A write that fails in transit may or may not have been done. A client can send an `Idempotency-Key` header with a
write, and a service that honours it does the write at most once per key, replaying its first answer to a repeat.

This package names that header, says which keys are valid, and lets a piece of work (a retry loop, a message handler,
a job) name itself once, so that when it runs again from the start its writes carry the same keys as the first time.
It neither sends nor honours the header itself: a client sends it, a service honours it, and a host that runs message
handlers or jobs begins an operation around each run. It has no dependencies.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Idempotency.Abstractions
```

## Usage

### Send a key with a write

A service's contract marks each operation that honours the header with the OpenAPI extension
`x-bitwarden-idempotency-key` (`IdempotencyKeys.Extension`). A client sends a key only to an operation marked so, and
sends no header to any other.

For each write it sends a key to, the client asks `IdempotentOperation.KeyForWrite(method, address, body)`. Outside an
operation that returns null and the client sends a fresh key per call, which covers its own retries of that call.
Inside one it returns a key derived from the operation's key, the method, the address, the body, and how many
identical writes the operation made before.

A valid key is 1 to 255 visible ASCII characters (`IdempotencyKeys.IsValid`). A service marks a replayed answer with
`Idempotency-Replayed: true` (`IdempotencyKeys.ReplayedHeader`), so a log or a test can tell a replay from a first
answer.

### Retry a piece of work as one operation

Begin one operation per attempt, with a key minted before the first. Each callee does each write once, and replays its
answer to the next attempt:

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

- The operation key must be stable for the logical operation and unique to it: a message's identifier, a job
  occurrence, or one minted before the first attempt. Never a person's data: no email address, no name.
- An operation key is 1 to 200 visible ASCII characters (`IdempotentOperation.MaximumKeyLength`); `Begin` throws
  `ArgumentException` otherwise.
- Begin the operation in the method that makes the writes, or one that awaits it. An operation begun inside an awaited
  method isn't seen by its caller. Once ended, an operation is never current again, even when operations are ended out
  of order.
- A write whose body differs from the first run's (it carries a time, or reads something the first run changed) gets a
  key of its own and is done as a new write. It is never refused for differing.

### Run a message handler or job as an operation

A host that runs message handlers or jobs begins an operation around each run, keyed by
`IdempotentOperation.ForMessage(handler, envelope)` or `IdempotentOperation.ForJob(job, dueAt)`. A redelivered message
or an occurrence run again repeats its key; two handlers of one envelope, or the next occurrence, don't share it. A
blank name throws `ArgumentException`, and a long one is shortened to its hash.

A handler or job whose writes must be done again when it runs again, because each depends on what the callee has
gained since, says so:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

[PerCallIdempotencyKeys("Each run sends the counts as they are now; replaying the first run's answer would hide newer ones.")]
public sealed class UsageReportJob
{
}
```

The host checks `PerCallIdempotencyKeysAttribute.IsOn(type)` and begins no operation around a type marked this way, so
each of its writes carries a fresh key. The reason is required. The handler may still begin operations of its own.

### Name something the operation creates or sends

`IdempotentOperation.DeriveId(purpose)` gives an identifier that is the same on every run of the operation, and null
outside one:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

public static class WelcomeMail
{
    public static Guid MessageId() => IdempotentOperation.DeriveId("welcome-mail") ?? Guid.NewGuid();
}
```

The purpose is hashed with the operation's key and never kept. Asked for twice in one run, the same purpose gives two
identifiers, in order.

### Honour the header in a service

A service that honours the header must store and look up each replay record under the authenticated caller and the key
together, never under the key alone. A key is not a secret: it travels in a request header, and a key derived inside an
operation is a hash that anyone who knows the operation's key and the write can compute. Keyed by caller, one caller
can't receive the answer recorded for another, or block another's write by sending the same key.

How a service answers a repeat with a different body, or one that arrives while the first is still running, and which
callers' keys it honours, are its own to decide.
[Bitwarden.Server.Sdk.Idempotency](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/PACKAGE.md)
honours the header for a service built on the SDK.

## Types

| Type                              | Purpose                                                         |
| --------------------------------- | --------------------------------------------------------------- |
| `IdempotencyKeys`                 | The headers, the contract extension, and a key's rules          |
| `IdempotentOperation`             | Names one logical operation, so its writes repeat the same keys |
| `PerCallIdempotencyKeysAttribute` | Takes a handler or job out of its operation, and says why       |
