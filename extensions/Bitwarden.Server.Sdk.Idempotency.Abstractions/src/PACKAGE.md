# Bitwarden.Server.Sdk.Idempotency.Abstractions

## About

The `Idempotency-Key` header a client sends with a write to a Bitwarden service, and the operations that make a piece
of work send the same keys when it runs again, so a callee that honours the header does each write at most once and
replays its first answer.

It has no dependencies, and neither sends nor honours the header itself: a client sends it, a service honours it, and a
host that runs message handlers or jobs begins an operation around each run.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Idempotency.Abstractions
```

## Configuration

The package reads no settings.

## Getting started

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

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency.Abstractions/src/README.md)
for a complete program.

## Documentation

### How a write's key is made

- A client sends a key only to an operation whose service's contract marks it `x-bitwarden-idempotency-key`
  (`IdempotencyKeys.Extension`).
- Outside an operation, a client sends a fresh key per call, which covers its own retries of that call.
- Inside one, `IdempotentOperation.KeyForWrite` derives the key from the operation's key, the method, the address, the
  body, and how many identical writes the operation made before. A write whose body differs from the first run's gets
  a key of its own and is done as a new write.
- `IdempotentOperation.ForMessage(handler, envelope)` and `ForJob(job, dueAt)` are the operation keys a host uses for
  a message handler's or a job's run; a blank name is refused. `[PerCallIdempotencyKeys(reason)]` on a handler or job
  asks the host not to begin one around it.
- `IdempotentOperation.DeriveId(purpose)` gives an identifier that is the same on every run, for something the
  operation creates or sends.
- An operation's key is stable for the logical operation, unique to it, and never a person's data. Begin it in the
  method that makes the writes, or one that awaits it.

### Types

| Type                              | Purpose                                                                     |
| --------------------------------- | --------------------------------------------------------------------------- |
| `IdempotencyKeys`                 | The headers, the contract extension, and a key's rules                      |
| `IdempotentOperation`             | Names one logical operation, so its writes repeat the same keys             |
| `PerCallIdempotencyKeysAttribute` | Takes a handler or job out of its operation, and says why                   |
