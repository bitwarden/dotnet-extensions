# Bitwarden.Server.Sdk.Idempotency

## About

Honours `Idempotency-Key` for operations marked `[AcceptsIdempotencyKey]`: it records each keyed write from another
Bitwarden service as it runs, keeps its answer, and replays that answer to a repeat instead of doing the write twice.
What a key is, and how a client derives one, is in
[Bitwarden.Server.Sdk.Idempotency.Abstractions](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency.Abstractions/src/PACKAGE.md).

Bitwarden.Server.Sdk.Service registers and orders it for every service, and publishes each marked operation in the
service's contract (`x-bitwarden-idempotency-key`), so a client package built from that contract sends a key to marked
operations and no other. Reference this package directly to mark an operation, or to compose a host yourself.

It depends on ASP.NET Core, Bitwarden.Server.Sdk.Authorization (it reads the caller), Bitwarden.Server.Sdk.Context,
Bitwarden.Server.Sdk.Errors, Bitwarden.Server.Sdk.Idempotency.Abstractions and StackExchange.Redis, and needs an
`IServiceIdentity` registration, which Bitwarden.Server.Sdk.Service provides.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Idempotency
```

## Configuration

| Setting                           | Default    | What it does                                                                                                                      |
| --------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `Idempotency:Enabled`             | `true`     | Whether keys are honoured at all.                                                                                                 |
| `Idempotency:Retention`           | `00:10:00` | How long a completed write's answer is kept for its key. A repeat after that is done again.                                       |
| `Idempotency:Lease`               | `00:02:00` | How long a write in progress holds its key. A repeat after that, should the instance running it have stopped, is done again.      |
| `Idempotency:MaximumBytes`        | `1048576`  | The largest request body fingerprinted, and the largest answer kept. See below.                                                   |
| `Idempotency:Redis:Configuration` | None       | The Redis records are kept in. With it, and a shared data protection key ring, records are kept there; otherwise in this process. |

- **Where records live.** In Redis only when `Idempotency:Redis:Configuration` is set and the service's data
  protection key ring is persisted where every instance reads it (the host configures a key repository, such as
  `PersistKeysToFileSystem` on a volume every instance mounts). Otherwise each instance keeps its own in memory, and a
  repeat that reaches another instance runs again.
- **A Redis shared with other services.** Records are kept as `{service}:idempotency:{SHA-256 of the caller, the key
and the context's owners}`. Every key starts with `{service}:`, so a Redis user that may write only the service's
  own prefix can keep them, with the same login as any other Redis the service writes under that prefix.
- **While Redis is away**, a keyed write runs as if it carried no key.
- **Past `MaximumBytes`**, a request runs as if it carried no key, and an answer is not kept, so its repeat runs again.
- **Each keyed write** costs two round trips to the store and holds its answer there for `Idempotency:Retention`.

## Usage

### Opt an operation in

Mark an operation whose repeat would do something twice that the state it leaves doesn't absorb: a create with an
identifier the service chooses, a charge or purchase, a mail or message sent, a secret issued, a count added to:

```csharp
using Bitwarden.Server.Sdk.Idempotency;

public static class WidgetPurchases
{
    /// <summary>
    /// Buy a widget
    /// </summary>
    [AcceptsIdempotencyKey]
    public static Purchase Buy(Purchase request) => request;
}

public sealed record Purchase(string Widget, int Quantity);
```

It goes on the handler method. On a handler's class it counts only for operations mapped with
Bitwarden.Server.Sdk.Resources' `WithStandards()`, which copies a class's attributes to each of its operations; a plain
`Map*` call reads the method's own attributes only. Leave it off a replace, a delete, a write that sets a value, and
anything that only reads, which must answer from the state now. A service client retries a write without a key only when
its connection never opened, so it can't arrive twice from a retry; but an operation run again after a failure (a
message redelivered, a job's next attempt) still sends it again, so an unmarked write must be safe to receive twice.

### Know which requests are recorded

A request is recorded only when all of these hold; everything else passes through untouched:

- it is a write: any method but `GET`, `HEAD`, `OPTIONS` and `TRACE`;
- it reached a mapped operation marked `[AcceptsIdempotencyKey]`;
- it doesn't ask only to be checked (`validateOnly=true`);
- it comes from another service: a token whose `client_id` is `internal.{service}`;
- it carries `Idempotency-Key`.

A browser's write, and so every operation people call directly, runs every time it arrives, whatever headers it
carries.

### Handle a repeat

- **Same request, same key:** a repeat with the same method, path, query and body gets the first answer (its status,
  `Content-Type`, `Location`, `ETag`, `Last-Modified` and body), marked `Idempotency-Replayed: true`.
- **Still running:** a repeat that arrives while the first runs is refused `409` with `Retry-After`. A service client
  waits and tries again up to its `Retries`; a write still running after that reaches its caller as
  `ServiceUnavailableException`.
- **Different request, same key:** refused as an `InvalidRequestException`, which the error handler in
  Bitwarden.Server.Sdk.Resources (registered by Service) answers `422`. A host without that handler answers `500`.
- **Malformed key:** a key that isn't one value of 1 to 255 visible ASCII characters is refused `400`.
- **Failed write:** one that fails with a `5xx` or an exception keeps nothing, so its repeat runs again.

What is kept is what the operation already answered the caller: Protected Data or metadata, never Vault Data in the
clear, which no service holds. Because other services may read the service's prefix of a shared Redis, every record is
sealed with the service's data protection keys before it is stored. A record this instance can't open is taken as
still running and refused `409`, never run past. A record is keyed by the calling service's `client_id`, its key, and
the organization, user and provider its verified context names, so one service can't replay another's answers, and a
service that sends one key for two people's requests runs both rather than answering one with the other's answer.

### Compose a host yourself

A host not built on Bitwarden.Server.Sdk.Service registers the package with the authorization it reads the caller from,
and adds its middleware last, so a request is authenticated, authorized and routed before anything is recorded:

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

app.MapPost("/api/v1/widgets", [AcceptsIdempotencyKey] () => Results.Created("/api/v1/widgets/1", new CreatedWidget("1")));

app.Run();

internal sealed record CreatedWidget(string Id);

internal sealed class WidgetServiceIdentity : IServiceIdentity
{
    public string Name => "widget-service";

    public string Title => "Widget Service";
}
```

Another service that sends `POST /api/v1/widgets` twice with the same `Idempotency-Key` gets `201` both times; the
second answer carries `Idempotency-Replayed: true`, and the handler ran once.

## Types

| Type                                      | Purpose                                                |
| ----------------------------------------- | ------------------------------------------------------ |
| `AcceptsIdempotencyKeyAttribute`          | Opts an operation in                                   |
| `IdempotencyServiceCollectionExtensions`  | `AddIdempotencyKeys(configuration)`: options and store |
| `IdempotencyApplicationBuilderExtensions` | `UseIdempotencyKeys()`: the middleware                 |
| `IdempotencyOptions`                      | The `Idempotency` settings                             |
