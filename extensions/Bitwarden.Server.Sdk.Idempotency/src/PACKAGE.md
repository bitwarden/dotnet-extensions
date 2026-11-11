# Bitwarden.Server.Sdk.Idempotency

## About

At-most-once writes for Bitwarden services: a write another service sends with an `Idempotency-Key`, to an operation
marked `[AcceptsIdempotencyKey]`, is done once, and a repeat with the same key is answered with the first answer
instead of being done again. A key reused for a
different request is refused `422` (by Bitwarden.Server.Sdk.Service's exception handler), and a repeat that arrives while the first is still running `409` with
`Retry-After`.

Bitwarden.Server.Sdk.Service registers and orders it for every service. It depends on ASP.NET Core,
Bitwarden.Server.Sdk.Authorization, Bitwarden.Server.Sdk.Context, Bitwarden.Server.Sdk.Errors,
Bitwarden.Server.Sdk.Idempotency.Abstractions and StackExchange.Redis.

## Installing

```bash
dotnet add package Bitwarden.Server.Sdk.Idempotency
```

## Configuration

| Setting                           | Default      | What it does                                                                                                                      |
| --------------------------------- | ------------ | --------------------------------------------------------------------------------------------------------------------------------- |
| `Idempotency:Enabled`             | `true`       | Whether keys are honoured at all.                                                                                                 |
| `Idempotency:Retention`           | `00:10:00`   | How long a completed write's answer is kept for its key. A repeat after that is done again.                                       |
| `Idempotency:Lease`               | `00:02:00`   | How long a write in progress holds its key. A repeat after that, should the instance running it have stopped, is done again.      |
| `Idempotency:MaximumBytes`        | `1048576`    | The largest request body fingerprinted, and the largest answer kept. A write past either runs without a key.                      |
| `Idempotency:Redis:Configuration` | None         | The Redis records are kept in. With it, and a shared data protection key ring, records are kept there; otherwise in this process. |

Records are kept under `{service}:idempotency:`, inside the service's own prefix of a Redis it may share with other
services, and sealed with the service's data protection keys, since those services may read that prefix. They are
shared across instances only when the key ring is persisted where every instance reads it.

## Getting started

A host that composes the package itself, rather than building on Bitwarden.Server.Sdk.Service, registers it with the
authorization it reads the caller from, and adds its middleware last:

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

See the
[README](https://github.com/bitwarden/dotnet-extensions/blob/main/extensions/Bitwarden.Server.Sdk.Idempotency/src/README.md)
for which requests are recorded.

## Documentation

- Only a write (any method but `GET`, `HEAD`, `OPTIONS` and `TRACE`) from another service (`client_id`
  `internal.{service}`), carrying the header, to a mapped operation marked `[AcceptsIdempotencyKey]` and not asked only
  to be checked (`validateOnly=true`), is recorded. Every other write, and a browser's, runs every time.
- A repeat with the same method, path, query and body gets the first answer: its status, `Content-Type`, `Location`,
  `ETag`, `Last-Modified` and body, marked `Idempotency-Replayed: true`.
- A write that fails with a `5xx` or an exception keeps nothing, so its repeat runs again.
- A key that isn't one value of 1 to 255 visible ASCII characters is refused `400`.
- `[AcceptsIdempotencyKey]` on a handler or its class opts an operation in. Mark one whose repeat would do something
  twice that the state it leaves doesn't absorb: a create with an identifier the service chooses, a charge or purchase,
  a mail or message sent, a secret issued, a count added to. Leave it off a replace, a delete, a write that sets a
  value, and anything that only reads. Bitwarden.Server.Sdk.Service publishes the mark in the contract
  (`x-bitwarden-idempotency-key`), and Bitwarden.Server.Sdk.Client sends a key only to marked operations.
- Each keyed write costs two round trips to the store, and its answer is held for `Idempotency:Retention`.

| Type                                      | Purpose                                                |
| ----------------------------------------- | ------------------------------------------------------ |
| `IdempotencyServiceCollectionExtensions`  | `AddIdempotencyKeys(configuration)`: options and store |
| `IdempotencyApplicationBuilderExtensions` | `UseIdempotencyKeys()`: the middleware                 |
| `IdempotencyOptions`                      | The `Idempotency` settings                             |
| `AcceptsIdempotencyKeyAttribute`          | Opts an operation in                                   |
