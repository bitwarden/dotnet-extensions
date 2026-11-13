# Bitwarden.Server.Sdk.Sagas

**Audience:** engineers changing this package.

Counts an operation's writes and carries how it handles a failure part way. See [PACKAGE.md](PACKAGE.md) for how to
use it.

## What belongs here

Only what both the side that sends a write and the side that runs the operation need. Nothing here touches
`HttpContext` or `HttpClient`, and nothing here decides what an undeclared span means. The hosts live elsewhere:

| Package                              | What it does with this one                                                 |
| ------------------------------------ | -------------------------------------------------------------------------- |
| Bitwarden.Server.Sdk.Service         | Begins an operation per endpoint; the guard and its setting                |
| Bitwarden.Server.Sdk.Messaging, Jobs | Begin an operation per message handler and job run                         |
| Bitwarden.Server.Sdk.Data            | Reports saves; begins one per outbox handler                               |
| Bitwarden.Server.Sdk.Client          | Reports each write it sends, skipping `ReadingRoutes` reads                |
| Bitwarden.Server.Sdk.Service.Testing | Counts writes through client doubles, by `[SendsWrites]`                   |
| Bitwarden.Server.Sdk.Client.Testing  | Checks each client method's name against what it sends, by `[SendsWrites]` |

No dependencies, and nothing about the transport.

## How it works

- **The ambient operation.** `ServiceWrites` keeps the current `ServiceWriteScope` in an `AsyncLocal`. `Begin` inside
  another operation nests: disposing the inner one restores the outer, and `Current` ignores a scope already disposed.
- **The guard runs under the scope's lock.** A write the operation's other work makes meanwhile waits for the decision,
  so a refusal reaches it rather than letting it be recorded as made. That is why a guard must not wait for another
  write of the same operation.
- **A refusal sticks.** `ServiceWriteScope.Record` keeps the guard's exception as an `ExceptionDispatchInfo` and throws
  it again for every later write and from `ThrowIfRefused`.
- **Shapes are flags.** `SpansServicesAttribute` refuses `None` and any bit outside the four shapes.

## Testing

Every source file with logic has a matching test class in `tests/Bitwarden.Server.Sdk.Sagas.Tests`.

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Sagas/tests/Bitwarden.Server.Sdk.Sagas.Tests
```

A change must keep:

- **Names only in the steps.** A step is a service's name or `database`, never a value the write carries.
- **A retry is one write.** A write is counted once per `Idempotency-Key` per service; one without a key, or with a
  blank one, is counted each time.
- **A refusal sticks**, including for a write made while the guard was deciding.
