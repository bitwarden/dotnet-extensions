# Bitwarden.Server.Sdk.Idempotency

Do a write another Bitwarden service sends at most once, and answer a repeat with the first answer.

A write that fails in transit may or may not have been done, so a service client sends it again with the same
`Idempotency-Key`. This package records each keyed write from another service as it runs, keeps its answer, and
replays that answer to a repeat instead of doing the write twice.

- **At most once per key.** A repeat with the same method, path, query and body gets the first answer, marked
  `Idempotency-Replayed: true`.
- **Misuse refused.** A key reused for a different request is refused `422` (an `InvalidRequestException`, which
  Bitwarden.Server.Sdk.Service's exception handler answers `422`; a host without one answers `500`). A repeat that
  arrives while the first is still running is refused `409` with `Retry-After`, which a service client waits for.
- **Failures not kept.** A write that fails with a `5xx` or an exception keeps nothing, so its repeat runs again.
- **Shared across instances.** Records are kept in Redis when one is configured, so a repeat that reaches another
  instance is still answered once.
- **Sealed.** A kept answer is sealed with the service's data protection keys before it is stored.

Bitwarden.Server.Sdk.Service registers and orders it for every service. Reference it directly only to compose a host
yourself, or to leave an operation out with `[IgnoreIdempotencyKey]`.

## What it applies to

A request is recorded only when all of these hold. Everything else passes through untouched:

- it is a write: any method but `GET`, `HEAD`, `OPTIONS` and `TRACE`;
- it reached a mapped operation that isn't marked `[IgnoreIdempotencyKey]`;
- it doesn't ask only to be checked (`validateOnly=true`);
- it comes from another service: a token whose `client_id` is `internal.{service}` (`InternalCaller`);
- it carries `Idempotency-Key`.

A browser's write, and so every operation people call directly, runs every time it arrives, whatever headers it
carries. A key that isn't one value of 1 to 255 visible ASCII characters is refused `400`.

## Getting started

A host that composes the package itself registers it, with the authorization it reads the caller from, and adds its
middleware last, as Bitwarden.Server.Sdk.Service does, so a request is authenticated, authorized and routed before
anything is recorded:

```bash
dotnet new web -n WidgetService
cd WidgetService
dotnet add package Bitwarden.Server.Sdk.Idempotency --prerelease
```

```csharp
using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Idempotency;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IServiceIdentity>(new WidgetServiceIdentity());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddBitwardenAuthentication();
builder.Services.AddCallerAuthorization(isProductService: false);
builder.Services.AddIdempotencyKeys(builder.Configuration);

var app = builder.Build();

app.UseForwardedClientAddress();
app.UseBitwardenAuthentication();
app.UseContextHeaders();
app.UseAuthorization();
app.UseCallChainCheck();
app.UseIdempotencyKeys();

app.MapPost("/api/v1/widgets", () => Results.Created("/api/v1/widgets/1", new CreatedWidget("1")));

app.Run();

internal sealed record CreatedWidget(string Id);

internal sealed class WidgetServiceIdentity : IServiceIdentity
{
    public string Name => "widget-service";

    public string Title => "Widget Service";
}
```

Another service that sends `POST /api/v1/widgets` twice with the same `Idempotency-Key` gets `201` both times; the
second answer carries `Idempotency-Replayed: true`, and the handler ran once. A generated client in
Bitwarden.Server.Sdk.Client sends the key on every write for you.

## Examples

### Leave an operation out

Mark an operation that must answer from the state now, or is idempotent by its own state:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

public static class WidgetChecks
{
    /// <summary>
    /// Check a widget's name is free
    /// </summary>
    [IgnoreIdempotencyKey]
    public static NameCheck CheckName(NameCheck request) => request;
}

public sealed record NameCheck(string Name, bool Free);
```

It goes on the handler method or its class. Service clients retry a keyed write after a failed connection, so an
operation left out must be safe to receive twice.

## Configuration

| Setting                           | Default      | What it does                                                                                                                      |
| --------------------------------- | ------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| `Idempotency:Enabled`             | `true`       | Whether keys are honoured at all.                                                                                                 |
| `Idempotency:Retention`           | `1.00:00:00` | How long a completed write's answer is kept for its key. A repeat after that is done again.                                       |
| `Idempotency:Lease`               | `00:02:00`   | How long a write in progress holds its key. A repeat after that, should the instance running it have stopped, is done again.      |
| `Idempotency:MaximumBytes`        | `1048576`    | The largest request body fingerprinted, and the largest answer kept. A write past either runs without a key.                      |
| `Idempotency:Redis:Configuration` | None         | The Redis records are kept in. With it, and a shared data protection key ring, records are kept there; otherwise in this process. |

- **Where records live.** In Redis, under the service's own prefix as `{service}:idempotency:{SHA-256 of the caller,
the key and the context's owners}`, only when `Idempotency:Redis:Configuration` is set and the service's data protection key ring is
  persisted where every instance reads it (the host configures a key repository, such as `PersistKeysToFileSystem` on a volume every instance mounts).
  Otherwise each instance keeps its own, in memory, and a repeat that reaches another instance runs again.
- **A Redis shared with other services.** Every key starts with `{service}:`, so a Redis user that may write only the
  service's own prefix can keep its records, and point `Idempotency:Redis:Configuration` at the same login as any other
  Redis the service writes under that prefix.
- **While Redis is away**, a keyed write runs as if it carried no key.

## Security

- **What is kept is what was already answered.** A record holds the status, the `Content-Type`, `Location`, `ETag` and
  `Last-Modified` headers, and the body the operation sent the caller: Protected Data or metadata, never Vault Data in
  the clear, which no service holds.
- **Sealed before it is stored.** The Redis may be shared with other services that can read the service's prefix, so
  every record is sealed with the service's data protection keys. A record this instance can't open is taken as still running, refused `409`, and
  never run past.
- **Per caller and per person.** A record is keyed by the calling service's `client_id`, its key, and the
  organization, user and provider its verified context names, so one service can't replay another's answers, and a
  service that sends one key for two people's requests runs both rather than answering one with the other's answer.

## Requirements

- .NET 10 and ASP.NET Core.
- Bitwarden.Server.Sdk.Authorization (it reads the caller), Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors,
  Bitwarden.Server.Sdk.Idempotency.Abstractions and StackExchange.Redis.
- An `IServiceIdentity` registration, which Bitwarden.Server.Sdk.Service provides.

## Contributing

Every type with logic has a test class of the same name in the package's `tests` folder; the Redis store's start a Redis container, so they need Docker. Run them from the repository root:

```bash
dotnet test --project extensions/Bitwarden.Server.Sdk.Idempotency/tests/Bitwarden.Server.Sdk.Idempotency.Tests
```

The middleware is also tested inside a whole service host, in Bitwarden.Server.Sdk.Service's tests. A change must keep:

- **At most once.** A write never runs twice for one caller and key while its record is kept, even across instances
  and when a record can't be opened.
- **Failures never replayed.** Only an answer below `500`, written by the operation, is kept.
- **Records sealed, under the protector's original purpose.** Renaming the purpose would make every kept answer
  unopenable.
- **Between services only.** A browser's write is never recorded.
